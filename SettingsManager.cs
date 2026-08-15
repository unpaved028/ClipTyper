using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClipTyper
{
    public enum NewlineMode
    {
        Enter = 0,
        ShiftEnter = 1,
        Space = 2,
        Ignore = 3
    }

    public enum CredentialMode
    {
        Off = 0,
        AutoDetect = 1,
        TabOnly = 2,
        EnterOnly = 3,
        Custom = 4
    }

    /// <summary>
    /// Application settings persisted to settings.json.
    /// Location depends on deployment mode:
    ///   Portable/Slim → next to the EXE
    ///   Winget        → %AppData%\ClipTyper\
    /// </summary>
    public class AppSettings
    {
        // Overlay
        public bool OverlayEnabled { get; set; } = false;
        public string OverlaySize { get; set; } = "Small";   // "Small" | "Medium" | "Large" (Deprecated, kept for compatibility)
        public int OverlayScalePercent { get; set; } = 100;
        public int OverlayMonitorIndex { get; set; } = 0;
        public int OverlayX { get; set; } = -1;              // -1 = default (right edge)
        public int OverlayY { get; set; } = -1;              // -1 = default (vertically centered)

        // Typing
        public int KeystrokeDelayMs { get; set; } = 25;
        public bool SanitizeInput { get; set; } = true;
        public int MaxTextLengthThreshold { get; set; } = 5000; // 0 = disabled
        public bool EnableVkCompatibilityMode { get; set; } = false;
        public bool SoundFeedbackEnabled { get; set; } = false;

        // Newline & Formatting (v1.6.0)
        public NewlineMode NewlineHandling { get; set; } = NewlineMode.Enter;
        public bool EnforcePlainText { get; set; } = false;

        // Credential Auto-Type (v1.6.0)
        public CredentialMode CredentialAutoTypeMode { get; set; } = CredentialMode.Off;
        public string CredentialCustomDelimiter { get; set; } = "";
        public int CredentialStageDelayMs { get; set; } = 200;
        public bool CredentialAutoClearClipboard { get; set; } = false;
        public int CredentialAutoClearDelaySeconds { get; set; } = 5;

        // Humanized Typing Jitter (v1.6.0)
        public bool EnableTypingJitter { get; set; } = false;
        public int TypingJitterRangeMs { get; set; } = 5;

        // Hotkey  (defaults: Ctrl+Shift = 0x0006, T = 0x54)
        public int HotkeyModifiers { get; set; } = 0x0006;
        public int HotkeyKey { get; set; } = 0x54;

        // Overlay Toggle Hotkey (defaults: Ctrl+Shift = 0x0006, H = 0x48)
        public int OverlayToggleModifiers { get; set; } = 0x0006;
        public int OverlayToggleKey { get; set; } = 0x48;
        public bool OverlayToggleEnabled { get; set; } = true;

        // Autostart (only used in Winget/installed mode)
        public bool AutoStartEnabled { get; set; } = true;

        // Automatic Update Check (v1.4.1+)
        public bool AutoUpdateCheckEnabled { get; set; } = true;
        public DateTime? LastUpdateCheckUtc { get; set; } = null;
    }

    /// <summary>
    /// Source-generated JSON serialization context for AppSettings.
    /// Required when PublishTrimmed is enabled, because reflection-based
    /// serialization is disabled by the IL linker.
    /// </summary>
    [JsonSerializable(typeof(AppSettings))]
    [JsonSourceGenerationOptions(WriteIndented = true)]
    internal partial class AppSettingsJsonContext : JsonSerializerContext { }

    /// <summary>
    /// Manages loading and saving of <see cref="AppSettings"/> to a JSON file.
    /// Detects portable vs. installed mode via a marker file (portable.marker)
    /// next to the executable.
    /// </summary>
    public static class SettingsManager
    {
        private static readonly string ExeDir;
        public static string SettingsDir { get; }
        private static readonly string SettingsFile;

        // JSON options are configured via the source-generated context
        // (AppSettingsJsonContext) which handles WriteIndented = true.

        /// <summary>
        /// True when running in portable mode (portable.marker exists next to EXE).
        /// In portable mode, settings are stored next to the executable.
        /// </summary>
        public static bool IsPortable { get; }

        /// <summary>
        /// The current in-memory settings instance.
        /// </summary>
        public static AppSettings Current { get; private set; } = new();

        static SettingsManager()
        {
            // For single-file published apps, AppContext.BaseDirectory may point
            // to a temp extraction directory. Environment.ProcessPath gives us the
            // actual EXE location, which is what we need for portable mode.
            string exePath = Environment.ProcessPath
                ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                ?? AppContext.BaseDirectory;
            ExeDir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;

            // Check for portable marker file
            string markerPath = Path.Combine(ExeDir, "portable.marker");
            IsPortable = File.Exists(markerPath);

            if (IsPortable)
            {
                // Portable / Slim: store settings next to the EXE
                SettingsDir = ExeDir;
            }
            else
            {
                // Winget / installed: store settings in %AppData%\ClipTyper
                SettingsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ClipTyper");
            }

            SettingsFile = Path.Combine(SettingsDir, "settings.json");
        }

        /// <summary>
        /// Loads settings from disk. If the file is missing or corrupt,
        /// returns (and persists) a fresh default settings instance.
        /// </summary>
        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    string json = File.ReadAllText(SettingsFile);
                    var loaded = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
                    if (loaded != null)
                    {
                        // Clamp keystroke delay to valid range
                        loaded.KeystrokeDelayMs = Math.Clamp(loaded.KeystrokeDelayMs, 5, 100);
                        
                        // Migrate legacy OverlaySize to OverlayScalePercent if applicable
                        if (loaded.OverlayScalePercent == 100 && !string.IsNullOrEmpty(loaded.OverlaySize))
                        {
                            loaded.OverlayScalePercent = loaded.OverlaySize switch
                            {
                                "Small" => 50,
                                "Large" => 200,
                                _ => 100
                            };
                        }
                        
                        // Clamp scale percent to valid range (25% to 200%)
                        loaded.OverlayScalePercent = Math.Clamp(loaded.OverlayScalePercent, 25, 200);

                        Current = loaded;
                        return Current;
                    }
                }
            }
            catch
            {
                // Corrupt file — fall through to defaults
            }

            Current = new AppSettings();
            Save(); // Persist defaults so the file always exists
            return Current;
        }

        /// <summary>
        /// Saves the current settings to disk. Creates the directory if it
        /// does not exist.
        /// </summary>
        public static void Save()
        {
            try
            {
                if (!Directory.Exists(SettingsDir))
                {
                    Directory.CreateDirectory(SettingsDir);
                }

                string json = JsonSerializer.Serialize(Current, AppSettingsJsonContext.Default.AppSettings);
                File.WriteAllText(SettingsFile, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }
    }
}
