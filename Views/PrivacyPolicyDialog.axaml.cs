using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Zer0Talk.Utilities;

namespace Zer0Talk.Views
{
    public partial class PrivacyPolicyDialog : Window
    {
        private readonly bool _mandatory;

        /// <summary>True if the user checked the accept checkbox and clicked Close.</summary>
        public bool Accepted { get; private set; }

        /// <summary>True if the user checked "Do not show again" before closing.</summary>
        public bool DoNotShowAgain { get; private set; }

        // Required by the Avalonia AXAML runtime loader
        public PrivacyPolicyDialog() : this(alreadyAccepted: false, doNotShowChecked: false, mandatory: false) { }

        /// <param name="alreadyAccepted">Pre-check the acceptance checkbox when the user has already accepted previously.</param>
        /// <param name="doNotShowChecked">Pre-populate the "Do not show again" checkbox state.</param>
        /// <param name="mandatory">When true, the dialog cannot be dismissed without accepting (first-run mode).</param>
        public PrivacyPolicyDialog(bool alreadyAccepted, bool doNotShowChecked, bool mandatory = false)
        {
            _mandatory = mandatory;
            InitializeComponent();

            if (alreadyAccepted)
            {
                AcceptCheckBox.IsChecked = true;
                CloseButton.IsEnabled = true;
                DoNotShowCheckBox.IsEnabled = true;
            }

            if (doNotShowChecked)
                DoNotShowCheckBox.IsChecked = true;

            if (mandatory)
                TitleCloseButton.IsVisible = false;

            this.KeyDown += PrivacyPolicyDialog_KeyDown;
        }

        private void PrivacyPolicyDialog_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (_mandatory) { e.Handled = true; return; }
                Accepted = AcceptCheckBox.IsChecked == true;
                DoNotShowAgain = DoNotShowCheckBox.IsChecked == true;
                Close();
            }
        }

        private void AcceptCheckBox_Changed(object? sender, RoutedEventArgs e)
        {
            var isChecked = AcceptCheckBox.IsChecked == true;
            CloseButton.IsEnabled = isChecked;
            DoNotShowCheckBox.IsEnabled = isChecked;
            if (!isChecked)
                DoNotShowCheckBox.IsChecked = false;
        }

        private void TitleClose_Click(object? sender, RoutedEventArgs e)
        {
            if (_mandatory && AcceptCheckBox.IsChecked != true) return;
            Accepted = AcceptCheckBox.IsChecked == true;
            DoNotShowAgain = DoNotShowCheckBox.IsChecked == true;
            Close();
        }

        private void Close_Click(object? sender, RoutedEventArgs e)
        {
            Accepted = true;
            DoNotShowAgain = DoNotShowCheckBox.IsChecked == true;
            Close();
        }

        private void OpenGitHub_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/AnotherLaughingMan/Zer0Talk-Releases/blob/main/docs/PRIVACY-POLICY.md",
                    UseShellExecute = true
                });
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
                }
                if (WindowDragHelper.TryBeginMoveDrag(this, e))
                    e.Handled = true;
            }
            catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            try { DataContext = null; } catch { }
        }
    }
}
