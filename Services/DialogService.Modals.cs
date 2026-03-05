#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

using Zer0Talk.Utilities;

namespace Zer0Talk.Services
{
    public partial class DialogService
    {
        // Simple text prompt dialog returning edited text or null if cancelled
        public async Task<string?> PromptAsync(string title, string initialText)
        {
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (lifetime?.MainWindow is not Window owner) return null;

            var tb = new TextBox { Text = initialText, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Height = 160 };
            var okBtn = new Button { Content = "Save", IsDefault = true };
            var cancelBtn = new Button { Content = "Cancel", IsCancel = true };
            string? result = null;

            var dialog = new Window
            {
                Title = title,
                // 16:9-ish sizing
                Width = 640,
                Height = 360,
                CanResize = true,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ExtendClientAreaToDecorationsHint = true,
                ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome,
                ExtendClientAreaTitleBarHeightHint = 32,
                Content = new StackPanel
                {
                    Margin = new Thickness(16),
                    Spacing = 8,
                    Children =
                    {
                        tb,
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            Spacing = 8,
                            Children = { cancelBtn, okBtn }
                        }
                    }
                }
            };
            okBtn.Click += (_, __) => { result = tb.Text ?? string.Empty; dialog.Close(); };
            cancelBtn.Click += (_, __) => { result = null; dialog.Close(); };
            await dialog.ShowDialog(owner);
            return result;
        }

        public async Task<bool> AskContactPermissionAsync(string requesterDisplay, string requesterUid)
        {
            var msg = $"{requesterDisplay} ({requesterUid}) wants to add you as a contact.";
            return await ConfirmAsync("Contact request", msg, "Agree", "Cancel");
        }

        // Warning dialog with triangle caution icon
        public async Task<bool> ConfirmWarningAsync(string title, string message, string confirmText = "OK", string cancelText = "Cancel")
        {
            return await ConfirmAsync(title, message, confirmText, cancelText, "\u26A0");
        }

        // Alert dialog with triangle caution icon (using Unicode triangle with exclamation)
        public async Task<bool> ConfirmAlertAsync(string title, string message, string confirmText = "OK", string cancelText = "Cancel")
        {
            return await ConfirmAsync(title, message, confirmText, cancelText, "\u26A0");
        }

        // Destructive action dialog with triangle caution icon
        public async Task<bool> ConfirmDestructiveAsync(string title, string message, string confirmText = "Delete", string cancelText = "Cancel")
        {
            return await ConfirmAsync(title, message, confirmText, cancelText, "\u26A0");
        }

        public async Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", string cancelText = "Cancel")
        {
            return await ConfirmAsync(title, message, confirmText, cancelText, null);
        }

        public async Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText, string? iconText)
        {
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (lifetime?.MainWindow is not Window owner) return false;

            var dialog = new Window
            {
                Title = title,
                MinWidth = 420,
                MaxWidth = 600,
                MinHeight = iconText != null ? 200 : 180,
                MaxHeight = 400, // Allow for longer messages
                CanResize = false,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ExtendClientAreaToDecorationsHint = true,
                ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome,
                ExtendClientAreaTitleBarHeightHint = 32
            };

            var cancelBtn = new Button 
            { 
                Content = cancelText, 
                IsDefault = false, 
                IsCancel = true,
                MinWidth = 90,
                Padding = new Avalonia.Thickness(12, 6)
            };
            
            var confirmBtn = new Button 
            { 
                Content = confirmText, 
                IsDefault = true,
                MinWidth = 120,
                Padding = new Avalonia.Thickness(12, 6)
            };

            var grid = new Grid
            {
                RowDefinitions = new RowDefinitions("*,Auto"),
                Margin = new Avalonia.Thickness(20, 16, 20, 16)
            };

            var messagePanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 12,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                Margin = new Avalonia.Thickness(0, 8, 0, 0),
                MaxWidth = 540 // Constrain to fit within dialog margins
            };

            // Add triangle caution icon if provided
            if (!string.IsNullOrEmpty(iconText))
            {
                var iconBlock = new TextBlock
                {
                    Text = iconText,
                    FontSize = 24,
                    FontStyle = Avalonia.Media.FontStyle.Normal,
                    FontWeight = Avalonia.Media.FontWeight.Normal,
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(255, 165, 0)), // Orange warning color
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                    Margin = new Avalonia.Thickness(0, 2, 0, 0), // Slight top margin to align with text
                    RenderTransform = null // Ensure no transforms are applied
                };
                messagePanel.Children.Add(iconBlock);
            }

            var messageBlock = new TextBlock 
            { 
                Text = message, 
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                MaxWidth = iconText != null ? 480 : 520 // Leave space for icon if present
            };
            messagePanel.Children.Add(messageBlock);
            Grid.SetRow(messagePanel, 0);

            var buttonGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                Margin = new Avalonia.Thickness(0, 16, 0, 0)
            };
            Grid.SetRow(buttonGrid, 1);

            Grid.SetColumn(cancelBtn, 0);
            Grid.SetColumn(confirmBtn, 2);
            buttonGrid.Children.Add(cancelBtn);
            buttonGrid.Children.Add(confirmBtn);

            grid.Children.Add(messagePanel);
            grid.Children.Add(buttonGrid);
            dialog.Content = grid;

            bool result = false;
            cancelBtn.Click += (_, __) => { result = false; dialog.Close(); };
            confirmBtn.Click += (_, __) => { result = true; dialog.Close(); };
            
            await dialog.ShowDialog(owner);
            return result;
        }

        // Movable verification modal with a single "Verify" and a "Cancel" button
        public async Task<bool> ShowVerificationDialogAsync(string peerDisplay, string peerUid)
        {
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (lifetime?.MainWindow is not Window owner) return false;

            string peerFingerprint;
            try
            {
                var peerHex = AppServices.Peers.Peers
                    .FirstOrDefault(p => string.Equals(p.UID, peerUid, StringComparison.OrdinalIgnoreCase))
                    ?.PublicKeyHex;

                if (string.IsNullOrWhiteSpace(peerHex))
                {
                    peerHex = AppServices.Contacts.Contacts
                        .FirstOrDefault(c => string.Equals(c.UID, peerUid, StringComparison.OrdinalIgnoreCase))
                        ?.ExpectedPublicKeyHex;
                }

                if (string.IsNullOrWhiteSpace(peerHex))
                {
                    peerHex = AppServices.Contacts.Contacts
                        .FirstOrDefault(c => string.Equals(c.UID, peerUid, StringComparison.OrdinalIgnoreCase))
                        ?.LastKnownPublicKeyHex;
                }

                peerFingerprint = TrustCeremonyFormatter.FingerprintFromPublicKeyHex(peerHex);
            }
            catch
            {
                peerFingerprint = "Unavailable";
            }

            var myFingerprint = TrustCeremonyFormatter.FingerprintFromPublicKeyHex(
                AppServices.Identity.PublicKey is { Length: > 0 }
                    ? Convert.ToHexString(AppServices.Identity.PublicKey)
                    : string.Empty);

            var dialog = new Window
            {
                Title = "Verification",
                MinWidth = 460,
                MaxWidth = 600,
                MinHeight = 200,
                MaxHeight = 400,
                CanResize = true,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ExtendClientAreaToDecorationsHint = true,
                ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome,
                ExtendClientAreaTitleBarHeightHint = 32
            };
            var root = new StackPanel { Margin = new Thickness(16), Spacing = 12, MaxWidth = 560 };
            root.Children.Add(new TextBlock { 
                Text = $"You and {peerDisplay} ({peerUid}) will verify each other.", 
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                MaxWidth = 520
            });

            root.Children.Add(new TextBlock
            {
                Text = "Compare both fingerprints over a trusted channel before pressing Verify.",
                Opacity = 0.8,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                MaxWidth = 520
            });

            var fingerprints = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("120,*"),
                RowDefinitions = new RowDefinitions("Auto,Auto"),
                RowSpacing = 6,
                ColumnSpacing = 10,
            };
            fingerprints.Children.Add(new TextBlock { Text = "Your Fingerprint", Opacity = 0.82 });
            var myText = new TextBlock { Text = myFingerprint, FontFamily = new Avalonia.Media.FontFamily("Consolas"), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            Grid.SetColumn(myText, 1);
            fingerprints.Children.Add(myText);

            var peerLabel = new TextBlock { Text = "Peer Fingerprint", Opacity = 0.82 };
            Grid.SetRow(peerLabel, 1);
            fingerprints.Children.Add(peerLabel);
            var peerText = new TextBlock { Text = peerFingerprint, FontFamily = new Avalonia.Media.FontFamily("Consolas"), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            Grid.SetRow(peerText, 1);
            Grid.SetColumn(peerText, 1);
            fingerprints.Children.Add(peerText);
            root.Children.Add(fingerprints);

            var actions = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8 };
            var cancel = new Button { Content = "Cancel" };
            var verify = new Button { Content = "Verify", IsDefault = true };
            actions.Children.Add(cancel);
            actions.Children.Add(verify);
            root.Children.Add(actions);
            dialog.Content = root;

            bool result = false;
            cancel.Click += (_, __) => { result = false; dialog.Close(); };
            verify.Click += (_, __) => { result = true; dialog.Close(); };
            await dialog.ShowDialog(owner);
            return result;
        }

        public async Task<UnsavedChangesAction> PromptUnsavedChangesAsync(
            string title = "Close Settings",
            string message = "You have unsaved changes. What would you like to do?",
            string saveText = "Save Changes",
            string discardText = "Discard",
            string cancelText = "Cancel")
        {
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (lifetime?.MainWindow is not Window owner)
            {
                return UnsavedChangesAction.Cancel;
            }

            // Chrome-styled modal window using app theme resources.
            var dialog = new Window
            {
                Title = title,
                MinWidth = 480,
                MaxWidth = 600,
                MinHeight = 220,
                MaxHeight = 350,
                CanResize = false,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ExtendClientAreaToDecorationsHint = true,
                ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome,
                ExtendClientAreaTitleBarHeightHint = 32
            };
            dialog.Background = (Avalonia.Media.IBrush?)Application.Current?.FindResource("App.Background");
            dialog.Foreground = (Avalonia.Media.IBrush?)Application.Current?.FindResource("App.ForegroundPrimary");

            var rootBorder = new Border
            {
                Padding = new Thickness(12),
                BorderThickness = new Thickness(1)
            };
            rootBorder.Background = (Avalonia.Media.IBrush?)Application.Current?.FindResource("App.Background");
            rootBorder.BorderBrush = (Avalonia.Media.IBrush?)Application.Current?.FindResource("App.Border");

            var root = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto, *, Auto")
            };

            var header = new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(4, 2, 4, 6)
            };
            header.BorderBrush = (Avalonia.Media.IBrush?)Application.Current?.FindResource("App.Border");
            header.Background = (Avalonia.Media.IBrush?)Application.Current?.FindResource("App.Background");
            header.Child = new TextBlock
            {
                Text = title,
                FontWeight = Avalonia.Media.FontWeight.SemiBold
            };
            Grid.SetRow(header, 0);

            var contentPanel = new StackPanel
            {
                Spacing = 8
            };
            
            // Add message with warning icon for unsaved changes
            var messagePanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 12
            };
            
            var warningIcon = new TextBlock
            {
                Text = "\u26A0", // Triangle warning symbol
                FontSize = 20,
                FontStyle = Avalonia.Media.FontStyle.Normal,
                FontWeight = Avalonia.Media.FontWeight.Normal,
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(255, 165, 0)), // Orange warning color
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                Margin = new Avalonia.Thickness(0, 2, 0, 0),
                RenderTransform = null // Ensure no transforms are applied
            };
            
            var messageText = new TextBlock
            {
                Text = message,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                MaxWidth = 480 // Leave space for icon
            };
            
            messagePanel.Children.Add(warningIcon);
            messagePanel.Children.Add(messageText);
            contentPanel.Children.Add(messagePanel);
            Grid.SetRow(contentPanel, 1);

            // Actions: Save (left), Discard (right). Cancel is not shown; Esc acts as Cancel.
            var actionsGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto, *, Auto"),
                Margin = new Thickness(0, 8, 0, 0)
            };
            var saveBtn = new Button
            {
                Content = saveText,
                IsDefault = true,
                MinWidth = 120,
                Padding = new Thickness(10, 6)
            };
            var discardBtn = new Button
            {
                Content = discardText,
                MinWidth = 120,
                Padding = new Thickness(10, 6)
            };
            Grid.SetColumn(saveBtn, 0);
            Grid.SetColumn(discardBtn, 2);
            actionsGrid.Children.Add(saveBtn);
            actionsGrid.Children.Add(discardBtn);
            Grid.SetRow(actionsGrid, 2);

            root.Children.Add(header);
            root.Children.Add(contentPanel);
            root.Children.Add(actionsGrid);
            rootBorder.Child = root;
            dialog.Content = rootBorder;

            var result = UnsavedChangesAction.Cancel;
            saveBtn.Click += (_, __) =>
            {
                result = UnsavedChangesAction.Save;
                dialog.Close();
            };
            discardBtn.Click += (_, __) =>
            {
                result = UnsavedChangesAction.Discard;
                dialog.Close();
            };

            // Accessibility: Esc = Cancel; Tab order default works via visual tree order.
            dialog.KeyDown += (_, e) =>
            {
                try
                {
                    if (e.Key == Avalonia.Input.Key.Escape)
                    {
                        result = UnsavedChangesAction.Cancel;
                        dialog.Close();
                    }
                }
                catch
                {
                }
            };

            // Log UI change
            try
            {
                WriteUiLog("[Dialog] UnsavedChanges chrome-styled; Save left, Discard right; Cancel removed (Esc supported)");
            }
            catch
            {
            }

            await dialog.ShowDialog(owner);
            return result;
        }

        private static void WriteUiLog(string line)
        {
            try
            {
                if (Zer0Talk.Utilities.LoggingPaths.Enabled)
                    System.IO.File.AppendAllText(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} {line}{Environment.NewLine}");
            }
            catch { }
        }

        public async Task ShowPassphraseDialogAsync(string passphrase)
        {
            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (lifetime?.MainWindow is not Window owner) return;

            var tb = new TextBox
            {
                Text = passphrase,
                IsReadOnly = true,
                FontFamily = new Avalonia.Media.FontFamily("Consolas"),
            };
            ToolTip.SetTip(tb, "Store this passphrase securely. Anyone with it can access your account.");

            var copyBtn = new Button { Content = "Copy" };
            var saveBtn = new Button { Content = "Save" };
            var okBtn = new Button { Content = "Done", IsDefault = true };

            var dialog = new Window
            {
                Title = "Your account passphrase",
                MinWidth = 540,
                MaxWidth = 700,
                MinHeight = 300,
                MaxHeight = 450,
                CanResize = false,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ExtendClientAreaToDecorationsHint = true,
                ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome,
                ExtendClientAreaTitleBarHeightHint = 32,
                Content = new StackPanel
                {
                    Margin = new Thickness(16),
                    Spacing = 8,
                    MaxWidth = 650,
                    Children =
                    {
                        new TextBlock { 
                            Text = "Write down or save this passphrase now. It cannot be recovered if lost.", 
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                            MaxWidth = 600
                        },
                        tb,
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            Spacing = 8,
                            Children = { copyBtn, saveBtn, okBtn }
                        }
                    }
                }
            };

            copyBtn.Click += async (_, __) =>
            {
                try { await dialog.Clipboard!.SetTextAsync(passphrase); }
                catch { }
            };

            saveBtn.Click += async (_, __) =>
            {
                try
                {
                    var dir = Zer0Talk.Utilities.AppDataPaths.Root;
                    Directory.CreateDirectory(dir);
                    IStorageFolder? start = null;
                    try { start = await dialog.StorageProvider.TryGetFolderFromPathAsync(dir); } catch { }
                    var file = await dialog.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                    {
                        Title = "Save passphrase",
                        SuggestedStartLocation = start,
                        SuggestedFileName = OperatingSystem.IsWindows() ? "passphrase.dpapi.txt" : "passphrase.txt",
                        FileTypeChoices = new[] { new FilePickerFileType("Text Files") { Patterns = new[] { "*.txt" } } }
                    });
                    if (file != null)
                    {
                        await using var stream = await file.OpenWriteAsync();
                        await using var writer = new StreamWriter(stream);
                        if (OperatingSystem.IsWindows())
                        {
                            var passBytes = Encoding.UTF8.GetBytes(passphrase);
                            try
                            {
                                var protectedBytes = ProtectedData.Protect(passBytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
                                try
                                {
                                    await writer.WriteAsync("Zer0Talk-DPAPI-v1\n");
                                    await writer.WriteAsync(Convert.ToBase64String(protectedBytes));
                                }
                                finally
                                {
                                    CryptographicOperations.ZeroMemory(protectedBytes);
                                }
                            }
                            finally
                            {
                                CryptographicOperations.ZeroMemory(passBytes);
                            }
                        }
                        else
                        {
                            await writer.WriteAsync(passphrase);
                        }
                        await writer.FlushAsync();
                    }
                }
                catch { }
            };

            okBtn.Click += (_, __) => dialog.Close();

            await dialog.ShowDialog(owner);
        }
    }
}
