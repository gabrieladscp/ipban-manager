using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using IPBanManager.Core.Abstractions;
using IPBanManager.Core.Errors;
using IPBanManager.Core.Updates;

namespace IPBanManager.Infrastructure.Updates;

/// <summary>Identidade do pacote Velopack. Deve ser igual ao --packId usado pelo workflow de release.</summary>
public static class UpdateIdentity
{
    public const string PackId = "IPBanManager";
}

/// <summary>
/// Guarda o pacote (.nupkg completo) da última versão boa em ProgramData e o restaura, com verificação
/// SHA-256, na pasta de pacotes do Velopack quando for necessário reverter.
/// </summary>
public sealed class RollbackPackageStore : IRollbackPackageStore
{
    private readonly Func<string> _packagesDir;
    private readonly string _appId;
    private readonly string _storageDir;
    private readonly IAppLogger _logger;

    public RollbackPackageStore(Func<string> packagesDir, string appId, IAppLogger logger, string storageDir = null)
    {
        _packagesDir = packagesDir;
        _appId = appId;
        _logger = logger ?? NullLogger.Instance;
        _storageDir = storageDir ?? Path.Combine(UpdateStorage.DefaultDirectory, "rollback");
    }

    public RollbackPackage Preserve(string version)
    {
        var source = FindInstalledPackage(version);
        if (source == null) return null;

        Directory.CreateDirectory(_storageDir);
        var target = Path.Combine(_storageDir, Path.GetFileName(source));
        File.Copy(source, target, overwrite: true);
        DurableIO.FlushToDisk(target);
        var hash = Sha256Of(target);

        // Só um pacote de reversão é mantido: os anteriores são removidos.
        foreach (var old in Directory.GetFiles(_storageDir, "*.nupkg").Where(f => !SamePath(f, target)))
            TryDelete(old);

        return new RollbackPackage(target, version, hash);
    }

    public bool Exists(RollbackPackage package) => package != null && File.Exists(package.File);

    public void Restore(RollbackPackage package)
    {
        if (package == null || !File.Exists(package.File))
            throw new ManagerException("O pacote da versão anterior não foi encontrado; não é possível reverter.");

        if (!string.Equals(Sha256Of(package.File), package.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(package.File);
            throw new ManagerException("O pacote da versão anterior está corrompido (falha na verificação SHA-256); a reversão foi cancelada.");
        }

        var packagesDir = _packagesDir();
        if (string.IsNullOrEmpty(packagesDir))
            throw new ManagerException("A pasta de pacotes do instalador não foi encontrada.");

        Directory.CreateDirectory(packagesDir);
        File.Copy(package.File, Path.Combine(packagesDir, Path.GetFileName(package.File)), overwrite: true);
    }

    private string FindInstalledPackage(string version)
    {
        var dir = _packagesDir();
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;

        // A cópia criada pelo instalador é "{id}-{versão}-full.nupkg"; pacotes baixados do feed incluem o canal:
        // "{id}-{versão}-{canal}-full.nupkg". Nomes exatos: "1.0.0" nunca casa com "1.0.0-beta.1".
        var names = new[] { "", "-stable", "-beta" }.Select(channel => $"{_appId}-{version}{channel}-full.nupkg");
        return names.Select(n => Path.Combine(dir, n)).FirstOrDefault(File.Exists);
    }

    public static string Sha256Of(string file)
    {
        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead(file))
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            _logger.Warn($"Não foi possível remover {file}: {ex.Message}");
        }
    }
}
