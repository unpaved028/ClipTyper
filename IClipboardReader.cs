using System;
using System.Threading;
using System.Windows.Forms;

namespace ClipTyper
{
    /// <summary>
    /// Abstraction for clipboard operations, allowing test isolation without touching the OS clipboard.
    /// </summary>
    public interface IClipboardReader
    {
        /// <summary>
        /// Reads text from the clipboard safely on an STA thread.
        /// </summary>
        string ReadText(bool enforcePlainText = false);

        /// <summary>
        /// Clears clipboard contents.
        /// </summary>
        void Clear();
    }

    /// <summary>
    /// Default Windows implementation of <see cref="IClipboardReader"/> using Windows Forms Clipboard.
    /// </summary>
    public class WindowsClipboardReader : IClipboardReader
    {
        public string ReadText(bool enforcePlainText = false)
        {
            string text = string.Empty;
            try
            {
                if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
                {
                    text = GetClipboardTextInternal(enforcePlainText);
                }
                else
                {
                    var thread = new Thread(() =>
                    {
                        text = GetClipboardTextInternal(enforcePlainText);
                    });
                    thread.SetApartmentState(ApartmentState.STA);
                    thread.Start();
                    thread.Join(2000);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Clipboard read error", ex);
            }
            return text;
        }

        public void Clear()
        {
            try
            {
                if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
                {
                    Clipboard.Clear();
                }
                else
                {
                    var thread = new Thread(() =>
                    {
                        try { Clipboard.Clear(); } catch { }
                    });
                    thread.SetApartmentState(ApartmentState.STA);
                    thread.Start();
                    thread.Join(2000);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Clipboard clear error", ex);
            }
        }

        private static string GetClipboardTextInternal(bool enforcePlainText)
        {
            if (!Clipboard.ContainsText())
            {
                return string.Empty;
            }

            if (enforcePlainText)
            {
                // Prefer Unicode plain text; fallback to standard text if empty
                string txt = Clipboard.GetText(TextDataFormat.UnicodeText);
                if (string.IsNullOrEmpty(txt))
                {
                    txt = Clipboard.GetText(TextDataFormat.Text);
                }
                return txt ?? string.Empty;
            }

            return Clipboard.GetText() ?? string.Empty;
        }
    }
}
