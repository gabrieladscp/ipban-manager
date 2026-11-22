using System;
using System.Threading.Tasks;
using IPBanManager.Core.Errors;
using IPBanManager.Core.Presenters;
using IPBanManager.Core.Updates;
using IPBanManager.Tests.Support;
using Xunit;

namespace IPBanManager.Tests.Presenters;

[Trait("Category", "Unit")]
public class UpdatePresenterTests
{
    private readonly FakeUpdateService _service = new FakeUpdateService();
    private readonly FakeUpdateView _view = new FakeUpdateView();
    private readonly FakeUpdateSettingsStore _settings = new FakeUpdateSettingsStore();
    private readonly FakeUnsavedProbe _unsaved = new FakeUnsavedProbe();
    private readonly RecordingLogger _logger = new RecordingLogger();
    private readonly UpdatePresenter _presenter;

    private static AppUpdate Update(string version = "1.1.0", bool downgrade = false) =>
        new AppUpdate(version, SemVer.Parse(version).Channel, 2_500_000, "notas", downgrade);

    public UpdatePresenterTests() =>
        _presenter = new UpdatePresenter(_view, _service, _settings, _unsaved, _logger);

    // ---------- carga inicial ----------

    [Fact]
    public async Task RefreshAsync_ShowsSettingsCurrentVersionAndRollbackOption()
    {
        _settings.Settings = new UpdateSettings { Channel = UpdateChannel.Beta };
        _service.CurrentVersion = "1.2.3";
        _service.Rollback = new RollbackInfo("1.2.0");

        await _presenter.RefreshAsync();

        Assert.Equal(UpdateChannel.Beta, _view.Settings.Channel);
        Assert.Equal("1.2.3", _view.Version);
        Assert.True(_view.Supported);
        Assert.Equal("1.2.0", _view.ShownRollback.Version);
    }

    [Fact]
    public async Task RefreshAsync_Unsupported_ExplainsWhy()
    {
        _service.IsSupported = false;
        _service.UnsupportedReason = "build de desenvolvimento";

        await _presenter.RefreshAsync();

        Assert.False(_view.Supported);
        Assert.Equal("build de desenvolvimento", _view.StatusText);
    }

    // ---------- verificação manual ----------

    [Fact]
    public async Task CheckNowAsync_NewVersion_ShowsItAndAnnouncesIt()
    {
        _service.NextUpdate = Update("1.1.0");

        await _presenter.CheckNowAsync();

        Assert.Equal("1.1.0", _view.Available.Version);
        Assert.Contains("1.1.0", _view.StatusText);
        Assert.False(_view.Busy);
    }

    [Fact]
    public async Task CheckNowAsync_UpToDate_SaysSoAndClearsPanel()
    {
        _service.NextUpdate = null;

        await _presenter.CheckNowAsync();

        Assert.Null(_view.Available);
        Assert.Contains("mais recente", _view.StatusText);
    }

    [Fact]
    public async Task CheckNowAsync_Unsupported_WarnsWithoutCallingService()
    {
        _service.IsSupported = false;

        await _presenter.CheckNowAsync();

        Assert.Equal(0, _service.CheckCalls);
        Assert.Single(_view.Warnings);
    }

    [Fact]
    public async Task CheckNowAsync_NetworkFailure_ShowsFriendlyErrorAndLogsIt()
    {
        _service.CheckException = new ManagerException("Não foi possível acessar o GitHub.");

        await _presenter.CheckNowAsync();

        Assert.Equal(new[] { "Não foi possível acessar o GitHub." }, _view.Errors);
        Assert.Contains(_logger.Entries, e => e.StartsWith("ERROR"));
        Assert.False(_view.Busy);
    }

    [Fact]
    public async Task CheckNowAsync_UsesPersistedSettings()
    {
        _settings.Settings = new UpdateSettings { Channel = UpdateChannel.Beta };

        await _presenter.CheckNowAsync();

        Assert.Equal(UpdateChannel.Beta, _service.LastCheckSettings.Channel);
    }

    // ---------- instalação manual ----------

    [Fact]
    public async Task InstallAsync_WithoutPriorCheck_AsksToCheckFirst()
    {
        await _presenter.InstallAsync();

        Assert.Equal(0, _service.DownloadCalls);
        Assert.Single(_view.Infos);
    }

    [Fact]
    public async Task InstallAsync_Confirmed_DownloadsThenAppliesOnce()
    {
        _service.NextUpdate = Update();
        await _presenter.CheckNowAsync();

        await _presenter.InstallAsync();

        Assert.Equal(1, _service.DownloadCalls);
        Assert.Equal(1, _service.ApplyCalls);
        Assert.Equal(100, _view.LastProgress);
    }

    [Fact]
    public async Task InstallAsync_UserDeclines_DoesNothing()
    {
        _service.NextUpdate = Update();
        await _presenter.CheckNowAsync();
        _view.ConfirmAnswer = false;

        await _presenter.InstallAsync();

        Assert.Equal(0, _service.DownloadCalls);
        Assert.Equal(0, _service.ApplyCalls);
        Assert.False(_view.Busy);
    }

    [Fact]
    public async Task InstallAsync_UnsavedChanges_RefusesBeforeAnyDownload()
    {
        _service.NextUpdate = Update();
        await _presenter.CheckNowAsync();
        _unsaved.HasUnsavedChanges = true;

        await _presenter.InstallAsync();

        Assert.Equal(0, _service.DownloadCalls);
        Assert.Contains("não salvas", _view.Errors[0]);
    }

    [Fact]
    public async Task InstallAsync_IntegrityFailure_ShowsErrorAndNeverApplies()
    {
        _service.NextUpdate = Update();
        await _presenter.CheckNowAsync();
        _service.DownloadException = new ManagerException("O pacote baixado falhou na verificação de integridade e foi descartado. Nada foi instalado.");

        await _presenter.InstallAsync();

        Assert.Equal(0, _service.ApplyCalls);
        Assert.Contains("integridade", _view.Errors[0]);
        Assert.Equal(-1, _view.LastProgress);
        Assert.Contains("Nada foi alterado", _view.StatusText);
        Assert.False(_view.Busy);
    }

    [Fact]
    public async Task InstallAsync_Downgrade_ConfirmationMentionsOlderVersion()
    {
        _service.CurrentVersion = "1.2.0-beta.1";
        _service.NextUpdate = Update("1.1.0", downgrade: true);
        await _presenter.CheckNowAsync();

        await _presenter.InstallAsync();

        Assert.Contains("mais antiga", _view.Questions[0]);
    }

    [Fact]
    public async Task InstallAsync_WhileAnotherOperationRuns_IsRejectedInsteadOfOverlapping()
    {
        _service.NextUpdate = Update();
        await _presenter.CheckNowAsync();
        _service.DownloadGate = new TaskCompletionSource<bool>();
        var first = _presenter.InstallAsync(); // fica preso no download

        await _presenter.CheckNowAsync();       // concorrente

        Assert.Contains(_view.Infos, i => i.Contains("em andamento"));
        _service.DownloadGate.SetResult(true);
        await first;
        Assert.Equal(1, _service.DownloadCalls);
    }

    [Fact]
    public async Task AutoCheckAsync_WhileConfirmationDialogIsOpen_DoesNotStartAnotherDownload()
    {
        // Regressão encontrada em teste real: o timer em segundo plano disparou durante o diálogo e baixou o mesmo pacote.
        _service.NextUpdate = Update();
        await _presenter.CheckNowAsync();
        var checksBefore = _service.CheckCalls;
        _view.OnConfirm = () => _presenter.AutoCheckAsync().GetAwaiter().GetResult();

        await _presenter.InstallAsync();

        Assert.Equal(checksBefore, _service.CheckCalls);
        Assert.Equal(1, _service.DownloadCalls);
    }

    // ---------- verificação automática ----------

    [Fact]
    public async Task AutoCheckAsync_AutoCheckDisabled_DoesNothing()
    {
        _settings.Settings = new UpdateSettings { AutoCheck = false };
        _service.NextUpdate = Update();

        await _presenter.AutoCheckAsync();

        Assert.Equal(0, _service.CheckCalls);
    }

    [Fact]
    public async Task AutoCheckAsync_Unsupported_DoesNothing()
    {
        _service.IsSupported = false;

        await _presenter.AutoCheckAsync();

        Assert.Equal(0, _service.CheckCalls);
    }

    [Fact]
    public async Task AutoCheckAsync_NewVersion_DownloadsAndOffersRestart()
    {
        _service.NextUpdate = Update();
        _view.ConfirmAnswer = false;

        await _presenter.AutoCheckAsync();

        Assert.Equal(1, _service.DownloadCalls);
        Assert.Contains("Reiniciar agora", _view.Questions[0]);
        Assert.Equal(0, _service.ApplyCalls);
    }

    [Fact]
    public async Task AutoCheckAsync_UserAcceptsRestart_AppliesUpdate()
    {
        _service.NextUpdate = Update();

        await _presenter.AutoCheckAsync();

        Assert.Equal(1, _service.ApplyCalls);
    }

    [Fact]
    public async Task AutoCheckAsync_UnsavedChanges_NeverInterruptsTheUser()
    {
        _service.NextUpdate = Update();
        _unsaved.HasUnsavedChanges = true;

        await _presenter.AutoCheckAsync();

        Assert.Empty(_view.Questions);
        Assert.Equal(0, _service.ApplyCalls);
        Assert.Contains(_view.Infos, i => i.Contains("próxima abertura"));
    }

    [Fact]
    public async Task AutoCheckAsync_AutoDownloadOff_OnlyAnnouncesTheVersion()
    {
        _settings.Settings = new UpdateSettings { AutoDownload = false };
        _service.NextUpdate = Update();

        await _presenter.AutoCheckAsync();

        Assert.Equal(0, _service.DownloadCalls);
        Assert.Contains(_view.Infos, i => i.Contains("1.1.0"));
    }

    [Fact]
    public async Task AutoCheckAsync_Downgrade_IsNeverInstalledAutomatically()
    {
        _service.NextUpdate = Update("1.0.0", downgrade: true);

        await _presenter.AutoCheckAsync();

        Assert.Equal(0, _service.DownloadCalls);
        Assert.Equal(0, _service.ApplyCalls);
    }

    [Fact]
    public async Task AutoCheckAsync_SameVersionDeclinedOnce_IsNotPromptedAgain()
    {
        _service.NextUpdate = Update();
        _view.ConfirmAnswer = false;

        await _presenter.AutoCheckAsync();
        await _presenter.AutoCheckAsync();

        Assert.Single(_view.Questions);
    }

    [Fact]
    public async Task AutoCheckAsync_NetworkError_IsOnlyLoggedAndNeverShownToTheUser()
    {
        _service.CheckException = new ManagerException("sem internet");

        await _presenter.AutoCheckAsync();

        Assert.Empty(_view.Errors);
        Assert.Empty(_view.Warnings);
        Assert.Contains(_logger.Entries, e => e.StartsWith("WARN") && e.Contains("sem internet"));
    }

    [Fact]
    public async Task AutoCheckAsync_DownloadFails_NeverApplies()
    {
        _service.NextUpdate = Update();
        _service.DownloadException = new ManagerException("pacote inválido");

        await _presenter.AutoCheckAsync();

        Assert.Equal(0, _service.ApplyCalls);
        Assert.Empty(_view.Errors);
    }

    // ---------- reversão manual ----------

    [Fact]
    public async Task RollbackAsync_NothingPreserved_Informs()
    {
        _service.Rollback = null;

        await _presenter.RollbackAsync();

        Assert.Null(_service.RolledBackFrom);
        Assert.Single(_view.Infos);
    }

    [Fact]
    public async Task RollbackAsync_Confirmed_RollsBackFromCurrentVersion()
    {
        _service.CurrentVersion = "1.1.0";
        _service.Rollback = new RollbackInfo("1.0.0");

        await _presenter.RollbackAsync();

        Assert.Equal("1.1.0", _service.RolledBackFrom);
        Assert.Contains("1.0.0", _view.Questions[0]);
    }

    [Fact]
    public async Task RollbackAsync_Declined_DoesNothing()
    {
        _service.Rollback = new RollbackInfo("1.0.0");
        _view.ConfirmAnswer = false;

        await _presenter.RollbackAsync();

        Assert.Null(_service.RolledBackFrom);
    }

    [Fact]
    public async Task RollbackAsync_UnsavedChanges_Refuses()
    {
        _service.Rollback = new RollbackInfo("1.0.0");
        _unsaved.HasUnsavedChanges = true;

        await _presenter.RollbackAsync();

        Assert.Null(_service.RolledBackFrom);
        Assert.Single(_view.Errors);
    }

    // ---------- preferências ----------

    [Fact]
    public async Task SettingsChanged_PersistsChannelAndExplainsBeta()
    {
        _view.Settings = new UpdateSettings { Channel = UpdateChannel.Beta };

        _view.RaiseSettingsChanged();
        await _view.NextMessageAsync();

        Assert.Equal(UpdateChannel.Beta, _settings.Settings.Channel);
        Assert.Contains("Beta", _view.StatusText);
        Assert.Null(_view.Available); // o resultado anterior não vale mais para o novo canal
    }

    [Fact]
    public async Task SettingsChanged_InvalidInterval_IsCorrectedToAtLeastOneHour()
    {
        _view.Settings = new UpdateSettings { CheckIntervalHours = 0 };

        _view.RaiseSettingsChanged();
        await _view.NextMessageAsync();

        Assert.Equal(1, _settings.Settings.CheckIntervalHours);
    }

    [Fact]
    public async Task CheckButton_Clicked_WiresToCheckFlow()
    {
        _service.CheckException = new ManagerException("falhou");

        _view.RaiseCheck();
        var message = await _view.NextMessageAsync();

        Assert.Equal("falhou", message);
    }
}
