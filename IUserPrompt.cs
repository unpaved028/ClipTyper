using System;
using System.Windows.Forms;

namespace ClipTyper
{
    /// <summary>
    /// Abstraction for user dialogs, preventing test runners from hanging on MessageBox calls.
    /// </summary>
    public interface IUserPrompt
    {
        /// <summary>
        /// Displays an informational or warning dialog.
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
        public void ShowWarning(string message, string caption)
        {
            MessageBox.Show(
                message,
                caption,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        public bool ShowConfirmation(string message, string caption)
        {
            var result = MessageBox.Show(
                message,
                caption,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            return result == DialogResult.Yes;
        }
    }
}
