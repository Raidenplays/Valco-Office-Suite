using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ValcoCode.Services;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Rendering;

namespace ValcoCode.Editor;

/// <summary>An AvalonEdit editor with VS Code-style extras: find/replace bar, folding, completion, squiggles.</summary>
public class CodeEditor : Grid
{
    public TextEditor Editor { get; } = new();
    public string Lang { get; private set; }
    public SquiggleRenderer Squiggles { get; }
    readonly SelectionMatchRenderer matches;
    FoldingManager? folding;
    readonly DispatcherTimer foldTimer;
    CompletionWindow? completion;
    readonly FindBar find;
    readonly ToolTip hoverTip = new() { Placement = PlacementMode.Mouse };
    public event Action? CaretMoved;

    public CodeEditor(string text, string lang, AppSettings s)
    {
        Lang = lang;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        Editor.Text = text;
        Editor.Document.UndoStack.ClearAll();
        Editor.ShowLineNumbers = true;
        Editor.Options.EnableHyperlinks = true;
        Editor.Options.EnableEmailHyperlinks = false;
        Editor.Options.HighlightCurrentLine = true;
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.AllowScrollBelowDocument = true;
        Editor.Options.EnableRectangularSelection = true;
        Editor.Options.CutCopyWholeLine = true;
        Editor.Options.ColumnRulerPosition = s.LineLength;
        Editor.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        Editor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Editor.Padding = new Thickness(4, 0, 0, 0);
        Editor.TextArea.TextView.Margin = new Thickness(6, 0, 0, 0);
        Editor.TextArea.SelectionCornerRadius = 2;
        SetRow(Editor, 1);
        Children.Add(Editor);

        find = new FindBar(Editor);
        SetRow(find, 0);
        Children.Add(find);

        Squiggles = new SquiggleRenderer(Editor);
        Editor.TextArea.TextView.BackgroundRenderers.Add(Squiggles);
        matches = new SelectionMatchRenderer(Editor);
        Editor.TextArea.TextView.BackgroundRenderers.Add(matches);
        Editor.TextArea.SelectionChanged += (_, _) => Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);

        foldTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        foldTimer.Tick += (_, _) => { foldTimer.Stop(); UpdateFoldings(); };
        Editor.TextChanged += (_, _) => { foldTimer.Stop(); foldTimer.Start(); };

        Editor.TextArea.Caret.PositionChanged += (_, _) => CaretMoved?.Invoke();
        Editor.TextArea.TextEntered += OnTextEntered;
        Editor.TextArea.TextEntering += OnTextEntering;
        Editor.TextArea.PreviewKeyDown += OnKey;
        Editor.MouseHover += OnHover;
        Editor.MouseHoverStopped += (_, _) => hoverTip.IsOpen = false;

        SetLanguage(lang, s.TabSize);
        ApplySettings(s);
        ApplyTheme();
    }

    public void SetLanguage(string lang, int tabSize)
    {
        Lang = lang;
        Editor.SyntaxHighlighting = Languages.Highlighting(lang);
        Editor.TextArea.IndentationStrategy = new SmartIndent(lang, tabSize);
        if (folding != null) { FoldingManager.Uninstall(folding); folding = null; }
        if (lang is "python" or "java" or "javascript" or "csharp" or "cpp" or "css" or "json")
        {
            folding = FoldingManager.Install(Editor.TextArea);
            UpdateFoldings();
        }
    }

    void UpdateFoldings()
    {
        if (folding == null) return;
        try { folding.UpdateFoldings(Lang == "python" ? Folding.Indent(Editor.Document) : Folding.Braces(Editor.Document), -1); }
        catch { }
    }

    public void ApplySettings(AppSettings s)
    {
        Editor.FontFamily = new FontFamily(s.FontFamily);
        Editor.FontSize = s.FontSize;
        Editor.WordWrap = s.WordWrap;
        Editor.Options.IndentationSize = s.TabSize;
        Editor.Options.ShowSpaces = s.ShowWhitespace;
        Editor.Options.ShowTabs = s.ShowWhitespace;
        Editor.Options.ColumnRulerPosition = s.LineLength;
        Editor.Options.ShowColumnRuler = Lang is "python" or "java";
        if (Editor.TextArea.IndentationStrategy is SmartIndent) Editor.TextArea.IndentationStrategy = new SmartIndent(Lang, s.TabSize);
    }

    public void ApplyTheme()
    {
        var t = ThemeManager.Current;
        var b = t.Brushes;
        Brush B(string k) => ThemeManager.MakeBrush(b[k]);
        Editor.Background = B("EditorBg");
        Editor.Foreground = B("EditorFg");
        Editor.LineNumbersForeground = B("GutterFg");
        Editor.TextArea.Caret.CaretBrush = B("Caret");
        var sel = ThemeManager.C(b["Selection"]);
        Editor.TextArea.SelectionBrush = new SolidColorBrush(sel);
        Editor.TextArea.SelectionForeground = null;
        Editor.TextArea.SelectionBorder = null;
        var al = ThemeManager.C(b["ActiveLine"]);
        Editor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(al);
        Editor.TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(al), 1);
        Editor.TextArea.TextView.LinkTextForegroundBrush = new SolidColorBrush(ThemeManager.C(t.Syntax.Function));
        Editor.TextArea.TextView.ColumnRulerPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)), 1);
        var gutter = ThemeManager.C(b["Gutter"]);
        Editor.TextArea.SetValue(FoldingMargin.FoldingMarkerBrushProperty, B("GutterFg"));
        Editor.TextArea.SetValue(FoldingMargin.FoldingMarkerBackgroundBrushProperty, new SolidColorBrush(gutter));
        Editor.TextArea.SetValue(FoldingMargin.SelectedFoldingMarkerBrushProperty, B("Fg"));
        Editor.TextArea.SetValue(FoldingMargin.SelectedFoldingMarkerBackgroundBrushProperty, new SolidColorBrush(gutter));
        var hl = ThemeManager.C(t.Syntax.Function);
        matches.Fill = new SolidColorBrush(Color.FromArgb(55, hl.R, hl.G, hl.B));
        Editor.TextArea.TextView.Redraw();
        find.ApplyTheme();
    }

    public void SetProblems(IEnumerable<Problem> problems)
    {
        Squiggles.Items.Clear();
        var doc = Editor.Document;
        foreach (var p in problems)
        {
            if (p.Line < 1 || p.Line > doc.LineCount) continue;
            var line = doc.GetLineByNumber(p.Line);
            int start, end;
            if (p.Source == "javac")
            {
                var txt = doc.GetText(line);
                start = line.Offset + (txt.Length - txt.TrimStart().Length);
                end = line.EndOffset;
            }
            else
            {
                start = Math.Min(line.Offset + Math.Max(0, p.Col - 1), line.EndOffset);
                var eline = doc.GetLineByNumber(Math.Clamp(p.EndLine, 1, doc.LineCount));
                end = Math.Min(eline.Offset + Math.Max(0, p.EndCol - 1), eline.EndOffset);
            }
            if (end <= start) end = Math.Min(start + 1, doc.TextLength);
            Squiggles.Items.Add(new Squiggle
            {
                Start = start, Length = end - start, Message = $"{p.Message}  ({p.Source}{(p.Code != null ? " " + p.Code : "")})",
                Color = p.Severity == "error" ? Color.FromRgb(0xf1, 0x4c, 0x4c) : Color.FromRgb(0xcc, 0xa7, 0x00),
            });
        }
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    void OnHover(object? sender, MouseEventArgs e)
    {
        var pos = Editor.GetPositionFromPoint(e.GetPosition(Editor));
        if (pos == null) return;
        var off = Editor.Document.GetOffset(pos.Value.Location);
        var sq = Squiggles.At(off);
        if (sq == null) return;
        hoverTip.Content = sq.Message;
        hoverTip.PlacementTarget = Editor;
        hoverTip.IsOpen = true;
        e.Handled = true;
    }

    // ---------------------------------------------------------------- typing helpers

    static readonly Dictionary<char, char> Pairs = new() { ['('] = ')', ['['] = ']', ['{'] = '}', ['"'] = '"', ['\''] = '\'' };

    void OnTextEntering(object sender, TextCompositionEventArgs e)
    {
        if (e.Text.Length != 1) return;
        var ch = e.Text[0];
        if (completion != null && !char.IsLetterOrDigit(ch) && ch != '_')
        {
            if (ch == '\t' || ch == '\n') completion.CompletionList.RequestInsertion(e);
        }
        var doc = Editor.Document;
        var off = Editor.CaretOffset;
        var next = off < doc.TextLength ? doc.GetCharAt(off) : '\0';
        // type over a closing bracket/quote we inserted
        if ((ch is ')' or ']' or '}' or '"' or '\'') && next == ch && Editor.SelectionLength == 0)
        {
            Editor.CaretOffset = off + 1;
            e.Handled = true;
            return;
        }
        if (Pairs.TryGetValue(ch, out var close))
        {
            if (Editor.SelectionLength > 0)
            {
                var sel = Editor.SelectedText;
                var start = Editor.SelectionStart;
                doc.Replace(start, Editor.SelectionLength, ch + sel + close);
                Editor.Select(start + 1, sel.Length);
                e.Handled = true;
                return;
            }
            var prev = off > 0 ? doc.GetCharAt(off - 1) : '\0';
            var isQuote = ch is '"' or '\'';
            if (isQuote && (char.IsLetterOrDigit(prev) || prev == ch)) return;
            if (!(next == '\0' || char.IsWhiteSpace(next) || next is ')' or ']' or '}' or ',' or ';' or ':')) return;
            doc.Insert(off, ch.ToString() + close);
            Editor.CaretOffset = off + 1;
            e.Handled = true;
        }
    }

    void OnTextEntered(object sender, TextCompositionEventArgs e)
    {
        if (e.Text == "}" && Lang != "python")
        {
            // outdent a lone closing brace
            var line = Editor.Document.GetLineByOffset(Editor.CaretOffset);
            var txt = Editor.Document.GetText(line);
            if (txt.Trim() == "}")
            {
                var unit = Editor.Options.IndentationSize;
                var lead = txt.Length - txt.TrimStart().Length;
                if (lead >= unit) Editor.Document.Remove(line.Offset, unit);
            }
        }
        if (completion == null && e.Text.Length == 1 && (char.IsLetter(e.Text[0]) || e.Text == "_" || e.Text == "."))
        {
            if (e.Text == "." || WordLen() >= 2) ShowCompletion(false);
        }
    }

    int WordLen()
    {
        var off = Editor.CaretOffset;
        int i = off;
        while (i > 0 && (char.IsLetterOrDigit(Editor.Document.GetCharAt(i - 1)) || Editor.Document.GetCharAt(i - 1) == '_')) i--;
        return off - i;
    }

    public void ShowCompletion(bool explicitly)
    {
        if (Lang is not ("python" or "java") && !explicitly) return;
        var items = Completions.For(Lang, Editor.Document, Editor.CaretOffset, out var start);
        if (items.Count == 0) return;
        completion = new CompletionWindow(Editor.TextArea) { StartOffset = start, CloseWhenCaretAtBeginning = true, MinWidth = 280 };
        var t = ThemeManager.Current.Brushes;
        completion.Background = ThemeManager.MakeBrush(t["MenuBg"]);
        completion.Foreground = ThemeManager.MakeBrush(t["MenuFg"]);
        completion.BorderBrush = ThemeManager.MakeBrush(t["Line"]);
        completion.CompletionList.ListBox.Background = completion.Background;
        completion.CompletionList.ListBox.Foreground = completion.Foreground;
        completion.CompletionList.ListBox.BorderThickness = new Thickness(0);
        foreach (var i in items.OrderByDescending(i => i.Priority).ThenBy(i => i.Text, StringComparer.OrdinalIgnoreCase))
            completion.CompletionList.CompletionData.Add(i);
        var prefix = Editor.Document.GetText(start, Editor.CaretOffset - start);
        completion.Show();
        if (prefix.Length > 0) completion.CompletionList.SelectItem(prefix);
        completion.Closed += (_, _) => completion = null;
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (ctrl && key == Key.Space) { ShowCompletion(true); e.Handled = true; }
        else if (ctrl && !shift && key == Key.OemQuestion) { EditCommands.ToggleComment(Editor, Lang); e.Handled = true; }
        else if (alt && !ctrl && (key == Key.Up || key == Key.Down)) { EditCommands.MoveLines(Editor, key == Key.Up ? -1 : 1, shift); e.Handled = true; }
        else if (ctrl && !shift && key == Key.D) { EditCommands.DuplicateOrSelectWord(Editor); e.Handled = true; }
        else if (ctrl && shift && key == Key.K) { DeleteLine(); e.Handled = true; }
        else if (ctrl && !shift && key == Key.Enter) { InsertLineBelow(); e.Handled = true; }
        else if (ctrl && key == Key.F) { find.Open(false); e.Handled = true; }
        else if (ctrl && key == Key.H) { find.Open(true); e.Handled = true; }
        else if (key == Key.F3) { find.Next(!shift); e.Handled = true; }
        else if (key == Key.Escape && find.Visibility == Visibility.Visible) { find.Close(); e.Handled = true; }
        else if (key == Key.Back && Editor.SelectionLength == 0)
        {
            // delete an empty bracket pair together
            var off = Editor.CaretOffset;
            var doc = Editor.Document;
            if (off > 0 && off < doc.TextLength && Pairs.TryGetValue(doc.GetCharAt(off - 1), out var c) && doc.GetCharAt(off) == c)
            {
                doc.Remove(off - 1, 2);
                e.Handled = true;
            }
            else if (Editor.Options.ConvertTabsToSpaces && off > 0)
            {
                // backspace through an indent level at once
                var line = doc.GetLineByOffset(off);
                var before = doc.GetText(line.Offset, off - line.Offset);
                var unit = Editor.Options.IndentationSize;
                if (before.Length > 0 && before.All(ch => ch == ' ') && before.Length % unit == 0)
                {
                    doc.Remove(off - unit, unit);
                    e.Handled = true;
                }
            }
        }
    }

    void DeleteLine()
    {
        var line = Editor.Document.GetLineByOffset(Editor.CaretOffset);
        Editor.Document.Remove(line.Offset, line.TotalLength > 0 ? line.TotalLength : line.Length);
    }

    void InsertLineBelow()
    {
        var line = Editor.Document.GetLineByOffset(Editor.CaretOffset);
        Editor.CaretOffset = line.EndOffset;
        Editor.TextArea.PerformTextInput("\n");
    }

    public void OpenFind(bool replace) => find.Open(replace);

    public void GoTo(int line, int col, int selectLength = 0)
    {
        var doc = Editor.Document;
        line = Math.Clamp(line, 1, doc.LineCount);
        var l = doc.GetLineByNumber(line);
        var off = Math.Min(l.Offset + Math.Max(0, col - 1), l.EndOffset);
        Editor.Select(off, Math.Min(selectLength, l.EndOffset - off));
        Editor.TextArea.Caret.Offset = off + Math.Min(selectLength, l.EndOffset - off);
        Editor.ScrollTo(line, col);
        Dispatcher.BeginInvoke(() => { Editor.TextArea.Focus(); Editor.ScrollToLine(line); }, DispatcherPriority.Input);
    }

    /// <summary>Replaces the text while keeping undo history and the cursor roughly where it was.</summary>
    public void ReplaceAll(string newText)
    {
        var old = Editor.Text;
        if (old == newText) return;
        int s = 0, max = Math.Min(old.Length, newText.Length);
        while (s < max && old[s] == newText[s]) s++;
        int eo = old.Length, en = newText.Length;
        while (eo > s && en > s && old[eo - 1] == newText[en - 1]) { eo--; en--; }
        var caretLine = Editor.TextArea.Caret.Line;
        var caretCol = Editor.TextArea.Caret.Column;
        Editor.Document.Replace(s, eo - s, newText.Substring(s, en - s));
        Editor.TextArea.Caret.Line = Math.Min(caretLine, Editor.Document.LineCount);
        Editor.TextArea.Caret.Column = caretCol;
    }
}

/// <summary>VS Code-style find/replace bar that sits above the editor.</summary>
public class FindBar : Border
{
    readonly TextEditor ed;
    readonly TextBox findBox = new() { Width = 220, Tag = "Find" };
    readonly TextBox replaceBox = new() { Width = 220, Tag = "Replace" };
    readonly ToggleButton caseBtn = Opt("Aa", "Match Case (Alt+C)");
    readonly ToggleButton wordBtn = Opt("ab", "Match Whole Word (Alt+W)");
    readonly ToggleButton regexBtn = Opt(".*", "Use Regular Expression (Alt+R)");
    readonly TextBlock count = new() { MinWidth = 70, Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel replaceRow;

    static ToggleButton Opt(string text, string tip) => new() { Content = text, ToolTip = tip, Width = 26, Height = 22, Margin = new Thickness(2, 0, 0, 0), Focusable = false, Padding = new Thickness(0) };

    public FindBar(TextEditor editor)
    {
        ed = editor;
        Visibility = Visibility.Collapsed;
        HorizontalAlignment = HorizontalAlignment.Right;
        Margin = new Thickness(0, 0, 18, 0);
        Padding = new Thickness(6, 4, 6, 4);
        BorderThickness = new Thickness(1, 0, 1, 1);
        Panel.SetZIndex(this, 10);
        var findRow = new StackPanel { Orientation = Orientation.Horizontal };
        findRow.Children.Add(findBox);
        findRow.Children.Add(caseBtn);
        findRow.Children.Add(wordBtn);
        findRow.Children.Add(regexBtn);
        findRow.Children.Add(count);
        findRow.Children.Add(Btn("", "Previous Match (Shift+F3)", () => Next(false)));
        findRow.Children.Add(Btn("", "Next Match (F3)", () => Next(true)));
        findRow.Children.Add(Btn("", "Close (Escape)", Close));
        replaceRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
        replaceRow.Children.Add(replaceBox);
        replaceRow.Children.Add(Btn("", "Replace (Enter)", ReplaceOne));
        replaceRow.Children.Add(Btn("", "Replace All (Ctrl+Alt+Enter)", ReplaceAllMatches));
        Child = new StackPanel { Children = { findRow, replaceRow } };
        findBox.TextChanged += (_, _) => { Highlight(); };
        foreach (var t in new[] { caseBtn, wordBtn, regexBtn }) { t.Checked += (_, _) => Highlight(); t.Unchecked += (_, _) => Highlight(); }
        findBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Next(!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)); e.Handled = true; }
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
        replaceBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { ReplaceAllMatches(); e.Handled = true; }
            else if (e.Key == Key.Enter) { ReplaceOne(); e.Handled = true; }
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Alt)
            {
                var k = e.SystemKey;
                if (k == Key.C) { caseBtn.IsChecked = !caseBtn.IsChecked; e.Handled = true; }
                if (k == Key.W) { wordBtn.IsChecked = !wordBtn.IsChecked; e.Handled = true; }
                if (k == Key.R) { regexBtn.IsChecked = !regexBtn.IsChecked; e.Handled = true; }
            }
        };
    }

    Button Btn(string glyph, string tip, Action a)
    {
        var b = new Button { Content = glyph, ToolTip = tip };
        b.SetResourceReference(StyleProperty, "IconButton");
        b.Click += (_, _) => a();
        return b;
    }

    public void ApplyTheme()
    {
        SetResourceReference(BackgroundProperty, "MenuBgBrush");
        SetResourceReference(BorderBrushProperty, "LineBrush");
        count.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        foreach (var t in new[] { caseBtn, wordBtn, regexBtn }) t.Style = (Style)Application.Current.Resources["OptToggle"];
    }

    public void Open(bool replace)
    {
        Visibility = Visibility.Visible;
        replaceRow.Visibility = replace ? Visibility.Visible : Visibility.Collapsed;
        if (ed.SelectionLength > 0 && !ed.SelectedText.Contains('\n')) findBox.Text = ed.SelectedText;
        findBox.Focus();
        findBox.SelectAll();
        Highlight();
    }

    public void Close()
    {
        Visibility = Visibility.Collapsed;
        ed.TextArea.TextView.BackgroundRenderers.Remove(marker);
        ed.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        ed.TextArea.Focus();
    }

    Regex? Pattern()
    {
        var q = findBox.Text;
        if (q.Length == 0) return null;
        var p = regexBtn.IsChecked == true ? q : Regex.Escape(q);
        if (wordBtn.IsChecked == true) p = @"\b" + p + @"\b";
        try { return new Regex(p, caseBtn.IsChecked == true ? RegexOptions.Multiline : RegexOptions.IgnoreCase | RegexOptions.Multiline); }
        catch { return null; }
    }

    readonly MatchMarker marker = new();

    void Highlight()
    {
        var re = Pattern();
        marker.Matches.Clear();
        if (re != null)
            foreach (Match m in re.Matches(ed.Text))
            {
                if (m.Length == 0) continue;
                marker.Matches.Add((m.Index, m.Length));
                if (marker.Matches.Count > 5000) break;
            }
        if (!ed.TextArea.TextView.BackgroundRenderers.Contains(marker)) ed.TextArea.TextView.BackgroundRenderers.Add(marker);
        var hl = ThemeManager.C(ThemeManager.Current.Syntax.Number);
        marker.Fill = new SolidColorBrush(Color.FromArgb(90, hl.R, hl.G, hl.B));
        ed.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        count.Text = re == null ? (findBox.Text.Length > 0 ? "Bad pattern" : "No results") : marker.Matches.Count == 0 ? "No results" : $"{marker.Matches.Count} results";
        if (marker.Matches.Count > 0)
        {
            var from = ed.SelectionStart;
            var first = marker.Matches.FirstOrDefault(x => x.Start >= from);
            if (first == default) first = marker.Matches[0];
            Select(first);
        }
    }

    void Select((int Start, int Len) m)
    {
        ed.Select(m.Start, m.Len);
        var line = ed.Document.GetLineByOffset(m.Start).LineNumber;
        ed.ScrollToLine(line);
        var i = marker.Matches.IndexOf(m);
        count.Text = $"{i + 1} of {marker.Matches.Count}";
    }

    public void Next(bool forward)
    {
        if (Visibility != Visibility.Visible) { Open(false); return; }
        if (marker.Matches.Count == 0) return;
        (int, int) m;
        if (forward)
        {
            var from = ed.SelectionStart + Math.Max(1, ed.SelectionLength);
            m = marker.Matches.FirstOrDefault(x => x.Start >= from);
            if (m == default) m = marker.Matches[0];
        }
        else
        {
            var from = ed.SelectionStart;
            m = marker.Matches.LastOrDefault(x => x.Start < from);
            if (m == default) m = marker.Matches[^1];
        }
        Select(m);
    }

    void ReplaceOne()
    {
        var re = Pattern();
        if (re == null) return;
        var sel = ed.SelectedText;
        var mm = re.Match(sel);
        if (ed.SelectionLength > 0 && mm.Success && mm.Length == sel.Length)
        {
            var rep = regexBtn.IsChecked == true ? mm.Result(replaceBox.Text) : replaceBox.Text;
            var start = ed.SelectionStart;
            ed.Document.Replace(start, ed.SelectionLength, rep);
            ed.Select(start + rep.Length, 0);
        }
        Highlight();
        Next(true);
    }

    void ReplaceAllMatches()
    {
        var re = Pattern();
        if (re == null) return;
        var text = ed.Text;
        var result = regexBtn.IsChecked == true ? re.Replace(text, replaceBox.Text) : re.Replace(text, _ => replaceBox.Text);
        if (result == text) return;
        ed.Document.Replace(0, ed.Document.TextLength, result);
        Highlight();
    }

    class MatchMarker : IBackgroundRenderer
    {
        public List<(int Start, int Len)> Matches { get; } = new();
        public Brush Fill { get; set; } = Brushes.Transparent;
        public KnownLayer Layer => KnownLayer.Selection;
        public void Draw(TextView tv, DrawingContext dc)
        {
            if (Matches.Count == 0 || !tv.VisualLinesValid || tv.VisualLines.Count == 0) return;
            int vs = tv.VisualLines.First().FirstDocumentLine.Offset, ve = tv.VisualLines.Last().LastDocumentLine.EndOffset;
            foreach (var (s, l) in Matches)
            {
                if (s > ve || s + l < vs) continue;
                foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(tv, new ICSharpCode.AvalonEdit.Document.TextSegment { StartOffset = s, Length = l }))
                    dc.DrawRoundedRectangle(Fill, null, r, 2, 2);
            }
        }
    }
}
