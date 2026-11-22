using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IPBanManager.Wpf.Views;

public partial class SaveBar : UserControl
{
    public static readonly DependencyProperty SaveTextProperty =
        DependencyProperty.Register(nameof(SaveText), typeof(string), typeof(SaveBar),
            new PropertyMetadata("Salvar (Ctrl+S)", (d, e) => ((SaveBar)d).SaveButton.Content = e.NewValue));

    public SaveBar() => InitializeComponent();

    public string SaveText
    {
        get => (string)GetValue(SaveTextProperty);
        set => SetValue(SaveTextProperty, value);
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Host()?.RequestSave();
    private void Reload_Click(object sender, RoutedEventArgs e) => Host()?.RequestReload();

    private ISaveBarHost Host()
    {
        DependencyObject node = this;
        while (node != null)
        {
            if (node is ISaveBarHost host) return host;
            node = VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node);
        }
        return null;
    }
}
