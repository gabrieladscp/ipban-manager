using System.Linq;
using IPBanManager.Core.Models;
using IPBanManager.Core.Views;

namespace IPBanManager.Wpf.Views;

public partial class ListsView : ConfigSectionViewBase, IListsView
{
    public ListsView() => InitializeComponent();

    protected override void Populate(IPBanConfiguration c)
    {
        Whitelist.Text = JoinLines(c.Whitelist);
        Blacklist.Text = JoinLines(c.Blacklist);
        UserWhitelist.Text = JoinLines(c.UserNameWhitelist);
        WhitelistRegex.Text = c.WhitelistRegex;
        BlacklistRegex.Text = c.BlacklistRegex;
        UserWhitelistRegex.Text = c.UserNameWhitelistRegex;
    }

    protected override void Collect(IPBanConfiguration c)
    {
        c.Whitelist = Lines(Whitelist.Text).ToList();
        c.Blacklist = Lines(Blacklist.Text).ToList();
        c.UserNameWhitelist = Lines(UserWhitelist.Text).ToList();
        c.WhitelistRegex = WhitelistRegex.Text.Trim();
        c.BlacklistRegex = BlacklistRegex.Text.Trim();
        c.UserNameWhitelistRegex = UserWhitelistRegex.Text.Trim();
    }
}
