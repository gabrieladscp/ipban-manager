using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using IPBanManager.Core.Updates;
using IPBanManager.Core.Views;
using IPBanManager.Wpf.Views;
using Forms = System.Windows.Forms;

namespace IPBanManager.Wpf;

public partial class MainWindow : Window, IMainView, IStatusSink, IUnsavedChangesProbe
{
    private readonly DispatcherTimer _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };

    public MainWindow()
    {
        InitializeComponent();
        Title = "IPBan Manager " + AppVersion.Current;
        _statusTimer.Tick += (s, e) => { _statusTimer.Stop(); StatusBarText.Text = "Pronto."; StatusBarText.Foreground = Brushes.Black; };
    }

    public event EventHandler ChangeDirectoryRequested;

    public string InstallDirectory { set => DirectoryText.Text = value; }

    public string InstallStatus { set => BannerText.Text = value; }

    public bool InstallValid
    {
        set
        {
            Banner.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            Tabs.IsEnabled = value;
        }
    }

    public void AddTab(string header, UserControl content) =>
        Tabs.Items.Add(new TabItem { Header = header, Content = content });

    public void ShowStatus(string message, bool isWarning)
    {
        StatusBarText.Text = (isWarning ? "⚠ " : "✔ ") + message.Replace('\n', ' ');
        StatusBarText.Foreground = isWarning ? Brushes.DarkOrange : Brushes.SeaGreen;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    public string PickDirectory(string current)
    {
        using (var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Selecione a pasta onde o IPBan está instalado (contém o arquivo ipban.config)",
            SelectedPath = current ?? ""
        })
        {
            return dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.SelectedPath : null;
        }
    }

    public void ShowError(string message) => MessageBox.Show(this, message, "IPBan Manager", MessageBoxButton.OK, MessageBoxImage.Error);
    public void ShowInfo(string message) => ShowStatus(message, false);
    public void ShowWarning(string message) => ShowStatus(message, true);
    public bool Confirm(string question) =>
        MessageBox.Show(this, question, "IPBan Manager", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    private void Change_Click(object sender, RoutedEventArgs e) => ChangeDirectoryRequested?.Invoke(this, EventArgs.Empty);

    public bool HasUnsavedChanges =>
        Tabs.Items.OfType<TabItem>().Select(t => t.Content).OfType<ISaveBarHost>().Any(h => h.IsDirty);

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (HasUnsavedChanges && !Confirm("Há alterações não salvas. Sair mesmo assim?"))
            e.Cancel = true;
    }
}
