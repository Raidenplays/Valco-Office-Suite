using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace ValcoWord
{
    /// <summary>Original bytes of an embedded picture, so saving doesn't re-encode JPEGs.</summary>
    public class ImageData
    {
        public byte[] Bytes;
        public string Ext = "png";
        public System.Windows.Media.Imaging.BitmapSource Original;
        public double OriginalWidth, OriginalHeight;
    }

    public class LoadedDocument
    {
        public FlowDocument Document;
        public PageSettings Page;
        public string Title, Author;
        public DocExtras Extras = new DocExtras();
    }

    static class Ns
    {
        public static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        public static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        public static readonly XNamespace WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        public static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
        public static readonly XNamespace PIC = "http://schemas.openxmlformats.org/drawingml/2006/picture";
        public static readonly XNamespace V = "urn:schemas-microsoft-com:vml";
        public static readonly XNamespace O = "urn:schemas-microsoft-com:office:office";
        public const string RelComments = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments";
        public const string RelFootnotes = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/footnotes";
        public const string RelEndnotes = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/endnotes";
        public const string RelCustomXml = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml";
        public const string CtComments = "application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml";
        public const string CtFootnotes = "application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml";
        public const string CtEndnotes = "application/vnd.openxmlformats-officedocument.wordprocessingml.endnotes+xml";
        public const string CtTemplate = "application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml";

        public const string RelOfficeDoc = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
        public const string RelStyles = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles";
        public const string RelNumbering = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering";
        public const string RelSettings = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings";
        public const string RelImage = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";
        public const string RelHyperlink = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";
        public const string RelTheme = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme";
        public const string RelHeader = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/header";
        public const string RelFooter = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer";
        public const string RelOfficeDocStrict = "http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument";

        public const string CtDocument = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
        public const string CtStyles = "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml";
        public const string CtNumbering = "application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml";
        public const string CtSettings = "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml";
        public const string CtHeader = "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml";
        public const string CtFooter = "application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml";
    }

    // =====================================================================================
    //  Writer
    // =====================================================================================
    public class DocxWriter
    {
        static readonly XNamespace W = Ns.W, R = Ns.R, WP = Ns.WP, A = Ns.A, PIC = Ns.PIC, V = Ns.V, O = Ns.O;

        PackagePart _doc;
        PageSettings _page;
        DocExtras _ex;
        int _imgNo, _drawNo, _bmId, _revId = 1;
        bool _inDel;
        bool _template;
        readonly List<Dictionary<int, TextMarkerStyle>> _nums = new List<Dictionary<int, TextMarkerStyle>>();
        readonly List<int> _numStarts = new List<int>();
        readonly List<CommentInfo> _comments = new List<CommentInfo>();
        readonly Dictionary<CommentInfo, int> _commentIds = new Dictionary<CommentInfo, int>();
        Section _notes;

        public static void Save(FlowDocument doc, PageSettings page, string title, string author, Stream stream, DocExtras extras = null, bool template = false)
        {
            new DocxWriter { _template = template }.Write(doc, page, title, author, stream, extras ?? new DocExtras());
        }

        public static void Save(FlowDocument doc, PageSettings page, string title, string author, string path, DocExtras extras = null)
        {
            var tmp = path + ".vwtmp";
            bool template = path.EndsWith(".dotx", StringComparison.OrdinalIgnoreCase);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.ReadWrite))
                Save(doc, page, title, author, fs, extras, template);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        static string Fill(string hex) => (hex ?? "").TrimStart('#');

        void Write(FlowDocument doc, PageSettings page, string title, string author, Stream stream, DocExtras ex)
        {
            _page = page;
            _ex = ex;
            using var pkg = Package.Open(stream, FileMode.Create, FileAccess.ReadWrite);
            var docUri = new Uri("/word/document.xml", UriKind.Relative);
            _doc = pkg.CreatePart(docUri, _template ? Ns.CtTemplate : Ns.CtDocument, CompressionOption.Normal);
            pkg.CreateRelationship(docUri, TargetMode.Internal, Ns.RelOfficeDoc, "rId1");

            var body = new XElement(W + "body");
            WriteBlocks(doc.Blocks, body, -1, 0);
            if (!body.Elements().Any()) body.Add(new XElement(W + "p"));

            var sect = new XElement(W + "sectPr");
            // header (with watermark) / footer
            if (page.HasHeader || !string.IsNullOrWhiteSpace(page.Watermark))
            {
                var hp = pkg.CreatePart(new Uri("/word/header1.xml", UriKind.Relative), Ns.CtHeader, CompressionOption.Normal);
                var id = _doc.CreateRelationship(new Uri("header1.xml", UriKind.Relative), TargetMode.Internal, Ns.RelHeader).Id;
                var para = SlotParagraph(page.HeaderLeft, page.HeaderText, page.HeaderRight, "Header");
                if (!string.IsNullOrWhiteSpace(page.Watermark)) para.Add(WatermarkRun(page.Watermark, page.WatermarkColor));
                Xml(hp, new XElement(W + "hdr", NsAttrs(), para));
                sect.Add(new XElement(W + "headerReference", new XAttribute(W + "type", "default"), new XAttribute(R + "id", id)));
            }
            if (page.HasFooter)
            {
                var fp = pkg.CreatePart(new Uri("/word/footer1.xml", UriKind.Relative), Ns.CtFooter, CompressionOption.Normal);
                var id = _doc.CreateRelationship(new Uri("footer1.xml", UriKind.Relative), TargetMode.Internal, Ns.RelFooter).Id;
                Xml(fp, new XElement(W + "ftr", NsAttrs(), SlotParagraph(page.FooterLeft, page.FooterCenter, page.FooterRight, "Footer")));
                sect.Add(new XElement(W + "footerReference", new XAttribute(W + "type", "default"), new XAttribute(R + "id", id)));
            }
            var pgSz = new XElement(W + "pgSz", new XAttribute(W + "w", Tw(page.WidthIn * 96)), new XAttribute(W + "h", Tw(page.HeightIn * 96)));
            if (page.Landscape) pgSz.Add(new XAttribute(W + "orient", "landscape"));
            sect.Add(pgSz,
                new XElement(W + "pgMar",
                    new XAttribute(W + "top", Tw(page.MarginTop * 96)), new XAttribute(W + "right", Tw(page.MarginRight * 96)),
                    new XAttribute(W + "bottom", Tw(page.MarginBottom * 96)), new XAttribute(W + "left", Tw(page.MarginLeft * 96)),
                    new XAttribute(W + "header", 720), new XAttribute(W + "footer", 720), new XAttribute(W + "gutter", 0)));
            if (page.PageBorder)
            {
                XElement B(string side) => new XElement(W + side, new XAttribute(W + "val", "single"), new XAttribute(W + "sz", Math.Max(2, (int)Math.Round(page.PageBorderWidth * 6))),
                    new XAttribute(W + "space", 24), new XAttribute(W + "color", Fill(page.PageBorderColor)));
                sect.Add(new XElement(W + "pgBorders", new XAttribute(W + "offsetFrom", "page"), B("top"), B("left"), B("bottom"), B("right")));
            }
            if (page.LineNumbers) sect.Add(new XElement(W + "lnNumType", new XAttribute(W + "countBy", 1), new XAttribute(W + "restart", "continuous")));
            if (page.NumFormat != "1" || page.NumStart != 1)
                sect.Add(new XElement(W + "pgNumType", new XAttribute(W + "fmt", page.NumFormat switch { "a" => "lowerLetter", "A" => "upperLetter", "i" => "lowerRoman", "I" => "upperRoman", "-1-" => "numberInDash", _ => "decimal" }), new XAttribute(W + "start", page.NumStart)));
            sect.Add(new XElement(W + "cols", new XAttribute(W + "num", Math.Max(1, page.Columns)), new XAttribute(W + "space", Tw(page.ColumnSpacingIn * 96))));
            body.Add(sect);

            var docEl = new XElement(W + "document", NsAttrs());
            if (!string.IsNullOrEmpty(page.PageColor)) docEl.Add(new XElement(W + "background", new XAttribute(W + "color", Fill(page.PageColor))));
            docEl.Add(body);
            Xml(_doc, docEl);

            var sp = pkg.CreatePart(new Uri("/word/styles.xml", UriKind.Relative), Ns.CtStyles, CompressionOption.Normal);
            _doc.CreateRelationship(new Uri("styles.xml", UriKind.Relative), TargetMode.Internal, Ns.RelStyles);
            Xml(sp, BuildStyles());

            if (_nums.Count > 0)
            {
                var np = pkg.CreatePart(new Uri("/word/numbering.xml", UriKind.Relative), Ns.CtNumbering, CompressionOption.Normal);
                _doc.CreateRelationship(new Uri("numbering.xml", UriKind.Relative), TargetMode.Internal, Ns.RelNumbering);
                Xml(np, BuildNumbering());
            }

            WriteNotes(pkg);
            WriteComments(pkg);
            WriteCustomXml(pkg);

            var settings = new XElement(W + "settings", new XAttribute(XNamespace.Xmlns + "w", W));
            if (_ex.Protected || _ex.MarkedFinal)
                settings.Add(new XElement(W + "documentProtection", new XAttribute(W + "edit", _ex.Protected && _ex.Custom.TryGetValue("protectMode", out var pm) ? (pm == "comments" ? "comments" : pm == "tracked" ? "trackedChanges" : "readOnly") : "readOnly"), new XAttribute(W + "enforcement", _ex.Protected ? 1 : 0)));
            if (!string.IsNullOrEmpty(page.PageColor)) settings.Add(new XElement(W + "displayBackgroundShape"));
            if (_ex.TrackChanges) settings.Add(new XElement(W + "trackRevisions"));
            settings.Add(new XElement(W + "defaultTabStop", new XAttribute(W + "val", 720)));
            if (page.Hyphenation) settings.Add(new XElement(W + "autoHyphenation"));
            settings.Add(new XElement(W + "compat", new XElement(W + "compatSetting",
                    new XAttribute(W + "name", "compatibilityMode"), new XAttribute(W + "uri", "http://schemas.microsoft.com/office/word"), new XAttribute(W + "val", "15"))));
            var setp = pkg.CreatePart(new Uri("/word/settings.xml", UriKind.Relative), Ns.CtSettings, CompressionOption.Normal);
            _doc.CreateRelationship(new Uri("settings.xml", UriKind.Relative), TargetMode.Internal, Ns.RelSettings);
            Xml(setp, settings);

            var props = pkg.PackageProperties;
            props.Title = title ?? "";
            props.Creator = author ?? "";
            props.LastModifiedBy = author ?? "";
            props.Modified = DateTime.UtcNow;
            if (props.Created == null) props.Created = DateTime.UtcNow;
            if (_ex.MarkedFinal) props.ContentStatus = "Final";
        }

        /// <summary>A header/footer paragraph with left, centre and right parts on tab stops.</summary>
        XElement SlotParagraph(string left, string center, string right, string style)
        {
            int body = Tw(_page.BodyWidthPx);
            var p = new XElement(W + "p", new XElement(W + "pPr", new XElement(W + "pStyle", new XAttribute(W + "val", style)),
                new XElement(W + "tabs", new XElement(W + "tab", new XAttribute(W + "val", "center"), new XAttribute(W + "pos", body / 2)),
                                         new XElement(W + "tab", new XAttribute(W + "val", "right"), new XAttribute(W + "pos", body)))));
            void Text(string s)
            {
                if (string.IsNullOrEmpty(s)) return;
                var parts = System.Text.RegularExpressions.Regex.Split(s, @"(\{PAGE\}|\{NUMPAGES\}|\{DATE\}|\{TITLE\}|\{AUTHOR\})");
                foreach (var part in parts)
                {
                    if (part.Length == 0) continue;
                    if (part.StartsWith("{") && part.EndsWith("}"))
                    {
                        var code = part.Trim('{', '}');
                        p.Add(new XElement(W + "fldSimple", new XAttribute(W + "instr", " " + code + " "), new XElement(W + "r", new XElement(W + "t", code == "PAGE" ? "1" : code))));
                    }
                    else p.Add(new XElement(W + "r", new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), part)));
                }
            }
            Text(left);
            if (!string.IsNullOrEmpty(center) || !string.IsNullOrEmpty(right)) p.Add(new XElement(W + "r", new XElement(W + "tab")));
            Text(center);
            if (!string.IsNullOrEmpty(right)) p.Add(new XElement(W + "r", new XElement(W + "tab")));
            Text(right);
            return p;
        }

        /// <summary>Word's own watermark shape: rotated WordArt text in the header.</summary>
        XElement WatermarkRun(string text, string color)
        {
            double w = _page.BodyWidthPx * 0.75 * 0.85, h = w / Math.Max(4, text.Length) * 1.6;
            return new XElement(W + "r", new XElement(W + "pict",
                new XElement(V + "shapetype", new XAttribute("id", "_x0000_t136"), new XAttribute("coordsize", "21600,21600"), new XAttribute(O + "spt", "136"), new XAttribute("adj", "10800"), new XAttribute("path", "m@7,l@8,m@5,21600l@6,21600e"),
                    new XElement(V + "path", new XAttribute("textpathok", "t"), new XAttribute(O + "connecttype", "custom")),
                    new XElement(V + "textpath", new XAttribute("on", "t"), new XAttribute("fitshape", "t"))),
                new XElement(V + "shape", new XAttribute("id", "PowerPlusWaterMarkObject"), new XAttribute(O + "spid", "_x0000_s2049"), new XAttribute("type", "#_x0000_t136"),
                    new XAttribute("style", $"position:absolute;margin-left:0;margin-top:0;width:{w:0}pt;height:{h:0}pt;rotation:315;z-index:-251657216;mso-position-horizontal:center;mso-position-horizontal-relative:margin;mso-position-vertical:center;mso-position-vertical-relative:margin"),
                    new XAttribute(O + "allowincell", "f"), new XAttribute("fillcolor", "#" + Fill(color)), new XAttribute("stroked", "f"),
                    new XElement(V + "fill", new XAttribute("opacity", ".5")),
                    new XElement(V + "textpath", new XAttribute("style", "font-family:\"Calibri\";font-size:1pt"), new XAttribute("string", text)))));
        }

        void WriteNotes(Package pkg)
        {
            if (_notes == null) return;
            foreach (var endnote in new[] { false, true })
            {
                var paras = _notes.Blocks.OfType<Paragraph>().Where(p => p.Tag is NoteBody nb && nb.Endnote == endnote).ToList();
                if (paras.Count == 0) continue;
                string kind = endnote ? "endnote" : "footnote";
                var root = new XElement(W + (kind + "s"), NsAttrs(),
                    new XElement(W + kind, new XAttribute(W + "type", "separator"), new XAttribute(W + "id", -1),
                        new XElement(W + "p", new XElement(W + "pPr", new XElement(W + "spacing", new XAttribute(W + "after", 0), new XAttribute(W + "line", 240), new XAttribute(W + "lineRule", "auto"))),
                            new XElement(W + "r", new XElement(W + "separator")))),
                    new XElement(W + kind, new XAttribute(W + "type", "continuationSeparator"), new XAttribute(W + "id", 0),
                        new XElement(W + "p", new XElement(W + "pPr", new XElement(W + "spacing", new XAttribute(W + "after", 0), new XAttribute(W + "line", 240), new XAttribute(W + "lineRule", "auto"))),
                            new XElement(W + "r", new XElement(W + "continuationSeparator")))));
                foreach (var p in paras)
                {
                    var nb = (NoteBody)p.Tag;
                    var x = new XElement(W + "p", new XElement(W + "pPr", new XElement(W + "pStyle", new XAttribute(W + "val", endnote ? "EndnoteText" : "FootnoteText"))),
                        new XElement(W + "r", new XElement(W + "rPr", new XElement(W + "vertAlign", new XAttribute(W + "val", "superscript"))), new XElement(W + (kind + "Ref"))));
                    var rest = p.Inlines.Where(i => !(i is Span s && (s.Tag as string) == "NoteNum")).ToList();
                    var tmp = new XElement("tmp");
                    foreach (var i in rest)
                    {
                        if (i is Run r) WriteRun(r, tmp);
                        else { var holder = new Span(); /* write through a temp collection */ WriteInlineSingle(i, tmp); }
                    }
                    foreach (var e in tmp.Elements()) x.Add(e);
                    root.Add(new XElement(W + kind, new XAttribute(W + "id", nb.Number), x));
                }
                var part = pkg.CreatePart(new Uri($"/word/{kind}s.xml", UriKind.Relative), endnote ? Ns.CtEndnotes : Ns.CtFootnotes, CompressionOption.Normal);
                _doc.CreateRelationship(new Uri($"{kind}s.xml", UriKind.Relative), TargetMode.Internal, endnote ? Ns.RelEndnotes : Ns.RelFootnotes);
                Xml(part, root);
            }
        }

        void WriteComments(Package pkg)
        {
            if (_comments.Count == 0) return;
            var root = new XElement(W + "comments", NsAttrs());
            foreach (var c in _comments)
            {
                var cx = new XElement(W + "comment", new XAttribute(W + "id", _commentIds[c]), new XAttribute(W + "author", c.Author ?? ""),
                    new XAttribute(W + "date", c.Date.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")), new XAttribute(W + "initials", c.Initials ?? ""));
                var lines = (c.Text ?? "").Replace("\r\n", "\n").Split('\n');
                bool first = true;
                foreach (var l in lines)
                {
                    var p = new XElement(W + "p", new XElement(W + "pPr", new XElement(W + "pStyle", new XAttribute(W + "val", "CommentText"))));
                    if (first) p.Add(new XElement(W + "r", new XElement(W + "annotationRef")));
                    p.Add(new XElement(W + "r", new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), l)));
                    cx.Add(p);
                    first = false;
                }
                root.Add(cx);
            }
            var part = pkg.CreatePart(new Uri("/word/comments.xml", UriKind.Relative), Ns.CtComments, CompressionOption.Normal);
            _doc.CreateRelationship(new Uri("comments.xml", UriKind.Relative), TargetMode.Internal, Ns.RelComments);
            Xml(part, root);
        }

        void WriteCustomXml(Package pkg)
        {
            XNamespace B = "http://schemas.openxmlformats.org/officeDocument/2006/bibliography";
            int n = 1;
            if (_ex.Sources.Count > 0)
            {
                var root = new XElement(B + "Sources", new XAttribute(XNamespace.Xmlns + "b", B), new XAttribute("xmlns", B),
                    new XAttribute("SelectedStyle", "\\" + _ex.BibStyle + ".XSL"), new XAttribute("StyleName", _ex.BibStyle));
                foreach (var s in _ex.Sources)
                {
                    var names = new XElement(B + "NameList");
                    foreach (var a in (s.Author ?? "").Split(';').Select(x => x.Trim()).Where(x => x.Length > 0))
                    {
                        var parts = a.Split(',');
                        names.Add(new XElement(B + "Person", new XElement(B + "Last", parts[0].Trim()), parts.Length > 1 ? new XElement(B + "First", parts[1].Trim()) : null));
                    }
                    root.Add(new XElement(B + "Source", new XElement(B + "Tag", s.Tag), new XElement(B + "SourceType", s.Type),
                        new XElement(B + "Guid", "{" + Guid.NewGuid().ToString().ToUpper() + "}"),
                        new XElement(B + "Author", new XElement(B + "Author", names)),
                        new XElement(B + "Title", s.Title), new XElement(B + "Year", s.Year), new XElement(B + "Publisher", s.Publisher),
                        new XElement(B + "City", s.City), new XElement(B + "URL", s.Url), new XElement(B + "JournalName", s.Journal), new XElement(B + "Pages", s.Pages)));
                }
                AddCustom(pkg, n++, root);
            }
            XNamespace VW = "urn:valco:word";
            var vx = new XElement(VW + "valco", new XAttribute("xmlns", VW),
                new XElement(VW + "theme", _ex.Theme), new XElement(VW + "styleSet", _ex.StyleSet), new XElement(VW + "final", _ex.MarkedFinal),
                new XElement(VW + "mergeSource", _ex.MergeSource ?? ""), new XElement(VW + "mergeType", _ex.MergeType ?? ""),
                new XElement(VW + "mergeFields", string.Join("\u001f", _ex.MergeFields)),
                new XElement(VW + "mergeRecords", _ex.MergeRecords.Select(r => new XElement(VW + "r", r.Select(kv => new XElement(VW + "f", new XAttribute("n", kv.Key), kv.Value))))),
                new XElement(VW + "mergeExcluded", string.Join(",", _ex.MergeExcluded)), new XElement(VW + "mergeIndex", _ex.MergeIndex), new XElement(VW + "mergePreview", _ex.MergePreview),
                new XElement(VW + "track", _ex.TrackChanges), new XElement(VW + "protected", _ex.Protected), new XElement(VW + "protectHash", _ex.ProtectHash ?? ""), new XElement(VW + "bibStyle", _ex.BibStyle ?? "APA"),
                new XElement(VW + "custom", _ex.Custom.Select(kv => new XElement(VW + "c", new XAttribute("k", kv.Key), kv.Value))),
                new XElement(VW + "page", System.Text.Json.JsonSerializer.Serialize(_page)));
            AddCustom(pkg, n, vx);
        }

        void AddCustom(Package pkg, int n, XElement root)
        {
            var uri = new Uri($"/customXml/item{n}.xml", UriKind.Relative);
            var part = pkg.CreatePart(uri, "application/xml", CompressionOption.Normal);
            _doc.CreateRelationship(new Uri($"../customXml/item{n}.xml", UriKind.Relative), TargetMode.Internal, Ns.RelCustomXml);
            using var s = part.GetStream(FileMode.Create, FileAccess.Write);
            new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root).Save(s, SaveOptions.DisableFormatting);
        }

        static object[] NsAttrs() => new object[]
        {
            new XAttribute(XNamespace.Xmlns + "w", Ns.W), new XAttribute(XNamespace.Xmlns + "r", Ns.R),
            new XAttribute(XNamespace.Xmlns + "wp", Ns.WP), new XAttribute(XNamespace.Xmlns + "a", Ns.A),
            new XAttribute(XNamespace.Xmlns + "pic", Ns.PIC), new XAttribute(XNamespace.Xmlns + "v", Ns.V), new XAttribute(XNamespace.Xmlns + "o", Ns.O)
        };

        static void Xml(PackagePart part, XElement root)
        {
            using var s = part.GetStream(FileMode.Create, FileAccess.Write);
            new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root).Save(s, SaveOptions.DisableFormatting);
        }

        static int Tw(double px) => (int)Math.Round(px * 15); // 1px = 1/96in = 15 twips
        static int HalfPt(double px) => (int)Math.Round(px * 0.75 * 2);
        static long Emu(double px) => (long)Math.Round(px * 9525);

        // ---------------- blocks ----------------
        void WriteBlocks(IEnumerable<Block> blocks, XElement into, int numId, int ilvl, bool inListItem = false)
        {
            foreach (var b in blocks.ToList())
            {
                switch (b)
                {
                    case Paragraph p:
                        if (p.Inlines.FirstInline is Floater dc && (dc.Tag as string) == "DropCap") into.Add(DropCapPara(dc, p));
                        into.Add(Para(p, numId, ilvl));
                        break;
                    case List l: WriteList(l, into, numId, ilvl); break;
                    case Table t: into.Add(TableXml(t)); break;
                    case Section s when SectionKind.Of(s) == SectionKind.Notes: _notes = s; break;
                    case Section s when SectionKind.Of(s) != null: WriteGenerated(s, into); break;
                    case Section s: WriteBlocks(s.Blocks, into, numId, ilvl); break;
                    case BlockUIContainer bc when bc.Child is Image img:
                        {
                            var p = new XElement(W + "p");
                            var ppr = new XElement(W + "pPr");
                            if (bc.BreakPageBefore) ppr.Add(new XElement(W + "pageBreakBefore"));
                            ppr.Add(new XElement(W + "jc", new XAttribute(W + "val", "center")));
                            p.Add(ppr, Drawing(img));
                            into.Add(p);
                            break;
                        }
                }
                // a list item paragraph only carries the number on its first paragraph
                if (inListItem) numId = -1;
            }
        }

        void WriteList(List l, XElement into, int parentNum, int parentLvl)
        {
            int num, lvl;
            if (parentNum < 0)
            {
                _nums.Add(new Dictionary<int, TextMarkerStyle>());
                _numStarts.Add(Math.Max(1, l.StartIndex));
                num = _nums.Count; lvl = 0;
            }
            else { num = parentNum; lvl = parentLvl + 1; }
            var map = _nums[num - 1];
            if (!map.ContainsKey(lvl)) map[lvl] = l.MarkerStyle;
            foreach (var li in l.ListItems)
            {
                bool first = true;
                foreach (var b in li.Blocks.ToList())
                {
                    if (b is List nested) { WriteList(nested, into, num, lvl); continue; }
                    if (b is Paragraph p)
                    {
                        var x = Para(p, first ? num : -1, lvl);
                        if (!first)
                        {
                            var ppr = x.Element(W + "pPr") ?? new XElement(W + "pPr");
                            if (ppr.Parent == null) x.AddFirst(ppr);
                            ppr.Elements(W + "ind").Remove();
                            InsertPPr(ppr, new XElement(W + "ind", new XAttribute(W + "left", 720 * (lvl + 1))));
                        }
                        into.Add(x);
                    }
                    else WriteBlocks(new[] { b }, into, -1, 0);
                    first = false;
                }
            }
        }

        static readonly string[] PPrOrder = { "pStyle", "keepNext", "keepLines", "pageBreakBefore", "framePr", "widowControl", "numPr", "suppressLineNumbers", "pBdr", "shd", "tabs", "suppressAutoHyphens", "spacing", "ind", "contextualSpacing", "jc", "rPr" };

        static void InsertPPr(XElement ppr, XElement el)
        {
            int idx = Array.IndexOf(PPrOrder, el.Name.LocalName);
            var after = ppr.Elements().LastOrDefault(e => Array.IndexOf(PPrOrder, e.Name.LocalName) <= idx);
            if (after == null) ppr.AddFirst(el); else after.AddAfterSelf(el);
        }

        static double Safe(double v) => double.IsNaN(v) || double.IsInfinity(v) ? 0 : v;
        static bool IsInList(Paragraph p) => Fmt.Ancestor<ListItem>(p) != null;

        XElement Para(Paragraph p, int numId, int ilvl)
        {
            var x = new XElement(W + "p");
            var ppr = new XElement(W + "pPr");
            var style = p.Tag as string;
            if (!string.IsNullOrEmpty(style) && style != "Normal" && StyleDef.All.Any(s => s.Id == style))
                ppr.Add(new XElement(W + "pStyle", new XAttribute(W + "val", style)));
            if (p.BreakPageBefore) ppr.Add(new XElement(W + "pageBreakBefore"));
            if (numId > 0)
                ppr.Add(new XElement(W + "numPr", new XElement(W + "ilvl", new XAttribute(W + "val", ilvl)), new XElement(W + "numId", new XAttribute(W + "val", numId))));
            if (p.BorderBrush is SolidColorBrush bb && (p.BorderThickness.Bottom > 0 || p.BorderThickness.Top > 0 || p.BorderThickness.Left > 0 || p.BorderThickness.Right > 0))
            {
                var bdr = new XElement(W + "pBdr");
                void Side(string name, double w) { if (w > 0) bdr.Add(new XElement(W + name, new XAttribute(W + "val", "single"), new XAttribute(W + "sz", Math.Max(2, (int)Math.Round(w * 6))), new XAttribute(W + "space", name == "left" || name == "right" ? 4 : 1), new XAttribute(W + "color", Fmt.Hex(bb.Color)))); }
                Side("top", p.BorderThickness.Top); Side("left", p.BorderThickness.Left); Side("bottom", p.BorderThickness.Bottom); Side("right", p.BorderThickness.Right);
                ppr.Add(bdr);
            }
            if (p.Background is SolidColorBrush pbg)
                ppr.Add(new XElement(W + "shd", new XAttribute(W + "val", "clear"), new XAttribute(W + "color", "auto"), new XAttribute(W + "fill", Fmt.Hex(pbg.Color))));
            var spacing = new XElement(W + "spacing",
                new XAttribute(W + "before", Tw(Safe(p.Margin.Top))), new XAttribute(W + "after", Tw(Safe(p.Margin.Bottom))),
                new XAttribute(W + "line", (int)Math.Round(Fmt.GetLineSpacing(p) * 240)), new XAttribute(W + "lineRule", "auto"));
            ppr.Add(spacing);
            if (numId > 0 || IsInList(p)) { spacing.SetAttributeValue(W + "after", Math.Max(Tw(Safe(p.Margin.Bottom)), 200)); ppr.Add(new XElement(W + "contextualSpacing")); }
            if (numId <= 0 || p.Margin.Left > 0.5 || Math.Abs(p.TextIndent) > 0.5 || p.Margin.Right > 0.5)
            {
                if (numId > 0)
                {
                    var ind = new XElement(W + "ind", new XAttribute(W + "left", 720 * (ilvl + 1) + Tw(Safe(p.Margin.Left))), new XAttribute(W + "hanging", 360));
                    if (p.Margin.Right > 0.5) ind.Add(new XAttribute(W + "right", Tw(Safe(p.Margin.Right))));
                    ppr.Add(ind);
                }
                else if (p.Margin.Left > 0.5 || Math.Abs(p.TextIndent) > 0.5 || p.Margin.Right > 0.5)
                {
                    var ind = new XElement(W + "ind", new XAttribute(W + "left", Tw(Safe(p.Margin.Left))), new XAttribute(W + "right", Tw(Safe(p.Margin.Right))));
                    if (p.TextIndent > 0) ind.Add(new XAttribute(W + "firstLine", Tw(Safe(p.TextIndent))));
                    else if (p.TextIndent < 0) ind.Add(new XAttribute(W + "hanging", Tw(-Safe(p.TextIndent))));
                    ppr.Add(ind);
                }
            }
            string jc = p.TextAlignment switch { TextAlignment.Center => "center", TextAlignment.Right => "right", TextAlignment.Justify => "both", _ => null };
            if (jc != null) ppr.Add(new XElement(W + "jc", new XAttribute(W + "val", jc)));
            // paragraph mark formatting so empty paragraphs keep their size
            ppr.Add(new XElement(W + "rPr", RunProps(p, true).Elements()));
            x.Add(ppr);
            WriteInlines(p.Inlines, x);
            return x;
        }

        void WriteInlines(InlineCollection inlines, XElement into)
        {
            foreach (var i in inlines.ToList())
            {
                switch (i)
                {
                    case Run r: WriteRun(r, into); break;
                    case LineBreak lb: into.Add(new XElement(W + "r", RunProps(lb), new XElement(W + "br"))); break;
                    case Hyperlink h when h.NavigateUri != null && h.NavigateUri.IsAbsoluteUri:
                        {
                            string id;
                            try { id = _doc.CreateRelationship(h.NavigateUri, TargetMode.External, Ns.RelHyperlink).Id; }
                            catch { WriteInlines(h.Inlines, into); break; }
                            var hx = new XElement(W + "hyperlink", new XAttribute(R + "id", id), new XAttribute(W + "history", 1));
                            WriteInlines(h.Inlines, hx);
                            into.Add(hx);
                            break;
                        }
                    case Hyperlink h when h.NavigateUri != null && h.NavigateUri.OriginalString.StartsWith("#"):
                        {
                            var hx = new XElement(W + "hyperlink", new XAttribute(W + "anchor", h.NavigateUri.OriginalString.Substring(1)), new XAttribute(W + "history", 1));
                            WriteInlines(h.Inlines, hx);
                            into.Add(hx);
                            break;
                        }
                    case Span s when s.Tag is CommentInfo ci:
                        {
                            if (!_commentIds.TryGetValue(ci, out var cid)) { cid = _commentIds.Count; _commentIds[ci] = cid; _comments.Add(ci); }
                            into.Add(new XElement(W + "commentRangeStart", new XAttribute(W + "id", cid)));
                            WriteInlines(s.Inlines, into);
                            into.Add(new XElement(W + "commentRangeEnd", new XAttribute(W + "id", cid)));
                            into.Add(new XElement(W + "r", new XElement(W + "rPr", new XElement(W + "rStyle", new XAttribute(W + "val", "CommentReference"))), new XElement(W + "commentReference", new XAttribute(W + "id", cid))));
                            break;
                        }
                    case Span s when s.Tag is RevInfo rev:
                        {
                            var rx = new XElement(W + (rev.Insert ? "ins" : "del"), new XAttribute(W + "id", _revId++), new XAttribute(W + "author", rev.Author ?? ""),
                                new XAttribute(W + "date", rev.Date.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")));
                            bool was = _inDel; _inDel = !rev.Insert;
                            WriteInlines(s.Inlines, rx);
                            _inDel = was;
                            into.Add(rx);
                            break;
                        }
                    case Span s when s.Tag is NoteRef nr:
                        into.Add(new XElement(W + "r", new XElement(W + "rPr", new XElement(W + "vertAlign", new XAttribute(W + "val", "superscript"))),
                            new XElement(W + (nr.Endnote ? "endnoteReference" : "footnoteReference"), new XAttribute(W + "id", nr.Number))));
                        break;
                    case Span s when s.Tag is BookmarkInfo bm:
                        {
                            int id = _bmId++;
                            into.Add(new XElement(W + "bookmarkStart", new XAttribute(W + "id", id), new XAttribute(W + "name", bm.Name)));
                            WriteInlines(s.Inlines, into);
                            into.Add(new XElement(W + "bookmarkEnd", new XAttribute(W + "id", id)));
                            break;
                        }
                    case Span s when s.Tag is FieldInfo fi: Field(" " + fi.Code + " ", s, into); break;
                    case Span s when s.Tag is MergeFieldInfo mf: Field($" MERGEFIELD {mf.Name.Replace(' ', '_')} ", s, into); break;
                    case Span s when s.Tag is CitationRef cr: Field($" CITATION {cr.SourceTag} \\l 1033 ", s, into); break;
                    case Span s when s.Tag is IndexEntry ie:
                        WriteInlines(s.Inlines, into);
                        into.Add(new XElement(W + "r", new XElement(W + "fldChar", new XAttribute(W + "fldCharType", "begin"))));
                        into.Add(new XElement(W + "r", new XElement(W + "instrText", new XAttribute(XNamespace.Xml + "space", "preserve"), $" {(ie.Kind == "TA" ? "TA \\l" : "XE")} \"{ie.Term}\" ")));
                        into.Add(new XElement(W + "r", new XElement(W + "fldChar", new XAttribute(W + "fldCharType", "end"))));
                        break;
                    case Span s: WriteInlines(s.Inlines, into); break;
                    case InlineUIContainer c when c.Child is Image img: into.Add(Drawing(img)); break;
                    case Floater f when (f.Tag as string) == "DropCap": break;
                    case Floater f:
                        {
                            var fimg = f.Blocks.OfType<BlockUIContainer>().Select(b => b.Child).OfType<Image>().FirstOrDefault();
                            if (fimg != null) into.Add(Drawing(fimg, f.HorizontalAlignment == HorizontalAlignment.Right ? "right" : "left"));
                            break;
                        }
                }
            }
        }

        void WriteInlineSingle(Inline i, XElement into)
        {
            var holder = new Paragraph();
            // write a single inline without moving it out of its paragraph
            switch (i)
            {
                case Run r: WriteRun(r, into); break;
                case Span sp: WriteInlines(sp.Inlines, into); break;
                case LineBreak lb: into.Add(new XElement(W + "r", new XElement(W + "br"))); break;
                case InlineUIContainer c when c.Child is Image img: into.Add(Drawing(img)); break;
            }
        }

        void Field(string instr, Span s, XElement into)
        {
            var fx = new XElement(W + "fldSimple", new XAttribute(W + "instr", instr));
            WriteInlines(s.Inlines, fx);
            into.Add(fx);
        }

        XElement DropCapPara(Floater f, Paragraph host)
        {
            var letterPara = f.Blocks.OfType<Paragraph>().FirstOrDefault();
            var run = letterPara == null ? null : Fmt.Descendants(letterPara).OfType<Run>().FirstOrDefault();
            double size = run?.FontSize ?? host.FontSize * 3;
            int lines = Math.Max(2, (int)Math.Round(size / Math.Max(1, host.FontSize * 1.2)));
            var x = new XElement(W + "p", new XElement(W + "pPr",
                new XElement(W + "keepNext"),
                new XElement(W + "framePr", new XAttribute(W + "dropCap", "drop"), new XAttribute(W + "lines", lines), new XAttribute(W + "wrap", "around"), new XAttribute(W + "vAnchor", "text"), new XAttribute(W + "hAnchor", "text")),
                new XElement(W + "spacing", new XAttribute(W + "after", 0), new XAttribute(W + "line", Tw(size * 0.9)), new XAttribute(W + "lineRule", "exact"))));
            if (run != null) WriteRun(run, x);
            return x;
        }

        /// <summary>Table of contents / index / table of authorities / bibliography.</summary>
        void WriteGenerated(Section s, XElement into)
        {
            var kind = SectionKind.Of(s);
            var content = new XElement(W + "sdtContent");
            int body = Tw(_page.BodyWidthPx);
            bool entryKind = kind == SectionKind.TOC || kind == SectionKind.Index || kind == SectionKind.TOA || kind == SectionKind.Figures;
            foreach (var b in s.Blocks)
            {
                if (b is Table t && entryKind)
                {
                    foreach (var row in t.RowGroups.SelectMany(g => g.Rows))
                    {
                        if (row.Cells.Count < 2) continue;
                        int level = row.Tag is int lv ? lv : 1;
                        var cellP = Fmt.AllParagraphs(row.Cells[0].Blocks).FirstOrDefault();
                        var text = Spans.Text(row.Cells[0]).Trim();
                        var page = Spans.Text(row.Cells[1]).Trim();
                        var p = new XElement(W + "p", new XElement(W + "pPr",
                            new XElement(W + "pStyle", new XAttribute(W + "val", (kind == SectionKind.TOC ? "TOC" : kind == SectionKind.Index ? "Index" : "TOA") + Math.Max(1, Math.Min(3, level)))),
                            new XElement(W + "tabs", new XElement(W + "tab", new XAttribute(W + "val", "right"), new XAttribute(W + "leader", "dot"), new XAttribute(W + "pos", body))),
                            new XElement(W + "spacing", new XAttribute(W + "after", 60)),
                            new XElement(W + "ind", new XAttribute(W + "left", 220 * (level - 1)))));
                        if (cellP != null && cellP.FontWeight.ToOpenTypeWeight() >= 600)
                            p.Add(new XElement(W + "r", new XElement(W + "rPr", new XElement(W + "b")), new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text)));
                        else p.Add(new XElement(W + "r", new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text)));
                        if (page.Length > 0) p.Add(new XElement(W + "r", new XElement(W + "tab")), new XElement(W + "r", new XElement(W + "t", page)));
                        content.Add(p);
                    }
                }
                else WriteBlocks(new[] { b }, content, -1, 0);
            }
            var sdtPr = new XElement(W + "sdtPr", new XElement(W + "tag", new XAttribute(W + "val", "Valco:" + kind)));
            if (kind == SectionKind.TOC) sdtPr.Add(new XElement(W + "docPartObj", new XElement(W + "docPartGallery", new XAttribute(W + "val", "Table of Contents")), new XElement(W + "docPartUnique")));
            if (kind == SectionKind.Bibliography) sdtPr.Add(new XElement(W + "docPartObj", new XElement(W + "docPartGallery", new XAttribute(W + "val", "Bibliographies")), new XElement(W + "docPartUnique")));
            into.Add(new XElement(W + "sdt", sdtPr, content));
        }

        void WriteRun(Run r, XElement into)
        {
            var text = r.Text ?? "";
            if (text.Length == 0) return;
            var rpr = RunProps(r);
            var x = new XElement(W + "r", rpr);
            var buf = new System.Text.StringBuilder();
            void Flush()
            {
                if (buf.Length == 0) return;
                x.Add(new XElement(W + (_inDel ? "delText" : "t"), new XAttribute(XNamespace.Xml + "space", "preserve"), buf.ToString()));
                buf.Clear();
            }
            foreach (var ch in text)
            {
                if (ch == '\t') { Flush(); x.Add(new XElement(W + "tab")); }
                else if (ch == '\n' || ch == '\v' || ch == '\u2028') { Flush(); x.Add(new XElement(W + "br")); }
                else if (ch == '\r') { }
                else if (ch < 0x20) { }
                else buf.Append(ch);
            }
            Flush();
            into.Add(x);
        }

        XElement RunProps(TextElement e, bool paragraphMark = false)
        {
            var rpr = new XElement(W + "rPr");
            var font = (e.FontFamily?.Source ?? "Calibri").Split(',')[0].Trim();
            if (font.StartsWith("Global ")) font = "Calibri";
            rpr.Add(new XElement(W + "rFonts", new XAttribute(W + "ascii", font), new XAttribute(W + "hAnsi", font), new XAttribute(W + "cs", font), new XAttribute(W + "eastAsia", font)));
            if (e.FontWeight.ToOpenTypeWeight() >= 600) rpr.Add(new XElement(W + "b"));
            if (e.FontStyle == FontStyles.Italic || e.FontStyle == FontStyles.Oblique) rpr.Add(new XElement(W + "i"));
            var deco = paragraphMark ? new TextDecorationCollection() : Fmt.EffectiveDecorations(e);
            if (Fmt.HasDecoration(deco, TextDecorationLocation.Strikethrough)) rpr.Add(new XElement(W + "strike"));
            if ((paragraphMark ? e.Foreground : Fmt.EffectiveForeground(e)) is SolidColorBrush fg && fg.Color != Colors.Black)
                rpr.Add(new XElement(W + "color", new XAttribute(W + "val", Fmt.Hex(fg.Color))));
            var hp = HalfPt(double.IsNaN(e.FontSize) ? 14.67 : e.FontSize);
            rpr.Add(new XElement(W + "sz", new XAttribute(W + "val", hp)), new XElement(W + "szCs", new XAttribute(W + "val", hp)));
            if (!paragraphMark)
            {
                var bg = Fmt.BrushColor(Fmt.EffectiveBackground(e));
                string hl = bg.HasValue ? Fmt.HighlightName(bg.Value) : null;
                if (hl != null) rpr.Add(new XElement(W + "highlight", new XAttribute(W + "val", hl)));
                if (Fmt.HasDecoration(deco, TextDecorationLocation.Underline)) rpr.Add(new XElement(W + "u", new XAttribute(W + "val", "single")));
                if (bg.HasValue && hl == null)
                    rpr.Add(new XElement(W + "shd", new XAttribute(W + "val", "clear"), new XAttribute(W + "color", "auto"), new XAttribute(W + "fill", Fmt.Hex(bg.Value))));
                var ba = Fmt.EffectiveBaseline(e);
                if (ba == BaselineAlignment.Superscript) rpr.Add(new XElement(W + "vertAlign", new XAttribute(W + "val", "superscript")));
                else if (ba == BaselineAlignment.Subscript) rpr.Add(new XElement(W + "vertAlign", new XAttribute(W + "val", "subscript")));
            }
            return rpr;
        }

        // ---------------- pictures ----------------
        XElement Drawing(Image img) => Drawing(img, null);

        XElement Drawing(Image img, string wrapSide)
        {
            var src = img.Source as BitmapSource;
            if (src == null) return new XElement(W + "r");
            byte[] bytes; string ext;
            if (img.Tag is ImageData d && d.Bytes != null) { bytes = d.Bytes; ext = d.Ext; }
            else
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(src));
                using var ms = new MemoryStream();
                enc.Save(ms);
                bytes = ms.ToArray(); ext = "png";
            }
            _imgNo++; _drawNo++;
            var name = $"image{_imgNo}.{ext}";
            var pkg = _doc.Package;
            var part = pkg.CreatePart(new Uri("/word/media/" + name, UriKind.Relative),
                ext == "jpeg" || ext == "jpg" ? "image/jpeg" : ext == "gif" ? "image/gif" : ext == "bmp" ? "image/bmp" : "image/png", CompressionOption.NotCompressed);
            using (var s = part.GetStream(FileMode.Create, FileAccess.Write)) s.Write(bytes, 0, bytes.Length);
            var rid = _doc.CreateRelationship(new Uri("media/" + name, UriKind.Relative), TargetMode.Internal, Ns.RelImage).Id;

            double w = !double.IsNaN(img.Width) ? img.Width : src.Width;
            double h = !double.IsNaN(img.Height) ? img.Height : src.Height;
            long cx = Emu(w), cy = Emu(h);
            XElement holder;
            if (wrapSide == null)
                holder = new XElement(WP + "inline", new XAttribute("distT", 0), new XAttribute("distB", 0), new XAttribute("distL", 0), new XAttribute("distR", 0));
            else
                holder = new XElement(WP + "anchor", new XAttribute("distT", 0), new XAttribute("distB", 0), new XAttribute("distL", 114300), new XAttribute("distR", 114300),
                    new XAttribute("simplePos", 0), new XAttribute("relativeHeight", 251659264 + _drawNo), new XAttribute("behindDoc", 0), new XAttribute("locked", 0),
                    new XAttribute("layoutInCell", 1), new XAttribute("allowOverlap", 1),
                    new XElement(WP + "simplePos", new XAttribute("x", 0), new XAttribute("y", 0)),
                    new XElement(WP + "positionH", new XAttribute("relativeFrom", "column"), new XElement(WP + "align", wrapSide)),
                    new XElement(WP + "positionV", new XAttribute("relativeFrom", "paragraph"), new XElement(WP + "posOffset", 0)));
            holder.Add(new XElement(WP + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)));
            if (wrapSide != null) holder.Add(new XElement(WP + "effectExtent", new XAttribute("l", 0), new XAttribute("t", 0), new XAttribute("r", 0), new XAttribute("b", 0)), new XElement(WP + "wrapSquare", new XAttribute("wrapText", "bothSides")));
            return new XElement(W + "r",
                new XElement(W + "drawing",
                    holder.AddAndReturn(
                        
                        new XElement(WP + "docPr", new XAttribute("id", _drawNo), new XAttribute("name", "Picture " + _drawNo)),
                        new XElement(WP + "cNvGraphicFramePr", new XElement(A + "graphicFrameLocks", new XAttribute("noChangeAspect", 1))),
                        new XElement(A + "graphic",
                            new XElement(A + "graphicData", new XAttribute("uri", "http://schemas.openxmlformats.org/drawingml/2006/picture"),
                                new XElement(PIC + "pic",
                                    new XElement(PIC + "nvPicPr",
                                        new XElement(PIC + "cNvPr", new XAttribute("id", 0), new XAttribute("name", name)),
                                        new XElement(PIC + "cNvPicPr")),
                                    new XElement(PIC + "blipFill",
                                        new XElement(A + "blip", new XAttribute(R + "embed", rid)),
                                        new XElement(A + "stretch", new XElement(A + "fillRect"))),
                                    new XElement(PIC + "spPr",
                                        new XElement(A + "xfrm", new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                                            new XElement(A + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy))),
                                        new XElement(A + "prstGeom", new XAttribute("prst", "rect"), new XElement(A + "avLst")))))))));
        }

        // ---------------- tables ----------------
        XElement TableXml(Table t)
        {
            int cols = Math.Max(t.Columns.Count, t.RowGroups.SelectMany(g => g.Rows).Select(r => r.Cells.Sum(c => Math.Max(1, c.ColumnSpan))).DefaultIfEmpty(1).Max());
            double body = _page.BodyWidthPx;
            var widths = new double[cols];
            double fixedSum = 0; int autoCount = 0;
            for (int i = 0; i < cols; i++)
            {
                var gl = i < t.Columns.Count ? t.Columns[i].Width : GridLength.Auto;
                if (gl.IsAbsolute) { widths[i] = gl.Value; fixedSum += gl.Value; } else autoCount++;
            }
            for (int i = 0; i < cols; i++) if (widths[i] == 0) widths[i] = autoCount > 0 ? Math.Max(30, (body - fixedSum) / autoCount) : body / cols;

            bool borders = t.RowGroups.SelectMany(g => g.Rows).SelectMany(r => r.Cells).Any(c => c.BorderThickness.Left > 0 || c.BorderThickness.Top > 0);
            var tblPr = new XElement(W + "tblPr", new XElement(W + "tblW", new XAttribute(W + "w", 0), new XAttribute(W + "type", "auto")));
            if (!double.IsNaN(t.Margin.Left) && t.Margin.Left > 0) tblPr.Add(new XElement(W + "tblInd", new XAttribute(W + "w", Tw(t.Margin.Left)), new XAttribute(W + "type", "dxa")));
            if (borders)
            {
                XElement B(string n) => new XElement(W + n, new XAttribute(W + "val", "single"), new XAttribute(W + "sz", 4), new XAttribute(W + "space", 0), new XAttribute(W + "color", "auto"));
                tblPr.Add(new XElement(W + "tblBorders", B("top"), B("left"), B("bottom"), B("right"), B("insideH"), B("insideV")));
            }
            tblPr.Add(new XElement(W + "tblLayout", new XAttribute(W + "type", "fixed")));
            tblPr.Add(new XElement(W + "tblLook", new XAttribute(W + "val", "04A0")));
            var grid = new XElement(W + "tblGrid", widths.Select(w => new XElement(W + "gridCol", new XAttribute(W + "w", Tw(w)))));
            var x = new XElement(W + "tbl", tblPr, grid);
            var pending = new (int Rows, int Span)[cols + 1];
            XElement Covered(int span, double w)
            {
                var pr = new XElement(W + "tcPr", new XElement(W + "tcW", new XAttribute(W + "w", Tw(w)), new XAttribute(W + "type", "dxa")));
                if (span > 1) pr.Add(new XElement(W + "gridSpan", new XAttribute(W + "val", span)));
                pr.Add(new XElement(W + "vMerge"));
                return new XElement(W + "tc", pr, new XElement(W + "p"));
            }
            foreach (var g in t.RowGroups)
                foreach (var row in g.Rows)
                {
                    var tr = new XElement(W + "tr");
                    if ((row.Tag as string) == "Header") tr.Add(new XElement(W + "trPr", new XElement(W + "tblHeader")));
                    int col = 0;
                    void FillCovered()
                    {
                        while (col < cols && pending[col].Rows > 0)
                        {
                            int sp = Math.Max(1, pending[col].Span); double cw = 0; for (int k = col; k < Math.Min(cols, col + sp); k++) cw += widths[k];
                            tr.Add(Covered(sp, cw)); pending[col].Rows--; col += sp;
                        }
                    }
                    foreach (var c in row.Cells)
                    {
                        FillCovered();
                        int span = Math.Max(1, c.ColumnSpan);
                        double w = 0; for (int k = col; k < Math.Min(cols, col + span); k++) w += widths[k];
                        var tcPr = new XElement(W + "tcPr", new XElement(W + "tcW", new XAttribute(W + "w", Tw(w)), new XAttribute(W + "type", "dxa")));
                        if (span > 1) tcPr.Add(new XElement(W + "gridSpan", new XAttribute(W + "val", span)));
                        if (c.RowSpan > 1) { tcPr.Add(new XElement(W + "vMerge", new XAttribute(W + "val", "restart"))); if (col < cols) pending[col] = (c.RowSpan - 1, span); }
                        {
                            var bt = c.BorderThickness; var bc = c.BorderBrush is SolidColorBrush bsb ? Fmt.Hex(bsb.Color) : "000000";
                            XElement Side(string n, double px) => px <= 0 ? new XElement(W + n, new XAttribute(W + "val", "nil")) : new XElement(W + n, new XAttribute(W + "val", "single"), new XAttribute(W + "sz", Math.Max(2, (int)Math.Round(px * 72 / 96 * 8))), new XAttribute(W + "space", 0), new XAttribute(W + "color", bc));
                            tcPr.Add(new XElement(W + "tcBorders", Side("top", bt.Top), Side("left", bt.Left), Side("bottom", bt.Bottom), Side("right", bt.Right)));
                        }
                        if (c.Background is SolidColorBrush cb)
                            tcPr.Add(new XElement(W + "shd", new XAttribute(W + "val", "clear"), new XAttribute(W + "color", "auto"), new XAttribute(W + "fill", Fmt.Hex(cb.Color))));
                        var pad = c.Padding;
                        if (Math.Abs(pad.Left - 7) > 0.5 || Math.Abs(pad.Top - 2) > 0.5 || Math.Abs(pad.Right - 7) > 0.5 || Math.Abs(pad.Bottom - 2) > 0.5)
                            tcPr.Add(new XElement(W + "tcMar", new XElement(W + "top", new XAttribute(W + "w", Tw(pad.Top)), new XAttribute(W + "type", "dxa")), new XElement(W + "left", new XAttribute(W + "w", Tw(pad.Left)), new XAttribute(W + "type", "dxa")),
                                new XElement(W + "bottom", new XAttribute(W + "w", Tw(pad.Bottom)), new XAttribute(W + "type", "dxa")), new XElement(W + "right", new XAttribute(W + "w", Tw(pad.Right)), new XAttribute(W + "type", "dxa"))));
                        var tc = new XElement(W + "tc", tcPr);
                        WriteBlocks(c.Blocks, tc, -1, 0);
                        if (!tc.Elements(W + "p").Any() || tc.Elements().Last().Name != W + "p") tc.Add(new XElement(W + "p"));
                        tr.Add(tc);
                        col += span;
                    }
                    FillCovered();
                    if (tr.Elements(W + "tc").Any()) x.Add(tr);
                }
            return x;
        }

        // ---------------- styles / numbering ----------------
        XElement BuildStyles()
        {
            var s = Settings.Current;
            var normal = StyleDef.Get("Normal");
            XElement RPr(StyleDef d)
            {
                var r = new XElement(W + "rPr", new XElement(W + "rFonts", new XAttribute(W + "ascii", d.Font), new XAttribute(W + "hAnsi", d.Font), new XAttribute(W + "cs", d.Font), new XAttribute(W + "eastAsia", d.Font)));
                if (d.Bold) r.Add(new XElement(W + "b"));
                if (d.Italic) r.Add(new XElement(W + "i"));
                if (d.Color != Colors.Black) r.Add(new XElement(W + "color", new XAttribute(W + "val", Fmt.Hex(d.Color))));
                r.Add(new XElement(W + "sz", new XAttribute(W + "val", (int)Math.Round(d.SizePt * 2))), new XElement(W + "szCs", new XAttribute(W + "val", (int)Math.Round(d.SizePt * 2))));
                return r;
            }
            XElement PPr(StyleDef d)
            {
                var p = new XElement(W + "pPr");
                if (d.Id.StartsWith("Heading") || d.Id == "Title") p.Add(new XElement(W + "keepNext"));
                if (d.BorderBottom.HasValue)
                    p.Add(new XElement(W + "pBdr", new XElement(W + "bottom", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", 8), new XAttribute(W + "space", 4), new XAttribute(W + "color", Fmt.Hex(d.BorderBottom.Value)))));
                if (d.Background.HasValue)
                    p.Add(new XElement(W + "shd", new XAttribute(W + "val", "clear"), new XAttribute(W + "color", "auto"), new XAttribute(W + "fill", Fmt.Hex(d.Background.Value))));
                p.Add(new XElement(W + "spacing", new XAttribute(W + "before", (int)(d.BeforePt * 20)), new XAttribute(W + "after", (int)(d.AfterPt * 20)),
                    new XAttribute(W + "line", (int)Math.Round(d.Line * 240)), new XAttribute(W + "lineRule", "auto")));
                if (d.IndentIn > 0) p.Add(new XElement(W + "ind", new XAttribute(W + "left", (int)(d.IndentIn * 1440))));
                if (d.Id.StartsWith("Heading")) p.Add(new XElement(W + "outlineLvl", new XAttribute(W + "val", d.Id.Last() - '1')));
                return p;
            }
            string WordName(StyleDef d) => d.Id switch
            {
                "Heading1" => "heading 1", "Heading2" => "heading 2", "Heading3" => "heading 3", _ => d.Name
            };
            var root = new XElement(W + "styles", new XAttribute(XNamespace.Xmlns + "w", W),
                new XElement(W + "docDefaults",
                    new XElement(W + "rPrDefault", new XElement(W + "rPr",
                        new XElement(W + "rFonts", new XAttribute(W + "ascii", normal.Font), new XAttribute(W + "hAnsi", normal.Font), new XAttribute(W + "cs", normal.Font), new XAttribute(W + "eastAsia", normal.Font)),
                        new XElement(W + "sz", new XAttribute(W + "val", (int)Math.Round(normal.SizePt * 2))), new XElement(W + "szCs", new XAttribute(W + "val", (int)Math.Round(normal.SizePt * 2))),
                        new XElement(W + "lang", new XAttribute(W + "val", "en-US")))),
                    new XElement(W + "pPrDefault", new XElement(W + "pPr", new XElement(W + "spacing", new XAttribute(W + "after", 200), new XAttribute(W + "line", 276), new XAttribute(W + "lineRule", "auto"))))));
            foreach (var d in StyleDef.All)
            {
                var st = new XElement(W + "style", new XAttribute(W + "type", "paragraph"), new XAttribute(W + "styleId", d.Id));
                if (d.Id == "Normal") st.Add(new XAttribute(W + "default", 1));
                st.Add(new XElement(W + "name", new XAttribute(W + "val", WordName(d))));
                if (d.Id != "Normal") st.Add(new XElement(W + "basedOn", new XAttribute(W + "val", "Normal")), new XElement(W + "next", new XAttribute(W + "val", "Normal")));
                st.Add(new XElement(W + "qFormat"));
                st.Add(PPr(d), RPr(d));
                root.Add(st);
            }
            root.Add(new XElement(W + "style", new XAttribute(W + "type", "character"), new XAttribute(W + "styleId", "Hyperlink"),
                new XElement(W + "name", new XAttribute(W + "val", "Hyperlink")),
                new XElement(W + "rPr", new XElement(W + "color", new XAttribute(W + "val", "0000FF")), new XElement(W + "u", new XAttribute(W + "val", "single")))));
            root.Add(new XElement(W + "style", new XAttribute(W + "type", "table"), new XAttribute(W + "default", 1), new XAttribute(W + "styleId", "TableNormal"),
                new XElement(W + "name", new XAttribute(W + "val", "Normal Table")),
                new XElement(W + "tblPr", new XElement(W + "tblCellMar",
                    new XElement(W + "left", new XAttribute(W + "w", 108), new XAttribute(W + "type", "dxa")),
                    new XElement(W + "right", new XAttribute(W + "w", 108), new XAttribute(W + "type", "dxa"))))));
            return root;
        }

        XElement BuildNumbering()
        {
            var root = new XElement(W + "numbering", new XAttribute(XNamespace.Xmlns + "w", W));
            for (int n = 0; n < _nums.Count; n++)
            {
                var map = _nums[n];
                var an = new XElement(W + "abstractNum", new XAttribute(W + "abstractNumId", n), new XElement(W + "multiLevelType", new XAttribute(W + "val", "hybridMultilevel")));
                for (int lvl = 0; lvl < 9; lvl++)
                {
                    TextMarkerStyle m;
                    if (!map.TryGetValue(lvl, out m))
                    {
                        var first = map.TryGetValue(0, out var f0) ? f0 : TextMarkerStyle.Disc;
                        m = Fmt.IsBullet(first) ? Fmt.BulletCycle[lvl % 3] : Fmt.NumberCycle[lvl % 3];
                    }
                    string fmt, text;
                    switch (m)
                    {
                        case TextMarkerStyle.Decimal: fmt = "decimal"; text = $"%{lvl + 1}."; break;
                        case TextMarkerStyle.LowerLatin: fmt = "lowerLetter"; text = $"%{lvl + 1}."; break;
                        case TextMarkerStyle.UpperLatin: fmt = "upperLetter"; text = $"%{lvl + 1}."; break;
                        case TextMarkerStyle.LowerRoman: fmt = "lowerRoman"; text = $"%{lvl + 1}."; break;
                        case TextMarkerStyle.UpperRoman: fmt = "upperRoman"; text = $"%{lvl + 1}."; break;
                        case TextMarkerStyle.Circle: fmt = "bullet"; text = "◦"; break;
                        case TextMarkerStyle.Square: case TextMarkerStyle.Box: fmt = "bullet"; text = "▪"; break;
                        default: fmt = "bullet"; text = "•"; break;
                    }
                    an.Add(new XElement(W + "lvl", new XAttribute(W + "ilvl", lvl),
                        new XElement(W + "start", new XAttribute(W + "val", lvl == 0 ? _numStarts[n] : 1)),
                        new XElement(W + "numFmt", new XAttribute(W + "val", fmt)),
                        new XElement(W + "lvlText", new XAttribute(W + "val", text)),
                        new XElement(W + "lvlJc", new XAttribute(W + "val", "left")),
                        new XElement(W + "pPr", new XElement(W + "ind", new XAttribute(W + "left", 720 * (lvl + 1)), new XAttribute(W + "hanging", 360)))));
                }
                root.Add(an);
            }
            for (int n = 0; n < _nums.Count; n++)
                root.Add(new XElement(W + "num", new XAttribute(W + "numId", n + 1), new XElement(W + "abstractNumId", new XAttribute(W + "val", n))));
            return root;
        }
    }

    // =====================================================================================
    //  Reader
    // =====================================================================================
    public partial class DocxReader
    {
        static readonly XNamespace W = Ns.W, R = Ns.R, WP = Ns.WP, A = Ns.A, V = Ns.V;

        class RP
        {
            public string Font; public double? Size; public bool? Bold, Italic, Underline, Strike, Caps, Hidden;
            public Color? Color, Highlight, Shade; public string VertAlign;
            public RP Clone() => (RP)MemberwiseClone();
        }
        class PP
        {
            public TextAlignment? Align; public double? Before, After, Line, LineExact, Left, Right, First;
            public bool? PageBreak; public Color? BorderColor; public double? BorderPx; public Color? Shade;
            public int? NumId, Ilvl;
            public PP Clone() => (PP)MemberwiseClone();
        }

        PackagePart _doc;
        Package _pkg;
        readonly Dictionary<string, XElement> _styles = new Dictionary<string, XElement>();
        string _defaultParaStyle;
        RP _defRP = new RP();
        PP _defPP = new PP();
        string _minorFont = "Calibri", _majorFont = "Cambria";
        readonly Dictionary<string, Color> _themeColors = new Dictionary<string, Color>();
        readonly Dictionary<int, int> _numToAbstract = new Dictionary<int, int>();
        readonly Dictionary<int, Dictionary<int, (string Fmt, int Start)>> _abstract = new Dictionary<int, Dictionary<int, (string, int)>>();
        readonly Dictionary<int, Dictionary<int, int>> _numStartOverride = new Dictionary<int, Dictionary<int, int>>();
        bool _pendingPageBreak;

        public static LoadedDocument Load(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var ms = new MemoryStream();
            fs.CopyTo(ms);
            ms.Position = 0;
            return Load(ms);
        }

        public static LoadedDocument Load(Stream s)
        {
            using var pkg = Package.Open(s, FileMode.Open, FileAccess.Read);
            return new DocxReader().Read(pkg);
        }

        static XDocument ReadXml(PackagePart part)
        {
            using var s = part.GetStream(FileMode.Open, FileAccess.Read);
            return XDocument.Load(s);
        }

        PackagePart RelPart(PackagePart from, string relType)
        {
            var rel = from.GetRelationshipsByType(relType).FirstOrDefault();
            if (rel == null) return null;
            var uri = PackUriHelper.ResolvePartUri(from.Uri, rel.TargetUri);
            return _pkg.PartExists(uri) ? _pkg.GetPart(uri) : null;
        }

        LoadedDocument Read(Package pkg)
        {
            _pkg = pkg;
            var rel = pkg.GetRelationshipsByType(Ns.RelOfficeDoc).FirstOrDefault() ?? pkg.GetRelationshipsByType(Ns.RelOfficeDocStrict).FirstOrDefault();
            if (rel == null) throw new InvalidDataException("This file is not a Word document.");
            _doc = pkg.GetPart(PackUriHelper.ResolvePartUri(new Uri("/", UriKind.Relative), rel.TargetUri));
            var xdoc = ReadXml(_doc);

            var theme = RelPart(_doc, Ns.RelTheme);
            if (theme != null) ReadTheme(ReadXml(theme));
            var styles = RelPart(_doc, Ns.RelStyles);
            if (styles != null) ReadStyles(ReadXml(styles));
            else { _defRP.Font = "Calibri"; _defRP.Size = 11; }
            var numbering = RelPart(_doc, Ns.RelNumbering);
            if (numbering != null) ReadNumbering(ReadXml(numbering));
            LoadSideParts();

            var fd = new FlowDocument();
            var body = xdoc.Root.Element(W + "body");
            var page = new PageSettings { PageNumbers = false };
            if (body != null)
            {
                ReadBlocks(body.Elements(), fd.Blocks);
                var sect = body.Element(W + "sectPr") ?? body.Descendants(W + "sectPr").LastOrDefault();
                if (sect != null) ReadSection(sect, page);
            }
            if (fd.Blocks.Count == 0) fd.Blocks.Add(new Paragraph());
            ReadDocumentBackground(xdoc, page);
            FinishNotes(fd);
            var extras = ReadExtras(ref page);

            var props = pkg.PackageProperties;
            return new LoadedDocument { Document = fd, Page = page, Title = props.Title, Author = props.Creator, Extras = extras };
        }

        // ---------------- theme / styles / numbering ----------------
        void ReadTheme(XDocument t)
        {
            var fs = t.Descendants(A + "fontScheme").FirstOrDefault();
            var minor = fs?.Element(A + "minorFont")?.Element(A + "latin")?.Attribute("typeface")?.Value;
            var major = fs?.Element(A + "majorFont")?.Element(A + "latin")?.Attribute("typeface")?.Value;
            if (!string.IsNullOrEmpty(minor)) _minorFont = minor;
            if (!string.IsNullOrEmpty(major)) _majorFont = major;
            var cs = t.Descendants(A + "clrScheme").FirstOrDefault();
            if (cs != null)
                foreach (var c in cs.Elements())
                {
                    var hex = c.Element(A + "srgbClr")?.Attribute("val")?.Value ?? c.Element(A + "sysClr")?.Attribute("lastClr")?.Value;
                    var col = ParseHex(hex);
                    if (col.HasValue) _themeColors[c.Name.LocalName] = col.Value;
                }
        }

        void ReadStyles(XDocument s)
        {
            foreach (var st in s.Root.Elements(W + "style"))
            {
                var id = st.Attribute(W + "styleId")?.Value;
                if (id == null) continue;
                _styles[id] = st;
                if (st.Attribute(W + "type")?.Value == "paragraph" && st.Attribute(W + "default") != null && On(st.Attribute(W + "default").Value)) _defaultParaStyle = id;
            }
            var dd = s.Root.Element(W + "docDefaults");
            _defRP = new RP { Font = "Times New Roman", Size = 10 };
            ApplyRPr(_defRP, dd?.Element(W + "rPrDefault")?.Element(W + "rPr"));
            ApplyPPr(_defPP, dd?.Element(W + "pPrDefault")?.Element(W + "pPr"));
        }

        IEnumerable<XElement> StyleChain(string id)
        {
            var chain = new List<XElement>();
            var seen = new HashSet<string>();
            while (id != null && _styles.TryGetValue(id, out var st) && seen.Add(id))
            {
                chain.Insert(0, st);
                id = st.Element(W + "basedOn")?.Attribute(W + "val")?.Value;
            }
            return chain;
        }

        void ReadNumbering(XDocument n)
        {
            foreach (var an in n.Root.Elements(W + "abstractNum"))
            {
                if (!int.TryParse(an.Attribute(W + "abstractNumId")?.Value, out var aid)) continue;
                var lv = new Dictionary<int, (string, int)>();
                foreach (var l in an.Elements(W + "lvl"))
                {
                    int.TryParse(l.Attribute(W + "ilvl")?.Value, out var il);
                    var fmt = l.Element(W + "numFmt")?.Attribute(W + "val")?.Value ?? "bullet";
                    var text = l.Element(W + "lvlText")?.Attribute(W + "val")?.Value ?? "";
                    if (fmt == "bullet") fmt = text.Contains("o") || text.Contains("◦") ? "bullet-circle" : text.Contains("§") || text.Contains("▪") || text.Contains("\uF0A7") ? "bullet-square" : "bullet";
                    int start = int.TryParse(l.Element(W + "start")?.Attribute(W + "val")?.Value, out var st) ? st : 1;
                    lv[il] = (fmt, start);
                }
                _abstract[aid] = lv;
            }
            foreach (var num in n.Root.Elements(W + "num"))
            {
                if (!int.TryParse(num.Attribute(W + "numId")?.Value, out var nid)) continue;
                if (int.TryParse(num.Element(W + "abstractNumId")?.Attribute(W + "val")?.Value, out var aid)) _numToAbstract[nid] = aid;
                foreach (var ov in num.Elements(W + "lvlOverride"))
                {
                    if (int.TryParse(ov.Attribute(W + "ilvl")?.Value, out var il) && int.TryParse(ov.Element(W + "startOverride")?.Attribute(W + "val")?.Value, out var so))
                    {
                        if (!_numStartOverride.TryGetValue(nid, out var d)) _numStartOverride[nid] = d = new Dictionary<int, int>();
                        d[il] = so;
                    }
                }
            }
        }

        (TextMarkerStyle Marker, int Start) NumFormat(int numId, int ilvl)
        {
            string fmt = "bullet"; int start = 1;
            if (_numToAbstract.TryGetValue(numId, out var aid) && _abstract.TryGetValue(aid, out var lv) && lv.TryGetValue(ilvl, out var f)) { fmt = f.Fmt; start = f.Start; }
            if (_numStartOverride.TryGetValue(numId, out var ov) && ov.TryGetValue(ilvl, out var so)) start = so;
            var m = fmt switch
            {
                "decimal" or "decimalZero" => TextMarkerStyle.Decimal,
                "lowerLetter" => TextMarkerStyle.LowerLatin,
                "upperLetter" => TextMarkerStyle.UpperLatin,
                "lowerRoman" => TextMarkerStyle.LowerRoman,
                "upperRoman" => TextMarkerStyle.UpperRoman,
                "bullet-circle" => TextMarkerStyle.Circle,
                "bullet-square" => TextMarkerStyle.Square,
                "none" => TextMarkerStyle.None,
                "bullet" => TextMarkerStyle.Disc,
                _ => TextMarkerStyle.Decimal
            };
            return (m, start);
        }

        // ---------------- property parsing ----------------
        static bool On(string v) => v == null || !(v == "0" || v == "false" || v == "off" || v == "none");
        static bool? Toggle(XElement e) => e == null ? (bool?)null : On(e.Attribute(W + "val")?.Value);
        static double? Num(XElement e, string attr)
        {
            var v = e?.Attribute(W + attr)?.Value;
            return v != null && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : (double?)null;
        }
        static Color? ParseHex(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex == "auto" || hex.Length != 6) return null;
            try { return Color.FromRgb(Convert.ToByte(hex.Substring(0, 2), 16), Convert.ToByte(hex.Substring(2, 2), 16), Convert.ToByte(hex.Substring(4, 2), 16)); }
            catch { return null; }
        }

        string ResolveFont(XElement rf)
        {
            if (rf == null) return null;
            var f = rf.Attribute(W + "ascii")?.Value ?? rf.Attribute(W + "hAnsi")?.Value;
            if (f != null) return f;
            var th = rf.Attribute(W + "asciiTheme")?.Value ?? rf.Attribute(W + "hAnsiTheme")?.Value;
            if (th != null) return th.StartsWith("major") ? _majorFont : _minorFont;
            return null;
        }

        void ApplyRPr(RP rp, XElement r)
        {
            if (r == null) return;
            var font = ResolveFont(r.Element(W + "rFonts"));
            if (font != null) rp.Font = font;
            var sz = Num(r.Element(W + "sz"), "val");
            if (sz.HasValue) rp.Size = sz.Value / 2;
            rp.Bold = Toggle(r.Element(W + "b")) ?? rp.Bold;
            rp.Italic = Toggle(r.Element(W + "i")) ?? rp.Italic;
            rp.Strike = Toggle(r.Element(W + "strike")) ?? Toggle(r.Element(W + "dstrike")) ?? rp.Strike;
            rp.Caps = Toggle(r.Element(W + "caps")) ?? rp.Caps;
            rp.Hidden = Toggle(r.Element(W + "vanish")) ?? rp.Hidden;
            var u = r.Element(W + "u");
            if (u != null) rp.Underline = On(u.Attribute(W + "val")?.Value);
            var c = r.Element(W + "color");
            if (c != null)
            {
                var tc = c.Attribute(W + "themeColor")?.Value;
                Color? col = ParseHex(c.Attribute(W + "val")?.Value);
                if (col == null && tc != null)
                {
                    var key = tc switch { "text1" => "dk1", "text2" => "dk2", "background1" => "lt1", "background2" => "lt2", _ => tc };
                    if (_themeColors.TryGetValue(key, out var thc)) col = thc;
                }
                rp.Color = col;
                if (c.Attribute(W + "val")?.Value == "auto") rp.Color = null;
            }
            var hl = r.Element(W + "highlight")?.Attribute(W + "val")?.Value;
            if (hl != null)
            {
                if (hl == "none") rp.Highlight = null;
                else { var h = Fmt.Highlights.FirstOrDefault(x => string.Equals(x.Name, hl, StringComparison.OrdinalIgnoreCase)); if (h.Name != null) rp.Highlight = h.Color; }
            }
            var shd = r.Element(W + "shd")?.Attribute(W + "fill")?.Value;
            if (shd != null) rp.Shade = ParseHex(shd);
            var va = r.Element(W + "vertAlign")?.Attribute(W + "val")?.Value;
            if (va != null) rp.VertAlign = va;
        }

        void ApplyPPr(PP pp, XElement p)
        {
            if (p == null) return;
            var jc = p.Element(W + "jc")?.Attribute(W + "val")?.Value;
            if (jc != null)
                pp.Align = jc switch { "center" => TextAlignment.Center, "right" or "end" => TextAlignment.Right, "both" or "distribute" => TextAlignment.Justify, _ => TextAlignment.Left };
            var sp = p.Element(W + "spacing");
            if (sp != null)
            {
                var b = Num(sp, "before"); if (b.HasValue) pp.Before = b / 20;
                var a = Num(sp, "after"); if (a.HasValue) pp.After = a / 20;
                if (On(sp.Attribute(W + "beforeAutospacing")?.Value) && sp.Attribute(W + "beforeAutospacing") != null) pp.Before = 14;
                if (On(sp.Attribute(W + "afterAutospacing")?.Value) && sp.Attribute(W + "afterAutospacing") != null) pp.After = 14;
                var line = Num(sp, "line");
                var rule = sp.Attribute(W + "lineRule")?.Value ?? "auto";
                if (line.HasValue)
                {
                    if (rule == "auto") { pp.Line = line / 240; pp.LineExact = null; }
                    else { pp.LineExact = line / 20; pp.Line = null; }
                }
            }
            var ind = p.Element(W + "ind");
            if (ind != null)
            {
                var l = Num(ind, "left") ?? Num(ind, "start"); if (l.HasValue) pp.Left = l / 15;
                var r = Num(ind, "right") ?? Num(ind, "end"); if (r.HasValue) pp.Right = r / 15;
                var fl = Num(ind, "firstLine"); if (fl.HasValue) pp.First = fl / 15;
                var hg = Num(ind, "hanging"); if (hg.HasValue) pp.First = -hg / 15;
            }
            var pb = Toggle(p.Element(W + "pageBreakBefore")); if (pb.HasValue) pp.PageBreak = pb;
            var bot = p.Element(W + "pBdr")?.Element(W + "bottom");
            if (bot != null && On(bot.Attribute(W + "val")?.Value) && bot.Attribute(W + "val")?.Value != "nil")
            {
                pp.BorderColor = ParseHex(bot.Attribute(W + "color")?.Value) ?? Colors.Black;
                pp.BorderPx = Math.Max(1, (Num(bot, "sz") ?? 4) / 6);
            }
            var shd = p.Element(W + "shd")?.Attribute(W + "fill")?.Value;
            if (shd != null) pp.Shade = ParseHex(shd);
            var np = p.Element(W + "numPr");
            if (np != null)
            {
                var nid = Num(np.Element(W + "numId"), "val"); if (nid.HasValue) pp.NumId = (int)nid;
                var il = Num(np.Element(W + "ilvl"), "val"); if (il.HasValue) pp.Ilvl = (int)il;
            }
        }

        (PP, RP) ParaStyleProps(string styleId)
        {
            var pp = _defPP.Clone();
            var rp = _defRP.Clone();
            foreach (var st in StyleChain(styleId ?? _defaultParaStyle))
            {
                ApplyPPr(pp, st.Element(W + "pPr"));
                ApplyRPr(rp, st.Element(W + "rPr"));
            }
            return (pp, rp);
        }

        string MapStyleTag(string styleId)
        {
            if (styleId == null) return null;
            if (StyleDef.All.Any(s => s.Id == styleId)) return styleId;
            var name = _styles.TryGetValue(styleId, out var st) ? st.Element(W + "name")?.Attribute(W + "val")?.Value?.ToLowerInvariant() : styleId.ToLowerInvariant();
            return name switch
            {
                "title" => "Title", "subtitle" => "Subtitle", "heading 1" => "Heading1", "heading 2" => "Heading2", "heading 3" => "Heading3",
                "quote" => "Quote", "no spacing" => "NoSpacing", "normal" => "Normal", _ => null
            };
        }

        // ---------------- blocks ----------------
        class ListState { public List List; public int NumId; public int Level; }

        void ReadBlocks(IEnumerable<XElement> elements, BlockCollection into)
        {
            var stack = new List<ListState>();
            foreach (var el in elements)
            {
                var name = el.Name.LocalName;
                if (el.Name.Namespace != W) continue;
                if (name == "p")
                {
                    if (TryReadDropCap(el)) continue;
                    foreach (var (para, pp) in ReadParagraph(el))
                    {
                        ApplyPendingDropCap(para);
                        if (pp.NumId.HasValue && pp.NumId.Value > 0 && NumFormat(pp.NumId.Value, pp.Ilvl ?? 0).Marker != TextMarkerStyle.None)
                            AddToList(stack, into, para, pp.NumId.Value, Math.Max(0, Math.Min(8, pp.Ilvl ?? 0)));
                        else
                        {
                            stack.Clear();
                            into.Add(para);
                        }
                    }
                }
                else if (name == "tbl") { stack.Clear(); var t = ReadTable(el); if (t != null) into.Add(t); }
                else if (name == "sdt") { stack.Clear(); ReadBlockSdt(el, into); }
                else if (name == "customXml" || name == "ins" || name == "smartTag") ReadBlocks(el.Elements(), into);
            }
        }

        void AddToList(List<ListState> stack, BlockCollection into, Paragraph para, int numId, int lvl)
        {
            if (stack.Count == 0 || stack[0].NumId != numId)
            {
                stack.Clear();
                var fmt = NumFormat(numId, 0);
                var top = new List { MarkerStyle = fmt.Marker, StartIndex = Math.Max(1, fmt.Start), Margin = new Thickness(0, 0, 0, 0), Padding = new Thickness(36, 0, 0, 0) };
                into.Add(top);
                stack.Add(new ListState { List = top, NumId = numId, Level = 0 });
            }
            while (stack.Count - 1 > lvl) stack.RemoveAt(stack.Count - 1);
            while (stack.Count - 1 < lvl)
            {
                var parent = stack[stack.Count - 1].List;
                var lastItem = parent.ListItems.LastListItem;
                if (lastItem == null) { lastItem = new ListItem(new Paragraph()); parent.ListItems.Add(lastItem); }
                var fmt = NumFormat(numId, stack.Count);
                var nested = new List { MarkerStyle = fmt.Marker, StartIndex = Math.Max(1, fmt.Start), Margin = new Thickness(0), Padding = new Thickness(24, 0, 0, 0) };
                lastItem.Blocks.Add(nested);
                stack.Add(new ListState { List = nested, NumId = numId, Level = stack.Count });
            }
            // list indentation comes from the List itself
            para.Margin = new Thickness(0, 0, para.Margin.Right, 0);
            para.TextIndent = 0;
            stack[0].List.Margin = new Thickness(0, 0, 0, 10 * Fmt.PxPerPt);
            stack[stack.Count - 1].List.ListItems.Add(new ListItem(para));
        }

        IEnumerable<(Paragraph, PP)> ReadParagraph(XElement p)
        {
            var pPr = p.Element(W + "pPr");
            var styleId = pPr?.Element(W + "pStyle")?.Attribute(W + "val")?.Value;
            var (pp, prp) = ParaStyleProps(styleId);
            ApplyPPr(pp, pPr);
            // numbering defined only by the style
            if (pp.NumId == null)
                foreach (var st in StyleChain(styleId ?? _defaultParaStyle))
                {
                    var np = st.Element(W + "pPr")?.Element(W + "numPr");
                    if (np != null) { var nid = Num(np.Element(W + "numId"), "val"); if (nid.HasValue) pp.NumId = (int)nid; }
                }

            var results = new List<(Paragraph, PP)>();
            Paragraph cur = NewPara(pp, prp, styleId);
            results.Add((cur, pp));
            if (_pendingPageBreak) { cur.BreakPageBefore = true; _pendingPageBreak = false; }

            void PageBreak()
            {
                if (!cur.Inlines.Any()) { cur.BreakPageBefore = true; return; }
                var pp2 = pp.Clone(); pp2.NumId = null;
                cur = NewPara(pp2, prp, styleId);
                cur.BreakPageBefore = true;
                results.Add((cur, pp2));
            }
            ReadInlines(p.Elements(), cur.Inlines, prp, ref cur, PageBreak);

            // a trailing page break means the next paragraph starts a page
            var last = results[results.Count - 1].Item1;
            if (results.Count > 1 && !last.Inlines.Any() && last.BreakPageBefore)
            {
                results.RemoveAt(results.Count - 1);
                _pendingPageBreak = true;
            }
            return results;
        }

        Paragraph NewPara(PP pp, RP rp, string styleId)
        {
            var para = new Paragraph();
            para.Tag = MapStyleTag(styleId);
            ApplyRunProps(para, rp, null);
            para.Margin = new Thickness(Math.Max(0, pp.Left ?? 0), (pp.Before ?? 0) * Fmt.PxPerPt, Math.Max(0, pp.Right ?? 0), (pp.After ?? 0) * Fmt.PxPerPt);
            para.TextIndent = pp.First ?? 0;
            if (pp.First.HasValue && pp.First < 0 && (pp.Left ?? 0) < -pp.First) para.TextIndent = -(pp.Left ?? 0);
            para.TextAlignment = pp.Align ?? TextAlignment.Left;
            if (pp.LineExact.HasValue) { para.LineHeight = pp.LineExact.Value * Fmt.PxPerPt; para.LineStackingStrategy = LineStackingStrategy.MaxHeight; }
            else Fmt.SetLineSpacing(para, pp.Line ?? 1);
            if (pp.PageBreak == true) para.BreakPageBefore = true;
            if (pp.BorderColor.HasValue)
            {
                para.BorderBrush = new SolidColorBrush(pp.BorderColor.Value);
                para.BorderThickness = new Thickness(0, 0, 0, pp.BorderPx ?? 1);
                para.Padding = new Thickness(0, 0, 0, 4);
            }
            if (pp.Shade.HasValue) para.Background = new SolidColorBrush(pp.Shade.Value);
            return para;
        }

        void ApplyRunProps(TextElement e, RP rp, RP baseRp)
        {
            if (baseRp == null || rp.Font != baseRp.Font) if (rp.Font != null) e.FontFamily = new FontFamily(rp.Font);
            if (baseRp == null || rp.Size != baseRp.Size) if (rp.Size.HasValue && rp.Size > 0) e.FontSize = rp.Size.Value * Fmt.PxPerPt;
            if (baseRp == null || rp.Bold != baseRp.Bold) e.FontWeight = rp.Bold == true ? FontWeights.Bold : FontWeights.Normal;
            if (baseRp == null || rp.Italic != baseRp.Italic) e.FontStyle = rp.Italic == true ? FontStyles.Italic : FontStyles.Normal;
            if (baseRp == null || rp.Color != baseRp.Color) e.Foreground = new SolidColorBrush(rp.Color ?? Colors.Black);
            if (e is Inline inl)
            {
                var deco = new TextDecorationCollection();
                if (rp.Underline == true) deco.Add(TextDecorations.Underline[0]);
                if (rp.Strike == true) deco.Add(TextDecorations.Strikethrough[0]);
                if (deco.Count > 0) inl.TextDecorations = deco;
                var bg = rp.Highlight ?? rp.Shade;
                if (bg.HasValue) inl.Background = new SolidColorBrush(bg.Value);
                if (rp.VertAlign == "superscript") inl.BaselineAlignment = BaselineAlignment.Superscript;
                else if (rp.VertAlign == "subscript") inl.BaselineAlignment = BaselineAlignment.Subscript;
            }
        }

        RP RunStyleProps(RP paraRp, XElement rPr)
        {
            var rp = paraRp.Clone();
            var rs = rPr?.Element(W + "rStyle")?.Attribute(W + "val")?.Value;
            if (rs != null) foreach (var st in StyleChain(rs)) ApplyRPr(rp, st.Element(W + "rPr"));
            ApplyRPr(rp, rPr);
            return rp;
        }

        void ReadInlines(IEnumerable<XElement> elements, InlineCollection into, RP paraRp, ref Paragraph cur, Action pageBreak)
        {
            foreach (var el in elements)
            {
                if (el.Name.Namespace != W) continue;
                switch (el.Name.LocalName)
                {
                    case "r":
                        {
                            var rp = RunStyleProps(paraRp, el.Element(W + "rPr"));
                            if (rp.Hidden == true) break;
                            var sb = new System.Text.StringBuilder();
                            void FlushText(InlineCollection target)
                            {
                                if (sb.Length == 0) return;
                                var text = rp.Caps == true ? sb.ToString().ToUpper() : sb.ToString();
                                var run = new Run(text);
                                ApplyRunProps(run, rp, paraRp);
                                target.Add(run);
                                sb.Clear();
                            }
                            foreach (var c in el.Elements())
                            {
                                switch (c.Name.LocalName)
                                {
                                    case "t": sb.Append(c.Value); break;
                                    case "delText": sb.Append(c.Value); break;
                                    case "fldChar": FlushText(into); into = FieldChar(c.Attribute(W + "fldCharType")?.Value, into); break;
                                    case "instrText": if (_fields.Count > 0) _fields[_fields.Count - 1].Instr.Append(c.Value); break;
                                    case "footnoteReference":
                                    case "endnoteReference":
                                        FlushText(into);
                                        into.Add(NoteReference(c.Name.LocalName == "endnoteReference", (int)(Num(c, "id") ?? 0), rp));
                                        break;
                                    case "tab": sb.Append('\t'); break;
                                    case "noBreakHyphen": sb.Append('‑'); break;
                                    case "softHyphen": break;
                                    case "sym":
                                        {
                                            var ch = c.Attribute(W + "char")?.Value;
                                            if (ch != null && int.TryParse(ch, NumberStyles.HexNumber, null, out var code))
                                            {
                                                if (code >= 0xF000) code -= 0xF000;
                                                sb.Append(SymbolMap(c.Attribute(W + "font")?.Value, code));
                                            }
                                            break;
                                        }
                                    case "br":
                                    case "cr":
                                        {
                                            var type = c.Attribute(W + "type")?.Value;
                                            if (type == "page")
                                            {
                                                FlushText(into);
                                                pageBreak();
                                                into = cur.Inlines;
                                            }
                                            else
                                            {
                                                FlushText(into);
                                                var lb = new LineBreak();
                                                into.Add(lb);
                                            }
                                            break;
                                        }
                                    case "lastRenderedPageBreak": break;
                                    case "commentReference":
                                        {
                                            FlushText(into);
                                            var cr = CommentReference(c);
                                            if (cr != null) into.Add(cr);
                                            break;
                                        }
                                    case "drawing":
                                    case "pict":
                                    case "object":
                                        {
                                            FlushText(into);
                                            var img = ReadImage(c);
                                            var anchor = c.Descendants(WP + "anchor").FirstOrDefault();
                                            if (img != null && anchor != null && anchor.Element(WP + "wrapNone") == null)
                                            {
                                                var alignH = anchor.Element(WP + "positionH")?.Element(WP + "align")?.Value;
                                                var side = alignH == "right" || alignH == "outside" ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                                                into.Add(new Floater(new BlockUIContainer(img)) { HorizontalAlignment = side, Width = img.Width + 8, Padding = new Thickness(side == HorizontalAlignment.Left ? 0 : 8, 0, side == HorizontalAlignment.Left ? 8 : 0, 4), Tag = "Wrap" });
                                            }
                                            else if (img != null) into.Add(new InlineUIContainer(img) { BaselineAlignment = BaselineAlignment.Baseline });
                                            break;
                                        }
                                }
                            }
                            FlushText(into);
                            break;
                        }
                    case "hyperlink":
                        {
                            var id = el.Attribute(R + "id")?.Value;
                            Uri target = null;
                            if (id != null && _doc.RelationshipExists(id)) target = _doc.GetRelationship(id).TargetUri;
                            if (target != null && target.IsAbsoluteUri)
                            {
                                var h = new Hyperlink { NavigateUri = target };
                                var hc = h.Inlines;
                                ReadInlines(el.Elements(), hc, paraRp, ref cur, pageBreak);
                                if (h.Inlines.Count > 0) into.Add(h);
                            }
                            else if (el.Attribute(W + "anchor") != null)
                            {
                                var h = new Hyperlink { NavigateUri = new Uri("#" + el.Attribute(W + "anchor").Value, UriKind.Relative) };
                                var hc = h.Inlines;
                                ReadInlines(el.Elements(), hc, paraRp, ref cur, pageBreak);
                                if (h.Inlines.Count > 0) into.Add(h);
                            }
                            else ReadInlines(el.Elements(), into, paraRp, ref cur, pageBreak);
                            break;
                        }
                    case "fldSimple":
                        {
                            var span = FieldSpan(el.Attribute(W + "instr")?.Value ?? "");
                            if (span == null) { ReadInlines(el.Elements(), into, paraRp, ref cur, pageBreak); break; }
                            var sc = span.Inlines;
                            ReadInlines(el.Elements(), sc, paraRp, ref cur, pageBreak);
                            into.Add(span);
                            break;
                        }
                    case "ins":
                    case "del":
                    case "moveFrom":
                    case "moveTo":
                        {
                            var rev = new RevInfo { Insert = el.Name.LocalName == "ins" || el.Name.LocalName == "moveTo", Author = el.Attribute(W + "author")?.Value ?? "" };
                            if (DateTime.TryParse(el.Attribute(W + "date")?.Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt)) rev.Date = dt.ToLocalTime();
                            var span = new Span { Tag = rev };
                            var sc = span.Inlines;
                            ReadInlines(el.Elements(), sc, paraRp, ref cur, pageBreak);
                            if (span.Inlines.Count > 0) into.Add(span);
                            break;
                        }
                    case "commentRangeStart":
                    case "bookmarkStart":
                        into = OpenRange(el, into);
                        break;
                    case "commentRangeEnd":
                    case "bookmarkEnd":
                        into = CloseRange(el, into);
                        break;
                    case "smartTag":
                    case "customXml":
                    case "bdo":
                    case "dir":
                        ReadInlines(el.Elements(), into, paraRp, ref cur, pageBreak);
                        break;
                    case "oMathPara":
                    case "oMath":
                        {
                            var text = string.Concat(el.Descendants().Where(d => d.Name.LocalName == "t").Select(t => t.Value));
                            if (text.Length > 0) into.Add(new Run(text) { FontFamily = new FontFamily("Cambria Math"), FontStyle = FontStyles.Italic });
                            break;
                        }
                    case "sdt":
                        ReadInlines(el.Element(W + "sdtContent")?.Elements() ?? Enumerable.Empty<XElement>(), into, paraRp, ref cur, pageBreak);
                        break;
                }
            }
        }

        static string SymbolMap(string font, int code)
        {
            if (font != null && font.StartsWith("Wingdings"))
                return code switch { 0xFC => "✓", 0xFB => "✗", 0x6C => "●", 0xA7 => "▪", 0xD8 => "➢", 0x9F => "•", _ => "•" };
            if (font == "Symbol")
                return code switch { 0xB7 => "•", 0xD3 => "©", 0xD2 => "®", 0xB0 => "°", 0xB1 => "±", 0xAE => "→", 0xAC => "←", _ => ((char)code).ToString() };
            return ((char)code).ToString();
        }

        Image ReadImage(XElement container)
        {
            try
            {
                string rid = container.Descendants(A + "blip").FirstOrDefault()?.Attribute(R + "embed")?.Value
                             ?? container.Descendants(V + "imagedata").FirstOrDefault()?.Attribute(R + "id")?.Value;
                if (rid == null || !_doc.RelationshipExists(rid)) return null;
                var rel = _doc.GetRelationship(rid);
                if (rel.TargetMode != TargetMode.Internal) return null;
                var uri = PackUriHelper.ResolvePartUri(_doc.Uri, rel.TargetUri);
                if (!_pkg.PartExists(uri)) return null;
                var part = _pkg.GetPart(uri);
                byte[] bytes;
                using (var s = part.GetStream(FileMode.Open, FileAccess.Read)) { var ms = new MemoryStream(); s.CopyTo(ms); bytes = ms.ToArray(); }
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.EndInit();
                bmp.Freeze();
                var ext = Path.GetExtension(uri.OriginalString).TrimStart('.').ToLowerInvariant();
                if (ext == "jpg") ext = "jpeg";
                if (ext != "png" && ext != "jpeg" && ext != "gif" && ext != "bmp") { bytes = null; ext = "png"; }
                double w = bmp.Width, h = bmp.Height;
                var extent = container.Descendants(WP + "extent").FirstOrDefault();
                if (extent != null && long.TryParse(extent.Attribute("cx")?.Value, out var cx) && long.TryParse(extent.Attribute("cy")?.Value, out var cy) && cx > 0 && cy > 0)
                { w = cx / 9525.0; h = cy / 9525.0; }
                return new Image { Source = bmp, Width = w, Height = h, Stretch = Stretch.Fill, Tag = new ImageData { Bytes = bytes, Ext = ext } };
            }
            catch { return null; }
        }

        // ---------------- tables ----------------
        Table ReadTable(XElement tbl)
        {
            var t = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 10) };
            var widths = tbl.Element(W + "tblGrid")?.Elements(W + "gridCol").Select(g => (Num(g, "w") ?? 1440) / 15).ToList() ?? new List<double>();
            foreach (var w in widths) t.Columns.Add(new TableColumn { Width = new GridLength(Math.Max(10, w)) });

            var tblPr = tbl.Element(W + "tblPr");
            bool borders = HasBorders(tblPr?.Element(W + "tblBorders"));
            var tstyle = tblPr?.Element(W + "tblStyle")?.Attribute(W + "val")?.Value;
            if (tblPr?.Element(W + "tblBorders") == null && tstyle != null)
                foreach (var st in StyleChain(tstyle))
                    if (st.Element(W + "tblPr")?.Element(W + "tblBorders") is XElement sb) borders = HasBorders(sb);
            var jc = tblPr?.Element(W + "jc")?.Attribute(W + "val")?.Value;

            var group = new TableRowGroup();
            t.RowGroups.Add(group);
            foreach (var tr in tbl.Elements(W + "tr"))
            {
                var row = new TableRow();
                foreach (var tc in tr.Elements(W + "tc"))
                {
                    var tcPr = tc.Element(W + "tcPr");
                    var cell = new TableCell { Padding = new Thickness(7, 2, 7, 2) };
                    var span = Num(tcPr?.Element(W + "gridSpan"), "val");
                    if (span.HasValue && span > 1) cell.ColumnSpan = (int)span;
                    bool cellBorders = borders;
                    var tcb = tcPr?.Element(W + "tcBorders");
                    if (tcb != null) cellBorders = HasBorders(tcb) || borders && !AllNil(tcb);
                    if (cellBorders) { cell.BorderBrush = Brushes.Black; cell.BorderThickness = new Thickness(0.5); }
                    if (tcb != null) ApplyCellBorders(cell, tcb, borders);
                    ApplyCellMargins(cell, tcPr?.Element(W + "tcMar"));
                    _cellCol[cell] = tc.ElementsBeforeSelf(W + "tc").Sum(x => (int)(Num(x.Element(W + "tcPr")?.Element(W + "gridSpan"), "val") ?? 1));
                    var fill = ParseHex(tcPr?.Element(W + "shd")?.Attribute(W + "fill")?.Value);
                    if (fill.HasValue) cell.Background = new SolidColorBrush(fill.Value);
                    var vm = tcPr?.Element(W + "vMerge");
                    bool continueMerge = vm != null && (vm.Attribute(W + "val")?.Value ?? "continue") == "continue";
                    if (continueMerge && MergeIntoAbove(group, row, cell)) continue;
                    if (!continueMerge) ReadBlocks(tc.Elements(), cell.Blocks);
                    if (cell.Blocks.Count == 0) cell.Blocks.Add(new Paragraph());
                    foreach (var p in Fmt.AllParagraphs(cell.Blocks)) if (p.Margin.Bottom > 6) p.Margin = new Thickness(p.Margin.Left, p.Margin.Top, p.Margin.Right, 0);
                    row.Cells.Add(cell);
                }
                if (tr.Element(W + "trPr")?.Element(W + "tblHeader") is XElement th && On(th.Attribute(W + "val")?.Value)) row.Tag = "Header";
                if (row.Cells.Count > 0 || group.Rows.Count > 0) group.Rows.Add(row);
            }
            if (group.Rows.Count == 0) return null;
            var ind = Num(tblPr?.Element(W + "tblInd"), "w");
            if (ind.HasValue && ind > 0) t.Margin = new Thickness(ind.Value / 15, 0, 0, 10);
            return t;
        }

        static bool HasBorders(XElement b)
        {
            if (b == null) return false;
            return b.Elements().Any(e => { var v = e.Attribute(W + "val")?.Value; return v != null && v != "nil" && v != "none"; });
        }
        static bool AllNil(XElement b) => b.Elements().All(e => { var v = e.Attribute(W + "val")?.Value; return v == "nil" || v == "none"; });

        // ---------------- section ----------------
        void ReadSection(XElement sect, PageSettings page)
        {
            var sz = sect.Element(W + "pgSz");
            if (sz != null)
            {
                double w = (Num(sz, "w") ?? 12240) / 1440, h = (Num(sz, "h") ?? 15840) / 1440;
                page.Landscape = sz.Attribute(W + "orient")?.Value == "landscape" || w > h;
                page.Paper = PageSettings.ClosestPaper(w, h);
            }
            ReadSectionExtras(sect, page);
            var mar = sect.Element(W + "pgMar");
            if (mar != null)
            {
                page.MarginTop = Math.Abs(Num(mar, "top") ?? 1440) / 1440;
                page.MarginBottom = Math.Abs(Num(mar, "bottom") ?? 1440) / 1440;
                page.MarginLeft = (Num(mar, "left") ?? 1440) / 1440;
                page.MarginRight = (Num(mar, "right") ?? 1440) / 1440;
            }
            try
            {
                var hdr = sect.Elements(W + "headerReference").FirstOrDefault(e => e.Attribute(W + "type")?.Value == "default");
                if (hdr != null)
                {
                    var x = ReadXml(_pkg.GetPart(PackUriHelper.ResolvePartUri(_doc.Uri, _doc.GetRelationship(hdr.Attribute(R + "id").Value).TargetUri)));
                    var (l, c, r) = ReadSlots(x);
                    page.HeaderLeft = l; page.HeaderText = c; page.HeaderRight = r;
                    ReadWatermark(x, page);
                }
                var ftr = sect.Elements(W + "footerReference").FirstOrDefault(e => e.Attribute(W + "type")?.Value == "default");
                if (ftr != null)
                {
                    var x = ReadXml(_pkg.GetPart(PackUriHelper.ResolvePartUri(_doc.Uri, _doc.GetRelationship(ftr.Attribute(R + "id").Value).TargetUri)));
                    var (l, c, r) = ReadSlots(x);
                    page.FooterLeft = l; page.FooterCenter = c; page.FooterRight = r;
                }
            }
            catch { }
        }
    }
}

namespace ValcoWord
{
    static class XExt
    {
        public static System.Xml.Linq.XElement AddAndReturn(this System.Xml.Linq.XElement e, params object[] content) { e.Add(content); return e; }
    }
}
