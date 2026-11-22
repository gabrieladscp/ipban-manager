using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IPBanManager.Core.Abstractions;
using IPBanManager.Core.Errors;
using IPBanManager.Core.Provisioning;
using IPBanManager.Core.Validation;
using IPBanManager.Core.Views;

namespace IPBanManager.Core.Presenters;

/// <summary>
/// Assistente de instalação/atualização do IPBan. Vive só enquanto o diálogo está aberto
/// (não assina eventos de objetos de vida longa).
/// </summary>
public sealed class IPBanSetupPresenter
{
    private readonly IIPBanSetupView _view;
    private readonly IIPBanProvisioner _provisioner;
    private readonly IRemoteAccessAddressProvider _addresses;
    private readonly IInstallationLocator _locator;
    private readonly IAppLogger _logger;
    private bool _running;

    public IPBanSetupPresenter(IIPBanSetupView view, IIPBanProvisioner provisioner,
        IRemoteAccessAddressProvider addresses, IInstallationLocator locator, IAppLogger logger)
    {
        _view = view;
        _provisioner = provisioner;
        _addresses = addresses;
        _locator = locator;
        _logger = logger ?? NullLogger.Instance;
        _view.InstallRequested += async (s, e) => await InstallAsync();
    }

    public bool Succeeded { get; private set; }

    public void Initialize()
    {
        var status = _provisioner.Inspect();
        var version = status.BundledVersion?.ToString(3) ?? "?";
        if (status.ServiceInstalled)
        {
            _view.ShowPlan($"Atualizar o IPBan para a versão {version}",
                $"Versão instalada: {status.InstalledVersion?.ToString(3) ?? "desconhecida"}, em {status.InstallDirectory}.\n" +
                "O serviço será parado por alguns segundos e reiniciado. Configurações (ipban.config), banco de dados e " +
                "logs são preservados; os arquivos antigos ficam guardados para reversão.",
                "Atualizar");
        }
        else
        {
            _view.ShowPlan($"Instalar e configurar o IPBan {version}",
                "Será feito automaticamente:\n" +
                "  • instalar o IPBan oficial em Arquivos de Programas\\IPBan;\n" +
                "  • ativar a auditoria de logon do Windows (necessária para detectar as tentativas de acesso);\n" +
                "  • criar o serviço IPBan (início automático e reinício em caso de falha) e iniciá-lo;\n" +
                "  • incluir na whitelist os endereços abaixo, para você nunca bloquear o próprio acesso.",
                "Instalar");
        }

        IReadOnlyList<string> suggested;
        try { suggested = _addresses.GetAdministratorAddresses(); }
        catch (Exception ex)
        {
            _logger.Warn("Não foi possível detectar o endereço da sessão remota: " + ex.Message);
            suggested = new string[0];
        }
        _view.WhitelistText = string.Join(Environment.NewLine, suggested);
    }

    public async Task InstallAsync()
    {
        if (_running) return;

        var entries = (_view.WhitelistText ?? "")
            .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim()).Where(e => e.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var invalid = entries.Where(e => !IpEntry.IsListEntry(e)).ToList();
        if (invalid.Count > 0)
        {
            _view.ShowError("Entradas inválidas na whitelist: " + string.Join(", ", invalid) +
                            "\nUse um IP, uma faixa CIDR (ex.: 203.0.113.0/24) ou um nome DNS por linha.");
            return;
        }
        var wide = entries.Where(IpEntry.IsWideRange).ToList();
        if (wide.Count > 0 && !_view.Confirm(
                "As faixas " + string.Join(", ", wide) + " liberam uma quantidade enorme de endereços, que nunca serão " +
                "bloqueados (inclusive atacantes). Continuar mesmo assim?"))
            return;
        var status = _provisioner.Inspect();
        if (entries.Count == 0 && !status.ServiceInstalled && !_view.Confirm(
                "A whitelist está vazia. Se você errar a senha algumas vezes, o seu próprio IP poderá ser bloqueado e " +
                "o acesso remoto a este servidor será perdido.\n\nInstalar mesmo assim?"))
            return;

        _running = true;
        _view.Busy = true;
        try
        {
            var progress = new Progress<string>(_view.AppendProgress);
            var options = new IPBanSetupOptions { Whitelist = entries };
            var result = await Task.Run(() => _provisioner.Install(options, progress));

            Succeeded = true;
            _logger.Info($"IPBan {result.Version} {(result.Upgraded ? "atualizado" : "instalado")} em {result.InstallDirectory}.");
            PointLocatorTo(result.InstallDirectory);

            foreach (var w in result.Warnings) _view.AppendProgress("⚠ " + w);
            _view.AppendProgress(result.Upgraded
                ? $"✔ IPBan atualizado para a versão {result.Version.ToString(3)} e em execução."
                : $"✔ IPBan {result.Version.ToString(3)} instalado, configurado e em execução.");
            _view.ShowFinished(true);
        }
        catch (Exception ex)
        {
            _logger.Error("Falha ao instalar o IPBan", ex);
            var message = ErrorMessages.Translate(ex);
            _view.AppendProgress("✖ " + message);
            _view.ShowFinished(false);
            _view.ShowError("A instalação do IPBan não foi concluída:\n\n" + message);
        }
        finally
        {
            _running = false;
            _view.Busy = false;
        }
    }

    private void PointLocatorTo(string directory)
    {
        if (string.Equals(_locator.InstallDirectory, directory, StringComparison.OrdinalIgnoreCase) && _locator.IsValid)
            return;
        try { _locator.SetInstallDirectory(directory); }
        catch (ManagerException ex) { _logger.Warn("Pasta do IPBan não pôde ser selecionada: " + ex.Message); }
    }
}
