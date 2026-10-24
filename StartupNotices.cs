using System;

namespace ClipTyper
{
    internal enum StartupNotice
    {
        None,
        Welcome,
        WhatsNew
    }

    /// <summary>
    /// One-time tray notifications at startup: a welcome after the first start, and a short
    /// "what's new" after updating to a new minor version (not for patch releases or betas of the
    /// same version), so existing users discover new features.
    /// </summary>
    internal static class StartupNotices
    {
        /// <summary>
        /// Highlight of the current minor release (max. 255 characters, end-user language).
        /// Update it for every minor release; see DEPLOY.md.
        /// </summary>
        public const string WhatsNewText =
            "Type into consoles that use another keyboard layout: right-click the tray icon and choose " +
            "Keyboard layout. Also new: dark mode, crisper icons and a reorganized Settings window.";

        public static StartupNotice Decide(bool firstRun, string? lastRunVersion, string currentVersion)
        {
            if (firstRun)
            {
                return StartupNotice.Welcome;
            }

            if (!TryGetMinorVersion(currentVersion, out var current))
            {
                return StartupNotice.None;
            }

            // Versions before v1.8.0 did not record the last run version: they were older
            if (!TryGetMinorVersion(lastRunVersion, out var last))
            {
                return StartupNotice.WhatsNew;
            }

            return current > last ? StartupNotice.WhatsNew : StartupNotice.None;
        }

        private static bool TryGetMinorVersion(string? text, out Version version)
        {
            version = new Version(0, 0);
            if (string.IsNullOrWhiteSpace(text)) return false;

            string s = text.Trim().TrimStart('v', 'V');
            int plus = s.IndexOf('+');
            if (plus >= 0) s = s.Substring(0, plus);
            int dash = s.IndexOf('-');
            if (dash >= 0) s = s.Substring(0, dash);

            if (!Version.TryParse(s, out var parsed)) return false;
            version = new Version(parsed.Major, parsed.Minor);
            return true;
        }
    }
}
