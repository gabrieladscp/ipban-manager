using System;
using System.Linq;
using System.Threading.Tasks;
using IPBanManager.Core.Abstractions;
using IPBanManager.Core.Validation;
using IPBanManager.Core.Views;

namespace IPBanManager.Core.Presenters;

public class BannedIpsPresenter : PresenterBase<IBannedIpsView>
{
    private readonly IBannedIpProvider _provider;
    private readonly IConfigurationRepository _config;
    private readonly IUnbanCommand _unban;

    public BannedIpsPresenter(IBannedIpsView view, IInstallationLocator locator, IBannedIpProvider provider,
        IConfigurationRepository config, IUnbanCommand unban, IAppLogger logger) : base(view, locator, logger)
    {
        _provider = provider;
        _config = config;
        _unban = unban;
        View.RefreshRequested += (s, e) => OnEvent(RefreshAsync);
        View.BanRequested += (s, e) => OnEvent(BanAsync);
        View.UnbanRequested += (s, e) => OnEvent(UnbanAsync);
    }

    public override async Task RefreshAsync()
    {
        if (!Locator.IsValid) return;
        var prefix = await Task.Run(() => _config.Load().FirewallRulePrefix);
        var items = await Task.Run(() => _provider.GetBannedIps(prefix));
        View.ShowBannedIps(items);
    }

    /// <summary>Banimento manual = inclusão na blacklist (o IPBan aplica no próximo ciclo).</summary>
    public async Task BanAsync()
    {
        var address = (View.ManualBanAddress ?? "").Trim();
        if (!IpEntry.IsIpOrCidr(address))
        {
            View.ShowError("Informe um IP (v4 ou v6) ou uma faixa CIDR válidos. Exemplos: 203.0.113.7 ou 198.51.100.0/24.");
            return;
        }
        if (IpEntry.IsLoopbackOrUnspecified(address))
        {
            View.ShowError("Endereços de loopback/não especificados não podem ser bloqueados.");
            return;
        }
        if (IpEntry.IsWideRange(address) &&
            !View.Confirm($"A faixa {address} é muito ampla e pode bloquear milhares de máquinas, inclusive você. Continuar?"))
            return;

        var cfg = await Task.Run(() => _config.Load());
        bool whitelisted = cfg.Whitelist.Contains(address, StringComparer.OrdinalIgnoreCase);
        if (whitelisted &&
            !View.Confirm($"{address} está na whitelist. Ele será removido da whitelist e bloqueado. Continuar?"))
            return;

        if (!cfg.Blacklist.Contains(address, StringComparer.OrdinalIgnoreCase))
            cfg.Blacklist.Add(address);
        cfg.Whitelist.RemoveAll(w => string.Equals(w, address, StringComparison.OrdinalIgnoreCase));
        await Task.Run(() => _config.Save(cfg));

        Logger.Info("IP adicionado à blacklist: " + address);
        View.ManualBanAddress = "";
        View.ShowInfo($"{address} adicionado à blacklist. Será bloqueado no próximo ciclo do serviço (cerca de 15 s).");
    }

    public async Task UnbanAsync()
    {
        var selected = View.SelectedAddresses;
        if (selected.Count == 0)
        {
            View.ShowInfo("Selecione ao menos um IP na lista.");
            return;
        }
        if (!View.Confirm($"Desbloquear {selected.Count} endereço(s)?")) return;

        await Task.Run(() =>
        {
            // Se o IP estiver na blacklist, seria bloqueado de novo: remove de lá também.
            var cfg = _config.Load();
            if (cfg.Blacklist.RemoveAll(b => selected.Contains(b, StringComparer.OrdinalIgnoreCase)) > 0)
                _config.Save(cfg);
            _unban.Unban(selected);
        });

        Logger.Info("Desbloqueio solicitado: " + string.Join(", ", selected));
        View.ShowInfo("Desbloqueio solicitado. O serviço processa no próximo ciclo (cerca de 15 s); use Atualizar para conferir.");
    }
}
