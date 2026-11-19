using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace ValcoWord
{
    public partial class MainWindow
    {
        string ReviewerName => string.IsNullOrWhiteSpace(Settings.Current.UserName) ? Environment.UserName : Settings.Current.UserName;
        string ReviewerInitials => !string.IsNullOrWhiteSpace(Settings.Current.Initials) ? Settings.Current.Initials : new string(ReviewerName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0])).Take(3).ToArray());

        // =====================================================================================
        //  Spelling & grammar
        // =====================================================================================
        readonly HashSet<string> _ignoredWords = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        readonly HashSet<string> _ignoredGrammar = new HashSet<string>();

        class ProofIssue { public TextRange Range; public bool Grammar; public string Rule; public List<string> Suggestions = new List<string>(); public SpellingError Spelling; }

        static readonly (Regex Pattern, string Rule, Func<Match, string> Fix)[] GrammarRules =
        {
            (new Regex(@"\b(\w+)\s+\1\b", RegexOptions.IgnoreCase), "Repeated Word", m => m.Groups[1].Value),
            (new Regex(@"(?<=\S)  +(?=\S)"), "Extra Space", m => " "),
            (new Regex(@"\s+([,.;:!?])(?!\d)"), "Space Before Punctuation", m => m.Groups[1].Value),
            (new Regex(@"(?<=[a-z])([,;:])(?=[A-Za-z])"), "Missing Space After Punctuation", m => m.Groups[1].Value + " "),
            (new Regex(@"\b[Aa] (?=[aeiouAEIOU][a-z])(?!(uni|use|usu|eu|one|once|ur))"), "Use \"an\" before a vowel sound", m => m.Value[0] == 'A' ? "An " : "an "),
            (new Regex(@"\b(could|should|would|must|might) of\b", RegexOptions.IgnoreCase), "Verb Form (\"of\" → \"have\")", m => m.Groups[1].Value + " have"),
            (new Regex(@"\balot\b", RegexOptions.IgnoreCase), "Misused Word", m => "a lot"),
            (new Regex(@"\b(their|there) (is|are|was|were)\b(?<=their \w+)", RegexOptions.IgnoreCase), "Possessive vs. Adverb", m => "there " + m.Groups[2].Value),
            (new Regex(@"\byour (welcome|right|wrong|correct)\b", RegexOptions.IgnoreCase), "Contraction (\"you're\")", m => "you're " + m.Groups[1].Value),
            (new Regex(@"\bits (a|an|the|been|not|going|time)\b"), "Contraction (\"it's\")", m => "it's " + m.Groups[1].Value),
            (new Regex(@"(?<=(^|[.!?]\s+))[a-z]"), "Capitalization", m => m.Value.ToUpperInvariant()),
            (new Regex(@"(?<=\s)i(?=[\s',.!?])"), "Capitalization", m => "I"),
            (new Regex(@"\b(less) (people|things|items|cars|books|students)\b", RegexOptions.IgnoreCase), "Less vs. Fewer", m => "fewer " + m.Groups[2].Value),
            (new Regex(@"\b(then) (I|we|you|he|she|they) (would|will)\b(?<=than.*)"), "Than vs. Then", m => m.Value),
        };

        ProofIssue NextIssue(TextPointer from, bool grammar)
        {
            // spelling: WPF's checker
            ProofIssue spell = null;
            if (SpellCheck.GetIsEnabled(Editor))
            {
                var p = from;
                for (int guard = 0; guard < 2000; guard++)
                {
                    var pos = Editor.GetNextSpellingErrorPosition(p, LogicalDirection.Forward);
                    if (pos == null) break;
                    var err = Editor.GetSpellingError(pos);
                    var range = Editor.GetSpellingErrorRange(pos);
                    if (err == null || range == null) break;
                    if (!_ignoredWords.Contains(range.Text) && !IsInGeneratedOrField(range.Start))
                    {
                        spell = new ProofIssue { Range = range, Spelling = err, Rule = "Not in Dictionary", Suggestions = err.Suggestions.Take(8).ToList() };
                        break;
                    }
                    p = range.End;
                }
            }
            if (!grammar) return spell;
            // grammar: scan paragraphs from the caret up to the spelling hit
            ProofIssue gram = null;
            foreach (var para in Fmt.AllParagraphs(Editor.Document.Blocks))
            {
                if (para.ContentEnd.CompareTo(from) <= 0) continue;
                if (spell != null && para.ContentStart.CompareTo(spell.Range.Start) > 0) break;
                if (Fmt.Ancestor<Section>(para) is Section gs && SectionKind.Of(gs) != null) continue;
                var text = Spans.Text(para);
                foreach (var (pattern, rule, fix) in GrammarRules)
                {
                    foreach (Match m in pattern.Matches(text))
                    {
                        if (m.Length == 0) continue;
                        var start = OffsetToPointer(para, m.Index); var end = OffsetToPointer(para, m.Index + m.Length);
                        if (start == null || end == null || start.CompareTo(from) < 0) continue;
                        string key = rule + "|" + m.Value + "|" + m.Index + "|" + text.GetHashCode();
                        if (_ignoredGrammar.Contains(key) || _ignoredGrammar.Contains(rule + "|*")) continue;
                        if (rule == "Capitalization" && m.Index > 0 && !Regex.IsMatch(text.Substring(0, m.Index).TrimEnd(), @"[.!?]$") && m.Value != "i") continue;
                        if (gram == null || start.CompareTo(gram.Range.Start) < 0)
                            gram = new ProofIssue { Range = new TextRange(start, end), Grammar = true, Rule = rule, Suggestions = new List<string> { fix(m) } };
                    }
                }
                if (gram != null) break;
            }
            if (gram != null && (spell == null || gram.Range.Start.CompareTo(spell.Range.Start) < 0)) return gram;
            return spell;
        }

        bool IsInGeneratedOrField(TextPointer p)
        {
            var sec = Fmt.AncestorAt<Section>(p);
            if (sec != null && SectionKind.Of(sec) != null && SectionKind.Of(sec) != SectionKind.Notes) return true;
            var span = Fmt.AncestorAt<Span>(p);
            return span?.Tag is MergeFieldInfo || span?.Tag is FieldInfo;
        }

        static TextPointer OffsetToPointer(Paragraph p, int offset)
        {
            var nav = p.ContentStart; int count = 0;
            while (nav != null && nav.CompareTo(p.ContentEnd) <= 0)
            {
                if (nav.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    var run = nav.GetTextInRun(LogicalDirection.Forward);
                    if (count + run.Length >= offset) return nav.GetPositionAtOffset(offset - count);
                    count += run.Length;
                }
                else if (nav.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart && nav.GetAdjacentElement(LogicalDirection.Forward) is LineBreak) count += Environment.NewLine.Length;
                nav = nav.GetNextContextPosition(LogicalDirection.Forward);
            }
            return offset == count ? p.ContentEnd : null;
        }

        void SpellingDialog_Click(object sender, RoutedEventArgs e) => SpellingAndGrammar();

        void SpellingAndGrammar()
        {
            if (!SpellCheck.GetIsEnabled(Editor)) { SpellCheck.SetIsEnabled(Editor, true); Editor.UpdateLayout(); }
            var d = new VistaWindow { Title = "Spelling and Grammar: English (Canada)", Owner = this, ShowInTaskbar = false, ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.Height, Width = 520, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var ruleText = new TextBlock { FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x00, 0x00)), Margin = new Thickness(0, 0, 0, 4) };
            var context = new RichTextBox { Height = 80, IsReadOnly = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily(Th.Minor), FontSize = 13 };
            var sugg = new ListBox { Height = 100 };
            var checkGrammar = new CheckBox { Content = "Check grammar", IsChecked = Settings.Current.GrammarCheck, Margin = new Thickness(0, 6, 0, 0) };
            var right = new StackPanel { Width = 130, Margin = new Thickness(10, 18, 0, 0) };
            var left = new StackPanel { Width = 340 };
            left.Children.Add(ruleText); left.Children.Add(context);
            left.Children.Add(new TextBlock { Text = "Suggestions:", Margin = new Thickness(0, 8, 0, 2) }); left.Children.Add(sugg);
            left.Children.Add(checkGrammar);
            var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(left); row.Children.Add(right);
            ProofIssue cur = null;
            TextPointer cursor = Editor.CaretPosition;
            bool wrapped = Editor.CaretPosition.CompareTo(Editor.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward)) <= 0;
            int changes = 0;
            Button B(string text, Action a) { var b = UI.Btn(text, (s, e2) => a()); b.Margin = new Thickness(0, 0, 0, 4); b.HorizontalAlignment = HorizontalAlignment.Stretch; right.Children.Add(b); return b; }
            Button addBtn = null, ignoreAll = null;
            void ShowIssue()
            {
                cur = NextIssue(cursor, checkGrammar.IsChecked == true);
                if (cur == null && !wrapped)
                {
                    wrapped = true;
                    cursor = Editor.Document.ContentStart;
                    cur = NextIssue(cursor, checkGrammar.IsChecked == true);
                }
                if (cur == null)
                {
                    d.Close();
                    VistaDialog.Show(this, changes > 0 ? $"The spelling and grammar check is complete. {changes} change{(changes == 1 ? " was" : "s were")} made." : "The spelling and grammar check is complete.", "Valco Word", MessageBoxButton.OK, DlgIcon.Info);
                    UpdateSpellStatus();
                    return;
                }
                Editor.Selection.Select(cur.Range.Start, cur.Range.End);
                BringSelectionIntoView();
                ruleText.Text = cur.Rule + ":";
                ruleText.Foreground = cur.Grammar ? new SolidColorBrush(Color.FromRgb(0x00, 0x80, 0x00)) : new SolidColorBrush(Color.FromRgb(0xC0, 0x00, 0x00));
                // context: the sentence (or paragraph) around the issue, with the issue highlighted
                var para = cur.Range.Start.Paragraph;
                var all = para == null ? cur.Range.Text : Spans.Text(para);
                var before = para == null ? "" : new TextRange(para.ContentStart, cur.Range.Start).Text;
                var bad = cur.Range.Text;
                var after = para == null ? "" : new TextRange(cur.Range.End, para.ContentEnd).Text;
                if (before.Length > 160) before = "…" + before.Substring(before.Length - 160);
                if (after.Length > 160) after = after.Substring(0, 160) + "…";
                var cp = new Paragraph { Margin = new Thickness(0) };
                cp.Inlines.Add(new Run(before));
                cp.Inlines.Add(new Run(bad) { Foreground = ruleText.Foreground, FontWeight = FontWeights.Bold });
                cp.Inlines.Add(new Run(after));
                context.Document = new FlowDocument(cp) { PagePadding = new Thickness(4) };
                sugg.ItemsSource = cur.Suggestions.Count > 0 ? cur.Suggestions : new List<string> { "(No Suggestions)" };
                sugg.SelectedIndex = cur.Suggestions.Count > 0 ? 0 : -1;
                addBtn.IsEnabled = !cur.Grammar;
                ignoreAll.Content = cur.Grammar ? "Ignore Rule" : "Ignore All";
            }
            string Replacement()
            {
                // the user may have typed a correction straight into the context box
                var edited = new TextRange(context.Document.ContentStart, context.Document.ContentEnd).Text.TrimEnd('\r', '\n');
                var para = cur.Range.Start.Paragraph;
                var original = para == null ? cur.Range.Text : Spans.Text(para);
                if (sugg.SelectedItem is string s && s != "(No Suggestions)") return s;
                return null;
            }
            void Apply(string with, bool all)
            {
                if (cur == null || with == null) { Advance(); return; }
                var word = cur.Range.Text;
                Editor.BeginChange();
                if (all && !cur.Grammar) changes += ReplaceAll(word, with, true, true);
                else
                {
                    var r = cur.Range;
                    var fmtRun = Fmt.AncestorAt<Run>(r.Start);
                    r.Text = with;
                    changes++;
                    cursor = r.End;
                }
                Editor.EndChange();
                MarkDirty();
                ShowIssue();
            }
            void Advance() { if (cur != null) cursor = cur.Range.End; ShowIssue(); }
            B("Ignore Once", () => { if (cur?.Grammar == true) _ignoredGrammar.Add(cur.Rule + "|" + cur.Range.Text + "|" + "x"); Advance(); });
            ignoreAll = B("Ignore All", () =>
            {
                if (cur == null) return;
                if (cur.Grammar) _ignoredGrammar.Add(cur.Rule + "|*"); else { _ignoredWords.Add(cur.Range.Text); cur.Spelling?.IgnoreAll(); }
                Advance();
            });
            addBtn = B("Add to Dictionary", () => { if (cur != null && !cur.Grammar) { AddToDictionary(cur.Range.Text); _ignoredWords.Add(cur.Range.Text); } Advance(); });
            right.Children.Add(new Border { Height = 8 });
            B("Change", () => Apply(Replacement(), false));
            B("Change All", () => Apply(Replacement(), true));
            right.Children.Add(new Border { Height = 8 });
            B("Options...", () => WordOptions("Proofing"));
            B("Undo", () => { if (Editor.CanUndo) { Editor.Undo(); changes = Math.Max(0, changes - 1); cursor = Editor.Selection.Start; ShowIssue(); } });
            sugg.MouseDoubleClick += (s, e2) => Apply(Replacement(), false);
            checkGrammar.Click += (s, e2) => { Settings.Current.GrammarCheck = checkGrammar.IsChecked == true; Settings.Save(); ShowIssue(); };
            d.Content = UI.DialogLayout(row, UI.Strip(UI.Btn("Close", (s, e2) => d.Close(), false, true)));
            d.Loaded += (s, e2) => ShowIssue();
            d.ShowDialog();
        }

        void UpdateSpellStatus()
        {
            if (SpellStatusIcon == null) return;
            bool errors = SpellCheck.GetIsEnabled(Editor) && Editor.GetNextSpellingErrorPosition(Editor.Document.ContentStart, LogicalDirection.Forward) != null;
            SpellStatusIcon.Opacity = errors ? 1 : 0.6;
            BtnSpellStatus.ToolTip = errors ? "Proofing errors were found. Click to correct." : "No proofing errors";
        }

        // =====================================================================================
        //  Research pane: research, thesaurus, translate
        // =====================================================================================
        static Dictionary<string, string> _thesaurus;
        static Dictionary<string, string> Thesaurus
        {
            get
            {
                if (_thesaurus != null) return _thesaurus;
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    using var s = typeof(MainWindow).Assembly.GetManifestResourceStream("thesaurus.gz");
                    if (s != null)
                    {
                        using var gz = new GZipStream(s, CompressionMode.Decompress);
                        using var rd = new StreamReader(gz, Encoding.UTF8);
                        string line;
                        while ((line = rd.ReadLine()) != null) { int t = line.IndexOf('\t'); if (t > 0) dict[line.Substring(0, t)] = line.Substring(t + 1); }
                    }
                }
                catch { }
                return _thesaurus = dict;
            }
        }

        static string Lemma(string w)
        {
            w = w.Trim().ToLowerInvariant();
            if (Thesaurus.ContainsKey(w)) return w;
            foreach (var (suf, rep) in new[] { ("ies", "y"), ("es", ""), ("s", ""), ("ied", "y"), ("ed", "e"), ("ed", ""), ("ing", "e"), ("ing", ""), ("ly", ""), ("er", ""), ("est", ""), ("ier", "y"), ("iest", "y") })
                if (w.EndsWith(suf) && w.Length > suf.Length + 2 && Thesaurus.ContainsKey(w.Substring(0, w.Length - suf.Length) + rep)) return w.Substring(0, w.Length - suf.Length) + rep;
            // doubled consonant: running -> run
            if (w.EndsWith("ing") && w.Length > 5 && w[w.Length - 4] == w[w.Length - 5] && Thesaurus.ContainsKey(w.Substring(0, w.Length - 4))) return w.Substring(0, w.Length - 4);
            return w;
        }

        string WordAtCaret()
        {
            if (!Editor.Selection.IsEmpty) return Editor.Selection.Text.Trim();
            var p = Editor.CaretPosition;
            string back = p.GetTextInRun(LogicalDirection.Backward), fwd = p.GetTextInRun(LogicalDirection.Forward);
            var a = new string(back.Reverse().TakeWhile(ch => char.IsLetter(ch) || ch == '\'' || ch == '-').Reverse().ToArray());
            var b = new string(fwd.TakeWhile(ch => char.IsLetter(ch) || ch == '\'' || ch == '-').ToArray());
            return (a + b).Trim('\'', '-');
        }

        void SelectWordAtCaret()
        {
            if (!Editor.Selection.IsEmpty) return;
            var p = Editor.CaretPosition;
            string back = p.GetTextInRun(LogicalDirection.Backward), fwd = p.GetTextInRun(LogicalDirection.Forward);
            int a = back.Reverse().TakeWhile(ch => char.IsLetter(ch) || ch == '\'').Count(), b = fwd.TakeWhile(ch => char.IsLetter(ch) || ch == '\'').Count();
            var s = p.GetPositionAtOffset(-a); var e = p.GetPositionAtOffset(b);
            if (s != null && e != null) Editor.Selection.Select(s, e);
        }

        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        static readonly (string Name, string Code)[] Languages =
        {
            ("English", "en"), ("French", "fr"), ("Spanish", "es"), ("German", "de"), ("Italian", "it"), ("Portuguese", "pt"), ("Dutch", "nl"), ("Swedish", "sv"), ("Polish", "pl"),
            ("Russian", "ru"), ("Ukrainian", "uk"), ("Greek", "el"), ("Turkish", "tr"), ("Arabic", "ar"), ("Hebrew", "he"), ("Hindi", "hi"), ("Chinese (Simplified)", "zh-CN"),
            ("Chinese (Traditional)", "zh-TW"), ("Japanese", "ja"), ("Korean", "ko"), ("Vietnamese", "vi"), ("Thai", "th"), ("Indonesian", "id"), ("Tagalog", "tl"),
        };

        void ShowResearchPane(string mode)
        {
            var term = WordAtCaret();
            var search = new TextBox { Height = 23, VerticalContentAlignment = VerticalAlignment.Center, Text = term };
            var service = new ComboBox { ItemsSource = new[] { "Research (Wikipedia)", "Thesaurus: English", "Translation" }, SelectedIndex = mode == "Thesaurus" ? 1 : mode == "Translate" ? 2 : 0, Margin = new Thickness(0, 4, 0, 4) };
            var from = new ComboBox { ItemsSource = Languages.Select(l => l.Name).ToList(), SelectedIndex = 0, Width = 110 };
            var to = new ComboBox { ItemsSource = Languages.Select(l => l.Name).ToList(), SelectedItem = Settings.Current.Custom.TryGetValue("translateTo", out var tt) ? tt : "French", Width = 110 };
            var langRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            langRow.Children.Add(new TextBlock { Text = "From ", VerticalAlignment = VerticalAlignment.Center }); langRow.Children.Add(from);
            langRow.Children.Add(new TextBlock { Text = " to ", VerticalAlignment = VerticalAlignment.Center }); langRow.Children.Add(to);
            var results = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            async void Go()
            {
                results.Children.Clear();
                langRow.Visibility = service.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
                var q = search.Text.Trim();
                if (q.Length == 0) { results.Children.Add(Note("Type a word or phrase, then press Enter.")); return; }
                switch (service.SelectedIndex)
                {
                    case 1: ShowThesaurus(q, results, w => { search.Text = w; Go(); }); break;
                    case 2:
                        {
                            results.Children.Add(Note("Translating…"));
                            var src = Languages[Math.Max(0, from.SelectedIndex)].Code; var dst = Languages[Math.Max(0, to.SelectedIndex)].Code;
                            Settings.Current.Custom["translateTo"] = (string)to.SelectedItem; Settings.Save();
                            try
                            {
                                var json = await Http.GetStringAsync($"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(q)}&langpair={src}|{dst}");
                                using var doc = JsonDocument.Parse(json);
                                var text = doc.RootElement.GetProperty("responseData").GetProperty("translatedText").GetString();
                                results.Children.Clear();
                                results.Children.Add(new TextBlock { Text = (string)to.SelectedItem, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x42, 0x8B)) });
                                results.Children.Add(new TextBox { Text = System.Net.WebUtility.HtmlDecode(text), TextWrapping = TextWrapping.Wrap, IsReadOnly = true, BorderThickness = new Thickness(0), Margin = new Thickness(0, 4, 0, 6) });
                                var ins = UI.Btn("Insert", (s, e) => { if (Editor.Selection.IsEmpty) InsertText(System.Net.WebUtility.HtmlDecode(text)); else { Editor.Selection.Text = System.Net.WebUtility.HtmlDecode(text); MarkDirty(); } });
                                ins.HorizontalAlignment = HorizontalAlignment.Left;
                                results.Children.Add(ins);
                                results.Children.Add(Note("Translation by MyMemory (online). Machine translation can be inaccurate."));
                            }
                            catch (Exception ex) { results.Children.Clear(); results.Children.Add(Note("The translation service couldn't be reached: " + ex.Message)); }
                            break;
                        }
                    default:
                        {
                            results.Children.Add(Note("Searching…"));
                            try
                            {
                                var sj = await Http.GetStringAsync($"https://en.wikipedia.org/w/api.php?action=query&list=search&format=json&srlimit=6&srsearch={Uri.EscapeDataString(q)}");
                                using var sdoc = JsonDocument.Parse(sj);
                                var hits = sdoc.RootElement.GetProperty("query").GetProperty("search").EnumerateArray().Select(h => h.GetProperty("title").GetString()).ToList();
                                results.Children.Clear();
                                if (hits.Count == 0) { results.Children.Add(Note("No results were found.")); break; }
                                results.Children.Add(new TextBlock { Text = "Wikipedia", FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x42, 0x8B)) });
                                foreach (var title in hits)
                                {
                                    var t = title;
                                    var exp = new Expander { Header = t, Margin = new Thickness(0, 2, 0, 2) };
                                    var body = new StackPanel();
                                    exp.Content = body;
                                    bool loaded = false;
                                    exp.Expanded += async (s, e) =>
                                    {
                                        if (loaded) return; loaded = true;
                                        body.Children.Add(Note("Loading…"));
                                        try
                                        {
                                            var js = await Http.GetStringAsync("https://en.wikipedia.org/api/rest_v1/page/summary/" + Uri.EscapeDataString(t.Replace(' ', '_')));
                                            using var jd = JsonDocument.Parse(js);
                                            var extract = jd.RootElement.TryGetProperty("extract", out var ex2) ? ex2.GetString() : "";
                                            var url = jd.RootElement.TryGetProperty("content_urls", out var cu) ? cu.GetProperty("desktop").GetProperty("page").GetString() : null;
                                            body.Children.Clear();
                                            body.Children.Add(new TextBox { Text = extract, TextWrapping = TextWrapping.Wrap, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent });
                                            var bar = new WrapPanel();
                                            var ins = UI.Btn("Insert Summary", (o, a) => { InsertText(extract + " (Wikipedia, \"" + t + "\")"); }); ins.Margin = new Thickness(0, 4, 4, 0);
                                            bar.Children.Add(ins);
                                            if (url != null) { var op = UI.Btn("Open", (o, a) => OpenLink(new Uri(url))); op.Margin = new Thickness(0, 4, 0, 0); bar.Children.Add(op); }
                                            body.Children.Add(bar);
                                        }
                                        catch (Exception ex) { body.Children.Clear(); body.Children.Add(Note("Couldn't load this article: " + ex.Message)); }
                                    };
                                    results.Children.Add(exp);
                                }
                                if (results.Children.Count > 1) ((Expander)results.Children[1]).IsExpanded = true;
                            }
                            catch (Exception ex) { results.Children.Clear(); results.Children.Add(Note("The research service couldn't be reached: " + ex.Message)); }
                            break;
                        }
                }
            }
            search.KeyDown += (s, e) => { if (e.Key == Key.Enter) Go(); };
            service.SelectionChanged += (s, e) => Go();
            from.SelectionChanged += (s, e) => { if (service.SelectedIndex == 2) Go(); };
            to.SelectionChanged += (s, e) => { if (service.SelectedIndex == 2) Go(); };
            var go = UI.Btn("Go", (s, e) => Go()); go.MinWidth = 36; go.Margin = new Thickness(4, 0, 0, 0);
            var row = new DockPanel(); DockPanel.SetDock(go, Dock.Right); row.Children.Add(go); row.Children.Add(search);
            var sp = new StackPanel { Margin = new Thickness(6) };
            sp.Children.Add(new TextBlock { Text = "Search for:" }); sp.Children.Add(row); sp.Children.Add(service); sp.Children.Add(langRow); sp.Children.Add(results);
            ShowRightPane("research", "Research", sp, null, 270);
            Go();
        }

        static TextBlock Note(string text) => new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 4, 0, 4) };

        void ShowThesaurus(string q, Panel results, Action<string> lookUp)
        {
            var key = Lemma(q);
            if (!Thesaurus.TryGetValue(key, out var raw))
            {
                results.Children.Add(Note($"No results were found for \"{q}\"."));
                var close = Thesaurus.Keys.Where(k => k.StartsWith(key.Substring(0, Math.Min(3, key.Length)))).Take(8).ToList();
                if (close.Count > 0) { results.Children.Add(Note("Did you mean:")); foreach (var c in close) results.Children.Add(WordLink(c, lookUp)); }
                return;
            }
            results.Children.Add(new TextBlock { Text = "Thesaurus: English", FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x42, 0x8B)) });
            foreach (var sense in raw.Split('\x1e'))
            {
                var parts = sense.Split('\x1f');
                if (parts.Length < 4) continue;
                var syns = parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries);
                var ants = parts[3].Split(',', StringSplitOptions.RemoveEmptyEntries);
                var exp = new Expander { IsExpanded = true, Margin = new Thickness(0, 4, 0, 0), Header = new TextBlock { Text = (syns.FirstOrDefault() ?? q) + " (" + parts[0] + ")", FontWeight = FontWeights.SemiBold } };
                var body = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
                if (parts[1].Length > 0) body.Children.Add(new TextBlock { Text = parts[1], FontStyle = FontStyles.Italic, Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2) });
                foreach (var s in syns) body.Children.Add(WordLink(s, lookUp));
                foreach (var a in ants) body.Children.Add(WordLink(a + " (Antonym)", lookUp, a));
                exp.Content = body;
                results.Children.Add(exp);
            }
            results.Children.Add(Note("Thesaurus data: WordNet 3.0, Princeton University."));
        }

        UIElement WordLink(string label, Action<string> lookUp, string word = null)
        {
            word ??= label;
            var b = new Button { Style = RB.BtnStyle, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(3, 1, 3, 1), Content = new TextBlock { Text = label, Foreground = Brushes.Black } };
            var w = word;
            b.Click += (s, e) => lookUp(w);
            b.ContextMenu = RB.Menu(RB.MI("Insert", () => ReplaceWordAtCaret(w)), RB.MI("Copy", () => Clipboard.SetText(w)), RB.MI("Look Up", () => lookUp(w)));
            b.MouseDoubleClick += (s, e) => ReplaceWordAtCaret(w);
            b.ToolTip = "Click to look up; double-click or right-click > Insert to replace the selected word";
            return b;
        }

        void ReplaceWordAtCaret(string w)
        {
            SelectWordAtCaret();
            Editor.BeginChange();
            if (Editor.Selection.IsEmpty) Editor.CaretPosition.InsertTextInRun(w);
            else
            {
                var orig = Editor.Selection.Text;
                if (orig.Length > 0 && char.IsUpper(orig[0])) w = char.ToUpper(w[0]) + w.Substring(1);
                Editor.Selection.Text = w;
            }
            Editor.EndChange();
            MarkDirty(); Editor.Focus();
        }

        // =====================================================================================
        //  Comments
        // =====================================================================================
        static readonly Color CommentColor = Color.FromRgb(0xF6, 0xD9, 0xE0);
        int _nextCommentId = 1;
        bool _showComments = true, _showInsDel = true;

        IEnumerable<Span> CommentSpans() => Spans.WithTag<CommentInfo>(Editor.Document);

        void NewComment()
        {
            if (_ex.Protected && ProtectMode == "read") { Flash("This document is protected."); return; }
            bool ro = Editor.IsReadOnly; Editor.IsReadOnly = false;
            if (Editor.Selection.IsEmpty) SelectWordAtCaret();
            var sel = Editor.Selection;
            if (sel.IsEmpty || sel.Start.Paragraph != sel.End.Paragraph)
            {
                var p = sel.Start.Paragraph;
                if (p == null) { Editor.IsReadOnly = ro; return; }
                if (sel.Start.Paragraph != sel.End.Paragraph) sel.Select(sel.Start, p.ContentEnd);
                if (sel.IsEmpty) { sel.Select(p.ContentStart, p.ContentEnd); }
            }
            Editor.BeginChange();
            var ci = new CommentInfo { Id = _nextCommentId++, Author = ReviewerName, Initials = ReviewerInitials, Date = DateTime.Now, Text = "" };
            Span span;
            if (sel.IsEmpty) span = new Span(new Run("​"), sel.Start.GetInsertionPosition(LogicalDirection.Forward));
            else span = new Span(sel.Start, sel.End);
            span.Tag = ci;
            if (_showComments) span.Background = new SolidColorBrush(CommentColor);
            Editor.EndChange();
            Editor.IsReadOnly = ro;
            MarkDirty();
            ShowCommentsPane(ci);
        }

        void ShowCommentsPane(CommentInfo focus = null)
        {
            var spans = CommentSpans().ToList();
            if (spans.Count == 0) { if (_rightPaneKind == "comments") HideRightPane(); return; }
            var sp = new StackPanel { Margin = new Thickness(4) };
            TextBox focusBox = null;
            foreach (var s in spans)
            {
                var span = s; var ci = (CommentInfo)s.Tag;
                var box = new TextBox { Text = ci.Text, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, MinHeight = 20 };
                box.TextChanged += (o, e) => { ci.Text = box.Text; MarkDirty(); };
                box.GotFocus += (o, e) => { Editor.Selection.Select(span.ContentStart, span.ContentEnd); BringSelectionIntoView(); };
                var head = new DockPanel();
                var del = new Button { Style = RB.BtnStyle, Content = new TextBlock { Text = "✕", Foreground = Brushes.DimGray, FontSize = 10 }, Width = 16, Height = 16, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center, ToolTip = "Delete comment" };
                del.Click += (o, e) => { RemoveComment(span); ShowCommentsPane(); };
                DockPanel.SetDock(del, Dock.Right); head.Children.Add(del);
                head.Children.Add(new TextBlock { Text = $"Comment [{ci.Initials}{spans.IndexOf(s) + 1}]  {ci.Author} · {ci.Date:g}", FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x2B, 0x45)) });
                var quote = new TextBlock { Text = "\"" + Truncate(Spans.Text(span).Replace("​", ""), 60) + "\"", FontSize = 10.5, Foreground = Brushes.DimGray, TextTrimming = TextTrimming.CharacterEllipsis };
                var inner = new StackPanel(); inner.Children.Add(head); inner.Children.Add(quote); inner.Children.Add(box);
                var balloon = new Border { Background = new SolidColorBrush(Color.FromRgb(0xFB, 0xEC, 0xF0)), BorderBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0x7C, 0x93)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 6), Child = inner };
                sp.Children.Add(balloon);
                if (ci == focus) focusBox = box;
            }
            var footer = new StackPanel { Orientation = Orientation.Horizontal };
            footer.Children.Add(RB.MenuButton("New Comment", NewComment, "IconComment"));
            footer.Children.Add(RB.MenuButton("Delete All", DeleteAllComments, "IconDeleteComment"));
            ShowRightPane("comments", "Comments", sp, footer, 250);
            if (focusBox != null) Dispatcher.BeginInvoke(new Action(() => focusBox.Focus()), System.Windows.Threading.DispatcherPriority.Input);
        }

        static string Truncate(string s, int n) => s.Length > n ? s.Substring(0, n) + "…" : s;

        void RemoveComment(Span s)
        {
            Editor.BeginChange();
            foreach (var r in s.Inlines.OfType<Run>().Where(r => r.Text == "​").ToList()) s.Inlines.Remove(r);
            if (s.Inlines.Count == 0) s.SiblingInlines.Remove(s); else Spans.Unwrap(s);
            Editor.EndChange();
            MarkDirty();
        }

        void DeleteComment()
        {
            var s = Fmt.AncestorAt<Span>(Editor.CaretPosition);
            while (s != null && !(s.Tag is CommentInfo)) s = s.Parent as Span;
            s ??= CommentSpans().FirstOrDefault(c => Editor.Selection.Start.CompareTo(c.ContentEnd) <= 0 && Editor.Selection.End.CompareTo(c.ContentStart) >= 0);
            if (s == null) { Flash("Click inside a comment's text first."); return; }
            RemoveComment(s);
            ShowCommentsPane();
        }

        void DeleteAllComments()
        {
            var all = CommentSpans().ToList();
            if (all.Count == 0) { Flash("There are no comments in this document."); return; }
            foreach (var s in all) RemoveComment(s);
            ShowCommentsPane();
            Flash($"Deleted {all.Count} comment{(all.Count == 1 ? "" : "s")}.");
        }

        void GoToComment(int dir)
        {
            var all = CommentSpans().ToList();
            if (all.Count == 0) { Flash("There are no comments in this document."); return; }
            var caret = Editor.Selection.Start;
            var t = dir > 0 ? all.FirstOrDefault(s => s.ContentStart.CompareTo(caret) > 0) : all.LastOrDefault(s => s.ContentStart.CompareTo(caret) < 0);
            t ??= dir > 0 ? all[0] : all[all.Count - 1];
            Editor.Selection.Select(t.ContentStart, t.ContentEnd);
            BringSelectionIntoView();
            ShowCommentsPane((CommentInfo)t.Tag);
        }

        // =====================================================================================
        //  Track changes
        // =====================================================================================
        int _markupMode;
        static readonly ConditionalWeakTable<Run, object> HiddenRunSizes = new ConditionalWeakTable<Run, object>();

        void SetTrackChanges(bool on)
        {
            if (!on && _ex.Protected && ProtectMode == "tracked") { Flash("Track Changes can't be turned off while the document is protected for tracked changes."); on = true; }
            _ex.TrackChanges = on;
            if (TbTrack != null) TbTrack.IsChecked = on;
            if (TrackStatusText != null) TrackStatusText.Text = "Track Changes: " + (on ? "On" : "Off");
            MarkDirty();
        }

        void TrackStatus_Click(object sender, RoutedEventArgs e) => SetTrackChanges(!_ex.TrackChanges);

        void StyleRevision(Span s)
        {
            var rev = (RevInfo)s.Tag;
            bool hide = (rev.Insert && _markupMode == 3) || (!rev.Insert && _markupMode == 1);
            bool plain = !_showInsDel || (rev.Insert && _markupMode == 1) || (!rev.Insert && _markupMode == 3);
            foreach (var r in Fmt.Descendants(s).OfType<Run>())
            {
                if (hide)
                {
                    if (!HiddenRunSizes.TryGetValue(r, out _)) HiddenRunSizes.Add(r, r.ReadLocalValue(TextElement.FontSizeProperty));
                    r.FontSize = 0.01;
                }
                else if (HiddenRunSizes.TryGetValue(r, out var old))
                {
                    if (old == DependencyProperty.UnsetValue) r.ClearValue(TextElement.FontSizeProperty); else r.FontSize = (double)old;
                    HiddenRunSizes.Remove(r);
                }
            }
            if (plain || hide) { s.ClearValue(TextElement.ForegroundProperty); s.ClearValue(Inline.TextDecorationsProperty); }
            else
            {
                s.Foreground = new SolidColorBrush(RevColor(rev.Author));
                s.TextDecorations = rev.Insert ? TextDecorations.Underline : TextDecorations.Strikethrough;
            }
        }

        static readonly Color[] ReviewerColors = { Color.FromRgb(0xC0, 0x00, 0x00), Color.FromRgb(0x00, 0x4F, 0xC0), Color.FromRgb(0x00, 0x80, 0x00), Color.FromRgb(0x80, 0x00, 0x80), Color.FromRgb(0xC0, 0x60, 0x00), Color.FromRgb(0x00, 0x80, 0x80) };
        readonly List<string> _reviewers = new List<string>();
        Color RevColor(string author)
        {
            if (!_reviewers.Contains(author ?? "")) _reviewers.Add(author ?? "");
            return ReviewerColors[_reviewers.IndexOf(author ?? "") % ReviewerColors.Length];
        }

        void ApplyMarkupView(int mode)
        {
            _markupMode = mode;
            bool was = _loading; _loading = true;
            foreach (var s in Spans.WithTag<RevInfo>(Editor.Document).ToList()) StyleRevision(s);
            _loading = was;
            if (CbMarkup != null && CbMarkup.SelectedIndex != mode) { _updatingUI = true; CbMarkup.SelectedIndex = mode; _updatingUI = false; }
            ScheduleLayout();
        }

        static bool IsRev(TextPointer p, bool insert, out Span span)
        {
            span = null;
            for (DependencyObject d = p.Parent; d != null; d = (d as TextElement)?.Parent)
                if (d is Span s && s.Tag is RevInfo ri && ri.Insert == insert) { span = s; return true; }
            return false;
        }

        /// <summary>Typing with Track Changes on: the new text goes into an insertion span.</summary>
        void TrackedInsert(string text)
        {
            Editor.BeginChange();
            try
            {
                if (!Editor.Selection.IsEmpty)
                {
                    var end = Editor.Selection.End;
                    var endOffset = Editor.Document.ContentStart.GetOffsetToPosition(end);
                    TrackedDelete(Editor.Selection.Start, end);
                    Editor.CaretPosition = Editor.Document.ContentStart.GetPositionAtOffset(endOffset) ?? Editor.Selection.End;
                }
                var pos = Editor.CaretPosition;
                // never type inside a deletion
                if (IsRev(pos, false, out var del)) pos = del.ElementEnd;
                if (IsRev(pos, true, out var ins) && ((RevInfo)ins.Tag).Author == ReviewerName)
                {
                    pos.InsertTextInRun(text);
                    Editor.CaretPosition = pos.GetPositionAtOffset(text.Length, LogicalDirection.Forward) ?? pos;
                    return;
                }
                var ip = pos.GetInsertionPosition(LogicalDirection.Forward);
                var span = new Span(new Run(text), ip) { Tag = new RevInfo { Insert = true, Author = ReviewerName, Date = DateTime.Now } };
                StyleRevision(span);
                Editor.CaretPosition = span.ContentEnd;
            }
            finally { Editor.EndChange(); MarkDirty(); }
        }

        /// <summary>Deleting with Track Changes on: text is wrapped in deletion spans (your own insertions are really removed).</summary>
        void TrackedDelete(TextPointer start, TextPointer end)
        {
            if (start.CompareTo(end) >= 0) return;
            var segs = new List<(TextPointer S, TextPointer E)>();
            var nav = start;
            while (nav != null && nav.CompareTo(end) < 0)
            {
                if (nav.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    var run = nav.GetTextInRun(LogicalDirection.Forward);
                    var segEnd = nav.GetPositionAtOffset(run.Length);
                    if (segEnd.CompareTo(end) > 0) segEnd = end;
                    segs.Add((nav, segEnd));
                    nav = segEnd;
                    if (nav.CompareTo(end) >= 0) break;
                }
                nav = nav.GetNextContextPosition(LogicalDirection.Forward);
            }
            for (int i = segs.Count - 1; i >= 0; i--)
            {
                var (s, e) = segs[i];
                if (IsRev(s, false, out _)) continue;
                if (IsRev(s, true, out var ins) && ((RevInfo)ins.Tag).Author == ReviewerName) { new TextRange(s, e).Text = ""; if (Spans.Text(ins).Length == 0) ins.SiblingInlines.Remove(ins); continue; }
                if (s.Paragraph == null || s.Paragraph != e.Paragraph) continue;
                var span = new Span(s, e) { Tag = new RevInfo { Insert = false, Author = ReviewerName, Date = DateTime.Now } };
                StyleRevision(span);
            }
        }

        /// <summary>Wraps freshly pasted content in insertion spans.</summary>
        void TrackedMarkInserted(TextPointer start, TextPointer end)
        {
            var segs = new List<(TextPointer S, TextPointer E)>();
            var nav = start;
            while (nav != null && nav.CompareTo(end) < 0)
            {
                if (nav.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    var run = nav.GetTextInRun(LogicalDirection.Forward);
                    var segEnd = nav.GetPositionAtOffset(run.Length);
                    if (segEnd.CompareTo(end) > 0) segEnd = end;
                    segs.Add((nav, segEnd));
                    nav = segEnd;
                    if (nav.CompareTo(end) >= 0) break;
                }
                nav = nav.GetNextContextPosition(LogicalDirection.Forward);
            }
            for (int i = segs.Count - 1; i >= 0; i--)
            {
                var (s, e) = segs[i];
                if (IsRev(s, true, out _) || s.Paragraph == null || s.Paragraph != e.Paragraph) continue;
                StyleRevision(new Span(s, e) { Tag = new RevInfo { Insert = true, Author = ReviewerName, Date = DateTime.Now } });
            }
        }

        /// <summary>Hooks keyboard/paste editing when Track Changes is on. Returns true when the key was handled.</summary>
        bool TrackKey(KeyEventArgs e)
        {
            if (!_ex.TrackChanges || Editor.IsReadOnly) return false;
            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            if (e.Key == Key.Back || e.Key == Key.Delete)
            {
                Editor.BeginChange();
                try
                {
                    var sel = Editor.Selection;
                    if (!sel.IsEmpty) { var st = sel.Start; TrackedDelete(sel.Start, sel.End); Editor.CaretPosition = e.Key == Key.Back ? st : Editor.Selection.End; }
                    else
                    {
                        var caret = Editor.CaretPosition;
                        if (e.Key == Key.Back)
                        {
                            var prev = caret.GetNextInsertionPosition(LogicalDirection.Backward);
                            while (prev != null && IsRev(prev, false, out var d0) && prev.CompareTo(d0.ContentStart) > 0) prev = d0.ElementStart.GetInsertionPosition(LogicalDirection.Backward).GetNextInsertionPosition(LogicalDirection.Backward);
                            if (prev == null) return true;
                            if (prev.Paragraph != caret.Paragraph) { Editor.CaretPosition = prev; return true; }
                            var mark = new TextRange(prev, caret);
                            if (ctrl) { var w = WordStartBefore(caret); if (w != null) mark = new TextRange(w, caret); }
                            TrackedDelete(mark.Start, mark.End);
                            Editor.CaretPosition = mark.Start;
                        }
                        else
                        {
                            var next = caret.GetNextInsertionPosition(LogicalDirection.Forward);
                            if (next == null) return true;
                            if (next.Paragraph != caret.Paragraph) { Editor.CaretPosition = next; return true; }
                            TrackedDelete(caret, next);
                            Editor.CaretPosition = next;
                        }
                    }
                }
                finally { Editor.EndChange(); MarkDirty(); }
                return true;
            }
            if (ctrl && e.Key == Key.X) { if (!Editor.Selection.IsEmpty) { Editor.Copy(); RememberClipboard(); Editor.BeginChange(); TrackedDelete(Editor.Selection.Start, Editor.Selection.End); Editor.EndChange(); MarkDirty(); } return true; }
            if (ctrl && e.Key == Key.V) { TrackedPaste(); return true; }
            return false;
        }

        static TextPointer WordStartBefore(TextPointer p)
        {
            var back = p.GetTextInRun(LogicalDirection.Backward);
            int n = back.Reverse().SkipWhile(char.IsWhiteSpace).TakeWhile(ch => !char.IsWhiteSpace(ch)).Count() + back.Reverse().TakeWhile(char.IsWhiteSpace).Count();
            return n == 0 ? null : p.GetPositionAtOffset(-n);
        }

        void TrackedPaste()
        {
            Editor.BeginChange();
            try
            {
                if (!Editor.Selection.IsEmpty) { var end = Editor.Selection.End; TrackedDelete(Editor.Selection.Start, end); Editor.CaretPosition = Editor.Selection.End; }
                var startOffset = Editor.Document.ContentStart.GetOffsetToPosition(Editor.CaretPosition);
                var endFromDocEnd = Editor.CaretPosition.GetOffsetToPosition(Editor.Document.ContentEnd);
                Editor.Paste();
                var s = Editor.Document.ContentStart.GetPositionAtOffset(startOffset);
                var e = Editor.Document.ContentEnd.GetPositionAtOffset(-endFromDocEnd);
                if (s != null && e != null && s.CompareTo(e) < 0) TrackedMarkInserted(s, e);
            }
            finally { Editor.EndChange(); MarkDirty(); }
        }

        void AcceptReject(bool accept, bool all, bool moveNext = true)
        {
            var revs = Spans.WithTag<RevInfo>(Editor.Document).ToList();
            if (revs.Count == 0) { Flash("There are no tracked changes in this document."); return; }
            var sel = Editor.Selection;
            var targets = all ? revs : revs.Where(s => sel.Start.CompareTo(s.ContentEnd) <= 0 && sel.End.CompareTo(s.ContentStart) >= 0).ToList();
            if (targets.Count == 0)
            {
                // nothing at the caret: jump to the next change instead
                GoToChange(1);
                return;
            }
            var lastEnd = targets.Last().ElementEnd;
            Editor.BeginChange();
            foreach (var s in targets)
            {
                var rev = (RevInfo)s.Tag;
                foreach (var r in Fmt.Descendants(s).OfType<Run>()) if (HiddenRunSizes.TryGetValue(r, out var old)) { if (old == DependencyProperty.UnsetValue) r.ClearValue(TextElement.FontSizeProperty); else r.FontSize = (double)old; HiddenRunSizes.Remove(r); }
                bool keep = accept == rev.Insert;
                if (keep) { s.ClearValue(TextElement.ForegroundProperty); s.ClearValue(Inline.TextDecorationsProperty); Spans.Unwrap(s); }
                else s.SiblingInlines.Remove(s);
            }
            Editor.EndChange();
            MarkDirty(); ScheduleLayout();
            if (!all && moveNext) { Editor.CaretPosition = lastEnd.IsInSameDocument(Editor.Document.ContentStart) ? lastEnd : Editor.CaretPosition; GoToChange(1, false); }
            if (all) Flash(accept ? "All changes accepted." : "All changes rejected.");
        }

        void GoToChange(int dir) => GoToChange(dir, true);
        void GoToChange(int dir, bool wrap)
        {
            var revs = Spans.WithTag<RevInfo>(Editor.Document).ToList();
            if (revs.Count == 0) { Flash("The document contains no comments or tracked changes."); return; }
            var caret = dir > 0 ? Editor.Selection.End : Editor.Selection.Start;
            var t = dir > 0 ? revs.FirstOrDefault(s => s.ContentStart.CompareTo(caret) >= 0 && !(Editor.Selection.Start.CompareTo(s.ContentStart) == 0)) : revs.LastOrDefault(s => s.ContentEnd.CompareTo(caret) <= 0 && !(Editor.Selection.Start.CompareTo(s.ContentStart) == 0));
            if (t == null) { if (!wrap) return; t = dir > 0 ? revs[0] : revs[revs.Count - 1]; }
            Editor.Selection.Select(t.ContentStart, t.ContentEnd);
            BringSelectionIntoView();
        }

        ContextMenu ShowMarkupMenu()
        {
            var m = RB.Menu();
            m.Items.Add(RB.MI("Comments", () => { _showComments = !_showComments; foreach (var s in CommentSpans()) { if (_showComments) s.Background = new SolidColorBrush(CommentColor); else s.ClearValue(TextElement.BackgroundProperty); } if (!_showComments && _rightPaneKind == "comments") HideRightPane(); }, null, null, _showComments));
            m.Items.Add(RB.MI("Insertions and Deletions", () => { _showInsDel = !_showInsDel; ApplyMarkupView(_markupMode); }, null, null, _showInsDel));
            m.Items.Add(RB.MI("Formatting", null, null, null, true));
            var reviewers = new MenuItem { Header = "Reviewers" };
            var names = Spans.Tagged<RevInfo>(Editor.Document).Select(r => r.Author).Concat(Spans.Tagged<CommentInfo>(Editor.Document).Select(c => c.Author)).Distinct().ToList();
            reviewers.Items.Add(RB.MI("All Reviewers", null, null, null, true));
            foreach (var n in names) reviewers.Items.Add(new MenuItem { Header = n, IsChecked = true, Foreground = new SolidColorBrush(RevColor(n)) });
            m.Items.Add(reviewers);
            m.Items.Add(new Separator());
            m.Items.Add(RB.MI("Reviewing Pane", ShowReviewingPane, "IconDocMap"));
            return m;
        }

        void ShowReviewingPane()
        {
            var items = Spans.All(Editor.Document).Where(s => s.Tag is RevInfo || s.Tag is CommentInfo).ToList();
            var sp = new StackPanel { Margin = new Thickness(4) };
            int ins = items.Count(s => s.Tag is RevInfo r && r.Insert), del = items.Count(s => s.Tag is RevInfo r && !r.Insert), com = items.Count(s => s.Tag is CommentInfo);
            sp.Children.Add(new TextBlock { Text = $"{ins + del + com} revisions: {ins} insertions, {del} deletions, {com} comments", FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 6), TextWrapping = TextWrapping.Wrap });
            foreach (var s in items)
            {
                var span = s;
                string kind, who; DateTime when; string text = Spans.Text(s).Replace("​", "");
                if (s.Tag is RevInfo r) { kind = r.Insert ? "Inserted" : "Deleted"; who = r.Author; when = r.Date; }
                else { var c = (CommentInfo)s.Tag; kind = "Comment"; who = c.Author; when = c.Date; text = c.Text + "  (on: \"" + Truncate(text, 40) + "\")"; }
                var b = new Button { Style = RB.BtnStyle, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(4), Margin = new Thickness(0, 0, 0, 2) };
                var inner = new StackPanel();
                inner.Children.Add(new TextBlock { Text = $"{who}  {kind}  {when:g}", FontWeight = FontWeights.SemiBold, FontSize = 10.5, Foreground = new SolidColorBrush(kind == "Comment" ? Color.FromRgb(0x9A, 0x2B, 0x45) : RevColor(who)) });
                inner.Children.Add(new TextBlock { Text = Truncate(text, 200), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Black, TextDecorations = kind == "Deleted" ? TextDecorations.Strikethrough : null });
                b.Content = inner;
                b.Click += (o, e) => { Editor.Selection.Select(span.ContentStart, span.ContentEnd); BringSelectionIntoView(); Editor.Focus(); };
                sp.Children.Add(b);
            }
            if (items.Count == 0) sp.Children.Add(Note("There are no comments or tracked changes in this document."));
            ShowLeftPaneContent("review", "Reviewing Pane", new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        }

        // =====================================================================================
        //  Compare
        // =====================================================================================
        void CompareDocuments()
        {
            var d = new DialogBase(this, "Compare Documents", 520);
            var orig = D.Box("", 360); var rev = D.Box(_path ?? "", 360);
            Button Browse(TextBox target) => UI.Btn("...", (s, e) => { var o = new OpenFileDialog { Filter = DocFormats.OpenFilter }; if (o.ShowDialog(d) == true) target.Text = o.FileName; });
            var r1 = new StackPanel { Orientation = Orientation.Horizontal }; r1.Children.Add(orig); var b1 = Browse(orig); b1.MinWidth = 30; b1.Margin = new Thickness(4, 0, 0, 0); r1.Children.Add(b1);
            var r2 = new StackPanel { Orientation = Orientation.Horizontal }; r2.Children.Add(rev); var b2 = Browse(rev); b2.MinWidth = 30; b2.Margin = new Thickness(4, 0, 0, 0); r2.Children.Add(b2);
            var label = D.Box(ReviewerName, 200);
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = "Original document:" }); sp.Children.Add(r1);
            sp.Children.Add(new TextBlock { Text = "Revised document:", Margin = new Thickness(0, 8, 0, 0) }); sp.Children.Add(r2);
            sp.Children.Add(new TextBlock { Text = "Label changes with:", Margin = new Thickness(0, 8, 0, 0) }); sp.Children.Add(label);
            d.Content = UI.DialogLayout(sp, UI.Strip(UI.Btn("OK", (s, e) => { if (File.Exists(orig.Text) && (File.Exists(rev.Text))) d.DialogResult = true; else VistaDialog.Error(d, "Choose both documents."); }, true), UI.Btn("Cancel", null, false, true)));
            if (d.ShowDialog() != true) return;
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var a = DocFormats.Load(orig.Text); var b = DocFormats.Load(rev.Text);
                var result = CompareFlow(a.Document, b.Document, label.Text.Trim().Length > 0 ? label.Text.Trim() : "Revised", a.Author ?? "Original");
                var w = OpenNewWindow();
                w.LoadGenerated(result, b.Page, "Compare Result");
                w._ex.TrackChanges = false;
                w.ApplyMarkupView(0);
                w.ShowReviewingPane();
            }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message, "The documents couldn't be compared."); }
            finally { Mouse.OverrideCursor = null; }
        }

        /// <summary>Builds a document from the revised version with differences against the original marked as tracked changes.</summary>
        static FlowDocument CompareFlow(FlowDocument original, FlowDocument revised, string author, string origAuthor)
        {
            var A = Fmt.AllParagraphs(original.Blocks).Select(Spans.Text).ToList();
            var bParas = Fmt.AllParagraphs(revised.Blocks).ToList();
            var B = bParas.Select(Spans.Text).ToList();
            var ops = Diff(A, B, StringComparer.Ordinal);
            var now = DateTime.Now;
            int bi = 0;
            Paragraph lastB = null;
            var pendingDeletes = new List<string>();
            void FlushDeletes(Paragraph before)
            {
                foreach (var text in pendingDeletes)
                {
                    var p = new Paragraph();
                    p.Inlines.Add(new Span(new Run(text)) { Tag = new RevInfo { Insert = false, Author = author, Date = now } });
                    if (before != null) before.SiblingBlocks.InsertBefore(before, p);
                    else if (lastB != null) lastB.SiblingBlocks.InsertAfter(lastB, p);
                    else revised.Blocks.Add(p);
                }
                pendingDeletes.Clear();
            }
            // pair a run of deletions with a run of insertions as a modified paragraph when they look alike
            for (int i = 0; i < ops.Count; i++)
            {
                var op = ops[i];
                if (op.Kind == '=') { FlushDeletes(bParas[bi]); lastB = bParas[bi]; bi++; continue; }
                if (op.Kind == '-')
                {
                    if (i + 1 < ops.Count && ops[i + 1].Kind == '+' && Similar(op.Text, ops[i + 1].Text))
                    {
                        FlushDeletes(bParas[bi]);
                        WordDiffInto(bParas[bi], op.Text, author, now);
                        lastB = bParas[bi]; bi++; i++;
                        continue;
                    }
                    pendingDeletes.Add(op.Text); continue;
                }
                // insertion: wrap the whole revised paragraph
                FlushDeletes(bParas[bi]);
                var para = bParas[bi];
                var start = para.ContentStart.GetInsertionPosition(LogicalDirection.Forward); var end = para.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
                if (start.CompareTo(end) < 0)
                {
                    var kids = para.Inlines.ToList();
                    var span = new Span { Tag = new RevInfo { Insert = true, Author = author, Date = now } };
                    foreach (var k in kids) { para.Inlines.Remove(k); span.Inlines.Add(k); }
                    para.Inlines.Add(span);
                }
                lastB = para; bi++;
            }
            FlushDeletes(null);
            return revised;
        }

        static bool Similar(string a, string b)
        {
            var wa = a.Split(' ', StringSplitOptions.RemoveEmptyEntries); var wb = b.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (wa.Length == 0 || wb.Length == 0) return false;
            int common = Diff(wa.ToList(), wb.ToList(), StringComparer.Ordinal).Count(o => o.Kind == '=');
            return common * 2.0 / (wa.Length + wb.Length) >= 0.4;
        }

        /// <summary>Rebuilds a paragraph's text as a word-level diff against the original text (keeps the first run's formatting).</summary>
        static void WordDiffInto(Paragraph p, string original, string author, DateTime now)
        {
            var fmtRun = Fmt.Descendants(p).OfType<Run>().FirstOrDefault();
            var tokensA = Regex.Matches(original, @"\s+|[^\s]+").Select(m => m.Value).ToList();
            var tokensB = Regex.Matches(Spans.Text(p), @"\s+|[^\s]+").Select(m => m.Value).ToList();
            var ops = Diff(tokensA, tokensB, StringComparer.Ordinal);
            Run R(string t) { var r = new Run(t); if (fmtRun != null) { r.FontFamily = fmtRun.FontFamily; r.FontSize = fmtRun.FontSize; r.FontWeight = fmtRun.FontWeight; r.FontStyle = fmtRun.FontStyle; r.Foreground = fmtRun.Foreground; } return r; }
            p.Inlines.Clear();
            var buf = new StringBuilder(); char kind = '=';
            void Flush()
            {
                if (buf.Length == 0) return;
                if (kind == '=') p.Inlines.Add(R(buf.ToString()));
                else p.Inlines.Add(new Span(R(buf.ToString())) { Tag = new RevInfo { Insert = kind == '+', Author = author, Date = now } });
                buf.Clear();
            }
            foreach (var op in ops) { if (op.Kind != kind) { Flush(); kind = op.Kind; } buf.Append(op.Text); }
            Flush();
        }

        /// <summary>Line diff (LCS); returns '=' kept, '-' only in A, '+' only in B, in order.</summary>
        static List<(char Kind, string Text)> Diff(List<string> a, List<string> b, IEqualityComparer<string> cmp)
        {
            int n = a.Count, m = b.Count;
            var res = new List<(char, string)>();
            if ((long)n * m > 4_000_000)
            {
                // too large for LCS: fall back to prefix/suffix matching
                int pre = 0; while (pre < n && pre < m && cmp.Equals(a[pre], b[pre])) pre++;
                int suf = 0; while (suf < n - pre && suf < m - pre && cmp.Equals(a[n - 1 - suf], b[m - 1 - suf])) suf++;
                for (int i = 0; i < pre; i++) res.Add(('=', b[i]));
                for (int i = pre; i < n - suf; i++) res.Add(('-', a[i]));
                for (int i = pre; i < m - suf; i++) res.Add(('+', b[i]));
                for (int i = m - suf; i < m; i++) res.Add(('=', b[i]));
                return res;
            }
            var L = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--) for (int j = m - 1; j >= 0; j--) L[i, j] = cmp.Equals(a[i], b[j]) ? L[i + 1, j + 1] + 1 : Math.Max(L[i + 1, j], L[i, j + 1]);
            int x = 0, y = 0;
            while (x < n && y < m)
            {
                if (cmp.Equals(a[x], b[y])) { res.Add(('=', b[y])); x++; y++; }
                else if (L[x + 1, y] >= L[x, y + 1]) { res.Add(('-', a[x])); x++; }
                else { res.Add(('+', b[y])); y++; }
            }
            while (x < n) res.Add(('-', a[x++]));
            while (y < m) res.Add(('+', b[y++]));
            return res;
        }

        // =====================================================================================
        //  Protect, mark as final, inspect
        // =====================================================================================
        string ProtectMode => _ex.Custom.TryGetValue("protectMode", out var m) ? m : "read";

        static string Hash(string pw) => string.IsNullOrEmpty(pw) ? "" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("valco-word:" + pw)));

        void ProtectDocument()
        {
            var sp = new StackPanel { Margin = new Thickness(8) };
            if (_ex.Protected)
            {
                sp.Children.Add(new TextBlock { Text = "This document is protected from unintentional editing. You may only " + (ProtectMode == "comments" ? "insert comments." : ProtectMode == "tracked" ? "make tracked changes." : "view this region."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
                var stop = UI.Btn("Stop Protection", (s, e) =>
                {
                    if (!string.IsNullOrEmpty(_ex.ProtectHash))
                    {
                        var pw = PasswordPrompt("Unprotect Document", false);
                        if (pw == null) return;
                        if (Hash(pw) != _ex.ProtectHash) { VistaDialog.Error(this, "The password is incorrect."); return; }
                    }
                    _ex.Protected = false; ApplyProtection(); MarkDirty(); ProtectDocument();
                });
                stop.HorizontalAlignment = HorizontalAlignment.Left;
                sp.Children.Add(stop);
            }
            else
            {
                var fmt = new CheckBox { Content = "Limit formatting to a selection of styles", IsChecked = _ex.Custom.TryGetValue("protectFormat", out var pf) && pf == "1" };
                var edit = new CheckBox { Content = "Allow only this type of editing in the document:", IsChecked = true, Margin = new Thickness(0, 10, 0, 4) };
                var mode = new ComboBox { ItemsSource = new[] { "No changes (Read only)", "Comments", "Tracked changes" }, SelectedIndex = ProtectMode == "comments" ? 1 : ProtectMode == "tracked" ? 2 : 0 };
                sp.Children.Add(new TextBlock { Text = "1. Formatting restrictions", FontWeight = FontWeights.SemiBold }); sp.Children.Add(fmt);
                sp.Children.Add(new TextBlock { Text = "2. Editing restrictions", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0) }); sp.Children.Add(edit); sp.Children.Add(mode);
                sp.Children.Add(new TextBlock { Text = "3. Start enforcement", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
                var start = UI.Btn("Yes, Start Enforcing Protection", (s, e) =>
                {
                    if (edit.IsChecked != true && fmt.IsChecked != true) { Flash("Choose a restriction first."); return; }
                    var pw = PasswordPrompt("Start Enforcing Protection", true);
                    if (pw == null) return;
                    _ex.Custom["protectMode"] = mode.SelectedIndex == 1 ? "comments" : mode.SelectedIndex == 2 ? "tracked" : "read";
                    _ex.Custom["protectFormat"] = fmt.IsChecked == true ? "1" : "0";
                    _ex.Protected = true; _ex.ProtectHash = Hash(pw);
                    ApplyProtection(); MarkDirty(); ProtectDocument();
                });
                start.HorizontalAlignment = HorizontalAlignment.Left;
                sp.Children.Add(start);
            }
            ShowRightPane("protect", "Restrict Formatting and Editing", sp, null, 260);
        }

        string PasswordPrompt(string title, bool confirm)
        {
            var d = new DialogBase(this, title, 320);
            var p1 = new PasswordBox { Width = 200, Height = 23 }; var p2 = new PasswordBox { Width = 200, Height = 23 };
            var form = confirm ? D.Form(("Enter new password (optional):", p1), ("Reenter password to confirm:", p2)) : D.Form(("Password:", p1));
            d.Content = UI.DialogLayout(form, UI.Strip(UI.Btn("OK", (s, e) => { if (confirm && p1.Password != p2.Password) { VistaDialog.Error(d, "The password confirmation doesn't match."); return; } d.DialogResult = true; }, true), UI.Btn("Cancel", null, false, true)));
            d.Loaded += (s, e) => p1.Focus();
            return d.ShowDialog() == true ? p1.Password : null;
        }

        void ApplyProtection()
        {
            bool ro = _ex.MarkedFinal || (_ex.Protected && ProtectMode != "tracked");
            Editor.IsReadOnly = ro;
            if (_ex.Protected && ProtectMode == "tracked" && !_ex.TrackChanges) SetTrackChanges(true);
            if (_ex.MarkedFinal) ShowMessageBar("An author has marked this document as final to discourage editing.", "Edit Anyway", () => { _ex.MarkedFinal = false; ApplyProtection(); MarkDirty(); });
            else if (_ex.Protected) ShowMessageBar(ProtectMode == "comments" ? "This document is protected. You can only add comments." : ProtectMode == "tracked" ? "This document is protected. All changes are tracked." : "This document is protected from editing.", "Restrict Editing...", ProtectDocument);
            else HideMessageBar();
        }

        void ShowMessageBar(string text, string button, Action click)
        {
            MessageBarText.Text = text;
            MessageBarButton.Content = button;
            MessageBarButton.Visibility = button == null ? Visibility.Collapsed : Visibility.Visible;
            if (_messageBarHandler != null) MessageBarButton.Click -= _messageBarHandler;
            _messageBarHandler = (s, e) => click?.Invoke();
            MessageBarButton.Click += _messageBarHandler;
            MessageBar.Visibility = Visibility.Visible;
        }
        RoutedEventHandler _messageBarHandler;
        void HideMessageBar() => MessageBar.Visibility = Visibility.Collapsed;

        void MarkAsFinal()
        {
            if (_ex.MarkedFinal) { _ex.MarkedFinal = false; ApplyProtection(); MarkDirty(); return; }
            if (VistaDialog.Show(this, "This document will be marked as final and then saved.", "Valco Word", MessageBoxButton.OKCancel, DlgIcon.Info) != MessageBoxResult.OK) return;
            _ex.MarkedFinal = true;
            if (!Save()) { _ex.MarkedFinal = false; return; }
            ApplyProtection();
            VistaDialog.Show(this, "This document has been marked as final to indicate that editing is complete and that this is the final version of the document.\n\nWhen a document is marked as final, the status property is set to \"Final\" and typing, editing commands, and proofing marks are turned off.", "Valco Word", MessageBoxButton.OK, DlgIcon.Info);
        }

        void InspectDocument()
        {
            var d = new DialogBase(this, "Document Inspector", 520);
            var checks = new (string Name, string Desc, CheckBox Box)[]
            {
                ("Comments and Revisions", "Inspects the document for comments and tracked changes.", null),
                ("Document Properties and Personal Information", "Inspects for hidden metadata or personal information saved with the document.", null),
                ("Custom XML Data", "Inspects for sources, mail merge data and other custom data stored with this document.", null),
                ("Headers, Footers, and Watermarks", "Inspects the document for information in headers, footers and watermarks.", null),
                ("Hidden Text", "Inspects the document for text that has been formatted as hidden.", null),
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = "To check the document for the selected content, click Inspect.", Margin = new Thickness(0, 0, 0, 8) });
            for (int i = 0; i < checks.Length; i++)
            {
                var cb = new CheckBox { Content = new TextBlock { Text = checks[i].Name, FontWeight = FontWeights.SemiBold }, IsChecked = true };
                checks[i].Box = cb;
                sp.Children.Add(cb);
                sp.Children.Add(new TextBlock { Text = checks[i].Desc, Foreground = Brushes.DimGray, Margin = new Thickness(20, 0, 0, 8), TextWrapping = TextWrapping.Wrap });
            }
            var resultPanel = new StackPanel();
            sp.Children.Add(resultPanel);
            Button inspect = null;
            inspect = UI.Btn("Inspect", (s, e) =>
            {
                resultPanel.Children.Clear();
                void Result(string title, string found, Action remove)
                {
                    var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
                    if (found != null && remove != null)
                    {
                        Button rb = null;
                        rb = UI.Btn("Remove All", (o, a) => { remove(); rb.IsEnabled = false; ((TextBlock)((StackPanel)row.Children[1]).Children[1]).Text = "All items were successfully removed."; MarkDirty(); });
                        DockPanel.SetDock(rb, Dock.Right); row.Children.Add(rb);
                    }
                    var t = new StackPanel();
                    t.Children.Add(new TextBlock { Text = (found != null ? "! " : "✓ ") + title, FontWeight = FontWeights.SemiBold, Foreground = found != null ? Brushes.DarkRed : Brushes.DarkGreen });
                    t.Children.Add(new TextBlock { Text = found ?? "No items were found.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(18, 0, 0, 0) });
                    if (row.Children.Count == 0) row.Children.Add(new Border());
                    row.Children.Add(t);
                    resultPanel.Children.Add(row);
                }
                resultPanel.Children.Add(new Border { Height = 1, Background = Brushes.LightGray, Margin = new Thickness(0, 6, 0, 6) });
                if (checks[0].Box.IsChecked == true)
                {
                    int c = CommentSpans().Count(), r = Spans.WithTag<RevInfo>(Editor.Document).Count();
                    Result("Comments and Revisions", c + r > 0 ? $"The following items were found: {c} comment(s), {r} revision(s)." : null, () => { foreach (var x in CommentSpans().ToList()) RemoveComment(x); AcceptReject(true, true); ShowCommentsPane(); });
                }
                if (checks[1].Box.IsChecked == true)
                    Result("Document Properties and Personal Information", !string.IsNullOrEmpty(_author) || !string.IsNullOrEmpty(_docTitle) || _ex.Custom.Keys.Any(k => k is "Subject" or "Company" or "Keywords") ? "The following document information was found: Document properties, Author." : null,
                        () => { _author = ""; _docTitle = ""; foreach (var k in new[] { "Subject", "Company", "Keywords", "Comments", "Category" }) _ex.Custom.Remove(k); _ex.Custom["removePersonal"] = "1"; });
                if (checks[2].Box.IsChecked == true)
                    Result("Custom XML Data", _ex.Sources.Count > 0 || _ex.MergeRecords.Count > 0 ? $"Custom XML data was found: {_ex.Sources.Count} source(s), {_ex.MergeRecords.Count} mail merge record(s)." : null,
                        () => { _ex.Sources.Clear(); StartMerge(null); });
                if (checks[3].Box.IsChecked == true)
                    Result("Headers, Footers, and Watermarks", _page.HasHeader || _page.HasFooter || !string.IsNullOrEmpty(_page.Watermark) ? "The following items were found: " + string.Join(", ", new[] { _page.HasHeader ? "Headers" : null, _page.HasFooter ? "Footers" : null, string.IsNullOrEmpty(_page.Watermark) ? null : "Watermarks" }.Where(x => x != null)) : null,
                        () => { _page.HeaderLeft = _page.HeaderText = _page.HeaderRight = _page.FooterLeft = _page.FooterCenter = _page.FooterRight = ""; _page.Watermark = ""; PageChanged(); });
                if (checks[4].Box.IsChecked == true) Result("Hidden Text", null, null);
                inspect.Content = "Reinspect";
                d.SizeToContent = SizeToContent.Height;
            }, true);
            d.Content = UI.DialogLayout(new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 560 }, UI.Strip(inspect, UI.Btn("Close", null, false, true)));
            d.ShowDialog();
        }
    }
}
