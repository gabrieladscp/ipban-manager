using System;
using System.IO;
using System.Linq;
using IPBanManager.Infrastructure;
using IPBanManager.Tests.Support;
using Xunit;

namespace IPBanManager.Tests.Infrastructure;

[Trait("Category", "Integration")]
public sealed class XmlConfigurationRepositoryTests : IDisposable
{
    private readonly TempDirectory _dir = new TempDirectory();
    private readonly XmlConfigurationRepository _repository;

    public XmlConfigurationRepositoryTests()
    {
        _dir.Write("ipban.config", SampleConfig.Xml);
        _repository = new XmlConfigurationRepository(new FakeLocator(_dir.Path));
    }

    public void Dispose() => _dir.Dispose();

    private string ConfigText() => File.ReadAllText(_dir.File("ipban.config"));

    [Fact]
    public void Load_ExistingValues_AreReadTyped()
    {
        var config = _repository.Load();

        Assert.Equal("01:00:00:00", config.BanTime);
        Assert.Equal(new[] { "1.1.1.1", "2.2.2.2" }, config.Whitelist);
    }

    [Fact]
    public void Load_MissingKeys_FallBackToDefaults() =>
        Assert.Equal(5, _repository.Load().FailedLoginAttemptsBeforeBan);

    [Fact]
    public void Save_ChangedValues_ArePersistedAndReloaded()
    {
        var config = _repository.Load();
        config.FailedLoginAttemptsBeforeBan = 3;
        config.Blacklist.Add("9.9.9.9");

        _repository.Save(config);

        var reloaded = _repository.Load();
        Assert.Equal(3, reloaded.FailedLoginAttemptsBeforeBan);
        Assert.Equal(new[] { "9.9.9.9" }, reloaded.Blacklist);
    }

    [Fact]
    public void Save_PreservesCommentsUnknownSectionsAndUnknownKeys()
    {
        var config = _repository.Load();

        _repository.Save(config);

        var xml = ConfigText();
        Assert.Contains("comentario importante", xml);
        Assert.Contains("<Source>SSH</Source>", xml);
        Assert.Contains("CustomKeyUnknownToTheGui", xml);
        Assert.Contains("keep-me", xml);
    }

    [Fact]
    public void Save_UnchangedValue_KeepsOriginalAttributeText()
    {
        _repository.Save(_repository.Load());

        // O valor original "1.1.1.1, 2.2.2.2" (com espaço) não deve ser reescrito se nada mudou.
        Assert.Contains("1.1.1.1, 2.2.2.2", ConfigText());
    }

    [Fact]
    public void Save_CreatesBackupOfPreviousFile()
    {
        _repository.Save(_repository.Load());

        var backup = Assert.Single(Directory.GetFiles(_dir.Path, "ipban.config.bak-*"));
        Assert.Contains("comentario importante", File.ReadAllText(backup));
    }

    [Fact]
    public void Save_ManyTimes_KeepsAtMostTenBackups()
    {
        for (int i = 0; i < 14; i++)
        {
            var config = _repository.Load();
            config.FailedLoginAttemptsBeforeBan = 2 + i;
            _repository.Save(config);
        }

        Assert.Equal(10, Directory.GetFiles(_dir.Path, "ipban.config.bak-*").Length);
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        _repository.Save(_repository.Load());

        Assert.Empty(Directory.GetFiles(_dir.Path, "*.tmp"));
    }

    [Fact]
    public void Save_MultilineValue_RoundTripsWithLineBreaks()
    {
        var config = _repository.Load();
        config.ProcessToRunOnBan = "a.exe|1\r\nb.exe|2";

        _repository.Save(config);

        var reloaded = _repository.Load().ProcessToRunOnBan;
        Assert.Contains("a.exe|1", reloaded);
        Assert.Contains("b.exe|2", reloaded);
        Assert.Contains("\n", reloaded);
    }

    [Fact]
    public void Save_KeyAddedExternallyAfterLoad_IsPreserved()
    {
        var config = _repository.Load();
        File.WriteAllText(_dir.File("ipban.config"),
            ConfigText().Replace("</appSettings>", "<add key=\"AddedByAnotherTool\" value=\"present\"/></appSettings>"));
        config.FailedLoginAttemptsBeforeBan = 7;

        _repository.Save(config);

        Assert.Contains("AddedByAnotherTool", ConfigText());
        Assert.Equal(7, _repository.Load().FailedLoginAttemptsBeforeBan);
    }

    [Fact]
    public void SaveRawXml_MalformedXml_ThrowsAndKeepsFile()
    {
        Assert.ThrowsAny<Exception>(() => _repository.SaveRawXml("<configuration>"));

        Assert.Contains("comentario importante", _repository.LoadRawXml());
    }

    [Fact]
    public void SaveRawXml_ValidXml_ReplacesFile()
    {
        _repository.SaveRawXml("<configuration><appSettings/></configuration>");

        Assert.Equal("<configuration><appSettings/></configuration>", _repository.LoadRawXml());
    }

    [Fact]
    public void Load_ConfigOpenedByAnotherProcessForReading_StillWorks()
    {
        using (new FileStream(_dir.File("ipban.config"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            Assert.Equal("01:00:00:00", _repository.Load().BanTime);
        }
    }
}
