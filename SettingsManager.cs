using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClipTyper
{
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
        public string OverlaySize { get; set; } = "Small";   // "Small" | "Medium" | "Large"
        public int OverlayX { get; set; } = -1;              // -1 = default (right edge)
        public int OverlayY { get; set; } = -1;              // -1 = default (vertically centered)

        // Typing
        public int KeystrokeDelayMs { get; set; } = 25;

        // Hotkey  (defaults: Ctrl+Shift = 0x0006, T = 0x54)
        public int HotkeyModifiers { get; set; } = 0x0006;
        public int HotkeyKey { get; set; } = 0x54;

        // Autostart (only used in Winget/installed mode)
        public bool AutoStartEnabled { get; set; } = true;
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
        private static readonly string SettingsDir;
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
