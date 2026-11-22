using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IPBanManager.Core.Models;

namespace IPBanManager.Infrastructure;

/// <summary>Converte entre os pares chave/valor do ipban.config e o modelo tipado.</summary>
public static class ConfigurationMapper
{
    public static IPBanConfiguration FromSettings(IDictionary<string, string> s)
    {
        var d = new IPBanConfiguration();
        string Str(string key, string def) => s.TryGetValue(key, out var v) && v != null ? v : def;
        int Int(string key, int def) => int.TryParse(Str(key, null), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;
        bool Bool(string key, bool def) => bool.TryParse(Str(key, null), out var v) ? v : def;

        d.FailedLoginAttemptsBeforeBan = Int("FailedLoginAttemptsBeforeBan", d.FailedLoginAttemptsBeforeBan);
        d.BanTime = Str("BanTime", d.BanTime);
        d.ExpireTime = Str("ExpireTime", d.ExpireTime);
        d.CycleTime = Str("CycleTime", d.CycleTime);
        d.MinimumTimeBetweenFailedLoginAttempts = Str("MinimumTimeBetweenFailedLoginAttempts", d.MinimumTimeBetweenFailedLoginAttempts);
        d.MinimumTimeBetweenSuccessfulLoginAttempts = Str("MinimumTimeBetweenSuccessfulLoginAttempts", d.MinimumTimeBetweenSuccessfulLoginAttempts);
        d.ResetFailedLoginCountForUnbannedIPAddresses = Bool("ResetFailedLoginCountForUnbannedIPAddresses", false);
        d.ClearBannedIPAddressesOnRestart = Bool("ClearBannedIPAddressesOnRestart", false);
        d.ClearFailedLoginsOnSuccessfulLogin = Bool("ClearFailedLoginsOnSuccessfulLogin", false);
        d.ProcessInternalIPAddresses = Bool("ProcessInternalIPAddresses", false);
        d.UseDefaultBannedIPAddressHandler = Bool("UseDefaultBannedIPAddressHandler", true);
        d.FirewallRulePrefix = Str("FirewallRulePrefix", d.FirewallRulePrefix);
        d.ExternalIPAddressUrl = Str("ExternalIPAddressUrl", d.ExternalIPAddressUrl);
        d.IPThreatApiKey = Str("IPThreatApiKey", "");
        d.TruncateUserNameChars = Str("TruncateUserNameChars", d.TruncateUserNameChars);
        d.UserNameWhitelistMinimumEditDistance = Int("UserNameWhitelistMinimumEditDistance", d.UserNameWhitelistMinimumEditDistance);
        d.FailedLoginAttemptsBeforeBanUserNameWhitelist = Int("FailedLoginAttemptsBeforeBanUserNameWhitelist", d.FailedLoginAttemptsBeforeBanUserNameWhitelist);
        d.UserNameWhitelist = SplitList(Str("UserNameWhitelist", ""));
        d.UserNameWhitelistRegex = Str("UserNameWhitelistRegex", "");
        d.Whitelist = SplitList(Str("Whitelist", ""));
        d.WhitelistRegex = Str("WhitelistRegex", "");
        d.Blacklist = SplitList(Str("Blacklist", ""));
        d.BlacklistRegex = Str("BlacklistRegex", "");
        d.FirewallRules = Str("FirewallRules", "").Trim();
        d.FirewallUriRules = Str("FirewallUriRules", "").Trim();
        d.ProcessToRunOnBan = Str("ProcessToRunOnBan", "");
        d.ProcessToRunOnSuccessfulLogin = Str("ProcessToRunOnSuccessfulLogin", "");
        d.ProcessToRunOnUnban = Str("ProcessToRunOnUnban", "");
        return d;
    }

    public static IDictionary<string, string> ToSettings(IPBanConfiguration c)
    {
        string B(bool v) => v ? "true" : "false";
        string I(int v) => v.ToString(CultureInfo.InvariantCulture);
        return new Dictionary<string, string>
        {
            ["FailedLoginAttemptsBeforeBan"] = I(c.FailedLoginAttemptsBeforeBan),
            ["BanTime"] = c.BanTime.Trim(),
            ["ExpireTime"] = c.ExpireTime.Trim(),
            ["CycleTime"] = c.CycleTime.Trim(),
            ["MinimumTimeBetweenFailedLoginAttempts"] = c.MinimumTimeBetweenFailedLoginAttempts.Trim(),
            ["MinimumTimeBetweenSuccessfulLoginAttempts"] = c.MinimumTimeBetweenSuccessfulLoginAttempts.Trim(),
            ["ResetFailedLoginCountForUnbannedIPAddresses"] = B(c.ResetFailedLoginCountForUnbannedIPAddresses),
            ["ClearBannedIPAddressesOnRestart"] = B(c.ClearBannedIPAddressesOnRestart),
            ["ClearFailedLoginsOnSuccessfulLogin"] = B(c.ClearFailedLoginsOnSuccessfulLogin),
            ["ProcessInternalIPAddresses"] = B(c.ProcessInternalIPAddresses),
            ["UseDefaultBannedIPAddressHandler"] = B(c.UseDefaultBannedIPAddressHandler),
            ["FirewallRulePrefix"] = c.FirewallRulePrefix.Trim(),
            ["ExternalIPAddressUrl"] = c.ExternalIPAddressUrl ?? "",
            ["IPThreatApiKey"] = c.IPThreatApiKey ?? "",
            ["TruncateUserNameChars"] = c.TruncateUserNameChars ?? "",
            ["UserNameWhitelistMinimumEditDistance"] = I(c.UserNameWhitelistMinimumEditDistance),
            ["FailedLoginAttemptsBeforeBanUserNameWhitelist"] = I(c.FailedLoginAttemptsBeforeBanUserNameWhitelist),
            ["UserNameWhitelist"] = JoinList(c.UserNameWhitelist),
            ["UserNameWhitelistRegex"] = c.UserNameWhitelistRegex ?? "",
            ["Whitelist"] = JoinList(c.Whitelist),
            ["WhitelistRegex"] = c.WhitelistRegex ?? "",
            ["Blacklist"] = JoinList(c.Blacklist),
            ["BlacklistRegex"] = c.BlacklistRegex ?? "",
            ["FirewallRules"] = c.FirewallRules ?? "",
            ["FirewallUriRules"] = c.FirewallUriRules ?? "",
            ["ProcessToRunOnBan"] = c.ProcessToRunOnBan ?? "",
            ["ProcessToRunOnSuccessfulLogin"] = c.ProcessToRunOnSuccessfulLogin ?? "",
            ["ProcessToRunOnUnban"] = c.ProcessToRunOnUnban ?? "",
        };
    }

    public static List<string> SplitList(string value) =>
        (value ?? "").Split(new[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();

    public static string JoinList(IEnumerable<string> items) =>
        string.Join(",", (items ?? Enumerable.Empty<string>()).Select(x => x.Trim()).Where(x => x.Length > 0));
}
