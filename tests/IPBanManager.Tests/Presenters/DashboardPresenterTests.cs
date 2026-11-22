using System.Threading.Tasks;
using IPBanManager.Core.Errors;
using IPBanManager.Core.Models;
using IPBanManager.Core.Presenters;
using IPBanManager.Tests.Support;
using Xunit;

namespace IPBanManager.Tests.Presenters;

[Trait("Category", "Unit")]
public class DashboardPresenterTests
{
    private readonly FakeServiceController _service = new FakeServiceController();
    private readonly FakeBannedProvider _provider = new FakeBannedProvider();
    private readonly FakeDashboardView _view = new FakeDashboardView();
    private readonly FakeLocator _locator = new FakeLocator();
    private readonly DashboardPresenter _presenter;

    public DashboardPresenterTests()
    {
        _presenter = new DashboardPresenter(_view, _locator, _service, _provider, new FakeConfigRepository(), new RecordingLogger());
    }

    [Theory]
    [InlineData(ServiceState.Running, "Em execução")]
    [InlineData(ServiceState.Stopped, "Parado")]
    [InlineData(ServiceState.NotInstalled, "não instalado")]
    public async Task RefreshAsync_ShowsServiceStateAndDescription(ServiceState state, string expectedText)
    {
        _service.State = state;

        await _presenter.RefreshAsync();

        Assert.Equal(state, _view.LastState);
        Assert.Contains(expectedText, _view.LastStateText);
    }

    [Fact]
    public async Task RefreshAsync_ShowsBannedCountFromFirewall()
    {
        await _presenter.RefreshAsync();

        Assert.Equal(1, _view.LastCount);
    }

    [Fact]
    public async Task RefreshAsync_InstallationInvalid_ShowsZeroAndSkipsFirewall()
    {
        _locator.IsValid = false;

        await _presenter.RefreshAsync();

        Assert.Equal(0, _view.LastCount);
        Assert.Equal(0, _provider.Calls);
    }

    [Fact]
    public async Task PollAsync_FewTimes_ChecksServiceButDoesNotReadFirewall()
    {
        for (int i = 0; i < DashboardPresenter.PollsPerBannedCountRefresh - 1; i++)
            await _presenter.PollAsync();

        Assert.Equal(DashboardPresenter.PollsPerBannedCountRefresh - 1, _service.GetStateCalls);
        Assert.Equal(0, _provider.Calls);
    }

    [Fact]
    public async Task PollAsync_AfterConfiguredNumberOfPolls_RefreshesBannedCountOnce()
    {
        for (int i = 0; i < DashboardPresenter.PollsPerBannedCountRefresh; i++)
            await _presenter.PollAsync();

        Assert.Equal(1, _provider.Calls);
    }

    [Fact]
    public async Task StopButton_NotConfirmed_DoesNotStopService()
    {
        _view.ConfirmAnswer = false;

        _view.RaiseStop(); // a confirmação é feita de forma síncrona, antes de qualquer await

        Assert.Equal(0, _service.StopCalls);
        Assert.Contains("NÃO serão bloqueados", _view.Questions[0]);
    }

    [Fact]
    public async Task StopButton_Confirmed_StopsServiceAndShowsStopped()
    {
        _view.RaiseStop();
        await _view.WaitForStateAsync(ServiceState.Stopped);

        Assert.Equal(1, _service.StopCalls);
    }

    [Fact]
    public async Task StartButton_ServiceFails_ShowsFriendlyErrorAndReenablesButtons()
    {
        _service.State = ServiceState.Stopped;
        _service.StartException = new ManagerException("Acesso negado ao iniciar o serviço.");

        _view.RaiseStart();
        var message = await _view.NextMessageAsync();

        Assert.Contains("Acesso negado", message);
        Assert.False(_view.Busy);
    }

    [Fact]
    public async Task StartButton_Success_RefreshesStateToRunning()
    {
        _service.State = ServiceState.Stopped;

        _view.RaiseStart();
        await _view.WaitForStateAsync(ServiceState.Running);

        Assert.Equal(1, _service.StartCalls);
    }

    [Fact]
    public async Task PollAsync_WhileAnotherRefreshIsStillRunning_IsIgnored()
    {
        _service.Gate = new System.Threading.ManualResetEventSlim(false);
        var slowRefresh = _presenter.RefreshAsync(); // fica preso em GetState até o gate abrir

        await _presenter.PollAsync();                // chega durante a consulta lenta: deve ser descartada
        _service.Gate.Set();
        await slowRefresh;

        Assert.Equal(1, _service.GetStateCalls);
    }
}
