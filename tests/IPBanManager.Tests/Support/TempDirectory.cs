using System;
using System.IO;

namespace IPBanManager.Tests.Support;

/// <summary>Pasta temporária isolada por teste (removida ao final).</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ipbanmgr-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public string Write(string name, string content)
    {
        var full = File(name);
        System.IO.File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { /* limpeza best effort */ }
        catch (UnauthorizedAccessException) { }
    }
}

internal static class SampleConfig
{
    public const string Xml = @"<?xml version=""1.0""?>
<configuration>
  <!-- comentario importante -->
  <LogFilesToParse><LogFiles><LogFile><Source>SSH</Source></LogFile></LogFiles></LogFilesToParse>
  <appSettings>
    <add key=""BanTime"" value=""01:00:00:00""/>
    <add key=""Whitelist"" value=""1.1.1.1, 2.2.2.2""/>
    <add key=""FirewallRules"" value=""a;allow;1.1.1.1;;.""/>
    <add key=""CustomKeyUnknownToTheGui"" value=""keep-me""/>
  </appSettings>
</configuration>";
}
