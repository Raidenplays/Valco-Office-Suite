using System;
using System.IO;
using System.Linq;
using System.Windows;
using ValcoSpreadsheets.Model;

namespace ValcoSpreadsheets
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            SetAppId();
            Valco.UI.OpaqueWindows.Install();   // stay readable under transparency mods (Windhawk etc.)
            DispatcherUnhandledException += (s, a) =>
            {
                a.Handled = true;
                try { VistaDialog.Error(Current?.MainWindow, a.Exception.Message, "Something went wrong"); } catch { }
            };
            Settings.Load();
            ShutdownMode = ShutdownMode.OnLastWindowClose;

            var files = e.Args.Where(File.Exists).ToList();
            var recovered = Recovered();
            if (files.Count == 0 && recovered.Count == 0)
            {
                var w = new MainWindow();
                MainWindow = w;
                w.Show();
                return;
            }
            MainWindow first = null;
            foreach (var f in files.Concat(recovered))
            {
                try
                {
                    var b = Book.Open(f);
                    if (recovered.Contains(f)) { b.MarkRecovered(); try { File.Delete(f); } catch { } }
                    else Settings.AddRecent(f);
                    var w = new MainWindow(b);
                    first ??= w;
                    w.Show();
                }
                catch (Exception ex)
                {
                    VistaDialog.Error(null, $"Valco Spreadsheets cannot open the file '{Path.GetFileName(f)}'.\n\n{ex.Message}", "Valco Spreadsheets");
                }
            }
            if (first == null) { first = new MainWindow(); first.Show(); }
            MainWindow = first;
            if (recovered.Count > 0)
                VistaDialog.Show(first, $"Valco Spreadsheets recovered {recovered.Count} workbook(s) from the last time it closed unexpectedly. Save the ones you want to keep.", "Document Recovery", MessageBoxButton.OK, DlgIcon.Info);
        }

        static System.Collections.Generic.List<string> Recovered()
        {
            try
            {
                if (!Directory.Exists(Settings.AutoRecoverDir)) return new System.Collections.Generic.List<string>();
                // only files no other running copy is writing
                var others = System.Diagnostics.Process.GetProcessesByName("ValcoSpreadsheets").Length > 1;
                if (others) return new System.Collections.Generic.List<string>();
                return Directory.GetFiles(Settings.AutoRecoverDir, "*.xlsx").Where(f => (DateTime.Now - File.GetLastWriteTime(f)).TotalDays < 14).ToList();
            }
            catch { return new System.Collections.Generic.List<string>(); }
        }

        /// <summary>Gives the taskbar button its own identity (not grouped with whatever launched us).</summary>
        static void SetAppId()
        {
            try { SetCurrentProcessExplicitAppUserModelID("ItsRaiden.ValcoSpreadsheets"); } catch { }
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
    }
}
