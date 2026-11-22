using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IPBanManager.Core.Validation;

namespace IPBanManager.Wpf.Views;

/// <summary>
/// Lista editável com campo de entrada validado: só aceita os caracteres do tipo de item, valida ao adicionar
/// (+ / Enter), remove os selecionados (− / Delete) e aceita colar vários itens de uma vez.
/// </summary>
public partial class EntryListEditor : UserControl
{
    /// <summary>Disparado (bubbling) quando a lista muda; a tela usa para marcar alterações não salvas.</summary>
    public static readonly RoutedEvent ItemsChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(ItemsChanged), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(EntryListEditor));

    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
    private static readonly Brush OkBrush = Brushes.SeaGreen;
    private readonly ObservableCollection<string> _items = new ObservableCollection<string>();

    public EntryListEditor()
    {
        InitializeComponent();
        Items.ItemsSource = _items;
        DataObject.AddPastingHandler(Input, OnPaste);
        _items.CollectionChanged += (s, e) => UpdateCount();
        UpdateCount();
    }

    public event RoutedEventHandler ItemsChanged
    {
        add => AddHandler(ItemsChangedEvent, value);
        remove => RemoveHandler(ItemsChangedEvent, value);
    }

    public ListEntryKind Kind { get; set; } = ListEntryKind.Address;

    public string PlaceholderText
    {
        get => Placeholder.Text;
        set => Placeholder.Text = value;
    }

    /// <summary>Itens atuais (carregar não dispara ItemsChanged).</summary>
    public IReadOnlyList<string> Entries
    {
        get => _items.ToList();
        set
        {
            _items.Clear();
            foreach (var item in value ?? Enumerable.Empty<string>()) _items.Add(item);
            Input.Clear();
            ShowMessage(null, false);
        }
    }

    /// <summary>Texto digitado e ainda não adicionado; se válido, é incluído (evita perder o que foi digitado ao salvar).</summary>
    public void CommitPending()
    {
        if (Input.Text.Trim().Length > 0) TryAdd(Input.Text);
    }

    private bool TryAdd(string text)
    {
        var error = ListEntryRules.Validate(Kind, text, _items, out var normalized);
        if (error != null)
        {
            ShowMessage(error, true);
            return false;
        }
        _items.Add(normalized);
        Items.ScrollIntoView(normalized);
        Input.Clear();
        ShowMessage($"✔ {normalized} adicionado.", false);
        RaiseEvent(new RoutedEventArgs(ItemsChangedEvent, this));
        return true;
    }

    private void AddMany(IReadOnlyList<string> tokens)
    {
        var added = new List<string>();
        var rejected = new List<string>();
        foreach (var token in tokens)
        {
            var error = ListEntryRules.Validate(Kind, token, _items, out var normalized);
            if (error == null) { _items.Add(normalized); added.Add(normalized); }
            else rejected.Add(token);
        }
        if (added.Count > 0) RaiseEvent(new RoutedEventArgs(ItemsChangedEvent, this));
        ShowMessage(rejected.Count == 0
                ? $"✔ {added.Count} item(ns) adicionado(s)."
                : $"{added.Count} adicionado(s); ignorados (inválidos ou repetidos): {string.Join(", ", rejected)}",
            rejected.Count > 0);
    }

    private void RemoveSelected()
    {
        var selected = Items.SelectedItems.Cast<string>().ToList();
        if (selected.Count == 0) return;
        foreach (var item in selected) _items.Remove(item);
        ShowMessage(selected.Count == 1 ? $"{selected[0]} removido." : $"{selected.Count} itens removidos.", false);
        RaiseEvent(new RoutedEventArgs(ItemsChangedEvent, this));
    }

    private void ShowMessage(string text, bool isError)
    {
        Message.Text = text ?? "";
        Message.Foreground = isError ? ErrorBrush : OkBrush;
        Input.BorderBrush = isError ? ErrorBrush : SystemColors.ControlDarkBrush;
    }

    private void UpdateCount() =>
        CountText.Text = _items.Count == 0 ? "Lista vazia." : _items.Count == 1 ? "1 item" : $"{_items.Count} itens";

    // ------------------------------------------------------------------ eventos

    private void Input_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (e.Text.Any(c => !ListEntryRules.IsAllowedChar(Kind, c))) e.Handled = true;
    }

    private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space) e.Handled = true; // espaço não passa pelo PreviewTextInput
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            TryAdd(Input.Text);
        }
    }

    private void Input_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Digitar no campo não altera a configuração: não deixa o evento chegar à tela (indicador de "não salvo").
        e.Handled = true;
        Placeholder.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        AddButton.IsEnabled = Input.Text.Trim().Length > 0;
        if (Message.Foreground == ErrorBrush && Message.Text.Length > 0) ShowMessage(null, false);
    }

    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        var text = e.SourceDataObject.GetData(DataFormats.UnicodeText) as string ?? "";
        e.CancelCommand();
        var tokens = ListEntryRules.SplitPasted(text);
        if (tokens.Count > 1) { AddMany(tokens); return; }
        // Um item só: cola no campo apenas os caracteres permitidos.
        var clean = new string(text.Where(c => ListEntryRules.IsAllowedChar(Kind, c)).ToArray());
        Input.SelectedText = clean;
        Input.CaretIndex = Input.SelectionStart + clean.Length;
        Input.SelectionLength = 0;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        TryAdd(Input.Text);
        Input.Focus();
    }

    private void Remove_Click(object sender, RoutedEventArgs e) => RemoveSelected();

    private void Items_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RemoveButton.IsEnabled = Items.SelectedItems.Count > 0;

    private void Items_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete) { RemoveSelected(); e.Handled = true; }
    }
}
