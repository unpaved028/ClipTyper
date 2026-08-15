using ClipTyper;
using Xunit;

namespace ClipTyper.Tests
{
    public class NewlineHandlingTest
    {
        [Fact]
        public void NewlineMode_EnumValues_AreDistinct()
        {
            Assert.NotEqual(NewlineMode.Enter, NewlineMode.ShiftEnter);
            Assert.NotEqual(NewlineMode.Enter, NewlineMode.Space);
            Assert.NotEqual(NewlineMode.Enter, NewlineMode.Ignore);
        }

        [Fact]
        public void CredentialMode_EnumValues_AreDistinct()
        {
            Assert.NotEqual(CredentialMode.Off, CredentialMode.AutoDetect);
            Assert.NotEqual(CredentialMode.AutoDetect, CredentialMode.TabOnly);
            Assert.NotEqual(CredentialMode.TabOnly, CredentialMode.EnterOnly);
            Assert.NotEqual(CredentialMode.EnterOnly, CredentialMode.Custom);
        }
    }
}
