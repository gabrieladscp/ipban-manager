using System;
using System.Windows;
using IPBanManager.Core.Updates;
using IPBanManager.Core.Views;

namespace IPBanManager.Wpf.Views;

public partial class UpdatesView : ViewBase, IUpdateView
{
    private bool _loading;

    public UpdatesView() => InitializeComponent();

    public event EventHandler CheckRequested;
    public event EventHandler InstallRequested;
    public event EventHandler RollbackRequested;
    public event EventHandler SettingsChanged;

    public UpdateSettings Settings
    {
        get => new UpdateSettings
        {
            Channel = BetaRadio.IsChecked == true ? UpdateChannel.Beta : UpdateChannel.Stable,
            AutoCheck = AutoCheck.IsChecked == true,
            AutoDownload = AutoDownload.IsChecked == true,
            CheckIntervalHours = _intervalHours
        };
        set
        {
            _loading = true;
            try
            {
                _intervalHours = value.CheckIntervalHours;
                StableRadio.IsChecked = value.Channel == UpdateChannel.Stable;
                BetaRadio.IsChecked = value.Channel == UpdateChannel.Beta;
                AutoCheck.IsChecked = value.AutoCheck;
                AutoDownload.IsChecked = value.AutoDownload;
            }
            finally { _loading = false; }
        }
    }

    private int _intervalHours = 6;

    private bool _supported;

    public void ShowCurrent(string version, string installedChannel, bool supported, string unsupportedReason)
    {
        _supported = supported;
        VersionText.Text = version;
        ChannelText.Text = supported ? $"  ({installedChannel})" : "  (desenvolvimento)";
        UnsupportedBanner.Visibility = supported ? Visibility.Collapsed : Visibility.Visible;
        UnsupportedText.Text = unsupportedReason ?? "";
        CheckButton.IsEnabled = supported;
    }

    public void ShowStatus(string text) => StatusText.Text = text;

    public void ShowAvailable(AppUpdate update)
    {
        if (update == null)
        {
            AvailablePanel.Visibility = Visibility.Collapsed;
            return;
        }
        AvailablePanel.Visibility = Visibility.Visible;
        var size = update.SizeBytes > 0 ? $" ({update.SizeBytes / 1024.0 / 1024.0:0.0} MB)" : "";
        AvailableTitle.Text = $"Versão {update.Version}{size} - canal {update.Channel}";
        NotesBox.Text = string.IsNullOrWhiteSpace(update.NotesMarkdown) ? "(sem notas de versão)" : update.NotesMarkdown;
        InstallButton.Content = update.IsDowngrade ? "Instalar esta versão (anterior)" : "Baixar e instalar";
    }

    public void ShowProgress(int percent)
    {
        Progress.Visibility = percent < 0 ? Visibility.Collapsed : Visibility.Visible;
        Progress.Value = Math.Max(0, percent);
    }

    public void ShowRollback(RollbackInfo info)
    {
        RollbackBox.Visibility = info == null ? Visibility.Collapsed : Visibility.Visible;
        if (info != null) RollbackText.Text = $"A versão {info.Version} está preservada neste computador e pode ser reinstalada.";
    }

    public bool Busy
    {
        set
        {
            CheckButton.IsEnabled = _supported && !value;
            InstallButton.IsEnabled = !value;
            Cursor = value ? System.Windows.Input.Cursors.Wait : null;
        }
    }

    private void Settings_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading) SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Check_Click(object s, RoutedEventArgs e) => CheckRequested?.Invoke(this, EventArgs.Empty);
    private void Install_Click(object s, RoutedEventArgs e) => InstallRequested?.Invoke(this, EventArgs.Empty);
    private void Rollback_Click(object s, RoutedEventArgs e) => RollbackRequested?.Invoke(this, EventArgs.Empty);
}
