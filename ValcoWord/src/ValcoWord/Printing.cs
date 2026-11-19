using System;
using System.Globalization;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Xps;
using System.Windows.Xps.Packaging;

namespace ValcoWord
{
    /// <summary>Wraps the flow paginator to add the header text and page numbers.</summary>
    public class DecoratedPaginator : DocumentPaginator
    {
        readonly DocumentPaginator _inner;
        readonly PageSettings _ps;

        public DecoratedPaginator(DocumentPaginator inner, PageSettings ps) { _inner = inner; _ps = ps; }

        public static string Expand(string text, int page, int pages, string title, PageSettings ps = null)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("{PAGE}", FormatNumber(page, ps)).Replace("{NUMPAGES}", pages.ToString())
                       .Replace("{DATE}", DateTime.Now.ToString("d")).Replace("{TITLE}", title ?? "").Replace("{AUTHOR}", Settings.Current.UserName ?? "");
        }

        public string Title;

        /// <summary>Formats a page number using the document's page-number format and starting number.</summary>
        public static string FormatNumber(int page, PageSettings ps)
        {
            int n = page + (ps == null ? 0 : ps.NumStart - 1);
            switch (ps?.NumFormat ?? "1")
            {
                case "-1-": return "- " + n + " -";
                case "a": return Letters(n).ToLowerInvariant();
                case "A": return Letters(n);
                case "i": return Roman(n).ToLowerInvariant();
                case "I": return Roman(n);
                default: return n.ToString();
            }
        }
        static string Letters(int n) { if (n < 1) return n.ToString(); var c = (char)('A' + (n - 1) % 26); return new string(c, (n - 1) / 26 + 1); }
        static string Roman(int n)
        {
            if (n < 1 || n > 3999) return n.ToString();
            var vals = new[] { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            var syms = new[] { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < vals.Length; i++) while (n >= vals[i]) { sb.Append(syms[i]); n -= vals[i]; }
            return sb.ToString();
        }

        static Color Hex(string h, Color fallback)
        {
            try { return string.IsNullOrEmpty(h) ? fallback : (Color)ColorConverter.ConvertFromString("#" + h.TrimStart('#')); } catch { return fallback; }
        }

        public override DocumentPage GetPage(int pageNumber)
        {
            var page = _inner.GetPage(pageNumber);
            if (page == DocumentPage.Missing) return page;
            var cv = new ContainerVisual();
            var bg = new DrawingVisual();
            var size = PageSize;
            using (var dc = bg.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Hex(_ps.PageColor, Colors.White)), null, new Rect(size));
                if (!string.IsNullOrWhiteSpace(_ps.Watermark))
                {
                    var ft = new FormattedText(_ps.Watermark, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Calibri"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 120,
                        new SolidColorBrush(Color.FromArgb(0x70, Hex(_ps.WatermarkColor, Colors.Silver).R, Hex(_ps.WatermarkColor, Colors.Silver).G, Hex(_ps.WatermarkColor, Colors.Silver).B)), 1.0);
                    double maxW = Math.Sqrt(size.Width * size.Width + size.Height * size.Height) * 0.75;
                    if (ft.Width > maxW) ft.SetFontSize(120 * maxW / ft.Width);
                    dc.PushTransform(new RotateTransform(-45, size.Width / 2, size.Height / 2));
                    dc.DrawText(ft, new Point((size.Width - ft.Width) / 2, (size.Height - ft.Height) / 2));
                    dc.Pop();
                }
                if (_ps.PageBorder)
                    dc.DrawRectangle(null, new Pen(new SolidColorBrush(Hex(_ps.PageBorderColor, Colors.Black)), Math.Max(0.5, _ps.PageBorderWidth)), new Rect(32, 32, size.Width - 64, size.Height - 64));
            }
            cv.Children.Add(bg);
            cv.Children.Add(page.Visual);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var tf = new Typeface("Calibri");
                int n = pageNumber + 1, total = PageCount;
                double ml = _ps.MarginLeft * 96, mr = _ps.MarginRight * 96;
                void Slot(string text, double y, int align, bool top)
                {
                    var s = Expand(text, n, total, Title, _ps);
                    if (string.IsNullOrWhiteSpace(s)) return;
                    var ft = new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf, 14.67, Brushes.Black, 1.0);
                    double x = align == 0 ? ml : align == 1 ? (size.Width - ft.Width) / 2 : size.Width - mr - ft.Width;
                    dc.DrawText(ft, new Point(x, top ? y : y - ft.Height));
                }
                double hy = Math.Max(14, 0.5 * 96), fy = size.Height - Math.Max(14, 0.5 * 96);
                Slot(_ps.HeaderLeft, hy, 0, true); Slot(_ps.HeaderText, hy, 1, true); Slot(_ps.HeaderRight, hy, 2, true);
                Slot(_ps.FooterLeft, fy, 0, false); Slot(_ps.FooterCenter, fy, 1, false); Slot(_ps.FooterRight, fy, 2, false);
            }
            cv.Children.Add(dv);
            return new DocumentPage(cv, PageSize, page.BleedBox, page.ContentBox);
        }

        public override bool IsPageCountValid => _inner.IsPageCountValid;
        public override int PageCount => _inner.PageCount;
        public override Size PageSize { get => _inner.PageSize; set => _inner.PageSize = value; }
        public override IDocumentPaginatorSource Source => _inner.Source;
        public override void ComputePageCount() => _inner.ComputePageCount();
    }

    public static class Printing
    {
        /// <summary>Makes an independent copy of the document laid out on real pages.</summary>
        public static FlowDocument PrintCopy(FlowDocument src, PageSettings ps)
        {
            var ms = new MemoryStream();
            DocxWriter.Save(src, ps, "", "", ms);
            ms.Position = 0;
            var doc = DocxReader.Load(ms).Document;
            doc.PageWidth = ps.WidthPx;
            doc.PageHeight = ps.HeightPx;
            doc.PagePadding = new Thickness(ps.MarginLeft * 96, ps.MarginTop * 96, ps.MarginRight * 96, ps.MarginBottom * 96);
            int cols = Math.Max(1, ps.Columns);
            doc.ColumnGap = ps.ColumnSpacingIn * 96;
            doc.ColumnWidth = cols == 1 ? ps.WidthPx : (ps.BodyWidthPx - (cols - 1) * doc.ColumnGap) / cols;
            doc.IsColumnWidthFlexible = cols == 1;
            doc.ColumnRuleWidth = 0;
            doc.IsHyphenationEnabled = ps.Hyphenation;
            doc.Background = Brushes.Transparent;
            doc.FontFamily = new FontFamily(Settings.Current.DefaultFont);
            doc.FontSize = Settings.Current.DefaultSizePt * Fmt.PxPerPt;
            doc.IsOptimalParagraphEnabled = true;
            return doc;
        }

        public static DocumentPaginator Paginator(FlowDocument src, PageSettings ps, string title = null)
        {
            var copy = PrintCopy(src, ps);
            var inner = ((IDocumentPaginatorSource)copy).DocumentPaginator;
            inner.PageSize = new Size(ps.WidthPx, ps.HeightPx);
            var p = new DecoratedPaginator(inner, ps) { Title = title };
            p.ComputePageCount();
            return p;
        }

        public static void Print(Window owner, FlowDocument doc, PageSettings ps, string title, bool showDialog = true)
        {
            var pd = new PrintDialog { UserPageRangeEnabled = false };
            if (showDialog && pd.ShowDialog() != true) return;
            try
            {
                if (pd.PrintTicket != null) pd.PrintTicket.PageOrientation = ps.Landscape ? PageOrientation.Landscape : PageOrientation.Portrait;
            }
            catch { }
            pd.PrintDocument(Paginator(doc, ps, title), title ?? "Document");
        }

        public static bool ExportPdf(Window owner, FlowDocument doc, PageSettings ps, string title)
        {
            PrintQueue queue = null;
            try
            {
                var server = new LocalPrintServer();
                queue = server.GetPrintQueues().FirstOrDefault(q => q.Name.IndexOf("Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch { }
            if (queue == null)
            {
                VistaDialog.Show(owner, "Valco Word uses Windows' built-in \"Microsoft Print to PDF\" printer to make PDF files, and it isn't installed on this PC.\n\nYou can turn it on in Windows Settings → Bluetooth & devices → Printers & scanners → Add device.",
                    "Export to PDF", MessageBoxButton.OK, DlgIcon.Warning, "The PDF printer isn't available");
                return false;
            }
            var ticket = queue.DefaultPrintTicket ?? new PrintTicket();
            ticket.PageOrientation = ps.Landscape ? PageOrientation.Landscape : PageOrientation.Portrait;
            var writer = PrintQueue.CreateXpsDocumentWriter(queue);
            writer.Write(Paginator(doc, ps, title), ticket);
            return true;
        }

        /// <summary>Writes a PDF straight to a file (no dialog) through "Microsoft Print to PDF"; pages are rendered at 200 dpi.</summary>
        public static bool ExportPdfTo(Window owner, FlowDocument doc, PageSettings ps, string title, string file)
        {
            bool has;
            try { has = System.Drawing.Printing.PrinterSettings.InstalledPrinters.Cast<string>().Any(p => p.Equals("Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase)); }
            catch { has = false; }
            if (!has)
            {
                VistaDialog.Show(owner, "Valco Word uses Windows' built-in \"Microsoft Print to PDF\" printer to make PDF files, and it isn't installed on this PC.", "PDF", MessageBoxButton.OK, DlgIcon.Warning);
                return false;
            }
            var pag = Paginator(doc, ps, title);
            pag.ComputePageCount();
            var images = new System.Collections.Generic.List<byte[]>();
            const double dpi = 200;
            for (int i = 0; i < pag.PageCount; i++)
            {
                var page = pag.GetPage(i);
                var size = page.Size;
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(size.Width * dpi / 96), (int)(size.Height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
                var host = new DrawingVisual();
                using (var dc = host.RenderOpen()) dc.DrawRectangle(Brushes.White, null, new Rect(size));
                rtb.Render(host);
                rtb.Render(page.Visual);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                using var ms = new MemoryStream(); enc.Save(ms); images.Add(ms.ToArray());
            }
            using var pd = new System.Drawing.Printing.PrintDocument();
            pd.PrinterSettings.PrinterName = "Microsoft Print to PDF";
            pd.PrinterSettings.PrintToFile = true;
            pd.PrinterSettings.PrintFileName = file;
            pd.DocumentName = title ?? "Document";
            pd.DefaultPageSettings.Landscape = ps.Landscape;
            double pw = Math.Min(ps.WidthIn, ps.HeightIn), ph = Math.Max(ps.WidthIn, ps.HeightIn);
            pd.DefaultPageSettings.PaperSize = new System.Drawing.Printing.PaperSize(ps.Paper ?? "Custom", (int)Math.Round(pw * 100), (int)Math.Round(ph * 100));
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
            // the PDF driver finishes writing asynchronously
            for (int i = 0; i < 50 && (!File.Exists(file) || new FileInfo(file).Length == 0); i++) System.Threading.Thread.Sleep(100);
            return File.Exists(file);
        }

        public static void ExportXps(FlowDocument doc, PageSettings ps, string path)
        {
            if (File.Exists(path)) File.Delete(path);
            using var xps = new XpsDocument(path, FileAccess.ReadWrite);
            XpsDocument.CreateXpsDocumentWriter(xps).Write(Paginator(doc, ps));
        }
    }

    /// <summary>Print Preview window.</summary>
    public class PreviewWindow : VistaWindow
    {
        readonly Package _pkg;
        readonly Uri _uri;
        readonly XpsDocument _xps;
        readonly DocumentViewer _viewer;

        public PreviewWindow(Window owner, FlowDocument doc, PageSettings ps, string title, Action print)
        {
            Owner = owner;
            Title = "Print Preview - " + title;
            Width = Math.Min(1000, SystemParameters.WorkArea.Width - 80);
            Height = SystemParameters.WorkArea.Height - 60;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var ms = new MemoryStream();
            _pkg = Package.Open(ms, FileMode.Create, FileAccess.ReadWrite);
            _uri = new Uri("pack://valcopreview" + Guid.NewGuid().ToString("N") + ".xps");
            PackageStore.AddPackage(_uri, _pkg);
            _xps = new XpsDocument(_pkg, CompressionOption.NotCompressed, _uri.AbsoluteUri);
            var pag = Printing.Paginator(doc, ps, title);
            XpsDocument.CreateXpsDocumentWriter(_xps).Write(pag);
            int pages = pag.PageCount;

            _viewer = new DocumentViewer { Document = _xps.GetFixedDocumentSequence(), Background = (Brush)FindResource("WorkspaceBrush") };
            _viewer.Loaded += (s, e) => _viewer.FitToWidth();

            var bar = new Border { Style = (Style)FindResource("CommandBar") };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(CmdBtn("IconPrint", "Print...", () => { print(); Close(); }));
            sp.Children.Add(CmdBtn("IconZoom", "One Page", () => _viewer.FitToHeight()));
            sp.Children.Add(CmdBtn("IconZoom", "Page Width", () => _viewer.FitToWidth()));
            sp.Children.Add(CmdBtn("IconZoom", "Two Pages", () => _viewer.FitToMaxPagesAcross(2)));
            sp.Children.Add(CmdBtn("IconDelete", "Close Print Preview", Close));
            sp.Children.Add(new TextBlock { Text = pages == 1 ? "1 page" : pages + " pages", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) });
            bar.Child = sp;

            var dp = new DockPanel();
            DockPanel.SetDock(bar, Dock.Top);
            dp.Children.Add(bar);
            dp.Children.Add(_viewer);
            Content = dp;
            PreviewKeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
            Closed += (s, e) =>
            {
                try { _xps.Close(); } catch { }
                try { PackageStore.RemovePackage(_uri); } catch { }
            };
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
}
