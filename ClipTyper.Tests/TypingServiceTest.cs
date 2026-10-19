using System;
using System.Threading;
using System.Threading.Tasks;
using ClipTyper;
using Xunit;

namespace ClipTyper.Tests
{
    public class FakeKeyboardSender : IKeyboardSender
    {
        public int SendTextCallCount { get; private set; }
        public int SendVirtualKeyCallCount { get; private set; }
        public string LastSentText { get; private set; } = string.Empty;
        public KeyboardLayoutMapper? LastVkLayout { get; private set; }
        public int SimulatedDelayMs { get; set; } = 10;
        public ushort LastVirtualKey { get; private set; }

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
            SendTextCallCount++;
            LastSentText = text;
            LastVkLayout = vkLayout;

            for (int i = 0; i < text.Length; i++)
            {
                if (isCancelRequested?.Invoke() == true)
                {
                    return TypeResult.AbortedByEscape;
                }

                onProgress?.Invoke(i + 1, text.Length);

                if (SimulatedDelayMs > 0)
                {
                    Thread.Sleep(Math.Min(SimulatedDelayMs, 5));
                }
            }

            return TypeResult.Completed;
        }

        public void SendVirtualKey(ushort vk, bool shift = false)
        {
            SendVirtualKeyCallCount++;
            LastVirtualKey = vk;
        }
    }

    public class FakeClipboardReader : IClipboardReader
    {
        public string TextToReturn { get; set; } = "Sample clipboard text";
        public bool ClearCalled { get; private set; }

        public string ReadText()
        {
            return TextToReturn;
        }

        public void Clear()
        {
            ClearCalled = true;
        }
    }

    public class FakeUserPrompt : IUserPrompt
    {
        public bool ConfirmationResult { get; set; } = true;
        public int WarningCount { get; private set; }
        public int ConfirmationCount { get; private set; }
        public string LastWarningMessage { get; private set; } = string.Empty;
        public string LastConfirmationMessage { get; private set; } = string.Empty;
        public ConfirmationRequest? LastConfirmation { get; private set; }

        public void ShowWarning(string message, string caption)
        {
            WarningCount++;
            LastWarningMessage = message;
        }

        public bool Confirm(ConfirmationRequest request)
        {
            ConfirmationCount++;
            LastConfirmation = request;
            // Everything the dialog can show, including the collapsed details
            LastConfirmationMessage = $"{request.Heading}\n{request.Text}\n{request.Details}";
            return ConfirmationResult;
        }
    }

    [Collection("SettingsState")]
    public class TypingServiceTest
    {
        [Fact]
        public void TypingService_InitialState_IsNotTyping()
        {
            var fakeSender = new FakeKeyboardSender();
            var fakeClipboard = new FakeClipboardReader();
            var fakePrompt = new FakeUserPrompt();
            var service = new TypingService(
                keyboardSender: fakeSender,
                clipboardReader: fakeClipboard,
                userPrompt: fakePrompt);

            Assert.False(service.IsTyping);
        }

        [Fact]
        public async Task TypingService_PreventsConcurrentTypingRuns_WithoutHardwareKeystrokes()
        {
            var fakeSender = new FakeKeyboardSender { SimulatedDelayMs = 25 };
            var fakeClipboard = new FakeClipboardReader { TextToReturn = "Concurrent test" };
            var fakePrompt = new FakeUserPrompt();
            var service = new TypingService(
                keyboardSender: fakeSender,
                clipboardReader: fakeClipboard,
                userPrompt: fakePrompt);

            // Trigger first run asynchronously
            Task task1 = service.TriggerClipTypeAsync();
            // Immediate second trigger should be rejected safely by the single typing guard
            Task task2 = service.TriggerClipTypeAsync();

            await Task.WhenAll(task1, task2);

            Assert.False(service.IsTyping);
            Assert.Equal(1, fakeSender.SendTextCallCount);
        }

        [Fact]
        public async Task TypingService_CancelTyping_RequestsCancellation()
        {
            var fakeSender = new FakeKeyboardSender { SimulatedDelayMs = 50 };
            var fakeClipboard = new FakeClipboardReader { TextToReturn = "Cancel test long string" };
            var fakePrompt = new FakeUserPrompt();
            var service = new TypingService(
                keyboardSender: fakeSender,
                clipboardReader: fakeClipboard,
                userPrompt: fakePrompt);

            // Start typing
            Task run = service.TriggerClipTypeAsync();
            // Cancel immediately
            service.CancelTyping();

            await run;
            Assert.False(service.IsTyping);
        }

        [Fact]
        public async Task TypingService_EmptyClipboard_FiresClipboardEmptyEvent_AndDoesNotType()
        {
            var fakeSender = new FakeKeyboardSender();
            var fakeClipboard = new FakeClipboardReader { TextToReturn = "" };
            var fakePrompt = new FakeUserPrompt();
            var service = new TypingService(
                keyboardSender: fakeSender,
                clipboardReader: fakeClipboard,
                userPrompt: fakePrompt);

            bool emptyFired = false;
            service.ClipboardEmpty += () => emptyFired = true;

            await service.TriggerClipTypeAsync();

            Assert.True(emptyFired);
            Assert.Equal(0, fakeSender.SendTextCallCount);
        }

        [Fact]
        public async Task TypingService_LargeTextThreshold_UserDeclined_AbortsWithoutTyping()
        {
            var fakeSender = new FakeKeyboardSender();
            var fakeClipboard = new FakeClipboardReader { TextToReturn = new string('A', 6000) };
            var fakePrompt = new FakeUserPrompt { ConfirmationResult = false };
            var service = new TypingService(
                keyboardSender: fakeSender,
                clipboardReader: fakeClipboard,
                userPrompt: fakePrompt);

            await service.TriggerClipTypeAsync();

            Assert.Equal(1, fakePrompt.ConfirmationCount);
            Assert.Equal(0, fakeSender.SendTextCallCount);
        }

        [Fact]
        public async Task TypingService_LargeTextThreshold_UserAccepted_TypesSuccessfully()
        {
            var fakeSender = new FakeKeyboardSender { SimulatedDelayMs = 0 };
            var fakeClipboard = new FakeClipboardReader { TextToReturn = new string('B', 6000) };
            var fakePrompt = new FakeUserPrompt { ConfirmationResult = true };
            var service = new TypingService(
                keyboardSender: fakeSender,
                clipboardReader: fakeClipboard,
                userPrompt: fakePrompt);

            await service.TriggerClipTypeAsync();

            Assert.Equal(1, fakePrompt.ConfirmationCount);
            Assert.Equal(1, fakeSender.SendTextCallCount);
            Assert.Equal(6000, fakeSender.LastSentText.Length);
        }

        [Fact]
        public async Task TypingService_CredentialAutoType_TrimsTrailingNewlineFromStage2()
        {
            var fakeSender = new FakeKeyboardSender { SimulatedDelayMs = 0 };
            var fakeClipboard = new FakeClipboardReader { TextToReturn = "admin\tsecretPass123\r\n" };
            var fakePrompt = new FakeUserPrompt();
            var service = new TypingService(
                keyboardSender: fakeSender,
                clipboardReader: fakeClipboard,
                userPrompt: fakePrompt);

            // Temporarily enable auto-detect credential mode
            var origMode = SettingsManager.Current.CredentialAutoTypeMode;
            try
            {
                SettingsManager.Current.CredentialAutoTypeMode = CredentialMode.AutoDetect;
                SettingsManager.Current.CredentialStageDelayMs = 0;

                await service.TriggerClipTypeAsync();

                Assert.Equal(2, fakeSender.SendTextCallCount); // Stage 1 (user) + Stage 2 (pass)
                Assert.Equal(1, fakeSender.SendVirtualKeyCallCount); // Tab transition
                Assert.Equal("secretPass123", fakeSender.LastSentText); // Trailing \r\n trimmed (TD-76)
            }
            finally
            {
                SettingsManager.Current.CredentialAutoTypeMode = origMode;
            }
        }

        [Theory]
        [InlineData(false, true)]  // clipboard still holds the typed credentials -> cleared
        [InlineData(true, false)]  // user copied something else meanwhile -> left alone
        public async Task TypingService_CredentialAutoClear_OnlyClearsWhatWasTyped(bool clipboardChanged, bool expectCleared)
        {
            var fakeSender = new FakeKeyboardSender { SimulatedDelayMs = 0 };
            var fakeClipboard = new FakeClipboardReader { TextToReturn = "admin\tsecretPass123" };
            var service = new TypingService(
                keyboardSender: fakeSender,
                clipboardReader: fakeClipboard,
                userPrompt: new FakeUserPrompt());

            var before = SettingsManager.Current.Clone();
            try
            {
                SettingsManager.Current.CredentialAutoTypeMode = CredentialMode.TabOnly;
                SettingsManager.Current.CredentialStageDelayMs = 0;
                SettingsManager.Current.CredentialAutoClearClipboard = true;
                SettingsManager.Current.CredentialAutoClearDelaySeconds = 1;

                await service.TriggerClipTypeAsync();
                if (clipboardChanged)
                {
                    fakeClipboard.TextToReturn = "something copied afterwards";
                }

                await Task.Delay(1600);

                Assert.Equal(expectCleared, fakeClipboard.ClearCalled);
            }
            finally
            {
                SettingsManager.Current.CopyFrom(before);
            }
        }

        private static TypingService CreateVkService(
            FakeKeyboardSender sender,
            FakeClipboardReader clipboard,
            FakeUserPrompt prompt,
            FakeKeyboardLayoutProvider provider)
        {
            return new TypingService(
                keyboardSender: sender,
                clipboardReader: clipboard,
                userPrompt: prompt,
                layoutProvider: provider);
        }

        private static void EnableVkMode(string targetLayout)
        {
            SettingsManager.Current.EnableVkCompatibilityMode = true;
            SettingsManager.Current.TargetKeyboardLayout = targetLayout;
            SettingsManager.Current.CredentialAutoTypeMode = CredentialMode.Off;
            SettingsManager.Current.CredentialStageDelayMs = 0;
        }

        [Fact]
        public async Task TypingService_VkMode_MissingCharacters_AsksFirst_AndDoesNotTypeWhenDeclined()
        {
            var sender = new FakeKeyboardSender { SimulatedDelayMs = 0 };
            var prompt = new FakeUserPrompt { ConfirmationResult = false };
            var layout = FakeKeyboardLayout.Basic();
            var provider = new FakeKeyboardLayoutProvider { Layout = layout };
            var service = CreateVkService(sender, new FakeClipboardReader { TextToReturn = "p\u00e4ssword" }, prompt, provider);

            var before = SettingsManager.Current.Clone();
            try
            {
                EnableVkMode("00000409");

                await service.TriggerClipTypeAsync();

                Assert.Equal("00000409", provider.LastKlid);
                Assert.Equal(1, prompt.ConfirmationCount);
                var request = prompt.LastConfirmation!;
                Assert.Contains("English (United States)", request.Text, StringComparison.Ordinal);
                // The characters are only in the collapsed details, never in the visible text
                Assert.DoesNotContain("\u00e4", request.Heading + request.Text, StringComparison.Ordinal);
                Assert.Contains("\u00e4", request.Details, StringComparison.Ordinal);
                Assert.False(request.ConfirmIsDefault);
                Assert.Equal(0, sender.SendTextCallCount);
                Assert.True(layout.Disposed);
            }
            finally
            {
                SettingsManager.Current.CopyFrom(before);
            }
        }

        [Fact]
        public async Task TypingService_VkMode_MissingCharacters_TypesWithTargetLayoutWhenConfirmed()
        {
            var sender = new FakeKeyboardSender { SimulatedDelayMs = 0 };
            var prompt = new FakeUserPrompt { ConfirmationResult = true };
            var layout = FakeKeyboardLayout.Basic();
            var provider = new FakeKeyboardLayoutProvider { Layout = layout };
            var service = CreateVkService(sender, new FakeClipboardReader { TextToReturn = "p\u00e4ssword" }, prompt, provider);

            var before = SettingsManager.Current.Clone();
            try
            {
                EnableVkMode("00000409");

                await service.TriggerClipTypeAsync();

                Assert.Equal(1, sender.SendTextCallCount);
                Assert.NotNull(sender.LastVkLayout);
                Assert.True(layout.Disposed);
            }
            finally
            {
                SettingsManager.Current.CopyFrom(before);
            }
        }

        [Fact]
        public async Task TypingService_VkMode_AllCharactersOnLayout_TypesWithoutAsking()
        {
            var sender = new FakeKeyboardSender { SimulatedDelayMs = 0 };
            var prompt = new FakeUserPrompt();
            var provider = new FakeKeyboardLayoutProvider { Layout = FakeKeyboardLayout.Basic() };
            var service = CreateVkService(sender, new FakeClipboardReader { TextToReturn = "Admin 2026\r\nnext line" }, prompt, provider);

            var before = SettingsManager.Current.Clone();
            try
            {
                EnableVkMode(KeyboardLayouts.SameAsThisPc);

                await service.TriggerClipTypeAsync();

                Assert.Equal(0, prompt.ConfirmationCount);
                Assert.Equal(1, sender.SendTextCallCount);
                Assert.NotNull(sender.LastVkLayout);
            }
            finally
            {
                SettingsManager.Current.CopyFrom(before);
            }
        }

        [Fact]
        public async Task TypingService_VkMode_CredentialsWithMissingCharacters_NeverShowsTheCharacters()
        {
            var sender = new FakeKeyboardSender { SimulatedDelayMs = 0 };
            var prompt = new FakeUserPrompt { ConfirmationResult = false };
            var provider = new FakeKeyboardLayoutProvider { Layout = FakeKeyboardLayout.Basic() };
            var service = CreateVkService(sender, new FakeClipboardReader { TextToReturn = "admin\tp\u00e4ss\u00a7word" }, prompt, provider);

            var before = SettingsManager.Current.Clone();
            try
            {
                EnableVkMode("00000409");
                SettingsManager.Current.CredentialAutoTypeMode = CredentialMode.TabOnly;

                await service.TriggerClipTypeAsync();

                Assert.Equal(1, prompt.ConfirmationCount);
                Assert.Contains("2 character(s)", prompt.LastConfirmationMessage, StringComparison.Ordinal);
                Assert.Null(prompt.LastConfirmation!.Details);
                Assert.DoesNotContain("\u00e4", prompt.LastConfirmationMessage, StringComparison.Ordinal);
                Assert.DoesNotContain("\u00a7", prompt.LastConfirmationMessage, StringComparison.Ordinal);
                Assert.Equal(0, sender.SendTextCallCount);
            }
            finally
            {
                SettingsManager.Current.CopyFrom(before);
            }
        }

        [Fact]
        public async Task TypingService_VkMode_LayoutCannotBeLoaded_WarnsAndDoesNotType()
        {
            var sender = new FakeKeyboardSender { SimulatedDelayMs = 0 };
            var prompt = new FakeUserPrompt();
            var provider = new FakeKeyboardLayoutProvider { Layout = null };
            var service = CreateVkService(sender, new FakeClipboardReader { TextToReturn = "password" }, prompt, provider);

            var before = SettingsManager.Current.Clone();
            try
            {
                EnableVkMode("00000405");

                await service.TriggerClipTypeAsync();

                Assert.Equal(1, prompt.WarningCount);
                Assert.Contains("Czech", prompt.LastWarningMessage, StringComparison.Ordinal);
                Assert.Equal(0, sender.SendTextCallCount);
            }
            finally
            {
                SettingsManager.Current.CopyFrom(before);
            }
        }

        [Fact]
        public async Task TypingService_UnicodeMode_DoesNotOpenAKeyboardLayout()
        {
            var sender = new FakeKeyboardSender { SimulatedDelayMs = 0 };
            var provider = new FakeKeyboardLayoutProvider { Layout = FakeKeyboardLayout.Basic() };
            var service = CreateVkService(sender, new FakeClipboardReader { TextToReturn = "p\u00e4ssword" }, new FakeUserPrompt(), provider);

            var before = SettingsManager.Current.Clone();
            try
            {
                SettingsManager.Current.EnableVkCompatibilityMode = false;
                SettingsManager.Current.CredentialAutoTypeMode = CredentialMode.Off;

                await service.TriggerClipTypeAsync();

                Assert.Equal(0, provider.OpenCount);
                Assert.Equal(1, sender.SendTextCallCount);
                Assert.Null(sender.LastVkLayout);
            }
            finally
            {
                SettingsManager.Current.CopyFrom(before);
            }
        }
    }
}
