using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using MimeKit;

namespace ValcoExpress
{
    public partial class App : Application
    {
        static Task<CoreWebView2Environment> _env;
        static Mutex _mutex;
        static EventWaitHandle _signal;
        public static Task<CoreWebView2Environment> WebEnvAsync() =>
            _env ??= CoreWebView2Environment.CreateAsync(null, Path.Combine(Storage.LocalDir, "WebView2"));

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += (s, a) =>
            {
                a.Handled = true;
                try { VistaDialog.Error(Current?.MainWindow, a.Exception.Message, "Something went wrong"); } catch { }
            };
            Storage.Init();
            OfficeTheme.Apply(Storage.Settings.ColorScheme);
            Mail.Init();

            var file = e.Args.FirstOrDefault(a => File.Exists(a) && a.EndsWith(".eml", StringComparison.OrdinalIgnoreCase));
            if (file != null)
            {
                ShutdownMode = ShutdownMode.OnLastWindowClose;
                MimeMessage msg;
                try { msg = MimeMessage.Load(file); }
                catch (Exception ex) { VistaDialog.Error(null, ex.Message, "This message could not be opened."); Shutdown(); return; }
                new ReadWindow(msg, null, null).Show();
                return;
            }
            // mailto: links (Valco Express set as the default e-mail program)
            var mailto = e.Args.FirstOrDefault(a => a.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase));
            if (mailto != null)
            {
                ShutdownMode = ShutdownMode.OnLastWindowClose;
                ComposeWindow.FromMailto(mailto).Show();
                return;
            }
            // One copy at a time: a second launch just brings the running one forward.
            var user = Environment.UserName;
            _mutex = new Mutex(true, "ValcoExpress.SingleInstance." + user, out bool first);
            _signal = new EventWaitHandle(false, EventResetMode.AutoReset, "ValcoExpress.Show." + user);
            bool background = e.Args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));
            if (!first)
            {
                if (!background) _signal.Set();
                Shutdown();
                return;
            }
            new Thread(() =>
            {
                while (_signal.WaitOne())
                    Dispatcher.BeginInvoke(new Action(() => (MainWindow as MainWindow)?.ShowFromTray()));
            }) { IsBackground = true }.Start();

            AutoStart.Apply(Storage.Settings.StartWithWindows);
            SessionEnding += (s2, a2) => (MainWindow as MainWindow)?.MarkExiting();
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var w = new MainWindow();
            MainWindow = w;
            if (!background) w.Show();
        }
    }

    public class IconKeyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is string key ? Application.Current.TryFindResource(key) as ImageSource : null;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
