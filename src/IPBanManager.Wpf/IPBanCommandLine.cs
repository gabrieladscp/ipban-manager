using System;
using System.Collections.Generic;
using System.Linq;
using IPBanManager.Core.Errors;
using IPBanManager.Core.Provisioning;
using IPBanManager.Core.Validation;
using IPBanManager.Infrastructure;
using IPBanManager.Infrastructure.Provisioning;

namespace IPBanManager.Wpf;

/// <summary>
/// Instalação sem interface, para implantação em massa e para o instalador offline:
///   IPBanManager.exe --install-ipban [--whitelist=203.0.113.10,198.51.100.0/24]
/// Instala se não existir, atualiza se a versão instalada for mais antiga e não faz nada se já estiver em dia.
/// Sem --whitelist, usa os IPs das sessões de Área de Trabalho Remota ativas.
/// Código de saída: 0 = sucesso, 1 = falha, 2 = parâmetros inválidos. Detalhes no log do aplicativo.
/// </summary>
internal static class IPBanCommandLine
{
    public const string InstallSwitch = "--install-ipban";
    private const string WhitelistPrefix = "--whitelist=";

    public static bool IsRequested(string[] args) =>
        args.Any(a => string.Equals(a, InstallSwitch, StringComparison.OrdinalIgnoreCase));

    public static int Run(string[] args)
    {
        var logger = new FileAppLogger();
        logger.Info("Instalação do IPBan por linha de comando: " + string.Join(" ", args));
        try
        {
            var provisioner = IPBanSetupFactory.CreateProvisioner(logger);
            var status = provisioner.Inspect();
            if (status.ServiceInstalled && !status.CanUpgrade)
            {
                logger.Info($"IPBan {status.InstalledVersion} já instalado e em dia; nada a fazer.");
                return 0;
            }
            if (!status.BundleAvailable)
            {
                logger.Error("Pacote do IPBan ausente nesta instalação do IPBan Manager.");
                return 1;
            }

            var whitelist = ParseWhitelist(args, out var invalid);
            if (invalid.Count > 0)
            {
                logger.Error("Whitelist inválida: " + string.Join(", ", invalid));
                return 2;
            }

            // Os passos já vão para o log pelo provisionador.
            var result = provisioner.Install(new IPBanSetupOptions { Whitelist = whitelist }, progress: null);
            foreach (var w in result.Warnings) logger.Warn(w);
            logger.Info($"IPBan {result.Version} {(result.Upgraded ? "atualizado" : "instalado")} em {result.InstallDirectory}.");
            return 0;
        }
        catch (Exception ex)
        {
            logger.Error("Instalação do IPBan falhou: " + ErrorMessages.Translate(ex), ex);
            return 1;
        }
    }

    private static IReadOnlyList<string> ParseWhitelist(string[] args, out List<string> invalid)
    {
        invalid = new List<string>();
        var arg = args.FirstOrDefault(a => a.StartsWith(WhitelistPrefix, StringComparison.OrdinalIgnoreCase));
        if (arg == null) return new RemoteAccessAddressProvider().GetAdministratorAddresses();

        var entries = arg.Substring(WhitelistPrefix.Length)
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim()).Where(e => e.Length > 0).ToList();
        invalid.AddRange(entries.Where(e => !IpEntry.IsListEntry(e)));
        return entries;
    }
}
