using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using Path = System.IO.Path;

namespace ValcoSetup
{
    public static class UpdateFeed
    {
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        static UpdateFeed() { Http.DefaultRequestHeaders.UserAgent.ParseAdd("ValcoOfficeUpdate/1.0"); }

        public static string FeedUrl
        {
            get
            {
                var u = Engine.GetOption("UpdateUrl", null) is string o && o.Length > 0 ? o : (Engine.InstalledManifest()?.UpdateUrl ?? new SuiteManifest().UpdateUrl);
                // the suite moved to github.com/Raidenplays/Valco-Office-Suite
                return u.Replace("/Raidenplays/valco-office/", "/Raidenplays/Valco-Office-Suite/", StringComparison.OrdinalIgnoreCase);
            }
        }

        public static async Task<SuiteManifest> FetchAsync(CancellationToken ct = default)
        {
            var json = await Http.GetStringAsync(FeedUrl, ct);
            return SuiteManifest.Parse(json);
        }

        /// <summary>Installed apps with a newer version on the server, plus apps that aren't installed yet.</summary>
        public static (List<AppEntry> Updates, List<AppEntry> New) Compare(SuiteManifest remote)
        {
            var installed = Engine.InstalledApps();
            var ups = remote.Apps.Where(a => installed.TryGetValue(a.Id, out var v) && Ver.Newer(a.Version, v)).ToList();
            var add = remote.Apps.Where(a => !installed.ContainsKey(a.Id)).ToList();
            return (ups, add);
        }

        public static Uri Resolve(string file) => new Uri(new Uri(FeedUrl), file);

        public static async Task Download(string file, string dest, Action<long, long> progress, CancellationToken ct)
        {
            using var resp = await Http.GetAsync(Resolve(file), HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? -1, done = 0;
            using var src = await resp.Content.ReadAsStreamAsync(ct);
            using var fs = File.Create(dest);
            var buf = new byte[81920];
            int n;
            while ((n = await src.ReadAsync(buf, 0, buf.Length, ct)) > 0)
            {
                await fs.WriteAsync(buf, 0, n, ct);
                done += n;
                progress(done, total);
            }
        }
    }

    /// <summary>Valco Office Update: Windows Vista Update-style window.</summary>
    public class UpdateWindow : VistaWindow
    {
        readonly Border _body = new Border { Padding = new Thickness(24, 16, 24, 16) };
        readonly StackPanel _buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        SuiteManifest _remote;
        CancellationTokenSource _cts;

        public UpdateWindow()
        {
            Title = "Valco Office Update";
            Width = 620; Height = 470; ResizeMode = ResizeMode.CanMinimize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Brushes.White;
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
            var head = new Grid { Height = 64, Background = new LinearGradientBrush(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xE7, 0xEF, 0xFA), 90) };
            var hs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            hs.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/ValcoOffice-mark.png")), Width = 40, Height = 40, Margin = new Thickness(0, 0, 12, 0) });
            hs.Children.Add(new TextBlock { Text = "Valco Office Update", FontSize = 20, Foreground = SetupWindow.HeadingBrush, VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(hs);
            head.Children.Add(new Border { Height = 3, VerticalAlignment = VerticalAlignment.Bottom, Background = new LinearGradientBrush(new GradientStopCollection { new GradientStop(Color.FromRgb(0x2F, 0x8B, 0xE0), 0), new GradientStop(Color.FromRgb(0x5C, 0xC8, 0x3A), 0.5), new GradientStop(Color.FromRgb(0xFF, 0xB2, 0x1E), 1) }, 0) });
            root.Children.Add(head);
            Grid.SetRow(_body, 1); root.Children.Add(_body);
            var footer = new Border { Background = new LinearGradientBrush(Color.FromRgb(0xF5, 0xF6, 0xF8), Color.FromRgb(0xE6, 0xE9, 0xEE), 90), BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xCF, 0xD9)), BorderThickness = new Thickness(0, 1, 0, 0), Child = _buttons };
            Grid.SetRow(footer, 2); root.Children.Add(footer);
            Content = root;
            Closed += (s, e) => { _cts?.Cancel(); Application.Current.Shutdown(); };
            Application.Current.MainWindow = this;
            Loaded += (s, e) => Check();
        }

        Button Btn(string text, Action a, bool def = false) { var b = UI.Btn(text, (s, e) => a(), def); b.MinWidth = 86; b.Margin = new Thickness(6, 0, 0, 0); _buttons.Children.Add(b); return b; }

        UIElement Status(ImageSource icon, Brush band, string title, string text)
        {
            var b = new Border { BorderBrush = band, BorderThickness = new Thickness(5, 0, 0, 0), Background = new SolidColorBrush(Color.FromRgb(0xF6, 0xF8, 0xFB)), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 12) };
            var g = new DockPanel();
            if (icon != null) { var im = new Image { Source = icon, Width = 40, Height = 40, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top }; DockPanel.SetDock(im, Dock.Left); g.Children.Add(im); }
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, FontSize = 16, Foreground = SetupWindow.HeadingBrush, TextWrapping = TextWrapping.Wrap });
            if (text != null) sp.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = SetupWindow.TextBrush, Margin = new Thickness(0, 4, 0, 0) });
            g.Children.Add(sp);
            b.Child = g;
            return b;
        }

        static ImageSource Shield(Color c, string glyph)
        {
            var dg = new DrawingGroup();
            dg.Children.Add(new GeometryDrawing(new LinearGradientBrush(Color.FromRgb((byte)Math.Min(255, c.R + 70), (byte)Math.Min(255, c.G + 70), (byte)Math.Min(255, c.B + 70)), c, 90), new Pen(new SolidColorBrush(Color.FromRgb((byte)(c.R * 0.6), (byte)(c.G * 0.6), (byte)(c.B * 0.6))), 1),
                Geometry.Parse("M20,2 L36,8 C36,24 30,33 20,38 C10,33 4,24 4,8 Z")));
            dg.Children.Add(new GeometryDrawing(null, new Pen(Brushes.White, 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, Geometry.Parse(glyph)));
            return new DrawingImage(dg);
        }
        static ImageSource OkShield => Shield(Color.FromRgb(0x3C, 0xA8, 0x3C), "M12,20 L18,26 L28,13");
        static ImageSource WarnShield => Shield(Color.FromRgb(0xE8, 0xA0, 0x10), "M20,11 V22 M20,28 V28.5");
        static ImageSource ErrShield => Shield(Color.FromRgb(0xC8, 0x2A, 0x2A), "M14,14 L26,26 M26,14 L14,26");

        async void Check()
        {
            _buttons.Children.Clear();
            var sp = new StackPanel();
            sp.Children.Add(Status(null, new SolidColorBrush(Color.FromRgb(0x2F, 0x8B, 0xE0)), "Checking for updates...", "Valco Office Update is looking for new versions of your Valco programs."));
            sp.Children.Add(new ProgressBar { Height = 14, IsIndeterminate = true, Margin = new Thickness(0, 4, 0, 0) });
            _body.Child = sp;
            _cts = new CancellationTokenSource();
            Btn("Cancel", Close);
            try
            {
                _remote = await UpdateFeed.FetchAsync(_cts.Token);
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Valco\Office\Update")) k.SetValue("LastCheck", DateTime.Now.ToString("o"));
                ShowResults();
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _buttons.Children.Clear();
                var p = new StackPanel();
                p.Children.Add(Status(ErrShield, new SolidColorBrush(Color.FromRgb(0xC8, 0x2A, 0x2A)), "Valco Office Update couldn't check for updates", ex.Message));
                p.Children.Add(new TextBlock { Text = "Check your Internet connection and try again.\n\nUpdate server: " + UpdateFeed.FeedUrl, TextWrapping = TextWrapping.Wrap, Foreground = SetupWindow.DimBrush });
                _body.Child = p;
                Btn("_Try Again", Check, true); Btn("Close", Close);
            }
            catch (OperationCanceledException) { }
        }

        void ShowResults()
        {
            _buttons.Children.Clear();
            var (ups, add) = UpdateFeed.Compare(_remote);
            bool setupNewer = Ver.Newer(_remote.SetupVersion, typeof(App).Assembly.GetName().Version?.ToString(3));
            var sp = new StackPanel();
            var lastCheck = DateTime.Now;
            if (ups.Count == 0 && add.Count == 0)
            {
                sp.Children.Add(Status(OkShield, new SolidColorBrush(Color.FromRgb(0x3C, 0xA8, 0x3C)), "Valco Office is up to date", "There are no new updates available for your Valco programs."));
                sp.Children.Add(InstalledList());
                sp.Children.Add(Footer(lastCheck));
                _body.Child = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                Btn("Check _Again", Check); Btn("Close", Close, true);
                return;
            }
            sp.Children.Add(Status(WarnShield, new SolidColorBrush(Color.FromRgb(0xE8, 0xA0, 0x10)),
                ups.Count > 0 ? $"{ups.Count} update{(ups.Count == 1 ? " is" : "s are")} available" : "New Valco programs are available",
                ups.Count > 0 ? "Select the updates you want to install, then click Install updates." : "You can add these programs to Valco Office."));
            var checks = new List<(CheckBox Box, AppEntry App)>();
            var installed = Engine.InstalledApps();
            void Group(string title, IEnumerable<AppEntry> apps, bool checkedDefault)
            {
                if (!apps.Any()) return;
                sp.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Foreground = SetupWindow.HeadingBrush, Margin = new Thickness(0, 6, 0, 4) });
                foreach (var a in apps)
                {
                    var cb = new CheckBox { IsChecked = checkedDefault, Margin = new Thickness(4, 2, 0, 2) };
                    var t = new StackPanel();
                    var cur = installed.TryGetValue(a.Id, out var v) ? $"{v} → {a.Version}" : a.Version;
                    t.Children.Add(new TextBlock { Text = $"{a.Name} {cur}   ({a.DownloadSize / (1024.0 * 1024):N1} MB)", Foreground = SetupWindow.TextBrush });
                    if (!string.IsNullOrWhiteSpace(a.Notes)) t.Children.Add(new TextBlock { Text = a.Notes, Foreground = SetupWindow.DimBrush, TextWrapping = TextWrapping.Wrap, MaxWidth = 500 });
                    cb.Content = t;
                    sp.Children.Add(cb);
                    checks.Add((cb, a));
                }
            }
            Group("Important updates", ups, true);
            Group("Optional: new programs", add, false);
            if (setupNewer) sp.Children.Add(new TextBlock { Text = $"Valco Office Setup {_remote.SetupVersion} will also be installed.", Foreground = SetupWindow.DimBrush, Margin = new Thickness(4, 8, 0, 0) });
            sp.Children.Add(Footer(lastCheck));
            _body.Child = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var install = Btn("_Install updates", async () => await DownloadAndApply(checks.Where(c => c.Box.IsChecked == true).Select(c => c.App).ToList(), setupNewer), true);
            foreach (var c in checks) c.Box.Click += (s, e) => install.IsEnabled = checks.Any(x => x.Box.IsChecked == true);
            Btn("Close", Close);
        }

        UIElement InstalledList()
        {
            var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            var installed = Engine.InstalledApps();
            var names = Engine.InstalledManifest()?.Apps.ToDictionary(a => a.Id, a => a.Name, StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, string>();
            sp.Children.Add(new TextBlock { Text = "Installed programs", FontWeight = FontWeights.SemiBold, Foreground = SetupWindow.HeadingBrush, Margin = new Thickness(0, 0, 0, 4) });
            foreach (var kv in installed) sp.Children.Add(new TextBlock { Text = $"   {(names.TryGetValue(kv.Key, out var n) ? n : kv.Key)}  {kv.Value}", Foreground = SetupWindow.TextBrush });
            if (installed.Count == 0) sp.Children.Add(new TextBlock { Text = "   No Valco Office programs are installed.", Foreground = SetupWindow.DimBrush });
            return sp;
        }

        UIElement Footer(DateTime last)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            var auto = new CheckBox { Content = "Check for updates automatically when I sign in to Windows", IsChecked = Engine.GetOption("AutoUpdate", "1") == "1" };
            auto.Click += (s, e) =>
            {
                // changing the setting needs administrator rights (it lives in HKLM)
                var exe = Environment.ProcessPath;
                try { Process.Start(new ProcessStartInfo(exe, auto.IsChecked == true ? "/setautoupdate on" : "/setautoupdate off") { UseShellExecute = true, Verb = "runas" })?.WaitForExit(); }
                catch { auto.IsChecked = !auto.IsChecked; }
            };
            sp.Children.Add(auto);
            sp.Children.Add(new TextBlock { Text = $"Most recent check for updates: {last:g}", Foreground = SetupWindow.DimBrush, Margin = new Thickness(0, 6, 0, 0) });
            return sp;
        }

        async Task DownloadAndApply(List<AppEntry> apps, bool setupNewer)
        {
            if (apps.Count == 0 && !setupNewer) return;
            _buttons.Children.Clear();
            var sp = new StackPanel();
            sp.Children.Add(Status(null, new SolidColorBrush(Color.FromRgb(0x2F, 0x8B, 0xE0)), "Downloading updates...", null));
            var line = new TextBlock { Foreground = SetupWindow.TextBrush, Margin = new Thickness(0, 4, 0, 4) };
            var bar = new ProgressBar { Height = 16, Maximum = 1 };
            sp.Children.Add(line); sp.Children.Add(bar);
            _body.Child = sp;
            _cts = new CancellationTokenSource();
            Btn("Cancel", () => _cts.Cancel());
            var dir = Path.Combine(Path.GetTempPath(), "ValcoOfficeUpdate", _remote.Version + "-" + DateTime.Now.Ticks);
            Directory.CreateDirectory(dir);
            try
            {
                long total = apps.Sum(a => Math.Max(1, a.DownloadSize)), before = 0;
                foreach (var a in apps)
                {
                    line.Text = $"Downloading {a.Name} {a.Version}...";
                    var dest = Path.Combine(dir, a.File);
                    await UpdateFeed.Download(a.File, dest, (d, t) => Dispatcher.Invoke(() => bar.Value = (before + d) / (double)total), _cts.Token);
                    if (!string.IsNullOrEmpty(a.Sha256) && !Engine.Sha256(dest).Equals(a.Sha256, StringComparison.OrdinalIgnoreCase)) throw new Exception($"The download of {a.Name} was damaged. Try again.");
                    if (!string.IsNullOrEmpty(a.Icon)) { try { Directory.CreateDirectory(Path.Combine(dir, Path.GetDirectoryName(a.Icon) ?? "")); await UpdateFeed.Download(a.Icon, Path.Combine(dir, a.Icon), (x, y) => { }, _cts.Token); } catch { } }
                    before += Math.Max(1, a.DownloadSize);
                }
                string runner = Environment.ProcessPath;
                if (setupNewer)
                {
                    line.Text = "Downloading Valco Office Setup...";
                    var s = Path.Combine(dir, "setup.exe");
                    await UpdateFeed.Download(_remote.SetupFile ?? "setup.exe", s, (d, t) => { }, _cts.Token);
                    if (!string.IsNullOrEmpty(_remote.SetupSha256) && !Engine.Sha256(s).Equals(_remote.SetupSha256, StringComparison.OrdinalIgnoreCase)) throw new Exception("The download of Valco Office Setup was damaged. Try again.");
                    runner = s;
                }
                // only the chosen apps go in this manifest; the apply step installs them and keeps everything else as it is
                var m = SuiteManifest.Parse(_remote.ToJson());
                var chosen = new HashSet<string>(apps.Select(a => a.Id));
                m.Apps.RemoveAll(a => !chosen.Contains(a.Id));
                File.WriteAllText(Path.Combine(dir, "valco-office.json"), m.ToJson());
                line.Text = "Installing updates...";
                bar.IsIndeterminate = true;
                Process.Start(new ProcessStartInfo(runner, $"/applyupdates \"{dir}\"") { UseShellExecute = true, Verb = "runas" });
                Close();
            }
            catch (OperationCanceledException) { ShowResults(); }
            catch (System.ComponentModel.Win32Exception) { ShowResults(); }
            catch (Exception ex)
            {
                _buttons.Children.Clear();
                var p = new StackPanel();
                p.Children.Add(Status(ErrShield, new SolidColorBrush(Color.FromRgb(0xC8, 0x2A, 0x2A)), "Some updates couldn't be downloaded", ex.Message));
                _body.Child = p;
                Btn("_Try Again", Check, true); Btn("Close", Close);
            }
        }
    }

    /// <summary>The elevated step that installs downloaded updates.</summary>
    public class ApplyUpdatesWindow : VistaWindow
    {
        public ApplyUpdatesWindow(string dir)
        {
            Title = "Valco Office Update";
            Width = 460; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Application.Current.MainWindow = this;
            var sp = new StackPanel { Margin = new Thickness(20) };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            head.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/ValcoOffice-mark.png")), Width = 36, Height = 36, Margin = new Thickness(0, 0, 12, 0) });
            var title = new TextBlock { Text = "Installing updates...", FontSize = 16, Foreground = SetupWindow.HeadingBrush, VerticalAlignment = VerticalAlignment.Center };
            head.Children.Add(title);
            var status = new TextBlock { Foreground = SetupWindow.TextBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
            var bar = new ProgressBar { Height = 16, Maximum = 1 };
            var close = UI.Btn("Close", (s, e) => Close(), true);
            close.HorizontalAlignment = HorizontalAlignment.Right; close.Margin = new Thickness(0, 14, 0, 0); close.Visibility = Visibility.Collapsed;
            sp.Children.Add(head); sp.Children.Add(status); sp.Children.Add(bar); sp.Children.Add(close);
            Content = sp;
            bool busy = true;
            Closing += (s, e) => { if (busy) e.Cancel = true; };
            Closed += (s, e) => Application.Current.Shutdown();
            Loaded += (s, e) => Task.Run(() =>
            {
                string error = null;
                try
                {
                    var m = SuiteManifest.Load(Path.Combine(dir, "valco-office.json"));
                    var eng = new Engine { Manifest = m, SourceDir = dir, InstallRoot = Engine.InstalledRoot ?? Engine.DefaultRoot, RemoveOldVersions = true };
                    eng.DesktopShortcuts = Engine.GetOption("DesktopShortcuts", "1") == "1";
                    eng.Associations = Engine.GetOption("Associations", "1") == "1";
                    eng.AutoUpdate = Engine.GetOption("AutoUpdate", "1") == "1";
                    eng.Progress = (p, t) => Dispatcher.Invoke(() => { bar.Value = p; status.Text = t; });
                    eng.AskCloseApps = names => Dispatcher.Invoke(() => VistaDialog.Show(this, "Close these programs so they can be updated:\n\n" + string.Join("\n", names.Select(n => "   •  " + n)) + "\n\nClick Yes after closing them, or No to close them now (unsaved changes will be lost).",
                        "Valco Office Update", MessageBoxButton.YesNo, DlgIcon.Warning) == MessageBoxResult.Yes);
                    var wanted = Engine.InstalledApps().Keys.Concat(m.Apps.Select(a => a.Id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    // the old setup.exe may still be closing; give it a moment so the new one can replace it
                    Thread.Sleep(1500);
                    eng.Apply(wanted, false, m.Apps.Select(a => a.Id).ToList());
                    try { Directory.Delete(dir, true); } catch { }
                }
                catch (Exception ex) { error = ex.Message; }
                Dispatcher.Invoke(() =>
                {
                    busy = false;
                    title.Text = error == null ? "Updates were installed successfully" : "Some updates weren't installed";
                    status.Text = error ?? "Your Valco programs are up to date.";
                    bar.Value = 1;
                    close.Visibility = Visibility.Visible;
                });
            });
        }
    }

    /// <summary>The quiet check that runs at sign-in and shows a notification when updates are available.</summary>
    public static class UpdateChecker
    {
        public static void RunQuiet()
        {
            var app = Application.Current;
            Task.Run(async () =>
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valco\Office\Update"))
                        if (DateTime.TryParse(k?.GetValue("LastCheck") as string, out var last) && (DateTime.Now - last).TotalHours < 20) { app.Dispatcher.Invoke(app.Shutdown); return; }
                    await Task.Delay(TimeSpan.FromSeconds(45));
                    var remote = await UpdateFeed.FetchAsync();
                    using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Valco\Office\Update")) k.SetValue("LastCheck", DateTime.Now.ToString("o"));
                    var (ups, _) = UpdateFeed.Compare(remote);
                    if (ups.Count == 0) { app.Dispatcher.Invoke(app.Shutdown); return; }
                    app.Dispatcher.Invoke(() => Notify(ups));
                }
                catch { app.Dispatcher.Invoke(app.Shutdown); }
            });
        }

        static void Notify(List<AppEntry> ups)
        {
            var app = Application.Current;
            System.Drawing.Icon icon;
            try { using var s = Application.GetResourceStream(new Uri("pack://application:,,,/ValcoOffice.ico")).Stream; icon = new System.Drawing.Icon(s, 16, 16); }
            catch { icon = System.Drawing.SystemIcons.Information; }
            var ni = new System.Windows.Forms.NotifyIcon { Icon = icon, Visible = true, Text = "Valco Office Update" };
            void Open()
            {
                ni.Visible = false; ni.Dispose();
                try { Process.Start(new ProcessStartInfo(Environment.ProcessPath, "/update") { UseShellExecute = true }); } catch { }
                app.Shutdown();
            }
            ni.BalloonTipClicked += (s, e) => Open();
            ni.Click += (s, e) => Open();
            ni.ShowBalloonTip(15000, "Updates are available for Valco Office", (ups.Count == 1 ? ups[0].Name + " " + ups[0].Version + " is ready." : $"{ups.Count} updates are ready.") + " Click here to install them.", System.Windows.Forms.ToolTipIcon.Info);
            // stay in the notification area for a while, then go away quietly
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
            t.Tick += (s, e) => { ni.Visible = false; ni.Dispose(); app.Shutdown(); };
            t.Start();
        }
    }
}
