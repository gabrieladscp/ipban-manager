using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using IPBanManager.Core.Errors;
using IPBanManager.Core.Models;
using IPBanManager.Core.Presenters;
using IPBanManager.Core.Provisioning;
using IPBanManager.Infrastructure;
using IPBanManager.Infrastructure.Provisioning;
using IPBanManager.Tests.Support;
using Xunit;

namespace IPBanManager.Tests.Infrastructure;

[Trait("Category", "Integration")]
public class IPBanFileDeployerTests : IDisposable
{
    private readonly TempDirectory _temp = new TempDirectory();
    private readonly IPBanFileDeployer _deployer = new IPBanFileDeployer(retries: 2, retryDelay: TimeSpan.Zero);
    private readonly string _target;
    private readonly string _backups;
    private readonly string _zip;

    public IPBanFileDeployerTests()
    {
        _target = Path.Combine(_temp.Path, "IPBan");
        _backups = Path.Combine(_temp.Path, "backups");
        _zip = BundleBuilder.Create(_temp.Path);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Deploy_FreshTarget_ExtractsEverythingAndReturnsNoBackup()
    {
        var backup = _deployer.Deploy(_zip, _target, _backups);

        Assert.Null(backup);
        Assert.Equal("new-exe", File.ReadAllText(Path.Combine(_target, "DigitalRuby.IPBan.exe")));
        Assert.True(File.Exists(Path.Combine(_target, "ipban.config")));
        Assert.False(Directory.Exists(Path.Combine(_target, ".ipbanmanager-staging")));
    }

    [Fact]
    public void Deploy_ExistingInstall_PreservesUserDataAndBacksUpOldBinaries()
    {
        Directory.CreateDirectory(Path.Combine(_target, "pt-BR"));
        File.WriteAllText(Path.Combine(_target, "ipban.config"), "<meu-config/>");
        File.WriteAllText(Path.Combine(_target, "nlog.config"), "<meu-nlog/>");
        File.WriteAllText(Path.Combine(_target, "ipban.sqlite"), "dados");
        File.WriteAllText(Path.Combine(_target, "logfile.txt"), "log");
        File.WriteAllText(Path.Combine(_target, "DigitalRuby.IPBan.exe"), "old-exe");
        File.WriteAllText(Path.Combine(_target, "DigitalRuby.IPBanCore.dll"), "old-dll");
        File.WriteAllText(Path.Combine(_target, "DigitalRuby.IPBan.deps.json"), "{}");
        File.WriteAllText(Path.Combine(_target, "pt-BR", "x.resources.dll"), "res");

        var backup = _deployer.Deploy(_zip, _target, _backups);

        Assert.Equal("<meu-config/>", File.ReadAllText(Path.Combine(_target, "ipban.config")));
        Assert.Equal("<meu-nlog/>", File.ReadAllText(Path.Combine(_target, "nlog.config")));
        Assert.Equal("dados", File.ReadAllText(Path.Combine(_target, "ipban.sqlite")));
        Assert.Equal("log", File.ReadAllText(Path.Combine(_target, "logfile.txt")));
        Assert.Equal("new-exe", File.ReadAllText(Path.Combine(_target, "DigitalRuby.IPBan.exe")));
        Assert.False(File.Exists(Path.Combine(_target, "DigitalRuby.IPBanCore.dll")));
        Assert.False(File.Exists(Path.Combine(_target, "DigitalRuby.IPBan.deps.json")));
        Assert.False(Directory.Exists(Path.Combine(_target, "pt-BR")));
        Assert.Equal("old-exe", File.ReadAllText(Path.Combine(backup, "DigitalRuby.IPBan.exe")));
        Assert.True(File.Exists(Path.Combine(backup, "pt-BR", "x.resources.dll")));
    }

    [Fact]
    public void Restore_PutsOldBinariesBack()
    {
        Directory.CreateDirectory(_target);
        File.WriteAllText(Path.Combine(_target, "DigitalRuby.IPBan.exe"), "old-exe");
        var backup = _deployer.Deploy(_zip, _target, _backups);

        _deployer.Restore(backup, _target);

        Assert.Equal("old-exe", File.ReadAllText(Path.Combine(_target, "DigitalRuby.IPBan.exe")));
    }

    [Fact]
    public void Deploy_LockedTargetFile_RestoresOldBinariesAndThrows()
    {
        Directory.CreateDirectory(_target);
        File.WriteAllText(Path.Combine(_target, "DigitalRuby.IPBan.exe"), "old-exe");
        File.WriteAllText(Path.Combine(_target, "README.md"), "old-readme");

        using (File.Open(Path.Combine(_target, "README.md"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => _deployer.Deploy(_zip, _target, _backups));
        }

        Assert.Equal("old-exe", File.ReadAllText(Path.Combine(_target, "DigitalRuby.IPBan.exe")));
    }

    [Fact]
    public void VerifyBundle_HashMatches_DoesNotThrow()
    {
        IPBanFileDeployer.VerifyBundle(_zip, BundleBuilder.Sha256(_zip).ToUpperInvariant());
    }

    [Fact]
    public void VerifyBundle_TamperedFile_Throws()
    {
        var expected = BundleBuilder.Sha256(_zip);
        File.AppendAllText(_zip, "x");

        var ex = Assert.Throws<ManagerException>(() => IPBanFileDeployer.VerifyBundle(_zip, expected));
        Assert.Contains("SHA-256", ex.Message);
    }

    [Fact]
    public void VerifyBundle_MissingFile_Throws()
    {
        Assert.Throws<ManagerException>(() => IPBanFileDeployer.VerifyBundle(_temp.File("nao-existe.zip"), "00"));
    }
}

[Trait("Category", "Integration")]
public class IPBanProvisionerTests : IDisposable
{
    private readonly TempDirectory _temp = new TempDirectory();
    private readonly FakeProvisioningHost _host = new FakeProvisioningHost();
    private readonly string _zip;
    private readonly IPBanProvisioner _provisioner;

    public IPBanProvisionerTests()
    {
        _zip = BundleBuilder.Create(_temp.Path);
        _host.ProgramFilesDirectory = Path.Combine(_temp.Path, "Program Files");
        var bundle = new IPBanBundle(_zip, new Version(4, 1, 0), BundleBuilder.Sha256(_zip));
        _provisioner = new IPBanProvisioner(bundle, _host, new IPBanFileDeployer(2, TimeSpan.Zero),
            dir => new XmlConfigurationRepository(new FixedInstallationLocator(dir)),
            backupRoot: Path.Combine(_temp.Path, "backups"));
    }

    public void Dispose() => _temp.Dispose();

    private string DefaultDir => Path.Combine(_host.ProgramFilesDirectory, "IPBan");

    private IPBanConfiguration LoadConfig(string dir) =>
        new XmlConfigurationRepository(new FixedInstallationLocator(dir)).Load();

    [Fact]
    public void Inspect_NoService_ReportsCanInstall()
    {
        var status = _provisioner.Inspect();

        Assert.True(status.CanInstall);
        Assert.False(status.CanUpgrade);
    }

    [Fact]
    public void Inspect_OlderServiceInstalled_ReportsCanUpgrade()
    {
        _host.ImagePath = "\"C:\\Program Files\\IPBan\\DigitalRuby.IPBan.exe\"";
        _host.InstalledVersion = new Version(1, 0, 0, 0);

        var status = _provisioner.Inspect();

        Assert.True(status.CanUpgrade);
        Assert.Equal(@"C:\Program Files\IPBan", status.InstallDirectory);
    }

    [Fact]
    public void Inspect_SameVersionInstalled_NothingToDo()
    {
        _host.ImagePath = @"C:\IPBan\DigitalRuby.IPBan.exe";
        _host.InstalledVersion = new Version(4, 1, 0, 0);

        var status = _provisioner.Inspect();

        Assert.False(status.CanInstall);
        Assert.False(status.CanUpgrade);
    }

    [Fact]
    public void Install_Fresh_DeploysConfiguresWhitelistAuditsRegistersAndStarts()
    {
        var result = _provisioner.Install(new IPBanSetupOptions { Whitelist = new[] { "203.0.113.10" } }, null);

        var exe = Path.Combine(DefaultDir, IPBanProvisioner.ExecutableName);
        Assert.False(result.Upgraded);
        Assert.Equal(DefaultDir, result.InstallDirectory);
        Assert.True(File.Exists(exe));
        Assert.Equal(new[] { "audit", "register:" + exe, "start" }, _host.Calls);
        Assert.Equal(new[] { "203.0.113.10" }, LoadConfig(DefaultDir).Whitelist);
    }

    [Fact]
    public void Install_Upgrade_StopsServiceKeepsConfigAndAddsOnlyMissingWhitelist()
    {
        var dir = Path.Combine(_temp.Path, "custom");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "ipban.config"), BundleBuilder.ConfigXml.Replace("value=\"\"", "value=\"198.51.100.1\""));
        File.WriteAllText(Path.Combine(dir, "DigitalRuby.IPBan.exe"), "old-exe");
        _host.ImagePath = Path.Combine(dir, "DigitalRuby.IPBan.exe");
        _host.InstalledVersion = new Version(1, 0, 0, 0);

        var result = _provisioner.Install(new IPBanSetupOptions { Whitelist = new[] { "198.51.100.1", "203.0.113.10" } }, null);

        Assert.True(result.Upgraded);
        Assert.Equal(dir, result.InstallDirectory);
        Assert.Equal("stop", _host.Calls.First());
        Assert.Equal(new[] { "198.51.100.1", "203.0.113.10" }, LoadConfig(dir).Whitelist);
        Assert.Equal("new-exe", File.ReadAllText(Path.Combine(dir, "DigitalRuby.IPBan.exe")));
    }

    [Fact]
    public void Install_UpgradeFailsToStart_RestoresOldBinariesAndRestartsService()
    {
        var dir = Path.Combine(_temp.Path, "custom");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "ipban.config"), BundleBuilder.ConfigXml);
        File.WriteAllText(Path.Combine(dir, "DigitalRuby.IPBan.exe"), "old-exe");
        _host.ImagePath = Path.Combine(dir, "DigitalRuby.IPBan.exe");
        _host.InstalledVersion = new Version(1, 0, 0, 0);
        _host.FailStartTimes = 1;

        Assert.Throws<InvalidOperationException>(() => _provisioner.Install(new IPBanSetupOptions(), null));

        Assert.Equal("old-exe", File.ReadAllText(Path.Combine(dir, "DigitalRuby.IPBan.exe")));
        Assert.Equal(new[] { "start", "stop", "start" }, _host.Calls.Skip(_host.Calls.Count - 3));
    }

    [Fact]
    public void Install_TamperedBundle_RefusesBeforeTouchingAnything()
    {
        File.AppendAllText(_zip, "x");

        Assert.Throws<ManagerException>(() => _provisioner.Install(new IPBanSetupOptions(), null));

        Assert.Empty(_host.Calls);
        Assert.False(Directory.Exists(DefaultDir));
    }

    [Fact]
    public void Install_NotAdministrator_Refuses()
    {
        _host.IsAdministrator = false;

        var ex = Assert.Throws<ManagerException>(() => _provisioner.Install(new IPBanSetupOptions(), null));
        Assert.Contains("Administrador", ex.Message);
    }

    [Fact]
    public void Install_AuditFailsAndFirewallDisabled_SucceedsWithWarnings()
    {
        _host.AuditError = "acesso negado";
        _host.DisabledProfiles.Add("Público");

        var result = _provisioner.Install(new IPBanSetupOptions(), null);

        Assert.Equal(2, result.Warnings.Count);
        Assert.Contains(result.Warnings, w => w.Contains("auditoria"));
        Assert.Contains(result.Warnings, w => w.Contains("Público"));
    }

    [Fact]
    public void Install_ReportsProgressSteps()
    {
        var steps = new System.Collections.Generic.List<string>();

        _provisioner.Install(new IPBanSetupOptions(), new SyncProgress(steps.Add));

        Assert.Contains(steps, s => s.Contains("integridade"));
        Assert.Contains(steps, s => s.Contains("Iniciando o serviço"));
    }

    private sealed class SyncProgress : IProgress<string>
    {
        private readonly Action<string> _report;
        public SyncProgress(Action<string> report) => _report = report;
        public void Report(string value) => _report(value);
    }
}

[Trait("Category", "Unit")]
public class RemoteAccessAddressProviderTests
{
    private static (IPEndPoint, IPEndPoint) Conn(int localPort, string remote) =>
        (new IPEndPoint(IPAddress.Any, localPort), new IPEndPoint(IPAddress.Parse(remote), 50000));

    [Fact]
    public void GetAdministratorAddresses_ReturnsDistinctRemotesOnRdpPortOnly()
    {
        var provider = new RemoteAccessAddressProvider(() => 3389, () => new[]
        {
            Conn(3389, "203.0.113.10"), Conn(3389, "203.0.113.10"), Conn(3389, "::ffff:198.51.100.7"),
            Conn(443, "192.0.2.1"), Conn(3389, "127.0.0.1"),
        });

        Assert.Equal(new[] { "198.51.100.7", "203.0.113.10" }, provider.GetAdministratorAddresses());
    }

    [Fact]
    public void GetAdministratorAddresses_CustomRdpPort_IsHonored()
    {
        var provider = new RemoteAccessAddressProvider(() => 40000, () => new[] { Conn(3389, "203.0.113.10"), Conn(40000, "192.0.2.5") });

        Assert.Equal(new[] { "192.0.2.5" }, provider.GetAdministratorAddresses());
    }

    [Fact]
    public void ParseExecutablePath_QuotedAndUnquoted()
    {
        Assert.Equal(@"C:\Program Files\IPBan\DigitalRuby.IPBan.exe",
            InstallationLocator.ParseExecutablePath("\"C:\\Program Files\\IPBan\\DigitalRuby.IPBan.exe\" --x"));
        Assert.Equal(@"C:\IPBan\DigitalRuby.IPBan.exe", InstallationLocator.ParseExecutablePath(@"C:\IPBan\DigitalRuby.IPBan.exe"));
        Assert.Null(InstallationLocator.ParseExecutablePath(" "));
    }
}

[Trait("Category", "Unit")]
public class DashboardPresenterVersionTests
{
    [Fact]
    public async Task RefreshAsync_OlderIPBanInstalled_OffersUpgrade()
    {
        var view = new FakeDashboardView();
        var launcher = new FakeSetupLauncher
        {
            Status = new IPBanSetupStatus
            {
                ServiceInstalled = true, BundleAvailable = true,
                InstalledVersion = new Version(1, 0, 0, 0), BundledVersion = new Version(4, 1, 0, 0),
            }
        };
        var presenter = new DashboardPresenter(view, new FakeLocator(), new FakeServiceController(), new FakeBannedProvider(),
            new FakeConfigRepository(), new RecordingLogger(), launcher);

        await presenter.RefreshAsync();

        Assert.Equal("1.0.0", view.InstalledVersion);
        Assert.Equal("4.1.0", view.UpgradeTo);
    }

    [Fact]
    public async Task RefreshAsync_UpToDate_ShowsVersionWithoutUpgrade()
    {
        var view = new FakeDashboardView();
        var launcher = new FakeSetupLauncher
        {
            Status = new IPBanSetupStatus
            {
                ServiceInstalled = true, BundleAvailable = true,
                InstalledVersion = new Version(4, 1, 0, 0), BundledVersion = new Version(4, 1, 0, 0),
            }
        };
        var presenter = new DashboardPresenter(view, new FakeLocator(), new FakeServiceController(), new FakeBannedProvider(),
            new FakeConfigRepository(), new RecordingLogger(), launcher);

        await presenter.RefreshAsync();

        Assert.Equal("4.1.0", view.InstalledVersion);
        Assert.Null(view.UpgradeTo);
    }
}
