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

        // VK Key mapping P/Invokes for compatibility mode
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern short VkKeyScanW(char ch);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

        // Process elevation & UIPI P/Invokes
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass, out int TokenInformation, int TokenInformationLength, out int ReturnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint TOKEN_QUERY = 0x0008;
        private const int TokenElevation = 20;
        private const int ERROR_ACCESS_DENIED = 5;

        /// <summary>
        /// Checks if the process of the given window is running with higher privileges
        /// (Elevated / Admin) while ClipTyper is running non-elevated (UIPI restriction).
        /// </summary>
        public static bool IsTargetWindowElevated(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;

            GetWindowThreadProcessId(hWnd, out uint targetPid);
            if (targetPid == 0) return false;

            bool currentIsElevated = IsProcessElevatedInternal(System.Diagnostics.Process.GetCurrentProcess().Handle);
            if (currentIsElevated) return false; // If ClipTyper is Admin, UIPI won't block it

            IntPtr hTargetProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, targetPid);
            if (hTargetProcess == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                if (error == ERROR_ACCESS_DENIED)
                {
                    // Access denied trying to query target process -> Target is elevated
                    return true;
                }
                return false;
            }

            try
            {
                bool targetIsElevated = IsProcessElevatedInternal(hTargetProcess);
                return targetIsElevated && !currentIsElevated;
            }
            finally
            {
                CloseHandle(hTargetProcess);
            }
        }

        private static bool IsProcessElevatedInternal(IntPtr hProcess)
        {
            if (!OpenProcessToken(hProcess, TOKEN_QUERY, out IntPtr hToken))
                return false;

            try
            {
                if (GetTokenInformation(hToken, TokenElevation, out int isElevated, sizeof(int), out _))
                {
                    return isElevated != 0;
                }
            }
            finally
            {
                CloseHandle(hToken);
            }
            return false;
        }
    }
}
