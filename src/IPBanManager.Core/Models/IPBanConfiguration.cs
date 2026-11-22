using System.Collections.Generic;

namespace IPBanManager.Core.Models;

/// <summary>Configuração do IPBan editável pela interface (subset tipado do ipban.config).</summary>
public class IPBanConfiguration
{
    // Detecção de falhas
    public int FailedLoginAttemptsBeforeBan { get; set; } = 5;
    public string BanTime { get; set; } = "01:00:00:00";
    public string ExpireTime { get; set; } = "01:00:00:00";
    public string CycleTime { get; set; } = "00:00:00:15";
    public string MinimumTimeBetweenFailedLoginAttempts { get; set; } = "00:00:00:01";
    public string MinimumTimeBetweenSuccessfulLoginAttempts { get; set; } = "00:00:00:05";

    // Comportamento
    public bool ResetFailedLoginCountForUnbannedIPAddresses { get; set; }
    public bool ClearBannedIPAddressesOnRestart { get; set; }
    public bool ClearFailedLoginsOnSuccessfulLogin { get; set; }
    public bool ProcessInternalIPAddresses { get; set; }
    public bool UseDefaultBannedIPAddressHandler { get; set; } = true;
    public string FirewallRulePrefix { get; set; } = "IPBan_";
    public string ExternalIPAddressUrl { get; set; } = "https://checkip.amazonaws.com/";
    public string IPThreatApiKey { get; set; } = "";

    // Nomes de usuário
    public string TruncateUserNameChars { get; set; } = "@";
    public int UserNameWhitelistMinimumEditDistance { get; set; } = 2;
    public int FailedLoginAttemptsBeforeBanUserNameWhitelist { get; set; } = 20;
    public List<string> UserNameWhitelist { get; set; } = new List<string>();
    public string UserNameWhitelistRegex { get; set; } = "";

    // Listas de IPs
    public List<string> Whitelist { get; set; } = new List<string>();
    public string WhitelistRegex { get; set; } = "";
    public List<string> Blacklist { get; set; } = new List<string>();
    public string BlacklistRegex { get; set; } = "";

    // Regras e ações (texto multilinha, um item por linha)
    public string FirewallRules { get; set; } = "";
    public string FirewallUriRules { get; set; } = "";
    public string ProcessToRunOnBan { get; set; } = "";
    public string ProcessToRunOnSuccessfulLogin { get; set; } = "";
    public string ProcessToRunOnUnban { get; set; } = "";
}
