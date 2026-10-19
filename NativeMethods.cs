using System;
using System.Runtime.InteropServices;

namespace ClipTyper
{
    /// <summary>
    /// P/Invoke declarations for window focus management.
    /// Used by the overlay to track and restore the foreground window
    /// before triggering clip-type.
    /// </summary>
    internal static class NativeMethods
    {
        /// <summary>
        /// Retrieves a handle to the foreground window (the window with which
        /// the user is currently working).
        /// </summary>
        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        /// <summary>
        /// Brings the thread that created the specified window into the
        /// foreground and activates the window.
        /// Note: This may fail silently if the calling process is not the
        /// foreground process. Use AttachThreadInput as a workaround.
        /// </summary>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        /// <summary>
        /// Destroys an icon and frees any memory the icon occupied.
        /// Required when creating icons via GetHicon() to prevent GDI leaks.
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyIcon(IntPtr hIcon);

        /// <summary>
        /// Retrieves the identifier of the thread that created the specified
        /// window and, optionally, the process that created the window.
        /// </summary>
        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        /// <summary>
        /// Attaches or detaches the input processing mechanism of one thread
        /// to that of another thread. This is required as a workaround for
        /// SetForegroundWindow restrictions — Windows only allows a process to
        /// set the foreground window if it is the foreground process or
        /// attached to the foreground thread.
        /// </summary>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        /// <summary>
        /// Retrieves the thread identifier of the calling thread.
        /// </summary>
        [DllImport("kernel32.dll")]
        internal static extern uint GetCurrentThreadId();

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

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUNDSMALL = 3;

        /// <summary>
        /// Windows 11: small rounded corners for a popup window (menus). No effect on Windows 10.
        /// </summary>
        public static void RoundWindowCorners(IntPtr hwnd)
        {
            int preference = DWMWCP_ROUNDSMALL;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }

        /// <summary>
        /// Process name of the window's owner (e.g. "mstsc"), or null if it cannot be read.
        /// Used for the diagnostic log only.
        /// </summary>
        public static string? TryGetProcessName(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return null;
            try
            {
                _ = GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == 0) return null;
                using var process = System.Diagnostics.Process.GetProcessById((int)pid);
                return process.ProcessName;
            }
            catch (Exception)
            {
                return null;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BY_HANDLE_FILE_INFORMATION
        {
            public uint FileAttributes;
            public uint CreationTimeLow, CreationTimeHigh;
            public uint LastAccessTimeLow, LastAccessTimeHigh;
            public uint LastWriteTimeLow, LastWriteTimeHigh;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh, FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh, FileIndexLow;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle hFile, out BY_HANDLE_FILE_INFORMATION lpFileInformation);

        /// <summary>
        /// Number of hard links (directory entries) pointing to the file; 1 for a normal file.
        /// </summary>
        public static int GetHardLinkCount(string path)
        {
            using var handle = System.IO.File.OpenHandle(path, System.IO.FileMode.Open, System.IO.FileAccess.Read,
                System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete);
            return GetFileInformationByHandle(handle, out var info) ? (int)info.NumberOfLinks : 1;
        }

        [DllImport("user32.dll")]
        internal static extern short GetKeyState(int nVirtKey);

        private const int VK_CAPITAL = 0x14;

        /// <summary>
        /// Reads the Caps Lock toggle state as the target window's thread sees it. A background
        /// thread's own key state is not updated by input it does not receive, so it attaches to
        /// the target thread's input state for the query.
        /// </summary>
        public static bool IsCapsLockOn(IntPtr targetHWnd)
        {
            uint currentThreadId = GetCurrentThreadId();
            uint targetThreadId = targetHWnd != IntPtr.Zero ? GetWindowThreadProcessId(targetHWnd, out _) : 0;
            bool attached = targetThreadId != 0 && targetThreadId != currentThreadId &&
                            AttachThreadInput(currentThreadId, targetThreadId, true);
            try
            {
                return (GetKeyState(VK_CAPITAL) & 0x0001) != 0;
            }
            finally
            {
                if (attached)
                {
                    AttachThreadInput(currentThreadId, targetThreadId, false);
                }
            }
        }

        // VK Key mapping P/Invokes for compatibility mode
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern short VkKeyScanExW(char ch, IntPtr dwhkl);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint MapVirtualKeyExW(uint uCode, uint uMapType, IntPtr dwhkl);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState, [Out] char[] pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);

        // Keyboard layout (HKL) P/Invokes for the target keyboard layout
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr LoadKeyboardLayoutW(string pwszKLID, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnloadKeyboardLayout(IntPtr hkl);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetKeyboardLayout(uint idThread);

        [DllImport("user32.dll")]
        internal static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[]? lpList);

        // Process elevation & UIPI P/Invokes
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass, out int TokenInformation, int TokenInformationLength, out int ReturnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CloseHandle(IntPtr hObject);

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

            _ = GetWindowThreadProcessId(hWnd, out uint targetPid);
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
