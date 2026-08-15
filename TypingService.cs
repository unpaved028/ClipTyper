using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClipTyper
{
    /// <summary>
    /// Encapsulates single-instance typing execution, clipboard extraction, safety checks,
    /// two-stage credential typing, thread-safe UI dialog marshalling, and progress notification.
    /// </summary>
    public class TypingService
    {
        private readonly SynchronizationContext? _syncContext;
        private int _isTypingFlag = 0; // 0 = idle, 1 = typing active

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

        public TypingService(SynchronizationContext? syncContext = null)
        {
            _syncContext = syncContext ?? SynchronizationContext.Current;
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

            try
            {
                await Task.Run(() => PerformClipType());
            }
            finally
            {
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
                    MessageBox.Show(
                        "The target window is running with Administrator privileges.\n\nClipTyper is currently running without Administrator privileges. Windows is blocking keyboard input to this window.\n\nPlease launch ClipTyper as Administrator as well.",
                        "ClipTyper - Administrator Privileges Required",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                });
                return;
            }

            var settings = SettingsManager.Current;

            // 2. Read text from Clipboard safely (respecting plain text enforcement)
            string textToType = ReadClipboardText(settings.EnforcePlainText);
            if (string.IsNullOrEmpty(textToType))
            {
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
                    var res = MessageBox.Show(
                        $"The clipboard text contains {textToType.Length:N0} characters.\n\nTyping will take approximately {timeStr}.\n\nDo you want to proceed with typing?",
                        "ClipTyper - Large Text",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    userConfirmed = (res == DialogResult.Yes);
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

            // 4. Check for Two-Stage Credential Auto-Type
            if (CredentialParser.TrySplit(
                textToType,
                settings.CredentialAutoTypeMode,
                settings.CredentialCustomDelimiter,
                out var credResult) && credResult != null)
            {
                PerformCredentialAutoType(credResult, settings, targetHWnd);
                return;
            }

            // 5. Standard Single-Stage Typing Simulation
            PostToUI(() => ProgressChanged?.Invoke(true, 0, textToType.Length));

            TypeResult result = KeyboardSimulator.SendText(
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
                }
            );

            PostToUI(() =>
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
            // Security: Never log actual credential content!
            Logger.LogInfo($"Starting Credential Auto-Type: Stage 1 ({cred.Part1.Length} chars), Stage 2 ({cred.Part2.Length} chars).");

            int totalChars = cred.Part1.Length + cred.Part2.Length;
            PostToUI(() => ProgressChanged?.Invoke(true, 0, totalChars));

            // ── Stage 1: Username ──
            TypeResult r1 = KeyboardSimulator.SendText(
                cred.Part1,
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
                }
            );

            if (r1 != TypeResult.Completed)
            {
                PostToUI(() =>
                {
                    ProgressChanged?.Invoke(false, 0, 0);
                    TypingCompleted?.Invoke(false);
                });
                HandleTypingResult(r1, cred.Part1.Length, settings);
                return;
            }

            // ── Transition: Send Tab or Enter ──
            KeyboardSimulator.SendVirtualKey(cred.TransitionVk, cred.TransitionNeedsShift);

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
                    MessageBox.Show(
                        "Credential typing was cancelled because the active window changed before typing the password.\n\nPlease refocus the target window and try again.",
                        "ClipTyper - Typing Cancelled",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                });
                return;
            }

            // ── Stage 2: Password ──
            TypeResult r2 = KeyboardSimulator.SendText(
                cred.Part2,
                delayMs: settings.KeystrokeDelayMs,
                targetHWnd: targetHWnd,
                enableVkMode: settings.EnableVkCompatibilityMode,
                sanitize: settings.SanitizeInput,
                newlineMode: NewlineMode.Enter,
                enableJitter: settings.EnableTypingJitter,
                jitterRangeMs: settings.TypingJitterRangeMs,
                onProgress: (current, _) =>
                {
                    PostToUI(() => ProgressChanged?.Invoke(true, cred.Part1.Length + current, totalChars));
                }
            );

            PostToUI(() =>
            {
                ProgressChanged?.Invoke(false, 0, 0);
                TypingCompleted?.Invoke(r2 == TypeResult.Completed);
            });

            HandleTypingResult(r2, totalChars, settings);

            // ── Optional Auto-Clear of Clipboard ──
            if (r2 == TypeResult.Completed && settings.CredentialAutoClearClipboard)
            {
                ScheduleClipboardClear(settings.CredentialAutoClearDelaySeconds);
            }
        }

        private void HandleTypingResult(TypeResult result, int charCount, AppSettings settings)
        {
            if (result == TypeResult.Completed)
            {
                if (settings.SoundFeedbackEnabled)
                {
                    try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
                }
                Logger.LogInfo($"Successfully typed {charCount} characters.");
            }
            else if (result == TypeResult.FocusLost)
            {
                Logger.LogWarning("Typing process cancelled due to focus loss.");
                PostToUISync(() =>
                {
                    MessageBox.Show(
                        "Typing was cancelled because the active window changed.\n\nPlease refocus the target window and try again.",
                        "ClipTyper - Typing Cancelled",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                });
            }
            else if (result == TypeResult.AbortedByEscape)
            {
                Logger.LogInfo("Typing cancelled by pressing Escape.");
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
                        Clipboard.Clear();
                        Logger.LogInfo("Clipboard automatically cleared after credential auto-type.");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("Failed to clear clipboard", ex);
                    }
                });
            });
        }

        public static string ReadClipboardText(bool enforcePlainText = false)
        {
            string textToType = "";
            try
            {
                if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
                {
                    if (Clipboard.ContainsText())
                    {
                        textToType = enforcePlainText
                            ? Clipboard.GetText(TextDataFormat.UnicodeText)
                            : Clipboard.GetText();
                    }
                }
                else
                {
                    var thread = new Thread(() =>
                    {
                        if (Clipboard.ContainsText())
                        {
                            textToType = enforcePlainText
                                ? Clipboard.GetText(TextDataFormat.UnicodeText)
                                : Clipboard.GetText();
                        }
                    });
                    thread.SetApartmentState(ApartmentState.STA);
                    thread.Start();
                    thread.Join(2000);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Clipboard error", ex);
            }
            return textToType;
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
    }
}
