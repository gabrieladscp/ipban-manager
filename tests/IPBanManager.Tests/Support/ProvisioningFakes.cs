using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using IPBanManager.Core.Provisioning;
using IPBanManager.Infrastructure.Provisioning;

namespace IPBanManager.Tests.Support;

internal sealed class FakeProvisioner : IIPBanProvisioner
{
    public IPBanSetupStatus Status { get; set; } = new IPBanSetupStatus
    {
        BundleAvailable = true, BundledVersion = new Version(4, 1, 0, 0),
    };
    public IPBanSetupOptions LastOptions { get; private set; }
    public int InstallCalls { get; private set; }
    public Exception InstallException { get; set; }
    public string ResultDirectory { get; set; } = @"C:\Program Files\IPBan";
    public List<string> ResultWarnings { get; } = new List<string>();

    public IPBanSetupStatus Inspect() => Status;

    public IPBanSetupResult Install(IPBanSetupOptions options, IProgress<string> progress)
    {
        InstallCalls++;
        LastOptions = options;
        if (InstallException != null) throw InstallException;
        var result = new IPBanSetupResult { InstallDirectory = ResultDirectory, Version = Status.BundledVersion, Upgraded = Status.ServiceInstalled };
        result.Warnings.AddRange(ResultWarnings);
        return result;
    }
}

internal sealed class FakeAddresses : IRemoteAccessAddressProvider
{
    public List<string> Addresses { get; } = new List<string>();
    public Exception Exception { get; set; }
    public IReadOnlyList<string> GetAdministratorAddresses() => Exception != null ? throw Exception : Addresses;
}

internal sealed class FakeSetupLauncher : IIPBanSetupLauncher
{
    public IPBanSetupStatus Status { get; set; } = new IPBanSetupStatus();
    public bool Result { get; set; } = true;
    public int ShowCalls { get; private set; }
    public Action OnShow { get; set; }
    public IPBanSetupStatus Inspect() => Status;
    public bool ShowSetup() { ShowCalls++; OnShow?.Invoke(); return Result; }
}

/// <summary>Sistema operacional simulado: registra as chamadas, sem tocar em serviços reais.</summary>
internal sealed class FakeProvisioningHost : IProvisioningHost
{
    public bool IsAdministrator { get; set; } = true;
    public bool Is64BitOperatingSystem { get; set; } = true;
    public string ProgramFilesDirectory { get; set; }
    public string ImagePath { get; set; }
    public Version InstalledVersion { get; set; }
    public List<string> Calls { get; } = new List<string>();
    public Exception StartException { get; set; }
    public int FailStartTimes { get; set; }
    public string AuditError { get; set; }
    public List<string> DisabledProfiles { get; } = new List<string>();

    public string GetServiceImagePath(string serviceName) => ImagePath;
    public Version GetFileVersion(string path) => InstalledVersion;
    public void StopService(string serviceName) => Calls.Add("stop");

    public void StartService(string serviceName)
    {
        Calls.Add("start");
        if (FailStartTimes > 0) { FailStartTimes--; throw StartException ?? new InvalidOperationException("falha ao iniciar"); }
    }

    public void RegisterService(string serviceName, string executablePath)
    {
        Calls.Add("register:" + executablePath);
        ImagePath = "\"" + executablePath + "\"";
    }

    public string EnableLogonAuditing() { Calls.Add("audit"); return AuditError; }
    public IReadOnlyList<string> GetDisabledFirewallProfiles() => DisabledProfiles;
}

internal static class BundleBuilder
{
    public const string ConfigXml = @"<?xml version=""1.0""?><configuration><appSettings>" +
                                    @"<add key=""Whitelist"" value=""""/><add key=""FirewallRulePrefix"" value=""IPBan_""/>" +
                                    "</appSettings></configuration>";

    /// <summary>Cria um zip no formato do pacote oficial (executável único + configs).</summary>
    public static string Create(string directory, string exeContent = "new-exe")
    {
        var zip = Path.Combine(directory, "bundle.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            Add(archive, "DigitalRuby.IPBan.exe", exeContent);
            Add(archive, "ipban.config", ConfigXml);
            Add(archive, "ipban.override.config", "<configuration/>");
            Add(archive, "README.md", "readme");
        }
        return zip;
    }

    public static string Sha256(string path)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
        using (var s = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
    }

    private static void Add(ZipArchive archive, string name, string content)
    {
        using (var w = new StreamWriter(archive.CreateEntry(name).Open())) w.Write(content);
    }
}
