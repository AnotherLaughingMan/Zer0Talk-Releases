using System;
using System.Windows.Forms;

namespace InstallMe.Lite
{
    public class UninstallForm : Form
    {
        private readonly InstallerConfig _config;
        private ProgressBar _progress;
        private Label _lblStatus;
        private Button _btnCancel;
        public bool CancelRequested { get; private set; }

        public UninstallForm(InstallerConfig config)
        {
            _config = config;
            Text = $"Uninstalling {_config.AppDisplayName}";
            Width = 520;
            Height = 200;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16),
                ColumnCount = 1,
                RowCount = 4
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var title = new Label
            {
                Text = $"Please wait while we remove {_config.AppDisplayName}",
                AutoSize = true,
                Font = new System.Drawing.Font(Font.FontFamily, 11, System.Drawing.FontStyle.Bold)
            };

            _lblStatus = new Label { Text = "Preparing to uninstall...", AutoSize = true, Margin = new Padding(0, 8, 0, 8) };
            _progress = new ProgressBar { Dock = DockStyle.Top, Height = 18 };

            _btnCancel = new Button { Text = "Cancel", AutoSize = true, MinimumSize = new System.Drawing.Size(90, 28) };
            _btnCancel.Click += (s, e) => { CancelRequested = true; _btnCancel.Enabled = false; };

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true
            };
            buttonPanel.Controls.Add(_btnCancel);

            root.Controls.Add(title, 0, 0);
            root.Controls.Add(_lblStatus, 0, 1);
            root.Controls.Add(_progress, 0, 2);
            root.Controls.Add(buttonPanel, 0, 3);

            Controls.Add(root);
            ThemeHelper.ApplyTheme(this);
        }

        public void SetStatus(string text, int percent)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SetStatus(text, percent)));
                return;
            }
            _lblStatus.Text = text;
            _progress.Value = Math.Max(0, Math.Min(100, percent));
        }
    }
}
