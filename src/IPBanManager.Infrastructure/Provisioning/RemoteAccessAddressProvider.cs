using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using IPBanManager.Core.Abstractions;
using IPBanManager.Core.Provisioning;
using Microsoft.Win32;

namespace IPBanManager.Infrastructure.Provisioning;

/// <summary>
/// Descobre de onde o administrador acessa o servidor: os endereços remotos das conexões TCP estabelecidas na porta
/// da Área de Trabalho Remota. É o endereço que o IPBan enxerga (já com NAT aplicado), diferente do IP que o cliente
/// RDP informa sobre si mesmo.
/// </summary>
public sealed class RemoteAccessAddressProvider : IRemoteAccessAddressProvider
{
    public const int DefaultRdpPort = 3389;
    private readonly Func<int> _rdpPort;
    private readonly Func<IEnumerable<(IPEndPoint Local, IPEndPoint Remote)>> _connections;

    public RemoteAccessAddressProvider() : this(ReadRdpPort, ActiveConnections) { }

    public RemoteAccessAddressProvider(Func<int> rdpPort, Func<IEnumerable<(IPEndPoint Local, IPEndPoint Remote)>> connections)
    {
        _rdpPort = rdpPort;
        _connections = connections;
    }

    public IReadOnlyList<string> GetAdministratorAddresses()
    {
        int port = _rdpPort();
        return _connections()
            .Where(c => c.Local.Port == port)
            .Select(c => Normalize(c.Remote.Address))
            .Where(a => !IPAddress.IsLoopback(a) && !a.Equals(IPAddress.Any) && !a.Equals(IPAddress.IPv6Any))
            .Select(a => a.ToString())
            .Distinct()
            .OrderBy(a => a, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>IPv4 mapeado em IPv6 (::ffff:1.2.3.4) vira IPv4, que é como o IPBan registra.</summary>
    public static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private static IEnumerable<(IPEndPoint, IPEndPoint)> ActiveConnections() =>
        IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections()
            .Where(c => c.State == TcpState.Established)
            .Select(c => (c.LocalEndPoint, c.RemoteEndPoint));

    private static int ReadRdpPort()
    {
        try
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp"))
                return key?.GetValue("PortNumber") is int port && port > 0 ? port : DefaultRdpPort;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException)
        {
            return DefaultRdpPort;
        }
    }
}

/// <summary>Localizador fixo numa pasta (usado para editar a configuração recém-instalada).</summary>
public sealed class FixedInstallationLocator : IInstallationLocator
{
    public FixedInstallationLocator(string directory, string serviceName = "IPBan")
    {
        InstallDirectory = directory;
        ServiceName = serviceName;
    }

    public string InstallDirectory { get; }
    public string ServiceName { get; }
    public bool IsValid => System.IO.File.Exists(System.IO.Path.Combine(InstallDirectory, InstallationLocator.ConfigFileName));
    public void SetInstallDirectory(string path) => throw new NotSupportedException();
    public event EventHandler InstallDirectoryChanged { add { } remove { } }
}
