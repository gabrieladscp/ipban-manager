using System;
using System.Collections.Generic;

namespace IPBanManager.Core.Provisioning;

/// <summary>Pacote oficial do IPBan distribuído junto com o aplicativo.</summary>
public sealed class IPBanBundle
{
    public IPBanBundle(string zipPath, Version version, string sha256)
    {
        ZipPath = zipPath;
        Version = version;
        Sha256 = sha256;
    }

    public string ZipPath { get; }
    public Version Version { get; }

    /// <summary>SHA-256 esperado (hex), gravado no executável na compilação.</summary>
    public string Sha256 { get; }
}

/// <summary>Situação do IPBan neste servidor em relação ao pacote embutido.</summary>
public sealed class IPBanSetupStatus
{
    public bool ServiceInstalled { get; set; }
    public string InstallDirectory { get; set; }
    public Version InstalledVersion { get; set; }
    public Version BundledVersion { get; set; }
    public bool BundleAvailable { get; set; }

    /// <summary>IPBan ausente e o aplicativo traz o pacote para instalá-lo.</summary>
    public bool CanInstall => BundleAvailable && !ServiceInstalled;

    /// <summary>IPBan instalado em versão mais antiga que a embutida.</summary>
    public bool CanUpgrade => BundleAvailable && ServiceInstalled && InstalledVersion != null
                              && BundledVersion != null && BundledVersion > InstalledVersion;
}

public sealed class IPBanSetupOptions
{
    /// <summary>Endereços acrescentados à whitelist (os já existentes são mantidos).</summary>
    public IReadOnlyList<string> Whitelist { get; set; } = new string[0];
}

public sealed class IPBanSetupResult
{
    public string InstallDirectory { get; set; }
    public Version Version { get; set; }
    public bool Upgraded { get; set; }

    /// <summary>Problemas que não impediram a instalação, mas exigem atenção (ex.: firewall desativado).</summary>
    public List<string> Warnings { get; } = new List<string>();
}

/// <summary>Instala/atualiza e configura o serviço IPBan a partir do pacote embutido.</summary>
public interface IIPBanProvisioner
{
    /// <summary>Consulta rápida (registro e versão do executável); não altera nada.</summary>
    IPBanSetupStatus Inspect();

    /// <summary>Instala ou atualiza, preservando configurações e dados. Desfaz a troca de binários em caso de falha.</summary>
    IPBanSetupResult Install(IPBanSetupOptions options, IProgress<string> progress);
}

/// <summary>Endereços de onde o administrador está conectado (sessões de Área de Trabalho Remota).</summary>
public interface IRemoteAccessAddressProvider
{
    IReadOnlyList<string> GetAdministratorAddresses();
}

/// <summary>Abre o assistente de instalação (implementado pela camada de interface).</summary>
public interface IIPBanSetupLauncher
{
    IPBanSetupStatus Inspect();

    /// <summary>Exibe o assistente; true se o IPBan foi instalado/atualizado.</summary>
    bool ShowSetup();
}
