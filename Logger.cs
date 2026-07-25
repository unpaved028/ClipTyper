using System;
using System.IO;

namespace ClipTyper
{
    /// <summary>
    /// Provides thread-safe, structured logging to a rotating file.
    /// Rotates the log file when it reaches 100 KB.
    /// </summary>
    public static class Logger
    {
        private static readonly object LogLock = new();
        private const long MaxLogSizeBytes = 100 * 1024; // 100 KB

        public static string LogDirectory => SettingsManager.SettingsDir;
        public static string LogFilePath => Path.Combine(LogDirectory, "clip-typer.log");
        public static string BackupLogFilePath => Path.Combine(LogDirectory, "clip-typer.log.bak");

        /// <summary>
        /// Logs an informational message.
        /// </summary>
        public static void LogInfo(string message)
        {
            Write("INFO", message);
        }

        /// <summary>
        /// Logs a warning message.
        /// </summary>
        public static void LogWarning(string message)
        {
            Write("WARN", message);
        }

        /// <summary>
        /// Logs an error message with optional exception details.
        /// </summary>
        public static void LogError(string message, Exception? ex = null)
        {
            string fullMessage = ex == null ? message : $"{message} - Exception: {ex}";
            Write("ERROR", fullMessage);
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (LogLock)
                {
                    if (!Directory.Exists(LogDirectory))
                    {
                        Directory.CreateDirectory(LogDirectory);
                    }

                    FileInfo fi = new FileInfo(LogFilePath);
                    if (fi.Exists && fi.Length >= MaxLogSizeBytes)
                    {
                        try
                        {
                            if (File.Exists(BackupLogFilePath))
                            {
                                File.Delete(BackupLogFilePath);
                            }
                            File.Move(LogFilePath, BackupLogFilePath);
                        }
                        catch
                        {
                            // If rotation fails, overwrite current log file
                            File.Delete(LogFilePath);
                        }
                    }

                    string timeStamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    string logLine = $"[{timeStamp}] [{level}] {message}{Environment.NewLine}";

                    File.AppendAllText(LogFilePath, logLine);
                }
            }
            catch
            {
                // Silently ignore logging failures to avoid crash loops during logging
            }
        }
    }
}
