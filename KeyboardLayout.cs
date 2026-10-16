using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ClipTyper
{
    /// <summary>
    /// A selectable target keyboard layout. <see cref="Klid"/> is the Windows keyboard
    /// layout identifier (e.g. "00000409" for US); an empty KLID means "same as this PC".
    /// </summary>
    public sealed record KeyboardLayoutOption(string Klid, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// Curated list of target keyboard layouts offered in Settings.
    /// </summary>
    public static class KeyboardLayouts
    {
        public const string SameAsThisPc = "";

        public static IReadOnlyList<KeyboardLayoutOption> All { get; } = new[]
        {
            new KeyboardLayoutOption(SameAsThisPc, "Same as this PC"),
            new KeyboardLayoutOption("00000409", "English (United States)"),
            new KeyboardLayoutOption("00020409", "English (United States-International)"),
            new KeyboardLayoutOption("00000809", "English (United Kingdom)"),
            new KeyboardLayoutOption("00000407", "German (Germany)"),
            new KeyboardLayoutOption("00000807", "German (Switzerland)"),
            new KeyboardLayoutOption("0000040C", "French (France)"),
            new KeyboardLayoutOption("0000080C", "French (Belgium)"),
            new KeyboardLayoutOption("0000100C", "French (Switzerland)"),
            new KeyboardLayoutOption("00000410", "Italian"),
            new KeyboardLayoutOption("0000040A", "Spanish"),
            new KeyboardLayoutOption("00000816", "Portuguese (Portugal)"),
            new KeyboardLayoutOption("00000416", "Portuguese (Brazil)"),
            new KeyboardLayoutOption("00000413", "Dutch"),
            new KeyboardLayoutOption("00000406", "Danish"),
            new KeyboardLayoutOption("00000414", "Norwegian"),
            new KeyboardLayoutOption("0000041D", "Swedish"),
            new KeyboardLayoutOption("0000040B", "Finnish"),
            new KeyboardLayoutOption("00000415", "Polish (Programmers)"),
            new KeyboardLayoutOption("00000405", "Czech"),
            new KeyboardLayoutOption("0000041B", "Slovak"),
            new KeyboardLayoutOption("0000040E", "Hungarian"),
            new KeyboardLayoutOption("00000419", "Russian"),
            new KeyboardLayoutOption("00000411", "Japanese"),
        };

        /// <summary>
        /// True for "same as this PC" (empty) or an 8-digit hexadecimal KLID.
        /// </summary>
        public static bool IsValidKlid(string? klid)
        {
            if (klid == null) return false;
            if (klid.Length == 0) return true;
            return klid.Length == 8 && uint.TryParse(klid, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
        }

        public static string GetDisplayName(string klid)
        {
            var option = All.FirstOrDefault(o => string.Equals(o.Klid, klid, StringComparison.OrdinalIgnoreCase));
            return option?.DisplayName ?? $"Keyboard layout {klid}";
        }
    }

    /// <summary>
    /// How to produce one character on the target layout: the key (virtual key + scan code)
    /// and the modifiers to hold. Dead keys need a trailing Space to produce the character itself.
    /// </summary>
    public readonly record struct KeyMapping(ushort Vk, ushort Scan, bool Extended, bool Shift, bool AltGr, bool IsDeadKey);

    /// <summary>
    /// Read-only view of one Windows keyboard layout. Mockable so the mapping logic
    /// can be tested without loading layouts or sending keystrokes.
    /// </summary>
    public interface IKeyboardLayout : IDisposable
    {
        /// <summary>VkKeyScanEx result: low byte = virtual key, high byte = shift state; -1 when no key produces the character.</summary>
        short VkKeyScan(char c);

        /// <summary>MapVirtualKeyEx(MAPVK_VK_TO_VSC_EX) result: scan code, with 0xE0/0xE1 in the high byte for extended keys.</summary>
        uint ScanCode(ushort vk);

        /// <summary>True when this key, with the given modifiers, is a dead key on this layout.</summary>
        bool IsDeadKey(ushort vk, ushort scan, bool shift, bool altGr);

        /// <summary>True when this is also the layout the target window uses on this PC.</summary>
        bool MatchesLocalLayout { get; }
    }

    /// <summary>
    /// Opens the keyboard layout used for VK compatibility mode.
    /// </summary>
    public interface IKeyboardLayoutProvider
    {
        /// <summary>
        /// Opens the layout with the given KLID, or the target window's current layout for an empty KLID.
        /// Returns null when the layout cannot be loaded. Dispose the result when typing is finished.
        /// </summary>
        IKeyboardLayout? Open(string klid, IntPtr targetHWnd);
    }

    /// <summary>
    /// Maps characters to keystrokes on a target keyboard layout (VK compatibility mode).
    /// </summary>
    public sealed class KeyboardLayoutMapper
    {
        private readonly IKeyboardLayout _layout;
        private readonly Dictionary<char, (bool Ok, KeyMapping Mapping)> _cache = new();

        public KeyboardLayoutMapper(IKeyboardLayout layout)
        {
            _layout = layout;
        }

        /// <summary>
        /// When the target layout is also the local one, AltGr is sent the way Windows reports a
        /// physical AltGr press (Left Ctrl + Right Alt together). For a different target layout only
        /// Right Alt is sent, so the remote side applies its own AltGr handling.
        /// </summary>
        public bool AltGrWithLeftCtrl => _layout.MatchesLocalLayout;

        public bool TryMap(char c, out KeyMapping mapping)
        {
            if (!_cache.TryGetValue(c, out var entry))
            {
                bool ok = TryMapUncached(c, out var m);
                entry = (ok, m);
                _cache[c] = entry;
            }

            mapping = entry.Mapping;
            return entry.Ok;
        }

        /// <summary>
        /// Returns the distinct characters of <paramref name="text"/> that have no key on the target
        /// layout, in order of first appearance. Line breaks (handled by the newline setting) and control
        /// characters (never typed) are ignored; a surrogate pair is reported as one element.
        /// </summary>
        public IReadOnlyList<string> FindUnmappable(string text)
        {
            var result = new List<string>();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r' || c == '\n' || KeyboardSimulator.IsSkippedControlChar(c)) continue;

                string element;
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    element = text.Substring(i, 2);
                    i++;
                }
                else if (TryMap(c, out _))
                {
                    continue;
                }
                else
                {
                    element = c.ToString();
                }

                if (!result.Contains(element))
                {
                    result.Add(element);
                }
            }
            return result;
        }

        private bool TryMapUncached(char c, out KeyMapping mapping)
        {
            mapping = default;

            // Control characters would map to Ctrl+key shortcuts (e.g. U+0003 -> Ctrl+C): never send those.
            if ((c < 0x20 && c != '\t') || c == '\u007F' || char.IsSurrogate(c))
            {
                return false;
            }

            short result = _layout.VkKeyScan(c);
            if (result == -1 || (result & 0xFF) == 0xFF)
            {
                return false;
            }

            ushort vk = (ushort)(result & 0xFF);
            int modifiers = (result >> 8) & 0xFF;
            bool shift = (modifiers & 1) != 0;
            bool ctrl = (modifiers & 2) != 0;
            bool alt = (modifiers & 4) != 0;

            // Ctrl-only / Alt-only / Hankaku combinations do not produce plain text input.
            if ((modifiers & ~0x7) != 0 || ctrl != alt)
            {
                return false;
            }

            bool altGr = ctrl && alt;
            uint scanEx = _layout.ScanCode(vk);
            ushort scan = (ushort)(scanEx & 0xFF);
            uint prefix = scanEx & 0xFF00;
            bool extended = prefix == 0xE000 || prefix == 0xE100;

            mapping = new KeyMapping(vk, scan, extended, shift, altGr, _layout.IsDeadKey(vk, scan, shift, altGr));
            return true;
        }
    }
}
