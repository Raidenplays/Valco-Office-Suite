using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.IO.Path;

namespace ValcoSetup
{
    /// <summary>The Office 2007-style setup wizard: license, install type, customize tabs, progress, finish, and maintenance.</summary>
    public class SetupWindow : VistaWindow
    {
        // ---------------- look ----------------
        public static readonly Brush HeadingBrush = new SolidColorBrush(Color.FromRgb(0x1F, 0x3F, 0x6E));
        public static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
        public static readonly Brush DimBrush = new SolidColorBrush(Color.FromRgb(0x6D, 0x6D, 0x6D));
        static ImageSource Mark => new BitmapImage(new Uri("pack://application:,,,/ValcoOffice-mark.png"));

        readonly Border _body = new Border { Padding = new Thickness(28, 18, 28, 14) };
        readonly StackPanel _buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _footerNote = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0), Foreground = DimBrush };

        SuiteManifest _m;
        readonly Engine _engine = new Engine();
        readonly Dictionary<string, bool> _selected = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        bool _maintenance, _busy;
        List<(string AppId, string Name, string ProductCode, string Version)> _previous = new List<(string, string, string, string)>();

        public SetupWindow()
        {
            Title = "Valco Office Setup";
            Width = 660; Height = 520;
            ResizeMode = ResizeMode.CanMinimize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Brushes.White;
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
            root.Children.Add(Header());
            Grid.SetRow(_body, 1); root.Children.Add(_body);
            var footer = new Border { Background = new LinearGradientBrush(Color.FromRgb(0xF5, 0xF6, 0xF8), Color.FromRgb(0xE6, 0xE9, 0xEE), 90), BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xCF, 0xD9)), BorderThickness = new Thickness(0, 1, 0, 0) };
            var fg = new Grid(); fg.Children.Add(_footerNote); fg.Children.Add(_buttons); footer.Child = fg;
            Grid.SetRow(footer, 2); root.Children.Add(footer);
            Content = root;
            Closing += (s, e) =>
            {
                if (_busy) { e.Cancel = true; return; }
            };
            Closed += (s, e) => Application.Current.Shutdown();
        }

        UIElement Header()
        {
            var g = new Grid { Height = 82, Background = new LinearGradientBrush(new GradientStopCollection { new GradientStop(Colors.White, 0), new GradientStop(Color.FromRgb(0xF2, 0xF6, 0xFC), 1) }, 90) };
            // faint swooshes, like the Office 2007 setup banner
            var sw = new System.Windows.Shapes.Path { Data = Geometry.Parse("M180,82 C320,10 470,90 660,8 L660,82 Z"), Fill = new LinearGradientBrush(Color.FromArgb(0x26, 0x4F, 0x8F, 0xD8), Color.FromArgb(0x00, 0x4F, 0x8F, 0xD8), 0), IsHitTestVisible = false };
            var sw2 = new System.Windows.Shapes.Path { Data = Geometry.Parse("M260,82 C400,40 520,96 660,40"), Stroke = new LinearGradientBrush(Color.FromArgb(0x00, 0x9B, 0xD8, 0x5A), Color.FromArgb(0x90, 0x9B, 0xD8, 0x5A), 0), StrokeThickness = 2, IsHitTestVisible = false };
            g.Children.Add(sw); g.Children.Add(sw2);
            var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(26, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(new TextBlock { Text = "Valco", FontFamily = new FontFamily("Segoe UI"), FontSize = 28, FontWeight = FontWeights.SemiBold, Foreground = HeadingBrush });
            title.Children.Add(new TextBlock { Text = " Office", FontFamily = new FontFamily("Segoe UI Light"), FontSize = 28, Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0x6F, 0xA8)) });
            g.Children.Add(title);
            g.Children.Add(new Image { Source = Mark, Width = 58, Height = 58, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 26, 0), VerticalAlignment = VerticalAlignment.Center });
            var line = new Border { Height = 3, VerticalAlignment = VerticalAlignment.Bottom, Background = new LinearGradientBrush(new GradientStopCollection { new GradientStop(Color.FromRgb(0x2F, 0x8B, 0xE0), 0), new GradientStop(Color.FromRgb(0x5C, 0xC8, 0x3A), 0.5), new GradientStop(Color.FromRgb(0xFF, 0xB2, 0x1E), 1) }, 0) };
            g.Children.Add(line);
            return g;
        }

        // =====================================================================================
        //  Entry points
        // =====================================================================================
        public static void StartInstall(string payload)
        {
            var splash = new Splash();
            splash.Show();
            Task.Run(() =>
            {
                SuiteManifest m = null; string error = null;
                try
                {
                    if (payload == null)
                    {
                        var self = new FileInfo(Environment.ProcessPath);
                        payload = Package.Extract(self.FullName, Path.Combine(Path.GetTempPath(), "ValcoOfficeSetup", $"{self.Length:x}-{self.LastWriteTimeUtc.Ticks:x}"));
                    }
                    m = SuiteManifest.Load(Path.Combine(payload, "valco-office.json"));
                    foreach (var a in m.Apps)
                    {
                        var f = Path.Combine(payload, a.File ?? "");
                        if (!File.Exists(f)) continue;
                        if (!string.IsNullOrEmpty(a.Sha256) && !Engine.Sha256(f).Equals(a.Sha256, StringComparison.OrdinalIgnoreCase))
                        { error = $"The installation file for {a.Name} ({a.File}) is damaged or incomplete. Download Valco Office Setup again."; break; }
                    }
                    m.Apps.RemoveAll(a => !File.Exists(Path.Combine(payload, a.File ?? "")));
                    if (m.Apps.Count == 0) error = "This copy of Valco Office Setup doesn't contain any programs.";
                }
                catch (Exception ex) { error = ex.Message; }
                splash.Dispatcher.Invoke(() =>
                {
                    splash.Close();
                    if (error != null) { VistaDialog.Show(null, error, "Valco Office Setup", MessageBoxButton.OK, DlgIcon.Error, "Setup can't continue"); Application.Current.Shutdown(); return; }
                    var w = new SetupWindow { _m = m };
                    w._engine.Manifest = m; w._engine.SourceDir = payload;
                    var installed = Engine.InstalledApps();
                    if (Engine.IsInstalled)
                    {
                        w._engine.InstallRoot = Engine.InstalledRoot;
                        // same or older suite already installed: Office shows its maintenance screen
                        bool newer = m.Apps.Any(a => !installed.ContainsKey(a.Id) ? false : Ver.Newer(a.Version, installed[a.Id])) || Ver.Newer(m.Version, Engine.GetOption("Version", "0"));
                        if (!newer) { w._maintenance = true; w._engine.Manifest = MergeWithInstalled(m); w.InitSelectionFromInstalled(); w.Show(); w.MaintenancePage(false); return; }
                    }
                    foreach (var a in m.Apps) w._selected[a.Id] = installed.ContainsKey(a.Id) || (installed.Count == 0 && a.DefaultSelected);
                    w._previous = Engine.PreviousVersions(m);
                    if (w._previous.Count > 0) foreach (var p in w._previous) w._selected[p.AppId] = true;
                    w.Show();
                    w.LicensePage();
                });
            });
            Application.Current.MainWindow = splash;
        }

        static SuiteManifest MergeWithInstalled(SuiteManifest m)
        {
            var inst = Engine.InstalledManifest();
            if (inst == null) return m;
            foreach (var a in m.Apps) { inst.Apps.RemoveAll(x => x.Id == a.Id); inst.Apps.Add(a); }
            return inst;
        }

        public static void StartMaintenance(bool removeOnly)
        {
            var m = Engine.InstalledManifest();
            if (m == null || !Engine.IsInstalled)
            {
                VistaDialog.Show(null, "Valco Office isn't installed on this computer.", "Valco Office Setup", MessageBoxButton.OK, DlgIcon.Info);
                Application.Current.Shutdown();
                return;
            }
            var w = new SetupWindow { _m = m, _maintenance = true };
            w._engine.Manifest = m;
            w._engine.InstallRoot = Engine.InstalledRoot;
            w._engine.SourceDir = w._engine.CacheDir;
            w.InitSelectionFromInstalled();
            Application.Current.MainWindow = w;
            w.Show();
            w.MaintenancePage(removeOnly);
        }

        void InitSelectionFromInstalled()
        {
            var installed = Engine.InstalledApps();
            foreach (var a in _engine.Manifest.Apps) _selected[a.Id] = installed.ContainsKey(a.Id);
            _engine.DesktopShortcuts = Engine.GetOption("DesktopShortcuts", "1") == "1";
            _engine.Associations = Engine.GetOption("Associations", "1") == "1";
            _engine.AutoUpdate = Engine.GetOption("AutoUpdate", "1") == "1";
        }

        // =====================================================================================
        //  Building blocks
        // =====================================================================================
        static TextBlock Heading(string text) => new TextBlock { Text = text, FontSize = 19, FontFamily = new FontFamily("Segoe UI"), Foreground = HeadingBrush, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap };
        static TextBlock Para(string text, double top = 0) => new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = TextBrush, Margin = new Thickness(0, top, 0, 6), LineHeight = 18 };

        Button FooterButton(string text, Action click, bool isDefault = false)
        {
            var b = UI.Btn(text, (s, e) => click(), isDefault);
            b.MinWidth = 86; b.Margin = new Thickness(6, 0, 0, 0);
            _buttons.Children.Add(b);
            return b;
        }

        void SetPage(UIElement content, string note = null)
        {
            _buttons.Children.Clear();
            _footerNote.Text = note ?? "";
            _body.Child = content;
        }

        /// <summary>The big glassy choice buttons of the "Choose the installation you want" page.</summary>
        static Button BigButton(string text, ImageSource icon, Action click)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            if (icon != null) content.Children.Add(new Image { Source = icon, Width = 32, Height = 32, Margin = new Thickness(0, 0, 12, 0) });
            content.Children.Add(new TextBlock { Text = text, FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Foreground = HeadingBrush });
            var b = new Button { Content = content, Width = 200, Height = 64, Margin = new Thickness(0, 0, 0, 14), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(16, 0, 10, 0), Cursor = Cursors.Hand };
            var t = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border), "B");
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            bd.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x9C, 0xB3, 0xD3)));
            bd.SetValue(Border.BackgroundProperty, new LinearGradientBrush(new GradientStopCollection { new GradientStop(Color.FromRgb(0xFF, 0xFF, 0xFF), 0), new GradientStop(Color.FromRgb(0xEE, 0xF3, 0xFA), 0.49), new GradientStop(Color.FromRgb(0xDD, 0xE7, 0xF5), 0.5), new GradientStop(Color.FromRgb(0xEC, 0xF2, 0xFB), 1) }, 90));
            bd.SetValue(UIElement.EffectProperty, new DropShadowEffect { ShadowDepth = 1, BlurRadius = 4, Opacity = 0.25 });
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            t.VisualTree = bd;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, new LinearGradientBrush(new GradientStopCollection { new GradientStop(Color.FromRgb(0xFF, 0xFD, 0xF0), 0), new GradientStop(Color.FromRgb(0xFF, 0xED, 0xB3), 0.49), new GradientStop(Color.FromRgb(0xFF, 0xD6, 0x60), 0.5), new GradientStop(Color.FromRgb(0xFF, 0xE7, 0xA0), 1) }, 90), "B"));
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0xDB, 0xA9, 0x3E)), "B"));
            var press = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            press.Setters.Add(new Setter(Border.BackgroundProperty, new LinearGradientBrush(Color.FromRgb(0xF8, 0xB6, 0x5D), Color.FromRgb(0xFC, 0xD3, 0x8E), 90), "B"));
            t.Triggers.Add(hover); t.Triggers.Add(press);
            b.Template = t;
            b.Click += (s, e) => click();
            return b;
        }

        static ImageSource AppIcon(string payload, AppEntry a, int size = 32)
        {
            foreach (var dir in new[] { payload, Path.Combine(Engine.InstalledRoot ?? "", "Setup", "Cache") })
            {
                if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(a.Icon)) continue;
                var f = Path.Combine(dir, a.Icon);
                if (!File.Exists(f)) continue;
                try
                {
                    var dec = BitmapDecoder.Create(new Uri(f), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    return dec.Frames.OrderBy(fr => Math.Abs(fr.PixelWidth - size)).First();
                }
                catch { }
            }
            return Mark;
        }

        // =====================================================================================
        //  1. License
        // =====================================================================================
        void LicensePage()
        {
            var sp = new DockPanel();
            var head = new StackPanel();
            head.Children.Add(Heading("Read the Valco Software License Terms"));
            head.Children.Add(Para("To continue with Valco Office Setup you need to accept the terms of the Valco Software License Terms. To review the agreement, scroll down or use the Page Down key. Click the check box to accept it."));
            DockPanel.SetDock(head, Dock.Top); sp.Children.Add(head);
            var accept = new CheckBox { Content = "I _accept the terms of this agreement", Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(accept, Dock.Bottom); sp.Children.Add(accept);
            var box = new RichTextBox { IsReadOnly = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0xAB, 0xAD, 0xB3)), Padding = new Thickness(6) };
            var rtf = Path.Combine(_engine.SourceDir ?? "", "License.rtf");
            try
            {
                if (File.Exists(rtf)) using (var fs = File.OpenRead(rtf)) new TextRange(box.Document.ContentStart, box.Document.ContentEnd).Load(fs, DataFormats.Rtf);
                else box.Document = new FlowDocument(new Paragraph(new Run(DefaultLicense)));
            }
            catch { box.Document = new FlowDocument(new Paragraph(new Run(DefaultLicense))); }
            box.Document.FontFamily = new FontFamily("Segoe UI"); box.Document.FontSize = 12;
            sp.Children.Add(box);
            SetPage(sp);
            var cont = FooterButton("_Continue", ChoosePage, true);
            cont.IsEnabled = false;
            accept.Click += (s, e) => cont.IsEnabled = accept.IsChecked == true;
        }

        const string DefaultLicense = "VALCO SOFTWARE LICENSE TERMS\n\nValco Office\n© 2026 ItsRaiden. All rights reserved.\n\nThese license terms are an agreement between ItsRaiden and you. They apply to Valco Office and each of its programs (Valco Word, Valco Express, Valco Code, Valco Weave and ValcoNet), including updates.\n\n1. You may install and use any number of copies of the software on your devices.\n2. The software is provided \"as is\", without warranty of any kind. To the extent permitted by law, ItsRaiden is not liable for any damages arising from the use of the software.\n3. Valco Word's thesaurus uses WordNet 3.0, © 2006 Princeton University, used under the WordNet license.\n4. ValcoNet uses the Gecko engine from Mozilla Firefox ESR, © Mozilla contributors, under the Mozilla Public License 2.0.\n5. Some features use online services (research, translation, updates). Using them sends the words you look up, or the version check, to those services.";

        // =====================================================================================
        //  2. Choose the installation you want
        // =====================================================================================
        void ChoosePage()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel();
            left.Children.Add(Heading("Choose the installation you want"));
            bool upgrade = _previous.Count > 0 || Engine.IsInstalled;
            left.Children.Add(Para(upgrade
                ? "An earlier version of a Valco program was found on this computer. Click Upgrade to upgrade it to Valco Office, or Customize to choose which programs to keep and install."
                : "Please choose your preferred installation of Valco Office below."));
            var sizeMb = _m.Apps.Where(a => _selected.TryGetValue(a.Id, out var s) && s).Sum(a => a.InstallSize) / (1024.0 * 1024);
            left.Children.Add(Para($"Install Now installs {string.Join(", ", _m.Apps.Where(a => _selected.TryGetValue(a.Id, out var s) && s).Select(a => a.Name))} ({sizeMb:N0} MB).", 8));
            var right = new StackPanel { Margin = new Thickness(24, 34, 0, 0) };
            right.Children.Add(BigButton(upgrade ? "Upgrade" : "Install Now", UI.Img("IconSave") as ImageSource ?? Mark, () => Install()));
            right.Children.Add(BigButton("Customize", UI.Img("IconOptions") as ImageSource ?? Mark, CustomizePage));
            g.Children.Add(left);
            Grid.SetColumn(right, 1); g.Children.Add(right);
            SetPage(g);
        }

        // =====================================================================================
        //  3. Customize: Upgrade / Installation Options / File Location / User Information
        // =====================================================================================
        void CustomizePage() => CustomizePage(false);
        void CustomizePage(bool maintenanceOnly)
        {
            var tabs = new TabControl { Margin = new Thickness(-8, -4, -8, 0) };
            if (!maintenanceOnly && _previous.Count > 0)
            {
                var sp = new StackPanel { Margin = new Thickness(10) };
                sp.Children.Add(Para("An earlier version of these Valco programs is installed:"));
                foreach (var p in _previous) sp.Children.Add(new TextBlock { Text = $"   •  {p.Name} {p.Version}", Foreground = TextBrush, Margin = new Thickness(0, 0, 0, 2) });
                var remove = new RadioButton { Content = "_Remove all previous versions", IsChecked = _engine.RemoveOldVersions, Margin = new Thickness(0, 12, 0, 4) };
                var keep = new RadioButton { Content = "_Keep all previous versions", IsChecked = !_engine.RemoveOldVersions };
                remove.Checked += (s, e) => _engine.RemoveOldVersions = true; keep.Checked += (s, e) => _engine.RemoveOldVersions = false;
                sp.Children.Add(remove); sp.Children.Add(keep);
                sp.Children.Add(Para("Keeping both versions installs separate Start menu shortcuts for each copy.", 10));
                tabs.Items.Add(new TabItem { Header = "Upgrade", Content = sp });
            }
            tabs.Items.Add(new TabItem { Header = "Installation Options", Content = InstallationOptions() });
            if (!maintenanceOnly)
            {
                // File Location
                var fl = new StackPanel { Margin = new Thickness(10) };
                fl.Children.Add(new TextBlock { Text = "Choose a file location", FontSize = 14, Foreground = HeadingBrush, Margin = new Thickness(0, 0, 0, 8) });
                fl.Children.Add(Para("Valco Office will be installed on your computer. To install it in a different location, click Browse and select the location."));
                var path = new TextBox { Text = _engine.InstallRoot, Height = 23, VerticalContentAlignment = VerticalAlignment.Center, IsEnabled = !Engine.IsInstalled };
                var browse = UI.Btn("B_rowse...", (s, e) =>
                {
                    var d = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose where to install Valco Office", SelectedPath = path.Text, UseDescriptionForTitle = true };
                    if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) path.Text = d.SelectedPath.EndsWith("Valco Office", StringComparison.OrdinalIgnoreCase) ? d.SelectedPath : Path.Combine(d.SelectedPath, "Valco Office");
                });
                browse.Margin = new Thickness(6, 0, 0, 0); browse.IsEnabled = !Engine.IsInstalled;
                var row = new DockPanel { Margin = new Thickness(0, 4, 0, 10) }; DockPanel.SetDock(browse, Dock.Right); row.Children.Add(browse); row.Children.Add(path);
                fl.Children.Add(row);
                var space = new TextBlock { Foreground = TextBrush };
                void Space() => space.Text = $"Space required on drive: {RequiredMb():N0} MB\nSpace available on drive: {AvailableGb(path.Text):N1} GB";
                path.TextChanged += (s, e) => { _engine.InstallRoot = path.Text.Trim(); Space(); };
                Space();
                fl.Children.Add(space);
                if (Engine.IsInstalled) fl.Children.Add(Para("Valco Office is already installed, so its location can't be changed.", 10));
                tabs.Items.Add(new TabItem { Header = "File Location", Content = fl });

                // User Information
                var ui = new StackPanel { Margin = new Thickness(10) };
                ui.Children.Add(new TextBlock { Text = "Type your information", FontSize = 14, Foreground = HeadingBrush, Margin = new Thickness(0, 0, 0, 8) });
                ui.Children.Add(Para("Valco Office programs use this information to identify the person who makes changes in shared documents (for example comments and tracked changes in Valco Word)."));
                var name = new TextBox { Width = 300, Height = 23, VerticalContentAlignment = VerticalAlignment.Center, Text = _engine.UserName ?? Environment.UserName };
                var ini = new TextBox { Width = 80, Height = 23, VerticalContentAlignment = VerticalAlignment.Center, Text = _engine.Initials ?? Initials(Environment.UserName) };
                var org = new TextBox { Width = 300, Height = 23, VerticalContentAlignment = VerticalAlignment.Center, Text = _engine.Organization ?? "" };
                name.TextChanged += (s, e) => { _engine.UserName = name.Text; ini.Text = Initials(name.Text); };
                ini.TextChanged += (s, e) => _engine.Initials = ini.Text;
                org.TextChanged += (s, e) => _engine.Organization = org.Text;
                _engine.UserName = name.Text; _engine.Initials = ini.Text;
                var form = new Grid { Margin = new Thickness(0, 8, 0, 0) };
                form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) }); form.ColumnDefinitions.Add(new ColumnDefinition());
                int r = 0;
                foreach (var (lab, box) in new (string, TextBox)[] { ("_Full name:", name), ("_Initials:", ini), ("_Organization:", org) })
                {
                    form.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
                    var l = new Label { Content = lab, Target = box, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetRow(l, r); Grid.SetRow(box, r); Grid.SetColumn(box, 1);
                    box.HorizontalAlignment = HorizontalAlignment.Left;
                    form.Children.Add(l); form.Children.Add(box); r++;
                }
                ui.Children.Add(form);
                tabs.Items.Add(new TabItem { Header = "User Information", Content = ui });
            }
            tabs.SelectedIndex = !maintenanceOnly && _previous.Count > 0 ? 1 : 0;
            SetPage(tabs);
            if (maintenanceOnly) FooterButton("_Continue", () => Install(), true);
            else FooterButton(_previous.Count > 0 || Engine.IsInstalled ? "_Upgrade" : "_Install Now", () => Install(), true);
        }

        static string Initials(string name) => new string((name ?? "").Split(new[] { ' ', '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0])).Take(3).ToArray());

        double RequiredMb() => _engine.Manifest.Apps.Where(a => _selected.TryGetValue(a.Id, out var s) && s).Sum(a => a.InstallSize + a.DownloadSize) / (1024.0 * 1024);

        static double AvailableGb(string path)
        {
            try { var root = Path.GetPathRoot(path); return new DriveInfo(root).AvailableFreeSpace / (1024.0 * 1024 * 1024); } catch { return 0; }
        }

        // ---------------- feature tree ----------------
        static ImageSource StateIcon(bool on)
        {
            var dg = new DrawingGroup();
            if (on)
            {
                // a little hard drive: "Run from My Computer"
                dg.Children.Add(new GeometryDrawing(new LinearGradientBrush(Color.FromRgb(0xF4, 0xF4, 0xF4), Color.FromRgb(0xA8, 0xAE, 0xB6), 90), new Pen(new SolidColorBrush(Color.FromRgb(0x55, 0x5B, 0x63)), 0.8), new RectangleGeometry(new Rect(1, 4, 14, 8), 1.5, 1.5)));
                dg.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x3C, 0xC8, 0x3C)), null, new EllipseGeometry(new Point(12.5, 9.5), 1.2, 1.2)));
                dg.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(Color.FromRgb(0x80, 0x86, 0x8E)), 0.6), Geometry.Parse("M3,7 H9")));
            }
            else
            {
                dg.Children.Add(new GeometryDrawing(new LinearGradientBrush(Color.FromRgb(0xF4, 0xF4, 0xF4), Color.FromRgb(0xC8, 0xCC, 0xD2), 90), new Pen(new SolidColorBrush(Color.FromRgb(0x90, 0x96, 0x9E)), 0.8), new RectangleGeometry(new Rect(1, 4, 14, 8), 1.5, 1.5)));
                dg.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(Color.FromRgb(0xD8, 0x1E, 0x1E)), 2.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, Geometry.Parse("M4,3 L12,13 M12,3 L4,13")));
            }
            dg.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
            return new DrawingImage(dg);
        }

        UIElement InstallationOptions()
        {
            var dock = new DockPanel { Margin = new Thickness(8) };
            var top = new TextBlock { Text = "Customize how Valco Office programs run", FontSize = 14, Foreground = HeadingBrush, Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(top, Dock.Top); dock.Children.Add(top);
            var desc = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = TextBrush, MinHeight = 34 };
            var space = new TextBlock { Foreground = DimBrush, Margin = new Thickness(0, 4, 0, 0) };
            var bottom = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            bottom.Children.Add(desc); bottom.Children.Add(space);
            DockPanel.SetDock(bottom, Dock.Bottom); dock.Children.Add(bottom);
            var tree = new TreeView { BorderBrush = new SolidColorBrush(Color.FromRgb(0xAB, 0xAD, 0xB3)), Padding = new Thickness(2) };
            dock.Children.Add(tree);
            void UpdateSpace() => space.Text = $"Total space required on drive: {RequiredMb():N0} MB    Space available on drive: {AvailableGb(_engine.InstallRoot):N1} GB";

            TreeViewItem Node(string name, ImageSource icon, Func<bool> get, Action<bool> set, string description, bool isParent = false, Action refreshChildren = null)
            {
                var stateImg = new Image { Width = 16, Height = 16 };
                var arrow = new System.Windows.Shapes.Path { Data = Geometry.Parse("M0,0 L5,0 L2.5,3 Z"), Fill = Brushes.Black, Margin = new Thickness(1, 6, 0, 0) };
                var sb = new StackPanel { Orientation = Orientation.Horizontal }; sb.Children.Add(stateImg); sb.Children.Add(arrow);
                var stateBtn = new Button { Content = sb, Padding = new Thickness(1), Margin = new Thickness(0, 0, 4, 0), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, Focusable = false, Cursor = Cursors.Hand, ToolTip = "Click to choose how this runs" };
                var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
                head.Children.Add(stateBtn);
                head.Children.Add(new Image { Source = icon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 5, 0) });
                head.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
                var item = new TreeViewItem { Header = head, IsExpanded = true };
                void Refresh() { stateImg.Source = StateIcon(get()); }
                Refresh();
                item.Tag = (Action)Refresh;
                stateBtn.Click += (s, e) =>
                {
                    var m = new ContextMenu();
                    void Opt(string text, bool val)
                    {
                        var mi = new MenuItem { Header = text, Icon = new Image { Source = StateIcon(val), Width = 16, Height = 16 } };
                        mi.Click += (o, a) => { set(val); Refresh(); refreshChildren?.Invoke(); RefreshAll(); UpdateSpace(); };
                        m.Items.Add(mi);
                    }
                    Opt(isParent ? "Run all from My Computer" : "Run from My Computer", true);
                    Opt("Not Available", false);
                    m.PlacementTarget = stateBtn; m.Placement = PlacementMode.Bottom; m.IsOpen = true;
                };
                item.Selected += (s, e) => { if (e.OriginalSource == item) { desc.Text = description; e.Handled = true; } };
                return item;
            }
            var all = new List<TreeViewItem>();
            void RefreshAll() { foreach (var i in all) (i.Tag as Action)?.Invoke(); }

            var rootDesc = "Valco Office: a Windows Vista-style office suite: word processing, e-mail, code and web editing, and a classic taskbar.";
            TreeViewItem root = null;
            root = Node("Valco Office", Mark, () => _selected.Values.Any(v => v), v => { foreach (var k in _selected.Keys.ToList()) _selected[k] = v; }, rootDesc, true);
            all.Add(root);
            foreach (var a in _engine.Manifest.Apps)
            {
                var app = a;
                var n = Node(app.Name, AppIcon(_engine.SourceDir, app, 16), () => _selected.TryGetValue(app.Id, out var s) && s, v => _selected[app.Id] = v,
                    $"{app.Name}: {app.Description}" + (app.Notes != null ? "\n" + app.Notes : "") + $"  (version {app.Version}, {app.InstallSize / (1024.0 * 1024):N0} MB)");
                all.Add(n);
                root.Items.Add(n);
            }
            var shared = Node("Valco Office Shared Features", Mark, () => _engine.DesktopShortcuts || _engine.Associations || _engine.AutoUpdate,
                v => { _engine.DesktopShortcuts = _engine.Associations = _engine.AutoUpdate = v; }, "Shared features used by all Valco Office programs.", true);
            all.Add(shared);
            foreach (var (nm, desc2, get, set) in new (string, string, Func<bool>, Action<bool>)[]
            {
                ("Desktop Shortcuts", "Puts a shortcut for each program on the desktop.", () => _engine.DesktopShortcuts, v => _engine.DesktopShortcuts = v),
                ("File Type Associations", "Adds Valco programs to Open With for documents, e-mail messages, web pages and code files.", () => _engine.Associations, v => _engine.Associations = v),
                ("Valco Office Update", "Checks for updates when you sign in to Windows and lets you know when they're available.", () => _engine.AutoUpdate, v => _engine.AutoUpdate = v),
            })
            {
                var n = Node(nm, UI.Img("IconOptions") ?? Mark, get, set, desc2);
                all.Add(n); shared.Items.Add(n);
            }
            root.Items.Add(shared);
            tree.Items.Add(root);
            root.IsSelected = true;
            desc.Text = rootDesc;
            UpdateSpace();
            return dock;
        }

        // =====================================================================================
        //  4. Progress
        // =====================================================================================
        void Install()
        {
            var wanted = _selected.Where(kv => kv.Value).Select(kv => kv.Key).ToList();
            if (wanted.Count == 0 && !_maintenance) { VistaDialog.Show(this, "Choose at least one program to install.", "Valco Office Setup", MessageBoxButton.OK, DlgIcon.Info); return; }
            if (wanted.Count == 0 && _maintenance) { RemoveAll(); return; }
            if (!_maintenance && AvailableGb(_engine.InstallRoot) * 1024 < RequiredMb()) { VistaDialog.Show(this, "There isn't enough free space on the drive. Free up some space or choose another location on the File Location tab.", "Valco Office Setup", MessageBoxButton.OK, DlgIcon.Warning); return; }
            Run(_maintenance ? "Configuration Progress" : "Installation Progress", e => e.Apply(wanted), ok => FinishPage(ok, _maintenance ? "changed" : "installed"));
        }

        void Run(string title, Action<Engine> work, Action<string> done)
        {
            var sp = new StackPanel();
            sp.Children.Add(Heading(title));
            var status = Para("Preparing...", 20);
            var bar = new ProgressBar { Height = 18, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 4, 0, 0) };
            sp.Children.Add(status); sp.Children.Add(bar);
            var note = Para("", 18); note.Foreground = DimBrush;
            sp.Children.Add(note);
            SetPage(sp);
            _busy = true;
            _engine.Progress = (p, s) => Dispatcher.Invoke(() => { bar.Value = p; status.Text = s; });
            _engine.AskCloseApps = names => Dispatcher.Invoke(() => VistaDialog.Show(this,
                "Close these programs to continue:\n\n" + string.Join("\n", names.Select(n => "   •  " + n)) + "\n\nClick Yes after closing them, or No to have Setup close them for you (unsaved changes will be lost).",
                "Valco Office Setup", MessageBoxButton.YesNo, DlgIcon.Warning, "Programs need to close") == MessageBoxResult.Yes);
            Task.Run(() =>
            {
                string error = null;
                try { work(_engine); }
                catch (Exception ex) { error = ex.Message; }
                Dispatcher.Invoke(() => { _busy = false; done(error); });
            });
        }

        // =====================================================================================
        //  5. Finish
        // =====================================================================================
        void FinishPage(string error, string verb)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var big = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/ValcoOffice-256.png")), Width = 120, Height = 120, Margin = new Thickness(0, 6, 24, 0), VerticalAlignment = VerticalAlignment.Top };
            g.Children.Add(big);
            var sp = new StackPanel();
            if (error == null)
            {
                sp.Children.Add(Heading($"Valco Office has been {verb} successfully"));
                if (verb == "removed")
                    sp.Children.Add(Para("All Valco Office programs have been removed from this computer. Some files are removed the next time you restart."));
                else
                {
                    sp.Children.Add(Para("You can find the Valco Office programs in the Start menu, in the Valco Office folder."));
                    sp.Children.Add(Para("Valco Office Update can check for the latest updates, fixes and new programs. Click Check for Updates to check now.", 8));
                    var launch = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
                    var installed = Engine.InstalledApps();
                    foreach (var a in _engine.Manifest.Apps.Where(a => installed.ContainsKey(a.Id)))
                    {
                        var app = a;
                        var exe = Path.Combine(_engine.InstallRoot, app.Folder ?? app.Name, app.Exe ?? "");
                        if (!File.Exists(exe)) continue;
                        var link = new Hyperlink(new Run("Open " + app.Name));
                        link.Click += (s, e) => { try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true }); } catch { } };
                        launch.Children.Add(new TextBlock(link) { Margin = new Thickness(0, 0, 0, 3) });
                    }
                    sp.Children.Add(launch);
                    if (_engine.Manifest.Apps.Any(a => installed.ContainsKey(a.Id) && !string.IsNullOrEmpty(a.DefaultApps)))
                    {
                        sp.Children.Add(Para("To open documents, e-mail links and web pages with Valco Office, make its programs your defaults. Windows asks you to confirm each one.", 10));
                        var def = new Button { Content = "Set _Default Programs...", HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 160, Height = 25, Padding = new Thickness(10, 0, 10, 0) };
                        def.Click += (s, e) => new DefaultAppsWindow(this).ShowDialog();
                        sp.Children.Add(def);
                    }
                }
            }
            else
            {
                sp.Children.Add(Heading("Valco Office Setup didn't finish"));
                sp.Children.Add(Para("Setup ran into a problem:"));
                sp.Children.Add(new TextBox { Text = error, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = Brushes.DarkRed, Margin = new Thickness(0, 4, 0, 8) });
                sp.Children.Add(Para("Run Setup again to retry. Programs that installed successfully are ready to use."));
            }
            Grid.SetColumn(sp, 1); g.Children.Add(sp);
            SetPage(g);
            if (error == null && verb != "removed")
                FooterButton("Check for _Updates", () =>
                {
                    var exe = Path.Combine(_engine.SetupDir, "setup.exe");
                    try { Process.Start(new ProcessStartInfo(File.Exists(exe) ? exe : Environment.ProcessPath, "/update") { UseShellExecute = true }); } catch { }
                    Close();
                });
            FooterButton("C_lose", Close, true);
        }

        // =====================================================================================
        //  Maintenance: Add or Remove Features / Repair / Remove
        // =====================================================================================
        void MaintenancePage(bool removeOnly)
        {
            if (removeOnly) { RemoveAll(); return; }
            var sp = new StackPanel();
            sp.Children.Add(Heading("Change your installation of Valco Office"));
            RadioButton Opt(string title, string text, bool check)
            {
                var r = new RadioButton { IsChecked = check, Margin = new Thickness(0, 12, 0, 0), Content = new StackPanel { Children = { new AccessText { Text = title, FontWeight = FontWeights.SemiBold, Foreground = HeadingBrush }, new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = TextBrush, MaxWidth = 520, Margin = new Thickness(0, 2, 0, 0) } } } };
                sp.Children.Add(r);
                return r;
            }
            var add = Opt("_Add or Remove Features", "Choose which Valco Office programs are installed on this computer.", true);
            var repair = Opt("_Repair", "Repair errors in your Valco Office installation by reinstalling every program from the local installation source.", false);
            var remove = Opt("R_emove", "Remove Valco Office and all of its programs from this computer.", false);
            var ver = new TextBlock { Text = $"Installed version: {Engine.GetOption("Version", "?")}", Foreground = DimBrush, Margin = new Thickness(0, 20, 0, 0) };
            sp.Children.Add(ver);
            SetPage(sp);
            FooterButton("_Continue", () =>
            {
                if (add.IsChecked == true) CustomizePage(true);
                else if (repair.IsChecked == true)
                {
                    var wanted = Engine.InstalledApps().Keys.ToList();
                    Run("Repair Progress", e => e.Apply(wanted, repairAll: true), ok => FinishPage(ok, "repaired"));
                }
                else RemoveAll();
            }, true);
        }

        void RemoveAll()
        {
            if (VistaDialog.Show(this, "Are you sure you want to remove Valco Office from your computer?", "Valco Office Setup", MessageBoxButton.YesNo, DlgIcon.Question) != MessageBoxResult.Yes)
            {
                if (_body.Child == null) Close();
                return;
            }
            Run("Uninstall Progress", e => e.UninstallAll(), ok => FinishPage(ok, "removed"));
        }
    }

    /// <summary>"Preparing necessary files..." splash, like the one Office 2007 shows while setup unpacks.</summary>
    public class Splash : Window
    {
        public Splash()
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
            Width = 420; Height = 210; WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = true; Title = "Valco Office Setup";
            var g = new Grid();
            var card = new Border
            {
                Margin = new Thickness(10), CornerRadius = new CornerRadius(4), BorderBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0xA4, 0xC8)), BorderThickness = new Thickness(1),
                Background = new LinearGradientBrush(Colors.White, Color.FromRgb(0xE8, 0xF0, 0xFA), 90), Effect = new DropShadowEffect { ShadowDepth = 3, BlurRadius = 10, Opacity = 0.4 }
            };
            var sp = new Grid { Margin = new Thickness(22) };
            var mark = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/ValcoOffice-mark.png")), Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(80, 8, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            title.Children.Add(new TextBlock { Text = "Valco", FontSize = 28, FontWeight = FontWeights.SemiBold, Foreground = SetupWindow.HeadingBrush });
            title.Children.Add(new TextBlock { Text = " Office", FontSize = 28, FontFamily = new FontFamily("Segoe UI Light"), Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0x6F, 0xA8)) });
            var bottom = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            bottom.Children.Add(new TextBlock { Text = "Preparing necessary files...", Foreground = SetupWindow.TextBrush, Margin = new Thickness(0, 0, 0, 6) });
            bottom.Children.Add(new ProgressBar { Height = 12, IsIndeterminate = true });
            sp.Children.Add(mark); sp.Children.Add(title); sp.Children.Add(bottom);
            card.Child = sp;
            g.Children.Add(card);
            Content = g;
            MouseLeftButtonDown += (s, e) => { try { DragMove(); } catch { } };
        }
    }
}
