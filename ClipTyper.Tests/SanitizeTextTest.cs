using ClipTyper;
using Xunit;

namespace ClipTyper.Tests
{
    public class SanitizeTextTest
    {
        [Fact]
        public void SanitizeText_RemovesBOMAndZeroWidthSpaces()
        {
            string raw = "\uFEFFHello\u200BWorld\0";
            string sanitized = KeyboardSimulator.SanitizeText(raw);

            Assert.Equal("HelloWorld", sanitized);
        }

        [Fact]
        public void SanitizeText_PreservesNormalNewlinesAndTabs()
        {
            string raw = "Line 1\r\nLine 2\tEnd";
            string sanitized = KeyboardSimulator.SanitizeText(raw);

            Assert.Equal("Line 1\r\nLine 2\tEnd", sanitized);
        }

        [Fact]
        public void SanitizeText_HandlesEmptyOrNull()
        {
            Assert.Equal(string.Empty, KeyboardSimulator.SanitizeText(""));
            Assert.Equal(string.Empty, KeyboardSimulator.SanitizeText(null!));
        }

        [Fact]
        public void SanitizeText_RemovesDirectionalMarksAndSoftHyphens()
        {
            string raw = "Test\u00ADSoft\u200EHyphen\u200FMark";
            string sanitized = KeyboardSimulator.SanitizeText(raw);

            Assert.Equal("TestSoftHyphenMark", sanitized);
        }
    }
}
