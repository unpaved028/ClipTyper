using System;
using System.IO;
using System.Threading.Tasks;
using ClipTyper;
using Xunit;

namespace ClipTyper.Tests
{
    [Collection("SettingsState")]
    public class SettingsManagerTest
    {
        [Fact]
        public void AppSettings_CloneAndCopyFrom_CopiesAllFields()
        {
            var original = new AppSettings
            {
                KeystrokeDelayMs = 75,
                NewlineHandling = NewlineMode.ShiftEnter,
                CredentialAutoTypeMode = CredentialMode.Custom,
                CredentialCustomDelimiter = "###",
                CredentialCustomTransitionKey = 0x0D,
                EnableTypingJitter = true,
                TypingJitterRangeMs = 12,
                OverlayScalePercent = 150,
                OverlayToggleEnabled = false,
                EnableDiagnosticLogging = false
            };

            var clone = original.Clone();
            Assert.Equal(75, clone.KeystrokeDelayMs);
            Assert.Equal(NewlineMode.ShiftEnter, clone.NewlineHandling);
            Assert.Equal(CredentialMode.Custom, clone.CredentialAutoTypeMode);
            Assert.Equal("###", clone.CredentialCustomDelimiter);
            Assert.Equal((ushort)0x0D, clone.CredentialCustomTransitionKey);
            Assert.True(clone.EnableTypingJitter);
            Assert.Equal(12, clone.TypingJitterRangeMs);
            Assert.Equal(150, clone.OverlayScalePercent);
            Assert.False(clone.OverlayToggleEnabled);
            Assert.False(clone.EnableDiagnosticLogging);

            var target = new AppSettings();
            target.CopyFrom(original);
            Assert.Equal(75, target.KeystrokeDelayMs);
            Assert.Equal(NewlineMode.ShiftEnter, target.NewlineHandling);
            Assert.Equal("###", target.CredentialCustomDelimiter);
            Assert.False(target.EnableDiagnosticLogging);
        }

        [Fact]
        public void SettingsManager_ThreadSafeSaveAndLoad_DoesNotThrow_AndDoesNotTouchRealSettings()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "ClipTyper_Test_" + Guid.NewGuid().ToString("N"));
            string tempFile = Path.Combine(tempDir, "settings.json");

            try
            {
                SettingsManager.SetSettingsFileForTesting(tempFile);

                Parallel.For(0, 10, i =>
                {
                    var s = SettingsManager.Current.Clone();
                    s.KeystrokeDelayMs = 10 + (i % 80);
                    SettingsManager.Replace(s);
                    var loaded = SettingsManager.Load();
                    Assert.NotNull(loaded);
                });

                Assert.True(File.Exists(tempFile));
            }
            finally
            {
                SettingsManager.SetSettingsFileForTesting(null);
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void SettingsManager_JsonRoundtrip_PreservesEveryField()
        {
            var before = SettingsManager.Current.Clone();
            string tempDir = Path.Combine(Path.GetTempPath(), "ClipTyper_Test_" + Guid.NewGuid().ToString("N"));
            string tempFile = Path.Combine(tempDir, "settings.json");
            var stamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

            var original = new AppSettings
            {
                OverlayEnabled = true,
                OverlaySize = "Medium",
                OverlayScalePercent = 150,
                OverlayMonitorIndex = 1,
                OverlayX = 40,
                OverlayY = 80,
                KeystrokeDelayMs = 40,
                SanitizeInput = false,
                MaxTextLengthThreshold = 8000,
                EnableVkCompatibilityMode = true,
                SoundFeedbackEnabled = true,
                NewlineHandling = NewlineMode.Space,
                EnforcePlainText = true,
                CredentialAutoTypeMode = CredentialMode.TabOnly,
                CredentialCustomDelimiter = "||",
                CredentialCustomTransitionKey = 0x0D,
                CredentialStageDelayMs = 500,
                CredentialAutoClearClipboard = true,
                CredentialAutoClearDelaySeconds = 9,
                EnableTypingJitter = true,
                TypingJitterRangeMs = 12,
                HotkeyModifiers = 3,
                HotkeyKey = 0x59,
                OverlayToggleModifiers = 4,
                OverlayToggleKey = 0x48,
                OverlayToggleEnabled = false,
                AutoStartEnabled = false,
                AutoUpdateCheckEnabled = false,
                LastUpdateCheckUtc = stamp,
                EnableDiagnosticLogging = false
            };

            try
            {
                SettingsManager.SetSettingsFileForTesting(tempFile);
                SettingsManager.Replace(original);
                var loaded = SettingsManager.Load();

                Assert.True(loaded.OverlayEnabled);
                Assert.Equal("Medium", loaded.OverlaySize);
                Assert.Equal(150, loaded.OverlayScalePercent);
                Assert.Equal(1, loaded.OverlayMonitorIndex);
                Assert.Equal(40, loaded.OverlayX);
                Assert.Equal(80, loaded.OverlayY);
                Assert.Equal(40, loaded.KeystrokeDelayMs);
                Assert.False(loaded.SanitizeInput);
                Assert.Equal(8000, loaded.MaxTextLengthThreshold);
                Assert.True(loaded.EnableVkCompatibilityMode);
                Assert.True(loaded.SoundFeedbackEnabled);
                Assert.Equal(NewlineMode.Space, loaded.NewlineHandling);
                Assert.True(loaded.EnforcePlainText);
                Assert.Equal(CredentialMode.TabOnly, loaded.CredentialAutoTypeMode);
                Assert.Equal("||", loaded.CredentialCustomDelimiter);
                Assert.Equal((ushort)0x0D, loaded.CredentialCustomTransitionKey);
                Assert.Equal(500, loaded.CredentialStageDelayMs);
                Assert.True(loaded.CredentialAutoClearClipboard);
                Assert.Equal(9, loaded.CredentialAutoClearDelaySeconds);
                Assert.True(loaded.EnableTypingJitter);
                Assert.Equal(12, loaded.TypingJitterRangeMs);
                Assert.Equal(3, loaded.HotkeyModifiers);
                Assert.Equal(0x59, loaded.HotkeyKey);
                Assert.Equal(4, loaded.OverlayToggleModifiers);
                Assert.Equal(0x48, loaded.OverlayToggleKey);
                Assert.False(loaded.OverlayToggleEnabled);
                Assert.False(loaded.AutoStartEnabled);
                Assert.False(loaded.AutoUpdateCheckEnabled);
                Assert.Equal(stamp, loaded.LastUpdateCheckUtc);
                Assert.False(loaded.EnableDiagnosticLogging);
            }
            finally
            {
                SettingsManager.SetSettingsFileForTesting(null);
                SettingsManager.Current.CopyFrom(before);
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }
    }
}
