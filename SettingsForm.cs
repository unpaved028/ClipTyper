using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClipTyper
{
    /// <summary>
    /// Settings dialog with four sections: Hotkey recorder, keystroke delay
    /// slider, overlay configuration, and autostart toggle (Winget only).
    /// </summary>
    public class SettingsForm : Form
    {
        // ── P/Invoke for hotkey conflict probe ──────────────────────
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int ProbeHotkeyId = 9999; // Unique ID for conflict probe

        // ── Controls ────────────────────────────────────────────────
        private TextBox _hotkeyBox = null!;
        private TextBox _toggleHotkeyBox = null!;
        private TrackBar _delaySlider = null!;
        private Label _delayLabel = null!;
        private CheckBox _overlayCheckbox = null!;
        private TrackBar _scaleSlider = null!;
        private Label _scaleLabel = null!;
        private ComboBox _monitorComboBox = null!;
        private Button _resetPositionBtn = null!;
        private CheckBox? _autostartCheckbox;
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

        // ── Blocked hotkey combinations ─────────────────────────────
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
        /// Raised when the user saves settings. Parameters:
        /// (Modifiers, Key, DelayMs, OverlayEnabled, OverlayScale, OverlayMonitor, ResetPosition, AutoStartEnabled, ToggleModifiers, ToggleKey, ToggleEnabled)
        /// </summary>
        public event Action<GlobalHotkey.Modifiers, Keys, int, bool, int, int, bool, bool, GlobalHotkey.Modifiers, Keys, bool>? SettingsSaved;

        /// <summary>
        /// Raised in real-time when the user moves the scale slider.
        /// </summary>
        public event Action<int>? LiveScaleChanged;

        public SettingsForm()
        {
            InitializeUI();
            LoadCurrentSettings();
        }

        // ── UI Construction ─────────────────────────────────────────

        private void InitializeUI()
        {
            Text = "Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            KeyPreview = true;

            int y = 12;

            // ── Hotkeys Group ───────────────────────────────────────
            var hotkeyGroup = new GroupBox
            {
                Text = "Hotkeys",
                Location = new Point(12, y),
                Size = new Size(380, 110)
            };

            var hotkeyLabel = new Label
            {
                Text = "Trigger Hotkey:",
                Location = new Point(12, 28),
                AutoSize = true
            };

            _hotkeyBox = new TextBox
            {
                Location = new Point(150, 25),
                Size = new Size(200, 23),
                ReadOnly = true,
                TextAlign = HorizontalAlignment.Center,
                Cursor = Cursors.Hand,
                BackColor = SystemColors.Window
            };
            _hotkeyBox.Enter += (_, _) => { _recordingTarget = RecordingTarget.TriggerHotkey; _hotkeyBox.BackColor = Color.LightYellow; };
            _hotkeyBox.Leave += (_, _) => { _recordingTarget = RecordingTarget.None; _hotkeyBox.BackColor = SystemColors.Window; };

            var toggleHotkeyLabel = new Label
            {
                Text = "Toggle Overlay:",
                Location = new Point(12, 58),
                AutoSize = true
            };

            _toggleHotkeyBox = new TextBox
            {
                Location = new Point(150, 55),
                Size = new Size(200, 23),
                ReadOnly = true,
                TextAlign = HorizontalAlignment.Center,
                Cursor = Cursors.Hand,
                BackColor = SystemColors.Window
            };
            _toggleHotkeyBox.Enter += (_, _) => { _recordingTarget = RecordingTarget.ToggleHotkey; _toggleHotkeyBox.BackColor = Color.LightYellow; };
            _toggleHotkeyBox.Leave += (_, _) => { _recordingTarget = RecordingTarget.None; _toggleHotkeyBox.BackColor = SystemColors.Window; };

            var hotkeyHint = new Label
            {
                Text = "Click and press new hotkey combination",
                Location = new Point(150, 82),
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Font = new Font(Font.FontFamily, 7.5f)
            };

            hotkeyGroup.Controls.AddRange(new Control[] { hotkeyLabel, _hotkeyBox, toggleHotkeyLabel, _toggleHotkeyBox, hotkeyHint });
            Controls.Add(hotkeyGroup);
            y += 120;

            // ── Typing Group ────────────────────────────────────────
            var typingGroup = new GroupBox
            {
                Text = "Typing",
                Location = new Point(12, y),
                Size = new Size(380, 70)
            };

            var delayCaption = new Label
            {
                Text = "Keystroke Delay:",
                Location = new Point(12, 28),
                AutoSize = true
            };

            _delaySlider = new TrackBar
            {
                Location = new Point(120, 20),
                Size = new Size(180, 45),
                Minimum = 5,
                Maximum = 100,
                TickFrequency = 5,
                SmallChange = 5,
                LargeChange = 10
            };
            _delaySlider.ValueChanged += (_, _) =>
            {
                _delayLabel.Text = $"{_delaySlider.Value} ms";
            };

            _delayLabel = new Label
            {
                Text = "25 ms",
                Location = new Point(310, 28),
                AutoSize = true
            };

            typingGroup.Controls.AddRange(new Control[] { delayCaption, _delaySlider, _delayLabel });
            Controls.Add(typingGroup);
            y += 80;

            // ── Overlay Group ───────────────────────────────────────
            var overlayGroup = new GroupBox
            {
                Text = "Overlay",
                Location = new Point(12, y),
                Size = new Size(380, 160)
            };

            _overlayCheckbox = new CheckBox
            {
                Text = "Show Overlay Button",
                Location = new Point(12, 25),
                AutoSize = true
            };

            var scaleLabelCaption = new Label
            {
                Text = "Scale:",
                Location = new Point(12, 58),
                AutoSize = true
            };

            _scaleSlider = new TrackBar
            {
                Location = new Point(80, 52),
                Size = new Size(220, 45),
                Minimum = 25,
                Maximum = 200,
                TickFrequency = 25,
                SmallChange = 5,
                LargeChange = 25
            };
            _scaleSlider.ValueChanged += (_, _) =>
            {
                _scaleLabel.Text = $"{_scaleSlider.Value}%";
                LiveScaleChanged?.Invoke(_scaleSlider.Value);
            };

            _scaleLabel = new Label
            {
                Text = "100%",
                Location = new Point(310, 58),
                AutoSize = true
            };

            var monitorLabelCaption = new Label
            {
                Text = "Monitor:",
                Location = new Point(12, 98),
                AutoSize = true
            };

            _monitorComboBox = new ComboBox
            {
                Location = new Point(80, 95),
                Size = new Size(220, 23),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            // Populate monitor options
            for (int i = 0; i < Screen.AllScreens.Length; i++)
            {
                var screen = Screen.AllScreens[i];
                string name = $"Monitor {i + 1}" + (screen.Primary ? " (Primary)" : "");
                _monitorComboBox.Items.Add(name);
            }

            _resetPositionBtn = new Button
            {
                Text = "Reset Position",
                Location = new Point(12, 128),
                Size = new Size(100, 23)
            };

            overlayGroup.Controls.AddRange(new Control[]
            {
                _overlayCheckbox, scaleLabelCaption, _scaleSlider, _scaleLabel,
                monitorLabelCaption, _monitorComboBox, _resetPositionBtn
            });
            Controls.Add(overlayGroup);
            y += 170;

            // ── Autostart Group (Winget/installed mode only) ────────
            if (!SettingsManager.IsPortable)
            {
                var autostartGroup = new GroupBox
                {
                    Text = "Startup",
                    Location = new Point(12, y),
                    Size = new Size(380, 50)
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

            // ── Buttons ─────────────────────────────────────────────
            _saveBtn = new Button
            {
                Text = "Save",
                Location = new Point(220, y),
                Size = new Size(80, 28),
                DialogResult = DialogResult.OK
            };
            _saveBtn.Click += OnSave;

            _cancelBtn = new Button
            {
                Text = "Cancel",
                Location = new Point(310, y),
                Size = new Size(80, 28),
                DialogResult = DialogResult.Cancel
            };

            Controls.AddRange(new Control[] { _saveBtn, _cancelBtn });
            AcceptButton = _saveBtn;
            CancelButton = _cancelBtn;

            // Set form height to fit all controls
            Size = new Size(420, y + 75);
        }

        // ── Load current settings into controls ─────────────────────

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

            // Keystroke delay
            _delaySlider.Value = Math.Clamp(s.KeystrokeDelayMs, 5, 100);
            _delayLabel.Text = $"{_delaySlider.Value} ms";

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
                // Read actual registry state (might differ from settings if user
                // manually edited the registry or a previous save failed)
                _autostartCheckbox.Checked = InstallHelper.IsAutoStartEnabled();
            }
        }

        // ── Hotkey Recorder ─────────────────────────────────────────

        /// <summary>
        /// Intercepts ALL key presses (including Tab, Enter, Escape) before
        /// they reach any control. This is where hotkey recording happens,
        /// because OnKeyDown doesn't reliably receive all key combinations
        /// when the focused control is a ReadOnly TextBox.
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_recordingTarget == RecordingTarget.None)
            {
                return base.ProcessCmdKey(ref msg, keyData);
            }

            // Extract the base key (strip modifier flags from keyData)
            Keys baseKey = keyData & Keys.KeyCode;

            // Ignore bare modifier presses — wait for a non-modifier key
            if (baseKey == Keys.ControlKey || baseKey == Keys.ShiftKey ||
                baseKey == Keys.Menu || baseKey == Keys.LWin ||
                baseKey == Keys.RWin)
            {
                return true; // Suppress but keep recording
            }

            // Build modifier flags from keyData
            var mods = GlobalHotkey.Modifiers.None;
            if ((keyData & Keys.Control) != 0) mods |= GlobalHotkey.Modifiers.Control;
            if ((keyData & Keys.Shift) != 0)   mods |= GlobalHotkey.Modifiers.Shift;
            if ((keyData & Keys.Alt) != 0)     mods |= GlobalHotkey.Modifiers.Alt;

            TextBox targetBox = _recordingTarget == RecordingTarget.TriggerHotkey ? _hotkeyBox : _toggleHotkeyBox;

            // Must have at least one modifier
            if (mods == GlobalHotkey.Modifiers.None)
            {
                targetBox.Text = "Need modifier (Ctrl/Shift/Alt)";
                return true;
            }

            // Check blocked combos
            foreach (var blocked in BlockedCombos)
            {
                if (mods == blocked.mod && baseKey == blocked.key)
                {
                    targetBox.Text = $"{FormatHotkey(mods, baseKey)} (blocked!)";
                    targetBox.BackColor = Color.FromArgb(255, 200, 200); // Light red
                    return true;
                }
            }

            // Valid combo — probe whether it's already in use
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
                targetBox.BackColor = Color.FromArgb(255, 220, 150); // Orange
            }

            // Move focus away from the recorder
            _recordingTarget = RecordingTarget.None;
            _delaySlider.Focus();
            return true;
        }

        /// <summary>
        /// Probes whether a hotkey combination is available by temporarily
        /// registering and unregistering it. Returns true if the combo is free.
        /// Checks for conflict with the other configured hotkey first.
        /// </summary>
        private bool ProbeHotkey(GlobalHotkey.Modifiers mods, Keys key, RecordingTarget target)
        {
            // Check conflict with the other hotkey currently being configured
            if (target == RecordingTarget.TriggerHotkey)
            {
                if (mods == _recordedToggleModifiers && key == _recordedToggleKey)
                {
                    return false; // Conflict with toggle hotkey
                }

                // If identical to active trigger hotkey in settings, it is ours
                var currentSettings = SettingsManager.Current;
                if ((int)mods == currentSettings.HotkeyModifiers &&
                    (int)key == currentSettings.HotkeyKey)
                {
                    return true;
                }
            }
            else if (target == RecordingTarget.ToggleHotkey)
            {
                if (mods == _recordedModifiers && key == _recordedKey)
                {
                    return false; // Conflict with trigger hotkey
                }

                // If identical to active toggle hotkey in settings, it is ours
                var currentSettings = SettingsManager.Current;
                if ((int)mods == currentSettings.OverlayToggleModifiers &&
                    (int)key == currentSettings.OverlayToggleKey)
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

        // ── Save ────────────────────────────────────────────────────

        private bool _resetPosition;

        private void OnSave(object? sender, EventArgs e)
        {
            _resetPosition = false;

            // Check if reset position was requested
            if (_resetPositionBtn.Tag is bool reset && reset)
            {
                _resetPosition = true;
            }

            int scale = _scaleSlider.Value;
            int monitorIndex = _monitorComboBox.SelectedIndex >= 0 ? _monitorComboBox.SelectedIndex : 0;
            bool autoStart = _autostartCheckbox?.Checked ?? SettingsManager.Current.AutoStartEnabled;

            SettingsSaved?.Invoke(
                _recordedModifiers,
                _recordedKey,
                _delaySlider.Value,
                _overlayCheckbox.Checked,
                scale,
                monitorIndex,
                _resetPosition,
                autoStart,
                _recordedToggleModifiers,
                _recordedToggleKey,
                true
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

        // ── Helpers ─────────────────────────────────────────────────

        /// <summary>
        /// Formats a hotkey combination as a human-readable string
        /// (e.g., "Ctrl + Shift + T").
        /// </summary>
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
