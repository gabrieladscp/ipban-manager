using System.Threading.Tasks;
using IPBanManager.Core.Presenters;
using IPBanManager.Tests.Support;
using Xunit;

namespace IPBanManager.Tests.Presenters;

[Trait("Category", "Unit")]
public class BannedIpsPresenterTests
{
    private readonly FakeConfigRepository _repository = new FakeConfigRepository();
    private readonly FakeBannedIpsView _view = new FakeBannedIpsView();
    private readonly FakeUnbanCommand _unban = new FakeUnbanCommand();
    private readonly FakeBannedProvider _provider = new FakeBannedProvider();
    private readonly BannedIpsPresenter _presenter;

    public BannedIpsPresenterTests()
    {
        _presenter = new BannedIpsPresenter(_view, new FakeLocator(), _provider, _repository, _unban, new RecordingLogger());
    }

    // ---------- bloqueio manual ----------

    [Fact]
    public async Task BanAsync_ValidAddress_AddsToBlacklistAndClearsInput()
    {
        _view.ManualBanAddress = "203.0.113.5";

        await _presenter.BanAsync();

        Assert.Equal(new[] { "203.0.113.5" }, _repository.Stored.Blacklist);
        Assert.Equal("", _view.ManualBanAddress);
        Assert.Single(_view.Infos);
    }

    [Fact]
    public async Task BanAsync_AddressAlreadyBlacklisted_DoesNotDuplicate()
    {
        _repository.Stored.Blacklist.Add("203.0.113.5");
        _view.ManualBanAddress = "203.0.113.5";

        await _presenter.BanAsync();

        Assert.Single(_repository.Stored.Blacklist);
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("1.2.3")]
    [InlineData("")]
    public async Task BanAsync_InvalidAddress_ShowsErrorAndDoesNotSave(string input)
    {
        _view.ManualBanAddress = input;

        await _presenter.BanAsync();

        Assert.Single(_view.Errors);
        Assert.Equal(0, _repository.SaveCount);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("0.0.0.0")]
    public async Task BanAsync_LoopbackOrUnspecified_IsRefused(string input)
    {
        _view.ManualBanAddress = input;

        await _presenter.BanAsync();

        Assert.Contains("loopback", _view.Errors[0]);
        Assert.Equal(0, _repository.SaveCount);
    }

    [Fact]
    public async Task BanAsync_WideRangeNotConfirmed_DoesNotSave()
    {
        _view.ManualBanAddress = "10.0.0.0/8";
        _view.ConfirmAnswer = false;

        await _presenter.BanAsync();

        Assert.Single(_view.Questions);
        Assert.Contains("muito ampla", _view.Questions[0]);
        Assert.Equal(0, _repository.SaveCount);
    }

    [Fact]
    public async Task BanAsync_WideRangeConfirmed_Saves()
    {
        _view.ManualBanAddress = "10.0.0.0/8";

        await _presenter.BanAsync();

        Assert.Equal(new[] { "10.0.0.0/8" }, _repository.Stored.Blacklist);
    }

    [Fact]
    public async Task BanAsync_AddressInWhitelistDeclined_KeepsWhitelistUntouched()
    {
        _repository.Stored.Whitelist.Add("8.8.8.8");
        _view.ManualBanAddress = "8.8.8.8";
        _view.ConfirmAnswer = false;

        await _presenter.BanAsync();

        Assert.Equal(new[] { "8.8.8.8" }, _repository.Stored.Whitelist);
        Assert.Empty(_repository.Stored.Blacklist);
    }

    [Fact]
    public async Task BanAsync_AddressInWhitelistAccepted_MovesItToBlacklist()
    {
        _repository.Stored.Whitelist.Add("8.8.8.8");
        _view.ManualBanAddress = "8.8.8.8";

        await _presenter.BanAsync();

        Assert.Empty(_repository.Stored.Whitelist);
        Assert.Equal(new[] { "8.8.8.8" }, _repository.Stored.Blacklist);
    }

    // ---------- desbloqueio ----------

    [Fact]
    public async Task UnbanAsync_SelectedAddress_SendsCommand()
    {
        _view.SelectedAddresses = new[] { "1.1.1.1" };

        await _presenter.UnbanAsync();

        Assert.Equal(new[] { "1.1.1.1" }, _unban.Sent);
    }

    [Fact]
    public async Task UnbanAsync_AddressInBlacklist_AlsoRemovesItFromBlacklist()
    {
        _repository.Stored.Blacklist.Add("1.1.1.1");
        _repository.Stored.Blacklist.Add("2.2.2.2");
        _view.SelectedAddresses = new[] { "1.1.1.1" };

        await _presenter.UnbanAsync();

        Assert.Equal(new[] { "2.2.2.2" }, _repository.Stored.Blacklist);
    }

    [Fact]
    public async Task UnbanAsync_NothingSelected_OnlyInforms()
    {
        await _presenter.UnbanAsync();

        Assert.Empty(_unban.Sent);
        Assert.Single(_view.Infos);
    }

    [Fact]
    public async Task UnbanAsync_NotConfirmed_SendsNothing()
    {
        _view.SelectedAddresses = new[] { "1.1.1.1" };
        _view.ConfirmAnswer = false;

        await _presenter.UnbanAsync();

        Assert.Empty(_unban.Sent);
    }

    // ---------- listagem / eventos ----------

    [Fact]
    public async Task RefreshAsync_ShowsProviderItems()
    {
        await _presenter.RefreshAsync();

        Assert.Single(_view.Shown);
    }

    [Fact]
    public async Task BanButton_Clicked_WiresToBanFlow()
    {
        _view.ManualBanAddress = "not-an-ip";

        _view.RaiseBan();
        var message = await _view.NextMessageAsync();

        Assert.Contains("IP", message);
    }
}
