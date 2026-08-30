using ClipTyper;
using Xunit;

namespace ClipTyper.Tests
{
    public class CredentialParserTest
    {
        [Fact]
        public void TrySplit_WhenModeOff_ReturnsFalse()
        {
            bool split = CredentialParser.TrySplit("admin\tsecret123", CredentialMode.Off, "", out var res);
            Assert.False(split);
            Assert.Null(res);
        }

        [Fact]
        public void TrySplit_TabOnly_SplitsOnTab()
        {
            bool split = CredentialParser.TrySplit("admin\tsecret123", CredentialMode.TabOnly, "", out var res);
            Assert.True(split);
            Assert.NotNull(res);
            Assert.Equal("admin", res!.Part1);
            Assert.Equal("secret123", res.Part2);
            Assert.Equal(CredentialParser.VK_TAB, res.TransitionVk);
        }

        [Fact]
        public void TrySplit_EnterOnly_SplitsOnNewline()
        {
            bool split = CredentialParser.TrySplit("admin\r\nsecret123", CredentialMode.EnterOnly, "", out var res);
            Assert.True(split);
            Assert.NotNull(res);
            Assert.Equal("admin", res!.Part1);
            Assert.Equal("secret123", res.Part2);
            Assert.Equal(CredentialParser.VK_RETURN, res.TransitionVk);
        }

        [Fact]
        public void TrySplit_CustomDelimiter_WithTabTransition_SplitsCorrectly()
        {
            bool split = CredentialParser.TrySplit("admin:::secret123", CredentialMode.Custom, ":::", out var res, customTransitionVk: CredentialParser.VK_TAB);
            Assert.True(split);
            Assert.NotNull(res);
            Assert.Equal("admin", res!.Part1);
            Assert.Equal("secret123", res.Part2);
            Assert.Equal(CredentialParser.VK_TAB, res.TransitionVk);
        }

        [Fact]
        public void TrySplit_CustomDelimiter_WithEnterTransition_SplitsCorrectly()
        {
            bool split = CredentialParser.TrySplit("admin:::secret123", CredentialMode.Custom, ":::", out var res, customTransitionVk: CredentialParser.VK_RETURN);
            Assert.True(split);
            Assert.NotNull(res);
            Assert.Equal("admin", res!.Part1);
            Assert.Equal("secret123", res.Part2);
            Assert.Equal(CredentialParser.VK_RETURN, res.TransitionVk);
        }

        [Fact]
        public void TrySplit_CustomDelimiter_EmptyDelimiter_ReturnsFalse()
        {
            bool split = CredentialParser.TrySplit("admin:::secret123", CredentialMode.Custom, "", out var res);
            Assert.False(split);
            Assert.Null(res);

            bool splitWhitespace = CredentialParser.TrySplit("admin:::secret123", CredentialMode.Custom, "   ", out var res2);
            Assert.False(splitWhitespace);
            Assert.Null(res2);
        }

        [Fact]
        public void TrySplit_AutoDetect_PrioritizesTabOverNewline()
        {
            // If text contains tab and newline, tab takes priority
            string text = "admin\tsecret\r\nextra";
            bool split = CredentialParser.TrySplit(text, CredentialMode.AutoDetect, "", out var res);
            Assert.True(split);
            Assert.NotNull(res);
            Assert.Equal("admin", res!.Part1);
            Assert.Equal("secret\r\nextra", res.Part2);
            Assert.Equal(CredentialParser.VK_TAB, res.TransitionVk);
        }

        [Fact]
        public void TrySplit_AutoDetect_FallsBackToNewlineWhenNoTab()
        {
            string text = "admin\nsecret123";
            bool split = CredentialParser.TrySplit(text, CredentialMode.AutoDetect, "", out var res);
            Assert.True(split);
            Assert.NotNull(res);
            Assert.Equal("admin", res!.Part1);
            Assert.Equal("secret123", res.Part2);
            Assert.Equal(CredentialParser.VK_RETURN, res.TransitionVk);
        }

        [Fact]
        public void TrySplit_SingleLineWithoutDelimiter_ReturnsFalse()
        {
            string text = "just-a-plain-string";
            bool split = CredentialParser.TrySplit(text, CredentialMode.AutoDetect, "", out var res);
            Assert.False(split);
            Assert.Null(res);
        }
    }
}
