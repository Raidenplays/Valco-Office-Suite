using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Xml.Linq;

namespace ValcoWord
{
    /// <summary>Reading of comments, revisions, notes, bookmarks, fields, generated sections and Valco's own settings.</summary>
    public partial class DocxReader
    {
        static readonly XNamespace Bib = "http://schemas.openxmlformats.org/officeDocument/2006/bibliography";
        static readonly XNamespace VW = "urn:valco:word";

        readonly Dictionary<string, CommentInfo> _commentMap = new Dictionary<string, CommentInfo>();
        readonly HashSet<string> _commentsUsed = new HashSet<string>();
        XDocument _footnotes, _endnotes, _settings;
        readonly List<XElement> _customXml = new List<XElement>();
        readonly List<(Span Span, bool Endnote, int Id)> _noteRefs = new List<(Span, bool, int)>();
        readonly Dictionary<TableCell, int> _cellCol = new Dictionary<TableCell, int>();

        class FieldState { public StringBuilder Instr = new StringBuilder(); public bool Separated; public Span Span; public InlineCollection Saved; }
        readonly List<FieldState> _fields = new List<FieldState>();
        readonly List<(string Kind, string Id, Span Span, InlineCollection Saved)> _ranges = new List<(string, string, Span, InlineCollection)>();

        (string Letter, string Font, double SizePt, Color? Color, int Lines, bool Margin)? _pendingDrop;

        // ---------------- parts ----------------
        void LoadSideParts()
        {
            try
            {
                var cp = RelPart(_doc, Ns.RelComments);
                if (cp != null)
                    foreach (var c in ReadXml(cp).Root.Elements(W + "comment"))
                    {
                        var id = c.Attribute(W + "id")?.Value; if (id == null) continue;
                        var ci = new CommentInfo { Author = c.Attribute(W + "author")?.Value ?? "", Initials = c.Attribute(W + "initials")?.Value ?? "", Text = string.Join("\n", c.Elements(W + "p").Select(p => string.Concat(p.Descendants(W + "t").Select(t => t.Value)))) };
                        if (int.TryParse(id, out var n)) ci.Id = n + 1;
                        if (DateTime.TryParse(c.Attribute(W + "date")?.Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt)) ci.Date = dt.ToLocalTime();
                        _commentMap[id] = ci;
                    }
            }
            catch { }
            try { var fp = RelPart(_doc, Ns.RelFootnotes); if (fp != null) _footnotes = ReadXml(fp); } catch { }
            try { var ep = RelPart(_doc, Ns.RelEndnotes); if (ep != null) _endnotes = ReadXml(ep); } catch { }
            try { var sp = RelPart(_doc, Ns.RelSettings); if (sp != null) _settings = ReadXml(sp); } catch { }
            try
            {
                foreach (var rel in _doc.GetRelationshipsByType(Ns.RelCustomXml))
                {
                    var uri = PackUriHelper.ResolvePartUri(_doc.Uri, rel.TargetUri);
                    if (_pkg.PartExists(uri)) _customXml.Add(ReadXml(_pkg.GetPart(uri)).Root);
                }
            }
            catch { }
        }

        // ---------------- comment and bookmark ranges ----------------
        InlineCollection OpenRange(XElement el, InlineCollection into)
        {
            var id = el.Attribute(W + "id")?.Value ?? "";
            Span span;
            string kind;
            if (el.Name.LocalName == "commentRangeStart")
            {
                if (!_commentMap.TryGetValue(id, out var ci)) return into;
                kind = "c"; _commentsUsed.Add(id);
                span = new Span { Tag = ci };
            }
            else
            {
                var name = el.Attribute(W + "name")?.Value;
                if (string.IsNullOrEmpty(name) || name == "_GoBack") return into;
                kind = "b";
                span = new Span { Tag = new BookmarkInfo { Name = name } };
            }
            into.Add(span);
            _ranges.Add((kind, id, span, into));
            return span.Inlines;
        }

        InlineCollection CloseRange(XElement el, InlineCollection into)
        {
            var kind = el.Name.LocalName == "commentRangeEnd" ? "c" : "b";
            var id = el.Attribute(W + "id")?.Value ?? "";
            int i = _ranges.FindLastIndex(r => r.Kind == kind && r.Id == id);
            if (i < 0) return into;
            var saved = _ranges[i].Saved;
            _ranges.RemoveRange(i, _ranges.Count - i);
            return saved;
        }

        Span CommentReference(XElement c)
        {
            var id = c.Attribute(W + "id")?.Value;
            if (id == null || _commentsUsed.Contains(id) || !_commentMap.TryGetValue(id, out var ci)) return null;
            _commentsUsed.Add(id);
            return new Span(new Run("\u200B")) { Tag = ci };
        }

        // ---------------- fields ----------------
        InlineCollection FieldChar(string type, InlineCollection into)
        {
            switch (type)
            {
                case "begin":
                    _fields.Add(new FieldState { Saved = into });
                    return into;
                case "separate":
                    {
                        if (_fields.Count == 0) return into;
                        var f = _fields[_fields.Count - 1];
                        f.Separated = true;
                        var span = FieldSpan(f.Instr.ToString());
                        if (span == null) return into;
                        into.Add(span);
                        f.Span = span; f.Saved = into;
                        return span.Inlines;
                    }
                case "end":
                    {
                        if (_fields.Count == 0) return into;
                        var f = _fields[_fields.Count - 1];
                        _fields.RemoveAt(_fields.Count - 1);
                        var instr = f.Instr.ToString().Trim();
                        if (!f.Separated && (instr.StartsWith("XE ") || instr.StartsWith("TA ")))
                        {
                            var m = Regex.Match(instr, "\"([^\"]*)\"");
                            var cat = Regex.Match(instr, @"\\c\s+(\d+)");
                            string[] cats = { "Cases", "Statutes", "Other Authorities", "Rules", "Treatises", "Regulations", "Constitutional Provisions" };
                            var entry = new IndexEntry { Term = m.Success ? m.Groups[1].Value : "", Kind = instr.StartsWith("TA") ? "TA" : "XE", Category = cat.Success && int.TryParse(cat.Groups[1].Value, out var ci) && ci >= 1 && ci <= cats.Length ? cats[ci - 1] : null };
                            if (into.LastInline is Run r)
                            {
                                into.Remove(r);
                                var span = new Span { Tag = entry };
                                span.Inlines.Add(r);
                                into.Add(span);
                            }
                        }
                        return f.Span != null ? f.Saved : into;
                    }
            }
            return into;
        }

        static Span FieldSpan(string instr)
        {
            instr = (instr ?? "").Trim();
            if (instr.Length == 0) return null;
            var head = instr.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            var name = head[0].ToUpperInvariant();
            var arg = head.Length > 1 ? head[1].Trim() : "";
            switch (name)
            {
                case "MERGEFIELD":
                    {
                        var field = Regex.Match(arg, "^\"?([^\"\\\\]+)\"?").Groups[1].Value.Trim();
                        return new Span { Tag = new MergeFieldInfo { Name = field.Replace('_', ' ') } };
                    }
                case "CITATION":
                    return new Span { Tag = new CitationRef { SourceTag = arg.Split(' ')[0] } };
                case "HYPERLINK":
                    {
                        var anchor = Regex.Match(arg, "\\\\l\\s+\"([^\"]+)\"");
                        var url = Regex.Match(arg, "^\"([^\"]+)\"");
                        try
                        {
                            if (anchor.Success) return new Hyperlink { NavigateUri = new Uri("#" + anchor.Groups[1].Value, UriKind.Relative) };
                            if (url.Success) return new Hyperlink { NavigateUri = new Uri(url.Groups[1].Value, UriKind.Absolute) };
                        }
                        catch { }
                        return null;
                    }
                case "PAGE": case "NUMPAGES": case "DATE": case "TIME": case "AUTHOR": case "TITLE": case "FILENAME": case "NUMWORDS": case "NUMCHARS":
                case "SAVEDATE": case "CREATEDATE": case "USERNAME": case "USERINITIALS": case "PAGEREF": case "REF": case "SEQ": case "SUBJECT": case "KEYWORDS": case "DOCPROPERTY":
                    return new Span { Tag = new FieldInfo { Code = instr } };
                default:
                    return name.StartsWith("=") ? new Span { Tag = new FieldInfo { Code = instr } } : null;
            }
        }

        // ---------------- footnotes / endnotes ----------------
        Span NoteReference(bool endnote, int id, RP rp)
        {
            var run = new Run("*") { BaselineAlignment = BaselineAlignment.Superscript };
            if (rp?.Size.HasValue == true) run.FontSize = rp.Size.Value * Fmt.PxPerPt * 0.75;
            var span = new Span(run) { Tag = new NoteRef { Endnote = endnote, Number = id } };
            _noteRefs.Add((span, endnote, id));
            return span;
        }

        void FinishNotes(FlowDocument fd)
        {
            if (_noteRefs.Count == 0) return;
            var sec = new Section { Tag = SectionKind.Notes };
            var sep = new Paragraph { Tag = "NoteSeparator", Margin = new Thickness(0, 24, 0, 6), BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 0, 0, 0.75), FontSize = 4 };
            sep.Inlines.Add(new Run(" "));
            sec.Blocks.Add(sep);
            var bodies = new List<Paragraph>();
            foreach (var endnote in new[] { false, true })
            {
                int n = 0;
                var src = endnote ? _endnotes : _footnotes;
                foreach (var (span, en, id) in _noteRefs.Where(r => r.Endnote == endnote))
                {
                    n++;
                    var label = endnote ? DecoratedPaginator.FormatNumber(n, new PageSettings { NumFormat = "i" }) : n.ToString();
                    ((NoteRef)span.Tag).Number = n;
                    if (span.Inlines.FirstInline is Run r) r.Text = label;
                    var body = new Paragraph { Tag = new NoteBody { Endnote = endnote, Number = n }, FontSize = 10 * Fmt.PxPerPt, Margin = new Thickness(0, 0, 0, 3) };
                    body.Inlines.Add(new Span(new Run(label) { BaselineAlignment = BaselineAlignment.Superscript }) { Tag = "NoteNum" });
                    body.Inlines.Add(new Run(" "));
                    var note = src?.Root?.Elements(W + (endnote ? "endnote" : "footnote")).FirstOrDefault(x => x.Attribute(W + "id")?.Value == id.ToString());
                    if (note != null)
                    {
                        bool first = true;
                        foreach (var p in note.Elements(W + "p"))
                        {
                            _ranges.Clear(); _fields.Clear();
                            foreach (var (para, _) in ReadParagraph(p))
                            {
                                if (!first) body.Inlines.Add(new LineBreak());
                                var kids = para.Inlines.ToList();
                                // drop the leading space Word puts after the note mark
                                if (first && kids.FirstOrDefault() is Run lead && lead.Text.StartsWith(" ")) lead.Text = lead.Text.TrimStart();
                                foreach (var k in kids) { para.Inlines.Remove(k); body.Inlines.Add(k); }
                                first = false;
                            }
                        }
                    }
                    bodies.Add(body);
                }
            }
            foreach (var b in bodies) sec.Blocks.Add(b);
            fd.Blocks.Add(sec);
        }

        // ---------------- generated sections (structured document tags) ----------------
        void ReadBlockSdt(XElement el, BlockCollection into)
        {
            var sdtPr = el.Element(W + "sdtPr");
            var content = el.Element(W + "sdtContent")?.Elements() ?? Enumerable.Empty<XElement>();
            var tag = sdtPr?.Element(W + "tag")?.Attribute(W + "val")?.Value;
            var gallery = sdtPr?.Descendants(W + "docPartGallery").FirstOrDefault()?.Attribute(W + "val")?.Value;
            string kind = tag != null && tag.StartsWith("Valco:") ? tag.Substring(6)
                : gallery == "Table of Contents" ? SectionKind.TOC : gallery == "Bibliographies" ? SectionKind.Bibliography : gallery == "Cover Pages" ? "Cover" : null;
            if (kind == null) { ReadBlocks(content, into); return; }
            var sec = new Section { Tag = kind };
            Table entries = null;
            foreach (var e in content)
            {
                var style = e.Name == W + "p" ? e.Element(W + "pPr")?.Element(W + "pStyle")?.Attribute(W + "val")?.Value : null;
                var m = style == null ? null : Regex.Match(style, @"^(TOC|Index|TOA)(\d)$", RegexOptions.IgnoreCase);
                if (m != null && m.Success)
                {
                    if (entries == null || sec.Blocks.LastBlock != entries)
                    {
                        entries = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 12) };
                        entries.Columns.Add(new TableColumn { Width = new GridLength(564) });
                        entries.Columns.Add(new TableColumn { Width = new GridLength(58) });
                        entries.RowGroups.Add(new TableRowGroup());
                        sec.Blocks.Add(entries);
                    }
                    int level = int.Parse(m.Groups[2].Value);
                    // text before the first tab is the entry, after it the page number
                    var sb = new StringBuilder(); string page = null; bool inInstr = false;
                    foreach (var d in e.Descendants())
                    {
                        if (d.Name == W + "fldChar") { var t = d.Attribute(W + "fldCharType")?.Value; inInstr = t == "begin"; if (t == "separate") inInstr = false; }
                        else if (d.Name == W + "tab" && d.Parent?.Name == W + "r") { if (page == null) { page = ""; } }
                        else if (d.Name == W + "t" && !inInstr) { if (page == null) sb.Append(d.Value); else page += d.Value; }
                    }
                    var row = new TableRow { Tag = level };
                    var lp = new Paragraph { Margin = new Thickness(14 * (level - 1), 0, 0, 0) };
                    if (level == 1 && kind == SectionKind.TOC) lp.FontWeight = FontWeights.Bold;
                    lp.Inlines.Add(new Run(sb.ToString()));
                    var rp = new Paragraph { Margin = new Thickness(0), TextAlignment = TextAlignment.Right };
                    if (page != null) rp.Inlines.Add(new Run(page.Trim()));
                    row.Cells.Add(new TableCell(lp) { BorderThickness = new Thickness(0), Padding = new Thickness(0, 1, 4, 1) });
                    row.Cells.Add(new TableCell(rp) { BorderThickness = new Thickness(0), Padding = new Thickness(0, 1, 0, 1) });
                    entries.RowGroups[0].Rows.Add(row);
                }
                else ReadBlocks(new[] { e }, sec.Blocks);
            }
            if (sec.Blocks.Count == 0) sec.Blocks.Add(new Paragraph());
            into.Add(sec);
        }

        // ---------------- drop caps ----------------
        bool TryReadDropCap(XElement p)
        {
            _ranges.Clear(); _fields.Clear();
            var fp = p.Element(W + "pPr")?.Element(W + "framePr");
            var mode = fp?.Attribute(W + "dropCap")?.Value;
            if (mode != "drop" && mode != "margin") return false;
            var r = p.Elements(W + "r").FirstOrDefault(x => x.Element(W + "t") != null);
            if (r == null) return true;
            var rp = RunStyleProps(_defRP, r.Element(W + "rPr"));
            int lines = (int)(Num(fp, "lines") ?? 3);
            _pendingDrop = (string.Concat(p.Descendants(W + "t").Select(t => t.Value)), rp.Font, rp.Size ?? 33, rp.Color, Math.Max(1, lines), mode == "margin");
            return true;
        }

        void ApplyPendingDropCap(Paragraph host)
        {
            if (_pendingDrop == null) return;
            var d = _pendingDrop.Value;
            _pendingDrop = null;
            if (string.IsNullOrEmpty(d.Letter)) return;
            double hostSize = host.FontSize > 0 ? host.FontSize : 14.67, lineH = hostSize * 1.22;
            double size = Math.Max(d.SizePt * Fmt.PxPerPt, lineH * d.Lines * 0.92);
            var ff = new FontFamily(d.Font ?? "Calibri");
            var ft = new FormattedText(d.Letter, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(ff, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, Brushes.Black, 1.0);
            var run = new Run(d.Letter) { FontSize = size, FontFamily = ff };
            if (d.Color.HasValue) run.Foreground = new SolidColorBrush(d.Color.Value);
            var lp = new Paragraph(run) { Margin = new Thickness(0), Padding = new Thickness(0), LineHeight = lineH * d.Lines, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
            var f = new Floater(lp)
            {
                Tag = "DropCap", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0), Width = Math.Ceiling(ft.WidthIncludingTrailingWhitespace + 2),
                Margin = d.Margin ? new Thickness(-(ft.WidthIncludingTrailingWhitespace + 6), 0, 4, 0) : new Thickness(0, 0, 4, 0)
            };
            if (host.Inlines.FirstInline != null) host.Inlines.InsertBefore(host.Inlines.FirstInline, f); else host.Inlines.Add(f);
        }

        // ---------------- tables ----------------
        static void ApplyCellBorders(TableCell cell, XElement tcb, bool tableBorders)
        {
            double Side(string name, double fallback)
            {
                var e = tcb.Element(W + name) ?? tcb.Element(W + (name == "left" ? "start" : name == "right" ? "end" : name));
                if (e == null) return fallback;
                var v = e.Attribute(W + "val")?.Value;
                if (v == null || v == "nil" || v == "none") return 0;
                var col = ParseHex(e.Attribute(W + "color")?.Value);
                if (col.HasValue) cell.BorderBrush = new SolidColorBrush(col.Value);
                return Math.Max(0.5, (Num(e, "sz") ?? 4) / 8 * 96 / 72);
            }
            double def = tableBorders ? 0.5 : 0;
            cell.BorderThickness = new Thickness(Side("left", def), Side("top", def), Side("right", def), Side("bottom", def));
            if (cell.BorderBrush == null) cell.BorderBrush = Brushes.Black;
        }

        static void ApplyCellMargins(TableCell cell, XElement mar)
        {
            if (mar == null) return;
            double M(string n, double d) { var e = mar.Element(W + n); var w = Num(e, "w"); return w.HasValue ? w.Value / 15 : d; }
            cell.Padding = new Thickness(M("left", cell.Padding.Left), M("top", cell.Padding.Top), M("right", cell.Padding.Right), M("bottom", cell.Padding.Bottom));
        }

        bool MergeIntoAbove(TableRowGroup group, TableRow row, TableCell cell)
        {
            if (!_cellCol.TryGetValue(cell, out var col)) return false;
            for (int i = group.Rows.Count - 1; i >= 0; i--)
            {
                var above = group.Rows[i].Cells.FirstOrDefault(c => _cellCol.TryGetValue(c, out var cc) && cc == col);
                if (above == null) continue;
                above.RowSpan = Math.Max(1, above.RowSpan) + 1;
                return true;
            }
            return false;
        }

        // ---------------- page: header/footer slots, watermark, columns, borders ----------------
        (string, string, string) ReadSlots(XDocument x)
        {
            var paras = x.Root.Elements(W + "p").Concat(x.Root.Descendants(W + "sdtContent").Elements(W + "p"))
                .Where(p => p.Descendants(W + "t").Any() || p.Descendants(W + "fldSimple").Any() || p.Descendants(W + "instrText").Any()).ToList();
            var p0 = paras.FirstOrDefault();
            if (p0 == null) return ("", "", "");
            var parts = new List<StringBuilder> { new StringBuilder() };
            string Token(string instr)
            {
                var n = instr.Trim().Split(' ')[0].ToUpperInvariant();
                return n switch { "PAGE" => "{PAGE}", "NUMPAGES" or "SECTIONPAGES" => "{NUMPAGES}", "DATE" or "TIME" or "PRINTDATE" or "SAVEDATE" => "{DATE}", "TITLE" => "{TITLE}", "AUTHOR" => "{AUTHOR}", _ => "" };
            }
            int state = 0; var instrBuf = new StringBuilder();
            foreach (var d in p0.Descendants())
            {
                if (d.Name == W + "fldSimple") { parts[^1].Append(Token(d.Attribute(W + "instr")?.Value ?? "")); continue; }
                if (d.Ancestors(W + "fldSimple").Any()) continue;
                if (d.Name == W + "fldChar")
                {
                    var t = d.Attribute(W + "fldCharType")?.Value;
                    if (t == "begin") { state = 1; instrBuf.Clear(); }
                    else if (t == "separate") { parts[^1].Append(Token(instrBuf.ToString())); state = 2; }
                    else if (t == "end") { if (state == 1) parts[^1].Append(Token(instrBuf.ToString())); state = 0; }
                    continue;
                }
                if (d.Name == W + "instrText") { instrBuf.Append(d.Value); continue; }
                if (state == 2) continue;
                if (d.Name == W + "tab" && d.Parent?.Name == W + "r") { parts.Add(new StringBuilder()); continue; }
                if (d.Name == W + "t") parts[^1].Append(d.Value);
            }
            var s = parts.Select(b => b.ToString().Trim()).ToList();
            if (s.Count >= 3) return (s[0], s[1], string.Join(" ", s.Skip(2)));
            if (s.Count == 2) return (s[0], "", s[1]);
            var jc = p0.Element(W + "pPr")?.Element(W + "jc")?.Attribute(W + "val")?.Value;
            return jc == "center" ? ("", s[0], "") : jc == "right" || jc == "end" ? ("", "", s[0]) : (s[0], "", "");
        }

        static void ReadWatermark(XDocument x, PageSettings page)
        {
            var tp = x.Descendants(V + "textpath").FirstOrDefault();
            var text = tp?.Attribute("string")?.Value;
            if (string.IsNullOrEmpty(text)) return;
            page.Watermark = text;
            var fill = tp.Parent?.Attribute("fillcolor")?.Value?.TrimStart('#');
            if (fill != null && fill.Length == 6) page.WatermarkColor = fill.ToUpperInvariant();
        }

        void ReadSectionExtras(XElement sect, PageSettings page)
        {
            var cols = sect.Element(W + "cols");
            if (cols != null)
            {
                page.Columns = Math.Max(1, Math.Min(4, (int)(Num(cols, "num") ?? 1)));
                var sp = Num(cols, "space"); if (sp.HasValue) page.ColumnSpacingIn = sp.Value / 1440;
            }
            var pb = sect.Element(W + "pgBorders");
            var top = pb?.Element(W + "top");
            if (top != null && top.Attribute(W + "val")?.Value is string v && v != "nil" && v != "none")
            {
                page.PageBorder = true;
                var c = top.Attribute(W + "color")?.Value; if (c != null && c.Length == 6) page.PageBorderColor = c.ToUpperInvariant();
                page.PageBorderWidth = Math.Max(0.25, (Num(top, "sz") ?? 6) / 6);
            }
            page.LineNumbers = sect.Element(W + "lnNumType") != null;
            var pn = sect.Element(W + "pgNumType");
            if (pn != null)
            {
                page.NumFormat = pn.Attribute(W + "fmt")?.Value switch { "lowerLetter" => "a", "upperLetter" => "A", "lowerRoman" => "i", "upperRoman" => "I", "numberInDash" => "-1-", _ => "1" };
                page.NumStart = (int)(Num(pn, "start") ?? 1);
            }
        }

        void ReadDocumentBackground(XDocument xdoc, PageSettings page)
        {
            var c = xdoc.Root.Element(W + "background")?.Attribute(W + "color")?.Value;
            if (c != null && c.Length == 6 && c.ToUpperInvariant() != "FFFFFF") page.PageColor = c.ToUpperInvariant();
            if (_settings?.Root?.Element(W + "autoHyphenation") is XElement ah && On(ah.Attribute(W + "val")?.Value)) page.Hyphenation = true;
        }

        // ---------------- Valco extras, sources, settings ----------------
        DocExtras ReadExtras(ref PageSettings page)
        {
            var ex = new DocExtras();
            var set = _settings?.Root;
            if (set != null)
            {
                if (set.Element(W + "trackRevisions") is XElement tr && On(tr.Attribute(W + "val")?.Value)) ex.TrackChanges = true;
                var dp = set.Element(W + "documentProtection");
                if (dp != null && On(dp.Attribute(W + "enforcement")?.Value))
                {
                    ex.Protected = true;
                    ex.Custom["protectMode"] = dp.Attribute(W + "edit")?.Value switch { "comments" => "comments", "trackedChanges" => "tracked", _ => "read" };
                }
            }
            foreach (var root in _customXml)
            {
                if (root.Name.Namespace == Bib)
                {
                    var style = root.Attribute("StyleName")?.Value;
                    if (!string.IsNullOrEmpty(style)) ex.BibStyle = style.Contains("MLA") ? "MLA" : style.Contains("Chicago") ? "Chicago" : "APA";
                    foreach (var s in root.Elements(Bib + "Source"))
                    {
                        string G(string n) => s.Element(Bib + n)?.Value ?? "";
                        var people = s.Descendants(Bib + "Person").Select(p => (p.Element(Bib + "Last")?.Value ?? "") + (p.Element(Bib + "First") != null ? ", " + p.Element(Bib + "First").Value : "")).ToList();
                        var corp = s.Descendants(Bib + "Corporate").FirstOrDefault()?.Value;
                        ex.Sources.Add(new Source
                        {
                            Tag = G("Tag"), Type = Regex.Replace(G("SourceType"), "(?<=[a-z])(?=[A-Z])", " ").Replace("Internet Site", "Web site"),
                            Author = people.Count > 0 ? string.Join("; ", people) : corp ?? "", Title = G("Title"), Year = G("Year"), Publisher = G("Publisher"), City = G("City"),
                            Url = G("URL"), Journal = G("JournalName"), Pages = G("Pages")
                        });
                    }
                }
                else if (root.Name.Namespace == VW)
                {
                    string V(string n) => root.Element(VW + n)?.Value;
                    bool B(string n) => bool.TryParse(V(n), out var b) && b;
                    ex.Theme = V("theme") ?? ex.Theme; ex.StyleSet = V("styleSet") ?? ex.StyleSet; ex.MarkedFinal = B("final");
                    ex.MergeSource = string.IsNullOrEmpty(V("mergeSource")) ? null : V("mergeSource");
                    ex.MergeType = string.IsNullOrEmpty(V("mergeType")) ? null : V("mergeType");
                    var mf = V("mergeFields"); if (!string.IsNullOrEmpty(mf)) ex.MergeFields = mf.Split('\u001f').ToList();
                    ex.MergeRecords = root.Element(VW + "mergeRecords")?.Elements(VW + "r").Select(r => r.Elements(VW + "f").GroupBy(f => (string)f.Attribute("n") ?? "").ToDictionary(g => g.Key, g => g.First().Value)).ToList() ?? ex.MergeRecords;
                    foreach (var n in (V("mergeExcluded") ?? "").Split(',')) if (int.TryParse(n, out var i)) ex.MergeExcluded.Add(i);
                    if (int.TryParse(V("mergeIndex"), out var mi)) ex.MergeIndex = mi;
                    ex.MergePreview = B("mergePreview");
                    if (root.Element(VW + "track") != null) ex.TrackChanges = B("track");
                    if (root.Element(VW + "protected") != null) ex.Protected = B("protected");
                    ex.ProtectHash = V("protectHash");
                    if (!string.IsNullOrEmpty(V("bibStyle"))) ex.BibStyle = V("bibStyle");
                    foreach (var c in root.Element(VW + "custom")?.Elements(VW + "c") ?? Enumerable.Empty<XElement>()) ex.Custom[(string)c.Attribute("k") ?? ""] = c.Value;
                    var pj = V("page");
                    if (!string.IsNullOrEmpty(pj))
                    {
                        try { var saved = System.Text.Json.JsonSerializer.Deserialize<PageSettings>(pj); if (saved != null) page = saved; } catch { }
                    }
                }
            }
            // merge preview text was saved; show placeholders again unless preview stays on
            return ex;
        }
    }
}
