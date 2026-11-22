using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IPBanManager.Core.Abstractions;
using IPBanManager.Core.Errors;
using IPBanManager.Core.Updates;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace IPBanManager.Infrastructure.Updates;

/// <summary>Valida de onde as atualizações podem vir: somente repositórios GitHub por HTTPS.</summary>
public static class UpdateRepository
{
    private static readonly Regex GitHub = new Regex(
        @"^https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsAcceptable(string location)
    {
        if (string.IsNullOrWhiteSpace(location)) return false;
        if (GitHub.IsMatch(location.Trim())) return true;
#if ALLOW_LOCAL_UPDATE_SOURCE
        // Somente em compilações de teste (QA local): feed em pasta.
        return Directory.Exists(location);
#else
        return false;
#endif
    }

    public static IUpdateSource CreateSource(string location, bool includePrereleases)
    {
#if ALLOW_LOCAL_UPDATE_SOURCE
        if (Directory.Exists(location)) return new SimpleFileSource(new DirectoryInfo(location));
#endif
        return new GithubSource(location.Trim().TrimEnd('/'), null, includePrereleases);
    }
}

/// <summary>
/// Atualização sobre o Velopack. O Velopack verifica o hash do pacote ao baixar; aqui há uma segunda
/// verificação explícita antes de aplicar, escolha entre os canais Stable/Beta por versão semântica,
/// repetição em falhas de rede e reversão para o pacote da última versão boa.
/// </summary>
public sealed class VelopackUpdateService : IUpdateService
{
    private const string NotInstalledReason =
        "Este aplicativo não foi instalado pelo instalador com atualização automática " +
        "(compilação de desenvolvimento, execução direta ou instalação manual). Instale pelo IPBanManager-stable-Setup.exe da página de Releases.";

    public const string StableChannelName = "stable";
    public const string BetaChannelName = "beta";

    private static readonly TimeSpan[] DefaultRetryDelays = { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5) };

    private sealed class Candidate
    {
        public UpdateManager Manager;
        public UpdateInfo Info;
    }

    private readonly string _location;
    private readonly UpdateHealthGuard _guard;
    private readonly IRollbackPackageStore _packages;
    private readonly IAppLogger _logger;
    private readonly IVelopackLocator _locator;
    private readonly Func<string, bool, IUpdateSource> _sourceFactory;
    private readonly Func<string> _fallbackVersion;
    private readonly TimeSpan[] _retryDelays;
    private readonly UpdateManager _probe;
    private IVelopackLocator InstalledLocator => _locator ?? (VelopackLocator.IsCurrentSet ? VelopackLocator.Current : null);
    private readonly string _unsupportedReason;
    private readonly Dictionary<string, Candidate> _candidates = new Dictionary<string, Candidate>();
    private Candidate _downloaded;

    public VelopackUpdateService(string repositoryLocation, UpdateHealthGuard guard, IRollbackPackageStore packages,
        IAppLogger logger, Func<string> fallbackVersion, IVelopackLocator locator = null,
        Func<string, bool, IUpdateSource> sourceFactory = null, TimeSpan[] retryDelays = null)
    {
        _location = repositoryLocation;
        _guard = guard;
        _packages = packages;
        _logger = logger ?? NullLogger.Instance;
        _fallbackVersion = fallbackVersion;
        _locator = locator;
        _sourceFactory = sourceFactory ?? UpdateRepository.CreateSource;
        _retryDelays = retryDelays ?? DefaultRetryDelays;

        if (!UpdateRepository.IsAcceptable(repositoryLocation))
        {
            _unsupportedReason = "O repositório de atualizações não está configurado nesta compilação (somente builds publicadas pelo GitHub Actions atualizam sozinhas).";
            return;
        }
        try
        {
            _probe = NewManager(false, null, allowDowngrade: false);
            if (!_probe.IsInstalled) _unsupportedReason = NotInstalledReason;
        }
        catch (Exception ex)
        {
            // Fora de uma instalação do Velopack (build de desenvolvimento, execução direta do .exe) o UpdateManager
            // não consegue localizar a instalação. Não é um erro: só não há atualização automática.
            _logger.Info("Atualização automática indisponível (não é uma instalação do Velopack): " + ex.Message);
            _unsupportedReason = NotInstalledReason;
        }
    }

    public bool IsSupported => _unsupportedReason == null;
    public string UnsupportedReason => _unsupportedReason;

    public string CurrentVersion =>
        _probe != null && _probe.IsInstalled && _probe.CurrentVersion != null
            ? _probe.CurrentVersion.ToString()
            : _fallbackVersion();

    public UpdateChannel InstalledChannel
    {
        get
        {
            var channel = _probe != null && _probe.IsInstalled ? InstalledLocator?.Channel : null;
            if (string.Equals(channel, BetaChannelName, StringComparison.OrdinalIgnoreCase)) return UpdateChannel.Beta;
            if (string.Equals(channel, StableChannelName, StringComparison.OrdinalIgnoreCase)) return UpdateChannel.Stable;
            return SemVer.TryParse(CurrentVersion, out var v) ? v.Channel : UpdateChannel.Stable;
        }
    }

    // ------------------------------------------------------------------ verificação

    public async Task<AppUpdate> CheckAsync(UpdateSettings settings, CancellationToken cancellation)
    {
        EnsureSupported();
        _candidates.Clear();

        var feeds = new List<Tuple<string, bool>> { Tuple.Create(StableChannelName, false) };
        if (settings.Channel == UpdateChannel.Beta) feeds.Add(Tuple.Create(BetaChannelName, true));

        Exception lastError = null;
        int answered = 0;
        foreach (var feed in feeds)
        {
            try
            {
                var candidate = await WithRetry(() => QueryFeedAsync(feed.Item1, feed.Item2), cancellation);
                answered++;
                if (candidate != null) Register(candidate);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                lastError = ex;
                _logger.Warn($"Falha ao consultar o canal '{feed.Item1}': {ex.Message}");
            }
        }
        if (answered == 0 && lastError != null) throw Friendly(lastError);

        return Choose(settings);
    }

    private async Task<Candidate> QueryFeedAsync(string channel, bool includePrereleases)
    {
        var manager = NewManager(includePrereleases, channel, allowDowngrade: true);
        var info = await manager.CheckForUpdatesAsync();
        return info == null ? null : new Candidate { Manager = manager, Info = info };
    }

    private void Register(Candidate candidate)
    {
        var version = candidate.Info.TargetFullRelease.Version.ToString();
        if (_guard.IsBlocked(version))
        {
            _logger.Info($"Versão {version} ignorada: foi revertida anteriormente por falha.");
            return;
        }
        _candidates[version] = candidate;
    }

    private AppUpdate Choose(UpdateSettings settings)
    {
        if (_candidates.Count == 0) return null;

        var best = _candidates.Keys.Select(SemVer.Parse).OrderByDescending(v => v).First();
        var current = SemVer.Parse(CurrentVersion);

        if (best > current)
            return ToUpdate(best, isDowngrade: false);

        // A mais nova do canal é mais antiga que a instalada: só interessa quando o usuário saiu do Beta para o Stable.
        if (best < current && current.IsPrerelease && settings.Channel == UpdateChannel.Stable)
            return ToUpdate(best, isDowngrade: true);

        return null;
    }

    private AppUpdate ToUpdate(SemVer version, bool isDowngrade)
    {
        var candidate = _candidates[_candidates.Keys.First(k => SemVer.Parse(k).Equals(version))];
        var asset = candidate.Info.TargetFullRelease;
        return new AppUpdate(version.ToString(), version.Channel, asset.Size, asset.NotesMarkdown, isDowngrade);
    }

    // ------------------------------------------------------------------ download + integridade

    public async Task DownloadAsync(AppUpdate update, IProgress<int> progress, CancellationToken cancellation)
    {
        EnsureSupported();
        var candidate = FindCandidate(update.Version)
            ?? throw new ManagerException("A atualização selecionada não está mais disponível. Verifique novamente.");

        // O Velopack apaga os pacotes antigos DURANTE o download (CleanPackagesExcept), então o pacote da versão
        // atual precisa ser guardado antes, para que a reversão continue possível.
        _guard.PrepareForUpdate(CurrentVersion);

        var asset = candidate.Info.TargetFullRelease;
        if (string.IsNullOrWhiteSpace(asset.SHA256) && string.IsNullOrWhiteSpace(asset.SHA1))
            throw new ManagerException("O pacote não informa nenhum hash de integridade; a atualização foi recusada por segurança.");

        // Sempre o pacote COMPLETO (sem deltas): um pacote reconstruído a partir de deltas não é idêntico, byte a byte,
        // ao publicado, então seus hashes não poderiam ser comparados com os do feed. Com o pacote completo, o
        // tamanho, o SHA-1 e o SHA-256 publicados valem para exatamente os bytes que serão instalados.
        var fullOnly = new UpdateInfo(asset, candidate.Info.IsDowngrade, null, new VelopackAsset[0]);

        try
        {
            await WithRetry(async () =>
            {
                // O Velopack baixa e confere o checksum do pacote; a verificação abaixo é uma segunda barreira.
                await candidate.Manager.DownloadUpdatesAsync(fullOnly, p => progress?.Report(p), cancellation);
                return true;
            }, cancellation);

            VerifyDownloadedPackage(asset);
        }
        catch (ManagerException) { throw; }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            DeleteDownloadedPackage(asset);
            throw IsIntegrityFailure(ex)
                ? new ManagerException("O pacote baixado falhou na verificação de integridade e foi descartado. Nada foi instalado.", ex)
                : Friendly(ex);
        }

        _downloaded = candidate;
        _logger.Info($"Atualização {update.Version} baixada e verificada.");
    }

    /// <summary>Segunda barreira de integridade, independente da feita pelo Velopack durante o download.</summary>
    private void VerifyDownloadedPackage(VelopackAsset asset)
    {
        var path = LocalPackagePath(asset);
        if (!File.Exists(path))
            throw new ManagerException("O pacote baixado não foi encontrado em disco.");

        // 1) tamanho exato
        if (asset.Size > 0 && new FileInfo(path).Length != asset.Size)
        {
            DeleteDownloadedPackage(asset);
            throw new ManagerException("O pacote baixado tem tamanho diferente do publicado e foi descartado. Nada foi instalado.");
        }

        // 2) SHA-256 calculado por nós, quando o feed o informa
        if (!string.IsNullOrWhiteSpace(asset.SHA256) &&
            !string.Equals(RollbackPackageStore.Sha256Of(path), asset.SHA256, StringComparison.OrdinalIgnoreCase))
        {
            DeleteDownloadedPackage(asset);
            throw new ManagerException("O SHA-256 do pacote baixado não confere com o publicado; o arquivo foi descartado. Nada foi instalado.");
        }

        // 3) SHA-1 do feed
        if (!string.IsNullOrWhiteSpace(asset.SHA1) &&
            !string.Equals(Sha1Of(path), asset.SHA1, StringComparison.OrdinalIgnoreCase))
        {
            DeleteDownloadedPackage(asset);
            throw new ManagerException("O SHA-1 do pacote baixado não confere com o publicado; o arquivo foi descartado. Nada foi instalado.");
        }
    }

    private string LocalPackagePath(VelopackAsset asset) =>
        Path.Combine(InstalledLocator?.PackagesDir ?? throw new ManagerException("A pasta de pacotes do instalador não foi encontrada."),
                     Path.GetFileName(asset.FileName)); // GetFileName impede path traversal vindo do feed

    private void DeleteDownloadedPackage(VelopackAsset asset)
    {
        try
        {
            var path = LocalPackagePath(asset);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            _logger.Warn("Não foi possível apagar o pacote inválido: " + ex.Message);
        }
    }

    private static string Sha1Of(string file)
    {
        using (var sha = System.Security.Cryptography.SHA1.Create())
        using (var stream = File.OpenRead(file))
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
    }

    private Candidate FindCandidate(string version)
    {
        if (!SemVer.TryParse(version, out var wanted)) return null;
        var key = _candidates.Keys.FirstOrDefault(k => SemVer.Parse(k).Equals(wanted));
        return key == null ? null : _candidates[key];
    }

    // ------------------------------------------------------------------ aplicar / reverter

    public void ApplyAndRestart()
    {
        EnsureSupported();
        if (_downloaded == null)
            throw new ManagerException("Nenhuma atualização foi baixada ainda.");

        _logger.Info($"Aplicando {_downloaded.Info.TargetFullRelease.Version} e reiniciando.");
        _downloaded.Manager.ApplyUpdatesAndRestart(_downloaded.Info.TargetFullRelease);
    }

    public RollbackInfo GetRollbackInfo()
    {
        var package = _guard.GetRollbackPackage();
        if (package == null || !File.Exists(package.File)) return null;
        return SemVer.TryParse(package.Version, out var target) && SemVer.TryParse(CurrentVersion, out var current) && target.Equals(current)
            ? null
            : new RollbackInfo(package.Version);
    }

    public void RollbackAndRestart(string failedVersion)
    {
        EnsureSupported();
        var package = _guard.GetRollbackPackage()
            ?? throw new ManagerException("Não há versão anterior preservada para reverter.");

        _packages.Restore(package); // confere SHA-256 antes de entregar ao instalador

        var asset = new VelopackAsset
        {
            PackageId = _probe.AppId,
            Version = SemanticVersion.Parse(package.Version),
            Type = VelopackAssetType.Full,
            FileName = Path.GetFileName(package.File),
            SHA256 = package.Sha256,
            Size = new FileInfo(package.File).Length
        };

        // Sem isto, o pacote da versão ruim (já baixado) seria reaplicado automaticamente na próxima abertura
        // pelo auto-apply do Velopack, desfazendo a reversão.
        PurgePackagesExcept(asset.FileName);

        _guard.RegisterRollback(failedVersion, package.Version);
        _logger.Warn($"Revertendo de {failedVersion} para {package.Version}.");

        var manager = NewManager(false, null, allowDowngrade: true);
        manager.ApplyUpdatesAndRestart(asset);
    }

    // ------------------------------------------------------------------ utilitários

    private void PurgePackagesExcept(string keepFileName)
    {
        var dir = InstalledLocator?.PackagesDir;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        foreach (var file in Directory.GetFiles(dir, "*.nupkg"))
        {
            if (string.Equals(Path.GetFileName(file), keepFileName, StringComparison.OrdinalIgnoreCase)) continue;
            try { File.Delete(file); _logger.Info("Pacote removido da fila de atualização: " + Path.GetFileName(file)); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _logger.Warn($"Não foi possível remover {Path.GetFileName(file)}: {ex.Message}");
            }
        }
    }

    private UpdateManager NewManager(bool includePrereleases, string channel, bool allowDowngrade) =>
        new UpdateManager(
            _sourceFactory(_location, includePrereleases),
            new UpdateOptions { ExplicitChannel = channel, AllowVersionDowngrade = allowDowngrade },
            _locator);

    private void EnsureSupported()
    {
        if (!IsSupported) throw new ManagerException(_unsupportedReason);
    }

    private async Task<T> WithRetry<T>(Func<Task<T>> operation, CancellationToken cancellation)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { return await operation(); }
            catch (Exception ex) when (attempt < _retryDelays.Length && IsTransient(ex) && !cancellation.IsCancellationRequested)
            {
                _logger.Warn($"Falha temporária de rede ({ex.Message}); nova tentativa em {_retryDelays[attempt].TotalSeconds:0}s.");
                await Task.Delay(_retryDelays[attempt], cancellation);
            }
        }
    }

    private static bool IsTransient(Exception ex) =>
        ex is HttpRequestException || ex is WebException || ex is TaskCanceledException || ex is TimeoutException || ex is IOException
        || (ex.InnerException != null && IsTransient(ex.InnerException));

    private static readonly string[] IntegrityMarkers =
        { "checksum", "hash", "sha1", "sha256", "doesn't match", "does not match", "mismatch" };

    /// <summary>Reconhece falhas de verificação de hash/checksum (mensagens do Velopack ou nossas).</summary>
    public static bool IsIntegrityFailure(Exception ex) =>
        IntegrityMarkers.Any(m => ex.Message.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0);

    private static ManagerException Friendly(Exception ex)
    {
        if (IsTransient(ex))
            return new ManagerException("Não foi possível acessar o GitHub. Verifique a conexão com a internet (e proxy/firewall) e tente novamente.", ex);
        if (ex.Message.IndexOf("403", StringComparison.Ordinal) >= 0 || ex.Message.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0)
            return new ManagerException("O GitHub limitou temporariamente as consultas. Tente novamente em alguns minutos.", ex);
        return new ManagerException("Falha ao consultar as atualizações: " + ex.Message, ex);
    }
}

