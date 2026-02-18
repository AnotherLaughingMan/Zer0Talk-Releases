using System;
using System.Collections.Generic;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using Zer0Talk.Models;
using Zer0Talk.Utilities;
using Zer0Talk.ViewModels;

namespace Zer0Talk.Views;

public partial class DiscoveredPeersWindow : Window
{
    private readonly Avalonia.Threading.DispatcherTimer _autoRefreshTimer = new()
    {
        Interval = TimeSpan.FromSeconds(2)
    };

    public DiscoveredPeersWindow()
    {
        InitializeComponent();
        _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        try
        {
            if (DataContext is NetworkViewModel vm)
            {
                vm.RefreshPeersRealtime();
            }
            _autoRefreshTimer.Start();
        }
        catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            _autoRefreshTimer.Stop();
            _autoRefreshTimer.Tick -= AutoRefreshTimer_Tick;
        }
        catch { }
        base.OnClosed(e);
    }

    private void AutoRefreshTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            if (DataContext is not NetworkViewModel vm) return;
            if (!vm.AutoRefreshPeers) return;
            vm.RefreshPeersRealtime();
        }
        catch { }
    }

    private void DragBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            var point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed) return;
            if (e.Source is Avalonia.Visual c)
            {
                if (c is Button || c.FindAncestorOfType<Button>() != null) return;
                if (c is TextBox || c.FindAncestorOfType<TextBox>() != null) return;
                if (c is ComboBox || c.FindAncestorOfType<ComboBox>() != null) return;
            }
                if (WindowDragHelper.TryBeginMoveDrag(this, e))
                {
                    e.Handled = true;
                }
        }
        catch { }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        try { Close(); } catch { }
    }

    private void DiscoveredPeersList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (sender is not ListBox listBox) return;
            if (DataContext is not NetworkViewModel vm) return;

            var selected = new List<Peer>();
            if (listBox.SelectedItems != null)
            {
                foreach (var item in listBox.SelectedItems)
                {
                    if (item is Peer peer) selected.Add(peer);
                }
            }
            vm.SelectedPeers = selected;
        }
        catch { }
    }
}
