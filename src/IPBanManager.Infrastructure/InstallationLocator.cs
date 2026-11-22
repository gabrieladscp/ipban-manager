using System;
using System.IO;
using IPBanManager.Core.Abstractions;
using IPBanManager.Core.Errors;
using Microsoft.Win32;

namespace IPBanManager.Infrastructure;

/// <summary>
/// Descobre a pasta do IPBan: preferência salva pelo usuário, senão o ImagePath do serviço Windows.
/// </summary>
public class InstallationLocator : IInstallationLocator
{
    public const string ConfigFileName = "ipban.config";
    private readonly string _preferenceFile;

    public InstallationLocator(string serviceName = "IPBan", string preferenceFile = null)
    {
        ServiceName = serviceName;
        _preferenceFile = preferenceFile ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "IPBanManager", "install-dir.txt");
        InstallDirectory = ReadPreference() ?? DetectFromService();
    }

    public string ServiceName { get; }
    public string InstallDirectory { get; private set; }
    public bool IsValid => !string.IsNullOrEmpty(InstallDirectory)
                           && File.Exists(Path.Combine(InstallDirectory, ConfigFileName));
    public event EventHandler InstallDirectoryChanged;

    public void SetInstallDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            throw new ManagerException("A pasta selecionada não existe.");
        if (!File.Exists(Path.Combine(path, ConfigFileName)))
            throw new ManagerException($"A pasta selecionada não contém o arquivo {ConfigFileName}. Escolha a pasta onde o IPBan está instalado.");

        InstallDirectory = path;
        SavePreference(path);
        InstallDirectoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SavePreference(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_preferenceFile));
            File.WriteAllText(_preferenceFile, path);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // A escolha vale para esta sessão mesmo que não seja possível lembrá-la.
        }
    }

    private string ReadPreference()
    {
        try
        {
            if (!File.Exists(_preferenceFile)) return null;
            var text = File.ReadAllText(_preferenceFile).Trim();
            return Directory.Exists(text) ? text : null;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return null; }
    }

    private string DetectFromService()
    {
        try
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + ServiceName))
                return ParseExecutableDirectory(key?.GetValue("ImagePath") as string);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Extrai a pasta do executável de um ImagePath (com ou sem aspas/argumentos).</summary>
    public static string ParseExecutableDirectory(string imagePath)
    {
        var exe = ParseExecutablePath(imagePath);
        return exe == null ? null : Path.GetDirectoryName(exe);
    }

    /// <summary>Extrai o caminho do executável de um ImagePath (com ou sem aspas/argumentos).</summary>
    public static string ParseExecutablePath(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return null;
        var text = imagePath.Trim();
        string exe;
        if (text.StartsWith("\""))
        {
            var end = text.IndexOf('"', 1);
            exe = end > 0 ? text.Substring(1, end - 1) : text.Trim('"');
        }
        else
        {
            var idx = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            exe = idx > 0 ? text.Substring(0, idx + 4) : text;
        }
        return exe;
    }
}
