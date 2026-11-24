// Shared by every Valco Office app (Word, Express, Code). Edit here, not in the apps.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace Valco.UI
{
    /// <summary>
    /// The Valco button (round orb with the suite mark) and the Quick Access Toolbar in a window's title bar,
    /// exactly as in Office 2007. Needs the shared VistaWindowStyle, which shows the window's Tag in the title row.
    /// </summary>
    public class ValcoTitleBar
    {
        public Button Orb { get; }
        public StackPanel Qat { get; } = new StackPanel { Orientation = Orientation.Horizontal, Height = 22 };
        readonly Window _window;
        readonly List<(string Key, string Icon, string Label, Action Run)> _options = new();
        Func<List<string>> _getItems;
        Action<List<string>> _setItems;
        RibbonBar _ribbon;
        Action<bool> _onMinimize;

        public ValcoTitleBar(Window window, Action showMenu, string orbTip)
        {
            _window = window;
            var canvas = new Canvas { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, ClipToBounds = false };
            Orb = new Button { Width = 40, Height = 40, Focusable = false, Cursor = Cursors.Hand, ToolTip = RB.Tip("Valco Button", orbTip), Template = OrbTemplate() };
            Orb.Click += (s, e) => showMenu();
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(Orb, true);
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(Qat, true);
            Canvas.SetLeft(Orb, 5); Canvas.SetTop(Orb, 4);
            Canvas.SetLeft(Qat, 52); Canvas.SetTop(Qat, 3);
            canvas.Children.Add(Qat);
            canvas.Children.Add(Orb);
            Panel.SetZIndex(Orb, 5);
            window.Tag = canvas;   // the window template shows Tag in the title bar
            window.StateChanged += (s, e) =>
            {
                double top = window.WindowState == WindowState.Maximized ? 6 : 4;
                Canvas.SetTop(Orb, top); Canvas.SetTop(Qat, top - 1);
            };
        }

        /// <summary>Defines the commands the user can put on the Quick Access Toolbar.</summary>
        public ValcoTitleBar QatOption(string key, string icon, string label, Action run) { _options.Add((key, icon, label, run)); return this; }

        /// <summary>Hooks up the Quick Access Toolbar to a stored list of keys and the ribbon (for Minimize the Ribbon).</summary>
        public void InitQat(Func<List<string>> getItems, Action<List<string>> setItems, RibbonBar ribbon, Action<bool> onMinimize = null)
        {
            _getItems = getItems; _setItems = setItems; _ribbon = ribbon; _onMinimize = onMinimize;
            BuildQat();
        }

        public readonly Dictionary<string, Button> QatButtons = new();

        public void BuildQat()
        {
            Qat.Children.Clear();
            QatButtons.Clear();
            foreach (var key in _getItems())
            {
                var opt = _options.FirstOrDefault(o => o.Key == key);
                if (opt.Key == null) continue;
                var b = new Button { Style = RB.BtnStyle, Width = 22, Height = 22, Padding = new Thickness(2), HorizontalContentAlignment = HorizontalAlignment.Center, ToolTip = opt.Label };
                var qi = new Image { Width = 16, Height = 16 };
                qi.SetResourceReference(Image.SourceProperty, opt.Icon); // follows the colour scheme like the ribbon
                b.Content = qi;
                var run = opt.Run;
                b.Click += (s, e) => run();
                QatButtons[key] = b;
                Qat.Children.Add(b);
            }
            var more = new Button
            {
                Style = RB.BtnStyle, Width = 14, Height = 22, Padding = new Thickness(1), HorizontalContentAlignment = HorizontalAlignment.Center, ToolTip = "Customize Quick Access Toolbar",
                Content = new Path { Data = Geometry.Parse("M0,0 H6 M0,3 L6,3 L3,6 Z"), Stroke = new SolidColorBrush(Color.FromRgb(0x2B, 0x3B, 0x50)), Fill = new SolidColorBrush(Color.FromRgb(0x2B, 0x3B, 0x50)) }
            };
            more.Click += (s, e) =>
            {
                var m = new ContextMenu();
                m.Items.Add(new MenuItem { Header = "Customize Quick Access Toolbar", IsEnabled = false, FontWeight = FontWeights.Bold });
                var items = _getItems();
                foreach (var o in _options)
                {
                    var key = o.Key;
                    m.Items.Add(RB.MI(o.Label, () =>
                    {
                        var l = _getItems();
                        if (l.Contains(key)) l.Remove(key); else l.Add(key);
                        _setItems(l);
                        BuildQat();
                    }, null, null, items.Contains(key)));
                }
                if (_ribbon != null)
                {
                    m.Items.Add(new Separator());
                    m.Items.Add(RB.MI(_ribbon.Minimized ? "Restore the Ribbon" : "Minimize the Ribbon", () => { _ribbon.ToggleMinimize(); _onMinimize?.Invoke(_ribbon.Minimized); }));
                }
                RB.OpenMenu(more, m);
            };
            Qat.Children.Add(more);
        }

        public static ControlTemplate OrbTemplate()
        {
            var t = new ControlTemplate(typeof(Button));
            var g = new FrameworkElementFactory(typeof(Grid));
            var outer = new FrameworkElementFactory(typeof(Ellipse), "Ring");
            outer.SetValue(Shape.FillProperty, new RadialGradientBrush(new GradientStopCollection { new GradientStop(Color.FromRgb(0xE9, 0xF2, 0xFC), 0), new GradientStop(Color.FromRgb(0x8C, 0xA9, 0xD1), 0.85), new GradientStop(Color.FromRgb(0x5A, 0x7A, 0xA8), 1) }));
            outer.SetValue(Shape.StrokeProperty, new SolidColorBrush(Color.FromRgb(0x3B, 0x5A, 0x86)));
            outer.SetValue(UIElement.EffectProperty, new DropShadowEffect { ShadowDepth = 1, BlurRadius = 4, Opacity = 0.45 });
            g.AppendChild(outer);
            var inner = new FrameworkElementFactory(typeof(Ellipse), "Glow");
            inner.SetValue(FrameworkElement.MarginProperty, new Thickness(3));
            inner.SetValue(Shape.FillProperty, new RadialGradientBrush(new GradientStopCollection { new GradientStop(Color.FromArgb(0x00, 0xFF, 0xD6, 0x60), 0.6), new GradientStop(Color.FromArgb(0x00, 0xFF, 0xD6, 0x60), 1) }) { GradientOrigin = new Point(0.5, 0.8) });
            g.AppendChild(inner);
            var img = new FrameworkElementFactory(typeof(Image));
            img.SetValue(Image.SourceProperty, OfficeTheme.SuiteMark);
            img.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 5, 6, 7));
            img.SetValue(RenderOptions.BitmapScalingModeProperty, BitmapScalingMode.HighQuality);
            g.AppendChild(img);
            t.VisualTree = g;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Shape.FillProperty, new RadialGradientBrush(new GradientStopCollection { new GradientStop(Color.FromArgb(0x00, 0xFF, 0xD0, 0x50), 0.55), new GradientStop(Color.FromArgb(0xC0, 0xFF, 0xC8, 0x40), 1) }), "Glow"));
            var press = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            press.Setters.Add(new Setter(Shape.FillProperty, new RadialGradientBrush(new GradientStopCollection { new GradientStop(Color.FromArgb(0x30, 0xF0, 0x90, 0x20), 0.4), new GradientStop(Color.FromArgb(0xE0, 0xF0, 0x90, 0x20), 1) }), "Glow"));
            t.Triggers.Add(hover); t.Triggers.Add(press);
            return t;
        }
    }

    /// <summary>An entry in the right-hand pane of the Valco menu.</summary>
    public record AppMenuEntry(string Icon, string Title, string Description, Action Run);

    /// <summary>The Valco menu that drops from the orb (Office 2007's Office menu): commands on the left, recent items or a sub-menu on the right.</summary>
    public class AppMenu
    {
        readonly List<(string Icon, string Text, Action Run, string SubTitle, AppMenuEntry[] Sub)> _items = new();
        readonly List<(string Text, string Icon, Action Run)> _footer = new();
        string _recentTitle = "Recent Items";
        Func<IEnumerable<(string Label, string Tip, Action Open)>> _recent = () => Enumerable.Empty<(string, string, Action)>();

        public AppMenu Item(string icon, string text, Action run) { _items.Add((icon, text, run, null, null)); return this; }
        public AppMenu Item(string icon, string text, Action run, string subTitle, params AppMenuEntry[] sub) { _items.Add((icon, text, run, subTitle, sub)); return this; }
        public AppMenu Sep() { _items.Add((null, null, null, null, null)); return this; }
        public AppMenu Recent(string title, Func<IEnumerable<(string Label, string Tip, Action Open)>> items) { _recentTitle = title; _recent = items; return this; }
        public AppMenu Footer(string text, string icon, Action run) { _footer.Add((text, icon, run)); return this; }

        static readonly Brush Line = Frozen(new SolidColorBrush(Color.FromRgb(0xC5, 0xD3, 0xE6)));
        static readonly Brush Heading = Frozen(new SolidColorBrush(Color.FromRgb(0x15, 0x42, 0x8B)));
        static Brush Frozen(Brush b) { b.Freeze(); return b; }

        static Border Header(string title) => new Border
        {
            BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(6, 4, 6, 4), Padding = new Thickness(0, 0, 0, 3),
            Child = new TextBlock { Text = title, FontWeight = FontWeights.Bold, Foreground = Heading }
        };

        public void Show(FrameworkElement orb)
        {
            var popup = new Popup { PlacementTarget = orb, Placement = PlacementMode.Bottom, VerticalOffset = -18, HorizontalOffset = -4, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade };
            var right = new ContentControl { Width = 330 };

            UIElement RecentList()
            {
                var sp = new StackPanel();
                sp.Children.Add(Header(_recentTitle));
                int i = 1;
                foreach (var r in _recent().Take(17))
                {
                    var open = r.Open;
                    var b = new Button
                    {
                        Style = RB.BtnStyle, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(4, 2, 4, 2), ToolTip = r.Tip,
                        Content = new TextBlock { Text = $"{i++}  {r.Label}", TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Brushes.Black }
                    };
                    b.Click += (s, e) => { popup.IsOpen = false; open(); };
                    sp.Children.Add(b);
                }
                return sp;
            }

            UIElement SubPanel(string title, AppMenuEntry[] items)
            {
                var sp = new StackPanel();
                sp.Children.Add(Header(title));
                foreach (var it in items)
                {
                    var b = new Button { Style = RB.BtnStyle, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(4), Margin = new Thickness(2, 0, 2, 1) };
                    var g = new DockPanel();
                    var img = new Image { Source = RB.Icon(it.Icon), Width = 32, Height = 32, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top };
                    DockPanel.SetDock(img, Dock.Left);
                    g.Children.Add(img);
                    var t = new StackPanel();
                    t.Children.Add(new TextBlock { Text = it.Title, FontWeight = FontWeights.Bold, Foreground = Brushes.Black });
                    t.Children.Add(new TextBlock { Text = it.Description, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)), MaxWidth = 250 });
                    g.Children.Add(t);
                    b.Content = g;
                    var run = it.Run;
                    b.Click += (s, e) => { popup.IsOpen = false; run(); };
                    sp.Children.Add(b);
                }
                return sp;
            }

            var left = new StackPanel { Width = 180, Margin = new Thickness(2) };
            foreach (var it in _items)
            {
                if (it.Text == null) { left.Children.Add(new Border { Height = 1, Background = Line, Margin = new Thickness(40, 2, 2, 2) }); continue; }
                var item = it;
                var b = new Button { Style = RB.BtnStyle, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(0, 0, 0, 1) };
                var dp = new DockPanel();
                if (item.Sub != null) { var arr = new Path { Data = Geometry.Parse("M0,0 L4,4 L0,8 Z"), Fill = Brushes.Black, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(arr, Dock.Right); dp.Children.Add(arr); }
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new Image { Source = RB.Icon(item.Icon), Width = 32, Height = 32, Margin = new Thickness(0, 0, 8, 0) });
                sp.Children.Add(new TextBlock { Text = item.Text, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Black });
                dp.Children.Add(sp);
                b.Content = dp;
                if (item.Sub != null)
                {
                    b.MouseEnter += (s, e) => right.Content = SubPanel(item.SubTitle, item.Sub);
                    b.Click += (s, e) => { if (item.Run != null) { popup.IsOpen = false; item.Run(); } else right.Content = SubPanel(item.SubTitle, item.Sub); };
                }
                else
                {
                    b.MouseEnter += (s, e) => right.Content = RecentList();
                    b.Click += (s, e) => { popup.IsOpen = false; item.Run?.Invoke(); };
                }
                left.Children.Add(b);
            }

            var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(4) };
            foreach (var f in _footer)
            {
                var run = f.Run;
                var b = RB.MenuButton(f.Text, () => { popup.IsOpen = false; run(); }, f.Icon);
                b.Background = Brushes.White; b.BorderBrush = Brushes.Gray; b.Margin = new Thickness(4, 0, 0, 0);
                bottom.Children.Add(b);
            }

            right.Content = RecentList();
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition());
            body.Children.Add(left);
            var rb = new Border { Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFD)), BorderBrush = Line, BorderThickness = new Thickness(1, 0, 0, 0), Child = right, MinHeight = 340 };
            Grid.SetColumn(rb, 1);
            body.Children.Add(rb);
            var dp2 = new DockPanel();
            DockPanel.SetDock(bottom, Dock.Bottom);
            dp2.Children.Add(bottom);
            dp2.Children.Add(new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0x9B, 0xB4, 0xD6)), BorderThickness = new Thickness(1), Child = body, Margin = new Thickness(4, 0, 4, 0) });
            popup.Child = new Border
            {
                CornerRadius = new CornerRadius(4), BorderBrush = new SolidColorBrush(Color.FromRgb(0x6F, 0x8F, 0xBD)), BorderThickness = new Thickness(1),
                Background = new LinearGradientBrush(Color.FromRgb(0xDF, 0xEA, 0xF8), Color.FromRgb(0xC2, 0xD5, 0xEE), 90),
                Padding = new Thickness(0, 22, 0, 0), Child = dp2, Effect = new DropShadowEffect { ShadowDepth = 3, Opacity = 0.4, BlurRadius = 8 }, Margin = new Thickness(0, 0, 8, 8)
            };
            popup.IsOpen = true;
        }
    }

    /// <summary>Suite-wide helpers: Valco Office Update and the shared user name.</summary>
    public static class Suite
    {
        /// <summary>Install folder of Valco Office (HKLM\SOFTWARE\Valco\Office\InstallRoot), or null when not installed by Setup.</summary>
        public static string InstallRoot
        {
            get
            {
                try
                {
                    using var k = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Valco\Office");
                    return k?.GetValue("InstallRoot") as string;
                }
                catch { return null; }
            }
        }

        /// <summary>Opens Valco Office Update. Returns false when Valco Office Setup isn't installed.</summary>
        public static bool CheckForUpdates()
        {
            var root = InstallRoot;
            var setup = root == null ? null : System.IO.Path.Combine(root, "Setup", "setup.exe");
            if (setup == null || !System.IO.File.Exists(setup)) return false;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(setup, "/update") { UseShellExecute = true }); return true; }
            catch { return false; }
        }

        /// <summary>The user name entered in Valco Office Setup (HKCU\Software\Valco\Office\UserInfo).</summary>
        public static string UserName
        {
            get
            {
                try { using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valco\Office\UserInfo"); return k?.GetValue("UserName") as string; }
                catch { return null; }
            }
        }

        /// <summary>Opens Settings → Default apps for a Valco app (registered by Setup under RegisteredApplications).</summary>
        public static void OpenDefaultApps(string registeredName)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:defaultapps?registeredAppMachine=" + Uri.EscapeDataString(registeredName)) { UseShellExecute = true }); }
            catch { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true }); } catch { } }
        }
    }
}
