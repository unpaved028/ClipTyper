using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClipTyper
{
    public class ClipTyperContext : ApplicationContext
    {
        private NotifyIcon _trayIcon;
        private GlobalHotkey _hotkey;
        private GlobalHotkey? _overlayToggleHotkey;
        private OverlayForm? _overlay;
        private const int HotkeyId = 1;
        private const int OverlayToggleHotkeyId = 2;

        // Hidden form to receive Windows messages
        private class HotkeyForm : Form
        {
            public event Action? HotkeyPressed;
            public event Action? OverlayToggleHotkeyPressed;

            public HotkeyForm()
            {
                this.FormBorderStyle = FormBorderStyle.None;
                this.ShowInTaskbar = false;
                this.Load += (s, e) => { this.Size = new Size(0, 0); };
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == GlobalHotkey.WM_HOTKEY)
                {
                    int id = m.WParam.ToInt32();
                    if (id == HotkeyId)
                    {
                        HotkeyPressed?.Invoke();
                    }
                    else if (id == OverlayToggleHotkeyId)
                    {
                        OverlayToggleHotkeyPressed?.Invoke();
                    }
                }
                base.WndProc(ref m);
            }
        }

        private HotkeyForm _hiddenForm;

        /// <summary>
        /// Loads the embedded app icon (256x256) from the assembly resources.
        /// Falls back to the default application icon if not found.
        /// </summary>
        private static Icon LoadEmbeddedIcon()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                // Resource logical name set in .csproj: ClipTyper.icon_app.ico
                using var stream = assembly.GetManifestResourceStream("ClipTyper.icon_app.ico");
                if (stream != null)
                {
                    return new Icon(stream, 32, 32);
                }
            }
            catch { /* fall through to default */ }

            return SystemIcons.Application;
        }

        public ClipTyperContext()
        {
            // Load persisted settings
            SettingsManager.Load();
            var settings = SettingsManager.Current;

            _trayIcon = new NotifyIcon()
            {
                Icon = LoadEmbeddedIcon(),
                ContextMenuStrip = new ContextMenuStrip(),
                Visible = true,
                Text = "ClipTyper"
            };

            _trayIcon.ContextMenuStrip.Items.Add("Settings", null, OnSettings);
            _trayIcon.ContextMenuStrip.Items.Add("About", null, OnAbout);
            _trayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
            _trayIcon.ContextMenuStrip.Items.Add("Exit", null, OnExit);

            _hiddenForm = new HotkeyForm();
            _hiddenForm.HotkeyPressed += OnHotkeyPressed;
            _hiddenForm.OverlayToggleHotkeyPressed += OnOverlayToggleHotkeyPressed;

            var handle = _hiddenForm.Handle;

            // Register the hotkey from settings (or default Ctrl+Shift+T)
            _hotkey = new GlobalHotkey(
                _hiddenForm.Handle,
                HotkeyId,
                (GlobalHotkey.Modifiers)settings.HotkeyModifiers,
                (Keys)settings.HotkeyKey);

            // Register the overlay toggle hotkey if enabled
            if (settings.OverlayToggleEnabled)
            {
                _overlayToggleHotkey = new GlobalHotkey(
                    _hiddenForm.Handle,
                    OverlayToggleHotkeyId,
                    (GlobalHotkey.Modifiers)settings.OverlayToggleModifiers,
                    (Keys)settings.OverlayToggleKey);
            }

            // Show overlay if enabled in settings
            if (settings.OverlayEnabled)
            {
                ShowOverlay();
            }

            // Winget/installed mode: ensure Start Menu shortcut + autostart
            if (!SettingsManager.IsPortable)
            {
                InstallHelper.EnsureShortcut();
                InstallHelper.SetAutoStart(settings.AutoStartEnabled);
            }
        }

        // ── Shared Clip-Type Trigger ────────────────────────────────

        /// <summary>
        /// Core clip-type logic shared by both the hotkey and the overlay.
        /// Reads the clipboard and types its text content via SendInput.
        /// </summary>
        /// <param name="restoreFocus">
        /// When true, focus has already been restored by the caller (overlay).
        /// When false, the hotkey was used and focus is already on the target.
        /// </param>
        private void TriggerClipType(bool restoreFocus)
        {
            string textToType = "";
            try
            {
                // Clipboard must be accessed from an STA thread
                if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
                {
                    if (Clipboard.ContainsText())
                    {
                        textToType = Clipboard.GetText();
                    }
                }
                else
                {
                    // Marshal to STA thread for clipboard access
                    var thread = new Thread(() =>
                    {
                        if (Clipboard.ContainsText())
                        {
                            textToType = Clipboard.GetText();
                        }
                    });
                    thread.SetApartmentState(ApartmentState.STA);
                    thread.Start();
                    thread.Join(2000);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Clipboard error: {ex.Message}");
            }

            if (!string.IsNullOrEmpty(textToType))
            {
                int delay = SettingsManager.Current.KeystrokeDelayMs;
                KeyboardSimulator.SendText(textToType, delay);
            }
        }

        // ── Hotkey Handler ──────────────────────────────────────────

        private void OnHotkeyPressed()
        {
            Task.Run(() =>
            {
                Thread.Sleep(100);
                TriggerClipType(restoreFocus: false);
            });
        }

        private void OnOverlayToggleHotkeyPressed()
        {
            if (_hiddenForm.InvokeRequired)
            {
                _hiddenForm.BeginInvoke(new Action(OnOverlayToggleHotkeyPressed));
                return;
            }

            var settings = SettingsManager.Current;
            settings.OverlayEnabled = !settings.OverlayEnabled;
            SettingsManager.Save();

            if (settings.OverlayEnabled)
            {
                ShowOverlay();
            }
            else
            {
                HideOverlay();
            }
        }

        // ── Overlay Management ──────────────────────────────────────

        private void ShowOverlay()
        {
            if (_overlay != null) return;

            _overlay = new OverlayForm((restoreFocus) =>
            {
                TriggerClipType(restoreFocus);
            });

            _overlay.OverlayHidden += () =>
            {
                HideOverlay();
                SettingsManager.Current.OverlayEnabled = false;
                SettingsManager.Save();
            };

            _overlay.Show();
        }

        private void HideOverlay()
        {
            if (_overlay == null) return;
            _overlay.Close();
            _overlay.Dispose();
            _overlay = null;
        }

        // ── Settings Dialog ─────────────────────────────────────────

        private void OnSettings(object? sender, EventArgs e)
        {
            int originalScale = SettingsManager.Current.OverlayScalePercent;
            using var form = new SettingsForm();

            // Set up live preview handler
            Action<int> liveScaleHandler = (scale) =>
            {
                if (_overlay != null && SettingsManager.Current.OverlayEnabled)
                {
                    _overlay.ApplyScale(scale);
                }
            };
            form.LiveScaleChanged += liveScaleHandler;

            form.SettingsSaved += (modifiers, key, delay, overlayEnabled, overlayScale, overlayMonitor, resetPosition, autoStart, toggleMods, toggleKey, toggleEnabled) =>
            {
                // Try to update the trigger hotkey
                if (modifiers != _hotkey.CurrentModifier || key != _hotkey.CurrentKey)
                {
                    if (!_hotkey.Reregister(modifiers, key))
                    {
                        MessageBox.Show(
                            "Could not register the hotkey. It may be in use by another application.\n\n" +
                            "The previous hotkey has been restored.",
                            "Hotkey Registration Failed",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }
                }

                // Try to update overlay toggle hotkey
                if (toggleEnabled)
                {
                    if (_overlayToggleHotkey == null)
                    {
                        _overlayToggleHotkey = new GlobalHotkey(_hiddenForm.Handle, OverlayToggleHotkeyId, toggleMods, toggleKey);
                    }
                    else if (toggleMods != _overlayToggleHotkey.CurrentModifier || toggleKey != _overlayToggleHotkey.CurrentKey)
                    {
                        if (!_overlayToggleHotkey.Reregister(toggleMods, toggleKey))
                        {
                            MessageBox.Show(
                                "Could not register the overlay toggle hotkey. It may be in use by another application.\n\n" +
                                "The previous hotkey has been restored.",
                                "Hotkey Registration Failed",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                            return;
                        }
                    }
                }
                else
                {
                    _overlayToggleHotkey?.Dispose();
                    _overlayToggleHotkey = null;
                }

                // Save all settings
                var s = SettingsManager.Current;
                s.HotkeyModifiers = (int)modifiers;
                s.HotkeyKey = (int)key;
                s.KeystrokeDelayMs = delay;
                s.OverlayEnabled = overlayEnabled;
                s.OverlayScalePercent = overlayScale;
                s.OverlayMonitorIndex = overlayMonitor;
                s.AutoStartEnabled = autoStart;
                s.OverlayToggleModifiers = (int)toggleMods;
                s.OverlayToggleKey = (int)toggleKey;
                s.OverlayToggleEnabled = toggleEnabled;

                if (resetPosition)
                {
                    s.OverlayX = -1;
                    s.OverlayY = -1;
                }

                SettingsManager.Save();

                // Apply overlay changes
                if (overlayEnabled)
                {
                    if (_overlay != null)
                    {
                        _overlay.ApplyScale(overlayScale);
                        _overlay.MoveToMonitor(overlayMonitor);
                        if (resetPosition)
                        {
                            _overlay.MoveToDefaultPosition();
                        }
                    }
                    else
                    {
                        ShowOverlay();
                    }
                }
                else
                {
                    HideOverlay();
                }

                // Apply autostart changes (Winget mode only)
                if (!SettingsManager.IsPortable)
                {
                    InstallHelper.SetAutoStart(autoStart);
                }
            };

            if (form.ShowDialog() != DialogResult.OK)
            {
                // Revert any live scale preview if canceled
                if (_overlay != null && SettingsManager.Current.OverlayEnabled)
                {
                    _overlay.ApplyScale(originalScale);
                }
            }
        }

        // ── About Dialog ────────────────────────────────────────────

        private void OnAbout(object? sender, EventArgs e)
        {
            string version = UpdateChecker.GetCurrentVersion();
            string hotkeyText = SettingsForm.FormatHotkey(_hotkey.CurrentModifier, _hotkey.CurrentKey);
            bool overlayActive = _overlay != null && SettingsManager.Current.OverlayEnabled;

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

            var aboutForm = new Form
            {
                Text = "About ClipTyper",
                Size = new Size(380, 310),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterScreen,
                ShowInTaskbar = false
            };

            var infoLabel = new Label
            {
                Text = $"ClipTyper v{version}\n\n" +
                       $"{triggerInfo}\n\n" +
                       "A lightweight portable typing tool for simulating\n" +
                       "keyboard input from clipboard text.",
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

            var updateBtn = new Button
            {
                Text = "Check for Updates",
                Location = new Point(15, 145),
                Size = new Size(140, 28)
            };

            var updateLabel = new Label
            {
                Text = "",
                Location = new Point(15, 180),
                Size = new Size(340, 40),
                AutoSize = false
            };

            updateBtn.Click += async (_, _) =>
            {
                updateBtn.Enabled = false;
                updateBtn.Text = "Checking...";
                updateLabel.Text = "";

                // Remove any previously added update action controls
                RemoveControlByName(aboutForm, "_updateAction");

                var result = await UpdateChecker.CheckAsync();

                if (result == null)
                {
                    updateLabel.Text = "Could not check for updates. Please check your internet connection.";
                }
                else if (result.IsUpdateAvailable)
                {
                    updateLabel.Text = $"Update available: v{result.LatestVersion}";

                    if (!SettingsManager.IsPortable)
                    {
                        // Winget mode: show winget command with copy button
                        string wingetCmd = "winget upgrade unpaved028.ClipTyper";
                        var cmdLabel = new Label
                        {
                            Name = "_updateAction",
                            Text = wingetCmd,
                            Location = new Point(15, 205),
                            AutoSize = true,
                            Font = new Font("Consolas", 9f),
                            ForeColor = Color.DarkBlue
                        };

                        var copyBtn = new Button
                        {
                            Name = "_updateAction",
                            Text = "📋 Copy",
                            Location = new Point(290, 201),
                            Size = new Size(65, 23)
                        };
                        copyBtn.Click += (_, _) =>
                        {
                            Clipboard.SetText(wingetCmd);
                            copyBtn.Text = "✓ Copied";
                        };

                        aboutForm.Controls.AddRange(new Control[] { cmdLabel, copyBtn });
                    }
                    else
                    {
                        // Portable/Slim: show GitHub download link
                        var downloadLink = new LinkLabel
                        {
                            Name = "_updateAction",
                            Text = "Download from GitHub",
                            Location = new Point(15, 205),
                            AutoSize = true
                        };
                        downloadLink.Click += (_, _) => OpenUrl(result.ReleaseUrl);
                        aboutForm.Controls.Add(downloadLink);
                    }
                }
                else
                {
                    updateLabel.Text = $"You're running the latest version (v{result.CurrentVersion}).";
                }

                updateBtn.Text = "Check for Updates";
                updateBtn.Enabled = true;
            };

            var closeBtn = new Button
            {
                Text = "Close",
                Location = new Point(270, 145),
                Size = new Size(80, 28),
                DialogResult = DialogResult.OK
            };

            aboutForm.Controls.AddRange(new Control[]
            {
                infoLabel, repoLink, issueLink,
                updateBtn, updateLabel, closeBtn
            });
            aboutForm.AcceptButton = closeBtn;
            aboutForm.ShowDialog();
        }

        /// <summary>
        /// Opens a URL in the default browser.
        /// </summary>
        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        /// <summary>
        /// Removes all controls with the specified Name from a form.
        /// Used to clean up dynamically added update action controls.
        /// </summary>
        private static void RemoveControlByName(Form form, string name)
        {
            for (int i = form.Controls.Count - 1; i >= 0; i--)
            {
                if (form.Controls[i].Name == name)
                {
                    form.Controls.RemoveAt(i);
                }
            }
        }

        // ── Exit ────────────────────────────────────────────────────

        private void OnExit(object? sender, EventArgs e)
        {
            _trayIcon.Visible = false;
            _hotkey?.Dispose();
            _overlayToggleHotkey?.Dispose();
            _overlay?.Dispose();
            _hiddenForm?.Dispose();
            Application.Exit();
        }
    }
}
