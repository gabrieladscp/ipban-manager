using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace IPBanManager.Wpf.Views;

/// <summary>
/// Lista de registros estruturados (uma linha do ipban.config cada) com um formulário de inclusão.
/// O formulário só gera o item depois de validado; os itens não são editados à mão.
/// </summary>
public partial class RecordListEditor : UserControl
{
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
    private readonly ObservableCollection<Item> _items = new ObservableCollection<Item>();

    public RecordListEditor()
    {
        InitializeComponent();
        Items.ItemsSource = _items;
        _items.CollectionChanged += (s, e) => UpdateCount();
        UpdateCount();
    }

    /// <summary>
    /// Liga o formulário de inclusão (declarado na tela): Enter adiciona, e preencher o formulário não marca a tela
    /// como "não salva" (só incluir/remover itens marca).
    /// </summary>
    public void AttachForm(UIElement form)
    {
        form.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, e) => e.Handled = true));
        var swallow = new RoutedEventHandler((s, e) => e.Handled = true);
        form.AddHandler(ToggleButton.CheckedEvent, swallow);
        form.AddHandler(ToggleButton.UncheckedEvent, swallow);
        form.AddHandler(KeyDownEvent, new KeyEventHandler(Form_KeyDown));
    }

    public string AddLabel { get => (string)AddButton.Content; set => AddButton.Content = value; }

    /// <summary>Lê o formulário: devolve a linha a gravar ou a mensagem de erro.</summary>
    public Func<IReadOnlyList<string>, (string Line, string Error)> BuildEntry { get; set; }

    /// <summary>Limpa o formulário depois de incluir.</summary>
    public Action ClearForm { get; set; }

    /// <summary>Texto exibido para cada linha (padrão: a própria linha).</summary>
    public Func<string, string> Describe { get; set; } = line => line;

    /// <summary>Reordena a lista após incluir (ex.: tempos em ordem crescente).</summary>
    public Func<IEnumerable<string>, IEnumerable<string>> Sort { get; set; }

    public IReadOnlyList<string> Entries
    {
        get => _items.Select(i => i.Line).ToList();
        set
        {
            _items.Clear();
            foreach (var line in value ?? Enumerable.Empty<string>()) _items.Add(new Item(line, Describe(line)));
            ShowMessage(null, false);
        }
    }

    private void Add()
    {
        if (BuildEntry == null) return;
        var (line, error) = BuildEntry(Entries);
        if (error != null) { ShowMessage(error, true); return; }
        if (_items.Any(i => string.Equals(i.Line, line, StringComparison.OrdinalIgnoreCase)))
        {
            ShowMessage("Esse item já está na lista.", true);
            return;
        }

        var lines = Entries.Concat(new[] { line });
        if (Sort != null) lines = Sort(lines);
        Entries = lines.ToList();
        Items.SelectedItem = _items.FirstOrDefault(i => i.Line == line);
        Items.ScrollIntoView(Items.SelectedItem);
        ClearForm?.Invoke();
        ShowMessage("✔ Adicionado.", false);
        RaiseEvent(new RoutedEventArgs(EntryListEditor.ItemsChangedEvent, this));
    }

    private void RemoveSelected()
    {
        var selected = Items.SelectedItems.Cast<Item>().ToList();
        if (selected.Count == 0) return;
        foreach (var item in selected) _items.Remove(item);
        ShowMessage(selected.Count == 1 ? "Item removido." : $"{selected.Count} itens removidos.", false);
        RaiseEvent(new RoutedEventArgs(EntryListEditor.ItemsChangedEvent, this));
    }

    private void ShowMessage(string text, bool isError)
    {
        Message.Text = text ?? "";
        Message.Foreground = isError ? ErrorBrush : Brushes.SeaGreen;
    }

    private void UpdateCount() =>
        CountText.Text = _items.Count == 0 ? "Nenhum item." : _items.Count == 1 ? "1 item" : $"{_items.Count} itens";

    private void Add_Click(object sender, RoutedEventArgs e) => Add();
    private void Remove_Click(object sender, RoutedEventArgs e) => RemoveSelected();

    private void Form_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.OriginalSource is TextBox) { Add(); e.Handled = true; }
    }

    private void Items_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RemoveButton.IsEnabled = Items.SelectedItems.Count > 0;

    private void Items_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete) { RemoveSelected(); e.Handled = true; }
    }

    private sealed class Item
    {
        public Item(string line, string display) { Line = line; Display = display; }
        public string Line { get; }
        public string Display { get; }
    }
}
