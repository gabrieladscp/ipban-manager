using System.Threading.Tasks;
using IPBanManager.Core.Errors;
using IPBanManager.Core.Presenters;
using IPBanManager.Tests.Support;
using Xunit;

namespace IPBanManager.Tests.Presenters;

[Trait("Category", "Unit")]
public class AdvancedPresenterTests
{
    private readonly FakeConfigRepository _repository = new FakeConfigRepository { RawXml = "<configuration><a/></configuration>" };
    private readonly FakeAdvancedView _view = new FakeAdvancedView();
    private readonly AdvancedPresenter _presenter;

    public AdvancedPresenterTests()
    {
        _presenter = new AdvancedPresenter(_view, new FakeLocator(), _repository, new RecordingLogger());
    }

    [Fact]
    public async Task RefreshAsync_LoadsRawXmlIntoView()
    {
        await _presenter.RefreshAsync();

        Assert.Equal(_repository.RawXml, _view.Xml);
    }

    [Fact]
    public async Task SaveAsync_MalformedXml_ShowsErrorAndDoesNotSave()
    {
        await _presenter.RefreshAsync();
        _view.Xml = "<configuration>";

        await _presenter.SaveAsync();

        Assert.Contains("XML inválido", _view.Errors[0]);
        Assert.Equal(0, _repository.RawSaveCount);
    }

    [Fact]
    public async Task SaveAsync_FileChangedSinceLoadAndUserDeclines_DoesNotOverwrite()
    {
        await _presenter.RefreshAsync();
        _repository.RawXml = "<configuration><changed-by-someone-else/></configuration>";
        _view.Xml = "<configuration><mine/></configuration>";
        _view.ConfirmAnswer = false;

        await _presenter.SaveAsync();

        Assert.Equal(0, _repository.RawSaveCount);
        Assert.Contains("alterado depois", _view.Questions[0]);
    }

    [Fact]
    public async Task SaveAsync_FileUnchangedAndConfirmed_Saves()
    {
        await _presenter.RefreshAsync();
        _view.Xml = "<configuration><mine/></configuration>";

        await _presenter.SaveAsync();

        Assert.Equal(1, _repository.RawSaveCount);
        Assert.Equal("<configuration><mine/></configuration>", _repository.RawXml);
    }

    [Fact]
    public async Task SaveAsync_SecondSaveAfterFirst_DoesNotReportFalseConflict()
    {
        await _presenter.RefreshAsync();
        _view.Xml = "<configuration><v1/></configuration>";
        await _presenter.SaveAsync();
        _view.Xml = "<configuration><v2/></configuration>";

        await _presenter.SaveAsync();

        Assert.Equal(2, _repository.RawSaveCount);
        Assert.DoesNotContain(_view.Questions, q => q.Contains("alterado depois"));
    }
}

[Trait("Category", "Unit")]
public class LogPresenterTests
{
    [Fact]
    public async Task RefreshAsync_AppliesViewFilterToLogText()
    {
        var reader = new FakeLogReader { Text = "INFO a\r\nWARN b\r\nINFO c" };
        var view = new FakeLogView { Filter = "warn" };
        var presenter = new LogPresenter(view, new FakeLocator(), reader, new RecordingLogger());

        await presenter.RefreshAsync();

        Assert.Equal("WARN b", view.LogText);
    }

    [Fact]
    public async Task RefreshAsync_InstallationInvalid_DoesNotRead()
    {
        var view = new FakeLogView();
        var presenter = new LogPresenter(view, new FakeLocator(isValid: false), new FakeLogReader { Text = "x" }, new RecordingLogger());

        await presenter.RefreshAsync();

        Assert.Null(view.LogText);
    }
}

[Trait("Category", "Unit")]
public class MainPresenterTests
{
    [Fact]
    public async Task RefreshAsync_InvalidInstallation_ShowsBannerGuidance()
    {
        var view = new FakeMainView();
        var presenter = new MainPresenter(view, new FakeLocator(isValid: false), new RecordingLogger());

        await presenter.RefreshAsync();

        Assert.False(view.InstallValid);
        Assert.Contains("ipban.config", view.InstallStatus);
    }

    [Fact]
    public async Task RefreshAsync_ValidInstallation_ShowsDirectory()
    {
        var view = new FakeMainView();
        var presenter = new MainPresenter(view, new FakeLocator("C:\\IPBan"), new RecordingLogger());

        await presenter.RefreshAsync();

        Assert.True(view.InstallValid);
        Assert.Equal("C:\\IPBan", view.InstallDirectory);
    }

    [Fact]
    public void ChangeDirectory_LocatorRejectsFolder_ShowsItsMessage()
    {
        var view = new FakeMainView { PickedDirectory = "C:\\vazia" };
        var locator = new FakeLocator { SetException = new ManagerException("não contém ipban.config") };
        var presenter = new MainPresenter(view, locator, new RecordingLogger());

        presenter.ChangeDirectory();

        Assert.Equal(new[] { "não contém ipban.config" }, view.Errors);
    }

    [Fact]
    public void ChangeDirectory_UserCancelsDialog_DoesNothing()
    {
        var view = new FakeMainView { PickedDirectory = null };
        var locator = new FakeLocator { SetException = new ManagerException("não deveria ser chamado") };
        var presenter = new MainPresenter(view, locator, new RecordingLogger());

        presenter.ChangeDirectory();

        Assert.Empty(view.Errors);
    }
}
