using System;
using System.Threading.Tasks;
using IPBanManager.Core.Abstractions;
using IPBanManager.Core.Models;
using IPBanManager.Core.Views;

namespace IPBanManager.Core.Presenters;

public class DashboardPresenter : PresenterBase<IDashboardView>
{
    /// <summary>A contagem de IPs (cara: lê o firewall) só é recalculada a cada N verificações leves.</summary>
    public const int PollsPerBannedCountRefresh = 12;

    private readonly IServiceController _service;
    private readonly IBannedIpProvider _banned;
    private readonly IConfigurationRepository _config;
    private bool _busy;
    private bool _refreshing;
    private int _pollsSinceCount;

    public DashboardPresenter(IDashboardView view, IInstallationLocator locator, IServiceController service,
        IBannedIpProvider banned, IConfigurationRepository config, IAppLogger logger)
        : base(view, locator, logger)
    {
        _service = service;
        _banned = banned;
        _config = config;

        View.StartRequested += (s, e) => OnEvent(() => ControlAsync(_service.Start));
        View.StopRequested += (s, e) => OnEvent(ConfirmedStopAsync);
        View.RestartRequested += (s, e) => OnEvent(() => ControlAsync(_service.Restart));
        View.RefreshRequested += (s, e) => OnEvent(RefreshAsync);
        View.PollRequested += (s, e) => OnEvent(PollAsync);
    }

    public override Task RefreshAsync() => RefreshCoreAsync(includeBannedCount: true);

    /// <summary>Verificação periódica: só consulta o serviço; o firewall é lido raramente.</summary>
    public async Task PollAsync()
    {
        _pollsSinceCount++;
        bool includeCount = _pollsSinceCount >= PollsPerBannedCountRefresh;
        await RefreshCoreAsync(includeCount);
    }

    private async Task RefreshCoreAsync(bool includeBannedCount)
    {
        if (_busy || _refreshing) return; // evita sobreposição e piscar de botões durante start/stop
        _refreshing = true;
        try
        {
            var state = await Task.Run(() => _service.GetState());
            View.ShowState(state, Describe(state));
            if (includeBannedCount) await RefreshBannedCountAsync();
        }
        finally { _refreshing = false; }
    }

    private async Task RefreshBannedCountAsync()
    {
        _pollsSinceCount = 0;
        if (!Locator.IsValid) { View.ShowBannedCount(0); return; }
        try
        {
            var prefix = await Task.Run(() => _config.Load().FirewallRulePrefix);
            var list = await Task.Run(() => _banned.GetBannedIps(prefix));
            View.ShowBannedCount(list.Count);
        }
        catch (Exception ex)
        {
            Logger.Warn("Contagem de IPs bloqueados indisponível: " + ex.Message);
            View.ShowBannedCount(0);
        }
    }

    private async Task ConfirmedStopAsync()
    {
        if (!View.Confirm("Parar o serviço IPBan? Enquanto estiver parado, novos ataques NÃO serão bloqueados.")) return;
        await ControlAsync(_service.Stop);
    }

    private async Task ControlAsync(Action operation)
    {
        if (_busy) return;
        _busy = true;
        View.Busy = true;
        try
        {
            await Task.Run(operation);
        }
        finally
        {
            _busy = false;
            View.Busy = false;
        }
        await RefreshCoreAsync(includeBannedCount: true);
    }

    private static string Describe(ServiceState state)
    {
        switch (state)
        {
            case ServiceState.Running: return "Em execução";
            case ServiceState.Stopped: return "Parado";
            case ServiceState.Transitioning: return "Alterando estado...";
            default: return "Serviço IPBan não instalado neste servidor";
        }
    }
}
