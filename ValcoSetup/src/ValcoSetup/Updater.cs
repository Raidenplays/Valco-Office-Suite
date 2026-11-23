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
    /// <summary>
    /// Updates come from the GitHub releases of Raidenplays/Valco-Office-Suite; the tag is the suite version (v1.0.0.464900).
    /// Each release has ValcoOffice-Setup.exe (setup + every program, for new installs) and the same programs as separate files
    /// (valco-office.json, ValcoOffice-Updater.exe, one .zip/.msi + .ico per program), so updates download only what changed.
    /// </summary>
    public static class UpdateFeed
    {
        public const string DefaultRepo = "Raidenplays/Valco-Office-Suite";
        public const string Asset = "ValcoOffice-Setup.exe";
        public const string ManifestAsset = "valco-office.json";
        public const string UpdaterAsset = "ValcoOffice-Updater.exe";
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(60) };
        static readonly HttpClient NoRedirect = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
        static UpdateFeed()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("ValcoOfficeUpdate/1.0");
            NoRedirect.DefaultRequestHeaders.UserAgent.ParseAdd("ValcoOfficeUpdate/1.0");
        }

        public static string Repo => Engine.GetOption("UpdateRepo", null) is string r && r.Contains('/') ? r : DefaultRepo;
        public static string LatestPage => $"https://github.com/{Repo}/releases/latest";
        public static string InstalledVersion => Engine.GetOption("Version", "0");

        /// <summary>Tag of the latest release, read from GitHub's redirect (no API limits).</summary>
        public static async Task<(string Version, string Tag)> LatestAsync(CancellationToken ct = default)
        {
            using var resp = await NoRedirect.GetAsync(LatestPage, HttpCompletionOption.ResponseHeadersRead, ct);
            var loc = resp.Headers.Location?.ToString();
            if (loc == null || !loc.Contains("/releases/tag/"))
            {
                // Nothing published yet (GitHub sends us to /releases): treat as up to date rather than an error.
                if (resp.IsSuccessStatusCode || (int)resp.StatusCode is 301 or 302 or 404) return (InstalledVersion, null);
                throw new Exception($"The update server answered {(int)resp.StatusCode} {resp.ReasonPhrase}.");
            }
            var tag = Uri.UnescapeDataString(loc.Substring(loc.LastIndexOf('/') + 1));
            return (tag.TrimStart('v', 'V'), tag);
        }

        public static bool IsNewer(string version) => Ver.Newer(version, InstalledVersion);

        static string AssetUrl(string tag, string name) => $"https://github.com/{Repo}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(name)}";

        public static Task Download(string tag, string dest, Action<long, long> progress, CancellationToken ct) => DownloadAsset(tag, Asset, dest, progress, ct);

        /// <summary>The release's program list, or null when the release only has the single installer (older releases).</summary>
        public static async Task<SuiteManifest> ManifestAsync(string tag, CancellationToken ct)
        {
            using var resp = await Http.GetAsync(AssetUrl(tag, ManifestAsset), ct);
            if (!resp.IsSuccessStatusCode) return null;
            try { return SuiteManifest.Parse(await resp.Content.ReadAsStringAsync(ct)); } catch { return null; }
        }

        public static async Task DownloadAsset(string tag, string name, string dest, Action<long, long> progress, CancellationToken ct)
        {
            using var resp = await Http.GetAsync(AssetUrl(tag, name), HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode) throw new Exception($"{name} couldn't be downloaded ({(int)resp.StatusCode} {resp.ReasonPhrase}).");
            long total = resp.Content.Headers.ContentLength ?? -1, done = 0;
            using var src = await resp.Content.ReadAsStreamAsync(ct);
            using var fs = File.Create(dest);
            var buf = new byte[1 << 17];
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
        string _latest, _tag;
        SuiteManifest _manifest;
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
            sp.Children.Add(Status(null, new SolidColorBrush(Color.FromRgb(0x2F, 0x8B, 0xE0)), "Checking for updates...", "Valco Office Update is looking for a new version of Valco Office."));
            sp.Children.Add(new ProgressBar { Height = 14, IsIndeterminate = true, Margin = new Thickness(0, 4, 0, 0) });
            _body.Child = sp;
            _cts = new CancellationTokenSource();
            Btn("Cancel", Close);
            try
            {
                (_latest, _tag) = await UpdateFeed.LatestAsync(_cts.Token);
                _manifest = null;
                if (_tag != null && UpdateFeed.IsNewer(_latest))
                {
                    try { _manifest = await UpdateFeed.ManifestAsync(_tag, _cts.Token); } catch (OperationCanceledException) { throw; } catch { }
                }
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Valco\Office\Update")) k.SetValue("LastCheck", DateTime.Now.ToString("o"));
                ShowResults();
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _buttons.Children.Clear();
                var p = new StackPanel();
                p.Children.Add(Status(ErrShield, new SolidColorBrush(Color.FromRgb(0xC8, 0x2A, 0x2A)), "Valco Office Update couldn't check for updates", ex.Message));
                p.Children.Add(new TextBlock { Text = "Check your Internet connection and try again.\n\nUpdates come from: " + UpdateFeed.LatestPage, TextWrapping = TextWrapping.Wrap, Foreground = SetupWindow.DimBrush });
                _body.Child = p;
                Btn("_Try Again", Check, true); Btn("Close", Close);
            }
            catch (OperationCanceledException) { }
        }

        void ShowResults()
        {
            _buttons.Children.Clear();
            var sp = new StackPanel();
            var lastCheck = DateTime.Now;
            if (!UpdateFeed.IsNewer(_latest))
            {
                sp.Children.Add(Status(OkShield, new SolidColorBrush(Color.FromRgb(0x3C, 0xA8, 0x3C)), "Valco Office is up to date", $"You have Valco Office {UpdateFeed.InstalledVersion}, the latest version."));
                sp.Children.Add(InstalledList());
                sp.Children.Add(Footer(lastCheck));
                _body.Child = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                Btn("Check _Again", Check); Btn("Close", Close, true);
                return;
            }
            var updates = PendingUpdates();
            if (updates != null && updates.Count == 0)
            {
                // the release only changed programs that aren't installed here
                sp.Children.Add(Status(OkShield, new SolidColorBrush(Color.FromRgb(0x3C, 0xA8, 0x3C)), "Your Valco Office programs are up to date",
                    $"Valco Office {_latest} only changes programs you haven't installed. Click Install updates to record the new version, or close this window."));
                sp.Children.Add(InstalledList());
            }
            else
            {
                long size = updates?.Sum(a => a.DownloadSize) ?? 0;
                sp.Children.Add(Status(WarnShield, new SolidColorBrush(Color.FromRgb(0xE8, 0xA0, 0x10)), "An update for Valco Office is available",
                    updates == null
                        ? $"Valco Office {_latest} is ready to download. You have version {UpdateFeed.InstalledVersion}. Your installed programs will be updated; programs you haven't installed stay as they are."
                        : $"Valco Office {_latest} is ready to download ({Math.Max(1, size / 1048576.0):N0} MB). You have version {UpdateFeed.InstalledVersion}. Only the programs below are downloaded; everything else stays as it is."));
                sp.Children.Add(updates == null ? InstalledList() : UpdatesList(updates));
            }
            sp.Children.Add(Footer(lastCheck));
            _body.Child = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Btn("_Install updates", async () => await DownloadAndApply(), true);
            Btn("Close", Close);
        }

        /// <summary>Installed programs that the latest release has a newer version of; null when the release has no manifest.</summary>
        List<AppEntry> PendingUpdates()
        {
            if (_manifest == null || _manifest.Apps.Count == 0) return null;
            var installed = Engine.InstalledApps();
            return _manifest.Apps.Where(a => installed.TryGetValue(a.Id, out var v) && Ver.Newer(a.Version, v)).ToList();
        }

        /// <summary>Each program that will be updated: old and new version, download size and what changed.</summary>
        UIElement UpdatesList(List<AppEntry> updates)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            var installed = Engine.InstalledApps();
            sp.Children.Add(new TextBlock { Text = "Updates for your programs", FontWeight = FontWeights.SemiBold, Foreground = SetupWindow.HeadingBrush, Margin = new Thickness(0, 0, 0, 4) });
            foreach (var a in updates)
            {
                installed.TryGetValue(a.Id, out var from);
                var head = new TextBlock { Foreground = SetupWindow.TextBrush, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
                head.Inlines.Add(new System.Windows.Documents.Run(a.Name) { FontWeight = FontWeights.SemiBold });
                head.Inlines.Add(new System.Windows.Documents.Run($"   {from} → {a.Version}   ({Math.Max(0.1, a.DownloadSize / 1048576.0):N1} MB)") { Foreground = SetupWindow.DimBrush });
                sp.Children.Add(head);
                var changes = (a.Changes ?? new List<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).Take(6).ToList();
                foreach (var c in changes)
                    sp.Children.Add(new TextBlock { Text = "   •  " + c, TextWrapping = TextWrapping.Wrap, Foreground = SetupWindow.TextBrush, Margin = new Thickness(8, 1, 0, 0) });
                if ((a.Changes?.Count ?? 0) > changes.Count)
                    sp.Children.Add(new TextBlock { Text = $"   and {a.Changes.Count - changes.Count} more", Foreground = SetupWindow.DimBrush, Margin = new Thickness(8, 1, 0, 0) });
            }
            var link = new TextBlock { Margin = new Thickness(0, 10, 0, 0) };
            var h = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("See the full release notes")) { NavigateUri = new Uri(UpdateFeed.LatestPage) };
            h.RequestNavigate += (s, e) => { try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { } };
            link.Inlines.Add(h);
            sp.Children.Add(link);
            return sp;
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

        async Task DownloadAndApply()
        {
            _buttons.Children.Clear();
            var sp = new StackPanel();
            sp.Children.Add(Status(null, new SolidColorBrush(Color.FromRgb(0x2F, 0x8B, 0xE0)), "Downloading updates...", null));
            var line = new TextBlock { Foreground = SetupWindow.TextBrush, Margin = new Thickness(0, 4, 0, 4), Text = $"Downloading Valco Office {_latest}..." };
            var bar = new ProgressBar { Height = 16, Maximum = 1 };
            sp.Children.Add(line); sp.Children.Add(bar);
            _body.Child = sp;
            _cts = new CancellationTokenSource();
            Btn("Cancel", () => _cts.Cancel());
            var dir = Path.Combine(Path.GetTempPath(), "ValcoOfficeUpdate", _latest + "-" + DateTime.Now.Ticks);
            Directory.CreateDirectory(dir);
            try
            {
                if (await TryDownloadPrograms(dir, line, bar)) { Close(); return; }
                var exe = Path.Combine(dir, UpdateFeed.Asset);
                await UpdateFeed.Download(_tag, exe, (d, t) => Dispatcher.Invoke(() =>
                {
                    bar.Value = t > 0 ? d / (double)t : 0;
                    line.Text = t > 0 ? $"Downloading Valco Office {_latest}...  {d / 1048576.0:N0} of {t / 1048576.0:N0} MB" : $"Downloading Valco Office {_latest}...  {d / 1048576.0:N0} MB";
                }), _cts.Token);
                line.Text = "Preparing the update...";
                bar.IsIndeterminate = true;
                var payload = Path.Combine(dir, "payload");
                await Task.Run(() => Package.Extract(exe, payload));
                // only the programs that are installed (and older) are updated; the apply step keeps everything else as it is
                var m = SuiteManifest.Load(Path.Combine(payload, "valco-office.json"));
                var installed = Engine.InstalledApps();
                m.Apps.RemoveAll(a => !installed.TryGetValue(a.Id, out var v) || !Ver.Newer(a.Version, v));
                File.WriteAllText(Path.Combine(payload, "valco-office.json"), m.ToJson());
                var runner = Path.Combine(dir, "setup.exe");
                Package.CopyExe(exe, runner);
                try { File.Delete(exe); } catch { }
                line.Text = "Installing updates...";
                Process.Start(new ProcessStartInfo(runner, $"/applyupdates \"{payload}\"") { UseShellExecute = true, Verb = "runas" });
                Close();
            }
            catch (OperationCanceledException) { ShowResults(); }
            catch (System.ComponentModel.Win32Exception) { ShowResults(); }
            catch (Exception ex)
            {
                _buttons.Children.Clear();
                var p = new StackPanel();
                p.Children.Add(Status(ErrShield, new SolidColorBrush(Color.FromRgb(0xC8, 0x2A, 0x2A)), "The update couldn't be downloaded", ex.Message));
                _body.Child = p;
                Btn("_Try Again", Check, true); Btn("Close", Close);
            }
        }

        /// <summary>
        /// Downloads only the programs that changed (plus the small updater) and starts the elevated apply step.
        /// Returns false when the release has no separate program files, so the caller falls back to the full installer.
        /// </summary>
        async Task<bool> TryDownloadPrograms(string dir, TextBlock line, ProgressBar bar)
        {
            var m = _manifest ?? await UpdateFeed.ManifestAsync(_tag, _cts.Token);
            if (m == null || m.Apps.Count == 0) return false;
            m = SuiteManifest.Parse(m.ToJson()); // a copy: the list below is trimmed to what's needed
            var installed = Engine.InstalledApps();
            m.Apps.RemoveAll(a => !installed.TryGetValue(a.Id, out var v) || !Ver.Newer(a.Version, v));
            var payload = Path.Combine(dir, "payload");
            Directory.CreateDirectory(payload);
            var runner = Path.Combine(dir, "setup.exe");
            // what to fetch: the updater, each changed program and its icon, and the license
            var jobs = new List<(string Name, string Dest, string Sha, long Size, bool Required)>
            {
                (UpdateFeed.UpdaterAsset, runner, m.SetupSha256, 0, true),
                ("License.rtf", Path.Combine(payload, "License.rtf"), null, 0, false),
            };
            foreach (var a in m.Apps)
            {
                jobs.Add((a.File, Path.Combine(payload, a.File), a.Sha256, a.DownloadSize, true));
                if (!string.IsNullOrEmpty(a.Icon)) jobs.Add((a.Icon, Path.Combine(payload, a.Icon), null, 0, false));
            }
            long total = jobs.Sum(j => j.Size), before = 0;
            var names = string.Join(", ", m.Apps.Select(a => a.Name));
            foreach (var j in jobs)
            {
                var label = m.Apps.FirstOrDefault(a => a.File == j.Name)?.Name ?? "Valco Office Update";
                try
                {
                    await UpdateFeed.DownloadAsset(_tag, j.Name, j.Dest, (d, t) => Dispatcher.Invoke(() =>
                    {
                        long all = Math.Max(total, before + Math.Max(t, d));
                        bar.Value = all > 0 ? (before + d) / (double)all : 0;
                        line.Text = $"Downloading {label}...  {(before + d) / 1048576.0:N1} of {all / 1048576.0:N1} MB";
                    }), _cts.Token);
                }
                catch (Exception) when (!j.Required) { continue; }
                if (!string.IsNullOrEmpty(j.Sha) && !Engine.Sha256(j.Dest).Equals(j.Sha, StringComparison.OrdinalIgnoreCase))
                    throw new Exception($"{j.Name} was damaged while downloading. Try again.");
                before += j.Size > 0 ? j.Size : new FileInfo(j.Dest).Length;
            }
            File.WriteAllText(Path.Combine(payload, "valco-office.json"), m.ToJson());
            line.Text = m.Apps.Count > 0 ? $"Installing updates for {names}..." : "Installing updates...";
            Process.Start(new ProcessStartInfo(runner, $"/applyupdates \"{payload}\"") { UseShellExecute = true, Verb = "runas" });
            return true;
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
                    var (latest, _) = await UpdateFeed.LatestAsync();
                    using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Valco\Office\Update")) k.SetValue("LastCheck", DateTime.Now.ToString("o"));
                    if (!UpdateFeed.IsNewer(latest)) { app.Dispatcher.Invoke(app.Shutdown); return; }
                    app.Dispatcher.Invoke(() => Notify(latest));
                }
                catch { app.Dispatcher.Invoke(app.Shutdown); }
            });
        }

        static void Notify(string latest)
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
            ni.ShowBalloonTip(15000, "An update for Valco Office is available", $"Valco Office {latest} is ready. Click here to install it.", System.Windows.Forms.ToolTipIcon.Info);
            // stay in the notification area for a while, then go away quietly
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
            t.Tick += (s, e) => { ni.Visible = false; ni.Dispose(); app.Shutdown(); };
            t.Start();
        }
    }
}
