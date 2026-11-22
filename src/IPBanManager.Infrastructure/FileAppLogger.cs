using System;
using System.IO;
using System.Text;
using IPBanManager.Core.Abstractions;

namespace IPBanManager.Infrastructure;

/// <summary>Log em arquivo com rotação por tamanho. Thread-safe e nunca lança exceções.</summary>
public sealed class FileAppLogger : IAppLogger
{
    private const long MaxBytes = 1024 * 1024;
    private const int MaxFiles = 3;
    private readonly string _path;
    private readonly object _gate = new object();

    public FileAppLogger(string path = null)
    {
        _path = path ?? DefaultPath;
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "IPBanManager", "logs", "manager.log");

    public void Info(string message) => Write("INFO ", message, null);
    public void Warn(string message) => Write("WARN ", message, null);
    public void Error(string message, Exception exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception ex)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                Rotate();
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                if (ex != null) line += Environment.NewLine + ex;
                File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // Logar nunca pode derrubar o aplicativo.
        }
    }

    private void Rotate()
    {
        var info = new FileInfo(_path);
        if (!info.Exists || info.Length < MaxBytes) return;
        for (int i = MaxFiles - 1; i >= 1; i--)
        {
            var from = i == 1 ? _path : $"{_path}.{i - 1}";
            var to = $"{_path}.{i}";
            if (File.Exists(to)) File.Delete(to);
            if (File.Exists(from)) File.Move(from, to);
        }
    }
}
