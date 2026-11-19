using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace ValcoSetup
{
    public partial class App : Application
    {
        public static string[] Args = new string[0];
        public static bool Has(string flag) => Args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
        public static string ArgAfter(string flag) { int i = Array.FindIndex(Args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase)); return i >= 0 && i + 1 < Args.Length ? Args[i + 1] : null; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Args = e.Args;
            DispatcherUnhandledException += (s, ex) =>
            {
                VistaDialog.Show(null, ex.Exception.Message, "Valco Office Setup", MessageBoxButton.OK, DlgIcon.Error, "Setup encountered an error");
                ex.Handled = true;
            };
            try
            {
                if (Has("/checkupdates")) { UpdateChecker.RunQuiet(); return; }
                if (Has("/update")) { new UpdateWindow().Show(); return; }
                if (Has("/defaults")) { ShutdownMode = ShutdownMode.OnLastWindowClose; new DefaultAppsWindow().Show(); return; }

                // everything else changes Program Files and HKLM
                if (!Engine.IsAdmin)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(Environment.ProcessPath, string.Join(" ", Args.Select(a => a.Contains(' ') ? "\"" + a + "\"" : a))) { UseShellExecute = true, Verb = "runas" });
                    }
                    catch { /* user cancelled the elevation prompt */ }
                    Shutdown();
                    return;
                }
                if (Has("/setautoupdate"))
                {
                    bool on = (ArgAfter("/setautoupdate") ?? "on") != "off";
                    using (var k = Engine.OpenRoot(true)) k.SetValue("AutoUpdate", on ? "1" : "0");
                    using (var run = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"))
                    {
                        var exe = System.IO.Path.Combine(Engine.InstalledRoot ?? "", "Setup", "setup.exe");
                        if (on && System.IO.File.Exists(exe)) run.SetValue("Valco Office Update", $"\"{exe}\" /checkupdates"); else run.DeleteValue("Valco Office Update", false);
                    }
                    Shutdown(); return;
                }
                if (Has("/applyupdates")) { new ApplyUpdatesWindow(ArgAfter("/applyupdates")).Show(); return; }
                if (Has("/uninstall")) { SetupWindow.StartMaintenance(removeOnly: true); return; }
                if (Has("/maintenance")) { SetupWindow.StartMaintenance(false); return; }
                if (HasPayload) { SetupWindow.StartInstall(PayloadDir); return; }
                if (Engine.IsInstalled) { SetupWindow.StartMaintenance(false); return; }
                VistaDialog.Show(null, "The installation files for Valco Office couldn't be found. Make sure the \"payload\" folder is next to setup.exe.", "Valco Office Setup", MessageBoxButton.OK, DlgIcon.Error, "Setup can't continue");
                Shutdown();
            }
            catch (Exception ex)
            {
                VistaDialog.Show(null, ex.Message, "Valco Office Setup", MessageBoxButton.OK, DlgIcon.Error, "Setup couldn't start");
                Shutdown();
            }
        }

        public static string PayloadDir
        {
            get
            {
                var dir = Path.GetDirectoryName(Environment.ProcessPath);
                foreach (var cand in new[] { Path.Combine(dir, "payload"), dir, Path.Combine(dir, "Cache") })
                    if (File.Exists(Path.Combine(cand, "valco-office.json"))) return cand;
                return null;
            }
        }

        public static bool HasPayload => PayloadDir != null && !PayloadDir.EndsWith("Cache", StringComparison.OrdinalIgnoreCase);
    }
}
