using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using IPBanManager.Core.Models;
using IPBanManager.Core.Views;

namespace IPBanManager.Wpf.Views;

public partial class DashboardView : ViewBase, IDashboardView
{
    private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };

    public DashboardView()
    {
        InitializeComponent();
        _timer.Tick += (s, e) => PollRequested?.Invoke(this, EventArgs.Empty);
        // O timer só roda com a aba visível: sem consumo de CPU/firewall em segundo plano.
        Loaded += (s, e) => { RefreshRequested?.Invoke(this, EventArgs.Empty); _timer.Start(); };
        Unloaded += (s, e) => _timer.Stop();
    }

    public event EventHandler StartRequested;
    public event EventHandler StopRequested;
    public event EventHandler RestartRequested;
    public event EventHandler RefreshRequested;
    public event EventHandler PollRequested;

    public bool Busy
    {
        set
        {
            StartButton.IsEnabled = StopButton.IsEnabled = RestartButton.IsEnabled = !value;
            RefreshButton.IsEnabled = !value;
            Cursor = value ? System.Windows.Input.Cursors.Wait : null;
        }
    }

    public void ShowState(ServiceState state, string text)
    {
        StateText.Text = text;
        Indicator.Fill = state == ServiceState.Running ? Brushes.SeaGreen
            : state == ServiceState.Stopped ? Brushes.Firebrick
            : state == ServiceState.Transitioning ? Brushes.Orange : Brushes.Gray;
        StartButton.IsEnabled = state == ServiceState.Stopped;
        StopButton.IsEnabled = state == ServiceState.Running;
        RestartButton.IsEnabled = state == ServiceState.Running;
    }

    public void ShowBannedCount(int count) => CountText.Text = count.ToString();

    public event EventHandler UpgradeIPBanRequested;

    public void ShowIPBanVersion(string installed, string upgradeTo)
    {
        VersionPanel.Visibility = installed == null ? Visibility.Collapsed : Visibility.Visible;
        VersionText.Text = installed ?? "";
        UpgradeButton.Visibility = upgradeTo == null ? Visibility.Collapsed : Visibility.Visible;
        UpgradeButton.Content = "Atualizar para " + upgradeTo + "...";
    }

    private void Upgrade_Click(object s, RoutedEventArgs e) => UpgradeIPBanRequested?.Invoke(this, EventArgs.Empty);

    private void Start_Click(object s, RoutedEventArgs e) => StartRequested?.Invoke(this, EventArgs.Empty);
    private void Stop_Click(object s, RoutedEventArgs e) => StopRequested?.Invoke(this, EventArgs.Empty);
    private void Restart_Click(object s, RoutedEventArgs e) => RestartRequested?.Invoke(this, EventArgs.Empty);
    private void Refresh_Click(object s, RoutedEventArgs e) => RefreshRequested?.Invoke(this, EventArgs.Empty);
}
