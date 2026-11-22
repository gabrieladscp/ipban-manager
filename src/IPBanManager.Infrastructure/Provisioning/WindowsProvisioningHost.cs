using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using IPBanManager.Core.Errors;
using Microsoft.Win32;

namespace IPBanManager.Infrastructure.Provisioning;

/// <summary>Operações do sistema operacional usadas na instalação do IPBan.</summary>
public interface IProvisioningHost
{
    bool IsAdministrator { get; }
    bool Is64BitOperatingSystem { get; }
    string ProgramFilesDirectory { get; }

    /// <summary>ImagePath do serviço, ou null se ele não existir.</summary>
    string GetServiceImagePath(string serviceName);

    Version GetFileVersion(string path);
    void StopService(string serviceName);
    void StartService(string serviceName);

    /// <summary>Cria o serviço (ou corrige o executável de um existente) com início automático atrasado e reinício em falha.</summary>
    void RegisterService(string serviceName, string executablePath);

    /// <summary>Ativa a auditoria de logon/logoff (eventos 4625 que o IPBan lê). Retorna o erro, ou null se OK.</summary>
    string EnableLogonAuditing();

    /// <summary>Perfis do Firewall do Windows desativados (o IPBan bloqueia pelo firewall).</summary>
    IReadOnlyList<string> GetDisabledFirewallProfiles();
}

public sealed class WindowsProvisioningHost : IProvisioningHost
{
    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(60);

    // Categorias Logon/Logoff e Account Logon (as mesmas do script oficial do IPBan).
    private static readonly string[] AuditCategories =
        { "{69979849-797A-11D9-BED3-505054503030}", "{69979850-797A-11D9-BED3-505054503030}" };

    public bool IsAdministrator
    {
        get
        {
            using (var identity = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public bool Is64BitOperatingSystem => Environment.Is64BitOperatingSystem;

    public string ProgramFilesDirectory
    {
        get
        {
            // Processo 32 bits enxergaria "Program Files (x86)"; o IPBan é x64.
            var p = Environment.GetEnvironmentVariable("ProgramW6432");
            return string.IsNullOrEmpty(p) ? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) : p;
        }
    }

    public string GetServiceImagePath(string serviceName)
    {
        using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + serviceName))
            return key?.GetValue("ImagePath") as string;
    }

    public Version GetFileVersion(string path)
    {
        if (!File.Exists(path)) return null;
        var info = FileVersionInfo.GetVersionInfo(path);
        return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, Math.Max(0, info.FilePrivatePart));
    }

    public void StopService(string serviceName)
    {
        using (var sc = new ServiceController(serviceName))
        {
            sc.Refresh();
            if (sc.Status == ServiceControllerStatus.Stopped) return;
            if (sc.Status != ServiceControllerStatus.StopPending) sc.Stop();
            WaitFor(sc, ServiceControllerStatus.Stopped, "parar");
        }
    }

    public void StartService(string serviceName)
    {
        using (var sc = new ServiceController(serviceName))
        {
            sc.Refresh();
            if (sc.Status == ServiceControllerStatus.Running) return;
            if (sc.Status != ServiceControllerStatus.StartPending) sc.Start();
            WaitFor(sc, ServiceControllerStatus.Running, "iniciar");
        }
    }

    private static void WaitFor(ServiceController sc, ServiceControllerStatus status, string verb)
    {
        try { sc.WaitForStatus(status, ServiceTimeout); }
        catch (System.ServiceProcess.TimeoutException ex)
        {
            throw new ManagerException($"O serviço {sc.ServiceName} não conseguiu {verb} em {ServiceTimeout.TotalSeconds:0} segundos.", ex);
        }
    }

    public void RegisterService(string serviceName, string executablePath)
    {
        var binPath = "\"" + executablePath + "\"";
        if (GetServiceImagePath(serviceName) == null)
            Sc($"create {serviceName} binPath= \"{Escape(binPath)}\" start= delayed-auto DisplayName= \"IPBan\"");
        else
            Sc($"config {serviceName} binPath= \"{Escape(binPath)}\" start= delayed-auto");

        Sc($"description {serviceName} \"Bloqueia automaticamente no firewall os IPs com tentativas de login abusivas (IPBan).\"");
        // Reinicia após 1 minuto nas 3 primeiras falhas; zera a contagem após 1 dia.
        Sc($"failure {serviceName} reset= 86400 actions= restart/60000/restart/60000/restart/60000");
    }

    private static string Escape(string text) => text.Replace("\"", "\\\"");

    private static void Sc(string arguments)
    {
        var (code, output) = Run("sc.exe", arguments);
        if (code != 0)
            throw new ManagerException($"Falha ao configurar o serviço (sc.exe {arguments.Split(' ')[0]}, código {code}): {output.Trim()}");
    }

    public string EnableLogonAuditing()
    {
        var errors = new List<string>();
        foreach (var category in AuditCategories)
        {
            var (code, output) = Run("auditpol.exe", $"/set /category:\"{category}\" /success:enable /failure:enable");
            if (code != 0) errors.Add(output.Trim());
        }
        return errors.Count == 0 ? null : string.Join(" ", errors);
    }

    public IReadOnlyList<string> GetDisabledFirewallProfiles()
    {
        var disabled = new List<string>();
        var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
        if (type == null) return disabled;
        object policy = null;
        try
        {
            policy = Activator.CreateInstance(type);
            dynamic fw = policy;
            // NET_FW_PROFILE2_DOMAIN = 1, PRIVATE = 2, PUBLIC = 4
            foreach (var (id, name) in new[] { (1, "Domínio"), (2, "Privado"), (4, "Público") })
                if (!(bool)fw.FirewallEnabled[id]) disabled.Add(name);
        }
        finally
        {
            if (policy != null && Marshal.IsComObject(policy)) Marshal.ReleaseComObject(policy);
        }
        return disabled;
    }

    private static (int Code, string Output) Run(string exe, string arguments)
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, exe), arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using (var p = Process.Start(psi))
        {
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(60000))
            {
                try { p.Kill(); } catch (InvalidOperationException) { }
                return (-1, exe + " não respondeu em 60 segundos.");
            }
            return (p.ExitCode, stdout.Result + stderr.Result);
        }
    }
}
