using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ValcoWord
{
    public partial class MainWindow
    {
        void PageChanged()
        {
            ApplyPage();
            MarkDirty();
            DrawOverlay();
        }

        // ---------------- margins / orientation / size / columns ----------------
        ContextMenu MarginsMenu()
        {
            var presets = new (string Name, double T, double B, double L, double R)[]
            {
                ("Normal", 1, 1, 1, 1), ("Narrow", 0.5, 0.5, 0.5, 0.5), ("Moderate", 1, 1, 0.75, 0.75), ("Wide", 1, 1, 2, 2), ("Mirrored", 1, 1, 1.25, 1), ("Office 2003 Default", 1, 1, 1.25, 1.25)
            };
            var m = new ContextMenu();
            foreach (var p in presets)
            {
                var pr = p;
                bool on = Math.Abs(_page.MarginTop - p.T) < .01 && Math.Abs(_page.MarginBottom - p.B) < .01 && Math.Abs(_page.MarginLeft - p.L) < .01 && Math.Abs(_page.MarginRight - p.R) < .01;
                var mi = RB.MI($"{p.Name}\tTop: {p.T}\"  Bottom: {p.B}\"  Left: {p.L}\"  Right: {p.R}\"", () =>
                {
                    _page.MarginTop = pr.T; _page.MarginBottom = pr.B; _page.MarginLeft = pr.L; _page.MarginRight = pr.R;
                    PageChanged();
                }, "IconMargins", null, on);
                m.Items.Add(mi);
            }
            m.Items.Add(new Separator());
            m.Items.Add(RB.MI("Custom Margins...", () => PageSetup_Click(null, null)));
            return m;
        }

        void SetOrientation(bool landscape)
        {
            if (_page.Landscape == landscape) return;
            _page.Landscape = landscape;
            PageChanged();
        }

        ContextMenu SizeMenu()
        {
            var m = new ContextMenu();
            foreach (var p in PageSettings.Papers)
            {
                var name = p.Name;
                m.Items.Add(RB.MI($"{p.Name}\t{p.W.ToString("0.##", CultureInfo.InvariantCulture)}\" x {p.H.ToString("0.##", CultureInfo.InvariantCulture)}\"", () => { _page.Paper = name; PageChanged(); }, "IconSize", null, _page.Paper == name));
            }
            m.Items.Add(new Separator());
            m.Items.Add(RB.MI("More Paper Sizes...", () => PageSetup_Click(null, null)));
            return m;
        }

        ContextMenu ColumnsMenu() => RB.Menu(
            RB.MI("One", () => SetColumns(1), "IconPrintLayout", null, _page.Columns <= 1),
            RB.MI("Two", () => SetColumns(2), "IconColumns", null, _page.Columns == 2),
            RB.MI("Three", () => SetColumns(3), "IconColumns", null, _page.Columns == 3),
            null,
            RB.MI("More Columns...", () =>
            {
                var d = new DialogBase(this, "Columns", 300);
                var n = D.Box(_page.Columns.ToString(), 50); var sp = D.Box(D.In(_page.ColumnSpacingIn), 60);
                d.Content = UI.DialogLayout(D.Form(("_Number of columns:", n), ("_Spacing:", sp)), UI.Strip(UI.Btn("OK", (s, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
                if (d.ShowDialog() != true) return;
                if (int.TryParse(n.Text, out var c) && c >= 1 && c <= 6) { _page.ColumnSpacingIn = Math.Max(0, D.ParseNum(sp.Text) ?? 0.5); SetColumns(c); }
            }));

        void SetColumns(int n)
        {
            _page.Columns = n;
            PageChanged();
            if (n > 1) Flash($"{n} columns. Text flows into columns on the printed page and in Print Preview; the guides show where the columns fall.");
        }

        ContextMenu BreaksMenu() => RB.Menu(
            new MenuItem { Header = "Page Breaks", IsEnabled = false, FontWeight = FontWeights.Bold },
            RB.MI("Page\tMark the point at which one page ends and the next page begins.", () => InsertPageBreak_Click(null, null), "IconPageBreak"),
            RB.MI("Column\tIndicate that the text following the column break will begin in the next column.", InsertColumnBreak, "IconColumnBreak"),
            RB.MI("Text Wrapping\tSeparate text around objects on web pages.", () => { Editor.CaretPosition.InsertLineBreak(); MarkDirty(); }, "IconBreaks"),
            new MenuItem { Header = "Section Breaks", IsEnabled = false, FontWeight = FontWeights.Bold },
            RB.MI("Next Page\tInsert a section break and start the new section on the next page.", () => InsertPageBreak_Click(null, null), "IconBreaks"),
            RB.MI("Continuous\tInsert a section break and start the new section on the same page.", () => { var np = Editor.CaretPosition.InsertParagraphBreak(); Editor.CaretPosition = np; MarkDirty(); }, "IconBreaks"));

        void InsertColumnBreak()
        {
            Editor.BeginChange();
            var np = Editor.CaretPosition.InsertParagraphBreak();
            if (np.Paragraph != null) np.Paragraph.BreakColumnBefore = true;
            Editor.CaretPosition = np;
            Editor.EndChange();
            MarkDirty(); ScheduleLayout();
        }

        void SetLineNumbers(bool on) { _page.LineNumbers = on; PageChanged(); }

        void SetHyphenation(bool on)
        {
            _page.Hyphenation = on;
            Editor.Document.IsHyphenationEnabled = on;
            PageChanged();
        }

        // ---------------- page background ----------------
        void ShowWatermarkGallery()
        {
            var presets = new[] { "CONFIDENTIAL", "DO NOT COPY", "DRAFT", "SAMPLE", "ASAP", "URGENT" };
            var tiles = presets.Select(t =>
            {
                var text = t;
                var g = new Grid { Width = 70, Height = 90, Background = Brushes.White };
                g.Children.Add(new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(-45), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                return (UIElement)RB.Tile(new Border { Child = g, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) }, text, () => SetWatermark(text, null), 80, 100);
            }).ToList();
            RB.ShowGallery(Ribbon, "Watermarks", tiles, 360, new UIElement[]
            {
                RB.MenuButton("Custom Watermark...", CustomWatermark, "IconWatermark"),
                RB.MenuButton("Remove Watermark", () => SetWatermark("", null), "IconDelete")
            });
        }

        void CustomWatermark()
        {
            var d = new DialogBase(this, "Printed Watermark", 340);
            var text = D.Box(string.IsNullOrEmpty(_page.Watermark) ? "ASAP" : _page.Watermark, 200);
            Color col = (Color)ColorConverter.ConvertFromString("#" + (_page.WatermarkColor ?? "C0C0C0"));
            var cb = UI.Btn("Color...", null); cb.Click += (s, e) => { var c = ColorDialog.Pick(d, col); if (c.HasValue) col = c.Value; };
            d.Content = UI.DialogLayout(D.Form(("_Text:", text), ("Color:", cb)), UI.Strip(UI.Btn("OK", (s, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
            if (d.ShowDialog() == true) SetWatermark(text.Text.Trim(), Fmt.Hex(col));
        }

        void SetWatermark(string text, string color)
        {
            _page.Watermark = text ?? "";
            if (color != null) _page.WatermarkColor = color;
            PageChanged();
        }

        void SetPageColor(Color? c)
        {
            _page.PageColor = c.HasValue ? Fmt.Hex(c.Value) : "";
            PageChanged();
        }

        void PageBordersDialog()
        {
            var d = new DialogBase(this, "Page Borders", 320);
            var none = new RadioButton { Content = "_None", IsChecked = !_page.PageBorder, Margin = new Thickness(0, 2, 0, 2) };
            var box = new RadioButton { Content = "_Box", IsChecked = _page.PageBorder, Margin = new Thickness(0, 2, 0, 2) };
            var width = new ComboBox { Width = 100, ItemsSource = new[] { "½ pt", "1 pt", "1½ pt", "2¼ pt", "3 pt", "4½ pt", "6 pt" } };
            var widths = new[] { 0.5, 1, 1.5, 2.25, 3, 4.5, 6 };
            width.SelectedIndex = Math.Max(0, Array.FindIndex(widths, w => Math.Abs(w - _page.PageBorderWidth) < 0.01));
            Color col = (Color)ColorConverter.ConvertFromString("#" + (_page.PageBorderColor ?? "000000"));
            var cb = UI.Btn("Color...", null); cb.Click += (s, e) => { var c = ColorDialog.Pick(d, col); if (c.HasValue) col = c.Value; };
            var sp = new StackPanel();
            sp.Children.Add(D.Group("Setting", new StackPanel { Children = { none, box } }));
            sp.Children.Add(D.Form(("_Width:", width), ("Color:", cb)));
            d.Content = UI.DialogLayout(sp, UI.Strip(UI.Btn("OK", (s, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
            if (d.ShowDialog() != true) return;
            _page.PageBorder = box.IsChecked == true;
            _page.PageBorderWidth = widths[Math.Max(0, width.SelectedIndex)];
            _page.PageBorderColor = Fmt.Hex(col);
            PageChanged();
        }

        /// <summary>Applies page colour, border, watermark, column guides, gridlines and line numbers to the on-screen page.</summary>
        void DrawPageDecorations()
        {
            Underlay.Children.Clear();
            Color pc = Colors.White;
            if (!string.IsNullOrEmpty(_page.PageColor)) try { pc = (Color)ColorConverter.ConvertFromString("#" + _page.PageColor); } catch { }
            Page.Background = new SolidColorBrush(pc);
            double w = _page.WidthPx, h = Math.Max(PageGrid.ActualHeight, _page.HeightPx);
            double ml = Editor.Margin.Left, mt = Editor.Margin.Top, bw = _page.BodyWidthPx;
            if (_view != "print") return;

            // page starts at 0 and at each boundary (in editor coordinates)
            var starts = new List<double> { -mt };
            foreach (var b in _boundaries) starts.Add(b.Y);

            if (_page.PageBorder)
            {
                var brush = new SolidColorBrush(Hex(_page.PageBorderColor, Colors.Black));
                Underlay.Children.Add(new System.Windows.Shapes.Rectangle { Width = w - 64, Height = h - 64, Stroke = brush, StrokeThickness = Math.Max(0.5, _page.PageBorderWidth) }.At(32, 32));
            }
            if (!string.IsNullOrWhiteSpace(_page.Watermark))
            {
                var c = Hex(_page.WatermarkColor, Colors.Silver);
                foreach (var y0 in starts)
                {
                    double top = y0 == -mt ? 0 : mt + y0 - mt;
                    var tb = new TextBlock { Text = _page.Watermark, FontFamily = new FontFamily("Calibri"), FontWeight = FontWeights.Bold, FontSize = 110, Foreground = new SolidColorBrush(Color.FromArgb(0x60, c.R, c.G, c.B)) };
                    tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    double maxW = Math.Sqrt(w * w + _page.HeightPx * _page.HeightPx) * 0.72;
                    if (tb.DesiredSize.Width > maxW) { tb.FontSize = 110 * maxW / tb.DesiredSize.Width; tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); }
                    tb.RenderTransformOrigin = new Point(0.5, 0.5);
                    tb.RenderTransform = new RotateTransform(-45);
                    Canvas.SetLeft(tb, (w - tb.DesiredSize.Width) / 2);
                    Canvas.SetTop(tb, (y0 == -mt ? 0 : mt + y0) + (Math.Min(_page.HeightPx, _page.BodyHeightPx + mt) - tb.DesiredSize.Height) / 2);
                    Underlay.Children.Add(tb);
                }
            }
            if (_page.Columns > 1)
            {
                double gap = _page.ColumnSpacingIn * 96, cw = (bw - gap * (_page.Columns - 1)) / _page.Columns;
                for (int i = 1; i < _page.Columns; i++)
                {
                    double x = ml + i * cw + (i - 0.5) * gap;
                    Underlay.Children.Add(new System.Windows.Shapes.Line { X1 = x, X2 = x, Y1 = mt, Y2 = h - Editor.Margin.Bottom, Stroke = new SolidColorBrush(Color.FromArgb(0x60, 0x7A, 0x9C, 0xC8)), StrokeDashArray = new DoubleCollection { 4, 4 } });
                }
            }
            if (_gridlines)
            {
                var gb = new SolidColorBrush(Color.FromArgb(0x50, 0x7A, 0x9C, 0xC8));
                for (double y = mt; y < h - Editor.Margin.Bottom; y += 18) Underlay.Children.Add(new System.Windows.Shapes.Line { X1 = ml, X2 = ml + bw, Y1 = y, Y2 = y, Stroke = gb, StrokeThickness = 0.5 });
                for (double x = ml; x <= ml + bw; x += 24) Underlay.Children.Add(new System.Windows.Shapes.Line { X1 = x, X2 = x, Y1 = mt, Y2 = h - Editor.Margin.Bottom, Stroke = gb, StrokeThickness = 0.5 });
            }
            if (_page.LineNumbers) DrawLineNumbers();
            DrawHeaderFooterPreview();
        }

        static Color Hex(string h, Color fallback)
        {
            try { return string.IsNullOrEmpty(h) ? fallback : (Color)ColorConverter.ConvertFromString("#" + h.TrimStart('#')); } catch { return fallback; }
        }

        void DrawLineNumbers()
        {
            var doc = Editor.Document;
            var pos = doc.ContentStart.GetLineStartPosition(0);
            int n = 1, guard = 0;
            double lastY = double.MinValue;
            while (pos != null && guard++ < 20000)
            {
                var r = pos.GetCharacterRect(LogicalDirection.Forward);
                if (!r.IsEmpty && r.Top > lastY + 1)
                {
                    lastY = r.Top;
                    var cell = Fmt.AncestorAt<TableCell>(pos);
                    if (cell == null)
                    {
                        var tb = new TextBlock { Text = n.ToString(), FontSize = 10, Foreground = Brushes.Gray, Width = 28, TextAlignment = TextAlignment.Right };
                        Canvas.SetLeft(tb, Editor.Margin.Left - 36);
                        Canvas.SetTop(tb, Editor.Margin.Top + r.Top + Math.Max(0, r.Height - 13) / 2);
                        Underlay.Children.Add(tb);
                        n++;
                    }
                }
                var next = pos.GetLineStartPosition(1, out int moved);
                if (moved == 0) break;
                pos = next;
            }
        }

        void DrawHeaderFooterPreview()
        {
            if (!_page.HasHeader && !_page.HasFooter) return;
            int pages = _pageCount;
            double ml = _page.MarginLeft * 96, mr = _page.MarginRight * 96, w = _page.WidthPx;
            var brush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
            void Slot(string text, int align, double y, int pageNo)
            {
                var s = DecoratedPaginator.Expand(text, pageNo, pages, _docTitle ?? DisplayName, _page);
                if (string.IsNullOrWhiteSpace(s)) return;
                var tb = new TextBlock { Text = s, FontFamily = new FontFamily("Calibri"), FontSize = 14.67, Foreground = brush };
                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double x = align == 0 ? ml : align == 1 ? (w - tb.DesiredSize.Width) / 2 : w - mr - tb.DesiredSize.Width;
                Canvas.SetLeft(tb, x); Canvas.SetTop(tb, y);
                Underlay.Children.Add(tb);
            }
            double hy = Math.Max(12, 48 - 10);
            Slot(_page.HeaderLeft, 0, hy, 1); Slot(_page.HeaderText, 1, hy, 1); Slot(_page.HeaderRight, 2, hy, 1);
            double fy = Math.Max(PageGrid.ActualHeight, _page.HeightPx) - 48 - 10;
            Slot(_page.FooterLeft, 0, fy, pages); Slot(_page.FooterCenter, 1, fy, pages); Slot(_page.FooterRight, 2, fy, pages);
            // page-number preview at each automatic page boundary
            int pn = 1;
            foreach (var b in _boundaries)
            {
                pn++;
                foreach (var (text, align) in new[] { (_page.FooterLeft, 0), (_page.FooterCenter, 1), (_page.FooterRight, 2) })
                {
                    if (string.IsNullOrEmpty(text) || !text.Contains("{PAGE}")) continue;
                    var s = DecoratedPaginator.Expand(text, pn - 1, pages, _docTitle ?? DisplayName, _page);
                    var tb = new TextBlock { Text = s, FontSize = 9, Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)) };
                    tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Canvas.SetLeft(tb, align == 0 ? ml : align == 1 ? (w - tb.DesiredSize.Width) / 2 : w - mr - tb.DesiredSize.Width);
                    Canvas.SetTop(tb, Editor.Margin.Top + b.Y - 14);
                    Underlay.Children.Add(tb);
                }
            }
        }

        // ---------------- picture position / rotate / align ----------------
        ContextMenu PositionMenu() => RB.Menu(
            RB.MI("In Line with Text", () => WrapPicture(null), "IconWrap"),
            RB.MI("Square — Left (text wraps on the right)", () => WrapPicture(HorizontalAlignment.Left), "IconWrap"),
            RB.MI("Square — Right (text wraps on the left)", () => WrapPicture(HorizontalAlignment.Right), "IconWrap"),
            RB.MI("Top and Bottom (centred on its own line)", () => { WrapPicture(null); AlignPicture(TextAlignment.Center); }, "IconWrap"));

        void WrapPicture(HorizontalAlignment? side)
        {
            var img = _selectedImage;
            if (img == null) { Flash("Click a picture first."); return; }
            RemoveImageAdorner();
            Editor.BeginChange();
            var parent = img.Parent;
            if (parent is InlineUIContainer iuc) iuc.Child = null;
            else if (parent is BlockUIContainer buc)
            {
                buc.Child = null;
                var fl = Fmt.Ancestor<Floater>(buc);
                if (fl != null) fl.SiblingInlines.Remove(fl);
            }
            TextPointer pos = parent is TextElement te ? te.ElementStart : Editor.CaretPosition;
            if (parent is InlineUIContainer oldC) oldC.SiblingInlines.Remove(oldC);
            pos = pos.GetInsertionPosition(LogicalDirection.Forward);
            if (side == null)
            {
                var c = new InlineUIContainer(img, pos);
                Editor.CaretPosition = c.ElementEnd;
            }
            else
            {
                double w = double.IsNaN(img.Width) ? img.ActualWidth : img.Width;
                var f = new Floater(new BlockUIContainer(img), pos) { HorizontalAlignment = side.Value, Width = w + 8, Padding = new Thickness(side == HorizontalAlignment.Left ? 0 : 8, 0, side == HorizontalAlignment.Left ? 8 : 0, 4), Tag = "Wrap" };
            }
            Editor.EndChange();
            MarkDirty(); ScheduleLayout();
        }

        void AlignPicture(TextAlignment a)
        {
            var p = _selectedImage != null ? Fmt.Ancestor<Paragraph>(_selectedImage.Parent as TextElement) : Editor.CaretPosition.Paragraph;
            if (p == null) return;
            p.TextAlignment = a;
            MarkDirty();
        }

        ContextMenu RotateMenu() => RB.Menu(
            RB.MI("Rotate Right 90°", () => TransformPicture(90, false, false), "IconRotate"),
            RB.MI("Rotate Left 90°", () => TransformPicture(-90, false, false), "IconRotate"),
            RB.MI("Flip Vertical", () => TransformPicture(0, false, true)),
            RB.MI("Flip Horizontal", () => TransformPicture(0, true, false)));

        void TransformPicture(double angle, bool flipH, bool flipV)
        {
            var img = _selectedImage;
            if (img?.Source is not BitmapSource src) { Flash("Click a picture first."); return; }
            BitmapSource result;
            if (angle != 0) result = new TransformedBitmap(src, new RotateTransform(angle));
            else result = new TransformedBitmap(src, new ScaleTransform(flipH ? -1 : 1, flipV ? -1 : 1));
            var frozen = BitmapFrame.Create(result); frozen.Freeze();
            double w = double.IsNaN(img.Width) ? img.ActualWidth : img.Width, h = double.IsNaN(img.Height) ? img.ActualHeight : img.Height;
            KeepOriginal(img);
            img.Source = frozen;
            if (angle != 0) { img.Width = h; img.Height = w; }
            ((ImageData)img.Tag).Bytes = null;
            MarkDirty(); ScheduleLayout();
        }

        static void KeepOriginal(Image img)
        {
            if (img.Tag is not ImageData d) { d = new ImageData(); img.Tag = d; }
            d.Original ??= img.Source as BitmapSource;
            if (d.OriginalWidth == 0) { d.OriginalWidth = double.IsNaN(img.Width) ? img.ActualWidth : img.Width; d.OriginalHeight = double.IsNaN(img.Height) ? img.ActualHeight : img.Height; }
        }
    }

    static class CanvasExt
    {
        public static T At<T>(this T e, double x, double y) where T : UIElement { Canvas.SetLeft(e, x); Canvas.SetTop(e, y); return e; }
    }
}

namespace ValcoWord
{
    public partial class MainWindow
    {
        /// <summary>Show/Hide ¶: paragraph marks, tabs and page-break markers.</summary>
        void DrawFormattingMarks()
        {
            var brush = new SolidColorBrush(Color.FromRgb(0x5B, 0x7F, 0xB0));
            foreach (var p in Fmt.AllParagraphs(Editor.Document.Blocks))
            {
                var r = p.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
                if (r.IsEmpty) continue;
                var tb = new TextBlock { Text = "¶", FontSize = Math.Max(9, Math.Min(p.FontSize, 24)), Foreground = brush };
                Canvas.SetLeft(tb, Editor.Margin.Left + r.Right + 1);
                Canvas.SetTop(tb, Editor.Margin.Top + r.Bottom - tb.FontSize * 1.25);
                Overlay.Children.Add(tb);
            }
        }
    }
}
