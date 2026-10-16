using System;
using System.IO;
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
        public bool OverlayEnabled { get; set; }
        public string OverlaySize { get; set; } = "Small";   // "Small" | "Medium" | "Large" (Deprecated, kept for compatibility)
        public int OverlayScalePercent { get; set; } = 100;
        public int OverlayMonitorIndex { get; set; }
        public int OverlayX { get; set; } = -1;              // -1 = default (right edge)
        public int OverlayY { get; set; } = -1;              // -1 = default (vertically centered)
        // false: OverlayX/Y were saved by a DPI-unaware version (logical pixels) and are migrated
        // once when the overlay is shown; stays false until then, so a save in between keeps it.
        public bool OverlayPositionDpiAware { get; set; } = true;

        // Typing
        public int KeystrokeDelayMs { get; set; } = 25;
        public bool SanitizeInput { get; set; } = true;
        public int MaxTextLengthThreshold { get; set; } = 5000; // 0 = disabled
        public bool EnableVkCompatibilityMode { get; set; }
        public string TargetKeyboardLayout { get; set; } = KeyboardLayouts.SameAsThisPc; // KLID used in VK mode, "" = same as this PC
        public bool SoundFeedbackEnabled { get; set; }

        // Newline handling (v1.6.0)
        public NewlineMode NewlineHandling { get; set; } = NewlineMode.Enter;

        // Credential Auto-Type (v1.6.0 / v1.6.1)
        public CredentialMode CredentialAutoTypeMode { get; set; }
        public string CredentialCustomDelimiter { get; set; } = "";
        public ushort CredentialCustomTransitionKey { get; set; } = 0x09; // 0x09 = Tab, 0x0D = Enter (TD-23)
        public int CredentialStageDelayMs { get; set; } = 200;
        public bool CredentialAutoClearClipboard { get; set; }
        public int CredentialAutoClearDelaySeconds { get; set; } = 5;

        // Humanized Typing Jitter (v1.6.0)
        public bool EnableTypingJitter { get; set; }
        public int TypingJitterRangeMs { get; set; } = 5;

        // Hotkey  (defaults: Ctrl+Alt = 0x0003, V = 0x56). Up to v1.7.0 the default was
        // Ctrl+Shift+T; existing settings.json files keep whatever they have stored.
        public int HotkeyModifiers { get; set; } = 0x0003;
        public int HotkeyKey { get; set; } = 0x56;

        // Overlay Toggle Hotkey (defaults: Ctrl+Shift = 0x0006, H = 0x48)
        public int OverlayToggleModifiers { get; set; } = 0x0006;
        public int OverlayToggleKey { get; set; } = 0x48;
        public bool OverlayToggleEnabled { get; set; } = true;

        // Autostart (only used in Winget/installed mode)
        public bool AutoStartEnabled { get; set; } = true;

        // Automatic Update Check (v1.4.1+)
        public bool AutoUpdateCheckEnabled { get; set; } = true;
        public DateTime? LastUpdateCheckUtc { get; set; }

        // Diagnostic Logging (v1.6.1 / TD-78)
        public bool EnableDiagnosticLogging { get; set; } = true;

        /// <summary>
        /// Creates a shallow clone of the current settings.
        /// </summary>
        public AppSettings Clone()
        {
            return (AppSettings)MemberwiseClone();
        }

        /// <summary>
        /// Copies all properties from the specified source instance.
        /// </summary>
        public void CopyFrom(AppSettings source)
        {
            OverlayEnabled = source.OverlayEnabled;
            OverlaySize = source.OverlaySize;
            OverlayScalePercent = source.OverlayScalePercent;
            OverlayMonitorIndex = source.OverlayMonitorIndex;
            OverlayX = source.OverlayX;
            OverlayY = source.OverlayY;
            OverlayPositionDpiAware = source.OverlayPositionDpiAware;

            KeystrokeDelayMs = source.KeystrokeDelayMs;
            SanitizeInput = source.SanitizeInput;
            MaxTextLengthThreshold = source.MaxTextLengthThreshold;
            EnableVkCompatibilityMode = source.EnableVkCompatibilityMode;
            TargetKeyboardLayout = source.TargetKeyboardLayout;
            SoundFeedbackEnabled = source.SoundFeedbackEnabled;

            NewlineHandling = source.NewlineHandling;

            CredentialAutoTypeMode = source.CredentialAutoTypeMode;
            CredentialCustomDelimiter = source.CredentialCustomDelimiter;
            CredentialCustomTransitionKey = source.CredentialCustomTransitionKey;
            CredentialStageDelayMs = source.CredentialStageDelayMs;
            CredentialAutoClearClipboard = source.CredentialAutoClearClipboard;
            CredentialAutoClearDelaySeconds = source.CredentialAutoClearDelaySeconds;

            EnableTypingJitter = source.EnableTypingJitter;
            TypingJitterRangeMs = source.TypingJitterRangeMs;

            HotkeyModifiers = source.HotkeyModifiers;
            HotkeyKey = source.HotkeyKey;

            OverlayToggleModifiers = source.OverlayToggleModifiers;
            OverlayToggleKey = source.OverlayToggleKey;
            OverlayToggleEnabled = source.OverlayToggleEnabled;

            AutoStartEnabled = source.AutoStartEnabled;
            AutoUpdateCheckEnabled = source.AutoUpdateCheckEnabled;
            LastUpdateCheckUtc = source.LastUpdateCheckUtc;

            EnableDiagnosticLogging = source.EnableDiagnosticLogging;
        }
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
    /// Thread-safe via a static synchronization lock (TD-38).
    /// </summary>
    public static class SettingsManager
    {
        private static readonly object _fileLock = new();
        public static string ExeDir { get; }
        public static string SettingsDir { get; }
        public static bool IsPortableFallbackToAppData { get; }
        private static string? _customSettingsFileForTesting;
        private static readonly string DefaultSettingsFile;

        /// <summary>
        /// Gets the active path to settings.json.
        /// </summary>
        public static string SettingsFile => _customSettingsFileForTesting ?? DefaultSettingsFile;

        /// <summary>
        /// Redirects the settings file to a custom path (e.g. a temp folder) during tests (TD-72).
        /// </summary>
        internal static void SetSettingsFileForTesting(string? customFile)
        {
            lock (_fileLock)
            {
                _customSettingsFileForTesting = customFile;
            }
        }

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

            // Portable when the marker file exists, or when this is one of the
            // release EXEs that are downloaded on their own without the marker.
            string markerPath = Path.Combine(ExeDir, "portable.marker");
            IsPortable = File.Exists(markerPath) || IsPortableExeName(exePath);

            if (IsPortable)
            {
                // Portable / Slim: check if directory is writable (TD-77)
                if (CheckDirectoryWritable(ExeDir))
                {
                    SettingsDir = ExeDir;
                }
                else
                {
                    // Fall back to %LocalAppData%\ClipTyper when directory is read-only
                    IsPortableFallbackToAppData = true;
                    SettingsDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "ClipTyper");
                }
            }
            else
            {
                // Winget / installed: store settings in %AppData%\ClipTyper
                SettingsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ClipTyper");
            }

            DefaultSettingsFile = Path.Combine(SettingsDir, "settings.json");
        }

        /// <summary>
        /// True for the Portable and Slim release assets (ClipTyper-Portable.exe,
        /// ClipTyper-Slim.exe), including browser-renamed copies such as
        /// "ClipTyper-Portable (1).exe".
        /// </summary>
        internal static bool IsPortableExeName(string exePath)
        {
            string name = Path.GetFileNameWithoutExtension(exePath);
            return name.StartsWith("ClipTyper-Portable", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("ClipTyper-Slim", StringComparison.OrdinalIgnoreCase);
        }

        private static bool CheckDirectoryWritable(string dir)
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                string testFile = Path.Combine(dir, $".writetest_{Guid.NewGuid():N}.tmp");
                File.WriteAllText(testFile, "1");
                File.Delete(testFile);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Loads settings from disk. If the file is missing or corrupt,
        /// returns (and persists) a fresh default settings instance.
        /// Validates and clamps all numeric and enum settings (TD-26).
        /// </summary>
        public static AppSettings Load()
        {
            lock (_fileLock)
            {
                try
                {
                    string targetFile = SettingsFile;
                    if (File.Exists(targetFile))
                    {
                        string json = File.ReadAllText(targetFile);
                        var loaded = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
                        if (loaded != null)
                        {
                            // Which properties the file really contains (missing ones got class defaults)
                            using var doc = JsonDocument.Parse(json);
                            bool InFile(string name) => doc.RootElement.ValueKind == JsonValueKind.Object
                                && doc.RootElement.TryGetProperty(name, out _);

                            // Clamp numerics (TD-26)
                            loaded.KeystrokeDelayMs = Math.Clamp(loaded.KeystrokeDelayMs, 5, 100);
                            loaded.CredentialStageDelayMs = Math.Clamp(loaded.CredentialStageDelayMs, 50, 2000);
                            loaded.CredentialAutoClearDelaySeconds = Math.Clamp(loaded.CredentialAutoClearDelaySeconds, 1, 60);
                            loaded.TypingJitterRangeMs = Math.Clamp(loaded.TypingJitterRangeMs, 1, 50);
                            loaded.MaxTextLengthThreshold = Math.Max(0, loaded.MaxTextLengthThreshold);

                            // Migrate legacy OverlaySize only for old files that contain OverlaySize but no
                            // OverlayScalePercent yet. OverlaySize is still written with its default, so it
                            // must not override an explicit 100% scale, and a file without it (e.g. "{}")
                            // must keep the default scale.
                            if (!InFile(nameof(AppSettings.OverlayScalePercent)) && InFile(nameof(AppSettings.OverlaySize)) &&
                                !string.IsNullOrEmpty(loaded.OverlaySize))
                            {
                                loaded.OverlayScalePercent = loaded.OverlaySize switch
                                {
                                    "Small" => 50,
                                    "Large" => 200,
                                    _ => 100
                                };
                            }

                            if (!InFile(nameof(AppSettings.OverlayPositionDpiAware)))
                            {
                                // Saved by a version before DPI awareness: only a stored position needs migrating
                                loaded.OverlayPositionDpiAware = loaded.OverlayX == -1;
                            }

                            // Clamp scale percent to valid range (25% to 200%)
                            loaded.OverlayScalePercent = Math.Clamp(loaded.OverlayScalePercent, 25, 200);

                            // Validate enums (TD-26)
                            if (!Enum.IsDefined(loaded.NewlineHandling))
                            {
                                loaded.NewlineHandling = NewlineMode.Enter;
                            }
                            if (!KeyboardLayouts.IsValidKlid(loaded.TargetKeyboardLayout))
                            {
                                loaded.TargetKeyboardLayout = KeyboardLayouts.SameAsThisPc;
                            }
                            if (!Enum.IsDefined(loaded.CredentialAutoTypeMode))
                            {
                                loaded.CredentialAutoTypeMode = CredentialMode.Off;
                            }

                            Current = loaded;
                            return Current;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Failed to load settings: {ex.Message}. Reverting to defaults.");
                }

                Current = new AppSettings();
                Save(); // Persist defaults so the file always exists
                return Current;
            }
        }

        /// <summary>
        /// Updates the current in-memory settings with a new instance and persists to disk.
        /// Single entry point to replace settings across the application (TD-39).
        /// </summary>
        public static void Replace(AppSettings newSettings)
        {
            lock (_fileLock)
            {
                Current.CopyFrom(newSettings);
                Save();
            }
        }

        /// <summary>
        /// Saves the current settings to disk. Creates the directory if it
        /// does not exist. Thread-safe via lock (TD-38).
        /// </summary>
        public static void Save()
        {
            lock (_fileLock)
            {
                try
                {
                    string targetFile = SettingsFile;
                    string targetDir = Path.GetDirectoryName(targetFile) ?? SettingsDir;

                    if (!Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    string json = JsonSerializer.Serialize(Current, AppSettingsJsonContext.Default.AppSettings);

                    if (File.Exists(targetFile) && NativeMethods.GetHardLinkCount(targetFile) > 1)
                    {
                        // Hard-linked file (e.g. Scoop "persist"): replacing it would break the link and the
                        // settings would be lost on the next update, so overwrite its content in place.
                        File.WriteAllText(targetFile, json);
                        return;
                    }

                    string tempFile = targetFile + ".tmp";
                    File.WriteAllText(tempFile, json);
                    File.Move(tempFile, targetFile, overwrite: true);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Failed to save settings: {ex.Message}");
                }
            }
        }
    }
}
