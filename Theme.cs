using System.Drawing;
using System.Windows.Forms;

namespace ClipTyper
{
    /// <summary>
    /// Appearance setting: follow Windows (default), or always light / dark.
    /// </summary>
    public enum AppTheme
    {
        System = 0,
        Light = 1,
        Dark = 2
    }

    /// <summary>
    /// Applies the light/dark appearance (WinForms color mode) and provides the few accent colors
    /// ClipTyper draws itself, readable on both backgrounds.
    /// </summary>
    internal static class Theme
    {
        public static string DisplayName(AppTheme theme) => theme switch
        {
            AppTheme.Light => "Light",
            AppTheme.Dark => "Dark",
            _ => "Auto (System)"
        };

        /// <summary>
        /// Sets the color mode for ClipTyper's windows. Call before windows are created; windows
        /// opened later (Settings, About) and the menus pick up a change immediately.
        /// </summary>
        public static void Apply(AppTheme theme)
        {
            Application.SetColorMode(theme switch
            {
                AppTheme.Light => SystemColorMode.Classic,
                AppTheme.Dark => SystemColorMode.Dark,
                _ => SystemColorMode.System
            });
        }

        public static bool IsDark => Application.IsDarkModeEnabled;

        public static Color Success => IsDark ? Color.FromArgb(108, 203, 95) : Color.FromArgb(16, 124, 16);
        public static Color Warning => IsDark ? Color.FromArgb(252, 185, 0) : Color.FromArgb(157, 93, 0);
        public static Color Danger => IsDark ? Color.FromArgb(255, 153, 164) : Color.FromArgb(196, 43, 28);
        public static Color Hint => SystemColors.GrayText;

        /// <summary>
        /// Link colors readable on the dark background (the default blue is too dark there).
        /// </summary>
        public static void StyleLink(LinkLabel link)
        {
            if (!IsDark) return;
            link.LinkColor = Color.FromArgb(108, 180, 255);
            link.ActiveLinkColor = Color.FromArgb(160, 205, 255);
            link.VisitedLinkColor = Color.FromArgb(108, 180, 255);
        }
    }
}
