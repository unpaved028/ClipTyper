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
        public void SettingsForm_HasTypingOverlayAndAppTabs_WithControlsOnTheExpectedPage()
        {
            RunOnSta(() =>
            {
                using var form = new SettingsForm();
                form.CreateControl();

                var tabs = form.Controls.OfType<TabControl>().Single();
                Assert.Equal(new[] { "Typing", "Overlay", "App" }, tabs.TabPages.Cast<TabPage>().Select(p => p.Text).ToArray());

                var typing = tabs.TabPages[0];
                var overlay = tabs.TabPages[1];
                var app = tabs.TabPages[2];

                Assert.Contains(Descendants(typing).OfType<CheckBox>(), c => c.Text.Contains("VK Compatibility", StringComparison.Ordinal));
                Assert.DoesNotContain(Descendants(overlay).OfType<CheckBox>(), c => c.Text.Contains("VK Compatibility", StringComparison.Ordinal));
                Assert.DoesNotContain(Descendants(app).OfType<CheckBox>(), c => c.Text.Contains("VK Compatibility", StringComparison.Ordinal));

                Assert.Single(Descendants(overlay).OfType<TrackBar>());
                Assert.Contains(Descendants(overlay).OfType<Button>(), b => b.Text == "Reset Position");

                Assert.Contains(Descendants(app).OfType<ComboBox>(), c => c.Items.Contains("Custom Delimiter"));
                Assert.Contains(Descendants(app).OfType<CheckBox>(), c => c.Text.Contains("sound signal", StringComparison.Ordinal));
                Assert.Contains(Descendants(app).OfType<CheckBox>(), c => c.Text.Contains("diagnostic logging", StringComparison.Ordinal));
                Assert.Contains(Descendants(app).OfType<CheckBox>(), c => c.Text.Contains("check for updates", StringComparison.Ordinal));

                var autostart = Descendants(form).OfType<CheckBox>().Where(c => c.Text.Contains("Windows startup", StringComparison.Ordinal)).ToList();
                if (SettingsManager.IsPortable)
                {
                    Assert.Empty(autostart);
                }
                else
                {
                    var box = Assert.Single(autostart);
                    Assert.Equal(app, box.Parent);
                }

                Assert.Equal(form, form.Controls.OfType<Button>().Single(b => b.Text == "Save").Parent);
                Assert.Equal(form, form.Controls.OfType<Button>().Single(b => b.Text == "Cancel").Parent);
            });
        }

        [Fact]
        public void SettingsForm_EnablesCredentialAndAutoClearFields_OnlyWhenSelected()
        {
            RunOnSta(() =>
            {
                using var form = new SettingsForm();
                ShowWithoutTakingFocus(form);
                form.Controls.OfType<TabControl>().Single().SelectedIndex = 2;

                var mode = Descendants(form).OfType<ComboBox>().Single(c => c.Items.Contains("Custom Delimiter"));
                var delimiter = Descendants(form).OfType<TextBox>().Single(t => t.Parent is GroupBox);
                var transition = Descendants(form).OfType<ComboBox>().Single(c => c.Items.Contains("Send Tab"));
                var autoClear = Descendants(form).OfType<CheckBox>().Single(c => c.Text.Contains("Auto-clear", StringComparison.Ordinal));
                var clearDelay = Descendants(form).OfType<NumericUpDown>().Single(n => n.Maximum == 60);
                var warning = Descendants(form).OfType<Label>().Single(l => l.Text.Contains("Erases credentials", StringComparison.Ordinal));

                mode.SelectedIndex = 0;
                Assert.False(delimiter.Enabled);
                Assert.False(transition.Enabled);

                mode.SelectedIndex = 4;
                Assert.True(delimiter.Enabled);
                Assert.True(transition.Enabled);

                autoClear.Checked = false;
                Assert.False(clearDelay.Enabled);
                Assert.False(warning.Visible);

                autoClear.Checked = true;
                Assert.True(clearDelay.Enabled);
                Assert.True(warning.Visible);
            });
        }

        [Fact]
        public void SettingsForm_ScaleSlider_RaisesLiveScaleChanged()
        {
            RunOnSta(() =>
            {
                using var form = new SettingsForm();
                form.CreateControl();

                int? seen = null;
                form.LiveScaleChanged += value => seen = value;

                var slider = Descendants(form.Controls.OfType<TabControl>().Single().TabPages[1]).OfType<TrackBar>().Single();
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
                current.EnforcePlainText = true;
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
                current.SoundFeedbackEnabled = true;
                current.OverlayEnabled = true;
                current.OverlayScalePercent = 150;
                current.OverlayMonitorIndex = 0;
                current.AutoStartEnabled = false;
                current.AutoUpdateCheckEnabled = false;
                current.LastUpdateCheckUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
                current.EnableDiagnosticLogging = false;

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
                Assert.True(saved.EnforcePlainText);
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
                Assert.True(saved.SoundFeedbackEnabled);
                Assert.True(saved.OverlayEnabled);
                Assert.Equal(150, saved.OverlayScalePercent);
                Assert.Equal(0, saved.OverlayMonitorIndex);
                Assert.False(saved.AutoStartEnabled);
                Assert.False(saved.AutoUpdateCheckEnabled);
                Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), saved.LastUpdateCheckUtc);
                Assert.False(saved.EnableDiagnosticLogging);
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
