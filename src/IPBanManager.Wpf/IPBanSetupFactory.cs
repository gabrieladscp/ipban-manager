using System;
using System.IO;
using System.Linq;
using System.Reflection;
using IPBanManager.Core.Abstractions;
using IPBanManager.Core.Provisioning;
using IPBanManager.Infrastructure;
using IPBanManager.Infrastructure.Provisioning;

namespace IPBanManager.Wpf;

/// <summary>Monta o instalador do IPBan (usado pela interface e pela linha de comando).</summary>
internal static class IPBanSetupFactory
{
    public const string ServiceName = "IPBan";

    /// <summary>Pacote embutido: arquivo ao lado do executável; versão e SHA-256 gravados na compilação.</summary>
    public static IPBanBundle Bundle()
    {
        var meta = typeof(IPBanSetupFactory).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToList();
        string Meta(string key) => meta.FirstOrDefault(a => a.Key == key)?.Value;

        Version.TryParse(Meta("IPBanBundleVersion") ?? "", out var version);
        var zip = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "IPBan", "IPBan-Windows-x64.zip");
        return new IPBanBundle(zip, version, Meta("IPBanBundleSha256"));
    }

    public static IIPBanProvisioner CreateProvisioner(IAppLogger logger) =>
        new IPBanProvisioner(Bundle(), new WindowsProvisioningHost(), new IPBanFileDeployer(),
            dir => new XmlConfigurationRepository(new FixedInstallationLocator(dir, ServiceName)),
            ServiceName, backupRoot: null, logger: logger);
}
