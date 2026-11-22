using System;
using System.Collections.Generic;
using IPBanManager.Core.Models;

namespace IPBanManager.Core.Views;

/// <summary>Contrato mínimo de toda View passiva: apenas exibe dados e dispara eventos.</summary>
public interface IView
{
    /// <summary>Erro de uma ação do usuário (modal).</summary>
    void ShowError(string message);

    /// <summary>Confirmação de sucesso (não intrusiva).</summary>
    void ShowInfo(string message);

    /// <summary>Problema em segundo plano, como falha de atualização (não intrusivo).</summary>
    void ShowWarning(string message);

    bool Confirm(string question);
}

public interface IMainView : IView
{
    string InstallDirectory { set; }
    string InstallStatus { set; }
    bool InstallValid { set; }
    string PickDirectory(string current);
    event EventHandler ChangeDirectoryRequested;

    /// <summary>Exibe o botão "Instalar IPBan" (IPBan ausente e pacote disponível).</summary>
    bool CanInstallIPBan { set; }
    event EventHandler InstallIPBanRequested;
}

/// <summary>Assistente que instala/atualiza e configura o IPBan.</summary>
public interface IIPBanSetupView : IView
{
    void ShowPlan(string heading, string description, string actionLabel);

    /// <summary>Endereços da whitelist, um por linha.</summary>
    string WhitelistText { get; set; }

    void AppendProgress(string line);
    bool Busy { set; }

    /// <summary>Fim da execução: troca os botões por "Fechar".</summary>
    void ShowFinished(bool success);

    event EventHandler InstallRequested;
}

public interface IDashboardView : IView
{
    void ShowState(ServiceState state, string text);
    void ShowBannedCount(int count);
    bool Busy { set; }
    event EventHandler StartRequested;
    event EventHandler StopRequested;
    event EventHandler RestartRequested;

    /// <summary>Atualização completa (botão, abertura da aba).</summary>
    event EventHandler RefreshRequested;

    /// <summary>Verificação periódica e leve (só o status do serviço).</summary>
    event EventHandler PollRequested;

    /// <summary>Versão do IPBan; <paramref name="upgradeTo"/> não nulo exibe o botão de atualização.</summary>
    void ShowIPBanVersion(string installed, string upgradeTo);
    event EventHandler UpgradeIPBanRequested;
}

/// <summary>Seção da configuração: a View lê/escreve somente os campos que lhe pertencem.</summary>
public interface IConfigSectionView : IView
{
    void Display(IPBanConfiguration configuration);
    void ApplyTo(IPBanConfiguration configuration);
    void MarkSaved();
    event EventHandler SaveRequested;
    event EventHandler ReloadRequested;
}

public interface ISettingsView : IConfigSectionView { }
public interface IListsView : IConfigSectionView { }
public interface IActionsView : IConfigSectionView { }

public interface IBannedIpsView : IView
{
    void ShowBannedIps(IReadOnlyList<BannedIp> items);
    IReadOnlyList<string> SelectedAddresses { get; }
    string ManualBanAddress { get; set; }
    event EventHandler RefreshRequested;
    event EventHandler BanRequested;
    event EventHandler UnbanRequested;
}

public interface IAdvancedView : IView
{
    string Xml { get; set; }
    event EventHandler SaveRequested;
    event EventHandler ReloadRequested;
}

public interface IUpdateView : IView
{
    /// <summary>Preferências exibidas/editadas pelo usuário (canal, verificação automática).</summary>
    IPBanManager.Core.Updates.UpdateSettings Settings { get; set; }

    void ShowCurrent(string version, string installedChannel, bool supported, string unsupportedReason);
    void ShowStatus(string text);

    /// <summary>Exibe a versão disponível; null limpa o painel.</summary>
    void ShowAvailable(IPBanManager.Core.Updates.AppUpdate update);

    /// <summary>Progresso do download (0-100); valor negativo oculta a barra.</summary>
    void ShowProgress(int percent);

    /// <summary>Exibe a opção de voltar à versão anterior; null oculta.</summary>
    void ShowRollback(IPBanManager.Core.Updates.RollbackInfo info);

    bool Busy { set; }

    event EventHandler CheckRequested;
    event EventHandler InstallRequested;
    event EventHandler RollbackRequested;
    event EventHandler SettingsChanged;
}

public interface ILogView : IView
{
    string LogText { set; }
    string Filter { get; }
    event EventHandler RefreshRequested;
}
