using System;

namespace ClipTyper
{
    /// <summary>
    /// Abstraction for simulating keystrokes.
    /// Allows mocking/faking keyboard output in unit tests without injecting real keystrokes.
    /// </summary>
    public interface IKeyboardSender
    {
        TypeResult SendText(
            string text,
            int delayMs = 25,
            IntPtr targetHWnd = default,
            KeyboardLayoutMapper? vkLayout = null,
            bool sanitize = true,
            NewlineMode newlineMode = NewlineMode.Enter,
            bool enableJitter = false,
            int jitterRangeMs = 5,
            Action<int, int>? onProgress = null,
            Func<bool>? isCancelRequested = null);

        void SendVirtualKey(ushort vk, bool shift = false);
    }

    /// <summary>
    /// Default Windows API implementation calling <see cref="KeyboardSimulator"/>.
    /// </summary>
    public class WindowsKeyboardSender : IKeyboardSender
    {
        public TypeResult SendText(
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
            return KeyboardSimulator.SendText(
                text,
                delayMs,
                targetHWnd,
                vkLayout,
                sanitize,
                newlineMode,
                enableJitter,
                jitterRangeMs,
                onProgress,
                isCancelRequested);
        }

        public void SendVirtualKey(ushort vk, bool shift = false)
        {
            KeyboardSimulator.SendVirtualKey(vk, shift);
        }
    }
}
