using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ValcoCode.Services;

namespace ValcoCode.Views;

public interface IWorkbench
{
    string? Root { get; }
    AppSettings Settings { get; }
    Tools Tools { get; }
    GitStatus? GitState { get; }
    GitHubUser? User { get; }
    Task OpenFile(string path, int line = 0, int col = 1, int len = 0);
    void Notify(string msg, bool error = false, params (string Label, Action Run)[] actions);
    Task RefreshGit();
    Task GitOp(string label, Func<Task> op);
    Task OpenDiff(GitRow row);
    Task RunFile(string? path = null);
    void OpenTerminalAt(string dir);
    void OnRenamed(string from, string to);
    void OnDeleted(string path);
    void ShowSignIn();
    void ShowPublish();
    void ShowClone();
    Task PickBranch();
    Task SaveAll();
    bool Confirm(string msg, string title = "Valco Code");
    string? Prompt(string title, string label, string initial = "");
    void OpenFolder(string? path = null);
    void CloseFolder();
    Task<int> ReplaceInFile(string path, Regex re, string replacement);
}

static class UI
{
    public static TextBlock Head(string text) => new() { Text = text, Style = (Style)Application.Current.Resources["SideHead"] };

    public static Button Icon(string glyph, string tip, RoutedEventHandler click)
    {
        var b = new Button { Content = glyph, ToolTip = tip, Style = (Style)Application.Current.Resources["IconButton"] };
        b.Click += click;
        return b;
    }

    public static Button Btn(string text, RoutedEventHandler click, bool primary = false)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10, 5, 10, 5) };
        if (primary) b.Style = (Style)Application.Current.Resources["PrimaryButton"];
        b.Click += click;
        return b;
    }

    public static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4), Style = (Style)Application.Current.Resources["Muted"] };

    public static DockPanel Header(string title, params UIElement[] buttons)
    {
        var d = new DockPanel { Height = 35, LastChildFill = true };
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 6, 0) };
        foreach (var b in buttons) sp.Children.Add(b);
        DockPanel.SetDock(sp, Dock.Right);
        d.Children.Add(sp);
        d.Children.Add(Head(title));
        return d;
    }
}

// ====================================================================== Explorer

public class ExplorerView : DockPanel
{
    readonly IWorkbench wb;
    readonly TreeView tree = new();
    readonly ObservableCollection<FileNode> roots = new();
    FileSystemWatcher? watcher;
    readonly DispatcherTimer reloadTimer;
    readonly TextBlock title = UI.Head("EXPLORER");
    readonly StackPanel empty = new() { Margin = new Thickness(16, 8, 16, 8) };
    Dictionary<string, string> decorations = new(StringComparer.OrdinalIgnoreCase);

    public ExplorerView(IWorkbench wb)
    {
        this.wb = wb;
        var header = UI.Header("", UI.Icon("", "New File…", (_, _) => NewItem(false)), UI.Icon("", "New Folder…", (_, _) => NewItem(true)),
            UI.Icon("", "Refresh Explorer", (_, _) => _ = ReloadAll()), UI.Icon("", "Collapse Folders", (_, _) => CollapseAll()));
        header.Children.RemoveAt(1);
        header.Children.Add(title);
        SetDock(header, Dock.Top);
        Children.Add(header);
        SetDock(empty, Dock.Top);
        Children.Add(empty);
        tree.ItemTemplate = (DataTemplate)Application.Current.Resources["FileNodeTemplate"];
        tree.ItemsSource = roots;
        tree.MouseLeftButtonUp += (_, e) =>
        {
            if (tree.SelectedItem is FileNode n && !n.IsDir && e.OriginalSource is DependencyObject d && ItemFrom(d) != null) _ = wb.OpenFile(n.FullPath);
        };
        tree.KeyDown += OnKey;
        tree.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && ItemFrom(d) is TreeViewItem tvi) { tvi.IsSelected = true; tvi.Focus(); }
        };
        tree.ContextMenuOpening += (_, e) => { tree.ContextMenu = BuildMenu(tree.SelectedItem as FileNode); };
        tree.ContextMenu = new ContextMenu();
        tree.AllowDrop = true;
        tree.PreviewMouseMove += DragStart;
        tree.Drop += OnDrop;
        Children.Add(tree);
        reloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        reloadTimer.Tick += (_, _) => { reloadTimer.Stop(); _ = ReloadAll(); };
        SetRoot(wb.Root);
    }

    static TreeViewItem? ItemFrom(DependencyObject d)
    {
        while (d != null && d is not TreeViewItem) d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
        return d as TreeViewItem;
    }

    public void SetRoot(string? root)
    {
        watcher?.Dispose();
        watcher = null;
        roots.Clear();
        empty.Children.Clear();
        if (root == null)
        {
            title.Text = "EXPLORER";
            empty.Children.Add(UI.Note("You have not yet opened a folder."));
            empty.Children.Add(UI.Btn("Open Folder", (_, _) => wb.OpenFolder(), true));
            empty.Children.Add(UI.Btn("Clone Repository", (_, _) => wb.ShowClone()));
            empty.Visibility = Visibility.Visible;
            return;
        }
        empty.Visibility = Visibility.Collapsed;
        title.Text = Path.GetFileName(root.TrimEnd('\\')).ToUpperInvariant();
        var rootNode = new FileNode(Path.GetFileName(root.TrimEnd('\\')), root, true, 0, null) { Loader = Load };
        // show the folder's contents directly (VS Code shows the folder name as the section title)
        _ = LoadInto(rootNode, roots, 0);
        try
        {
            watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName };
            FileSystemEventHandler h = (_, e) =>
            {
                if (e.FullPath.Contains(@"\.git\") || e.FullPath.EndsWith(@"\.git")) return;
                Dispatcher.BeginInvoke(() => { reloadTimer.Stop(); reloadTimer.Start(); });
            };
            watcher.Created += h;
            watcher.Deleted += h;
            watcher.Renamed += (s, e) => h(s, e);
            watcher.EnableRaisingEvents = true;
        }
        catch { }
    }

    static List<(string Name, string Path, bool Dir)> List(string dir)
    {
        var res = new List<(string, string, bool)>();
        try
        {
            foreach (var d in Directory.GetDirectories(dir))
            {
                var n = Path.GetFileName(d);
                if (n == ".git") continue;
                res.Add((n, d, true));
            }
            var files = Directory.GetFiles(dir).Select(f => (Path.GetFileName(f), f, false)).ToList();
            res.Sort((a, b) => string.Compare(a.Item1, b.Item1, StringComparison.OrdinalIgnoreCase));
            files.Sort((a, b) => string.Compare(a.Item1, b.Item1, StringComparison.OrdinalIgnoreCase));
            res.AddRange(files);
        }
        catch { }
        return res;
    }

    async Task Load(FileNode node) => await LoadInto(node, node.Children, node.Depth + 1, node);

    async Task LoadInto(FileNode node, ObservableCollection<FileNode> target, int depth, FileNode? parent = null)
    {
        var items = await Task.Run(() => List(node.FullPath));
        // merge so expanded folders stay expanded
        var existing = target.Where(n => n != FileNode.Placeholder).ToDictionary(n => n.FullPath, StringComparer.OrdinalIgnoreCase);
        target.Remove(FileNode.Placeholder);
        var wanted = new List<FileNode>();
        foreach (var (name, path, dir) in items)
        {
            if (!existing.TryGetValue(path, out var n)) n = new FileNode(name, path, dir, depth, parent) { Loader = Load };
            wanted.Add(n);
        }
        for (int i = target.Count - 1; i >= 0; i--) if (!wanted.Contains(target[i])) target.RemoveAt(i);
        for (int i = 0; i < wanted.Count; i++)
        {
            var cur = target.IndexOf(wanted[i]);
            if (cur == i) continue;
            if (cur >= 0) target.Move(cur, i);
            else target.Insert(i, wanted[i]);
        }
        ApplyDecorations(target);
    }

    public async Task ReloadAll()
    {
        if (wb.Root == null) return;
        var rootNode = new FileNode("", wb.Root, true, -1, null);
        await LoadInto(rootNode, roots, 0);
        foreach (var n in Flatten(roots).Where(n => n.IsDir && n.IsExpanded && n.Loaded).ToList()) await Load(n);
    }

    static IEnumerable<FileNode> Flatten(IEnumerable<FileNode> nodes)
    {
        foreach (var n in nodes)
        {
            if (n == FileNode.Placeholder) continue;
            yield return n;
            foreach (var c in Flatten(n.Children)) yield return c;
        }
    }

    void CollapseAll()
    {
        foreach (var n in Flatten(roots)) n.IsExpanded = false;
    }

    public void SetDecorations(Dictionary<string, string> map)
    {
        decorations = map;
        ApplyDecorations(roots);
    }

    void ApplyDecorations(IEnumerable<FileNode> nodes)
    {
        foreach (var n in Flatten(nodes))
        {
            if (n.IsDir)
            {
                var prefix = n.FullPath.TrimEnd('\\') + "\\";
                string? found = null;
                foreach (var (k, v) in decorations)
                    if (k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { found = v is "U" or "A" ? "dot-new" : "dot"; if (v == "!" ) break; }
                n.Badge = found;
            }
            else n.Badge = decorations.TryGetValue(n.FullPath, out var l) ? l : null;
            n.RefreshTheme();
        }
    }

    /// <summary>Selects and reveals a file in the tree.</summary>
    public async Task Reveal(string path)
    {
        if (wb.Root == null || !path.StartsWith(wb.Root, StringComparison.OrdinalIgnoreCase)) return;
        var rel = path[wb.Root.TrimEnd('\\').Length..].Trim('\\').Split('\\');
        IEnumerable<FileNode> level = roots;
        FileNode? node = null;
        var cur = wb.Root.TrimEnd('\\');
        foreach (var part in rel)
        {
            cur += "\\" + part;
            node = level.FirstOrDefault(n => n.FullPath.Equals(cur, StringComparison.OrdinalIgnoreCase));
            if (node == null) return;
            if (node.IsDir && cur != path)
            {
                if (!node.Loaded) { node.Loaded = true; await Load(node); }
                node.IsExpanded = true;
                level = node.Children;
            }
        }
        if (node == null) return;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        var tvi = FindContainer(tree, node);
        if (tvi != null) { tvi.IsSelected = true; tvi.BringIntoView(); }
    }

    static TreeViewItem? FindContainer(ItemsControl parent, object item)
    {
        if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem direct) return direct;
        foreach (var child in parent.Items)
            if (parent.ItemContainerGenerator.ContainerFromItem(child) is TreeViewItem tvi && tvi.IsExpanded)
            {
                var r = FindContainer(tvi, item);
                if (r != null) return r;
            }
        return null;
    }

    string TargetDir()
    {
        if (tree.SelectedItem is FileNode n) return n.IsDir ? n.FullPath : Path.GetDirectoryName(n.FullPath)!;
        return wb.Root!;
    }

    void NewItem(bool folder, string? dir = null)
    {
        if (wb.Root == null) return;
        dir ??= TargetDir();
        var name = wb.Prompt(folder ? "New Folder" : "New File", folder ? "Folder name:" : "File name (you can include folders, like src/Main.java):");
        if (string.IsNullOrWhiteSpace(name)) return;
        var target = Path.Combine(dir, name.Trim().Replace('/', '\\'));
        try
        {
            if (File.Exists(target) || Directory.Exists(target)) { wb.Notify("A file or folder with that name already exists.", true); return; }
            if (folder) Directory.CreateDirectory(target);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, Starter(target));
            }
            _ = ReloadAll().ContinueWith(_ => Dispatcher.BeginInvoke(async () => await Reveal(target)));
            if (!folder) _ = wb.OpenFile(target);
        }
        catch (Exception e) { wb.Notify(e.Message, true); }
    }

    /// <summary>A tiny bit of starter code for new Java/Python files.</summary>
    static string Starter(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var cls = Path.GetFileNameWithoutExtension(path);
        if (ext == ".java" && Regex.IsMatch(cls, @"^[A-Za-z_$][\w$]*$"))
            return $"public class {cls} {{\n    public static void main(String[] args) {{\n        \n    }}\n}}\n";
        return "";
    }

    void Rename(FileNode n)
    {
        var name = wb.Prompt("Rename", "New name:", n.Name);
        if (string.IsNullOrWhiteSpace(name) || name == n.Name) return;
        var to = Path.Combine(Path.GetDirectoryName(n.FullPath)!, name.Trim());
        try
        {
            if (n.IsDir) Directory.Move(n.FullPath, to); else File.Move(n.FullPath, to);
            wb.OnRenamed(n.FullPath, to);
            _ = ReloadAll();
        }
        catch (Exception e) { wb.Notify(e.Message, true); }
    }

    void Delete(FileNode n)
    {
        if (!wb.Confirm($"Move \"{n.Name}\" to the Recycle Bin?", "Delete")) return;
        if (!Shell.Recycle(n.FullPath)) { wb.Notify($"Couldn't delete {n.Name}.", true); return; }
        wb.OnDeleted(n.FullPath);
        _ = ReloadAll();
    }

    ContextMenu BuildMenu(FileNode? n)
    {
        var m = new ContextMenu();
        MenuItem Item(string h, Action a, string? key = null) { var i = new MenuItem { Header = h, InputGestureText = key ?? "" }; i.Click += (_, _) => a(); return i; }
        var dir = n == null ? wb.Root! : n.IsDir ? n.FullPath : Path.GetDirectoryName(n.FullPath)!;
        m.Items.Add(Item("New File…", () => NewItem(false, dir)));
        m.Items.Add(Item("New Folder…", () => NewItem(true, dir)));
        m.Items.Add(new Separator());
        if (n != null && !n.IsDir && (n.Name.EndsWith(".py", StringComparison.OrdinalIgnoreCase) || n.Name.EndsWith(".java", StringComparison.OrdinalIgnoreCase)))
        {
            m.Items.Add(Item("Run File", () => _ = wb.RunFile(n.FullPath), "F5"));
            m.Items.Add(new Separator());
        }
        m.Items.Add(Item("Reveal in File Explorer", () => Shell.Reveal(n?.FullPath ?? dir), "Shift+Alt+R"));
        m.Items.Add(Item("Open in Integrated Terminal", () => wb.OpenTerminalAt(dir)));
        if (n != null)
        {
            m.Items.Add(new Separator());
            m.Items.Add(Item("Copy Path", () => Clipboard.SetText(n.FullPath), "Shift+Alt+C"));
            m.Items.Add(Item("Copy Relative Path", () => Clipboard.SetText(Path.GetRelativePath(wb.Root!, n.FullPath))));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Rename…", () => Rename(n), "F2"));
            m.Items.Add(Item("Delete", () => Delete(n), "Delete"));
        }
        return m;
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (tree.SelectedItem is not FileNode n) return;
        if (e.Key == Key.F2) { Rename(n); e.Handled = true; }
        else if (e.Key == Key.Delete) { Delete(n); e.Handled = true; }
        else if (e.Key == Key.Enter) { if (n.IsDir) n.IsExpanded = !n.IsExpanded; else _ = wb.OpenFile(n.FullPath); e.Handled = true; }
    }

    Point dragStart;
    void DragStart(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { dragStart = e.GetPosition(tree); return; }
        var p = e.GetPosition(tree);
        if (Math.Abs(p.X - dragStart.X) < 6 && Math.Abs(p.Y - dragStart.Y) < 6) return;
        if (tree.SelectedItem is FileNode n && e.OriginalSource is DependencyObject d && ItemFrom(d) != null)
            DragDrop.DoDragDrop(tree, new DataObject("valcocode/path", n.FullPath), DragDropEffects.Move);
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData("valcocode/path") is not string from || wb.Root == null) return;
        var targetItem = e.OriginalSource is DependencyObject d ? ItemFrom(d) : null;
        var target = targetItem?.DataContext is FileNode t ? (t.IsDir ? t.FullPath : Path.GetDirectoryName(t.FullPath)!) : wb.Root;
        var to = Path.Combine(target, Path.GetFileName(from));
        if (to.Equals(from, StringComparison.OrdinalIgnoreCase) || target.StartsWith(from + "\\", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            if (Directory.Exists(from)) Directory.Move(from, to); else File.Move(from, to);
            wb.OnRenamed(from, to);
            _ = ReloadAll();
        }
        catch (Exception ex) { wb.Notify(ex.Message, true); }
    }
}

// ====================================================================== Search

public class SearchView : DockPanel
{
    readonly IWorkbench wb;
    readonly TextBox query = new() { Tag = "Search" };
    readonly TextBox replace = new() { Tag = "Replace", Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
    readonly ToggleButton cs = new() { Content = "Aa", ToolTip = "Match Case", Width = 24 };
    readonly ToggleButton ww = new() { Content = "ab", ToolTip = "Match Whole Word", Width = 24 };
    readonly ToggleButton rx = new() { Content = ".*", ToolTip = "Use Regular Expression", Width = 24 };
    readonly TextBlock summary = new() { Margin = new Thickness(12, 2, 12, 4), FontSize = 11.5 };
    readonly TreeView results = new();
    readonly ObservableCollection<SearchNode> nodes = new();
    readonly DispatcherTimer timer;
    CancellationTokenSource? cts;
    Button replaceAllBtn = null!;

    static readonly string[] Skip = { ".git", "node_modules", "target", "__pycache__", ".venv", "venv", "build", "out", "bin", "obj", ".idea", ".gradle", "dist" };

    public SearchView(IWorkbench wb)
    {
        this.wb = wb;
        var header = UI.Header("SEARCH", UI.Icon("", "Refresh", (_, _) => Run()), UI.Icon("", "Clear Search Results", (_, _) => query.Text = ""));
        SetDock(header, Dock.Top);
        Children.Add(header);

        var box = new Grid { Margin = new Thickness(4, 0, 10, 4) };
        box.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        box.ColumnDefinitions.Add(new ColumnDefinition());
        var twist = UI.Icon("", "Toggle Replace", (s, _) =>
        {
            var show = replace.Visibility != Visibility.Visible;
            replace.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            replaceAllBtn.Visibility = replace.Visibility;
            ((Button)s).Content = show ? "" : "";
        });
        twist.VerticalAlignment = VerticalAlignment.Top;
        twist.FontSize = 10;
        box.Children.Add(twist);
        var stack = new StackPanel();
        Grid.SetColumn(stack, 1);
        var qrow = new DockPanel();
        var opts = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 0) };
        foreach (var t in new[] { cs, ww, rx }) { t.Style = (Style)Application.Current.Resources["OptToggle"]; t.Height = 24; opts.Children.Add(t); t.Click += (_, _) => Run(); }
        DockPanel.SetDock(opts, Dock.Right);
        qrow.Children.Add(opts);
        qrow.Children.Add(query);
        stack.Children.Add(qrow);
        var rrow = new DockPanel();
        replaceAllBtn = UI.Icon("", "Replace All", (_, _) => _ = ReplaceAll());
        replaceAllBtn.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(replaceAllBtn, Dock.Right);
        rrow.Children.Add(replaceAllBtn);
        rrow.Children.Add(replace);
        stack.Children.Add(rrow);
        box.Children.Add(stack);
        SetDock(box, Dock.Top);
        Children.Add(box);
        summary.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        SetDock(summary, Dock.Top);
        Children.Add(summary);
        results.ItemTemplate = (DataTemplate)Application.Current.Resources["SearchNodeTemplate"];
        results.ItemsSource = nodes;
        results.MouseLeftButtonUp += (_, _) =>
        {
            if (results.SelectedItem is SearchNode { IsFile: false } h) _ = wb.OpenFile(h.Path, h.Line, h.Col, h.Len);
        };
        Children.Add(results);
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) => { timer.Stop(); Run(); };
        query.TextChanged += (_, _) => { timer.Stop(); timer.Start(); };
        query.KeyDown += (_, e) => { if (e.Key == Key.Enter) Run(); };
    }

    public void FocusQuery(string? initial)
    {
        if (!string.IsNullOrEmpty(initial)) query.Text = initial;
        query.Focus();
        query.SelectAll();
    }

    Regex? Pattern()
    {
        if (query.Text.Length == 0) return null;
        var p = rx.IsChecked == true ? query.Text : Regex.Escape(query.Text);
        if (ww.IsChecked == true) p = @"\b" + p + @"\b";
        try { return new Regex(p, cs.IsChecked == true ? RegexOptions.None : RegexOptions.IgnoreCase); } catch { return null; }
    }

    async void Run()
    {
        cts?.Cancel();
        nodes.Clear();
        var root = wb.Root;
        var re = Pattern();
        if (root == null) { summary.Text = "Open a folder to search its files."; return; }
        if (re == null) { summary.Text = query.Text.Length > 0 ? "That isn't a valid regular expression." : ""; return; }
        var my = cts = new CancellationTokenSource();
        summary.Text = "Searching…";
        var found = await Task.Run(() =>
        {
            var res = new List<(string File, List<(int Line, int Col, int Len, string Text)> Hits)>();
            var total = 0;
            foreach (var f in Walk(root))
            {
                if (my.IsCancellationRequested || total > 5000) break;
                try
                {
                    var fi = new FileInfo(f);
                    if (fi.Length > 2_000_000) continue;
                    var bytes = File.ReadAllBytes(f);
                    if (bytes.Take(4000).Contains((byte)0)) continue;
                    var text = System.Text.Encoding.UTF8.GetString(bytes);
                    var hits = new List<(int, int, int, string)>();
                    var lines = text.Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var m = re.Match(lines[i]);
                        if (m.Success && m.Length > 0) { hits.Add((i + 1, m.Index + 1, m.Length, lines[i].TrimEnd('\r'))); total++; }
                    }
                    if (hits.Count > 0) res.Add((f, hits));
                }
                catch { }
            }
            return res;
        });
        if (my.IsCancellationRequested) return;
        var count = 0;
        foreach (var (file, hits) in found)
        {
            var fn = new SearchNode { IsFile = true, Path = file, Before = Path.GetFileName(file), Dir = "  " + Path.GetRelativePath(root, Path.GetDirectoryName(file)!).Replace(".", "") };
            foreach (var (line, col, len, text) in hits)
            {
                var start = col - 1;
                var lead = Math.Max(0, start - 30);
                fn.Children.Add(new SearchNode
                {
                    Path = file, Line = line, Col = col, Len = len,
                    Before = (lead > 0 ? "…" : "") + text[lead..start].TrimStart(),
                    Match = text.Substring(start, Math.Min(len, text.Length - start)),
                    After = text[Math.Min(text.Length, start + len)..],
                });
                count++;
            }
            nodes.Add(fn);
        }
        summary.Text = count == 0 ? "No results found." : $"{count} result{(count == 1 ? "" : "s")} in {found.Count} file{(found.Count == 1 ? "" : "s")}";
    }

    static IEnumerable<string> Walk(string dir, int depth = 0)
    {
        if (depth > 25) yield break;
        string[] files = Array.Empty<string>(), dirs = Array.Empty<string>();
        try { files = Directory.GetFiles(dir); dirs = Directory.GetDirectories(dir); } catch { }
        foreach (var f in files) yield return f;
        foreach (var d in dirs)
            if (!Skip.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
                foreach (var f in Walk(d, depth + 1)) yield return f;
    }

    async Task ReplaceAll()
    {
        var re = Pattern();
        if (re == null || nodes.Count == 0) return;
        var total = nodes.Sum(n => n.Children.Count);
        if (!wb.Confirm($"Replace {total} match{(total == 1 ? "" : "es")} of \"{query.Text}\" with \"{replace.Text}\" in {nodes.Count} file{(nodes.Count == 1 ? "" : "s")}?", "Replace All")) return;
        var rep = rx.IsChecked == true ? replace.Text : replace.Text.Replace("$", "$$");
        foreach (var n in nodes.ToList()) await wb.ReplaceInFile(n.Path, re, rep);
        Run();
    }
}

// ====================================================================== Source Control

public class ScmView : DockPanel
{
    readonly IWorkbench wb;
    readonly TextBox message = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 28, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top };
    readonly StackPanel body = new() { Margin = new Thickness(10, 0, 10, 8) };
    readonly ListBox staged = new(), changes = new(), conflicts = new(), history = new();
    readonly ToggleButton changesTab = new() { Content = "Changes" }, historyTab = new() { Content = "History" };
    readonly Button branchBtn = new();
    readonly ScrollViewer scroller = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    public ScmView(IWorkbench wb)
    {
        this.wb = wb;
        var header = UI.Header("SOURCE CONTROL",
            UI.Icon("", "Commit (Ctrl+Enter)", (_, _) => _ = Commit(null)),
            UI.Icon("", "Refresh", (_, _) => _ = wb.RefreshGit()),
            UI.Icon("", "More Actions…", (s, _) => ShowMore((FrameworkElement)s)));
        SetDock(header, Dock.Top);
        Children.Add(header);
        scroller.Content = body;
        Children.Add(scroller);
        foreach (var l in new[] { staged, changes, conflicts })
        {
            l.ItemTemplate = (DataTemplate)Application.Current.Resources["GitRowTemplate"];
            ScrollViewer.SetVerticalScrollBarVisibility(l, ScrollBarVisibility.Disabled);
            l.PreviewMouseWheel += (s, e) => { e.Handled = true; scroller.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent }); };
            l.AddHandler(Button.ClickEvent, new RoutedEventHandler(OnRowButton));
            l.MouseLeftButtonUp += (s, e) =>
            {
                if (e.OriginalSource is DependencyObject d && FindParent<Button>(d) != null) return;
                if (((ListBox)s!).SelectedItem is GitRow r) _ = wb.OpenDiff(r);
            };
            l.ContextMenuOpening += (s, e) => { ((ListBox)s!).ContextMenu = RowMenu(((ListBox)s!).SelectedItem as GitRow); };
            l.ContextMenu = new ContextMenu();
        }
        history.ItemTemplate = (DataTemplate)Application.Current.Resources["CommitTemplate"];
        message.Tag = "Message (Ctrl+Enter to commit)";
        message.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { e.Handled = true; _ = Commit(null); }
        };
        changesTab.Style = historyTab.Style = (Style)Application.Current.Resources["TabToggle"];
        changesTab.IsChecked = true;
        changesTab.Click += (_, _) => { changesTab.IsChecked = true; historyTab.IsChecked = false; Render(); };
        historyTab.Click += (_, _) => { historyTab.IsChecked = true; changesTab.IsChecked = false; Render(); };
        branchBtn.Style = (Style)Application.Current.Resources["IconButton"];
        branchBtn.FontFamily = new FontFamily("Segoe UI");
        branchBtn.FontSize = 12;
        branchBtn.ToolTip = "Checkout branch…";
        branchBtn.Click += (_, _) => _ = wb.PickBranch();
        Render();
    }

    static T? FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        while (d != null) { if (d is T t) return t; d = VisualTreeHelper.GetParent(d); }
        return null;
    }

    public void FocusMessage() => message.Focus();

    static void Detach(FrameworkElement e)
    {
        switch (e.Parent)
        {
            case Panel p: p.Children.Remove(e); break;
            case Decorator d: d.Child = null; break;
            case ContentControl c: c.Content = null; break;
            case ItemsControl i: i.Items.Remove(e); break;
        }
    }

    public void Render()
    {
        body.Children.Clear();
        foreach (var e in new FrameworkElement[] { branchBtn, changesTab, historyTab, message, staged, changes, conflicts, history })
            Detach(e);
        var st = wb.GitState;
        if (wb.Root == null)
        {
            body.Children.Add(UI.Note("Open a folder that has a Git repository, or clone one from GitHub."));
            body.Children.Add(UI.Btn("Clone Repository", (_, _) => wb.ShowClone(), true));
            return;
        }
        if (wb.Tools.Git == null)
        {
            body.Children.Add(UI.Note("Git isn't installed on this computer. Install it from git-scm.com, then restart Valco Code."));
            body.Children.Add(UI.Btn("Download Git", (_, _) => Shell.Open("https://git-scm.com/download/win"), true));
            return;
        }
        if (st == null || !st.IsRepo)
        {
            body.Children.Add(UI.Note("This folder isn't a Git repository yet."));
            body.Children.Add(UI.Btn("Initialize Repository", (_, _) => _ = wb.GitOp("Initializing…", async () =>
            {
                var r = await Git.Raw(wb.Root!, new[] { "init", "-b", "main" });
                if (!r.Ok) await Git.Run(wb.Root!, "init");
            }), true));
            body.Children.Add(UI.Btn("Publish to GitHub", (_, _) => wb.ShowPublish()));
            body.Children.Add(UI.Note("Publishing creates a repository on your GitHub account, commits this folder and pushes it."));
            return;
        }
        var tabs = new DockPanel { Margin = new Thickness(-6, 0, 0, 6) };
        branchBtn.Content = "⑂ " + st.Branch;
        DockPanel.SetDock(branchBtn, Dock.Right);
        tabs.Children.Add(branchBtn);
        tabs.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { changesTab, historyTab } });
        body.Children.Add(tabs);

        if (historyTab.IsChecked == true)
        {
            history.ItemsSource = null;
            body.Children.Add(history);
            if (!st.HasCommits) body.Children.Add(UI.Note("No commits yet."));
            else _ = LoadHistory(st.Top);
            return;
        }

        body.Children.Add(message);
        var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var commit = UI.Btn("✓ Commit", (_, _) => _ = Commit(null), true);
        commit.Margin = new Thickness(0);
        var drop = UI.Btn("⌄", (s, ev) =>
        {
            var m = new ContextMenu { PlacementTarget = (UIElement)s, Placement = PlacementMode.Bottom };
            m.Items.Add(MI("Commit", () => _ = Commit(null)));
            m.Items.Add(MI("Commit & Push", () => _ = Commit("push")));
            m.Items.Add(MI("Commit & Sync", () => _ = Commit("sync")));
            m.IsOpen = true;
        }, true);
        drop.Margin = new Thickness(1, 0, 0, 0);
        drop.Padding = new Thickness(8, 5, 8, 5);
        Grid.SetColumn(drop, 1);
        row.Children.Add(commit);
        row.Children.Add(drop);
        body.Children.Add(row);

        if (st.Count == 0 && st.HasCommits && (st.Ahead > 0 || st.Behind > 0 || st.Upstream == null))
        {
            var label = st.RemoteUrl == null ? "☁ Publish to GitHub" : st.Upstream == null ? "☁ Publish Branch" : $"⇅ Sync Changes {(st.Behind > 0 ? st.Behind + "↓ " : "")}{(st.Ahead > 0 ? st.Ahead + "↑" : "")}";
            body.Children.Add(UI.Btn(label, (_, _) => _ = Sync()));
        }

        void Group(string title, List<GitFile> files, ListBox list, params UIElement[] actions)
        {
            if (files.Count == 0) return;
            var head = new DockPanel { Margin = new Thickness(0, 10, 0, 2) };
            var acts = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var a in actions) acts.Children.Add(a);
            acts.Children.Add(new Border { Style = (Style)Application.Current.Resources["CountBadge"], Margin = new Thickness(6, 0, 0, 0), Child = new TextBlock { Text = files.Count.ToString(), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Foreground = (Brush)Application.Current.Resources["BadgeFgBrush"] } });
            DockPanel.SetDock(acts, Dock.Right);
            head.Children.Add(acts);
            head.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            body.Children.Add(head);
            list.ItemsSource = files.Select(f => new GitRow { File = f, Top = st!.Top }).ToList();
            body.Children.Add(list);
        }
        Group("Merge Changes", st.Conflicts, conflicts);
        Group("Staged Changes", st.Staged, staged, UI.Icon("", "Unstage All Changes", (_, _) => _ = Unstage(st.Staged)));
        Group("Changes", st.Changes, changes,
            UI.Icon("", "Discard All Changes", (_, _) => _ = Discard(st.Changes)),
            UI.Icon("", "Stage All Changes", (_, _) => _ = Stage(st.Changes)));
        if (st.Count == 0) body.Children.Add(UI.Note("No changes. Everything is committed."));

        var card = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        card.Children.Add(new Border { Height = 1, Background = (Brush)Application.Current.Resources["LineBrush"], Margin = new Thickness(0, 0, 0, 8) });
        if (st.RemoteUrl != null)
        {
            var web = Git.WebUrl(st.RemoteUrl);
            var link = new Button { Content = "↗ " + (web?.Replace("https://github.com/", "") ?? st.RemoteUrl), Style = (Style)Application.Current.Resources["LinkButton"], HorizontalAlignment = HorizontalAlignment.Left };
            link.Click += (_, _) => { if (web != null) Shell.Open(web); };
            card.Children.Add(link);
        }
        else card.Children.Add(UI.Btn("☁ Publish to GitHub", (_, _) => wb.ShowPublish()));
        var acct = new Button { Content = wb.User != null ? $"GitHub: {wb.User.Login}" : "Sign in to GitHub", Style = (Style)Application.Current.Resources["LinkButton"], HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        acct.Click += (_, _) => wb.ShowSignIn();
        card.Children.Add(acct);
        body.Children.Add(card);
    }

    async Task LoadHistory(string top)
    {
        try { history.ItemsSource = (await Git.Log(top)).Select(c => new CommitRow(c)).ToList(); }
        catch (Exception e) { wb.Notify(e.Message, true); }
    }

    static MenuItem MI(string h, Action a) { var i = new MenuItem { Header = h }; i.Click += (_, _) => a(); return i; }

    void ShowMore(FrameworkElement anchor)
    {
        var st = wb.GitState;
        var m = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        m.Items.Add(MI("Pull", () => _ = wb.GitOp("Pulling…", Pull)));
        m.Items.Add(MI("Push", () => _ = wb.GitOp("Pushing…", Push)));
        m.Items.Add(MI("Fetch", () => _ = wb.GitOp("Fetching…", async () => await Git.Run(Top, true, "fetch", "--prune"))));
        m.Items.Add(new Separator());
        m.Items.Add(MI("Checkout to…", () => _ = wb.PickBranch()));
        m.Items.Add(new Separator());
        m.Items.Add(MI("Commit & Push", () => _ = Commit("push")));
        m.Items.Add(MI("Commit & Sync", () => _ = Commit("sync")));
        m.Items.Add(MI("Undo Last Commit", () => _ = wb.GitOp("Undoing last commit…", async () => await Git.Run(Top, "reset", "--soft", "HEAD~1"))));
        m.Items.Add(new Separator());
        if (st != null)
        {
            m.Items.Add(MI("Stage All Changes", () => _ = Stage(st.Changes)));
            m.Items.Add(MI("Unstage All Changes", () => _ = Unstage(st.Staged)));
            m.Items.Add(MI("Discard All Changes", () => _ = Discard(st.Changes)));
            m.Items.Add(new Separator());
        }
        m.Items.Add(MI("Publish to GitHub…", wb.ShowPublish));
        m.Items.Add(MI("Clone Repository…", wb.ShowClone));
        m.Items.Add(MI(wb.User != null ? $"GitHub: {wb.User.Login}…" : "Sign in to GitHub…", wb.ShowSignIn));
        m.IsOpen = true;
    }

    string Top => wb.GitState?.Top ?? wb.Root!;

    ContextMenu RowMenu(GitRow? r)
    {
        var m = new ContextMenu();
        if (r == null) return m;
        m.Items.Add(MI("Open Changes", () => _ = wb.OpenDiff(r)));
        if (r.Letter != "D") m.Items.Add(MI("Open File", () => _ = wb.OpenFile(r.FullPath)));
        m.Items.Add(new Separator());
        if (r.File.Staged) m.Items.Add(MI("Unstage Changes", () => _ = Unstage(new() { r.File })));
        else
        {
            m.Items.Add(MI("Stage Changes", () => _ = Stage(new() { r.File })));
            m.Items.Add(MI("Discard Changes", () => _ = Discard(new() { r.File })));
            m.Items.Add(new Separator());
            m.Items.Add(MI("Add to .gitignore", () => _ = AddIgnore(r.File.Path)));
        }
        m.Items.Add(MI("Reveal in File Explorer", () => Shell.Reveal(r.FullPath)));
        return m;
    }

    void OnRowButton(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button b || b.DataContext is not GitRow r) return;
        e.Handled = true;
        switch (b.Tag as string)
        {
            case "open": _ = wb.OpenFile(r.FullPath); break;
            case "discard": _ = Discard(new() { r.File }); break;
            case "stage": _ = r.File.Staged ? Unstage(new() { r.File }) : Stage(new() { r.File }); break;
        }
    }

    Task Stage(List<GitFile> files) => wb.GitOp("Staging…", async () =>
    {
        foreach (var chunk in files.Chunk(100)) await Git.Run(Top, new[] { "add", "-A", "--" }.Concat(chunk.Select(f => f.Path)).ToArray());
    });

    Task Unstage(List<GitFile> files) => wb.GitOp("Unstaging…", async () =>
    {
        foreach (var chunk in files.Chunk(100))
        {
            if (wb.GitState?.HasCommits == true) await Git.Run(Top, new[] { "reset", "-q", "HEAD", "--" }.Concat(chunk.Select(f => f.Path)).ToArray());
            else await Git.Run(Top, new[] { "rm", "--cached", "-r", "-q", "--" }.Concat(chunk.Select(f => f.Path)).ToArray());
        }
    });

    async Task Discard(List<GitFile> files)
    {
        if (files.Count == 0) return;
        var what = files.Count == 1 ? $"\"{files[0].Name}\"" : $"{files.Count} files";
        var untracked = files.Where(f => f.Work == '?').ToList();
        if (!wb.Confirm($"Discard changes in {what}?{(untracked.Count > 0 ? " New files will go to the Recycle Bin." : "")} This can't be undone.", "Discard Changes")) return;
        await wb.GitOp("Discarding…", async () =>
        {
            var tracked = files.Where(f => f.Work != '?').ToList();
            foreach (var chunk in tracked.Chunk(100)) await Git.Run(Top, new[] { "checkout", "--" }.Concat(chunk.Select(f => f.Path)).ToArray());
            foreach (var f in untracked) Shell.Recycle(Path.Combine(Top, f.Path.Replace('/', '\\')));
        });
    }

    async Task AddIgnore(string gitPath)
    {
        var gi = Path.Combine(Top, ".gitignore");
        var cur = File.Exists(gi) ? await File.ReadAllTextAsync(gi) : "";
        await File.WriteAllTextAsync(gi, cur + (cur.Length > 0 && !cur.EndsWith('\n') ? "\n" : "") + gitPath + "\n");
        await wb.RefreshGit();
    }

    async Task Pull() => await Git.Run(Top, true, "-c", "pull.rebase=false", "pull", "--no-edit");

    async Task Push()
    {
        var st = await Git.Status(Top);
        if (st.RemoteUrl == null) { wb.ShowPublish(); return; }
        if (st.Upstream == null) await Git.Run(Top, true, "push", "-u", "origin", st.Branch);
        else await Git.Run(Top, true, "push");
    }

    async Task Sync()
    {
        var st = wb.GitState;
        if (st == null) return;
        if (st.RemoteUrl == null) { wb.ShowPublish(); return; }
        await wb.GitOp(st.Upstream == null ? "Publishing branch…" : "Syncing…", async () =>
        {
            if (st.Upstream != null && st.Behind > 0) await Pull();
            await Push();
        });
    }

    async Task Commit(string? then)
    {
        var st = wb.GitState;
        if (st == null || !st.IsRepo) return;
        await wb.SaveAll();
        var msg = message.Text.Trim();
        if (msg.Length == 0)
        {
            wb.Notify("Type a commit message first (what did you change?).", true);
            message.Focus();
            return;
        }
        if (!await Git.EnsureIdentity(Top, wb.User))
        {
            var name = wb.Prompt("Git identity", "Git needs your name for commits (saved in your global Git settings):");
            if (string.IsNullOrWhiteSpace(name)) return;
            var email = wb.Prompt("Git identity", "And your email (use the one on your GitHub account):");
            if (string.IsNullOrWhiteSpace(email)) return;
            await Git.Run(Top, "config", "--global", "user.name", name.Trim());
            await Git.Run(Top, "config", "--global", "user.email", email.Trim());
        }
        var fresh = await Git.Status(Top);
        if (fresh.Staged.Count == 0)
        {
            if (fresh.Changes.Count == 0) { wb.Notify("There are no changes to commit."); return; }
            if (!wb.Confirm("There are no staged changes. Stage all your changes and commit them directly?", "Commit")) return;
        }
        await wb.GitOp(then == null ? "Committing…" : then == "push" ? "Committing and pushing…" : "Committing and syncing…", async () =>
        {
            if (fresh.Staged.Count == 0) await Git.Run(Top, "add", "-A");
            await Git.Run(Top, "commit", "-m", msg);
            message.Text = "";
            if (then == "sync") { var s2 = await Git.Status(Top); if (s2.Upstream != null) await Pull(); }
            if (then != null) await Push();
        });
    }
}

// ====================================================================== Run

public class RunView : DockPanel
{
    readonly IWorkbench wb;
    readonly StackPanel body = new() { Margin = new Thickness(12, 0, 12, 8) };
    public RunView(IWorkbench wb)
    {
        this.wb = wb;
        var header = UI.Header("RUN AND DEBUG");
        SetDock(header, Dock.Top);
        Children.Add(header);
        Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Render(null);
    }

    public void Render(string? activeTitle)
    {
        body.Children.Clear();
        body.Children.Add(UI.Btn("▶  Run " + (activeTitle ?? "File"), (_, _) => _ = wb.RunFile(), true));
        body.Children.Add(UI.Note("F5 runs the open Python or Java file in the terminal panel. Type into the line at the bottom of the terminal to answer input() and Scanner."));
        void Tool(string name, string? path, string hint)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            sp.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold });
            var t = new TextBlock { Text = path ?? "Not found — " + hint, TextWrapping = TextWrapping.Wrap, FontSize = 11.5 };
            t.SetResourceReference(TextBlock.ForegroundProperty, path != null ? "OkBrush" : "DangerBrush");
            sp.Children.Add(t);
            body.Children.Add(sp);
        }
        Tool("Python", wb.Tools.Python, "install it from python.org and tick \"Add python.exe to PATH\"");
        Tool("Java (JDK)", wb.Tools.Javac, "install Temurin 21 from adoptium.net");
        Tool("Git", wb.Tools.Git, "install it from git-scm.com");
        Tool("Python formatter", wb.Tools.Ruff, "reinstall Valco Code");
        Tool("Java formatter", wb.Tools.JavaFormatter, "reinstall Valco Code");
        body.Children.Add(UI.Btn("Check Again", (_, _) => { Git.Exe = null; ((MainWindow)Window.GetWindow(this)).ReloadTools(); }));
        body.Children.Add(UI.Note("Java: Valco Code compiles every .java file in the project (packages work), then runs the class you have open."));
    }
}
