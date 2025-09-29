using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

using P2PTalk.ViewModels;

namespace P2PTalk.Views
{
    public partial class AddContactWindow : Window
    {
        public AddContactWindow()
        {
            InitializeComponent();
            if (DataContext is AddContactViewModel vm)
            {
                vm.CloseRequested += ok => { Close(ok); };
            }
        }

        // [DRAGBAR] Begin moving window on left-click; ignore clicks on interactive controls.
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
                BeginMoveDrag(e);
            }
            catch { }
        }

        protected override void OnClosed(System.EventArgs e)
        {
            base.OnClosed(e);
            try { if (DataContext is System.IDisposable d) d.Dispose(); } catch { }
            try { DataContext = null; } catch { }
        }
    }
}
