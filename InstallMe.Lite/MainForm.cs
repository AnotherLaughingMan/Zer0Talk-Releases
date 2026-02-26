using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace InstallMe.Lite
{
    public class MainForm : Form
    {
        private readonly InstallerConfig _config;
        private Button _btnInstall;
        private Button _btnUninstall;
        private TextBox _txtTarget;
        private Label _lblStatus;
        private Label _lblTitle;
        private Label _lblSubtitle;
        private RichTextBox _logBox;
        private CheckBox _chkRestart;
        private bool _isFinished;

        public MainForm(InstallerConfig config)
        {
            _config = config;

            Text = $"{_config.AppDisplayName} Installer";
            Width = 740;
            Height = 560;
            MinimumSize = new System.Drawing.Size(740, 560);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16),
                ColumnCount = 1,
                RowCount = 7
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var banner = new Panel
            {
                Dock = DockStyle.Top,
                Height = 64,
                BackColor = System.Drawing.Color.FromArgb(14, 24, 36),
                Padding = new Padding(10, 8, 10, 8)
            };

            var bannerText = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                ForeColor = System.Drawing.Color.FromArgb(120, 200, 180),
                Font = new System.Drawing.Font("Consolas", 9, System.Drawing.FontStyle.Regular),
                Text = BuildBannerText()
            };
            banner.Controls.Add(bannerText);

            _lblTitle = new Label
            {
                Text = $"{_config.AppDisplayName} Setup",
                AutoSize = true,
                Font = new System.Drawing.Font(Font.FontFamily, 14, System.Drawing.FontStyle.Bold),
                Margin = new Padding(0, 10, 0, 0)
            };

            _lblSubtitle = new Label
            {
                Text = $"Setup wizard for {_config.AppDisplayName} on this PC.",
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 12)
            };

            var locationGroup = new GroupBox
            {
                Text = "Install Options",
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(12)
            };

            var locationPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 3,
                RowCount = 1,
                AutoSize = true
            };
            locationPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            locationPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            locationPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var lblTarget = new Label { Text = "Install location:", AutoSize = true, Margin = new Padding(0, 6, 8, 0) };
            _txtTarget = new TextBox { Dock = DockStyle.Fill, Text = _config.DefaultInstallPath };
            var btnTargetBrowse = new Button { Text = "Browse...", AutoSize = true };
            btnTargetBrowse.Click += (s, e) => BrowseTarget();

            _chkRestart = new CheckBox
            {
                Text = $"Restart {_config.AppDisplayName} after install",
                AutoSize = true,
                Checked = false,
                Visible = false,
                Margin = new Padding(0, 8, 0, 0)
            };

            locationPanel.Controls.Add(lblTarget, 0, 0);
            locationPanel.Controls.Add(_txtTarget, 1, 0);
            locationPanel.Controls.Add(btnTargetBrowse, 2, 0);

            var optionsLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 2,
                AutoSize = true
            };
            optionsLayout.Controls.Add(locationPanel, 0, 0);
            optionsLayout.Controls.Add(_chkRestart, 0, 1);

            locationGroup.Controls.Add(optionsLayout);

            var logGroup = new GroupBox
            {
                Text = "Install Log",
                Dock = DockStyle.Fill,
                AutoSize = false,
                Height = 220,
                Padding = new Padding(12)
            };

            var logLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            logLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            logLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var note = new Label
            {
                Text = $"Your messages and settings in %APPDATA%\\{_config.AppDataFolderName} are preserved during uninstall.",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6)
            };

            _logBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = System.Drawing.Color.FromArgb(24, 24, 24),
                ForeColor = System.Drawing.Color.Gainsboro,
                Font = new System.Drawing.Font("Consolas", 9, System.Drawing.FontStyle.Regular),
                WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };

            logLayout.Controls.Add(note, 0, 0);
            logLayout.Controls.Add(_logBox, 0, 1);
            logGroup.Controls.Add(logLayout);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true
            };

            _btnInstall = new Button { Text = "Install", AutoSize = true, MinimumSize = new System.Drawing.Size(110, 32) };
            _btnInstall.Click += async (s, e) => await OnInstallButtonClickAsync();

            _btnUninstall = new Button { Text = "Uninstall", AutoSize = true, MinimumSize = new System.Drawing.Size(110, 32) };
            _btnUninstall.Click += async (s, e) => await UninstallAsync();

            buttonPanel.Controls.Add(_btnInstall);
            buttonPanel.Controls.Add(_btnUninstall);

            _lblStatus = new Label
            {
                Text = "Status: Ready",
                AutoSize = true,
                Margin = new Padding(0, 12, 0, 0)
            };

            root.Controls.Add(banner, 0, 0);
            root.Controls.Add(_lblTitle, 0, 1);
            root.Controls.Add(_lblSubtitle, 0, 2);
            root.Controls.Add(locationGroup, 0, 3);
            root.Controls.Add(logGroup, 0, 4);
            root.Controls.Add(_lblStatus, 0, 5);
            root.Controls.Add(buttonPanel, 0, 6);

            Controls.Add(root);
            ThemeHelper.ApplyTheme(this);

            Uninstaller.LogSink = AppendLog;
            AppendLog($"Note: Your messages and settings in %APPDATA%\\{_config.AppDataFolderName} are preserved during uninstall.");
            UpdateInstallState();
        }

        private static string BuildBannerText()
        {
            var word = "Installer";
            var binaryWord = string.Join(" ", word.Select(ch => Convert.ToString(ch, 2).PadLeft(8, '0')));
            var line = $"{binaryWord}  {binaryWord}  {binaryWord}";
            return string.Join(Environment.NewLine, Enumerable.Repeat(line, 3));
        }

        private void UpdateInstallState()
        {
            try
            {
                var path = Uninstaller.GetInstallPath();
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                {
                    _txtTarget.Text = path;
                    _btnInstall.Text = "Update";
                    _lblStatus.Text = $"Status: {_config.AppDisplayName} detected. Ready to update.";
                }
            }
            catch { }
        }

        private void AppendLog(string message)
        {
            try
            {
                if (_logBox.InvokeRequired)
                {
                    _logBox.BeginInvoke(new Action(() => AppendLog(message)));
                    return;
                }

                var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
                _logBox.AppendText(line + Environment.NewLine);
                _logBox.SelectionStart = _logBox.TextLength;
                _logBox.ScrollToCaret();
            }
            catch { }
        }

        private async System.Threading.Tasks.Task OnInstallButtonClickAsync()
        {
            if (_isFinished)
            {
                Close();
                return;
            }

            await InstallAsync();
        }

        private void BrowseTarget()
        {
            using var dlg = new FolderBrowserDialog();
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _txtTarget.Text = dlg.SelectedPath;
            }
        }

        private async System.Threading.Tasks.Task InstallAsync()
        {
            try
            {
                if (!await EnsureAppClosedBeforeInstallAsync())
                {
                    return;
                }

                _lblStatus.Text = "Status: Checking for legacy installations...";
                AppendLog("Checking for legacy installations...");
                var removedItems = await System.Threading.Tasks.Task.Run(() => Uninstaller.DetectAndRemoveLegacyInstalls());
                
                if (removedItems.Count > 0)
                {
                    var message = "Removed legacy installations:\n\n" + string.Join("\n", removedItems.Take(10));
                    if (removedItems.Count > 10)
                    {
                        message += $"\n... and {removedItems.Count - 10} more items";
                    }
                    MessageBox.Show(message, "Old Installations Removed", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    foreach (var item in removedItems.Take(50))
                    {
                        AppendLog("Removed: " + item);
                    }
                    if (removedItems.Count > 50) AppendLog($"Removed: ... and {removedItems.Count - 50} more items");
                }

                string? tempExtract = null;
                var src = string.Empty;
                var dst = _txtTarget.Text.Trim();
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = _config.PackageResourceName;
                foreach (var rn in asm.GetManifestResourceNames())
                {
                    if (rn.EndsWith(_config.PackageResourceName, StringComparison.OrdinalIgnoreCase))
                    {
                        resourceName = rn;
                        break;
                    }
                }
                using var rs = asm.GetManifestResourceStream(resourceName);
                if (rs != null)
                {
                    AppendLog("Extracting embedded package...");
                    var tmpZip = Path.Combine(Path.GetTempPath(), _config.ShortcutName + "_release_" + Guid.NewGuid().ToString("N") + ".zip");
                    using (var fs = File.Create(tmpZip)) rs.CopyTo(fs);
                    tempExtract = Path.Combine(Path.GetTempPath(), _config.ShortcutName + "_Package_" + Guid.NewGuid().ToString("N"));
                    System.IO.Compression.ZipFile.ExtractToDirectory(tmpZip, tempExtract);
                    try { File.Delete(tmpZip); } catch { }
                    src = tempExtract;
                }
                if (string.IsNullOrEmpty(src) || !Directory.Exists(src))
                {
                    MessageBox.Show($"Embedded {_config.PackageResourceName} not found inside the installer. Please embed it before publishing.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                _lblStatus.Text = "Status: Installing...";
                _btnInstall.Enabled = false;
                _btnUninstall.Enabled = false;

                // Ensure target exists
                Directory.CreateDirectory(dst);

                // Copy files recursively
                AppendLog($"Installing to {dst}");
                await System.Threading.Tasks.Task.Run(() => CopyDirectoryRecursive(src, dst));

                // Clean up any prior pins/shortcuts to avoid duplicate taskbar entries
                AppendLog("Cleaning old taskbar pins and shortcuts...");
                try { Uninstaller.RemoveTaskbarPins(); } catch { }
                try { Uninstaller.CleanupTrayIconSettings(); } catch { }
                try { Uninstaller.RemoveShortcutsNow(); } catch { }

                // Create shortcut on desktop and Start Menu
                AppendLog("Creating shortcuts...");
                try { ShortcutHelper.CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), _config.ShortcutName + ".lnk"), Path.Combine(dst, _config.ExecutableName), null, dst, _config.AppDisplayName, _config.AppUserModelId); } catch { }
                try { ShortcutHelper.CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", _config.ShortcutName + ".lnk"), Path.Combine(dst, _config.ExecutableName), null, dst, _config.AppDisplayName, _config.AppUserModelId); } catch { }

                // Write simple uninstall marker - the uninstaller will read this
                var uninstallerPath = Path.Combine(dst, "InstallMe.Lite.exe");
                // Copy this running executable into the install folder as the uninstaller
                var self = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(self) && File.Exists(self))
                {
                    try { File.Copy(self, uninstallerPath, true); } catch { /* ignore */ }
                }

                // Write uninstall registration in HKCU so Add/Remove Programs can display it
                AppendLog("Writing uninstall registration...");
                try { Uninstaller.WriteInstallPathToRegistry(dst); } catch { }

                _lblStatus.Text = "Status: Install complete.";
                AppendLog("Install complete.");
                _btnInstall.Text = "Finished";
                _btnInstall.Enabled = true;
                _btnUninstall.Enabled = false;
                _isFinished = true;
                if (!string.IsNullOrEmpty(tempExtract) && Directory.Exists(tempExtract))
                {
                    try { Directory.Delete(tempExtract, true); } catch { }
                }

                // Restart app if the user opted in
                if (_chkRestart.Checked)
                {
                    var exePath = Path.Combine(dst, _config.ExecutableName);
                    if (File.Exists(exePath))
                    {
                        AppendLog($"Restarting {_config.AppDisplayName}...");
                        try { Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true }); }
                        catch (Exception rex) { AppendLog("Failed to restart: " + rex.Message); }
                    }
                }
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "Status: Install failed: " + ex.Message;
                AppendLog("ERROR: " + ex.Message);
            }
            finally
            {
                _btnInstall.Enabled = true;
                _btnUninstall.Enabled = !_isFinished;
            }
        }

        private async System.Threading.Tasks.Task UninstallAsync()
        {
            try
            {
                if (!await EnsureAppClosedBeforeUninstallAsync())
                {
                    return;
                }

                var dst = _txtTarget.Text.Trim();
                if (string.IsNullOrEmpty(dst) || !Directory.Exists(dst))
                {
                    MessageBox.Show("Please select a valid install folder.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                AppendLog("Uninstall requested.");

                var result = MessageBox.Show(
                    $"This will completely remove {_config.AppDisplayName} from your system, including:\n\n" +
                    "• Installation files\n" +
                    "• Desktop and Start Menu shortcuts\n" +
                    "• Taskbar pinned entries\n" +
                    "• Windows Apps registry entries\n" +
                    "• Startup entries\n" +
                    "• Legacy app entries (if found)\n\n" +
                    $"Your user data in %APPDATA%\\{_config.AppDataFolderName} will be preserved.\n\n" +
                    "Continue with uninstall?",
                    "Confirm Uninstall",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result != DialogResult.Yes)
                    return;

                using var form = new UninstallForm(_config);
                form.Show();

                form.SetStatus("Stopping running processes...", 10);
                Application.DoEvents();
                Uninstaller.StopProcesses(dst);
                AppendLog("Stopped running processes.");

                form.SetStatus("Removing from Windows Startup...", 20);
                Application.DoEvents();
                Uninstaller.RemoveFromStartup();
                AppendLog("Removed startup entries.");

                form.SetStatus("Removing taskbar pins...", 30);
                Application.DoEvents();
                Uninstaller.RemoveTaskbarPins();
                AppendLog("Removed taskbar pins.");

                form.SetStatus("Cleaning tray icon cache...", 35);
                Application.DoEvents();
                Uninstaller.CleanupTrayIconSettings();
                AppendLog("Cleaned tray icon settings.");

                form.SetStatus("Removing shortcuts...", 40);
                Application.DoEvents();
                Uninstaller.RemoveShortcutsNow();
                AppendLog("Removed shortcuts.");

                form.SetStatus("Removing files...", 55);
                Application.DoEvents();
                if (!Uninstaller.DeleteDirectoryWithRetries(dst, 6, TimeSpan.FromSeconds(1)))
                {
                    form.SetStatus("Failed to remove files; scheduling cleanup on reboot.", 70);
                    Application.DoEvents();
                    Uninstaller.ScheduleDelete(dst);
                    AppendLog("Scheduled file removal on reboot.");
                }
                else
                {
                    form.SetStatus("Files removed.", 70);
                    Application.DoEvents();
                    AppendLog("Files removed.");
                }

                form.SetStatus("Cleaning Windows Apps registry...", 80);
                Application.DoEvents();
                Uninstaller.RemoveRegistryKey();
                AppendLog("Removed Windows Apps registry entries.");

                form.SetStatus("Removing legacy registry entries...", 88);
                Application.DoEvents();
                Uninstaller.CleanupRegistryHiveEntries();
                AppendLog("Removed legacy registry entries.");

                form.SetStatus("Removing any legacy app entries...", 90);
                Application.DoEvents();
                Uninstaller.DetectAndRemoveLegacyInstalls();
                AppendLog("Removed legacy app entries.");

                form.SetStatus("Refreshing Windows Apps list...", 95);
                Application.DoEvents();
                Uninstaller.ForceRefreshInstalledApps();
                AppendLog("Requested Windows Apps list refresh.");

                form.SetStatus("Uninstall complete.", 100);
                Application.DoEvents();
                AppendLog("Uninstall complete.");
                System.Threading.Thread.Sleep(800);
                form.Close();

                MessageBox.Show($"{_config.AppDisplayName} has been successfully uninstalled.\n\nYour user data remains in %APPDATA%\\{_config.AppDataFolderName} if you wish to reinstall later.\n\nNote: Windows may take a few moments to update the installed apps list.", "Uninstall Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "Status: Uninstall failed: " + ex.Message;
                AppendLog("ERROR: " + ex.Message);
                MessageBox.Show("Uninstall failed: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CopyDirectoryRecursive(string sourceDir, string destinationDir)
        {
            foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dir.Replace(sourceDir, destinationDir));
            }

            foreach (var file in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
            {
                var dest = file.Replace(sourceDir, destinationDir);
                File.Copy(file, dest, true);
                AppendLog("Copy: " + dest);
            }
        }

        private async System.Threading.Tasks.Task<bool> EnsureAppClosedBeforeInstallAsync()
        {
            var running = FindRunningAppProcesses();
            if (running.Count == 0)
            {
                return true;
            }

            var message =
                $"{_config.AppDisplayName} is currently running.\n\n" +
                $"Installation/Update cannot proceed while {_config.AppDisplayName} is open.\n\n" +
                $"Do you want setup to close {_config.AppDisplayName} now and continue?";

            var answer = MessageBox.Show(
                message,
                $"{_config.AppDisplayName} Is Running",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (answer != DialogResult.Yes)
            {
                _lblStatus.Text = $"Status: Install canceled ({_config.AppDisplayName} still running).";
                AppendLog($"Install canceled by user: {_config.AppDisplayName} is still running.");
                MessageBox.Show(
                    $"Installation cannot proceed until {_config.AppDisplayName} is shut down.",
                    "Install Canceled",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return false;
            }

            _lblStatus.Text = $"Status: Closing running {_config.AppDisplayName}...";
            AppendLog($"Attempting to close running {_config.AppDisplayName}...");

            await System.Threading.Tasks.Task.Run(() => TryCloseAppProcesses(running));

            var remaining = FindRunningAppProcesses();
            if (remaining.Count > 0)
            {
                _lblStatus.Text = $"Status: Install canceled (could not close {_config.AppDisplayName}).";
                AppendLog($"Install canceled: {_config.AppDisplayName} is still running after close attempt.");
                MessageBox.Show(
                    $"Installation cannot proceed until {_config.AppDisplayName} is shut down.",
                    "Install Canceled",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }

            // Client was running and we closed it — show and pre-check the restart option
            _chkRestart.Visible = true;
            _chkRestart.Checked = true;

            AppendLog($"{_config.AppDisplayName} closed successfully. Continuing install.");
            return true;
        }

        private async System.Threading.Tasks.Task<bool> EnsureAppClosedBeforeUninstallAsync()
        {
            var running = FindRunningAppProcesses();
            if (running.Count == 0)
            {
                return true;
            }

            var message =
                $"{_config.AppDisplayName} is currently running.\n\n" +
                $"Uninstall cannot proceed while {_config.AppDisplayName} is open.\n\n" +
                $"Do you want setup to close {_config.AppDisplayName} now and continue?";

            var answer = MessageBox.Show(
                message,
                $"{_config.AppDisplayName} Is Running",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (answer != DialogResult.Yes)
            {
                _lblStatus.Text = $"Status: Uninstall canceled ({_config.AppDisplayName} still running).";
                AppendLog($"Uninstall canceled by user: {_config.AppDisplayName} is still running.");
                MessageBox.Show(
                    $"Uninstall cannot proceed until {_config.AppDisplayName} is shut down.",
                    "Uninstall Canceled",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return false;
            }

            _lblStatus.Text = $"Status: Closing running {_config.AppDisplayName}...";
            AppendLog($"Attempting to close running {_config.AppDisplayName}...");

            await System.Threading.Tasks.Task.Run(() => TryCloseAppProcesses(running));

            var remaining = FindRunningAppProcesses();
            if (remaining.Count > 0)
            {
                _lblStatus.Text = $"Status: Uninstall canceled (could not close {_config.AppDisplayName}).";
                AppendLog($"Uninstall canceled: {_config.AppDisplayName} is still running after close attempt.");
                MessageBox.Show(
                    $"Uninstall cannot proceed until {_config.AppDisplayName} is shut down.",
                    "Uninstall Canceled",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }

            AppendLog($"{_config.AppDisplayName} closed successfully. Continuing uninstall.");
            return true;
        }

        private List<Process> FindRunningAppProcesses()
        {
            var matches = new List<Process>();
            var all = Process.GetProcesses();
            foreach (var process in all)
            {
                try
                {
                    if (process.HasExited) continue;
                    var name = process.ProcessName;
                    if (string.Equals(name, _config.ExecutableProcessName, StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add(process);
                    }
                    else
                    {
                        process.Dispose();
                    }
                }
                catch
                {
                    try { process.Dispose(); } catch { }
                }
            }
            return matches;
        }

        private static void TryCloseAppProcesses(IEnumerable<Process> processes)
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.HasExited)
                    {
                        process.Dispose();
                        continue;
                    }

                    try
                    {
                        if (process.MainWindowHandle != IntPtr.Zero)
                        {
                            process.CloseMainWindow();
                            if (process.WaitForExit(6000))
                            {
                                process.Dispose();
                                continue;
                            }
                        }
                    }
                    catch { }

                    try
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(3000);
                    }
                    catch { }
                }
                catch { }
                finally
                {
                    try { process.Dispose(); } catch { }
                }
            }
        }

        private void CreateDesktopShortcut(string targetFolder)
        {
            try
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var lnkPath = Path.Combine(desktop, _config.ShortcutName + ".lnk");
                var exePath = Path.Combine(targetFolder, _config.ExecutableName);

                // Create a simple .url as fallback if COM shortcut creation not available
                var useCom = false;
                if (!useCom)
                {
                    var content = "[InternetShortcut]\r\nURL=file://" + exePath + "\r\n";
                    File.WriteAllText(lnkPath, content);
                }
            }
            catch { }
        }
    }
}