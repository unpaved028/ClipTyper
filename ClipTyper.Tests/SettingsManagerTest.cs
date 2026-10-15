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
                TargetKeyboardLayout = "00000407",
                SoundFeedbackEnabled = true,
                NewlineHandling = NewlineMode.Space,
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
                Assert.Equal("00000407", loaded.TargetKeyboardLayout);
                Assert.True(loaded.SoundFeedbackEnabled);
                Assert.Equal(NewlineMode.Space, loaded.NewlineHandling);
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

        [Theory]
        // Explicit 100% must survive a restart even though OverlaySize is still written as "Small"
        [InlineData("{\"OverlaySize\":\"Small\",\"OverlayScalePercent\":100}", 100)]
        [InlineData("{\"OverlaySize\":\"Large\",\"OverlayScalePercent\":75}", 75)]
        // Legacy files without OverlayScalePercent are still migrated
        [InlineData("{\"OverlaySize\":\"Small\"}", 50)]
        [InlineData("{\"OverlaySize\":\"Medium\"}", 100)]
        [InlineData("{\"OverlaySize\":\"Large\"}", 200)]
        // Neither property in the file (e.g. Scoop's initial "{}"): default 100 %, no migration
        [InlineData("{}", 100)]
        public void SettingsManager_Load_MigratesLegacyOverlaySizeOnlyWhenScaleIsMissing(string json, int expectedScale)
        {
            var before = SettingsManager.Current.Clone();
            string tempDir = Path.Combine(Path.GetTempPath(), "ClipTyper_Test_" + Guid.NewGuid().ToString("N"));
            string tempFile = Path.Combine(tempDir, "settings.json");

            try
            {
                Directory.CreateDirectory(tempDir);
                File.WriteAllText(tempFile, json);
                SettingsManager.SetSettingsFileForTesting(tempFile);

                Assert.Equal(expectedScale, SettingsManager.Load().OverlayScalePercent);
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

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

        [Fact]
        public void SettingsManager_Save_KeepsHardLinkIntact_ForScoopPersist()
        {
            var before = SettingsManager.Current.Clone();
            string tempDir = Path.Combine(Path.GetTempPath(), "ClipTyper_Test_" + Guid.NewGuid().ToString("N"));
            string persistFile = Path.Combine(tempDir, "persist", "settings.json");
            string appFile = Path.Combine(tempDir, "app", "settings.json");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(persistFile)!);
                Directory.CreateDirectory(Path.GetDirectoryName(appFile)!);
                File.WriteAllText(persistFile, "{}");
                Assert.True(CreateHardLinkW(appFile, persistFile, IntPtr.Zero), "could not create hard link");

                SettingsManager.SetSettingsFileForTesting(appFile);
                SettingsManager.Load();
                SettingsManager.Current.KeystrokeDelayMs = 77;
                SettingsManager.Save();

                // The persisted copy (other link) sees the change, and the link still exists
                Assert.Contains("\"KeystrokeDelayMs\": 77", File.ReadAllText(persistFile), StringComparison.Ordinal);
                Assert.Equal(2, NativeMethods.GetHardLinkCount(appFile));
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

        [Theory]
        // Saved by a DPI-unaware version: position must be migrated once
        [InlineData("{\"OverlayX\":2500,\"OverlayY\":600}", true)]
        // Default position: nothing to migrate
        [InlineData("{\"OverlayX\":-1,\"OverlayY\":-1}", false)]
        // Saved by a DPI-aware version
        [InlineData("{\"OverlayX\":2500,\"OverlayY\":600,\"OverlayPositionDpiAware\":true}", false)]
        public void SettingsManager_Load_FlagsOverlayPositionsFromDpiUnawareVersions(string json, bool expectMigration)
        {
            var before = SettingsManager.Current.Clone();
            string tempDir = Path.Combine(Path.GetTempPath(), "ClipTyper_Test_" + Guid.NewGuid().ToString("N"));
            string tempFile = Path.Combine(tempDir, "settings.json");

            try
            {
                Directory.CreateDirectory(tempDir);
                File.WriteAllText(tempFile, json);
                SettingsManager.SetSettingsFileForTesting(tempFile);

                var loaded = SettingsManager.Load();

                Assert.Equal(!expectMigration, loaded.OverlayPositionDpiAware);
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

        [Theory]
        [InlineData("{\"TargetKeyboardLayout\":\"0000040C\"}", "0000040C")]
        [InlineData("{\"TargetKeyboardLayout\":\"US\"}", "")]
        [InlineData("{\"TargetKeyboardLayout\":null}", "")]
        [InlineData("{}", "")]
        public void SettingsManager_Load_ValidatesTargetKeyboardLayout(string json, string expected)
        {
            var before = SettingsManager.Current.Clone();
            string tempDir = Path.Combine(Path.GetTempPath(), "ClipTyper_Test_" + Guid.NewGuid().ToString("N"));
            string tempFile = Path.Combine(tempDir, "settings.json");

            try
            {
                Directory.CreateDirectory(tempDir);
                File.WriteAllText(tempFile, json);
                SettingsManager.SetSettingsFileForTesting(tempFile);

                Assert.Equal(expected, SettingsManager.Load().TargetKeyboardLayout);
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

        [Theory]
        [InlineData(@"C:\Tools\ClipTyper-Portable.exe", true)]
        [InlineData(@"C:\Tools\ClipTyper-Slim.exe", true)]
        [InlineData(@"C:\Users\me\Downloads\ClipTyper-Portable (1).exe", true)]
        [InlineData(@"C:\Tools\cliptyper-portable.EXE", true)]
        [InlineData(@"C:\Users\me\AppData\Local\Programs\ClipTyper\ClipTyper.exe", false)]
        public void SettingsManager_IsPortableExeName_RecognizesStandaloneReleaseExes(string exePath, bool expected)
        {
            Assert.Equal(expected, SettingsManager.IsPortableExeName(exePath));
        }
    }
}
