using System;
using System.Threading.Tasks;
using IPBanManager.Core.Abstractions;
using IPBanManager.Core.Errors;
using IPBanManager.Core.Views;

namespace IPBanManager.Core.Presenters;

public interface IRefreshable
{
    /// <summary>Carga inicial: falhas viram aviso não modal em vez de interromper a abertura.</summary>
    Task InitializeAsync();
    Task RefreshAsync();
}

public abstract class PresenterBase<TView> : IRefreshable where TView : IView
{
    protected PresenterBase(TView view, IInstallationLocator locator, IAppLogger logger)
    {
        View = view;
        Locator = locator;
        Logger = logger ?? NullLogger.Instance;
        Locator.InstallDirectoryChanged += async (s, e) => await InitializeAsync();
    }

    protected TView View { get; }
    protected IInstallationLocator Locator { get; }
    protected IAppLogger Logger { get; }

    /// <summary>Recarrega os dados exibidos pela View.</summary>
    public abstract Task RefreshAsync();

    public async Task InitializeAsync()
    {
        try { await RefreshAsync(); }
        catch (Exception ex)
        {
            Logger.Error($"Falha ao atualizar {GetType().Name}", ex);
            View.ShowWarning(ErrorMessages.Translate(ex));
        }
    }

    /// <summary>Executa uma ação do usuário; falhas são registradas e exibidas na View.</summary>
    protected async Task GuardedAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex)
        {
            Logger.Error($"Falha em {GetType().Name}", ex);
            View.ShowError(ErrorMessages.Translate(ex));
        }
    }

    protected async void OnEvent(Func<Task> action) => await GuardedAsync(action);
}
