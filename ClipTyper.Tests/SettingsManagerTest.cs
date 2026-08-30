using System;
using System.IO;
using System.Threading.Tasks;
using ClipTyper;
using Xunit;

namespace ClipTyper.Tests
{
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
    }
}
