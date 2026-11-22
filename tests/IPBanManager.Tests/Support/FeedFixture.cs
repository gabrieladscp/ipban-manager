using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace IPBanManager.Tests.Support;

/// <summary>Feed de releases em disco no formato do Velopack (releases.{canal}.json + pacotes .nupkg).</summary>
internal sealed class FeedFixture : IDisposable
{
    private readonly Dictionary<string, List<string>> _assetsByChannel = new Dictionary<string, List<string>>();

    public TempDirectory Feed { get; } = new TempDirectory();
    public TempDirectory InstalledPackages { get; } = new TempDirectory();

    /// <summary>Publica uma versão no canal. Os hashes descrevem o conteúdo ORIGINAL.</summary>
    public string AddRelease(string channel, string version, string content = null, long? sizeInFeed = null,
        bool omitHashes = false, bool tamperAfterPublishing = false, string notes = "notas")
    {
        var bytes = Encoding.UTF8.GetBytes(content ?? ("pacote-" + channel + "-" + version + new string('x', 200)));
        var fileName = $"IPBanManager-{version}-{channel}-full.nupkg";
        var path = Path.Combine(Feed.Path, fileName);
        File.WriteAllBytes(path, bytes);

        string sha1, sha256;
        using (var s1 = SHA1.Create()) sha1 = Hex(s1.ComputeHash(bytes));
        using (var s2 = SHA256.Create()) sha256 = Hex(s2.ComputeHash(bytes));

        if (tamperAfterPublishing)
        {
            bytes[bytes.Length / 2] ^= 0xFF; // mesmo tamanho, conteúdo diferente
            File.WriteAllBytes(path, bytes);
        }

        var json = $"{{\"PackageId\":\"IPBanManager\",\"Version\":\"{version}\",\"Type\":\"Full\",\"FileName\":\"{fileName}\"," +
                   (omitHashes ? "" : $"\"SHA1\":\"{sha1}\",\"SHA256\":\"{sha256}\",") +
                   $"\"Size\":{sizeInFeed ?? bytes.Length},\"NotesMarkdown\":\"{notes}\"}}";

        if (!_assetsByChannel.TryGetValue(channel, out var list)) _assetsByChannel[channel] = list = new List<string>();
        list.Add(json);
        File.WriteAllText(Path.Combine(Feed.Path, $"releases.{channel}.json"), "{\"Assets\":[" + string.Join(",", list) + "]}");
        return path;
    }

    public string InstalledPackageFor(string version) =>
        Directory.GetFiles(InstalledPackages.Path, $"*{version}*").FirstOrDefault();

    private static string Hex(byte[] hash) => BitConverter.ToString(hash).Replace("-", "");

    public void Dispose()
    {
        Feed.Dispose();
        InstalledPackages.Dispose();
    }
}
