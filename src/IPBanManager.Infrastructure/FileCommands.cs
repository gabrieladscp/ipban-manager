using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using IPBanManager.Core.Abstractions;

namespace IPBanManager.Infrastructure;

/// <summary>Grava IPs em unban.txt; o IPBan lê o arquivo a cada ciclo e o remove.</summary>
public class UnbanFileCommand : IUnbanCommand
{
    private readonly IInstallationLocator _locator;

    public UnbanFileCommand(IInstallationLocator locator) => _locator = locator;

    public void Unban(IEnumerable<string> addresses)
    {
        var path = Path.Combine(_locator.InstallDirectory, "unban.txt");
        var text = string.Concat(addresses.Select(a => a.Trim() + Environment.NewLine));
        using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            var bytes = new UTF8Encoding(false).GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}

public class LogFileReader : ILogReader
{
    private const int MaxBytes = 512 * 1024;
    private readonly IInstallationLocator _locator;

    public LogFileReader(IInstallationLocator locator) => _locator = locator;

    public string ReadTail(int maxLines)
    {
        var path = Path.Combine(_locator.InstallDirectory, "logfile.txt");
        if (!File.Exists(path)) return "(o IPBan ainda não gerou o logfile.txt)";

        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            bool truncated = stream.Length > MaxBytes;
            if (truncated) stream.Seek(-MaxBytes, SeekOrigin.End);
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                var lines = reader.ReadToEnd().Split('\n').ToList();
                if (truncated && lines.Count > 0) lines.RemoveAt(0); // primeira linha pode estar cortada
                while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0)
                    lines.RemoveAt(lines.Count - 1); // linha vazia após o último \n não é registro
                return string.Join(Environment.NewLine,
                    lines.Skip(Math.Max(0, lines.Count - maxLines)).Select(l => l.TrimEnd('\r')));
            }
        }
    }
}
