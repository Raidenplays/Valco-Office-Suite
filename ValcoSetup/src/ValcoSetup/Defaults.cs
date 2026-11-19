using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ValcoSetup
{
    /// <summary>
    /// "Set Default Programs" for Valco Office, like Vista's Default Programs control panel.
    /// Windows 11 protects each user's defaults with a hash, so no program may change them silently:
    /// Setup registers every Valco program (Capabilities + RegisteredApplications) and this window
    /// opens Settings → Default apps on each program's page, where one click makes it the default.
    /// </summary>
    public class DefaultAppsWindow : VistaWindow
    {
        record Prog(string Name, string Registered, string Exe, string Kinds);

        static List<Prog> Installed()
        {
            var list = new List<Prog>();
            var root = Engine.InstalledRoot;
            var m = Engine.InstalledManifest();
            var installed = Engine.InstalledApps();
            if (m == null) return list;
            foreach (var a in m.Apps.Where(a => installed.ContainsKey(a.Id) && !string.IsNullOrEmpty(a.DefaultApps)))
            {
                var exe = Path.Combine(root ?? "", a.Folder ?? a.Name, a.Exe ?? "");
                list.Add(new Prog(a.Name, a.DefaultApps, exe, Kinds(a.DefaultApps)));
            }
            return list;
        }

        /// <summary>What a program can be the default for, read from its registered Capabilities.</summary>
        static string Kinds(string registered)
        {
            try
            {
                using var reg = Registry.LocalMachine.OpenSubKey(@"Software\RegisteredApplications");
                var capPath = reg?.GetValue(registered) as string;
                if (capPath == null) return "";
                using var cap = Registry.LocalMachine.OpenSubKey(capPath);
                var parts = new List<string>();
                using (var fa = cap?.OpenSubKey("FileAssociations")) if (fa != null) parts.AddRange(fa.GetValueNames());
                using (var ua = cap?.OpenSubKey("URLAssociations")) if (ua != null) parts.AddRange(ua.GetValueNames().Select(n => n + ":"));
                if (parts.Count > 8) return string.Join(", ", parts.Take(8)) + $" and {parts.Count - 8} more";
                return string.Join(", ", parts);
            }
            catch { return ""; }
        }

        public static void Open(string registered)
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:defaultapps?registeredAppMachine=" + Uri.EscapeDataString(registered)) { UseShellExecute = true }); }
            catch { try { Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true }); } catch { } }
        }

        public DefaultAppsWindow(Window owner = null)
        {
            Title = "Set Default Programs - Valco Office";
            Width = 560; SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            if (owner != null) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var sp = new StackPanel { Margin = new Thickness(18, 14, 18, 10) };
            sp.Children.Add(new TextBlock { Text = "Make Valco Office your default programs", FontSize = 17, Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x39, 0x87)), Margin = new Thickness(0, 0, 0, 8) });
            sp.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
                Text = "Windows only lets you choose default programs yourself. Click Set as default next to a program: Windows Settings opens on that program's page. Click \"Set default\" at the top of that page to use it for everything it can open."
            });
            var progs = Installed();
            if (progs.Count == 0)
                sp.Children.Add(new TextBlock { Text = "No Valco Office programs are installed.", Foreground = Brushes.DimGray });
            foreach (var p in progs)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
                var btn = new Button { Content = "Set as _default", MinWidth = 110, Height = 25, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
                var prog = p;
                btn.Click += (s, e) => Open(prog.Registered);
                DockPanel.SetDock(btn, Dock.Right);
                row.Children.Add(btn);
                ImageSource icon = null;
                try { if (File.Exists(p.Exe)) icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(System.Drawing.Icon.ExtractAssociatedIcon(p.Exe).Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32)); } catch { }
                var img = new Image { Source = icon, Width = 32, Height = 32, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top };
                DockPanel.SetDock(img, Dock.Left);
                row.Children.Add(img);
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = p.Name, FontWeight = FontWeights.SemiBold });
                text.Children.Add(new TextBlock { Text = p.Kinds.Length > 0 ? "Opens " + p.Kinds : "", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray });
                row.Children.Add(text);
                sp.Children.Add(row);
            }
            var close = new Button { Content = "Close", MinWidth = 75, Height = 25, IsCancel = true, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
            close.Click += (s, e) => Close();
            sp.Children.Add(close);
            Content = sp;
        }
    }
}
