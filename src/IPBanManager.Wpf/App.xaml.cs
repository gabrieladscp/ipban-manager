using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using IPBanManager.Core.Abstractions;
using IPBanManager.Core.Presenters;
using IPBanManager.Core.Updates;
using IPBanManager.Core.Validation;
using IPBanManager.Core.Views;
using IPBanManager.Infrastructure;
using IPBanManager.Infrastructure.Updates;
using IPBanManager.Wpf.Views;
using Velopack.Locators;

namespace IPBanManager.Wpf;

/// <summary>Composition Root: único lugar que conhece as implementações concretas.</summary>
public partial class App : Application
{
    /// <summary>Tempo de funcionamento estável após o qual a versão é considerada saudável.</summary>
    private static readonly TimeSpan HealthyAfter = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FirstUpdateCheckDelay = TimeSpan.FromSeconds(45);

    private Mutex _singleInstance;
    private IAppLogger _logger = NullLogger.Instance;
    private object[] _presenters;
    private UpdateHealthGuard _guard;
    private VelopackUpdateService _updateService;
    private UpdatePresenter _updatePresenter;
    private UpdateSettingsAccessor _updateSettings;
    private DispatcherTimer _healthTimer;
    private DispatcherTimer _updateTimer;
    private bool _startedOk;
    private bool _healthy;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _logger = new FileAppLogger();
        RegisterGlobalExceptionHandlers();

        _singleInstance = new Mutex(true, @"Global\IPBanManager.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("O IPBan Manager já está em execução.", "IPBan Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            _singleInstance = null;
            Shutdown();
            return;
        }

        try
        {
            _logger.Info("Aplicativo iniciado, versão " + AppVersion.Current);
            ComposeUpdates();
            if (RollbackIfUnhealthy()) return; // o processo é reiniciado pelo instalador

            var refreshes = Compose();
            await Task.WhenAll(refreshes);
            _startedOk = true;
            StartHealthMonitoring();
            StartUpdateScheduler();
        }
        catch (Exception ex)
        {
            _logger.Error("Falha na inicialização", ex);
            MessageBox.Show("Não foi possível iniciar o IPBan Manager:\n\n" + ex.Message + "\n\nDetalhes: " + FileAppLogger.DefaultPath,
                "IPBan Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    // ------------------------------------------------------------------ atualização automática

    private void ComposeUpdates()
    {
        var stateStore = new FileUpdateStateStore(_logger);
        var packages = new RollbackPackageStore(
            () => VelopackLocator.IsCurrentSet ? VelopackLocator.Current.PackagesDir : null,
            UpdateIdentity.PackId, _logger);
        _guard = new UpdateHealthGuard(stateStore, packages, _logger);
        _updateService = new VelopackUpdateService(RepositoryUrl(), _guard, packages, _logger, () => AppVersion.Current);
        _updateSettings = new UpdateSettingsAccessor(new FileUpdateSettingsStore(_logger));
    }

    /// <summary>Se a versão recém-instalada falhou repetidamente, volta para a última versão boa antes de abrir a janela.</summary>
    private bool RollbackIfUnhealthy()
    {
        if (!_updateService.IsSupported) return false; // desenvolvimento / instalação que não é do Velopack

        var decision = _guard.EvaluateStartup(_updateService.CurrentVersion);
        if (decision.Action != StartupAction.Rollback) return false;

        try
        {
            _logger.Warn($"Reversão automática: {decision.FailedVersion} -> {decision.TargetVersion}");
            _updateService.RollbackAndRestart(decision.FailedVersion);
            return true;
        }
        catch (Exception ex)
        {
            // Sem como reverter: segue com a versão atual em vez de deixar o usuário sem aplicativo.
            _logger.Error("A reversão automática falhou; seguindo com a versão atual", ex);
            return false;
        }
    }

    private void StartHealthMonitoring()
    {
        if (!_updateService.IsSupported) return;
        _healthTimer = new DispatcherTimer { Interval = HealthyAfter };
        _healthTimer.Tick += (s, e) =>
        {
            _healthTimer.Stop();
            MarkHealthy();
        };
        _healthTimer.Start();
    }

    private void MarkHealthy()
    {
        if (_healthy || !_updateService.IsSupported) return;
        _healthy = true;
        try
        {
            if (_guard.MarkHealthy(_updateService.CurrentVersion))
                _logger.Info($"Versão {_updateService.CurrentVersion} confirmada como saudável.");
        }
        catch (Exception ex) { _logger.Warn("Não foi possível registrar a saúde da versão: " + ex.Message); }
    }

    private void StartUpdateScheduler()
    {
        if (!_updateService.IsSupported) return;
        _updateTimer = new DispatcherTimer { Interval = FirstUpdateCheckDelay };
        _updateTimer.Tick += async (s, e) =>
        {
            _updateTimer.Interval = TimeSpan.FromHours(Math.Max(1, _updateSettings.Current.CheckIntervalHours));
            await _updatePresenter.AutoCheckAsync();
        };
        _updateTimer.Start();
    }

    private static string RepositoryUrl() =>
        typeof(App).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "UpdateRepoUrl")?.Value;

    // ------------------------------------------------------------------ composição das telas

    private Task[] Compose()
    {
        var locator = new InstallationLocator();
        var config = new XmlConfigurationRepository(locator);
        var service = new WindowsServiceController(locator);
        var banned = new FirewallBannedIpProvider();
        var validator = new ConfigurationValidator();

        var window = new MainWindow();
        var dashboard = new DashboardView();
        var settings = new SettingsView();
        var lists = new ListsView();
        var actions = new ActionsView();
        var bannedView = new BannedIpsView();
        var advanced = new AdvancedView();
        var logView = new LogView();
        var updatesView = new UpdatesView();

        _updatePresenter = new UpdatePresenter(updatesView, _updateService, _updateSettings.Store, window, _logger);

        // Presenters ficam referenciados aqui (os eventos das Views também os mantêm vivos).
        _presenters = new object[]
        {
            new MainPresenter(window, locator, _logger),
            new DashboardPresenter(dashboard, locator, service, banned, config, _logger),
            new ConfigSectionPresenter<ISettingsView>(settings, locator, config, validator, _logger),
            new ConfigSectionPresenter<IListsView>(lists, locator, config, validator, _logger),
            new ConfigSectionPresenter<IActionsView>(actions, locator, config, validator, _logger),
            new BannedIpsPresenter(bannedView, locator, banned, config, new UnbanFileCommand(locator), _logger),
            new AdvancedPresenter(advanced, locator, config, _logger),
            new LogPresenter(logView, locator, new LogFileReader(locator), _logger),
            _updatePresenter,
        };

        window.AddTab("Painel", dashboard);
        window.AddTab("Configurações", settings);
        window.AddTab("Listas (Whitelist/Blacklist)", lists);
        window.AddTab("Regras e ações", actions);
        window.AddTab("IPs bloqueados", bannedView);
        window.AddTab("Avançado (XML)", advanced);
        window.AddTab("Logs", logView);
        window.AddTab("Atualizações", updatesView);
        window.Show();

        // InitializeAsync nunca lança: falhas viram aviso na barra de status.
        return _presenters.Cast<IRefreshable>().Select(p => p.InitializeAsync()).ToArray();
    }

    // ------------------------------------------------------------------ tratamento de falhas

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (s, e) =>
        {
            _logger.Error("Exceção não tratada na interface", e.Exception);
            NoteCrashBeforeHealthy();
            MessageBox.Show("Ocorreu um erro inesperado, mas o aplicativo continua em execução:\n\n" + e.Exception.Message
                + "\n\nDetalhes: " + FileAppLogger.DefaultPath, "IPBan Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            _logger.Error("Exceção fatal não tratada", e.ExceptionObject as Exception);
            NoteCrashBeforeHealthy();
        };
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            _logger.Error("Exceção de tarefa não observada", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>Falha grave logo após uma atualização: o próximo início reverte para a versão anterior.</summary>
    private void NoteCrashBeforeHealthy()
    {
        if (_healthy || _guard == null || _updateService == null || !_updateService.IsSupported) return;
        try { _guard.RecordCrash(_updateService.CurrentVersion); }
        catch (Exception ex) { _logger.Warn("Não foi possível registrar a falha: " + ex.Message); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Encerramento normal depois de iniciar com sucesso também comprova a versão (evita reverter por aberturas curtas).
        if (_startedOk) MarkHealthy();
        _logger.Info("Aplicativo encerrado.");
        try { _singleInstance?.ReleaseMutex(); } catch (ApplicationException) { }
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Lê as preferências de atualização do disco a cada uso (podem ser alteradas na aba).</summary>
    private sealed class UpdateSettingsAccessor
    {
        public UpdateSettingsAccessor(IUpdateSettingsStore store) => Store = store;
        public IUpdateSettingsStore Store { get; }
        public UpdateSettings Current => Store.Load();
    }
}
