using System.Linq;
using System.Reflection;
using IPBanManager.Core.Updates;

namespace IPBanManager.Wpf;

/// <summary>Versão do próprio executável, completa e em SemVer (ex.: 1.0.1-beta.1), sem metadados de build.</summary>
internal static class AppVersion
{
    public static string Current { get; } = Resolve(typeof(AppVersion).Assembly);

    private static string Resolve(Assembly assembly)
    {
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? assembly.GetName().Version.ToString(3);
        return SemVer.TryParse(info, out var version) ? version.ToString() : info.Split('+').First();
    }
}
