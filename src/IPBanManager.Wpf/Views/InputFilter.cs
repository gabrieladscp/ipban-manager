using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IPBanManager.Core.Validation;

namespace IPBanManager.Wpf.Views;

public enum InputFilterKind
{
    None,
    Digits,
    Address,
    IpAddress,
    IpList,
    RuleName,
    Ports,
    NoSpaces,
}

/// <summary>
/// Comportamento anexável a um TextBox: bloqueia a digitação e a colagem de caracteres não permitidos.
/// Uso: &lt;TextBox local:InputFilter.Kind="Digits"/&gt;
/// </summary>
public static class InputFilter
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.RegisterAttached(
        "Kind", typeof(InputFilterKind), typeof(InputFilter), new PropertyMetadata(InputFilterKind.None, OnKindChanged));

    public static InputFilterKind GetKind(DependencyObject d) => (InputFilterKind)d.GetValue(KindProperty);
    public static void SetKind(DependencyObject d, InputFilterKind value) => d.SetValue(KindProperty, value);

    public static bool IsAllowed(InputFilterKind kind, char c)
    {
        switch (kind)
        {
            case InputFilterKind.Digits: return c >= '0' && c <= '9';
            case InputFilterKind.Address: return ListEntryRules.IsAllowedChar(ListEntryKind.Address, c);
            case InputFilterKind.IpAddress: return IsIpChar(c);
            case InputFilterKind.IpList: return IsIpChar(c) || c == ',';
            case InputFilterKind.RuleName: return RuleName.IsAllowedChar(c);
            case InputFilterKind.Ports: return FirewallRuleEntry.IsAllowedPortChar(c);
            case InputFilterKind.NoSpaces: return !char.IsWhiteSpace(c) && !char.IsControl(c);
            default: return true;
        }
    }

    private static bool IsIpChar(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F') || c == '.' || c == ':' || c == '/';

    private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (!(d is TextBox box)) return;
        box.PreviewTextInput -= OnPreviewTextInput;
        box.PreviewKeyDown -= OnPreviewKeyDown;
        DataObject.RemovePastingHandler(box, OnPaste);
        if ((InputFilterKind)e.NewValue == InputFilterKind.None) return;
        box.PreviewTextInput += OnPreviewTextInput;
        box.PreviewKeyDown += OnPreviewKeyDown;
        DataObject.AddPastingHandler(box, OnPaste);
    }

    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var kind = GetKind((DependencyObject)sender);
        if (e.Text.Any(c => !IsAllowed(kind, c))) e.Handled = true;
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Espaço não passa pelo PreviewTextInput.
        if (e.Key == Key.Space && !IsAllowed(GetKind((DependencyObject)sender), ' ')) e.Handled = true;
    }

    private static void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        var box = (TextBox)sender;
        var kind = GetKind(box);
        var text = e.SourceDataObject.GetData(DataFormats.UnicodeText) as string;
        e.CancelCommand();
        if (string.IsNullOrEmpty(text)) return;
        var clean = new string(text.Where(c => IsAllowed(kind, c)).ToArray());
        if (box.MaxLength > 0)
            clean = clean.Substring(0, Math.Max(0, Math.Min(clean.Length, box.MaxLength - box.Text.Length + box.SelectionLength)));
        box.SelectedText = clean;
        box.CaretIndex = box.SelectionStart + clean.Length;
        box.SelectionLength = 0;
    }
}
