namespace IPBanManager.Core.Models;

public enum ServiceState { NotInstalled, Stopped, Running, Transitioning }

public class BannedIp
{
    public BannedIp(string address, string ruleName, string origin = "")
    {
        Address = address;
        RuleName = ruleName;
        Origin = origin;
    }

    public string Address { get; }
    public string RuleName { get; }

    /// <summary>Automático (falhas de login) ou Blacklist (manual/configuração).</summary>
    public string Origin { get; }
}
