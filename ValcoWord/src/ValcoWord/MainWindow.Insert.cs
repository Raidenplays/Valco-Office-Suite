using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace ValcoWord
{
    public partial class MainWindow
    {
        // =====================================================================================
        //  Shared helpers
        // =====================================================================================
        static StyleDef.Theme Th => StyleDef.CurrentTheme;
        static SolidColorBrush Br(Color c) => new SolidColorBrush(c);
        static SolidColorBrush Br(string hex) => new SolidColorBrush(StyleDef.C(hex.StartsWith("#") ? hex : "#" + hex));
        static Color[] Accents => new[] { Th.Accent1, Th.Accent2, Th.Accent3, Th.Accent4, Th.Accent5, Th.Accent6 };
        static Color Lighter(Color c, double f) => Color.FromRgb((byte)(c.R + (255 - c.R) * f), (byte)(c.G + (255 - c.G) * f), (byte)(c.B + (255 - c.B) * f));
        static Color Darker2(Color c, double f) => Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));

        /// <summary>Renders a visual to a 2x PNG so inserted graphics stay crisp and save into .docx.</summary>
        static (BitmapSource Src, byte[] Png) RenderVisual(FrameworkElement fe, double w, double h, double scale = 2)
        {
            fe.Measure(new Size(w, h));
            fe.Arrange(new Rect(0, 0, w, h));
            fe.UpdateLayout();
            var rtb = new RenderTargetBitmap((int)Math.Ceiling(w * scale), (int)Math.Ceiling(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            rtb.Render(fe);
            rtb.Freeze();
            return (rtb, EncodePng(rtb));
        }

        static byte[] EncodePng(BitmapSource src)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(src));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }

        void InsertVisual(FrameworkElement fe, double w, double h)
        {
            var (src, png) = RenderVisual(fe, w, h);
            InsertImage(src, new ImageData { Bytes = png, Ext = "png" });
        }

        Paragraph NewPara(string text = "", string style = "Normal")
        {
            var p = new Paragraph();
            StyleDef.Get(style).Apply(p, false);
            if (!string.IsNullOrEmpty(text)) p.Inlines.Add(new Run(text));
            return p;
        }

        /// <summary>Inserts blocks after the caret's paragraph (or in place of it if it's empty).</summary>
        void InsertBlocksAtCaret(params Block[] blocks)
        {
            Editor.BeginChange();
            try
            {
                var para = Editor.CaretPosition.Paragraph;
                Block anchor = para;
                if (para != null && para.Parent is ListItem li) anchor = UI.FindAncestor<List>(li) as Block ?? para;
                if (para == null) { foreach (var b in blocks) Editor.Document.Blocks.Add(b); }
                else
                {
                    bool empty = Spans.Text(para).Length == 0 && !para.Inlines.Any(i => i is InlineUIContainer || i is Floater);
                    var coll = anchor.SiblingBlocks;
                    if (empty && anchor == para) { foreach (var b in blocks) coll.InsertBefore(para, b); }
                    else
                    {
                        Block prev = anchor;
                        foreach (var b in blocks) { coll.InsertAfter(prev, b); prev = b; }
                        if (prev.NextBlock == null) coll.InsertAfter(prev, NewPara());
                    }
                }
            }
            finally { Editor.EndChange(); }
            MarkDirty(); ScheduleLayout();
        }

        /// <summary>Page number (1-based) of a position, from the page boundaries.</summary>
        int PageOf(TextPointer tp)
        {
            if (tp == null) return 1;
            var r = tp.GetCharacterRect(LogicalDirection.Forward);
            if (r.IsEmpty) return 1;
            return 1 + _boundaries.Count(b => b.Y <= r.Top + 1);
        }

        void GoToPosition(TextPointer tp)
        {
            if (tp == null) return;
            Editor.Focus();
            Editor.Selection.Select(tp, tp);
            BringSelectionIntoView();
        }

        /// <summary>Follows a link: "#name" jumps to a bookmark, anything else opens in the browser.</summary>
        void FollowLink(Uri uri)
        {
            if (uri == null) return;
            var s = uri.OriginalString;
            if (s.StartsWith("#"))
            {
                var name = s.Substring(1);
                var bm = Spans.WithTag<BookmarkInfo>(Editor.Document).FirstOrDefault(x => ((BookmarkInfo)x.Tag).Name == name);
                if (bm != null) GoToPosition(bm.ContentStart);
                else Flash("The bookmark \"" + name + "\" doesn't exist.");
                return;
            }
            OpenLink(uri);
        }

        // =====================================================================================
        //  Pages: cover page, blank page
        // =====================================================================================
        static readonly string[] CoverDesigns = { "Aero", "Horizon", "Glass Band", "Classic", "Pinstripe", "Sidebar", "Bold Block", "Simple" };

        ContextMenu CoverPageMenu()
        {
            var m = RB.Menu();
            var host = new WrapPanel { Width = 330 };
            foreach (var d in CoverDesigns)
            {
                var name = d;
                var tile = RB.Tile(CoverThumb(name), name, () => { m.IsOpen = false; InsertCoverPage(name); }, 76, 104);
                host.Children.Add(tile);
            }
            m.Items.Add(new MenuItem { Header = host, StaysOpenOnClick = true, Padding = new Thickness(0) });
            m.Items.Add(new Separator());
            m.Items.Add(RB.MI("Remove Current Cover Page", RemoveCoverPage, "IconDelete"));
            return m;
        }

        UIElement CoverThumb(string design)
        {
            var c = new Canvas { Width = 68, Height = 88, Background = Brushes.White, ClipToBounds = true };
            var a1 = Th.Accent1; var dk = Th.Dark2;
            void R(double x, double y, double w, double h, Color col) { var r = new Rectangle { Width = w, Height = h, Fill = Br(col) }; Canvas.SetLeft(r, x); Canvas.SetTop(r, y); c.Children.Add(r); }
            void L(double x, double y, double w, double t, Color col) => R(x, y, w, t, col);
            switch (design)
            {
                case "Aero": R(0, 0, 68, 30, a1); L(8, 36, 50, 4, dk); L(8, 43, 36, 2, Lighter(dk, .4)); break;
                case "Horizon": R(0, 50, 68, 38, Lighter(a1, .2)); L(8, 20, 52, 4, dk); L(8, 28, 30, 2, a1); break;
                case "Glass Band": R(0, 30, 68, 18, a1); R(0, 30, 68, 7, Lighter(a1, .5)); L(8, 54, 46, 3, dk); break;
                case "Classic": L(8, 30, 52, 1, dk); L(14, 36, 40, 4, dk); L(20, 43, 28, 2, Lighter(dk, .4)); L(8, 50, 52, 1, dk); break;
                case "Pinstripe": for (int i = 0; i < 9; i++) L(0, i * 4, 68, 1, Lighter(a1, .3)); L(8, 44, 52, 4, dk); L(8, 51, 30, 2, a1); break;
                case "Sidebar": R(0, 0, 18, 88, a1); L(24, 30, 38, 4, dk); L(24, 37, 26, 2, Lighter(dk, .4)); break;
                case "Bold Block": R(6, 22, 56, 40, dk); L(12, 34, 40, 4, Colors.White); L(12, 42, 26, 2, Lighter(a1, .6)); break;
                default: L(8, 36, 52, 4, dk); L(8, 43, 30, 2, Lighter(dk, .4)); break;
            }
            return new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = c };
        }

        void InsertCoverPage(string design)
        {
            RemoveCoverPage(false);
            var sec = new Section { Tag = "Cover" };
            var a1 = Th.Accent1; var dk = Th.Dark2;
            Paragraph P(string text, double sizePt, Color fg, bool bold = false, string font = null, TextAlignment align = TextAlignment.Left, double after = 6)
            {
                var p = NewPara();
                p.Margin = new Thickness(0, 0, 0, after * Fmt.PxPerPt);
                p.TextAlignment = align;
                var r = new Run(text) { FontSize = sizePt * Fmt.PxPerPt, Foreground = Br(fg) };
                if (bold) r.FontWeight = FontWeights.Bold;
                if (font != null) r.FontFamily = new FontFamily(font);
                p.Inlines.Add(r);
                return p;
            }
            Paragraph Band(Color col, double heightPt)
            {
                var p = NewPara();
                p.Background = Br(col);
                p.Margin = new Thickness(0);
                p.Inlines.Add(new Run(" ") { FontSize = heightPt * Fmt.PxPerPt * 0.8 });
                return p;
            }
            var title = string.IsNullOrEmpty(_docTitle) ? "[Type the document title]" : _docTitle;
            var author = string.IsNullOrEmpty(Settings.Current.UserName) ? Environment.UserName : Settings.Current.UserName;
            var date = DateTime.Now.ToString("MMMM d, yyyy", CultureInfo.CurrentCulture);
            string major = Th.Major;
            switch (design)
            {
                case "Aero":
                    sec.Blocks.Add(Band(a1, 150));
                    sec.Blocks.Add(P("", 12, dk, after: 24));
                    sec.Blocks.Add(P(title, 36, dk, true, major));
                    sec.Blocks.Add(P("[Type the document subtitle]", 18, Lighter(dk, .35), font: major, after: 220));
                    sec.Blocks.Add(P(author, 12, dk, true));
                    sec.Blocks.Add(P(date, 12, Lighter(dk, .35)));
                    break;
                case "Horizon":
                    sec.Blocks.Add(P("", 12, dk, after: 120));
                    sec.Blocks.Add(P(title, 40, dk, false, major));
                    sec.Blocks.Add(P("[Type the document subtitle]", 16, a1, font: major, after: 40));
                    sec.Blocks.Add(Band(Lighter(a1, .2), 200));
                    sec.Blocks.Add(P(author + "  |  " + date, 11, dk, after: 0));
                    break;
                case "Glass Band":
                    sec.Blocks.Add(P("", 12, dk, after: 140));
                    sec.Blocks.Add(Band(Lighter(a1, .5), 14));
                    var gb = P(title, 32, Colors.White, true, major, TextAlignment.Center, 0); gb.Background = Br(a1); gb.Padding = new Thickness(0, 10, 0, 10);
                    sec.Blocks.Add(gb);
                    sec.Blocks.Add(Band(Darker2(a1, .75), 6));
                    sec.Blocks.Add(P("[Type the document subtitle]", 16, dk, font: major, align: TextAlignment.Center, after: 200));
                    sec.Blocks.Add(P(author, 12, dk, align: TextAlignment.Center));
                    sec.Blocks.Add(P(date, 12, Lighter(dk, .35), align: TextAlignment.Center));
                    break;
                case "Classic":
                    sec.Blocks.Add(P("", 12, dk, after: 160));
                    var c1 = P(title, 30, dk, false, "Times New Roman", TextAlignment.Center, 4); c1.BorderBrush = Br(dk); c1.BorderThickness = new Thickness(0, 1, 0, 0); c1.Padding = new Thickness(0, 12, 0, 0);
                    sec.Blocks.Add(c1);
                    var c2 = P("[Type the document subtitle]", 16, Lighter(dk, .3), font: "Times New Roman", align: TextAlignment.Center, after: 200); c2.BorderBrush = Br(dk); c2.BorderThickness = new Thickness(0, 0, 0, 1); c2.Padding = new Thickness(0, 0, 0, 12);
                    sec.Blocks.Add(c2);
                    sec.Blocks.Add(P(author, 12, dk, align: TextAlignment.Center));
                    sec.Blocks.Add(P(date, 12, dk, align: TextAlignment.Center));
                    break;
                case "Pinstripe":
                    for (int i = 0; i < 6; i++) sec.Blocks.Add(Band(i % 2 == 0 ? Lighter(a1, .25) : Colors.White, 6));
                    sec.Blocks.Add(P("", 12, dk, after: 100));
                    sec.Blocks.Add(P(title, 34, dk, true, major));
                    sec.Blocks.Add(P("[Type the document subtitle]", 16, a1, font: major, after: 180));
                    sec.Blocks.Add(P(author, 12, dk));
                    sec.Blocks.Add(P(date, 12, Lighter(dk, .35)));
                    break;
                case "Sidebar":
                    {
                        var t = new Table { CellSpacing = 0 };
                        t.Columns.Add(new TableColumn { Width = new GridLength(140) });
                        t.Columns.Add(new TableColumn { Width = new GridLength(_page.BodyWidthPx - 140) });
                        var g = new TableRowGroup(); var row = new TableRow();
                        var left = new TableCell(P(" ", 12, Colors.White, after: 560)) { Background = Br(a1), BorderThickness = new Thickness(0) };
                        var right = new TableCell { Padding = new Thickness(18, 120, 0, 0), BorderThickness = new Thickness(0) };
                        right.Blocks.Add(P(title, 32, dk, true, major));
                        right.Blocks.Add(P("[Type the document subtitle]", 16, a1, font: major, after: 220));
                        right.Blocks.Add(P(author, 12, dk));
                        right.Blocks.Add(P(date, 12, Lighter(dk, .35)));
                        row.Cells.Add(left); row.Cells.Add(right); g.Rows.Add(row); t.RowGroups.Add(g);
                        sec.Blocks.Add(t);
                        break;
                    }
                case "Bold Block":
                    sec.Blocks.Add(P("", 12, dk, after: 110));
                    var bb = P(title, 36, Colors.White, true, major, TextAlignment.Left, 0); bb.Background = Br(dk); bb.Padding = new Thickness(24, 30, 24, 4);
                    sec.Blocks.Add(bb);
                    var bs = P("[Type the document subtitle]", 16, Lighter(a1, .6), font: major, after: 0); bs.Background = Br(dk); bs.Padding = new Thickness(24, 0, 24, 30);
                    sec.Blocks.Add(bs);
                    sec.Blocks.Add(P("", 12, dk, after: 160));
                    sec.Blocks.Add(P(author, 12, dk, true));
                    sec.Blocks.Add(P(date, 12, Lighter(dk, .35)));
                    break;
                default:
                    sec.Blocks.Add(P("", 12, dk, after: 180));
                    sec.Blocks.Add(P(title, 32, dk, false, major));
                    sec.Blocks.Add(P("[Type the document subtitle]", 16, Lighter(dk, .35), font: major, after: 220));
                    sec.Blocks.Add(P(author, 12, dk));
                    sec.Blocks.Add(P(date, 12, Lighter(dk, .35)));
                    break;
            }
            Editor.BeginChange();
            var first = Editor.Document.Blocks.FirstBlock;
            if (first == null) Editor.Document.Blocks.Add(sec);
            else
            {
                Editor.Document.Blocks.InsertBefore(first, sec);
                first.BreakPageBefore = true;
            }
            Editor.EndChange();
            GoToPosition(Editor.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward));
            MarkDirty(); ScheduleLayout();
        }

        void RemoveCoverPage() => RemoveCoverPage(true);
        void RemoveCoverPage(bool tell)
        {
            var cover = Editor.Document.Blocks.OfType<Section>().FirstOrDefault(s => (s.Tag as string) == "Cover");
            if (cover == null) { if (tell) Flash("This document doesn't have a cover page."); return; }
            Editor.BeginChange();
            var next = cover.NextBlock;
            Editor.Document.Blocks.Remove(cover);
            if (next != null) next.BreakPageBefore = false;
            Editor.EndChange();
            MarkDirty(); ScheduleLayout();
        }

        void InsertBlankPage()
        {
            Editor.BeginChange();
            if (!Editor.Selection.IsEmpty) Editor.Selection.Text = "";
            var pos = Editor.CaretPosition;
            var np = pos.InsertParagraphBreak();
            var blank = np.Paragraph;
            if (blank != null)
            {
                blank.BreakPageBefore = true;
                var after = blank.ContentStart.InsertParagraphBreak();
                if (after.Paragraph != null) after.Paragraph.BreakPageBefore = true;
                // the page break pushed the blank paragraph's text to the following page
                Editor.CaretPosition = after.Paragraph?.ContentStart ?? after;
            }
            Editor.EndChange();
            MarkDirty(); ScheduleLayout(); Editor.Focus();
        }

        // =====================================================================================
        //  Tables menu
        // =====================================================================================
        ContextMenu TableMenu()
        {
            var m = RB.Menu();
            var label = new TextBlock { Text = "Insert Table", FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 4), Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x42, 0x8B)) };
            var grid = new UniformGrid { Rows = 8, Columns = 10 };
            var cells = new Border[8, 10];
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 10; c++)
                {
                    int rr = r, cc = c;
                    var b = new Border { Width = 16, Height = 16, Margin = new Thickness(1), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Background = Brushes.White };
                    b.MouseEnter += (s, e) =>
                    {
                        for (int i = 0; i < 8; i++) for (int j = 0; j < 10; j++)
                        {
                            bool on = i <= rr && j <= cc;
                            cells[i, j].Background = on ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x80)) : Brushes.White;
                            cells[i, j].BorderBrush = on ? new SolidColorBrush(Color.FromRgb(0xE0, 0x90, 0x30)) : Brushes.Gray;
                        }
                        label.Text = $"{cc + 1}x{rr + 1} Table";
                    };
                    b.MouseLeftButtonUp += (s, e) => { m.IsOpen = false; InsertTable(rr + 1, cc + 1); };
                    cells[r, c] = b;
                    grid.Children.Add(b);
                }
            var sp = new StackPanel(); sp.Children.Add(label); sp.Children.Add(grid);
            m.Items.Add(new MenuItem { Header = sp, StaysOpenOnClick = true });
            m.Items.Add(new Separator());
            m.Items.Add(RB.MI("Insert Table...", () => InsertTableDialog_Click(null, null), "IconTable"));
            m.Items.Add(RB.MI("Convert Text to Table...", ConvertTextToTable, "IconConvert"));
            var quick = new MenuItem { Header = "Quick Tables" };
            foreach (var q in new[] { "Calendar", "Double Table", "Matrix", "Tabular List", "With Subheads" })
            {
                var name = q;
                quick.Items.Add(RB.MI(name, () => InsertQuickTable(name)));
            }
            m.Items.Add(quick);
            return m;
        }

        void ConvertTextToTable()
        {
            var paras = Fmt.SelectedParagraphs(Editor).Where(p => Fmt.Ancestor<TableCell>(p) == null).ToList();
            if (paras.Count == 0) { Flash("Select the text you want to convert."); return; }
            var lines = paras.Select(p => Spans.Text(p)).ToList();
            char sep = lines.Any(l => l.Contains('\t')) ? '\t' : lines.Any(l => l.Contains(',')) ? ',' : lines.Any(l => l.Contains(';')) ? ';' : '\t';
            var d = new DialogBase(this, "Convert Text to Table", 330);
            var cols = D.Box(lines.Max(l => l.Split(sep).Length).ToString(), 60);
            var rTab = new RadioButton { Content = "Tabs", IsChecked = sep == '\t', Margin = new Thickness(0, 0, 10, 0) };
            var rComma = new RadioButton { Content = "Commas", IsChecked = sep == ',', Margin = new Thickness(0, 0, 10, 0) };
            var rSemi = new RadioButton { Content = "Semicolons", IsChecked = sep == ';' };
            var seps = new StackPanel { Orientation = Orientation.Horizontal }; seps.Children.Add(rTab); seps.Children.Add(rComma); seps.Children.Add(rSemi);
            var sp = new StackPanel();
            sp.Children.Add(D.Group("Table size", D.Form(("Number of columns:", cols), ("Number of rows:", new TextBlock { Text = lines.Count.ToString() }))));
            sp.Children.Add(D.Group("Separate text at", seps));
            d.Content = UI.DialogLayout(sp, UI.Strip(UI.Btn("OK", (s, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
            if (d.ShowDialog() != true) return;
            sep = rComma.IsChecked == true ? ',' : rSemi.IsChecked == true ? ';' : '\t';
            int n = Math.Max(1, (int)(D.ParseNum(cols.Text) ?? 1));
            var rows = lines.Select(l => l.Split(sep)).ToList();
            var t = BuildTable(rows.Select(r => Enumerable.Range(0, n).Select(i => i < r.Length ? r[i].Trim() : "").ToArray()).ToList());
            Editor.BeginChange();
            paras[0].SiblingBlocks.InsertBefore(paras[0], t);
            foreach (var p in paras) (p.Parent as FlowDocument)?.Blocks.Remove(p);
            foreach (var p in paras) if (p.Parent is Section s) s.Blocks.Remove(p);
            Editor.EndChange();
            MarkDirty(); ScheduleLayout();
        }

        Table BuildTable(List<string[]> rows, bool borders = true, bool headerBold = false)
        {
            int cols = rows.Max(r => r.Length);
            var t = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 10) };
            double colW = Math.Floor((_page.BodyWidthPx - 2) / cols);
            for (int c = 0; c < cols; c++) t.Columns.Add(new TableColumn { Width = new GridLength(colW) });
            var g = new TableRowGroup();
            for (int r = 0; r < rows.Count; r++)
            {
                var row = new TableRow();
                for (int c = 0; c < cols; c++)
                {
                    var cell = NewCell(borders);
                    var p = (Paragraph)cell.Blocks.FirstBlock;
                    var text = c < rows[r].Length ? rows[r][c] : "";
                    if (text.Length > 0) p.Inlines.Add(new Run(text) { FontWeight = headerBold && r == 0 ? FontWeights.Bold : FontWeights.Normal });
                    row.Cells.Add(cell);
                }
                g.Rows.Add(row);
            }
            t.RowGroups.Add(g);
            return t;
        }

        void InsertQuickTable(string kind)
        {
            Table t;
            switch (kind)
            {
                case "Calendar":
                    {
                        var now = DateTime.Now; var first = new DateTime(now.Year, now.Month, 1);
                        var rows = new List<string[]> { new[] { "S", "M", "T", "W", "T", "F", "S" } };
                        int day = 1, offset = (int)first.DayOfWeek, days = DateTime.DaysInMonth(now.Year, now.Month);
                        while (day <= days)
                        {
                            var r = new string[7];
                            for (int i = 0; i < 7; i++) { if ((rows.Count == 1 && i < offset) || day > days) r[i] = ""; else r[i] = (day++).ToString(); }
                            rows.Add(r);
                        }
                        InsertBlocksAtCaret(NewPara(now.ToString("MMMM yyyy"), "Heading1"));
                        t = BuildTable(rows, true, true);
                        foreach (var c in t.RowGroups[0].Rows[0].Cells) c.Background = Br(Th.Accent1);
                        foreach (var r in Fmt.AllParagraphs(t.RowGroups[0].Rows[0].Cells.SelectMany(c => c.Blocks))) { r.Foreground = Brushes.White; r.TextAlignment = TextAlignment.Center; }
                        break;
                    }
                case "Double Table":
                    t = BuildTable(new List<string[]> { new[] { "Class", "Graduate", "", "Class", "Graduate" }, new[] { "Undergraduate", "", "", "Undergraduate", "" }, new[] { "Cedar College", "4", "", "Elm College", "5" }, new[] { "Maple Academy", "3", "", "Pine College", "2" }, new[] { "Oak Institute", "1", "", "Total", "15" } }, true, true);
                    break;
                case "Matrix":
                    t = BuildTable(new List<string[]> { new[] { "City or Town", "Point A", "Point B", "Point C", "Point D" }, new[] { "Point A", "—", "", "", "" }, new[] { "Point B", "87", "—", "", "" }, new[] { "Point C", "64", "56", "—", "" }, new[] { "Point D", "37", "32", "91", "—" } }, true, true);
                    break;
                case "Tabular List":
                    t = BuildTable(new List<string[]> { new[] { "Item", "Needed" }, new[] { "Books", "1" }, new[] { "Magazines", "3" }, new[] { "Notebooks", "1" }, new[] { "Paper pads", "1" }, new[] { "Pens", "3" }, new[] { "Pencils", "2" } }, true, true);
                    break;
                default:
                    t = BuildTable(new List<string[]> { new[] { "College", "New students", "Graduating students", "Change" }, new[] { "Undergraduate", "", "", "" }, new[] { "Cedar University", "110", "103", "+7" }, new[] { "Elm College", "223", "214", "+9" }, new[] { "Graduate", "", "", "" }, new[] { "Lake College", "24", "20", "+4" }, new[] { "Total", "357", "337", "+20" } }, true, true);
                    break;
            }
            InsertBlocksAtCaret(t);
            ApplyTableStyleTo(t, kind == "Calendar" ? "Light Grid" : "Medium Shading 1");
        }

        // =====================================================================================
        //  Illustrations: clip art, shapes, SmartArt, chart
        // =====================================================================================
        // vector clip art: keywords, then (geometry, fill hex, stroke hex) layers drawn in a 100x100 box
        static readonly (string Name, string Keywords, (string Geo, string Fill, string Stroke)[] Layers)[] ClipArt =
        {
            ("Sun", "sun weather summer sunny day", new (string, string, string)[] { ("M50,20 A30,30 0 1 1 49.9,20 Z", "FFC83D", "D99A00"), ("M50,2 V14 M50,86 V98 M2,50 H14 M86,50 H98 M16,16 L25,25 M75,75 L84,84 M84,16 L75,25 M16,84 L25,75", null, "F0A800") }),
            ("Star", "star award favourite rating", new (string, string, string)[] { ("M50,5 L61,38 L96,38 L68,59 L79,93 L50,72 L21,93 L32,59 L4,38 L39,38 Z", "FFD54A", "C79A00") }),
            ("Heart", "heart love valentine", new (string, string, string)[] { ("M50,88 C20,65 4,48 4,30 C4,14 17,6 29,6 C39,6 46,12 50,20 C54,12 61,6 71,6 C83,6 96,14 96,30 C96,48 80,65 50,88 Z", "E8424B", "A3202A") }),
            ("House", "house home building real estate", new (string, string, string)[] { ("M15,48 L50,15 L85,48 V92 H15 Z", "F4E3C1", "8A5A2B"), ("M8,52 L50,12 L92,52 L86,58 L50,24 L14,58 Z", "C0504D", "7A2622"), ("M42,92 V66 H58 V92 Z", "7A4A1F", "4D2E12"), ("M24,60 H36 V72 H24 Z M64,60 H76 V72 H64 Z", "9DD3F5", "3B6A8C") }),
            ("Tree", "tree nature forest plant green", new (string, string, string)[] { ("M44,70 H56 V96 H44 Z", "8A5A2B", "5C3A18"), ("M50,4 C72,4 88,22 88,42 C88,62 70,74 50,74 C30,74 12,62 12,42 C12,22 28,4 50,4 Z", "6BB34A", "3E7A2A") }),
            ("Lightbulb", "idea lightbulb bulb think innovation", new (string, string, string)[] { ("M50,6 C30,6 18,21 18,38 C18,52 28,58 34,68 V76 H66 V68 C72,58 82,52 82,38 C82,21 70,6 50,6 Z", "FFE97A", "C9A400"), ("M34,78 H66 V86 H34 Z M38,88 H62 V94 H38 Z", "A0A8B4", "6B7280") }),
            ("Telephone", "phone telephone call contact", new (string, string, string)[] { ("M22,10 C14,12 8,22 12,34 C20,60 40,80 66,88 C78,92 88,86 90,78 L78,64 C74,60 68,62 64,66 C58,64 40,46 34,36 C38,32 40,26 36,22 Z", "4F81BD", "264D80") }),
            ("Envelope", "mail email letter envelope message", new (string, string, string)[] { ("M8,24 H92 V80 H8 Z", "FFFFFF", "6B7280"), ("M8,24 L50,56 L92,24", null, "6B7280") }),
            ("Clock", "clock time hour schedule watch", new (string, string, string)[] { ("M50,6 A44,44 0 1 1 49.9,6 Z", "FFFFFF", "334155"), ("M50,50 V20 M50,50 L70,62", null, "334155") }),
            ("Check Mark", "check ok yes done tick correct", new (string, string, string)[] { ("M10,54 L36,80 L90,22 L80,12 L36,58 L20,42 Z", "4CAF50", "2E7D32") }),
            ("Cross", "no wrong cross x error", new (string, string, string)[] { ("M18,8 L50,40 L82,8 L92,18 L60,50 L92,82 L82,92 L50,60 L18,92 L8,82 L40,50 L8,18 Z", "E53935", "8E1A17") }),
            ("Arrow", "arrow right next direction forward", new (string, string, string)[] { ("M6,38 H58 V18 L94,50 L58,82 V62 H6 Z", "4F81BD", "264D80") }),
            ("Flag", "flag goal finish milestone", new (string, string, string)[] { ("M18,6 H24 V96 H18 Z", "6B7280", "374151"), ("M24,10 C40,4 56,18 72,12 C78,10 84,8 90,10 V48 C74,44 60,56 44,52 C36,50 30,48 24,50 Z", "E53935", "8E1A17") }),
            ("Book", "book reading school education library", new (string, string, string)[] { ("M10,20 C24,14 38,14 50,22 V90 C38,82 24,82 10,88 Z", "4F81BD", "264D80"), ("M90,20 C76,14 62,14 50,22 V90 C62,82 76,82 90,88 Z", "6EA0D8", "264D80") }),
            ("Computer", "computer pc monitor technology office", new (string, string, string)[] { ("M8,12 H92 V66 H8 Z", "334155", "1F2937"), ("M14,18 H86 V60 H14 Z", "8EC9F0", "3B6A8C"), ("M40,66 H60 V80 H40 Z M26,80 H74 V88 H26 Z", "9CA3AF", "4B5563") }),
            ("Cloud", "cloud weather sky cloudy", new (string, string, string)[] { ("M26,78 C12,78 4,68 4,58 C4,46 14,38 26,40 C28,24 40,14 54,14 C70,14 80,26 82,40 C92,40 98,50 98,60 C98,70 90,78 80,78 Z", "FFFFFF", "7AA6D8") }),
            ("Snowflake", "snow winter cold christmas snowflake", new (string, string, string)[] { ("M50,4 V96 M10,27 L90,73 M10,73 L90,27 M42,12 L50,20 L58,12 M42,88 L50,80 L58,88", null, "4BACC6") }),
            ("Music", "music note song sound", new (string, string, string)[] { ("M38,16 L86,6 V66 C86,74 78,80 70,80 C62,80 56,74 56,68 C56,60 64,56 72,56 C76,56 78,57 80,58 V24 L44,32 V76 C44,84 36,90 28,90 C20,90 14,84 14,78 C14,70 22,66 30,66 C34,66 36,67 38,68 Z", "334155", "1F2937") }),
            ("Gift", "gift present birthday celebration", new (string, string, string)[] { ("M12,40 H88 V92 H12 Z", "C0504D", "7A2622"), ("M6,28 H94 V42 H6 Z", "E07B78", "7A2622"), ("M44,28 H56 V92 H44 Z", "FFD54A", "C79A00"), ("M50,28 C40,10 22,12 28,24 C30,28 40,28 50,28 C60,28 70,28 72,24 C78,12 60,10 50,28 Z", "FFD54A", "C79A00") }),
            ("Calendar", "calendar date schedule month", new (string, string, string)[] { ("M10,16 H90 V92 H10 Z", "FFFFFF", "6B7280"), ("M10,16 H90 V34 H10 Z", "C0504D", "7A2622"), ("M22,44 H34 V56 H22 Z M44,44 H56 V56 H44 Z M66,44 H78 V56 H66 Z M22,66 H34 V78 H22 Z M44,66 H56 V78 H44 Z", "9CA3AF", null) }),
            ("Trophy", "trophy award winner cup prize", new (string, string, string)[] { ("M28,8 H72 V36 C72,52 62,62 50,62 C38,62 28,52 28,36 Z", "FFC83D", "B88400"), ("M28,14 H14 C14,32 22,40 30,40 M72,14 H86 C86,32 78,40 70,40", null, "B88400"), ("M44,62 H56 V78 H44 Z M30,78 H70 V92 H30 Z", "B88400", "7A5800") }),
            ("Handshake", "business deal agreement handshake partner", new (string, string, string)[] { ("M4,40 L26,30 L50,44 L74,30 L96,40 L96,62 L74,70 L50,58 L26,70 L4,62 Z", "F2C89B", "A0682E"), ("M26,70 L40,82 L52,74 L62,82 L74,70", null, "A0682E") }),
            ("Chart", "chart graph growth business statistics", new (string, string, string)[] { ("M10,90 H92 M10,90 V8", null, "374151"), ("M18,70 H32 V90 H18 Z", "4F81BD", null), ("M40,52 H54 V90 H40 Z", "C0504D", null), ("M62,30 H76 V90 H62 Z", "9BBB59", null) }),
            ("Smiley", "smile happy face emotion", new (string, string, string)[] { ("M50,6 A44,44 0 1 1 49.9,6 Z", "FFD54A", "C79A00"), ("M34,36 A5,5 0 1 1 33.9,36 Z M66,36 A5,5 0 1 1 65.9,36 Z", "334155", null), ("M28,58 C36,76 64,76 72,58", null, "334155") }),
        };

        void ShowClipArtPane()
        {
            var search = new TextBox { Height = 23, VerticalContentAlignment = VerticalAlignment.Center };
            var results = new WrapPanel { Margin = new Thickness(4) };
            var includePics = new CheckBox { Content = "Include my Pictures folder", IsChecked = true, Margin = new Thickness(0, 4, 0, 4) };
            void Run()
            {
                results.Children.Clear();
                var q = search.Text.Trim().ToLowerInvariant();
                foreach (var ca in ClipArt.Where(c => q.Length == 0 || c.Keywords.Contains(q) || c.Name.ToLowerInvariant().Contains(q)))
                {
                    var art = ca;
                    var tile = RB.Tile(ClipArtVisual(art.Layers, 56), art.Name, () => InsertClipArt(art.Layers), 66, 66);
                    results.Children.Add(tile);
                }
                if (includePics.IsChecked == true)
                {
                    try
                    {
                        var dir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                        var files = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories).Where(IsImageFile)
                            .Where(f => q.Length == 0 || System.IO.Path.GetFileNameWithoutExtension(f).ToLowerInvariant().Contains(q)).Take(40);
                        foreach (var f in files)
                        {
                            var file = f;
                            Image thumb = null;
                            try
                            {
                                var bi = new BitmapImage(); bi.BeginInit(); bi.UriSource = new Uri(file); bi.DecodePixelWidth = 112; bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit(); bi.Freeze();
                                thumb = new Image { Source = bi, Width = 56, Height = 56, Stretch = Stretch.Uniform };
                            }
                            catch { continue; }
                            results.Children.Add(RB.Tile(thumb, System.IO.Path.GetFileName(file), () => InsertImageFile(file), 66, 66));
                        }
                    }
                    catch { }
                }
                if (results.Children.Count == 0) results.Children.Add(new TextBlock { Text = "No results were found.", Foreground = Brushes.Gray, Margin = new Thickness(4) });
            }
            search.KeyDown += (s, e) => { if (e.Key == Key.Enter) Run(); };
            includePics.Click += (s, e) => Run();
            var go = UI.Btn("Go", (s, e) => Run());
            go.MinWidth = 40; go.Margin = new Thickness(4, 0, 0, 0);
            var row = new DockPanel(); DockPanel.SetDock(go, Dock.Right); row.Children.Add(go); row.Children.Add(search);
            var sp = new StackPanel { Margin = new Thickness(6) };
            sp.Children.Add(new TextBlock { Text = "Search for:" });
            sp.Children.Add(row);
            sp.Children.Add(includePics);
            sp.Children.Add(results);
            ShowRightPane("clipart", "Clip Art", sp, null, 250);
            Run();
        }

        static FrameworkElement ClipArtVisual((string Geo, string Fill, string Stroke)[] layers, double size)
        {
            var c = new Canvas { Width = 100, Height = 100 };
            foreach (var l in layers)
            {
                var p = new Path { Data = Geometry.Parse(l.Geo), StrokeThickness = 3, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                if (l.Fill != null) p.Fill = Br(l.Fill);
                if (l.Stroke != null) p.Stroke = Br(l.Stroke);
                c.Children.Add(p);
            }
            return new Viewbox { Width = size, Height = size, Child = c };
        }

        void InsertClipArt((string Geo, string Fill, string Stroke)[] layers) => InsertVisual(ClipArtVisual(layers, 144), 144, 144);

        // ---------------- shapes ----------------
        static readonly (string Category, (string Name, string Geo)[] Items)[] ShapeCatalog =
        {
            ("Lines", new[] { ("Line", "M0,100 L100,0"), ("Arrow", "M0,100 L92,8 M70,6 L94,6 L94,30"), ("Double Arrow", "M8,92 L92,8 M70,6 L94,6 L94,30 M6,70 L6,94 L30,94"), ("Curve", "M0,100 C20,0 80,100 100,0") }),
            ("Basic Shapes", new[] { ("Rectangle", "M0,0 H100 V100 H0 Z"), ("Rounded Rectangle", "M15,0 H85 A15,15 0 0 1 100,15 V85 A15,15 0 0 1 85,100 H15 A15,15 0 0 1 0,85 V15 A15,15 0 0 1 15,0 Z"),
                ("Oval", "M50,0 A50,50 0 1 1 49.9,0 Z"), ("Isosceles Triangle", "M50,0 L100,100 H0 Z"), ("Right Triangle", "M0,0 L100,100 H0 Z"), ("Diamond", "M50,0 L100,50 L50,100 L0,50 Z"),
                ("Pentagon", "M50,0 L100,38 L81,100 H19 L0,38 Z"), ("Hexagon", "M25,0 H75 L100,50 L75,100 H25 L0,50 Z"), ("Octagon", "M30,0 H70 L100,30 V70 L70,100 H30 L0,70 V30 Z"),
                ("Parallelogram", "M25,0 H100 L75,100 H0 Z"), ("Trapezoid", "M20,0 H80 L100,100 H0 Z"), ("Cross", "M35,0 H65 V35 H100 V65 H65 V100 H35 V65 H0 V35 H35 Z"),
                ("Heart", "M50,95 C20,70 0,52 0,30 C0,12 14,2 28,2 C38,2 46,8 50,18 C54,8 62,2 72,2 C86,2 100,12 100,30 C100,52 80,70 50,95 Z"),
                ("Lightning Bolt", "M40,0 L70,0 L52,40 L80,40 L28,100 L42,54 L18,54 Z"), ("Moon", "M60,0 A50,50 0 1 0 60,100 A38,50 0 1 1 60,0 Z"),
                ("Smiley Face", "M50,0 A50,50 0 1 1 49.9,0 Z M33,30 A6,6 0 1 0 33.1,30 Z M67,30 A6,6 0 1 0 67.1,30 Z M25,60 C35,80 65,80 75,60 C65,70 35,70 25,60 Z"),
                ("Cube", "M0,25 L25,0 H100 V75 L75,100 H0 Z M0,25 H75 V100 M75,25 L100,0"), ("Can", "M0,15 A50,15 0 0 1 100,15 V85 A50,15 0 0 1 0,85 Z M0,15 A50,15 0 0 0 100,15") }),
            ("Block Arrows", new[] { ("Right Arrow", "M0,25 H60 V0 L100,50 L60,100 V75 H0 Z"), ("Left Arrow", "M100,25 H40 V0 L0,50 L40,100 V75 H100 Z"), ("Up Arrow", "M25,100 V40 H0 L50,0 L100,40 H75 V100 Z"),
                ("Down Arrow", "M25,0 V60 H0 L50,100 L100,60 H75 V0 Z"), ("Left-Right Arrow", "M0,50 L25,0 V25 H75 V0 L100,50 L75,100 V75 H25 V100 Z"), ("Chevron", "M0,0 H70 L100,50 L70,100 H0 L30,50 Z"),
                ("Pentagon Arrow", "M0,0 H70 L100,50 L70,100 H0 Z"), ("Curved Arrow", "M10,100 C10,40 40,20 70,20 V0 L100,35 L70,70 V50 C50,50 35,65 35,100 Z") }),
            ("Flowchart", new[] { ("Process", "M0,10 H100 V90 H0 Z"), ("Decision", "M50,0 L100,50 L50,100 L0,50 Z"), ("Terminator", "M25,10 H75 A25,40 0 0 1 75,90 H25 A25,40 0 0 1 25,10 Z"),
                ("Data", "M20,10 H100 L80,90 H0 Z"), ("Document", "M0,0 H100 V80 C75,60 50,100 25,90 C15,86 8,82 0,80 Z"), ("Connector", "M50,20 A30,30 0 1 1 49.9,20 Z") }),
            ("Stars and Banners", new[] { ("5-Point Star", "M50,0 L61,35 L98,35 L68,57 L79,92 L50,71 L21,92 L32,57 L2,35 L39,35 Z"), ("4-Point Star", "M50,0 L62,38 L100,50 L62,62 L50,100 L38,62 L0,50 L38,38 Z"),
                ("Explosion", "M50,0 L58,28 L86,10 L72,38 L100,46 L72,58 L90,88 L60,72 L50,100 L40,72 L10,88 L28,58 L0,46 L28,38 L14,10 L42,28 Z"), ("Ribbon", "M0,30 H15 V20 H85 V30 H100 L90,50 L100,70 H85 V80 H15 V70 H0 L10,50 Z") }),
            ("Callouts", new[] { ("Rectangular Callout", "M0,0 H100 V70 H45 L20,100 L28,70 H0 Z"), ("Rounded Callout", "M10,0 H90 A10,10 0 0 1 100,10 V60 A10,10 0 0 1 90,70 H45 L20,100 L28,70 H10 A10,10 0 0 1 0,60 V10 A10,10 0 0 1 10,0 Z"),
                ("Oval Callout", "M50,0 A50,35 0 1 1 30,68 L10,100 L22,62 A50,35 0 0 1 50,0 Z"), ("Cloud Callout", "M25,70 C8,70 0,58 4,46 C0,32 12,22 24,26 C28,10 46,4 58,12 C70,2 90,10 90,26 C102,32 100,52 88,58 C92,70 78,78 66,72 C58,80 40,80 34,72 Z M18,86 A5,5 0 1 1 17.9,86 Z M8,96 A3,3 0 1 1 7.9,96 Z") }),
        };

        void ShowShapesGallery()
        {
            var sp = new StackPanel();
            foreach (var (cat, items) in ShapeCatalog)
            {
                sp.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xE7, 0xEE)), Padding = new Thickness(6, 2, 6, 2), Child = new TextBlock { Text = cat, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x42, 0x8B)) } });
                var wrap = new WrapPanel { Margin = new Thickness(4) };
                foreach (var (name, geo) in items)
                {
                    var n = name; var g = geo; bool line = cat == "Lines";
                    var path = new Path { Data = Geometry.Parse(g), Stretch = Stretch.Uniform, Width = 18, Height = 18, Stroke = Brushes.DimGray, StrokeThickness = 1, Fill = line ? null : new SolidColorBrush(Color.FromRgb(0xE8, 0xEE, 0xF7)) };
                    var b = new Button { Style = RB.BtnStyle, Width = 26, Height = 26, Padding = new Thickness(2), HorizontalContentAlignment = HorizontalAlignment.Center, Content = path, ToolTip = n };
                    b.Click += (s, e) => { _shapePopup.IsOpen = false; InsertShapeDialog(n, g, line); };
                    wrap.Children.Add(b);
                }
                sp.Children.Add(wrap);
            }
            _shapePopup = new Popup { PlacementTarget = Mouse.DirectlyOver as UIElement ?? Ribbon, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true,
                Child = new Border { Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Width = 290, Child = sp, Margin = new Thickness(0, 0, 5, 5), Effect = new DropShadowEffect { ShadowDepth = 2, Opacity = 0.35, BlurRadius = 5 } } };
            _shapePopup.IsOpen = true;
        }
        Popup _shapePopup;

        void InsertShapeDialog(string name, string geo, bool line)
        {
            var d = new DialogBase(this, "Format " + name, 360);
            Color fill = Th.Accent1, stroke = Darker2(Th.Accent1, .7), text = Colors.White;
            var fillBtn = ColorButton(() => fill, c => fill = c);
            var strokeBtn = ColorButton(() => stroke, c => stroke = c);
            var textBtn = ColorButton(() => text, c => text = c);
            var noFill = new CheckBox { Content = "No fill", IsChecked = line, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var fillRow = new StackPanel { Orientation = Orientation.Horizontal }; fillRow.Children.Add(fillBtn); fillRow.Children.Add(noFill);
            var weight = D.Box(line ? "3" : "2", 50);
            var w = D.Box("2", 60); var h = D.Box(line ? "2" : "1.5", 60);
            var txt = new TextBox { Width = 210, Height = 46, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, IsEnabled = !line };
            var shadow = new CheckBox { Content = "Shadow", IsChecked = !line };
            var gloss = new CheckBox { Content = "Glossy (Vista style)", IsChecked = !line };
            var preview = new Border { Width = 90, Height = 70, Margin = new Thickness(10, 0, 0, 0), BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Background = Brushes.White };
            FrameworkElement Build(double ww, double hh) => ShapeVisual(geo, line, noFill.IsChecked == true ? (Color?)null : fill, stroke, D.ParseNum(weight.Text) ?? 2, txt.Text, text, shadow.IsChecked == true, gloss.IsChecked == true, ww, hh);
            void Refresh() { double ww = (D.ParseNum(w.Text) ?? 2), hh = (D.ParseNum(h.Text) ?? 1.5); double s = Math.Min(80 / ww, 60 / hh); preview.Child = Build(ww * s, hh * s); }
            foreach (var tb in new[] { weight, w, h, txt }) tb.TextChanged += (s, e) => Refresh();
            foreach (var cb in new[] { noFill, shadow, gloss }) cb.Click += (s, e) => Refresh();
            foreach (var b in new[] { fillBtn, strokeBtn, textBtn }) b.Tag = (Action)Refresh;
            var form = D.Form(("Fill:", fillRow), ("Line color:", strokeBtn), ("Line weight (pt):", weight), ("Width (in):", w), ("Height (in):", h), ("Text:", txt), ("Text color:", textBtn), (null, shadow), (null, gloss));
            var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(form); row.Children.Add(preview);
            d.Content = UI.DialogLayout(row, UI.Strip(UI.Btn("Insert", (s, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
            d.Loaded += (s, e) => Refresh();
            if (d.ShowDialog() != true) return;
            double W = Math.Max(0.2, D.ParseNum(w.Text) ?? 2) * 96, H = Math.Max(0.2, D.ParseNum(h.Text) ?? 1.5) * 96;
            InsertVisual(Build(W, H), W + 8, H + 8);
        }

        Button ColorButton(Func<Color> get, Action<Color> set)
        {
            var sw = new Border { Width = 40, Height = 14, Background = Br(get()), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) };
            var b = new Button { Content = sw, Padding = new Thickness(3), Margin = new Thickness(0, 2, 0, 2), HorizontalAlignment = HorizontalAlignment.Left };
            b.Click += (s, e) => ColorPalette.ShowText(b, null, c => { if (c.HasValue) { set(c.Value); sw.Background = Br(c.Value); (b.Tag as Action)?.Invoke(); } });
            return b;
        }

        static FrameworkElement ShapeVisual(string geo, bool line, Color? fill, Color stroke, double weightPt, string text, Color textColor, bool shadow, bool gloss, double w, double h)
        {
            var grid = new Grid { Width = w + 8, Height = h + 8 };
            var path = new Path { Data = Geometry.Parse(geo), Stretch = Stretch.Fill, Width = w, Height = h, Stroke = Br(stroke), StrokeThickness = weightPt * 96 / 72, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            if (fill.HasValue && !line)
            {
                path.Fill = gloss
                    ? new LinearGradientBrush(new GradientStopCollection { new GradientStop(Lighter(fill.Value, .45), 0), new GradientStop(Lighter(fill.Value, .1), .48), new GradientStop(fill.Value, .52), new GradientStop(Darker2(fill.Value, .85), 1) }, 90)
                    : (Brush)Br(fill.Value);
            }
            if (shadow) path.Effect = new DropShadowEffect { ShadowDepth = 3, BlurRadius = 5, Opacity = 0.4 };
            grid.Children.Add(path);
            if (!string.IsNullOrWhiteSpace(text))
                grid.Children.Add(new TextBlock { Text = text, Foreground = Br(textColor), FontFamily = new FontFamily(Th.Minor), FontSize = 14.67, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = w * 0.8 });
            return grid;
        }

        // ---------------- SmartArt ----------------
        static readonly (string Layout, string Desc)[] SmartArtLayouts =
        {
            ("Basic Block List", "Shows non-sequential or grouped blocks of information."), ("Vertical Bullet List", "Shows blocks of text stacked vertically."),
            ("Basic Process", "Shows a progression or sequential steps in a task, process or workflow."), ("Step Up Process", "Shows a series of steps that build on each other."),
            ("Basic Cycle", "Shows a continuing sequence of stages."), ("Hierarchy", "Shows an organization chart or tree. Indent lines with Tab for lower levels."),
            ("Basic Pyramid", "Shows proportional, interconnected or hierarchical relationships."), ("Basic Venn", "Shows overlapping relationships."), ("Radial", "Shows the relationship to a central idea."),
        };

        void InsertSmartArt()
        {
            var d = new DialogBase(this, "Choose a SmartArt Graphic", 640);
            var list = new ListBox { Width = 180, Height = 300 };
            foreach (var l in SmartArtLayouts) list.Items.Add(l.Layout);
            var desc = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 6), Foreground = Brushes.DimGray };
            var txt = new TextBox { AcceptsReturn = true, AcceptsTab = true, Height = 120, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"),
                Text = "First idea\nSecond idea\nThird idea\nFourth idea" };
            var colors = new ComboBox { Width = 160 };
            foreach (var c in new[] { "Colorful", "Accent 1", "Accent 2", "Accent 3", "Accent 4", "Accent 5", "Accent 6" }) colors.Items.Add(c);
            colors.SelectedIndex = 1;
            var preview = new Border { Height = 150, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Background = Brushes.White, Margin = new Thickness(0, 6, 0, 0) };
            FrameworkElement Build(double w, double h) => SmartArtVisual((string)list.SelectedItem ?? "Basic Block List", txt.Text, colors.SelectedIndex, w, h);
            void Refresh()
            {
                var l = SmartArtLayouts.FirstOrDefault(x => x.Layout == (string)list.SelectedItem);
                desc.Text = l.Desc;
                preview.Child = new Viewbox { Child = Build(420, 260), Margin = new Thickness(4) };
            }
            list.SelectionChanged += (s, e) => Refresh();
            txt.TextChanged += (s, e) => Refresh();
            colors.SelectionChanged += (s, e) => Refresh();
            list.SelectedIndex = 0;
            var right = new StackPanel { Margin = new Thickness(12, 0, 0, 0), Width = 400 };
            right.Children.Add(desc);
            right.Children.Add(new TextBlock { Text = "Type your text (one item per line, Tab to indent):" });
            right.Children.Add(txt);
            var cr = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) }; cr.Children.Add(new TextBlock { Text = "Colors:", Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center }); cr.Children.Add(colors);
            right.Children.Add(cr);
            right.Children.Add(preview);
            var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(list); row.Children.Add(right);
            d.Content = UI.DialogLayout(row, UI.Strip(UI.Btn("OK", (s, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
            d.Loaded += (s, e) => Refresh();
            if (d.ShowDialog() != true) return;
            double W = Math.Min(_page.BodyWidthPx, 576), H = W * 0.6;
            InsertVisual(Build(W, H), W, H);
        }

        static FrameworkElement SmartArtVisual(string layout, string text, int colorMode, double w, double h)
        {
            var items = text.Replace("\r", "").Split('\n').Where(l => l.Trim().Length > 0).Select(l => (Level: l.TakeWhile(ch => ch == '\t' || ch == ' ').Count(ch => ch == '\t') + l.TakeWhile(ch => ch == ' ').Count() / 4, Text: l.Trim())).ToList();
            if (items.Count == 0) items.Add((0, "[Text]"));
            var top = items.Where(i => i.Level == 0).Select(i => i.Text).ToList();
            if (top.Count == 0) top = items.Select(i => i.Text).ToList();
            Color Col(int i) => colorMode == 0 ? Accents[i % 6] : Lighter(Accents[colorMode - 1], Math.Min(0.5, i * 0.08));
            var c = new Canvas { Width = w, Height = h, Background = Brushes.White };
            Border Box(string t, double x, double y, double bw, double bh, Color col, double radius = 6, double font = 14)
            {
                var b = new Border
                {
                    Width = bw, Height = bh, CornerRadius = new CornerRadius(radius),
                    Background = new LinearGradientBrush(Lighter(col, .25), col, 90), BorderBrush = Brushes.White, BorderThickness = new Thickness(1.5),
                    Effect = new DropShadowEffect { ShadowDepth = 2, BlurRadius = 4, Opacity = .3 },
                    Child = new TextBlock { Text = t, Foreground = Brushes.White, FontFamily = new FontFamily(Th.Minor), FontSize = font, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) }
                };
                Canvas.SetLeft(b, x); Canvas.SetTop(b, y); c.Children.Add(b);
                return b;
            }
            void Arrow(double x1, double y1, double x2, double y2, Color col)
            {
                double ang = Math.Atan2(y2 - y1, x2 - x1), len = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
                var p = new Path { Data = Geometry.Parse($"M0,-5 H{Math.Max(4, len - 12):0.#} V-11 L{len:0.#},0 L{Math.Max(4, len - 12):0.#},11 V5 H0 Z"), Fill = Br(Lighter(col, .45)), RenderTransform = new RotateTransform(ang * 180 / Math.PI) };
                Canvas.SetLeft(p, x1); Canvas.SetTop(p, y1); c.Children.Add(p);
            }
            int n = top.Count;
            switch (layout)
            {
                case "Vertical Bullet List":
                    {
                        double bh = Math.Min(60, (h - 10) / n - 8);
                        for (int i = 0; i < n; i++) Box(top[i], 10, 6 + i * (bh + 8), w - 20, bh, Col(i), 4);
                        break;
                    }
                case "Basic Process":
                    {
                        double bw = (w - 20 - (n - 1) * 36) / n, bh = Math.Min(h * 0.5, bw * 0.7), y = (h - bh) / 2;
                        for (int i = 0; i < n; i++)
                        {
                            double x = 10 + i * (bw + 36);
                            Box(top[i], x, y, bw, bh, Col(i));
                            if (i < n - 1) Arrow(x + bw + 6, h / 2, x + bw + 30, h / 2, Col(i));
                        }
                        break;
                    }
                case "Step Up Process":
                    {
                        double bw = (w - 20 - (n - 1) * 8) / n, step = (h - 40) / Math.Max(1, n);
                        for (int i = 0; i < n; i++)
                        {
                            double bh = 40 + step * i;
                            Box(top[i], 10 + i * (bw + 8), h - 10 - bh, bw, bh, Col(i), 3);
                        }
                        break;
                    }
                case "Basic Cycle":
                case "Radial":
                    {
                        bool radial = layout == "Radial";
                        double cx = w / 2, cy = h / 2, r = Math.Min(w, h) / 2 - 40, bs = Math.Min(90, r * 0.9);
                        var outer = radial && items.Count > 1 ? items.Skip(1).Select(i => i.Text).ToList() : top;
                        if (radial)
                        {
                            Box(items[0].Text, cx - bs * 0.6, cy - bs * 0.6, bs * 1.2, bs * 1.2, Col(0), bs * 0.6);
                        }
                        int m = Math.Max(1, outer.Count);
                        for (int i = 0; i < m; i++)
                        {
                            double a = -Math.PI / 2 + i * 2 * Math.PI / m;
                            double x = cx + r * Math.Cos(a), y = cy + r * Math.Sin(a);
                            if (radial)
                            {
                                var ln = new Line { X1 = cx, Y1 = cy, X2 = x, Y2 = y, Stroke = Br(Lighter(Col(0), .4)), StrokeThickness = 3 };
                                c.Children.Insert(0, ln);
                            }
                            else
                            {
                                double a2 = a + Math.PI / m;
                                Arrow(cx + r * 0.95 * Math.Cos(a2 - 0.25), cy + r * 0.95 * Math.Sin(a2 - 0.25), cx + r * 0.95 * Math.Cos(a2 + 0.25), cy + r * 0.95 * Math.Sin(a2 + 0.25), Col(i));
                            }
                            Box(outer[i], x - bs / 2, y - bs * 0.35, bs, bs * 0.7, Col(i + (radial ? 1 : 0)), bs * 0.35, 12);
                        }
                        break;
                    }
                case "Hierarchy":
                    {
                        // level-0 is the root(s); level-1 children under them
                        var root = items[0];
                        var kids = items.Skip(1).Where(i => i.Level >= 1).ToList();
                        if (kids.Count == 0) kids = items.Skip(1).ToList();
                        double bw = Math.Min(140, w / 3), bh = 50;
                        Box(root.Text, (w - bw) / 2, 10, bw, bh, Col(0));
                        int k = Math.Max(1, kids.Count);
                        double kw = Math.Min(130, (w - 20 - (k - 1) * 10) / k), ky = h * 0.55;
                        double total = k * kw + (k - 1) * 10, startX = (w - total) / 2;
                        var line = new Path { Stroke = Br(Lighter(Col(0), .2)), StrokeThickness = 2 };
                        var geo = new System.Text.StringBuilder($"M{w / 2:0.#},{10 + bh:0.#} V{(10 + bh + ky) / 2:0.#}");
                        if (kids.Count > 0) geo.Append($" M{startX + kw / 2:0.#},{(10 + bh + ky) / 2:0.#} H{startX + total - kw / 2:0.#}");
                        for (int i = 0; i < kids.Count; i++) geo.Append($" M{startX + i * (kw + 10) + kw / 2:0.#},{(10 + bh + ky) / 2:0.#} V{ky:0.#}");
                        line.Data = Geometry.Parse(geo.ToString());
                        c.Children.Add(line);
                        for (int i = 0; i < kids.Count; i++) Box(kids[i].Text, startX + i * (kw + 10), ky, kw, bh, Col(i + 1));
                        break;
                    }
                case "Basic Pyramid":
                    {
                        double bandH = (h - 20) / n;
                        for (int i = 0; i < n; i++)
                        {
                            double y0 = 10 + i * bandH, y1 = y0 + bandH - 3;
                            double hw0 = (w * 0.45) * (i / (double)n), hw1 = (w * 0.45) * ((i + 1) / (double)n);
                            var p = new Path { Data = Geometry.Parse($"M{w / 2 - hw0:0.#},{y0:0.#} H{w / 2 + hw0:0.#} L{w / 2 + hw1:0.#},{y1:0.#} H{w / 2 - hw1:0.#} Z"), Fill = new LinearGradientBrush(Lighter(Col(i), .25), Col(i), 90), Stroke = Brushes.White, StrokeThickness = 1.5 };
                            c.Children.Add(p);
                            var tb = new TextBlock { Text = top[i], Foreground = Brushes.White, FontSize = 13, FontFamily = new FontFamily(Th.Minor), Width = w, TextAlignment = TextAlignment.Center };
                            Canvas.SetTop(tb, y0 + bandH / 2 - 10); c.Children.Add(tb);
                        }
                        break;
                    }
                case "Basic Venn":
                    {
                        double r = Math.Min(w / (n + 1.2), h / 2.2) * 0.9;
                        for (int i = 0; i < n; i++)
                        {
                            double a = -Math.PI / 2 + i * 2 * Math.PI / n, cx = w / 2 + (n > 1 ? r * 0.6 * Math.Cos(a) : 0), cy = h / 2 + (n > 1 ? r * 0.6 * Math.Sin(a) : 0);
                            var e = new Ellipse { Width = r * 2, Height = r * 2, Fill = new SolidColorBrush(Color.FromArgb(0x99, Col(i).R, Col(i).G, Col(i).B)), Stroke = Brushes.White, StrokeThickness = 1.5 };
                            Canvas.SetLeft(e, cx - r); Canvas.SetTop(e, cy - r); c.Children.Add(e);
                            var tb = new TextBlock { Text = top[i], Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 13, Width = r * 1.2, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
                            Canvas.SetLeft(tb, cx + (n > 1 ? r * 0.5 * Math.Cos(a) : 0) - r * 0.6); Canvas.SetTop(tb, cy + (n > 1 ? r * 0.5 * Math.Sin(a) : 0) - 10); c.Children.Add(tb);
                        }
                        break;
                    }
                default:
                    {
                        int cols = (int)Math.Ceiling(Math.Sqrt(n * w / h)), rows = (int)Math.Ceiling(n / (double)cols);
                        double bw = (w - 20 - (cols - 1) * 10) / cols, bh = Math.Min((h - 20 - (rows - 1) * 10) / rows, bw * 0.65);
                        double oy = (h - (rows * bh + (rows - 1) * 10)) / 2;
                        for (int i = 0; i < n; i++)
                        {
                            int rr = i / cols, cc = i % cols;
                            int inRow = Math.Min(cols, n - rr * cols);
                            double ox = (w - (inRow * bw + (inRow - 1) * 10)) / 2;
                            Box(top[i], ox + cc * (bw + 10), oy + rr * (bh + 10), bw, bh, Col(i), 4);
                        }
                        break;
                    }
            }
            return c;
        }

        // ---------------- Chart ----------------
        void InsertChart()
        {
            var d = new DialogBase(this, "Insert Chart", 680);
            var types = new ListBox { Width = 130, Height = 250 };
            foreach (var t in new[] { "Column", "Stacked Column", "Bar", "Line", "Pie", "Area", "Scatter", "Doughnut" }) types.Items.Add(t);
            types.SelectedIndex = 0;
            var title = D.Box("Chart Title", 250);
            var data = new TextBox { AcceptsReturn = true, AcceptsTab = true, Height = 120, FontFamily = new FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Text = "\tSeries 1\tSeries 2\tSeries 3\nCategory 1\t4.3\t2.4\t2\nCategory 2\t2.5\t4.4\t2\nCategory 3\t3.5\t1.8\t3\nCategory 4\t4.5\t2.8\t5" };
            var legend = new CheckBox { Content = "Show legend", IsChecked = true };
            var labels = new CheckBox { Content = "Show data labels", IsChecked = false, Margin = new Thickness(10, 0, 0, 0) };
            var preview = new Border { Height = 200, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Background = Brushes.White, Margin = new Thickness(0, 6, 0, 0) };
            FrameworkElement Build(double w, double h) => ChartVisual((string)types.SelectedItem, title.Text, data.Text, legend.IsChecked == true, labels.IsChecked == true, w, h);
            void Refresh() { try { preview.Child = new Viewbox { Child = Build(480, 300) }; } catch { } }
            types.SelectionChanged += (s, e) => Refresh(); title.TextChanged += (s, e) => Refresh(); data.TextChanged += (s, e) => Refresh();
            legend.Click += (s, e) => Refresh(); labels.Click += (s, e) => Refresh();
            var right = new StackPanel { Margin = new Thickness(12, 0, 0, 0), Width = 500 };
            right.Children.Add(D.Form(("Title:", title)));
            right.Children.Add(new TextBlock { Text = "Data (Tab or comma separated; first row = series, first column = categories):", Margin = new Thickness(0, 6, 0, 2) });
            right.Children.Add(data);
            var opts = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) }; opts.Children.Add(legend); opts.Children.Add(labels);
            right.Children.Add(opts);
            right.Children.Add(preview);
            var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(types); row.Children.Add(right);
            d.Content = UI.DialogLayout(row, UI.Strip(UI.Btn("OK", (s, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
            d.Loaded += (s, e) => Refresh();
            if (d.ShowDialog() != true) return;
            double W = Math.Min(_page.BodyWidthPx, 576), H = W * 0.6;
            InsertVisual(Build(W, H), W, H);
        }

        static FrameworkElement ChartVisual(string type, string title, string raw, bool legend, bool labels, double w, double h)
        {
            var rows = raw.Replace("\r", "").Split('\n').Where(l => l.Trim().Length > 0).Select(l => l.Split(l.Contains('\t') ? '\t' : ',')).ToList();
            var series = rows.Count > 0 ? rows[0].Skip(1).Select(s => s.Trim()).ToList() : new List<string>();
            var cats = rows.Skip(1).Select(r => r[0].Trim()).ToList();
            var vals = rows.Skip(1).Select(r => series.Select((s, i) => i + 1 < r.Length && double.TryParse(r[i + 1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0).ToArray()).ToList();
            int S = series.Count, N = cats.Count;
            var c = new Canvas { Width = w, Height = h, Background = Brushes.White };
            var font = new FontFamily(Th.Minor);
            void Text(string t, double x, double y, double size = 11, bool center = false, Brush fg = null, double width = 0, FontWeight? weight = null)
            {
                var tb = new TextBlock { Text = t, FontSize = size, FontFamily = font, Foreground = fg ?? Brushes.Black };
                if (weight.HasValue) tb.FontWeight = weight.Value;
                if (center) { tb.Width = width; tb.TextAlignment = TextAlignment.Center; }
                Canvas.SetLeft(tb, x); Canvas.SetTop(tb, y); c.Children.Add(tb);
            }
            double top = 8;
            if (!string.IsNullOrWhiteSpace(title)) { Text(title, 0, 4, 16, true, null, w, FontWeights.Bold); top = 32; }
            double legendW = legend && S > 0 ? 100 : 0;
            if (legend && S > 0 && (type == "Pie" || type == "Doughnut"))
            {
                for (int i = 0; i < N; i++) { var r = new Rectangle { Width = 10, Height = 10, Fill = Br(Accents[i % 6]) }; Canvas.SetLeft(r, w - legendW + 6); Canvas.SetTop(r, top + 10 + i * 18); c.Children.Add(r); Text(cats[i], w - legendW + 20, top + 6 + i * 18); }
            }
            else if (legend)
            {
                for (int i = 0; i < S; i++) { var r = new Rectangle { Width = 10, Height = 10, Fill = Br(Accents[i % 6]) }; Canvas.SetLeft(r, w - legendW + 6); Canvas.SetTop(r, top + 10 + i * 18); c.Children.Add(r); Text(series[i], w - legendW + 20, top + 6 + i * 18); }
            }
            if (N == 0 || S == 0) return c;
            double L = 40, R = w - legendW - 10, T = top + 6, B = h - 26;
            if (type == "Pie" || type == "Doughnut")
            {
                double total = vals.Sum(v => Math.Max(0, v[0])); if (total <= 0) total = 1;
                double cx = (L - 30 + R) / 2, cy = (T + B + 20) / 2, r = Math.Min(R - L, B - T + 20) / 2 - 4, a0 = -Math.PI / 2;
                for (int i = 0; i < N; i++)
                {
                    double frac = Math.Max(0, vals[i][0]) / total, a1 = a0 + frac * 2 * Math.PI;
                    if (frac >= 0.9999) { var e = new Ellipse { Width = r * 2, Height = r * 2, Fill = Br(Accents[i % 6]) }; Canvas.SetLeft(e, cx - r); Canvas.SetTop(e, cy - r); c.Children.Add(e); }
                    else if (frac > 0)
                    {
                        var fig = new PathFigure { StartPoint = new Point(cx, cy), IsClosed = true };
                        fig.Segments.Add(new LineSegment(new Point(cx + r * Math.Cos(a0), cy + r * Math.Sin(a0)), true));
                        fig.Segments.Add(new ArcSegment(new Point(cx + r * Math.Cos(a1), cy + r * Math.Sin(a1)), new Size(r, r), 0, frac > 0.5, SweepDirection.Clockwise, true));
                        c.Children.Add(new Path { Data = new PathGeometry(new[] { fig }), Fill = Br(Accents[i % 6]), Stroke = Brushes.White, StrokeThickness = 1.5 });
                    }
                    if (labels && frac > 0.03) { double am = (a0 + a1) / 2; Text((frac * 100).ToString("0") + "%", cx + r * 0.65 * Math.Cos(am) - 14, cy + r * 0.65 * Math.Sin(am) - 8, 11, false, Brushes.White); }
                    a0 = a1;
                }
                if (type == "Doughnut") { var hole = new Ellipse { Width = r, Height = r, Fill = Brushes.White }; Canvas.SetLeft(hole, cx - r / 2); Canvas.SetTop(hole, cy - r / 2); c.Children.Add(hole); }
                return c;
            }
            bool stacked = type == "Stacked Column";
            double max = stacked ? vals.Max(v => v.Where(x => x > 0).Sum()) : vals.SelectMany(v => v).DefaultIfEmpty(0).Max();
            double min = Math.Min(0, vals.SelectMany(v => v).DefaultIfEmpty(0).Min());
            if (max <= min) max = min + 1;
            double stepV = NiceStep((max - min) / 5); max = Math.Ceiling(max / stepV) * stepV; min = Math.Floor(min / stepV) * stepV;
            bool horiz = type == "Bar";
            Func<double, double> Y = v => B - (v - min) / (max - min) * (B - T);
            Func<double, double> X = v => L + (v - min) / (max - min) * (R - L);
            // gridlines + axis labels
            for (double v = min; v <= max + stepV / 2; v += stepV)
            {
                if (horiz) { c.Children.Add(new Line { X1 = X(v), X2 = X(v), Y1 = T, Y2 = B, Stroke = Br("D9D9D9") }); Text(v.ToString("0.##"), X(v) - 10, B + 2, 10); }
                else { c.Children.Add(new Line { X1 = L, X2 = R, Y1 = Y(v), Y2 = Y(v), Stroke = Br("D9D9D9") }); Text(v.ToString("0.##"), 2, Y(v) - 7, 10); }
            }
            c.Children.Add(new Line { X1 = L, X2 = horiz ? L : R, Y1 = horiz ? T : B, Y2 = B, Stroke = Br("868686") });
            if (type == "Scatter")
            {
                for (int s = 0; s < S; s++)
                    for (int i = 0; i < N; i++)
                    {
                        double xv = double.TryParse(cats[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var cx) ? cx : i + 1;
                        double px = L + (xv - 0) / Math.Max(1, N + 1) * (R - L) * (cx != 0 ? 1 : 1);
                        if (double.TryParse(cats[i], NumberStyles.Float, CultureInfo.InvariantCulture, out _)) px = L + (i + 1) / (double)(N + 1) * (R - L);
                        else px = L + (i + 1) / (double)(N + 1) * (R - L);
                        var e = new Ellipse { Width = 8, Height = 8, Fill = Br(Accents[s % 6]) }; Canvas.SetLeft(e, px - 4); Canvas.SetTop(e, Y(vals[i][s]) - 4); c.Children.Add(e);
                    }
                for (int i = 0; i < N; i++) Text(cats[i], L + (i + 1) / (double)(N + 1) * (R - L) - 30, B + 2, 10, true, null, 60);
                return c;
            }
            double slot = (horiz ? (B - T) : (R - L)) / N;
            for (int i = 0; i < N; i++)
            {
                if (horiz) Text(cats[i], 0, T + slot * i + slot / 2 - 7, 10);
                else Text(cats[i], L + slot * i, B + 4, 10, true, null, slot);
            }
            if (type == "Line" || type == "Area")
            {
                for (int s = 0; s < S; s++)
                {
                    var pts = Enumerable.Range(0, N).Select(i => new Point(L + slot * i + slot / 2, Y(vals[i][s]))).ToList();
                    if (type == "Area")
                    {
                        var fig = new PathFigure { StartPoint = new Point(pts[0].X, Y(Math.Max(0, min))), IsClosed = true };
                        foreach (var p in pts) fig.Segments.Add(new LineSegment(p, true));
                        fig.Segments.Add(new LineSegment(new Point(pts[N - 1].X, Y(Math.Max(0, min))), true));
                        var col = Accents[s % 6];
                        c.Children.Add(new Path { Data = new PathGeometry(new[] { fig }), Fill = new SolidColorBrush(Color.FromArgb(0xA0, col.R, col.G, col.B)) });
                    }
                    else
                    {
                        c.Children.Add(new Polyline { Points = new PointCollection(pts), Stroke = Br(Accents[s % 6]), StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round });
                        foreach (var p in pts) { var e = new Ellipse { Width = 7, Height = 7, Fill = Br(Accents[s % 6]) }; Canvas.SetLeft(e, p.X - 3.5); Canvas.SetTop(e, p.Y - 3.5); c.Children.Add(e); }
                    }
                    if (labels) for (int i = 0; i < N; i++) Text(vals[i][s].ToString("0.##"), pts[i].X - 8, pts[i].Y - 18, 10);
                }
                return c;
            }
            for (int i = 0; i < N; i++)
            {
                double acc = 0;
                double gap = slot * 0.2, bw = stacked ? slot - 2 * gap : (slot - 2 * gap) / S;
                for (int s = 0; s < S; s++)
                {
                    double v = vals[i][s];
                    Rectangle r;
                    var col = Accents[s % 6];
                    var fill = new LinearGradientBrush(Lighter(col, .25), col, horiz ? 90 : 0);
                    if (horiz)
                    {
                        double y0 = T + slot * i + gap + s * bw;
                        double x0 = X(Math.Min(0, v)), x1 = X(Math.Max(0, v));
                        r = new Rectangle { Width = Math.Max(0, x1 - x0), Height = bw - 1, Fill = fill }; Canvas.SetLeft(r, x0); Canvas.SetTop(r, y0);
                        if (labels) Text(v.ToString("0.##"), x1 + 2, y0 + bw / 2 - 7, 10);
                    }
                    else
                    {
                        double x0 = L + slot * i + gap + (stacked ? 0 : s * bw);
                        double y0 = stacked ? Y(acc + v) : Y(Math.Max(0, v)), y1 = stacked ? Y(acc) : Y(Math.Min(0, v));
                        r = new Rectangle { Width = bw - 1, Height = Math.Max(0, y1 - y0), Fill = fill }; Canvas.SetLeft(r, x0); Canvas.SetTop(r, y0);
                        if (labels) Text(v.ToString("0.##"), x0, y0 - 15, 10, true, null, bw);
                        acc += v;
                    }
                    c.Children.Add(r);
                }
            }
            return c;
        }

        static double NiceStep(double raw)
        {
            if (raw <= 0) return 1;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(raw))), f = raw / mag;
            return (f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10) * mag;
        }

        // =====================================================================================
        //  Links: bookmarks and cross-references
        // =====================================================================================
        void BookmarkDialog()
        {
            var d = new DialogBase(this, "Bookmark", 360);
            var name = D.Box("", 300);
            var list = new ListBox { Height = 160, Width = 300 };
            var byName = new RadioButton { Content = "Name", IsChecked = true, Margin = new Thickness(0, 0, 10, 0) };
            var byLoc = new RadioButton { Content = "Location" };
            var hidden = new CheckBox { Content = "Hidden bookmarks", Margin = new Thickness(0, 4, 0, 0) };
            void Fill()
            {
                var all = Spans.WithTag<BookmarkInfo>(Editor.Document).Where(s => hidden.IsChecked == true || !((BookmarkInfo)s.Tag).Name.StartsWith("_")).ToList();
                var names = byName.IsChecked == true ? all.Select(s => ((BookmarkInfo)s.Tag).Name).OrderBy(x => x) : all.Select(s => ((BookmarkInfo)s.Tag).Name);
                list.ItemsSource = names.ToList();
            }
            byName.Click += (s, e) => Fill(); byLoc.Click += (s, e) => Fill(); hidden.Click += (s, e) => Fill();
            list.SelectionChanged += (s, e) => { if (list.SelectedItem is string n) name.Text = n; };
            var sort = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) }; sort.Children.Add(new TextBlock { Text = "Sort by:", Margin = new Thickness(0, 0, 8, 0) }); sort.Children.Add(byName); sort.Children.Add(byLoc);
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = "Bookmark name:" }); sp.Children.Add(name); sp.Children.Add(list); sp.Children.Add(sort); sp.Children.Add(hidden);
            var add = UI.Btn("Add", (s, e) =>
            {
                var n = name.Text.Trim();
                if (n.Length == 0 || !System.Text.RegularExpressions.Regex.IsMatch(n, @"^[A-Za-z_][A-Za-z0-9_]*$")) { VistaDialog.Error(d, "Bookmark names must start with a letter and can contain only letters, numbers and underscores."); return; }
                foreach (var old in Spans.WithTag<BookmarkInfo>(Editor.Document).Where(x => ((BookmarkInfo)x.Tag).Name == n).ToList()) Spans.Unwrap(old);
                AddBookmark(n, Editor.Selection.Start, Editor.Selection.End);
                d.DialogResult = true;
            }, true);
            var del = UI.Btn("Delete", (s, e) => { if (list.SelectedItem is string n) { foreach (var old in Spans.WithTag<BookmarkInfo>(Editor.Document).Where(x => ((BookmarkInfo)x.Tag).Name == n).ToList()) Spans.Unwrap(old); MarkDirty(); Fill(); } });
            var go = UI.Btn("Go To", (s, e) => { if (list.SelectedItem is string n) FollowLink(new Uri("#" + n, UriKind.Relative)); });
            d.Content = UI.DialogLayout(sp, UI.Strip(add, del, go, UI.Btn("Close", null, false, true)));
            Fill();
            d.ShowDialog();
        }

        Span AddBookmark(string name, TextPointer start, TextPointer end)
        {
            Editor.BeginChange();
            Span s;
            try
            {
                if (start.Paragraph != null && start.Paragraph == end.Paragraph && start.CompareTo(end) < 0) s = new Span(start, end);
                else { s = new Span(new Run(""), start.GetInsertionPosition(LogicalDirection.Forward)); }
                s.Tag = new BookmarkInfo { Name = name };
            }
            finally { Editor.EndChange(); }
            MarkDirty();
            return s;
        }

        /// <summary>Makes sure a paragraph is wrapped in a hidden bookmark and returns its name.</summary>
        string EnsureParagraphBookmark(Paragraph p)
        {
            var existing = p.Inlines.OfType<Span>().FirstOrDefault(s => s.Tag is BookmarkInfo bi && bi.Name.StartsWith("_Ref"));
            if (existing != null) return ((BookmarkInfo)existing.Tag).Name;
            var name = "_Ref" + new Random().Next(10000000, 99999999);
            var start = p.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
            var end = p.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
            if (start.CompareTo(end) >= 0) return null;
            var s = new Span(start, end) { Tag = new BookmarkInfo { Name = name } };
            return name;
        }

        void CrossReference()
        {
            var d = new DialogBase(this, "Cross-reference", 470);
            var type = new ComboBox { Width = 180 };
            foreach (var t in new[] { "Heading", "Bookmark", "Figure", "Table", "Footnote", "Endnote" }) type.Items.Add(t);
            type.SelectedIndex = 0;
            var what = new ComboBox { Width = 200 };
            var asLink = new CheckBox { Content = "Insert as hyperlink", IsChecked = true, Margin = new Thickness(0, 6, 0, 0) };
            var list = new ListBox { Height = 180 };
            var items = new List<(string Label, Func<string> Anchor, Func<TextPointer> Pos, string Text)>();
            void Fill()
            {
                items.Clear();
                string kind = (string)type.SelectedItem;
                what.Items.Clear();
                if (kind == "Heading") foreach (var x in new[] { "Heading text", "Page number", "Above/below" }) what.Items.Add(x);
                else if (kind == "Bookmark") foreach (var x in new[] { "Bookmark text", "Page number", "Above/below" }) what.Items.Add(x);
                else if (kind == "Footnote" || kind == "Endnote") foreach (var x in new[] { "Note number", "Page number" }) what.Items.Add(x);
                else foreach (var x in new[] { "Entire caption", "Only label and number", "Page number" }) what.Items.Add(x);
                what.SelectedIndex = 0;
                switch (kind)
                {
                    case "Heading":
                        foreach (var p in Fmt.AllParagraphs(Editor.Document.Blocks).Where(p => (p.Tag as string)?.StartsWith("Heading") == true))
                        {
                            var para = p; var lvl = int.TryParse(((string)p.Tag).Substring(7), out var lv) ? lv : 1;
                            items.Add((new string(' ', (lvl - 1) * 3) + Spans.Text(p), () => EnsureParagraphBookmark(para), () => para.ContentStart, Spans.Text(p)));
                        }
                        break;
                    case "Bookmark":
                        foreach (var s in Spans.WithTag<BookmarkInfo>(Editor.Document).Where(s => !((BookmarkInfo)s.Tag).Name.StartsWith("_")))
                        {
                            var sp = s; var n = ((BookmarkInfo)s.Tag).Name;
                            items.Add((n, () => n, () => sp.ContentStart, Spans.Text(sp)));
                        }
                        break;
                    case "Figure":
                    case "Table":
                        foreach (var p in Fmt.AllParagraphs(Editor.Document.Blocks).Where(p => (p.Tag as string) == "Caption" && Spans.Text(p).StartsWith(kind)))
                        {
                            var para = p;
                            items.Add((Spans.Text(p), () => EnsureParagraphBookmark(para), () => para.ContentStart, Spans.Text(p)));
                        }
                        break;
                    default:
                        bool en = kind == "Endnote";
                        foreach (var s in Spans.WithTag<NoteRef>(Editor.Document).Where(s => ((NoteRef)s.Tag).Endnote == en))
                        {
                            var sp = s; var nr = (NoteRef)s.Tag;
                            var body = NotesSection()?.Blocks.OfType<Paragraph>().FirstOrDefault(p => p.Tag is NoteBody nb && nb.Endnote == en && nb.Number == nr.Number);
                            items.Add(($"{nr.Number}  {(body == null ? "" : Spans.Text(body).Trim())}", () => null, () => sp.ContentStart, Spans.Text(sp)));
                        }
                        break;
                }
                list.ItemsSource = items.Select(i => i.Label).ToList();
                if (items.Count > 0) list.SelectedIndex = 0;
            }
            type.SelectionChanged += (s, e) => Fill();
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            var c1 = new StackPanel(); c1.Children.Add(new TextBlock { Text = "Reference type:" }); c1.Children.Add(type);
            var c2 = new StackPanel { Margin = new Thickness(16, 0, 0, 0) }; c2.Children.Add(new TextBlock { Text = "Insert reference to:" }); c2.Children.Add(what);
            top.Children.Add(c1); top.Children.Add(c2);
            var sp2 = new StackPanel(); sp2.Children.Add(top); sp2.Children.Add(asLink); sp2.Children.Add(new TextBlock { Text = "For which item:", Margin = new Thickness(0, 6, 0, 2) }); sp2.Children.Add(list);
            var insert = UI.Btn("Insert", (s, e) =>
            {
                if (list.SelectedIndex < 0) return;
                var it = items[list.SelectedIndex];
                string mode = (string)what.SelectedItem ?? "";
                var pos = it.Pos();
                string text = mode.StartsWith("Page") ? PageOf(pos).ToString()
                    : mode == "Above/below" ? (pos.CompareTo(Editor.CaretPosition) < 0 ? "above" : "below")
                    : mode == "Only label and number" ? string.Join(" ", it.Text.Split(' ').Take(2)).TrimEnd(':', '.')
                    : it.Text.Trim();
                var anchor = it.Anchor();
                Editor.BeginChange();
                if (!Editor.Selection.IsEmpty) Editor.Selection.Text = "";
                var ip = Editor.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
                Inline inl;
                if (asLink.IsChecked == true && anchor != null) inl = new Hyperlink(new Run(text), ip) { NavigateUri = new Uri("#" + anchor, UriKind.Relative) };
                else inl = new Span(new Run(text), ip) { Tag = new FieldInfo { Code = mode.StartsWith("Page") ? $"PAGEREF {anchor} \\h" : $"REF {anchor} \\h" } };
                Editor.CaretPosition = inl.ElementEnd;
                Editor.EndChange();
                MarkDirty();
            }, true);
            d.Content = UI.DialogLayout(sp2, UI.Strip(insert, UI.Btn("Close", null, false, true)));
            Fill();
            d.ShowDialog();
        }

        // =====================================================================================
        //  Header, footer, page numbers
        // =====================================================================================
        ContextMenu HeaderFooterMenu(bool header)
        {
            string W = header ? "Header" : "Footer";
            var presets = new (string Name, string L, string C, string R)[]
            {
                ("Blank", "", "[Type text]", ""), ("Blank (Three Columns)", "[Type text]", "[Type text]", "[Type text]"), ("Title", "", "{TITLE}", ""),
                ("Title and Date", "{TITLE}", "", "{DATE}"), ("Author and Page", "{AUTHOR}", "", "Page {PAGE}"), ("Page X of Y", "", "Page {PAGE} of {NUMPAGES}", ""),
                ("Title Right, Page Left", "{PAGE}", "", "{TITLE}"), ("Confidential", "{AUTHOR}", "CONFIDENTIAL", "{DATE}"),
            };
            var m = RB.Menu();
            m.Items.Add(new MenuItem { Header = "Built-In", IsEnabled = false, FontWeight = FontWeights.Bold });
            foreach (var p in presets)
            {
                var pr = p;
                var mi = new MenuItem { Header = HeaderPresetPreview(pr.Name, pr.L, pr.C, pr.R) };
                mi.Click += (s, e) => { SetHeaderFooter(header, pr.L, pr.C, pr.R); };
                m.Items.Add(mi);
            }
            m.Items.Add(new Separator());
            m.Items.Add(RB.MI("Edit " + W + "...", () => EditHeaderFooter(header), header ? "IconHeader" : "IconFooter"));
            m.Items.Add(RB.MI("Remove " + W, () => SetHeaderFooter(header, "", "", "")));
            return m;
        }

        UIElement HeaderPresetPreview(string name, string l, string c, string r)
        {
            var sp = new StackPanel { Width = 260 };
            sp.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold });
            var g = new Grid { Height = 20, Background = Brushes.White, Margin = new Thickness(0, 2, 0, 2) };
            string Prev(string t) => DecoratedPaginator.Expand(t, 1, 3, _docTitle ?? DisplayName, _page);
            g.Children.Add(new TextBlock { Text = Prev(l), FontSize = 9, Foreground = Brushes.DimGray, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 4, 0, 0) });
            g.Children.Add(new TextBlock { Text = Prev(c), FontSize = 9, Foreground = Brushes.DimGray, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
            g.Children.Add(new TextBlock { Text = Prev(r), FontSize = 9, Foreground = Brushes.DimGray, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 4, 0) });
            sp.Children.Add(new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Child = g });
            return sp;
        }

        void SetHeaderFooter(bool header, string l, string c, string r)
        {
            if (header) { _page.HeaderLeft = l; _page.HeaderText = c; _page.HeaderRight = r; }
            else { _page.FooterLeft = l; _page.FooterCenter = c; _page.FooterRight = r; }
            PageChanged();
        }

        void EditHeaderFooter(bool header)
        {
            var d = new DialogBase(this, header ? "Header" : "Footer", 560);
            var l = D.Box(header ? _page.HeaderLeft : _page.FooterLeft, 160);
            var c = D.Box(header ? _page.HeaderText : _page.FooterCenter, 160);
            var r = D.Box(header ? _page.HeaderRight : _page.FooterRight, 160);
            TextBox last = c;
            foreach (var tb in new[] { l, c, r }) { var t = tb; t.GotFocus += (s, e) => last = t; }
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (lab, tb) in new[] { ("Left", l), ("Center", c), ("Right", r) })
            {
                var col = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
                col.Children.Add(new TextBlock { Text = lab }); col.Children.Add(tb);
                row.Children.Add(col);
            }
            var fields = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var (lab, tok) in new[] { ("Page Number", "{PAGE}"), ("Number of Pages", "{NUMPAGES}"), ("Date", "{DATE}"), ("Title", "{TITLE}"), ("Author", "{AUTHOR}") })
            {
                var t = tok;
                var b = UI.Btn(lab, (s, e) => { var i = last.CaretIndex; last.Text = last.Text.Insert(i, t); last.CaretIndex = i + t.Length; last.Focus(); });
                b.Margin = new Thickness(0, 0, 6, 4); b.MinWidth = 0; b.Padding = new Thickness(8, 2, 8, 2);
                fields.Children.Add(b);
            }
            var sp = new StackPanel();
            sp.Children.Add(row);
            sp.Children.Add(new TextBlock { Text = "Insert a field at the cursor:", Margin = new Thickness(0, 10, 0, 0) });
            sp.Children.Add(fields);
            d.Content = UI.DialogLayout(sp, UI.Strip(UI.Btn("OK", (s, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
            if (d.ShowDialog() == true) SetHeaderFooter(header, l.Text, c.Text, r.Text);
        }

        ContextMenu PageNumberMenu()
        {
            var m = RB.Menu();
            foreach (var (where, header) in new[] { ("Top of Page", true), ("Bottom of Page", false) })
            {
                var sub = new MenuItem { Header = where };
                foreach (var (lab, slot) in new[] { ("Plain Number 1 (left)", 0), ("Plain Number 2 (center)", 1), ("Plain Number 3 (right)", 2), ("Page X of Y (center)", 3), ("Accent Bar (right)", 4) })
                {
                    var h = header; var sl = slot;
                    sub.Items.Add(RB.MI(lab, () => SetPageNumber(h, sl)));
                }
                m.Items.Add(sub);
            }
            var margins = new MenuItem { Header = "Page Margins" };
            margins.Items.Add(RB.MI("Right Margin, Top", () => SetPageNumber(true, 5)));
            margins.Items.Add(RB.MI("Right Margin, Bottom", () => SetPageNumber(false, 5)));
            m.Items.Add(margins);
            m.Items.Add(RB.MI("Current Position", () => InsertField("PAGE")));
            m.Items.Add(new Separator());
            m.Items.Add(RB.MI("Format Page Numbers...", FormatPageNumbers));
            m.Items.Add(RB.MI("Remove Page Numbers", () => { _page.PageNumbers = false; PageChanged(); }, "IconDelete"));
            return m;
        }

        void SetPageNumber(bool header, int slot)
        {
            string tok = slot == 3 ? "Page {PAGE} of {NUMPAGES}" : slot == 4 ? "{PAGE} | Page" : slot == 5 ? "Page {PAGE}" : "{PAGE}";
            string l = "", c = "", r = "";
            if (slot == 0) l = tok; else if (slot == 1 || slot == 3) c = tok; else r = tok;
            if (header) { _page.HeaderLeft = l == "" ? _page.HeaderLeft : l; _page.HeaderText = c == "" ? _page.HeaderText : c; _page.HeaderRight = r == "" ? _page.HeaderRight : r; }
            else { _page.FooterLeft = l == "" ? _page.FooterLeft : l; _page.FooterCenter = c == "" ? _page.FooterCenter : c; _page.FooterRight = r == "" ? _page.FooterRight : r; }
            PageChanged();
        }

        void FormatPageNumbers()
        {
            var d = new DialogBase(this, "Page Number Format", 320);
            var fmt = new ComboBox { Width = 150 };
            var formats = new[] { ("1", "1, 2, 3, ..."), ("-1-", "- 1 -, - 2 -, ..."), ("a", "a, b, c, ..."), ("A", "A, B, C, ..."), ("i", "i, ii, iii, ..."), ("I", "I, II, III, ...") };
            foreach (var f in formats) fmt.Items.Add(f.Item2);
            fmt.SelectedIndex = Math.Max(0, Array.FindIndex(formats, f => f.Item1 == _page.NumFormat));
            var start = D.Box(_page.NumStart.ToString(), 60);
            d.Content = UI.DialogLayout(D.Form(("Number format:", fmt), ("Start at:", start)), UI.Strip(UI.Btn("OK", (s, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
            if (d.ShowDialog() != true) return;
            _page.NumFormat = formats[Math.Max(0, fmt.SelectedIndex)].Item1;
            _page.NumStart = (int)(D.ParseNum(start.Text) ?? 1);
            PageChanged();
        }

        // =====================================================================================
        //  Text: text boxes, quick parts, fields, WordArt, drop cap, signature line
        // =====================================================================================
        ContextMenu TextBoxMenu()
        {
            var m = RB.Menu();
            m.Items.Add(new MenuItem { Header = "Built-In", IsEnabled = false, FontWeight = FontWeights.Bold });
            foreach (var n in new[] { "Simple Text Box", "Shaded Sidebar", "Pull Quote", "Accent Bar Quote", "Rounded Annotation" })
            {
                var name = n;
                m.Items.Add(RB.MI(name, () => InsertTextBox(name), "IconTextBox"));
            }
            m.Items.Add(new Separator());
            m.Items.Add(RB.MI("Draw Text Box", () => InsertTextBox("Simple Text Box")));
            m.Items.Add(RB.MI("Save Selection to Text Box Gallery...", SaveQuickPart));
            return m;
        }

        void InsertTextBox(string kind)
        {
            var a1 = Th.Accent1;
            string text = kind == "Pull Quote" || kind == "Accent Bar Quote" ? "[Type a quote from the document or the summary of an interesting point. You can position the text box anywhere in the document.]"
                : kind == "Shaded Sidebar" ? "[Type sidebar content. A sidebar is a standalone supplement to the main document.]" : "[Type text]";
            if (!Editor.Selection.IsEmpty && Editor.Selection.Start.Paragraph == Editor.Selection.End.Paragraph) text = Editor.Selection.Text;
            double width = kind == "Shaded Sidebar" ? _page.BodyWidthPx * 0.45 : kind == "Simple Text Box" ? _page.BodyWidthPx * 0.5 : _page.BodyWidthPx * 0.7;
            var t = new Table { CellSpacing = 0, Margin = new Thickness(kind == "Pull Quote" ? (_page.BodyWidthPx - width) / 2 : 0, 6, 0, 12), Tag = "TextBox" };
            t.Columns.Add(new TableColumn { Width = new GridLength(width) });
            var p = NewCellParagraph();
            p.Inlines.Add(new Run(text));
            var cell = new TableCell(p) { Padding = new Thickness(10, 8, 10, 8), BorderBrush = Brushes.Black, BorderThickness = new Thickness(0.75) };
            switch (kind)
            {
                case "Shaded Sidebar": cell.Background = Br(Lighter(a1, .75)); cell.BorderBrush = Br(a1); cell.BorderThickness = new Thickness(0, 3, 0, 3); break;
                case "Pull Quote": cell.BorderBrush = Br(a1); cell.BorderThickness = new Thickness(0, 2, 0, 2); p.FontStyle = FontStyles.Italic; p.FontSize = 14 * Fmt.PxPerPt; p.Foreground = Br(Darker2(a1, .8)); p.TextAlignment = TextAlignment.Center; break;
                case "Accent Bar Quote": cell.BorderBrush = Br(a1); cell.BorderThickness = new Thickness(6, 0, 0, 0); cell.Background = Br(Lighter(a1, .88)); p.FontStyle = FontStyles.Italic; p.FontSize = 13 * Fmt.PxPerPt; break;
                case "Rounded Annotation": cell.BorderBrush = Br(Th.Accent6); cell.BorderThickness = new Thickness(1.5); cell.Background = Br(Lighter(Th.Accent6, .8)); break;
            }
            var g = new TableRowGroup(); var row = new TableRow(); row.Cells.Add(cell); g.Rows.Add(row); t.RowGroups.Add(g);
            if (!Editor.Selection.IsEmpty && Editor.Selection.Start.Paragraph == Editor.Selection.End.Paragraph) Editor.Selection.Text = "";
            InsertBlocksAtCaret(t);
            Editor.Selection.Select(p.ContentStart, p.ContentEnd);
            Editor.Focus();
        }

        static string QuickPartsDir
        {
            get
            {
                var d = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Valco", "Valco Word", "Quick Parts");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        ContextMenu QuickPartsMenu()
        {
            var m = RB.Menu();
            var parts = Directory.GetFiles(QuickPartsDir, "*.vqp").OrderBy(f => f).ToList();
            if (parts.Count > 0)
            {
                m.Items.Add(new MenuItem { Header = "General", IsEnabled = false, FontWeight = FontWeights.Bold });
                foreach (var f in parts)
                {
                    var file = f;
                    m.Items.Add(RB.MI(System.IO.Path.GetFileNameWithoutExtension(f), () => InsertQuickPart(file), "IconQuickParts"));
                }
                m.Items.Add(new Separator());
            }
            var props = new MenuItem { Header = "Document Property" };
            foreach (var (lab, code) in new[] { ("Author", "AUTHOR"), ("Title", "TITLE"), ("Company", "DOCPROPERTY Company"), ("Subject", "SUBJECT"), ("Keywords", "KEYWORDS"), ("Publish Date", "SAVEDATE \\@ \"MMMM d, yyyy\""), ("File Name", "FILENAME") })
            {
                var c = code;
                props.Items.Add(RB.MI(lab, () => InsertField(c)));
            }
            m.Items.Add(props);
            m.Items.Add(RB.MI("Field...", FieldDialog, "IconQuickParts"));
            m.Items.Add(RB.MI("Building Blocks Organizer...", BuildingBlocksOrganizer));
            m.Items.Add(new Separator());
            m.Items.Add(RB.MI("Save Selection to Quick Part Gallery...", SaveQuickPart));
            return m;
        }

        void SaveQuickPart()
        {
            if (Editor.Selection.IsEmpty) { Flash("Select the content you want to save first."); return; }
            var suggestion = new string(Editor.Selection.Text.Trim().Take(24).Where(ch => !System.IO.Path.GetInvalidFileNameChars().Contains(ch)).ToArray()).Trim();
            var name = InputDialog.Ask(this, "Create New Building Block", "Name:", suggestion.Length > 0 ? suggestion : "Quick Part");
            if (string.IsNullOrWhiteSpace(name)) return;
            name = new string(name.Where(ch => !System.IO.Path.GetInvalidFileNameChars().Contains(ch)).ToArray()).Trim();
            try
            {
                using var fs = File.Create(System.IO.Path.Combine(QuickPartsDir, name + ".vqp"));
                Editor.Selection.Save(fs, DataFormats.XamlPackage);
                Flash("Saved \"" + name + "\" to the Quick Part Gallery.");
            }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message, "The Quick Part couldn't be saved."); }
        }

        void InsertQuickPart(string file)
        {
            try
            {
                using var fs = File.OpenRead(file);
                Editor.BeginChange();
                Editor.Selection.Load(fs, DataFormats.XamlPackage);
                Editor.EndChange();
                MarkDirty(); ScheduleLayout(); Editor.Focus();
            }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message, "That Quick Part couldn't be inserted."); }
        }

        void BuildingBlocksOrganizer()
        {
            var d = new DialogBase(this, "Building Blocks Organizer", 420);
            var list = new ListBox { Height = 220 };
            void Fill() => list.ItemsSource = Directory.GetFiles(QuickPartsDir, "*.vqp").Select(System.IO.Path.GetFileNameWithoutExtension).OrderBy(x => x).ToList();
            Fill();
            var ins = UI.Btn("Insert", (s, e) => { if (list.SelectedItem is string n) { InsertQuickPart(System.IO.Path.Combine(QuickPartsDir, n + ".vqp")); d.Close(); } }, true);
            var del = UI.Btn("Delete", (s, e) =>
            {
                if (list.SelectedItem is string n && VistaDialog.Confirm(d, $"Do you want to delete \"{n}\"?"))
                {
                    var f = System.IO.Path.Combine(QuickPartsDir, n + ".vqp");
                    try { File.Move(f, f + ".deleted", true); File.Delete(f + ".deleted"); } catch { }
                    Fill();
                }
            });
            var sp = new StackPanel(); sp.Children.Add(new TextBlock { Text = "Building blocks:", Margin = new Thickness(0, 0, 0, 4) }); sp.Children.Add(list);
            if (list.Items.Count == 0) sp.Children.Add(new TextBlock { Text = "Select some content and use \"Save Selection to Quick Part Gallery\" to add one.", Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
            d.Content = UI.DialogLayout(sp, UI.Strip(ins, del, UI.Btn("Close", null, false, true)));
            d.ShowDialog();
        }

        static readonly (string Name, string Code, string Desc)[] FieldList =
        {
            ("Author", "AUTHOR", "The document author's name"), ("CreateDate", "CREATEDATE \\@ \"M/d/yyyy\"", "The date the document was created"), ("Date", "DATE \\@ \"MMMM d, yyyy\"", "Today's date"),
            ("FileName", "FILENAME", "The document's file name"), ("NumChars", "NUMCHARS", "The number of characters in the document"), ("NumPages", "NUMPAGES", "The number of pages in the document"),
            ("NumWords", "NUMWORDS", "The number of words in the document"), ("Page", "PAGE", "The number of the current page"), ("SaveDate", "SAVEDATE \\@ \"M/d/yyyy\"", "The date the document was last saved"),
            ("Time", "TIME \\@ \"h:mm am/pm\"", "The current time"), ("Title", "TITLE", "The document's title"), ("UserName", "USERNAME", "Your user name"), ("UserInitials", "USERINITIALS", "Your initials"),
        };

        void FieldDialog()
        {
            var d = new DialogBase(this, "Field", 460);
            var list = new ListBox { Height = 220, Width = 160 };
            foreach (var f in FieldList) list.Items.Add(f.Name);
            var desc = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Width = 240 };
            var code = D.Box("", 240);
            var result = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x42, 0x8B)), Margin = new Thickness(0, 6, 0, 0) };
            list.SelectionChanged += (s, e) => { if (list.SelectedIndex >= 0) { desc.Text = FieldList[list.SelectedIndex].Desc; code.Text = FieldList[list.SelectedIndex].Code; } };
            code.TextChanged += (s, e) => result.Text = "Result: " + EvaluateField(code.Text);
            list.SelectedIndex = 2;
            var right = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            right.Children.Add(desc); right.Children.Add(new TextBlock { Text = "Field code:", Margin = new Thickness(0, 10, 0, 0) }); right.Children.Add(code); right.Children.Add(result);
            var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(list); row.Children.Add(right);
            d.Content = UI.DialogLayout(row, UI.Strip(UI.Btn("OK", (s, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
            if (d.ShowDialog() == true && code.Text.Trim().Length > 0) InsertField(code.Text.Trim());
        }

        string EvaluateField(string code)
        {
            code = (code ?? "").Trim();
            var name = code.Split(' ')[0].ToUpperInvariant();
            var m = System.Text.RegularExpressions.Regex.Match(code, "\\\\@ \"([^\"]+)\"");
            string fmt = m.Success ? m.Groups[1].Value.Replace("am/pm", "tt").Replace("AM/PM", "tt") : null;
            string Date(DateTime dt) => fmt != null ? dt.ToString(fmt, CultureInfo.CurrentCulture) : dt.ToString("d");
            switch (name)
            {
                case "DATE": return Date(DateTime.Now);
                case "TIME": return fmt != null ? DateTime.Now.ToString(fmt) : DateTime.Now.ToString("t");
                case "CREATEDATE": return Date(_path != null && File.Exists(_path) ? File.GetCreationTime(_path) : DateTime.Now);
                case "SAVEDATE": return Date(_path != null && File.Exists(_path) ? File.GetLastWriteTime(_path) : DateTime.Now);
                case "AUTHOR": return string.IsNullOrEmpty(_author) ? (Settings.Current.UserName ?? Environment.UserName) : _author;
                case "TITLE": return string.IsNullOrEmpty(_docTitle) ? "[Title]" : _docTitle;
                case "SUBJECT": return _ex.Custom.TryGetValue("Subject", out var su) && su.Length > 0 ? su : "[Subject]";
                case "KEYWORDS": return _ex.Custom.TryGetValue("Keywords", out var kw) && kw.Length > 0 ? kw : "[Keywords]";
                case "DOCPROPERTY": return _ex.Custom.TryGetValue("Company", out var co) && co.Length > 0 ? co : "[Company]";
                case "FILENAME": return DisplayName;
                case "NUMPAGES": return _pageCount.ToString();
                case "PAGE": return DecoratedPaginator.FormatNumber(PageOf(Editor.CaretPosition), _page);
                case "NUMWORDS": return CountWords().ToString();
                case "NUMCHARS": return DocFormats.PlainText(Editor.Document).Count(ch => !char.IsWhiteSpace(ch)).ToString();
                case "USERNAME": return Settings.Current.UserName ?? Environment.UserName;
                case "USERINITIALS": return Settings.Current.Initials ?? "";
                default: return "Error! Unknown field.";
            }
        }

        int CountWords() => System.Text.RegularExpressions.Regex.Matches(DocFormats.PlainText(Editor.Document), @"[\p{L}\p{N}']+").Count;

        void InsertField(string code)
        {
            Editor.BeginChange();
            if (!Editor.Selection.IsEmpty) Editor.Selection.Text = "";
            var ip = Editor.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
            var s = new Span(new Run(EvaluateField(code)), ip) { Tag = new FieldInfo { Code = code } };
            Editor.CaretPosition = s.ElementEnd;
            Editor.EndChange();
            MarkDirty(); Editor.Focus();
        }

        /// <summary>F9: refreshes every field (and page references) in the document.</summary>
        void UpdateFields()
        {
            Editor.BeginChange();
            foreach (var s in Spans.WithTag<FieldInfo>(Editor.Document).ToList())
            {
                var code = ((FieldInfo)s.Tag).Code ?? "";
                string val;
                if (code.StartsWith("PAGEREF") || code.StartsWith("REF"))
                {
                    var anchor = code.Split(' ').Skip(1).FirstOrDefault();
                    var bm = Spans.WithTag<BookmarkInfo>(Editor.Document).FirstOrDefault(x => ((BookmarkInfo)x.Tag).Name == anchor);
                    if (bm == null) continue;
                    val = code.StartsWith("PAGEREF") ? PageOf(bm.ContentStart).ToString() : Spans.Text(bm).Trim();
                }
                else if (code.StartsWith("PAGE"))
                {
                    val = DecoratedPaginator.FormatNumber(PageOf(s.ContentStart), _page);
                }
                else val = EvaluateField(code);
                var first = s.Inlines.FirstInline as Run;
                if (first != null) { first.Text = val; foreach (var extra in s.Inlines.Skip(1).ToList()) s.Inlines.Remove(extra); }
            }
            Editor.EndChange();
            Flash("Fields updated.");
        }

        // ---------------- WordArt ----------------
        static readonly (string Name, Func<Brush> Fill, Color Stroke, bool Shadow, bool Reflect)[] WordArtStyles =
        {
            ("Fill - Accent 1", () => new LinearGradientBrush(Lighter(Th.Accent1, .45), Th.Accent1, 90), Colors.White, true, false),
            ("Gradient Gold", () => new LinearGradientBrush(new GradientStopCollection { new GradientStop(Color.FromRgb(0xFF, 0xF0, 0xA0), 0), new GradientStop(Color.FromRgb(0xE0, 0xA8, 0x20), .5), new GradientStop(Color.FromRgb(0xA0, 0x68, 0x00), 1) }, 90), Color.FromRgb(0x7A, 0x50, 0x00), true, false),
            ("Rainbow", () => new LinearGradientBrush(new GradientStopCollection { new GradientStop(Colors.Red, 0), new GradientStop(Colors.Orange, .2), new GradientStop(Colors.Gold, .4), new GradientStop(Colors.LimeGreen, .6), new GradientStop(Colors.DodgerBlue, .8), new GradientStop(Colors.MediumPurple, 1) }, 0), Colors.White, false, false),
            ("Aqua Glass", () => new LinearGradientBrush(new GradientStopCollection { new GradientStop(Color.FromRgb(0xC8, 0xF4, 0xFF), 0), new GradientStop(Color.FromRgb(0x4A, 0xC0, 0xE8), .5), new GradientStop(Color.FromRgb(0x10, 0x70, 0xB0), .52), new GradientStop(Color.FromRgb(0x40, 0xA8, 0xE0), 1) }, 90), Color.FromRgb(0x0A, 0x40, 0x70), true, true),
            ("Outline Only", () => Brushes.White, Color.FromRgb(0x1F, 0x49, 0x7D), false, false),
            ("Fire", () => new LinearGradientBrush(new GradientStopCollection { new GradientStop(Colors.Yellow, 0), new GradientStop(Colors.OrangeRed, .6), new GradientStop(Colors.DarkRed, 1) }, 90), Color.FromRgb(0x60, 0x10, 0x00), true, false),
            ("Silver", () => new LinearGradientBrush(new GradientStopCollection { new GradientStop(Colors.White, 0), new GradientStop(Color.FromRgb(0xA8, 0xB0, 0xBC), .5), new GradientStop(Color.FromRgb(0x60, 0x68, 0x74), 1) }, 90), Color.FromRgb(0x40, 0x44, 0x4C), true, true),
            ("Accent 2 Reflection", () => Br(Th.Accent2), Colors.White, false, true),
            ("Dark Shadow", () => Br(Th.Dark2), Colors.Black, true, false),
            ("Green Lime", () => new LinearGradientBrush(Color.FromRgb(0xD4, 0xFF, 0x9A), Color.FromRgb(0x3E, 0x9A, 0x22), 90), Color.FromRgb(0x1E, 0x5A, 0x12), true, false),
            ("Purple Glow", () => new LinearGradientBrush(Color.FromRgb(0xE0, 0xC8, 0xFF), Color.FromRgb(0x70, 0x40, 0xB0), 90), Colors.White, true, false),
            ("Classic Black", () => Brushes.Black, Colors.Black, false, false),
        };

        void ShowWordArtGallery()
        {
            var tiles = WordArtStyles.Select((st, i) =>
            {
                int idx = i;
                var v = WordArtVisual("A", "Arial Black", 40, true, false, idx, out var w, out var h);
                return (UIElement)RB.Tile(new Viewbox { Width = 48, Height = 48, Child = v }, st.Name, () => WordArtDialog(idx), 58, 58);
            });
            RB.ShowGallery(Mouse.DirectlyOver as FrameworkElement ?? Ribbon, "WordArt", tiles, 270);
        }

        void WordArtDialog(int style)
        {
            var d = new DialogBase(this, "Edit WordArt Text", 480);
            var font = new ComboBox { Width = 180, IsEditable = true, ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(f => f).ToList(), Text = "Arial Black" };
            var size = new ComboBox { Width = 60, IsEditable = true, ItemsSource = new[] { "24", "28", "36", "40", "48", "54", "60", "72", "96" }, Text = "40" };
            var bold = new ToggleButton { Content = new TextBlock { Text = "B", FontWeight = FontWeights.Bold }, Width = 24, Margin = new Thickness(6, 0, 0, 0) };
            var ital = new ToggleButton { Content = new TextBlock { Text = "I", FontStyle = FontStyles.Italic }, Width = 24, Margin = new Thickness(2, 0, 0, 0) };
            var text = new TextBox { Height = 80, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Text = Editor.Selection.IsEmpty ? "Your Text Here" : Editor.Selection.Text.Trim() };
            var preview = new Border { Height = 110, Background = Brushes.White, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Margin = new Thickness(0, 8, 0, 0) };
            void Refresh()
            {
                double sz = D.ParseNum(size.Text) ?? 40;
                var v = WordArtVisual(text.Text, font.Text, sz, bold.IsChecked == true, ital.IsChecked == true, style, out _, out _);
                preview.Child = new Viewbox { Child = v, Margin = new Thickness(6), StretchDirection = StretchDirection.DownOnly };
            }
            font.SelectionChanged += (s, e) => Dispatcher.BeginInvoke(new Action(Refresh));
            font.LostFocus += (s, e) => Refresh();
            size.SelectionChanged += (s, e) => Dispatcher.BeginInvoke(new Action(Refresh));
            size.LostFocus += (s, e) => Refresh();
            bold.Click += (s, e) => Refresh(); ital.Click += (s, e) => Refresh(); text.TextChanged += (s, e) => Refresh();
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(new TextBlock { Text = "Font:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) }); row.Children.Add(font);
            row.Children.Add(new TextBlock { Text = "Size:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) }); row.Children.Add(size);
            row.Children.Add(bold); row.Children.Add(ital);
            var sp = new StackPanel(); sp.Children.Add(row); sp.Children.Add(new TextBlock { Text = "Text:" }); sp.Children.Add(text); sp.Children.Add(preview);
            d.Content = UI.DialogLayout(sp, UI.Strip(UI.Btn("OK", (s, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
            d.Loaded += (s, e) => Refresh();
            if (d.ShowDialog() != true || string.IsNullOrWhiteSpace(text.Text)) return;
            var vis = WordArtVisual(text.Text, font.Text, D.ParseNum(size.Text) ?? 40, bold.IsChecked == true, ital.IsChecked == true, style, out var W, out var H);
            if (!Editor.Selection.IsEmpty) Editor.Selection.Text = "";
            InsertVisual(vis, W, H);
        }

        static FrameworkElement WordArtVisual(string text, string font, double sizePt, bool bold, bool italic, int style, out double w, out double h)
        {
            var st = WordArtStyles[Math.Max(0, Math.Min(WordArtStyles.Length - 1, style))];
            var ft = new FormattedText(string.IsNullOrEmpty(text) ? " " : text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily(string.IsNullOrWhiteSpace(font) ? "Arial Black" : font), italic ? FontStyles.Italic : FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
                sizePt * 96 / 72, Brushes.Black, 1.0) { TextAlignment = TextAlignment.Left };
            var geo = ft.BuildGeometry(new Point(6, 4));
            var bounds = geo.Bounds;
            w = Math.Ceiling(bounds.Right + 10); h = Math.Ceiling(bounds.Bottom + 8);
            var c = new Canvas { Width = w, Height = st.Reflect ? h * 1.55 : h };
            var path = new Path { Data = geo, Fill = st.Fill(), Stroke = Br(st.Stroke), StrokeThickness = Math.Max(1, sizePt / 30), StrokeLineJoin = PenLineJoin.Round };
            if (st.Shadow) path.Effect = new DropShadowEffect { ShadowDepth = Math.Max(2, sizePt / 14), BlurRadius = 4, Opacity = .45 };
            c.Children.Add(path);
            if (st.Reflect)
            {
                var refl = new Path { Data = geo, Fill = st.Fill(), RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, -1), new TranslateTransform(0, bounds.Bottom * 2 + 2) } },
                    OpacityMask = new LinearGradientBrush(new GradientStopCollection { new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.0), new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.5), new GradientStop(Color.FromArgb(0x70, 0, 0, 0), 1) }, 90) };
                c.Children.Add(refl);
                h = Math.Ceiling(h * 1.55);
            }
            return c;
        }

        // ---------------- Drop cap ----------------
        ContextMenu DropCapMenu()
        {
            var p = Editor.CaretPosition.Paragraph;
            var cur = p?.Inlines.FirstInline is Floater f && (f.Tag as string) == "DropCap" ? (f.Margin.Left < 0 ? "margin" : "drop") : "none";
            return RB.Menu(
                RB.MI("None", () => SetDropCap("none", 3, null, 0), null, null, cur == "none"),
                RB.MI("Dropped", () => SetDropCap("drop", 3, null, 0), "IconDropCap", null, cur == "drop"),
                RB.MI("In Margin", () => SetDropCap("margin", 3, null, 0), null, null, cur == "margin"),
                null,
                RB.MI("Drop Cap Options...", DropCapOptions));
        }

        void DropCapOptions()
        {
            var d = new DialogBase(this, "Drop Cap", 320);
            var none = new RadioButton { Content = "None", Margin = new Thickness(0, 0, 12, 0) };
            var drop = new RadioButton { Content = "Dropped", IsChecked = true, Margin = new Thickness(0, 0, 12, 0) };
            var margin = new RadioButton { Content = "In margin" };
            var pos = new StackPanel { Orientation = Orientation.Horizontal }; pos.Children.Add(none); pos.Children.Add(drop); pos.Children.Add(margin);
            var font = new ComboBox { Width = 180, IsEditable = true, ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(f => f).ToList() };
            var run = Editor.CaretPosition.Paragraph == null ? null : Fmt.Descendants(Editor.CaretPosition.Paragraph).OfType<Run>().FirstOrDefault(r => !string.IsNullOrEmpty(r.Text));
            font.Text = run?.FontFamily.Source ?? Th.Minor;
            var lines = D.Box("3", 50); var dist = D.Box("0", 50);
            var sp = new StackPanel(); sp.Children.Add(D.Group("Position", pos));
            sp.Children.Add(D.Group("Options", D.Form(("Font:", font), ("Lines to drop:", lines), ("Distance from text (in):", dist))));
            d.Content = UI.DialogLayout(sp, UI.Strip(UI.Btn("OK", (s, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
            if (d.ShowDialog() != true) return;
            SetDropCap(none.IsChecked == true ? "none" : margin.IsChecked == true ? "margin" : "drop", (int)Math.Max(1, Math.Min(10, D.ParseNum(lines.Text) ?? 3)), font.Text, (D.ParseNum(dist.Text) ?? 0) * 96);
        }

        void SetDropCap(string mode, int lines, string font, double distance)
        {
            var p = Editor.CaretPosition.Paragraph;
            if (p == null) return;
            Editor.BeginChange();
            try
            {
                // undo an existing drop cap first
                if (p.Inlines.FirstInline is Floater old && (old.Tag as string) == "DropCap")
                {
                    var letter = Fmt.AllParagraphs(old.Blocks).SelectMany(x => Fmt.Descendants(x)).OfType<Run>().FirstOrDefault()?.Text ?? "";
                    p.Inlines.Remove(old);
                    var firstRun = Fmt.Descendants(p).OfType<Run>().FirstOrDefault();
                    if (firstRun != null) firstRun.Text = letter + firstRun.Text; else p.Inlines.Add(new Run(letter));
                }
                if (mode == "none") return;
                var run = Fmt.Descendants(p).OfType<Run>().FirstOrDefault(r => !string.IsNullOrEmpty(r.Text?.Trim()));
                if (run == null) { Flash("Click in a paragraph with text to add a drop cap."); return; }
                int idx = run.Text.TakeWhile(char.IsWhiteSpace).Count();
                string ch = run.Text.Substring(idx, 1);
                run.Text = run.Text.Remove(idx, 1);
                double fs = run.FontSize, lineH = fs * 1.22;
                double size = lineH * lines * 0.92;
                var ff = string.IsNullOrWhiteSpace(font) ? run.FontFamily : new FontFamily(font);
                var ft = new FormattedText(ch, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(ff, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, Brushes.Black, 1.0);
                var lp = new Paragraph(new Run(ch) { FontSize = size, FontFamily = ff, Foreground = run.Foreground })
                {
                    Margin = new Thickness(0), Padding = new Thickness(0), LineHeight = lineH * lines, LineStackingStrategy = LineStackingStrategy.BlockLineHeight
                };
                var f = new Floater(lp)
                {
                    Tag = "DropCap", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0), Width = Math.Ceiling(ft.WidthIncludingTrailingWhitespace + 2),
                    Margin = mode == "margin" ? new Thickness(-(ft.WidthIncludingTrailingWhitespace + 6), 0, 4 + distance, 0) : new Thickness(0, 0, 4 + distance, 0)
                };
                if (p.Inlines.FirstInline != null) p.Inlines.InsertBefore(p.Inlines.FirstInline, f); else p.Inlines.Add(f);
            }
            finally { Editor.EndChange(); MarkDirty(); ScheduleLayout(); }
        }

        // ---------------- Signature line ----------------
        void InsertSignatureLine()
        {
            var d = new DialogBase(this, "Signature Setup", 400);
            var signer = D.Box(Settings.Current.UserName ?? "", 300);
            var title = D.Box("", 300);
            var email = D.Box("", 300);
            var instr = new TextBox { Width = 300, Height = 44, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, Text = "Before signing this document, verify that the content you are signing is correct." };
            var showDate = new CheckBox { Content = "Show sign date in signature line", IsChecked = true, Margin = new Thickness(0, 6, 0, 0) };
            var sp = new StackPanel();
            foreach (var (l, c) in new (string, UIElement)[] { ("Suggested signer (for example, John Doe):", signer), ("Suggested signer's title (for example, Manager):", title), ("Suggested signer's e-mail address:", email), ("Instructions to the signer:", instr) })
            { sp.Children.Add(new TextBlock { Text = l, Margin = new Thickness(0, 4, 0, 0) }); sp.Children.Add(c); }
            sp.Children.Add(showDate);
            d.Content = UI.DialogLayout(sp, UI.Strip(UI.Btn("OK", (s, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
            if (d.ShowDialog() != true) return;
            var t = new Table { CellSpacing = 0, Margin = new Thickness(0, 24, 0, 12), Tag = "Signature" };
            t.Columns.Add(new TableColumn { Width = new GridLength(288) });
            if (showDate.IsChecked == true) { t.Columns.Add(new TableColumn { Width = new GridLength(24) }); t.Columns.Add(new TableColumn { Width = new GridLength(130) }); }
            var g = new TableRowGroup();
            var r1 = new TableRow();
            var x = NewCellParagraph(); x.Inlines.Add(new Run("X") { FontSize = 20 * Fmt.PxPerPt, FontWeight = FontWeights.Bold, Foreground = Brushes.Gray }); x.Margin = new Thickness(0, 18, 0, 0);
            r1.Cells.Add(new TableCell(x) { BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 0, 0, 1.5), Padding = new Thickness(2) });
            if (showDate.IsChecked == true)
            {
                r1.Cells.Add(new TableCell(NewCellParagraph()) { BorderThickness = new Thickness(0) });
                var dp = NewCellParagraph(); dp.Margin = new Thickness(0, 22, 0, 0);
                r1.Cells.Add(new TableCell(dp) { BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 0, 0, 1.5), Padding = new Thickness(2) });
            }
            var r2 = new TableRow();
            var np = NewCellParagraph(); np.Inlines.Add(new Run(signer.Text)); if (title.Text.Length > 0) { np.Inlines.Add(new LineBreak()); np.Inlines.Add(new Run(title.Text) { Foreground = Brushes.DimGray }); }
            if (email.Text.Length > 0) { np.Inlines.Add(new LineBreak()); np.Inlines.Add(new Run(email.Text) { Foreground = Brushes.DimGray }); }
            r2.Cells.Add(new TableCell(np) { BorderThickness = new Thickness(0), Padding = new Thickness(2) });
            if (showDate.IsChecked == true)
            {
                r2.Cells.Add(new TableCell(NewCellParagraph()) { BorderThickness = new Thickness(0) });
                var dl = NewCellParagraph(); dl.Inlines.Add(new Run("Date"));
                r2.Cells.Add(new TableCell(dl) { BorderThickness = new Thickness(0), Padding = new Thickness(2) });
            }
            g.Rows.Add(r1); g.Rows.Add(r2); t.RowGroups.Add(g);
            InsertBlocksAtCaret(t);
            if (instr.Text.Trim().Length > 0) Flash(instr.Text.Trim());
        }

        // =====================================================================================
        //  Symbols and equations
        // =====================================================================================
        static readonly string[] CommonSymbols = { "€", "£", "¥", "©", "®", "™", "±", "≠", "≤", "≥", "÷", "×", "∞", "µ", "α", "β", "π", "Ω", "∑", "°", "√", "→", "←", "↑", "↓", "•", "…", "—", "§", "¶", "✓", "★" };

        ContextMenu SymbolMenu()
        {
            var m = RB.Menu();
            var recent = (Settings.Current.Custom.TryGetValue("recentSymbols", out var r) ? r : "").Select(ch => ch.ToString()).Where(s => s.Trim().Length > 0);
            var syms = recent.Concat(CommonSymbols).Distinct().Take(20).ToList();
            var grid = new UniformGrid { Columns = 5 };
            foreach (var s in syms)
            {
                var sym = s;
                var b = new Button { Style = RB.BtnStyle, Width = 30, Height = 30, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center, Content = new TextBlock { Text = sym, FontSize = 16, FontFamily = new FontFamily("Segoe UI Symbol") }, ToolTip = $"U+{(int)sym[0]:X4}" };
                b.Click += (o, e) => { m.IsOpen = false; InsertSymbolText(sym); };
                grid.Children.Add(b);
            }
            m.Items.Add(new MenuItem { Header = grid, StaysOpenOnClick = true, Padding = new Thickness(0) });
            m.Items.Add(new Separator());
            m.Items.Add(RB.MI("More Symbols...", () => { var d = new SymbolDialog(this); d.Insert += InsertSymbolText; d.ShowDialog(); }, "IconSymbol"));
            return m;
        }

        void InsertSymbolText(string s)
        {
            InsertText(s);
            var recent = Settings.Current.Custom.TryGetValue("recentSymbols", out var r) ? r : "";
            recent = new string((s + recent.Replace(s, "")).Take(20).ToArray());
            Settings.Current.Custom["recentSymbols"] = recent;
            Settings.Save();
        }

        static readonly (string Name, string Eq)[] BuiltInEquations =
        {
            ("Area of Circle", "A = πr²"),
            ("Binomial Theorem", "(x + a)ⁿ = ∑ₖ₌₀ⁿ (ⁿₖ) xᵏaⁿ⁻ᵏ"),
            ("Expansion of a Sum", "(1 + x)ⁿ = 1 + nx/1! + n(n − 1)x²/2! + ⋯"),
            ("Fourier Series", "f(x) = a₀ + ∑ₙ₌₁^∞ (aₙ cos nπx/L + bₙ sin nπx/L)"),
            ("Pythagorean Theorem", "a² + b² = c²"),
            ("Quadratic Formula", "x = (−b ± √(b² − 4ac)) / 2a"),
            ("Taylor Expansion", "eˣ = 1 + x/1! + x²/2! + x³/3! + ⋯,  −∞ < x < ∞"),
            ("Trig Identity 1", "sin α ± sin β = 2 sin ½(α ± β) cos ½(α ∓ β)"),
            ("Trig Identity 2", "cos α + cos β = 2 cos ½(α + β) cos ½(α − β)"),
        };

        ContextMenu EquationMenu()
        {
            var m = RB.Menu();
            m.Items.Add(new MenuItem { Header = "Built-In", IsEnabled = false, FontWeight = FontWeights.Bold });
            foreach (var (name, eq) in BuiltInEquations)
            {
                var e = eq;
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold });
                sp.Children.Add(new TextBlock { Text = e, FontFamily = new FontFamily("Cambria Math"), FontSize = 14, Margin = new Thickness(8, 2, 0, 2) });
                var mi = new MenuItem { Header = sp };
                mi.Click += (s, a) => InsertEquation(e, true);
                m.Items.Add(mi);
            }
            m.Items.Add(new Separator());
            m.Items.Add(RB.MI("Insert New Equation", EquationEditor, "IconEquation", "Alt+="));
            return m;
        }

        void InsertEquation(string text, bool display)
        {
            Editor.BeginChange();
            if (!Editor.Selection.IsEmpty) Editor.Selection.Text = "";
            var para = Editor.CaretPosition.Paragraph;
            bool emptyPara = para != null && Spans.Text(para).Trim().Length == 0;
            var run = new Run(text) { FontFamily = new FontFamily("Cambria Math"), FontStyle = FontStyles.Italic };
            if (display && !emptyPara && para != null)
            {
                var np = NewPara(); np.TextAlignment = TextAlignment.Center; np.Inlines.Add(run);
                Editor.EndChange();
                InsertBlocksAtCaret(np);
                return;
            }
            var ip = Editor.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
            var span = new Span(run, ip);
            if (display && para != null) para.TextAlignment = TextAlignment.Center;
            Editor.CaretPosition = span.ElementEnd;
            Editor.EndChange();
            MarkDirty(); Editor.Focus();
        }

        void EquationEditor()
        {
            var d = new DialogBase(this, "Equation", 560);
            var input = new TextBox { Height = 60, AcceptsReturn = false, FontFamily = new FontFamily("Consolas"), FontSize = 14, TextWrapping = TextWrapping.Wrap, Text = "x = (-b +- sqrt(b^2 - 4ac))/2a" };
            var preview = new TextBlock { FontFamily = new FontFamily("Cambria Math"), FontSize = 22, FontStyle = FontStyles.Italic, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6), MinHeight = 40 };
            input.TextChanged += (s, e) => preview.Text = LinearToUnicode(input.Text);
            var pad = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var t in new[] { "±", "∞", "=", "≠", "≈", "×", "÷", "≤", "≥", "√", "∑", "∫", "∂", "∆", "∇", "π", "θ", "α", "β", "γ", "δ", "λ", "μ", "σ", "φ", "ω", "Ω", "→", "⇒", "∈", "∉", "∪", "∩", "⊂", "∀", "∃", "°", "′", "⋯" })
            {
                var tok = t;
                var b = new Button { Style = RB.BtnStyle, Width = 26, Height = 26, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center, Content = new TextBlock { Text = tok, FontFamily = new FontFamily("Cambria Math"), FontSize = 15 } };
                b.Click += (s, e) => { int i = input.CaretIndex; input.Text = input.Text.Insert(i, tok); input.CaretIndex = i + tok.Length; input.Focus(); };
                pad.Children.Add(b);
            }
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = "Type the equation. Use ^ for superscript, _ for subscript, sqrt( ), +-, <=, >=, !=, ->, pi, alpha, sum, int, inf:", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
            sp.Children.Add(input); sp.Children.Add(pad);
            sp.Children.Add(new TextBlock { Text = "Preview:", Margin = new Thickness(0, 8, 0, 0) });
            sp.Children.Add(new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Background = Brushes.White, Child = preview });
            var display = new CheckBox { Content = "Display on its own line (centered)", IsChecked = true, Margin = new Thickness(0, 6, 0, 0) };
            sp.Children.Add(display);
            d.Content = UI.DialogLayout(sp, UI.Strip(UI.Btn("Insert", (s, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
            d.Loaded += (s, e) => { preview.Text = LinearToUnicode(input.Text); input.Focus(); input.SelectAll(); };
            if (d.ShowDialog() == true && input.Text.Trim().Length > 0) InsertEquation(LinearToUnicode(input.Text), display.IsChecked == true);
        }

        static string LinearToUnicode(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var words = new (string, string)[]
            {
                ("alpha", "α"), ("beta", "β"), ("gamma", "γ"), ("delta", "δ"), ("epsilon", "ε"), ("theta", "θ"), ("lambda", "λ"), ("mu", "μ"), ("sigma", "σ"), ("phi", "φ"), ("omega", "ω"),
                ("Delta", "Δ"), ("Sigma", "Σ"), ("Omega", "Ω"), ("pi", "π"), ("infinity", "∞"), ("inf", "∞"), ("sum", "∑"), ("int", "∫"), ("partial", "∂"), ("nabla", "∇"),
                ("+-", "±"), ("-+", "∓"), ("<=", "≤"), (">=", "≥"), ("!=", "≠"), ("~=", "≈"), ("->", "→"), ("=>", "⇒"), ("<-", "←"), ("*", "·"), ("...", "⋯"), ("-", "−"),
            };
            var sb = new System.Text.StringBuilder();
            const string sup = "⁰¹²³⁴⁵⁶⁷⁸⁹", sub = "₀₁₂₃₄₅₆₇₈₉";
            string SupOf(char c) => char.IsDigit(c) ? sup[c - '0'].ToString() : c switch { '+' => "⁺", '-' => "⁻", '=' => "⁼", '(' => "⁽", ')' => "⁾", 'n' => "ⁿ", 'i' => "ⁱ", 'x' => "ˣ", 'k' => "ᵏ", 'a' => "ᵃ", 'b' => "ᵇ", 'c' => "ᶜ", 'd' => "ᵈ", 'e' => "ᵉ", 'm' => "ᵐ", 't' => "ᵗ", 'y' => "ʸ", '2' => "²", _ => c.ToString() };
            string SubOf(char c) => char.IsDigit(c) ? sub[c - '0'].ToString() : c switch { '+' => "₊", '-' => "₋", '=' => "₌", '(' => "₍", ')' => "₎", 'a' => "ₐ", 'e' => "ₑ", 'i' => "ᵢ", 'j' => "ⱼ", 'k' => "ₖ", 'n' => "ₙ", 'x' => "ₓ", 'm' => "ₘ", 't' => "ₜ", _ => c.ToString() };
            int i = 0;
            while (i < s.Length)
            {
                if (s[i] == '^' || s[i] == '_')
                {
                    bool up = s[i] == '^'; i++;
                    string grp;
                    if (i < s.Length && s[i] == '(') { int j = s.IndexOf(')', i); if (j < 0) j = s.Length - 1; grp = s.Substring(i + 1, j - i - 1); i = j + 1; }
                    else { int j = i; while (j < s.Length && (char.IsLetterOrDigit(s[j]))) { j++; if (j - i >= 1 && !char.IsDigit(s[i])) break; } grp = s.Substring(i, Math.Max(1, j - i)).Substring(0, Math.Min(s.Length - i, Math.Max(1, j - i))); i += grp.Length; }
                    foreach (var c in grp) sb.Append(up ? SupOf(c) : SubOf(c));
                    continue;
                }
                if (string.CompareOrdinal(s, i, "sqrt", 0, 4) == 0) { sb.Append('√'); i += 4; continue; }
                bool matched = false;
                foreach (var (w, u) in words)
                {
                    if (i + w.Length <= s.Length && string.CompareOrdinal(s, i, w, 0, w.Length) == 0)
                    {
                        bool alpha = char.IsLetter(w[0]);
                        bool boundaryBefore = i == 0 || !char.IsLetter(s[i - 1]);
                        bool boundaryAfter = i + w.Length >= s.Length || !char.IsLetter(s[i + w.Length]);
                        if (alpha && !(boundaryBefore && boundaryAfter)) continue;
                        sb.Append(u); i += w.Length; matched = true; break;
                    }
                }
                if (matched) continue;
                sb.Append(s[i]); i++;
            }
            return sb.ToString();
        }
    }
}
