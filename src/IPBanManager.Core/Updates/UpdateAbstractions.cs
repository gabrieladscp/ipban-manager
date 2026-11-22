using System;
using System.Threading;
using System.Threading.Tasks;

namespace IPBanManager.Core.Updates;

/// <summary>Serviço de atualização (implementado sobre o Velopack na camada de infraestrutura).</summary>
public interface IUpdateService
{
    /// <summary>Falso em compilações de desenvolvimento ou instalações que não são do Velopack.</summary>
    bool IsSupported { get; }

    string UnsupportedReason { get; }
    string CurrentVersion { get; }
    UpdateChannel InstalledChannel { get; }

    /// <summary>Retorna a melhor versão disponível para as preferências, ou null se já estiver atualizado.</summary>
    Task<AppUpdate> CheckAsync(UpdateSettings settings, CancellationToken cancellation);

    /// <summary>Baixa e verifica a integridade do pacote. Lança <c>ManagerException</c> se o pacote for inválido.</summary>
    Task DownloadAsync(AppUpdate update, IProgress<int> progress, CancellationToken cancellation);

    /// <summary>Encerra o aplicativo, instala a versão baixada e reabre.</summary>
    void ApplyAndRestart();

    /// <summary>Versão anterior disponível para reversão, ou null.</summary>
    RollbackInfo GetRollbackInfo();

    /// <summary>Reinstala a última versão saudável preservada. O app é encerrado e reaberto.</summary>
    void RollbackAndRestart(string failedVersion);
}

public interface IUpdateSettingsStore
{
    UpdateSettings Load();
    void Save(UpdateSettings settings);
}

public interface IUpdateStateStore
{
    UpdateHealthState Load();
    void Save(UpdateHealthState state);
}

/// <summary>Pacote completo de uma versão, guardado fora da pasta do app para permitir reversão.</summary>
public class RollbackPackage
{
    public RollbackPackage(string file, string version, string sha256)
    {
        File = file;
        Version = version;
        Sha256 = sha256;
    }

    public string File { get; }
    public string Version { get; }
    public string Sha256 { get; }
}

public interface IRollbackPackageStore
{
    /// <summary>Copia o pacote da versão instalada para o armazenamento de reversão. Null se não houver pacote.</summary>
    RollbackPackage Preserve(string version);

    /// <summary>Indica se o arquivo do pacote preservado ainda existe.</summary>
    bool Exists(RollbackPackage package);

    /// <summary>Verifica a integridade (SHA-256) e disponibiliza o pacote para o instalador. Lança ManagerException se corrompido.</summary>
    void Restore(RollbackPackage package);
}

/// <summary>Permite ao updater evitar reiniciar o aplicativo com edições não salvas.</summary>
public interface IUnsavedChangesProbe
{
    bool HasUnsavedChanges { get; }
}
