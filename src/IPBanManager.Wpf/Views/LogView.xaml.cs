using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IPBanManager.Core.Views;

namespace IPBanManager.Wpf.Views;

public partial class LogView : ViewBase, ILogView
{
    private readonly DispatcherTimer _autoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };

    public LogView()
    {
        InitializeComponent();
        _autoTimer.Tick += (s, e) => Raise();
        _debounce.Tick += (s, e) => { _debounce.Stop(); Raise(); };
        Loaded += (s, e) =>
        {
            Raise();
            if (AutoRefresh.IsChecked == true) _autoTimer.Start();
        };
        // Sem aba visível não há leitura periódica do arquivo de log.
        Unloaded += (s, e) => { _autoTimer.Stop(); _debounce.Stop(); };
    }

    public event EventHandler RefreshRequested;

    public string Filter => FilterBox.Text;

    public string LogText
    {
        set
        {
            LogBox.Text = value;
            LogBox.ScrollToEnd();
        }
    }

    private void Raise() => RefreshRequested?.Invoke(this, EventArgs.Empty);

    private void Refresh_Click(object s, RoutedEventArgs e) => Raise();

    private void Filter_TextChanged(object s, TextChangedEventArgs e)
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void AutoRefresh_Changed(object s, RoutedEventArgs e)
    {
        if (AutoRefresh.IsChecked == true) _autoTimer.Start();
        else _autoTimer.Stop();
    }
}
