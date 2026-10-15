using ClipTyper;
using Xunit;

namespace ClipTyper.Tests
{
    /// <summary>
    /// In-memory keyboard layout: no Windows layout is loaded and no keystrokes are sent.
    /// </summary>
    public sealed class FakeKeyboardLayout : IKeyboardLayout
    {
        public Dictionary<char, short> Keys { get; } = new();
        public Dictionary<ushort, uint> ScanCodes { get; } = new();
        public HashSet<(ushort Vk, bool Shift, bool AltGr)> DeadKeys { get; } = new();
        public bool MatchesLocalLayout { get; set; }
        public bool Disposed { get; private set; }

        public short VkKeyScan(char c) => Keys.TryGetValue(c, out var result) ? result : (short)-1;

        public uint ScanCode(ushort vk) => ScanCodes.TryGetValue(vk, out var scan) ? scan : 0;

        public bool IsDeadKey(ushort vk, ushort scan, bool shift, bool altGr) => DeadKeys.Contains((vk, shift, altGr));

        public void Dispose() => Disposed = true;

        /// <summary>Lowercase letters, digits and space, like any Latin layout.</summary>
        public static FakeKeyboardLayout Basic()
        {
            var layout = new FakeKeyboardLayout();
            for (char c = 'a'; c <= 'z'; c++)
            {
                ushort vk = (ushort)char.ToUpperInvariant(c);
                layout.Keys[c] = (short)vk;
                layout.Keys[char.ToUpperInvariant(c)] = (short)(0x0100 | vk);
                layout.ScanCodes[vk] = (uint)(0x10 + (c - 'a'));
            }
            for (char c = '0'; c <= '9'; c++)
            {
                layout.Keys[c] = (short)c;
                layout.ScanCodes[c] = (uint)(0x02 + (c - '0'));
            }
            layout.Keys[' '] = 0x20;
            layout.ScanCodes[0x20] = 0x39;
            layout.Keys['\t'] = 0x09;
            layout.ScanCodes[0x09] = 0x0F;
            return layout;
        }

        /// <summary>Basic layout plus a few German specifics: AltGr+Q = @, dead ^ (but not °), ä.</summary>
        public static FakeKeyboardLayout GermanExcerpt()
        {
            var layout = Basic();
            layout.Keys['@'] = 0x0651;          // AltGr (Ctrl+Alt) + Q
            layout.Keys['^'] = 0x00DC;          // VK_OEM_5, dead key without Shift
            layout.Keys['°'] = 0x01DC;          // Shift + VK_OEM_5, not dead
            layout.ScanCodes[0xDC] = 0x29;
            layout.DeadKeys.Add((0xDC, false, false));
            layout.Keys['ä'] = 0x00DE;          // VK_OEM_7
            layout.ScanCodes[0xDE] = 0x28;
            layout.Keys['\u0003'] = 0x0243;     // what VkKeyScan reports for a control character: Ctrl+C
            return layout;
        }
    }

    public sealed class FakeKeyboardLayoutProvider : IKeyboardLayoutProvider
    {
        public IKeyboardLayout? Layout { get; set; }
        public int OpenCount { get; private set; }
        public string? LastKlid { get; private set; }

        public IKeyboardLayout? Open(string klid, IntPtr targetHWnd)
        {
            OpenCount++;
            LastKlid = klid;
            return Layout;
        }
    }

    public class KeyboardLayoutTest
    {
        private const uint KEYUP = KeyboardSimulator.KEYEVENTF_KEYUP;
        private const uint EXTENDED = KeyboardSimulator.KEYEVENTF_EXTENDEDKEY;

        [Fact]
        public void TryMap_PlainShiftedAndAltGrCharacters_UseTheTargetLayoutKeyAndScanCode()
        {
            var mapper = new KeyboardLayoutMapper(FakeKeyboardLayout.GermanExcerpt());

            Assert.True(mapper.TryMap('a', out var a));
            Assert.Equal(new KeyMapping(0x41, 0x10, false, Shift: false, AltGr: false, IsDeadKey: false), a);

            Assert.True(mapper.TryMap('A', out var upper));
            Assert.True(upper.Shift);
            Assert.False(upper.AltGr);

            Assert.True(mapper.TryMap('@', out var at));
            Assert.Equal((ushort)0x51, at.Vk);
            Assert.True(at.AltGr);
            Assert.False(at.Shift);
        }

        [Fact]
        public void TryMap_RejectsControlCharacters_EvenWhenTheLayoutMapsThemToCtrlShortcuts()
        {
            var mapper = new KeyboardLayoutMapper(FakeKeyboardLayout.GermanExcerpt());

            Assert.False(mapper.TryMap('\u0003', out _));
            Assert.False(mapper.TryMap('\u007F', out _));
            Assert.True(mapper.TryMap('\t', out var tab));
            Assert.Equal((ushort)0x0F, tab.Scan);
        }

        [Fact]
        public void TryMap_FlagsDeadKey_OnlyForTheShiftStateThatIsDead()
        {
            var mapper = new KeyboardLayoutMapper(FakeKeyboardLayout.GermanExcerpt());

            Assert.True(mapper.TryMap('^', out var caret));
            Assert.True(caret.IsDeadKey);

            Assert.True(mapper.TryMap('°', out var degree));
            Assert.False(degree.IsDeadKey);
        }

        [Fact]
        public void TryMap_ExtendedScanCode_SetsExtendedFlag()
        {
            var layout = FakeKeyboardLayout.Basic();
            layout.ScanCodes['A'] = 0xE01C;
            var mapper = new KeyboardLayoutMapper(layout);

            Assert.True(mapper.TryMap('a', out var mapping));
            Assert.True(mapping.Extended);
            Assert.Equal((ushort)0x1C, mapping.Scan);
        }

        [Fact]
        public void FindUnmappable_ReturnsDistinctMissingCharacters_IgnoringLineBreaks_AndKeepsSurrogatePairsTogether()
        {
            var mapper = new KeyboardLayoutMapper(FakeKeyboardLayout.Basic());

            var missing = mapper.FindUnmappable("päss wörd ä\r\n\U0001F600 ok");

            Assert.Equal(new[] { "ä", "ö", "\U0001F600" }, missing);
        }

        [Fact]
        public void BuildMappedKeySteps_AltGrOnLocalLayout_SendsLeftCtrlAndRightAltTogether()
        {
            var mapping = new KeyMapping(0x51, 0x10, false, Shift: false, AltGr: true, IsDeadKey: false);

            var steps = KeyboardSimulator.BuildMappedKeySteps(mapping, altGrWithLeftCtrl: true);

            Assert.Equal(4, steps.Count);
            var down = steps[0].Inputs;
            Assert.Equal(2, down.Length);
            Assert.Equal((ushort)0x11, down[0].U.ki.wVk);
            Assert.Equal((ushort)0x1D, down[0].U.ki.wScan);
            Assert.Equal(0u, down[0].U.ki.dwFlags);
            Assert.Equal((ushort)0x12, down[1].U.ki.wVk);
            Assert.Equal((ushort)0x38, down[1].U.ki.wScan);
            Assert.Equal(EXTENDED, down[1].U.ki.dwFlags);

            var up = steps[3].Inputs;
            Assert.Equal(2, up.Length);
            Assert.Equal(EXTENDED | KEYUP, up[0].U.ki.dwFlags);
            Assert.Equal(KEYUP, up[1].U.ki.dwFlags);
        }

        [Fact]
        public void BuildMappedKeySteps_AltGrOnOtherLayout_SendsRightAltOnly()
        {
            var mapping = new KeyMapping(0x51, 0x10, false, Shift: false, AltGr: true, IsDeadKey: false);

            var steps = KeyboardSimulator.BuildMappedKeySteps(mapping, altGrWithLeftCtrl: false);

            var down = Assert.Single(steps[0].Inputs);
            Assert.Equal((ushort)0x12, down.U.ki.wVk);
            Assert.Equal(EXTENDED, down.U.ki.dwFlags);
        }

        [Fact]
        public void BuildMappedKeySteps_KeyEventsCarryScanCodeAndExtendedFlag()
        {
            var mapping = new KeyMapping(0x41, 0x1E, true, Shift: false, AltGr: false, IsDeadKey: false);

            var steps = KeyboardSimulator.BuildMappedKeySteps(mapping, altGrWithLeftCtrl: false);

            Assert.Equal(2, steps.Count);
            var down = Assert.Single(steps[0].Inputs);
            var up = Assert.Single(steps[1].Inputs);
            Assert.Equal((ushort)0x41, down.U.ki.wVk);
            Assert.Equal((ushort)0x1E, down.U.ki.wScan);
            Assert.Equal(EXTENDED, down.U.ki.dwFlags);
            Assert.Equal(EXTENDED | KEYUP, up.U.ki.dwFlags);
        }

        [Fact]
        public void BuildMappedKeySteps_DeadKey_IsFollowedBySpaceAfterModifiersAreReleased()
        {
            var mapping = new KeyMapping(0xDC, 0x29, false, Shift: true, AltGr: false, IsDeadKey: true);

            var steps = KeyboardSimulator.BuildMappedKeySteps(mapping, altGrWithLeftCtrl: false);

            Assert.Equal(6, steps.Count);
            Assert.Equal((ushort)0x10, steps[3].Inputs[0].U.ki.wVk);          // Shift up
            Assert.Equal(KEYUP, steps[3].Inputs[0].U.ki.dwFlags);
            Assert.Equal((ushort)0x20, steps[4].Inputs[0].U.ki.wVk);          // Space down
            Assert.Equal((ushort)0x39, steps[4].Inputs[0].U.ki.wScan);
            Assert.Equal(0u, steps[4].Inputs[0].U.ki.dwFlags);
            Assert.Equal(KEYUP, steps[5].Inputs[0].U.ki.dwFlags);              // Space up
        }

        [Fact]
        public void FindUnmappable_IgnoresControlCharacters_BecauseTheyAreNeverTyped()
        {
            var mapper = new KeyboardLayoutMapper(FakeKeyboardLayout.Basic());

            Assert.Empty(mapper.FindUnmappable("ok\u0003\u001B\u0008ok\tok"));
        }

        [Theory]
        [InlineData('\u0003', true)]
        [InlineData('\u001B', true)]
        [InlineData('\u0008', true)]
        [InlineData('\u007F', true)]
        [InlineData('\t', false)]
        [InlineData('\n', false)]
        [InlineData('\r', false)]
        [InlineData('a', false)]
        [InlineData('ä', false)]
        public void IsSkippedControlChar_SkipsControlCharactersButNotTabsAndLineBreaks(char c, bool expected)
        {
            Assert.Equal(expected, KeyboardSimulator.IsSkippedControlChar(c));
        }

        [Fact]
        public void BuildUnicodePairInputs_SendsBothSurrogateHalvesInOneBatch()
        {
            string emoji = "\U0001F600";
            var inputs = KeyboardSimulator.BuildUnicodePairInputs(emoji[0], emoji[1]);

            Assert.Equal(4, inputs.Length);
            Assert.Equal((ushort)emoji[0], inputs[0].U.ki.wScan);
            Assert.Equal(KeyboardSimulator.KEYEVENTF_UNICODE, inputs[0].U.ki.dwFlags);
            Assert.Equal(KeyboardSimulator.KEYEVENTF_UNICODE | KEYUP, inputs[1].U.ki.dwFlags);
            Assert.Equal((ushort)emoji[1], inputs[2].U.ki.wScan);
            Assert.Equal(KeyboardSimulator.KEYEVENTF_UNICODE | KEYUP, inputs[3].U.ki.dwFlags);
            Assert.All(inputs, i => Assert.Equal((ushort)0, i.U.ki.wVk));
        }

        [Theory]
        [InlineData("", true)]
        [InlineData("00000409", true)]
        [InlineData("0000040c", true)]
        [InlineData("409", false)]
        [InlineData("0000040G", false)]
        [InlineData(null, false)]
        public void KeyboardLayouts_IsValidKlid(string? klid, bool expected)
        {
            Assert.Equal(expected, KeyboardLayouts.IsValidKlid(klid));
        }

        [Fact]
        public void KeyboardLayouts_List_StartsWithSameAsThisPc_AndHasUniqueValidKlids()
        {
            Assert.Equal(KeyboardLayouts.SameAsThisPc, KeyboardLayouts.All[0].Klid);
            Assert.All(KeyboardLayouts.All, o => Assert.True(KeyboardLayouts.IsValidKlid(o.Klid)));
            Assert.Equal(KeyboardLayouts.All.Count, KeyboardLayouts.All.Select(o => o.Klid).Distinct().Count());
        }
    }
}
