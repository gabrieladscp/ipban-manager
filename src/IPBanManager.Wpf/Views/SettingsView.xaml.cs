using IPBanManager.Core.Models;
using IPBanManager.Core.Views;

namespace IPBanManager.Wpf.Views;

public partial class SettingsView : ConfigSectionViewBase, ISettingsView
{
    public SettingsView() => InitializeComponent();

    protected override void Populate(IPBanConfiguration c)
    {
        FailedAttempts.Text = c.FailedLoginAttemptsBeforeBan.ToString();
        BanTime.Text = c.BanTime;
        ExpireTime.Value = c.ExpireTime;
        CycleTime.Value = c.CycleTime;
        MinFailed.Value = c.MinimumTimeBetweenFailedLoginAttempts;
        MinSuccess.Value = c.MinimumTimeBetweenSuccessfulLoginAttempts;
        ResetUnbanned.IsChecked = c.ResetFailedLoginCountForUnbannedIPAddresses;
        ClearOnRestart.IsChecked = c.ClearBannedIPAddressesOnRestart;
        ClearOnSuccess.IsChecked = c.ClearFailedLoginsOnSuccessfulLogin;
        InternalIps.IsChecked = c.ProcessInternalIPAddresses;
        DefaultHandler.IsChecked = c.UseDefaultBannedIPAddressHandler;
        Prefix.Text = c.FirewallRulePrefix;
        ExternalUrl.Text = c.ExternalIPAddressUrl;
        ApiKey.Text = c.IPThreatApiKey;
        Truncate.Text = c.TruncateUserNameChars;
        EditDistance.Text = c.UserNameWhitelistMinimumEditDistance.ToString();
        UserAttempts.Text = c.FailedLoginAttemptsBeforeBanUserNameWhitelist.ToString();
    }

    protected override void Collect(IPBanConfiguration c)
    {
        c.FailedLoginAttemptsBeforeBan = ParseInt(FailedAttempts.Text);
        c.BanTime = BanTime.Text;
        c.ExpireTime = ExpireTime.Value;
        c.CycleTime = CycleTime.Value;
        c.MinimumTimeBetweenFailedLoginAttempts = MinFailed.Value;
        c.MinimumTimeBetweenSuccessfulLoginAttempts = MinSuccess.Value;
        c.ResetFailedLoginCountForUnbannedIPAddresses = ResetUnbanned.IsChecked == true;
        c.ClearBannedIPAddressesOnRestart = ClearOnRestart.IsChecked == true;
        c.ClearFailedLoginsOnSuccessfulLogin = ClearOnSuccess.IsChecked == true;
        c.ProcessInternalIPAddresses = InternalIps.IsChecked == true;
        c.UseDefaultBannedIPAddressHandler = DefaultHandler.IsChecked == true;
        c.FirewallRulePrefix = Prefix.Text;
        c.ExternalIPAddressUrl = ExternalUrl.Text;
        c.IPThreatApiKey = ApiKey.Text;
        c.TruncateUserNameChars = Truncate.Text;
        c.UserNameWhitelistMinimumEditDistance = ParseInt(EditDistance.Text);
        c.FailedLoginAttemptsBeforeBanUserNameWhitelist = ParseInt(UserAttempts.Text);
    }
}
