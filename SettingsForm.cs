using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClipTyper
{
    /// <summary>
    /// Settings dialog with five tabs: Typing, Keyboard, Credentials, Overlay and General.
    /// Layout coordinates are 96-DPI pixels; WinForms scales them to the monitor's DPI.
    /// Colors follow the light/dark appearance (see <see cref="Theme"/>).
    /// </summary>
    public class SettingsForm : Form
    {
        // ── P/Invoke for hotkey conflict probe ──────────────────────
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int ProbeHotkeyId = 9999;

        // ── Layout grid (96-DPI pixels) ─────────────────────────────
        private const int LabelX = 16;      // left column: labels, checkboxes, radio buttons
        private const int ControlX = 210;   // right column: inputs
        private const int RightEdge = 500;  // end of the content area
        private const int IndentX = 36;     // hints below a checkbox / radio button

        private const string HotkeyHintText = "Click the box, then press the new shortcut.";

        private readonly Font _headerFont;

        // ── Controls ────────────────────────────────────────────────
        private TabControl _tabs = null!;

        // Typing
        private TextBox _hotkeyBox = null!;
        private Label _hotkeyStatus = null!;
        private TrackBar _delaySlider = null!;
        private Label _delayLabel = null!;
        private CheckBox _jitterCheckbox = null!;
        private NumericUpDown _jitterRangeInput = null!;
        private ComboBox _newlineComboBox = null!;
        private CheckBox _sanitizeInputCheckbox = null!;
        private CheckBox _maxLenEnableCheckbox = null!;
        private NumericUpDown _maxLenInput = null!;

        // Keyboard
        private RadioButton _unicodeRadio = null!;
        private RadioButton _vkRadio = null!;
        private ComboBox _targetLayoutComboBox = null!;

        // Credentials
        private ComboBox _credModeComboBox = null!;
        private TextBox _customDelimiterBox = null!;
        private ComboBox _customTransitionComboBox = null!;
        private NumericUpDown _stageDelayInput = null!;
        private CheckBox _autoClearCheckbox = null!;
        private NumericUpDown _autoClearDelayInput = null!;

        // Overlay
        private CheckBox _overlayCheckbox = null!;
        private TrackBar _scaleSlider = null!;
        private Label _scaleLabel = null!;
        private ComboBox _monitorComboBox = null!;
        private Button _resetPositionBtn = null!;
        private CheckBox _toggleHotkeyEnableCheckbox = null!;
        private TextBox _toggleHotkeyBox = null!;
        private Label _toggleHotkeyStatus = null!;

        // General
        private ComboBox _themeComboBox = null!;
        private CheckBox? _autostartCheckbox;
        private CheckBox _autoUpdateCheckbox = null!;
        private CheckBox _soundFeedbackCheckbox = null!;
        private CheckBox _loggingCheckbox = null!;
        private Button _openFolderButton = null!;

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
        /// Raised in real-time when the user moves the overlay size slider.
        /// </summary>
        public event Action<int>? LiveScaleChanged;

        public SettingsForm()
        {
            _headerFont = new Font("Segoe UI Semibold", 10f);
            InitializeUI();
            LoadCurrentSettings();
        }

        // ── Layout helper ───────────────────────────────────────────

        /// <summary>
        /// Places controls on a tab page top to bottom on a two-column grid.
        /// </summary>
        private sealed class PageBuilder
        {
            private readonly TabPage _page;
            private readonly Font _headerFont;
            private bool _first = true;

            public int Y { get; set; } = 10;

            public PageBuilder(TabPage page, Font headerFont)
            {
                _page = page;
                _headerFont = headerFont;
            }

            public void Header(string text)
            {
                if (!_first) Y += 14;
                _first = false;
                Add(new Label { Text = text, Font = _headerFont, AutoSize = true, UseMnemonic = false, Location = new Point(LabelX, Y) });
                Y += 30;
            }

            /// <summary>Label in the left column, control in the right column.</summary>
            public void Row(string label, Control control, int height = 32)
            {
                Add(new Label { Text = label, AutoSize = true, UseMnemonic = false, Location = new Point(LabelX, Y + 4) });
                control.Location = new Point(ControlX, Y);
                Add(control);
                Y += height;
            }

            /// <summary>Control at the given x position on its own line.</summary>
            public void Line(Control control, int x = LabelX, int height = 30)
            {
                control.Location = new Point(x, Y);
                Add(control);
                Y += height;
            }

            /// <summary>Gray explanation text; height reserved for the given number of lines.</summary>
            public Label Hint(string text, int x = LabelX, int lines = 1)
            {
                var label = new Label
                {
                    Text = text,
                    ForeColor = Theme.Hint,
                    UseMnemonic = false,
                    Location = new Point(x, Y - 4),
                    Size = new Size(RightEdge - x, 18 * lines)
                };
                Add(label);
                Y += 18 * lines + 4;
                return label;
            }

            /// <summary>Adds a control at an absolute position without moving the cursor.</summary>
            public void Place(Control control, int x, int yOffset = 0)
            {
                control.Location = new Point(x, Y + yOffset);
                Add(control);
            }

            public void Add(Control control) => _page.Controls.Add(control);
        }

        private static NumericUpDown Number(int min, int max, int value, int width, int increment = 1) =>
            new NumericUpDown { Minimum = min, Maximum = max, Value = value, Increment = increment, Size = new Size(width, 23) };

        private static ComboBox Choice(int width, params object[] items)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Size = new Size(width, 23) };
            combo.Items.AddRange(items);
            return combo;
        }

        private static Label Caption(string text) => new Label { Text = text, AutoSize = true, UseMnemonic = false };

        // ── UI ──────────────────────────────────────────────────────

        private void InitializeUI()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;

            Text = "ClipTyper Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            KeyPreview = true;

            _tabs = new TabControl { Location = new Point(12, 12), Size = new Size(536, 430) };
            _tabs.TabPages.Add(BuildTypingPage());
            _tabs.TabPages.Add(BuildKeyboardPage());
            _tabs.TabPages.Add(BuildCredentialsPage());
            _tabs.TabPages.Add(BuildOverlayPage());
            _tabs.TabPages.Add(BuildGeneralPage());
            Controls.Add(_tabs);

            _saveBtn = new Button { Text = "Save", Location = new Point(366, 454), Size = new Size(86, 28) };
            _saveBtn.Click += OnSave;
            _cancelBtn = new Button { Text = "Cancel", Location = new Point(462, 454), Size = new Size(86, 28), DialogResult = DialogResult.Cancel };
            Controls.AddRange(new Control[] { _saveBtn, _cancelBtn });
            AcceptButton = _saveBtn;
            CancelButton = _cancelBtn;

            ClientSize = new Size(560, 494);
            ActiveControl = _tabs; // not the hotkey box: focusing it starts recording
            ResumeLayout(false);
        }

        private TabPage BuildTypingPage()
        {
            var page = new TabPage("Typing");
            var b = new PageBuilder(page, _headerFont);

            b.Header("Hotkey");
            _hotkeyBox = new TextBox { Size = new Size(RightEdge - ControlX, 23), ReadOnly = true, AccessibleName = "Hotkey to type the clipboard" };
            _hotkeyBox.GotFocus += (_, _) => StartRecording(RecordingTarget.TriggerHotkey);
            b.Row("Type the clipboard", _hotkeyBox, height: 28);
            _hotkeyStatus = b.Hint(HotkeyHintText, ControlX, lines: 2);

            b.Header("Speed");
            _delaySlider = new TrackBar { Minimum = 5, Maximum = 100, TickStyle = TickStyle.None, AutoSize = false, Size = new Size(230, 30), SmallChange = 5, LargeChange = 10 };
            _delayLabel = Caption("25 ms");
            _delaySlider.ValueChanged += (_, _) => _delayLabel.Text = $"{_delaySlider.Value} ms";
            b.Place(_delayLabel, ControlX + 238, 4);
            b.Row("Delay between keys", _delaySlider, height: 36);

            _jitterCheckbox = new CheckBox { Text = "Humanized timing", AutoSize = true };
            _jitterRangeInput = Number(1, 50, 5, 56);
            _jitterCheckbox.CheckedChanged += (_, _) => _jitterRangeInput.Enabled = _jitterCheckbox.Checked;
            b.Place(Caption("vary by ±"), ControlX, 4);
            b.Place(_jitterRangeInput, ControlX + 62);
            b.Place(Caption("ms"), ControlX + 124, 4);
            b.Line(_jitterCheckbox, height: 32);

            b.Header("Text");
            _newlineComboBox = Choice(RightEdge - ControlX,
                "Enter (default)",
                "Shift + Enter (chats like Teams)",
                "Space (join lines)",
                "Ignore (remove line breaks)");
            b.Row("Line breaks", _newlineComboBox);

            _sanitizeInputCheckbox = new CheckBox { Text = "Remove invisible characters (BOM, zero-width spaces, null bytes)", AutoSize = true, Checked = true };
            b.Line(_sanitizeInputCheckbox);

            _maxLenEnableCheckbox = new CheckBox { Text = "Ask before typing more than", AutoSize = true, Checked = true };
            _maxLenInput = Number(100, 500000, 5000, 80, 500);
            _maxLenEnableCheckbox.CheckedChanged += (_, _) => _maxLenInput.Enabled = _maxLenEnableCheckbox.Checked;
            b.Place(_maxLenInput, ControlX);
            b.Place(Caption("characters"), ControlX + 86, 4);
            b.Line(_maxLenEnableCheckbox);

            return page;
        }

        private TabPage BuildKeyboardPage()
        {
            var page = new TabPage("Keyboard");
            var b = new PageBuilder(page, _headerFont);

            b.Header("Typing method");
            _unicodeRadio = new RadioButton { Text = "Characters (Unicode)", AutoSize = true, Checked = true };
            b.Line(_unicodeRadio, height: 24);
            b.Hint("Works for local apps, RDP sessions and most web pages.", IndentX);

            _vkRadio = new RadioButton { Text = "Key presses (VK Compatibility Mode)", AutoSize = true };
            _vkRadio.CheckedChanged += (_, _) => _targetLayoutComboBox.Enabled = _vkRadio.Checked;
            b.Line(_vkRadio, height: 24);
            b.Hint("For consoles that only accept key presses: iLO, iDRAC, VMRC, Hyper-V, KVM-over-IP and Teams remote control.", IndentX, lines: 2);

            b.Header("Console keyboard layout");
            _targetLayoutComboBox = Choice(RightEdge - ControlX);
            _targetLayoutComboBox.Enabled = false;
            foreach (var option in KeyboardLayouts.All)
            {
                _targetLayoutComboBox.Items.Add(option);
            }
            b.Row("Keyboard layout", _targetLayoutComboBox);
            b.Hint("Choose the layout the server or console uses, for example English (United States) for a US iLO. " +
                   "Characters that layout cannot type are listed before typing starts.", lines: 2);
            b.Hint("Tip: switch the method and the layout quickly from the tray icon or overlay menu.");

            return page;
        }

        private TabPage BuildCredentialsPage()
        {
            var page = new TabPage("Credentials");
            var b = new PageBuilder(page, _headerFont);

            b.Header("Credential Auto-Type");
            b.Hint("Types a user name and a password in two steps when the clipboard holds exactly two parts, " +
                   "for example user<Tab>password.", lines: 2);

            _credModeComboBox = Choice(RightEdge - ControlX,
                "Disabled (normal typing)",
                "Auto-detect (Tab or line break)",
                "Tab only (user<Tab>password)",
                "Line break only (user<Enter>password)",
                "Custom delimiter");
            _credModeComboBox.SelectedIndexChanged += (_, _) =>
            {
                bool isCustom = _credModeComboBox.SelectedIndex == (int)CredentialMode.Custom;
                _customDelimiterBox.Enabled = isCustom;
                _customTransitionComboBox.Enabled = isCustom;
            };
            b.Row("Mode", _credModeComboBox);

            _customDelimiterBox = new TextBox { Size = new Size(100, 23), Enabled = false, AccessibleName = "Custom delimiter" };
            _customTransitionComboBox = Choice(110, "then Tab", "then Enter");
            _customTransitionComboBox.Enabled = false;
            b.Place(_customTransitionComboBox, ControlX + 108);
            b.Row("Custom delimiter", _customDelimiterBox);

            _stageDelayInput = Number(50, 2000, 200, 70, 50);
            b.Place(Caption("ms"), ControlX + 76, 4);
            b.Row("Pause before password", _stageDelayInput);

            b.Header("Clipboard");
            _autoClearCheckbox = new CheckBox { Text = "Clear the clipboard after", AutoSize = true };
            _autoClearDelayInput = Number(1, 60, 5, 56);
            _autoClearDelayInput.Enabled = false;
            _autoClearCheckbox.CheckedChanged += (_, _) => _autoClearDelayInput.Enabled = _autoClearCheckbox.Checked;
            b.Place(_autoClearDelayInput, ControlX);
            b.Place(Caption("seconds"), ControlX + 62, 4);
            b.Line(_autoClearCheckbox);
            b.Hint("Only if the clipboard still holds the typed credentials. The Windows clipboard history (Win+V) is not cleared.", IndentX, lines: 2);

            return page;
        }

        private TabPage BuildOverlayPage()
        {
            var page = new TabPage("Overlay");
            var b = new PageBuilder(page, _headerFont);

            b.Header("Overlay button");
            _overlayCheckbox = new CheckBox { Text = "Show the overlay button", AutoSize = true };
            _overlayCheckbox.CheckedChanged += (_, _) => UpdateOverlayControls();
            b.Line(_overlayCheckbox, height: 24);
            b.Hint("A floating button for fullscreen RDP and KVM sessions, where the hotkey goes to the remote computer.", IndentX, lines: 2);

            _scaleSlider = new TrackBar { Minimum = 25, Maximum = 200, TickStyle = TickStyle.None, AutoSize = false, Size = new Size(230, 30), SmallChange = 5, LargeChange = 25 };
            _scaleLabel = Caption("100%");
            _scaleSlider.ValueChanged += (_, _) =>
            {
                _scaleLabel.Text = $"{_scaleSlider.Value}%";
                LiveScaleChanged?.Invoke(_scaleSlider.Value);
            };
            b.Place(_scaleLabel, ControlX + 238, 4);
            b.Row("Size", _scaleSlider, height: 36);

            _monitorComboBox = Choice(RightEdge - ControlX);
            for (int i = 0; i < Screen.AllScreens.Length; i++)
            {
                _monitorComboBox.Items.Add($"Monitor {i + 1}" + (Screen.AllScreens[i].Primary ? " (Primary)" : ""));
            }
            b.Row("Monitor", _monitorComboBox);

            _resetPositionBtn = new Button { Text = "Reset position", Size = new Size(140, 28) };
            _resetPositionBtn.Click += (_, _) =>
            {
                _resetPositionBtn.Tag = true;
                _resetPositionBtn.Text = "Will be reset";
                _resetPositionBtn.Enabled = false;
            };
            b.Line(_resetPositionBtn, ControlX, height: 34);

            b.Header("Show / hide shortcut");
            _toggleHotkeyEnableCheckbox = new CheckBox { Text = "Show or hide the overlay with a shortcut", AutoSize = true };
            _toggleHotkeyEnableCheckbox.CheckedChanged += (_, _) => _toggleHotkeyBox.Enabled = _toggleHotkeyEnableCheckbox.Checked;
            b.Line(_toggleHotkeyEnableCheckbox);

            _toggleHotkeyBox = new TextBox { Size = new Size(RightEdge - ControlX, 23), ReadOnly = true, AccessibleName = "Shortcut to show or hide the overlay" };
            _toggleHotkeyBox.GotFocus += (_, _) => StartRecording(RecordingTarget.ToggleHotkey);
            b.Row("Shortcut", _toggleHotkeyBox, height: 28);
            _toggleHotkeyStatus = b.Hint(HotkeyHintText, ControlX, lines: 2);

            return page;
        }

        private TabPage BuildGeneralPage()
        {
            var page = new TabPage("General");
            var b = new PageBuilder(page, _headerFont);

            b.Header("Appearance");
            _themeComboBox = Choice(160, Theme.DisplayName(AppTheme.System), Theme.DisplayName(AppTheme.Light), Theme.DisplayName(AppTheme.Dark));
            b.Row("Theme", _themeComboBox, height: 28);
            b.Hint("For Settings, About and the menus. Auto follows the Windows setting.");

            b.Header("Startup and updates");
            if (!SettingsManager.IsPortable)
            {
                _autostartCheckbox = new CheckBox { Text = "Start ClipTyper with Windows", AutoSize = true };
                b.Line(_autostartCheckbox);
            }
            _autoUpdateCheckbox = new CheckBox { Text = "Check for updates automatically (once a day)", AutoSize = true };
            b.Line(_autoUpdateCheckbox);

            b.Header("Feedback");
            _soundFeedbackCheckbox = new CheckBox { Text = "Play a sound when typing finishes", AutoSize = true };
            b.Line(_soundFeedbackCheckbox);

            b.Header("Troubleshooting");
            _loggingCheckbox = new CheckBox { Text = "Write a diagnostic log (never contains clipboard text)", AutoSize = true, Checked = true };
            b.Line(_loggingCheckbox);

            _openFolderButton = new Button { Text = "Open folder", Size = new Size(120, 28) };
            _openFolderButton.Click += (_, _) => OpenSettingsFolder();
            b.Row("Settings and log file", _openFolderButton);

            return page;
        }

        private void UpdateOverlayControls()
        {
            bool on = _overlayCheckbox.Checked;
            _scaleSlider.Enabled = on;
            _monitorComboBox.Enabled = on;
            _resetPositionBtn.Enabled = on && _resetPositionBtn.Tag is not true;
        }

        private static void OpenSettingsFolder()
        {
            try
            {
                Directory.CreateDirectory(SettingsManager.SettingsDir);
                Process.Start(new ProcessStartInfo(SettingsManager.SettingsDir) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Could not open the settings folder: {ex.Message}");
            }
        }

        // ── Load / save ─────────────────────────────────────────────

        private void LoadCurrentSettings()
        {
            var s = SettingsManager.Current;

            // Typing
            _recordedModifiers = (GlobalHotkey.Modifiers)s.HotkeyModifiers;
            _recordedKey = (Keys)s.HotkeyKey;
            _hotkeyBox.Text = FormatHotkey(_recordedModifiers, _recordedKey);
            _delaySlider.Value = Math.Clamp(s.KeystrokeDelayMs, 5, 100);
            _delayLabel.Text = $"{_delaySlider.Value} ms";
            _jitterCheckbox.Checked = s.EnableTypingJitter;
            _jitterRangeInput.Value = Math.Clamp(s.TypingJitterRangeMs, 1, 50);
            _jitterRangeInput.Enabled = s.EnableTypingJitter;
            _newlineComboBox.SelectedIndex = Math.Clamp((int)s.NewlineHandling, 0, 3);
            _sanitizeInputCheckbox.Checked = s.SanitizeInput;
            _maxLenEnableCheckbox.Checked = s.MaxTextLengthThreshold > 0;
            _maxLenInput.Value = s.MaxTextLengthThreshold > 0 ? Math.Clamp(s.MaxTextLengthThreshold, 100, 500000) : 5000;
            _maxLenInput.Enabled = s.MaxTextLengthThreshold > 0;

            // Keyboard
            _vkRadio.Checked = s.EnableVkCompatibilityMode;
            _unicodeRadio.Checked = !s.EnableVkCompatibilityMode;
            _targetLayoutComboBox.Enabled = s.EnableVkCompatibilityMode;
            SelectTargetLayout(s.TargetKeyboardLayout);

            // Credentials
            _credModeComboBox.SelectedIndex = Math.Clamp((int)s.CredentialAutoTypeMode, 0, 4);
            _customDelimiterBox.Text = s.CredentialCustomDelimiter;
            bool isCustom = s.CredentialAutoTypeMode == CredentialMode.Custom;
            _customDelimiterBox.Enabled = isCustom;
            _customTransitionComboBox.SelectedIndex = s.CredentialCustomTransitionKey == 0x0D ? 1 : 0;
            _customTransitionComboBox.Enabled = isCustom;
            _stageDelayInput.Value = Math.Clamp(s.CredentialStageDelayMs, 50, 2000);
            _autoClearCheckbox.Checked = s.CredentialAutoClearClipboard;
            _autoClearDelayInput.Value = Math.Clamp(s.CredentialAutoClearDelaySeconds, 1, 60);
            _autoClearDelayInput.Enabled = s.CredentialAutoClearClipboard;

            // Overlay
            _overlayCheckbox.Checked = s.OverlayEnabled;
            _scaleSlider.Value = Math.Clamp(s.OverlayScalePercent, 25, 200);
            _scaleLabel.Text = $"{_scaleSlider.Value}%";
            int monIdx = Math.Clamp(s.OverlayMonitorIndex, 0, Screen.AllScreens.Length - 1);
            if (_monitorComboBox.Items.Count > monIdx)
            {
                _monitorComboBox.SelectedIndex = monIdx;
            }
            UpdateOverlayControls();
            _recordedToggleModifiers = (GlobalHotkey.Modifiers)s.OverlayToggleModifiers;
            _recordedToggleKey = (Keys)s.OverlayToggleKey;
            _toggleHotkeyBox.Text = FormatHotkey(_recordedToggleModifiers, _recordedToggleKey);
            _toggleHotkeyEnableCheckbox.Checked = s.OverlayToggleEnabled;
            _toggleHotkeyBox.Enabled = s.OverlayToggleEnabled;

            // General
            _themeComboBox.SelectedIndex = Enum.IsDefined(s.Theme) ? (int)s.Theme : 0;
            if (_autostartCheckbox != null)
            {
                _autostartCheckbox.Checked = s.AutoStartEnabled;
            }
            _autoUpdateCheckbox.Checked = s.AutoUpdateCheckEnabled;
            _soundFeedbackCheckbox.Checked = s.SoundFeedbackEnabled;
            _loggingCheckbox.Checked = s.EnableDiagnosticLogging;
        }

        private void SelectTargetLayout(string klid)
        {
            foreach (KeyboardLayoutOption option in _targetLayoutComboBox.Items)
            {
                if (string.Equals(option.Klid, klid, StringComparison.OrdinalIgnoreCase))
                {
                    _targetLayoutComboBox.SelectedItem = option;
                    return;
                }
            }

            // A valid KLID that is not in the curated list (e.g. edited in settings.json): keep it selectable
            var custom = new KeyboardLayoutOption(klid, $"Keyboard layout {klid}");
            _targetLayoutComboBox.Items.Add(custom);
            _targetLayoutComboBox.SelectedItem = custom;
        }

        private void OnSave(object? sender, EventArgs e)
        {
            // TD-54: Validate empty custom delimiter
            if (_credModeComboBox.SelectedIndex == (int)CredentialMode.Custom &&
                string.IsNullOrWhiteSpace(_customDelimiterBox.Text))
            {
                _tabs.SelectedIndex = 2;
                Dialogs.Show(
                    this,
                    TaskDialogIcon.Warning,
                    "Custom delimiter missing",
                    "Enter the text that separates the user name and the password, or choose another mode.");
                _customDelimiterBox.Focus();
                return;
            }

            bool resetPosition = _resetPositionBtn.Tag is true;
            var current = SettingsManager.Current;

            var s = new AppSettings
            {
                HotkeyModifiers = (int)_recordedModifiers,
                HotkeyKey = (int)_recordedKey,
                KeystrokeDelayMs = _delaySlider.Value,
                EnableTypingJitter = _jitterCheckbox.Checked,
                TypingJitterRangeMs = (int)_jitterRangeInput.Value,
                NewlineHandling = (NewlineMode)_newlineComboBox.SelectedIndex,
                SanitizeInput = _sanitizeInputCheckbox.Checked,
                MaxTextLengthThreshold = _maxLenEnableCheckbox.Checked ? (int)_maxLenInput.Value : 0,

                EnableVkCompatibilityMode = _vkRadio.Checked,
                TargetKeyboardLayout = (_targetLayoutComboBox.SelectedItem as KeyboardLayoutOption)?.Klid ?? KeyboardLayouts.SameAsThisPc,

                CredentialAutoTypeMode = (CredentialMode)_credModeComboBox.SelectedIndex,
                CredentialCustomDelimiter = _customDelimiterBox.Text,
                CredentialCustomTransitionKey = _customTransitionComboBox.SelectedIndex == 1 ? (ushort)0x0D : (ushort)0x09,
                CredentialStageDelayMs = (int)_stageDelayInput.Value,
                CredentialAutoClearClipboard = _autoClearCheckbox.Checked,
                CredentialAutoClearDelaySeconds = (int)_autoClearDelayInput.Value,

                OverlayEnabled = _overlayCheckbox.Checked,
                OverlayScalePercent = _scaleSlider.Value,
                OverlayMonitorIndex = _monitorComboBox.SelectedIndex >= 0 ? _monitorComboBox.SelectedIndex : 0,
                OverlayToggleEnabled = _toggleHotkeyEnableCheckbox.Checked,
                OverlayToggleModifiers = (int)_recordedToggleModifiers,
                OverlayToggleKey = (int)_recordedToggleKey,

                Theme = (AppTheme)Math.Max(0, _themeComboBox.SelectedIndex),
                AutoStartEnabled = _autostartCheckbox?.Checked ?? current.AutoStartEnabled,
                AutoUpdateCheckEnabled = _autoUpdateCheckbox.Checked,
                SoundFeedbackEnabled = _soundFeedbackCheckbox.Checked,
                EnableDiagnosticLogging = _loggingCheckbox.Checked,

                // Not edited here: keep as they are
                LastUpdateCheckUtc = current.LastUpdateCheckUtc,
                LastRunVersion = current.LastRunVersion
            };

            SettingsSaved?.Invoke(s, resetPosition);
            DialogResult = DialogResult.OK;
            Close();
        }

        // ── Hotkey recorder ─────────────────────────────────────────

        private TextBox BoxFor(RecordingTarget target) => target == RecordingTarget.TriggerHotkey ? _hotkeyBox : _toggleHotkeyBox;
        private Label StatusFor(RecordingTarget target) => target == RecordingTarget.TriggerHotkey ? _hotkeyStatus : _toggleHotkeyStatus;

        private void StartRecording(RecordingTarget target)
        {
            _recordingTarget = target;
            BoxFor(target).Text = "Press the new shortcut…";
            SetStatus(target, "Press the new shortcut. Esc cancels.", Theme.Hint);
        }

        private void SetStatus(RecordingTarget target, string text, Color color)
        {
            var status = StatusFor(target);
            status.Text = text;
            status.ForeColor = color;
        }

        private void StopRecording(bool restoreText)
        {
            var target = _recordingTarget;
            _recordingTarget = RecordingTarget.None;
            if (restoreText)
            {
                BoxFor(target).Text = target == RecordingTarget.TriggerHotkey
                    ? FormatHotkey(_recordedModifiers, _recordedKey)
                    : FormatHotkey(_recordedToggleModifiers, _recordedToggleKey);
                SetStatus(target, HotkeyHintText, Theme.Hint);
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_recordingTarget == RecordingTarget.None)
            {
                return base.ProcessCmdKey(ref msg, keyData);
            }

            var target = _recordingTarget;
            var box = BoxFor(target);
            Keys keyCode = keyData & Keys.KeyCode;
            bool hasCtrlOrAlt = (keyData & (Keys.Control | Keys.Alt)) != 0;

            // Esc cancels; Enter without Ctrl/Alt keeps the old shortcut
            if (keyCode == Keys.Escape || (keyCode == Keys.Enter && !hasCtrlOrAlt && (keyData & Keys.Shift) == 0))
            {
                StopRecording(restoreText: true);
                return true;
            }

            // Tab / Shift+Tab: leave the field like anywhere else in the dialog
            if (keyCode == Keys.Tab && !hasCtrlOrAlt)
            {
                StopRecording(restoreText: true);
                return base.ProcessCmdKey(ref msg, keyData);
            }

            if (keyCode == Keys.ControlKey || keyCode == Keys.ShiftKey || keyCode == Keys.Menu)
            {
                return true; // wait for the actual key
            }

            GlobalHotkey.Modifiers mods = GlobalHotkey.Modifiers.None;
            if ((keyData & Keys.Control) != 0) mods |= GlobalHotkey.Modifiers.Control;
            if ((keyData & Keys.Alt) != 0) mods |= GlobalHotkey.Modifiers.Alt;
            if ((keyData & Keys.Shift) != 0) mods |= GlobalHotkey.Modifiers.Shift;
            Keys baseKey = keyData & ~Keys.Modifiers;

            // TD-66: Require at least one modifier, so keys like F1 or F5 are never taken system-wide
            if (mods == GlobalHotkey.Modifiers.None)
            {
                SetStatus(target, "Use Ctrl, Alt or Shift together with a key.", Theme.Warning);
                return true;
            }

            foreach (var blocked in BlockedCombos)
            {
                if (mods == blocked.mod && baseKey == blocked.key)
                {
                    SetStatus(target, $"{FormatHotkey(mods, baseKey)} is reserved and cannot be used.", Theme.Warning);
                    return true;
                }
            }

            bool isFree = ProbeHotkey(mods, baseKey, target);
            if (target == RecordingTarget.TriggerHotkey)
            {
                _recordedModifiers = mods;
                _recordedKey = baseKey;
            }
            else
            {
                _recordedToggleModifiers = mods;
                _recordedToggleKey = baseKey;
            }

            string combo = FormatHotkey(mods, baseKey);
            box.Text = combo;
            StopRecording(restoreText: false);
            if (isFree)
            {
                SetStatus(target, $"{combo} is available.", Theme.Success);
            }
            else
            {
                SetStatus(target, $"{combo} is already used by another app or by the other ClipTyper shortcut.", Theme.Warning);
            }
            SelectNextControl(box, forward: true, tabStopOnly: true, nested: true, wrap: true);
            return true;
        }

        private bool ProbeHotkey(GlobalHotkey.Modifiers mods, Keys key, RecordingTarget target)
        {
            var currentSettings = SettingsManager.Current;
            if (target == RecordingTarget.TriggerHotkey)
            {
                if (mods == _recordedToggleModifiers && key == _recordedToggleKey)
                {
                    return false;
                }
                if ((int)mods == currentSettings.HotkeyModifiers && (int)key == currentSettings.HotkeyKey)
                {
                    return true; // registered by ClipTyper itself
                }
            }
            else if (target == RecordingTarget.ToggleHotkey)
            {
                if (mods == _recordedModifiers && key == _recordedKey)
                {
                    return false;
                }
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

        public static string FormatHotkey(GlobalHotkey.Modifiers mods, Keys key)
        {
            var parts = new System.Collections.Generic.List<string>();

            if (mods.HasFlag(GlobalHotkey.Modifiers.Control)) parts.Add("Ctrl");
            if (mods.HasFlag(GlobalHotkey.Modifiers.Alt)) parts.Add("Alt");
            if (mods.HasFlag(GlobalHotkey.Modifiers.Shift)) parts.Add("Shift");
            if (mods.HasFlag(GlobalHotkey.Modifiers.Win)) parts.Add("Win");

            if (key != Keys.None)
            {
                parts.Add(key.ToString());
            }

            return parts.Count > 0 ? string.Join(" + ", parts) : "None";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _headerFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
