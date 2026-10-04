using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClipTyper
{
    /// <summary>
    /// Settings dialog with three tabs: Typing, Overlay, and App.
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
        private CheckBox _toggleHotkeyEnableCheckbox = null!;

        // Typing & Speed
        private TrackBar _delaySlider = null!;
        private Label _delayLabel = null!;
        private ComboBox _newlineComboBox = null!;
        private CheckBox _enforcePlainTextCheckbox = null!;
        private CheckBox _jitterCheckbox = null!;
        private NumericUpDown _jitterRangeInput = null!;

        // Safety & Compatibility
        private CheckBox _sanitizeInputCheckbox = null!;
        private CheckBox _maxLenEnableCheckbox = null!;
        private NumericUpDown _maxLenInput = null!;
        private CheckBox _vkModeCheckbox = null!;
        private CheckBox _soundFeedbackCheckbox = null!;

        // Credential Auto-Type (v1.6.0 / v1.6.1)
        private ComboBox _credModeComboBox = null!;
        private TextBox _customDelimiterBox = null!;
        private ComboBox _customTransitionComboBox = null!;
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
        private CheckBox _loggingCheckbox = null!;

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
        /// Raised when the user saves settings. Passes modified settings instance and resetPosition flag (TD-39).
        /// </summary>
        public event Action<AppSettings, bool>? SettingsSaved;

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
            AutoScroll = false;

            var tabs = new TabControl
            {
                Location = new Point(12, 12),
                Size = new Size(424, 420)
            };
            var typingPage = new TabPage("Typing") { AutoScroll = true };
            var overlayPage = new TabPage("Overlay") { AutoScroll = true };
            var appPage = new TabPage("App") { AutoScroll = true };

            var hotkeyLabel = new Label { Text = "Type Clipboard:", Location = new Point(12, 16), AutoSize = true };
            _hotkeyBox = new TextBox { Location = new Point(150, 13), Size = new Size(250, 23), ReadOnly = true, BackColor = SystemColors.Window };
            _hotkeyBox.GotFocus += (_, _) => StartRecording(RecordingTarget.TriggerHotkey, _hotkeyBox);
            var hotkeyHint = new Label { Text = "Click a box and press keys to record.", Location = new Point(12, 42), ForeColor = Color.Gray, AutoSize = true };

            var delayCaption = new Label { Text = "Keystroke Delay:", Location = new Point(12, 76), AutoSize = true };
            _delayLabel = new Label { Text = "25 ms", Location = new Point(360, 76), AutoSize = true };
            _delaySlider = new TrackBar { Location = new Point(140, 68), Size = new Size(210, 45), Minimum = 5, Maximum = 100, TickFrequency = 5, SmallChange = 5, LargeChange = 10 };
            _delaySlider.ValueChanged += (_, _) => { _delayLabel.Text = $"{_delaySlider.Value} ms"; };

            var newlineLabel = new Label { Text = "Newline Handling:", Location = new Point(12, 120), AutoSize = true };
            _newlineComboBox = new ComboBox { Location = new Point(150, 117), Size = new Size(250, 23), DropDownStyle = ComboBoxStyle.DropDownList };
            _newlineComboBox.Items.AddRange(new object[]
            {
                "Enter (Default)",
                "Shift + Enter (Teams / Chats)",
                "Space (Replace newlines)",
                "Ignore (Strip newlines)"
            });

            _enforcePlainTextCheckbox = new CheckBox
            {
                Text = "Plain-Text Mode (prefer Unicode plain-text format)",
                Location = new Point(12, 152),
                Size = new Size(390, 20)
            };

            _jitterCheckbox = new CheckBox
            {
                Text = "Humanized jitter:",
                Location = new Point(12, 180),
                AutoSize = true
            };
            _jitterRangeInput = new NumericUpDown
            {
                Location = new Point(150, 178),
                Size = new Size(50, 23),
                Minimum = 1,
                Maximum = 50,
                Value = 5,
                Enabled = false
            };
            var jitterMsLabel = new Label { Text = "± ms variance", Location = new Point(208, 180), AutoSize = true };
            _jitterCheckbox.CheckedChanged += (_, _) =>
            {
                _jitterRangeInput.Enabled = _jitterCheckbox.Checked;
            };

            _vkModeCheckbox = new CheckBox
            {
                Text = "VK Compatibility Mode (iLO / iDRAC, Teams / RDP)",
                Location = new Point(12, 212),
                AutoSize = true
            };

            _sanitizeInputCheckbox = new CheckBox
            {
                Text = "Sanitize text (remove BOM, null-bytes & invisible chars)",
                Location = new Point(12, 240),
                Size = new Size(390, 20),
                Checked = true
            };

            _maxLenEnableCheckbox = new CheckBox
            {
                Text = "Confirm before typing text >",
                Location = new Point(12, 272),
                AutoSize = true,
                Checked = true
            };
            _maxLenInput = new NumericUpDown { Location = new Point(210, 270), Size = new Size(75, 23), Minimum = 100, Maximum = 500000, Value = 5000, Increment = 500 };
            var maxLenCharsLabel = new Label { Text = "chars", Location = new Point(292, 272), AutoSize = true };
            _maxLenEnableCheckbox.CheckedChanged += (_, _) =>
            {
                _maxLenInput.Enabled = _maxLenEnableCheckbox.Checked;
            };

            typingPage.Controls.AddRange(new Control[]
            {
                hotkeyLabel, _hotkeyBox, hotkeyHint,
                delayCaption, _delaySlider, _delayLabel,
                newlineLabel, _newlineComboBox,
                _enforcePlainTextCheckbox,
                _jitterCheckbox, _jitterRangeInput, jitterMsLabel,
                _vkModeCheckbox,
                _sanitizeInputCheckbox,
                _maxLenEnableCheckbox, _maxLenInput, maxLenCharsLabel
            });

            _overlayCheckbox = new CheckBox { Text = "Show Overlay Button", Location = new Point(12, 16), AutoSize = true };
            var toggleHotkeyLabel = new Label { Text = "Toggle Overlay:", Location = new Point(12, 52), AutoSize = true };
            _toggleHotkeyBox = new TextBox { Location = new Point(150, 49), Size = new Size(250, 23), ReadOnly = true, BackColor = SystemColors.Window };
            _toggleHotkeyBox.GotFocus += (_, _) => StartRecording(RecordingTarget.ToggleHotkey, _toggleHotkeyBox);
            var toggleHint = new Label { Text = "Click a box and press keys to record.", Location = new Point(12, 78), ForeColor = Color.Gray, AutoSize = true };
            _toggleHotkeyEnableCheckbox = new CheckBox
            {
                Text = "Enable overlay toggle hotkey",
                Location = new Point(12, 104),
                AutoSize = true
            };
            _toggleHotkeyEnableCheckbox.CheckedChanged += (_, _) =>
            {
                _toggleHotkeyBox.Enabled = _toggleHotkeyEnableCheckbox.Checked;
            };

            var scaleLabelCaption = new Label { Text = "Scale:", Location = new Point(12, 144), AutoSize = true };
            _scaleLabel = new Label { Text = "100%", Location = new Point(360, 144), AutoSize = true };
            _scaleSlider = new TrackBar { Location = new Point(80, 136), Size = new Size(270, 45), Minimum = 25, Maximum = 200, TickFrequency = 25, SmallChange = 5, LargeChange = 25 };
            _scaleSlider.ValueChanged += (_, _) =>
            {
                _scaleLabel.Text = $"{_scaleSlider.Value}%";
                LiveScaleChanged?.Invoke(_scaleSlider.Value);
            };

            var monitorLabelCaption = new Label { Text = "Monitor:", Location = new Point(12, 192), AutoSize = true };
            _monitorComboBox = new ComboBox { Location = new Point(80, 189), Size = new Size(270, 23), DropDownStyle = ComboBoxStyle.DropDownList };
            for (int i = 0; i < Screen.AllScreens.Length; i++)
            {
                var screen = Screen.AllScreens[i];
                string name = $"Monitor {i + 1}" + (screen.Primary ? " (Primary)" : "");
                _monitorComboBox.Items.Add(name);
            }

            _resetPositionBtn = new Button { Text = "Reset Position", Location = new Point(12, 228), Size = new Size(110, 24) };

            overlayPage.Controls.AddRange(new Control[]
            {
                _overlayCheckbox,
                toggleHotkeyLabel, _toggleHotkeyBox, toggleHint, _toggleHotkeyEnableCheckbox,
                scaleLabelCaption, _scaleSlider, _scaleLabel,
                monitorLabelCaption, _monitorComboBox, _resetPositionBtn
            });

            var credGroup = new GroupBox
            {
                Text = "Credential Auto-Type",
                Location = new Point(8, 8),
                Size = new Size(392, 196)
            };

            var credModeLabel = new Label { Text = "Mode:", Location = new Point(12, 28), AutoSize = true };
            _credModeComboBox = new ComboBox { Location = new Point(130, 25), Size = new Size(245, 23), DropDownStyle = ComboBoxStyle.DropDownList };
            _credModeComboBox.Items.AddRange(new object[]
            {
                "Disabled (Normal typing)",
                "Auto-Detect (Tab or Newline delimiter)",
                "Tab-Separated only (User<Tab>Pass)",
                "Newline-Separated only (User<Enter>Pass)",
                "Custom Delimiter"
            });

            var customDelimLabel = new Label { Text = "Custom Delim / Key:", Location = new Point(12, 60), AutoSize = true };
            _customDelimiterBox = new TextBox { Location = new Point(130, 57), Size = new Size(110, 23), Enabled = false };
            _customTransitionComboBox = new ComboBox { Location = new Point(248, 57), Size = new Size(127, 23), DropDownStyle = ComboBoxStyle.DropDownList, Enabled = false };
            _customTransitionComboBox.Items.AddRange(new object[] { "Send Tab", "Send Enter" });

            var stageDelayLabel = new Label { Text = "Stage Pause:", Location = new Point(12, 94), AutoSize = true };
            _stageDelayInput = new NumericUpDown { Location = new Point(130, 92), Size = new Size(70, 23), Minimum = 50, Maximum = 2000, Value = 200, Increment = 50 };
            var msLabel = new Label { Text = "ms between user & pass", Location = new Point(208, 94), AutoSize = true };

            _autoClearCheckbox = new CheckBox
            {
                Text = "Auto-clear clipboard after typing in",
                Location = new Point(12, 124),
                AutoSize = true
            };
            _autoClearDelayInput = new NumericUpDown { Location = new Point(248, 122), Size = new Size(50, 23), Minimum = 1, Maximum = 60, Value = 5, Enabled = false };
            var secLabel = new Label { Text = "seconds", Location = new Point(304, 124), AutoSize = true };

            _autoClearWarningLabel = new Label
            {
                Text = "Erases credentials from clipboard after the delay.",
                Location = new Point(12, 156),
                ForeColor = Color.DarkOrange,
                AutoSize = true,
                Visible = false
            };

            _credModeComboBox.SelectedIndexChanged += (_, _) =>
            {
                bool isCustom = (_credModeComboBox.SelectedIndex == 4);
                _customDelimiterBox.Enabled = isCustom;
                _customTransitionComboBox.Enabled = isCustom;
            };

            _autoClearCheckbox.CheckedChanged += (_, _) =>
            {
                _autoClearDelayInput.Enabled = _autoClearCheckbox.Checked;
                _autoClearWarningLabel.Visible = _autoClearCheckbox.Checked;
            };

            credGroup.Controls.AddRange(new Control[]
            {
                credModeLabel, _credModeComboBox,
                customDelimLabel, _customDelimiterBox, _customTransitionComboBox,
                stageDelayLabel, _stageDelayInput, msLabel,
                _autoClearCheckbox, _autoClearDelayInput, secLabel,
                _autoClearWarningLabel
            });

            _soundFeedbackCheckbox = new CheckBox
            {
                Text = "Play sound signal when typing completes",
                Location = new Point(12, 216),
                Size = new Size(390, 20)
            };
            _loggingCheckbox = new CheckBox
            {
                Text = "Enable diagnostic logging (clip-typer.log)",
                Location = new Point(12, 244),
                Size = new Size(390, 20),
                Checked = true
            };

            int appY = 272;
            if (!SettingsManager.IsPortable)
            {
                _autostartCheckbox = new CheckBox
                {
                    Text = "Run ClipTyper at Windows startup",
                    Location = new Point(12, appY),
                    AutoSize = true
                };
                appPage.Controls.Add(_autostartCheckbox);
                appY += 28;
            }

            _autoUpdateCheckbox = new CheckBox
            {
                Text = "Automatically check for updates",
                Location = new Point(12, appY),
                AutoSize = true
            };

            appPage.Controls.Add(credGroup);
            appPage.Controls.Add(_soundFeedbackCheckbox);
            appPage.Controls.Add(_loggingCheckbox);
            appPage.Controls.Add(_autoUpdateCheckbox);

            tabs.TabPages.Add(typingPage);
            tabs.TabPages.Add(overlayPage);
            tabs.TabPages.Add(appPage);
            Controls.Add(tabs);

            _saveBtn = new Button { Text = "Save", Location = new Point(254, 444), Size = new Size(85, 28) };
            _saveBtn.Click += OnSave;

            _cancelBtn = new Button { Text = "Cancel", Location = new Point(347, 444), Size = new Size(85, 28), DialogResult = DialogResult.Cancel };

            Controls.AddRange(new Control[] { _saveBtn, _cancelBtn });
            AcceptButton = _saveBtn;
            CancelButton = _cancelBtn;

            ClientSize = new Size(448, 488);
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
            _toggleHotkeyEnableCheckbox.Checked = s.OverlayToggleEnabled;
            _toggleHotkeyBox.Enabled = s.OverlayToggleEnabled;

            // Keystroke delay & formatting
            _delaySlider.Value = Math.Clamp(s.KeystrokeDelayMs, 5, 100);
            _delayLabel.Text = $"{_delaySlider.Value} ms";
            _newlineComboBox.SelectedIndex = Math.Clamp((int)s.NewlineHandling, 0, 3);
            _enforcePlainTextCheckbox.Checked = s.EnforcePlainText;
            _jitterCheckbox.Checked = s.EnableTypingJitter;
            _jitterRangeInput.Value = Math.Clamp(s.TypingJitterRangeMs, 1, 50);
            _jitterRangeInput.Enabled = s.EnableTypingJitter;

            // Credential Auto-Type
            _credModeComboBox.SelectedIndex = Math.Clamp((int)s.CredentialAutoTypeMode, 0, 4);
            _customDelimiterBox.Text = s.CredentialCustomDelimiter;
            bool isCustom = (s.CredentialAutoTypeMode == CredentialMode.Custom);
            _customDelimiterBox.Enabled = isCustom;
            _customTransitionComboBox.SelectedIndex = (s.CredentialCustomTransitionKey == 0x0D) ? 1 : 0;
            _customTransitionComboBox.Enabled = isCustom;

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
            _loggingCheckbox.Checked = s.EnableDiagnosticLogging;

            // Overlay
            _overlayCheckbox.Checked = s.OverlayEnabled;
            _scaleSlider.Value = Math.Clamp(s.OverlayScalePercent, 25, 200);
            _scaleLabel.Text = $"{_scaleSlider.Value}%";
            int monIdx = Math.Clamp(s.OverlayMonitorIndex, 0, Screen.AllScreens.Length - 1);
            if (_monitorComboBox.Items.Count > monIdx)
            {
                _monitorComboBox.SelectedIndex = monIdx;
            }

            // Startup & Updates
            if (_autostartCheckbox != null)
            {
                _autostartCheckbox.Checked = s.AutoStartEnabled;
            }
            _autoUpdateCheckbox.Checked = s.AutoUpdateCheckEnabled;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_recordingTarget == RecordingTarget.None)
            {
                return base.ProcessCmdKey(ref msg, keyData);
            }

            TextBox targetBox = _recordingTarget == RecordingTarget.TriggerHotkey ? _hotkeyBox : _toggleHotkeyBox;

            // Handle Escape to cancel recording
            if ((keyData & Keys.KeyCode) == Keys.Escape)
            {
                if (_recordingTarget == RecordingTarget.TriggerHotkey)
                {
                    targetBox.Text = FormatHotkey(_recordedModifiers, _recordedKey);
                }
                else
                {
                    targetBox.Text = FormatHotkey(_recordedToggleModifiers, _recordedToggleKey);
                }
                targetBox.BackColor = SystemColors.Window;
                var cancelledTarget = _recordingTarget;
                _recordingTarget = RecordingTarget.None;
                if (cancelledTarget == RecordingTarget.TriggerHotkey)
                {
                    _delaySlider.Focus();
                }
                else
                {
                    _scaleSlider.Focus();
                }
                return true;
            }

            Keys keyCode = keyData & Keys.KeyCode;
            if (keyCode == Keys.ControlKey || keyCode == Keys.ShiftKey || keyCode == Keys.Menu)
            {
                return true;
            }

            GlobalHotkey.Modifiers mods = GlobalHotkey.Modifiers.None;
            if ((keyData & Keys.Control) != 0) mods |= GlobalHotkey.Modifiers.Control;
            if ((keyData & Keys.Alt) != 0)     mods |= GlobalHotkey.Modifiers.Alt;
            if ((keyData & Keys.Shift) != 0)   mods |= GlobalHotkey.Modifiers.Shift;

            Keys baseKey = keyData & ~Keys.Modifiers;

            // TD-66: Require at least one modifier for all hotkeys (including function keys)
            // to avoid hijacking system-wide keys like F1 or F5.
            if (mods == GlobalHotkey.Modifiers.None)
            {
                // TD-67: Provide clear feedback when modifier is missing
                targetBox.Text = "Need modifier (Ctrl/Shift/Alt)";
                targetBox.BackColor = Color.FromArgb(255, 235, 180);
                return true;
            }

            foreach (var blocked in BlockedCombos)
            {
                if (mods == blocked.mod && baseKey == blocked.key)
                {
                    // TD-67: Feedback for reserved system combinations
                    targetBox.Text = $"{FormatHotkey(mods, baseKey)} (Reserved)";
                    targetBox.BackColor = Color.FromArgb(255, 220, 180);
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

            var completedTarget = _recordingTarget;
            _recordingTarget = RecordingTarget.None;
            if (completedTarget == RecordingTarget.TriggerHotkey)
            {
                _delaySlider.Focus();
            }
            else
            {
                _scaleSlider.Focus();
            }
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
            // TD-54: Validate empty custom delimiter
            if (_credModeComboBox.SelectedIndex == (int)CredentialMode.Custom &&
                string.IsNullOrWhiteSpace(_customDelimiterBox.Text))
            {
                MessageBox.Show(
                    "Please enter a custom delimiter text, or select a different Credential Auto-Type mode.",
                    "Invalid Custom Delimiter",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                _customDelimiterBox.Focus();
                return;
            }

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
                OverlayToggleEnabled = _toggleHotkeyEnableCheckbox.Checked,
                KeystrokeDelayMs = _delaySlider.Value,
                NewlineHandling = (NewlineMode)_newlineComboBox.SelectedIndex,
                EnforcePlainText = _enforcePlainTextCheckbox.Checked,
                EnableTypingJitter = _jitterCheckbox.Checked,
                TypingJitterRangeMs = (int)_jitterRangeInput.Value,
                CredentialAutoTypeMode = (CredentialMode)_credModeComboBox.SelectedIndex,
                CredentialCustomDelimiter = _customDelimiterBox.Text,
                CredentialCustomTransitionKey = (_customTransitionComboBox.SelectedIndex == 1) ? (ushort)0x0D : (ushort)0x09,
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
                LastUpdateCheckUtc = SettingsManager.Current.LastUpdateCheckUtc,
                EnableDiagnosticLogging = _loggingCheckbox.Checked
            };

            SettingsSaved?.Invoke(s, resetPosition);
            DialogResult = DialogResult.OK;
            Close();
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
