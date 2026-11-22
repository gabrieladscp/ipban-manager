using System.Threading.Tasks;
using IPBanManager.Core.Presenters;
using IPBanManager.Core.Views;
using IPBanManager.Core.Validation;
using IPBanManager.Tests.Support;
using Xunit;

namespace IPBanManager.Tests.Presenters;

[Trait("Category", "Unit")]
public class ConfigSectionPresenterTests
{
    private readonly FakeConfigRepository _repository = new FakeConfigRepository();
    private readonly FakeSettingsView _view = new FakeSettingsView();
    private readonly FakeLocator _locator = new FakeLocator();
    private readonly RecordingLogger _logger = new RecordingLogger();
    private readonly ConfigSectionPresenter<ISettingsView> _presenter;

    public ConfigSectionPresenterTests()
    {
        _presenter = new ConfigSectionPresenter<ISettingsView>(_view, _locator, _repository, new ConfigurationValidator(), _logger);
    }

    [Fact]
    public async Task SaveAsync_InvalidValues_ShowsErrorsAndDoesNotPersist()
    {
        _view.AttemptsToApply = 0;

        await _presenter.SaveAsync();

        Assert.Equal(0, _repository.SaveCount);
        Assert.Single(_view.Errors);
        Assert.Contains("maior ou igual a 1", _view.Errors[0]);
        Assert.False(_view.SavedMarked);
    }

    [Fact]
    public async Task SaveAsync_ValidValues_PersistsMarksSavedAndNotifies()
    {
        _view.AttemptsToApply = 7;

        await _presenter.SaveAsync();

        Assert.Equal(1, _repository.SaveCount);
        Assert.Equal(7, _repository.Stored.FailedLoginAttemptsBeforeBan);
        Assert.True(_view.SavedMarked);
        Assert.Single(_view.Infos);
    }

    [Fact]
    public async Task SaveAsync_InstallationNotFound_ShowsErrorWithoutTouchingRepository()
    {
        _locator.IsValid = false;

        await _presenter.SaveAsync();

        Assert.Equal(0, _repository.SaveCount);
        Assert.Contains("pasta de instalação", _view.Errors[0]);
    }

    [Fact]
    public async Task RefreshAsync_ValidInstallation_DisplaysConfiguration()
    {
        await _presenter.RefreshAsync();

        Assert.NotNull(_view.Displayed);
    }

    [Fact]
    public async Task RefreshAsync_InstallationNotFound_DoesNothing()
    {
        _locator.IsValid = false;

        await _presenter.RefreshAsync();

        Assert.Null(_view.Displayed);
    }

    [Fact]
    public async Task InitializeAsync_RepositoryFails_ShowsWarningInsteadOfThrowingAndLogs()
    {
        _repository.LoadException = new System.IO.IOException("em uso");

        await _presenter.InitializeAsync();

        Assert.Single(_view.Warnings);
        Assert.Empty(_view.Errors);
        Assert.Contains(_logger.Entries, e => e.StartsWith("ERROR"));
    }

    [Fact]
    public async Task SaveButton_RepositoryThrows_ShowsTranslatedErrorToUser()
    {
        _repository.LoadException = new System.UnauthorizedAccessException();

        _view.RaiseSave();
        var message = await _view.NextMessageAsync();

        Assert.Contains("Administrador", message);
        Assert.Single(_view.Errors);
    }

    [Fact]
    public async Task InstallDirectoryChanged_ReloadsTheView()
    {
        _locator.SetInstallDirectory("C:\\outra");

        var displayed = await _view.WaitForDisplayAsync();

        Assert.NotNull(displayed);
    }
}
