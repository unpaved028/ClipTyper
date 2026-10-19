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
        private SettingsForm? _openSettingsForm;
        private AboutForm? _openAboutForm;
        private Action? _balloonClickAction;
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
        /// Loads the embedded app icon in the small-icon size of the current DPI (16 px at 100 %,
        /// 24 px at 150 %). Falls back to the default application icon if not found.
        /// </summary>
        private static Icon LoadEmbeddedIcon()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("ClipTyper.icon_app.ico");
                if (stream != null)
                {
                    return new Icon(stream, SystemInformation.SmallIconSize);
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

            // Load persisted settings and apply the appearance before any window is created
            SettingsManager.Load();
            var settings = SettingsManager.Current;
            Theme.Apply(settings.Theme);

            _hiddenForm = new HotkeyForm();
            _hiddenForm.HotkeyPressed += OnHotkeyPressed;
            _hiddenForm.OverlayToggleHotkeyPressed += OnOverlayToggleHotkeyPressed;
            _hiddenForm.CloseRequested += () => OnExit(this, EventArgs.Empty);

            var handle = _hiddenForm.Handle;

            _trayIcon = new NotifyIcon()
            {
                Icon = LoadEmbeddedIcon(),
                ContextMenuStrip = new ContextMenuStrip { Renderer = ModernMenuRenderer.Instance },
                Visible = true,
                Text = "ClipTyper"
            };
            _trayIcon.MouseDoubleClick += (_, e) =>
            {
                if (e.Button == MouseButtons.Left) OnSettings(this, EventArgs.Empty);
            };
            _trayIcon.BalloonTipClicked += (_, _) =>
            {
                var action = _balloonClickAction;
                _balloonClickAction = null;
                action?.Invoke();
            };

            _typingService = new TypingService(
                SynchronizationContext.Current,
                userPrompt: new WindowsUserPrompt(notify: (title, message) =>
                    ShowBalloon(6000, title, message, ToolTipIcon.Warning)));
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
                ShowBalloon(3000, "ClipTyper", "The clipboard contains no text to type.", ToolTipIcon.Info);
            };

            // TD-70: Build tray menu once, cache bold font, avoid allocation leaks on menu open
            _boldMenuFont = new Font(_trayIcon.ContextMenuStrip.Font, FontStyle.Bold);
            _stopTypingMenuItem = new ToolStripMenuItem("⏹ Stop Typing", null, (_, _) => _typingService.CancelTyping())
            {
                Font = _boldMenuFont,
                Tag = ModernMenuRenderer.DangerTag,
                Visible = false
            };
            _stopTypingSeparator = new ToolStripSeparator() { Visible = false };

            _trayIcon.ContextMenuStrip.Items.Add(_stopTypingMenuItem);
            _trayIcon.ContextMenuStrip.Items.Add(_stopTypingSeparator);
            TypingModeMenu.AddTo(_trayIcon.ContextMenuStrip);
            _trayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
            _trayIcon.ContextMenuStrip.Items.Add("Settings", null, OnSettings);
            _trayIcon.ContextMenuStrip.Items.Add("About", null, OnAbout);
            _trayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
            _trayIcon.ContextMenuStrip.Items.Add("Exit", null, OnExit);

            _trayIcon.ContextMenuStrip.Opening += OnContextMenuOpening;

            // Register the hotkey from settings (or default Ctrl+Alt+V)
            _hotkey = new GlobalHotkey(
                handle,
                HotkeyId,
                (GlobalHotkey.Modifiers)settings.HotkeyModifiers,
                (Keys)settings.HotkeyKey);

            // TD-03: Check if primary hotkey registration succeeded
            bool startupWarningShown = false;
            if (!_hotkey.IsRegistered)
            {
                string hotkeyName = SettingsForm.FormatHotkey((GlobalHotkey.Modifiers)settings.HotkeyModifiers, (Keys)settings.HotkeyKey);
                Logger.LogWarning($"Primary hotkey '{hotkeyName}' could not be registered on startup.");
                ShowBalloon(
                    5000,
                    "ClipTyper - Hotkey Registration Failed",
                    $"The hotkey '{hotkeyName}' is already in use by another application.\n\nPlease open Settings and choose a different hotkey.",
                    ToolTipIcon.Warning,
                    () => OnSettings(this, EventArgs.Empty));
                startupWarningShown = true;
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
                ShowBalloon(
                    6000,
                    "ClipTyper - Read-Only Directory",
                    "ClipTyper is running from a write-protected directory.\nSettings are being saved to LocalAppData instead.",
                    ToolTipIcon.Warning);
                startupWarningShown = true;
            }

            ShowStartupNotice(startupWarningShown);

            // Check for updates in background (24h throttled)
            RunStartupUpdateCheck();
        }

        /// <summary>
        /// Tray notification; <paramref name="onClick"/> runs when the user clicks it.
        /// </summary>
        private void ShowBalloon(int timeoutMs, string title, string text, ToolTipIcon icon, Action? onClick = null)
        {
            _balloonClickAction = onClick;
            _trayIcon.ShowBalloonTip(timeoutMs, title, text, icon);
        }

        /// <summary>
        /// Welcome after the first start, "what's new" after updating to a new minor version.
        /// Skipped when a startup warning (hotkey taken, read-only folder) is already showing.
        /// </summary>
        private void ShowStartupNotice(bool startupWarningShown)
        {
            var settings = SettingsManager.Current;
            string currentVersion = UpdateChecker.GetCurrentVersion();
            var notice = StartupNotices.Decide(SettingsManager.IsFirstRun, settings.LastRunVersion, currentVersion);

            if (settings.LastRunVersion != currentVersion)
            {
                settings.LastRunVersion = currentVersion;
                SettingsManager.Save();
            }

            if (startupWarningShown)
            {
                return;
            }

            if (notice == StartupNotice.Welcome)
            {
                string hotkey = SettingsForm.FormatHotkey(_hotkey.CurrentModifier, _hotkey.CurrentKey);
                ShowBalloon(
                    8000,
                    "ClipTyper is running",
                    $"Press {hotkey} to type the clipboard into the active window. Right-click this icon for settings.",
                    ToolTipIcon.Info,
                    () => OnSettings(this, EventArgs.Empty));
            }
            else if (notice == StartupNotice.WhatsNew)
            {
                ShowBalloon(
                    8000,
                    $"ClipTyper updated to v{currentVersion}",
                    StartupNotices.WhatsNewText,
                    ToolTipIcon.Info,
                    () => OpenUrl($"https://github.com/unpaved028/ClipTyper/releases/tag/v{currentVersion}"));
            }
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Could not open {url}: {ex.Message}");
            }
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
                if (result == null)
                {
                    return; // offline or GitHub unreachable: try again on next start
                }

                settings.LastUpdateCheckUtc = DateTime.UtcNow;
                SettingsManager.Save();

                if (result.IsUpdateAvailable)
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

            ShowBalloon(
                5000,
                "ClipTyper Update Available",
                $"Version v{result.LatestVersion} is available. Click here for details.",
                ToolTipIcon.Info,
                () => OnAbout(this, EventArgs.Empty));
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

                // Copy the icon before freeing the GetHicon() handle: NotifyIcon re-sends its icon
                // handle on every tooltip change, so it must stay valid while it is displayed.
                IntPtr hIcon = bmp.GetHicon();
                Icon newIcon;
                using (var temporary = Icon.FromHandle(hIcon))
                {
                    newIcon = (Icon)temporary.Clone();
                }
                NativeMethods.DestroyIcon(hIcon);

                _trayIcon.Icon = newIcon;
                _badgedTrayIcon?.Dispose();
                _badgedTrayIcon = newIcon;
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
            // Dispose only: Close() sends WM_CLOSE, which OverlayForm treats as an
            // external shutdown request and answers with Application.Exit().
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
            if (_openSettingsForm != null)
            {
                _openSettingsForm.Activate();
                return;
            }

            int originalScale = SettingsManager.Current.OverlayScalePercent;

            using var form = new SettingsForm();
            _openSettingsForm = form;

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
                if (!_hotkey.IsRegistered || newMod != _hotkey.CurrentModifier || newKey != _hotkey.CurrentKey)
                {
                    if (!_hotkey.Reregister(newMod, newKey))
                    {
                        Dialogs.Show(
                            _openSettingsForm,
                            TaskDialogIcon.Warning,
                            "The hotkey could not be registered",
                            $"{SettingsForm.FormatHotkey(newMod, newKey)} is probably used by another application." +
                            (_hotkey.IsRegistered ? " The previous hotkey is still active." : ""));
                        // TD-40: Revert only the hotkey property so other settings are NOT lost.
                        // Use the saved setting, not _hotkey.Current*: those are None when the
                        // hotkey could not be registered at startup.
                        updatedSettings.HotkeyModifiers = current.HotkeyModifiers;
                        updatedSettings.HotkeyKey = current.HotkeyKey;
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
                            Dialogs.Show(
                                _openSettingsForm,
                                TaskDialogIcon.Warning,
                                "The overlay shortcut could not be registered",
                                $"{SettingsForm.FormatHotkey(toggleMods, toggleKey)} is probably used by another application.");

                            _overlayToggleHotkey.Dispose();
                            _overlayToggleHotkey = null;
                            // TD-68: Revert settings on failed registration
                            updatedSettings.OverlayToggleModifiers = current.OverlayToggleModifiers;
                            updatedSettings.OverlayToggleKey = current.OverlayToggleKey;
                        }
                    }
                    else if (!_overlayToggleHotkey.IsRegistered || toggleMods != _overlayToggleHotkey.CurrentModifier || toggleKey != _overlayToggleHotkey.CurrentKey)
                    {
                        if (!_overlayToggleHotkey.Reregister(toggleMods, toggleKey))
                        {
                            Dialogs.Show(
                                _openSettingsForm,
                                TaskDialogIcon.Warning,
                                "The overlay shortcut could not be registered",
                                $"{SettingsForm.FormatHotkey(toggleMods, toggleKey)} is probably used by another application." +
                                (_overlayToggleHotkey.IsRegistered ? " The previous shortcut is still active." : ""));

                            // TD-68: Revert settings on failed registration
                            updatedSettings.OverlayToggleModifiers = current.OverlayToggleModifiers;
                            updatedSettings.OverlayToggleKey = current.OverlayToggleKey;
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
                    updatedSettings.OverlayPositionDpiAware = current.OverlayPositionDpiAware;
                }

                bool themeChanged = updatedSettings.Theme != current.Theme;

                // TD-39: Save all updated settings via single Replace method
                SettingsManager.Replace(updatedSettings);

                if (themeChanged)
                {
                    Theme.Apply(updatedSettings.Theme);
                }

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

            DialogResult result;
            try
            {
                result = form.ShowDialog();
            }
            finally
            {
                _openSettingsForm = null;
            }

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

            if (_openAboutForm != null)
            {
                _openAboutForm.Activate();
                return;
            }

            using var aboutForm = new AboutForm(hotkeyText, overlayActive, _cachedUpdateResult);
            aboutForm.UpdateAvailable += NotifyUpdateAvailable;
            _openAboutForm = aboutForm;
            try
            {
                aboutForm.ShowDialog();
            }
            finally
            {
                _openAboutForm = null;
            }
        }

        // ── Shutdown ────────────────────────────────────────────────

        private void OnExit(object? sender, EventArgs e)
        {
            _trayIcon.Visible = false;
            _badgedTrayIcon?.Dispose();
            _trayIcon.Dispose();
            _boldMenuFont?.Dispose();
            _overlay?.Dispose();
            _overlay = null;
            _hotkey.Dispose();
            _overlayToggleHotkey?.Dispose();
            _typingService.Dispose();
            _hiddenForm.Dispose();
            Application.Exit();
        }
    }
}
