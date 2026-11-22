using System;
using System.Threading.Tasks;
using IPBanManager.Core.Abstractions;
using IPBanManager.Core.Errors;
using IPBanManager.Core.Provisioning;
using IPBanManager.Core.Views;

namespace IPBanManager.Core.Presenters;

public class MainPresenter : PresenterBase<IMainView>
{
    private readonly IIPBanSetupLauncher _setup;

    public MainPresenter(IMainView view, IInstallationLocator locator, IAppLogger logger, IIPBanSetupLauncher setup = null)
        : base(view, locator, logger)
    {
        _setup = setup;
        View.ChangeDirectoryRequested += (s, e) => ChangeDirectory();
        View.InstallIPBanRequested += (s, e) => RunSetup();
    }

    public override Task RefreshAsync()
    {
        View.InstallDirectory = Locator.InstallDirectory ?? "(não encontrado)";
        View.InstallValid = Locator.IsValid;

        bool canInstall = !Locator.IsValid && SafeInspect()?.CanInstall == true;
        View.CanInstallIPBan = canInstall;
        View.InstallStatus = Locator.IsValid
            ? "Instalação do IPBan detectada."
            : canInstall
                ? "O IPBan não está instalado neste servidor. Clique em \"Instalar IPBan\" para instalá-lo e configurá-lo " +
                  "automaticamente, ou em \"Alterar pasta...\" se ele estiver instalado em outro local."
                : "IPBan não encontrado: a pasta precisa conter o arquivo ipban.config. Clique em \"Alterar pasta...\" e selecione a pasta de instalação.";
        return Task.CompletedTask;
    }

    /// <summary>Na abertura: se o IPBan não existe e pode ser instalado, abre o assistente direto.</summary>
    public void OfferSetupIfMissing()
    {
        if (Locator.IsValid || SafeInspect()?.CanInstall != true) return;
        RunSetup();
    }

    public void ChangeDirectory()
    {
        var chosen = View.PickDirectory(Locator.InstallDirectory);
        if (string.IsNullOrWhiteSpace(chosen)) return;
        try
        {
            Locator.SetInstallDirectory(chosen);
        }
        catch (ManagerException ex)
        {
            View.ShowError(ex.Message);
        }
    }

    private void RunSetup()
    {
        if (_setup == null) return;
        try
        {
            if (_setup.ShowSetup()) _ = RefreshAsync(); // síncrono; a troca de pasta também dispara atualização
        }
        catch (Exception ex)
        {
            Logger.Error("Falha no assistente de instalação do IPBan", ex);
            View.ShowError(ErrorMessages.Translate(ex));
        }
    }

    private IPBanSetupStatus SafeInspect()
    {
        if (_setup == null) return null;
        try { return _setup.Inspect(); }
        catch (Exception ex)
        {
            Logger.Warn("Não foi possível verificar a instalação do IPBan: " + ex.Message);
            return null;
        }
    }
}
