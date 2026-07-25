using System;
using System.Threading;
using System.Windows.Forms;

namespace ClipTyper
{
    static class Program
    {
        private static Mutex? _singleInstanceMutex;

        [STAThread]
        static void Main()
        {
            const string mutexName = @"Local\ClipTyper_SingleInstance_Mutex";
            _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);

            if (!createdNew)
            {
                MessageBox.Show(
                    "ClipTyper is already running.",
                    "ClipTyper",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            // Set unhandled exception mode to catch UI thread exceptions
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Logger.LogError("Unhandled AppDomain Exception", e.ExceptionObject as Exception);
            };

            Application.ThreadException += (s, e) =>
            {
                Logger.LogError("Unhandled Application Thread Exception", e.Exception);
                MessageBox.Show(
                    $"An unexpected error occurred:\n{e.Exception.Message}\n\nDetails have been saved to the log file.",
                    "ClipTyper Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            };

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                
                Logger.LogInfo("ClipTyper started.");
                Application.Run(new ClipTyperContext());
                Logger.LogInfo("ClipTyper stopped.");
            }
            catch (Exception ex)
            {
                Logger.LogError("Fatal error in Application.Run", ex);
            }
            finally
            {
                _singleInstanceMutex.ReleaseMutex();
                _singleInstanceMutex.Dispose();
            }
        }
    }
}
