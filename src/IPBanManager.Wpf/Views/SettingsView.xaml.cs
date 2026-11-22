using System;
using System.Linq;
using IPBanManager.Core.Models;
using IPBanManager.Core.Validation;
using IPBanManager.Core.Views;

namespace IPBanManager.Wpf.Views;

public partial class SettingsView : ConfigSectionViewBase, ISettingsView
{
    public SettingsView()
    {
        InitializeComponent();
        BanTimeInput.Value = "01:00:00:00";
        BanTime.AttachForm(BanTimeForm);
        BanTime.Describe = line => $"{line}   ({BanTimeRules.Describe(line)})";
        BanTime.Sort = BanTimeRules.Sort;
        BanTime.BuildEntry = existing =>
        {
            var value = BanTimeInput.Value;
            return (value, BanTimeRules.ValidateNew(value, existing));
        };
    }

    protected override void Populate(IPBanConfiguration c)
    {
        FailedAttempts.Value = c.FailedLoginAttemptsBeforeBan;
        BanTime.Entries = (c.BanTime ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(v => v.Trim()).Where(v => v.Length > 0).ToList();
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
        EditDistance.Value = c.UserNameWhitelistMinimumEditDistance;
        UserAttempts.Value = c.FailedLoginAttemptsBeforeBanUserNameWhitelist;
    }

    protected override void Collect(IPBanConfiguration c)
    {
        c.FailedLoginAttemptsBeforeBan = FailedAttempts.Value;
        c.BanTime = string.Join(",", BanTime.Entries);
        c.ExpireTime = ExpireTime.Value;
        c.CycleTime = CycleTime.Value;
        c.MinimumTimeBetweenFailedLoginAttempts = MinFailed.Value;
        c.MinimumTimeBetweenSuccessfulLoginAttempts = MinSuccess.Value;
        c.ResetFailedLoginCountForUnbannedIPAddresses = ResetUnbanned.IsChecked == true;
        c.ClearBannedIPAddressesOnRestart = ClearOnRestart.IsChecked == true;
        c.ClearFailedLoginsOnSuccessfulLogin = ClearOnSuccess.IsChecked == true;
        c.ProcessInternalIPAddresses = InternalIps.IsChecked == true;
        c.UseDefaultBannedIPAddressHandler = DefaultHandler.IsChecked == true;
        c.FirewallRulePrefix = Prefix.Text.Trim();
        c.ExternalIPAddressUrl = ExternalUrl.Text.Trim();
        c.IPThreatApiKey = ApiKey.Text.Trim();
        c.TruncateUserNameChars = Truncate.Text;
        c.UserNameWhitelistMinimumEditDistance = EditDistance.Value;
        c.FailedLoginAttemptsBeforeBanUserNameWhitelist = UserAttempts.Value;
    }
}
