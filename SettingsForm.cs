using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClipTyper
{
    /// <summary>
    /// Settings dialog with categorized groups: Hotkey recorder, keystroke delay & formatting,
    /// safety & target compatibility, credential auto-type, overlay configuration, and autostart.
    /// </summary>
    public class SettingsForm : Form
    {
        // ── P/Invoke for hotkey conflict probe ──────────────────────
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int ProbeHotkeyId = 9999;

        // ── Controls ────────────────────────────────────────────────
        // Hotkeys
        private TextBox _hotkeyBox = null!;
        private TextBox _toggleHotkeyBox = null!;

        // Typing & Speed
        private TrackBar _delaySlider = null!;
        private Label _delayLabel = null!;
        private ComboBox _newlineComboBox = null!;
        private CheckBox _enforcePlainTextCheckbox = null!;
        private CheckBox _jitterCheckbox = null!;

        // Safety & Compatibility
        private CheckBox _sanitizeInputCheckbox = null!;
        private CheckBox _maxLenEnableCheckbox = null!;
        private NumericUpDown _maxLenInput = null!;
        private CheckBox _vkModeCheckbox = null!;
        private CheckBox _soundFeedbackCheckbox = null!;

        // Credential Auto-Type (v1.6.0)
        private ComboBox _credModeComboBox = null!;
        private TextBox _customDelimiterBox = null!;
        private NumericUpDown _stageDelayInput = null!;
        private CheckBox _autoClearCheckbox = null!;
        private NumericUpDown _autoClearDelayInput = null!;
        private Label _autoClearWarningLabel = null!;

        // Overlay
        private CheckBox _overlayCheckbox = null!;
        private TrackBar _scaleSlider = null!;
        private Label _scaleLabel = null!;
        private ComboBox _monitorComboBox = null!;
        private Button _resetPositionBtn = null!;

        // System / Startup
        private CheckBox? _autostartCheckbox;
        private CheckBox _autoUpdateCheckbox = null!;

        // Actions
        private Button _saveBtn = null!;
        private Button _cancelBtn = null!;

        // ── Hotkey recorder state ───────────────────────────────────
        private Keys _recordedKey = Keys.None;
        private GlobalHotkey.Modifiers _recordedModifiers = GlobalHotkey.Modifiers.None;
        private Keys _recordedToggleKey = Keys.None;
        private GlobalHotkey.Modifiers _recordedToggleModifiers = GlobalHotkey.Modifiers.None;

        private enum RecordingTarget
        {
            None,
            TriggerHotkey,
            ToggleHotkey
        }
        private RecordingTarget _recordingTarget = RecordingTarget.None;

        private static readonly (GlobalHotkey.Modifiers mod, Keys key)[] BlockedCombos =
        {
            (GlobalHotkey.Modifiers.Control, Keys.C),
            (GlobalHotkey.Modifiers.Control, Keys.V),
            (GlobalHotkey.Modifiers.Control, Keys.X),
            (GlobalHotkey.Modifiers.Control, Keys.Z),
            (GlobalHotkey.Modifiers.Control, Keys.A),
            (GlobalHotkey.Modifiers.Alt, Keys.F4),
            (GlobalHotkey.Modifiers.Alt, Keys.Tab),
        };

        /// <summary>
        /// Raised when the user saves settings. Passes modified settings instance and resetPosition flag.
        /// </summary>
        public event Action<AppSettings, GlobalHotkey.Modifiers, Keys, GlobalHotkey.Modifiers, Keys, bool, bool>? SettingsSaved;

        /// <summary>
        /// Raised in real-time when the user moves the scale slider.
        /// </summary>
        public event Action<int>? LiveScaleChanged;

        public SettingsForm()
        {
            InitializeUI();
            LoadCurrentSettings();
        }

        private void InitializeUI()
        {
            Text = "Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            KeyPreview = true;
            AutoScroll = true;

            int y = 12;
            int groupWidth = 400;

            // ── 1. Hotkeys Group ────────────────────────────────────
            var hotkeyGroup = new GroupBox
            {
                Text = "Hotkeys",
                Location = new Point(12, y),
                Size = new Size(groupWidth, 110)
            };

            var hotkeyLabel = new Label { Text = "Type Clipboard:", Location = new Point(12, 25), AutoSize = true };
            _hotkeyBox = new TextBox { Location = new Point(170, 22), Size = new Size(210, 23), ReadOnly = true, BackColor = SystemColors.Window };
            _hotkeyBox.GotFocus += (_, _) => StartRecording(RecordingTarget.TriggerHotkey, _hotkeyBox);

            var toggleHotkeyLabel = new Label { Text = "Toggle Overlay:", Location = new Point(12, 55), AutoSize = true };
            _toggleHotkeyBox = new TextBox { Location = new Point(170, 52), Size = new Size(210, 23), ReadOnly = true, BackColor = SystemColors.Window };
            _toggleHotkeyBox.GotFocus += (_, _) => StartRecording(RecordingTarget.ToggleHotkey, _toggleHotkeyBox);

            var hotkeyHint = new Label { Text = "Click a box and press keys to record.", Location = new Point(12, 83), ForeColor = Color.Gray, AutoSize = true };

            hotkeyGroup.Controls.AddRange(new Control[] { hotkeyLabel, _hotkeyBox, toggleHotkeyLabel, _toggleHotkeyBox, hotkeyHint });
            Controls.Add(hotkeyGroup);
            y += 120;

            // ── 2. Typing & Formatting Group ────────────────────────
            var typingGroup = new GroupBox
            {
                Text = "Typing & Formatting",
                Location = new Point(12, y),
                Size = new Size(groupWidth, 160)
            };

            var delayCaption = new Label { Text = "Keystroke Delay:", Location = new Point(12, 25), AutoSize = true };
            _delaySlider = new TrackBar { Location = new Point(130, 18), Size = new Size(190, 45), Minimum = 5, Maximum = 100, TickFrequency = 5, SmallChange = 5, LargeChange = 10 };
            _delaySlider.ValueChanged += (_, _) => { _delayLabel.Text = $"{_delaySlider.Value} ms"; };
            _delayLabel = new Label { Text = "25 ms", Location = new Point(330, 25), AutoSize = true };

            var newlineLabel = new Label { Text = "Newline Handling:", Location = new Point(12, 65), AutoSize = true };
            _newlineComboBox = new ComboBox { Location = new Point(135, 62), Size = new Size(245, 23), DropDownStyle = ComboBoxStyle.DropDownList };
            _newlineComboBox.Items.AddRange(new object[]
            {
                "Enter (Default)",
                "Shift + Enter (Teams / Chats)",
                "Space (Replace newlines)",
                "Ignore (Strip newlines)"
            });

            _enforcePlainTextCheckbox = new CheckBox
            {
                Text = "Enforce Plain-Text (strip RTF & HTML formatting)",
                Location = new Point(12, 98),
                Size = new Size(370, 20)
            };

            _jitterCheckbox = new CheckBox
            {
                Text = "Humanized typing jitter (±5 ms random variance)",
                Location = new Point(12, 126),
                Size = new Size(370, 20)
            };

            typingGroup.Controls.AddRange(new Control[] { delayCaption, _delaySlider, _delayLabel, newlineLabel, _newlineComboBox, _enforcePlainTextCheckbox, _jitterCheckbox });
            Controls.Add(typingGroup);
            y += 170;

            // ── 3. Credential Auto-Type Group ───────────────────────
            var credGroup = new GroupBox
            {
                Text = "Credential Auto-Type (Two-Stage Login)",
                Location = new Point(12, y),
                Size = new Size(groupWidth, 195)
            };

            var credModeLabel = new Label { Text = "Mode:", Location = new Point(12, 25), AutoSize = true };
            _credModeComboBox = new ComboBox { Location = new Point(135, 22), Size = new Size(245, 23), DropDownStyle = ComboBoxStyle.DropDownList };
            _credModeComboBox.Items.AddRange(new object[]
            {
                "Disabled (Normal typing)",
                "Auto-Detect (Tab or Newline delimiter)",
                "Tab-Separated only (User<Tab>Pass)",
                "Newline-Separated only (User<Enter>Pass)",
                "Custom Delimiter"
            });

            var customDelimLabel = new Label { Text = "Custom Delimiter:", Location = new Point(12, 58), AutoSize = true };
            _customDelimiterBox = new TextBox { Location = new Point(135, 55), Size = new Size(120, 23), Enabled = false };

            var stageDelayLabel = new Label { Text = "Stage Pause:", Location = new Point(12, 90), AutoSize = true };
            _stageDelayInput = new NumericUpDown { Location = new Point(135, 88), Size = new Size(70, 23), Minimum = 50, Maximum = 2000, Value = 200, Increment = 50 };
            var msLabel = new Label { Text = "ms between user & pass", Location = new Point(210, 90), AutoSize = true };

            _autoClearCheckbox = new CheckBox
            {
                Text = "Auto-clear clipboard after typing in",
                Location = new Point(12, 122),
                AutoSize = true
            };
            _autoClearDelayInput = new NumericUpDown { Location = new Point(235, 120), Size = new Size(50, 23), Minimum = 1, Maximum = 60, Value = 5, Enabled = false };
            var secLabel = new Label { Text = "seconds", Location = new Point(290, 122), AutoSize = true };

            _autoClearWarningLabel = new Label
            {
                Text = "⚠ Erases credentials from clipboard after delay.",
                Location = new Point(12, 152),
                ForeColor = Color.DarkOrange,
                AutoSize = true,
                Visible = false
            };

            _credModeComboBox.SelectedIndexChanged += (_, _) =>
            {
                _customDelimiterBox.Enabled = (_credModeComboBox.SelectedIndex == 4);
            };

            _autoClearCheckbox.CheckedChanged += (_, _) =>
            {
                _autoClearDelayInput.Enabled = _autoClearCheckbox.Checked;
                _autoClearWarningLabel.Visible = _autoClearCheckbox.Checked;
            };

            credGroup.Controls.AddRange(new Control[]
            {
                credModeLabel, _credModeComboBox,
                customDelimLabel, _customDelimiterBox,
                stageDelayLabel, _stageDelayInput, msLabel,
                _autoClearCheckbox, _autoClearDelayInput, secLabel,
                _autoClearWarningLabel
            });
            Controls.Add(credGroup);
            y += 205;

            // ── 4. Safety & Compatibility Group ─────────────────────
            var safetyGroup = new GroupBox
            {
                Text = "Safety & Target Compatibility",
                Location = new Point(12, y),
                Size = new Size(groupWidth, 140)
            };

            _sanitizeInputCheckbox = new CheckBox
            {
                Text = "Sanitize text (remove BOM, null-bytes & invisible chars)",
                Location = new Point(12, 22),
                Size = new Size(375, 20),
                Checked = true
            };

            _maxLenEnableCheckbox = new CheckBox
            {
                Text = "Confirm before typing text >",
                Location = new Point(12, 48),
                AutoSize = true,
                Checked = true
            };

            _maxLenInput = new NumericUpDown { Location = new Point(210, 46), Size = new Size(75, 23), Minimum = 100, Maximum = 500000, Value = 5000, Increment = 500 };
            var maxLenCharsLabel = new Label { Text = "chars", Location = new Point(290, 48), AutoSize = true };

            _maxLenEnableCheckbox.CheckedChanged += (_, _) =>
            {
                _maxLenInput.Enabled = _maxLenEnableCheckbox.Checked;
            };

            _vkModeCheckbox = new CheckBox
            {
                Text = "VK Compatibility Mode (for Teams screen control / RDP)",
                Location = new Point(12, 78),
                Size = new Size(375, 20)
            };

            _soundFeedbackCheckbox = new CheckBox
            {
                Text = "Play sound signal when typing completes",
                Location = new Point(12, 108),
                Size = new Size(375, 20)
            };

            safetyGroup.Controls.AddRange(new Control[]
            {
                _sanitizeInputCheckbox,
                _maxLenEnableCheckbox, _maxLenInput, maxLenCharsLabel,
                _vkModeCheckbox,
                _soundFeedbackCheckbox
            });
            Controls.Add(safetyGroup);
            y += 150;

            // ── 5. Overlay Group ────────────────────────────────────
            var overlayGroup = new GroupBox
            {
                Text = "Overlay",
                Location = new Point(12, y),
                Size = new Size(groupWidth, 160)
            };

            _overlayCheckbox = new CheckBox { Text = "Show Overlay Button", Location = new Point(12, 25), AutoSize = true };
            var scaleLabelCaption = new Label { Text = "Scale:", Location = new Point(12, 58), AutoSize = true };
            _scaleSlider = new TrackBar { Location = new Point(80, 52), Size = new Size(230, 45), Minimum = 25, Maximum = 200, TickFrequency = 25, SmallChange = 5, LargeChange = 25 };
            _scaleSlider.ValueChanged += (_, _) =>
            {
                _scaleLabel.Text = $"{_scaleSlider.Value}%";
                LiveScaleChanged?.Invoke(_scaleSlider.Value);
            };
            _scaleLabel = new Label { Text = "100%", Location = new Point(320, 58), AutoSize = true };

            var monitorLabelCaption = new Label { Text = "Monitor:", Location = new Point(12, 98), AutoSize = true };
            _monitorComboBox = new ComboBox { Location = new Point(80, 95), Size = new Size(230, 23), DropDownStyle = ComboBoxStyle.DropDownList };
            for (int i = 0; i < Screen.AllScreens.Length; i++)
            {
                var screen = Screen.AllScreens[i];
                string name = $"Monitor {i + 1}" + (screen.Primary ? " (Primary)" : "");
                _monitorComboBox.Items.Add(name);
            }

            _resetPositionBtn = new Button { Text = "Reset Position", Location = new Point(12, 128), Size = new Size(110, 24) };

            overlayGroup.Controls.AddRange(new Control[]
            {
                _overlayCheckbox, scaleLabelCaption, _scaleSlider, _scaleLabel,
                monitorLabelCaption, _monitorComboBox, _resetPositionBtn
            });
            Controls.Add(overlayGroup);
            y += 170;

            // ── 6. Startup Group ────────────────────────────────────
            if (!SettingsManager.IsPortable)
            {
                var autostartGroup = new GroupBox
                {
                    Text = "Startup",
                    Location = new Point(12, y),
                    Size = new Size(groupWidth, 50)
                };

                _autostartCheckbox = new CheckBox
                {
                    Text = "Run ClipTyper at Windows startup",
                    Location = new Point(12, 20),
                    AutoSize = true
                };

                autostartGroup.Controls.Add(_autostartCheckbox);
                Controls.Add(autostartGroup);
                y += 60;
            }

            // ── 7. Updates Group ────────────────────────────────────
            var updatesGroup = new GroupBox
            {
                Text = "Updates",
                Location = new Point(12, y),
                Size = new Size(groupWidth, 50)
            };

            _autoUpdateCheckbox = new CheckBox
            {
                Text = "Automatically check for updates",
                Location = new Point(12, 20),
                AutoSize = true
            };

            updatesGroup.Controls.Add(_autoUpdateCheckbox);
            Controls.Add(updatesGroup);
            y += 60;

            // ── Buttons ─────────────────────────────────────────────
            _saveBtn = new Button { Text = "Save", Location = new Point(230, y), Size = new Size(85, 28), DialogResult = DialogResult.OK };
            _saveBtn.Click += OnSave;

            _cancelBtn = new Button { Text = "Cancel", Location = new Point(325, y), Size = new Size(85, 28), DialogResult = DialogResult.Cancel };

            Controls.AddRange(new Control[] { _saveBtn, _cancelBtn });
            AcceptButton = _saveBtn;
            CancelButton = _cancelBtn;

            ClientSize = new Size(424, y + 45);
        }

        private void StartRecording(RecordingTarget target, TextBox box)
        {
            _recordingTarget = target;
            box.BackColor = Color.LightYellow;
        }

        private void LoadCurrentSettings()
        {
            var s = SettingsManager.Current;

            // Trigger hotkey
            _recordedModifiers = (GlobalHotkey.Modifiers)s.HotkeyModifiers;
            _recordedKey = (Keys)s.HotkeyKey;
            _hotkeyBox.Text = FormatHotkey(_recordedModifiers, _recordedKey);

            // Toggle hotkey
            _recordedToggleModifiers = (GlobalHotkey.Modifiers)s.OverlayToggleModifiers;
            _recordedToggleKey = (Keys)s.OverlayToggleKey;
            _toggleHotkeyBox.Text = FormatHotkey(_recordedToggleModifiers, _recordedToggleKey);

            // Keystroke delay & formatting
            _delaySlider.Value = Math.Clamp(s.KeystrokeDelayMs, 5, 100);
            _delayLabel.Text = $"{_delaySlider.Value} ms";
            _newlineComboBox.SelectedIndex = Math.Clamp((int)s.NewlineHandling, 0, 3);
            _enforcePlainTextCheckbox.Checked = s.EnforcePlainText;
            _jitterCheckbox.Checked = s.EnableTypingJitter;

            // Credential Auto-Type
            _credModeComboBox.SelectedIndex = Math.Clamp((int)s.CredentialAutoTypeMode, 0, 4);
            _customDelimiterBox.Text = s.CredentialCustomDelimiter;
            _customDelimiterBox.Enabled = (s.CredentialAutoTypeMode == CredentialMode.Custom);
            _stageDelayInput.Value = Math.Clamp(s.CredentialStageDelayMs, 50, 2000);
            _autoClearCheckbox.Checked = s.CredentialAutoClearClipboard;
            _autoClearDelayInput.Value = Math.Clamp(s.CredentialAutoClearDelaySeconds, 1, 60);
            _autoClearDelayInput.Enabled = s.CredentialAutoClearClipboard;
            _autoClearWarningLabel.Visible = s.CredentialAutoClearClipboard;

            // Safety & Compatibility
            _sanitizeInputCheckbox.Checked = s.SanitizeInput;
            if (s.MaxTextLengthThreshold > 0)
            {
                _maxLenEnableCheckbox.Checked = true;
                _maxLenInput.Enabled = true;
                _maxLenInput.Value = Math.Clamp(s.MaxTextLengthThreshold, 100, 500000);
            }
            else
            {
                _maxLenEnableCheckbox.Checked = false;
                _maxLenInput.Enabled = false;
                _maxLenInput.Value = 5000;
            }

            _vkModeCheckbox.Checked = s.EnableVkCompatibilityMode;
            _soundFeedbackCheckbox.Checked = s.SoundFeedbackEnabled;

            // Overlay Checkbox & Scale
            _overlayCheckbox.Checked = s.OverlayEnabled;
            _scaleSlider.Value = Math.Clamp(s.OverlayScalePercent, 25, 200);
            _scaleLabel.Text = $"{_scaleSlider.Value}%";

            // Monitor selection
            int monitorIndex = s.OverlayMonitorIndex;
            if (monitorIndex >= 0 && monitorIndex < _monitorComboBox.Items.Count)
            {
                _monitorComboBox.SelectedIndex = monitorIndex;
            }
            else if (_monitorComboBox.Items.Count > 0)
            {
                _monitorComboBox.SelectedIndex = 0;
            }

            if (_autostartCheckbox != null)
            {
                _autostartCheckbox.Checked = InstallHelper.IsAutoStartEnabled();
            }

            _autoUpdateCheckbox.Checked = s.AutoUpdateCheckEnabled;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_recordingTarget == RecordingTarget.None)
            {
                return base.ProcessCmdKey(ref msg, keyData);
            }

            Keys baseKey = keyData & Keys.KeyCode;

            if (baseKey == Keys.ControlKey || baseKey == Keys.ShiftKey ||
                baseKey == Keys.Menu || baseKey == Keys.LWin ||
                baseKey == Keys.RWin)
            {
                return true;
            }

            var mods = GlobalHotkey.Modifiers.None;
            if ((keyData & Keys.Control) != 0) mods |= GlobalHotkey.Modifiers.Control;
            if ((keyData & Keys.Shift) != 0)   mods |= GlobalHotkey.Modifiers.Shift;
            if ((keyData & Keys.Alt) != 0)     mods |= GlobalHotkey.Modifiers.Alt;

            TextBox targetBox = _recordingTarget == RecordingTarget.TriggerHotkey ? _hotkeyBox : _toggleHotkeyBox;

            if (mods == GlobalHotkey.Modifiers.None)
            {
                targetBox.Text = "Need modifier (Ctrl/Shift/Alt)";
                return true;
            }

            foreach (var blocked in BlockedCombos)
            {
                if (mods == blocked.mod && baseKey == blocked.key)
                {
                    targetBox.Text = $"{FormatHotkey(mods, baseKey)} (blocked!)";
                    targetBox.BackColor = Color.FromArgb(255, 200, 200);
                    return true;
                }
            }

            bool isFree = ProbeHotkey(mods, baseKey, _recordingTarget);

            if (_recordingTarget == RecordingTarget.TriggerHotkey)
            {
                _recordedModifiers = mods;
                _recordedKey = baseKey;
            }
            else
            {
                _recordedToggleModifiers = mods;
                _recordedToggleKey = baseKey;
            }

            targetBox.Text = FormatHotkey(mods, baseKey);

            if (isFree)
            {
                targetBox.BackColor = Color.LightGreen;
            }
            else
            {
                targetBox.Text += " (in use!)";
                targetBox.BackColor = Color.FromArgb(255, 220, 150);
            }

            _recordingTarget = RecordingTarget.None;
            _delaySlider.Focus();
            return true;
        }

        private bool ProbeHotkey(GlobalHotkey.Modifiers mods, Keys key, RecordingTarget target)
        {
            if (target == RecordingTarget.TriggerHotkey)
            {
                if (mods == _recordedToggleModifiers && key == _recordedToggleKey)
                {
                    return false;
                }

                var currentSettings = SettingsManager.Current;
                if ((int)mods == currentSettings.HotkeyModifiers && (int)key == currentSettings.HotkeyKey)
                {
                    return true;
                }
            }
            else if (target == RecordingTarget.ToggleHotkey)
            {
                if (mods == _recordedModifiers && key == _recordedKey)
                {
                    return false;
                }

                var currentSettings = SettingsManager.Current;
                if ((int)mods == currentSettings.OverlayToggleModifiers && (int)key == currentSettings.OverlayToggleKey)
                {
                    return true;
                }
            }

            bool registered = RegisterHotKey(Handle, ProbeHotkeyId, (uint)mods, (uint)key);
            if (registered)
            {
                UnregisterHotKey(Handle, ProbeHotkeyId);
            }
            return registered;
        }

        private void OnSave(object? sender, EventArgs e)
        {
            bool resetPosition = false;
            if (_resetPositionBtn.Tag is bool reset && reset)
            {
                resetPosition = true;
            }

            var s = new AppSettings
            {
                HotkeyModifiers = (int)_recordedModifiers,
                HotkeyKey = (int)_recordedKey,
                OverlayToggleModifiers = (int)_recordedToggleModifiers,
                OverlayToggleKey = (int)_recordedToggleKey,
                OverlayToggleEnabled = true,
                KeystrokeDelayMs = _delaySlider.Value,
                NewlineHandling = (NewlineMode)_newlineComboBox.SelectedIndex,
                EnforcePlainText = _enforcePlainTextCheckbox.Checked,
                EnableTypingJitter = _jitterCheckbox.Checked,
                TypingJitterRangeMs = 5,
                CredentialAutoTypeMode = (CredentialMode)_credModeComboBox.SelectedIndex,
                CredentialCustomDelimiter = _customDelimiterBox.Text,
                CredentialStageDelayMs = (int)_stageDelayInput.Value,
                CredentialAutoClearClipboard = _autoClearCheckbox.Checked,
                CredentialAutoClearDelaySeconds = (int)_autoClearDelayInput.Value,
                SanitizeInput = _sanitizeInputCheckbox.Checked,
                MaxTextLengthThreshold = _maxLenEnableCheckbox.Checked ? (int)_maxLenInput.Value : 0,
                EnableVkCompatibilityMode = _vkModeCheckbox.Checked,
                SoundFeedbackEnabled = _soundFeedbackCheckbox.Checked,
                OverlayEnabled = _overlayCheckbox.Checked,
                OverlayScalePercent = _scaleSlider.Value,
                OverlayMonitorIndex = _monitorComboBox.SelectedIndex >= 0 ? _monitorComboBox.SelectedIndex : 0,
                AutoStartEnabled = _autostartCheckbox?.Checked ?? SettingsManager.Current.AutoStartEnabled,
                AutoUpdateCheckEnabled = _autoUpdateCheckbox.Checked,
                LastUpdateCheckUtc = SettingsManager.Current.LastUpdateCheckUtc
            };

            SettingsSaved?.Invoke(
                s,
                _recordedModifiers,
                _recordedKey,
                _recordedToggleModifiers,
                _recordedToggleKey,
                true,
                resetPosition
            );
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _resetPositionBtn.Click += (_, _) =>
            {
                _resetPositionBtn.Tag = true;
                _resetPositionBtn.Text = "✓ Will Reset";
                _resetPositionBtn.Enabled = false;
            };
        }

        public static string FormatHotkey(GlobalHotkey.Modifiers mods, Keys key)
        {
            var parts = new System.Collections.Generic.List<string>();

            if (mods.HasFlag(GlobalHotkey.Modifiers.Control)) parts.Add("Ctrl");
            if (mods.HasFlag(GlobalHotkey.Modifiers.Alt))     parts.Add("Alt");
            if (mods.HasFlag(GlobalHotkey.Modifiers.Shift))   parts.Add("Shift");
            if (mods.HasFlag(GlobalHotkey.Modifiers.Win))     parts.Add("Win");

            if (key != Keys.None)
            {
                parts.Add(key.ToString());
            }

            return parts.Count > 0 ? string.Join(" + ", parts) : "None";
        }
    }
}
