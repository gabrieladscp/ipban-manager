using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IPBanManager.Core.Errors;
using IPBanManager.Core.Updates;
using IPBanManager.Infrastructure.Updates;
using IPBanManager.Tests.Support;
using Velopack.Locators;
using Velopack.Sources;
using Xunit;

namespace IPBanManager.Tests.Infrastructure;

[Trait("Category", "Unit")]
public class UpdateRepositoryTests
{
    [Theory]
    [InlineData("https://github.com/acme/ipban-manager")]
    [InlineData("https://github.com/acme/ipban-manager/")]
    [InlineData("https://github.com/Some-Org/repo.name_1")]
    public void IsAcceptable_GitHubHttpsRepositories_ReturnsTrue(string url) =>
        Assert.True(UpdateRepository.IsAcceptable(url));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("http://github.com/acme/app")]                 // sem TLS
    [InlineData("https://evil.example.com/acme/app")]          // outro host
    [InlineData("https://github.com.evil.com/acme/app")]       // host parecido
    [InlineData("https://github.com/acme")]                    // sem repositório
    [InlineData("https://github.com/acme/app/releases")]       // caminho extra
    [InlineData("https://user@github.com/acme/app")]           // credenciais embutidas
    [InlineData("ftp://github.com/acme/app")]
    [InlineData("C:\\feed")]                                   // pasta local: só em build de QA
    public void IsAcceptable_Everything_Else_ReturnsFalse(string url) =>
        Assert.False(UpdateRepository.IsAcceptable(url));
}

[Trait("Category", "Unit")]
public class IntegrityFailureDetectionTests
{
    [Theory]
    [InlineData("SHA256 doesn't match (AAA != BBB).")]   // mensagem real do Velopack
    [InlineData("SHA1 checksum mismatch")]
    [InlineData("The hash does not match")]
    public void IsIntegrityFailure_RecognizesVelopackChecksumMessages(string message) =>
        Assert.True(VelopackUpdateService.IsIntegrityFailure(new Exception(message)));

    [Theory]
    [InlineData("Unable to connect to the remote server")]
    [InlineData("The operation timed out")]
    public void IsIntegrityFailure_NetworkProblems_AreNotIntegrity(string message) =>
        Assert.False(VelopackUpdateService.IsIntegrityFailure(new Exception(message)));
}

/// <summary>
/// Exercita o serviço real do Velopack (UpdateManager) contra um feed em disco, simulando uma instalação
/// com TestVelopackLocator — sem rede e sem instalar nada.
/// </summary>
[Trait("Category", "Integration")]
public sealed class VelopackUpdateServiceTests : IDisposable
{
    private const string Repo = "https://github.com/acme/ipban-manager";
    private readonly FeedFixture _feed = new FeedFixture();
    private readonly FakeStateStore _state = new FakeStateStore();
    private readonly FakeRollbackPackages _packages = new FakeRollbackPackages();
    private readonly RecordingLogger _logger = new RecordingLogger();
    private readonly UpdateHealthGuard _guard;

    public VelopackUpdateServiceTests() => _guard = new UpdateHealthGuard(_state, _packages, _logger);
    public void Dispose() => _feed.Dispose();

    private VelopackUpdateService NewService(string installedVersion = "1.0.0", string repo = Repo) =>
        new VelopackUpdateService(repo, _guard, _packages, _logger, () => installedVersion,
            locator: new TestVelopackLocator("IPBanManager", installedVersion, _feed.InstalledPackages.Path, null),
            sourceFactory: (location, includePrereleases) => new SimpleFileSource(new DirectoryInfo(_feed.Feed.Path)),
            retryDelays: new TimeSpan[0]);

    private static readonly UpdateSettings Stable = new UpdateSettings { Channel = UpdateChannel.Stable };
    private static readonly UpdateSettings Beta = new UpdateSettings { Channel = UpdateChannel.Beta };

    // ---------- suporte ----------

    [Fact]
    public void IsSupported_ValidRepositoryAndInstalledApp_IsTrue() =>
        Assert.True(NewService().IsSupported);

    [Theory]
    [InlineData("")]
    [InlineData("https://evil.example.com/a/b")]
    [InlineData("http://github.com/a/b")]
    public void IsSupported_UntrustedOrMissingRepository_IsFalseWithExplanation(string repo)
    {
        var service = NewService(repo: repo);

        Assert.False(service.IsSupported);
        Assert.Contains("repositório", service.UnsupportedReason);
    }

    [Fact]
    public async Task CheckAsync_WhenUnsupported_ThrowsFriendlyException()
    {
        var service = NewService(repo: "https://evil.example.com/a/b");

        await Assert.ThrowsAsync<ManagerException>(() => service.CheckAsync(Stable, CancellationToken.None));
    }

    [Fact]
    public void IsSupported_NotInstalledByVelopack_ExplainsHowToInstall()
    {
        // Sem locator injetado: o processo de teste não é uma instalação Velopack (igual a um build de desenvolvimento).
        var service = new VelopackUpdateService(Repo, _guard, _packages, _logger, () => "1.0.0",
            sourceFactory: (l, p) => new SimpleFileSource(new DirectoryInfo(_feed.Feed.Path)));

        Assert.False(service.IsSupported);
        Assert.Contains("instalador", service.UnsupportedReason);
        Assert.Equal("1.0.0", service.CurrentVersion);
    }

    // ---------- verificação ----------

    [Fact]
    public async Task CheckAsync_NewerStableRelease_IsOffered()
    {
        _feed.AddRelease("stable", "1.1.0", notes: "corrige bugs");

        var update = await NewService().CheckAsync(Stable, CancellationToken.None);

        Assert.Equal("1.1.0", update.Version);
        Assert.Equal(UpdateChannel.Stable, update.Channel);
        Assert.False(update.IsDowngrade);
        Assert.Equal("corrige bugs", update.NotesMarkdown);
        Assert.True(update.SizeBytes > 0);
    }

    [Fact]
    public async Task CheckAsync_SameVersionPublished_ReturnsNull()
    {
        _feed.AddRelease("stable", "1.0.0");

        Assert.Null(await NewService().CheckAsync(Stable, CancellationToken.None));
    }

    [Fact]
    public async Task CheckAsync_EmptyFeed_ReturnsNull() =>
        Assert.Null(await NewService().CheckAsync(Stable, CancellationToken.None));

    [Fact]
    public async Task CheckAsync_SeveralReleases_PicksTheHighestBySemanticVersion()
    {
        _feed.AddRelease("stable", "1.2.0");
        _feed.AddRelease("stable", "1.10.0"); // comparação numérica: 1.10.0 > 1.9.0 > 1.2.0
        _feed.AddRelease("stable", "1.9.0");

        var update = await NewService().CheckAsync(Stable, CancellationToken.None);

        Assert.Equal("1.10.0", update.Version);
    }

    [Fact]
    public async Task CheckAsync_StableUser_NeverSeesBetaReleases()
    {
        _feed.AddRelease("beta", "1.1.0-beta.1");

        Assert.Null(await NewService().CheckAsync(Stable, CancellationToken.None));
    }

    [Fact]
    public async Task CheckAsync_BetaUser_GetsBetaWhenItIsTheNewest()
    {
        _feed.AddRelease("stable", "1.1.0");
        _feed.AddRelease("beta", "1.2.0-beta.1");

        var update = await NewService().CheckAsync(Beta, CancellationToken.None);

        Assert.Equal("1.2.0-beta.1", update.Version);
        Assert.Equal(UpdateChannel.Beta, update.Channel);
    }

    [Fact]
    public async Task CheckAsync_BetaUser_GetsNewerStableOverOlderBeta()
    {
        // O estável 1.2.0 é mais novo que o beta 1.2.0-beta.3: quem está no Beta deve recebê-lo.
        _feed.AddRelease("beta", "1.2.0-beta.3");
        _feed.AddRelease("stable", "1.2.0");

        var update = await NewService(installedVersion: "1.2.0-beta.3").CheckAsync(Beta, CancellationToken.None);

        Assert.Equal("1.2.0", update.Version);
    }

    [Fact]
    public async Task CheckAsync_BetaUser_AlreadyOnNewestBeta_ReturnsNull()
    {
        _feed.AddRelease("stable", "1.1.0");
        _feed.AddRelease("beta", "1.2.0-beta.1");

        Assert.Null(await NewService(installedVersion: "1.2.0-beta.1").CheckAsync(Beta, CancellationToken.None));
    }

    [Fact]
    public async Task CheckAsync_BlockedVersion_IsNeverOffered()
    {
        _feed.AddRelease("stable", "1.1.0");
        _guard.RegisterRollback("1.1.0", "1.0.0");

        Assert.Null(await NewService().CheckAsync(Stable, CancellationToken.None));
    }

    [Fact]
    public async Task CheckAsync_BlockedVersionButNewerFixExists_OffersTheFix()
    {
        _feed.AddRelease("stable", "1.1.0");
        _feed.AddRelease("stable", "1.1.1");
        _guard.RegisterRollback("1.1.0", "1.0.0");

        var update = await NewService().CheckAsync(Stable, CancellationToken.None);

        Assert.Equal("1.1.1", update.Version);
    }

    [Fact]
    public async Task CheckAsync_BetaBuildSwitchedToStable_OffersTheOlderStableAsDowngrade()
    {
        _feed.AddRelease("stable", "1.1.0");

        var update = await NewService(installedVersion: "1.2.0-beta.1").CheckAsync(Stable, CancellationToken.None);

        Assert.Equal("1.1.0", update.Version);
        Assert.True(update.IsDowngrade);
    }

    [Fact]
    public async Task CheckAsync_StableBuildNeverGetsAnOlderVersionOffered()
    {
        _feed.AddRelease("stable", "0.9.0");

        Assert.Null(await NewService(installedVersion: "1.0.0").CheckAsync(Stable, CancellationToken.None));
    }

    // ---------- download + integridade ----------

    [Fact]
    public async Task DownloadAsync_ValidPackage_IsStoredAndVerified()
    {
        _feed.AddRelease("stable", "1.1.0");
        var service = NewService();
        var update = await service.CheckAsync(Stable, CancellationToken.None);

        await service.DownloadAsync(update, null, CancellationToken.None);

        Assert.NotNull(_feed.InstalledPackageFor("1.1.0"));
    }

    [Fact]
    public async Task DownloadAsync_FirstStep_PreservesTheCurrentVersionForRollback()
    {
        _state.State.LastKnownGoodVersion = "1.0.0";
        _feed.AddRelease("stable", "1.1.0");
        var service = NewService();
        var update = await service.CheckAsync(Stable, CancellationToken.None);

        await service.DownloadAsync(update, null, CancellationToken.None);

        Assert.Equal(new[] { "1.0.0" }, _packages.Preserved);
    }

    [Fact]
    public async Task DownloadAsync_ReportsProgress()
    {
        _feed.AddRelease("stable", "1.1.0");
        var service = NewService();
        var update = await service.CheckAsync(Stable, CancellationToken.None);
        var reports = new System.Collections.Generic.List<int>();

        await service.DownloadAsync(update, new SyncProgress(reports.Add), CancellationToken.None);

        Assert.NotEmpty(reports);
        Assert.Equal(100, reports.Last());
    }

    [Fact]
    public async Task DownloadAsync_TamperedPackage_IsRejectedAndNothingStaysOnDisk()
    {
        _feed.AddRelease("stable", "1.1.0", tamperAfterPublishing: true);
        var service = NewService();
        var update = await service.CheckAsync(Stable, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ManagerException>(() => service.DownloadAsync(update, null, CancellationToken.None));

        Assert.Contains("integridade", ex.Message);
        Assert.Contains("Nada foi instalado", ex.Message);
        Assert.Null(_feed.InstalledPackageFor("1.1.0"));
    }

    [Fact]
    public async Task DownloadAsync_TamperedPackage_CannotBeAppliedAfterwards()
    {
        _feed.AddRelease("stable", "1.1.0", tamperAfterPublishing: true);
        var service = NewService();
        var update = await service.CheckAsync(Stable, CancellationToken.None);
        await Assert.ThrowsAsync<ManagerException>(() => service.DownloadAsync(update, null, CancellationToken.None));

        var ex = Assert.Throws<ManagerException>(() => service.ApplyAndRestart());

        Assert.Contains("Nenhuma atualização", ex.Message);
    }

    [Fact]
    public async Task DownloadAsync_WrongSizeInFeed_IsRejected()
    {
        _feed.AddRelease("stable", "1.1.0", sizeInFeed: 12345);
        var service = NewService();
        var update = await service.CheckAsync(Stable, CancellationToken.None);

        await Assert.ThrowsAsync<ManagerException>(() => service.DownloadAsync(update, null, CancellationToken.None));

        Assert.Null(_feed.InstalledPackageFor("1.1.0"));
    }

    [Fact]
    public async Task DownloadAsync_FeedWithoutAnyHash_IsRefusedForSafety()
    {
        _feed.AddRelease("stable", "1.1.0", omitHashes: true);
        var service = NewService();
        var update = await service.CheckAsync(Stable, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ManagerException>(() => service.DownloadAsync(update, null, CancellationToken.None));

        Assert.Contains("hash", ex.Message);
        Assert.Null(_feed.InstalledPackageFor("1.1.0"));
    }

    [Fact]
    public async Task DownloadAsync_VersionNotFromLastCheck_IsRejected()
    {
        _feed.AddRelease("stable", "1.1.0");
        var service = NewService();
        await service.CheckAsync(Stable, CancellationToken.None);

        await Assert.ThrowsAsync<ManagerException>(() =>
            service.DownloadAsync(new AppUpdate("9.9.9", UpdateChannel.Stable, 1, "", false), null, CancellationToken.None));
    }

    [Fact]
    public async Task ApplyAndRestart_NothingDownloaded_RefusesInsteadOfExiting()
    {
        _feed.AddRelease("stable", "1.1.0");
        var service = NewService();
        await service.CheckAsync(Stable, CancellationToken.None);

        Assert.Throws<ManagerException>(() => service.ApplyAndRestart());
    }

    // ---------- reversão ----------

    [Fact]
    public void GetRollbackInfo_PackagePreservedForOlderVersion_IsOffered()
    {
        var file = _feed.Feed.Write("old.nupkg", "x");
        _state.State.RollbackPackageFile = file;
        _state.State.RollbackPackageVersion = "0.9.0";

        Assert.Equal("0.9.0", NewService("1.0.0").GetRollbackInfo().Version);
    }

    [Fact]
    public void GetRollbackInfo_PackageIsTheRunningVersion_IsNotOffered()
    {
        var file = _feed.Feed.Write("same.nupkg", "x");
        _state.State.RollbackPackageFile = file;
        _state.State.RollbackPackageVersion = "1.0.0";

        Assert.Null(NewService("1.0.0").GetRollbackInfo());
    }

    [Fact]
    public void GetRollbackInfo_PackageFileDeleted_IsNotOffered()
    {
        _state.State.RollbackPackageFile = _feed.Feed.File("sumiu.nupkg");
        _state.State.RollbackPackageVersion = "0.9.0";

        Assert.Null(NewService("1.0.0").GetRollbackInfo());
    }

    [Fact]
    public void GetRollbackInfo_NothingPreserved_IsNotOffered() =>
        Assert.Null(NewService().GetRollbackInfo());

    [Fact]
    public void RollbackAndRestart_NothingPreserved_ThrowsFriendlyException()
    {
        var ex = Assert.Throws<ManagerException>(() => NewService().RollbackAndRestart("1.1.0"));

        Assert.Contains("versão anterior", ex.Message);
    }

    /// <summary>IProgress que executa de forma síncrona (Progress&lt;T&gt; posta em outra thread).</summary>
    private sealed class SyncProgress : IProgress<int>
    {
        private readonly Action<int> _report;
        public SyncProgress(Action<int> report) => _report = report;
        public void Report(int value) => _report(value);
    }
}
