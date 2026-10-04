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
        public int SimulatedDelayMs { get; set; } = 10;
        public ushort LastVirtualKey { get; private set; }

        public TypeResult SendText(
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
            SendTextCallCount++;
            LastSentText = text;

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

        public string ReadText(bool enforcePlainText = false)
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

        public void ShowWarning(string message, string caption)
        {
            WarningCount++;
            LastWarningMessage = message;
        }

        public bool ShowConfirmation(string message, string caption)
        {
            ConfirmationCount++;
            LastConfirmationMessage = message;
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
    }
}
