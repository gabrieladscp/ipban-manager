using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace IPBanManager.Core.Validation;

/// <summary>Nomes/prefixos usados pelo IPBan em regras do firewall: letras, números, '_' e '-'.</summary>
public static class RuleName
{
    private static readonly Regex Valid = new Regex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled);

    public static bool IsAllowedChar(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_' || c == '-';

    /// <summary>Retorna o erro, ou null se válido.</summary>
    public static string Validate(string label, string value) =>
        Valid.IsMatch(value ?? "") ? null : $"{label}: use de 1 a 64 caracteres entre letras, números, '_' e '-'.";
}

/// <summary>Durações (DD:HH:MM:SS) da lista de tempos de bloqueio.</summary>
public static class BanTimeRules
{
    /// <summary>Valida uma nova duração para a lista; retorna o erro, ou null.</summary>
    public static string ValidateNew(string value, IEnumerable<string> existing)
    {
        if (!TimeSpanFormat.TryParse(value, out var span)) return "Duração inválida.";
        var current = existing.Select(e => TimeSpanFormat.TryParse(e, out var s) ? s : (TimeSpan?)null).ToList();
        if (span == TimeSpan.Zero && current.Count > 0)
            return "00:00:00:00 (bloqueio máximo) só pode ser usado sozinho na lista.";
        if (current.Any(s => s == TimeSpan.Zero))
            return "A lista contém o bloqueio máximo (00:00:00:00); remova-o para usar tempos escalonados.";
        if (current.Any(s => s == span)) return "Esse tempo já está na lista.";
        return null;
    }

    /// <summary>Ordena em ordem crescente (exigida pelo IPBan para o bloqueio escalonado).</summary>
    public static List<string> Sort(IEnumerable<string> values) =>
        values.OrderBy(v => TimeSpanFormat.TryParse(v, out var s) ? s : TimeSpan.MaxValue).ToList();

    /// <summary>Texto amigável: "1 dia", "2 h 30 min", "bloqueio máximo".</summary>
    public static string Describe(string value)
    {
        if (!TimeSpanFormat.TryParse(value, out var s)) return value;
        if (s == TimeSpan.Zero) return "bloqueio máximo";
        var parts = new List<string>();
        if (s.Days > 0) parts.Add(s.Days == 1 ? "1 dia" : $"{s.Days} dias");
        if (s.Hours > 0) parts.Add($"{s.Hours} h");
        if (s.Minutes > 0) parts.Add($"{s.Minutes} min");
        if (s.Seconds > 0) parts.Add($"{s.Seconds} s");
        return string.Join(" ", parts);
    }
}

/// <summary>Regra de firewall personalizada: nome;allow|block;IPs;portas;regex da plataforma.</summary>
public sealed class FirewallRuleEntry
{
    public static readonly IReadOnlyList<(string Regex, string Label)> Platforms =
        new[] { (".", "Todas"), ("windows", "Windows"), ("linux", "Linux") };

    public string Name { get; set; }
    public bool Allow { get; set; }
    public List<string> Addresses { get; set; } = new List<string>();
    public string Ports { get; set; } = "";
    public string Platform { get; set; } = ".";

    public string Format() => $"{Name};{(Allow ? "allow" : "block")};{string.Join(",", Addresses)};{Ports};{Platform}";

    public static bool IsAllowedPortChar(char c) => (c >= '0' && c <= '9') || c == ',' || c == '-';

    /// <summary>Monta e valida a regra a partir do formulário. Retorna o erro, ou null.</summary>
    public static string Create(string name, bool allow, string addresses, string ports, string platform,
        out FirewallRuleEntry entry)
    {
        entry = null;
        var error = RuleName.Validate("Nome da regra", (name ?? "").Trim());
        if (error != null) return error;

        var list = new List<string>();
        foreach (var token in ListEntryRules.SplitPasted(addresses))
        {
            var e = ListEntryRules.Validate(ListEntryKind.FirewallAddress, token, list, out var normalized);
            if (e != null) return $"IP '{token}': {e}";
            list.Add(normalized);
        }
        if (list.Count == 0) return "Informe ao menos um IP ou faixa CIDR.";

        var portsText = (ports ?? "").Replace(" ", "");
        error = ValidatePorts(portsText);
        if (error != null) return error;

        entry = new FirewallRuleEntry
        {
            Name = name.Trim(), Allow = allow, Addresses = list, Ports = portsText,
            Platform = string.IsNullOrWhiteSpace(platform) ? "." : platform,
        };
        return null;
    }

    /// <summary>Vazio = todas as portas; senão lista de portas/faixas (1-65535), ex.: 80,443,8000-8100.</summary>
    public static string ValidatePorts(string ports)
    {
        if (string.IsNullOrEmpty(ports)) return null;
        foreach (var part in ports.Split(','))
        {
            var bounds = part.Split('-');
            if (bounds.Length > 2 || bounds.Any(b => !IsPort(b)))
                return $"Porta inválida: '{part}'. Use números de 1 a 65535, separados por vírgula; faixas com hífen (ex.: 80,443,8000-8100).";
            if (bounds.Length == 2 && int.Parse(bounds[0]) > int.Parse(bounds[1]))
                return $"Faixa de portas invertida: '{part}'.";
        }
        return null;
    }

    private static bool IsPort(string text) =>
        text.Length > 0 && text.Length <= 5 && text.All(char.IsDigit) && int.Parse(text) is int p && p >= 1 && p <= 65535;

    public static string Describe(string line)
    {
        var p = (line ?? "").Split(';');
        if (p.Length < 3) return line;
        var action = string.Equals(p[1], "allow", StringComparison.OrdinalIgnoreCase) ? "PERMITIR" : "BLOQUEAR";
        var ports = p.Length > 3 && p[3].Length > 0 ? "portas " + p[3] : "todas as portas";
        return $"{p[0]}: {action} {p[2]} ({ports})";
    }
}

/// <summary>Lista externa de bloqueio: prefixo,intervalo,URL,máximo.</summary>
public static class UriRuleEntry
{
    public const int MaxCountLimit = 1000000;

    public static string Create(string prefix, string interval, string url, int maxCount, out string line)
    {
        line = null;
        var error = RuleName.Validate("Prefixo", (prefix ?? "").Trim());
        if (error != null) return error;
        if (!TimeSpanFormat.TryParse(interval, out var span) || span < TimeSpan.FromMinutes(1))
            return "Intervalo de atualização: informe pelo menos 1 minuto.";
        error = UrlRules.Validate("URL da lista", url, required: true);
        if (error != null) return error;
        if (maxCount < 1 || maxCount > MaxCountLimit) return $"Máximo de IPs: informe de 1 a {MaxCountLimit:N0}.";
        line = $"{prefix.Trim()},{interval},{url.Trim()},{maxCount.ToString(CultureInfo.InvariantCulture)}";
        return null;
    }
}

/// <summary>Processo executado em eventos do IPBan: caminho|argumentos.</summary>
public static class ProcessEntry
{
    public static readonly IReadOnlyList<string> Variables =
        new[] { "###IPADDRESS###", "###USERNAME###", "###SOURCE###", "###COUNT###" };

    /// <summary>Retorna o erro, ou null. <paramref name="fileExists"/> permite testar sem disco.</summary>
    public static string Create(string path, string arguments, Func<string, bool> fileExists, out string line)
    {
        line = null;
        var p = (path ?? "").Trim().Trim('"');
        if (p.Length == 0) return "Escolha o programa a executar.";
        if (p.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || p.Contains("|") || !Path.IsPathRooted(p))
            return "Caminho do programa inválido: use o caminho completo (ex.: C:\\Scripts\\avisar.exe).";
        if (!fileExists(p)) return $"O programa '{p}' não existe neste servidor.";
        var args = (arguments ?? "").Trim();
        if (args.Contains("|") || args.Contains("\n")) return "Os argumentos não podem conter '|' nem quebras de linha.";
        line = p + "|" + args;
        return null;
    }
}

public static class UrlRules
{
    /// <summary>URL http/https absoluta. Retorna o erro, ou null.</summary>
    public static string Validate(string label, string url, bool required)
    {
        var value = (url ?? "").Trim();
        if (value.Length == 0) return required ? $"{label}: informe o endereço." : null;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
               && !string.IsNullOrEmpty(uri.Host)
            ? null
            : $"{label}: endereço inválido. Use um endereço completo começando com https:// ou http://.";
    }
}
