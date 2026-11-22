using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IPBanManager.Core.Errors;
using IPBanManager.Core.Updates;
using IPBanManager.Core.Views;

namespace IPBanManager.Tests.Support;

internal sealed class FakeStateStore : IUpdateStateStore
{
    public UpdateHealthState State { get; set; } = new UpdateHealthState();
    public int Saves { get; private set; }

    public UpdateHealthState Load() => Clone(State);

    public void Save(UpdateHealthState state)
    {
        State = Clone(state);
        Saves++;
    }

    private static UpdateHealthState Clone(UpdateHealthState s) => new UpdateHealthState
    {
        LastKnownGoodVersion = s.LastKnownGoodVersion,
        PendingVersion = s.PendingVersion,
        FailedStarts = s.FailedStarts,
        CrashedBeforeHealthy = s.CrashedBeforeHealthy,
        BlockedVersions = new List<string>(s.BlockedVersions),
        RollbackPackageFile = s.RollbackPackageFile,
        RollbackPackageVersion = s.RollbackPackageVersion,
        RollbackPackageSha256 = s.RollbackPackageSha256,
    };
}

internal sealed class FakeRollbackPackages : IRollbackPackageStore
{
    public bool HasInstalledPackage { get; set; } = true;
    public bool PackageFileExists { get; set; } = true;
    public List<string> Preserved { get; } = new List<string>();

    public RollbackPackage Preserve(string version)
    {
        if (!HasInstalledPackage) return null;
        Preserved.Add(version);
        return new RollbackPackage($"C:\\rollback\\app-{version}.nupkg", version, "ABC123");
    }

    public bool Exists(RollbackPackage package) => PackageFileExists;
    public void Restore(RollbackPackage package) { }
}

internal sealed class FakeUpdateSettingsStore : IUpdateSettingsStore
{
    public UpdateSettings Settings { get; set; } = new UpdateSettings();
    public int Saves { get; private set; }
    public UpdateSettings Load() => Settings.Clone();
    public void Save(UpdateSettings settings) { Settings = settings.Clone(); Saves++; }
}

internal sealed class FakeUnsavedProbe : IUnsavedChangesProbe
{
    public bool HasUnsavedChanges { get; set; }
}

internal sealed class FakeUpdateService : IUpdateService
{
    public bool IsSupported { get; set; } = true;
    public string UnsupportedReason { get; set; } = "indisponível";
    public string CurrentVersion { get; set; } = "1.0.0";
    public UpdateChannel InstalledChannel { get; set; } = UpdateChannel.Stable;
    public AppUpdate NextUpdate { get; set; }
    public Exception CheckException { get; set; }
    public Exception DownloadException { get; set; }
    public RollbackInfo Rollback { get; set; }
    public UpdateSettings LastCheckSettings { get; private set; }
    public int CheckCalls { get; private set; }
    public int DownloadCalls { get; private set; }
    public int ApplyCalls { get; private set; }
    public string RolledBackFrom { get; private set; }

    /// <summary>Se definido, o download aguarda este sinal (simula download demorado).</summary>
    public TaskCompletionSource<bool> DownloadGate { get; set; }

    public Task<AppUpdate> CheckAsync(UpdateSettings settings, CancellationToken cancellation)
    {
        CheckCalls++;
        LastCheckSettings = settings;
        if (CheckException != null) throw CheckException;
        return Task.FromResult(NextUpdate);
    }

    public async Task DownloadAsync(AppUpdate update, IProgress<int> progress, CancellationToken cancellation)
    {
        DownloadCalls++;
        if (DownloadException != null) throw DownloadException;
        if (DownloadGate != null) await DownloadGate.Task;
        progress?.Report(100);
    }

    public void ApplyAndRestart() => ApplyCalls++;
    public RollbackInfo GetRollbackInfo() => Rollback;
    public void RollbackAndRestart(string failedVersion) => RolledBackFrom = failedVersion;
}

internal sealed class FakeUpdateView : FakeViewBase, IUpdateView
{
    public UpdateSettings Settings { get; set; } = new UpdateSettings();
    public string Version { get; private set; }
    public bool? Supported { get; private set; }
    public string StatusText { get; private set; }
    public AppUpdate Available { get; private set; }
    public int LastProgress { get; private set; } = -1;
    public RollbackInfo ShownRollback { get; private set; }
    public bool Busy { get; set; }
    public List<bool> BusyChanges { get; } = new List<bool>();

    public event EventHandler CheckRequested;
    public event EventHandler InstallRequested;
    public event EventHandler RollbackRequested;
    public event EventHandler SettingsChanged;

    public void ShowCurrent(string version, string installedChannel, bool supported, string unsupportedReason)
    {
        Version = version;
        Supported = supported;
    }

    public void ShowStatus(string text) => StatusText = text;
    public void ShowAvailable(AppUpdate update) => Available = update;
    public void ShowProgress(int percent) => LastProgress = percent;
    public void ShowRollback(RollbackInfo info) => ShownRollback = info;

    public void RaiseCheck() => CheckRequested?.Invoke(this, EventArgs.Empty);
    public void RaiseInstall() => InstallRequested?.Invoke(this, EventArgs.Empty);
    public void RaiseRollback() => RollbackRequested?.Invoke(this, EventArgs.Empty);
    public void RaiseSettingsChanged() => SettingsChanged?.Invoke(this, EventArgs.Empty);
}
