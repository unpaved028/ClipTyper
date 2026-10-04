using System;
using System.Threading;
using System.Threading.Tasks;

namespace ClipTyper
{
    /// <summary>
    /// Encapsulates single-instance typing execution, clipboard extraction, safety checks,
    /// two-stage credential typing, thread-safe UI dialog marshalling, and progress notification.
    /// </summary>
    public class TypingService : IDisposable
    {
        private readonly SynchronizationContext? _syncContext;
        private readonly IKeyboardSender _keyboardSender;
        private readonly IClipboardReader _clipboardReader;
        private readonly IUserPrompt _userPrompt;
        private int _isTypingFlag; // 0 = idle, 1 = typing active
        private CancellationTokenSource? _cts;

        /// <summary>
        /// Gets whether a typing session is currently in progress.
        /// </summary>
        public bool IsTyping => Volatile.Read(ref _isTypingFlag) == 1;

        /// <summary>
        /// Event fired to report progress updates (isTyping, currentChars, totalChars).
        /// </summary>
        public event Action<bool, int, int>? ProgressChanged;

        /// <summary>
        /// Event fired when typing completes (true if completed successfully, false if cancelled/aborted).
        /// </summary>
        public event Action<bool>? TypingCompleted;

        /// <summary>
        /// Event fired when a clip-type action was triggered but the clipboard contained no usable text (TD-41).
        /// </summary>
        public event Action? ClipboardEmpty;

        public TypingService(
            SynchronizationContext? syncContext = null,
            IKeyboardSender? keyboardSender = null,
            IClipboardReader? clipboardReader = null,
            IUserPrompt? userPrompt = null)
        {
            _syncContext = syncContext ?? SynchronizationContext.Current;
            _keyboardSender = keyboardSender ?? new WindowsKeyboardSender();
            _clipboardReader = clipboardReader ?? new WindowsClipboardReader();
            _userPrompt = userPrompt ?? new WindowsUserPrompt();
        }

        /// <summary>
        /// Requests cancellation of the currently active typing session (TD-60, TD-69).
        /// </summary>
        public void CancelTyping()
        {
            try
            {
                _cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // CTS was disposed concurrently as typing completed; safe to ignore.
            }
        }

        /// <summary>
        /// Triggers clip-type operation safely. Prevents parallel runs via atomic flag (TD-01).
        /// Reads clipboard, checks UIPI elevation, max length threshold, and executes keyboard simulation.
        /// </summary>
        public async Task TriggerClipTypeAsync()
        {
            // TD-01: Single Typing Guard - atomic check and set
            if (Interlocked.CompareExchange(ref _isTypingFlag, 1, 0) != 0)
            {
                Logger.LogInfo("Clip-type trigger ignored: typing is already in progress.");
                return;
            }

            _cts = new CancellationTokenSource();
            CancellationTokenSource? ctsToDispose = null;

            try
            {
                await Task.Run(() => PerformClipType());
            }
            finally
            {
                ctsToDispose = _cts;
                _cts = null;
                try
                {
                    ctsToDispose?.Dispose();
                }
                catch (ObjectDisposedException) { }

                Interlocked.Exchange(ref _isTypingFlag, 0);
            }
        }

        private void PerformClipType()
        {
            IntPtr targetHWnd = NativeMethods.GetForegroundWindow();

            // 1. UIPI Check: Target window elevated?
            if (NativeMethods.IsTargetWindowElevated(targetHWnd))
            {
                Logger.LogWarning("Target window is elevated (Admin). Input blocked by UIPI.");
                PostToUISync(() =>
                {
                    _userPrompt.ShowWarning(
                        "The target window is running with Administrator privileges.\n\nClipTyper is currently running without Administrator privileges. Windows is blocking keyboard input to this window.\n\nPlease launch ClipTyper as Administrator as well.",
                        "ClipTyper - Administrator Privileges Required");
                });
                return;
            }

            var settings = SettingsManager.Current;

            // 2. Read text from Clipboard safely (respecting plain text enforcement)
            string textToType = _clipboardReader.ReadText(settings.EnforcePlainText);
            if (string.IsNullOrEmpty(textToType))
            {
                Logger.LogInfo("Clip-type trigger ignored: clipboard contains no usable text.");
                PostToUISync(() => ClipboardEmpty?.Invoke());
                return;
            }

            // 3. Max Text Length Check & Confirmation
            if (settings.MaxTextLengthThreshold > 0 && textToType.Length > settings.MaxTextLengthThreshold)
            {
                double estSeconds = (textToType.Length * (settings.KeystrokeDelayMs + 5)) / 1000.0;
                string timeStr = estSeconds >= 60 ? $"{estSeconds / 60:F1} minutes" : $"{estSeconds:F0} seconds";

                bool userConfirmed = false;
                PostToUISync(() =>
                {
                    userConfirmed = _userPrompt.ShowConfirmation(
                        $"The clipboard text contains {textToType.Length:N0} characters.\n\nTyping will take approximately {timeStr}.\n\nDo you want to proceed with typing?",
                        "ClipTyper - Large Text");
                });

                if (!userConfirmed)
                {
                    Logger.LogInfo("Large text typing cancelled by user.");
                    return;
                }

                // Restore focus to target window after user confirmed the dialog
                NativeMethods.ForceForegroundWindow(targetHWnd);
                Thread.Sleep(200);
            }

            // 4. Check for Two-Stage Credential Auto-Type (TD-23: custom transition key)
            if (CredentialParser.TrySplit(
                textToType,
                settings.CredentialAutoTypeMode,
                settings.CredentialCustomDelimiter,
                out var credResult,
                settings.CredentialCustomTransitionKey) && credResult != null)
            {
                PerformCredentialAutoType(credResult, settings, targetHWnd);
                return;
            }

            // 5. Standard Single-Stage Typing Simulation
            PostToUI(() => ProgressChanged?.Invoke(true, 0, textToType.Length));

            TypeResult result = _keyboardSender.SendText(
                textToType,
                delayMs: settings.KeystrokeDelayMs,
                targetHWnd: targetHWnd,
                enableVkMode: settings.EnableVkCompatibilityMode,
                sanitize: settings.SanitizeInput,
                newlineMode: settings.NewlineHandling,
                enableJitter: settings.EnableTypingJitter,
                jitterRangeMs: settings.TypingJitterRangeMs,
                onProgress: (current, total) =>
                {
                    PostToUI(() => ProgressChanged?.Invoke(true, current, total));
                },
                isCancelRequested: () => _cts?.IsCancellationRequested == true
            );

            PostToUISync(() =>
            {
                ProgressChanged?.Invoke(false, 0, 0);
                TypingCompleted?.Invoke(result == TypeResult.Completed);
            });

            HandleTypingResult(result, textToType.Length, settings);
        }

        private void PerformCredentialAutoType(
            CredentialSplitResult cred,
            AppSettings settings,
            IntPtr targetHWnd)
        {
            // Security: Never log actual credential content or lengths (TD-79)!
            Logger.LogInfo("Starting Credential Auto-Type (2 stages).");

            // TD-76: Trim trailing newlines from Stage 2 password to avoid premature form submission
            string stage1Text = cred.Part1;
            string stage2Text = cred.Part2.TrimEnd('\r', '\n');

            int totalChars = stage1Text.Length + stage2Text.Length;
            PostToUI(() => ProgressChanged?.Invoke(true, 0, totalChars));

            // ── Stage 1: Username ──
            TypeResult r1 = _keyboardSender.SendText(
                stage1Text,
                delayMs: settings.KeystrokeDelayMs,
                targetHWnd: targetHWnd,
                enableVkMode: settings.EnableVkCompatibilityMode,
                sanitize: settings.SanitizeInput,
                newlineMode: NewlineMode.Enter,
                enableJitter: settings.EnableTypingJitter,
                jitterRangeMs: settings.TypingJitterRangeMs,
                onProgress: (current, _) =>
                {
                    PostToUI(() => ProgressChanged?.Invoke(true, current, totalChars));
                },
                isCancelRequested: () => _cts?.IsCancellationRequested == true
            );

            if (r1 != TypeResult.Completed)
            {
                PostToUISync(() =>
                {
                    ProgressChanged?.Invoke(false, 0, 0);
                    TypingCompleted?.Invoke(false);
                });
                HandleTypingResult(r1, stage1Text.Length, settings);
                return;
            }

            // ── Transition: Send Tab or Enter ──
            _keyboardSender.SendVirtualKey(cred.TransitionVk, cred.TransitionNeedsShift);

            // ── Stage Delay ──
            if (settings.CredentialStageDelayMs > 0)
            {
                Thread.Sleep(settings.CredentialStageDelayMs);
            }

            // ── Stage 2 Safety Check: Focus verification ──
            if (targetHWnd != IntPtr.Zero && NativeMethods.GetForegroundWindow() != targetHWnd)
            {
                Logger.LogWarning("Credential Auto-Type aborted before Stage 2: target window lost focus.");
                PostToUI(() =>
                {
                    ProgressChanged?.Invoke(false, 0, 0);
                    TypingCompleted?.Invoke(false);
                });
                PostToUISync(() =>
                {
                    _userPrompt.ShowWarning(
                        "Credential typing was cancelled because the active window changed before typing the password.\n\nPlease refocus the target window and try again.",
                        "ClipTyper - Typing Cancelled");
                });
                return;
            }

            // ── Stage 2: Password ──
            TypeResult r2 = _keyboardSender.SendText(
                stage2Text,
                delayMs: settings.KeystrokeDelayMs,
                targetHWnd: targetHWnd,
                enableVkMode: settings.EnableVkCompatibilityMode,
                sanitize: settings.SanitizeInput,
                newlineMode: NewlineMode.Enter,
                enableJitter: settings.EnableTypingJitter,
                jitterRangeMs: settings.TypingJitterRangeMs,
                onProgress: (current, _) =>
                {
                    PostToUI(() => ProgressChanged?.Invoke(true, stage1Text.Length + current, totalChars));
                },
                isCancelRequested: () => _cts?.IsCancellationRequested == true
            );

            PostToUISync(() =>
            {
                ProgressChanged?.Invoke(false, 0, 0);
                TypingCompleted?.Invoke(r2 == TypeResult.Completed);
            });

            HandleTypingResult(r2, totalChars, settings, logCharacterCount: false);

            // ── Optional Auto-Clear of Clipboard ──
            if (r2 == TypeResult.Completed && settings.CredentialAutoClearClipboard)
            {
                ScheduleClipboardClear(settings.CredentialAutoClearDelaySeconds);
            }
        }

        private void HandleTypingResult(TypeResult result, int charCount, AppSettings settings, bool logCharacterCount = true)
        {
            if (result == TypeResult.Completed)
            {
                if (settings.SoundFeedbackEnabled)
                {
                    try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
                }

                if (logCharacterCount)
                {
                    Logger.LogInfo($"Successfully typed {charCount} characters.");
                }
                else
                {
                    Logger.LogInfo("Credential auto-type finished.");
                }
            }
            else if (result == TypeResult.FocusLost)
            {
                Logger.LogWarning("Typing process cancelled due to focus loss.");
                PostToUISync(() =>
                {
                    _userPrompt.ShowWarning(
                        "Typing was cancelled because the active window changed.\n\nPlease refocus the target window and try again.",
                        "ClipTyper - Typing Cancelled");
                });
            }
            else if (result == TypeResult.AbortedByEscape)
            {
                Logger.LogInfo("Typing cancelled by pressing Escape or programmatic abort.");
            }
        }

        private void ScheduleClipboardClear(int delaySeconds)
        {
            Task.Run(async () =>
            {
                await Task.Delay(Math.Max(1, delaySeconds) * 1000);
                PostToUI(() =>
                {
                    try
                    {
                        _clipboardReader.Clear();
                        Logger.LogInfo("Clipboard automatically cleared after credential auto-type.");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("Failed to clear clipboard", ex);
                    }
                });
            });
        }

        /// <summary>
        /// Convenience method to read text directly via <see cref="WindowsClipboardReader"/>.
        /// </summary>
        public static string ReadClipboardText(bool enforcePlainText = false)
        {
            return new WindowsClipboardReader().ReadText(enforcePlainText);
        }

        private void PostToUI(Action action)
        {
            if (_syncContext != null)
            {
                _syncContext.Post(_ => action(), null);
            }
            else
            {
                action();
            }
        }

        private void PostToUISync(Action action)
        {
            if (_syncContext != null)
            {
                _syncContext.Send(_ => action(), null);
            }
            else
            {
                action();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cts?.Dispose();
                _cts = null;
            }
        }
    }
}
