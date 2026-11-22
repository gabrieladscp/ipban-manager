using System;
using System.IO;
using System.Linq;
using IPBanManager.Core.Abstractions;
using IPBanManager.Core.Errors;
using IPBanManager.Core.Models;
using IPBanManager.Core.Provisioning;

namespace IPBanManager.Infrastructure.Provisioning;

/// <summary>
/// Instala/atualiza o IPBan oficial embutido no aplicativo e o deixa pronto para uso, na mesma sequência do script
/// oficial (install_latest.ps1): arquivos em Program Files\IPBan, auditoria de logon, serviço com início automático
/// atrasado e reinício em falha. Além disso inclui a whitelist do administrador e confere o firewall.
/// </summary>
public sealed class IPBanProvisioner : IIPBanProvisioner
{
    public const string ExecutableName = "DigitalRuby.IPBan.exe";

    private readonly IPBanBundle _bundle;
    private readonly IProvisioningHost _host;
    private readonly IPBanFileDeployer _deployer;
    private readonly Func<string, IConfigurationRepository> _configFor;
    private readonly string _serviceName;
    private readonly string _backupRoot;
    private readonly IAppLogger _logger;

    public IPBanProvisioner(IPBanBundle bundle, IProvisioningHost host, IPBanFileDeployer deployer,
        Func<string, IConfigurationRepository> configFor, string serviceName = "IPBan",
        string backupRoot = null, IAppLogger logger = null)
    {
        _bundle = bundle;
        _host = host;
        _deployer = deployer;
        _configFor = configFor;
        _serviceName = serviceName;
        _backupRoot = backupRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "IPBanManager", "ipban-backup");
        _logger = logger ?? NullLogger.Instance;
    }

    public IPBanSetupStatus Inspect()
    {
        var imagePath = _host.GetServiceImagePath(_serviceName);
        var status = new IPBanSetupStatus
        {
            BundledVersion = _bundle?.Version,
            BundleAvailable = _bundle != null && _bundle.Version != null && File.Exists(_bundle.ZipPath),
            ServiceInstalled = imagePath != null,
        };
        if (imagePath != null)
        {
            status.InstallDirectory = InstallationLocator.ParseExecutableDirectory(imagePath);
            status.InstalledVersion = _host.GetFileVersion(InstallationLocator.ParseExecutablePath(imagePath));
        }
        return status;
    }

    public IPBanSetupResult Install(IPBanSetupOptions options, IProgress<string> progress)
    {
        options = options ?? new IPBanSetupOptions();
        void Step(string text) { _logger.Info("[IPBan setup] " + text); progress?.Report(text); }

        if (!_host.IsAdministrator)
            throw new ManagerException("É preciso executar como Administrador para instalar o IPBan.");
        if (!_host.Is64BitOperatingSystem)
            throw new ManagerException("O IPBan embutido é para Windows 64 bits; este sistema é 32 bits.");

        Step("Verificando a integridade do pacote do IPBan...");
        IPBanFileDeployer.VerifyBundle(_bundle?.ZipPath, _bundle?.Sha256);

        var status = Inspect();
        var targetDir = status.ServiceInstalled && !string.IsNullOrEmpty(status.InstallDirectory)
            ? status.InstallDirectory
            : Path.Combine(_host.ProgramFilesDirectory, "IPBan");
        var result = new IPBanSetupResult { InstallDirectory = targetDir, Version = _bundle.Version, Upgraded = status.ServiceInstalled };

        if (status.ServiceInstalled)
        {
            Step("Parando o serviço IPBan...");
            _host.StopService(_serviceName);
        }

        Step($"Copiando os arquivos para {targetDir}...");
        string backup;
        try
        {
            backup = _deployer.Deploy(_bundle.ZipPath, targetDir, _backupRoot);
        }
        catch
        {
            RestartPrevious(status);
            throw;
        }

        try
        {
            var exe = Path.Combine(targetDir, ExecutableName);
            if (!File.Exists(exe))
                throw new ManagerException($"O pacote do IPBan não contém {ExecutableName}.");

            Step("Configurando a whitelist...");
            ApplyWhitelist(targetDir, options, result);

            Step("Ativando a auditoria de logon do Windows...");
            var auditError = _host.EnableLogonAuditing();
            if (auditError != null)
                result.Warnings.Add("Não foi possível ativar a auditoria de logon (o IPBan pode não detectar falhas de login): " + auditError);

            Step(status.ServiceInstalled ? "Atualizando o serviço IPBan..." : "Criando o serviço IPBan...");
            _host.RegisterService(_serviceName, exe);

            Step("Iniciando o serviço IPBan...");
            _host.StartService(_serviceName);
        }
        catch (Exception ex) when (backup != null)
        {
            // Atualização falhou depois da troca: volta os binários antigos e religa o serviço como estava.
            _logger.Error("Falha na atualização do IPBan; restaurando a versão anterior", ex);
            Step("Falhou; restaurando a versão anterior do IPBan...");
            try
            {
                _host.StopService(_serviceName);
                _deployer.Restore(backup, targetDir);
                _host.StartService(_serviceName);
            }
            catch (Exception restoreEx) { _logger.Error("A restauração do IPBan anterior também falhou", restoreEx); }
            throw;
        }

        CheckFirewall(result);
        return result;
    }

    private void ApplyWhitelist(string targetDir, IPBanSetupOptions options, IPBanSetupResult result)
    {
        var additions = options.Whitelist?.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()).ToList();
        if (additions == null || additions.Count == 0) return;

        var repo = _configFor(targetDir);
        IPBanConfiguration config = repo.Load();
        var missing = additions.Where(a => !config.Whitelist.Contains(a, StringComparer.OrdinalIgnoreCase)).ToList();
        if (missing.Count == 0) return;
        config.Whitelist.AddRange(missing);
        repo.Save(config);
        _logger.Info("Whitelist: incluídos " + string.Join(", ", missing));
    }

    private void CheckFirewall(IPBanSetupResult result)
    {
        try
        {
            var disabled = _host.GetDisabledFirewallProfiles();
            if (disabled.Count > 0)
                result.Warnings.Add("O Firewall do Windows está DESATIVADO nos perfis: " + string.Join(", ", disabled) +
                                    ". O IPBan bloqueia pelo firewall; ative-o para que os bloqueios tenham efeito.");
        }
        catch (Exception ex) { _logger.Warn("Não foi possível consultar o firewall: " + ex.Message); }
    }

    private void RestartPrevious(IPBanSetupStatus status)
    {
        if (!status.ServiceInstalled) return;
        try { _host.StartService(_serviceName); }
        catch (Exception ex) { _logger.Error("Não foi possível religar o IPBan anterior", ex); }
    }
}
