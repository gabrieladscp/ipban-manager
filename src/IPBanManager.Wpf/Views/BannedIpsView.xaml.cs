using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using IPBanManager.Core.Models;
using IPBanManager.Core.Views;

namespace IPBanManager.Wpf.Views;

public partial class BannedIpsView : ViewBase, IBannedIpsView
{
    private int _total;

    public BannedIpsView()
    {
        InitializeComponent();
        Loaded += (s, e) => RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler RefreshRequested;
    public event EventHandler BanRequested;
    public event EventHandler UnbanRequested;

    public IReadOnlyList<string> SelectedAddresses =>
        Grid.SelectedItems.Cast<BannedIp>().Select(b => b.Address).ToList();

    public string ManualBanAddress
    {
        get => ManualAddress.Text;
        set => ManualAddress.Text = value;
    }

    public void ShowBannedIps(IReadOnlyList<BannedIp> items)
    {
        _total = items.Count;
        var view = CollectionViewSource.GetDefaultView(items);
        view.Filter = Matches;
        Grid.ItemsSource = view;
        UpdateCount();
    }

    private bool Matches(object item)
    {
        var text = FilterBox.Text.Trim();
        if (text.Length == 0) return true;
        var ip = (BannedIp)item;
        return ip.Address.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
            || ip.Origin.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
            || ip.RuleName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void UpdateCount()
    {
        int shown = Grid.Items.Count;
        CountText.Text = shown == _total ? $"{_total} endereço(s) bloqueado(s)" : $"{shown} de {_total} endereço(s)";
    }

    private void Filter_TextChanged(object s, TextChangedEventArgs e)
    {
        (Grid.ItemsSource as ICollectionView)?.Refresh();
        UpdateCount();
    }

    private void ManualAddress_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) BanRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Refresh_Click(object s, RoutedEventArgs e) => RefreshRequested?.Invoke(this, EventArgs.Empty);
    private void Ban_Click(object s, RoutedEventArgs e) => BanRequested?.Invoke(this, EventArgs.Empty);
    private void Unban_Click(object s, RoutedEventArgs e) => UnbanRequested?.Invoke(this, EventArgs.Empty);
}
