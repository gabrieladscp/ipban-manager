using IPBanManager.Core.Updates;
using IPBanManager.Tests.Support;
using Xunit;

namespace IPBanManager.Tests.Core;

[Trait("Category", "Unit")]
public class UpdateHealthGuardTests
{
    private readonly FakeStateStore _store = new FakeStateStore();
    private readonly FakeRollbackPackages _packages = new FakeRollbackPackages();
    private readonly UpdateHealthGuard _guard;

    public UpdateHealthGuardTests() =>
        _guard = new UpdateHealthGuard(_store, _packages, new RecordingLogger());

    /// <summary>Estado típico após uma atualização: 1.0.0 era a boa, 1.1.0 acabou de ser instalada.</summary>
    private void GivenFreshlyUpdatedTo(string version, string previousGood = "1.0.0")
    {
        _store.State.LastKnownGoodVersion = previousGood;
        _store.State.RollbackPackageFile = $"C:\\rollback\\app-{previousGood}.nupkg";
        _store.State.RollbackPackageVersion = previousGood;
        _store.State.RollbackPackageSha256 = "ABC123";
    }

    [Fact]
    public void EvaluateStartup_FirstRun_RecordsCurrentVersionAsGood()
    {
        var decision = _guard.EvaluateStartup("1.0.0");

        Assert.Equal(StartupAction.Continue, decision.Action);
        Assert.Equal("1.0.0", _store.State.LastKnownGoodVersion);
    }

    [Fact]
    public void EvaluateStartup_SameVersionAsLastGood_ContinuesAndClearsCounters()
    {
        _store.State.LastKnownGoodVersion = "1.0.0";
        _store.State.FailedStarts = 5;

        var decision = _guard.EvaluateStartup("1.0.0");

        Assert.Equal(StartupAction.Continue, decision.Action);
        Assert.Equal(0, _store.State.FailedStarts);
    }

    [Fact]
    public void EvaluateStartup_NewVersionFirstStart_ContinuesAndCountsAttempt()
    {
        GivenFreshlyUpdatedTo("1.1.0");

        var decision = _guard.EvaluateStartup("1.1.0");

        Assert.Equal(StartupAction.Continue, decision.Action);
        Assert.Equal("1.1.0", _store.State.PendingVersion);
        Assert.Equal(1, _store.State.FailedStarts);
    }

    [Fact]
    public void EvaluateStartup_TwoFailedStarts_ThirdStartRequestsRollback()
    {
        GivenFreshlyUpdatedTo("1.1.0");

        _guard.EvaluateStartup("1.1.0"); // 1ª abertura (não chegou a ficar saudável)
        _guard.EvaluateStartup("1.1.0"); // 2ª
        var third = _guard.EvaluateStartup("1.1.0");

        Assert.Equal(StartupAction.Rollback, third.Action);
        Assert.Equal("1.0.0", third.TargetVersion);
        Assert.Equal("1.1.0", third.FailedVersion);
    }

    [Fact]
    public void EvaluateStartup_CrashRecordedBeforeHealthy_RollsBackOnNextStart()
    {
        GivenFreshlyUpdatedTo("1.1.0");
        _guard.EvaluateStartup("1.1.0");
        _guard.RecordCrash("1.1.0");

        var decision = _guard.EvaluateStartup("1.1.0");

        Assert.Equal(StartupAction.Rollback, decision.Action);
    }

    [Fact]
    public void EvaluateStartup_UnstableButNoRollbackPackage_KeepsRunning()
    {
        _store.State.LastKnownGoodVersion = "1.0.0"; // sem pacote preservado
        _guard.EvaluateStartup("1.1.0");
        _guard.RecordCrash("1.1.0");

        var decision = _guard.EvaluateStartup("1.1.0");

        Assert.Equal(StartupAction.Continue, decision.Action);
    }

    [Fact]
    public void MarkHealthy_PromotesNewVersionAndResetsEverything()
    {
        GivenFreshlyUpdatedTo("1.1.0");
        _guard.EvaluateStartup("1.1.0");

        var promoted = _guard.MarkHealthy("1.1.0");

        Assert.True(promoted);
        Assert.Equal("1.1.0", _store.State.LastKnownGoodVersion);
        Assert.Null(_store.State.PendingVersion);
        Assert.Equal(0, _store.State.FailedStarts);
        Assert.False(_store.State.CrashedBeforeHealthy);
    }

    [Fact]
    public void MarkHealthy_AlreadyGood_ReportsNoPromotion()
    {
        _store.State.LastKnownGoodVersion = "1.0.0";

        Assert.False(_guard.MarkHealthy("1.0.0"));
    }

    [Fact]
    public void MarkHealthy_AfterCrash_ClearsCrashFlag()
    {
        GivenFreshlyUpdatedTo("1.1.0");
        _guard.RecordCrash("1.1.0");

        _guard.MarkHealthy("1.1.0");

        Assert.False(_store.State.CrashedBeforeHealthy);
    }

    [Fact]
    public void RecordCrash_OnProvenVersion_IsIgnored()
    {
        _store.State.LastKnownGoodVersion = "1.0.0";

        _guard.RecordCrash("1.0.0");

        Assert.False(_store.State.CrashedBeforeHealthy);
    }

    [Fact]
    public void RegisterRollback_BlocksFailedVersionOnceAndTargetsGoodOne()
    {
        GivenFreshlyUpdatedTo("1.1.0");

        _guard.RegisterRollback("1.1.0", "1.0.0");
        _guard.RegisterRollback("1.1.0", "1.0.0"); // idempotente

        Assert.Single(_store.State.BlockedVersions);
        Assert.True(_guard.IsBlocked("1.1.0"));
        Assert.True(_guard.IsBlocked("v1.1.0"));
        Assert.False(_guard.IsBlocked("1.1.1"));
        Assert.Equal("1.0.0", _store.State.LastKnownGoodVersion);
        Assert.Equal(0, _store.State.FailedStarts);
    }

    [Fact]
    public void PrepareForUpdate_ProvenVersion_PreservesItsPackage()
    {
        _store.State.LastKnownGoodVersion = "1.0.0";

        _guard.PrepareForUpdate("1.0.0");

        Assert.Equal(new[] { "1.0.0" }, _packages.Preserved);
        Assert.Equal("1.0.0", _store.State.RollbackPackageVersion);
        Assert.Equal("ABC123", _store.State.RollbackPackageSha256);
    }

    [Fact]
    public void PrepareForUpdate_UnprovenVersion_KeepsExistingRollbackPackage()
    {
        GivenFreshlyUpdatedTo("1.1.0");

        _guard.PrepareForUpdate("1.1.0"); // 1.1.0 ainda não foi comprovada

        Assert.Empty(_packages.Preserved);
        Assert.Equal("1.0.0", _store.State.RollbackPackageVersion);
    }

    [Fact]
    public void PrepareForUpdate_CalledAgainAfterFailedDownload_DoesNotRepeatOrWarnAboutMissingPackage()
    {
        _store.State.LastKnownGoodVersion = "1.0.0";
        _guard.PrepareForUpdate("1.0.0");
        _packages.HasInstalledPackage = false; // o Velopack já apagou o pacote instalado

        _guard.PrepareForUpdate("1.0.0");

        Assert.Single(_packages.Preserved);
        Assert.Equal("1.0.0", _store.State.RollbackPackageVersion);
    }

    [Fact]
    public void PrepareForUpdate_NoInstalledPackage_LeavesStateUntouched()
    {
        _store.State.LastKnownGoodVersion = "1.0.0";
        _packages.HasInstalledPackage = false;

        _guard.PrepareForUpdate("1.0.0");

        Assert.Null(_store.State.RollbackPackageFile);
    }

    [Fact]
    public void GetRollbackPackage_NothingPreserved_ReturnsNull() =>
        Assert.Null(_guard.GetRollbackPackage());
}
