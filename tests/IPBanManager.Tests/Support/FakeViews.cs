using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IPBanManager.Core.Models;
using IPBanManager.Core.Views;

namespace IPBanManager.Tests.Support;

/// <summary>Base dos fakes de View: registra mensagens e permite aguardar eventos assíncronos sem Task.Delay.</summary>
internal abstract class FakeViewBase : IView
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly object _gate = new object();
    private readonly Queue<string> _unconsumed = new Queue<string>();
    private TaskCompletionSource<string> _waiter;

    public List<string> Errors { get; } = new List<string>();
    public List<string> Infos { get; } = new List<string>();
    public List<string> Warnings { get; } = new List<string>();
    public List<string> Questions { get; } = new List<string>();
    public bool ConfirmAnswer { get; set; } = true;

    public void ShowError(string message) { Errors.Add(message); Signal(message); }
    public void ShowInfo(string message) { Infos.Add(message); Signal(message); }
    public void ShowWarning(string message) { Warnings.Add(message); Signal(message); }

    /// <summary>Executado enquanto o diálogo de confirmação está "aberto" (permite simular eventos concorrentes).</summary>
    public Action OnConfirm { get; set; }

    public bool Confirm(string question)
    {
        Questions.Add(question);
        OnConfirm?.Invoke();
        return ConfirmAnswer;
    }

    /// <summary>
    /// Devolve a próxima mensagem ainda não consumida (erro/info/aviso). Funciona mesmo que a mensagem tenha
    /// sido exibida antes da chamada; falha com timeout se nunca vier.
    /// </summary>
    public async Task<string> NextMessageAsync()
    {
        TaskCompletionSource<string> waiter;
        lock (_gate)
        {
            if (_unconsumed.Count > 0) return _unconsumed.Dequeue();
            waiter = _waiter = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        var finished = await Task.WhenAny(waiter.Task, Task.Delay(Timeout));
        if (finished != waiter.Task) throw new TimeoutException("A View não recebeu nenhuma mensagem.");
        return await waiter.Task;
    }

    private void Signal(string message)
    {
        lock (_gate)
        {
            if (_waiter != null)
            {
                var waiter = _waiter;
                _waiter = null;
                waiter.TrySetResult(message);
            }
            else
            {
                _unconsumed.Enqueue(message);
            }
        }
    }
}

internal sealed class FakeMainView : FakeViewBase, IMainView
{
    public string InstallDirectory { get; set; }
    public string InstallStatus { get; set; }
    public bool InstallValid { get; set; }
    public string PickedDirectory { get; set; }
    public string PickDirectory(string current) => PickedDirectory;
    public event EventHandler ChangeDirectoryRequested;
    public void RaiseChangeDirectory() => ChangeDirectoryRequested?.Invoke(this, EventArgs.Empty);
}

internal sealed class FakeDashboardView : FakeViewBase, IDashboardView
{
    public ServiceState? LastState { get; private set; }
    public string LastStateText { get; private set; }
    public int? LastCount { get; private set; }
    public int ShowStateCalls { get; private set; }
    public bool Busy { get; set; }

    public event EventHandler StartRequested;
    public event EventHandler StopRequested;
    public event EventHandler RestartRequested;
    public event EventHandler RefreshRequested;
    public event EventHandler PollRequested;

    private readonly List<(ServiceState State, TaskCompletionSource<bool> Signal)> _stateWaiters =
        new List<(ServiceState, TaskCompletionSource<bool>)>();

    public void ShowState(ServiceState state, string text)
    {
        LastState = state;
        LastStateText = text;
        ShowStateCalls++;
        lock (_stateWaiters)
        {
            foreach (var waiter in _stateWaiters.FindAll(w => w.State == state)) waiter.Signal.TrySetResult(true);
        }
    }

    /// <summary>Aguarda (com timeout, sem sleeps) a View receber o estado informado.</summary>
    public async Task WaitForStateAsync(ServiceState state)
    {
        TaskCompletionSource<bool> signal;
        lock (_stateWaiters)
        {
            if (LastState == state) return;
            signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _stateWaiters.Add((state, signal));
        }
        if (await Task.WhenAny(signal.Task, Task.Delay(TimeSpan.FromSeconds(5))) != signal.Task)
            throw new TimeoutException($"A View não recebeu o estado {state}.");
    }
    public void ShowBannedCount(int count) => LastCount = count;
    public void RaiseStart() => StartRequested?.Invoke(this, EventArgs.Empty);
    public void RaiseStop() => StopRequested?.Invoke(this, EventArgs.Empty);
    public void RaiseRestart() => RestartRequested?.Invoke(this, EventArgs.Empty);
    public void RaiseRefresh() => RefreshRequested?.Invoke(this, EventArgs.Empty);
    public void RaisePoll() => PollRequested?.Invoke(this, EventArgs.Empty);
}

internal sealed class FakeSettingsView : FakeViewBase, ISettingsView
{
    public int AttemptsToApply { get; set; } = 5;
    private readonly TaskCompletionSource<IPBanConfiguration> _displayed =
        new TaskCompletionSource<IPBanConfiguration>(TaskCreationOptions.RunContinuationsAsynchronously);

    public IPBanConfiguration Displayed { get; private set; }
    public bool SavedMarked { get; private set; }
    public event EventHandler SaveRequested;
    public event EventHandler ReloadRequested;

    /// <summary>Completa quando Display for chamado pela primeira vez (com timeout).</summary>
    public async Task<IPBanConfiguration> WaitForDisplayAsync()
    {
        if (await Task.WhenAny(_displayed.Task, Task.Delay(TimeSpan.FromSeconds(5))) != _displayed.Task)
            throw new TimeoutException("A View nunca recebeu a configuração.");
        return await _displayed.Task;
    }

    public void Display(IPBanConfiguration configuration)
    {
        Displayed = configuration;
        _displayed.TrySetResult(configuration);
    }
    public void ApplyTo(IPBanConfiguration configuration) => configuration.FailedLoginAttemptsBeforeBan = AttemptsToApply;
    public void MarkSaved() => SavedMarked = true;
    public void RaiseSave() => SaveRequested?.Invoke(this, EventArgs.Empty);
    public void RaiseReload() => ReloadRequested?.Invoke(this, EventArgs.Empty);
}

internal sealed class FakeBannedIpsView : FakeViewBase, IBannedIpsView
{
    public string ManualBanAddress { get; set; } = "";
    public IReadOnlyList<string> SelectedAddresses { get; set; } = new string[0];
    public IReadOnlyList<BannedIp> Shown { get; private set; }
    public event EventHandler RefreshRequested;
    public event EventHandler BanRequested;
    public event EventHandler UnbanRequested;

    public void ShowBannedIps(IReadOnlyList<BannedIp> items) => Shown = items;
    public void RaiseBan() => BanRequested?.Invoke(this, EventArgs.Empty);
    public void RaiseUnban() => UnbanRequested?.Invoke(this, EventArgs.Empty);
    public void RaiseRefresh() => RefreshRequested?.Invoke(this, EventArgs.Empty);
}

internal sealed class FakeAdvancedView : FakeViewBase, IAdvancedView
{
    public string Xml { get; set; } = "";
    public event EventHandler SaveRequested;
    public event EventHandler ReloadRequested;
    public void RaiseSave() => SaveRequested?.Invoke(this, EventArgs.Empty);
    public void RaiseReload() => ReloadRequested?.Invoke(this, EventArgs.Empty);
}

internal sealed class FakeLogView : FakeViewBase, ILogView
{
    public string LogText { get; set; }
    public string Filter { get; set; } = "";
    public event EventHandler RefreshRequested;
    public void RaiseRefresh() => RefreshRequested?.Invoke(this, EventArgs.Empty);
}
