using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IPBanManager.Core.Errors;
using IPBanManager.Core.Updates;
using IPBanManager.Infrastructure.Updates;
using IPBanManager.Tests.Support;
using Xunit;

namespace IPBanManager.Tests.Infrastructure;

[Trait("Category", "Integration")]
public sealed class XmlFileStoreTests : IDisposable
{
    private readonly TempDirectory _dir = new TempDirectory();
    private readonly RecordingLogger _logger = new RecordingLogger();
    public void Dispose() => _dir.Dispose();

    [Fact]
    public void SaveThenLoad_RoundTripsState()
    {
        var store = new FileUpdateStateStore(_logger, _dir.Path);
        store.Save(new UpdateHealthState
        {
            LastKnownGoodVersion = "1.0.0",
            PendingVersion = "1.1.0",
            FailedStarts = 2,
            BlockedVersions = { "1.0.5", "1.0.6-beta.1" },
            RollbackPackageSha256 = "ABC"
        });

        var loaded = store.Load();

        Assert.Equal("1.0.0", loaded.LastKnownGoodVersion);
        Assert.Equal("1.1.0", loaded.PendingVersion);
        Assert.Equal(2, loaded.FailedStarts);
        Assert.Equal(new[] { "1.0.5", "1.0.6-beta.1" }, loaded.BlockedVersions);
        Assert.Equal("ABC", loaded.RollbackPackageSha256);
    }

    [Fact]
    public void Load_FileMissing_ReturnsDefaults() =>
        Assert.Null(new FileUpdateStateStore(_logger, _dir.Path).Load().LastKnownGoodVersion);

    [Fact]
    public void Load_FileZeroFilledByPowerLoss_FallsBackToDefaultsAndWarns()
    {
        // Reproduz o que um reinício abrupto deixou em disco: arquivo com tamanho certo e só bytes zero.
        File.WriteAllBytes(_dir.File("state.xml"), new byte[345]);

        var state = new FileUpdateStateStore(_logger, _dir.Path).Load();

        Assert.Null(state.LastKnownGoodVersion);
        Assert.Contains(_logger.Entries, e => e.StartsWith("WARN") && e.Contains("state.xml"));
    }

    [Fact]
    public void Load_GarbageContent_FallsBackToDefaults()
    {
        _dir.Write("settings.xml", "isto não é xml <<<");

        var settings = new FileUpdateSettingsStore(_logger, _dir.Path).Load();

        Assert.Equal(UpdateChannel.Stable, settings.Channel);
        Assert.True(settings.AutoCheck);
    }

    [Fact]
    public void Load_XmlWithExternalEntity_IsNotResolved()
    {
        // DTD proibido: um settings.xml malicioso não pode ler arquivos locais (XXE).
        var secret = _dir.Write("secret.txt", "TOP-SECRET");
        _dir.Write("settings.xml",
            $"<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY e SYSTEM \"file:///{secret.Replace('\\', '/')}\">]>" +
            "<UpdateSettings><AutoCheck>true</AutoCheck><CheckIntervalHours>&e;</CheckIntervalHours></UpdateSettings>");

        var settings = new FileUpdateSettingsStore(_logger, _dir.Path).Load();

        Assert.Equal(6, settings.CheckIntervalHours); // valor padrão; a entidade nunca foi expandida
    }

    [Fact]
    public void Save_LeavesNoTemporaryFile_AndOverwritesExisting()
    {
        var store = new FileUpdateSettingsStore(_logger, _dir.Path);
        store.Save(new UpdateSettings { Channel = UpdateChannel.Beta });
        store.Save(new UpdateSettings { Channel = UpdateChannel.Stable, AutoCheck = false });

        Assert.Empty(Directory.GetFiles(_dir.Path, "*.tmp"));
        Assert.False(store.Load().AutoCheck);
        Assert.Equal(UpdateChannel.Stable, store.Load().Channel);
    }

    [Fact]
    public void Settings_InvalidInterval_IsSanitizedOnLoad()
    {
        var store = new FileUpdateSettingsStore(_logger, _dir.Path);
        store.Save(new UpdateSettings { CheckIntervalHours = 0 });

        Assert.Equal(6, store.Load().CheckIntervalHours);
    }

    [Fact]
    public void Save_ConcurrentWriters_NeverCorruptTheFile()
    {
        var store = new FileUpdateStateStore(_logger, _dir.Path);

        Parallel.For(0, 100, i => store.Save(new UpdateHealthState { FailedStarts = i }));

        var loaded = store.Load();
        Assert.InRange(loaded.FailedStarts, 0, 99);
        Assert.DoesNotContain(_logger.Entries, e => e.StartsWith("WARN"));
    }
}

[Trait("Category", "Integration")]
public sealed class RollbackPackageStoreTests : IDisposable
{
    private const string AppId = "IPBanManager";
    private readonly TempDirectory _packagesDir = new TempDirectory();
    private readonly TempDirectory _storage = new TempDirectory();
    private readonly RollbackPackageStore _store;

    public RollbackPackageStoreTests()
    {
        _store = new RollbackPackageStore(() => _packagesDir.Path, AppId, new RecordingLogger(), _storage.File("rollback"));
    }

    public void Dispose() { _packagesDir.Dispose(); _storage.Dispose(); }

    private string InstalledPackage(string version, string content = "pacote") =>
        _packagesDir.Write($"{AppId}-{version}-full.nupkg", content);

    [Fact]
    public void Preserve_InstalledPackage_CopiesItWithItsSha256()
    {
        InstalledPackage("1.0.0", "conteudo-1.0.0");

        var package = _store.Preserve("1.0.0");

        Assert.True(File.Exists(package.File));
        Assert.Equal("1.0.0", package.Version);
        Assert.Equal(RollbackPackageStore.Sha256Of(package.File), package.Sha256);
        Assert.Equal("conteudo-1.0.0", File.ReadAllText(package.File));
    }

    [Fact]
    public void Preserve_AlsoFindsPackagesNamedWithChannel()
    {
        _packagesDir.Write($"{AppId}-1.0.0-stable-full.nupkg", "x");

        Assert.NotNull(_store.Preserve("1.0.0"));
    }

    [Fact]
    public void Preserve_StableVersion_NeverPicksTheBetaPackageOfTheSameBase()
    {
        InstalledPackage("1.0.0-beta.1", "BETA");

        Assert.Null(_store.Preserve("1.0.0")); // 1.0.0 != 1.0.0-beta.1
    }

    [Fact]
    public void Preserve_NoPackage_ReturnsNull() =>
        Assert.Null(_store.Preserve("9.9.9"));

    [Fact]
    public void Preserve_KeepsOnlyTheLatestRollbackPackage()
    {
        InstalledPackage("1.0.0");
        InstalledPackage("1.1.0");

        _store.Preserve("1.0.0");
        _store.Preserve("1.1.0");

        var files = Directory.GetFiles(_storage.File("rollback"), "*.nupkg");
        Assert.Single(files);
        Assert.Contains("1.1.0", files[0]);
    }

    [Fact]
    public void Restore_IntactPackage_IsCopiedToTheInstallerPackagesFolder()
    {
        InstalledPackage("1.0.0", "bom");
        var package = _store.Preserve("1.0.0");
        File.Delete(Path.Combine(_packagesDir.Path, $"{AppId}-1.0.0-full.nupkg")); // o Velopack limpa os pacotes antigos

        _store.Restore(package);

        Assert.Equal("bom", File.ReadAllText(Path.Combine(_packagesDir.Path, $"{AppId}-1.0.0-full.nupkg")));
    }

    [Fact]
    public void Restore_TamperedPackage_IsRefusedAndDeleted()
    {
        InstalledPackage("1.0.0", "original");
        var package = _store.Preserve("1.0.0");
        File.WriteAllText(package.File, "adulterado");

        var ex = Assert.Throws<ManagerException>(() => _store.Restore(package));

        Assert.Contains("SHA-256", ex.Message);
        Assert.False(File.Exists(package.File));
    }

    [Fact]
    public void Restore_MissingFile_ThrowsFriendlyError()
    {
        var ghost = new RollbackPackage(_storage.File("nao-existe.nupkg"), "1.0.0", "00");

        Assert.Throws<ManagerException>(() => _store.Restore(ghost));
    }

    [Fact]
    public void Exists_ReflectsTheFileOnDisk()
    {
        InstalledPackage("1.0.0");
        var package = _store.Preserve("1.0.0");

        Assert.True(_store.Exists(package));
        File.Delete(package.File);
        Assert.False(_store.Exists(package));
        Assert.False(_store.Exists(null));
    }
}
