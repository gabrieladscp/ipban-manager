using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using IPBanManager.Core.Abstractions;
using IPBanManager.Core.Models;

namespace IPBanManager.Infrastructure;

/// <summary>
/// Lê/grava o ipban.config preservando comentários, ExpressionsToBlock e LogFilesToParse.
/// Só toca nos valores que realmente mudaram, cria backup e grava de forma atômica.
/// O serviço IPBan vigia o arquivo, então leituras/gravações repetem em caso de arquivo em uso.
/// </summary>
public class XmlConfigurationRepository : IConfigurationRepository
{
    private const int MaxBackups = 10;
    private const int Attempts = 4;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);
    private static readonly object WriteGate = new object();
    private readonly IInstallationLocator _locator;

    public XmlConfigurationRepository(IInstallationLocator locator) => _locator = locator;

    private string ConfigPath => Path.Combine(_locator.InstallDirectory ?? "", InstallationLocator.ConfigFileName);

    public IPBanConfiguration Load()
    {
        var doc = LoadDocument();
        var settings = AppSettings(doc, create: false)?.Elements("add")
            .Where(e => e.Attribute("key") != null)
            .GroupBy(e => e.Attribute("key").Value)
            .ToDictionary(g => g.Key, g => (string)g.Last().Attribute("value") ?? "")
            ?? new Dictionary<string, string>();
        return ConfigurationMapper.FromSettings(settings);
    }

    public void Save(IPBanConfiguration configuration)
    {
        lock (WriteGate)
        {
            var doc = LoadDocument(); // sempre parte do arquivo atual: edições externas em outras chaves são preservadas
            var section = AppSettings(doc, create: true);
            foreach (var pair in ConfigurationMapper.ToSettings(configuration))
            {
                var element = section.Elements("add").LastOrDefault(e => (string)e.Attribute("key") == pair.Key);
                if (element == null)
                    section.Add(new XElement("add", new XAttribute("key", pair.Key), new XAttribute("value", pair.Value)));
                else if (!SameValue(pair.Key, (string)element.Attribute("value") ?? "", pair.Value))
                    element.SetAttributeValue("value", pair.Value);
            }
            WriteDocument(doc);
        }
    }

    public string LoadRawXml() => WithRetry(() => File.ReadAllText(ConfigPath));

    public void SaveRawXml(string xml)
    {
        XDocument.Parse(xml); // garante XML bem formado antes de gravar
        lock (WriteGate) WriteAtomically(xml);
    }

    private XDocument LoadDocument() =>
        WithRetry(() =>
        {
            using (var stream = new FileStream(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        });

    private static XElement AppSettings(XDocument doc, bool create)
    {
        var root = doc.Root ?? throw new InvalidDataException("ipban.config sem elemento raiz.");
        var section = root.Element("appSettings");
        if (section == null && create)
        {
            section = new XElement("appSettings");
            root.Add(section);
        }
        return section;
    }

    private static readonly HashSet<string> ListKeys =
        new HashSet<string> { "Whitelist", "Blacklist", "UserNameWhitelist" };

    /// <summary>Evita reescrever valores que o usuário não alterou (inclusive listas só reformatadas).</summary>
    private static bool SameValue(string key, string current, string wanted)
    {
        if (Flatten(current) == Flatten(wanted)) return true; // atributos XML normalizam quebras de linha em espaços
        return ListKeys.Contains(key)
            && ConfigurationMapper.SplitList(current).SequenceEqual(ConfigurationMapper.SplitList(wanted));
    }

    private static string Flatten(string s) => s.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');

    private void WriteDocument(XDocument doc)
    {
        var builder = new StringBuilder();
        using (var writer = new Utf8StringWriter(builder))
            doc.Save(writer, SaveOptions.DisableFormatting);
        WriteAtomically(builder.ToString());
    }

    private void WriteAtomically(string content)
    {
        var path = ConfigPath;
        var temp = path + ".tmp";
        WithRetry(() =>
        {
            CreateBackup(path);
            DurableIO.WriteAllText(temp, content, new UTF8Encoding(false)); // flush em disco antes da troca atômica
            File.Replace(temp, path, null); // troca atômica: o serviço nunca vê um arquivo pela metade
            return true;
        });
    }

    private static void CreateBackup(string path)
    {
        if (!File.Exists(path)) return;
        File.Copy(path, $"{path}.bak-{DateTime.Now:yyyyMMddHHmmssfff}");
        var obsolete = Directory.GetFiles(Path.GetDirectoryName(path), Path.GetFileName(path) + ".bak-*")
            .OrderByDescending(f => f).Skip(MaxBackups);
        foreach (var file in obsolete) File.Delete(file);
    }

    private static T WithRetry<T>(Func<T> action)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { return action(); }
            catch (IOException) when (attempt < Attempts) { Thread.Sleep(RetryDelay); }
        }
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public Utf8StringWriter(StringBuilder sb) : base(sb) { }
        public override Encoding Encoding { get; } = new UTF8Encoding(false);
    }
}
