using System;
using System.Windows;
using IPBanManager.Core.Abstractions;
using IPBanManager.Core.Presenters;
using IPBanManager.Core.Provisioning;
using IPBanManager.Wpf.Views;

namespace IPBanManager.Wpf;

/// <summary>Abre o assistente de instalação do IPBan como diálogo modal da janela principal.</summary>
public sealed class IPBanSetupLauncher : IIPBanSetupLauncher
{
    private readonly IIPBanProvisioner _provisioner;
    private readonly IRemoteAccessAddressProvider _addresses;
    private readonly IInstallationLocator _locator;
    private readonly IAppLogger _logger;
    private bool _open;

    public IPBanSetupLauncher(IIPBanProvisioner provisioner, IRemoteAccessAddressProvider addresses,
        IInstallationLocator locator, IAppLogger logger)
    {
        _provisioner = provisioner;
        _addresses = addresses;
        _locator = locator;
        _logger = logger;
    }

    public IPBanSetupStatus Inspect() => _provisioner.Inspect();

    public bool ShowSetup()
    {
        if (_open) return false;
        _open = true;
        try
        {
            var window = new IPBanSetupWindow();
            var owner = Application.Current?.MainWindow;
            if (owner != null && owner.IsVisible && !ReferenceEquals(owner, window)) window.Owner = owner;
            else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var presenter = new IPBanSetupPresenter(window, _provisioner, _addresses, _locator, _logger);
            presenter.Initialize();
            window.ShowDialog();
            return presenter.Succeeded;
        }
        finally { _open = false; }
    }
}
