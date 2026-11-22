using IPBanManager.Core.Models;
using IPBanManager.Core.Views;

namespace IPBanManager.Wpf.Views;

public partial class ActionsView : ConfigSectionViewBase, IActionsView
{
    public ActionsView() => InitializeComponent();

    protected override void Populate(IPBanConfiguration c)
    {
        FirewallRules.Text = c.FirewallRules;
        UriRules.Text = c.FirewallUriRules;
        OnBan.Text = c.ProcessToRunOnBan;
        OnUnban.Text = c.ProcessToRunOnUnban;
        OnSuccess.Text = c.ProcessToRunOnSuccessfulLogin;
    }

    protected override void Collect(IPBanConfiguration c)
    {
        c.FirewallRules = FirewallRules.Text.Trim();
        c.FirewallUriRules = UriRules.Text.Trim();
        c.ProcessToRunOnBan = OnBan.Text.Trim();
        c.ProcessToRunOnUnban = OnUnban.Text.Trim();
        c.ProcessToRunOnSuccessfulLogin = OnSuccess.Text.Trim();
    }
}
