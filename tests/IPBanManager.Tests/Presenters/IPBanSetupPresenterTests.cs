using System;
using System.Threading.Tasks;
using IPBanManager.Core.Errors;
using IPBanManager.Core.Presenters;
using IPBanManager.Core.Provisioning;
using IPBanManager.Tests.Support;
using Xunit;

namespace IPBanManager.Tests.Presenters;

[Trait("Category", "Unit")]
public class IPBanSetupPresenterTests
{
    private readonly FakeSetupView _view = new FakeSetupView();
    private readonly FakeProvisioner _provisioner = new FakeProvisioner();
    private readonly FakeAddresses _addresses = new FakeAddresses();
    private readonly FakeLocator _locator = new FakeLocator(null, isValid: false);
    private readonly IPBanSetupPresenter _presenter;

    public IPBanSetupPresenterTests()
    {
        _presenter = new IPBanSetupPresenter(_view, _provisioner, _addresses, _locator, new RecordingLogger());
    }

    [Fact]
    public void Initialize_NotInstalled_ShowsInstallPlanAndPrefillsRemoteAddresses()
    {
        _addresses.Addresses.AddRange(new[] { "203.0.113.10", "198.51.100.7" });

        _presenter.Initialize();

        Assert.Equal("Instalar", _view.ActionLabel);
        Assert.Contains("4.1.0", _view.Heading);
        Assert.Equal("203.0.113.10" + Environment.NewLine + "198.51.100.7", _view.WhitelistText);
    }

    [Fact]
    public void Initialize_AlreadyInstalled_ShowsUpgradePlan()
    {
        _provisioner.Status.ServiceInstalled = true;
        _provisioner.Status.InstalledVersion = new Version(1, 0, 0, 0);

        _presenter.Initialize();

        Assert.Equal("Atualizar", _view.ActionLabel);
        Assert.Contains("Atualizar o IPBan para a versão 4.1.0", _view.Heading);
    }

    [Fact]
    public void Initialize_AddressDetectionFails_LeavesWhitelistEmptyWithoutError()
    {
        _addresses.Exception = new InvalidOperationException("sem acesso");

        _presenter.Initialize();

        Assert.Equal("", _view.WhitelistText);
        Assert.Empty(_view.Errors);
    }

    [Fact]
    public async Task InstallAsync_ValidWhitelist_InstallsPointsLocatorAndFinishes()
    {
        _view.WhitelistText = "203.0.113.10\r\n198.51.100.0/24, 203.0.113.10";

        await _presenter.InstallAsync();

        Assert.Equal(new[] { "203.0.113.10", "198.51.100.0/24" }, _provisioner.LastOptions.Whitelist);
        Assert.Equal(@"C:\Program Files\IPBan", _locator.InstallDirectory);
        Assert.True(_presenter.Succeeded);
        Assert.True(_view.Finished);
        Assert.False(_view.Busy);
    }

    [Fact]
    public async Task InstallAsync_InvalidEntry_ShowsErrorAndDoesNotInstall()
    {
        _view.WhitelistText = "203.0.113.10\r\nnão é ip|x";

        await _presenter.InstallAsync();

        Assert.Equal(0, _provisioner.InstallCalls);
        Assert.Contains("Entradas inválidas", _view.Errors[0]);
    }

    [Fact]
    public async Task InstallAsync_EmptyWhitelistOnFreshInstallAndUserDeclines_DoesNotInstall()
    {
        _view.WhitelistText = "";
        _view.ConfirmAnswer = false;

        await _presenter.InstallAsync();

        Assert.Equal(0, _provisioner.InstallCalls);
        Assert.Contains("whitelist está vazia", _view.Questions[0]);
    }

    [Fact]
    public async Task InstallAsync_WideRangeAndUserDeclines_DoesNotInstall()
    {
        _view.WhitelistText = "10.0.0.0/8";
        _view.ConfirmAnswer = false;

        await _presenter.InstallAsync();

        Assert.Equal(0, _provisioner.InstallCalls);
        Assert.Contains("10.0.0.0/8", _view.Questions[0]);
    }

    [Fact]
    public async Task InstallAsync_ProvisionerFails_ShowsTranslatedErrorAndAllowsRetry()
    {
        _view.WhitelistText = "203.0.113.10";
        _provisioner.InstallException = new ManagerException("Falha ao configurar o serviço.");

        await _presenter.InstallAsync();

        Assert.False(_presenter.Succeeded);
        Assert.False(_view.Finished);
        Assert.Contains("Falha ao configurar o serviço.", _view.Errors[0]);
        Assert.False(_view.Busy);

        _provisioner.InstallException = null;
        await _presenter.InstallAsync();
        Assert.True(_presenter.Succeeded);
    }

    [Fact]
    public async Task InstallAsync_ResultWithWarnings_ShowsThemInProgress()
    {
        _view.WhitelistText = "203.0.113.10";
        _provisioner.ResultWarnings.Add("Firewall desativado");

        await _presenter.InstallAsync();

        Assert.Contains(_view.Progress, l => l.Contains("Firewall desativado"));
    }

    [Fact]
    public async Task InstallAsync_LocatorAlreadyPointsToDirectory_DoesNotResetIt()
    {
        var locator = new FakeLocator(@"C:\Program Files\IPBan", isValid: true);
        int changes = 0;
        locator.InstallDirectoryChanged += (s, e) => changes++;
        var presenter = new IPBanSetupPresenter(_view, _provisioner, _addresses, locator, new RecordingLogger());
        _view.WhitelistText = "203.0.113.10";

        await presenter.InstallAsync();

        Assert.Equal(0, changes);
    }
}

[Trait("Category", "Unit")]
public class MainPresenterSetupTests
{
    [Fact]
    public async Task RefreshAsync_IPBanMissingAndBundleAvailable_ShowsInstallButton()
    {
        var view = new FakeMainView();
        var launcher = new FakeSetupLauncher { Status = new IPBanSetupStatus { BundleAvailable = true } };
        var presenter = new MainPresenter(view, new FakeLocator(null, isValid: false), new RecordingLogger(), launcher);

        await presenter.RefreshAsync();

        Assert.True(view.CanInstallIPBan);
        Assert.Contains("Instalar IPBan", view.InstallStatus);
    }

    [Fact]
    public async Task RefreshAsync_IPBanPresent_HidesInstallButton()
    {
        var view = new FakeMainView();
        var launcher = new FakeSetupLauncher { Status = new IPBanSetupStatus { BundleAvailable = true, ServiceInstalled = true } };
        var presenter = new MainPresenter(view, new FakeLocator(), new RecordingLogger(), launcher);

        await presenter.RefreshAsync();

        Assert.False(view.CanInstallIPBan);
    }

    [Fact]
    public void OfferSetupIfMissing_OpensWizardOnlyWhenIPBanIsMissing()
    {
        var launcher = new FakeSetupLauncher { Status = new IPBanSetupStatus { BundleAvailable = true } };
        new MainPresenter(new FakeMainView(), new FakeLocator(null, isValid: false), new RecordingLogger(), launcher)
            .OfferSetupIfMissing();
        Assert.Equal(1, launcher.ShowCalls);

        var installed = new FakeSetupLauncher { Status = new IPBanSetupStatus { BundleAvailable = true, ServiceInstalled = true } };
        new MainPresenter(new FakeMainView(), new FakeLocator(), new RecordingLogger(), installed).OfferSetupIfMissing();
        Assert.Equal(0, installed.ShowCalls);
    }

    [Fact]
    public void InstallButton_LauncherThrows_ShowsError()
    {
        var view = new FakeMainView();
        var launcher = new FakeSetupLauncher { OnShow = () => throw new InvalidOperationException("boom") };
        new MainPresenter(view, new FakeLocator(null, isValid: false), new RecordingLogger(), launcher);

        view.RaiseInstallIPBan();

        Assert.Single(view.Errors);
    }
}
