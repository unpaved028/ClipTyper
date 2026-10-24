using System.Drawing;
using System.Windows.Forms;

namespace ClipTyper
{
    /// <summary>
    /// A Yes/No style question shown as a task dialog: heading, explanation, a named confirm
    /// button, and optional details that stay collapsed until the user expands them.
    /// </summary>
    public sealed record ConfirmationRequest(
        string Caption,
        string Heading,
        string Text,
        string ConfirmButtonText,
        bool ConfirmIsDefault = true,
        string? Details = null,
        string DetailsButtonText = "Show details");

    /// <summary>
    /// Modern Windows task dialogs for ClipTyper's messages and questions.
    /// </summary>
    internal static class Dialogs
    {
        public const string Caption = "ClipTyper";

        public static void Show(IWin32Window? owner, TaskDialogIcon icon, string heading, string text, string caption = Caption)
        {
            var page = new TaskDialogPage
            {
                Caption = caption,
                Heading = heading,
                Text = text,
                Icon = icon,
                AllowCancel = true,
                SizeToContent = true,
                // Own button instead of the standard one: Windows would translate it, ClipTyper is English
                Buttons = { new TaskDialogButton("OK") }
            };
            ShowPage(owner, page);
        }

        public static bool Confirm(IWin32Window? owner, ConfirmationRequest request)
        {
            var page = BuildConfirmationPage(request, out var confirm);
            return ShowPage(owner, page) == confirm;
        }

        /// <summary>
        /// Builds the question page. The same button instances are used in <c>Buttons</c>,
        /// <c>DefaultButton</c> and for comparing the result. Both buttons are custom ones, so they are
        /// English like the rest of ClipTyper (the standard Cancel button follows the Windows language);
        /// Esc and the close button still cancel because <c>AllowCancel</c> is set.
        /// </summary>
        internal static TaskDialogPage BuildConfirmationPage(ConfirmationRequest request, out TaskDialogButton confirm)
        {
            confirm = new TaskDialogButton(request.ConfirmButtonText);
            var cancel = new TaskDialogButton("Cancel");
            var page = new TaskDialogPage
            {
                Caption = request.Caption,
                Heading = request.Heading,
                Text = request.Text,
                Icon = TaskDialogIcon.Warning,
                AllowCancel = true,
                SizeToContent = true,
                Buttons = { confirm, cancel },
                DefaultButton = request.ConfirmIsDefault ? confirm : cancel
            };
            if (!string.IsNullOrEmpty(request.Details))
            {
                page.Expander = new TaskDialogExpander(request.Details)
                {
                    CollapsedButtonText = request.DetailsButtonText,
                    ExpandedButtonText = request.DetailsButtonText.Replace("Show", "Hide"),
                    Position = TaskDialogExpanderPosition.AfterText
                };
            }
            return page;
        }

        /// <summary>
        /// An invisible 1x1 TopMost window in the middle of the monitor the user works on. Dialogs owned
        /// by it open above fullscreen RDP / console windows instead of unseen behind them, centered
        /// on that monitor.
        /// </summary>
        public static Form CreateTopMostOwner()
        {
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

        private static TaskDialogButton ShowPage(IWin32Window? owner, TaskDialogPage page)
        {
            return owner != null
                ? TaskDialog.ShowDialog(owner, page, TaskDialogStartupLocation.CenterOwner)
                : TaskDialog.ShowDialog(page, TaskDialogStartupLocation.CenterScreen);
        }
    }
}
