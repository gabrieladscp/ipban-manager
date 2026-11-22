using System.Threading.Tasks;
using IPBanManager.Core.Abstractions;
using IPBanManager.Core.Errors;
using IPBanManager.Core.Views;

namespace IPBanManager.Core.Presenters;

public class MainPresenter : PresenterBase<IMainView>
{
    public MainPresenter(IMainView view, IInstallationLocator locator, IAppLogger logger) : base(view, locator, logger)
    {
        View.ChangeDirectoryRequested += (s, e) => ChangeDirectory();
    }

    public override Task RefreshAsync()
    {
        View.InstallDirectory = Locator.InstallDirectory ?? "(não encontrado)";
        View.InstallValid = Locator.IsValid;
        View.InstallStatus = Locator.IsValid
            ? "Instalação do IPBan detectada."
            : "IPBan não encontrado: a pasta precisa conter o arquivo ipban.config. Clique em \"Alterar pasta...\" e selecione a pasta de instalação.";
        return Task.CompletedTask;
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
}
