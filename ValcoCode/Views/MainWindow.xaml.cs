using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ValcoCode.Editor;
using ValcoCode.Services;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace ValcoCode.Views;

public partial class MainWindow : VistaWindow, IWorkbench
{
    public AppSettings Settings { get; } = AppSettings.Load();
    public Tools Tools { get; private set; } = new();
    public string? Root { get; private set; }
    public GitStatus? GitState { get; private set; }
    public GitHubUser? User { get; private set; }

    readonly List<DocTab> tabs = new();
    DocTab? active;
    readonly Dictionary<string, List<Problem>> problems = new(StringComparer.OrdinalIgnoreCase);
    readonly TerminalView terminal = new();
    ExplorerView explorer = null!;
    SearchView search = null!;
    ScmView scm = null!;
    RunView runView = null!;
    string view = "explorer";
    bool sidebarOpen = true, panelOpen;
    string panelTab = "terminal";
    readonly StringBuilder output = new();
    readonly DispatcherTimer lintTimer, gitTimer, autoSaveTimer;
    string? chord;
    DateTime chordAt;
    int untitled = 1;
    record Cmd(string Id, string Title, string? Key, Action Run);
    List<Cmd> commands = new();

    public MainWindow()
    {
        InitializeComponent();
        ThemeManager.Apply(ThemeManager.ById(Settings.Theme));
        Languages.RecolorAll(ThemeManager.Current);
        ReloadTools();
        explorer = new ExplorerView(this);
        search = new SearchView(this);
        scm = new ScmView(this);
        runView = new RunView(this);
        TerminalHost.Content = terminal;
        ProblemsList.ItemTemplate = (DataTemplate)FindResource("ProblemTemplate");
        PaletteList.ItemTemplate = (DataTemplate)FindResource("PickTemplate");

        lintTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        lintTimer.Tick += async (_, _) => { lintTimer.Stop(); await LintActive(); };
        gitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        gitTimer.Tick += async (_, _) => { gitTimer.Stop(); await RefreshGit(); };
        autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        autoSaveTimer.Tick += async (_, _) => { autoSaveTimer.Stop(); foreach (var t in tabs.Where(t => t.Dirty && t.Path != null).ToList()) await Save(t, noFormat: true); };
        var poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        poll.Tick += async (_, _) => { if (IsActive) await RefreshGit(); };
        poll.Start();

        BuildCommands();
        BuildMenu();
        BuildRibbon();
        ThemeManager.Changed += OnThemeChanged;
        PreviewKeyDown += OnKey;
        Activated += async (_, _) => await CheckDiskChanges();
        Closing += OnClosing;
        SourceInitialized += (_, _) => StyleTitleBar(this);
        Loaded += async (_, _) => await Startup();

        if (!double.IsNaN(Settings.WinLeft)) { Left = Settings.WinLeft; Top = Settings.WinTop; WindowStartupLocation = WindowStartupLocation.Manual; }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Width = Settings.WinWidth;
        Height = Settings.WinHeight;
        if (Settings.WinMax) WindowState = WindowState.Maximized;
        SideCol.Width = new GridLength(Settings.SidebarWidth);
        PanelRow.Height = new GridLength(Settings.PanelHeight);
        ShowView("explorer");
        SetPanel(false);
        UpdateStatus();
    }

    public void ReloadTools()
    {
        Tools = Tools.Find();
        Git.Exe = Tools.Git;
        runView?.Render(active?.Title);
        scm?.Render();
    }

    async Task Startup()
    {
        var tok = TokenStore.Get();
        if (tok != null)
        {
            try { User = await GitHub.User(tok); UpdateAccount(); }
            catch (Exception e) { Notify("GitHub sign-in problem: " + e.Message, true, ("Sign in again", ShowSignIn)); }
        }
        var args = Environment.GetCommandLineArgs().Skip(1).ToList();
        if (args.Count > 0 && Directory.Exists(args[0])) SetRoot(args[0]);
        else if (Settings.SessionRoot != null && Directory.Exists(Settings.SessionRoot)) SetRoot(Settings.SessionRoot);
        if (args.Count > 0 && File.Exists(args[0])) await OpenFile(args[0]);
        else
        {
            foreach (var f in Settings.SessionFiles.Where(File.Exists).ToList()) await OpenFileCore(f, 0, 1, 0, true);
            if (Settings.SessionActive != null && File.Exists(Settings.SessionActive)) await OpenFileCore(Settings.SessionActive, 0, 1, 0, true);
        }
        if (tabs.Count == 0) OpenSpecial(TabKind.Welcome);
    }

    // ====================================================================== theme

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Colours the native title bar to match the theme (dark mode on Windows 10+, exact colours on Windows 11).</summary>
    public static void StyleTitleBar(Window w)
    {
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            var t = ThemeManager.Current;
            int dark = t.Dark || t.Id == "vista" ? 1 : 0;
            DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
            if (t.Caption != null)
            {
                var c = ThemeManager.C(t.Caption);
                int cap = c.R | (c.G << 8) | (c.B << 16);
                DwmSetWindowAttribute(hwnd, 35, ref cap, 4);
                DwmSetWindowAttribute(hwnd, 34, ref cap, 4);
            }
            if (t.CaptionText != null)
            {
                var c = ThemeManager.C(t.CaptionText);
                int txt = c.R | (c.G << 8) | (c.B << 16);
                DwmSetWindowAttribute(hwnd, 36, ref txt, 4);
            }
        }
        catch { }
    }

    void SetTheme(string id, bool save = true)
    {
        ThemeManager.Apply(ThemeManager.ById(id));
        if (save) { Settings.Theme = id; Settings.Save(); BuildMenu(); }
    }

    void OnThemeChanged()
    {
        Languages.RecolorAll(ThemeManager.Current);
        foreach (var t in tabs)
        {
            t.Code?.ApplyTheme();
            if (t.Kind == TabKind.Welcome) t.View = BuildWelcome();
            if (t.Kind == TabKind.Settings) t.View = BuildSettings();
            if (t.View is DiffViewer d) d.ApplyTheme();
        }
        terminal.ApplyTheme(Settings.TerminalFontSize);
        StyleTitleBar(this);
        explorer.SetDecorations(Decorations());
        scm.Render();
        RenderTabs();
        if (active != null) EditorHost.Content = active.Content;
        UpdateStatus();
        BuildMenu();
    }

    // ====================================================================== tabs

    void RenderTabs()
    {
        TabStrip.Children.Clear();
        foreach (var t in tabs) TabStrip.Children.Add(TabHeader(t));
    }

    FrameworkElement TabHeader(DocTab t)
    {
        var isActive = t == active;
        var b = new Border
        {
            BorderThickness = new Thickness(0, isActive ? 2 : 0, 1, 0),
            Padding = new Thickness(10, isActive ? 0 : 2, 4, 0),
            Cursor = Cursors.Hand,
            ToolTip = t.Path ?? t.Title,
            CornerRadius = ThemeManager.Current.Id == "vista" ? new CornerRadius(3, 3, 0, 0) : new CornerRadius(0),
            Margin = ThemeManager.Current.Id == "vista" ? new Thickness(1, 3, 0, 0) : new Thickness(0),
        };
        b.SetResourceReference(Border.BackgroundProperty, isActive ? "TabActiveBrush" : "TabInactiveBrush");
        b.BorderBrush = isActive ? (Brush)FindResource("TabAccentBrush") : (Brush)FindResource("LineBrush");
        if (isActive) b.BorderThickness = new Thickness(0, 2, 1, 0);
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var (icon, brush) = TabIcon(t);
        row.Children.Add(new TextBlock { Text = icon, Foreground = brush, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, FontFamily = icon.Length == 1 && icon[0] > '' ? new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets") : new FontFamily("Segoe UI") });
        var title = new TextBlock { Text = t.Title, VerticalAlignment = VerticalAlignment.Center, FontStyle = t.Kind == TabKind.Diff ? FontStyles.Italic : FontStyles.Normal };
        title.SetResourceReference(TextBlock.ForegroundProperty, isActive ? "TabActiveFgBrush" : "TabInactiveFgBrush");
        row.Children.Add(title);
        var close = new Button { Style = (Style)FindResource("IconButton"), FontSize = t.Dirty ? 9 : 10, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(3, 1, 3, 1), ToolTip = "Close (Ctrl+W)" };
        close.Content = t.Dirty ? "" : "";
        close.Opacity = t.Dirty || isActive ? 1 : 0;
        b.MouseEnter += (_, _) => close.Opacity = 1;
        b.MouseLeave += (_, _) => close.Opacity = t.Dirty || t == active ? 1 : 0;
        close.MouseEnter += (_, _) => close.Content = "";
        close.MouseLeave += (_, _) => close.Content = t.Dirty ? "" : "";
        close.Click += async (_, _) => await CloseTab(t);
        row.Children.Add(close);
        b.Child = row;
        b.MouseDown += async (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle) { e.Handled = true; await CloseTab(t); }
            else if (e.ChangedButton == MouseButton.Left) Activate(t);
        };
        b.ContextMenu = TabMenu(t);
        t.Header = b;
        if (isActive) Dispatcher.BeginInvoke(() => b.BringIntoView(), DispatcherPriority.Loaded);
        return b;
    }

    ContextMenu TabMenu(DocTab t)
    {
        var m = new ContextMenu();
        MenuItem I(string h, Action a, string k = "") { var i = new MenuItem { Header = h, InputGestureText = k }; i.Click += (_, _) => a(); return i; }
        m.Items.Add(I("Close", () => _ = CloseTab(t), "Ctrl+W"));
        m.Items.Add(I("Close Others", async () => { foreach (var x in tabs.Where(x => x != t).ToList()) await CloseTab(x); }));
        m.Items.Add(I("Close to the Right", async () => { foreach (var x in tabs.Skip(tabs.IndexOf(t) + 1).ToList()) await CloseTab(x); }));
        m.Items.Add(I("Close Saved", async () => { foreach (var x in tabs.Where(x => !x.Dirty).ToList()) await CloseTab(x); }));
        m.Items.Add(I("Close All", async () => { foreach (var x in tabs.ToList()) await CloseTab(x); }, "Ctrl+K W"));
        if (t.Path != null)
        {
            m.Items.Add(new Separator());
            m.Items.Add(I("Copy Path", () => Clipboard.SetText(t.Path)));
            m.Items.Add(I("Reveal in File Explorer", () => Shell.Reveal(t.Path)));
            m.Items.Add(I("Reveal in Explorer View", () => { ShowView("explorer"); _ = explorer.Reveal(t.Path); }));
        }
        return m;
    }

    static (string, Brush) TabIcon(DocTab t) => t.Kind switch
    {
        TabKind.Welcome => ("", FileIcons.B("#36c2b4")),
        TabKind.Settings or TabKind.Shortcuts => ("", FileIcons.B("#8a8a8a")),
        TabKind.Diff => ("", FileIcons.B("#e8b04a")),
        _ => t.Path != null ? FileIcons.For(t.Path) : t.Lang == "python" ? FileIcons.For("x.py") : t.Lang == "java" ? FileIcons.For("x.java") : ("≡", FileIcons.B("#8a8a8a")),
    };

    void Activate(DocTab? t)
    {
        active = t;
        EditorHost.Content = t?.Content ?? BuildWatermark();
        RenderTabs();
        Breadcrumbs.Text = t?.Path != null ? (Root != null && t.Path.StartsWith(Root, StringComparison.OrdinalIgnoreCase) ? Path.GetRelativePath(Root, t.Path) : t.Path).Replace("\\", "  ›  ") : t?.Kind == TabKind.File ? t.Title : "";
        Breadcrumbs.Visibility = t?.Kind is TabKind.File or TabKind.Diff ? Visibility.Visible : Visibility.Collapsed;
        if (t?.Code != null) Dispatcher.BeginInvoke(() => t.Code.Editor.TextArea.Focus(), DispatcherPriority.Input);
        UpdateTitle();
        UpdateStatus();
        runView.Render(t?.Kind == TabKind.File ? t.Title : null);
        if (t?.Code != null && t.Lang == "python") { lintTimer.Stop(); lintTimer.Start(); }
        SaveSession();
    }

    void UpdateTitle()
    {
        var parts = new List<string>();
        if (active != null) parts.Add((active.Dirty ? "● " : "") + active.Title);
        if (Root != null) parts.Add(Path.GetFileName(Root.TrimEnd('\\')));
        parts.Add("Valco Code");
        Title = string.Join(" — ", parts);
        CommandCenterText.Text = Root != null ? Path.GetFileName(Root.TrimEnd('\\')) : "Valco Code";
    }

    DocTab NewFileTab(string? path, string text, string? lang = null)
    {
        lang ??= Languages.Detect(path);
        var t = new DocTab { Kind = TabKind.File, Path = path, Title = path != null ? Path.GetFileName(path) : $"Untitled-{untitled++}", Lang = lang, SavedText = text };
        t.Code = new CodeEditor(text, lang, Settings);
        t.Code.Editor.TextChanged += (_, _) => OnEdited(t);
        t.Code.CaretMoved += () => { if (t == active) UpdateCursor(); };
        if (path != null) try { t.DiskTime = File.GetLastWriteTimeUtc(path); } catch { }
        if (problems.TryGetValue(path ?? "", out var ps)) t.Code.SetProblems(ps);
        return t;
    }

    void OnEdited(DocTab t)
    {
        var dirty = t.Code!.Editor.Document.TextLength != t.SavedText.Length || t.Code.Editor.Text != t.SavedText;
        if (dirty != t.Dirty)
        {
            t.Dirty = dirty;
            RenderTabs();
            UpdateTitle();
        }
        if (t.Lang == "python") { lintTimer.Stop(); lintTimer.Start(); }
        if (Settings.AutoSave && t.Path != null) { autoSaveTimer.Stop(); autoSaveTimer.Start(); }
    }

    public async Task OpenFile(string path, int line = 0, int col = 1, int len = 0) => await OpenFileCore(path, line, col, len, false);

    async Task OpenFileCore(string path, int line, int col, int len, bool quiet)
    {
        var t = tabs.FirstOrDefault(x => x.Kind == TabKind.File && string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
        if (t == null)
        {
            string text;
            try
            {
                var fi = new FileInfo(path);
                if (fi.Length > 30_000_000) { Notify($"{fi.Name} is too large to open (over 30 MB).", true); return; }
                var bytes = await File.ReadAllBytesAsync(path);
                if (bytes.Take(8000).Contains((byte)0))
                {
                    if (!quiet) Notify($"{fi.Name} looks like a binary file, so it wasn't opened.", false, ("Open with Windows", () => Shell.Open(path)));
                    return;
                }
                text = DecodeText(bytes);
            }
            catch (Exception e)
            {
                if (!quiet) Notify($"Can't open {Path.GetFileName(path)}: {e.Message}", true);
                return;
            }
            t = NewFileTab(path, text);
            tabs.RemoveAll(x => x.Kind == TabKind.Welcome);
            tabs.Add(t);
        }
        Activate(t);
        if (line > 0) t.Code!.GoTo(line, col, len);
    }

    static string DecodeText(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch { return Encoding.Latin1.GetString(bytes); }
    }

    void NewUntitled(string lang = "plaintext")
    {
        var t = NewFileTab(null, "", lang);
        tabs.RemoveAll(x => x.Kind == TabKind.Welcome);
        tabs.Add(t);
        Activate(t);
    }

    void OpenSpecial(TabKind kind)
    {
        var t = tabs.FirstOrDefault(x => x.Kind == kind);
        if (t == null)
        {
            t = new DocTab { Kind = kind, Title = kind switch { TabKind.Welcome => "Welcome", TabKind.Settings => "Settings", _ => "Keyboard Shortcuts" } };
            t.View = kind switch { TabKind.Welcome => BuildWelcome(), TabKind.Settings => BuildSettings(), _ => BuildShortcuts() };
            tabs.Add(t);
        }
        Activate(t);
    }

    async Task CloseTab(DocTab t, bool force = false)
    {
        if (t.Dirty && !force)
        {
            var r = MessageBox.Show(this, $"Do you want to save the changes you made to {t.Title}?\n\nYour changes will be lost if you don't save them.", "Valco Code", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (r == MessageBoxResult.Cancel) return;
            if (r == MessageBoxResult.Yes && !await Save(t)) return;
        }
        var i = tabs.IndexOf(t);
        tabs.Remove(t);
        if (active == t) Activate(tabs.Count > 0 ? tabs[Math.Min(i, tabs.Count - 1)] : null);
        else RenderTabs();
        SaveSession();
    }

    async Task<bool> Save(DocTab t, bool saveAs = false, bool noFormat = false)
    {
        if (t.Kind != TabKind.File || t.Code == null) return false;
        var path = t.Path;
        if (path == null || saveAs)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = path != null ? Path.GetFileName(path) : t.Lang == "python" ? "main.py" : t.Lang == "java" ? "Main.java" : t.Title + ".txt",
                InitialDirectory = path != null ? Path.GetDirectoryName(path) : Root ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Filter = "All files (*.*)|*.*|Python (*.py)|*.py|Java (*.java)|*.java|Text (*.txt)|*.txt",
            };
            if (dlg.ShowDialog(this) != true) return false;
            path = dlg.FileName;
        }
        if (Settings.FormatOnSave && !noFormat && Formatter.CanFormat(t.Lang)) await FormatTab(t, silent: true);
        var text = t.Code.Editor.Text;
        try
        {
            var tmp = path + ".valcocode-tmp";
            await File.WriteAllTextAsync(tmp, text, new UTF8Encoding(false));
            File.Move(tmp, path, true);
            t.DiskTime = File.GetLastWriteTimeUtc(path);
        }
        catch (Exception e)
        {
            Notify($"Couldn't save {Path.GetFileName(path)}: {e.Message}", true);
            return false;
        }
        t.SavedText = text;
        t.Dirty = false;
        if (t.Path != path)
        {
            t.Path = path;
            t.Title = Path.GetFileName(path);
            var lang = Languages.Detect(path);
            if (lang != t.Lang) { t.Lang = lang; t.Code.SetLanguage(lang, Settings.TabSize); t.Code.ApplySettings(Settings); t.Code.ApplyTheme(); }
        }
        if (t == active) Activate(t); else RenderTabs();
        UpdateTitle();
        gitTimer.Stop(); gitTimer.Start();
        return true;
    }

    public async Task SaveAll()
    {
        foreach (var t in tabs.Where(t => t.Dirty && t.Path != null).ToList()) await Save(t, noFormat: true);
    }

    async Task CheckDiskChanges()
    {
        foreach (var t in tabs.Where(t => t.Kind == TabKind.File && t.Path != null).ToList())
        {
            try
            {
                if (!File.Exists(t.Path)) continue;
                var time = File.GetLastWriteTimeUtc(t.Path!);
                if (time == t.DiskTime) continue;
                t.DiskTime = time;
                var disk = DecodeText(await File.ReadAllBytesAsync(t.Path!));
                if (disk == t.SavedText) continue;
                if (t.Dirty)
                {
                    Notify($"{t.Title} was changed outside Valco Code, and you have unsaved edits.", false,
                        ("Load disk version", () => { t.Code!.ReplaceAll(disk); t.SavedText = disk; OnEdited(t); }));
                    continue;
                }
                t.Code!.ReplaceAll(disk);
                t.SavedText = disk;
                OnEdited(t);
            }
            catch { }
        }
        await RefreshGit();
    }

    void SaveSession()
    {
        Settings.SessionRoot = Root;
        Settings.SessionFiles = tabs.Where(t => t.Kind == TabKind.File && t.Path != null).Select(t => t.Path!).ToList();
        Settings.SessionActive = active?.Kind == TabKind.File ? active.Path : null;
        Settings.Save();
    }

    // ====================================================================== folders

    public void OpenFolder(string? path = null)
    {
        if (path == null)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Open Folder" };
            if (dlg.ShowDialog(this) != true) return;
            path = dlg.FolderName;
        }
        if (!Directory.Exists(path)) { Notify("That folder doesn't exist anymore.", true); Settings.Recent.Remove(path); Settings.Save(); BuildMenu(); return; }
        if (tabs.Any(t => t.Dirty) && !Confirm("Some files have unsaved changes. Open the folder anyway and close them?", "Unsaved changes")) return;
        tabs.Clear();
        problems.Clear();
        SetRoot(path);
        OpenSpecial(TabKind.Welcome);
    }

    void SetRoot(string? path)
    {
        Root = path;
        explorer.SetRoot(path);
        terminal.DefaultCwd = path ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path != null)
        {
            Settings.Recent.RemoveAll(r => r.Equals(path, StringComparison.OrdinalIgnoreCase));
            Settings.Recent.Insert(0, path);
            if (Settings.Recent.Count > 12) Settings.Recent.RemoveRange(12, Settings.Recent.Count - 12);
        }
        GitState = null;
        UpdateProblems();
        SaveSession();
        BuildMenu();
        UpdateTitle();
        _ = RefreshGit();
        ShowView("explorer");
    }

    public void CloseFolder()
    {
        if (tabs.Any(t => t.Dirty) && !Confirm("Close the folder and lose unsaved changes?", "Unsaved changes")) return;
        tabs.Clear();
        problems.Clear();
        SetRoot(null);
        OpenSpecial(TabKind.Welcome);
    }

    public void OnRenamed(string from, string to)
    {
        foreach (var t in tabs.Where(t => t.Path != null))
        {
            if (t.Path!.Equals(from, StringComparison.OrdinalIgnoreCase) || t.Path.StartsWith(from + "\\", StringComparison.OrdinalIgnoreCase))
            {
                t.Path = to + t.Path[from.Length..];
                t.Title = Path.GetFileName(t.Path);
            }
        }
        RenderTabs();
        UpdateTitle();
        gitTimer.Stop(); gitTimer.Start();
    }

    public void OnDeleted(string path)
    {
        foreach (var t in tabs.Where(t => t.Path != null && (t.Path.Equals(path, StringComparison.OrdinalIgnoreCase) || t.Path.StartsWith(path + "\\", StringComparison.OrdinalIgnoreCase))).ToList())
            _ = CloseTab(t, true);
        gitTimer.Stop(); gitTimer.Start();
    }

    // ====================================================================== side bar & panel

    void ShowView(string v)
    {
        view = v;
        SideContent.Content = v switch { "search" => search, "scm" => scm, "run" => runView, _ => explorer };
        ActExplorer.IsChecked = sidebarOpen && v == "explorer";
        ActSearch.IsChecked = sidebarOpen && v == "search";
        ActScm.IsChecked = sidebarOpen && v == "scm";
        ActRun.IsChecked = sidebarOpen && v == "run";
        if (!sidebarOpen) SetSidebar(true);
        if (v == "scm") scm.Render();
    }

    void SetSidebar(bool open)
    {
        sidebarOpen = open;
        SideBar.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        SideSplitter.Visibility = SideBar.Visibility;
        SideCol.Width = open ? new GridLength(Math.Max(170, Settings.SidebarWidth)) : new GridLength(0);
        SplitCol.Width = new GridLength(open ? 4 : 0);
        ActExplorer.IsChecked = open && view == "explorer";
        ActSearch.IsChecked = open && view == "search";
        ActScm.IsChecked = open && view == "scm";
        ActRun.IsChecked = open && view == "run";
    }

    void Activity_Click(object sender, RoutedEventArgs e)
    {
        var v = (string)((FrameworkElement)sender).Tag;
        if (sidebarOpen && view == v) SetSidebar(false);
        else
        {
            ShowView(v);
            if (v == "search") search.FocusQuery(null);
        }
    }

    void SetPanel(bool open, string? tab = null)
    {
        panelOpen = open;
        BottomPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        PanelSplitter.Visibility = BottomPanel.Visibility;
        PanelRow.Height = open ? new GridLength(Math.Max(90, Settings.PanelHeight)) : new GridLength(0);
        PanelSplitRow.Height = new GridLength(open ? 4 : 0);
        if (tab != null) panelTab = tab;
        TabProblems.IsChecked = panelTab == "problems";
        TabOutput.IsChecked = panelTab == "output";
        TabTerminal.IsChecked = panelTab == "terminal";
        ProblemsList.Visibility = panelTab == "problems" ? Visibility.Visible : Visibility.Collapsed;
        OutputBox.Visibility = panelTab == "output" ? Visibility.Visible : Visibility.Collapsed;
        TerminalHost.Visibility = panelTab == "terminal" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var b in new[] { TermNew, TermStop, TermClear, TermKill }) b.Visibility = panelTab == "terminal" ? Visibility.Visible : Visibility.Collapsed;
        if (open && panelTab == "terminal") { terminal.EnsureShell(); Dispatcher.BeginInvoke(terminal.FocusInput, DispatcherPriority.Input); }
        if (panelTab == "output") OutputBox.Text = output.ToString();
    }

    void PanelTab_Click(object sender, RoutedEventArgs e) => SetPanel(true, (string)((FrameworkElement)sender).Tag);
    void PanelClose_Click(object sender, RoutedEventArgs e) => SetPanel(false);
    void PanelMax_Click(object sender, RoutedEventArgs e)
    {
        var max = ActualHeight - 220;
        Settings.PanelHeight = Settings.PanelHeight > max * 0.8 ? 240 : max;
        SetPanel(true);
    }
    void TermNew_Click(object sender, RoutedEventArgs e) { SetPanel(true, "terminal"); terminal.NewShell(Root); }
    void TermStop_Click(object sender, RoutedEventArgs e) => terminal.Stop();
    void TermClear_Click(object sender, RoutedEventArgs e) => terminal.ClearActive();
    void TermKill_Click(object sender, RoutedEventArgs e) => terminal.KillActive();

    public void OpenTerminalAt(string dir) { SetPanel(true, "terminal"); terminal.NewShell(dir); }

    void Splitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (sidebarOpen) Settings.SidebarWidth = SideCol.ActualWidth;
        if (panelOpen) Settings.PanelHeight = PanelRow.ActualHeight;
        Settings.Save();
    }

    void TabScroll_Wheel(object sender, MouseWheelEventArgs e)
    {
        TabScroll.ScrollToHorizontalOffset(TabScroll.HorizontalOffset - e.Delta / 2.0);
        e.Handled = true;
    }

    public void Log(string s)
    {
        output.AppendLine($"[{DateTime.Now:HH:mm:ss}] {s}");
        if (panelTab == "output" && panelOpen) { OutputBox.Text = output.ToString(); OutputBox.ScrollToEnd(); }
    }

    // ====================================================================== notifications

    public void Notify(string msg, bool error = false, params (string Label, Action Run)[] actions)
    {
        var box = new Border { Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(12, 10, 8, 10), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) };
        box.SetResourceReference(Border.BackgroundProperty, "MenuBgBrush");
        box.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        box.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.35 };
        var dock = new DockPanel();
        var icon = new TextBlock { Text = error ? "" : "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 16, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top };
        icon.SetResourceReference(TextBlock.ForegroundProperty, error ? "DangerBrush" : "TabAccentBrush");
        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(icon);
        var x = new Button { Content = "", Style = (Style)FindResource("IconButton"), VerticalAlignment = VerticalAlignment.Top, FontSize = 10 };
        x.Click += (_, _) => Notes.Children.Remove(box);
        DockPanel.SetDock(x, Dock.Right);
        dock.Children.Add(x);
        var stack = new StackPanel();
        var text = new TextBlock { Text = msg, TextWrapping = TextWrapping.Wrap };
        text.SetResourceReference(TextBlock.ForegroundProperty, "MenuFgBrush");
        stack.Children.Add(text);
        if (actions.Length > 0)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            foreach (var (label, run) in actions)
            {
                var b = new Button { Content = label, Style = (Style)FindResource("PrimaryButton"), Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
                b.Click += (_, _) => { Notes.Children.Remove(box); run(); };
                row.Children.Add(b);
            }
            stack.Children.Add(row);
        }
        dock.Children.Add(stack);
        box.Child = dock;
        Notes.Children.Add(box);
        while (Notes.Children.Count > 4) Notes.Children.RemoveAt(0);
        Log((error ? "ERROR: " : "") + msg);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(error || actions.Length > 0 ? 12 : 5) };
        timer.Tick += (_, _) => { timer.Stop(); if (!box.IsMouseOver) Notes.Children.Remove(box); else timer.Start(); };
        timer.Start();
    }

    public bool Confirm(string msg, string title = "Valco Code") =>
        MessageBox.Show(this, msg, title, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    public string? Prompt(string title, string label, string initial = "")
    {
        var d = new PromptDialog(this, title, label, initial);
        return d.ShowDialog() == true ? d.Result : null;
    }

    // ====================================================================== formatting & problems

    async Task FormatTab(DocTab t, bool silent = false)
    {
        if (t.Code == null) return;
        if (!Formatter.CanFormat(t.Lang))
        {
            if (!silent) Notify($"There's no formatter for {Languages.Name(t.Lang)} files. Valco Code formats Python (Ruff) and Java (google-java-format).");
            return;
        }
        var before = t.Code.Editor.Text;
        SbFormatter.Content = "⟳ Formatting…";
        try
        {
            var after = await Formatter.Format(before, t.Lang, t.Path, Tools, Settings.TabSize, Settings.LineLength);
            if (t.Code.Editor.Text != before) return; // you kept typing
            if (before.Contains("\r\n") && !after.Contains("\r\n")) after = after.Replace("\n", "\r\n");
            t.Code.ReplaceAll(after);
        }
        catch (Exception e)
        {
            if (!silent) Notify($"Couldn't format {t.Title}. {e.Message}", true);
            Log($"Format failed for {t.Title}: {e.Message}");
        }
        finally { UpdateStatus(); }
    }

    async Task LintActive()
    {
        var t = active;
        if (t?.Code == null || t.Lang != "python" || Tools.Ruff == null) return;
        var path = t.Path ?? Path.Combine(Root ?? Path.GetTempPath(), t.Title + ".py");
        var list = await Linter.LintPython(t.Code.Editor.Text, path, Tools, Settings.LineLength);
        var key = t.Path ?? t.Title;
        if (list.Count > 0) problems[key] = list.Select(p => p with { Path = key }).ToList(); else problems.Remove(key);
        t.Code.SetProblems(list);
        UpdateProblems();
    }

    void UpdateProblems()
    {
        var rows = new List<ProblemRow>();
        int errors = 0, warnings = 0;
        foreach (var (path, list) in problems.OrderBy(k => k.Key))
        {
            var dir = Root != null && path.StartsWith(Root, StringComparison.OrdinalIgnoreCase) ? Path.GetRelativePath(Root, Path.GetDirectoryName(path) ?? "") : Path.GetDirectoryName(path) ?? "";
            rows.Add(new ProblemRow { Text = Path.GetFileName(path), Where = (dir == "." ? "" : dir) + $"   {list.Count}", Icon = "", IconBrush = FileIcons.For(path).Brush, Weight = FontWeights.SemiBold, FilePath = path });
            foreach (var p in list.OrderBy(p => p.Line))
            {
                if (p.Severity == "error") errors++; else warnings++;
                rows.Add(new ProblemRow
                {
                    Text = p.Message, Where = $"{p.Source}{(p.Code != null ? "(" + p.Code + ")" : "")}  [Ln {p.Line}, Col {p.Col}]",
                    Icon = p.Severity == "error" ? "" : "", IconBrush = p.Severity == "error" ? FileIcons.B("#f14c4c") : FileIcons.B("#cca700"),
                    Indent = new Thickness(18, 0, 0, 0), Problem = p, FilePath = path,
                });
            }
        }
        ProblemsList.ItemsSource = rows;
        var total = errors + warnings;
        ProblemsBadge.Visibility = total > 0 ? Visibility.Visible : Visibility.Collapsed;
        ProblemsBadgeText.Text = total.ToString();
        SbProblems.Content = $"⊗ {errors}   ⚠ {warnings}";
        if (rows.Count == 0) ProblemsList.ItemsSource = new[] { new ProblemRow { Text = "No problems have been detected in the workspace.", Icon = "", IconBrush = FileIcons.B("#57c27a") } };
    }

    async void Problems_Open(object sender, MouseButtonEventArgs e)
    {
        if (ProblemsList.SelectedItem is ProblemRow { FilePath: not null } r && File.Exists(r.FilePath))
            await OpenFile(r.FilePath, r.Problem?.Line ?? 0, r.Problem?.Col ?? 1);
    }
    void Problems_Key(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) Problems_Open(sender, null!); }

    // ====================================================================== running

    public async Task RunFile(string? path = null)
    {
        var t = path != null ? tabs.FirstOrDefault(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase)) : active;
        if (t is { Kind: TabKind.File } && (t.Dirty || t.Path == null))
        {
            if (!await Save(t)) return;
        }
        var file = path ?? t?.Path;
        if (file == null || (path == null && t != null && t.Kind != TabKind.File)) { Notify("Open a Python or Java file to run it."); return; }
        var lang = t?.Lang ?? Languages.Detect(file);
        var cwd = Root ?? Path.GetDirectoryName(file)!;
        if (lang == "python")
        {
            if (Tools.Python == null) { Notify("Python wasn't found. Install it from python.org (tick \"Add python.exe to PATH\"), then click Check Again in the Run view.", true, ("Get Python", () => Shell.Open("https://www.python.org/downloads/"))); return; }
            SetPanel(true, "terminal");
            var args = Path.GetFileName(Tools.Python).Equals("py.exe", StringComparison.OrdinalIgnoreCase) ? new[] { "-3", "-u", file } : new[] { "-u", file };
            terminal.RunProgram("Python", Tools.Python, args, cwd, $"> python {Path.GetFileName(file)}");
        }
        else if (lang == "java")
        {
            if (Tools.Javac == null || Tools.Java == null) { Notify("A Java JDK wasn't found. Install Temurin 21 from adoptium.net, then click Check Again in the Run view.", true, ("Get Java", () => Shell.Open("https://adoptium.net/"))); return; }
            SetPanel(true, "terminal");
            SbBusy.Text = "Compiling…";
            JavaBuild b;
            try { b = await JavaBuilder.Build(file, Tools); }
            catch (Exception e) { Notify(e.Message, true); SbBusy.Text = ""; return; }
            finally { SbBusy.Text = ""; }
            foreach (var k in problems.Keys.Where(k => k.EndsWith(".java", StringComparison.OrdinalIgnoreCase)).ToList()) problems.Remove(k);
            foreach (var g in b.Problems.GroupBy(p => p.Path, StringComparer.OrdinalIgnoreCase)) problems[g.Key] = g.ToList();
            foreach (var tab in tabs.Where(x => x.Lang == "java" && x.Code != null))
                tab.Code!.SetProblems(b.Problems.Where(p => string.Equals(p.Path, tab.Path, StringComparison.OrdinalIgnoreCase)));
            UpdateProblems();
            if (!b.Ok)
            {
                var errs = b.Problems.Count(p => p.Severity == "error");
                terminal.Print("Java", $"Compile failed ({errs} error{(errs == 1 ? "" : "s")}):\n\n{b.Output}\n", Color.FromRgb(0xf1, 0x4c, 0x4c));
                return;
            }
            terminal.RunProgram("Java", Tools.Java, new[] { "-cp", b.ClassDir, "-Dfile.encoding=UTF-8", "ValcoCodeRunner", b.MainClass }, Path.GetDirectoryName(file)!, $"> java {b.MainClass}");
        }
        else Notify($"Valco Code runs Python and Java files. This is a {Languages.Name(lang)} file.");
    }

    // ====================================================================== git

    public async Task RefreshGit()
    {
        if (Root == null || Tools.Git == null) { GitState = null; UpdateGitUi(); return; }
        try { GitState = await Git.Status(Root); }
        catch (Exception e) { Log("git status failed: " + e.Message); }
        UpdateGitUi();
    }

    Dictionary<string, string> Decorations()
    {
        var m = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var st = GitState;
        if (st?.IsRepo != true) return m;
        foreach (var f in st.Staged) m[Path.Combine(st.Top, f.Path.Replace('/', '\\'))] = f.Letter;
        foreach (var f in st.Changes) m[Path.Combine(st.Top, f.Path.Replace('/', '\\'))] = f.Letter;
        foreach (var f in st.Conflicts) m[Path.Combine(st.Top, f.Path.Replace('/', '\\'))] = "!";
        return m;
    }

    void UpdateGitUi()
    {
        var st = GitState;
        explorer.SetDecorations(Decorations());
        if (view == "scm") scm.Render();
        var n = st?.Count ?? 0;
        ScmBadge.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        ScmBadgeText.Text = n > 99 ? "99+" : n.ToString();
        if (st?.IsRepo == true)
        {
            SbBranch.Visibility = Visibility.Visible;
            SbBranch.Content = "⑂ " + st.Branch + (n > 0 ? "*" : "");
            SbSync.Visibility = Visibility.Visible;
            SbSync.Content = st.RemoteUrl == null ? "☁ Publish" : st.Upstream == null ? "☁ Publish Branch" : $"⇅ {st.Behind}↓ {st.Ahead}↑";
            SbSync.ToolTip = st.RemoteUrl == null ? "Publish to GitHub" : st.Upstream == null ? "Publish this branch" : "Sync changes (pull, then push)";
        }
        else { SbBranch.Visibility = Visibility.Collapsed; SbSync.Visibility = Visibility.Collapsed; }
    }

    public async Task GitOp(string label, Func<Task> op)
    {
        SbBusy.Text = "⟳ " + label;
        Log(label);
        try { await op(); }
        catch (Exception e)
        {
            Log(e.Message);
            var friendly = Git.FriendlyError(e.Message);
            if (friendly.StartsWith("GitHub needs you to sign in")) Notify(friendly, true, ("Sign in to GitHub", ShowSignIn));
            else Notify(friendly, true, ("Show Output", () => SetPanel(true, "output")));
        }
        finally
        {
            SbBusy.Text = "";
            await RefreshGit();
            await explorer.ReloadAll();
        }
    }

    public async Task OpenDiff(GitRow row)
    {
        var st = GitState;
        if (st == null) return;
        var staged = row.File.Staged;
        string diff;
        if (row.File.Work == '?' && !staged)
        {
            var text = File.Exists(row.FullPath) ? DecodeText(await File.ReadAllBytesAsync(row.FullPath)) : "";
            diff = $"new file: {row.Path}\n@@ -0,0 +1,{text.Split('\n').Length} @@\n" + string.Join("\n", text.Replace("\r\n", "\n").Split('\n').Select(l => "+" + l));
        }
        else
        {
            var args = new List<string> { "diff", "--no-color", "-U3" };
            if (staged) args.Add("--cached");
            args.Add("--");
            args.Add(row.Path);
            var r = await Git.Raw(st.Top, args);
            diff = r.Stdout;
            if (string.IsNullOrWhiteSpace(diff)) diff = "(no text changes)";
        }
        var title = $"{row.Name} ({(staged ? "Index" : "Working Tree")})";
        var existing = tabs.FirstOrDefault(t => t.Kind == TabKind.Diff && t.Title == title);
        if (existing != null) tabs.Remove(existing);
        var tab = new DocTab { Kind = TabKind.Diff, Title = title, Path = row.FullPath, Lang = Languages.Detect(row.Path) };
        tab.View = new DiffViewer(diff, () => _ = OpenFile(row.FullPath), Settings);
        tabs.RemoveAll(x => x.Kind == TabKind.Welcome);
        tabs.Add(tab);
        Activate(tab);
    }

    public async Task PickBranch()
    {
        var st = GitState;
        if (st?.IsRepo != true) { Notify("This folder isn't a Git repository."); return; }
        (List<string> Local, List<string> Remote, string Current) b;
        try { b = await Git.Branches(st.Top); } catch (Exception e) { Notify(e.Message, true); return; }
        var items = new List<PickItem> { new() { Id = "+new", Label = "+ Create new branch…" } };
        items.AddRange(b.Local.Select(n => new PickItem { Id = "l:" + n, Label = n, Icon = "⑂", Description = n == b.Current ? "current" : null }));
        items.AddRange(b.Remote.Where(r => !b.Local.Contains(r[(r.IndexOf('/') + 1)..])).Select(n => new PickItem { Id = "r:" + n, Label = n, Icon = "☁", Description = "remote branch" }));
        ShowPalette("Select a branch to check out", items, pick: (it, q) =>
        {
            if (it == null || it.Id == "+new")
            {
                var name = it == null ? q : Prompt("Create Branch", "New branch name:");
                if (string.IsNullOrWhiteSpace(name)) return;
                name = Regex.Replace(name.Trim(), @"\s+", "-");
                _ = GitOp($"Creating branch {name}…", async () => await Git.Run(st.Top, "checkout", "-b", name));
                return;
            }
            var n = it.Id[2..];
            if (it.Id.StartsWith("r:")) _ = GitOp($"Checking out {n}…", async () => await Git.Run(st.Top, "checkout", "--track", n));
            else _ = GitOp($"Checking out {n}…", async () => await Git.Run(st.Top, "checkout", n));
        }, allowFree: true);
    }

    public void ShowSignIn()
    {
        var d = new SignInDialog(this, User);
        var res = d.ShowDialog();
        if (res == true || d.User != User) { User = d.User; UpdateAccount(); scm.Render(); BuildMenu(); if (User != null) Notify($"Signed in to GitHub as {User.Login}."); }
    }

    void UpdateAccount()
    {
        ActAccount.ToolTip = User != null ? $"GitHub: {User.Login}" : "Accounts — Sign in to GitHub";
        ActAccount.IsChecked = User != null;
    }

    public void ShowPublish()
    {
        if (Root == null) { Notify("Open a folder first."); return; }
        if (Tools.Git == null) { Notify("Git isn't installed. Install it from git-scm.com first.", true); return; }
        if (User == null)
        {
            ShowSignIn();
            if (User == null) return;
        }
        var d = new PublishDialog(this, GitState?.IsRepo == true ? GitState.Top : Root, User, Log);
        if (d.ShowDialog() == true && d.Url != null)
        {
            var url = d.Url;
            Notify($"Published to {url.Replace("https://", "")}", false, ("Open on GitHub", () => Shell.Open(url)));
        }
        _ = RefreshGit();
    }

    public void ShowClone()
    {
        if (Tools.Git == null) { Notify("Git isn't installed. Install it from git-scm.com first.", true, ("Get Git", () => Shell.Open("https://git-scm.com/download/win"))); return; }
        var d = new CloneDialog(this, User, ShowSignIn);
        if (d.ShowDialog() == true && d.ClonedPath != null) OpenFolder(d.ClonedPath);
    }

    public async Task<int> ReplaceInFile(string path, Regex re, string replacement)
    {
        var t = tabs.FirstOrDefault(x => x.Code != null && string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
        if (t != null)
        {
            var before = t.Code!.Editor.Text;
            var n = re.Matches(before).Count;
            t.Code.ReplaceAll(re.Replace(before, replacement));
            await Save(t, noFormat: true);
            return n;
        }
        var text = DecodeText(await File.ReadAllBytesAsync(path));
        var count = re.Matches(text).Count;
        if (count > 0) await File.WriteAllTextAsync(path, re.Replace(text, replacement), new UTF8Encoding(false));
        return count;
    }

    // ====================================================================== status bar

    void UpdateCursor()
    {
        if (active?.Code == null) return;
        var c = active.Code.Editor.TextArea.Caret;
        var sel = active.Code.Editor.SelectionLength;
        SbCursor.Content = $"Ln {c.Line}, Col {c.Column}" + (sel > 0 ? $" ({sel} selected)" : "");
    }

    void UpdateStatus()
    {
        var isFile = active?.Kind == TabKind.File && active.Code != null;
        foreach (var b in new[] { SbCursor, SbIndent, SbEncoding, SbEol, SbLang }) b.Visibility = isFile ? Visibility.Visible : Visibility.Collapsed;
        SbFormatter.Visibility = isFile && Formatter.CanFormat(active!.Lang) ? Visibility.Visible : Visibility.Collapsed;
        RunBtn.Visibility = isFile && active!.Lang is "python" or "java" ? Visibility.Visible : Visibility.Collapsed;
        FormatBtn.Visibility = isFile ? Visibility.Visible : Visibility.Collapsed;
        if (isFile)
        {
            UpdateCursor();
            SbIndent.Content = $"Spaces: {Settings.TabSize}";
            SbEol.Content = active!.Code!.Editor.Text.Contains("\r\n") ? "CRLF" : "LF";
            SbLang.Content = Languages.Name(active.Lang);
            SbFormatter.Content = "✓ " + Formatter.Describe(active.Lang);
        }
        SbTheme.Content = "◐ " + ThemeManager.Current.Name;
        if (SbProblems.Content == null) UpdateProblems();
    }

    void Branch_Click(object sender, RoutedEventArgs e) => _ = PickBranch();
    void Sync_Click(object sender, RoutedEventArgs e) => Cmd_("git.sync");
    void SbProblems_Click(object sender, RoutedEventArgs e) => SetPanel(!(panelOpen && panelTab == "problems"), "problems");
    void GoToLine_Click(object sender, RoutedEventArgs e) => GoToLine();
    void Indent_Click(object sender, RoutedEventArgs e) => PickIndent();
    void Lang_Click(object sender, RoutedEventArgs e) => PickLanguage();
    void Theme_Click(object sender, RoutedEventArgs e) => PickTheme();
    void Eol_Click(object sender, RoutedEventArgs e)
    {
        if (active?.Code == null) return;
        ShowPalette("Select End of Line Sequence", new() { new() { Id = "lf", Label = "LF" }, new() { Id = "crlf", Label = "CRLF" } }, pick: (it, _) =>
        {
            if (it == null || active?.Code == null) return;
            var txt = active.Code.Editor.Text.Replace("\r\n", "\n");
            active.Code.ReplaceAll(it.Id == "crlf" ? txt.Replace("\n", "\r\n") : txt);
            UpdateStatus();
        });
    }
    void Bell_Click(object sender, RoutedEventArgs e) => SetPanel(true, "output");

    /// <summary>The Valco Office button: suite-wide shortcuts, like the orb in Valco Word.</summary>
    void ValcoButton_Click(object sender, RoutedEventArgs e)
    {
        var m = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
        var head = new MenuItem { Header = "Valco Office", IsEnabled = false, FontWeight = FontWeights.SemiBold };
        head.Icon = new System.Windows.Controls.Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/ValcoOffice-mark.png")), Width = 16, Height = 16 };
        m.Items.Add(head);
        m.Items.Add(new Separator());
        foreach (var id in new[] { "new", "openfile", "openfolder", "save", "saveas" }) m.Items.Add(MenuFor(id));
        m.Items.Add(new Separator());
        foreach (var id in new[] { "settings", "theme", "welcome", "about" }) m.Items.Add(MenuFor(id));
        m.Items.Add(new Separator());
        var exit = new MenuItem { Header = "Exit Valco Code", InputGestureText = "Alt+F4" };
        exit.Click += (_, _) => Close();
        m.Items.Add(exit);
        m.IsOpen = true;
    }
    void Run_Click(object sender, RoutedEventArgs e) => _ = RunFile();
    void Format_Click(object sender, RoutedEventArgs e) { if (active != null) _ = FormatTab(active); }
    void Settings_Click(object sender, RoutedEventArgs e) { ActSettings.IsChecked = false; ShowSettingsMenu((FrameworkElement)sender); }
    void Account_Click(object sender, RoutedEventArgs e) { ShowSignIn(); UpdateAccount(); }
    void CommandCenter_Click(object sender, MouseButtonEventArgs e) => QuickOpen();
    void TabMore_Click(object sender, RoutedEventArgs e)
    {
        var m = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
        foreach (var id in new[] { "closeall", "saveall", "wrap", "format", "formatonsave" }) m.Items.Add(MenuFor(id));
        m.IsOpen = true;
    }

    void ShowSettingsMenu(FrameworkElement anchor)
    {
        var m = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Right };
        foreach (var id in new[] { "palette", "settings", "shortcuts", "theme" }) m.Items.Add(MenuFor(id));
        m.Items.Add(new Separator());
        m.Items.Add(MenuFor("about", "About Valco Code"));
        m.IsOpen = true;
    }

    // ====================================================================== commands & keyboard

    void Cmd_(string id) => commands.FirstOrDefault(c => c.Id == id)?.Run();

    void BuildCommands()
    {
        commands = new()
        {
            new("palette", "Show All Commands", "Ctrl+Shift+P", () => CommandPalette()),
            new("quickopen", "Go to File…", "Ctrl+P", () => QuickOpen()),
            new("gotoline", "Go to Line…", "Ctrl+G", () => GoToLine()),
            new("new", "File: New Text File", "Ctrl+N", () => NewUntitled()),
            new("newpy", "File: New Python File", null, () => NewUntitled("python")),
            new("newjava", "File: New Java File", null, () => NewUntitled("java")),
            new("openfile", "File: Open File…", "Ctrl+O", OpenFileDialog),
            new("openfolder", "File: Open Folder…", "Ctrl+K Ctrl+O", () => OpenFolder()),
            new("closefolder", "File: Close Folder", "Ctrl+K F", CloseFolder),
            new("save", "File: Save", "Ctrl+S", () => { if (active != null) _ = Save(active); }),
            new("saveas", "File: Save As…", "Ctrl+Shift+S", () => { if (active != null) _ = Save(active, saveAs: true); }),
            new("saveall", "File: Save All", "Ctrl+K S", () => _ = SaveAll()),
            new("close", "View: Close Editor", "Ctrl+W", () => { if (active != null) _ = CloseTab(active); }),
            new("closeall", "View: Close All Editors", "Ctrl+K W", async () => { foreach (var t in tabs.ToList()) await CloseTab(t); }),
            new("autosave", "File: Toggle Auto Save", null, () => { Settings.AutoSave = !Settings.AutoSave; Settings.Save(); BuildMenu(); Notify("Auto Save is " + (Settings.AutoSave ? "on." : "off.")); }),
            new("format", "Format Document", "Shift+Alt+F", () => { if (active != null) _ = FormatTab(active); }),
            new("formatonsave", "Preferences: Toggle Format on Save", null, () => { Settings.FormatOnSave = !Settings.FormatOnSave; Settings.Save(); BuildMenu(); Notify("Format on Save is " + (Settings.FormatOnSave ? "on." : "off.")); }),
            new("comment", "Toggle Line Comment", "Ctrl+/", () => { if (active?.Code != null) EditCommands.ToggleComment(active.Code.Editor, active.Lang); }),
            new("find", "Find", "Ctrl+F", () => active?.Code?.OpenFind(false)),
            new("replace", "Replace", "Ctrl+H", () => active?.Code?.OpenFind(true)),
            new("findfiles", "Search: Find in Files", "Ctrl+Shift+F", FindInFiles),
            new("replacefiles", "Search: Replace in Files", "Ctrl+Shift+H", FindInFiles),
            new("undo", "Undo", "Ctrl+Z", () => active?.Code?.Editor.Undo()),
            new("redo", "Redo", "Ctrl+Y", () => active?.Code?.Editor.Redo()),
            new("selectall", "Select All", "Ctrl+A", () => active?.Code?.Editor.SelectAll()),
            new("moveup", "Move Line Up", "Alt+Up", () => { if (active?.Code != null) EditCommands.MoveLines(active.Code.Editor, -1, false); }),
            new("movedown", "Move Line Down", "Alt+Down", () => { if (active?.Code != null) EditCommands.MoveLines(active.Code.Editor, 1, false); }),
            new("copyup", "Copy Line Up", "Shift+Alt+Up", () => { if (active?.Code != null) EditCommands.MoveLines(active.Code.Editor, -1, true); }),
            new("copydown", "Copy Line Down", "Shift+Alt+Down", () => { if (active?.Code != null) EditCommands.MoveLines(active.Code.Editor, 1, true); }),
            new("suggest", "Trigger Suggest", "Ctrl+Space", () => active?.Code?.ShowCompletion(true)),
            new("fold", "Fold All", "Ctrl+K Ctrl+0", () => FoldAll(true)),
            new("unfold", "Unfold All", "Ctrl+K Ctrl+J", () => FoldAll(false)),
            new("wrap", "View: Toggle Word Wrap", "Alt+Z", () => { Settings.WordWrap = !Settings.WordWrap; ApplySettingsAll(); BuildMenu(); }),
            new("whitespace", "View: Toggle Render Whitespace", null, () => { Settings.ShowWhitespace = !Settings.ShowWhitespace; ApplySettingsAll(); BuildMenu(); }),
            new("explorer", "View: Show Explorer", "Ctrl+Shift+E", () => ShowView("explorer")),
            new("search", "View: Show Search", "Ctrl+Shift+F", FindInFiles),
            new("scm", "View: Show Source Control", "Ctrl+Shift+G", () => { ShowView("scm"); scm.FocusMessage(); }),
            new("runview", "View: Show Run and Debug", "Ctrl+Shift+D", () => ShowView("run")),
            new("sidebar", "View: Toggle Primary Side Bar", "Ctrl+B", () => SetSidebar(!sidebarOpen)),
            new("panel", "View: Toggle Panel", "Ctrl+J", () => SetPanel(!panelOpen)),
            new("problems", "View: Show Problems", "Ctrl+Shift+M", () => SetPanel(true, "problems")),
            new("output", "View: Show Output", "Ctrl+Shift+U", () => SetPanel(true, "output")),
            new("terminal", "View: Toggle Terminal", "Ctrl+`", () => { if (panelOpen && panelTab == "terminal") SetPanel(false); else SetPanel(true, "terminal"); }),
            new("newterm", "Terminal: Create New Terminal", "Ctrl+Shift+`", () => { SetPanel(true, "terminal"); terminal.NewShell(Root); }),
            new("killterm", "Terminal: Kill the Active Terminal", null, () => terminal.KillActive()),
            new("clearterm", "Terminal: Clear", null, () => terminal.ClearActive()),
            new("zoomin", "View: Zoom In", "Ctrl+=", () => Zoom(1)),
            new("zoomout", "View: Zoom Out", "Ctrl+-", () => Zoom(-1)),
            new("zoomreset", "View: Reset Zoom", "Ctrl+0", () => Zoom(0)),
            new("run", "Run: Run File", "F5", () => _ = RunFile()),
            new("run2", "Run: Run Without Debugging", "Ctrl+F5", () => _ = RunFile()),
            new("stop", "Run: Stop", "Shift+F5", () => terminal.Stop()),
            new("theme", "Preferences: Color Theme", "Ctrl+K Ctrl+T", PickTheme),
            new("settings", "Preferences: Open Settings", "Ctrl+,", () => OpenSpecial(TabKind.Settings)),
            new("shortcuts", "Preferences: Keyboard Shortcuts", "Ctrl+K Ctrl+S", () => OpenSpecial(TabKind.Shortcuts)),
            new("lang", "Change Language Mode", "Ctrl+K M", PickLanguage),
            new("indent", "Change Tab Size", null, PickIndent),
            new("welcome", "Help: Welcome", null, () => OpenSpecial(TabKind.Welcome)),
            new("about", "Help: About Valco Code", null, () => new AboutDialog(this).ShowDialog()),
            new("git.commit", "Git: Commit", null, () => { ShowView("scm"); scm.FocusMessage(); }),
            new("git.push", "Git: Push", null, () => GitCmd("Pushing…", async st =>
            {
                if (st.RemoteUrl == null) { ShowPublish(); return; }
                if (st.Upstream == null) await Git.Run(st.Top, true, "push", "-u", "origin", st.Branch); else await Git.Run(st.Top, true, "push");
            })),
            new("git.pull", "Git: Pull", null, () => GitCmd("Pulling…", async st => await Git.Run(st.Top, true, "-c", "pull.rebase=false", "pull", "--no-edit"))),
            new("git.sync", "Git: Sync", null, () => GitCmd("Syncing…", async st =>
            {
                if (st.RemoteUrl == null) { ShowPublish(); return; }
                if (st.Upstream == null) { await Git.Run(st.Top, true, "push", "-u", "origin", st.Branch); return; }
                await Git.Run(st.Top, true, "-c", "pull.rebase=false", "pull", "--no-edit");
                await Git.Run(st.Top, true, "push");
            })),
            new("git.fetch", "Git: Fetch", null, () => GitCmd("Fetching…", async st => await Git.Run(st.Top, true, "fetch", "--prune"))),
            new("git.checkout", "Git: Checkout to…", null, () => _ = PickBranch()),
            new("git.init", "Git: Initialize Repository", null, () => { if (Root != null) _ = GitOp("Initializing…", async () => { var r = await Git.Raw(Root, new[] { "init", "-b", "main" }); if (!r.Ok) await Git.Run(Root, "init"); }); }),
            new("git.clone", "Git: Clone…", null, ShowClone),
            new("git.publish", "Publish to GitHub", null, ShowPublish),
            new("gh.signin", "GitHub: Sign In / Account", null, ShowSignIn),
        };
    }

    void GitCmd(string label, Func<GitStatus, Task> op)
    {
        var st = GitState;
        if (st?.IsRepo != true) { Notify("This folder isn't a Git repository.", false, ("Initialize Repository", () => Cmd_("git.init"))); return; }
        _ = GitOp(label, () => op(st));
    }

    void FoldAll(bool fold)
    {
        var area = active?.Code?.Editor.TextArea;
        if (area == null) return;
        var fm = area.GetService(typeof(ICSharpCode.AvalonEdit.Folding.FoldingManager)) as ICSharpCode.AvalonEdit.Folding.FoldingManager;
        if (fm == null) return;
        foreach (var f in fm.AllFoldings) f.IsFolded = fold;
    }

    void Zoom(int d)
    {
        Settings.FontSize = d == 0 ? 14 : Math.Clamp(Settings.FontSize + d, 8, 36);
        Settings.TerminalFontSize = d == 0 ? 13 : Math.Clamp(Settings.TerminalFontSize + d, 8, 32);
        ApplySettingsAll();
    }

    void ApplySettingsAll()
    {
        Settings.Save();
        foreach (var t in tabs) t.Code?.ApplySettings(Settings);
        terminal.ApplyTheme(Settings.TerminalFontSize);
        UpdateStatus();
    }

    void FindInFiles()
    {
        var sel = active?.Code?.Editor.SelectedText;
        ShowView("search");
        search.FocusQuery(sel != null && sel.Length > 0 && !sel.Contains('\n') ? sel : null);
    }

    void OpenFileDialog()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, InitialDirectory = Root ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) };
        if (dlg.ShowDialog(this) == true) foreach (var f in dlg.FileNames) _ = OpenFile(f);
    }

    static string KeyName(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return "";
        var m = Keyboard.Modifiers;
        var parts = new List<string>();
        if (m.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (m.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (m.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        var k = key switch
        {
            Key.Oem3 => "`", Key.OemPlus or Key.Add => "=", Key.OemMinus or Key.Subtract => "-", Key.OemQuestion or Key.Divide => "/",
            Key.OemComma => ",", Key.OemPeriod => ".", Key.D0 or Key.NumPad0 => "0", Key.Return => "Enter", Key.Prior => "PageUp", Key.Next => "PageDown",
            >= Key.D1 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
            _ => key.ToString(),
        };
        parts.Add(k);
        return string.Join("+", parts);
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (PaletteVeil.Visibility == Visibility.Visible) return;
        var k = KeyName(e);
        if (k.Length == 0) return;
        var focus = Keyboard.FocusedElement as DependencyObject;
        var inEditor = focus is ICSharpCode.AvalonEdit.Editing.TextArea;
        var inTextBox = focus is TextBox or PasswordBox;
        if (chord != null && (DateTime.Now - chordAt).TotalSeconds < 2)
        {
            var full = chord + " " + k;
            chord = null;
            var cc = commands.FirstOrDefault(c => c.Key == full);
            if (cc != null) { e.Handled = true; cc.Run(); return; }
            if (k != "Ctrl+K") return;
        }
        chord = null;
        if (k == "Ctrl+K" && !inTextBox) { chord = k; chordAt = DateTime.Now; e.Handled = true; SbBusy.Text = "(Ctrl+K) was pressed. Waiting for second key of chord…"; DelayClearChordHint(); return; }
        // keys the editor handles itself
        var editorOwned = new HashSet<string> { "Ctrl+Z", "Ctrl+Y", "Ctrl+A", "Ctrl+/", "Alt+Up", "Alt+Down", "Shift+Alt+Up", "Shift+Alt+Down", "Ctrl+F", "Ctrl+H", "Ctrl+Space", "Ctrl+Shift+Z", "Ctrl+D", "F3", "Shift+F3" };
        if (editorOwned.Contains(k) && (inEditor || inTextBox)) return;
        if (inTextBox && !new[] { "Ctrl+Shift+P", "Ctrl+P", "Ctrl+S", "Ctrl+`", "Ctrl+B", "Ctrl+J", "F5", "Ctrl+Shift+`", "Ctrl+W", "Ctrl+N", "Ctrl+Shift+G", "Ctrl+Shift+E", "Ctrl+Shift+F", "Ctrl+Tab" }.Contains(k)) return;
        var alias = new Dictionary<string, string> { ["Ctrl+Shift+I"] = "format", ["F1"] = "palette", ["Ctrl+Shift+Z"] = "redo", ["Ctrl+E"] = "quickopen", ["Ctrl+Shift+N"] = "new", ["Ctrl+Add"] = "zoomin" };
        if (k is "Ctrl+Tab" or "Ctrl+Shift+Tab" or "Ctrl+PageDown" or "Ctrl+PageUp")
        {
            e.Handled = true;
            if (tabs.Count == 0) return;
            var i = active != null ? tabs.IndexOf(active) : 0;
            var fwd = k is "Ctrl+Tab" or "Ctrl+PageDown";
            Activate(tabs[(i + (fwd ? 1 : tabs.Count - 1)) % tabs.Count]);
            return;
        }
        var c = commands.FirstOrDefault(x => x.Key == k) ?? (alias.TryGetValue(k, out var id) ? commands.FirstOrDefault(x => x.Id == id) : null);
        if (c == null) return;
        e.Handled = true;
        c.Run();
    }

    async void DelayClearChordHint()
    {
        await Task.Delay(2000);
        if (chord == null && SbBusy.Text.StartsWith("(Ctrl+K)")) SbBusy.Text = "";
        if (chord != null && (DateTime.Now - chordAt).TotalSeconds >= 2) { chord = null; SbBusy.Text = ""; }
    }

    // ====================================================================== menu bar

    MenuItem MenuFor(string id, string? label = null)
    {
        var c = commands.First(x => x.Id == id);
        var mi = new MenuItem { Header = label ?? Regex.Replace(c.Title, @"^[\w ]+: ", ""), InputGestureText = c.Key ?? "" };
        mi.Click += (_, _) => c.Run();
        return mi;
    }

    void BuildMenu()
    {
        SyncRibbon();
        MainMenu.Items.Clear();
        MenuItem Top(string h) { var m = new MenuItem { Header = h }; MainMenu.Items.Add(m); return m; }
        MenuItem Check(string h, bool on, Action a) { var m = new MenuItem { Header = h, IsCheckable = true, IsChecked = on }; m.Click += (_, _) => a(); return m; }
        Separator S() => new();

        var file = Top("_File");
        foreach (var x in new object[] { MenuFor("new", "New Text File"), MenuFor("newpy", "New Python File"), MenuFor("newjava", "New Java File"), S(), MenuFor("openfile", "Open File…"), MenuFor("openfolder", "Open Folder…") }) file.Items.Add(x);
        var recent = new MenuItem { Header = "Open Recent" };
        foreach (var r in Settings.Recent) { var mi = new MenuItem { Header = r.Replace("_", "__") }; var path = r; mi.Click += (_, _) => OpenFolder(path); recent.Items.Add(mi); }
        if (Settings.Recent.Count == 0) recent.Items.Add(new MenuItem { Header = "(none)", IsEnabled = false });
        else { recent.Items.Add(S()); var clr = new MenuItem { Header = "Clear Recently Opened" }; clr.Click += (_, _) => { Settings.Recent.Clear(); Settings.Save(); BuildMenu(); }; recent.Items.Add(clr); }
        file.Items.Add(recent);
        file.Items.Add(MenuFor("git.clone", "Clone Git Repository…"));
        file.Items.Add(S());
        foreach (var id in new[] { "save", "saveas", "saveall" }) file.Items.Add(MenuFor(id));
        file.Items.Add(S());
        file.Items.Add(Check("Auto Save", Settings.AutoSave, () => Cmd_("autosave")));
        file.Items.Add(Check("Format on Save", Settings.FormatOnSave, () => Cmd_("formatonsave")));
        var prefs = new MenuItem { Header = "Preferences" };
        prefs.Items.Add(MenuFor("settings", "Settings"));
        prefs.Items.Add(MenuFor("theme", "Color Theme"));
        prefs.Items.Add(MenuFor("shortcuts", "Keyboard Shortcuts"));
        file.Items.Add(prefs);
        file.Items.Add(S());
        file.Items.Add(MenuFor("close", "Close Editor"));
        var cf = MenuFor("closefolder", "Close Folder"); cf.IsEnabled = Root != null; file.Items.Add(cf);
        file.Items.Add(S());
        var exit = new MenuItem { Header = "Exit", InputGestureText = "Alt+F4" }; exit.Click += (_, _) => Close(); file.Items.Add(exit);

        var edit = Top("_Edit");
        foreach (var x in new object[] { MenuFor("undo"), MenuFor("redo"), S() }) edit.Items.Add(x);
        foreach (var (h, k, a) in new (string, string, Action)[] { ("Cut", "Ctrl+X", () => active?.Code?.Editor.Cut()), ("Copy", "Ctrl+C", () => active?.Code?.Editor.Copy()), ("Paste", "Ctrl+V", () => active?.Code?.Editor.Paste()) })
        { var mi = new MenuItem { Header = h, InputGestureText = k }; mi.Click += (_, _) => a(); edit.Items.Add(mi); }
        foreach (var x in new object[] { S(), MenuFor("find"), MenuFor("replace"), S(), MenuFor("findfiles", "Find in Files"), MenuFor("replacefiles", "Replace in Files"), S(), MenuFor("comment"), MenuFor("format") }) edit.Items.Add(x);

        var sel = Top("_Selection");
        foreach (var x in new object[] { MenuFor("selectall"), S(), MenuFor("copyup"), MenuFor("copydown"), MenuFor("moveup"), MenuFor("movedown") }) sel.Items.Add(x);
        sel.Items.Add(new MenuItem { Header = "Select Word / Next Occurrence", InputGestureText = "Ctrl+D", IsEnabled = false });

        var viewM = Top("_View");
        foreach (var x in new object[] { MenuFor("palette", "Command Palette…"), MenuFor("quickopen"), MenuFor("gotoline"), S(), MenuFor("explorer", "Explorer"), MenuFor("search", "Search"), MenuFor("scm", "Source Control"), MenuFor("runview", "Run"), S(), MenuFor("problems", "Problems"), MenuFor("output", "Output"), MenuFor("terminal", "Terminal"), S() }) viewM.Items.Add(x);
        var themes = new MenuItem { Header = "Colour Scheme" };
        foreach (var t in ThemeManager.All)
        {
            var id = t.Id;
            if (id == "weavecraft") themes.Items.Add(S());
            themes.Items.Add(Check(t.Name, ThemeManager.Current.Id == id, () => SetTheme(id)));
        }
        viewM.Items.Add(themes);
        foreach (var x in new object[] { MenuFor("sidebar", "Toggle Side Bar"), MenuFor("panel", "Toggle Panel"), Check("Word Wrap", Settings.WordWrap, () => Cmd_("wrap")), Check("Render Whitespace", Settings.ShowWhitespace, () => Cmd_("whitespace")), S(), MenuFor("fold"), MenuFor("unfold"), S(), MenuFor("zoomin", "Zoom In"), MenuFor("zoomout", "Zoom Out"), MenuFor("zoomreset", "Reset Zoom") }) viewM.Items.Add(x);

        var run = Top("_Run");
        foreach (var id in new[] { "run", "run2", "stop" }) run.Items.Add(MenuFor(id));

        var term = Top("_Terminal");
        foreach (var id in new[] { "newterm", "terminal", "killterm", "clearterm" }) term.Items.Add(MenuFor(id));

        var git = Top("_Git");
        foreach (var x in new object[] { MenuFor("git.commit", "Commit…"), MenuFor("git.push", "Push"), MenuFor("git.pull", "Pull"), MenuFor("git.sync", "Sync"), MenuFor("git.fetch", "Fetch"), S(), MenuFor("git.checkout", "Checkout to…"), MenuFor("git.init", "Initialize Repository"), MenuFor("git.clone", "Clone…"), MenuFor("git.publish", "Publish to GitHub…"), S(), MenuFor("gh.signin", User != null ? $"GitHub: {User.Login}…" : "Sign in to GitHub…") }) git.Items.Add(x);

        var help = Top("_Help");
        foreach (var x in new object[] { MenuFor("welcome", "Welcome"), MenuFor("shortcuts", "Keyboard Shortcuts"), MenuFor("palette", "Show All Commands"), S(), MenuFor("about", "About Valco Code") }) help.Items.Add(x);
    }

    // ====================================================================== command palette

    List<PickItem> paletteItems = new();
    Func<string, List<PickItem>>? paletteProvider;
    Action<PickItem?, string>? palettePick;
    Action<PickItem?>? paletteHighlight;
    Action? paletteCancel;
    Func<string, bool>? paletteQuery;
    bool paletteFree;

    void ShowPalette(string placeholder, List<PickItem>? items = null, Func<string, List<PickItem>>? provider = null, Action<PickItem?, string>? pick = null,
        Action<PickItem?>? highlight = null, Action? cancel = null, string initial = "", Func<string, bool>? onQuery = null, bool allowFree = false)
    {
        paletteItems = items ?? new();
        paletteProvider = provider;
        palettePick = pick;
        paletteHighlight = highlight;
        paletteCancel = cancel;
        paletteQuery = onQuery;
        paletteFree = allowFree;
        PaletteInput.Tag = placeholder;
        PaletteVeil.Visibility = Visibility.Visible;
        settingPalette = true;
        PaletteInput.Text = initial;
        settingPalette = false;
        PaletteInput.CaretIndex = initial.Length;
        FilterPalette();
        Dispatcher.BeginInvoke(() => PaletteInput.Focus(), DispatcherPriority.Input);
    }

    bool settingPalette;

    void FilterPalette()
    {
        var q = PaletteInput.Text;
        List<PickItem> shown;
        if (paletteProvider != null) shown = paletteProvider(q);
        else if (q.Trim().Length == 0) shown = paletteItems;
        else shown = paletteItems.Select(i => (i, s: Fuzzy(q.Trim(), i.Label, i.Description))).Where(x => x.s > 0).OrderByDescending(x => x.s).Select(x => x.i).ToList();
        PaletteList.ItemsSource = shown.Take(300).ToList();
        if (shown.Count > 0) PaletteList.SelectedIndex = 0;
    }

    static double Fuzzy(string q, string text, string? extra)
    {
        double Score(string t)
        {
            var lt = t.ToLowerInvariant();
            var lq = q.ToLowerInvariant();
            int ti = 0, prev = -2;
            double s = 0;
            foreach (var ch in lq)
            {
                if (ch == ' ') continue;
                var i = lt.IndexOf(ch, ti);
                if (i < 0) return 0;
                s += i == prev + 1 ? 5 : 1;
                if (i == 0 || " /\\._-".Contains(lt[i - 1]) || char.IsUpper(t[i])) s += 3;
                prev = i;
                ti = i + 1;
            }
            var c = lt.IndexOf(lq, StringComparison.Ordinal);
            if (c >= 0) s += 20 - Math.Min(c, 10);
            return s - t.Length / 100.0;
        }
        var a = Score(text);
        if (a > 0) return a;
        return extra != null ? Score(extra + " " + text) * 0.5 : 0;
    }

    void ClosePalette(bool cancelled)
    {
        PaletteVeil.Visibility = Visibility.Collapsed;
        if (cancelled) paletteCancel?.Invoke();
        if (active?.Code != null) Dispatcher.BeginInvoke(() => active?.Code?.Editor.TextArea.Focus(), DispatcherPriority.Input);
    }

    void PickPalette()
    {
        var it = PaletteList.SelectedItem as PickItem;
        if (it == null && !paletteFree) return;
        var q = PaletteInput.Text;
        var pick = palettePick;
        ClosePalette(false);
        pick?.Invoke(it, q);
    }

    void PaletteInput_Changed(object sender, TextChangedEventArgs e)
    {
        if (settingPalette) return;
        if (paletteQuery != null && paletteQuery(PaletteInput.Text)) return;
        FilterPalette();
    }

    void PaletteInput_Key(object sender, KeyEventArgs e)
    {
        var n = PaletteList.Items.Count;
        switch (e.Key)
        {
            case Key.Down: PaletteList.SelectedIndex = Math.Min(PaletteList.SelectedIndex + 1, n - 1); e.Handled = true; break;
            case Key.Up: PaletteList.SelectedIndex = Math.Max(PaletteList.SelectedIndex - 1, 0); e.Handled = true; break;
            case Key.PageDown: PaletteList.SelectedIndex = Math.Min(PaletteList.SelectedIndex + 10, n - 1); e.Handled = true; break;
            case Key.PageUp: PaletteList.SelectedIndex = Math.Max(PaletteList.SelectedIndex - 10, 0); e.Handled = true; break;
            case Key.Enter: PickPalette(); e.Handled = true; break;
            case Key.Escape: ClosePalette(true); e.Handled = true; break;
        }
        if (PaletteList.SelectedItem != null) PaletteList.ScrollIntoView(PaletteList.SelectedItem);
    }

    void PaletteList_Click(object sender, MouseButtonEventArgs e) { if (PaletteList.SelectedItem != null) PickPalette(); }
    void PaletteList_Selection(object sender, SelectionChangedEventArgs e) => paletteHighlight?.Invoke(PaletteList.SelectedItem as PickItem);
    void PaletteVeil_MouseDown(object sender, MouseButtonEventArgs e) => ClosePalette(true);
    void PaletteBox_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    List<string> fileCache = new();
    string? fileCacheRoot;

    void QuickOpen(string initial = "")
    {
        if (initial.StartsWith('>')) { CommandPalette(initial[1..]); return; }
        if (initial.StartsWith(':')) { GoToLine(initial); return; }
        if (Root != null && fileCacheRoot != Root)
        {
            fileCacheRoot = Root;
            fileCache = ListFiles(Root).ToList();
        }
        else if (Root != null) _ = Task.Run(() => ListFiles(Root!).ToList()).ContinueWith(t => fileCache = t.Result);
        var open = tabs.Where(t => t.Path != null).Select(t => t.Path!).ToList();
        var files = open.Concat(Root != null ? fileCache : new()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var items = files.Select(f =>
        {
            var (ic, br) = FileIcons.For(f);
            return new PickItem { Id = f, Label = Path.GetFileName(f), Description = Root != null && f.StartsWith(Root, StringComparison.OrdinalIgnoreCase) ? Path.GetRelativePath(Root, Path.GetDirectoryName(f)!) : Path.GetDirectoryName(f), Icon = ic, IconBrush = br, Hint = open.Contains(f) ? "open" : null };
        }).ToList();
        ShowPalette(Root != null ? "Search files by name (type > for commands, : to go to a line)" : "Open a folder to search files. Type > for commands.", items,
            pick: (it, q) => { if (it != null) _ = OpenFile(it.Id); }, initial: initial,
            onQuery: q =>
            {
                if (q.StartsWith('>')) { CommandPalette(q[1..]); return true; }
                if (q.StartsWith(':')) { GoToLine(q); return true; }
                return false;
            });
    }

    static IEnumerable<string> ListFiles(string root)
    {
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".git", "node_modules", "target", "__pycache__", ".venv", "venv", "build", "out", "bin", "obj", ".idea", ".gradle", "dist" };
        var stack = new Stack<(string, int)>();
        stack.Push((root, 0));
        int count = 0;
        while (stack.Count > 0 && count < 20000)
        {
            var (d, depth) = stack.Pop();
            string[] files = Array.Empty<string>(), dirs = Array.Empty<string>();
            try { files = Directory.GetFiles(d); dirs = Directory.GetDirectories(d); } catch { }
            foreach (var f in files) { count++; yield return f; }
            if (depth < 20) foreach (var sub in dirs) if (!skip.Contains(Path.GetFileName(sub))) stack.Push((sub, depth + 1));
        }
    }

    void CommandPalette(string initial = "")
    {
        ShowPalette("Type the name of a command to run.", initial: ">" + initial,
            provider: q =>
            {
                var query = q.TrimStart('>').Trim();
                var words = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return commands.Where(c => words.All(w => c.Title.ToLowerInvariant().Contains(w)))
                    .Select(c => new PickItem { Id = c.Id, Label = c.Title, Hint = c.Key }).ToList();
            },
            pick: (it, _) => { if (it != null) Dispatcher.BeginInvoke(() => Cmd_(it.Id)); },
            onQuery: q => { if (!q.StartsWith('>')) { QuickOpen(q); return true; } return false; });
    }

    void GoToLine(string initial = ":")
    {
        var ed = active?.Code;
        if (ed == null) { Notify("Open a file first."); return; }
        ShowPalette("Type a line number to go to", initial: initial, provider: q =>
        {
            var m = Regex.Match(q.TrimStart(':'), @"^(\d+)(?:[:,](\d+))?");
            var lines = ed.Editor.Document.LineCount;
            if (!m.Success) return new() { new PickItem { Id = "x", Label = $"Current line: {ed.Editor.TextArea.Caret.Line}, column {ed.Editor.TextArea.Caret.Column}. Type a line number between 1 and {lines}." } };
            return new() { new PickItem { Id = m.Groups[1].Value + ":" + (m.Groups[2].Success ? m.Groups[2].Value : "1"), Label = $"Go to line {m.Groups[1].Value}" + (m.Groups[2].Success ? $", column {m.Groups[2].Value}" : "") } };
        }, pick: (it, _) =>
        {
            if (it == null || it.Id == "x") return;
            var p = it.Id.Split(':');
            ed.GoTo(int.Parse(p[0]), int.Parse(p[1]));
        });
    }

    void PickTheme()
    {
        var orig = ThemeManager.Current.Id;
        ShowPalette("Select Color Theme (Up/Down keys to preview)",
            ThemeManager.All.Select(t => new PickItem { Id = t.Id, Label = t.Name, Description = t.Dark ? "dark" : "light", Hint = t.Id == orig ? "current" : null, Icon = "◐" }).ToList(),
            highlight: it => { if (it != null && it.Id != ThemeManager.Current.Id) SetTheme(it.Id, false); },
            pick: (it, _) => SetTheme(it?.Id ?? orig),
            cancel: () => SetTheme(orig, false));
        var idx = Array.FindIndex(ThemeManager.All, t => t.Id == orig);
        PaletteList.SelectedIndex = idx;
    }

    void PickLanguage()
    {
        if (active?.Code == null) return;
        var t = active;
        ShowPalette("Select Language Mode", Languages.All.Select(l => new PickItem { Id = l.Id, Label = l.Name, Hint = l.Id == t.Lang ? "current" : null }).ToList(),
            pick: (it, _) =>
            {
                if (it == null) return;
                t.Lang = it.Id;
                t.Code!.SetLanguage(it.Id, Settings.TabSize);
                t.Code.ApplySettings(Settings);
                t.Code.ApplyTheme();
                RenderTabs();
                UpdateStatus();
            });
    }

    void PickIndent()
    {
        ShowPalette("Indent using spaces — select tab size", new[] { 2, 3, 4, 8 }.Select(n => new PickItem { Id = n.ToString(), Label = $"{n} spaces", Hint = n == Settings.TabSize ? "current" : null }).ToList(),
            pick: (it, _) => { if (it != null) { Settings.TabSize = int.Parse(it.Id); ApplySettingsAll(); } });
    }

    // ====================================================================== special pages

    FrameworkElement BuildWatermark()
    {
        var g = new Grid();
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(new System.Windows.Controls.Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/logo.png")), Width = 120, Height = 120, Opacity = 0.12, Margin = new Thickness(0, 0, 0, 20) });
        foreach (var (a, k) in new[] { ("Show All Commands", "Ctrl+Shift+P"), ("Go to File", "Ctrl+P"), ("Run File", "F5"), ("Format Document", "Shift+Alt+F"), ("Toggle Terminal", "Ctrl+`") })
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3), Width = 320 };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var l = new TextBlock { Text = a, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 12, 0) };
            l.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            row.Children.Add(l);
            var r = Pages.Keys(k);
            Grid.SetColumn(r, 1);
            row.Children.Add(r);
            sp.Children.Add(row);
        }
        g.Children.Add(sp);
        return g;
    }

    FrameworkElement BuildWelcome() => Pages.Welcome(this, Settings.Recent,
        newFile: NewUntitled, openFile: () => OpenFileDialog(), openFolder: OpenFolder, clone: ShowClone, theme: PickTheme);

    FrameworkElement BuildSettings() => Pages.SettingsPage(Settings, id => SetTheme(id), ApplySettingsAll);

    FrameworkElement BuildShortcuts() => Pages.Shortcuts(commands.Where(c => c.Key != null).Select(c => (c.Title, c.Key!)).ToList());

    // ====================================================================== closing

    void OnClosing(object? sender, CancelEventArgs e)
    {
        var dirty = tabs.Where(t => t.Dirty).ToList();
        if (dirty.Count > 0)
        {
            var r = MessageBox.Show(this, $"Do you want to save the changes to {dirty.Count} file{(dirty.Count == 1 ? "" : "s")}?\n\n{string.Join("\n", dirty.Select(d => d.Title))}", "Valco Code", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (r == MessageBoxResult.Cancel) { e.Cancel = true; return; }
            if (r == MessageBoxResult.Yes)
            {
                foreach (var t in dirty)
                {
                    if (t.Path == null) { e.Cancel = true; Activate(t); _ = Save(t); return; }
                    File.WriteAllText(t.Path, t.Code!.Editor.Text, new UTF8Encoding(false));
                }
            }
        }
        Settings.WinMax = WindowState == WindowState.Maximized;
        var b = RestoreBounds;
        if (!b.IsEmpty) { Settings.WinLeft = b.Left; Settings.WinTop = b.Top; Settings.WinWidth = b.Width; Settings.WinHeight = b.Height; }
        SaveSession();
        terminal.KillAll();
    }
}

/// <summary>Shows a git diff with added/removed lines coloured.</summary>
public class DiffViewer : DockPanel
{
    readonly TextEditor ed = new() { IsReadOnly = true, ShowLineNumbers = false };
    public DiffViewer(string diff, Action openFile, AppSettings s)
    {
        var bar = new DockPanel { Margin = new Thickness(10, 4, 10, 4) };
        var open = new Button { Content = "Open File", Padding = new Thickness(10, 2, 10, 2) };
        open.Click += (_, _) => openFile();
        DockPanel.SetDock(open, Dock.Right);
        bar.Children.Add(open);
        var hint = new TextBlock { Text = "Green lines were added, red lines were removed.", VerticalAlignment = VerticalAlignment.Center };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        bar.Children.Add(hint);
        SetDock(bar, Dock.Top);
        Children.Add(bar);
        ed.Text = diff;
        ed.FontFamily = new FontFamily(s.FontFamily);
        ed.FontSize = s.FontSize;
        ed.Padding = new Thickness(8, 0, 0, 0);
        ed.TextArea.TextView.LineTransformers.Add(new DiffColorizer());
        Children.Add(ed);
        ApplyTheme();
    }

    public void ApplyTheme()
    {
        var b = ThemeManager.Current.Brushes;
        ed.Background = ThemeManager.MakeBrush(b["EditorBg"]);
        ed.Foreground = ThemeManager.MakeBrush(b["EditorFg"]);
        ed.TextArea.TextView.Redraw();
    }

    class DiffColorizer : DocumentColorizingTransformer
    {
        protected override void ColorizeLine(DocumentLine line)
        {
            var text = CurrentContext.Document.GetText(line);
            Brush? bg = null, fg = null;
            if (text.StartsWith("+++") || text.StartsWith("---") || text.StartsWith("diff ") || text.StartsWith("index ") || text.StartsWith("new file")) fg = Brushes.Gray;
            else if (text.StartsWith("+")) bg = new SolidColorBrush(Color.FromArgb(60, 0x2e, 0xa0, 0x43));
            else if (text.StartsWith("-")) bg = new SolidColorBrush(Color.FromArgb(60, 0xf8, 0x51, 0x49));
            else if (text.StartsWith("@@")) fg = new SolidColorBrush(Color.FromRgb(0x4f, 0x9c, 0xd9));
            if (bg == null && fg == null) return;
            ChangeLinePart(line.Offset, line.EndOffset, el =>
            {
                if (bg != null) el.BackgroundBrush = bg;
                if (fg != null) el.TextRunProperties.SetForegroundBrush(fg);
            });
        }
    }
}
