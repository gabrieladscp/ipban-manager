using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace IPBanManager.Core.Validation;

public enum ListEntryKind
{
    /// <summary>IP (v4/v6), faixa CIDR ou nome de domínio.</summary>
    Address,

    /// <summary>Nome de usuário do Windows.</summary>
    UserName,

    /// <summary>Somente IP (v4/v6) ou faixa CIDR, sem domínio (bloqueio manual).</summary>
    IpAddress,

    /// <summary>IP ou faixa CIDR de regra de firewall: faixas amplas são permitidas (ex.: 0.0.0.0/1).</summary>
    FirewallAddress,
}

/// <summary>Regras de digitação e validação dos itens das listas (whitelist/blacklist/usuários).</summary>
public static class ListEntryRules
{
    private static readonly Regex Domain = new Regex(
        @"^(?=.{1,253}$)([a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Caracteres que podem ser digitados no campo (o resto é ignorado).</summary>
    public static bool IsAllowedChar(ListEntryKind kind, char c)
    {
        if (kind == ListEntryKind.UserName)
            return !char.IsWhiteSpace(c) && !char.IsControl(c) && c != ',' && c != '|' && c != ';';
        return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
               || c == '.' || c == ':' || c == '/' || c == '-';
    }

    /// <summary>Separadores aceitos ao colar vários itens de uma vez.</summary>
    public static IReadOnlyList<string> SplitPasted(string text) =>
        (text ?? "").Split(new[] { ' ', '\t', '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim()).Where(t => t.Length > 0).ToList();

    /// <summary>
    /// Valida um novo item. Retorna a mensagem de erro, ou null se válido (com <paramref name="normalized"/> pronto
    /// para gravar: IP canônico, domínio em minúsculas).
    /// </summary>
    public static string Validate(ListEntryKind kind, string text, IEnumerable<string> existing, out string normalized)
    {
        normalized = null;
        var value = (text ?? "").Trim();
        if (value.Length == 0) return "Digite um valor.";
        if (value.Any(c => !IsAllowedChar(kind, c))) return "Contém caracteres não permitidos.";

        string error = kind == ListEntryKind.UserName
            ? ValidateUserName(value, out normalized)
            : ValidateAddress(value, kind != ListEntryKind.Address, kind == ListEntryKind.FirewallAddress, out normalized);
        if (error != null) return error;

        var candidate = normalized;
        if (existing != null && existing.Any(e => string.Equals(e?.Trim(), candidate, StringComparison.OrdinalIgnoreCase)))
            return $"'{candidate}' já está na lista.";
        return null;
    }

    private static string ValidateUserName(string value, out string normalized)
    {
        normalized = value;
        return value.Length > 104 ? "Nome de usuário longo demais." : null;
    }

    private static string ValidateAddress(string value, bool ipOnly, bool allowWide, out string normalized)
    {
        normalized = null;
        var lower = value.ToLowerInvariant();

        if (lower.Contains(':')) return ValidateIPv6(lower, allowWide, out normalized);
        if (lower.All(c => char.IsDigit(c) || c == '.' || c == '/')) return ValidateIPv4(lower, allowWide, out normalized);
        if (ipOnly) return "Informe um IP (ex.: 203.0.113.7) ou uma faixa CIDR (ex.: 198.51.100.0/24); domínios não são aceitos aqui.";
        if (lower.Contains('/')) return "Faixa CIDR só vale para IP (ex.: 203.0.113.0/24).";
        if (!Domain.IsMatch(lower))
            return "Domínio inválido. Use um nome completo, como servidor.exemplo.com.br.";
        normalized = lower;
        return null;
    }

    private static string ValidateIPv4(string value, bool allowWide, out string normalized)
    {
        const string help = "IP inválido. Use 4 números de 0 a 255 separados por ponto (ex.: 203.0.113.10) " +
                            "ou uma faixa CIDR (ex.: 203.0.113.0/24).";
        normalized = null;
        var parts = value.Split('/');
        if (parts.Length > 2) return help;

        var octets = parts[0].Split('.');
        if (octets.Length != 4) return help;
        foreach (var o in octets)
        {
            // Sem zeros à esquerda: "010" seria lido como octal por algumas ferramentas.
            if (o.Length == 0 || o.Length > 3 || (o.Length > 1 && o[0] == '0') || int.Parse(o) > 255) return help;
        }

        var ip = string.Join(".", octets);
        if (parts.Length == 1)
        {
            if (ip == "0.0.0.0" || octets[0] == "127") return "Endereços locais (0.0.0.0, 127.x.x.x) não podem ser usados.";
            normalized = ip;
            return null;
        }

        if (parts[1].Length == 0 || parts[1].Length > 2 || (parts[1].Length > 1 && parts[1][0] == '0')
            || !int.TryParse(parts[1], out var prefix) || prefix > 32)
            return "Faixa CIDR inválida: o número após a barra deve ir de 0 a 32 (ex.: 203.0.113.0/24).";
        if (prefix < 8 && !allowWide) return "Faixa grande demais (menor que /8): cobriria milhões de endereços.";
        if (octets[0] == "127" && !allowWide) return "Endereços locais (127.x.x.x) não podem ser usados.";
        normalized = ip + "/" + prefix;
        return null;
    }

    private static string ValidateIPv6(string value, bool allowWide, out string normalized)
    {
        normalized = null;
        var parts = value.Split('/');
        if (parts.Length > 2 || !IPAddress.TryParse(parts[0], out var ip) || ip.AddressFamily != AddressFamily.InterNetworkV6)
            return "Endereço IPv6 inválido (ex.: 2001:db8::10 ou 2001:db8::/32).";
        var canonical = ip.ToString();
        if (parts.Length == 1)
        {
            if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.IPv6Any)) return "Endereços locais (::1, ::) não podem ser usados.";
            normalized = canonical;
            return null;
        }
        int min = allowWide ? 0 : 16;
        if (!int.TryParse(parts[1], out var prefix) || prefix < min || prefix > 128)
            return $"Faixa IPv6 inválida: o prefixo deve ir de {min} a 128.";
        normalized = canonical + "/" + prefix;
        return null;
    }
}
