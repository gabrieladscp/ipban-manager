using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using IPBanManager.Core.Errors;

namespace IPBanManager.Infrastructure.Provisioning;

/// <summary>
/// Copia os arquivos do pacote oficial do IPBan para a pasta de instalação.
/// Configurações, banco de dados e logs nunca são tocados; os binários antigos são movidos para uma pasta de
/// backup (e voltam se algo falhar).
/// </summary>
public class IPBanFileDeployer
{
    /// <summary>Arquivos do usuário que vêm no pacote, mas não podem sobrescrever os existentes.</summary>
    public static readonly ISet<string> PreservedFiles =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ipban.config", "ipban.override.config", "nlog.config" };

    /// <summary>Extensões de binários/artefatos de programa (o resto da pasta é dado do usuário e fica onde está).</summary>
    private static readonly ISet<string> BinaryExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".exe", ".dll", ".pdb", ".json", ".xml" };

    private const int MaxBackups = 3;
    private readonly int _retries;
    private readonly TimeSpan _retryDelay;

    public IPBanFileDeployer(int retries = 10, TimeSpan? retryDelay = null)
    {
        _retries = retries;
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(500);
    }

    /// <summary>Confere o SHA-256 do pacote; lança se o arquivo não existir ou tiver sido alterado.</summary>
    public static void VerifyBundle(string zipPath, string expectedSha256)
    {
        if (string.IsNullOrEmpty(zipPath) || !File.Exists(zipPath))
            throw new ManagerException("O pacote do IPBan não foi encontrado junto ao aplicativo. Reinstale o IPBan Manager.");
        if (string.IsNullOrWhiteSpace(expectedSha256))
            throw new ManagerException("Esta compilação do IPBan Manager não contém o hash do pacote do IPBan.");

        string actual;
        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead(zipPath))
            actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        if (!string.Equals(actual, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ManagerException("O pacote do IPBan está corrompido ou foi alterado (SHA-256 não confere). " +
                                       "A instalação foi cancelada por segurança. Reinstale o IPBan Manager.");
    }

    /// <summary>
    /// Implanta o pacote em <paramref name="targetDir"/>. Retorna a pasta de backup com os binários anteriores
    /// (null em instalação nova).
    /// </summary>
    public string Deploy(string zipPath, string targetDir, string backupRoot)
    {
        Directory.CreateDirectory(targetDir);
        var staging = Path.Combine(targetDir, ".ipbanmanager-staging");
        DeleteDirectory(staging);
        ZipFile.ExtractToDirectory(zipPath, staging);

        string backup = null;
        try
        {
            backup = MoveExistingBinaries(targetDir, backupRoot, staging);
            foreach (var file in Directory.GetFiles(staging, "*", SearchOption.AllDirectories))
            {
                var relative = file.Substring(staging.Length + 1);
                var dest = Path.Combine(targetDir, relative);
                if (PreservedFiles.Contains(relative) && File.Exists(dest)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                Retry(() => File.Copy(file, dest, overwrite: true));
            }
            return backup;
        }
        catch
        {
            if (backup != null) Restore(backup, targetDir);
            throw;
        }
        finally
        {
            DeleteDirectory(staging);
        }
    }

    /// <summary>Devolve os binários guardados por <see cref="Deploy"/> (desfaz a atualização).</summary>
    public void Restore(string backupDir, string targetDir)
    {
        if (backupDir == null || !Directory.Exists(backupDir)) return;
        foreach (var file in Directory.GetFiles(backupDir, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(targetDir, file.Substring(backupDir.Length + 1));
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            Retry(() => File.Copy(file, dest, overwrite: true));
        }
    }

    /// <summary>
    /// Move binários e subpastas de recursos da versão anterior para o backup, para não sobrarem arquivos antigos
    /// misturados ao executável novo. Dados (config, sqlite, logs, txt) ficam.
    /// </summary>
    private string MoveExistingBinaries(string targetDir, string backupRoot, string staging)
    {
        var files = Directory.GetFiles(targetDir)
            .Where(f => BinaryExtensions.Contains(Path.GetExtension(f)) && !PreservedFiles.Contains(Path.GetFileName(f)))
            .ToList();
        var dirs = Directory.GetDirectories(targetDir)
            .Where(d => !string.Equals(d, staging, StringComparison.OrdinalIgnoreCase)
                        && Directory.GetFiles(d, "*", SearchOption.AllDirectories)
                            .All(f => BinaryExtensions.Contains(Path.GetExtension(f))))
            .ToList();
        if (files.Count == 0 && dirs.Count == 0) return null;

        var backup = Path.Combine(backupRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(backup);
        try
        {
            foreach (var f in files) Retry(() => File.Move(f, Path.Combine(backup, Path.GetFileName(f))));
            foreach (var d in dirs) Retry(() => Directory.Move(d, Path.Combine(backup, Path.GetFileName(d))));
        }
        catch
        {
            Restore(backup, targetDir);
            throw;
        }
        PruneBackups(backupRoot);
        return backup;
    }

    private static void PruneBackups(string backupRoot)
    {
        foreach (var old in new DirectoryInfo(backupRoot).GetDirectories()
                     .OrderByDescending(d => d.Name).Skip(MaxBackups))
        {
            try { old.Delete(true); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }
    }

    private static void DeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
    }

    /// <summary>O executável pode continuar bloqueado alguns instantes depois que o serviço para.</summary>
    private void Retry(Action action)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { action(); return; }
            catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < _retries)
            {
                Thread.Sleep(_retryDelay);
            }
        }
    }
}
