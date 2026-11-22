using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;
using IPBanManager.Core.Models;
using IPBanManager.Core.Views;

namespace IPBanManager.Wpf.Views;

public partial class ListsView : ConfigSectionViewBase, IListsView
{
    public ListsView()
    {
        InitializeComponent();
        // Abrir/fechar o Expander não é alteração de configuração.
        var swallow = new RoutedEventHandler((s, e) => e.Handled = true);
        RegexExpander.AddHandler(ToggleButton.CheckedEvent, swallow);
        RegexExpander.AddHandler(ToggleButton.UncheckedEvent, swallow);
    }

    protected override void Populate(IPBanConfiguration c)
    {
        Whitelist.Entries = c.Whitelist;
        Blacklist.Entries = c.Blacklist;
        UserWhitelist.Entries = c.UserNameWhitelist;
        WhitelistRegex.Text = c.WhitelistRegex;
        BlacklistRegex.Text = c.BlacklistRegex;
        UserWhitelistRegex.Text = c.UserNameWhitelistRegex;
    }

    protected override void Collect(IPBanConfiguration c)
    {
        // O que foi digitado e não adicionado entra na lista se for válido (não se perde ao salvar).
        Whitelist.CommitPending();
        Blacklist.CommitPending();
        UserWhitelist.CommitPending();

        c.Whitelist = Whitelist.Entries.ToList();
        c.Blacklist = Blacklist.Entries.ToList();
        c.UserNameWhitelist = UserWhitelist.Entries.ToList();
        c.WhitelistRegex = WhitelistRegex.Text.Trim();
        c.BlacklistRegex = BlacklistRegex.Text.Trim();
        c.UserNameWhitelistRegex = UserWhitelistRegex.Text.Trim();
    }
}
