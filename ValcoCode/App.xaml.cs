using System.IO;
using System.Windows;
using System.Windows.Threading;
using ValcoCode.Services;

namespace ValcoCode;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnError;
        AppDomain.CurrentDomain.UnhandledException += (_, a) => LogCrash(a.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, a) => { LogCrash(a.Exception); a.SetObserved(); };
        base.OnStartup(e);
        Valco.UI.OpaqueWindows.Install();   // stay readable under transparency mods (Windhawk etc.)
        ThemeManager.Apply(ThemeManager.ById(AppSettings.Load().Theme));
        var w = new Views.MainWindow();
        MainWindow = w;
        w.Show();
    }

    void OnError(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        e.Handled = true;
        if (MainWindow is Views.MainWindow mw && mw.IsLoaded)
            mw.Notify("Something went wrong: " + e.Exception.Message + " (details in %AppData%\\Valco Code\\crash.log)", true);
        else
            MessageBox.Show(e.Exception.ToString(), "Valco Code hit a problem", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    static void LogCrash(Exception? ex)
    {
        if (ex == null) return;
        try { File.AppendAllText(Path.Combine(AppSettings.DataDir, "crash.log"), $"[{DateTime.Now}] {ex}\n\n"); } catch { }
    }
}
