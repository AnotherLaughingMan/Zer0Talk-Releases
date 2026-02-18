using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Zer0Talk.Utilities;
using Zer0Talk.ViewModels;

namespace Zer0Talk.Views
{
    public partial class LostPassphraseDialog : Window
    {
        public LostPassphraseDialog()
        {
            InitializeComponent();
            this.KeyDown += LostPassphraseDialog_KeyDown;
            try { Utilities.LoggingPaths.TryWrite(Utilities.LoggingPaths.Theme, $"[{DateTime.UtcNow:O}] lostpass.dialog open\n"); } catch { }
        }

        private void LostPassphraseDialog_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                try { Utilities.LoggingPaths.TryWrite(Utilities.LoggingPaths.Theme, $"[{DateTime.UtcNow:O}] lostpass.dialog esc-close\n"); } catch { }
                Close();
            }
        }

        private void Close_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            try { Utilities.LoggingPaths.TryWrite(Utilities.LoggingPaths.Theme, $"[{DateTime.UtcNow:O}] lostpass.dialog close\n"); } catch { }
            Close();
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
                }
                if (WindowDragHelper.TryBeginMoveDrag(this, e))
                {
                    e.Handled = true;
                }
            }
            catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            try { if (DataContext is IDisposable d) d.Dispose(); } catch { }
            try { DataContext = null; } catch { }
        }
    }
}
