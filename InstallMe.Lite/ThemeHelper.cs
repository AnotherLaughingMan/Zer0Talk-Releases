using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace InstallMe.Lite
{
    internal static class ThemeHelper
    {
        private const string PersonalizeKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        public static bool IsDarkMode()
        {
            try
            {
                if (SystemInformation.HighContrast) return false;
                using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
                var value = key?.GetValue("AppsUseLightTheme");
                if (value is int intValue)
                {
                    return intValue == 0;
                }
            }
            catch { }

            return false;
        }

        public static void ApplyTheme(Control root)
        {
            var dark = IsDarkMode();
            var back = dark ? Color.FromArgb(32, 32, 32) : SystemColors.Window;
            var surface = dark ? Color.FromArgb(45, 45, 48) : SystemColors.Control;
            var fore = dark ? Color.WhiteSmoke : SystemColors.ControlText;
            var input = dark ? Color.FromArgb(50, 50, 50) : SystemColors.Window;

            ApplyToControl(root, back, surface, fore, input);
        }

        private static void ApplyToControl(Control control, Color back, Color surface, Color fore, Color input)
        {
            if (control is Form)
            {
                control.BackColor = back;
                control.ForeColor = fore;
            }
            else if (control is Panel || control is TableLayoutPanel || control is FlowLayoutPanel || control is GroupBox)
            {
                control.BackColor = back;
                control.ForeColor = fore;
            }
            else if (control is TextBox || control is RichTextBox)
            {
                control.BackColor = input;
                control.ForeColor = fore;
                if (control is TextBox tb) tb.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (control is Button btn)
            {
                btn.FlatStyle = FlatStyle.Flat;
                btn.BackColor = surface;
                btn.ForeColor = fore;
                btn.FlatAppearance.BorderColor = Color.FromArgb(Math.Min(surface.R + 20, 255), Math.Min(surface.G + 20, 255), Math.Min(surface.B + 20, 255));
            }
            else if (control is Label)
            {
                control.ForeColor = fore;
            }
            else
            {
                control.ForeColor = fore;
            }

            foreach (Control child in control.Controls)
            {
                ApplyToControl(child, back, surface, fore, input);
            }
        }
    }
}
