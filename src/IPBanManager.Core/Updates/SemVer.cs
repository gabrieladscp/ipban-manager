using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace IPBanManager.Core.Updates;

/// <summary>
/// Versão semântica 2.0 (https://semver.org): MAJOR.MINOR.PATCH[-prerelease][+build].
/// Metadados de build são ignorados na comparação, conforme a especificação.
/// </summary>
public sealed class SemVer : IComparable<SemVer>, IEquatable<SemVer>
{
    private static readonly Regex Pattern = new Regex(
        @"^v?(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)" +
        @"(?:-(?<pre>(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*))?" +
        @"(?:\+(?<build>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string[] _preRelease;

    private SemVer(int major, int minor, int patch, string[] preRelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        _preRelease = preRelease;
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public bool IsPrerelease => _preRelease.Length > 0;

    /// <summary>Canal natural da versão: qualquer pré-release pertence ao Beta.</summary>
    public UpdateChannel Channel => IsPrerelease ? UpdateChannel.Beta : UpdateChannel.Stable;

    public static bool TryParse(string text, out SemVer version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var m = Pattern.Match(text.Trim());
        if (!m.Success) return false;
        if (!int.TryParse(m.Groups["major"].Value, out var major) ||
            !int.TryParse(m.Groups["minor"].Value, out var minor) ||
            !int.TryParse(m.Groups["patch"].Value, out var patch)) return false;

        var pre = m.Groups["pre"].Success ? m.Groups["pre"].Value.Split('.') : new string[0];
        version = new SemVer(major, minor, patch, pre);
        return true;
    }

    public static SemVer Parse(string text) =>
        TryParse(text, out var v) ? v : throw new FormatException($"'{text}' não é uma versão semântica válida (MAJOR.MINOR.PATCH[-pre]).");

    public int CompareTo(SemVer other)
    {
        if (other is null) return 1;
        int c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;

        // Uma versão estável tem precedência sobre qualquer pré-release da mesma base.
        if (!IsPrerelease && !other.IsPrerelease) return 0;
        if (!IsPrerelease) return 1;
        if (!other.IsPrerelease) return -1;

        int shared = Math.Min(_preRelease.Length, other._preRelease.Length);
        for (int i = 0; i < shared; i++)
        {
            c = CompareIdentifier(_preRelease[i], other._preRelease[i]);
            if (c != 0) return c;
        }
        return _preRelease.Length.CompareTo(other._preRelease.Length);
    }

    private static int CompareIdentifier(string a, string b)
    {
        bool aNum = a.All(char.IsDigit), bNum = b.All(char.IsDigit);
        if (aNum && bNum)
        {
            // Sem overflow: compara por tamanho e depois lexicograficamente (já sem zeros à esquerda).
            int byLength = a.Length.CompareTo(b.Length);
            return byLength != 0 ? byLength : string.CompareOrdinal(a, b);
        }
        if (aNum) return -1; // numéricos < alfanuméricos
        if (bNum) return 1;
        return string.CompareOrdinal(a, b);
    }

    public bool Equals(SemVer other) => other is not null && CompareTo(other) == 0;
    public override bool Equals(object obj) => Equals(obj as SemVer);

    public override int GetHashCode()
    {
        unchecked
        {
            int h = Major * 397 ^ Minor;
            h = h * 397 ^ Patch;
            foreach (var id in _preRelease) h = h * 397 ^ id.GetHashCode();
            return h;
        }
    }

    public override string ToString() =>
        $"{Major}.{Minor}.{Patch}" + (IsPrerelease ? "-" + string.Join(".", _preRelease) : "");

    public static bool operator >(SemVer a, SemVer b) => a.CompareTo(b) > 0;
    public static bool operator <(SemVer a, SemVer b) => a.CompareTo(b) < 0;
    public static bool operator >=(SemVer a, SemVer b) => a.CompareTo(b) >= 0;
    public static bool operator <=(SemVer a, SemVer b) => a.CompareTo(b) <= 0;
}
