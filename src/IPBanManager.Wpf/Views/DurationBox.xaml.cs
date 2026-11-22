using System;
using System.Windows;
using System.Windows.Controls;
using IPBanManager.Core.Validation;

namespace IPBanManager.Wpf.Views;

/// <summary>
/// Editor de duração por campos numéricos; expõe o valor no formato DD:HH:MM:SS do IPBan.
/// Valores excedentes são convertidos ao sair do campo (ex.: 90 min vira 1 h 30 min).
/// </summary>
public partial class DurationBox : UserControl
{
    private const int MaxDays = 9999;
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
        get => _unparsableOriginal ?? Format(Total());
        set
        {
            if (TimeSpanFormat.TryParse(value, out var span)) Show(span);
            else
            {
                _loading = true;
                try
                {
                    _unparsableOriginal = value;
                    Days.Text = Hours.Text = Minutes.Text = Seconds.Text = "";
                }
                finally { _loading = false; }
            }
        }
    }

    public TimeSpan Span => Total();

    public void Clear() => Show(TimeSpan.Zero);

    private TimeSpan Total()
    {
        var total = TimeSpan.FromDays(Part(Days)) + TimeSpan.FromHours(Part(Hours))
                    + TimeSpan.FromMinutes(Part(Minutes)) + TimeSpan.FromSeconds(Part(Seconds));
        var max = TimeSpan.FromDays(MaxDays) + new TimeSpan(23, 59, 59);
        return total > max ? max : total;
    }

    private static string Format(TimeSpan s) => $"{s.Days:00}:{s.Hours:00}:{s.Minutes:00}:{s.Seconds:00}";

    private void Show(TimeSpan span)
    {
        _loading = true;
        try
        {
            _unparsableOriginal = null;
            SetIfDifferent(Days, span.Days);
            SetIfDifferent(Hours, span.Hours);
            SetIfDifferent(Minutes, span.Minutes);
            SetIfDifferent(Seconds, span.Seconds);
        }
        finally { _loading = false; }
    }

    private static void SetIfDifferent(TextBox box, int value)
    {
        var text = value.ToString();
        if (box.Text != text) box.Text = text;
    }

    private static int Part(TextBox box) => int.TryParse(box.Text, out var v) ? v : 0;

    private void Part_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_unparsableOriginal == null) Show(Total()); // normaliza 90 min -> 1 h 30 min, campos vazios -> 0
    }
}
