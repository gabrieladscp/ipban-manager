using System.Linq;
using System.Threading.Tasks;
using IPBanManager.Core.Abstractions;
using IPBanManager.Core.Validation;
using IPBanManager.Core.Views;

namespace IPBanManager.Core.Presenters;

/// <summary>Carrega/valida/salva uma seção do ipban.config. O IPBan recarrega o arquivo sozinho ao ser alterado.</summary>
public class ConfigSectionPresenter<TView> : PresenterBase<TView> where TView : IConfigSectionView
{
    private readonly IConfigurationRepository _repository;
    private readonly ConfigurationValidator _validator;

    public ConfigSectionPresenter(TView view, IInstallationLocator locator, IConfigurationRepository repository,
        ConfigurationValidator validator, IAppLogger logger) : base(view, locator, logger)
    {
        _repository = repository;
        _validator = validator;
        View.SaveRequested += (s, e) => OnEvent(SaveAsync);
        View.ReloadRequested += (s, e) => OnEvent(RefreshAsync);
    }

    public override async Task RefreshAsync()
    {
        if (!Locator.IsValid) return;
        var configuration = await Task.Run(() => _repository.Load());
        View.Display(configuration);
    }

    public async Task SaveAsync()
    {
        if (!Locator.IsValid)
        {
            View.ShowError("Selecione primeiro a pasta de instalação do IPBan.");
            return;
        }

        var configuration = await Task.Run(() => _repository.Load());
        View.ApplyTo(configuration);

        var errors = _validator.Validate(configuration);
        if (errors.Any())
        {
            View.ShowError("Corrija os problemas antes de salvar:\n\n• " + string.Join("\n• ", errors));
            return;
        }

        await Task.Run(() => _repository.Save(configuration));
        Logger.Info("Configuração salva.");
        View.MarkSaved();
        View.ShowInfo("Configuração salva. O serviço IPBan aplica as mudanças automaticamente.");
    }
}
