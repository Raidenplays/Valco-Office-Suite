using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ValcoCode.Services;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace ValcoCode.Views;

/// <summary>One shell or program session shown in the terminal panel.</summary>
public class TermSession
{
    public string Name = "cmd";
    public Process? Proc;
    public readonly TextDocument Doc = new();
    public readonly List<string> History = new();
    public int HistoryIndex = -1;
    public bool Exited;
    public string Cwd = "";
    public bool IsShell;
    public readonly List<(int Start, int Len, Color Color)> Colors = new();
}

/// <summary>
/// Integrated terminal: a cmd.exe shell (or a running Python/Java program) with its output
/// streamed into a read-only editor and an input line underneath, like a console.
/// </summary>
public class TerminalView : Grid
{
    readonly TextEditor output = new() { IsReadOnly = true, ShowLineNumbers = false, WordWrap = true };
    readonly TextBox input = new() { BorderThickness = new Thickness(0), Padding = new Thickness(2, 3, 2, 3) };
    readonly TextBlock prompt = new() { Text = "›", Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold };
    readonly ListBox list = new() { Width = 150 };
    readonly List<TermSession> sessions = new();
    TermSession? active;
    readonly DispatcherTimer flush;
    readonly Colorizer colorizer;
    public string DefaultCwd { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public event Action? SessionsChanged;

    public TerminalView()
    {
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        output.Options.EnableHyperlinks = true;
        output.Options.HighlightCurrentLine = false;
        output.TextArea.Caret.CaretBrush = Brushes.Transparent;
        output.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        output.Padding = new Thickness(8, 4, 4, 0);
        colorizer = new Colorizer(this);
        output.TextArea.TextView.LineTransformers.Add(colorizer);
        Children.Add(output);

        var inputRow = new DockPanel();
        DockPanel.SetDock(prompt, Dock.Left);
        inputRow.Children.Add(prompt);
        inputRow.Children.Add(input);
        SetRow(inputRow, 1);
        Children.Add(inputRow);

        SetColumn(list, 1);
        SetRowSpan(list, 2);
        list.BorderThickness = new Thickness(1, 0, 0, 0);
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is ListBoxItem li && li.Tag is TermSession s && s != active) Show(s); };
        Children.Add(list);

        input.PreviewKeyDown += OnInputKey;
        output.TextArea.PreviewTextInput += (_, e) => { input.Focus(); input.AppendText(e.Text); input.CaretIndex = input.Text.Length; e.Handled = true; };
        output.PreviewMouseUp += (_, _) => { if (output.SelectionLength == 0) input.Focus(); };

        flush = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(30) };
        flush.Tick += (_, _) => Flush();
        flush.Start();
        ApplyTheme(13);
    }

    public void ApplyTheme(double fontSize)
    {
        var b = ThemeManager.Current.Brushes;
        var bg = ThemeManager.MakeBrush(b["TermBg"]);
        var fg = ThemeManager.MakeBrush(b["TermFg"]);
        output.Background = bg;
        output.Foreground = fg;
        input.Background = bg;
        input.Foreground = fg;
        input.CaretBrush = fg;
        prompt.Foreground = ThemeManager.MakeBrush(b["TabAccent"]);
        Background = bg;
        var font = new FontFamily("Cascadia Mono, Consolas");
        output.FontFamily = font;
        input.FontFamily = font;
        output.FontSize = fontSize;
        input.FontSize = fontSize;
        list.SetResourceReference(BackgroundProperty, "PanelBgBrush");
        list.SetResourceReference(Control.BorderBrushProperty, "LineBrush");
        output.TextArea.SelectionBrush = new SolidColorBrush(ThemeManager.C(b["Selection"]));
        output.TextArea.SelectionForeground = null;
        output.TextArea.TextView.Redraw();
    }

    // ---------------------------------------------------------------- sessions

    TermSession Create(string name, string cwd, bool shell)
    {
        var s = new TermSession { Name = name, Cwd = cwd, IsShell = shell };
        sessions.Add(s);
        RefreshList();
        return s;
    }

    void RefreshList()
    {
        list.Items.Clear();
        foreach (var s in sessions)
        {
            var item = new ListBoxItem
            {
                Tag = s,
                Content = new TextBlock { Text = (s.IsShell ? "  " : "  ") + s.Name + (s.Exited ? " (done)" : ""), TextTrimming = TextTrimming.CharacterEllipsis, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI") },
            };
            list.Items.Add(item);
            if (s == active) list.SelectedItem = item;
        }
        list.Visibility = sessions.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        SessionsChanged?.Invoke();
    }

    void Show(TermSession s)
    {
        active = s;
        output.Document = s.Doc;
        output.ScrollToEnd();
        prompt.Visibility = s.Exited && !s.IsShell ? Visibility.Hidden : Visibility.Visible;
        RefreshList();
        input.Focus();
    }

    public void NewShell(string? cwd = null)
    {
        var dir = cwd != null && Directory.Exists(cwd) ? cwd : DefaultCwd;
        var s = Create("cmd", dir, true);
        Start(s, "cmd.exe", new[] { "/Q", "/K", "prompt $P$G$S" }, dir);
        Show(s);
    }

    public void EnsureShell()
    {
        if (sessions.Count == 0) NewShell();
        else if (active == null) Show(sessions[0]);
    }

    /// <summary>Runs a program in its own session (re-using one with the same name if it has finished).</summary>
    public void RunProgram(string name, string file, IEnumerable<string> args, string cwd, string? banner = null)
    {
        var s = sessions.FirstOrDefault(x => x.Name == name && !x.IsShell);
        if (s != null && s.Proc != null && !s.Exited)
        {
            try { s.Proc.Kill(true); } catch { }
        }
        if (s == null) s = Create(name, cwd, false);
        s.Exited = false;
        s.Doc.Text = "";
        s.Colors.Clear();
        Show(s);
        if (banner != null) Append(s, banner + "\n", ThemeManager.C(ThemeManager.Current.Brushes["Muted"]));
        Start(s, file, args, cwd);
    }

    public void Print(string name, string text, Color? color = null)
    {
        var s = sessions.FirstOrDefault(x => x.Name == name && !x.IsShell) ?? Create(name, DefaultCwd, false);
        s.Exited = true;
        Show(s);
        Append(s, text, color);
    }

    void Start(TermSession s, string file, IEnumerable<string> args, string cwd)
    {
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = cwd,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["PYTHONUNBUFFERED"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUTF8"] = "1";
        psi.Environment["TERM_PROGRAM"] = "ValcoCode";
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        s.Proc = p;
        try
        {
            p.Start();
        }
        catch (Exception e)
        {
            Append(s, $"Couldn't start {file}: {e.Message}\n", Colors.IndianRed);
            s.Exited = true;
            return;
        }
        if (s.IsShell) p.StandardInput.WriteLine("chcp 65001 >nul");
        _ = Pump(s, p.StandardOutput, null);
        _ = Pump(s, p.StandardError, s.IsShell ? null : Color.FromRgb(0xf1, 0x4c, 0x4c));
        p.Exited += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            Flush();
            s.Exited = true;
            var code = 0;
            try { code = p.ExitCode; } catch { }
            if (!s.IsShell)
                Append(s, $"\n[{(code == 0 ? "Finished" : "Exited with code " + code)}]\n", code == 0 ? ThemeManager.C(ThemeManager.Current.Brushes["Muted"]) : Color.FromRgb(0xf1, 0x4c, 0x4c));
            else Append(s, "\n[shell closed — press Enter to restart]\n", ThemeManager.C(ThemeManager.Current.Brushes["Muted"]));
            if (s == active && !s.IsShell) prompt.Visibility = Visibility.Hidden;
            RefreshList();
        });
    }

    readonly object pendingLock = new();
    readonly List<(TermSession S, string Text, Color? Color)> pending = new();
    static readonly Regex Ansi = new(@"\x1b\[[0-9;?]*[ -/]*[@-~]|\x1b\][^\x07\x1b]*(\x07|\x1b\\)|\x1b[()][0-9A-B]|\x1b[=>]", RegexOptions.Compiled);

    async Task Pump(TermSession s, StreamReader r, Color? color)
    {
        var buf = new char[4096];
        try
        {
            while (true)
            {
                var n = await r.ReadAsync(buf, 0, buf.Length);
                if (n <= 0) break;
                var text = Ansi.Replace(new string(buf, 0, n), "").Replace("\r\n", "\n").Replace("\r", "");
                lock (pendingLock) pending.Add((s, text, color));
            }
        }
        catch { }
    }

    void Flush()
    {
        List<(TermSession S, string Text, Color? Color)> batch;
        lock (pendingLock)
        {
            if (pending.Count == 0) return;
            batch = new(pending);
            pending.Clear();
        }
        foreach (var (s, t, c) in batch) Append(s, t, c);
    }

    void Append(TermSession s, string text, Color? color = null)
    {
        // \b (backspace) from programs: drop the previous character
        if (text.Contains('\b'))
        {
            var sb = new StringBuilder();
            foreach (var ch in text) { if (ch == '\b') { if (sb.Length > 0) sb.Length--; } else sb.Append(ch); }
            text = sb.ToString();
        }
        var atEnd = s == active && (output.TextArea.TextView.VerticalOffset + output.TextArea.TextView.ActualHeight >= output.TextArea.TextView.DocumentHeight - 30);
        var start = s.Doc.TextLength;
        s.Doc.Insert(start, text);
        if (color != null) s.Colors.Add((start, text.Length, color.Value));
        // keep the scrollback bounded
        if (s.Doc.TextLength > 400_000)
        {
            var cut = s.Doc.TextLength - 300_000;
            s.Doc.Remove(0, cut);
            for (int i = s.Colors.Count - 1; i >= 0; i--)
            {
                var (cs, cl, cc) = s.Colors[i];
                if (cs + cl <= cut) s.Colors.RemoveAt(i);
                else s.Colors[i] = (Math.Max(0, cs - cut), cl - Math.Max(0, cut - cs), cc);
            }
        }
        if (s == active && atEnd) output.ScrollToEnd();
    }

    void OnInputKey(object sender, KeyEventArgs e)
    {
        var s = active;
        if (s == null) return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            var line = input.Text;
            input.Clear();
            if (s.Exited)
            {
                if (s.IsShell) { s.Doc.Text = ""; s.Colors.Clear(); s.Exited = false; Start(s, "cmd.exe", new[] { "/Q", "/K", "prompt $P$G$S" }, s.Cwd); }
                return;
            }
            if (line.Trim().Equals("cls", StringComparison.OrdinalIgnoreCase) || line.Trim().Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                s.Doc.Text = "";
                s.Colors.Clear();
                if (s.IsShell) s.Proc?.StandardInput.WriteLine("");
                return;
            }
            if (line.Length > 0) { s.History.Remove(line); s.History.Add(line); }
            s.HistoryIndex = -1;
            // echo what you typed, like a real console
            Append(s, line + "\n", ThemeManager.C(ThemeManager.Current.Syntax.String));
            try { s.Proc?.StandardInput.WriteLine(line); s.Proc?.StandardInput.Flush(); } catch { }
        }
        else if (e.Key == Key.Up && s.History.Count > 0)
        {
            s.HistoryIndex = s.HistoryIndex < 0 ? s.History.Count - 1 : Math.Max(0, s.HistoryIndex - 1);
            input.Text = s.History[s.HistoryIndex];
            input.CaretIndex = input.Text.Length;
            e.Handled = true;
        }
        else if (e.Key == Key.Down && s.HistoryIndex >= 0)
        {
            s.HistoryIndex++;
            input.Text = s.HistoryIndex < s.History.Count ? s.History[s.HistoryIndex] : "";
            if (s.HistoryIndex >= s.History.Count) s.HistoryIndex = -1;
            input.CaretIndex = input.Text.Length;
            e.Handled = true;
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && input.SelectionLength == 0)
        {
            Stop();
            e.Handled = true;
        }
    }

    /// <summary>Stops the running program (like Ctrl+C).</summary>
    public void Stop()
    {
        var s = active;
        if (s?.Proc == null || s.Exited) return;
        if (s.IsShell)
        {
            // kill whatever the shell is running, keep the shell
            try
            {
                foreach (var child in ChildProcesses(s.Proc.Id)) { try { child.Kill(true); } catch { } }
            }
            catch { }
            Append(s, "^C\n", Colors.Gray);
        }
        else
        {
            try { s.Proc.Kill(true); } catch { }
        }
    }

    static IEnumerable<Process> ChildProcesses(int parentId)
    {
        // uses WMI-free approach: walk all processes and match parent ids via NtQuery is overkill; use wmic-less toolhelp snapshot
        var snapshot = Native.CreateToolhelp32Snapshot(2, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1)) yield break;
        try
        {
            var entry = new Native.PROCESSENTRY32 { dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.PROCESSENTRY32>() };
            var ids = new List<int>();
            if (Native.Process32First(snapshot, ref entry))
                do { if (entry.th32ParentProcessID == parentId) ids.Add((int)entry.th32ProcessID); } while (Native.Process32Next(snapshot, ref entry));
            foreach (var id in ids)
            {
                Process? p = null;
                try { p = Process.GetProcessById(id); } catch { }
                if (p != null && !p.ProcessName.Equals("conhost", StringComparison.OrdinalIgnoreCase)) yield return p;
            }
        }
        finally { Native.CloseHandle(snapshot); }
    }

    public void KillActive()
    {
        var s = active;
        if (s == null) return;
        try { if (s.Proc != null && !s.Exited) s.Proc.Kill(true); } catch { }
        sessions.Remove(s);
        active = null;
        if (sessions.Count > 0) Show(sessions[^1]);
        else { output.Document = new TextDocument(); RefreshList(); }
    }

    public void ClearActive()
    {
        if (active == null) return;
        active.Doc.Text = "";
        active.Colors.Clear();
    }

    public void FocusInput() => input.Focus();

    public void KillAll()
    {
        foreach (var s in sessions) try { if (s.Proc != null && !s.Exited) s.Proc.Kill(true); } catch { }
    }

    class Colorizer(TerminalView owner) : DocumentColorizingTransformer
    {
        protected override void ColorizeLine(DocumentLine line)
        {
            var s = owner.active;
            if (s == null) return;
            foreach (var (start, len, color) in s.Colors)
            {
                var a = Math.Max(start, line.Offset);
                var b = Math.Min(start + len, line.EndOffset);
                if (a >= b) continue;
                var brush = new SolidColorBrush(color);
                ChangeLinePart(a, b, el => el.TextRunProperties.SetForegroundBrush(brush));
            }
            // tracebacks and Java exceptions in red, file:line links stand out
            var text = CurrentContext.Document.GetText(line);
            if (Regex.IsMatch(text, @"^(Traceback|\s+File "".*"", line \d+|\w+(\.\w+)*(Error|Exception)\b|Exception in thread|\s+at [\w$.]+\()"))
                ChangeLinePart(line.Offset, line.EndOffset, el => el.TextRunProperties.SetForegroundBrush(new SolidColorBrush(Color.FromRgb(0xf1, 0x4c, 0x4c))));
        }
    }
}

static class Native
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    public struct PROCESSENTRY32
    {
        public uint dwSize, cntUsage, th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern bool Process32First(IntPtr snap, ref PROCESSENTRY32 e);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern bool Process32Next(IntPtr snap, ref PROCESSENTRY32 e);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
}
