using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using IPBanManager.Core.Views;

namespace IPBanManager.Wpf.Views;

public partial class IPBanSetupWindow : Window, IIPBanSetupView
{
    private const string Caption = "IPBan Manager";
    private bool _busy;
    private bool _finished;

    public IPBanSetupWindow()
    {
        InitializeComponent();
    }

    public event EventHandler InstallRequested;

    public void ShowPlan(string heading, string description, string actionLabel)
    {
        HeadingText.Text = heading;
        DescriptionText.Text = description;
        InstallButton.Content = actionLabel;
    }

    public string WhitelistText
    {
        get => WhitelistBox.Text;
        set => WhitelistBox.Text = value ?? "";
    }

    public void AppendProgress(string line)
    {
        ProgressBox.AppendText((ProgressBox.Text.Length > 0 ? Environment.NewLine : "") + line);
        ProgressBox.ScrollToEnd();
    }

    public bool Busy
    {
        set
        {
            _busy = value;
            InstallButton.IsEnabled = !value && !_finished;
            CloseButton.IsEnabled = !value;
            WhitelistBox.IsReadOnly = value || _finished;
            Cursor = value ? Cursors.Wait : null;
        }
    }

    public void ShowFinished(bool success)
    {
        if (!success) return; // permite corrigir e tentar de novo
        _finished = true;
        InstallButton.Visibility = Visibility.Collapsed;
        CloseButton.Content = "Fechar";
        CloseButton.IsDefault = true;
        WhitelistBox.IsReadOnly = true;
    }

    public void ShowError(string message) => MessageBox.Show(this, message, Caption, MessageBoxButton.OK, MessageBoxImage.Error);
    public void ShowInfo(string message) => AppendProgress(message);
    public void ShowWarning(string message) => AppendProgress("⚠ " + message);
    public bool Confirm(string question) =>
        MessageBox.Show(this, question, Caption, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    private void Install_Click(object sender, RoutedEventArgs e) => InstallRequested?.Invoke(this, EventArgs.Empty);
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        // Interromper no meio deixaria o serviço parado: só fecha quando a operação terminar.
        if (_busy) e.Cancel = true;
    }
}
