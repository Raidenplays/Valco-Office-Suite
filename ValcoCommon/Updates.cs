// Shared by every Valco Office app (Word, Express, Code). Edit here, not in the apps.
using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Valco.UI
{
    /// <summary>
    /// Checks the Valco Office GitHub releases (github.com/Raidenplays/Valco-Office-Suite) for a newer version of the app.
    /// Automatic check once a day at startup; manual check from Help / Options. Installing hands over to Valco Office Update
    /// (setup.exe /update) or, without Setup, opens the release page.
    /// </summary>
    public static class Updates
    {
        public const string Repo = "Raidenplays/Valco-Office-Suite";
        public const string FeedUrl = "https://github.com/" + Repo + "/releases/latest/download/valco-office.json";
        public const string ReleasePage = "https://github.com/" + Repo + "/releases/latest";
        const string StateKey = @"Software\Valco\Office\Updates";

        public record Result(bool Ok, bool Available, string Latest, string Installed, string Description, string Error);

        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        static Updates() { Http.DefaultRequestHeaders.UserAgent.ParseAdd("ValcoOffice/1.0"); }

        /// <summary>Version installed by Valco Office Setup (HKLM\SOFTWARE\Valco\Office\Apps\&lt;id&gt;), else the exe's version.</summary>
        public static string InstalledVersion(string appId)
        {
            try
            {
                using var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Valco\Office\Apps\" + appId);
                if (k?.GetValue("Version") is string v && v.Length > 0) return v;
            }
            catch { }
            var a = Assembly.GetEntryAssembly();
            var info = a?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0];
            return info ?? a?.GetName().Version?.ToString() ?? "0.0.0";
        }

        static Version Parse(string v)
        {
            var parts = (v ?? "0").Split('.', '-', '+').Take(4).Select(p => int.TryParse(p, out var n) ? n : 0).ToList();
            while (parts.Count < 2) parts.Add(0);
            return parts.Count switch { 2 => new Version(parts[0], parts[1]), 3 => new Version(parts[0], parts[1], parts[2]), _ => new Version(parts[0], parts[1], parts[2], parts[3]) };
        }

        public static async Task<Result> CheckAsync(string appId)
        {
            var installed = InstalledVersion(appId);
            try
            {
                var json = await Http.GetStringAsync(FeedUrl);
                using var doc = JsonDocument.Parse(json);
                foreach (var app in doc.RootElement.GetProperty("apps").EnumerateArray())
                {
                    if (!string.Equals(app.GetProperty("id").GetString(), appId, StringComparison.OrdinalIgnoreCase)) continue;
                    var latest = app.GetProperty("version").GetString();
                    var desc = app.TryGetProperty("notes", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                    return new Result(true, Parse(latest) > Parse(installed), latest, installed, desc, null);
                }
                return new Result(true, false, null, installed, null, null);
            }
            catch (Exception ex) { return new Result(false, false, null, installed, null, ex.Message); }
        }

        static bool AutoCheckDue()
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(StateKey);
                if (k.GetValue("AutoCheck") is int on && on == 0) return false;
                if (k.GetValue("LastCheck") is string s && DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var last) && DateTime.UtcNow - last < TimeSpan.FromHours(20)) return false;
                k.SetValue("LastCheck", DateTime.UtcNow.ToString("o"));
                return true;
            }
            catch { return true; }
        }

        /// <summary>Whether apps check GitHub for updates by themselves (HKCU\Software\Valco\Office\Updates\AutoCheck).</summary>
        public static bool AutoCheck
        {
            get { try { using var k = Registry.CurrentUser.OpenSubKey(StateKey); return !(k?.GetValue("AutoCheck") is int on && on == 0); } catch { return true; } }
            set { try { using var k = Registry.CurrentUser.CreateSubKey(StateKey); k.SetValue("AutoCheck", value ? 1 : 0, RegistryValueKind.DWord); } catch { } }
        }

        /// <summary>Call once when the main window has loaded: checks quietly (at most once a day) and offers the update.</summary>
        public static void CheckOnStartup(Window owner, string appId, string appName)
        {
            if (!AutoCheckDue()) return;
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(8));
                var r = await CheckAsync(appId);
                if (r.Ok && r.Available && !Skipped(appId, r.Latest))
                    owner.Dispatcher.Invoke(() => { if (owner.IsLoaded) Offer(owner, appName, appId, r, true); });
            });
        }

        /// <summary>Help / Options → Check for Updates.</summary>
        public static async void CheckNow(Window owner, string appId, string appName)
        {
            var cursor = owner.Cursor;
            owner.Cursor = System.Windows.Input.Cursors.Wait;
            var r = await CheckAsync(appId);
            owner.Cursor = cursor;
            if (!r.Ok) { Message(owner, appName, "Valco Office Update couldn't reach the update server.", "Check your Internet connection and try again.\n\n" + r.Error, false); return; }
            if (!r.Available) { Message(owner, appName, $"{appName} is up to date.", $"You have version {r.Installed}, the latest version.", true); return; }
            Offer(owner, appName, appId, r, false);
        }

        static bool Skipped(string appId, string version)
        {
            try { using var k = Registry.CurrentUser.OpenSubKey(StateKey); return k?.GetValue("Skip." + appId) as string == version; } catch { return false; }
        }

        static void Skip(string appId, string version)
        {
            try { using var k = Registry.CurrentUser.CreateSubKey(StateKey); k.SetValue("Skip." + appId, version); } catch { }
        }

        /// <summary>Starts Valco Office Update if Setup is installed; otherwise opens the release page.</summary>
        public static void Install()
        {
            if (Suite.CheckForUpdates()) return;
            try { Process.Start(new ProcessStartInfo(ReleasePage) { UseShellExecute = true }); } catch { }
        }

        static Window Dialog(Window owner, string appName, string heading, string body, out StackPanel buttons)
        {
            var w = new Window
            {
                Title = "Valco Office Update", Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight, ShowInTaskbar = false, FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
                Background = Brushes.White
            };
            var root = new DockPanel { Width = 440 };
            buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 10, 12, 10) };
            var strip = new Border { Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)), BorderBrush = new SolidColorBrush(Color.FromRgb(0xDF, 0xDF, 0xDF)), BorderThickness = new Thickness(0, 1, 0, 0), Child = buttons };
            DockPanel.SetDock(strip, Dock.Bottom);
            root.Children.Add(strip);
            var body2 = new DockPanel { Margin = new Thickness(16, 16, 16, 12) };
            var mark = new Image { Source = OfficeTheme.SuiteMark, Width = 40, Height = 40, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top };
            DockPanel.SetDock(mark, Dock.Left);
            body2.Children.Add(mark);
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = heading, FontSize = 15, Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x39, 0x87)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            text.Children.Add(new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap });
            body2.Children.Add(text);
            root.Children.Add(body2);
            w.Content = root;
            return w;
        }

        static Button Btn(string text, bool isDefault = false, bool isCancel = false) =>
            new Button { Content = text, MinWidth = 86, Height = 25, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 0, 10, 0), IsDefault = isDefault, IsCancel = isCancel };

        static void Message(Window owner, string appName, string heading, string body, bool ok)
        {
            var w = Dialog(owner, appName, heading, body, out var buttons);
            var b = Btn("OK", true, true);
            b.Click += (s, e) => w.Close();
            buttons.Children.Add(b);
            w.ShowDialog();
        }

        static void Offer(Window owner, string appName, string appId, Result r, bool automatic)
        {
            var body = $"Version {r.Latest} is ready to download. You have version {r.Installed}." +
                       (string.IsNullOrWhiteSpace(r.Description) ? "" : "\n\n" + r.Description) +
                       "\n\nValco Office Update will close the Valco programs it updates, so save your work first.";
            var w = Dialog(owner, appName, $"An update for {appName} is available", body, out var buttons);
            var install = Btn("_Install Updates", true);
            install.Click += (s, e) => { w.Close(); Install(); };
            buttons.Children.Add(install);
            if (automatic)
            {
                var skip = Btn("_Skip This Version");
                skip.Click += (s, e) => { Skip(appId, r.Latest); w.Close(); };
                buttons.Children.Add(skip);
            }
            var later = Btn("_Remind Me Later", false, true);
            later.Click += (s, e) => w.Close();
            buttons.Children.Add(later);
            w.ShowDialog();
        }
    }
}
