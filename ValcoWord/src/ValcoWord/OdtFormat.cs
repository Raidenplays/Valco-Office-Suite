using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace ValcoWord
{
    /// <summary>OpenDocument Text (.odt) reader and writer for the formatting Valco Word uses.</summary>
    public static class OdtFormat
    {
        static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
        static readonly XNamespace Style = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
        static readonly XNamespace Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
        static readonly XNamespace Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
        static readonly XNamespace Draw = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";
        static readonly XNamespace Fo = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
        static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";
        static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
        static readonly XNamespace Meta = "urn:oasis:names:tc:opendocument:xmlns:meta:1.0";
        static readonly XNamespace Svg = "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0";
        static readonly XNamespace Manifest = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static string In(double px) => (px / 96).ToString("0.####", Inv) + "in";
        static string Pt(double px) => (px * 72 / 96).ToString("0.##", Inv) + "pt";

        // =====================================================================================
        //  Writer
        // =====================================================================================
        class Writer
        {
            readonly Dictionary<string, string> _textStyles = new Dictionary<string, string>();
            readonly Dictionary<string, string> _paraStyles = new Dictionary<string, string>();
            readonly Dictionary<string, string> _cellStyles = new Dictionary<string, string>();
            readonly XElement _auto = new XElement(Office + "automatic-styles");
            readonly List<(string Name, byte[] Bytes, string Mime)> _images = new List<(string, byte[], string)>();
            int _tableN;

            public void Save(FlowDocument doc, PageSettings page, string title, string author, string path)
            {
                var body = new XElement(Office + "text");
                WriteBlocks(doc.Blocks, body);
                var content = new XDocument(new XElement(Office + "document-content", NsAttrs(), new XAttribute(Office + "version", "1.2"),
                    new XElement(Office + "font-face-decls"), _auto, new XElement(Office + "body", body)));
                var styles = new XDocument(new XElement(Office + "document-styles", NsAttrs(), new XAttribute(Office + "version", "1.2"),
                    new XElement(Office + "styles",
                        new XElement(Style + "default-style", new XAttribute(Style + "family", "paragraph"),
                            new XElement(Style + "text-properties", new XAttribute(Style + "font-name", Settings.Current.DefaultFont), new XAttribute(Fo + "font-family", Settings.Current.DefaultFont), new XAttribute(Fo + "font-size", Settings.Current.DefaultSizePt.ToString(Inv) + "pt"))),
                        HeadingStyle(1, 16), HeadingStyle(2, 13), HeadingStyle(3, 12)),
                    new XElement(Office + "automatic-styles",
                        new XElement(Style + "page-layout", new XAttribute(Style + "name", "pm1"),
                            new XElement(Style + "page-layout-properties",
                                new XAttribute(Fo + "page-width", page.WidthIn.ToString("0.###", Inv) + "in"), new XAttribute(Fo + "page-height", page.HeightIn.ToString("0.###", Inv) + "in"),
                                new XAttribute(Style + "print-orientation", page.Landscape ? "landscape" : "portrait"),
                                new XAttribute(Fo + "margin-left", page.MarginLeft.ToString(Inv) + "in"), new XAttribute(Fo + "margin-right", page.MarginRight.ToString(Inv) + "in"),
                                new XAttribute(Fo + "margin-top", page.MarginTop.ToString(Inv) + "in"), new XAttribute(Fo + "margin-bottom", page.MarginBottom.ToString(Inv) + "in")))),
                    new XElement(Office + "master-styles",
                        new XElement(Style + "master-page", new XAttribute(Style + "name", "Standard"), new XAttribute(Style + "page-layout-name", "pm1"),
                            HeaderFooter(page, true), HeaderFooter(page, false)))));
                var meta = new XDocument(new XElement(Office + "document-meta", NsAttrs(), new XAttribute(Office + "version", "1.2"),
                    new XElement(Office + "meta", new XElement(Meta + "generator", "Valco Word"), new XElement(Dc + "title", title ?? ""), new XElement(Meta + "initial-creator", author ?? ""), new XElement(Dc + "creator", author ?? ""),
                        new XElement(Dc + "date", DateTime.UtcNow.ToString("s", Inv)))));
                var manifest = new XElement(Manifest + "manifest", new XAttribute(XNamespace.Xmlns + "manifest", Manifest), new XAttribute(Manifest + "version", "1.2"),
                    FileEntry("/", "application/vnd.oasis.opendocument.text"), FileEntry("content.xml", "text/xml"), FileEntry("styles.xml", "text/xml"), FileEntry("meta.xml", "text/xml"));
                foreach (var img in _images) manifest.Add(FileEntry("Pictures/" + img.Name, img.Mime));

                var tmp = path + ".vwtmp";
                using (var fs = new FileStream(tmp, FileMode.Create))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    var mt = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
                    using (var w = new StreamWriter(mt.Open(), new UTF8Encoding(false))) w.Write("application/vnd.oasis.opendocument.text");
                    void Xml(string name, XDocument x) { using var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open(); x.Save(s, SaveOptions.DisableFormatting); }
                    Xml("content.xml", content); Xml("styles.xml", styles); Xml("meta.xml", meta);
                    Xml("META-INF/manifest.xml", new XDocument(manifest));
                    foreach (var img in _images) using (var s = zip.CreateEntry("Pictures/" + img.Name, CompressionLevel.NoCompression).Open()) s.Write(img.Bytes, 0, img.Bytes.Length);
                }
                if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
            }

            static XElement FileEntry(string path, string type) => new XElement(Manifest + "file-entry", new XAttribute(Manifest + "full-path", path), new XAttribute(Manifest + "media-type", type));

            static object[] NsAttrs() => new object[]
            {
                new XAttribute(XNamespace.Xmlns + "office", Office), new XAttribute(XNamespace.Xmlns + "style", Style), new XAttribute(XNamespace.Xmlns + "text", Text),
                new XAttribute(XNamespace.Xmlns + "table", Table), new XAttribute(XNamespace.Xmlns + "draw", Draw), new XAttribute(XNamespace.Xmlns + "fo", Fo),
                new XAttribute(XNamespace.Xmlns + "xlink", XLink), new XAttribute(XNamespace.Xmlns + "dc", Dc), new XAttribute(XNamespace.Xmlns + "meta", Meta), new XAttribute(XNamespace.Xmlns + "svg", Svg),
            };

            static XElement HeadingStyle(int level, double size) => new XElement(Style + "style", new XAttribute(Style + "name", "Heading_20_" + level), new XAttribute(Style + "display-name", "Heading " + level),
                new XAttribute(Style + "family", "paragraph"), new XAttribute(Style + "default-outline-level", level),
                new XElement(Style + "paragraph-properties", new XAttribute(Fo + "margin-top", "0.17in"), new XAttribute(Fo + "margin-bottom", "0.08in"), new XAttribute(Fo + "keep-with-next", "always")),
                new XElement(Style + "text-properties", new XAttribute(Fo + "font-size", size.ToString(Inv) + "pt"), new XAttribute(Fo + "font-weight", "bold")));

            static XElement HeaderFooter(PageSettings p, bool header)
            {
                var parts = header ? new[] { p.HeaderLeft, p.HeaderText, p.HeaderRight } : new[] { p.FooterLeft, p.FooterCenter, p.FooterRight };
                if (parts.All(string.IsNullOrWhiteSpace)) return null;
                var para = new XElement(Text + "p");
                for (int i = 0; i < 3; i++)
                {
                    if (i > 0) para.Add(new XElement(Text + "tab"));
                    var s = parts[i] ?? "";
                    int pos = 0;
                    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(s, @"\{(PAGE|NUMPAGES|DATE|TITLE|AUTHOR)\}"))
                    {
                        if (m.Index > pos) para.Add(s.Substring(pos, m.Index - pos));
                        para.Add(m.Groups[1].Value switch
                        {
                            "PAGE" => new XElement(Text + "page-number", new XAttribute(Text + "select-page", "current"), "1"),
                            "NUMPAGES" => new XElement(Text + "page-count", "1"),
                            "DATE" => new XElement(Text + "date", DateTime.Now.ToString("d")),
                            "TITLE" => new XElement(Text + "title"),
                            _ => new XElement(Text + "initial-creator"),
                        });
                        pos = m.Index + m.Length;
                    }
                    if (pos < s.Length) para.Add(s.Substring(pos));
                }
                return new XElement(Style + (header ? "header" : "footer"), para);
            }

            void WriteBlocks(IEnumerable<Block> blocks, XElement into)
            {
                foreach (var b in blocks.ToList())
                {
                    switch (b)
                    {
                        case Paragraph p: into.Add(Para(p)); break;
                        case List l: into.Add(ListXml(l)); break;
                        case Table t: into.Add(TableXml(t)); break;
                        case Section s when SectionKind.Of(s) == SectionKind.Notes: break;
                        case Section s: WriteBlocks(s.Blocks, into); break;
                        case BlockUIContainer bc when bc.Child is Image img:
                            {
                                var p = new XElement(Text + "p", new XAttribute(Text + "style-name", ParaStyle(TextAlignment.Center, new Thickness(0), 0, null, false)));
                                var f = Frame(img); if (f != null) p.Add(f);
                                into.Add(p);
                                break;
                            }
                    }
                }
            }

            XElement ListXml(List l)
            {
                var x = new XElement(Text + "list");
                foreach (var li in l.ListItems)
                {
                    var item = new XElement(Text + "list-item");
                    WriteBlocks(li.Blocks, item);
                    x.Add(item);
                }
                return x;
            }

            XElement Para(Paragraph p)
            {
                var tag = p.Tag as string;
                int level = tag != null && tag.StartsWith("Heading") && int.TryParse(tag.Substring(7), out var lv) ? lv : 0;
                var x = level > 0 ? new XElement(Text + "h", new XAttribute(Text + "outline-level", level)) : new XElement(Text + "p");
                var bg = p.Background is SolidColorBrush sb ? Fmt.Hex(sb.Color) : null;
                x.Add(new XAttribute(Text + "style-name", ParaStyle(p.TextAlignment, p.Margin, p.TextIndent, bg, p.BreakPageBefore, level > 0 ? "Heading_20_" + level : null)));
                WriteInlines(p.Inlines, x);
                return x;
            }

            string ParaStyle(TextAlignment a, Thickness m, double indent, string bg, bool pageBreak, string parent = null)
            {
                string align = a switch { TextAlignment.Center => "center", TextAlignment.Right => "end", TextAlignment.Justify => "justify", _ => "start" };
                double Safe(double v) => double.IsNaN(v) ? 0 : v;
                string key = $"{align}|{Safe(m.Left)}|{Safe(m.Right)}|{Safe(m.Top)}|{Safe(m.Bottom)}|{Safe(indent)}|{bg}|{pageBreak}|{parent}";
                if (_paraStyles.TryGetValue(key, out var name)) return name;
                name = "P" + (_paraStyles.Count + 1);
                _paraStyles[key] = name;
                var props = new XElement(Style + "paragraph-properties", new XAttribute(Fo + "text-align", align),
                    new XAttribute(Fo + "margin-left", In(Safe(m.Left))), new XAttribute(Fo + "margin-right", In(Safe(m.Right))),
                    new XAttribute(Fo + "margin-top", In(Safe(m.Top))), new XAttribute(Fo + "margin-bottom", In(Safe(m.Bottom))), new XAttribute(Fo + "text-indent", In(Safe(indent))));
                if (bg != null) props.Add(new XAttribute(Fo + "background-color", "#" + bg));
                if (pageBreak) props.Add(new XAttribute(Fo + "break-before", "page"));
                var st = new XElement(Style + "style", new XAttribute(Style + "name", name), new XAttribute(Style + "family", "paragraph"), props);
                if (parent != null) st.Add(new XAttribute(Style + "parent-style-name", parent));
                _auto.Add(st);
                return name;
            }

            void WriteInlines(InlineCollection inlines, XElement into)
            {
                foreach (var i in inlines.ToList())
                {
                    switch (i)
                    {
                        case Run r: WriteRun(r, into); break;
                        case LineBreak _: into.Add(new XElement(Text + "line-break")); break;
                        case Hyperlink h when h.NavigateUri != null:
                            {
                                var a = new XElement(Text + "a", new XAttribute(XLink + "type", "simple"), new XAttribute(XLink + "href", h.NavigateUri.OriginalString));
                                WriteInlines(h.Inlines, a); into.Add(a); break;
                            }
                        case Span s when s.Tag is RevInfo ri && !ri.Insert: break; // deleted text isn't part of the saved text
                        case Span s when s.Tag is NoteRef nr:
                            {
                                var body = NotesBody(s, nr);
                                var note = new XElement(Text + "note", new XAttribute(Text + "note-class", nr.Endnote ? "endnote" : "footnote"),
                                    new XElement(Text + "note-citation", nr.Number), new XElement(Text + "note-body", new XElement(Text + "p", body ?? "")));
                                into.Add(note);
                                break;
                            }
                        case Span s when s.Tag is BookmarkInfo bm:
                            into.Add(new XElement(Text + "bookmark-start", new XAttribute(Text + "name", bm.Name)));
                            WriteInlines(s.Inlines, into);
                            into.Add(new XElement(Text + "bookmark-end", new XAttribute(Text + "name", bm.Name)));
                            break;
                        case Span s when s.Tag is CommentInfo ci:
                            into.Add(new XElement(Office + "annotation", new XElement(Dc + "creator", ci.Author), new XElement(Dc + "date", ci.Date.ToString("s", Inv)), new XElement(Text + "p", ci.Text)));
                            WriteInlines(s.Inlines, into);
                            break;
                        case Span s: WriteInlines(s.Inlines, into); break;
                        case InlineUIContainer c when c.Child is Image img: { var f = Frame(img); if (f != null) into.Add(f); break; }
                        case Floater f:
                            {
                                if ((f.Tag as string) == "DropCap") { foreach (var r in Fmt.AllParagraphs(f.Blocks).SelectMany(Fmt.Descendants).OfType<Run>()) WriteRun(r, into); break; }
                                var img = f.Blocks.OfType<BlockUIContainer>().Select(b => b.Child).OfType<Image>().FirstOrDefault();
                                if (img != null) { var fr = Frame(img); if (fr != null) into.Add(fr); }
                                break;
                            }
                    }
                }
            }

            static string NotesBody(Span s, NoteRef nr)
            {
                FlowDocument fd = null;
                for (DependencyObject d = s; d != null; d = LogicalTreeHelper.GetParent(d)) if (d is FlowDocument f) { fd = f; break; }
                var sec = fd?.Blocks.OfType<Section>().FirstOrDefault(x => SectionKind.Of(x) == SectionKind.Notes);
                var p = sec?.Blocks.OfType<Paragraph>().FirstOrDefault(x => x.Tag is NoteBody nb && nb.Endnote == nr.Endnote && nb.Number == nr.Number);
                return p == null ? null : string.Concat(p.Inlines.Skip(1).Select(x => x is Run r ? r.Text : x is Span sp ? Spans.Text(sp) : "")).Trim();
            }

            void WriteRun(Run r, XElement into)
            {
                var text = r.Text ?? "";
                if (text.Length == 0) return;
                var span = new XElement(Text + "span", new XAttribute(Text + "style-name", TextStyle(r)));
                var buf = new StringBuilder();
                void Flush() { if (buf.Length > 0) { span.Add(buf.ToString()); buf.Clear(); } }
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    if (c == '\t') { Flush(); span.Add(new XElement(Text + "tab")); }
                    else if (c == '\n' || c == '\u2028' || c == '\v') { Flush(); span.Add(new XElement(Text + "line-break")); }
                    else if (c == ' ' && i + 1 < text.Length && text[i + 1] == ' ')
                    {
                        int n = 0; while (i + n < text.Length && text[i + n] == ' ') n++;
                        buf.Append(' '); Flush(); span.Add(new XElement(Text + "s", new XAttribute(Text + "c", n - 1))); i += n - 1;
                    }
                    else if (c >= 0x20 || c == '​') buf.Append(c);
                }
                Flush();
                into.Add(span);
            }

            string TextStyle(Run r)
            {
                var family = (r.FontFamily?.Source ?? "Calibri").Split(',')[0].Trim();
                bool bold = r.FontWeight.ToOpenTypeWeight() >= 600, italic = r.FontStyle != FontStyles.Normal;
                var deco = Fmt.EffectiveDecorations(r);
                bool under = Fmt.HasDecoration(deco, TextDecorationLocation.Underline), strike = Fmt.HasDecoration(deco, TextDecorationLocation.Strikethrough);
                var fg = Fmt.BrushColor(Fmt.EffectiveForeground(r)); var bg = Fmt.BrushColor(Fmt.EffectiveBackground(r));
                var ba = Fmt.EffectiveBaseline(r);
                string key = $"{family}|{r.FontSize:0.##}|{bold}|{italic}|{under}|{strike}|{fg}|{bg}|{ba}";
                if (_textStyles.TryGetValue(key, out var name)) return name;
                name = "T" + (_textStyles.Count + 1);
                _textStyles[key] = name;
                var tp = new XElement(Style + "text-properties", new XAttribute(Style + "font-name", family), new XAttribute(Fo + "font-family", family), new XAttribute(Fo + "font-size", Pt(r.FontSize)));
                if (bold) tp.Add(new XAttribute(Fo + "font-weight", "bold"));
                if (italic) tp.Add(new XAttribute(Fo + "font-style", "italic"));
                if (under) tp.Add(new XAttribute(Style + "text-underline-style", "solid"), new XAttribute(Style + "text-underline-width", "auto"), new XAttribute(Style + "text-underline-color", "font-color"));
                if (strike) tp.Add(new XAttribute(Style + "text-line-through-style", "solid"));
                if (fg.HasValue && fg.Value != Colors.Black) tp.Add(new XAttribute(Fo + "color", "#" + Fmt.Hex(fg.Value)));
                if (bg.HasValue) tp.Add(new XAttribute(Fo + "background-color", "#" + Fmt.Hex(bg.Value)));
                if (ba == BaselineAlignment.Superscript) tp.Add(new XAttribute(Style + "text-position", "super 58%"));
                else if (ba == BaselineAlignment.Subscript) tp.Add(new XAttribute(Style + "text-position", "sub 58%"));
                _auto.Add(new XElement(Style + "style", new XAttribute(Style + "name", name), new XAttribute(Style + "family", "text"), tp));
                return name;
            }

            XElement Frame(Image img)
            {
                byte[] bytes; string ext;
                if (img.Tag is ImageData d && d.Bytes != null) { bytes = d.Bytes; ext = d.Ext ?? "png"; }
                else if (img.Source is BitmapSource bs) { var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bs)); using var ms = new MemoryStream(); enc.Save(ms); bytes = ms.ToArray(); ext = "png"; }
                else return null;
                var name = "img" + (_images.Count + 1) + "." + (ext == "jpeg" ? "jpg" : ext);
                _images.Add((name, bytes, ext == "jpeg" ? "image/jpeg" : "image/" + ext));
                double w = double.IsNaN(img.Width) ? img.ActualWidth : img.Width, h = double.IsNaN(img.Height) ? img.ActualHeight : img.Height;
                return new XElement(Draw + "frame", new XAttribute(Draw + "name", name), new XAttribute(Text + "anchor-type", "as-char"),
                    new XAttribute(Svg + "width", In(w)), new XAttribute(Svg + "height", In(h)),
                    new XElement(Draw + "image", new XAttribute(XLink + "href", "Pictures/" + name), new XAttribute(XLink + "type", "simple"), new XAttribute(XLink + "show", "embed"), new XAttribute(XLink + "actuate", "onLoad")));
            }

            XElement TableXml(Table t)
            {
                var name = "Table" + (++_tableN);
                double total = t.Columns.Sum(c => c.Width.IsAbsolute ? c.Width.Value : 100);
                _auto.Add(new XElement(Style + "style", new XAttribute(Style + "name", name), new XAttribute(Style + "family", "table"),
                    new XElement(Style + "table-properties", new XAttribute(Style + "width", In(total)), new XAttribute(Table + "align", "left"))));
                var x = new XElement(Table + "table", new XAttribute(Table + "name", name), new XAttribute(Table + "style-name", name));
                for (int i = 0; i < t.Columns.Count; i++)
                {
                    var cs = name + ".C" + i;
                    _auto.Add(new XElement(Style + "style", new XAttribute(Style + "name", cs), new XAttribute(Style + "family", "table-column"),
                        new XElement(Style + "table-column-properties", new XAttribute(Style + "column-width", In(t.Columns[i].Width.IsAbsolute ? t.Columns[i].Width.Value : 100)))));
                    x.Add(new XElement(Table + "table-column", new XAttribute(Table + "style-name", cs)));
                }
                foreach (var row in t.RowGroups.SelectMany(g => g.Rows))
                {
                    var tr = new XElement(Table + (((row.Tag as string) == "Header") ? "table-row" : "table-row"));
                    foreach (var c in row.Cells)
                    {
                        var tc = new XElement(Table + "table-cell", new XAttribute(Office + "value-type", "string"), new XAttribute(Table + "style-name", CellStyle(c)));
                        if (c.ColumnSpan > 1) tc.Add(new XAttribute(Table + "number-columns-spanned", c.ColumnSpan));
                        if (c.RowSpan > 1) tc.Add(new XAttribute(Table + "number-rows-spanned", c.RowSpan));
                        WriteBlocks(c.Blocks, tc);
                        if (!tc.Elements().Any(e => e.Name == Text + "p" || e.Name == Text + "h")) tc.Add(new XElement(Text + "p"));
                        tr.Add(tc);
                        for (int k = 1; k < c.ColumnSpan; k++) tr.Add(new XElement(Table + "covered-table-cell"));
                    }
                    x.Add(tr);
                }
                return x;
            }

            string CellStyle(TableCell c)
            {
                string B(double w) => w <= 0 ? "none" : $"{(w * 72 / 96).ToString("0.##", Inv)}pt solid #{(c.BorderBrush is SolidColorBrush sb ? Fmt.Hex(sb.Color) : "000000")}";
                var bt = c.BorderThickness;
                var bg = c.Background is SolidColorBrush b ? "#" + Fmt.Hex(b.Color) : "transparent";
                string key = $"{B(bt.Left)}|{B(bt.Top)}|{B(bt.Right)}|{B(bt.Bottom)}|{bg}|{c.Padding}";
                if (_cellStyles.TryGetValue(key, out var name)) return name;
                name = "Cell" + (_cellStyles.Count + 1);
                _cellStyles[key] = name;
                _auto.Add(new XElement(Style + "style", new XAttribute(Style + "name", name), new XAttribute(Style + "family", "table-cell"),
                    new XElement(Style + "table-cell-properties", new XAttribute(Fo + "border-left", B(bt.Left)), new XAttribute(Fo + "border-top", B(bt.Top)), new XAttribute(Fo + "border-right", B(bt.Right)),
                        new XAttribute(Fo + "border-bottom", B(bt.Bottom)), new XAttribute(Fo + "background-color", bg), new XAttribute(Fo + "padding", In(Math.Max(c.Padding.Left, 2))))));
                return name;
            }
        }

        public static void Save(string path, FlowDocument doc, PageSettings page, string title, string author) => new Writer().Save(doc, page, title, author, path);

        // =====================================================================================
        //  Reader
        // =====================================================================================
        class StyleInfo
        {
            public string Parent; public bool? Bold, Italic, Under, Strike; public double? SizePt; public string Font; public Color? Color, Back; public BaselineAlignment? Baseline;
            public TextAlignment? Align; public Thickness? Margin; public double? Indent; public bool Break; public int Outline;
        }

        public static LoadedDocument Load(string path)
        {
            using var zip = ZipFile.OpenRead(path);
            XDocument Doc(string n) { var e = zip.GetEntry(n); if (e == null) return null; using var s = e.Open(); return XDocument.Load(s); }
            var content = Doc("content.xml") ?? throw new InvalidDataException("This isn't a valid OpenDocument file.");
            var styles = Doc("styles.xml");
            var meta = Doc("meta.xml");
            var map = new Dictionary<string, StyleInfo>();
            void Read(XElement container)
            {
                if (container == null) return;
                foreach (var st in container.Elements(Style + "style"))
                {
                    var si = new StyleInfo { Parent = (string)st.Attribute(Style + "parent-style-name") };
                    int.TryParse((string)st.Attribute(Style + "default-outline-level"), out si.Outline);
                    var tp = st.Element(Style + "text-properties");
                    if (tp != null)
                    {
                        var fw = (string)tp.Attribute(Fo + "font-weight"); if (fw != null) si.Bold = fw == "bold" || (int.TryParse(fw, out var n) && n >= 600);
                        var fs = (string)tp.Attribute(Fo + "font-style"); if (fs != null) si.Italic = fs == "italic" || fs == "oblique";
                        var us = (string)tp.Attribute(Style + "text-underline-style"); if (us != null) si.Under = us != "none";
                        var ls = (string)tp.Attribute(Style + "text-line-through-style"); if (ls != null) si.Strike = ls != "none";
                        si.SizePt = Length((string)tp.Attribute(Fo + "font-size"), true);
                        si.Font = (string)tp.Attribute(Style + "font-name") ?? ((string)tp.Attribute(Fo + "font-family"))?.Trim('\'');
                        si.Color = Col((string)tp.Attribute(Fo + "color")); si.Back = Col((string)tp.Attribute(Fo + "background-color"));
                        var pos = (string)tp.Attribute(Style + "text-position");
                        if (pos != null) si.Baseline = pos.StartsWith("super") ? BaselineAlignment.Superscript : pos.StartsWith("sub") ? BaselineAlignment.Subscript : (BaselineAlignment?)null;
                    }
                    var pp = st.Element(Style + "paragraph-properties");
                    if (pp != null)
                    {
                        var al = (string)pp.Attribute(Fo + "text-align");
                        if (al != null) si.Align = al switch { "center" => TextAlignment.Center, "end" or "right" => TextAlignment.Right, "justify" => TextAlignment.Justify, _ => TextAlignment.Left };
                        double? L(string n) => Length((string)pp.Attribute(Fo + n), false);
                        if (pp.Attribute(Fo + "margin-left") != null || pp.Attribute(Fo + "margin-bottom") != null || pp.Attribute(Fo + "margin-top") != null)
                            si.Margin = new Thickness(L("margin-left") ?? 0, L("margin-top") ?? 0, L("margin-right") ?? 0, L("margin-bottom") ?? 0);
                        si.Indent = L("text-indent");
                        si.Break = (string)pp.Attribute(Fo + "break-before") == "page";
                        if (si.Back == null) si.Back = Col((string)pp.Attribute(Fo + "background-color"));
                    }
                    map[(string)st.Attribute(Style + "name") ?? ""] = si;
                }
            }
            Read(styles?.Root?.Element(Office + "styles"));
            Read(styles?.Root?.Element(Office + "automatic-styles"));
            Read(content.Root.Element(Office + "automatic-styles"));

            StyleInfo Resolve(string name)
            {
                var r = new StyleInfo();
                var chain = new List<StyleInfo>();
                for (int guard = 0; name != null && guard < 10 && map.TryGetValue(name, out var s); guard++) { chain.Add(s); name = s.Parent; }
                chain.Reverse();
                foreach (var s in chain)
                {
                    r.Bold = s.Bold ?? r.Bold; r.Italic = s.Italic ?? r.Italic; r.Under = s.Under ?? r.Under; r.Strike = s.Strike ?? r.Strike; r.SizePt = s.SizePt ?? r.SizePt; r.Font = s.Font ?? r.Font;
                    r.Color = s.Color ?? r.Color; r.Back = s.Back ?? r.Back; r.Baseline = s.Baseline ?? r.Baseline; r.Align = s.Align ?? r.Align; r.Margin = s.Margin ?? r.Margin; r.Indent = s.Indent ?? r.Indent;
                    r.Break |= s.Break; if (s.Outline > 0) r.Outline = s.Outline;
                }
                return r;
            }

            void ApplyText(TextElement e, StyleInfo s)
            {
                if (s.Bold.HasValue) e.FontWeight = s.Bold.Value ? FontWeights.Bold : FontWeights.Normal;
                if (s.Italic.HasValue) e.FontStyle = s.Italic.Value ? FontStyles.Italic : FontStyles.Normal;
                if (s.SizePt.HasValue) e.FontSize = s.SizePt.Value * Fmt.PxPerPt;
                if (!string.IsNullOrEmpty(s.Font)) e.FontFamily = new FontFamily(s.Font);
                if (s.Color.HasValue) e.Foreground = new SolidColorBrush(s.Color.Value);
                if (s.Back.HasValue && !(e is Paragraph)) e.Background = new SolidColorBrush(s.Back.Value);
                if (e is Inline inl)
                {
                    var deco = new TextDecorationCollection();
                    if (s.Under == true) deco.Add(TextDecorations.Underline);
                    if (s.Strike == true) deco.Add(TextDecorations.Strikethrough);
                    if (deco.Count > 0) inl.TextDecorations = deco;
                    if (s.Baseline.HasValue) inl.BaselineAlignment = s.Baseline.Value;
                }
            }

            var fd = new FlowDocument();
            var page = Settings.Current.Page.Clone();
            var pl = styles?.Root?.Descendants(Style + "page-layout-properties").FirstOrDefault();
            if (pl != null)
            {
                double? In2(string n) { var v = Length((string)pl.Attribute(Fo + n), false); return v.HasValue ? v.Value / 96 : (double?)null; }
                var w = In2("page-width"); var h = In2("page-height");
                if (w.HasValue && h.HasValue)
                {
                    page.Landscape = w > h;
                    page.Paper = PageSettings.ClosestPaper(w.Value, h.Value);
                }
                page.MarginLeft = In2("margin-left") ?? page.MarginLeft; page.MarginRight = In2("margin-right") ?? page.MarginRight;
                page.MarginTop = In2("margin-top") ?? page.MarginTop; page.MarginBottom = In2("margin-bottom") ?? page.MarginBottom;
            }
            // header/footer text (tab-separated left / centre / right)
            string Slot(XElement p, int i)
            {
                if (p == null) return "";
                var parts = new List<StringBuilder> { new StringBuilder() };
                foreach (var n in p.DescendantNodes())
                {
                    if (n is XText t && !(t.Parent.Name == Text + "page-number" || t.Parent.Name == Text + "page-count" || t.Parent.Name == Text + "date")) parts[^1].Append(t.Value);
                    else if (n is XElement e)
                    {
                        if (e.Name == Text + "tab") parts.Add(new StringBuilder());
                        else if (e.Name == Text + "page-number") parts[^1].Append("{PAGE}");
                        else if (e.Name == Text + "page-count") parts[^1].Append("{NUMPAGES}");
                        else if (e.Name == Text + "date") parts[^1].Append("{DATE}");
                        else if (e.Name == Text + "title") parts[^1].Append("{TITLE}");
                        else if (e.Name == Text + "initial-creator" || e.Name == Text + "author-name") parts[^1].Append("{AUTHOR}");
                    }
                }
                if (parts.Count == 1) return i == 1 ? parts[0].ToString() : "";
                return i < parts.Count ? parts[i].ToString() : "";
            }
            var mp = styles?.Root?.Descendants(Style + "master-page").FirstOrDefault();
            var hp = mp?.Element(Style + "header")?.Element(Text + "p"); var fp = mp?.Element(Style + "footer")?.Element(Text + "p");
            if (hp != null) { page.HeaderLeft = Slot(hp, 0); page.HeaderText = Slot(hp, 1); page.HeaderRight = Slot(hp, 2); }
            if (fp != null) { page.FooterLeft = Slot(fp, 0); page.FooterCenter = Slot(fp, 1); page.FooterRight = Slot(fp, 2); }

            var notes = new Section { Tag = SectionKind.Notes };
            int fnN = 0, enN = 0;

            void Inlines(XElement x, InlineCollection into)
            {
                foreach (var n in x.Nodes())
                {
                    if (n is XText t) { var s = System.Text.RegularExpressions.Regex.Replace(t.Value, @"[\r\n\t]+", ""); if (s.Length > 0) into.Add(new Run(s)); continue; }
                    if (!(n is XElement e)) continue;
                    if (e.Name == Text + "span")
                    {
                        var span = new Span();
                        ApplyText(span, Resolve((string)e.Attribute(Text + "style-name")));
                        Inlines(e, span.Inlines);
                        into.Add(span);
                    }
                    else if (e.Name == Text + "s") into.Add(new Run(new string(' ', Math.Max(1, (int?)e.Attribute(Text + "c") ?? 1))));
                    else if (e.Name == Text + "tab") into.Add(new Run("\t"));
                    else if (e.Name == Text + "line-break") into.Add(new LineBreak());
                    else if (e.Name == Text + "a")
                    {
                        var href = (string)e.Attribute(XLink + "href");
                        Uri uri = null;
                        try { uri = href.StartsWith("#") ? new Uri(href, UriKind.Relative) : new Uri(href, UriKind.Absolute); } catch { }
                        var h = new Hyperlink { NavigateUri = uri };
                        Inlines(e, h.Inlines);
                        into.Add(h);
                    }
                    else if (e.Name == Text + "note")
                    {
                        bool en = (string)e.Attribute(Text + "note-class") == "endnote";
                        int num = en ? ++enN : ++fnN;
                        into.Add(new Span(new Run(en ? DecoratedPaginator.FormatNumber(num, new PageSettings { NumFormat = "i" }) : num.ToString()) { BaselineAlignment = BaselineAlignment.Superscript }) { Tag = new NoteRef { Endnote = en, Number = num } });
                        var bp = new Paragraph { Tag = new NoteBody { Endnote = en, Number = num }, FontSize = 10 * Fmt.PxPerPt, Margin = new Thickness(0, 0, 0, 3) };
                        bp.Inlines.Add(new Span(new Run(num.ToString()) { BaselineAlignment = BaselineAlignment.Superscript }) { Tag = "NoteNum" });
                        bp.Inlines.Add(new Run(" " + string.Join(" ", e.Element(Text + "note-body")?.Elements().Select(p => p.Value) ?? new string[0])));
                        notes.Blocks.Add(bp);
                    }
                    else if (e.Name == Office + "annotation")
                    {
                        var ci = new CommentInfo { Author = e.Element(Dc + "creator")?.Value ?? "", Text = string.Join("\n", e.Elements(Text + "p").Select(p => p.Value)) };
                        if (DateTime.TryParse(e.Element(Dc + "date")?.Value, out var dt)) ci.Date = dt;
                        into.Add(new Span(new Run("​")) { Tag = ci });
                    }
                    else if (e.Name == Draw + "frame")
                    {
                        var img = e.Element(Draw + "image");
                        var href = (string)img?.Attribute(XLink + "href");
                        var entry = href == null ? null : zip.GetEntry(href.TrimStart('.', '/'));
                        if (entry == null) continue;
                        byte[] bytes; using (var s = entry.Open()) using (var ms = new MemoryStream()) { s.CopyTo(ms); bytes = ms.ToArray(); }
                        try
                        {
                            var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = new MemoryStream(bytes); bi.EndInit(); bi.Freeze();
                            double w = Length((string)e.Attribute(Svg + "width"), false) ?? bi.Width, h = Length((string)e.Attribute(Svg + "height"), false) ?? bi.Height;
                            var ext = Path.GetExtension(href).TrimStart('.').ToLowerInvariant(); if (ext == "jpg") ext = "jpeg";
                            into.Add(new InlineUIContainer(new Image { Source = bi, Width = w, Height = h, Stretch = Stretch.Fill, Tag = new ImageData { Bytes = bytes, Ext = ext } }));
                        }
                        catch { }
                    }
                    else if (e.Name == Text + "bookmark" || e.Name == Text + "bookmark-start")
                        into.Add(new Span(new Run("")) { Tag = new BookmarkInfo { Name = (string)e.Attribute(Text + "name") } });
                    else if (e.Name == Text + "page-number" || e.Name == Text + "date" || e.Name == Text + "time" || e.Name == Text + "title" || e.Name == Text + "page-count")
                        into.Add(new Span(new Run(e.Value)) { Tag = new FieldInfo { Code = e.Name.LocalName.ToUpperInvariant().Replace("-", "") } });
                    else Inlines(e, into);
                }
            }

            Paragraph Para(XElement x)
            {
                var p = new Paragraph();
                var s = Resolve((string)x.Attribute(Text + "style-name"));
                int outline = x.Name == Text + "h" ? ((int?)x.Attribute(Text + "outline-level") ?? (s.Outline > 0 ? s.Outline : 1)) : 0;
                if (outline > 0) StyleDef.Get("Heading" + Math.Min(3, outline)).Apply(p, false);
                else StyleDef.Get("Normal").Apply(p, false);
                if (s.Align.HasValue) p.TextAlignment = s.Align.Value;
                if (s.Margin.HasValue) p.Margin = s.Margin.Value;
                if (s.Indent.HasValue) p.TextIndent = s.Indent.Value;
                if (s.Break) p.BreakPageBefore = true;
                if (s.Back.HasValue) p.Background = new SolidColorBrush(s.Back.Value);
                var copy = new StyleInfo { Bold = s.Bold, Italic = s.Italic, SizePt = s.SizePt, Font = s.Font, Color = s.Color };
                ApplyText(p, copy);
                Inlines(x, p.Inlines);
                return p;
            }

            void Blocks(XElement container, BlockCollection into)
            {
                foreach (var e in container.Elements())
                {
                    if (e.Name == Text + "p" || e.Name == Text + "h") into.Add(Para(e));
                    else if (e.Name == Text + "list")
                    {
                        var list = new List { MarkerStyle = TextMarkerStyle.Disc };
                        foreach (var li in e.Elements(Text + "list-item"))
                        {
                            var item = new ListItem();
                            Blocks(li, item.Blocks);
                            if (item.Blocks.Count == 0) item.Blocks.Add(new Paragraph());
                            foreach (var p in item.Blocks.OfType<Paragraph>()) p.Margin = new Thickness(0);
                            list.ListItems.Add(item);
                        }
                        into.Add(list);
                    }
                    else if (e.Name == Table + "table")
                    {
                        var t = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 10) };
                        var colStyles = content.Root.Element(Office + "automatic-styles")?.Elements(Style + "style").ToDictionary(s => (string)s.Attribute(Style + "name") ?? "", s => s) ?? new Dictionary<string, XElement>();
                        foreach (var col in e.Descendants(Table + "table-column"))
                        {
                            int rep = (int?)col.Attribute(Table + "number-columns-repeated") ?? 1;
                            double? w = null;
                            if (colStyles.TryGetValue((string)col.Attribute(Table + "style-name") ?? "", out var cs)) w = Length((string)cs.Element(Style + "table-column-properties")?.Attribute(Style + "column-width"), false);
                            for (int i = 0; i < Math.Min(rep, 64); i++) t.Columns.Add(new TableColumn { Width = w.HasValue ? new GridLength(w.Value) : new GridLength(1, GridUnitType.Star) });
                        }
                        var g = new TableRowGroup();
                        foreach (var row in e.Descendants(Table + "table-row"))
                        {
                            var tr = new TableRow();
                            foreach (var c in row.Elements(Table + "table-cell"))
                            {
                                var cell = new TableCell { BorderBrush = Brushes.Black, BorderThickness = new Thickness(0.5), Padding = new Thickness(5, 2, 5, 2) };
                                if (colStyles.TryGetValue((string)c.Attribute(Table + "style-name") ?? "", out var cst) && cst.Element(Style + "table-cell-properties") is XElement cp)
                                {
                                    double W(string side) { var v = (string)cp.Attribute(Fo + "border-" + side) ?? (string)cp.Attribute(Fo + "border"); return v == null || v == "none" ? 0 : (Length(v.Split(' ')[0], false) ?? 0.75); }
                                    cell.BorderThickness = new Thickness(W("left"), W("top"), W("right"), W("bottom"));
                                    var bg = Col((string)cp.Attribute(Fo + "background-color")); if (bg.HasValue) cell.Background = new SolidColorBrush(bg.Value);
                                }
                                cell.ColumnSpan = Math.Max(1, (int?)c.Attribute(Table + "number-columns-spanned") ?? 1);
                                cell.RowSpan = Math.Max(1, (int?)c.Attribute(Table + "number-rows-spanned") ?? 1);
                                Blocks(c, cell.Blocks);
                                if (cell.Blocks.Count == 0) cell.Blocks.Add(new Paragraph());
                                foreach (var p in cell.Blocks.OfType<Paragraph>()) p.Margin = new Thickness(0);
                                tr.Cells.Add(cell);
                            }
                            g.Rows.Add(tr);
                        }
                        t.RowGroups.Add(g);
                        if (t.Columns.Count == 0) for (int i = 0; i < g.Rows.Select(r => r.Cells.Sum(c => c.ColumnSpan)).DefaultIfEmpty(1).Max(); i++) t.Columns.Add(new TableColumn());
                        into.Add(t);
                    }
                    else if (e.Name == Text + "section" || e.Name == Text + "table-of-content" || e.Name == Text + "index-body") Blocks(e, into);
                }
            }

            var body = content.Root.Element(Office + "body")?.Element(Office + "text");
            if (body != null) Blocks(body, fd.Blocks);
            if (fd.Blocks.Count == 0) fd.Blocks.Add(new Paragraph());
            if (notes.Blocks.Count > 0)
            {
                var sep = new Paragraph { Tag = "NoteSeparator", Margin = new Thickness(0, 24, 0, 6), BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 0, 0, 0.75), FontSize = 4 };
                sep.Inlines.Add(new Run(" "));
                notes.Blocks.InsertBefore(notes.Blocks.FirstBlock, sep);
                fd.Blocks.Add(notes);
            }
            var m = meta?.Root?.Element(Office + "meta");
            return new LoadedDocument { Document = fd, Page = page, Title = m?.Element(Dc + "title")?.Value, Author = m?.Element(Meta + "initial-creator")?.Value ?? m?.Element(Dc + "creator")?.Value };
        }

        static Color? Col(string v)
        {
            if (string.IsNullOrEmpty(v) || v == "transparent") return null;
            try { return (Color)ColorConverter.ConvertFromString(v); } catch { return null; }
        }

        /// <summary>Converts an ODF length to device pixels, or to points when asPoints is set.</summary>
        static double? Length(string v, bool asPoints)
        {
            if (string.IsNullOrWhiteSpace(v)) return null;
            v = v.Trim();
            var m = System.Text.RegularExpressions.Regex.Match(v, @"^(-?[\d.]+)\s*(in|cm|mm|pt|pc|px|%)?$");
            if (!m.Success || !double.TryParse(m.Groups[1].Value, NumberStyles.Float, Inv, out var n)) return null;
            double px = m.Groups[2].Value switch { "in" => n * 96, "cm" => n * 96 / 2.54, "mm" => n * 96 / 25.4, "pt" => n * 96 / 72, "pc" => n * 16, "%" => n, _ => n };
            return asPoints ? px * 72 / 96 : px;
        }
    }
}
