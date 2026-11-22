using System;
using Velopack;

namespace IPBanManager.Wpf;

/// <summary>
/// Ponto de entrada. O Velopack precisa rodar antes de qualquer outra coisa: ele trata os parâmetros especiais
/// usados na instalação/atualização/desinstalação (atalhos, hooks) e aplica uma atualização já baixada.
/// Em execução normal (sem esses parâmetros) o Run() retorna imediatamente e a aplicação WPF segue como antes.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        VelopackApp.Build()
            .SetAutoApplyOnStartup(true) // versão já baixada é instalada na próxima abertura
            .Run();

        // Instalação do IPBan sem interface (implantação em massa / instalador offline).
        if (IPBanCommandLine.IsRequested(args)) return IPBanCommandLine.Run(args);

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
