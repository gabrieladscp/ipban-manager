using System;
using System.Text.RegularExpressions;

namespace IPBanManager.Core.Validation;

/// <summary>Formato de duração do IPBan: DD:HH:MM:SS.</summary>
public static class TimeSpanFormat
{
    private static readonly Regex Pattern = new Regex(@"^(\d{1,4}):([0-1]\d|2[0-3]):([0-5]\d):([0-5]\d)$");

    public static bool TryParse(string text, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        var m = Pattern.Match((text ?? "").Trim());
        if (!m.Success) return false;
        value = new TimeSpan(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
            int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value));
        return true;
    }

    public static bool IsValid(string text) => TryParse(text, out _);
}
