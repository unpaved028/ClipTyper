using System;
using System.Windows.Forms;

namespace ClipTyper
{
    /// <summary>
    /// Abstraction for user dialogs, preventing test runners from hanging on dialogs.
    /// </summary>
    public interface IUserPrompt
    {
        /// <summary>
        /// Shows a warning. Non-blocking (tray notification) when a notifier is available,
        /// so a hidden dialog can never block the next typing run.
        /// </summary>
        void ShowWarning(string message, string caption);

        /// <summary>
        /// Asks a question and returns true if the user chose the confirm button.
        /// </summary>
        bool Confirm(ConfirmationRequest request);
    }

    /// <summary>
    /// Default implementation: tray notifications for warnings, task dialogs for questions.
    /// </summary>
    public class WindowsUserPrompt : IUserPrompt
    {
        private readonly Action<string, string>? _notify;

        /// <param name="notify">
        /// Shows a non-blocking notification (title, message), e.g. a tray balloon.
        /// Without it, warnings fall back to a dialog.
        /// </param>
        public WindowsUserPrompt(Action<string, string>? notify = null)
        {
            _notify = notify;
        }

        public void ShowWarning(string message, string caption)
        {
            if (_notify != null)
            {
                _notify(caption, message);
                return;
            }

            using var owner = Dialogs.CreateTopMostOwner();
            Dialogs.Show(owner, TaskDialogIcon.Warning, caption, message);
        }

        public bool Confirm(ConfirmationRequest request)
        {
            // Owned by an invisible TopMost window so the question appears above fullscreen
            // RDP / console windows instead of opening unseen behind them.
            using var owner = Dialogs.CreateTopMostOwner();
            return Dialogs.Confirm(owner, request);
        }
    }
}
