using System;
using System.Collections.Generic;
using System.Linq;
using IPBanManager.Core.Abstractions;
using IPBanManager.Core.Models;

namespace IPBanManager.Tests.Support;

internal sealed class FakeLocator : IInstallationLocator
{
    public FakeLocator(string directory = "C:\\fake", bool isValid = true)
    {
        InstallDirectory = directory;
        IsValid = isValid;
    }

    public string InstallDirectory { get; private set; }
    public bool IsValid { get; set; }
    public string ServiceName => "IPBan";
    public event EventHandler InstallDirectoryChanged;

    public void SetInstallDirectory(string path)
    {
        if (SetException != null) throw SetException;
        InstallDirectory = path;
        InstallDirectoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public Exception SetException { get; set; }
}

/// <summary>Repositório em memória; Load/Save trabalham com cópias, como o arquivo real.</summary>
internal sealed class FakeConfigRepository : IConfigurationRepository
{
    public IPBanConfiguration Stored { get; set; } = new IPBanConfiguration();
    public string RawXml { get; set; } = "<configuration/>";
    public int SaveCount { get; private set; }
    public int RawSaveCount { get; private set; }
    public Exception LoadException { get; set; }

    public IPBanConfiguration Load()
    {
        if (LoadException != null) throw LoadException;
        return Clone(Stored);
    }

    public void Save(IPBanConfiguration configuration)
    {
        Stored = Clone(configuration);
        SaveCount++;
    }

    public string LoadRawXml() => RawXml;

    public void SaveRawXml(string xml)
    {
        RawXml = xml;
        RawSaveCount++;
    }

    private static IPBanConfiguration Clone(IPBanConfiguration c) => new IPBanConfiguration
    {
        FailedLoginAttemptsBeforeBan = c.FailedLoginAttemptsBeforeBan,
        FirewallRulePrefix = c.FirewallRulePrefix,
        Whitelist = c.Whitelist.ToList(),
        Blacklist = c.Blacklist.ToList(),
    };
}

internal sealed class FakeServiceController : IServiceController
{
    public ServiceState State { get; set; } = ServiceState.Running;
    public Exception StartException { get; set; }
    public int StartCalls { get; private set; }
    public int StopCalls { get; private set; }

    /// <summary>Se definido, GetState bloqueia até o gate ser liberado (simula consulta lenta ao serviço).</summary>
    public System.Threading.ManualResetEventSlim Gate { get; set; }

    public ServiceState GetState()
    {
        System.Threading.Interlocked.Increment(ref _getStateCalls);
        Gate?.Wait(TimeSpan.FromSeconds(5));
        return State;
    }

    private int _getStateCalls;
    public int GetStateCalls => _getStateCalls;

    public void Start()
    {
        StartCalls++;
        if (StartException != null) throw StartException;
        State = ServiceState.Running;
    }

    public void Stop()
    {
        StopCalls++;
        State = ServiceState.Stopped;
    }

    public void Restart()
    {
        Stop();
        Start();
    }
}

internal sealed class FakeBannedProvider : IBannedIpProvider
{
    public List<BannedIp> Items { get; } = new List<BannedIp> { new BannedIp("1.1.1.1", "IPBan_Block_0", "Automático") };
    public int Calls { get; private set; }

    public IReadOnlyList<BannedIp> GetBannedIps(string firewallRulePrefix)
    {
        Calls++;
        return Items;
    }
}

internal sealed class FakeUnbanCommand : IUnbanCommand
{
    public List<string> Sent { get; } = new List<string>();
    public void Unban(IEnumerable<string> addresses) => Sent.AddRange(addresses);
}

internal sealed class FakeLogReader : ILogReader
{
    public string Text { get; set; } = "";
    public string ReadTail(int maxLines) => Text;
}

internal sealed class RecordingLogger : IAppLogger
{
    public List<string> Entries { get; } = new List<string>();
    public void Info(string message) => Entries.Add("INFO " + message);
    public void Warn(string message) => Entries.Add("WARN " + message);
    public void Error(string message, Exception exception = null) => Entries.Add("ERROR " + message);
}
