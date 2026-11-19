using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ValcoWord
{
    public static class DocFormats
    {
        public const string OpenFilter =
            "All Documents|*.docx;*.docm;*.dotx;*.dotm;*.doc;*.odt;*.rtf;*.txt;*.text;*.md;*.log;*.htm;*.html|" +
            "Word Document (*.docx)|*.docx;*.docm;*.dotx;*.dotm|Word 97-2003 Document (*.doc)|*.doc|OpenDocument Text (*.odt)|*.odt|Rich Text Format (*.rtf)|*.rtf|" +
            "Text Document (*.txt)|*.txt;*.text;*.md;*.log|Web Page (*.htm, *.html)|*.htm;*.html|All Files (*.*)|*.*";
        public const string SaveFilter = "Word Document (*.docx)|*.docx|Rich Text Format (*.rtf)|*.rtf|Text Document (*.txt)|*.txt|Word Template (*.dotx)|*.dotx|OpenDocument Text (*.odt)|*.odt";

        public static bool CanSave(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".docx" || ext == ".rtf" || ext == ".txt" || ext == ".odt" || ext == ".dotx";
        }

        public static LoadedDocument Load(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            byte[] head = new byte[8];
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) fs.Read(head, 0, 8);
            bool isZip = head[0] == 'P' && head[1] == 'K';
            bool isRtf = head[0] == '{' && head[1] == '\\' && head[2] == 'r' && head[3] == 't' && head[4] == 'f';
            bool isOle = head[0] == 0xD0 && head[1] == 0xCF && head[2] == 0x11 && head[3] == 0xE0;

            if (isZip && ext == ".odt") return OdtFormat.Load(path);
            if (isZip) { try { return DocxReader.Load(path); } catch when (IsOdt(path)) { return OdtFormat.Load(path); } }
            if (isRtf) return LoadRtf(path);
            if (isOle) return new LoadedDocument { Document = TextDocument(BinaryDoc.ExtractText(path), false), Page = Settings.Current.Page.Clone() };
            if (ext == ".htm" || ext == ".html") return LoadHtml(path);
            return new LoadedDocument { Document = TextDocument(ReadText(path), true), Page = Settings.Current.Page.Clone() };
        }

        static string ReadText(string path)
        {
            var bytes = File.ReadAllBytes(path);
            // UTF-8 unless it's not valid, then the Windows code page
            try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿'); }
            catch
            {
                using var r = new StreamReader(new MemoryStream(bytes), Encoding.Latin1, true);
                return r.ReadToEnd();
            }
        }

        static LoadedDocument LoadRtf(string path)
        {
            var fd = new FlowDocument();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                new TextRange(fd.ContentStart, fd.ContentEnd).Load(fs, DataFormats.Rtf);
            return new LoadedDocument { Document = fd, Page = Settings.Current.Page.Clone() };
        }

        static LoadedDocument LoadHtml(string path)
        {
            var html = ReadText(path);
            var text = System.Text.RegularExpressions.Regex.Replace(html, @"<(script|style)[\s\S]*?</\1>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<(br|/p|/div|/h\d|/li|/tr)[^>]*>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<[^>]+>", "");
            text = WebUtility.HtmlDecode(text);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n");
            return new LoadedDocument { Document = TextDocument(text.Trim(), false), Page = Settings.Current.Page.Clone() };
        }

        public static FlowDocument TextDocument(string text, bool plain)
        {
            var fd = new FlowDocument();
            var style = StyleDef.Get(plain ? "NoSpacing" : "Normal");
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (var line in lines)
            {
                var p = new Paragraph();
                style.Apply(p, false);
                if (plain) { p.FontFamily = new FontFamily("Consolas"); p.FontSize = 10 * Fmt.PxPerPt; }
                if (line.Length > 0) p.Inlines.Add(new Run(line));
                fd.Blocks.Add(p);
            }
            if (fd.Blocks.Count == 0) fd.Blocks.Add(new Paragraph());
            return fd;
        }

        public static void Save(string path, FlowDocument doc, PageSettings page, string title, string author, DocExtras extras = null)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".rtf":
                    {
                        var tmp = path + ".vwtmp";
                        using (var fs = new FileStream(tmp, FileMode.Create))
                            new TextRange(doc.ContentStart, doc.ContentEnd).Save(fs, DataFormats.Rtf);
                        Replace(tmp, path);
                        break;
                    }
                case ".txt":
                    {
                        var tmp = path + ".vwtmp";
                        File.WriteAllText(tmp, PlainText(doc), new UTF8Encoding(false));
                        Replace(tmp, path);
                        break;
                    }
                case ".odt":
                    OdtFormat.Save(path, doc, page, title, author);
                    break;
                default:
                    DocxWriter.Save(doc, page, title, author, path, extras);
                    break;
            }
        }

        static bool IsOdt(string path)
        {
            try { using var z = System.IO.Compression.ZipFile.OpenRead(path); return z.GetEntry("mimetype") != null && z.GetEntry("content.xml") != null; }
            catch { return false; }
        }

        static void Replace(string tmp, string path)
        {
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        public static string PlainText(FlowDocument doc)
        {
            var t = new TextRange(doc.ContentStart, doc.ContentEnd).Text;
            if (t.EndsWith("\r\n")) t = t.Substring(0, t.Length - 2);
            return t;
        }

        // ---------------------------------------------------------------- HTML export
        public static string ToHtml(FlowDocument doc, PageSettings page, string title)
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n<title>").Append(WebUtility.HtmlEncode(title ?? "Document")).Append("</title>\n");
            sb.Append("<style>body{background:#e8eef5;margin:0;padding:24px}main{background:#fff;max-width:")
              .Append((int)page.BodyWidthPx).Append("px;margin:0 auto;padding:")
              .Append((int)(page.MarginTop * 96)).Append("px ").Append((int)(page.MarginRight * 96)).Append("px;box-shadow:0 2px 8px rgba(0,0,0,.25)}p{margin:0}table{border-collapse:collapse}td{vertical-align:top}</style>\n</head>\n<body>\n<main>\n");
            Blocks(doc.Blocks, sb);
            sb.Append("</main>\n</body>\n</html>\n");
            return sb.ToString();
        }

        static string Css(Color c) => "#" + Fmt.Hex(c);
        static string Num(double v) => (double.IsNaN(v) ? 0 : v).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        static void Blocks(IEnumerable<Block> blocks, StringBuilder sb)
        {
            foreach (var b in blocks)
            {
                switch (b)
                {
                    case Paragraph p:
                        {
                            var tag = p.Tag as string;
                            string el = tag switch { "Title" => "h1", "Heading1" => "h1", "Heading2" => "h2", "Heading3" => "h3", "Quote" => "blockquote", _ => "p" };
                            sb.Append('<').Append(el).Append(" style=\"");
                            sb.Append("margin:").Append(Num(p.Margin.Top)).Append("px ").Append(Num(p.Margin.Right)).Append("px ").Append(Num(p.Margin.Bottom)).Append("px ").Append(Num(p.Margin.Left)).Append("px;");
                            if (p.TextAlignment != TextAlignment.Left) sb.Append("text-align:").Append(p.TextAlignment == TextAlignment.Justify ? "justify" : p.TextAlignment.ToString().ToLowerInvariant()).Append(';');
                            if (Math.Abs(p.TextIndent) > 0.5) sb.Append("text-indent:").Append(Num(p.TextIndent)).Append("px;");
                            sb.Append("font-family:'").Append(p.FontFamily.Source).Append("';font-size:").Append(Num(p.FontSize * 0.75)).Append("pt;");
                            if (p.FontWeight.ToOpenTypeWeight() >= 600) sb.Append("font-weight:bold;"); else if (el.StartsWith("h")) sb.Append("font-weight:normal;");
                            if (p.FontStyle == FontStyles.Italic) sb.Append("font-style:italic;");
                            if (p.Foreground is SolidColorBrush fg) sb.Append("color:").Append(Css(fg.Color)).Append(';');
                            if (!double.IsNaN(p.LineHeight)) sb.Append("line-height:").Append(Num(p.LineHeight)).Append("px;");
                            if (p.Background is SolidColorBrush bg) sb.Append("background:").Append(Css(bg.Color)).Append(';');
                            if (p.BorderThickness.Bottom > 0 && p.BorderBrush is SolidColorBrush bb) sb.Append("border-bottom:").Append(Num(p.BorderThickness.Bottom)).Append("px solid ").Append(Css(bb.Color)).Append(';');
                            if (p.BreakPageBefore) sb.Append("page-break-before:always;");
                            sb.Append("\">");
                            Inlines(p.Inlines, sb);
                            if (!p.Inlines.Any()) sb.Append("&nbsp;");
                            sb.Append("</").Append(el).Append(">\n");
                            break;
                        }
                    case List l:
                        {
                            bool ol = !Fmt.IsBullet(l.MarkerStyle);
                            string type = l.MarkerStyle switch
                            {
                                TextMarkerStyle.LowerLatin => "lower-alpha", TextMarkerStyle.UpperLatin => "upper-alpha", TextMarkerStyle.LowerRoman => "lower-roman",
                                TextMarkerStyle.UpperRoman => "upper-roman", TextMarkerStyle.Circle => "circle", TextMarkerStyle.Square => "square", TextMarkerStyle.Box => "square",
                                TextMarkerStyle.Decimal => "decimal", _ => "disc"
                            };
                            sb.Append(ol ? "<ol" : "<ul").Append(" style=\"list-style-type:").Append(type).Append("\"");
                            if (ol && l.StartIndex > 1) sb.Append(" start=\"").Append(l.StartIndex).Append('"');
                            sb.Append(">\n");
                            foreach (var li in l.ListItems) { sb.Append("<li>"); Blocks(li.Blocks, sb); sb.Append("</li>\n"); }
                            sb.Append(ol ? "</ol>\n" : "</ul>\n");
                            break;
                        }
                    case Table t:
                        {
                            sb.Append("<table>\n");
                            foreach (var g in t.RowGroups)
                                foreach (var r in g.Rows)
                                {
                                    sb.Append("<tr>");
                                    foreach (var c in r.Cells)
                                    {
                                        sb.Append("<td");
                                        if (c.ColumnSpan > 1) sb.Append(" colspan=\"").Append(c.ColumnSpan).Append('"');
                                        sb.Append(" style=\"padding:2px 7px;");
                                        if (c.BorderThickness.Left > 0) sb.Append("border:1px solid #000;");
                                        if (c.Background is SolidColorBrush cb) sb.Append("background:").Append(Css(cb.Color)).Append(';');
                                        sb.Append("\">");
                                        Blocks(c.Blocks, sb);
                                        sb.Append("</td>");
                                    }
                                    sb.Append("</tr>\n");
                                }
                            sb.Append("</table>\n");
                            break;
                        }
                    case Section s: Blocks(s.Blocks, sb); break;
                    case BlockUIContainer bc when bc.Child is Image img: sb.Append("<p>").Append(ImgTag(img)).Append("</p>\n"); break;
                }
            }
        }

        static void Inlines(InlineCollection inlines, StringBuilder sb)
        {
            foreach (var i in inlines)
            {
                switch (i)
                {
                    case Run r:
                        {
                            var css = new StringBuilder();
                            var par = Fmt.Ancestor<Paragraph>(r);
                            if (par != null)
                            {
                                if (r.FontFamily.Source != par.FontFamily.Source) css.Append("font-family:'").Append(r.FontFamily.Source).Append("';");
                                if (Math.Abs(r.FontSize - par.FontSize) > 0.1) css.Append("font-size:").Append(Num(r.FontSize * 0.75)).Append("pt;");
                                if (r.FontWeight != par.FontWeight) css.Append("font-weight:").Append(r.FontWeight.ToOpenTypeWeight()).Append(';');
                                if (r.FontStyle != par.FontStyle) css.Append("font-style:").Append(r.FontStyle == FontStyles.Italic ? "italic" : "normal").Append(';');
                                if (r.Foreground is SolidColorBrush f && !(par.Foreground is SolidColorBrush pf && pf.Color == f.Color)) css.Append("color:").Append(Css(f.Color)).Append(';');
                            }
                            var deco = Fmt.EffectiveDecorations(r);
                            var d = new List<string>();
                            if (Fmt.HasDecoration(deco, TextDecorationLocation.Underline) && !(r.Parent is Hyperlink)) d.Add("underline");
                            if (Fmt.HasDecoration(deco, TextDecorationLocation.Strikethrough)) d.Add("line-through");
                            if (d.Count > 0) css.Append("text-decoration:").Append(string.Join(" ", d)).Append(';');
                            var bg = Fmt.BrushColor(Fmt.EffectiveBackground(r));
                            if (bg.HasValue) css.Append("background:").Append(Css(bg.Value)).Append(';');
                            var ba = Fmt.EffectiveBaseline(r);
                            string text = WebUtility.HtmlEncode(r.Text).Replace("\t", "&emsp;");
                            if (ba == BaselineAlignment.Superscript) text = "<sup>" + text + "</sup>";
                            else if (ba == BaselineAlignment.Subscript) text = "<sub>" + text + "</sub>";
                            if (css.Length > 0) sb.Append("<span style=\"").Append(css).Append("\">").Append(text).Append("</span>");
                            else sb.Append(text);
                            break;
                        }
                    case LineBreak: sb.Append("<br>"); break;
                    case Hyperlink h:
                        sb.Append("<a href=\"").Append(WebUtility.HtmlEncode(h.NavigateUri?.ToString() ?? "#")).Append("\">");
                        Inlines(h.Inlines, sb);
                        sb.Append("</a>");
                        break;
                    case Span s: Inlines(s.Inlines, sb); break;
                    case InlineUIContainer c when c.Child is Image img: sb.Append(ImgTag(img)); break;
                }
            }
        }

        static string ImgTag(Image img)
        {
            if (!(img.Source is BitmapSource src)) return "";
            byte[] bytes; string mime;
            if (img.Tag is ImageData d && d.Bytes != null) { bytes = d.Bytes; mime = d.Ext == "jpeg" ? "image/jpeg" : "image/" + d.Ext; }
            else
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(src));
                using var ms = new MemoryStream();
                enc.Save(ms);
                bytes = ms.ToArray(); mime = "image/png";
            }
            double w = double.IsNaN(img.Width) ? src.Width : img.Width, h = double.IsNaN(img.Height) ? src.Height : img.Height;
            return $"<img src=\"data:{mime};base64,{Convert.ToBase64String(bytes)}\" width=\"{Num(w)}\" height=\"{Num(h)}\" alt=\"\">";
        }
    }

    /// <summary>Reads the text of a Word 97-2003 (.doc) file from its OLE compound file and piece table.</summary>
    public static class BinaryDoc
    {
        public static string ExtractText(string path)
        {
            var data = File.ReadAllBytes(path);
            var cfb = new CompoundFile(data);
            var word = cfb.Stream("WordDocument") ?? throw new InvalidDataException("This file isn't a Word document.");
            ushort flags = BitConverter.ToUInt16(word, 0x0A);
            if ((flags & 0x0100) != 0) throw new InvalidDataException("This document is password-protected.");
            var table = cfb.Stream((flags & 0x0200) != 0 ? "1Table" : "0Table") ?? throw new InvalidDataException("This Word document is damaged.");

            // FibRgFcLcb97: fcClx / lcbClx are entries 33 (0-based) in the fc/lcb array
            int csw = BitConverter.ToUInt16(word, 32);
            int rgLwOff = 34 + csw * 2;
            int cslw = BitConverter.ToUInt16(word, rgLwOff);
            int cbRgFcLcbOff = rgLwOff + 2 + cslw * 4;
            int fcLcbStart = cbRgFcLcbOff + 2;
            int fcClx = BitConverter.ToInt32(word, fcLcbStart + 33 * 8);
            int lcbClx = BitConverter.ToInt32(word, fcLcbStart + 33 * 8 + 4);
            int ccpText = BitConverter.ToInt32(word, rgLwOff + 2 + 3 * 4);

            int pos = fcClx;
            while (pos < fcClx + lcbClx && table[pos] == 0x01)
            {
                int cb = BitConverter.ToUInt16(table, pos + 1);
                pos += 3 + cb;
            }
            if (table[pos] != 0x02) throw new InvalidDataException("This Word document is damaged.");
            int lcb = BitConverter.ToInt32(table, pos + 1);
            int plc = pos + 5;
            int n = (lcb - 4) / 12;
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                int cpStart = BitConverter.ToInt32(table, plc + i * 4);
                int cpEnd = BitConverter.ToInt32(table, plc + (i + 1) * 4);
                if (cpStart >= ccpText) break;
                cpEnd = Math.Min(cpEnd, ccpText);
                int pcdOff = plc + (n + 1) * 4 + i * 8;
                uint fc = BitConverter.ToUInt32(table, pcdOff + 2);
                bool compressed = (fc & 0x40000000) != 0;
                fc &= 0x3FFFFFFF;
                int count = cpEnd - cpStart;
                if (compressed)
                {
                    int start = (int)(fc / 2);
                    for (int k = 0; k < count && start + k < word.Length; k++) sb.Append(Cp1252(word[start + k]));
                }
                else
                {
                    int start = (int)fc;
                    for (int k = 0; k < count && start + k * 2 + 1 < word.Length; k++) sb.Append((char)BitConverter.ToUInt16(word, start + k * 2));
                }
            }
            // clean Word's control characters: fields, cell marks, page breaks
            var outp = new StringBuilder();
            int fieldDepth = 0; var skipping = new Stack<bool>();
            foreach (var ch in sb.ToString())
            {
                switch (ch)
                {
                    case '\x13': skipping.Push(true); fieldDepth++; continue;      // field begin: skip instructions
                    case '\x14': if (skipping.Count > 0) { skipping.Pop(); skipping.Push(false); } continue; // separator: show result
                    case '\x15': if (skipping.Count > 0) skipping.Pop(); fieldDepth = Math.Max(0, fieldDepth - 1); continue;
                }
                if (skipping.Count > 0 && skipping.Peek()) continue;
                switch (ch)
                {
                    case '\r': outp.Append('\n'); break;
                    case '\x07': outp.Append('\t'); break; // cell / row end
                    case '\x0B': outp.Append('\n'); break;
                    case '\x0C': outp.Append('\n'); break;
                    case '\x1E': outp.Append('-'); break;
                    case '\x1F': break;
                    case '\x01': case '\x08': break; // pictures / drawn objects
                    default: if (ch >= ' ' || ch == '\t') outp.Append(ch); break;
                }
            }
            return outp.ToString().TrimEnd('\n');
        }

        static char Cp1252(byte b)
        {
            if (b < 0x80 || b >= 0xA0) return (char)b;
            const string map = "€\u0081‚ƒ„…†‡ˆ‰Š‹Œ\u008DŽ\u008F\u0090‘’“”•–—˜™š›œ\u009DžŸ";
            return map[b - 0x80];
        }

        /// <summary>Minimal reader for OLE2 compound files.</summary>
        class CompoundFile
        {
            readonly byte[] _d;
            readonly int _sec, _mini;
            readonly List<int> _fat = new List<int>(), _miniFat = new List<int>();
            readonly List<(string Name, int Type, int Start, long Size)> _dir = new List<(string, int, int, long)>();
            readonly byte[] _miniStream;
            readonly int _cutoff;

            public CompoundFile(byte[] d)
            {
                _d = d;
                _sec = 1 << BitConverter.ToUInt16(d, 30);
                _mini = 1 << BitConverter.ToUInt16(d, 32);
                int nFat = BitConverter.ToInt32(d, 44);
                int dirStart = BitConverter.ToInt32(d, 48);
                _cutoff = BitConverter.ToInt32(d, 56);
                int miniFatStart = BitConverter.ToInt32(d, 60);
                int difStart = BitConverter.ToInt32(d, 68);
                var fatSectors = new List<int>();
                for (int i = 0; i < 109 && fatSectors.Count < nFat; i++) fatSectors.Add(BitConverter.ToInt32(d, 76 + i * 4));
                int dif = difStart;
                while (fatSectors.Count < nFat && dif >= 0 && dif < 0x7FFFFFF0)
                {
                    int off = Off(dif);
                    for (int i = 0; i < _sec / 4 - 1 && fatSectors.Count < nFat; i++) fatSectors.Add(BitConverter.ToInt32(d, off + i * 4));
                    dif = BitConverter.ToInt32(d, off + _sec - 4);
                }
                foreach (var s in fatSectors) { int off = Off(s); for (int i = 0; i < _sec / 4; i++) _fat.Add(BitConverter.ToInt32(d, off + i * 4)); }
                var dirBytes = Chain(dirStart, -1);
                for (int i = 0; i + 128 <= dirBytes.Length; i += 128)
                {
                    int nameLen = BitConverter.ToUInt16(dirBytes, i + 64);
                    string name = nameLen > 2 ? Encoding.Unicode.GetString(dirBytes, i, nameLen - 2) : "";
                    _dir.Add((name, dirBytes[i + 66], BitConverter.ToInt32(dirBytes, i + 116), BitConverter.ToUInt32(dirBytes, i + 120)));
                }
                var mf = miniFatStart >= 0 && miniFatStart < 0x7FFFFFF0 ? Chain(miniFatStart, -1) : new byte[0];
                for (int i = 0; i + 4 <= mf.Length; i += 4) _miniFat.Add(BitConverter.ToInt32(mf, i));
                var root = _dir.FirstOrDefault(x => x.Type == 5);
                _miniStream = root.Start >= 0 && root.Name != null ? Chain(root.Start, root.Size) : new byte[0];
            }

            int Off(int sector) => (sector + 1) * _sec;

            byte[] Chain(int start, long size)
            {
                var ms = new MemoryStream();
                int s = start, guard = 0;
                while (s >= 0 && s < _fat.Count + 1 && s < 0x7FFFFFF0 && guard++ < 1_000_000)
                {
                    int off = Off(s);
                    if (off + _sec > _d.Length) { ms.Write(_d, off, Math.Max(0, _d.Length - off)); break; }
                    ms.Write(_d, off, _sec);
                    if (s >= _fat.Count) break;
                    s = _fat[s];
                }
                var a = ms.ToArray();
                if (size >= 0 && a.Length > size) Array.Resize(ref a, (int)size);
                return a;
            }

            byte[] MiniChain(int start, long size)
            {
                var ms = new MemoryStream();
                int s = start, guard = 0;
                while (s >= 0 && s < 0x7FFFFFF0 && guard++ < 1_000_000)
                {
                    int off = s * _mini;
                    if (off + _mini > _miniStream.Length) break;
                    ms.Write(_miniStream, off, _mini);
                    if (s >= _miniFat.Count) break;
                    s = _miniFat[s];
                }
                var a = ms.ToArray();
                if (a.Length > size) Array.Resize(ref a, (int)size);
                return a;
            }

            public byte[] Stream(string name)
            {
                var e = _dir.FirstOrDefault(x => x.Type == 2 && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (e.Name == null) return null;
                return e.Size < _cutoff ? MiniChain(e.Start, e.Size) : Chain(e.Start, e.Size);
            }
        }
    }
}
