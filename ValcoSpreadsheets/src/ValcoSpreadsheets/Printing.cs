using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Xps.Packaging;
using ClosedXML.Excel;
using ValcoSpreadsheets.Grid;
using ValcoSpreadsheets.Model;

namespace ValcoSpreadsheets
{
    /// <summary>The page setup of a sheet in inches/percent, read from the worksheet.</summary>
    public class PrintSetup
    {
        public double PaperW = 8.5, PaperH = 11;
        public bool Landscape;
        public double Top = 0.75, Bottom = 0.75, Left = 0.7, Right = 0.7, HeaderMargin = 0.3, FooterMargin = 0.3;
        public int Scale = 100, PagesWide, PagesTall;
        public bool Gridlines, Headings, CenterH, CenterV, OverThenDown;
        public CellRect? Area, Selection;
        public int RepeatRow1, RepeatRow2, RepeatCol1, RepeatCol2;
        public List<int> RowBreaks = new(), ColBreaks = new();
        public string HeaderL = "", HeaderC = "", HeaderR = "", FooterL = "", FooterC = "", FooterR = "";

        public double PageW => (Landscape ? PaperH : PaperW) * 96;
        public double PageH => (Landscape ? PaperW : PaperH) * 96;

        public static (double W, double H) PaperSize(XLPaperSize p) => p switch
        {
            XLPaperSize.LegalPaper => (8.5, 14), XLPaperSize.A4Paper => (8.27, 11.69), XLPaperSize.A5Paper => (5.83, 8.27),
            XLPaperSize.ExecutivePaper => (7.25, 10.5), XLPaperSize.A3Paper => (11.69, 16.54), XLPaperSize.TabloidPaper => (11, 17), _ => (8.5, 11)
        };

        public static PrintSetup For(IXLWorksheet ws)
        {
            var p = ws.PageSetup;
            var s = new PrintSetup();
            (s.PaperW, s.PaperH) = PaperSize(p.PaperSize);
            s.Landscape = p.PageOrientation == XLPageOrientation.Landscape;
            s.Top = p.Margins.Top; s.Bottom = p.Margins.Bottom; s.Left = p.Margins.Left; s.Right = p.Margins.Right;
            s.HeaderMargin = p.Margins.Header; s.FooterMargin = p.Margins.Footer;
            s.Scale = p.Scale > 0 ? p.Scale : 100; s.PagesWide = p.PagesWide; s.PagesTall = p.PagesTall;
            s.Gridlines = p.ShowGridlines; s.Headings = p.ShowRowAndColumnHeadings;
            s.CenterH = p.CenterHorizontally; s.CenterV = p.CenterVertically;
            s.OverThenDown = p.PageOrder == XLPageOrderValues.OverThenDown;
            try
            {
                var pa = p.PrintAreas.FirstOrDefault();
                if (pa != null) { var a = pa.RangeAddress; s.Area = new CellRect(a.FirstAddress.RowNumber, a.FirstAddress.ColumnNumber, a.LastAddress.RowNumber, a.LastAddress.ColumnNumber); }
            }
            catch { }
            s.RepeatRow1 = p.FirstRowToRepeatAtTop; s.RepeatRow2 = p.LastRowToRepeatAtTop;
            s.RepeatCol1 = p.FirstColumnToRepeatAtLeft; s.RepeatCol2 = p.LastColumnToRepeatAtLeft;
            s.RowBreaks = p.RowBreaks.ToList(); s.ColBreaks = p.ColumnBreaks.ToList();
            string T(IXLHFItem i) { try { return i.GetText(XLHFOccurrence.AllPages) ?? ""; } catch { return ""; } }
            s.HeaderL = T(p.Header.Left); s.HeaderC = T(p.Header.Center); s.HeaderR = T(p.Header.Right);
            s.FooterL = T(p.Footer.Left); s.FooterC = T(p.Footer.Center); s.FooterR = T(p.Footer.Right);
            return s;
        }
    }

    /// <summary>Splits a sheet into printed pages and draws each one.</summary>
    public static class SheetPrinter
    {
        public static FixedDocument BuildDocument(Book book, IXLWorksheet ws, SheetLayout layout, PrintSetup s, double printableW = 0, double printableH = 0)
        {
            var doc = new FixedDocument();
            double pageW = s.PageW, pageH = s.PageH;
            doc.DocumentPaginator.PageSize = new Size(pageW, pageH);
            var area = s.Selection ?? s.Area ?? UsedArea(ws);
            double bodyW = pageW - (s.Left + s.Right) * 96, bodyH = pageH - (s.Top + s.Bottom) * 96;
            double headW = s.Headings ? 30 : 0, headH = s.Headings ? 18 : 0;

            // repeat rows/columns
            var repRows = s.RepeatRow1 > 0 ? Enumerable.Range(s.RepeatRow1, Math.Max(1, s.RepeatRow2 - s.RepeatRow1 + 1)).ToList() : new List<int>();
            var repCols = s.RepeatCol1 > 0 ? Enumerable.Range(s.RepeatCol1, Math.Max(1, s.RepeatCol2 - s.RepeatCol1 + 1)).ToList() : new List<int>();
            double repH = repRows.Sum(r => layout.Rows.Size(r)), repW = repCols.Sum(c => layout.Cols.Size(c));

            // scale: fixed percent or fit to pages
            double scale = s.Scale / 100.0;
            double totalW = Enumerable.Range(area.C1, area.Cols).Sum(c => layout.Cols.Size(c)), totalH = 0;
            for (int r = area.R1; r <= area.R2; r++) totalH += layout.Rows.Size(r);
            if (s.PagesWide > 0 || s.PagesTall > 0)
            {
                double sw = s.PagesWide > 0 ? (bodyW * s.PagesWide) / (totalW + headW * s.PagesWide + repW * s.PagesWide) : double.MaxValue;
                double sh = s.PagesTall > 0 ? (bodyH * s.PagesTall) / (totalH + headH * s.PagesTall + repH * s.PagesTall) : double.MaxValue;
                scale = Math.Clamp(Math.Min(Math.Min(sw, sh), 1.0), 0.1, 4);
            }
            double availW = bodyW / scale - headW - repW, availH = bodyH / scale - headH - repH;

            var colPages = Split(area.C1, area.C2, layout.Cols, availW, s.ColBreaks);
            var rowPages = Split(area.R1, area.R2, layout.Rows, availH, s.RowBreaks);
            var pages = new List<(int R1, int R2, int C1, int C2)>();
            if (s.OverThenDown) foreach (var rp in rowPages) foreach (var cp in colPages) pages.Add((rp.Item1, rp.Item2, cp.Item1, cp.Item2));
            else foreach (var cp in colPages) foreach (var rp in rowPages) pages.Add((rp.Item1, rp.Item2, cp.Item1, cp.Item2));
            int n = 0;
            foreach (var pg in pages)
            {
                n++;
                var drawing = new DrawingGroup();
                using (var dc = drawing.Open())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pageW, pageH));
                    var rows = repRows.Where(r => r < pg.R1).Concat(Enumerable.Range(pg.R1, pg.R2 - pg.R1 + 1)).Where(r => !layout.Rows.IsHidden(r)).ToList();
                    var cols = repCols.Where(c => c < pg.C1).Concat(Enumerable.Range(pg.C1, pg.C2 - pg.C1 + 1)).Where(c => !layout.Cols.IsHidden(c)).ToList();
                    double contentW = (headW + cols.Sum(c => layout.Cols.Size(c))) * scale, contentH = (headH + rows.Sum(r => layout.Rows.Size(r))) * scale;
                    double ox = s.Left * 96 + (s.CenterH ? Math.Max(0, (bodyW - contentW) / 2) : 0);
                    double oy = s.Top * 96 + (s.CenterV ? Math.Max(0, (bodyH - contentH) / 2) : 0);
                    dc.PushTransform(new TranslateTransform(ox, oy));
                    dc.PushTransform(new ScaleTransform(scale, scale));
                    DrawPage(dc, book, ws, layout, rows, cols, s, headW, headH);
                    dc.Pop(); dc.Pop();
                    DrawHeaderFooter(dc, s, n, pages.Count, book, ws, pageW, pageH);
                }
                var img = new Image { Source = new DrawingImage(drawing), Width = pageW, Height = pageH, Stretch = Stretch.Fill };
                var fp = new FixedPage { Width = pageW, Height = pageH, Background = Brushes.White };
                fp.Children.Add(img);
                var pc = new PageContent();
                ((System.Windows.Markup.IAddChild)pc).AddChild(fp);
                doc.Pages.Add(pc);
            }
            return doc;
        }

        static CellRect UsedArea(IXLWorksheet ws)
        {
            var used = ws.RangeUsed(XLCellsUsedOptions.All);
            if (used == null) return new CellRect(1, 1, 1, 1);
            var a = used.RangeAddress;
            int r2 = a.LastAddress.RowNumber, c2 = a.LastAddress.ColumnNumber;
            foreach (var p in ws.Pictures) { try { r2 = Math.Max(r2, p.BottomRightCell?.Address.RowNumber ?? r2); c2 = Math.Max(c2, p.BottomRightCell?.Address.ColumnNumber ?? c2); } catch { } }
            return new CellRect(1, 1, r2, c2);
        }

        static List<(int, int)> Split(int from, int to, Axis axis, double avail, List<int> breaks)
        {
            var list = new List<(int, int)>();
            int start = from; double used = 0;
            for (int i = from; i <= to; i++)
            {
                double s = axis.Size(i);
                bool forced = breaks.Contains(i - 1) && i > start;
                if ((used + s > avail && i > start) || forced) { list.Add((start, i - 1)); start = i; used = 0; }
                used += s;
            }
            list.Add((start, to));
            return list;
        }

        static void DrawPage(DrawingContext dc, Book book, IXLWorksheet ws, SheetLayout layout, List<int> rows, List<int> cols, PrintSetup s, double headW, double headH)
        {
            var wb = book.Wb;
            var xs = new Dictionary<int, double>(); var ys = new Dictionary<int, double>();
            double x = headW; foreach (var c in cols) { xs[c] = x; x += layout.Cols.Size(c); }
            double y = headH; foreach (var r in rows) { ys[r] = y; y += layout.Rows.Size(r); }
            double right = x, bottom = y;
            var dip = 1.0;
            var grid = new Pen(new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)), 0.5);
            var cf = new CondFormatCache(ws, wb);
            Rect CellBox(int r, int c)
            {
                var m = layout.MergeAt(r, c);
                if (m.HasValue)
                {
                    double x1 = xs.TryGetValue(m.Value.C1, out var a) ? a : xs[c], y1 = ys.TryGetValue(m.Value.R1, out var b) ? b : ys[r];
                    double x2 = x1, y2 = y1;
                    for (int cc = m.Value.C1; cc <= m.Value.C2; cc++) if (xs.ContainsKey(cc)) x2 = Math.Max(x2, xs[cc] + layout.Cols.Size(cc));
                    for (int rr = m.Value.R1; rr <= m.Value.R2; rr++) if (ys.ContainsKey(rr)) y2 = Math.Max(y2, ys[rr] + layout.Rows.Size(rr));
                    return new Rect(x1, y1, x2 - x1, y2 - y1);
                }
                return new Rect(xs[c], ys[r], layout.Cols.Size(c), layout.Rows.Size(r));
            }
            var cells = new List<IXLCell>();
            var filled = new HashSet<(int, int)>();
            foreach (var r in rows)
                foreach (var c in ws.Range(r, cols.Min(), r, cols.Max()).CellsUsed(XLCellsUsedOptions.All))
                {
                    if (!xs.ContainsKey(c.Address.ColumnNumber)) continue;
                    cells.Add(c);
                    if (!c.IsEmpty(XLCellsUsedOptions.Contents)) filled.Add((r, c.Address.ColumnNumber));
                }
            // fills
            foreach (var c in cells)
            {
                int r = c.Address.RowNumber, col = c.Address.ColumnNumber;
                var m = layout.MergeAt(r, col);
                if (m.HasValue && (m.Value.R1 != r || m.Value.C1 != col)) continue;
                var fill = cf.For(c).Fill ?? SheetCanvas.FillOf(c.Style, wb);
                if (fill.HasValue) dc.DrawRectangle(new SolidColorBrush(fill.Value), null, CellBox(r, col));
            }
            // gridlines
            if (s.Gridlines)
            {
                foreach (var r in rows) dc.DrawLine(grid, new Point(headW, ys[r] + layout.Rows.Size(r)), new Point(right, ys[r] + layout.Rows.Size(r)));
                foreach (var c in cols) dc.DrawLine(grid, new Point(xs[c] + layout.Cols.Size(c), headH), new Point(xs[c] + layout.Cols.Size(c), bottom));
                dc.DrawLine(grid, new Point(headW, headH), new Point(right, headH));
                dc.DrawLine(grid, new Point(headW, headH), new Point(headW, bottom));
            }
            // text
            foreach (var c in cells)
            {
                int r = c.Address.RowNumber, col = c.Address.ColumnNumber;
                if (c.IsEmpty(XLCellsUsedOptions.Contents)) continue;
                var m = layout.MergeAt(r, col);
                if (m.HasValue && (m.Value.R1 != r || m.Value.C1 != col)) continue;
                string text; bool isNum = false;
                try { var v = c.Value; isNum = v.IsNumber || v.IsDateTime || v.IsTimeSpan; text = c.GetFormattedString(); } catch { text = "#VALUE!"; }
                if (string.IsNullOrEmpty(text)) continue;
                var st = c.Style; var f = st.Font;
                var color = cf.For(c).Font ?? XlColors.ToWpf(f.FontColor, wb, true) ?? Colors.Black;
                var face = new Typeface(new FontFamily(f.FontName ?? "Calibri"), f.Italic ? FontStyles.Italic : FontStyles.Normal, f.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
                var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, Units.PtToPx(f.FontSize > 0 ? f.FontSize : 11), new SolidColorBrush(color), dip);
                if (f.Underline != XLFontUnderlineValues.None) ft.SetTextDecorations(TextDecorations.Underline);
                if (f.Strikethrough) ft.SetTextDecorations(TextDecorations.Strikethrough);
                var box = CellBox(r, col);
                var h = st.Alignment.Horizontal;
                if (h == XLAlignmentHorizontalValues.General) h = isNum ? XLAlignmentHorizontalValues.Right : XLAlignmentHorizontalValues.Left;
                bool wrap = st.Alignment.WrapText;
                double pad = 2.5 + st.Alignment.Indent * 9;
                var clip = box;
                if (wrap) { ft.MaxTextWidth = Math.Max(1, box.Width - pad * 2); ft.MaxTextHeight = Math.Max(1, box.Height); }
                else if (isNum && ft.Width > box.Width - pad) ft = new FormattedText(new string('#', Math.Max(1, (int)(box.Width / 7))), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, ft.Height * 0.75, new SolidColorBrush(color), dip);
                else if (!isNum && !m.HasValue && h == XLAlignmentHorizontalValues.Left && ft.Width + pad > box.Width)
                {
                    double rr = box.Right; int nc = cols.IndexOf(col) + 1;
                    while (rr - box.Left < ft.Width + pad * 2 && nc < cols.Count && !filled.Contains((r, cols[nc]))) { rr += layout.Cols.Size(cols[nc]); nc++; }
                    clip = new Rect(box.Left, box.Top, rr - box.Left, box.Height);
                }
                double tx = h switch { XLAlignmentHorizontalValues.Right => box.Right - pad - ft.Width, XLAlignmentHorizontalValues.Center or XLAlignmentHorizontalValues.CenterContinuous => box.X + (box.Width - ft.Width) / 2, _ => box.X + pad };
                if (wrap) { ft.TextAlignment = h == XLAlignmentHorizontalValues.Right ? TextAlignment.Right : h == XLAlignmentHorizontalValues.Center ? TextAlignment.Center : TextAlignment.Left; tx = box.X + pad; }
                double ty = st.Alignment.Vertical switch { XLAlignmentVerticalValues.Top => box.Y + 1, XLAlignmentVerticalValues.Center => box.Y + (box.Height - ft.Height) / 2, _ => box.Bottom - ft.Height - 1 };
                dc.PushClip(new RectangleGeometry(clip));
                dc.DrawText(ft, new Point(tx, ty));
                dc.Pop();
            }
            // borders
            foreach (var c in cells)
            {
                IXLBorder b; try { b = c.Style.Border; } catch { continue; }
                var box = new Rect(xs[c.Address.ColumnNumber], ys[c.Address.RowNumber], layout.Cols.Size(c.Address.ColumnNumber), layout.Rows.Size(c.Address.RowNumber));
                void L(XLBorderStyleValues st, XLColor col, Point a, Point z) { if (st == XLBorderStyleValues.None) return; dc.DrawLine(new Pen(new SolidColorBrush(XlColors.ToWpf(col, wb, true) ?? Colors.Black), st == XLBorderStyleValues.Thick ? 2.5 : st == XLBorderStyleValues.Medium ? 1.6 : 0.8), a, z); }
                L(b.TopBorder, b.TopBorderColor, box.TopLeft, box.TopRight);
                L(b.BottomBorder, b.BottomBorderColor, box.BottomLeft, box.BottomRight);
                L(b.LeftBorder, b.LeftBorderColor, box.TopLeft, box.BottomLeft);
                L(b.RightBorder, b.RightBorderColor, box.TopRight, box.BottomRight);
            }
            // pictures
            foreach (var p in ws.Pictures)
            {
                try
                {
                    var tl = p.TopLeftCell; if (tl == null || !xs.ContainsKey(tl.Address.ColumnNumber) || !ys.ContainsKey(tl.Address.RowNumber)) continue;
                    var bmp = new BitmapImage(); bmp.BeginInit(); bmp.StreamSource = new MemoryStream(p.ImageStream.ToArray()); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.EndInit();
                    dc.DrawImage(bmp, new Rect(xs[tl.Address.ColumnNumber], ys[tl.Address.RowNumber], p.Width, p.Height));
                }
                catch { }
            }
            // charts
            foreach (var ch in ChartStore.For(book).Where(x => x.Sheet == ws.Name))
            {
                if (!xs.ContainsKey(ch.Col) || !ys.ContainsKey(ch.Row)) continue;
                var rect = new Rect(xs[ch.Col] + ch.OffsetX, ys[ch.Row] + ch.OffsetY, ch.Width, ch.Height);
                ChartRenderer.Draw(dc, ch, book.Wb, rect);
            }
            // headings
            if (s.Headings)
            {
                var hb = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE)); var hp = new Pen(Brushes.Gray, 0.5);
                var face = new Typeface("Calibri");
                foreach (var c in cols)
                {
                    var rc = new Rect(xs[c], 0, layout.Cols.Size(c), headH); dc.DrawRectangle(hb, hp, rc);
                    var ft = new FormattedText(A1.Col(c), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 11, Brushes.Black, dip);
                    dc.DrawText(ft, new Point(rc.X + (rc.Width - ft.Width) / 2, (headH - ft.Height) / 2));
                }
                foreach (var r in rows)
                {
                    var rc = new Rect(0, ys[r], headW, layout.Rows.Size(r)); dc.DrawRectangle(hb, hp, rc);
                    var ft = new FormattedText(r.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 11, Brushes.Black, dip);
                    dc.DrawText(ft, new Point((headW - ft.Width) / 2, rc.Y + (rc.Height - ft.Height) / 2));
                }
            }
        }

        public static string ExpandCodes(string t, int page, int pages, Book book, IXLWorksheet ws)
        {
            if (string.IsNullOrEmpty(t)) return "";
            return t.Replace("&P", page.ToString()).Replace("&N", pages.ToString()).Replace("&A", ws.Name).Replace("&F", book.DisplayName)
                .Replace("&D", DateTime.Today.ToString("d", CultureInfo.CurrentCulture)).Replace("&T", DateTime.Now.ToString("t", CultureInfo.CurrentCulture))
                .Replace("&Z", book.FilePath != null ? System.IO.Path.GetDirectoryName(book.FilePath) + "\\" : "").Replace("&&", "&");
        }

        static void DrawHeaderFooter(DrawingContext dc, PrintSetup s, int page, int pages, Book book, IXLWorksheet ws, double pageW, double pageH)
        {
            var face = new Typeface("Calibri");
            void Text(string t, double y, int align)
            {
                t = ExpandCodes(System.Text.RegularExpressions.Regex.Replace(t ?? "", "&\"[^\"]*\"|&\\d+|&[BIUSEXYKL]", ""), page, pages, book, ws);
                if (t.Length == 0) return;
                var ft = new FormattedText(t, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, 14.67, Brushes.Black, 1.0);
                double x = align == 0 ? s.Left * 96 : align == 1 ? (pageW - ft.Width) / 2 : pageW - s.Right * 96 - ft.Width;
                dc.DrawText(ft, new Point(x, y));
            }
            double hy = s.HeaderMargin * 96, fy = pageH - s.FooterMargin * 96 - 18;
            Text(s.HeaderL, hy, 0); Text(s.HeaderC, hy, 1); Text(s.HeaderR, hy, 2);
            Text(s.FooterL, fy, 0); Text(s.FooterC, fy, 1); Text(s.FooterR, fy, 2);
        }

        public static void SaveXps(FixedDocument doc, string path)
        {
            if (File.Exists(path)) File.Delete(path);
            using var xps = new XpsDocument(path, FileAccess.ReadWrite);
            XpsDocument.CreateXpsDocumentWriter(xps).Write(doc);
        }

        /// <summary>PDF through Windows' "Microsoft Print to PDF" printer (pages rendered at 200 dpi), straight to the file.</summary>
        public static void SavePdf(FixedDocument doc, string file)
        {
            bool has;
            try { has = System.Drawing.Printing.PrinterSettings.InstalledPrinters.Cast<string>().Any(p => p.Equals("Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase)); }
            catch { has = false; }
            if (!has) throw new InvalidOperationException("Valco Spreadsheets uses Windows' built-in \"Microsoft Print to PDF\" printer to make PDF files, and it isn't installed on this PC. You can turn it on in Settings → Bluetooth & devices → Printers & scanners, or save as XPS instead.");
            var pag = doc.DocumentPaginator;
            pag.ComputePageCount();
            var images = new List<byte[]>();
            const double dpi = 200;
            Size size = pag.PageSize;
            for (int i = 0; i < pag.PageCount; i++)
            {
                var page = pag.GetPage(i);
                size = page.Size;
                var rtb = new RenderTargetBitmap((int)(size.Width * dpi / 96), (int)(size.Height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
                var bg = new DrawingVisual();
                using (var dc = bg.RenderOpen()) dc.DrawRectangle(Brushes.White, null, new Rect(size));
                rtb.Render(bg);
                var fp = (FixedPage)page.Visual;
                fp.Measure(size); fp.Arrange(new Rect(size)); fp.UpdateLayout();
                rtb.Render(fp);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using var ms = new MemoryStream(); enc.Save(ms); images.Add(ms.ToArray());
            }
            if (File.Exists(file)) File.Delete(file);
            using var pd = new System.Drawing.Printing.PrintDocument();
            pd.PrinterSettings.PrinterName = "Microsoft Print to PDF";
            pd.PrinterSettings.PrintToFile = true;
            pd.PrinterSettings.PrintFileName = file;
            bool landscape = size.Width > size.Height;
            pd.DefaultPageSettings.Landscape = landscape;
            double pw = Math.Min(size.Width, size.Height) / 96, ph = Math.Max(size.Width, size.Height) / 96;
            pd.DefaultPageSettings.PaperSize = new System.Drawing.Printing.PaperSize("Custom", (int)Math.Round(pw * 100), (int)Math.Round(ph * 100));
            pd.DefaultPageSettings.Margins = new System.Drawing.Printing.Margins(0, 0, 0, 0);
            pd.PrintController = new System.Drawing.Printing.StandardPrintController();
            int n = 0;
            pd.PrintPage += (s, e) =>
            {
                using var ms = new MemoryStream(images[n]);
                using var bmp = new System.Drawing.Bitmap(ms);
                e.Graphics.DrawImage(bmp, new System.Drawing.RectangleF(-e.PageSettings.HardMarginX, -e.PageSettings.HardMarginY, e.PageBounds.Width, e.PageBounds.Height));
                n++;
                e.HasMorePages = n < images.Count;
            };
            pd.Print();
            for (int i = 0; i < 50 && (!File.Exists(file) || new FileInfo(file).Length == 0); i++) System.Threading.Thread.Sleep(100);
        }
    }

    /// <summary>Print Preview: the pages in a DocumentViewer with Print, Page Setup, zoom and Close.</summary>
    public class PrintPreviewWindow : VistaWindow
    {
        readonly DocumentViewer _viewer;
        public PrintPreviewWindow(Window owner, FixedDocument doc, string title, Action print, Func<FixedDocument> pageSetup)
        {
            Owner = owner;
            Title = "Print Preview - " + title;
            Width = Math.Min(1000, SystemParameters.WorkArea.Width - 80);
            Height = SystemParameters.WorkArea.Height - 60;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            _viewer = new DocumentViewer { Document = doc, Background = (Brush)Application.Current.FindResource("WorkspaceBrush") };
            _viewer.Loaded += (s, e) => _viewer.FitToHeight();
            var bar = new Border { Style = (Style)Application.Current.FindResource("CommandBar") };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var count = new TextBlock { Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            void Count() { var n = ((FixedDocument)_viewer.Document).Pages.Count; count.Text = n == 1 ? "Page 1 of 1" : n + " pages"; }
            sp.Children.Add(CmdBtn("IconPrint", "Print...", () => { Close(); print(); }));
            sp.Children.Add(CmdBtn("IconPageSetup", "Page Setup...", () => { _viewer.Document = pageSetup(); Count(); }));
            sp.Children.Add(CmdBtn("IconZoom", "Zoom", () => { if (_viewer.Zoom > 120) _viewer.FitToHeight(); else _viewer.Zoom = 150; }));
            sp.Children.Add(CmdBtn("IconOnePage", "One Page", () => _viewer.FitToHeight()));
            sp.Children.Add(CmdBtn("IconPageWidth", "Page Width", () => _viewer.FitToWidth()));
            sp.Children.Add(CmdBtn("IconDelete", "Close Print Preview", Close));
            sp.Children.Add(count);
            Count();
            bar.Child = sp;
            var dp = new DockPanel();
            DockPanel.SetDock(bar, Dock.Top);
            dp.Children.Add(bar);
            dp.Children.Add(_viewer);
            Content = dp;
            PreviewKeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
        }

        static Button CmdBtn(string icon, string text, Action click)
        {
            var b = new Button { Style = (Style)Application.Current.FindResource("CmdButton") };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(UI.Icon(icon, 16));
            sp.Children.Add(new TextBlock { Text = text, Style = (Style)Application.Current.FindResource("CmdLabel") });
            b.Content = sp;
            b.Click += (s, e) => click();
            return b;
        }
    }

    /// <summary>Page Setup: Page, Margins, Header/Footer, Sheet.</summary>
    public class PageSetupDialog : VDialog
    {
        public PageSetupDialog(Window owner, IXLWorksheet ws, CellRect selection) : base(owner, "Page Setup", 500)
        {
            var p = ws.PageSetup;
            var tabs = new TabControl { Height = 360 };
            // Page
            var pg = new StackPanel { Margin = new Thickness(10) };
            var portrait = Radio("Portrait", "o", p.PageOrientation != XLPageOrientation.Landscape); var landscape = Radio("Landscape", "o", p.PageOrientation == XLPageOrientation.Landscape);
            pg.Children.Add(Heading("Orientation")); pg.Children.Add(Row(portrait, landscape));
            pg.Children.Add(Heading("Scaling"));
            var adjust = Radio("Adjust to:", "sc", p.PagesWide == 0 && p.PagesTall == 0); var fit = Radio("Fit to:", "sc", p.PagesWide > 0 || p.PagesTall > 0);
            var pct = Box((p.Scale > 0 ? p.Scale : 100).ToString(), 50); var wide = Box(Math.Max(1, p.PagesWide).ToString(), 40); var tall = Box(p.PagesTall > 0 ? p.PagesTall.ToString() : "", 40);
            pg.Children.Add(Row(adjust, pct, new TextBlock { Text = " % normal size", VerticalAlignment = VerticalAlignment.Center }));
            pg.Children.Add(Row(fit, wide, new TextBlock { Text = " page(s) wide by ", VerticalAlignment = VerticalAlignment.Center }, tall, new TextBlock { Text = " tall", VerticalAlignment = VerticalAlignment.Center }));
            var papers = new[] { ("Letter", XLPaperSize.LetterPaper), ("Legal", XLPaperSize.LegalPaper), ("A4", XLPaperSize.A4Paper), ("A5", XLPaperSize.A5Paper), ("Executive", XLPaperSize.ExecutivePaper), ("Tabloid", XLPaperSize.TabloidPaper), ("A3", XLPaperSize.A3Paper) };
            var paper = Combo(papers.Select(x => x.Item1), Math.Max(0, Array.FindIndex(papers, x => x.Item2 == p.PaperSize)), 160);
            pg.Children.Add(Heading("Paper")); pg.Children.Add(Row(new TextBlock { Text = "Paper size:", Width = 80, VerticalAlignment = VerticalAlignment.Center }, paper));
            tabs.Items.Add(new TabItem { Header = "Page", Content = pg });
            // Margins
            var mg = new StackPanel { Margin = new Thickness(10) };
            var top = Box(p.Margins.Top.ToString("0.##"), 60); var bottom = Box(p.Margins.Bottom.ToString("0.##"), 60); var left = Box(p.Margins.Left.ToString("0.##"), 60); var right = Box(p.Margins.Right.ToString("0.##"), 60);
            var header = Box(p.Margins.Header.ToString("0.##"), 60); var footer = Box(p.Margins.Footer.ToString("0.##"), 60);
            mg.Children.Add(Label("Margins in inches:"));
            mg.Children.Add(Row(new TextBlock { Text = "Top:", Width = 60, VerticalAlignment = VerticalAlignment.Center }, top, new TextBlock { Text = "   Header:", Width = 70, VerticalAlignment = VerticalAlignment.Center }, header));
            mg.Children.Add(Row(new TextBlock { Text = "Left:", Width = 60, VerticalAlignment = VerticalAlignment.Center }, left, new TextBlock { Text = "   Right:", Width = 70, VerticalAlignment = VerticalAlignment.Center }, right));
            mg.Children.Add(Row(new TextBlock { Text = "Bottom:", Width = 60, VerticalAlignment = VerticalAlignment.Center }, bottom, new TextBlock { Text = "   Footer:", Width = 70, VerticalAlignment = VerticalAlignment.Center }, footer));
            var ch = Chk("Center on page horizontally", p.CenterHorizontally); var cv = Chk("Center on page vertically", p.CenterVertically);
            mg.Children.Add(ch); mg.Children.Add(cv);
            tabs.Items.Add(new TabItem { Header = "Margins", Content = mg });
            // Header/Footer
            var hf = new StackPanel { Margin = new Thickness(10) };
            var presets = new[] { "(none)", "Page &P", "Page &P of &N", "&A", "&F", "&D", "Confidential" };
            string Get(IXLHFItem i) { try { return i.GetText(XLHFOccurrence.AllPages) ?? ""; } catch { return ""; } }
            var hc = Combo(presets, 0, 300, true); hc.Text = Get(p.Header.Center);
            var fc = Combo(presets, 0, 300, true); fc.Text = Get(p.Footer.Center);
            hf.Children.Add(Label("Header (centre):")); hf.Children.Add(hc);
            hf.Children.Add(Label("Footer (centre):", 8)); hf.Children.Add(fc);
            hf.Children.Add(new TextBlock { Text = "Codes: &P page number, &N number of pages, &A sheet name, &F file name, &D date, &T time.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 10, 0, 0) });
            tabs.Items.Add(new TabItem { Header = "Header/Footer", Content = hf });
            // Sheet
            var sh = new StackPanel { Margin = new Thickness(10) };
            var areaText = p.PrintAreas.FirstOrDefault() is IXLRange pa ? pa.RangeAddress.ToString() : "";
            var area = Box(areaText, 200);
            sh.Children.Add(Row(new TextBlock { Text = "Print area:", Width = 140, VerticalAlignment = VerticalAlignment.Center }, area));
            var rowsRep = Box(p.FirstRowToRepeatAtTop > 0 ? $"${p.FirstRowToRepeatAtTop}:${p.LastRowToRepeatAtTop}" : "", 120);
            var colsRep = Box(p.FirstColumnToRepeatAtLeft > 0 ? $"${A1.Col(p.FirstColumnToRepeatAtLeft)}:${A1.Col(p.LastColumnToRepeatAtLeft)}" : "", 120);
            sh.Children.Add(Heading("Print titles"));
            sh.Children.Add(Row(new TextBlock { Text = "Rows to repeat at top:", Width = 140, VerticalAlignment = VerticalAlignment.Center }, rowsRep));
            sh.Children.Add(Row(new TextBlock { Text = "Columns to repeat at left:", Width = 140, VerticalAlignment = VerticalAlignment.Center }, colsRep));
            sh.Children.Add(Heading("Print"));
            var gl = Chk("Gridlines", p.ShowGridlines); var hd = Chk("Row and column headings", p.ShowRowAndColumnHeadings); var bw = Chk("Black and white", p.BlackAndWhite);
            sh.Children.Add(gl); sh.Children.Add(hd); sh.Children.Add(bw);
            sh.Children.Add(Heading("Page order"));
            var down = Radio("Down, then over", "po", p.PageOrder != XLPageOrderValues.OverThenDown); var over = Radio("Over, then down", "po", p.PageOrder == XLPageOrderValues.OverThenDown);
            sh.Children.Add(down); sh.Children.Add(over);
            tabs.Items.Add(new TabItem { Header = "Sheet", Content = sh });

            Build(tabs, () =>
            {
                double D(TextBox b, double def) => double.TryParse(b.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var v) && v >= 0 && v < 20 ? v : def;
                p.PageOrientation = landscape.IsChecked == true ? XLPageOrientation.Landscape : XLPageOrientation.Portrait;
                if (fit.IsChecked == true) p.FitToPages(int.TryParse(wide.Text, out var w) ? Math.Max(0, w) : 1, int.TryParse(tall.Text, out var t) ? Math.Max(0, t) : 0);
                else { p.PagesWide = 0; p.PagesTall = 0; p.Scale = int.TryParse(pct.Text, out var sc) ? Math.Clamp(sc, 10, 400) : 100; }
                p.PaperSize = papers[Math.Max(0, paper.SelectedIndex)].Item2;
                p.Margins.Top = D(top, 0.75); p.Margins.Bottom = D(bottom, 0.75); p.Margins.Left = D(left, 0.7); p.Margins.Right = D(right, 0.7);
                p.Margins.Header = D(header, 0.3); p.Margins.Footer = D(footer, 0.3);
                p.CenterHorizontally = ch.IsChecked == true; p.CenterVertically = cv.IsChecked == true;
                p.Header.Center.Clear(); p.Footer.Center.Clear();
                if (hc.Text.Length > 0 && hc.Text != "(none)") p.Header.Center.AddText(hc.Text, XLHFOccurrence.AllPages);
                if (fc.Text.Length > 0 && fc.Text != "(none)") p.Footer.Center.AddText(fc.Text, XLHFOccurrence.AllPages);
                p.PrintAreas.Clear();
                if (A1.TryParse(area.Text, out var ar)) p.PrintAreas.Add(ar.R1, ar.C1, ar.R2, ar.C2);
                try { if (rowsRep.Text.Trim().Length > 0 && A1.TryParse(rowsRep.Text, out var rr)) p.SetRowsToRepeatAtTop(rr.R1, rr.R2); else if (p.FirstRowToRepeatAtTop > 0) p.SetRowsToRepeatAtTop(0, 0); } catch { }
                try { if (colsRep.Text.Trim().Length > 0 && A1.TryParse(colsRep.Text, out var cr)) p.SetColumnsToRepeatAtLeft(cr.C1, cr.C2); else if (p.FirstColumnToRepeatAtLeft > 0) p.SetColumnsToRepeatAtLeft(0, 0); } catch { }
                p.ShowGridlines = gl.IsChecked == true; p.ShowRowAndColumnHeadings = hd.IsChecked == true; p.BlackAndWhite = bw.IsChecked == true;
                p.PageOrder = over.IsChecked == true ? XLPageOrderValues.OverThenDown : XLPageOrderValues.DownThenOver;
                return true;
            });
        }
    }
}
