using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IPBanManager.Wpf.Views;

/// <summary>Campo de número inteiro: só dígitos, ▲▼ / setas / roda do mouse, valor sempre dentro dos limites.</summary>
public partial class NumericBox : UserControl
{
    private int _minimum;
    private int _maximum = 9999;

    public NumericBox()
    {
        InitializeComponent();
        UpdateRange();
    }

    public int Minimum { get => _minimum; set { _minimum = value; UpdateRange(); } }
    public int Maximum { get => _maximum; set { _maximum = value; UpdateRange(); } }

    /// <summary>Valor atual, sempre limitado a [Minimum, Maximum].</summary>
    public int Value
    {
        get => Clamp(int.TryParse(Box.Text, out var v) ? v : _minimum);
        set => Box.Text = Clamp(value).ToString();
    }

    private int Clamp(int v) => Math.Max(_minimum, Math.Min(_maximum, v));

    private void UpdateRange()
    {
        RangeText.Text = $"({_minimum} a {_maximum})";
        Box.MaxLength = Math.Max(1, _maximum.ToString().Length);
    }

    private void Step(int delta)
    {
        Value = Value + delta;
        Box.CaretIndex = Box.Text.Length;
    }

    private void Up_Click(object sender, RoutedEventArgs e) => Step(1);
    private void Down_Click(object sender, RoutedEventArgs e) => Step(-1);

    private void Box_LostFocus(object sender, RoutedEventArgs e)
    {
        var normalized = Value.ToString();
        if (Box.Text != normalized) Box.Text = normalized; // vazio ou fora dos limites vira o valor válido mais próximo
    }

    private void Box_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Up) { Step(1); e.Handled = true; }
        else if (e.Key == Key.Down) { Step(-1); e.Handled = true; }
    }

    private void Box_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Box.IsKeyboardFocusWithin) return;
        Step(e.Delta > 0 ? 1 : -1);
        e.Handled = true;
    }
}
