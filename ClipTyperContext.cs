using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace ClipTyper
{
    public class ClipTyperContext : ApplicationContext
    {
        private NotifyIcon _trayIcon;
        private ToolStripMenuItem _stopTypingMenuItem = null!;
        private ToolStripSeparator _stopTypingSeparator = null!;
        private Font? _boldMenuFont;
        private GlobalHotkey _hotkey;
        private GlobalHotkey? _overlayToggleHotkey;
        private OverlayForm? _overlay;
        private Icon? _badgedTrayIcon;
        private UpdateChecker.UpdateCheckResult? _cachedUpdateResult;
        private readonly TypingService _typingService;
        private const int HotkeyId = 1;
        private const int OverlayToggleHotkeyId = 2;

        // Hidden form to receive Windows messages
        private class HotkeyForm : Form
        {
            public event Action? HotkeyPressed;
            public event Action? OverlayToggleHotkeyPressed;
            public event Action? CloseRequested;

            private const int WM_CLOSE = 0x0010;
            private const int WM_QUERYENDSESSION = 0x0011;
            private const int WM_ENDSESSION = 0x0016;

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
                else if (m.Msg == WM_CLOSE || m.Msg == WM_QUERYENDSESSION || m.Msg == WM_ENDSESSION)
                {
                    CloseRequested?.Invoke();
                }
                base.WndProc(ref m);
            }
        }

        private readonly HotkeyForm _hiddenForm;

        /// <summary>
        /// Loads the embedded app icon (256x256) from the assembly resources.
        /// Falls back to the default application icon if not found.
        /// </summary>
        private static Icon LoadEmbeddedIcon()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
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
            // TD-74: Ensure WinForms SynchronizationContext is installed before capturing
            if (SynchronizationContext.Current == null)
            {
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            }

            // Load persisted settings
            SettingsManager.Load();
            var settings = SettingsManager.Current;

            _hiddenForm = new HotkeyForm();
            _hiddenForm.HotkeyPressed += OnHotkeyPressed;
            _hiddenForm.OverlayToggleHotkeyPressed += OnOverlayToggleHotkeyPressed;
            _hiddenForm.CloseRequested += () => OnExit(this, EventArgs.Empty);

            var handle = _hiddenForm.Handle;

            _trayIcon = new NotifyIcon()
            {
                Icon = LoadEmbeddedIcon(),
                ContextMenuStrip = new ContextMenuStrip(),
                Visible = true,
                Text = "ClipTyper"
            };

            _typingService = new TypingService(SynchronizationContext.Current);
            _typingService.ProgressChanged += (isTyping, current, total) =>
            {
                _overlay?.SetTypingState(isTyping, current, total);
                // TD-09: Update tray icon tooltip text with progress
                if (isTyping)
                {
                    string progressText = (total > 0) ? $"ClipTyper ({current * 100 / total}%)" : "ClipTyper (Typing...)";
                    if (_trayIcon.Text != progressText) _trayIcon.Text = progressText;
                }
                else
                {
                    _trayIcon.Text = "ClipTyper";
                }
            };

            _typingService.TypingCompleted += (completed) =>
            {
                _overlay?.SetTypingState(false, completed: completed);
                _trayIcon.Text = "ClipTyper";
            };

            // TD-41: User feedback when clipboard has no usable text
            _typingService.ClipboardEmpty += () =>
            {
                _trayIcon.ShowBalloonTip(
                    3000,
                    "ClipTyper",
                    "The clipboard contains no text to type.",
                    ToolTipIcon.Info);
            };

            // TD-70: Build tray menu once, cache bold font, avoid allocation leaks on menu open
            _boldMenuFont = new Font(_trayIcon.ContextMenuStrip.Font, FontStyle.Bold);
            _stopTypingMenuItem = new ToolStripMenuItem("⏹ Stop Typing", null, (_, _) => _typingService.CancelTyping())
            {
                Font = _boldMenuFont,
                ForeColor = Color.DarkRed,
                Visible = false
            };
            _stopTypingSeparator = new ToolStripSeparator() { Visible = false };

            _trayIcon.ContextMenuStrip.Items.Add(_stopTypingMenuItem);
            _trayIcon.ContextMenuStrip.Items.Add(_stopTypingSeparator);
            _trayIcon.ContextMenuStrip.Items.Add("Settings", null, OnSettings);
            _trayIcon.ContextMenuStrip.Items.Add("About", null, OnAbout);
            _trayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
            _trayIcon.ContextMenuStrip.Items.Add("Exit", null, OnExit);

            _trayIcon.ContextMenuStrip.Opening += OnContextMenuOpening;

            // Register the hotkey from settings (or default Ctrl+Shift+T)
            _hotkey = new GlobalHotkey(
                handle,
                HotkeyId,
                (GlobalHotkey.Modifiers)settings.HotkeyModifiers,
                (Keys)settings.HotkeyKey);

            // TD-03: Check if primary hotkey registration succeeded
            if (!_hotkey.IsRegistered)
            {
                string hotkeyName = SettingsForm.FormatHotkey((GlobalHotkey.Modifiers)settings.HotkeyModifiers, (Keys)settings.HotkeyKey);
                Logger.LogWarning($"Primary hotkey '{hotkeyName}' could not be registered on startup.");
                _trayIcon.ShowBalloonTip(
                    5000,
                    "ClipTyper - Hotkey Registration Failed",
                    $"The hotkey '{hotkeyName}' is already in use by another application.\n\nPlease open Settings and choose a different hotkey.",
                    ToolTipIcon.Warning);
            }

            // Register the overlay toggle hotkey if enabled
            if (settings.OverlayToggleEnabled)
            {
                _overlayToggleHotkey = new GlobalHotkey(
                    handle,
                    OverlayToggleHotkeyId,
                    (GlobalHotkey.Modifiers)settings.OverlayToggleModifiers,
                    (Keys)settings.OverlayToggleKey);

                if (!_overlayToggleHotkey.IsRegistered)
                {
                    string toggleName = SettingsForm.FormatHotkey((GlobalHotkey.Modifiers)settings.OverlayToggleModifiers, (Keys)settings.OverlayToggleKey);
                    Logger.LogWarning($"Overlay toggle hotkey '{toggleName}' could not be registered on startup.");
                }
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

            // TD-77: Warn if portable mode directory is read-only and falling back to LocalAppData
            if (SettingsManager.IsPortableFallbackToAppData)
            {
                Logger.LogWarning($"Running in portable mode, but directory '{SettingsManager.ExeDir}' is write-protected. Falling back to '{SettingsManager.SettingsDir}'.");
                _trayIcon.ShowBalloonTip(
                    6000,
                    "ClipTyper - Read-Only Directory",
                    "ClipTyper is running from a write-protected directory.\nSettings are being saved to LocalAppData instead.",
                    ToolTipIcon.Warning);
            }

            // Check for updates in background (24h throttled)
            RunStartupUpdateCheck();
        }

        private void OnContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // TD-70: Toggle visibility instead of rebuilding menu on each open
            bool isTyping = _typingService.IsTyping;
            _stopTypingMenuItem.Visible = isTyping;
            _stopTypingSeparator.Visible = isTyping;
        }

        // ── Automated Update Check & Badging ──────────────────────

        private void RunStartupUpdateCheck()
        {
            var settings = SettingsManager.Current;
            if (!settings.AutoUpdateCheckEnabled) return;

            if (settings.LastUpdateCheckUtc.HasValue &&
                (DateTime.UtcNow - settings.LastUpdateCheckUtc.Value).TotalHours < 24)
            {
                return;
            }

            System.Threading.Tasks.Task.Run(async () =>
            {
                var result = await UpdateChecker.CheckAsync();
                settings.LastUpdateCheckUtc = DateTime.UtcNow;
                SettingsManager.Save();

                if (result != null && result.IsUpdateAvailable)
                {
                    if (_hiddenForm.InvokeRequired)
                    {
                        _hiddenForm.BeginInvoke(new Action(() => NotifyUpdateAvailable(result)));
                    }
                    else
                    {
                        NotifyUpdateAvailable(result);
                    }
                }
            });
        }

        private void NotifyUpdateAvailable(UpdateChecker.UpdateCheckResult result)
        {
            _cachedUpdateResult = result;
            ApplyTrayIconBadge(true);
            _overlay?.SetUpdateBadge(true);

            _trayIcon.BalloonTipClicked -= OnBalloonTipClicked;
            _trayIcon.BalloonTipClicked += OnBalloonTipClicked;
            _trayIcon.ShowBalloonTip(
                5000,
                "ClipTyper Update Available",
                $"Version v{result.LatestVersion} is available. Click here for details.",
                ToolTipIcon.Info);
        }

        private void OnBalloonTipClicked(object? sender, EventArgs e)
        {
            OnAbout(sender, e);
        }

        private void ApplyTrayIconBadge(bool hasBadge)
        {
            if (!hasBadge)
            {
                _trayIcon.Icon = LoadEmbeddedIcon();
                if (_badgedTrayIcon != null)
                {
                    _badgedTrayIcon.Dispose();
                    _badgedTrayIcon = null;
                }
                return;
            }

            try
            {
                using var baseIcon = LoadEmbeddedIcon();
                using var bmp = baseIcon.ToBitmap();
                using var g = Graphics.FromImage(bmp);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                int size = Math.Max(8, bmp.Width / 4);
                int margin = 1;
                int x = bmp.Width - size - margin;
                int y = margin;

                using (var whiteBrush = new SolidBrush(Color.White))
                {
                    g.FillEllipse(whiteBrush, x - 1, y - 1, size + 2, size + 2);
                }
                using (var redBrush = new SolidBrush(Color.Red))
                {
                    g.FillEllipse(redBrush, x, y, size, size);
                }

                IntPtr hIcon = bmp.GetHicon();
                var newIcon = Icon.FromHandle(hIcon);
                _trayIcon.Icon = newIcon;
                _badgedTrayIcon?.Dispose();
                _badgedTrayIcon = newIcon;

                // Free native Win32 icon handle created by GetHicon()
                NativeMethods.DestroyIcon(hIcon);
            }
            catch
            {
                // Fall back to unbadged icon on GDI error
                _trayIcon.Icon = LoadEmbeddedIcon();
            }
        }

        // ── Floating Overlay Management ─────────────────────────────

        private void ShowOverlay()
        {
            if (_overlay != null) return;

            _overlay = new OverlayForm(
                triggerClipType: () =>
                {
                    _ = _typingService.TriggerClipTypeAsync();
                },
                onStopTyping: () =>
                {
                    _typingService.CancelTyping();
                },
                isTypingQuery: () => _typingService.IsTyping);

            _overlay.OverlayHidden += () =>
            {
                var settings = SettingsManager.Current;
                settings.OverlayEnabled = false;
                SettingsManager.Save();
                HideOverlay();
            };

            _overlay.ScaleChanged += (newScale) =>
            {
                // TD-71: OverlayForm already saved settings during ApplyScale/SavePosition; keep in-memory model in sync
                var settings = SettingsManager.Current;
                settings.OverlayScalePercent = newScale;
            };

            if (_cachedUpdateResult != null && _cachedUpdateResult.IsUpdateAvailable)
            {
                _overlay.SetUpdateBadge(true);
            }

            _overlay.Show();
        }

        private void HideOverlay()
        {
            if (_overlay == null) return;
            _overlay.Close();
            _overlay.Dispose();
            _overlay = null;
        }

        private void OnOverlayToggleHotkeyPressed()
        {
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

        // ── Hotkey Handler ──────────────────────────────────────────

        private void OnHotkeyPressed()
        {
            _ = _typingService.TriggerClipTypeAsync();
        }

        // ── Settings Dialog ─────────────────────────────────────────

        private void OnSettings(object? sender, EventArgs e)
        {
            int originalScale = SettingsManager.Current.OverlayScalePercent;

            using var form = new SettingsForm();

            Action<int> liveScaleHandler = (scale) =>
            {
                if (_overlay != null && SettingsManager.Current.OverlayEnabled)
                {
                    _overlay.ApplyScale(scale);
                }
            };
            form.LiveScaleChanged += liveScaleHandler;

            form.SettingsSaved += (updatedSettings, resetPosition) =>
            {
                var current = SettingsManager.Current;

                // Try to update the trigger hotkey
                var newMod = (GlobalHotkey.Modifiers)updatedSettings.HotkeyModifiers;
                var newKey = (Keys)updatedSettings.HotkeyKey;
                if (newMod != _hotkey.CurrentModifier || newKey != _hotkey.CurrentKey)
                {
                    if (!_hotkey.Reregister(newMod, newKey))
                    {
                        MessageBox.Show(
                            "Could not register the hotkey. It may be in use by another application.\n\n" +
                            "The previous hotkey has been restored.",
                            "Hotkey Registration Failed",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        // TD-40: Revert only the hotkey property so other settings are NOT lost
                        updatedSettings.HotkeyModifiers = (int)_hotkey.CurrentModifier;
                        updatedSettings.HotkeyKey = (int)_hotkey.CurrentKey;
                    }
                }

                // Try to update overlay toggle hotkey
                if (updatedSettings.OverlayToggleEnabled)
                {
                    var toggleMods = (GlobalHotkey.Modifiers)updatedSettings.OverlayToggleModifiers;
                    var toggleKey = (Keys)updatedSettings.OverlayToggleKey;

                    if (_overlayToggleHotkey == null)
                    {
                        _overlayToggleHotkey = new GlobalHotkey(_hiddenForm.Handle, OverlayToggleHotkeyId, toggleMods, toggleKey);
                        if (!_overlayToggleHotkey.IsRegistered)
                        {
                            MessageBox.Show(
                                "Could not register the overlay toggle hotkey. It may be in use by another application.",
                                "Hotkey Registration Failed",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);

                            _overlayToggleHotkey.Dispose();
                            _overlayToggleHotkey = null;
                            // TD-68: Revert settings on failed registration
                            updatedSettings.OverlayToggleModifiers = current.OverlayToggleModifiers;
                            updatedSettings.OverlayToggleKey = current.OverlayToggleKey;
                        }
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

                            // TD-68: Revert settings on failed registration
                            updatedSettings.OverlayToggleModifiers = (int)_overlayToggleHotkey.CurrentModifier;
                            updatedSettings.OverlayToggleKey = (int)_overlayToggleHotkey.CurrentKey;
                        }
                    }
                }
                else
                {
                    _overlayToggleHotkey?.Dispose();
                    _overlayToggleHotkey = null;
                }

                // Copy positions if not reset
                if (resetPosition)
                {
                    updatedSettings.OverlayX = -1;
                    updatedSettings.OverlayY = -1;
                }
                else
                {
                    updatedSettings.OverlayX = current.OverlayX;
                    updatedSettings.OverlayY = current.OverlayY;
                }

                // TD-39: Save all updated settings via single Replace method
                SettingsManager.Replace(updatedSettings);

                // Apply overlay changes
                if (updatedSettings.OverlayEnabled)
                {
                    if (_overlay != null)
                    {
                        _overlay.ApplyScale(updatedSettings.OverlayScalePercent);
                        _overlay.MoveToMonitor(updatedSettings.OverlayMonitorIndex);
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

                // Force topmost to be re-applied
                if (_overlay != null)
                {
                    _overlay.TopMost = false;
                    _overlay.TopMost = true;
                }

                // Winget/installed mode: update autostart
                if (!SettingsManager.IsPortable)
                {
                    InstallHelper.SetAutoStart(updatedSettings.AutoStartEnabled);
                }
            };

            var result = form.ShowDialog();

            form.LiveScaleChanged -= liveScaleHandler;

            // Revert live preview scale on Cancel
            if (result != DialogResult.OK && _overlay != null && SettingsManager.Current.OverlayEnabled)
            {
                _overlay.ApplyScale(originalScale);
            }

            // Force topmost to be re-applied after dialog is closed
            if (_overlay != null)
            {
                _overlay.TopMost = false;
                _overlay.TopMost = true;
            }
        }

        // ── About Dialog (TD-05, TD-06) ─────────────────────────────

        private void OnAbout(object? sender, EventArgs e)
        {
            string hotkeyText = SettingsForm.FormatHotkey(_hotkey.CurrentModifier, _hotkey.CurrentKey);
            bool overlayActive = _overlay != null && SettingsManager.Current.OverlayEnabled;

            using var aboutForm = new AboutForm(hotkeyText, overlayActive, _cachedUpdateResult);
            aboutForm.UpdateAvailable += NotifyUpdateAvailable;
            aboutForm.ShowDialog();
        }

        // ── Shutdown ────────────────────────────────────────────────

        private void OnExit(object? sender, EventArgs e)
        {
            _trayIcon.Visible = false;
            _badgedTrayIcon?.Dispose();
            _trayIcon.Dispose();
            _boldMenuFont?.Dispose();
            _overlay?.Close();
            _overlay?.Dispose();
            _hotkey.Dispose();
            _overlayToggleHotkey?.Dispose();
            _typingService.Dispose();
            _hiddenForm.Dispose();
            Application.Exit();
        }
    }
}
