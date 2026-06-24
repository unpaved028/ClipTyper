using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace ClipTyper
{
    /// <summary>
    /// Manages installed-mode (Winget) features:
    /// - Start Menu shortcut creation/update
    /// - Autostart via HKCU\...\Run registry entry
    /// All operations are user-space only — no admin required.
    /// </summary>
    public static class InstallHelper
    {
        private const string AppName = "ClipTyper";
        private const string AutostartRegKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        private static readonly string ExePath =
            System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
            ?? Path.Combine(AppContext.BaseDirectory, "ClipTyper.exe");

        private static readonly string ShortcutPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs",
            $"{AppName}.lnk");

        // ── Start Menu Shortcut ─────────────────────────────────────

        /// <summary>
        /// Creates or updates the Start Menu shortcut if it doesn't exist
        /// or points to a different EXE path (e.g., after winget update).
        /// </summary>
        public static void EnsureShortcut()
        {
            try
            {
                if (File.Exists(ShortcutPath))
                {
                    // Check if existing shortcut already points to current EXE
                    string? existingTarget = ReadShortcutTarget(ShortcutPath);
                    if (string.Equals(existingTarget, ExePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return; // Already up to date
                    }
                }

                CreateShortcut(ShortcutPath, ExePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create Start Menu shortcut: {ex.Message}");
            }
        }

        /// <summary>
        /// Removes the Start Menu shortcut (e.g., on uninstall).
        /// </summary>
        public static void RemoveShortcut()
        {
            try
            {
                if (File.Exists(ShortcutPath))
                {
                    File.Delete(ShortcutPath);
                }
            }
            catch { }
        }

        // ── Autostart ───────────────────────────────────────────────

        /// <summary>
        /// Sets or removes the autostart registry entry based on the enabled flag.
        /// Uses HKCU (user-level, no admin required).
        /// </summary>
        public static void SetAutoStart(bool enabled)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(AutostartRegKey, writable: true);
                if (key == null) return;

                if (enabled)
                {
                    key.SetValue(AppName, $"\"{ExePath}\"");
                }
                else
                {
                    key.DeleteValue(AppName, throwOnMissingValue: false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to set autostart: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks whether the autostart registry entry currently exists.
        /// </summary>
        public static bool IsAutoStartEnabled()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(AutostartRegKey);
                return key?.GetValue(AppName) != null;
            }
            catch
            {
                return false;
            }
        }

        // ── COM Interop for .lnk Shortcut Creation ─────────────────

        /// <summary>
        /// Creates a .lnk shortcut file using the COM IShellLink interface.
        /// </summary>
        private static void CreateShortcut(string shortcutPath, string targetPath)
        {
            var shellLink = (IShellLink)new ShellLink();

            shellLink.SetPath(targetPath);
            shellLink.SetWorkingDirectory(Path.GetDirectoryName(targetPath) ?? "");
            shellLink.SetDescription("ClipTyper — Type clipboard contents via simulated keystrokes");
            shellLink.SetIconLocation(targetPath, 0);

            var persistFile = (IPersistFile)shellLink;

            // Ensure parent directory exists
            string? dir = Path.GetDirectoryName(shortcutPath);
            if (dir != null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            persistFile.Save(shortcutPath, false);

            Marshal.ReleaseComObject(shellLink);
        }

        /// <summary>
        /// Reads the target path from an existing .lnk shortcut file.
        /// </summary>
        private static string? ReadShortcutTarget(string shortcutPath)
        {
            try
            {
                var shellLink = (IShellLink)new ShellLink();
                var persistFile = (IPersistFile)shellLink;
                persistFile.Load(shortcutPath, 0); // STGM_READ

                var sb = new StringBuilder(260);
                shellLink.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);

                Marshal.ReleaseComObject(shellLink);
                return sb.ToString();
            }
            catch
            {
                return null;
            }
        }

        // ── COM Declarations for IShellLink ─────────────────────────

        // CLSID_ShellLink: 00021401-0000-0000-C000-000000000046
        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        // IShellLink interface (Unicode version)
        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLink
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
                int cchMaxPath, IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName,
                int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir,
                int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs,
                int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath,
                int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }
    }
}
