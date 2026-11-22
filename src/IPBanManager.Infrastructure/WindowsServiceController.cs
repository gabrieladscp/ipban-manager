using System;
using System.ComponentModel;
using System.ServiceProcess;
using IPBanManager.Core.Abstractions;
using IPBanManager.Core.Errors;
using IPBanManager.Core.Models;

namespace IPBanManager.Infrastructure;

public class WindowsServiceController : IServiceController
{
    private const int ErrorAccessDenied = 5;
    private const int ErrorServiceDoesNotExist = 1060;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly IInstallationLocator _locator;

    public WindowsServiceController(IInstallationLocator locator) => _locator = locator;

    public ServiceState GetState()
    {
        try
        {
            using (var sc = new ServiceController(_locator.ServiceName))
            {
                switch (sc.Status)
                {
                    case ServiceControllerStatus.Running: return ServiceState.Running;
                    case ServiceControllerStatus.Stopped: return ServiceState.Stopped;
                    default: return ServiceState.Transitioning;
                }
            }
        }
        catch (InvalidOperationException ex) when (NativeCode(ex) == ErrorServiceDoesNotExist)
        {
            return ServiceState.NotInstalled;
        }
        catch (InvalidOperationException ex)
        {
            throw Translate(ex, "consultar");
        }
    }

    public void Start() => Execute("iniciar", ServiceControllerStatus.Running, sc =>
    {
        if (sc.Status == ServiceControllerStatus.Running) return false;
        sc.Start();
        return true;
    });

    public void Stop() => Execute("parar", ServiceControllerStatus.Stopped, sc =>
    {
        if (sc.Status == ServiceControllerStatus.Stopped) return false;
        sc.Stop();
        return true;
    });

    public void Restart()
    {
        Stop();
        Start();
    }

    private void Execute(string verb, ServiceControllerStatus target, Func<ServiceController, bool> action)
    {
        try
        {
            using (var sc = new ServiceController(_locator.ServiceName))
            {
                if (action(sc)) sc.WaitForStatus(target, Timeout);
            }
        }
        catch (System.ServiceProcess.TimeoutException ex)
        {
            throw new ManagerException(
                $"O serviço {_locator.ServiceName} não terminou de {verb} em {Timeout.TotalSeconds:0} segundos. " +
                "Verifique o status no Painel ou no Gerenciador de Serviços do Windows.", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw Translate(ex, verb);
        }
    }

    private ManagerException Translate(InvalidOperationException ex, string verb)
    {
        switch (NativeCode(ex))
        {
            case ErrorAccessDenied:
                return new ManagerException($"Acesso negado ao {verb} o serviço. Execute o IPBan Manager como Administrador.", ex);
            case ErrorServiceDoesNotExist:
                return new ManagerException($"O serviço {_locator.ServiceName} não está instalado neste servidor.", ex);
            default:
                return new ManagerException($"Não foi possível {verb} o serviço {_locator.ServiceName}: {ex.InnerException?.Message ?? ex.Message}", ex);
        }
    }

    private static int NativeCode(Exception ex) => (ex.InnerException as Win32Exception)?.NativeErrorCode ?? 0;
}
