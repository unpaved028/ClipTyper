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
            // Crisp UI on scaled displays (125 %, 150 %, ...) instead of bitmap-stretched windows
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            // Before the first window: task dialogs need visual styles (Common Controls 6)
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            const string mutexName = @"Local\ClipTyper_SingleInstance_Mutex";
            _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);

            if (!createdNew)
            {
                Dialogs.Show(
                    null,
                    TaskDialogIcon.Information,
                    "ClipTyper is already running",
                    "Look for the ClipTyper icon in the notification area of the taskbar. Double-click it for settings, or right-click it for the menu.");
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
                Dialogs.Show(
                    null,
                    TaskDialogIcon.Error,
                    "Something went wrong",
                    $"{e.Exception.Message}\n\nDetails were written to clip-typer.log (Settings > General > Open folder).");
            };

            try
            {
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
