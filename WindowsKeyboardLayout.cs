using System;
using System.Globalization;

namespace ClipTyper
{
    /// <summary>
    /// Default <see cref="IKeyboardLayoutProvider"/> backed by the Windows keyboard layout APIs.
    /// </summary>
    public sealed class WindowsKeyboardLayoutProvider : IKeyboardLayoutProvider
    {
        public IKeyboardLayout? Open(string klid, IntPtr targetHWnd) => WindowsKeyboardLayout.Open(klid, targetHWnd);
    }

    /// <summary>
    /// A Windows keyboard layout (HKL). A layout that is not installed for the user is loaded only
    /// for the duration of one typing run and unloaded again on Dispose, so the user's language
    /// list is left as it was.
    /// </summary>
    internal sealed class WindowsKeyboardLayout : IKeyboardLayout
    {
        private const uint KLF_NOTELLSHELL = 0x00000080;
        private const uint MAPVK_VK_TO_VSC_EX = 4;
        private const uint ToUnicodeDoNotChangeKeyboardState = 0x4; // Windows 10 1607+

        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12;
        private const int VK_LSHIFT = 0xA0;
        private const int VK_LCONTROL = 0xA2;
        private const int VK_RMENU = 0xA5;

        private readonly IntPtr _hkl;
        private bool _unloadOnDispose;

        public bool MatchesLocalLayout { get; }

        private WindowsKeyboardLayout(IntPtr hkl, bool unloadOnDispose, bool matchesLocalLayout)
        {
            _hkl = hkl;
            _unloadOnDispose = unloadOnDispose;
            MatchesLocalLayout = matchesLocalLayout;
        }

        public static WindowsKeyboardLayout? Open(string klid, IntPtr targetHWnd)
        {
            uint targetThreadId = targetHWnd != IntPtr.Zero
                ? NativeMethods.GetWindowThreadProcessId(targetHWnd, out _)
                : 0;
            IntPtr localHkl = NativeMethods.GetKeyboardLayout(targetThreadId);

            if (string.IsNullOrEmpty(klid))
            {
                return localHkl == IntPtr.Zero ? null : new WindowsKeyboardLayout(localHkl, false, true);
            }

            if (!KeyboardLayouts.IsValidKlid(klid))
            {
                return null;
            }

            IntPtr[] loadedBefore = GetLoadedLayouts();
            IntPtr hkl = NativeMethods.LoadKeyboardLayoutW(klid, KLF_NOTELLSHELL);
            if (hkl == IntPtr.Zero)
            {
                return null;
            }

            bool wasLoaded = Array.IndexOf(loadedBefore, hkl) >= 0;

            // LoadKeyboardLayout falls back to another layout when the requested one is not available.
            long requestedLanguage = uint.Parse(klid, NumberStyles.HexNumber, CultureInfo.InvariantCulture) & 0xFFFF;
            if ((hkl.ToInt64() & 0xFFFF) != requestedLanguage)
            {
                if (!wasLoaded)
                {
                    _ = NativeMethods.UnloadKeyboardLayout(hkl);
                }
                return null;
            }

            return new WindowsKeyboardLayout(hkl, !wasLoaded, hkl == localHkl);
        }

        public short VkKeyScan(char c) => NativeMethods.VkKeyScanExW(c, _hkl);

        public uint ScanCode(ushort vk) => NativeMethods.MapVirtualKeyExW(vk, MAPVK_VK_TO_VSC_EX, _hkl);

        public bool IsDeadKey(ushort vk, ushort scan, bool shift, bool altGr)
        {
            var keyState = new byte[256];
            if (shift)
            {
                keyState[VK_SHIFT] = 0x80;
                keyState[VK_LSHIFT] = 0x80;
            }
            if (altGr)
            {
                keyState[VK_CONTROL] = 0x80;
                keyState[VK_LCONTROL] = 0x80;
                keyState[VK_MENU] = 0x80;
                keyState[VK_RMENU] = 0x80;
            }

            var buffer = new char[8];
            int result = NativeMethods.ToUnicodeEx(vk, scan, keyState, buffer, buffer.Length, ToUnicodeDoNotChangeKeyboardState, _hkl);
            return result < 0;
        }

        public void Dispose()
        {
            if (_unloadOnDispose)
            {
                _unloadOnDispose = false;
                if (!NativeMethods.UnloadKeyboardLayout(_hkl))
                {
                    Logger.LogWarning("Could not unload the temporarily loaded target keyboard layout.");
                }
            }
        }

        private static IntPtr[] GetLoadedLayouts()
        {
            int count = NativeMethods.GetKeyboardLayoutList(0, null);
            if (count <= 0) return Array.Empty<IntPtr>();

            var list = new IntPtr[count];
            int actual = NativeMethods.GetKeyboardLayoutList(list.Length, list);
            if (actual < list.Length)
            {
                Array.Resize(ref list, Math.Max(0, actual));
            }
            return list;
        }
    }
}
