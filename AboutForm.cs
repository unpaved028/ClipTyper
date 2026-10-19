using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace ClipTyper
{
    /// <summary>
    /// About dialog displaying application version, links, and GitHub update check results.
    /// Extracted from ClipTyperContext to improve modularity (TD-05, TD-06).
    /// All controls are created up front in auto-sizing panels, so the dialog scales with the
    /// monitor DPI and grows or shrinks when the update section is shown.
    /// </summary>
    public class AboutForm : Form
    {
        private const string WingetUpgradeCommand = "winget upgrade unpaved028.ClipTyper";
        private const int ContentWidth = 340;

        private readonly Button _updateBtn;
        private readonly Label _updateLabel;
        private readonly TextBox _notesBox;
        private readonly LinkLabel _readMoreLink;
        private readonly FlowLayoutPanel _wingetRow;
        private readonly Button _copyCmdBtn;
        private readonly LinkLabel _downloadLink;
        private string _releaseUrl = "https://github.com/unpaved028/ClipTyper/releases/latest";

        public event Action<UpdateChecker.UpdateCheckResult>? UpdateAvailable;

        public AboutForm(
            string hotkeyText,
            bool overlayActive,
            UpdateChecker.UpdateCheckResult? cachedUpdateResult)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;

            Text = "About ClipTyper";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            string version = UpdateChecker.GetCurrentVersion();

            // Build dynamic trigger description
            string triggerInfo;
            if (hotkeyText != "None" && overlayActive)
            {
                triggerInfo = $"Press {hotkeyText} or click the Overlay to type the clipboard contents.";
            }
            else if (hotkeyText != "None")
            {
                triggerInfo = $"Press {hotkeyText} to type the clipboard contents.";
            }
            else if (overlayActive)
            {
                triggerInfo = "Click the Overlay to type the clipboard contents.";
            }
            else
            {
                triggerInfo = "Configure a hotkey or enable the Overlay in Settings.";
            }

            var infoLabel = new Label
            {
                Text = $"ClipTyper v{version}\n\n" +
                       $"{triggerInfo}\n\n" +
                       "The last mile for text that cannot be pasted.\n" +
                       "Types clipboard text reliably into remote consoles & locked fields.",
                AutoSize = true,
                MaximumSize = new Size(ContentWidth, 0),
                UseMnemonic = false,
                Margin = new Padding(0, 0, 0, 10)
            };

            // ── Links ───────────────────────────────────────────────
            var repoLink = new LinkLabel { Text = "GitHub Repository", AutoSize = true, Margin = new Padding(0, 0, 24, 0) };
            repoLink.LinkClicked += (_, _) => OpenUrl("https://github.com/unpaved028/ClipTyper");
            var issueLink = new LinkLabel { Text = "Report a Bug", AutoSize = true, Margin = Padding.Empty };
            issueLink.LinkClicked += (_, _) => OpenUrl("https://github.com/unpaved028/ClipTyper/issues/new/choose");
            var linkRow = Row(repoLink, issueLink);
            linkRow.Margin = new Padding(0, 0, 0, 12);

            // ── Update check & close ────────────────────────────────
            _updateBtn = new Button { Text = "Check for Updates", Size = new Size(140, 28), Margin = new Padding(0, 0, 115, 0) };
            _updateBtn.Click += OnCheckForUpdatesClick;
            var closeBtn = new Button { Text = "Close", Size = new Size(85, 28), DialogResult = DialogResult.OK, Margin = Padding.Empty };
            var buttonRow = Row(_updateBtn, closeBtn);

            _updateLabel = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(ContentWidth, 0),
                Margin = new Padding(0, 10, 0, 0),
                Visible = false
            };

            _notesBox = new TextBox
            {
                Size = new Size(ContentWidth, 110),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = SystemColors.Control,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 6, 0, 0),
                Visible = false
            };

            _readMoreLink = new LinkLabel { Text = "Read more...", AutoSize = true, Margin = new Padding(0, 4, 0, 0), Visible = false };
            _readMoreLink.LinkClicked += (_, _) => OpenUrl(_releaseUrl);

            // Installed (Winget) mode: winget command with copy button
            var cmdLabel = new Label
            {
                Text = WingetUpgradeCommand,
                AutoSize = true,
                Font = new Font("Consolas", 8.5f),
                Margin = new Padding(0, 6, 12, 0)
            };
            _copyCmdBtn = new Button { Text = "Copy", Size = new Size(80, 24), Margin = Padding.Empty };
            _copyCmdBtn.Click += (_, _) =>
            {
                try
                {
                    Clipboard.SetText(WingetUpgradeCommand);
                    _copyCmdBtn.Text = "✓ Copied";
                }
                catch { }
            };
            _wingetRow = Row(cmdLabel, _copyCmdBtn);
            _wingetRow.Margin = new Padding(0, 8, 0, 0);
            _wingetRow.Visible = false;

            // Portable mode: link directly to release page
            _downloadLink = new LinkLabel { Text = "Download latest release on GitHub", AutoSize = true, Margin = new Padding(0, 8, 0, 0), Visible = false };
            _downloadLink.LinkClicked += (_, _) => OpenUrl(_releaseUrl);

            var content = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(15),
                Margin = Padding.Empty
            };
            content.Controls.AddRange(new Control[]
            {
                infoLabel, linkRow, buttonRow, _updateLabel, _notesBox, _readMoreLink, _wingetRow, _downloadLink
            });
            Controls.Add(content);
            foreach (var link in new[] { repoLink, issueLink, _readMoreLink, _downloadLink })
            {
                Theme.StyleLink(link);
            }

            AcceptButton = closeBtn;
            CancelButton = closeBtn;

            // Render cached update result if already known
            if (cachedUpdateResult != null && cachedUpdateResult.IsUpdateAvailable)
            {
                RenderUpdateResult(cachedUpdateResult);
            }

            ResumeLayout(true);
        }

        private static FlowLayoutPanel Row(params Control[] controls)
        {
            var row = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = Padding.Empty
            };
            row.Controls.AddRange(controls);
            return row;
        }

        private async void OnCheckForUpdatesClick(object? sender, EventArgs e)
        {
            _updateBtn.Enabled = false;
            _updateBtn.Text = "Checking...";
            HideUpdateDetails();

            var result = await UpdateChecker.CheckAsync();
            if (IsDisposed)
            {
                return;
            }

            if (result == null)
            {
                _updateLabel.Text = "Could not check for updates. Please check your internet connection.";
                _updateLabel.Visible = true;
            }
            else
            {
                SettingsManager.Current.LastUpdateCheckUtc = DateTime.UtcNow;
                SettingsManager.Save();

                if (result.IsUpdateAvailable)
                {
                    UpdateAvailable?.Invoke(result);
                }
                RenderUpdateResult(result);
            }

            _updateBtn.Text = "Check for Updates";
            _updateBtn.Enabled = true;
        }

        private void HideUpdateDetails()
        {
            _updateLabel.Visible = false;
            _notesBox.Visible = false;
            _readMoreLink.Visible = false;
            _wingetRow.Visible = false;
            _downloadLink.Visible = false;
        }

        private void RenderUpdateResult(UpdateChecker.UpdateCheckResult result)
        {
            HideUpdateDetails();
            _releaseUrl = result.ReleaseUrl;

            if (!result.IsUpdateAvailable)
            {
                _updateLabel.Text = $"ClipTyper is up to date (v{result.CurrentVersion}).";
                _updateLabel.Visible = true;
                return;
            }

            _updateLabel.Text = $"Update available: v{result.LatestVersion}";
            _updateLabel.Visible = true;

            string notes = UpdateChecker.FormatReleaseNotes(result.ReleaseNotes);
            if (notes.Length > 0)
            {
                _notesBox.Text = notes;
                _notesBox.Visible = true;
                _readMoreLink.Visible = true;
            }

            if (SettingsManager.IsPortable)
            {
                _downloadLink.Visible = true;
            }
            else
            {
                _copyCmdBtn.Text = "Copy";
                _wingetRow.Visible = true;
            }
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }
    }
}
