using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ValcoCode.Services;

namespace ValcoCode.Views;

/// <summary>Welcome, Settings and Keyboard Shortcuts pages shown as editor tabs.</summary>
public static class Pages
{
    public static FrameworkElement Keys(string combo)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var chord in combo.Split(' '))
        {
            foreach (var k in chord.Split('+'))
            {
                var b = new Border { BorderThickness = new Thickness(1, 1, 1, 2), CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 0, 5, 1), Margin = new Thickness(0, 0, 3, 0) };
                b.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
                b.SetResourceReference(Border.BackgroundProperty, "Panel2Brush");
                var t = new TextBlock { Text = k, FontSize = 11.5 };
                t.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
                b.Child = t;
                sp.Children.Add(b);
            }
            sp.Children.Add(new Border { Width = 6 });
        }
        return sp;
    }

    static TextBlock T(string text, double size = 13, bool muted = false, FontWeight? weight = null, Thickness? margin = null)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, FontWeight = weight ?? FontWeights.Normal, Margin = margin ?? new Thickness(0) };
        t.SetResourceReference(TextBlock.ForegroundProperty, muted ? "MutedBrush" : "FgBrush");
        return t;
    }

    static Button Link(string glyph, string text, Action click)
    {
        var b = new Button { Style = (Style)Application.Current.Resources["LinkButton"], Margin = new Thickness(0, 3, 0, 3), HorizontalAlignment = HorizontalAlignment.Left, Content = $"{glyph}   {text}" };
        b.Click += (_, _) => click();
        return b;
    }

    public static FrameworkElement Welcome(Window owner, List<string> recent, Action<string> newFile, Action openFile, Action<string?> openFolder, Action clone, Action theme)
    {
        var root = new StackPanel { MaxWidth = 980, Margin = new Thickness(60, 50, 60, 40) };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 34) };
        head.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png")), Width = 64, Height = 64, Margin = new Thickness(0, 0, 18, 0) });
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(T("Valco Code", 34, weight: FontWeights.Light));
        titles.Children.Add(T("Code, the Vista way  ·  © 2026 ItsRaiden", 14, true));
        var suite = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        suite.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/ValcoOffice-mark.png")), Width = 18, Height = 18, Margin = new Thickness(0, 0, 6, 0) });
        suite.Children.Add(T("Part of Valco Office", 12.5, true));
        titles.Children.Add(suite);
        head.Children.Add(titles);
        root.Children.Add(head);

        var cols = new Grid();
        cols.ColumnDefinitions.Add(new ColumnDefinition());
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        cols.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel();
        left.Children.Add(T("Start", 18, margin: new Thickness(0, 0, 0, 8)));
        left.Children.Add(Link("🐍", "New Python File…", () => newFile("python")));
        left.Children.Add(Link("☕", "New Java File…", () => newFile("java")));
        left.Children.Add(Link("🗎", "Open File…", openFile));
        left.Children.Add(Link("🗁", "Open Folder…", () => openFolder(null)));
        left.Children.Add(Link("⑂", "Clone Git Repository…", clone));
        left.Children.Add(T("Recent", 18, margin: new Thickness(0, 26, 0, 8)));
        if (recent.Count == 0) left.Children.Add(T("Folders you open show up here.", 12.5, true));
        foreach (var r in recent.Take(7))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            var b = new Button { Style = (Style)Application.Current.Resources["LinkButton"], Content = Path.GetFileName(r.TrimEnd('\\')) };
            var path = r;
            b.Click += (_, _) => openFolder(path);
            row.Children.Add(b);
            row.Children.Add(T("   " + Path.GetDirectoryName(r.TrimEnd('\\')), 12, true));
            left.Children.Add(row);
        }
        cols.Children.Add(left);

        var right = new StackPanel();
        Grid.SetColumn(right, 2);
        right.Children.Add(T("Walkthroughs", 18, margin: new Thickness(0, 0, 0, 8)));
        Card(right, "Format with a keybind", "Shift+Alt+F formats Python (Ruff, Black style) and Java (google-java-format). Ctrl+Shift+I works too. Turn on Format on Save in the File menu.", "Shift+Alt+F");
        Card(right, "Run your code", "F5 runs the open .py or .java file. Its output shows in the terminal panel; type into the line at the bottom to answer input() or Scanner.", "F5");
        Card(right, "Commit and push", "Ctrl+Shift+G opens Source Control: stage, commit (Ctrl+Enter), push, pull, and publish a folder to GitHub.", "Ctrl+Shift+G");
        var themeCard = Card(right, "Pick a theme", $"Now: {ThemeManager.Current.Name}. Choose WeaveCraft Dark, Windows Vista, Dreamweaver CS6 or 2077.", "Ctrl+K Ctrl+T");
        themeCard.Cursor = System.Windows.Input.Cursors.Hand;
        themeCard.MouseLeftButtonUp += (_, _) => theme();
        cols.Children.Add(right);
        root.Children.Add(cols);
        var sv = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        sv.SetResourceReference(Control.BackgroundProperty, "EditorBgBrush");
        return sv;
    }

    static Border Card(Panel parent, string title, string body, string keys)
    {
        var b = new Border { Padding = new Thickness(14, 10, 14, 12), Margin = new Thickness(0, 0, 0, 8), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
        b.SetResourceReference(Border.BackgroundProperty, "Panel2Brush");
        b.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        var sp = new StackPanel();
        var top = new DockPanel();
        var k = Keys(keys);
        DockPanel.SetDock(k, Dock.Right);
        top.Children.Add(k);
        top.Children.Add(T(title, 13.5, weight: FontWeights.SemiBold));
        sp.Children.Add(top);
        sp.Children.Add(T(body, 12.5, true, margin: new Thickness(0, 4, 0, 0)));
        b.Child = sp;
        parent.Children.Add(b);
        return b;
    }

    public static FrameworkElement SettingsPage(AppSettings s, Action<string> setTheme, Action apply)
    {
        var root = new StackPanel { MaxWidth = 820, Margin = new Thickness(40, 30, 40, 40), HorizontalAlignment = HorizontalAlignment.Left };
        root.Children.Add(T("Settings", 26, weight: FontWeights.Light, margin: new Thickness(0, 0, 0, 16)));

        void Section(string name) => root.Children.Add(T(name, 16, weight: FontWeights.SemiBold, margin: new Thickness(0, 18, 0, 6)));
        void Row(string title, string desc, UIElement control)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
            sp.Children.Add(T(title, 13, weight: FontWeights.SemiBold));
            sp.Children.Add(T(desc, 12.5, true, margin: new Thickness(0, 2, 0, 6)));
            sp.Children.Add(control);
            root.Children.Add(sp);
        }
        TextBox Num(double v, Action<double> set, double min, double max)
        {
            var tb = new TextBox { Text = v.ToString(), Width = 80, HorizontalAlignment = HorizontalAlignment.Left };
            tb.LostFocus += (_, _) => { if (double.TryParse(tb.Text, out var x)) { set(Math.Clamp(x, min, max)); apply(); } tb.Text = Math.Clamp(double.TryParse(tb.Text, out var y) ? y : v, min, max).ToString(); };
            tb.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) System.Windows.Input.Keyboard.ClearFocus(); };
            return tb;
        }
        CheckBox Check(string label, bool v, Action<bool> set)
        {
            var c = new CheckBox { Content = label, IsChecked = v };
            c.Click += (_, _) => { set(c.IsChecked == true); apply(); };
            return c;
        }

        Section("Appearance");
        var cards = new WrapPanel();
        foreach (var t in ThemeManager.All)
        {
            var b = new Border { Width = 170, Margin = new Thickness(0, 0, 10, 10), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(2), Cursor = System.Windows.Input.Cursors.Hand, Padding = new Thickness(0, 0, 0, 8) };
            b.BorderBrush = t.Id == ThemeManager.Current.Id ? (Brush)Application.Current.Resources["TabAccentBrush"] : (Brush)Application.Current.Resources["LineBrush"];
            b.SetResourceReference(Border.BackgroundProperty, "Panel2Brush");
            var sp = new StackPanel();
            var swatch = new Border { Height = 70, CornerRadius = new CornerRadius(4, 4, 0, 0), Background = ThemeManager.MakeBrush(t.Brushes["EditorBg"]) };
            var lines = new StackPanel { Margin = new Thickness(10, 10, 10, 0) };
            foreach (var (c, w) in new[] { (t.Syntax.Keyword, 50), (t.Syntax.Function, 90), (t.Syntax.String, 70), (t.Syntax.Comment, 110) })
                lines.Children.Add(new Border { Height = 6, Width = w, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(ThemeManager.C(c)), Margin = new Thickness(0, 0, 0, 6) });
            swatch.Child = lines;
            var bar = new Border { Height = 8, Background = ThemeManager.MakeBrush(t.Brushes["MenuBar"]) };
            sp.Children.Add(bar);
            sp.Children.Add(swatch);
            sp.Children.Add(T(t.Name, 12.5, margin: new Thickness(10, 8, 0, 0)));
            b.Child = sp;
            var id = t.Id;
            b.MouseLeftButtonUp += (_, _) => setTheme(id);
            cards.Children.Add(b);
        }
        Row("Color Theme", "The look of the whole app: menus, side bar, editor and terminal.", cards);
        Row("Editor: Font Size", "Font size in the editor. Ctrl+= and Ctrl+- also change it.", Num(s.FontSize, v => s.FontSize = v, 8, 36));
        var font = new TextBox { Text = s.FontFamily, Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
        font.LostFocus += (_, _) => { s.FontFamily = font.Text.Trim().Length > 0 ? font.Text.Trim() : "Cascadia Mono, Consolas"; apply(); };
        Row("Editor: Font Family", "Fonts to try, in order.", font);
        Row("Terminal: Font Size", "Font size in the terminal panel.", Num(s.TerminalFontSize, v => s.TerminalFontSize = v, 8, 32));
        Row("Editor: Word Wrap", "Wrap long lines to the window width (Alt+Z).", Check("Wrap lines", s.WordWrap, v => s.WordWrap = v));
        Row("Editor: Render Whitespace", "Show dots for spaces and arrows for tabs.", Check("Show whitespace", s.ShowWhitespace, v => s.ShowWhitespace = v));

        Section("Formatting");
        Row("Editor: Tab Size", "Spaces per indent. Python and Java usually use 4 (Java with 2 uses Google style).", Num(s.TabSize, v => s.TabSize = (int)v, 1, 8));
        Row("Formatter: Line Length", "Python lines longer than this get wrapped by the formatter (Black uses 88). Also shown as a ruler.", Num(s.LineLength, v => s.LineLength = (int)v, 40, 200));
        Row("Editor: Format on Save", "Format Python and Java files every time you save.", Check("Format on save", s.FormatOnSave, v => s.FormatOnSave = v));

        Section("Files");
        Row("Files: Auto Save", "Save a second after you stop typing.", Check("Auto save", s.AutoSave, v => s.AutoSave = v));

        var sv = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        sv.SetResourceReference(Control.BackgroundProperty, "EditorBgBrush");
        return sv;
    }

    public static FrameworkElement Shortcuts(List<(string Title, string Key)> cmds)
    {
        var dock = new DockPanel { Margin = new Thickness(30, 24, 30, 10) };
        var head = T("Keyboard Shortcuts", 24, weight: FontWeights.Light, margin: new Thickness(0, 0, 0, 10));
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);
        var filter = new TextBox { Tag = "Type to search in keybindings", Margin = new Thickness(0, 0, 0, 10), MaxWidth = 500, HorizontalAlignment = HorizontalAlignment.Left, Width = 500 };
        DockPanel.SetDock(filter, Dock.Top);
        dock.Children.Add(filter);
        var extra = new List<(string, string)>
        {
            ("Select Word / Next Occurrence", "Ctrl+D"), ("Delete Line", "Ctrl+Shift+K"), ("Insert Line Below", "Ctrl+Enter"),
            ("Indent / Outdent", "Tab Shift+Tab"), ("Commit (in Source Control)", "Ctrl+Enter"), ("Format Document (alias)", "Ctrl+Shift+I"),
            ("Next / Previous Editor", "Ctrl+Tab Ctrl+Shift+Tab"), ("Find Next / Previous", "F3 Shift+F3"),
        };
        var all = cmds.Concat(extra).ToList();
        var list = new StackPanel();
        void Render()
        {
            list.Children.Clear();
            foreach (var (title, key) in all.Where(c => (c.Item1 + " " + c.Item2).Contains(filter.Text, StringComparison.OrdinalIgnoreCase)))
            {
                var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.Children.Add(T(title, 13));
                var k = Keys(key);
                Grid.SetColumn(k, 1);
                row.Children.Add(k);
                list.Children.Add(row);
            }
        }
        filter.TextChanged += (_, _) => Render();
        Render();
        dock.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var outer = new Border { Child = dock };
        outer.SetResourceReference(Border.BackgroundProperty, "EditorBgBrush");
        return outer;
    }
}
