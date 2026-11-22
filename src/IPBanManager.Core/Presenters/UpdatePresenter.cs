using System;
using System.Threading;
using System.Threading.Tasks;
using IPBanManager.Core.Abstractions;
using IPBanManager.Core.Errors;
using IPBanManager.Core.Updates;
using IPBanManager.Core.Views;

namespace IPBanManager.Core.Presenters;

/// <summary>
/// Orquestra verificação, download, instalação e reversão. Nunca reinicia o aplicativo sem confirmação
/// e nunca com edições não salvas; uma versão baixada é aplicada no próximo início de qualquer forma.
/// </summary>
public class UpdatePresenter : IRefreshable
{
    private readonly IUpdateView _view;
    private readonly IUpdateService _service;
    private readonly IUpdateSettingsStore _settingsStore;
    private readonly IUnsavedChangesProbe _unsaved;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
    private AppUpdate _available;
    private string _promptedVersion;

    public UpdatePresenter(IUpdateView view, IUpdateService service, IUpdateSettingsStore settingsStore,
        IUnsavedChangesProbe unsaved, IAppLogger logger)
    {
        _view = view;
        _service = service;
        _settingsStore = settingsStore;
        _unsaved = unsaved;
        _logger = logger ?? NullLogger.Instance;

        _view.CheckRequested += (s, e) => OnEvent(CheckNowAsync);
        _view.InstallRequested += (s, e) => OnEvent(InstallAsync);
        _view.RollbackRequested += (s, e) => OnEvent(RollbackAsync);
        _view.SettingsChanged += (s, e) => OnEvent(SaveSettingsAsync);
    }

    public Task InitializeAsync() => RefreshAsync();

    public Task RefreshAsync()
    {
        try
        {
            _view.Settings = _settingsStore.Load();
            _view.ShowCurrent(_service.CurrentVersion, _service.InstalledChannel.ToString(), _service.IsSupported, _service.UnsupportedReason);
            _view.ShowRollback(_service.GetRollbackInfo());
            _view.ShowStatus(_service.IsSupported ? "Pronto para verificar atualizações." : _service.UnsupportedReason);
        }
        catch (Exception ex)
        {
            _logger.Error("Falha ao carregar as preferências de atualização", ex);
            _view.ShowWarning(ErrorMessages.Translate(ex));
        }
        return Task.CompletedTask;
    }

    // ---------- verificação manual ----------

    public async Task CheckNowAsync()
    {
        if (!EnsureSupported()) return;
        if (!await _gate.WaitAsync(0)) { _view.ShowInfo("Já existe uma operação de atualização em andamento."); return; }
        try
        {
            _view.Busy = true;
            _view.ShowStatus("Verificando atualizações...");
            var update = await _service.CheckAsync(CurrentSettings(), CancellationToken.None);
            ShowCheckResult(update);
        }
        catch (Exception ex)
        {
            _logger.Error("Falha ao verificar atualizações", ex);
            _view.ShowStatus("Não foi possível verificar atualizações.");
            _view.ShowError(ErrorMessages.Translate(ex));
        }
        finally
        {
            _view.Busy = false;
            _gate.Release();
        }
    }

    private void ShowCheckResult(AppUpdate update)
    {
        _available = update;
        _view.ShowAvailable(update);
        _view.ShowStatus(update == null
            ? $"Você já está na versão mais recente ({_service.CurrentVersion})."
            : update.IsDowngrade
                ? $"A versão {update.Version} está disponível no canal selecionado (anterior à atual)."
                : $"Nova versão disponível: {update.Version}.");
    }

    // ---------- instalação ----------

    public async Task InstallAsync()
    {
        if (!EnsureSupported()) return;
        var update = _available;
        if (update == null) { _view.ShowInfo("Verifique as atualizações primeiro."); return; }
        if (_unsaved.HasUnsavedChanges)
        {
            _view.ShowError("Há alterações não salvas. Salve ou descarte-as antes de atualizar, pois o aplicativo será reiniciado.");
            return;
        }

        // O gate é obtido ANTES do diálogo: enquanto o usuário decide, a verificação automática em segundo plano
        // não pode iniciar outro download do mesmo pacote.
        if (!await _gate.WaitAsync(0)) { _view.ShowInfo("Já existe uma operação de atualização em andamento."); return; }
        try
        {
            var question = update.IsDowngrade
                ? $"Instalar a versão {update.Version} (mais antiga que a atual {_service.CurrentVersion})? Suas configurações e dados serão mantidos."
                : $"Baixar e instalar a versão {update.Version}? O aplicativo será reiniciado. Suas configurações e dados serão mantidos.";
            if (!_view.Confirm(question)) return;

            _view.Busy = true;
            await DownloadWithProgressAsync(update);
        }
        catch (Exception ex)
        {
            _logger.Error("Falha ao baixar a atualização", ex);
            _view.ShowProgress(-1);
            _view.ShowStatus("Falha ao baixar a atualização. Nada foi alterado.");
            _view.ShowError(ErrorMessages.Translate(ex));
            return;
        }
        finally
        {
            _view.Busy = false;
            _gate.Release();
        }

        _logger.Info($"Aplicando atualização para {update.Version}.");
        _service.ApplyAndRestart();
    }

    private async Task DownloadWithProgressAsync(AppUpdate update)
    {
        _view.ShowStatus($"Baixando a versão {update.Version}...");
        _view.ShowProgress(0);
        var progress = new Progress<int>(p => _view.ShowProgress(p));
        await _service.DownloadAsync(update, progress, CancellationToken.None);
        _view.ShowProgress(100);
        _view.ShowStatus($"Versão {update.Version} baixada e verificada.");
    }

    // ---------- verificação automática (segundo plano) ----------

    /// <summary>Chamada periodicamente pelo agendador. Erros são apenas registrados em log.</summary>
    public async Task AutoCheckAsync()
    {
        try
        {
            var settings = CurrentSettings();
            if (!_service.IsSupported || !settings.AutoCheck) return;
            if (!await _gate.WaitAsync(0)) return;
            try
            {
                var update = await _service.CheckAsync(settings, CancellationToken.None);
                ShowCheckResult(update);
                if (update == null || update.IsDowngrade) return; // reversões de versão só por ação do usuário

                if (!settings.AutoDownload)
                {
                    _view.ShowInfo($"Nova versão {update.Version} disponível (aba Atualizações).");
                    return;
                }

                await DownloadWithProgressAsync(update);
            }
            finally { _gate.Release(); }

            await OfferRestartAsync(_available);
        }
        catch (Exception ex)
        {
            // Rede indisponível, limite da API do GitHub etc.: não incomoda o usuário.
            _logger.Warn("Verificação automática de atualização falhou: " + ex.Message);
            _view.ShowProgress(-1);
        }
    }

    private Task OfferRestartAsync(AppUpdate update)
    {
        if (update == null || _promptedVersion == update.Version) return Task.CompletedTask;
        _promptedVersion = update.Version;

        if (_unsaved.HasUnsavedChanges)
        {
            _view.ShowInfo($"Versão {update.Version} baixada. Será instalada na próxima abertura (há alterações não salvas).");
            return Task.CompletedTask;
        }
        if (_view.Confirm($"A versão {update.Version} foi baixada e verificada. Reiniciar agora para instalar?\n\n" +
                          "Se preferir, ela será instalada automaticamente na próxima vez que o aplicativo for aberto."))
        {
            _logger.Info($"Aplicando atualização para {update.Version} (confirmada pelo usuário).");
            _service.ApplyAndRestart();
        }
        return Task.CompletedTask;
    }

    // ---------- reversão manual ----------

    public async Task RollbackAsync()
    {
        var info = _service.GetRollbackInfo();
        if (info == null) { _view.ShowInfo("Não há versão anterior preservada."); return; }
        if (_unsaved.HasUnsavedChanges)
        {
            _view.ShowError("Há alterações não salvas. Salve ou descarte-as antes de reverter.");
            return;
        }
        if (!_view.Confirm($"Voltar para a versão {info.Version}? O aplicativo será reiniciado. Suas configurações e dados serão mantidos.")) return;

        _logger.Info($"Reversão manual para {info.Version} solicitada.");
        await Task.Run(() => _service.RollbackAndRestart(_service.CurrentVersion));
    }

    // ---------- preferências ----------

    private async Task SaveSettingsAsync()
    {
        var settings = _view.Settings;
        if (settings.CheckIntervalHours < 1) settings.CheckIntervalHours = 1;
        await Task.Run(() => _settingsStore.Save(settings));
        _available = null;
        _view.ShowAvailable(null);
        _view.ShowStatus(settings.Channel == UpdateChannel.Beta
            ? "Canal Beta ativado: você receberá versões de teste, que podem conter falhas."
            : "Canal Stable ativado: somente versões estáveis.");
        _view.ShowInfo("Preferências de atualização salvas.");
    }

    private UpdateSettings CurrentSettings() => _settingsStore.Load();

    private bool EnsureSupported()
    {
        if (_service.IsSupported) return true;
        _view.ShowWarning(_service.UnsupportedReason);
        return false;
    }

    private async void OnEvent(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex)
        {
            _logger.Error("Falha em UpdatePresenter", ex);
            _view.ShowError(ErrorMessages.Translate(ex));
        }
    }
}
