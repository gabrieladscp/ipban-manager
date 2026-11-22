using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using IPBanManager.Core.Validation;

namespace IPBanManager.Wpf.Views;

/// <summary>Editor de duração por campos numéricos; expõe o valor no formato DD:HH:MM:SS do IPBan.</summary>
public partial class DurationBox : UserControl
{
    private string _unparsableOriginal; // valor fora do formato, preservado até o usuário editar
    private bool _loading;

    public DurationBox()
    {
        InitializeComponent();
        AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler((s, e) => { if (!_loading) _unparsableOriginal = null; }));
    }

    public string Value
    {
        get => _unparsableOriginal ?? $"{Part(Days):00}:{Part(Hours):00}:{Part(Minutes):00}:{Part(Seconds):00}";
        set
        {
            _loading = true;
            try
            {
                if (TimeSpanFormat.TryParse(value, out var span))
                {
                    _unparsableOriginal = null;
                    Days.Text = span.Days.ToString();
                    Hours.Text = span.Hours.ToString();
                    Minutes.Text = span.Minutes.ToString();
                    Seconds.Text = span.Seconds.ToString();
                }
                else
                {
                    _unparsableOriginal = value;
                    Days.Text = Hours.Text = Minutes.Text = Seconds.Text = "";
                }
            }
            finally { _loading = false; }
        }
    }

    private static int Part(TextBox box) => int.TryParse(box.Text, out var v) ? v : 0;

    private void Digits_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsDigit);
}
