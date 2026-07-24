using System;
using System.Windows.Forms;

namespace ClipTyper
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                System.IO.File.WriteAllText("crash.log", e.ExceptionObject?.ToString());
            };
            Application.ThreadException += (s, e) =>
            {
                System.IO.File.WriteAllText("crash.log", e.Exception?.ToString());
            };

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                
                Application.Run(new ClipTyperContext());
            }
            catch (Exception ex)
            {
                System.IO.File.WriteAllText("crash.log", ex.ToString());
                Console.WriteLine(ex.ToString());
            }
        }
    }
}
