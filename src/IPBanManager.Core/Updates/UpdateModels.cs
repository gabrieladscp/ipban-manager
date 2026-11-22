using System;
using System.Collections.Generic;

namespace IPBanManager.Core.Updates;

public enum UpdateChannel
{
    /// <summary>Somente versões estáveis (sem pré-release).</summary>
    Stable,

    /// <summary>Versões Beta (pré-release) e também as estáveis mais novas.</summary>
    Beta
}

/// <summary>Preferências de atualização, por máquina.</summary>
public class UpdateSettings
{
    public UpdateChannel Channel { get; set; } = UpdateChannel.Stable;
    public bool AutoCheck { get; set; } = true;

    /// <summary>Baixa automaticamente (a instalação sempre respeita o aviso ao usuário).</summary>
    public bool AutoDownload { get; set; } = true;

    public int CheckIntervalHours { get; set; } = 6;

    public UpdateSettings Clone() => (UpdateSettings)MemberwiseClone();
}

/// <summary>Uma versão disponível no feed de releases.</summary>
public class AppUpdate
{
    public AppUpdate(string version, UpdateChannel channel, long sizeBytes, string notesMarkdown, bool isDowngrade)
    {
        Version = version;
        Channel = channel;
        SizeBytes = sizeBytes;
        NotesMarkdown = notesMarkdown ?? "";
        IsDowngrade = isDowngrade;
    }

    public string Version { get; }
    public UpdateChannel Channel { get; }
    public long SizeBytes { get; }
    public string NotesMarkdown { get; }

    /// <summary>Versão mais antiga que a atual (ex.: voltar do Beta para o Stable).</summary>
    public bool IsDowngrade { get; }
}

/// <summary>Versão anterior preservada, pronta para reversão.</summary>
public class RollbackInfo
{
    public RollbackInfo(string version) => Version = version;
    public string Version { get; }
}

/// <summary>Estado persistido para decidir se uma versão recém-instalada é saudável.</summary>
public class UpdateHealthState
{
    /// <summary>Última versão que comprovadamente funcionou (alvo de reversão).</summary>
    public string LastKnownGoodVersion { get; set; }

    /// <summary>Versão iniciada mas ainda não comprovada.</summary>
    public string PendingVersion { get; set; }

    /// <summary>Inicializações da versão pendente que não chegaram a ficar saudáveis.</summary>
    public int FailedStarts { get; set; }

    public bool CrashedBeforeHealthy { get; set; }

    /// <summary>Versões revertidas por falha: nunca são oferecidas de novo automaticamente.</summary>
    public List<string> BlockedVersions { get; set; } = new List<string>();

    public string RollbackPackageFile { get; set; }
    public string RollbackPackageVersion { get; set; }
    public string RollbackPackageSha256 { get; set; }
}

public enum StartupAction
{
    Continue,
    Rollback
}

public class StartupDecision
{
    private StartupDecision(StartupAction action, string target, string failedVersion)
    {
        Action = action;
        TargetVersion = target;
        FailedVersion = failedVersion;
    }

    public StartupAction Action { get; }
    public string TargetVersion { get; }
    public string FailedVersion { get; }

    public static StartupDecision Continue() => new StartupDecision(StartupAction.Continue, null, null);
    public static StartupDecision Rollback(string target, string failed) => new StartupDecision(StartupAction.Rollback, target, failed);
}
