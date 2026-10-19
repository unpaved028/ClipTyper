using System.Reflection;
using System.Runtime.ExceptionServices;
using ClipTyper;
using Xunit;

namespace ClipTyper.Tests
{
    [Collection("SettingsState")]
    public class AppearanceAndNoticesTest
    {
        // ── Startup notices ─────────────────────────────────────────

        [Theory]
        [InlineData(true, null, "1.8.0", "Welcome")]
        [InlineData(true, "1.7.0", "1.8.0", "Welcome")]         // first run wins
        [InlineData(false, null, "1.8.0", "WhatsNew")]          // settings from v1.7.0 or older
        [InlineData(false, "1.7.0", "1.8.0", "WhatsNew")]
        [InlineData(false, "1.7.0", "1.8.0-beta1", "WhatsNew")] // beta testers see it too
        [InlineData(false, "1.8.0-beta1", "1.8.0", "None")]     // not again for the final release
        [InlineData(false, "1.8.0", "1.8.1", "None")]           // patch releases stay quiet
        [InlineData(false, "1.8.0", "1.8.0", "None")]
        [InlineData(false, "1.9.0", "1.8.0", "None")]           // downgrade
        public void StartupNotices_Decide(bool firstRun, string? lastRun, string current, string expected)
        {
            Assert.Equal(expected, StartupNotices.Decide(firstRun, lastRun, current).ToString());
        }

        [Fact]
        public void StartupNotices_WhatsNewText_FitsATrayNotification()
        {
            Assert.InRange(StartupNotices.WhatsNewText.Length, 20, 255);
        }

        // ── Diagnostic log line ─────────────────────────────────────

        [Fact]
        public void DescribeTypingRun_NamesMethodLayoutAndTarget_ButNoText()
        {
            var settings = new AppSettings
            {
                EnableVkCompatibilityMode = true,
                TargetKeyboardLayout = "00000409",
                KeystrokeDelayMs = 40,
                NewlineHandling = NewlineMode.Enter
            };

            string line = TypingService.DescribeTypingRun(settings, isCredential: true, targetProcess: "mstsc");

            Assert.Contains("key presses (VK)", line, StringComparison.Ordinal);
            Assert.Contains("English (United States) [00000409]", line, StringComparison.Ordinal);
            Assert.Contains("delay=40 ms", line, StringComparison.Ordinal);
            Assert.Contains("credentials=2 stages", line, StringComparison.Ordinal);
            Assert.Contains("target=mstsc", line, StringComparison.Ordinal);
        }

        [Fact]
        public void DescribeTypingRun_UnicodeMode_AndUnknownTarget()
        {
            string line = TypingService.DescribeTypingRun(new AppSettings(), isCredential: false, targetProcess: null);

            Assert.Contains("characters (Unicode)", line, StringComparison.Ordinal);
            Assert.DoesNotContain("layout=", line, StringComparison.Ordinal);
            Assert.Contains("target=unknown", line, StringComparison.Ordinal);
        }

        // ── Settings: theme, last run version, first run ────────────

        [Theory]
        [InlineData("{\"Theme\":2}", AppTheme.Dark)]
        [InlineData("{\"Theme\":1}", AppTheme.Light)]
        [InlineData("{\"Theme\":99}", AppTheme.System)]   // invalid value
        [InlineData("{}", AppTheme.System)]               // older settings file
        public void SettingsManager_Load_ValidatesTheme(string json, AppTheme expected)
        {
            WithSettingsFile(json, () => Assert.Equal(expected, SettingsManager.Load().Theme));
        }

        [Fact]
        public void SettingsManager_IsFirstRun_OnlyWithoutSettingsFile()
        {
            WithSettingsFile(null, () =>
            {
                SettingsManager.Load();
                Assert.True(SettingsManager.IsFirstRun);
                SettingsManager.Load(); // the first Load wrote the defaults
                Assert.False(SettingsManager.IsFirstRun);
            });
        }

        [Fact]
        public void SettingsManager_ThemeAndLastRunVersion_RoundTrip()
        {
            WithSettingsFile("{}", () =>
            {
                SettingsManager.Load();
                SettingsManager.Current.Theme = AppTheme.Light;
                SettingsManager.Current.LastRunVersion = "1.8.0-beta1";
                SettingsManager.Save();

                var loaded = SettingsManager.Load();
                Assert.Equal(AppTheme.Light, loaded.Theme);
                Assert.Equal("1.8.0-beta1", loaded.LastRunVersion);
            });
        }

        // ── Icon and menu style ─────────────────────────────────────

        [Theory]
        [InlineData("ClipTyper.icon_app.ico", 16)]
        [InlineData("ClipTyper.icon_app.ico", 20)]
        [InlineData("ClipTyper.icon_app.ico", 24)]
        [InlineData("ClipTyper.icon_app.ico", 32)]
        [InlineData("ClipTyper.icon_app.ico", 48)]
        [InlineData("ClipTyper.icon_overlay.ico", 128)]
        [InlineData("ClipTyper.icon_large.ico", 256)]   // the overlay's large image
        public void EmbeddedIcons_ProvideAnExactFrameForEverySizeTheAppRequests(string resource, int size)
        {
            using var stream = typeof(SettingsForm).Assembly.GetManifestResourceStream(resource);
            Assert.NotNull(stream);
            using var icon = new Icon(stream, size, size);
            Assert.Equal(size, icon.Width);
            Assert.Equal(size, icon.Height);
        }

        [Fact]
        public void ModernMenuRenderer_GivesMenuItemsRoomierRows_IncludingLaterSubmenuItems()
        {
            RunOnSta(() =>
            {
                using var menu = new ContextMenuStrip { Renderer = ModernMenuRenderer.Instance };
                var item = new ToolStripMenuItem("Keyboard layout");
                menu.Items.Add(item);
                item.DropDownItems.Add("English (United States)");

                Assert.Equal(new Padding(4, 5, 4, 5), item.Padding);
                Assert.Equal(new Padding(4, 5, 4, 5), item.DropDownItems[0].Padding);
                Assert.Same(ModernMenuRenderer.Instance, item.DropDown.Renderer);
            });
        }

        // ── Task dialog question ────────────────────────────────────

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ConfirmationPage_DefaultButtonIsOneOfItsButtons(bool confirmIsDefault)
        {
            var page = Dialogs.BuildConfirmationPage(
                new ConfirmationRequest("Caption", "Heading", "Text", "Type anyway", confirmIsDefault),
                out var confirm);

            Assert.Equal(2, page.Buttons.Count);
            Assert.Same(confirm, page.Buttons[0]);
            Assert.Contains(page.DefaultButton!, page.Buttons);
            Assert.Equal(confirmIsDefault, ReferenceEquals(page.DefaultButton, confirm));
            Assert.Equal("Type anyway", confirm.Text);
            Assert.Equal("Cancel", page.Buttons[1].Text); // English, not the Windows-translated standard button
        }

        [Fact]
        public void ConfirmationPage_DetailsStayCollapsedBehindTheirButton()
        {
            var withDetails = Dialogs.BuildConfirmationPage(
                new ConfirmationRequest("C", "H", "T", "OK", Details: "ä  §", DetailsButtonText: "Show characters"), out _);
            var withoutDetails = Dialogs.BuildConfirmationPage(new ConfirmationRequest("C", "H", "T", "OK"), out _);

            Assert.NotNull(withDetails.Expander);
            Assert.False(withDetails.Expander!.Expanded);
            Assert.Equal("Show characters", withDetails.Expander.CollapsedButtonText);
            Assert.Equal("Hide characters", withDetails.Expander.ExpandedButtonText);
            Assert.DoesNotContain("ä", withDetails.Text, StringComparison.Ordinal);
            // WinForms creates an empty expander on access; without text it is not shown
            Assert.True(string.IsNullOrEmpty(withoutDetails.Expander?.Text));
        }

        // ── Helpers ─────────────────────────────────────────────────

        private static void WithSettingsFile(string? json, Action action)
        {
            var before = SettingsManager.Current.Clone();
            string tempDir = Path.Combine(Path.GetTempPath(), "ClipTyper_Test_" + Guid.NewGuid().ToString("N"));
            string tempFile = Path.Combine(tempDir, "settings.json");
            try
            {
                Directory.CreateDirectory(tempDir);
                if (json != null)
                {
                    File.WriteAllText(tempFile, json);
                }
                SettingsManager.SetSettingsFileForTesting(tempFile);
                action();
            }
            finally
            {
                SettingsManager.SetSettingsFileForTesting(null);
                SettingsManager.Current.CopyFrom(before);
                try { Directory.Delete(tempDir, true); } catch { }
            }
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
    }
}
