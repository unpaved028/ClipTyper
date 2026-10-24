using System.Runtime.ExceptionServices;
using ClipTyper;
using Xunit;

namespace ClipTyper.Tests
{
    [Collection("SettingsState")]
    public class AboutAndPromptTest
    {
        // Release notes body exactly as the v1.7.0 GitHub release returns it (mixed line endings)
        private const string V170Body =
            "## What's new in v1.7.0\n\n- ClipTyper now runs on the current supported .NET release (.NET 10)\r\n" +
            "- Settings are organized into Typing, Overlay, and App tabs\r\n- Uses **bold** and `code`\r\n";

        [Fact]
        public void FormatReleaseNotes_TurnsMarkdownIntoPlainTextWithWindowsLineBreaks()
        {
            string text = UpdateChecker.FormatReleaseNotes(V170Body);

            Assert.Equal(
                "What's new in v1.7.0\r\n\r\n" +
                "• ClipTyper now runs on the current supported .NET release (.NET 10)\r\n" +
                "• Settings are organized into Typing, Overlay, and App tabs\r\n" +
                "• Uses bold and code",
                text);
        }

        [Theory]
        [InlineData("1.8.0", "1.7.0", true)]
        [InlineData("v1.8.0", "1.7.0", true)]
        [InlineData("1.7.0", "1.7.0", false)]
        [InlineData("1.7.0", "1.8.0", false)]          // local build newer than the latest release
        [InlineData("1.8.0", "1.8.0-beta1", true)]     // beta testers are offered the final release
        [InlineData("1.7.0", "1.8.0-beta1", false)]    // the older final release is not offered to beta testers
        [InlineData("1.8.0", "1.8.0+abc123", false)]   // build metadata does not count
        [InlineData("1.8", "1.8.0", false)]
        [InlineData("not-a-version", "1.7.0", false)]
        public void IsNewerRelease_ComparesReleasesAndPreReleases(string latest, string current, bool expected)
        {
            Assert.Equal(expected, UpdateChecker.IsNewerRelease(latest, current));
        }

        [Fact]
        public void GetCurrentVersion_HasNoBuildMetadata()
        {
            string version = UpdateChecker.GetCurrentVersion();
            Assert.DoesNotContain("+", version, StringComparison.Ordinal);
            Assert.Matches(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$", version);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  \r\n ")]
        public void FormatReleaseNotes_EmptyInput_ReturnsEmpty(string? body)
        {
            Assert.Equal(string.Empty, UpdateChecker.FormatReleaseNotes(body));
        }

        [Fact]
        public void AboutForm_CachedUpdate_ShowsFormattedNotesAndUpgradeAction()
        {
            var update = new UpdateChecker.UpdateCheckResult(true, "1.7.0", "1.8.0", "https://example.invalid/release", V170Body);

            RunOnSta(() =>
            {
                using var form = new AboutForm("Ctrl + Alt + V", overlayActive: false, update);
                form.Opacity = 0;
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-32000, -32000);
                form.Show();

                var all = Descendants(form).ToList();
                Assert.Contains(all.OfType<Label>(), l => l.Visible && l.Text == "Update available: v1.8.0");

                var notes = all.OfType<TextBox>().Single();
                Assert.True(notes.Visible);
                Assert.StartsWith("What's new in v1.7.0", notes.Text, StringComparison.Ordinal);
                Assert.DoesNotContain("##", notes.Text, StringComparison.Ordinal);

                bool wingetVisible = all.OfType<Label>().Any(l => l.Visible && l.Text.StartsWith("winget upgrade", StringComparison.Ordinal));
                bool downloadVisible = all.OfType<LinkLabel>().Any(l => l.Visible && l.Text.StartsWith("Download latest release", StringComparison.Ordinal));
                Assert.Equal(!SettingsManager.IsPortable, wingetVisible);
                Assert.Equal(SettingsManager.IsPortable, downloadVisible);
            });
        }

        [Fact]
        public void WindowsUserPrompt_WithNotifier_ShowsWarningsWithoutBlockingDialog()
        {
            string? seenTitle = null;
            string? seenMessage = null;
            var prompt = new WindowsUserPrompt(notify: (title, message) =>
            {
                seenTitle = title;
                seenMessage = message;
            });

            prompt.ShowWarning("Typing stopped.", "ClipTyper - Typing cancelled");

            Assert.Equal("ClipTyper - Typing cancelled", seenTitle);
            Assert.Equal("Typing stopped.", seenMessage);
        }

        [Fact]
        public async Task TypingService_UnexpectedException_IsCaught_AndResetsTypingState()
        {
            var service = new TypingService(
                keyboardSender: new FakeKeyboardSender(),
                clipboardReader: new ThrowingClipboardReader(),
                userPrompt: new FakeUserPrompt());

            bool? completed = null;
            service.TypingCompleted += ok => completed = ok;

            await service.TriggerClipTypeAsync();

            // The reset is posted to the synchronization context, which may run it asynchronously
            SpinWait.SpinUntil(() => completed != null, TimeSpan.FromSeconds(2));
            Assert.False(service.IsTyping);
            Assert.False(completed);
        }

        private sealed class ThrowingClipboardReader : IClipboardReader
        {
            public string ReadText() => throw new InvalidOperationException("simulated failure");

            public void Clear()
            {
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
