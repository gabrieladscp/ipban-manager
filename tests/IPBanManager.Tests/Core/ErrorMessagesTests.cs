using System;
using System.IO;
using System.Xml;
using IPBanManager.Core.Errors;
using IPBanManager.Core.Presenters;
using Xunit;

namespace IPBanManager.Tests.Core;

[Trait("Category", "Unit")]
public class ErrorMessagesTests
{
    [Fact]
    public void Translate_ManagerException_ReturnsItsOwnMessage() =>
        Assert.Equal("já amigável", ErrorMessages.Translate(new ManagerException("já amigável")));

    [Fact]
    public void Translate_UnauthorizedAccess_SuggestsRunningAsAdministrator() =>
        Assert.Contains("Administrador", ErrorMessages.Translate(new UnauthorizedAccessException()));

    [Fact]
    public void Translate_IOException_ExplainsFileInUse() =>
        Assert.Contains("em uso", ErrorMessages.Translate(new IOException()));

    [Fact]
    public void Translate_FileNotFound_PointsToInstallFolder() =>
        Assert.Contains("pasta de instalação", ErrorMessages.Translate(new FileNotFoundException()));

    [Fact]
    public void Translate_XmlException_MentionsInvalidConfig() =>
        Assert.Contains("XML válido", ErrorMessages.Translate(new XmlException("boom")));

    [Fact]
    public void Translate_UnknownException_IncludesMessageAndLogHint()
    {
        var text = ErrorMessages.Translate(new InvalidOperationException("kaput"));

        Assert.Contains("kaput", text);
        Assert.Contains("log", text);
    }
}

[Trait("Category", "Unit")]
public class LogFilterTests
{
    private const string Log = "a INFO ok\r\nb WARN disk\r\nc info other";

    [Fact]
    public void ApplyFilter_EmptyFilter_ReturnsOriginal() =>
        Assert.Equal(Log, LogPresenter.ApplyFilter(Log, "  "));

    [Fact]
    public void ApplyFilter_IsCaseInsensitiveAndKeepsOnlyMatches()
    {
        var result = LogPresenter.ApplyFilter(Log, "info");

        Assert.Equal("a INFO ok" + Environment.NewLine + "c info other", result);
    }

    [Fact]
    public void ApplyFilter_NoMatch_ReturnsEmpty() =>
        Assert.Equal("", LogPresenter.ApplyFilter(Log, "zzz"));
}
