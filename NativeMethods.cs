using System;
using System.Runtime.InteropServices;

namespace ClipTyper
{
    /// <summary>
    /// P/Invoke declarations for window focus management.
    /// Used by the overlay to track and restore the foreground window
    /// before triggering clip-type.
    /// </summary>
    public static class NativeMethods
    {
        /// <summary>
        /// Retrieves a handle to the foreground window (the window with which
        /// the user is currently working).
        /// </summary>
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        /// <summary>
        /// Brings the thread that created the specified window into the
        /// foreground and activates the window.
        /// Note: This may fail silently if the calling process is not the
        /// foreground process. Use AttachThreadInput as a workaround.
        /// </summary>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        /// <summary>
        /// Retrieves the identifier of the thread that created the specified
        /// window and, optionally, the process that created the window.
        /// </summary>
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        /// <summary>
        /// Attaches or detaches the input processing mechanism of one thread
        /// to that of another thread. This is required as a workaround for
        /// SetForegroundWindow restrictions — Windows only allows a process to
        /// set the foreground window if it is the foreground process or
        /// attached to the foreground thread.
        /// </summary>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        /// <summary>
        /// Retrieves the thread identifier of the calling thread.
        /// </summary>
        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        /// <summary>
        /// Reliably sets the foreground window by first attaching to the
        /// target window's input thread. This works around the Windows
        /// restriction that prevents background processes from stealing focus.
        /// </summary>
        public static bool ForceForegroundWindow(IntPtr hWnd)
        {
            uint currentThreadId = GetCurrentThreadId();
            uint targetThreadId = GetWindowThreadProcessId(hWnd, out _);

            if (currentThreadId != targetThreadId)
            {
                AttachThreadInput(currentThreadId, targetThreadId, true);
                bool result = SetForegroundWindow(hWnd);
                AttachThreadInput(currentThreadId, targetThreadId, false);
                return result;
            }

            return SetForegroundWindow(hWnd);
        }
    }
}
