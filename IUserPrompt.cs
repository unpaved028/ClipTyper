using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClipTyper
{
    /// <summary>
    /// Abstraction for user dialogs, preventing test runners from hanging on MessageBox calls.
    /// </summary>
    public interface IUserPrompt
    {
        /// <summary>
        /// Shows a warning. Non-blocking (tray notification) when a notifier is available,
        /// so a hidden dialog can never block the next typing run.
        /// </summary>
        void ShowWarning(string message, string caption);

        /// <summary>
        /// Displays a Yes/No confirmation dialog and returns true if confirmed (Yes).
        /// </summary>
        bool ShowConfirmation(string message, string caption);
    }

    /// <summary>
    /// Default Windows Forms implementation of <see cref="IUserPrompt"/>.
    /// </summary>
    public class WindowsUserPrompt : IUserPrompt
    {
        private readonly Action<string, string>? _notify;

        /// <param name="notify">
        /// Shows a non-blocking notification (title, message), e.g. a tray balloon.
        /// Without it, warnings fall back to a message box.
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

            using var owner = CreateTopMostOwner();
            MessageBox.Show(owner, message, caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public bool ShowConfirmation(string message, string caption)
        {
            // Owned by an invisible TopMost window so the question appears above fullscreen
            // RDP / console windows instead of opening unseen behind them.
            using var owner = CreateTopMostOwner();
            var result = MessageBox.Show(owner, message, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            return result == DialogResult.Yes;
        }

        private static Form CreateTopMostOwner()
        {
            // Message boxes are centered on their owner: put the invisible 1x1 owner in the middle of
            // the monitor the user is working on (the target window's monitor).
            var area = Screen.FromHandle(NativeMethods.GetForegroundWindow()).WorkingArea;
            var owner = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(area.Left + area.Width / 2, area.Top + area.Height / 2),
                Size = new Size(1, 1),
                Opacity = 0,
                TopMost = true
            };
            owner.Show();
            owner.Activate();
            return owner;
        }
    }
}
