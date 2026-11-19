using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ValcoWord
{
    /// <summary>A paragraph style (Word 2007 "Quick Styles").</summary>
    public class StyleDef
    {
        public string Id, Name, Font;
        public double SizePt;
        public bool Bold, Italic;
        public Color Color = Colors.Black;
        public double BeforePt, AfterPt = 10, Line = 1.15, IndentIn;
        public Color? BorderBottom, Background;
        public bool UseDefaultFont;

        public static List<StyleDef> All = new List<StyleDef>();
        public static StyleDef Get(string id) => All.FirstOrDefault(s => s.Id == id) ?? All[0];

        public TextAlignment Align = TextAlignment.Left;
        public static string ThemeName = "Office", StyleSetName = "Word 2007";

        public class Theme
        {
            public string Name, Major, Minor; public Color Dark2, Accent1, Accent2, Accent3, Accent4, Accent5, Accent6;
        }

        static Theme T(string n, string major, string minor, string dk2, params string[] acc) => new Theme
        {
            Name = n, Major = major, Minor = minor, Dark2 = C("#" + dk2), Accent1 = C("#" + acc[0]), Accent2 = C("#" + acc[1]), Accent3 = C("#" + acc[2]),
            Accent4 = C("#" + acc[3]), Accent5 = C("#" + acc[4]), Accent6 = C("#" + acc[5])
        };

        public static readonly List<Theme> Themes = new List<Theme>
        {
            T("Office", "Cambria", "Calibri", "1F497D", "4F81BD", "C0504D", "9BBB59", "8064A2", "4BACC6", "F79646"),
            T("Apex", "Lucida Sans", "Book Antiqua", "69676D", "CEB966", "9CB084", "6BB1C9", "6585CF", "7E6BC9", "A379BB"),
            T("Aspect", "Verdana", "Verdana", "323232", "F07F09", "9F2936", "1B587C", "4E8542", "604878", "C19859"),
            T("Civic", "Georgia", "Georgia", "646B86", "D16349", "CCB400", "8CADAE", "8C7B70", "8FB08C", "D19049"),
            T("Concourse", "Lucida Sans Unicode", "Lucida Sans Unicode", "464646", "2DA2BF", "DA1F28", "EB641B", "39639D", "474B78", "7D3C4A"),
            T("Equity", "Franklin Gothic Book", "Perpetua", "696464", "D34817", "9B2D1F", "A28E6A", "956251", "918485", "855D5D"),
            T("Flow", "Calibri", "Constantia", "04617B", "0F6FC6", "009DD9", "0BD0D9", "10CF9B", "7CCA62", "A5C249"),
            T("Foundry", "Rockwell", "Rockwell", "676A55", "72A376", "B0CCB0", "A8CDD7", "C0BEAF", "CEC597", "E8B7B7"),
            T("Median", "Tw Cen MT", "Tw Cen MT", "775F55", "94B6D2", "DD8047", "A5AB81", "D8B25C", "7BA79D", "968C8C"),
            T("Metro", "Consolas", "Corbel", "4E5B6F", "7FD13B", "EA157A", "FEB80A", "00ADDC", "738AC8", "1AB39F"),
            T("Module", "Corbel", "Corbel", "5A6378", "F0AD00", "60B5CC", "E66C7D", "6BB76D", "E88651", "C64847"),
            T("Opulent", "Trebuchet MS", "Trebuchet MS", "B13F9A", "B83D68", "AC66BB", "DE6C36", "F9B639", "CF6DA4", "FA8D3D"),
            T("Oriel", "Century Schoolbook", "Century Schoolbook", "575F6D", "FE8637", "7598D9", "B32C16", "F5CD2D", "AEBAD5", "777C84"),
            T("Origin", "Bookman Old Style", "Gill Sans MT", "464653", "727CA3", "9FB8CD", "D2DA7A", "FADA7A", "B88472", "8E736A"),
            T("Paper", "Constantia", "Constantia", "444D26", "A5B592", "F3A447", "E7BC29", "D092A7", "9C85C0", "809EC2"),
            T("Solstice", "Gill Sans MT", "Gill Sans MT", "4F271C", "3891A7", "FEB80A", "C32D2E", "84AA33", "964305", "475A8D"),
            T("Technic", "Franklin Gothic Book", "Arial", "3B3B3B", "6EA0B0", "CCAF0A", "8D89A4", "748560", "9E9273", "7E848D"),
            T("Trek", "Franklin Gothic Medium", "Franklin Gothic Book", "4E3B30", "F0A22E", "A5644E", "B58B80", "C3986D", "A19574", "C17529"),
            T("Urban", "Trebuchet MS", "Georgia", "424456", "53548A", "438086", "A04DA3", "C4652D", "8B5D3D", "5C92B5"),
            T("Verve", "Century Gothic", "Century Gothic", "666666", "FF388C", "E40059", "9C007F", "68007F", "005BD3", "00349E"),
        };

        public static readonly string[] StyleSets = { "Word 2007", "Word 2003", "Distinctive", "Elegant", "Fancy", "Formal", "Manuscript", "Modern", "Simple", "Traditional" };

        public static Theme CurrentTheme => Themes.FirstOrDefault(t => t.Name == ThemeName) ?? Themes[0];

        static Color Darker(Color c, double f) => Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));

        public static void Init() => Init(ThemeName, StyleSetName);

        public static void Init(string theme, string styleSet)
        {
            ThemeName = theme ?? "Office"; StyleSetName = styleSet ?? "Word 2007";
            var s = Settings.Current;
            var th = CurrentTheme;
            // the default document font follows the theme unless the user picked their own in Options
            string body = th.Name == "Office" ? s.DefaultFont : th.Minor;
            string head = th.Major;
            var a1 = th.Accent1; var a1d = Darker(a1, 0.75);
            All = new List<StyleDef>
            {
                new StyleDef { Id = "Normal", Name = "Normal", Font = body, SizePt = s.DefaultSizePt },
                new StyleDef { Id = "NoSpacing", Name = "No Spacing", Font = body, SizePt = s.DefaultSizePt, AfterPt = 0, Line = 1 },
                new StyleDef { Id = "Title", Name = "Title", Font = head, SizePt = 26, Color = th.Dark2, AfterPt = 15, Line = 1, BorderBottom = a1 },
                new StyleDef { Id = "Subtitle", Name = "Subtitle", Font = head, SizePt = 12, Italic = true, Color = a1, AfterPt = 10 },
                new StyleDef { Id = "Heading1", Name = "Heading 1", Font = head, SizePt = 14, Bold = true, Color = a1d, BeforePt = 24, AfterPt = 0 },
                new StyleDef { Id = "Heading2", Name = "Heading 2", Font = head, SizePt = 13, Bold = true, Color = a1, BeforePt = 10, AfterPt = 0 },
                new StyleDef { Id = "Heading3", Name = "Heading 3", Font = head, SizePt = 11, Bold = true, Color = a1, BeforePt = 10, AfterPt = 0 },
                new StyleDef { Id = "Quote", Name = "Quote", Font = body, SizePt = s.DefaultSizePt, Italic = true, Color = C("#404040"), IndentIn = 0.5 },
                new StyleDef { Id = "IntenseQuote", Name = "Intense Quote", Font = body, SizePt = s.DefaultSizePt, Bold = true, Italic = true, Color = a1, IndentIn = 0.6, BeforePt = 10, AfterPt = 14, BorderBottom = a1 },
                new StyleDef { Id = "Caption", Name = "Caption", Font = body, SizePt = 9, Bold = true, Color = a1, AfterPt = 10, Line = 1 },
                new StyleDef { Id = "ListParagraph", Name = "List Paragraph", Font = body, SizePt = s.DefaultSizePt, IndentIn = 0.5 },
                new StyleDef { Id = "Code", Name = "Code", Font = "Consolas", SizePt = 10, AfterPt = 0, Line = 1, Background = C("#F2F2F2") },
            };
            StyleDef G(string id) => All.First(x => x.Id == id);
            switch (StyleSetName)
            {
                case "Word 2003":
                    foreach (var x in All.Where(x => x.Id != "Code")) { x.Font = x.Id.StartsWith("Heading") || x.Id == "Title" ? "Arial" : "Times New Roman"; x.Line = 1; x.AfterPt = x.Id.StartsWith("Heading") ? 3 : 0; x.Color = x.Id == "Caption" ? Colors.Black : x.Color; }
                    G("Normal").SizePt = G("NoSpacing").SizePt = G("Quote").SizePt = G("ListParagraph").SizePt = 12;
                    G("Title").SizePt = 16; G("Title").Bold = true; G("Title").Align = TextAlignment.Center; G("Title").BorderBottom = null; G("Title").Color = Colors.Black;
                    G("Heading1").SizePt = 16; G("Heading2").SizePt = 14; G("Heading2").Italic = true; G("Heading3").SizePt = 13;
                    foreach (var h in All.Where(x => x.Id.StartsWith("Heading"))) { h.Color = Colors.Black; h.BeforePt = 12; }
                    break;
                case "Distinctive":
                    G("Title").Italic = true; G("Title").BorderBottom = null; G("Title").SizePt = 28; G("Title").Color = a1d;
                    G("Heading1").Italic = true; G("Heading1").SizePt = 16; G("Heading2").Italic = true; G("Subtitle").Bold = true;
                    break;
                case "Elegant":
                    foreach (var h in All.Where(x => x.Id.StartsWith("Heading") || x.Id == "Title" || x.Id == "Subtitle")) { h.Bold = false; h.Align = TextAlignment.Center; }
                    G("Title").SizePt = 30; G("Title").BorderBottom = null; G("Heading1").SizePt = 16;
                    break;
                case "Fancy":
                    G("Title").Background = a1; G("Title").Color = Colors.White; G("Title").BorderBottom = null;
                    G("Heading1").Background = Color.FromRgb((byte)(a1.R + (255 - a1.R) * 0.75), (byte)(a1.G + (255 - a1.G) * 0.75), (byte)(a1.B + (255 - a1.B) * 0.75));
                    G("Heading1").Color = a1d;
                    break;
                case "Formal":
                    G("Title").Align = TextAlignment.Center; G("Title").BorderBottom = null; G("Title").Color = Colors.Black; G("Title").SizePt = 24;
                    foreach (var h in All.Where(x => x.Id.StartsWith("Heading"))) { h.Color = Colors.Black; h.Align = TextAlignment.Center; }
                    G("Normal").AfterPt = 6; G("Normal").Line = 1.5;
                    break;
                case "Manuscript":
                    foreach (var x in All.Where(x => x.Id != "Code")) { x.Font = "Courier New"; x.Color = Colors.Black; x.BorderBottom = null; }
                    G("Normal").Line = 2; G("Normal").SizePt = 12; G("Title").SizePt = 18; G("Title").Align = TextAlignment.Center;
                    break;
                case "Modern":
                    foreach (var h in All.Where(x => x.Id.StartsWith("Heading") || x.Id == "Title")) { h.Font = body; h.BorderBottom = null; }
                    G("Title").SizePt = 32; G("Title").Color = a1; G("Heading1").SizePt = 18; G("Heading1").Bold = false;
                    break;
                case "Simple":
                    foreach (var x in All) { x.BorderBottom = null; if (x.Id.StartsWith("Heading") || x.Id == "Title") x.Color = Colors.Black; }
                    G("Normal").AfterPt = 6; G("Normal").Line = 1;
                    break;
                case "Traditional":
                    foreach (var x in All.Where(x => x.Id != "Code")) x.Font = "Times New Roman";
                    G("Title").Align = TextAlignment.Center; G("Title").BorderBottom = null; G("Title").Color = Colors.Black; G("Title").Italic = true;
                    foreach (var h in All.Where(x => x.Id.StartsWith("Heading"))) h.Color = Colors.Black;
                    break;
            }
            foreach (var x in Custom) if (!All.Any(a => a.Id == x.Id)) All.Add(x);
        }

        public static readonly List<StyleDef> Custom = new List<StyleDef>();

        public static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        public FontFamily Family => new FontFamily(Font);
        public double SizePx => SizePt * 96.0 / 72.0;

        public void Apply(Paragraph p, bool clearRuns = true)
        {
            p.Tag = Id;
            p.FontFamily = Family;
            p.FontSize = SizePx;
            p.FontWeight = Bold ? FontWeights.Bold : FontWeights.Normal;
            p.FontStyle = Italic ? FontStyles.Italic : FontStyles.Normal;
            p.Foreground = new SolidColorBrush(Color);
            p.Margin = new Thickness(IndentIn * 96, BeforePt * 96 / 72, double.IsNaN(p.Margin.Right) ? 0 : p.Margin.Right, AfterPt * 96 / 72);
            p.TextAlignment = Align;
            Fmt.SetLineSpacing(p, Line);
            if (BorderBottom.HasValue)
            {
                p.BorderBrush = new SolidColorBrush(BorderBottom.Value);
                p.BorderThickness = new Thickness(0, 0, 0, 1.33);
                p.Padding = new Thickness(0, 0, 0, 5);
            }
            else if (p.Tag as string != "HRule")
            {
                p.ClearValue(Block.BorderBrushProperty);
                p.ClearValue(Block.BorderThicknessProperty);
                p.ClearValue(Block.PaddingProperty);
            }
            if (Background.HasValue) p.Background = new SolidColorBrush(Background.Value);
            else p.ClearValue(TextElement.BackgroundProperty);
            if (clearRuns)
                foreach (var i in Fmt.Descendants(p))
                {
                    i.ClearValue(TextElement.FontFamilyProperty);
                    i.ClearValue(TextElement.FontSizeProperty);
                    i.ClearValue(TextElement.FontWeightProperty);
                    i.ClearValue(TextElement.FontStyleProperty);
                    i.ClearValue(TextElement.ForegroundProperty);
                }
        }
    }

    public static class Fmt
    {
        public const double PxPerPt = 96.0 / 72.0;

        public static IEnumerable<TextElement> Descendants(TextElement e)
        {
            IEnumerable<Inline> kids = e switch
            {
                Paragraph p => p.Inlines,
                Span s => s.Inlines,
                _ => Enumerable.Empty<Inline>()
            };
            foreach (var k in kids)
            {
                yield return k;
                foreach (var d in Descendants(k)) yield return d;
            }
        }

        public static void SetLineSpacing(Paragraph p, double factor)
        {
            if (Math.Abs(factor - 1) < 0.01) p.LineHeight = double.NaN;
            else p.LineHeight = Math.Round(p.FontSize * 1.22 * factor, 2);
            p.LineStackingStrategy = LineStackingStrategy.MaxHeight;
        }

        public static double GetLineSpacing(Paragraph p)
        {
            if (double.IsNaN(p.LineHeight) || p.FontSize <= 0) return 1;
            return Math.Round(p.LineHeight / (p.FontSize * 1.22) * 20) / 20;
        }

        /// <summary>Every paragraph in the document, in reading order (through lists, tables and sections).</summary>
        public static IEnumerable<Paragraph> AllParagraphs(IEnumerable<Block> blocks)
        {
            foreach (var b in blocks)
            {
                switch (b)
                {
                    case Paragraph p: yield return p; break;
                    case List l:
                        foreach (var li in l.ListItems) foreach (var x in AllParagraphs(li.Blocks)) yield return x;
                        break;
                    case Table t:
                        foreach (var g in t.RowGroups) foreach (var r in g.Rows) foreach (var c in r.Cells)
                                    foreach (var x in AllParagraphs(c.Blocks)) yield return x;
                        break;
                    case Section s: foreach (var x in AllParagraphs(s.Blocks)) yield return x; break;
                }
            }
        }

        public static IEnumerable<Block> AllBlocks(IEnumerable<Block> blocks)
        {
            foreach (var b in blocks)
            {
                yield return b;
                switch (b)
                {
                    case List l: foreach (var li in l.ListItems) foreach (var x in AllBlocks(li.Blocks)) yield return x; break;
                    case Table t:
                        foreach (var g in t.RowGroups) foreach (var r in g.Rows) foreach (var c in r.Cells)
                                    foreach (var x in AllBlocks(c.Blocks)) yield return x;
                        break;
                    case Section s: foreach (var x in AllBlocks(s.Blocks)) yield return x; break;
                }
            }
        }

        public static List<Paragraph> SelectedParagraphs(RichTextBox rtb)
        {
            var sel = rtb.Selection;
            var start = sel.Start.Paragraph;
            var end = sel.End.Paragraph;
            var list = new List<Paragraph>();
            if (start == null && end == null) return list;
            bool inside = false;
            foreach (var p in AllParagraphs(rtb.Document.Blocks))
            {
                if (p == start || (start == null && p.ContentStart.CompareTo(sel.Start) >= 0)) inside = true;
                if (inside) list.Add(p);
                if (p == end || (end == null && p.ContentEnd.CompareTo(sel.End) >= 0)) { if (inside) break; }
                if (inside && p.ContentStart.CompareTo(sel.End) > 0) { list.Remove(p); break; }
            }
            if (list.Count == 0 && start != null) list.Add(start);
            return list;
        }

        public static T Ancestor<T>(TextElement e) where T : TextElement
        {
            DependencyObject d = e;
            while (d != null)
            {
                if (d is T t) return t;
                d = d is TextElement te ? te.Parent : null;
            }
            return null;
        }

        public static T AncestorAt<T>(TextPointer p) where T : TextElement
        {
            var d = p?.Parent;
            while (d != null)
            {
                if (d is T t) return t;
                d = d is TextElement te ? te.Parent : null;
            }
            return null;
        }

        /// <summary>Text decorations applied to this inline or any of its ancestors up to the paragraph.</summary>
        static bool IsMarkup(DependencyObject d) => d is Span s && (s.Tag is RevInfo || s.Tag is CommentInfo || s.Tag is MergeFieldInfo);

        public static TextDecorationCollection EffectiveDecorations(TextElement e)
        {
            var result = new TextDecorationCollection();
            DependencyObject d = e;
            while (d is Inline i)
            {
                if (!IsMarkup(i) && i.TextDecorations != null && !(i is Hyperlink && i.ReadLocalValue(Inline.TextDecorationsProperty) == DependencyProperty.UnsetValue && false))
                    foreach (var td in i.TextDecorations) result.Add(td);
                d = i.Parent;
            }
            if (d is Paragraph p && p.TextDecorations != null) foreach (var td in p.TextDecorations) result.Add(td);
            return result;
        }

        /// <summary>Foreground ignoring colours that only come from revision / comment markup.</summary>
        public static Brush EffectiveForeground(TextElement e)
        {
            DependencyObject d = e;
            while (d is TextElement te)
            {
                if (!IsMarkup(te) && te.ReadLocalValue(TextElement.ForegroundProperty) != DependencyProperty.UnsetValue) return te.Foreground;
                if (te is Hyperlink h) return h.Foreground;
                if (te is Paragraph p) return p.Foreground;
                d = te.Parent;
            }
            return e.Foreground;
        }

        public static bool HasDecoration(TextDecorationCollection c, TextDecorationLocation loc) => c != null && c.Any(t => t.Location == loc);

        public static Brush EffectiveBackground(TextElement e)
        {
            DependencyObject d = e;
            while (d is Inline i)
            {
                if (IsMarkup(i)) { d = i.Parent; continue; }
                if (i.Background != null) return i.Background is SolidColorBrush sb && sb.Color.A == 0 ? null : i.Background;
                d = i.Parent;
            }
            return null;
        }

        public static BaselineAlignment EffectiveBaseline(TextElement e)
        {
            DependencyObject d = e;
            while (d is Inline i)
            {
                if (i.BaselineAlignment != BaselineAlignment.Baseline) return i.BaselineAlignment;
                d = i.Parent;
            }
            return BaselineAlignment.Baseline;
        }

        public static string Hex(Color c) => $"{c.R:X2}{c.G:X2}{c.B:X2}";

        public static Color? BrushColor(Brush b) => b is SolidColorBrush s ? s.Color : (Color?)null;

        public static readonly (string Name, Color Color)[] Highlights =
        {
            ("yellow", Color.FromRgb(255,255,0)), ("green", Color.FromRgb(0,255,0)), ("cyan", Color.FromRgb(0,255,255)),
            ("magenta", Color.FromRgb(255,0,255)), ("blue", Color.FromRgb(0,0,255)), ("red", Color.FromRgb(255,0,0)),
            ("darkBlue", Color.FromRgb(0,0,128)), ("darkCyan", Color.FromRgb(0,128,128)), ("darkGreen", Color.FromRgb(0,128,0)),
            ("darkMagenta", Color.FromRgb(128,0,128)), ("darkRed", Color.FromRgb(128,0,0)), ("darkYellow", Color.FromRgb(128,128,0)),
            ("darkGray", Color.FromRgb(128,128,128)), ("lightGray", Color.FromRgb(192,192,192)), ("black", Color.FromRgb(0,0,0)),
            ("white", Color.FromRgb(255,255,255)),
        };

        public static string HighlightName(Color c) => Highlights.FirstOrDefault(h => h.Color == c).Name;

        public static TextMarkerStyle[] BulletCycle = { TextMarkerStyle.Disc, TextMarkerStyle.Circle, TextMarkerStyle.Square };
        public static TextMarkerStyle[] NumberCycle = { TextMarkerStyle.Decimal, TextMarkerStyle.LowerLatin, TextMarkerStyle.LowerRoman };
        public static bool IsBullet(TextMarkerStyle m) => m == TextMarkerStyle.Disc || m == TextMarkerStyle.Circle || m == TextMarkerStyle.Square || m == TextMarkerStyle.Box || m == TextMarkerStyle.None;

        public static int ListDepth(List l)
        {
            int depth = 0;
            DependencyObject d = l.Parent;
            while (d != null)
            {
                if (d is List) depth++;
                d = d is TextElement te ? te.Parent : null;
            }
            return depth;
        }
    }
}
