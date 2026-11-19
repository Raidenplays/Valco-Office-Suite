using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace ValcoWord
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += (s, a) =>
            {
                a.Handled = true;
                try { VistaDialog.Error(Current?.MainWindow, a.Exception.Message, "Something went wrong"); } catch { }
            };
            Settings.Load();
            // name and initials typed into Valco Office Setup (User Information tab)
            try
            {
                using var ui = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valco\Office\UserInfo");
                if (ui != null)
                {
                    if (string.IsNullOrWhiteSpace(Settings.Current.UserName) && ui.GetValue("UserName") is string un && un.Length > 0) Settings.Current.UserName = un;
                    if (string.IsNullOrWhiteSpace(Settings.Current.Initials) && ui.GetValue("Initials") is string ini && ini.Length > 0) Settings.Current.Initials = ini;
                }
            }
            catch { }
            StyleDef.Init();
            ShutdownMode = ShutdownMode.OnLastWindowClose;

            var files = e.Args.Where(File.Exists).ToList();
            var recovered = AutoRecover.Pending();
            MainWindow first;
            try { first = new MainWindow(); }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Valco Word couldn't start");
                Shutdown();
                return;
            }
            if (files.Count == 0)
            {
                var w = first;
                MainWindow = w;
                w.Show();
                if (recovered.Count > 0) w.OfferRecovery(recovered);
            }
            else
            {
                foreach (var f in files)
                {
                    var w = first ?? new MainWindow();
                    first = null;
                    MainWindow ??= w;
                    w.Show();
                    w.OpenFile(f);
                }
            }
        }
    }
}
