using System;
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
            public static int Size => Marshal.SizeOf(typeof(INPUT));
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
        public const uint KEYEVENTF_UNICODE = 0x0004;
        public const uint KEYEVENTF_KEYUP = 0x0002;

        // Virtual key codes
        private const ushort VK_SHIFT    = 0x10;
        private const ushort VK_CONTROL  = 0x11;
        private const ushort VK_MENU     = 0x12;  // Alt
        private const ushort VK_ESCAPE   = 0x1B;
        private const ushort VK_LWIN     = 0x5B;
        private const ushort VK_RWIN     = 0x5C;

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
        /// Performs focus checks, Escape emergency abort detection, and optional VK compatibility mode.
        /// </summary>
        public static TypeResult SendText(
            string text,
            int delayMs = 25,
            IntPtr targetHWnd = default,
            bool enableVkMode = false,
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

                // Skip carriage returns — newlines handled via '\n'
                if (c == '\r') continue;

                if (c == '\n')
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
                else if (enableVkMode)
                {
                    SendCharVk(c);
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

        private static void SendCharVk(char c)
        {
            short scan = NativeMethods.VkKeyScanW(c);
            if (scan == -1)
            {
                // Fallback to Unicode mode if character cannot be mapped on current layout
                SendKeyPress(c, isVirtualKey: false);
                return;
            }

            byte vkCode = (byte)(scan & 0xFF);
            byte shiftState = (byte)((scan >> 8) & 0xFF);

            bool shift = (shiftState & 1) != 0;
            bool ctrl = (shiftState & 2) != 0;
            bool alt = (shiftState & 4) != 0;

            if (shift) SendModifierDown(VK_SHIFT);
            if (ctrl) SendModifierDown(VK_CONTROL);
            if (alt) SendModifierDown(VK_MENU);

            SendKeyPress(vkCode, isVirtualKey: true);

            if (alt) SendModifierUp(VK_MENU);
            if (ctrl) SendModifierUp(VK_CONTROL);
            if (shift) SendModifierUp(VK_SHIFT);
        }

        private static void SendModifierDown(ushort vk)
        {
            INPUT[] input = new INPUT[1];
            input[0] = new INPUT { type = INPUT_KEYBOARD };
            input[0].U.ki.wVk = vk;
            _ = SendInput(1, input, INPUT.Size);
            Thread.Sleep(2);
        }

        private static void SendModifierUp(ushort vk)
        {
            INPUT[] input = new INPUT[1];
            input[0] = new INPUT { type = INPUT_KEYBOARD };
            input[0].U.ki.wVk = vk;
            input[0].U.ki.dwFlags = KEYEVENTF_KEYUP;
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
                down[0].U.ki.wVk = keyOrChar;
                up[0].U.ki.wVk = keyOrChar;
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
