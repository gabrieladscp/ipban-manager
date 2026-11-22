using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using IPBanManager.Core.Models;
using IPBanManager.Core.Views;

namespace IPBanManager.Wpf.Views;

/// <summary>Barra de status da janela principal (mensagens não intrusivas).</summary>
public interface IStatusSink
{
    void ShowStatus(string message, bool isWarning);
}

/// <summary>Implementa os diálogos comuns de IView.</summary>
public class ViewBase : UserControl, IView
{
    private const string Title = "IPBan Manager";

    public void ShowError(string message) =>
        MessageBox.Show(Owner(), message, Title, MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInfo(string message) => Status()?.ShowStatus(message, false);

    public void ShowWarning(string message) => Status()?.ShowStatus(message, true);

    public bool Confirm(string question) =>
        MessageBox.Show(Owner(), question, Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
        == MessageBoxResult.Yes;

    private Window Owner() => Window.GetWindow(this) ?? Application.Current?.MainWindow;
    private IStatusSink Status() => Owner() as IStatusSink;

    protected static string[] Lines(string text) =>
        (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();

    protected static int ParseInt(string text) => int.TryParse((text ?? "").Trim(), out var v) ? v : -1;
}

/// <summary>Contrato usado pela SaveBar e pela janela (aviso de alterações não salvas).</summary>
public interface ISaveBarHost
{
    bool IsDirty { get; }
    void RequestSave();
    void RequestReload();
}

/// <summary>Base de telas editáveis: rastreia alterações não salvas e atende Ctrl+S.</summary>
public abstract class EditableViewBase : ViewBase, ISaveBarHost
{
    public static readonly DependencyProperty IsDirtyProperty =
        DependencyProperty.Register(nameof(IsDirty), typeof(bool), typeof(EditableViewBase), new PropertyMetadata(false));

    private int _suppress;

    protected EditableViewBase()
    {
        AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, e) => MarkDirty()));
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((s, e) => MarkDirty()));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler((s, e) => MarkDirty()));
        AddHandler(EntryListEditor.ItemsChangedEvent, new RoutedEventHandler((s, e) => MarkDirty()));
        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                RequestSave();
            }
        };
    }

    public bool IsDirty
    {
        get => (bool)GetValue(IsDirtyProperty);
        private set => SetValue(IsDirtyProperty, value);
    }

    public abstract void RequestSave();
    public abstract void RequestReload();

    /// <summary>Executa uma carga programática de campos sem marcar a tela como alterada.</summary>
    protected void WithoutDirtyTracking(Action load)
    {
        _suppress++;
        try { load(); }
        finally
        {
            // TextChanged é síncrono, então já foi disparado dentro do bloco.
            _suppress--;
            IsDirty = false;
        }
    }

    public void MarkSaved() => IsDirty = false;

    private void MarkDirty()
    {
        if (_suppress == 0) IsDirty = true;
    }
}

/// <summary>Base das telas de configuração com botões Salvar/Recarregar.</summary>
public abstract class ConfigSectionViewBase : EditableViewBase, IConfigSectionView
{
    public event EventHandler SaveRequested;
    public event EventHandler ReloadRequested;

    protected abstract void Populate(IPBanConfiguration configuration);
    protected abstract void Collect(IPBanConfiguration configuration);

    public void Display(IPBanConfiguration configuration) => WithoutDirtyTracking(() => Populate(configuration));
    public void ApplyTo(IPBanConfiguration configuration) => Collect(configuration);

    public override void RequestSave() => SaveRequested?.Invoke(this, EventArgs.Empty);
    public override void RequestReload()
    {
        if (IsDirty && !Confirm("Descartar as alterações não salvas e recarregar do arquivo?")) return;
        ReloadRequested?.Invoke(this, EventArgs.Empty);
    }

    protected static string JoinLines(IEnumerable<string> items) => string.Join(Environment.NewLine, items);
}
