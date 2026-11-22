using System;
using System.Linq;
using IPBanManager.Core.Abstractions;

namespace IPBanManager.Core.Updates;

/// <summary>
/// Decide se uma versão recém-instalada é saudável. Uma versão só vira "última boa conhecida" depois de
/// concluir a inicialização e ficar estável (ou encerrar normalmente). Se ela falhar ao iniciar repetidas
/// vezes, ou travar antes de ficar saudável, o próximo início pede reversão para a última versão boa.
/// </summary>
public class UpdateHealthGuard
{
    /// <summary>Quantas inicializações sem sucesso são toleradas antes de reverter.</summary>
    public const int MaxFailedStarts = 2;

    private readonly IUpdateStateStore _store;
    private readonly IRollbackPackageStore _packages;
    private readonly IAppLogger _logger;
    private readonly object _gate = new object();

    public UpdateHealthGuard(IUpdateStateStore store, IRollbackPackageStore packages, IAppLogger logger)
    {
        _store = store;
        _packages = packages;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Chamar imediatamente antes de aplicar uma atualização: guarda o pacote da versão atual (se ela já foi
    /// comprovada) para que ela possa ser restaurada, mesmo que o instalador apague os pacotes antigos.
    /// </summary>
    public void PrepareForUpdate(string currentVersion)
    {
        lock (_gate)
        {
            var state = _store.Load();
            if (!SameVersion(state.LastKnownGoodVersion, currentVersion))
            {
                _logger.Warn($"A versão {currentVersion} ainda não foi comprovada; mantendo o pacote de reversão de {state.LastKnownGoodVersion}.");
                return;
            }
            // Tentativas repetidas (ex.: download que falhou): o Velopack já pode ter apagado o pacote instalado,
            // mas o backup feito na primeira tentativa continua válido.
            var existing = GetRollbackPackage();
            if (existing != null && SameVersion(existing.Version, currentVersion) && _packages.Exists(existing))
                return;

            var package = _packages.Preserve(currentVersion);
            if (package == null)
            {
                _logger.Warn($"Pacote da versão {currentVersion} não encontrado; reversão automática indisponível para esta atualização.");
                return;
            }
            state.RollbackPackageFile = package.File;
            state.RollbackPackageVersion = package.Version;
            state.RollbackPackageSha256 = package.Sha256;
            _store.Save(state);
            _logger.Info($"Pacote de reversão da versão {package.Version} preservado.");
        }
    }

    /// <summary>Pacote de reversão preservado, ou null.</summary>
    public RollbackPackage GetRollbackPackage()
    {
        var state = _store.Load();
        return string.IsNullOrEmpty(state.RollbackPackageFile) || string.IsNullOrEmpty(state.RollbackPackageVersion)
            ? null
            : new RollbackPackage(state.RollbackPackageFile, state.RollbackPackageVersion, state.RollbackPackageSha256);
    }

    /// <summary>Chamar no início do processo, antes de abrir a interface.</summary>
    public StartupDecision EvaluateStartup(string currentVersion)
    {
        lock (_gate)
        {
            var state = _store.Load();

            if (string.IsNullOrEmpty(state.LastKnownGoodVersion) || SameVersion(state.LastKnownGoodVersion, currentVersion))
            {
                // Primeira execução registrada ou versão já comprovada.
                state.LastKnownGoodVersion = currentVersion;
                state.PendingVersion = null;
                state.FailedStarts = 0;
                state.CrashedBeforeHealthy = false;
                _store.Save(state);
                return StartupDecision.Continue();
            }

            // Versão diferente da última boa: está em período de prova.
            if (!SameVersion(state.PendingVersion, currentVersion))
            {
                state.PendingVersion = currentVersion;
                state.FailedStarts = 0;
                state.CrashedBeforeHealthy = false;
            }

            bool shouldRollback = state.CrashedBeforeHealthy || state.FailedStarts >= MaxFailedStarts;
            if (shouldRollback && !string.IsNullOrEmpty(state.RollbackPackageFile))
            {
                _logger.Error($"Versão {currentVersion} não ficou saudável (falhas: {state.FailedStarts}, crash: {state.CrashedBeforeHealthy}). " +
                              $"Revertendo para {state.RollbackPackageVersion}.");
                _store.Save(state);
                return StartupDecision.Rollback(state.RollbackPackageVersion, currentVersion);
            }

            if (shouldRollback)
                _logger.Warn($"Versão {currentVersion} instável, mas não há pacote preservado para reverter para {state.LastKnownGoodVersion}.");

            state.FailedStarts++;
            _store.Save(state);
            return StartupDecision.Continue();
        }
    }

    /// <summary>Marca a versão atual como boa. Retorna true se ela acabou de ser promovida.</summary>
    public bool MarkHealthy(string currentVersion)
    {
        lock (_gate)
        {
            var state = _store.Load();
            bool promoted = !SameVersion(state.LastKnownGoodVersion, currentVersion);
            state.LastKnownGoodVersion = currentVersion;
            state.PendingVersion = null;
            state.FailedStarts = 0;
            state.CrashedBeforeHealthy = false;
            _store.Save(state);
            return promoted;
        }
    }

    /// <summary>Registra falha grave durante o período de prova: o próximo início reverte.</summary>
    public void RecordCrash(string currentVersion)
    {
        lock (_gate)
        {
            var state = _store.Load();
            if (!SameVersion(state.LastKnownGoodVersion, currentVersion))
            {
                state.PendingVersion = currentVersion;
                state.CrashedBeforeHealthy = true;
                _store.Save(state);
            }
        }
    }

    /// <summary>Registra uma reversão: bloqueia a versão ruim e passa a considerar a de destino como boa.</summary>
    public void RegisterRollback(string failedVersion, string targetVersion)
    {
        lock (_gate)
        {
            var state = _store.Load();
            if (!string.IsNullOrEmpty(failedVersion) && !state.BlockedVersions.Any(v => SameVersion(v, failedVersion)))
                state.BlockedVersions.Add(failedVersion);
            state.LastKnownGoodVersion = targetVersion;
            state.PendingVersion = null;
            state.FailedStarts = 0;
            state.CrashedBeforeHealthy = false;
            _store.Save(state);
        }
    }

    public bool IsBlocked(string version) =>
        _store.Load().BlockedVersions.Any(v => SameVersion(v, version));

    private static bool SameVersion(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        return SemVer.TryParse(a, out var x) && SemVer.TryParse(b, out var y) ? x.Equals(y) : a == b;
    }
}
