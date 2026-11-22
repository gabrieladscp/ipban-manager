using System;
using System.Linq;
using IPBanManager.Core.Models;
using IPBanManager.Core.Validation;
using IPBanManager.Core.Views;

namespace IPBanManager.Wpf.Views;

public partial class ActionsView : ConfigSectionViewBase, IActionsView
{
    public ActionsView()
    {
        InitializeComponent();

        RulePlatform.ItemsSource = FirewallRuleEntry.Platforms.Select(p => Tuple.Create(p.Regex, p.Label)).ToList();
        RulePlatform.SelectedIndex = 0;
        FirewallRules.AttachForm(FirewallForm);
        UriRules.AttachForm(UriForm);
        FirewallRules.Describe = FirewallRuleEntry.Describe;
        FirewallRules.BuildEntry = existing =>
        {
            var error = FirewallRuleEntry.Create(RuleNameBox.Text, RuleAction.SelectedIndex == 0, RuleIps.Text, RulePorts.Text,
                RulePlatform.SelectedValue as string, out var entry);
            if (error == null && existing.Any(l => l.Split(';')[0].Equals(entry.Name, StringComparison.OrdinalIgnoreCase)))
                error = $"Já existe uma regra chamada '{entry.Name}'.";
            return (entry?.Format(), error);
        };
        FirewallRules.ClearForm = () => { RuleNameBox.Clear(); RuleIps.Clear(); RulePorts.Clear(); };

        UriMax.Value = 10000;
        UriInterval.Value = "01:00:00:00";
        UriRules.BuildEntry = existing =>
        {
            var error = UriRuleEntry.Create(UriPrefix.Text, UriInterval.Value, UriUrl.Text, UriMax.Value, out var line);
            return (line, error);
        };
        UriRules.ClearForm = () => { UriPrefix.Clear(); UriUrl.Clear(); };
    }

    protected override void Populate(IPBanConfiguration c)
    {
        FirewallRules.Entries = Lines(c.FirewallRules);
        UriRules.Entries = Lines(c.FirewallUriRules);
        OnBan.Value = c.ProcessToRunOnBan;
        OnUnban.Value = c.ProcessToRunOnUnban;
        OnSuccess.Value = c.ProcessToRunOnSuccessfulLogin;
    }

    protected override void Collect(IPBanConfiguration c)
    {
        c.FirewallRules = string.Join("\n", FirewallRules.Entries);
        c.FirewallUriRules = string.Join("\n", UriRules.Entries);
        c.ProcessToRunOnBan = OnBan.Value;
        c.ProcessToRunOnUnban = OnUnban.Value;
        c.ProcessToRunOnSuccessfulLogin = OnSuccess.Value;
    }
}
