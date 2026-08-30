using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace ClipTyper
{
    /// <summary>
    /// About dialog displaying application version, links, and GitHub update check results.
    /// Extracted from ClipTyperContext to improve modularity (TD-05, TD-06).
    /// </summary>
    public class AboutForm : Form
    {
        private readonly Label _infoLabel;
        private readonly Button _updateBtn;
        private readonly Label _updateLabel;
        private readonly Button _closeBtn;

        public event Action<UpdateChecker.UpdateCheckResult>? UpdateAvailable;

        public AboutForm(
            string hotkeyText,
            bool overlayActive,
            UpdateChecker.UpdateCheckResult? cachedUpdateResult)
        {
            Text = "About ClipTyper";
            Size = new Size(380, 260);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;

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

            _infoLabel = new Label
            {
                Text = $"ClipTyper v{version}\n\n" +
                       $"{triggerInfo}\n\n" +
                       "The last mile for text that cannot be pasted.\n" +
                       "Types clipboard text reliably into remote consoles & locked fields.",
                Location = new Point(15, 15),
                Size = new Size(340, 95),
                AutoSize = false
            };

            // ── Links ───────────────────────────────────────────────
            var repoLink = new LinkLabel
            {
                Text = "GitHub Repository",
                Location = new Point(15, 115),
                AutoSize = true
            };
            repoLink.Click += (_, _) => OpenUrl("https://github.com/unpaved028/ClipTyper");

            var issueLink = new LinkLabel
            {
                Text = "Report a Bug",
                Location = new Point(160, 115),
                AutoSize = true
            };
            issueLink.Click += (_, _) => OpenUrl("https://github.com/unpaved028/ClipTyper/issues/new");

            // ── Update Check ────────────────────────────────────────
            _updateBtn = new Button
            {
                Text = "Check for Updates",
                Location = new Point(15, 145),
                Size = new Size(140, 28)
            };

            _updateLabel = new Label
            {
                Text = "",
                Location = new Point(15, 180),
                Size = new Size(340, 22),
                AutoSize = false
            };

            _updateBtn.Click += async (_, _) =>
            {
                _updateBtn.Enabled = false;
                _updateBtn.Text = "Checking...";
                _updateLabel.Text = "";
                RemoveControlByName("_updateAction");

                var result = await UpdateChecker.CheckAsync();
                SettingsManager.Current.LastUpdateCheckUtc = DateTime.UtcNow;
                SettingsManager.Save();

                if (result == null)
                {
                    _updateLabel.Text = "Could not check for updates. Please check your internet connection.";
                    Size = new Size(380, 260);
                }
                else
                {
                    if (result.IsUpdateAvailable)
                    {
                        UpdateAvailable?.Invoke(result);
                    }
                    RenderUpdateResult(result);
                }

                _updateBtn.Text = "Check for Updates";
                _updateBtn.Enabled = true;
            };

            _closeBtn = new Button
            {
                Text = "Close",
                Location = new Point(270, 145),
                Size = new Size(80, 28),
                DialogResult = DialogResult.OK
            };

            Controls.AddRange(new Control[]
            {
                _infoLabel, repoLink, issueLink,
                _updateBtn, _updateLabel, _closeBtn
            });
            AcceptButton = _closeBtn;

            // Render cached update result if already known
            if (cachedUpdateResult != null && cachedUpdateResult.IsUpdateAvailable)
            {
                RenderUpdateResult(cachedUpdateResult);
            }
        }

        private void RenderUpdateResult(UpdateChecker.UpdateCheckResult result)
        {
            RemoveControlByName("_updateAction");

            if (result.IsUpdateAvailable)
            {
                _updateLabel.Text = $"Update available: v{result.LatestVersion}";
                int currentY = 205;

                // Display release notes snippet if present
                if (!string.IsNullOrWhiteSpace(result.ReleaseNotes))
                {
                    string notes = result.ReleaseNotes.Trim();
                    if (notes.Length > 300)
                    {
                        notes = notes.Substring(0, 300).TrimEnd() + "...";
                    }

                    var notesBox = new TextBox
                    {
                        Name = "_updateAction",
                        Text = notes,
                        Location = new Point(15, currentY),
                        Size = new Size(335, 70),
                        Multiline = true,
                        ReadOnly = true,
                        ScrollBars = ScrollBars.Vertical,
                        BackColor = SystemColors.Control,
                        BorderStyle = BorderStyle.FixedSingle,
                        Font = new Font("Segoe UI", 8.5f)
                    };

                    var readMoreLink = new LinkLabel
                    {
                        Name = "_updateAction",
                        Text = "Read more...",
                        Location = new Point(15, currentY + 75),
                        AutoSize = true
                    };
                    readMoreLink.Click += (_, _) => OpenUrl(result.ReleaseUrl);

                    Controls.AddRange(new Control[] { notesBox, readMoreLink });
                    currentY += 98;
                }

                if (!SettingsManager.IsPortable)
                {
                    // Winget mode: show winget command with copy button
                    string wingetCmd = "winget upgrade unpaved028.ClipTyper";
                    var cmdLabel = new Label
                    {
                        Name = "_updateAction",
                        Text = wingetCmd,
                        Location = new Point(15, currentY + 3),
                        Size = new Size(240, 20),
                        Font = new Font("Consolas", 8.5f)
                    };

                    var copyCmdBtn = new Button
                    {
                        Name = "_updateAction",
                        Text = "Copy",
                        Location = new Point(270, currentY),
                        Size = new Size(80, 24)
                    };
                    copyCmdBtn.Click += (_, _) =>
                    {
                        try
                        {
                            Clipboard.SetText(wingetCmd);
                            copyCmdBtn.Text = "✓ Copied";
                        }
                        catch { }
                    };

                    Controls.AddRange(new Control[] { cmdLabel, copyCmdBtn });
                    currentY += 32;
                }
                else
                {
                    // Portable mode: link directly to release page
                    var downloadLink = new LinkLabel
                    {
                        Name = "_updateAction",
                        Text = "Download latest release on GitHub",
                        Location = new Point(15, currentY + 3),
                        AutoSize = true
                    };
                    downloadLink.Click += (_, _) => OpenUrl(result.ReleaseUrl);

                    Controls.Add(downloadLink);
                    currentY += 28;
                }

                Size = new Size(380, currentY + 45);
            }
            else
            {
                _updateLabel.Text = $"ClipTyper is up to date (v{result.CurrentVersion}).";
                Size = new Size(380, 260);
            }
        }

        private void RemoveControlByName(string name)
        {
            for (int i = Controls.Count - 1; i >= 0; i--)
            {
                if (Controls[i].Name == name)
                {
                    var c = Controls[i];
                    Controls.RemoveAt(i);
                    c.Dispose();
                }
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
