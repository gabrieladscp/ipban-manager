using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using IPBanManager.Core.Abstractions;
using IPBanManager.Core.Updates;

namespace IPBanManager.Infrastructure.Updates;

/// <summary>Local dos dados de atualização: fora da pasta do app, portanto preservados em qualquer atualização.</summary>
public static class UpdateStorage
{
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "IPBanManager", "update");
}

/// <summary>Persistência XML atômica e tolerante a arquivos corrompidos (volta ao padrão e registra em log).</summary>
public class XmlFileStore<T> where T : class, new()
{
    private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(T));
    private readonly string _path;
    private readonly IAppLogger _logger;
    private readonly object _gate = new object();

    public XmlFileStore(string path, IAppLogger logger)
    {
        _path = path;
        _logger = logger ?? NullLogger.Instance;
    }

    public T Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path)) return new T();
                using (var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                    return (T)Serializer.Deserialize(reader);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is XmlException || ex is IOException)
            {
                _logger.Warn($"Arquivo {Path.GetFileName(_path)} ilegível ({ex.Message}); usando valores padrão.");
                return new T();
            }
        }
    }

    public void Save(T value)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            var temp = _path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false), CloseOutput = false }))
                    Serializer.Serialize(writer, value);
                stream.Flush(flushToDisk: true); // durável antes da troca (ver DurableIO)
            }

            if (File.Exists(_path)) File.Replace(temp, _path, null);
            else File.Move(temp, _path);
        }
    }
}

public sealed class FileUpdateSettingsStore : IUpdateSettingsStore
{
    private readonly XmlFileStore<UpdateSettings> _store;

    public FileUpdateSettingsStore(IAppLogger logger, string directory = null)
    {
        _store = new XmlFileStore<UpdateSettings>(Path.Combine(directory ?? UpdateStorage.DefaultDirectory, "settings.xml"), logger);
    }

    public UpdateSettings Load()
    {
        var settings = _store.Load();
        if (settings.CheckIntervalHours < 1) settings.CheckIntervalHours = 6;
        return settings;
    }

    public void Save(UpdateSettings settings) => _store.Save(settings);
}

public sealed class FileUpdateStateStore : IUpdateStateStore
{
    private readonly XmlFileStore<UpdateHealthState> _store;

    public FileUpdateStateStore(IAppLogger logger, string directory = null)
    {
        _store = new XmlFileStore<UpdateHealthState>(Path.Combine(directory ?? UpdateStorage.DefaultDirectory, "state.xml"), logger);
    }

    public UpdateHealthState Load() => _store.Load();
    public void Save(UpdateHealthState state) => _store.Save(state);
}
