using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly IKeyboardLayoutProvider _layoutProvider;
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
            IUserPrompt? userPrompt = null,
            IKeyboardLayoutProvider? layoutProvider = null)
        {
            _syncContext = syncContext ?? SynchronizationContext.Current;
            _keyboardSender = keyboardSender ?? new WindowsKeyboardSender();
            _clipboardReader = clipboardReader ?? new WindowsClipboardReader();
            _userPrompt = userPrompt ?? new WindowsUserPrompt();
            _layoutProvider = layoutProvider ?? new WindowsKeyboardLayoutProvider();
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
            catch (Exception ex)
            {
                // Callers fire and forget this task, so an exception would otherwise vanish unlogged
                // and leave the overlay/tray stuck in the "typing" state.
                Logger.LogError("Clip-type failed", ex);
                PostToUI(() =>
                {
                    ProgressChanged?.Invoke(false, 0, 0);
                    TypingCompleted?.Invoke(false);
                });
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
                        "The target window runs as Administrator, so Windows blocks typed input from ClipTyper. Start ClipTyper as Administrator to type into this window.",
                        "ClipTyper - Administrator window");
                });
                return;
            }

            var settings = SettingsManager.Current;

            // 2. Read plain text from Clipboard safely
            string textToType = _clipboardReader.ReadText();
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
            CredentialParser.TrySplit(
                textToType,
                settings.CredentialAutoTypeMode,
                settings.CredentialCustomDelimiter,
                out var credResult,
                settings.CredentialCustomTransitionKey);

            // 5. VK compatibility mode: open the target keyboard layout for the whole typing run
            IKeyboardLayout? layout = null;
            try
            {
                KeyboardLayoutMapper? vkLayout = null;
                if (settings.EnableVkCompatibilityMode)
                {
                    layout = _layoutProvider.Open(settings.TargetKeyboardLayout, targetHWnd);
                    if (layout == null)
                    {
                        Logger.LogWarning($"Target keyboard layout '{settings.TargetKeyboardLayout}' could not be loaded.");
                        PostToUISync(() =>
                        {
                            _userPrompt.ShowWarning(
                                $"The keyboard layout \"{KeyboardLayouts.GetDisplayName(settings.TargetKeyboardLayout)}\" could not be loaded, so nothing was typed. Choose a different target keyboard in Settings.",
                                "ClipTyper - Keyboard layout");
                        });
                        return;
                    }

                    vkLayout = new KeyboardLayoutMapper(layout);
                    var textsToCheck = credResult != null
                        ? new[] { credResult.Part1, credResult.Part2 }
                        : new[] { textToType };
                    if (!ConfirmUnmappableCharacters(vkLayout, textsToCheck, settings, isCredential: credResult != null, targetHWnd))
                    {
                        return;
                    }
                }

                if (credResult != null)
                {
                    PerformCredentialAutoType(credResult, settings, targetHWnd, textToType, vkLayout);
                    return;
                }

                PerformStandardType(textToType, settings, targetHWnd, vkLayout);
            }
            finally
            {
                layout?.Dispose();
            }
        }

        /// <summary>
        /// Warns before typing when characters have no key on the target layout. Returns false
        /// when the user cancels. Credential characters are never shown, only counted.
        /// </summary>
        private bool ConfirmUnmappableCharacters(
            KeyboardLayoutMapper vkLayout,
            IEnumerable<string> texts,
            AppSettings settings,
            bool isCredential,
            IntPtr targetHWnd)
        {
            var unmappable = texts
                .Select(t => settings.SanitizeInput ? KeyboardSimulator.SanitizeText(t) : t)
                .SelectMany(vkLayout.FindUnmappable)
                .Distinct()
                .ToList();
            if (unmappable.Count == 0)
            {
                return true;
            }

            Logger.LogWarning($"{unmappable.Count} distinct character(s) have no key on the target keyboard layout.");

            string layoutText = string.IsNullOrEmpty(settings.TargetKeyboardLayout)
                ? "the current keyboard layout"
                : $"the target keyboard layout \"{KeyboardLayouts.GetDisplayName(settings.TargetKeyboardLayout)}\"";
            string which = isCredential
                ? $"The credentials contain {unmappable.Count} character(s) that cannot be typed with {layoutText}."
                : $"These characters cannot be typed with {layoutText}:\n\n{FormatCharacters(unmappable)}";

            bool userConfirmed = false;
            PostToUISync(() =>
            {
                userConfirmed = _userPrompt.ShowConfirmation(
                    which + "\n\nThey would be sent as Unicode characters, which most remote consoles ignore or type incorrectly.\n\nDo you want to type anyway?",
                    "ClipTyper - Characters Not On Keyboard Layout");
            });

            if (!userConfirmed)
            {
                Logger.LogInfo("Typing cancelled by user: characters missing on the target keyboard layout.");
                return false;
            }

            NativeMethods.ForceForegroundWindow(targetHWnd);
            Thread.Sleep(200);
            return true;
        }

        private static string FormatCharacters(List<string> characters)
        {
            const int maxShown = 15;
            var shown = characters.Take(maxShown).Select(c =>
                c.Length == 1 && (char.IsControl(c[0]) || char.IsWhiteSpace(c[0]) || char.GetUnicodeCategory(c[0]) == System.Globalization.UnicodeCategory.Format)
                    ? $"U+{(int)c[0]:X4}"
                    : c);
            string list = string.Join("  ", shown);
            return characters.Count > maxShown ? $"{list}  (+{characters.Count - maxShown} more)" : list;
        }

        private void PerformStandardType(string textToType, AppSettings settings, IntPtr targetHWnd, KeyboardLayoutMapper? vkLayout)
        {
            PostToUI(() => ProgressChanged?.Invoke(true, 0, textToType.Length));

            TypeResult result = _keyboardSender.SendText(
                textToType,
                delayMs: settings.KeystrokeDelayMs,
                targetHWnd: targetHWnd,
                vkLayout: vkLayout,
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
            IntPtr targetHWnd,
            string clipboardText,
            KeyboardLayoutMapper? vkLayout)
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
                vkLayout: vkLayout,
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
                        "The password was not typed because the active window changed after the username. Click into the login field and try again.",
                        "ClipTyper - Typing cancelled");
                });
                return;
            }

            // ── Stage 2: Password ──
            TypeResult r2 = _keyboardSender.SendText(
                stage2Text,
                delayMs: settings.KeystrokeDelayMs,
                targetHWnd: targetHWnd,
                vkLayout: vkLayout,
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
                ScheduleClipboardClear(settings.CredentialAutoClearDelaySeconds, clipboardText);
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
                        "Typing stopped because the active window changed. Click into the target window and try again.",
                        "ClipTyper - Typing cancelled");
                });
            }
            else if (result == TypeResult.AbortedByEscape)
            {
                Logger.LogInfo("Typing cancelled by pressing Escape or programmatic abort.");
            }
        }

        private void ScheduleClipboardClear(int delaySeconds, string typedClipboardText)
        {
            Task.Run(async () =>
            {
                await Task.Delay(Math.Max(1, delaySeconds) * 1000);
                PostToUI(() =>
                {
                    try
                    {
                        // Only clear what was typed: leave anything the user copied in the meantime.
                        if (!string.Equals(_clipboardReader.ReadText(), typedClipboardText, StringComparison.Ordinal))
                        {
                            Logger.LogInfo("Clipboard auto-clear skipped: clipboard content changed since typing.");
                            return;
                        }

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
        public static string ReadClipboardText()
        {
            return new WindowsClipboardReader().ReadText();
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
