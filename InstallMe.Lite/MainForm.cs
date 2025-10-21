using System;
using System.IO;
using System.Windows.Forms;

namespace InstallMe.Lite
{
    public class MainForm : Form
    {
        private Button _btnInstall;
        private Button _btnUninstall;
        private TextBox _txtSource;
        private TextBox _txtTarget;
        private Label _lblStatus;

        public MainForm()
        {
            Text = "InstallMe Lite - Zer0Talk Installer";
            Width = 600;
            Height = 260;

            var lblSource = new Label { Text = "Source Folder:", Left = 10, Top = 20, Width = 100 };
            _txtSource = new TextBox { Left = 120, Top = 20, Width = 380 };
            var btnBrowse = new Button { Text = "Browse...", Left = 510, Top = 18, Width = 60 };
            btnBrowse.Click += (s, e) => BrowseSource();

            var lblTarget = new Label { Text = "Install To:", Left = 10, Top = 60, Width = 100 };
            _txtTarget = new TextBox { Left = 120, Top = 60, Width = 380, Text = "C:\\Apps\\Zer0Talk" };
            var btnTargetBrowse = new Button { Text = "Browse...", Left = 510, Top = 58, Width = 60 };
            btnTargetBrowse.Click += (s, e) => BrowseTarget();

            _btnInstall = new Button { Text = "Install", Left = 120, Top = 100, Width = 120 };
            _btnInstall.Click += async (s, e) => await InstallAsync();

            _btnUninstall = new Button { Text = "Uninstall", Left = 250, Top = 100, Width = 120 };
            _btnUninstall.Click += (s, e) => Uninstall();

            _lblStatus = new Label { Text = "Status: Ready", Left = 10, Top = 150, Width = 560, Height = 50 };

            Controls.AddRange(new Control[] { lblSource, _txtSource, btnBrowse, lblTarget, _txtTarget, btnTargetBrowse, _btnInstall, _btnUninstall, _lblStatus });
        }

        private void BrowseSource()
        {
            using var dlg = new FolderBrowserDialog();
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _txtSource.Text = dlg.SelectedPath;
            }
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
                var src = _txtSource.Text.Trim();
                var dst = _txtTarget.Text.Trim();
                if (string.IsNullOrEmpty(src) || !Directory.Exists(src))
                {
                    MessageBox.Show("Please select a valid source folder.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                _lblStatus.Text = "Status: Installing...";
                _btnInstall.Enabled = false;
                _btnUninstall.Enabled = false;

                // Ensure target exists
                Directory.CreateDirectory(dst);

                // Copy files recursively
                await System.Threading.Tasks.Task.Run(() => CopyDirectoryRecursive(src, dst));

                // Create shortcut on desktop
                CreateDesktopShortcut(dst);

                // Write simple uninstall marker - the uninstaller will read this
                var uninstallerPath = Path.Combine(dst, "uninstall.exe");
                // For now, just copy this executable as the uninstaller placeholder if exists in source
                var self = System.Reflection.Assembly.GetEntryAssembly()?.Location;
                if (!string.IsNullOrEmpty(self) && File.Exists(self))
                {
                    try { File.Copy(self, uninstallerPath, true); } catch { /* ignore */ }
                }

                _lblStatus.Text = "Status: Install complete.";
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "Status: Install failed: " + ex.Message;
            }
            finally
            {
                _btnInstall.Enabled = true;
                _btnUninstall.Enabled = true;
            }
        }

        private void Uninstall()
        {
            try
            {
                var dst = _txtTarget.Text.Trim();
                if (string.IsNullOrEmpty(dst) || !Directory.Exists(dst))
                {
                    MessageBox.Show("Please select a valid install folder.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Remove files except %APPDATA%\Zer0Talk which is outside install folder
                Directory.Delete(dst, true);

                // Remove desktop shortcut
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var lnk = Path.Combine(desktop, "Zer0Talk.lnk");
                if (File.Exists(lnk)) File.Delete(lnk);

                _lblStatus.Text = "Status: Uninstall complete.";
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "Status: Uninstall failed: " + ex.Message;
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
            }
        }

        private void CreateDesktopShortcut(string targetFolder)
        {
            try
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var lnkPath = Path.Combine(desktop, "Zer0Talk.lnk");
                var exePath = Path.Combine(targetFolder, "Zer0Talk.exe");

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