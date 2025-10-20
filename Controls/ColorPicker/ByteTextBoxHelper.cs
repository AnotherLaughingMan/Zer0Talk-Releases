using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Interactivity;

namespace ZTalk.Controls.ColorPicker
{
    public static class ByteTextBoxHelper
    {
        public static readonly AttachedProperty<bool> EnableByteInputProperty =
            AvaloniaProperty.RegisterAttached<Control, bool>("EnableByteInput", typeof(ByteTextBoxHelper), false);

        public static readonly AttachedProperty<bool> HasErrorProperty =
            AvaloniaProperty.RegisterAttached<Control, bool>("HasError", typeof(ByteTextBoxHelper), false);

        public static void SetEnableByteInput(AvaloniaObject element, bool value)
        {
            element.SetValue(EnableByteInputProperty, value);
            if (element is TextBox tb && value)
                AttachHandlers(tb);
        }

        public static bool GetEnableByteInput(AvaloniaObject element) => element.GetValue(EnableByteInputProperty);

        public static bool GetHasError(AvaloniaObject element) => element.GetValue(HasErrorProperty);
        private static void SetHasError(AvaloniaObject element, bool value) => element.SetValue(HasErrorProperty, value);

        private static void AttachHandlers(TextBox tb)
        {
            tb.AddHandler(InputElement.TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
            tb.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            tb.LostFocus += Tb_LostFocus;
            // Initial validation
            ValidateAndApply(tb);
        }

        private static void OnTextInput(object? sender, TextInputEventArgs e)
        {
            if (sender is not TextBox tb) return;
            // Allow only digits
            var txt = e.Text ?? string.Empty;
            foreach (var ch in txt)
            {
                if (!char.IsDigit(ch))
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        private static void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (sender is not TextBox tb) return;
            // Allow Backspace/Delete etc.
            if (e.Key == Key.Back || e.Key == Key.Delete || e.Key == Key.Tab || e.Key == Key.Enter) return;

            // If ctrl/cmd combos, ignore here
            if ((e.KeyModifiers & KeyModifiers.Control) != 0) return;

            // arrow-key nudging
            var step = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? 10 : 1;
            if (e.Key == Key.Up || e.Key == Key.Right)
            {
                if (!int.TryParse(tb.Text, out var v)) v = 0;
                v = Math.Clamp(v + step, 0, 255);
                tb.Text = v.ToString();
                ValidateAndApply(tb);
                e.Handled = true;
                return;
            }
            else if (e.Key == Key.Down || e.Key == Key.Left)
            {
                if (!int.TryParse(tb.Text, out var v)) v = 0;
                v = Math.Clamp(v - step, 0, 255);
                tb.Text = v.ToString();
                ValidateAndApply(tb);
                e.Handled = true;
                return;
            }

            // allow digits via keydown as well
            if (e.Key < Key.D0 || e.Key > Key.D9)
            {
                if (e.Key < Key.NumPad0 || e.Key > Key.NumPad9)
                {
                    // not a digit
                    // keep event unhandled to allow other behaviors
                }
            }
        }

        private static void Tb_LostFocus(object? sender, global::System.EventArgs e)
        {
            if (sender is not TextBox tb) return;
            ValidateAndApply(tb);
        }

        private static void ValidateAndApply(TextBox tb)
        {
            if (string.IsNullOrWhiteSpace(tb.Text))
            {
                SetHasError(tb, true);
                tb.Background = new SolidColorBrush(Color.Parse("#2A1A1A"));
                ToolTip.SetTip(tb, "Enter a number between 0 and 255");
                return;
            }

            if (!int.TryParse(tb.Text, out var value))
            {
                SetHasError(tb, true);
                tb.Background = new SolidColorBrush(Color.Parse("#2A1A1A"));
                ToolTip.SetTip(tb, "Invalid number");
                return;
            }

            var clamped = Math.Clamp(value, 0, 255);
            if (clamped != value)
            {
                tb.Text = clamped.ToString();
            }

            SetHasError(tb, false);
            tb.Background = Brushes.Transparent;
            ToolTip.SetTip(tb, null);
        }
    }
}
