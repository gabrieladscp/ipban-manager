using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using IPBanManager.Core.Models;

namespace IPBanManager.Core.Validation;

public class ConfigurationValidator
{
    public IReadOnlyList<string> Validate(IPBanConfiguration c)
    {
        var errors = new List<string>();

        if (c.FailedLoginAttemptsBeforeBan < 1)
            errors.Add("Tentativas antes do bloqueio: informe um número inteiro maior ou igual a 1.");
        if (c.UserNameWhitelistMinimumEditDistance < 0)
            errors.Add("Distância mínima de edição: informe um número inteiro maior ou igual a 0.");
        if (c.FailedLoginAttemptsBeforeBanUserNameWhitelist < 1)
            errors.Add("Tentativas para usuários da whitelist: informe um número inteiro maior ou igual a 1.");

        ValidateBanTime(c.BanTime, errors);
        RequireDuration("Tempo de expiração", c.ExpireTime, errors);
        RequireDuration("Intervalo de ciclo", c.CycleTime, errors);
        RequireDuration("Tempo mínimo entre falhas", c.MinimumTimeBetweenFailedLoginAttempts, errors);
        RequireDuration("Tempo mínimo entre sucessos", c.MinimumTimeBetweenSuccessfulLoginAttempts, errors);

        if (string.IsNullOrWhiteSpace(c.FirewallRulePrefix))
            errors.Add("Prefixo das regras de firewall é obrigatório.");

        RequireRegex("Regex da whitelist", c.WhitelistRegex, errors);
        RequireRegex("Regex da blacklist", c.BlacklistRegex, errors);
        RequireRegex("Regex da whitelist de usuários", c.UserNameWhitelistRegex, errors);

        foreach (var entry in c.Whitelist.Concat(c.Blacklist))
            if (!IpEntry.IsListEntry(entry))
                errors.Add($"Entrada inválida nas listas: '{entry}'.");

        return errors;
    }

    private static void ValidateBanTime(string value, List<string> errors)
    {
        var spans = new List<TimeSpan>();
        foreach (var part in (value ?? "").Split(',').Select(p => p.Trim()))
        {
            if (!TimeSpanFormat.TryParse(part, out var span))
            {
                errors.Add($"Tempo de bloqueio inválido: '{part}'. Use DD:HH:MM:SS (vários valores separados por vírgula).");
                return;
            }
            spans.Add(span);
        }
        for (int i = 1; i < spans.Count; i++)
        {
            if (spans[i] <= spans[i - 1])
            {
                errors.Add("Com vários tempos de bloqueio, os valores devem estar em ordem estritamente crescente.");
                return;
            }
        }
    }

    private static void RequireDuration(string name, string value, List<string> errors)
    {
        if (!TimeSpanFormat.IsValid(value))
            errors.Add($"{name} inválido: '{value}'. Use DD:HH:MM:SS.");
    }

    private static void RequireRegex(string name, string pattern, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return;
        try { new Regex(pattern); }
        catch (ArgumentException ex) { errors.Add($"{name} inválida: {ex.Message}"); }
    }
}
