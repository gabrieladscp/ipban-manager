using System;
using System.IO;
using System.Linq;
using IPBanManager.Core.Errors;
using IPBanManager.Infrastructure;
using IPBanManager.Tests.Support;
using Xunit;

namespace IPBanManager.Tests.Infrastructure;

[Trait("Category", "Integration")]
public sealed class UnbanFileCommandTests : IDisposable
{
    private readonly TempDirectory _dir = new TempDirectory();
    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Unban_CalledTwice_AppendsEachAddressOnItsOwnLine()
    {
        var command = new UnbanFileCommand(new FakeLocator(_dir.Path));

        command.Unban(new[] { "1.1.1.1" });
        command.Unban(new[] { "2.2.2.2", " 3.3.3.3 " });

        Assert.Equal(new[] { "1.1.1.1", "2.2.2.2", "3.3.3.3" }, File.ReadAllLines(_dir.File("unban.txt")));
    }

    [Fact]
    public void Unban_FileHeldOpenByService_StillAppends()
    {
        var path = _dir.Write("unban.txt", "");
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            new UnbanFileCommand(new FakeLocator(_dir.Path)).Unban(new[] { "4.4.4.4" });
        }

        Assert.Equal(new[] { "4.4.4.4" }, File.ReadAllLines(path));
    }
}

[Trait("Category", "Integration")]
public sealed class LogFileReaderTests : IDisposable
{
    private readonly TempDirectory _dir = new TempDirectory();
    public void Dispose() => _dir.Dispose();

    [Fact]
    public void ReadTail_MissingFile_ReturnsFriendlyText()
    {
        var text = new LogFileReader(new FakeLocator(_dir.Path)).ReadTail(10);

        Assert.Contains("ainda não gerou", text);
    }

    [Fact]
    public void ReadTail_ManyLines_ReturnsOnlyTheLastOnes()
    {
        _dir.Write("logfile.txt", string.Join("\r\n", Enumerable.Range(1, 100).Select(i => "linha " + i)));

        var lines = new LogFileReader(new FakeLocator(_dir.Path)).ReadTail(3).Split(new[] { Environment.NewLine }, StringSplitOptions.None);

        Assert.Equal(new[] { "linha 98", "linha 99", "linha 100" }, lines);
    }

    [Fact]
    public void ReadTail_FileBeingWrittenByService_CanBeRead()
    {
        var path = _dir.File("logfile.txt");
        using (var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        using (var sw = new StreamWriter(writer))
        {
            sw.WriteLine("em escrita");
            sw.Flush();

            var text = new LogFileReader(new FakeLocator(_dir.Path)).ReadTail(5);

            Assert.Contains("em escrita", text);
        }
    }

    [Fact]
    public void ReadTail_HugeFile_ReadsOnlyTheEndAndDropsPartialFirstLine()
    {
        var path = _dir.File("logfile.txt");
        using (var sw = new StreamWriter(path))
            for (int i = 0; i < 40000; i++) sw.WriteLine("registro numero " + i);

        var text = new LogFileReader(new FakeLocator(_dir.Path)).ReadTail(2);

        Assert.EndsWith("registro numero 39999", text);
    }
}

[Trait("Category", "Integration")]
public sealed class InstallationLocatorTests : IDisposable
{
    private readonly TempDirectory _dir = new TempDirectory();
    public void Dispose() => _dir.Dispose();

    private InstallationLocator NewLocator() =>
        new InstallationLocator("ServiceThatDoesNotExist-" + Guid.NewGuid().ToString("N"), _dir.File("pref.txt"));

    [Fact]
    public void SetInstallDirectory_FolderWithConfig_ChangesDirectoryAndRaisesEvent()
    {
        var install = Directory.CreateDirectory(_dir.File("ipban")).FullName;
        File.WriteAllText(Path.Combine(install, "ipban.config"), "<configuration/>");
        var locator = NewLocator();
        int raised = 0;
        locator.InstallDirectoryChanged += (s, e) => raised++;

        locator.SetInstallDirectory(install);

        Assert.Equal(install, locator.InstallDirectory);
        Assert.True(locator.IsValid);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void SetInstallDirectory_Choice_IsRememberedByNextInstance()
    {
        var install = Directory.CreateDirectory(_dir.File("ipban")).FullName;
        File.WriteAllText(Path.Combine(install, "ipban.config"), "<configuration/>");
        NewLocator().SetInstallDirectory(install);

        var next = NewLocator();

        Assert.Equal(install, next.InstallDirectory);
    }

    [Fact]
    public void SetInstallDirectory_MissingFolder_ThrowsManagerException() =>
        Assert.Throws<ManagerException>(() => NewLocator().SetInstallDirectory(_dir.File("nao-existe")));

    [Fact]
    public void SetInstallDirectory_FolderWithoutConfig_ThrowsManagerExceptionAndKeepsOldValue()
    {
        var locator = NewLocator();
        var before = locator.InstallDirectory;
        var empty = Directory.CreateDirectory(_dir.File("vazia")).FullName;

        var ex = Assert.Throws<ManagerException>(() => locator.SetInstallDirectory(empty));

        Assert.Contains("ipban.config", ex.Message);
        Assert.Equal(before, locator.InstallDirectory);
    }

    [Fact]
    public void IsValid_NoServiceAndNoPreference_IsFalse() =>
        Assert.False(NewLocator().IsValid);

    [Theory]
    [InlineData("\"C:\\Program Files\\IPBan\\DigitalRuby.IPBan.exe\"", "C:\\Program Files\\IPBan")]
    [InlineData("C:\\Program Files\\IPBan\\DigitalRuby.IPBan.exe", "C:\\Program Files\\IPBan")]
    [InlineData("C:\\x\\a.exe -service", "C:\\x")]
    [InlineData("\"C:\\a b\\x.exe\" --flag", "C:\\a b")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ParseExecutableDirectory_HandlesQuotesAndArguments(string imagePath, string expected) =>
        Assert.Equal(expected, InstallationLocator.ParseExecutableDirectory(imagePath));
}

[Trait("Category", "Integration")]
public sealed class FileAppLoggerTests : IDisposable
{
    private readonly TempDirectory _dir = new TempDirectory();
    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Error_WithException_WritesMessageAndStackInformation()
    {
        var path = _dir.File("logs\\app.log");
        var logger = new FileAppLogger(path);

        logger.Error("falhou", new InvalidOperationException("detalhe-especial"));

        var text = File.ReadAllText(path);
        Assert.Contains("[ERROR] falhou", text);
        Assert.Contains("detalhe-especial", text);
    }

    [Fact]
    public void Write_PastSizeLimit_RotatesAndKeepsAtMostThreeFiles()
    {
        var path = _dir.File("app.log");
        var logger = new FileAppLogger(path);
        var big = new string('x', 200 * 1024);

        for (int i = 0; i < 30; i++) logger.Info(big);

        var files = Directory.GetFiles(_dir.Path, "app.log*");
        Assert.InRange(files.Length, 2, 3);
        Assert.All(files, f => Assert.True(new FileInfo(f).Length < 2 * 1024 * 1024));
    }

    [Fact]
    public void Info_UnwritableLocation_NeverThrows()
    {
        var logger = new FileAppLogger("Z:\\pasta-inexistente\\<>|\\app.log");

        var exception = Record.Exception(() => logger.Info("ok"));

        Assert.Null(exception);
    }

    [Fact]
    public void Info_ManyThreads_AllEntriesWritten()
    {
        var path = _dir.File("threads.log");
        var logger = new FileAppLogger(path);

        System.Threading.Tasks.Parallel.For(0, 200, i => logger.Info("entrada " + i));

        Assert.Equal(200, File.ReadAllLines(path).Count(l => l.Contains("entrada")));
    }
}
