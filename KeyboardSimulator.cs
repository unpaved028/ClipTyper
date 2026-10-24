using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ClipTyper
{
    public enum TypeResult
    {
        Completed,
        FocusLost,
        AbortedByEscape
    }

    public static class KeyboardSimulator
    {
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int vKey);

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion U;
            public static int Size => Marshal.SizeOf<INPUT>();
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx; public int dy; public uint mouseData; 
            public uint dwFlags; public uint time; public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT
        {
            public uint uMsg; public ushort wParamL; public ushort wParamH;
        }

        public const int INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        public const uint KEYEVENTF_UNICODE = 0x0004;
        public const uint KEYEVENTF_KEYUP = 0x0002;

        // Virtual key codes
        private const ushort VK_SHIFT    = 0x10;
        private const ushort VK_CONTROL  = 0x11;
        private const ushort VK_MENU     = 0x12;  // Alt
        private const ushort VK_CAPITAL  = 0x14;
        private const ushort VK_ESCAPE   = 0x1B;
        private const ushort VK_SPACE    = 0x20;
        private const ushort VK_LWIN     = 0x5B;
        private const ushort VK_RWIN     = 0x5C;

        // Scan codes of the keys used around mapped characters (identical on all layouts)
        private const ushort SC_LSHIFT   = 0x2A;
        private const ushort SC_LCONTROL = 0x1D;
        private const ushort SC_ALT      = 0x38;  // Right Alt (AltGr) with the extended flag
        private const ushort SC_SPACE    = 0x39;

        private const uint MAPVK_VK_TO_VSC = 0;

        /// <summary>
        /// One SendInput call plus the pause that follows it.
        /// </summary>
        internal readonly record struct KeyStep(INPUT[] Inputs, int SleepAfterMs);

        /// <summary>
        /// Sanitizes text by removing non-printable control characters, BOM, zero-width spaces, etc.
        /// </summary>
        public static string SanitizeText(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            StringBuilder sb = new StringBuilder(input.Length);
            foreach (char c in input)
            {
                // Filter out BOM, zero-width spaces, null bytes, soft hyphens, directional marks
                if (c == '\uFEFF' || c == '\u200B' || c == '\0' || c == '\u00AD' || c == '\u200E' || c == '\u200F')
                    continue;

                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Sends KeyUp events for all modifier keys (Ctrl, Shift, Alt, Win).
        /// </summary>
        public static void ReleaseModifiers()
        {
            ushort[] modifiers = { VK_SHIFT, VK_CONTROL, VK_MENU, VK_LWIN, VK_RWIN };

            foreach (var vk in modifiers)
            {
                if ((GetAsyncKeyState(vk) & 0x8000) != 0)
                {
                    INPUT[] inputs = new INPUT[1];
                    inputs[0] = new INPUT { type = INPUT_KEYBOARD };
                    inputs[0].U.ki.wVk = vk;
                    inputs[0].U.ki.dwFlags = KEYEVENTF_KEYUP;
                    _ = SendInput(1, inputs, INPUT.Size);
                }
            }
        }

        /// <summary>
        /// Sends a standalone virtual key press (e.g. Tab, Enter, or Shift+Enter).
        /// </summary>
        public static void SendVirtualKey(ushort vk, bool shift = false)
        {
            if (shift) SendModifierDown(VK_SHIFT);
            SendKeyPress(vk, isVirtualKey: true);
            if (shift) SendModifierUp(VK_SHIFT);
        }

        /// <summary>
        /// Simulates typing text into the target window.
        /// Performs focus checks, Escape emergency abort detection, and optional VK compatibility mode:
        /// with <paramref name="vkLayout"/> set, characters are sent as key presses of that keyboard
        /// layout (virtual key + scan code) instead of Unicode characters.
        /// </summary>
        public static TypeResult SendText(
            string text,
            int delayMs = 25,
            IntPtr targetHWnd = default,
            KeyboardLayoutMapper? vkLayout = null,
            bool sanitize = true,
            NewlineMode newlineMode = NewlineMode.Enter,
            bool enableJitter = false,
            int jitterRangeMs = 5,
            Action<int, int>? onProgress = null,
            Func<bool>? isCancelRequested = null)
        {
            if (string.IsNullOrEmpty(text)) return TypeResult.Completed;

            if (sanitize)
            {
                text = SanitizeText(text);
            }

            ReleaseModifiers();
            Thread.Sleep(50); // Let OS process key releases

            // VK mode presses real keys, so an active Caps Lock would invert letters (passwords!).
            // Turn it off while typing and back on afterwards, as a person at the keyboard would.
            bool capsLockTurnedOff = false;
            if (vkLayout != null && NativeMethods.IsCapsLockOn(targetHWnd))
            {
                ToggleCapsLock();
                capsLockTurnedOff = true;
            }

            try
            {
                int totalChars = text.Length;

                for (int i = 0; i < totalChars; i++)
                {
                    // 1. Emergency Abort via Escape
                    if ((GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0 || isCancelRequested?.Invoke() == true)
                    {
                        ReleaseModifiers();
                        return TypeResult.AbortedByEscape;
                    }

                    // 2. Focus-Loss Protection
                    if (targetHWnd != IntPtr.Zero && NativeMethods.GetForegroundWindow() != targetHWnd)
                    {
                        ReleaseModifiers();
                        return TypeResult.FocusLost;
                    }

                    char c = text[i];

                    // CRLF counts as one newline; a lone CR (classic Mac text) is a newline too (TD-56)
                    if (c == '\r')
                    {
                        if (i + 1 < totalChars && text[i + 1] == '\n') continue;
                        c = '\n';
                    }

                    // Control characters (ESC, backspace, U+0003 ...) could act as commands in the
                    // target (e.g. Ctrl+C in a terminal), so they are never typed (TD-61)
                    if (IsSkippedControlChar(c))
                    {
                        onProgress?.Invoke(i + 1, totalChars);
                        continue;
                    }

                    // Characters outside the Basic Multilingual Plane (emoji) are two UTF-16 units:
                    // send both halves together so no delay or abort check can split them (TD-45)
                    if (char.IsHighSurrogate(c) && i + 1 < totalChars && char.IsLowSurrogate(text[i + 1]))
                    {
                        _ = SendInput(4, BuildUnicodePairInputs(c, text[i + 1]), INPUT.Size);
                        i++;
                    }
                    else if (c == '\n')
                    {
                        switch (newlineMode)
                        {
                            case NewlineMode.Enter:
                                SendKeyPress(0x0D, isVirtualKey: true); // VK_RETURN
                                break;
                            case NewlineMode.ShiftEnter:
                                SendVirtualKey(0x0D, shift: true); // Shift + Enter
                                break;
                            case NewlineMode.Space:
                                SendKeyPress(' ', isVirtualKey: false); // Replace with space
                                break;
                            case NewlineMode.Ignore:
                                // Skip newline entirely
                                break;
                        }
                    }
                    else if (vkLayout != null)
                    {
                        if (vkLayout.TryMap(c, out var mapping))
                        {
                            SendMappedKey(mapping, vkLayout.AltGrWithLeftCtrl);
                        }
                        else
                        {
                            // No key on the target layout (the user was warned before typing): Unicode fallback
                            SendKeyPress(c, isVirtualKey: false);
                        }
                    }
                    else
                    {
                        SendKeyPress(c, isVirtualKey: false);
                    }

                    onProgress?.Invoke(i + 1, totalChars);

                    if (delayMs > 0)
                    {
                        int actualDelay = delayMs;
                        if (enableJitter && jitterRangeMs > 0)
                        {
                            actualDelay += Random.Shared.Next(-jitterRangeMs, jitterRangeMs + 1);
                            actualDelay = Math.Max(1, actualDelay);
                        }
                        Thread.Sleep(actualDelay);
                    }
                }

                return TypeResult.Completed;
            }
            finally
            {
                if (capsLockTurnedOff)
                {
                    ToggleCapsLock();
                }
            }
        }

        /// <summary>
        /// C0 control characters and DEL, except Tab and line breaks, are never typed.
        /// </summary>
        internal static bool IsSkippedControlChar(char c)
        {
            return (c < 0x20 && c != '\t' && c != '\n' && c != '\r') || c == '\u007F';
        }

        /// <summary>
        /// Key down/up for both halves of a surrogate pair, sent in one SendInput call.
        /// </summary>
        internal static INPUT[] BuildUnicodePairInputs(char high, char low)
        {
            return new[]
            {
                UnicodeKey(high, keyUp: false),
                UnicodeKey(high, keyUp: true),
                UnicodeKey(low, keyUp: false),
                UnicodeKey(low, keyUp: true),
            };
        }

        private static INPUT UnicodeKey(char c, bool keyUp)
        {
            var input = new INPUT { type = INPUT_KEYBOARD };
            input.U.ki.wScan = c;
            input.U.ki.dwFlags = KEYEVENTF_UNICODE | (keyUp ? KEYEVENTF_KEYUP : 0);
            return input;
        }

        private static void ToggleCapsLock()
        {
            SendKeyPress(VK_CAPITAL, isVirtualKey: true);
            Thread.Sleep(30);
        }

        private static void SendMappedKey(KeyMapping mapping, bool altGrWithLeftCtrl)
        {
            foreach (var step in BuildMappedKeySteps(mapping, altGrWithLeftCtrl))
            {
                _ = SendInput((uint)step.Inputs.Length, step.Inputs, INPUT.Size);
                Thread.Sleep(step.SleepAfterMs);
            }
        }

        /// <summary>
        /// Builds the key events for one mapped character: modifiers down, key down/up, modifiers up,
        /// and a Space after a dead key so the dead key's own character is produced.
        /// Every event carries its scan code, which position-based consoles (iLO, iDRAC, VMRC, KVM) read.
        /// </summary>
        internal static List<KeyStep> BuildMappedKeySteps(KeyMapping mapping, bool altGrWithLeftCtrl)
        {
            var steps = new List<KeyStep>();

            if (mapping.Shift)
            {
                steps.Add(new KeyStep(new[] { Key(VK_SHIFT, SC_LSHIFT, false, keyUp: false) }, 2));
            }
            if (mapping.AltGr)
            {
                // A physical AltGr press arrives as Left Ctrl + Right Alt in one go on AltGr layouts.
                steps.Add(new KeyStep(altGrWithLeftCtrl
                    ? new[] { Key(VK_CONTROL, SC_LCONTROL, false, keyUp: false), Key(VK_MENU, SC_ALT, true, keyUp: false) }
                    : new[] { Key(VK_MENU, SC_ALT, true, keyUp: false) }, 2));
            }

            steps.Add(new KeyStep(new[] { Key(mapping.Vk, mapping.Scan, mapping.Extended, keyUp: false) }, 5));
            steps.Add(new KeyStep(new[] { Key(mapping.Vk, mapping.Scan, mapping.Extended, keyUp: true) }, 2));

            if (mapping.AltGr)
            {
                steps.Add(new KeyStep(altGrWithLeftCtrl
                    ? new[] { Key(VK_MENU, SC_ALT, true, keyUp: true), Key(VK_CONTROL, SC_LCONTROL, false, keyUp: true) }
                    : new[] { Key(VK_MENU, SC_ALT, true, keyUp: true) }, 2));
            }
            if (mapping.Shift)
            {
                steps.Add(new KeyStep(new[] { Key(VK_SHIFT, SC_LSHIFT, false, keyUp: true) }, 2));
            }

            if (mapping.IsDeadKey)
            {
                steps.Add(new KeyStep(new[] { Key(VK_SPACE, SC_SPACE, false, keyUp: false) }, 5));
                steps.Add(new KeyStep(new[] { Key(VK_SPACE, SC_SPACE, false, keyUp: true) }, 2));
            }

            return steps;
        }

        private static INPUT Key(ushort vk, ushort scan, bool extended, bool keyUp)
        {
            var input = new INPUT { type = INPUT_KEYBOARD };
            input.U.ki.wVk = vk;
            input.U.ki.wScan = scan;
            input.U.ki.dwFlags = (extended ? KEYEVENTF_EXTENDEDKEY : 0) | (keyUp ? KEYEVENTF_KEYUP : 0);
            return input;
        }

        private static ushort ScanCodeOf(ushort vk) => (ushort)NativeMethods.MapVirtualKeyW(vk, MAPVK_VK_TO_VSC);

        private static void SendModifierDown(ushort vk)
        {
            INPUT[] input = { Key(vk, ScanCodeOf(vk), false, keyUp: false) };
            _ = SendInput(1, input, INPUT.Size);
            Thread.Sleep(2);
        }

        private static void SendModifierUp(ushort vk)
        {
            INPUT[] input = { Key(vk, ScanCodeOf(vk), false, keyUp: true) };
            _ = SendInput(1, input, INPUT.Size);
            Thread.Sleep(2);
        }

        private static void SendKeyPress(ushort keyOrChar, bool isVirtualKey)
        {
            INPUT[] down = new INPUT[1];
            down[0] = new INPUT { type = INPUT_KEYBOARD };

            INPUT[] up = new INPUT[1];
            up[0] = new INPUT { type = INPUT_KEYBOARD };

            if (isVirtualKey)
            {
                // Enter, Tab, Space: scan code is layout-independent; consoles that read scan codes need it
                ushort scan = ScanCodeOf(keyOrChar);
                down[0].U.ki.wVk = keyOrChar;
                down[0].U.ki.wScan = scan;
                up[0].U.ki.wVk = keyOrChar;
                up[0].U.ki.wScan = scan;
                up[0].U.ki.dwFlags = KEYEVENTF_KEYUP;
            }
            else
            {
                down[0].U.ki.wScan = keyOrChar;
                down[0].U.ki.dwFlags = KEYEVENTF_UNICODE;

                up[0].U.ki.wScan = keyOrChar;
                up[0].U.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
            }

            _ = SendInput(1, down, INPUT.Size);
            Thread.Sleep(5);
            _ = SendInput(1, up, INPUT.Size);
        }
    }
}
