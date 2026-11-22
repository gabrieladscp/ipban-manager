using System;
using System.Collections.Generic;
using IPBanManager.Core.Models;

namespace IPBanManager.Core.Abstractions;

/// <summary>Localiza (e permite alterar) a pasta de instalação do IPBan.</summary>
public interface IInstallationLocator
{
    string InstallDirectory { get; }
    bool IsValid { get; }
    string ServiceName { get; }
    void SetInstallDirectory(string path);
    event EventHandler InstallDirectoryChanged;
}

public interface IConfigurationRepository
{
    IPBanConfiguration Load();
    void Save(IPBanConfiguration configuration);
    string LoadRawXml();
    void SaveRawXml(string xml);
}

public interface IServiceController
{
    ServiceState GetState();
    void Start();
    void Stop();
    void Restart();
}

public interface IBannedIpProvider
{
    IReadOnlyList<BannedIp> GetBannedIps(string firewallRulePrefix);
}

/// <summary>Envia comandos de desbloqueio ao serviço (arquivo unban.txt monitorado pelo IPBan).</summary>
public interface IUnbanCommand
{
    void Unban(IEnumerable<string> addresses);
}

public interface ILogReader
{
    string ReadTail(int maxLines);
}

/// <summary>Log do próprio aplicativo (diagnóstico). Nunca deve lançar exceções.</summary>
public interface IAppLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception exception = null);
}

public sealed class NullLogger : IAppLogger
{
    public static readonly NullLogger Instance = new NullLogger();
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception exception = null) { }
}
