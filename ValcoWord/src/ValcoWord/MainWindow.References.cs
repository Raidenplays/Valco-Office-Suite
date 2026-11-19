using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ValcoWord
{
    public partial class MainWindow
    {
        // =====================================================================================
        //  Layout helper: make sure page boundaries are current before reading page numbers
        // =====================================================================================
        void RefreshLayoutNow()
        {
            Editor.UpdateLayout();
            UpdatePages();
        }

        static int HeadingLevel(Paragraph p)
        {
            var tag = p.Tag as string;
            if (tag == null || !tag.StartsWith("Heading")) return 0;
            return int.TryParse(tag.Substring(7), out var l) ? l : 0;
        }

        // =====================================================================================
        //  Generated sections: table of contents, figures, index, authorities
        // =====================================================================================
        ContextMenu TocMenu()
        {
            var m = RB.Menu();
            m.Items.Add(new MenuItem { Header = "Built-In", IsEnabled = false, FontWeight = FontWeights.Bold });
            m.Items.Add(TocPreviewItem("Automatic Table 1", "Contents", () => InsertToc("Contents", 3, true)));
            m.Items.Add(TocPreviewItem("Automatic Table 2", "Table of Contents", () => InsertToc("Table of Contents", 3, true)));
            m.Items.Add(TocPreviewItem("Manual Table", "Table of Contents", () => InsertManualToc()));
            m.Items.Add(new Separator());
            m.Items.Add(RB.MI("Insert Table of Contents...", TocDialog, "IconTOC"));
            m.Items.Add(RB.MI("Remove Table of Contents", () => RemoveGenerated(SectionKind.TOC), "IconDelete"));
            return m;
        }

        MenuItem TocPreviewItem(string name, string title, Action act)
        {
            var sp = new StackPanel { Width = 240 };
            sp.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold });
            var box = new StackPanel { Background = Brushes.White, Margin = new Thickness(0, 2, 0, 2) };
            box.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, Foreground = Br(Th.Accent1), Margin = new Thickness(4, 2, 0, 2) });
            foreach (var (t, ind) in new[] { ("Heading 1 ........................ 1", 4), ("Heading 2 ...................... 1", 14), ("Heading 3 .................... 1", 24) })
                box.Children.Add(new TextBlock { Text = t, FontSize = 9, Margin = new Thickness(ind, 0, 0, 1) });
            sp.Children.Add(new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Child = box });
            var mi = new MenuItem { Header = sp };
            mi.Click += (s, e) => act();
            return mi;
        }

        void TocDialog()
        {
            var d = new DialogBase(this, "Table of Contents", 360);
            var levels = D.Box("3", 50);
            var pages = new CheckBox { Content = "Show page numbers", IsChecked = true };
            var links = new CheckBox { Content = "Use hyperlinks instead of page numbers in Web Layout", IsChecked = true, Margin = new Thickness(0, 4, 0, 0) };
            var title = D.Box("Contents", 200);
            var sp = new StackPanel();
            sp.Children.Add(D.Form(("Title:", title), ("Show levels:", levels)));
            sp.Children.Add(pages); sp.Children.Add(links);
            d.Content = UI.DialogLayout(sp, UI.Strip(UI.Btn("OK", (s, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
            if (d.ShowDialog() != true) return;
            _ex.Custom["tocLevels"] = ((int)Math.Max(1, Math.Min(9, D.ParseNum(levels.Text) ?? 3))).ToString();
            _ex.Custom["tocPages"] = pages.IsChecked == true ? "1" : "0";
            InsertToc(title.Text, int.Parse(_ex.Custom["tocLevels"]), pages.IsChecked == true);
        }

        void InsertToc(string title, int levels, bool pages)
        {
            _ex.Custom["tocTitle"] = title;
            _ex.Custom["tocLevels"] = levels.ToString();
            _ex.Custom["tocPages"] = pages ? "1" : "0";
            if (!Fmt.AllParagraphs(Editor.Document.Blocks).Any(p => HeadingLevel(p) > 0))
                Flash("No headings found. Apply Heading 1–3 styles to your headings, then click Update Table.");
            InsertGenerated(SectionKind.TOC);
        }

        void InsertManualToc()
        {
            var sec = new Section { Tag = SectionKind.TOC };
            sec.Blocks.Add(GeneratedTitle("Table of Contents"));
            var t = EntryTable();
            foreach (var (text, lvl) in new[] { ("Type chapter title (level 1)", 1), ("Type chapter title (level 2)", 2), ("Type chapter title (level 3)", 3), ("Type chapter title (level 1)", 1) })
                AddEntryRow(t, text, lvl, "1", null);
            sec.Blocks.Add(t);
            InsertBlocksAtCaret(sec);
        }

        Paragraph GeneratedTitle(string text)
        {
            var p = NewPara(text, "Heading1");
            p.Tag = "TOCHeading";
            p.Margin = new Thickness(0, 12, 0, 8);
            return p;
        }

        Table EntryTable()
        {
            var t = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 12) };
            t.Columns.Add(new TableColumn { Width = new GridLength(_page.BodyWidthPx - 60) });
            t.Columns.Add(new TableColumn { Width = new GridLength(58) });
            t.RowGroups.Add(new TableRowGroup());
            return t;
        }

        void AddEntryRow(Table t, string text, int level, string page, string anchor, bool bold = false)
        {
            var row = new TableRow { Tag = level };
            var lp = NewCellParagraph();
            lp.Margin = new Thickness(14 * (level - 1), 0, 0, 0);
            if (bold) lp.FontWeight = FontWeights.Bold;
            if (anchor != null) lp.Inlines.Add(new Hyperlink(new Run(text)) { NavigateUri = new Uri("#" + anchor, UriKind.Relative), Foreground = Brushes.Black, TextDecorations = null });
            else lp.Inlines.Add(new Run(text));
            var rp = NewCellParagraph(); rp.TextAlignment = TextAlignment.Right;
            if (page != null) rp.Inlines.Add(new Run(page));
            row.Cells.Add(new TableCell(lp) { BorderThickness = new Thickness(0), Padding = new Thickness(0, 1, 4, 1) });
            row.Cells.Add(new TableCell(rp) { BorderThickness = new Thickness(0), Padding = new Thickness(0, 1, 0, 1) });
            t.RowGroups[0].Rows.Add(row);
        }

        Section FindGenerated(string kind) => Fmt.AllBlocks(Editor.Document.Blocks).OfType<Section>().FirstOrDefault(s => SectionKind.Of(s) == kind);

        void RemoveGenerated(string kind)
        {
            var s = FindGenerated(kind);
            if (s == null) { Flash("There isn't one in this document."); return; }
            Editor.BeginChange();
            s.SiblingBlocks.Remove(s);
            Editor.EndChange();
            MarkDirty(); ScheduleLayout();
        }

        void InsertGenerated(string kind)
        {
            var existing = FindGenerated(kind);
            if (existing != null) { UpdateGenerated(kind); GoToPosition(existing.ContentStart); return; }
            var sec = new Section { Tag = kind };
            InsertBlocksAtCaret(sec);
            FillGenerated(sec);
        }

        void UpdateGenerated(string kind)
        {
            var sec = FindGenerated(kind);
            if (sec == null) { Flash("There isn't one in this document to update. Insert it first."); return; }
            FillGenerated(sec);
            Flash("Updated.");
        }

        /// <summary>Builds the content of a generated section; two passes so page numbers account for the section's own length.</summary>
        void FillGenerated(Section sec)
        {
            var kind = SectionKind.Of(sec);
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1) RefreshLayoutNow();
                Editor.BeginChange();
                sec.Blocks.Clear();
                switch (kind)
                {
                    case SectionKind.TOC: BuildToc(sec, pass == 1); break;
                    case SectionKind.Figures: BuildFigures(sec, pass == 1); break;
                    case SectionKind.Index: BuildIndex(sec, pass == 1); break;
                    case SectionKind.TOA: BuildToa(sec, pass == 1); break;
                    case SectionKind.Bibliography: BuildBibliography(sec, _ex.Custom.TryGetValue("bibTitle", out var bt) ? bt : "Bibliography"); break;
                }
                if (sec.Blocks.Count == 0) sec.Blocks.Add(NewPara());
                Editor.EndChange();
                if (kind == SectionKind.Bibliography) break;
            }
            MarkDirty(); ScheduleLayout();
        }

        void BuildToc(Section sec, bool withPages)
        {
            int levels = _ex.Custom.TryGetValue("tocLevels", out var l) && int.TryParse(l, out var lv) ? lv : 3;
            bool pages = !_ex.Custom.TryGetValue("tocPages", out var pg) || pg != "0";
            sec.Blocks.Add(GeneratedTitle(_ex.Custom.TryGetValue("tocTitle", out var tt) ? tt : "Contents"));
            var t = EntryTable();
            foreach (var p in Fmt.AllParagraphs(Editor.Document.Blocks).ToList())
            {
                int level = HeadingLevel(p);
                if (level < 1 || level > levels || Fmt.Ancestor<Section>(p) == sec) continue;
                if (Spans.Text(p).Trim().Length == 0) continue;
                var anchor = EnsureParagraphBookmark(p);
                AddEntryRow(t, Spans.Text(p).Trim(), level, pages ? (withPages ? DecoratedPaginator.FormatNumber(PageOf(p.ContentStart), _page) : "") : null, anchor, level == 1);
            }
            if (t.RowGroups[0].Rows.Count == 0) sec.Blocks.Add(NewPara("No table of contents entries found."));
            else sec.Blocks.Add(t);
        }

        void BuildFigures(Section sec, bool withPages)
        {
            var t = EntryTable();
            foreach (var p in Fmt.AllParagraphs(Editor.Document.Blocks).Where(p => (p.Tag as string) == "Caption").ToList())
            {
                if (Fmt.Ancestor<Section>(p) == sec) continue;
                AddEntryRow(t, Spans.Text(p).Trim(), 1, withPages ? PageOf(p.ContentStart).ToString() : "", EnsureParagraphBookmark(p));
            }
            if (t.RowGroups[0].Rows.Count == 0) sec.Blocks.Add(NewPara("No table of figures entries found."));
            else sec.Blocks.Add(t);
        }

        void BuildIndex(Section sec, bool withPages)
        {
            var entries = Spans.WithTag<IndexEntry>(Editor.Document).Where(s => ((IndexEntry)s.Tag).Kind == "XE")
                .Select(s => (Term: ((IndexEntry)s.Tag).Term, Page: withPages ? PageOf(s.ContentStart) : 0)).ToList();
            if (entries.Count == 0) { sec.Blocks.Add(NewPara("No index entries found. Select a word and click Mark Entry.")); return; }
            var t = EntryTable();
            char letter = '\0';
            foreach (var g in entries.GroupBy(e => e.Term, StringComparer.CurrentCultureIgnoreCase).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                var first = char.ToUpperInvariant(g.Key.FirstOrDefault());
                if (first != letter) { letter = first; AddEntryRow(t, letter.ToString(), 1, null, null, true); }
                var parts = g.Key.Split(':');
                var pagesText = withPages ? string.Join(", ", g.Select(x => x.Page).Distinct().OrderBy(x => x)) : "";
                AddEntryRow(t, parts.Length > 1 ? parts[1].Trim() : g.Key, parts.Length > 1 ? 3 : 2, pagesText, null);
            }
            sec.Blocks.Add(t);
        }

        static readonly string[] ToaCategories = { "Cases", "Statutes", "Other Authorities", "Rules", "Treatises", "Regulations", "Constitutional Provisions" };

        void BuildToa(Section sec, bool withPages)
        {
            var entries = Spans.WithTag<IndexEntry>(Editor.Document).Where(s => ((IndexEntry)s.Tag).Kind == "TA").Select(s => (Entry: (IndexEntry)s.Tag, Page: withPages ? PageOf(s.ContentStart) : 0)).ToList();
            if (entries.Count == 0) { sec.Blocks.Add(NewPara("No table of authorities entries found. Select a citation and click Mark Citation.")); return; }
            foreach (var cat in entries.GroupBy(e => string.IsNullOrEmpty(e.Entry.Category) ? "Cases" : e.Entry.Category).OrderBy(g => Array.IndexOf(ToaCategories, g.Key)))
            {
                sec.Blocks.Add(NewPara(cat.Key, "Heading2"));
                var t = EntryTable();
                foreach (var g in cat.GroupBy(e => e.Entry.Term).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
                    AddEntryRow(t, g.Key, 1, withPages ? string.Join(", ", g.Select(x => x.Page).Distinct().OrderBy(x => x)) : "", null);
                sec.Blocks.Add(t);
            }
        }

        void MarkEntry(string kind)
        {
            var sel = Editor.Selection;
            if (sel.IsEmpty || sel.Start.Paragraph != sel.End.Paragraph) { Flash(kind == "TA" ? "Select a citation in one paragraph first." : "Select the text you want to index first."); return; }
            var d = new DialogBase(this, kind == "TA" ? "Mark Citation" : "Mark Index Entry", 380);
            var main = D.Box(sel.Text.Trim(), 280);
            var sub = D.Box("", 280);
            var cat = new ComboBox { Width = 200, ItemsSource = ToaCategories, SelectedIndex = 0 };
            var form = kind == "TA" ? D.Form(("Short citation:", main), ("Category:", cat)) : D.Form(("Main entry:", main), ("Subentry:", sub));
            bool all = false;
            var mark = UI.Btn("Mark", (s, e) => d.DialogResult = true, true);
            var markAll = UI.Btn("Mark All", (s, e) => { all = true; d.DialogResult = true; });
            d.Content = UI.DialogLayout(form, UI.Strip(mark, markAll, UI.Btn("Cancel", null, false, true)));
            if (d.ShowDialog() != true || main.Text.Trim().Length == 0) return;
            string term = main.Text.Trim() + (sub.Text.Trim().Length > 0 ? ":" + sub.Text.Trim() : "");
            string category = kind == "TA" ? (string)cat.SelectedItem : null;
            Editor.BeginChange();
            int n = 0;
            if (all)
            {
                var needle = sel.Text;
                foreach (var p in Fmt.AllParagraphs(Editor.Document.Blocks).ToList())
                {
                    if (Fmt.Ancestor<Section>(p) is Section gs && SectionKind.Of(gs) != null) continue;
                    var tp = p.ContentStart;
                    while (tp != null && tp.CompareTo(p.ContentEnd) < 0)
                    {
                        var r = FindIn(tp, p.ContentEnd, needle);
                        if (r == null) break;
                        if (Fmt.AncestorAt<Span>(r.Start)?.Tag is IndexEntry) { tp = r.End; continue; }
                        var span = new Span(r.Start, r.End) { Tag = new IndexEntry { Term = term, Kind = kind, Category = category } };
                        n++;
                        tp = span.ElementEnd;
                    }
                }
            }
            else { new Span(sel.Start, sel.End) { Tag = new IndexEntry { Term = term, Kind = kind, Category = category } }; n = 1; }
            Editor.EndChange();
            MarkDirty();
            Flash(n == 1 ? "Marked 1 entry." : $"Marked {n} entries.");
        }

        static TextRange FindIn(TextPointer from, TextPointer to, string needle)
        {
            var tp = from;
            while (tp != null && tp.CompareTo(to) < 0)
            {
                if (tp.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    var run = tp.GetTextInRun(LogicalDirection.Forward);
                    int i = run.IndexOf(needle, StringComparison.CurrentCulture);
                    if (i >= 0)
                    {
                        var s = tp.GetPositionAtOffset(i);
                        var e = s?.GetPositionAtOffset(needle.Length);
                        if (s != null && e != null) return new TextRange(s, e);
                    }
                }
                tp = tp.GetNextContextPosition(LogicalDirection.Forward);
            }
            return null;
        }

        // =====================================================================================
        //  Footnotes and endnotes
        // =====================================================================================
        Section NotesSection() => Editor.Document.Blocks.OfType<Section>().FirstOrDefault(s => SectionKind.Of(s) == SectionKind.Notes);

        Section EnsureNotesSection()
        {
            var s = NotesSection();
            if (s != null) return s;
            s = new Section { Tag = SectionKind.Notes };
            var sep = new Paragraph { Tag = "NoteSeparator", Margin = new Thickness(0, 24, 0, 6), BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 0, 0, 0.75), FontSize = 4 };
            sep.Inlines.Add(new Run(" "));
            s.Blocks.Add(sep);
            Editor.Document.Blocks.Add(s);
            return s;
        }

        static string NoteLabel(bool endnote, int n) => endnote ? RomanLower(n) : n.ToString();
        static string RomanLower(int n) => DecoratedPaginator.FormatNumber(n, new PageSettings { NumFormat = "i" });

        void InsertNote(bool endnote)
        {
            if (NotesSection() is Section ns && Fmt.AncestorAt<Section>(Editor.CaretPosition) == ns) { Flash("Click in the main document text first."); return; }
            Editor.BeginChange();
            if (!Editor.Selection.IsEmpty) Editor.Selection.Select(Editor.Selection.End, Editor.Selection.End);
            var ip = Editor.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
            var nr = new NoteRef { Endnote = endnote, Number = 9999 };
            var refSpan = new Span(new Run("*") { BaselineAlignment = BaselineAlignment.Superscript, FontSize = Math.Max(8, (Editor.Selection.GetPropertyValue(TextElement.FontSizeProperty) as double? ?? 14.67) * 0.75) }, ip) { Tag = nr };
            var sec = EnsureNotesSection();
            var body = NewPara(); body.Tag = new NoteBody { Endnote = endnote, Number = 9999 };
            body.FontSize = 10 * Fmt.PxPerPt; body.Margin = new Thickness(0, 0, 0, 3);
            body.Inlines.Add(new Span(new Run("*") { BaselineAlignment = BaselineAlignment.Superscript }) { Tag = "NoteNum" });
            body.Inlines.Add(new Run(" "));
            sec.Blocks.Add(body);
            Editor.EndChange();
            _noteLinks.Add((refSpan, body));
            RenumberNotes();
            Editor.CaretPosition = body.ContentEnd;
            BringSelectionIntoView();
            MarkDirty(); ScheduleLayout(); Editor.Focus();
        }

        readonly List<(Span Ref, Paragraph Body)> _noteLinks = new List<(Span, Paragraph)>();

        /// <summary>Numbers notes in document order, keeps the note text in the same order and drops notes whose reference was deleted.</summary>
        void RenumberNotes()
        {
            var sec = NotesSection();
            if (sec == null) return;
            var refs = Spans.WithTag<NoteRef>(Editor.Document).Where(s => Fmt.Ancestor<Section>(s) != sec).ToList();
            var bodies = sec.Blocks.OfType<Paragraph>().Where(p => p.Tag is NoteBody).ToList();
            // pair refs with bodies: by explicit link first, then by original number
            var pairs = new List<(Span Ref, Paragraph Body)>();
            foreach (var r in refs)
            {
                var link = _noteLinks.FirstOrDefault(l => l.Ref == r);
                var nr = (NoteRef)r.Tag;
                var body = link.Body != null && bodies.Contains(link.Body) ? link.Body : bodies.FirstOrDefault(b => ((NoteBody)b.Tag).Endnote == nr.Endnote && ((NoteBody)b.Tag).Number == nr.Number && !pairs.Any(p => p.Body == b));
                pairs.Add((r, body));
            }
            _noteLinks.Clear();
            bool changed = false;
            Editor.BeginChange();
            foreach (var endnote in new[] { false, true })
            {
                int n = 0;
                foreach (var (r, body) in pairs.Where(p => ((NoteRef)p.Ref.Tag).Endnote == endnote))
                {
                    n++;
                    var nr = (NoteRef)r.Tag;
                    var label = NoteLabel(endnote, n);
                    if (nr.Number != n) { nr.Number = n; changed = true; }
                    if (r.Inlines.FirstInline is Run rr && rr.Text != label) { rr.Text = label; changed = true; }
                    if (body != null)
                    {
                        var nb = (NoteBody)body.Tag; nb.Number = n;
                        if (body.Inlines.FirstInline is Span ns && ns.Inlines.FirstInline is Run nrun && nrun.Text != label) { nrun.Text = label; changed = true; }
                    }
                    _noteLinks.Add((r, body));
                }
            }
            // orphaned note text (its reference was deleted)
            foreach (var b in bodies.Where(b => !pairs.Any(p => p.Body == b)).ToList()) { sec.Blocks.Remove(b); changed = true; }
            // order: footnotes then endnotes, each by number
            var ordered = sec.Blocks.OfType<Paragraph>().Where(p => p.Tag is NoteBody).OrderBy(p => ((NoteBody)p.Tag).Endnote).ThenBy(p => ((NoteBody)p.Tag).Number).ToList();
            var current = sec.Blocks.OfType<Paragraph>().Where(p => p.Tag is NoteBody).ToList();
            if (!ordered.SequenceEqual(current))
            {
                foreach (var b in current) sec.Blocks.Remove(b);
                foreach (var b in ordered) sec.Blocks.Add(b);
                changed = true;
            }
            if (!sec.Blocks.OfType<Paragraph>().Any(p => p.Tag is NoteBody)) { Editor.Document.Blocks.Remove(sec); changed = true; }
            Editor.EndChange();
            if (changed) MarkDirty();
        }

        void GoToNote(int dir)
        {
            var refs = Spans.WithTag<NoteRef>(Editor.Document).ToList();
            if (refs.Count == 0) { Flash("There are no footnotes or endnotes in this document."); return; }
            var caret = Editor.CaretPosition;
            var target = dir > 0 ? refs.FirstOrDefault(r => r.ContentStart.CompareTo(caret) > 0) : refs.LastOrDefault(r => r.ContentEnd.CompareTo(caret) < 0);
            target ??= dir > 0 ? refs[0] : refs[refs.Count - 1];
            GoToPosition(target.ContentEnd);
        }

        void ShowNotes()
        {
            var sec = NotesSection();
            if (sec == null) { Flash("There are no footnotes or endnotes in this document."); return; }
            var caretSec = Fmt.AncestorAt<Section>(Editor.CaretPosition);
            if (caretSec == sec)
            {
                var body = Editor.CaretPosition.Paragraph;
                var link = _noteLinks.FirstOrDefault(l => l.Body == body);
                if (link.Ref != null) GoToPosition(link.Ref.ContentEnd);
                return;
            }
            var near = Fmt.AncestorAt<Span>(Editor.CaretPosition);
            var lk = _noteLinks.FirstOrDefault(l => l.Ref == near);
            GoToPosition(lk.Body != null ? lk.Body.ContentEnd : sec.ContentStart);
        }

        // =====================================================================================
        //  Citations, sources and bibliography
        // =====================================================================================
        static string MasterSourcesPath => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(QuickPartsDir), "Sources.json");

        static List<Source> LoadMasterSources()
        {
            try { if (File.Exists(MasterSourcesPath)) return JsonSerializer.Deserialize<List<Source>>(File.ReadAllText(MasterSourcesPath), new JsonSerializerOptions { IncludeFields = true }) ?? new List<Source>(); }
            catch { }
            return new List<Source>();
        }

        static void SaveMasterSources(List<Source> list)
        {
            try { File.WriteAllText(MasterSourcesPath, JsonSerializer.Serialize(list, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true })); } catch { }
        }

        ContextMenu CitationMenu()
        {
            var m = RB.Menu();
            foreach (var s in _ex.Sources.OrderBy(x => x.FirstAuthorLast))
            {
                var src = s;
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(src.Author) ? src.Tag : src.Author, FontWeight = FontWeights.SemiBold });
                sp.Children.Add(new TextBlock { Text = $"{src.Title}, ({src.Year})", Foreground = Brushes.DimGray, FontSize = 11 });
                var mi = new MenuItem { Header = sp };
                mi.Click += (o, e) => InsertCitation(src);
                m.Items.Add(mi);
            }
            if (_ex.Sources.Count > 0) m.Items.Add(new Separator());
            m.Items.Add(RB.MI("Add New Source...", () => { var s = EditSource(null); if (s != null) { _ex.Sources.Add(s); AddToMaster(s); InsertCitation(s); } }, "IconCitation"));
            m.Items.Add(RB.MI("Add New Placeholder...", () =>
            {
                var tag = InputDialog.Ask(this, "Placeholder Name", "Type the tag name of the source. You can add more information to this source later by clicking Edit in the Source Manager.", "Placeholder" + (_ex.Sources.Count + 1));
                if (string.IsNullOrWhiteSpace(tag)) return;
                var s = new Source { Tag = tag.Trim(), Author = tag.Trim() };
                _ex.Sources.Add(s); InsertCitation(s);
            }));
            return m;
        }

        void AddToMaster(Source s)
        {
            var master = LoadMasterSources();
            if (!master.Any(x => x.Tag == s.Tag)) { master.Add(s); SaveMasterSources(master); }
        }

        void InsertCitation(Source s)
        {
            Editor.BeginChange();
            if (!Editor.Selection.IsEmpty) Editor.Selection.Select(Editor.Selection.End, Editor.Selection.End);
            var ip = Editor.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
            var span = new Span(new Run(s.Cite(_ex.BibStyle)), ip) { Tag = new CitationRef { SourceTag = s.Tag } };
            if (ip.GetTextInRun(LogicalDirection.Backward) is string before && before.Length > 0 && !char.IsWhiteSpace(before[before.Length - 1]))
                span.SiblingInlines.InsertBefore(span, new Run(" "));
            Editor.CaretPosition = span.ElementEnd;
            Editor.EndChange();
            MarkDirty(); Editor.Focus();
        }

        Source EditSource(Source existing)
        {
            var s = existing == null ? new Source() : new Source { Tag = existing.Tag, Type = existing.Type, Author = existing.Author, Title = existing.Title, Year = existing.Year, Publisher = existing.Publisher, City = existing.City, Url = existing.Url, Journal = existing.Journal, Pages = existing.Pages };
            var d = new DialogBase(this, existing == null ? "Create Source" : "Edit Source", 460);
            var type = new ComboBox { Width = 220, ItemsSource = new[] { "Book", "Book Section", "Journal Article", "Article in a Periodical", "Conference Proceedings", "Report", "Web site", "Document from Web site", "Electronic Source", "Film", "Interview", "Patent", "Case", "Miscellaneous" }, SelectedItem = s.Type };
            if (type.SelectedItem == null) type.SelectedIndex = 0;
            var author = D.Box(s.Author, 300); var title = D.Box(s.Title, 300); var year = D.Box(s.Year, 80);
            var city = D.Box(s.City, 200); var pub = D.Box(s.Publisher, 300); var journal = D.Box(s.Journal, 300); var pages = D.Box(s.Pages, 100); var url = D.Box(s.Url, 300);
            var tag = D.Box(s.Tag, 160);
            void AutoTag() { if (existing == null) tag.Text = (s.FirstAuthorLastOf(author.Text) + year.Text).Replace(" ", ""); }
            author.TextChanged += (o, e) => AutoTag(); year.TextChanged += (o, e) => AutoTag();
            var sp = new StackPanel();
            sp.Children.Add(D.Form(("Type of Source:", type), ("Author (Last, First; ...):", author), ("Title:", title), ("Year:", year), ("City:", city), ("Publisher:", pub),
                ("Journal / Periodical:", journal), ("Pages:", pages), ("URL:", url), ("Tag name:", tag)));
            d.Content = UI.DialogLayout(sp, UI.Strip(UI.Btn("OK", (o, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
            if (d.ShowDialog() != true) return null;
            s.Type = (string)type.SelectedItem; s.Author = author.Text.Trim(); s.Title = title.Text.Trim(); s.Year = year.Text.Trim(); s.City = city.Text.Trim();
            s.Publisher = pub.Text.Trim(); s.Journal = journal.Text.Trim(); s.Pages = pages.Text.Trim(); s.Url = url.Text.Trim();
            s.Tag = tag.Text.Trim().Length > 0 ? tag.Text.Trim() : "Source" + DateTime.Now.Ticks % 100000;
            return s;
        }

        void ManageSources()
        {
            var master = LoadMasterSources();
            var d = new DialogBase(this, "Source Manager", 720);
            var lMaster = new ListBox { Width = 300, Height = 230 };
            var lCurrent = new ListBox { Width = 300, Height = 230 };
            var preview = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), MinHeight = 36 };
            string Label(Source s) => $"{(string.IsNullOrEmpty(s.Author) ? s.Tag : s.Author)}; {s.Title} ({s.Year})";
            void Fill()
            {
                lMaster.ItemsSource = master.Select(Label).ToList();
                lCurrent.ItemsSource = _ex.Sources.Select(s => (Spans.Tagged<CitationRef>(Editor.Document).Any(c => c.SourceTag == s.Tag) ? "✓ " : "   ") + Label(s)).ToList();
            }
            lMaster.SelectionChanged += (o, e) => { if (lMaster.SelectedIndex >= 0) preview.Text = master[lMaster.SelectedIndex].Reference(_ex.BibStyle); };
            lCurrent.SelectionChanged += (o, e) => { if (lCurrent.SelectedIndex >= 0) preview.Text = _ex.Sources[lCurrent.SelectedIndex].Reference(_ex.BibStyle); };
            Button B(string text, Action a) { var b = UI.Btn(text, (o, e) => { a(); Fill(); }); b.Margin = new Thickness(0, 0, 0, 4); return b; }
            var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            mid.Children.Add(B("Copy ->", () => { if (lMaster.SelectedIndex >= 0 && !_ex.Sources.Any(s => s.Tag == master[lMaster.SelectedIndex].Tag)) _ex.Sources.Add(master[lMaster.SelectedIndex]); MarkDirty(); }));
            mid.Children.Add(B("<- Copy", () => { if (lCurrent.SelectedIndex >= 0 && !master.Any(s => s.Tag == _ex.Sources[lCurrent.SelectedIndex].Tag)) { master.Add(_ex.Sources[lCurrent.SelectedIndex]); SaveMasterSources(master); } }));
            mid.Children.Add(B("Delete", () =>
            {
                if (lCurrent.SelectedIndex >= 0)
                {
                    var s = _ex.Sources[lCurrent.SelectedIndex];
                    if (Spans.Tagged<CitationRef>(Editor.Document).Any(c => c.SourceTag == s.Tag)) { VistaDialog.Error(d, "This source is cited in the document. Remove its citations first."); return; }
                    _ex.Sources.Remove(s); MarkDirty();
                }
                else if (lMaster.SelectedIndex >= 0) { master.RemoveAt(lMaster.SelectedIndex); SaveMasterSources(master); }
            }));
            mid.Children.Add(B("Edit...", () =>
            {
                if (lCurrent.SelectedIndex >= 0)
                {
                    var old = _ex.Sources[lCurrent.SelectedIndex]; var s = EditSource(old);
                    if (s != null) { foreach (var c in Spans.Tagged<CitationRef>(Editor.Document).Where(c => c.SourceTag == old.Tag)) c.SourceTag = s.Tag; _ex.Sources[lCurrent.SelectedIndex] = s; MarkDirty(); RefreshCitations(); }
                }
                else if (lMaster.SelectedIndex >= 0) { var s = EditSource(master[lMaster.SelectedIndex]); if (s != null) { master[lMaster.SelectedIndex] = s; SaveMasterSources(master); } }
            }));
            mid.Children.Add(B("New...", () => { var s = EditSource(null); if (s != null) { _ex.Sources.Add(s); master.Add(s); SaveMasterSources(master); MarkDirty(); } }));
            var c1 = new StackPanel(); c1.Children.Add(new TextBlock { Text = "Master List" }); c1.Children.Add(lMaster);
            var c2 = new StackPanel(); c2.Children.Add(new TextBlock { Text = "Current List  (✓ cited source)" }); c2.Children.Add(lCurrent);
            var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(c1); row.Children.Add(mid); row.Children.Add(c2);
            var sp = new StackPanel(); sp.Children.Add(row);
            sp.Children.Add(new TextBlock { Text = $"Preview ({_ex.BibStyle}):", Margin = new Thickness(0, 10, 0, 0), FontWeight = FontWeights.SemiBold });
            sp.Children.Add(new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Padding = new Thickness(6), Child = preview, Background = Brushes.White });
            d.Content = UI.DialogLayout(sp, UI.Strip(UI.Btn("Close", null, true, true)));
            Fill();
            d.ShowDialog();
            RefreshCitations();
        }

        void RefreshCitations()
        {
            Editor.BeginChange();
            foreach (var span in Spans.WithTag<CitationRef>(Editor.Document).ToList())
            {
                var src = _ex.Sources.FirstOrDefault(s => s.Tag == ((CitationRef)span.Tag).SourceTag);
                if (src == null) continue;
                if (span.Inlines.FirstInline is Run r) { r.Text = src.Cite(_ex.BibStyle); foreach (var extra in span.Inlines.Skip(1).ToList()) span.Inlines.Remove(extra); }
            }
            Editor.EndChange();
            var bib = FindGenerated(SectionKind.Bibliography);
            if (bib != null) FillGenerated(bib);
            if (CbBibStyle != null && (string)CbBibStyle.SelectedItem != _ex.BibStyle) { _updatingUI = true; CbBibStyle.SelectedItem = _ex.BibStyle; _updatingUI = false; }
        }

        void InsertBibliography(string title)
        {
            if (_ex.Sources.Count == 0) { Flash("There are no sources in the current document. Add one with Insert Citation."); return; }
            _ex.Custom["bibTitle"] = title;
            var existing = FindGenerated(SectionKind.Bibliography);
            if (existing != null) { FillGenerated(existing); GoToPosition(existing.ContentStart); return; }
            var sec = new Section { Tag = SectionKind.Bibliography };
            InsertBlocksAtCaret(sec);
            FillGenerated(sec);
        }

        void BuildBibliography(Section sec, string title)
        {
            sec.Blocks.Add(GeneratedTitle(title));
            var cited = new HashSet<string>(Spans.Tagged<CitationRef>(Editor.Document).Select(c => c.SourceTag));
            var list = _ex.Sources.Where(s => cited.Count == 0 || cited.Contains(s.Tag)).OrderBy(s => s.FirstAuthorLast, StringComparer.CurrentCultureIgnoreCase).ToList();
            foreach (var s in list)
            {
                var p = NewPara();
                p.Margin = new Thickness(36, 0, 0, 8);
                p.TextIndent = -36;
                var refText = s.Reference(_ex.BibStyle);
                int ti = string.IsNullOrEmpty(s.Title) ? -1 : refText.IndexOf(s.Title, StringComparison.Ordinal);
                if (ti >= 0)
                {
                    p.Inlines.Add(new Run(refText.Substring(0, ti)));
                    p.Inlines.Add(new Run(s.Title) { FontStyle = FontStyles.Italic });
                    p.Inlines.Add(new Run(refText.Substring(ti + s.Title.Length)));
                }
                else p.Inlines.Add(new Run(refText));
                sec.Blocks.Add(p);
            }
        }

        // =====================================================================================
        //  Captions
        // =====================================================================================
        void InsertCaption()
        {
            var labels = new List<string> { "Figure", "Table", "Equation" };
            if (Settings.Current.Custom.TryGetValue("captionLabels", out var custom)) labels.AddRange(custom.Split('|').Where(x => x.Length > 0 && !labels.Contains(x)));
            bool inTable = CurrentCell() != null;
            var d = new DialogBase(this, "Caption", 400);
            var label = new ComboBox { Width = 160, ItemsSource = labels, SelectedItem = _selectedImage != null ? "Figure" : inTable ? "Table" : "Figure" };
            var text = D.Box("", 330);
            var pos = new ComboBox { Width = 160, ItemsSource = new[] { "Below selected item", "Above selected item" }, SelectedIndex = inTable ? 1 : 0 };
            var exclude = new CheckBox { Content = "Exclude label from caption", Margin = new Thickness(0, 6, 0, 0) };
            var preview = new TextBlock { FontWeight = FontWeights.Bold, Foreground = Br(Th.Accent1), Margin = new Thickness(0, 4, 0, 0) };
            void Prev() => preview.Text = (exclude.IsChecked == true ? "" : label.SelectedItem + " ") + (CaptionCount((string)label.SelectedItem) + 1) + (text.Text.Length > 0 ? ": " + text.Text : "");
            label.SelectionChanged += (s, e) => Prev(); text.TextChanged += (s, e) => Prev(); exclude.Click += (s, e) => Prev();
            var newLabel = UI.Btn("New Label...", (s, e) =>
            {
                var n = InputDialog.Ask(d, "New Label", "Label:", "");
                if (string.IsNullOrWhiteSpace(n)) return;
                labels.Add(n.Trim());
                Settings.Current.Custom["captionLabels"] = string.Join("|", labels.Skip(3)); Settings.Save();
                label.ItemsSource = null; label.ItemsSource = labels; label.SelectedItem = n.Trim();
            });
            var sp = new StackPanel();
            sp.Children.Add(D.Form(("Caption text:", text), ("Label:", label), ("Position:", pos)));
            sp.Children.Add(exclude);
            sp.Children.Add(preview);
            d.Content = UI.DialogLayout(sp, UI.Strip(UI.Btn("OK", (s, e) => d.DialogResult = true, true), newLabel, UI.Btn("Cancel", null, false, true)));
            d.Loaded += (s, e) => { Prev(); text.Focus(); };
            if (d.ShowDialog() != true) return;
            string lab = (string)label.SelectedItem ?? "Figure";
            var cap = NewPara("", "Caption");
            cap.Tag = "Caption";
            if (exclude.IsChecked != true) cap.Inlines.Add(new Run(lab + " "));
            cap.Inlines.Add(new Span(new Run("1")) { Tag = new FieldInfo { Code = "SEQ " + lab + " \\* ARABIC" } });
            if (text.Text.Length > 0) cap.Inlines.Add(new Run(": " + text.Text));
            bool above = pos.SelectedIndex == 1;
            Editor.BeginChange();
            Block anchor = (Block)CurrentCell()?.T ?? (Block)Editor.CaretPosition.Paragraph ?? Editor.Document.Blocks.LastBlock;
            if (anchor?.Parent is ListItem li) anchor = UI.FindAncestor<List>(li) ?? anchor;
            if (anchor == null) Editor.Document.Blocks.Add(cap);
            else if (above) anchor.SiblingBlocks.InsertBefore(anchor, cap);
            else anchor.SiblingBlocks.InsertAfter(anchor, cap);
            Editor.EndChange();
            RenumberCaptions();
            MarkDirty(); ScheduleLayout();
        }

        int CaptionCount(string label) => Spans.WithTag<FieldInfo>(Editor.Document).Count(s => ((FieldInfo)s.Tag).Code?.StartsWith("SEQ " + label + " ") == true);

        void RenumberCaptions()
        {
            var counters = new Dictionary<string, int>();
            foreach (var s in Spans.WithTag<FieldInfo>(Editor.Document).ToList())
            {
                var code = ((FieldInfo)s.Tag).Code ?? "";
                if (!code.StartsWith("SEQ ")) continue;
                var lab = code.Split(' ')[1];
                counters[lab] = counters.TryGetValue(lab, out var n) ? n + 1 : 1;
                if (s.Inlines.FirstInline is Run r) r.Text = counters[lab].ToString();
            }
        }
    }

    public static class SourceExt
    {
        public static string FirstAuthorLastOf(this Source _, string author) => (author ?? "").Split(';')[0].Split(',')[0].Trim();
    }
}
