using System.Runtime.ExceptionServices;
using System.Threading;
using ClipTyper;
using Xunit;

namespace ClipTyper.Tests
{
    [CollectionDefinition("SettingsState")]
    public class SettingsStateCollection
    {
    }

    [Collection("SettingsState")]
    public class SettingsFormTest
    {
        [Fact]
        public void SettingsForm_HasFiveTabs_WithControlsOnTheExpectedPage()
        {
            RunOnSta(() =>
            {
                using var form = new SettingsForm();
                form.CreateControl();

                Assert.Equal("ClipTyper Settings", form.Text);
                var tabs = form.Controls.OfType<TabControl>().Single();
                Assert.Equal(new[] { "Typing", "Keyboard", "Credentials", "Overlay", "General" },
                    tabs.TabPages.Cast<TabPage>().Select(p => p.Text).ToArray());

                var typing = tabs.TabPages[0];
                var keyboard = tabs.TabPages[1];
                var credentials = tabs.TabPages[2];
                var overlay = tabs.TabPages[3];
                var general = tabs.TabPages[4];

                // Typing: hotkey, delay slider, line breaks
                Assert.Contains(Descendants(typing).OfType<TextBox>(), t => t.AccessibleName == "Hotkey to type the clipboard");
                Assert.Single(Descendants(typing).OfType<TrackBar>());
                Assert.Contains(Descendants(typing).OfType<ComboBox>(), c => c.Items.Contains("Enter (default)"));

                // Keyboard: the two typing methods and the console layout
                var radios = Descendants(keyboard).OfType<RadioButton>().Select(r => r.Text).ToArray();
                Assert.Equal(new[] { "Characters (Unicode)", "Key presses (VK Compatibility Mode)" }, radios);
                Assert.Contains(Descendants(keyboard).OfType<ComboBox>(), c => c.Items.OfType<KeyboardLayoutOption>().Any());

                // Credentials
                Assert.Contains(Descendants(credentials).OfType<ComboBox>(), c => c.Items.Contains("Custom delimiter"));
                Assert.Contains(Descendants(credentials).OfType<CheckBox>(), c => c.Text.Contains("Clear the clipboard", StringComparison.Ordinal));

                // Overlay
                Assert.Single(Descendants(overlay).OfType<TrackBar>());
                Assert.Contains(Descendants(overlay).OfType<Button>(), b => b.Text == "Reset position");
                Assert.Contains(Descendants(overlay).OfType<TextBox>(), t => t.AccessibleName == "Shortcut to show or hide the overlay");

                // General: theme, updates, sound, logging, open folder
                Assert.Contains(Descendants(general).OfType<ComboBox>(), c => c.Items.Contains("Auto (System)"));
                Assert.Contains(Descendants(general).OfType<CheckBox>(), c => c.Text.Contains("updates", StringComparison.Ordinal));
                Assert.Contains(Descendants(general).OfType<CheckBox>(), c => c.Text.Contains("sound", StringComparison.Ordinal));
                Assert.Contains(Descendants(general).OfType<CheckBox>(), c => c.Text.Contains("diagnostic log", StringComparison.Ordinal));
                Assert.Contains(Descendants(general).OfType<Button>(), b => b.Text == "Open folder");

                var autostart = Descendants(form).OfType<CheckBox>().Where(c => c.Text.Contains("with Windows", StringComparison.Ordinal)).ToList();
                if (SettingsManager.IsPortable)
                {
                    Assert.Empty(autostart);
                }
                else
                {
                    Assert.Equal(general, Assert.Single(autostart).Parent);
                }

                Assert.Equal(form, form.Controls.OfType<Button>().Single(b => b.Text == "Save").Parent);
                Assert.Equal(form, form.Controls.OfType<Button>().Single(b => b.Text == "Cancel").Parent);
            });
        }

        [Fact]
        public void SettingsForm_NoLabelLosesAnAmpersand()
        {
            RunOnSta(() =>
            {
                using var form = new SettingsForm();
                form.CreateControl();
                // A single '&' in a Label/CheckBox text is swallowed as a mnemonic unless UseMnemonic is off
                foreach (var label in Descendants(form).OfType<Label>().Where(l => l.Text.Contains('&')))
                {
                    Assert.False(label.UseMnemonic, label.Text);
                }
                foreach (var box in Descendants(form).OfType<ButtonBase>().Where(c => c.Text.Contains('&')))
                {
                    Assert.Contains("&&", box.Text, StringComparison.Ordinal);
                }
            });
        }

        [Fact]
        public void SettingsForm_EnablesDependentFields_OnlyWhenSelected()
        {
            RunOnSta(() =>
            {
                using var form = new SettingsForm();
                ShowWithoutTakingFocus(form);

                var mode = Descendants(form).OfType<ComboBox>().Single(c => c.Items.Contains("Custom delimiter"));
                var delimiter = Descendants(form).OfType<TextBox>().Single(t => t.AccessibleName == "Custom delimiter");
                var transition = Descendants(form).OfType<ComboBox>().Single(c => c.Items.Contains("then Tab"));
                var autoClear = Descendants(form).OfType<CheckBox>().Single(c => c.Text.Contains("Clear the clipboard", StringComparison.Ordinal));
                var clearDelay = Descendants(form).OfType<NumericUpDown>().Single(n => n.Maximum == 60);

                mode.SelectedIndex = 0;
                Assert.False(delimiter.Enabled);
                Assert.False(transition.Enabled);
                mode.SelectedIndex = 4;
                Assert.True(delimiter.Enabled);
                Assert.True(transition.Enabled);

                autoClear.Checked = false;
                Assert.False(clearDelay.Enabled);
                autoClear.Checked = true;
                Assert.True(clearDelay.Enabled);

                // Overlay options follow "Show the overlay button"
                var overlayPage = form.Controls.OfType<TabControl>().Single().TabPages[3];
                var showOverlay = Descendants(overlayPage).OfType<CheckBox>().Single(c => c.Text == "Show the overlay button");
                var size = Descendants(overlayPage).OfType<TrackBar>().Single();
                var reset = Descendants(overlayPage).OfType<Button>().Single(b => b.Text == "Reset position");
                showOverlay.Checked = false;
                Assert.False(size.Enabled);
                Assert.False(reset.Enabled);
                showOverlay.Checked = true;
                Assert.True(size.Enabled);
                Assert.True(reset.Enabled);
            });
        }

        [Fact]
        public void SettingsForm_OverlaySizeSlider_RaisesLiveScaleChanged()
        {
            RunOnSta(() =>
            {
                using var form = new SettingsForm();
                form.CreateControl();

                int? seen = null;
                form.LiveScaleChanged += value => seen = value;

                var slider = Descendants(form.Controls.OfType<TabControl>().Single().TabPages[3]).OfType<TrackBar>().Single();
                int next = slider.Value >= slider.Maximum ? slider.Minimum : slider.Value + slider.LargeChange;
                slider.Value = next;

                Assert.Equal(next, seen);
            });
        }

        [Fact]
        public void SettingsForm_Save_RaisesEveryDialogField_WithoutWritingSettings()
        {
            var before = SettingsManager.Current.Clone();
            try
            {
                var current = SettingsManager.Current;
                current.HotkeyModifiers = (int)(GlobalHotkey.Modifiers.Control | GlobalHotkey.Modifiers.Alt);
                current.HotkeyKey = (int)Keys.Y;
                current.OverlayToggleModifiers = (int)GlobalHotkey.Modifiers.Shift;
                current.OverlayToggleKey = (int)Keys.H;
                current.OverlayToggleEnabled = false;
                current.KeystrokeDelayMs = 40;
                current.NewlineHandling = NewlineMode.ShiftEnter;
                current.EnableTypingJitter = true;
                current.TypingJitterRangeMs = 12;
                current.CredentialAutoTypeMode = CredentialMode.Custom;
                current.CredentialCustomDelimiter = "||";
                current.CredentialCustomTransitionKey = 0x0D;
                current.CredentialStageDelayMs = 500;
                current.CredentialAutoClearClipboard = true;
                current.CredentialAutoClearDelaySeconds = 9;
                current.SanitizeInput = false;
                current.MaxTextLengthThreshold = 8000;
                current.EnableVkCompatibilityMode = true;
                current.TargetKeyboardLayout = "0000040C";
                current.SoundFeedbackEnabled = true;
                current.OverlayEnabled = true;
                current.OverlayScalePercent = 150;
                current.OverlayMonitorIndex = 0;
                current.AutoStartEnabled = false;
                current.AutoUpdateCheckEnabled = false;
                current.LastUpdateCheckUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
                current.EnableDiagnosticLogging = false;
                current.Theme = AppTheme.Dark;
                current.LastRunVersion = "1.8.0";

                AppSettings? saved = null;
                bool resetPosition = true;

                RunOnSta(() =>
                {
                    using var form = new SettingsForm();
                    ShowWithoutTakingFocus(form);
                    form.SettingsSaved += (settings, reset) =>
                    {
                        saved = settings;
                        resetPosition = reset;
                    };

                    form.Controls.OfType<Button>().Single(b => b.Text == "Save").PerformClick();
                });

                Assert.NotNull(saved);
                Assert.False(resetPosition);
                Assert.Equal((int)(GlobalHotkey.Modifiers.Control | GlobalHotkey.Modifiers.Alt), saved.HotkeyModifiers);
                Assert.Equal((int)Keys.Y, saved.HotkeyKey);
                Assert.Equal((int)GlobalHotkey.Modifiers.Shift, saved.OverlayToggleModifiers);
                Assert.Equal((int)Keys.H, saved.OverlayToggleKey);
                Assert.False(saved.OverlayToggleEnabled);
                Assert.Equal(40, saved.KeystrokeDelayMs);
                Assert.Equal(NewlineMode.ShiftEnter, saved.NewlineHandling);
                Assert.True(saved.EnableTypingJitter);
                Assert.Equal(12, saved.TypingJitterRangeMs);
                Assert.Equal(CredentialMode.Custom, saved.CredentialAutoTypeMode);
                Assert.Equal("||", saved.CredentialCustomDelimiter);
                Assert.Equal((ushort)0x0D, saved.CredentialCustomTransitionKey);
                Assert.Equal(500, saved.CredentialStageDelayMs);
                Assert.True(saved.CredentialAutoClearClipboard);
                Assert.Equal(9, saved.CredentialAutoClearDelaySeconds);
                Assert.False(saved.SanitizeInput);
                Assert.Equal(8000, saved.MaxTextLengthThreshold);
                Assert.True(saved.EnableVkCompatibilityMode);
                Assert.Equal("0000040C", saved.TargetKeyboardLayout);
                Assert.True(saved.SoundFeedbackEnabled);
                Assert.True(saved.OverlayEnabled);
                Assert.Equal(150, saved.OverlayScalePercent);
                Assert.Equal(0, saved.OverlayMonitorIndex);
                Assert.False(saved.AutoStartEnabled);
                Assert.False(saved.AutoUpdateCheckEnabled);
                Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), saved.LastUpdateCheckUtc);
                Assert.False(saved.EnableDiagnosticLogging);
                Assert.Equal(AppTheme.Dark, saved.Theme);
                Assert.Equal("1.8.0", saved.LastRunVersion);
            }
            finally
            {
                SettingsManager.Current.CopyFrom(before);
            }
        }

        [Theory]
        [InlineData("", "Same as this PC")]
        [InlineData("00000409", "English (United States)")]
        [InlineData("00001009", "Keyboard layout 00001009")] // valid KLID outside the curated list
        public void SettingsForm_ConsoleLayout_ShowsStoredLayout_SavesItBack_AndFollowsTypingMethod(string klid, string expectedText)
        {
            var before = SettingsManager.Current.Clone();
            try
            {
                SettingsManager.Current.EnableVkCompatibilityMode = false;
                SettingsManager.Current.TargetKeyboardLayout = klid;
                SettingsManager.Current.CredentialAutoTypeMode = CredentialMode.Off;

                AppSettings? saved = null;
                RunOnSta(() =>
                {
                    using var form = new SettingsForm();
                    ShowWithoutTakingFocus(form);
                    form.SettingsSaved += (settings, _) => saved = settings;

                    var keyboard = form.Controls.OfType<TabControl>().Single().TabPages[1];
                    var keyPresses = Descendants(keyboard).OfType<RadioButton>().Single(r => r.Text.StartsWith("Key presses", StringComparison.Ordinal));
                    var target = Descendants(keyboard).OfType<ComboBox>().Single(c => c.Items.OfType<KeyboardLayoutOption>().Any());

                    Assert.Equal(expectedText, target.Text);
                    Assert.False(target.Enabled);
                    keyPresses.Checked = true;
                    Assert.True(target.Enabled);

                    form.Controls.OfType<Button>().Single(b => b.Text == "Save").PerformClick();
                });

                Assert.NotNull(saved);
                Assert.Equal(klid, saved.TargetKeyboardLayout);
                Assert.True(saved.EnableVkCompatibilityMode);
            }
            finally
            {
                SettingsManager.Current.CopyFrom(before);
            }
        }

        [Theory]
        [InlineData(AppTheme.System, "Auto (System)")]
        [InlineData(AppTheme.Light, "Light")]
        [InlineData(AppTheme.Dark, "Dark")]
        public void SettingsForm_Theme_ShowsAndSavesTheChoice(AppTheme theme, string expectedText)
        {
            var before = SettingsManager.Current.Clone();
            try
            {
                SettingsManager.Current.Theme = theme;
                SettingsManager.Current.CredentialAutoTypeMode = CredentialMode.Off;

                AppSettings? saved = null;
                RunOnSta(() =>
                {
                    using var form = new SettingsForm();
                    ShowWithoutTakingFocus(form);
                    form.SettingsSaved += (settings, _) => saved = settings;

                    var general = form.Controls.OfType<TabControl>().Single().TabPages[4];
                    var themeBox = Descendants(general).OfType<ComboBox>().Single(c => c.Items.Contains("Auto (System)"));
                    Assert.Equal(expectedText, themeBox.Text);

                    form.Controls.OfType<Button>().Single(b => b.Text == "Save").PerformClick();
                });

                Assert.Equal(theme, saved!.Theme);
            }
            finally
            {
                SettingsManager.Current.CopyFrom(before);
            }
        }

        private static void ShowWithoutTakingFocus(Form form)
        {
            form.Opacity = 0;
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.Show();
        }

        private static void RunOnSta(Action action)
        {
            Exception? caught = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    caught = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (caught != null)
            {
                ExceptionDispatchInfo.Capture(caught).Throw();
            }
        }

        private static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (var nested in Descendants(child))
                {
                    yield return nested;
                }
            }
        }
    }
}
