using System.Threading.Tasks;
using System.Xml;
using IPBanManager.Core.Abstractions;
using IPBanManager.Core.Views;

namespace IPBanManager.Core.Presenters;

/// <summary>Editor do XML bruto (expressões de Event Viewer, arquivos de log etc.).</summary>
public class AdvancedPresenter : PresenterBase<IAdvancedView>
{
    private readonly IConfigurationRepository _repository;
    private string _loadedSnapshot;

    public AdvancedPresenter(IAdvancedView view, IInstallationLocator locator, IConfigurationRepository repository,
        IAppLogger logger) : base(view, locator, logger)
    {
        _repository = repository;
        View.SaveRequested += (s, e) => OnEvent(SaveAsync);
        View.ReloadRequested += (s, e) => OnEvent(RefreshAsync);
    }

    public override async Task RefreshAsync()
    {
        if (!Locator.IsValid) return;
        _loadedSnapshot = await Task.Run(() => _repository.LoadRawXml());
        View.Xml = _loadedSnapshot;
    }

    public async Task SaveAsync()
    {
        try { new XmlDocument().LoadXml(View.Xml); }
        catch (XmlException ex)
        {
            View.ShowError("XML inválido, nada foi salvo: " + ex.Message);
            return;
        }

        // O arquivo pode ter sido alterado (pela aba de configurações ou por outra pessoa) desde que foi carregado aqui.
        var current = await Task.Run(() => _repository.LoadRawXml());
        if (_loadedSnapshot != null && current != _loadedSnapshot &&
            !View.Confirm("O ipban.config foi alterado depois que esta tela foi carregada. Salvar agora vai sobrescrever essas alterações. Continuar?"))
            return;

        if (!View.Confirm("Salvar o XML bruto? Um backup do arquivo atual será criado.")) return;

        await Task.Run(() => _repository.SaveRawXml(View.Xml));
        _loadedSnapshot = await Task.Run(() => _repository.LoadRawXml());
        Logger.Info("XML bruto salvo.");
        View.ShowInfo("Arquivo salvo.");
    }
}
