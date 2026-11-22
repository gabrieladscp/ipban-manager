using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using IPBanManager.Core.Abstractions;
using IPBanManager.Core.Errors;
using IPBanManager.Core.Models;

namespace IPBanManager.Infrastructure;

/// <summary>Lista os IPs bloqueados lendo as regras do Windows Firewall (COM HNetCfg.FwPolicy2).</summary>
public class FirewallBannedIpProvider : IBannedIpProvider
{
    public IReadOnlyList<BannedIp> GetBannedIps(string firewallRulePrefix)
    {
        var prefix = firewallRulePrefix ?? "IPBan_";
        var autoPrefix = prefix + "Block_";                // bloqueios automáticos por falha de login
        var blacklistPrefix = prefix + "GlobalBlacklist_"; // entradas da Blacklist do ipban.config

        object policy = null, rules = null;
        var result = new List<BannedIp>();
        try
        {
            var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2")
                       ?? throw new ManagerException("O Windows Firewall não está disponível neste computador.");
            policy = Activator.CreateInstance(type);
            rules = ((dynamic)policy).Rules;

            foreach (var item in (IEnumerable)rules)
            {
                try
                {
                    dynamic rule = item;
                    string name = rule.Name;
                    string origin = OriginOf(name, autoPrefix, blacklistPrefix);
                    if (origin == null) continue;

                    string remote = rule.RemoteAddresses;
                    result.AddRange(ParseRemoteAddresses(remote).Select(a => new BannedIp(a, name, origin)));
                }
                finally
                {
                    Release(item); // cada regra é um objeto COM: sem isso há vazamento a cada consulta
                }
            }
        }
        catch (COMException ex)
        {
            throw new ManagerException(
                "Não foi possível ler o Windows Firewall. Verifique se o serviço \"Windows Defender Firewall\" está em execução.", ex);
        }
        finally
        {
            Release(rules);
            Release(policy);
        }
        return result.OrderBy(b => b.Address, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string OriginOf(string ruleName, string autoPrefix, string blacklistPrefix)
    {
        if (ruleName == null) return null;
        if (ruleName.StartsWith(autoPrefix, StringComparison.OrdinalIgnoreCase)) return "Automático";
        if (ruleName.StartsWith(blacklistPrefix, StringComparison.OrdinalIgnoreCase)) return "Blacklist";
        return null;
    }

    private static void Release(object com)
    {
        if (com != null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
    }

    /// <summary>Converte "1.2.3.4/255.255.255.255,10.0.0.0/255.0.0.0" em "1.2.3.4,10.0.0.0/8".</summary>
    public static IEnumerable<string> ParseRemoteAddresses(string remoteAddresses)
    {
        if (string.IsNullOrWhiteSpace(remoteAddresses) || remoteAddresses == "*") yield break;
        foreach (var raw in remoteAddresses.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            yield return Normalize(raw.Trim());
    }

    public static string Normalize(string entry)
    {
        var parts = entry.Split('/');
        if (parts.Length != 2) return entry;
        if (IPAddress.TryParse(parts[1], out var mask) && mask.AddressFamily == AddressFamily.InterNetwork
            && IPAddress.TryParse(parts[0], out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
        {
            int bits = mask.GetAddressBytes().Sum(b => Convert.ToString(b, 2).Count(c => c == '1'));
            return bits == 32 ? parts[0] : parts[0] + "/" + bits;
        }
        if (int.TryParse(parts[1], out var prefix) && prefix == 128) return parts[0];
        return entry;
    }
}
