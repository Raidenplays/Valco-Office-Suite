using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ClosedXML.Excel;
using ClosedXML.Excel.Drawings;
using ValcoSpreadsheets.Model;

namespace ValcoSpreadsheets.Grid
{
    /// <summary>Per-sheet view state (scroll position, selection) kept while switching sheets.</summary>
    public class ViewState
    {
        public int TopRow = 1, LeftCol = 1;
        public int ActiveRow = 1, ActiveCol = 1;
        public int AnchorRow = 1, AnchorCol = 1;
        public List<CellRect> Selection = new() { new CellRect(1, 1, 1, 1) };
    }

    public enum EditMode { Ready, Enter, Edit, Point }

    /// <summary>
    /// The worksheet grid: Excel 2007 look (blue headers, gold selected headers, black selection border with fill handle),
    /// in-cell editor, scrolling, frozen panes, filter buttons. Everything is drawn in OnRender for speed.
    /// </summary>
    public class SheetView : System.Windows.Controls.Grid
    {
        public readonly SheetCanvas Canvas;
        public readonly TextBox Editor;
        public readonly ScrollBar VScroll, HScroll;
        readonly System.Windows.Controls.Canvas _overlay = new System.Windows.Controls.Canvas { ClipToBounds = true };

        public SheetView()
        {
            ColumnDefinitions.Add(new ColumnDefinition());
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            // the editor and scroll bars exist before the canvas, which hooks up to them
            Editor = new TextBox
            {
                BorderThickness = new Thickness(0), Padding = new Thickness(1, 0, 1, 0), AcceptsReturn = false, AcceptsTab = false,
                Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.NoWrap, Background = Brushes.White,
                VerticalContentAlignment = VerticalAlignment.Bottom, FocusVisualStyle = null
            };
            VScroll = new ScrollBar { Orientation = Orientation.Vertical, Minimum = 1, SmallChange = 1, LargeChange = 20 };
            HScroll = new ScrollBar { Orientation = Orientation.Horizontal, Minimum = 1, SmallChange = 1, LargeChange = 8, Height = 17 };
            Canvas = new SheetCanvas(this);
            Children.Add(Canvas);
            Children.Add(_overlay);
            SetColumn(VScroll, 1);
            Children.Add(VScroll);
            _overlay.Children.Add(Editor);
            VScroll.Scroll += (s, e) => Canvas.ScrollTo((int)Math.Round(VScroll.Value), null);
            HScroll.Scroll += (s, e) => Canvas.ScrollTo(null, (int)Math.Round(HScroll.Value));
            Focusable = false;
            Background = Brushes.White;
        }

        public void PlaceEditor(Rect r, double fontPx)
        {
            System.Windows.Controls.Canvas.SetLeft(Editor, r.X);
            System.Windows.Controls.Canvas.SetTop(Editor, r.Y);
            Editor.MinWidth = r.Width;
            Editor.Width = double.NaN;
            Editor.MaxWidth = Math.Max(r.Width, ActualWidth - r.X - 20);
            Editor.Height = double.NaN;
            Editor.MinHeight = r.Height;
        }

        public System.Windows.Controls.Canvas Overlay => _overlay;
    }

    public class SheetCanvas : FrameworkElement
    {
        readonly SheetView _host;
        public Book Book;
        public int SheetIndex = 1;
        public IXLWorksheet Sheet => Book.Wb.Worksheet(SheetIndex);
        public SheetLayout Layout { get; } = new SheetLayout();
        public ViewState View = new ViewState();
        readonly Dictionary<string, ViewState> _views = new();
        public double Zoom = 1;
        public EditMode Mode = EditMode.Ready;
        public bool ShowFormulas, ShowHeadings = true;
        public bool? ShowGridOverride;

        // events for the window
        public event Action SelectionChanged;
        public event Action<string> EditCommitted;   // text typed into the active cell
        public event Action ModeChanged;
        public event Action ViewScrolled;
        public event Action<Point> ContextMenuRequested;
        public event Action<int, int, Point> FilterButtonClicked;
        public event Action<CellRect, CellRect, bool> FillDragged;
        public event Action<bool, List<int>, double> SizeDragged;     // columns?, indexes, new px (unzoomed)
        public event Action<bool, int> AutoFitRequested;             // columns?, index
        public event Action<int, int> HyperlinkClicked;
        public event Action<CellRect, int, int, bool> MoveDragged;    // source, dest row, dest col, copy

        public SheetCanvas(SheetView host)
        {
            _host = host;
            ClipToBounds = true;
            Focusable = true;
            FocusVisualStyle = null;
            _ants = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _ants.Tick += (s, e) => { _antsOffset = (_antsOffset + 1) % 8; if (CopyMarquee.HasValue) InvalidateVisual(); else _ants.Stop(); };
            _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            _scrollTimer.Tick += (s, e) => AutoScrollTick();
            host.Editor.PreviewKeyDown += Editor_PreviewKeyDown;
            host.Editor.TextChanged += (s, e) => { if (Mode != EditMode.Ready) EditorTextChanged?.Invoke(_host.Editor.Text); };
        }

        public event Action<string> EditorTextChanged;

        // ===================================================================== sheet switching
        public void Attach(Book book, int sheetIndex)
        {
            Book = book;
            SheetIndex = Math.Clamp(sheetIndex, 1, book.Wb.Worksheets.Count);
            var name = Sheet.Name;
            if (!_views.TryGetValue(name, out View)) { View = new ViewState(); _views[name] = View; ReadSheetView(); }
            Zoom = Math.Clamp((Sheet.SheetView.ZoomScale > 0 ? Sheet.SheetView.ZoomScale : 100) / 100.0, 0.1, 4);
            Relayout();
            SelectionChanged?.Invoke();
        }

        void ReadSheetView()
        {
            try
            {
                var tl = Sheet.SheetView.TopLeftCellAddress;
                if (tl != null && tl.RowNumber > 0) { View.TopRow = tl.RowNumber; View.LeftCol = tl.ColumnNumber; }
                var sel = Sheet.SelectedRanges?.FirstOrDefault();
                var ac = Sheet.ActiveCell;
                if (ac != null) { View.ActiveRow = View.AnchorRow = ac.Address.RowNumber; View.ActiveCol = View.AnchorCol = ac.Address.ColumnNumber; View.Selection = new List<CellRect> { new CellRect(View.ActiveRow, View.ActiveCol, View.ActiveRow, View.ActiveCol) }; }
            }
            catch { }
        }

        /// <summary>Saves the scroll position/selection into the sheet so the file reopens where it was.</summary>
        public void WriteSheetView()
        {
            try
            {
                Sheet.SheetView.TopLeftCellAddress = Sheet.Cell(Math.Max(View.TopRow, 1), Math.Max(View.LeftCol, 1)).Address;
                Sheet.SheetView.ZoomScale = (int)Math.Round(Zoom * 100);
                foreach (var ws in Book.Wb.Worksheets) ws.SetTabSelected(ws.Position == SheetIndex);
                Sheet.SetTabActive();
                Sheet.Cell(View.ActiveRow, View.ActiveCol).SetActive();
                Sheet.SelectedRanges.RemoveAll();
                foreach (var r in View.Selection) if (r.Count < 5_000_000) Sheet.SelectedRanges.Add(Sheet.Range(r.R1, r.C1, r.R2, r.C2));
            }
            catch { }
        }

        public void ForgetViews() => _views.Clear();
        public void RenameView(string oldName, string newName) { if (_views.Remove(oldName, out var v)) _views[newName] = v; }

        public int FrozenRows => Math.Max(0, Sheet.SheetView.SplitRow);
        public int FrozenCols => Math.Max(0, Sheet.SheetView.SplitColumn);
        bool Gridlines => ShowGridOverride ?? Sheet.ShowGridLines;

        /// <summary>Re-reads widths/heights/merges after a change and redraws.</summary>
        public void Relayout()
        {
            Layout.Rebuild(Sheet);
            if (View.TopRow <= FrozenRows) View.TopRow = FrozenRows + 1;
            if (View.LeftCol <= FrozenCols) View.LeftCol = FrozenCols + 1;
            UpdateScrollBars();
            InvalidateVisual();
        }

        public void Redraw() { InvalidateVisual(); UpdateScrollBars(); }

        // ===================================================================== geometry
        public double HeaderH => ShowHeadings ? Math.Round(Math.Clamp(20 * Zoom, 14, 36)) : 0;
        public double HeaderW
        {
            get
            {
                if (!ShowHeadings) return 0;
                int maxRow = Math.Max(View.TopRow + 60, 1000);
                int digits = Math.Max(3, maxRow.ToString(CultureInfo.InvariantCulture).Length);
                return Math.Round(Math.Max(26, (digits * 7 + 10) * Math.Clamp(Zoom, 0.6, 2)));
            }
        }

        public struct Lane { public int Index; public double Pos, Size; }
        public List<Lane> RowLanes = new(), ColLanes = new();

        void BuildLanes()
        {
            RowLanes = Lanes(Layout.Rows, FrozenRows, View.TopRow, HeaderH, ActualHeight, SheetLimits.Rows);
            ColLanes = Lanes(Layout.Cols, FrozenCols, View.LeftCol, HeaderW, ActualWidth, SheetLimits.Cols);
        }

        List<Lane> Lanes(Axis axis, int frozen, int start, double origin, double limit, int max)
        {
            var list = new List<Lane>();
            double p = origin;
            for (int i = 1; i <= frozen; i++)
            {
                double s = axis.Size(i) * Zoom;
                list.Add(new Lane { Index = i, Pos = p, Size = s });
                p += s;
            }
            for (int i = Math.Max(start, frozen + 1); i <= max && p < limit; i++)
            {
                double s = axis.Size(i) * Zoom;
                list.Add(new Lane { Index = i, Pos = p, Size = s });
                p += s;
            }
            return list;
        }

        /// <summary>Screen x of the start of column c (may be off-screen).</summary>
        public double ColX(int c)
        {
            if (c <= FrozenCols) return HeaderW + Layout.Cols.Pos(c) * Zoom;
            double frozenW = Layout.Cols.Pos(FrozenCols + 1) * Zoom;
            return HeaderW + frozenW + (Layout.Cols.Pos(c) - Layout.Cols.Pos(View.LeftCol)) * Zoom;
        }

        public double RowY(int r)
        {
            if (r <= FrozenRows) return HeaderH + Layout.Rows.Pos(r) * Zoom;
            double frozenH = Layout.Rows.Pos(FrozenRows + 1) * Zoom;
            return HeaderH + frozenH + (Layout.Rows.Pos(r) - Layout.Rows.Pos(View.TopRow)) * Zoom;
        }

        public Rect CellRectPx(CellRect r)
        {
            double x1 = ColX(r.C1), x2 = ColX(r.C2) + Layout.Cols.Size(r.C2) * Zoom;
            double y1 = RowY(r.R1), y2 = RowY(r.R2) + Layout.Rows.Size(r.R2) * Zoom;
            return new Rect(x1, y1, Math.Max(0, x2 - x1), Math.Max(0, y2 - y1));
        }

        double FrozenRight => HeaderW + Layout.Cols.Pos(FrozenCols + 1) * Zoom;
        double FrozenBottom => HeaderH + Layout.Rows.Pos(FrozenRows + 1) * Zoom;

        /// <summary>Hit-tests a point to a cell (row/col 0 when over a header).</summary>
        public (int Row, int Col) CellAt(Point p, bool clamp = true)
        {
            int row = 0, col = 0;
            if (p.Y >= HeaderH)
            {
                foreach (var l in RowLanes) if (p.Y >= l.Pos && p.Y < l.Pos + l.Size) { row = l.Index; break; }
                if (row == 0 && clamp) row = p.Y < HeaderH + 1 ? RowLanes.FirstOrDefault().Index : RowLanes.Count > 0 ? RowLanes[^1].Index : 1;
            }
            if (p.X >= HeaderW)
            {
                foreach (var l in ColLanes) if (p.X >= l.Pos && p.X < l.Pos + l.Size) { col = l.Index; break; }
                if (col == 0 && clamp) col = ColLanes.Count > 0 ? ColLanes[^1].Index : 1;
            }
            return (Math.Max(row, 0), Math.Max(col, 0));
        }

        public int VisibleRowCount => Math.Max(1, RowLanes.Count(l => l.Index > FrozenRows && l.Pos + l.Size <= ActualHeight));
        public int VisibleColCount => Math.Max(1, ColLanes.Count(l => l.Index > FrozenCols && l.Pos + l.Size <= ActualWidth));

        // ===================================================================== scrolling
        public void ScrollTo(int? top, int? left)
        {
            if (top.HasValue) View.TopRow = Layout.Rows.NextVisible(Math.Clamp(top.Value, FrozenRows + 1, SheetLimits.Rows), 1, SheetLimits.Rows);
            if (left.HasValue) View.LeftCol = Layout.Cols.NextVisible(Math.Clamp(left.Value, FrozenCols + 1, SheetLimits.Cols), 1, SheetLimits.Cols);
            UpdateScrollBars();
            InvalidateVisual();
            ViewScrolled?.Invoke();
            if (Mode != EditMode.Ready) PositionEditor();
        }

        public void UpdateScrollBars()
        {
            var v = _host.VScroll; var h = _host.HScroll;
            int vis = Math.Max(1, VisibleRowCount), visC = Math.Max(1, VisibleColCount);
            v.Minimum = FrozenRows + 1;
            v.Maximum = Math.Max(Math.Max(Layout.LastRow, View.ActiveRow) + vis / 2, View.TopRow + vis) ;
            v.ViewportSize = vis;
            v.LargeChange = vis;
            v.Value = View.TopRow;
            h.Minimum = FrozenCols + 1;
            h.Maximum = Math.Max(Math.Max(Layout.LastCol, View.ActiveCol) + visC / 2, View.LeftCol + visC);
            h.ViewportSize = visC;
            h.LargeChange = visC;
            h.Value = View.LeftCol;
        }

        /// <summary>Scrolls just enough to show a cell.</summary>
        public void EnsureVisible(int row, int col)
        {
            BuildLanes();
            bool changed = false;
            if (row > FrozenRows)
            {
                if (row < View.TopRow) { View.TopRow = row; changed = true; }
                else
                {
                    double avail = ActualHeight - FrozenBottom;
                    double h = (Layout.Rows.Pos(row + 1) - Layout.Rows.Pos(View.TopRow)) * Zoom;
                    if (h > avail && avail > 0)
                    {
                        int top = View.TopRow;
                        while (top < row && (Layout.Rows.Pos(row + 1) - Layout.Rows.Pos(top)) * Zoom > avail) top++;
                        View.TopRow = top; changed = true;
                    }
                }
            }
            if (col > FrozenCols)
            {
                if (col < View.LeftCol) { View.LeftCol = col; changed = true; }
                else
                {
                    double avail = ActualWidth - FrozenRight;
                    double w = (Layout.Cols.Pos(col + 1) - Layout.Cols.Pos(View.LeftCol)) * Zoom;
                    if (w > avail && avail > 0)
                    {
                        int left = View.LeftCol;
                        while (left < col && (Layout.Cols.Pos(col + 1) - Layout.Cols.Pos(left)) * Zoom > avail) left++;
                        View.LeftCol = left; changed = true;
                    }
                }
            }
            if (changed) { UpdateScrollBars(); ViewScrolled?.Invoke(); }
            InvalidateVisual();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            BuildLanes();
            UpdateScrollBars();
        }

        // ===================================================================== selection
        public CellRect ActiveRect => View.Selection[^1];
        public IXLCell ActiveCell => Sheet.Cell(View.ActiveRow, View.ActiveCol);

        public void Select(int row, int col, bool extend = false, bool add = false)
        {
            row = Math.Clamp(row, 1, SheetLimits.Rows); col = Math.Clamp(col, 1, SheetLimits.Cols);
            if (extend)
            {
                var r = Layout.ExpandToMerges(new CellRect(View.AnchorRow, View.AnchorCol, row, col));
                View.Selection[^1] = r;
                // the active cell stays at the anchor when extending
            }
            else
            {
                var m = Layout.MergeAt(row, col);
                var r = m ?? new CellRect(row, col, row, col);
                if (m.HasValue) { row = m.Value.R1; col = m.Value.C1; }
                if (add) View.Selection.Add(r); else View.Selection = new List<CellRect> { r };
                View.ActiveRow = View.AnchorRow = row;
                View.ActiveCol = View.AnchorCol = col;
            }
            SelectionChanged?.Invoke();
            InvalidateVisual();
        }

        public void SelectRect(CellRect r, int? activeRow = null, int? activeCol = null)
        {
            View.Selection = new List<CellRect> { r };
            View.ActiveRow = View.AnchorRow = activeRow ?? r.R1;
            View.ActiveCol = View.AnchorCol = activeCol ?? r.C1;
            SelectionChanged?.Invoke();
            InvalidateVisual();
        }

        public void SelectAll() => SelectRect(new CellRect(1, 1, SheetLimits.Rows, SheetLimits.Cols), View.ActiveRow, View.ActiveCol);

        /// <summary>Moves the active cell inside the selection (Enter/Tab with a range selected), or moves it.</summary>
        public void MoveActive(int dr, int dc, bool withinSelection)
        {
            var sel = ActiveRect;
            if (withinSelection && sel.Count > 1 && View.Selection.Count == 1)
            {
                int r = View.ActiveRow, c = View.ActiveCol;
                if (dc != 0)
                {
                    c += dc;
                    if (c > sel.C2) { c = sel.C1; r++; if (r > sel.R2) r = sel.R1; }
                    if (c < sel.C1) { c = sel.C2; r--; if (r < sel.R1) r = sel.R2; }
                }
                else
                {
                    r += dr;
                    if (r > sel.R2) { r = sel.R1; c++; if (c > sel.C2) c = sel.C1; }
                    if (r < sel.R1) { r = sel.R2; c--; if (c < sel.C1) c = sel.C2; }
                }
                View.ActiveRow = r; View.ActiveCol = c;
                EnsureVisible(r, c);
                SelectionChanged?.Invoke();
                InvalidateVisual();
                return;
            }
            int nr = View.ActiveRow, nc = View.ActiveCol;
            var m = Layout.MergeAt(nr, nc);
            if (m.HasValue) { if (dr > 0) nr = m.Value.R2; if (dc > 0) nc = m.Value.C2; }
            nr = Layout.Rows.NextVisible(Math.Clamp(nr + dr, 1, SheetLimits.Rows), dr >= 0 ? 1 : -1, SheetLimits.Rows);
            nc = Layout.Cols.NextVisible(Math.Clamp(nc + dc, 1, SheetLimits.Cols), dc >= 0 ? 1 : -1, SheetLimits.Cols);
            Select(nr, nc);
            EnsureVisible(nr, nc);
        }

        public IEnumerable<(int Row, int Col)> SelectedCellsUsed()
        {
            var seen = new HashSet<(int, int)>();
            foreach (var r in View.Selection)
                foreach (var c in Sheet.Range(r.R1, r.C1, r.R2, r.C2).CellsUsed(XLCellsUsedOptions.Contents))
                    if (seen.Add((c.Address.RowNumber, c.Address.ColumnNumber))) yield return (c.Address.RowNumber, c.Address.ColumnNumber);
        }

        // ===================================================================== copy marquee
        public CellRect? CopyMarquee;
        readonly DispatcherTimer _ants;
        int _antsOffset;
        public void SetMarquee(CellRect? r) { CopyMarquee = r; if (r.HasValue) _ants.Start(); else _ants.Stop(); InvalidateVisual(); }

        // ===================================================================== rendering
        static readonly Brush HeaderBg = Frozen(new LinearGradientBrush(Color.FromRgb(0xF6, 0xF9, 0xFC), Color.FromRgb(0xE4, 0xEC, 0xF7), 90));
        static readonly Brush HeaderSel = Frozen(new LinearGradientBrush(new GradientStopCollection { new GradientStop(Color.FromRgb(0xF8, 0xD8, 0x9F), 0), new GradientStop(Color.FromRgb(0xF1, 0xC1, 0x5F), 1) }, 90));
        static readonly Brush HeaderAll = Frozen(new LinearGradientBrush(new GradientStopCollection { new GradientStop(Color.FromRgb(0xFF, 0xD5, 0x8D), 0), new GradientStop(Color.FromRgb(0xF2, 0x94, 0x36), 1) }, 90));
        static readonly Pen HeaderLine = FrozenPen(Color.FromRgb(0x9E, 0xB6, 0xCE), 1);
        static readonly Pen GridPen = FrozenPen(Color.FromRgb(0xD0, 0xD7, 0xE5), 1);
        static readonly Brush SelFill = Frozen(new SolidColorBrush(Color.FromArgb(0x38, 0x31, 0x6A, 0xC5)));
        static readonly Pen SelPen = FrozenPen(Colors.Black, 2);
        static readonly Pen FreezePen = FrozenPen(Color.FromRgb(0x30, 0x30, 0x30), 1);
        static readonly Brush HeaderText = Frozen(new SolidColorBrush(Color.FromRgb(0x27, 0x41, 0x5F)));
        static readonly Typeface HeaderFace = new Typeface("Calibri");
        static readonly Dictionary<string, Typeface> Faces = new();
        static readonly Dictionary<Color, SolidColorBrush> BrushCache = new();

        static Brush Frozen(Brush b) { b.Freeze(); return b; }
        static Pen FrozenPen(Color c, double w) { var p = new Pen(new SolidColorBrush(c), w); p.Freeze(); return p; }
        static SolidColorBrush B(Color c) { if (!BrushCache.TryGetValue(c, out var b)) { b = new SolidColorBrush(c); b.Freeze(); BrushCache[c] = b; } return b; }

        static Typeface Face(string name, bool bold, bool italic)
        {
            var key = name + (bold ? "|b" : "") + (italic ? "|i" : "");
            if (!Faces.TryGetValue(key, out var f))
            {
                f = new Typeface(new FontFamily(string.IsNullOrEmpty(name) ? "Calibri" : name), italic ? FontStyles.Italic : FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
                Faces[key] = f;
            }
            return f;
        }

        double Dip => VisualTreeHelper.GetDpi(this).PixelsPerDip;

        protected override void OnRender(DrawingContext dc)
        {
            if (Book == null) return;
            BuildLanes();
            double W = ActualWidth, H = ActualHeight;
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, W, H));
            if (RowLanes.Count == 0 || ColLanes.Count == 0) return;
            var ws = Sheet;
            var wb = Book.Wb;
            double hx = HeaderW, hy = HeaderH;
            var cellArea = new Rect(hx, hy, Math.Max(0, W - hx), Math.Max(0, H - hy));
            int rFirst = RowLanes[0].Index, rLast = RowLanes[^1].Index;
            int cFirst = ColLanes[0].Index, cLast = ColLanes[^1].Index;
            var visRows = RowLanes.Select(l => l.Index).ToHashSet();
            var visCols = ColLanes.Select(l => l.Index).ToHashSet();

            dc.PushClip(new RectangleGeometry(cellArea));

            // ---- cells in view (plus a margin to the left for text that overflows into view)
            int scanC1 = Math.Max(1, (FrozenCols > 0 ? 1 : cFirst) - 12);
            var cells = new List<IXLCell>();
            var filled = new HashSet<(int, int)>();
            foreach (var band in RowBands())
                foreach (var colBand in ColBands(scanC1))
                    foreach (var c in ws.Range(band.Item1, colBand.Item1, band.Item2, colBand.Item2).CellsUsed(XLCellsUsedOptions.All))
                    {
                        cells.Add(c);
                        if (!c.IsEmpty(XLCellsUsedOptions.Contents)) filled.Add((c.Address.RowNumber, c.Address.ColumnNumber));
                    }

            // ---- empty cells' row/column fills
            if (Layout.ColStyles.Count > 0 || Layout.RowStyles.Count > 0)
            {
                foreach (var rl in RowLanes)
                    foreach (var cl in ColLanes)
                    {
                        IXLStyle st = null;
                        if (Layout.RowStyles.TryGetValue(rl.Index, out var rs)) st = rs;
                        else if (Layout.ColStyles.TryGetValue(cl.Index, out var cs)) st = cs;
                        if (st == null) continue;
                        var fill = FillOf(st, wb);
                        if (fill.HasValue) dc.DrawRectangle(B(fill.Value), null, new Rect(cl.Pos, rl.Pos, cl.Size, rl.Size));
                    }
            }

            // ---- gridlines
            if (Gridlines)
            {
                var gl = new StreamGeometry();
                using (var g = gl.Open())
                {
                    foreach (var l in RowLanes) { double y = Math.Floor(l.Pos + l.Size) - 0.5; g.BeginFigure(new Point(hx, y), false, false); g.LineTo(new Point(W, y), true, false); }
                    foreach (var l in ColLanes) { double x = Math.Floor(l.Pos + l.Size) - 0.5; g.BeginFigure(new Point(x, hy), false, false); g.LineTo(new Point(x, H), true, false); }
                }
                gl.Freeze();
                dc.DrawGeometry(null, GridPen, gl);
            }

            // ---- fills (cover gridlines) and merged backgrounds
            var cf = new CondFormatCache(ws, wb);
            foreach (var c in cells)
            {
                int r = c.Address.RowNumber, col = c.Address.ColumnNumber;
                if (!visRows.Contains(r) || !visCols.Contains(col)) continue;
                var m = Layout.MergeAt(r, col);
                if (m.HasValue && (m.Value.R1 != r || m.Value.C1 != col)) continue;
                var fill = cf.For(c).Fill ?? FillOf(c.Style, wb);
                if (!fill.HasValue) continue;
                var rect = m.HasValue ? CellRectPx(m.Value) : new Rect(ColX(col), RowY(r), Layout.Cols.Size(col) * Zoom, Layout.Rows.Size(r) * Zoom);
                dc.DrawRectangle(B(fill.Value), null, new Rect(rect.X - 0.5, rect.Y - 0.5, rect.Width, rect.Height));
            }
            foreach (var m in Layout.Merges)
            {
                if (m.R2 < rFirst && m.R1 > FrozenRows) continue;
                if (m.C2 < cFirst && m.C1 > FrozenCols) continue;
                if (m.R1 > rLast || m.C1 > cLast) continue;
                var tl = ws.Cell(m.R1, m.C1);
                var fill = cf.For(tl).Fill ?? FillOf(tl.Style, wb);
                var rect = CellRectPx(m);
                dc.DrawRectangle(fill.HasValue ? B(fill.Value) : Brushes.White, null, new Rect(rect.X, rect.Y, rect.Width - 1, rect.Height - 1));
            }

            // ---- text
            double dip = Dip;
            foreach (var c in cells)
            {
                int r = c.Address.RowNumber, col = c.Address.ColumnNumber;
                if (!visRows.Contains(r)) continue;
                if (c.IsEmpty(XLCellsUsedOptions.Contents)) continue;
                var m = Layout.MergeAt(r, col);
                if (m.HasValue && (m.Value.R1 != r || m.Value.C1 != col)) continue;
                if (!m.HasValue && !visCols.Contains(col) && col >= cFirst) continue;
                DrawCellText(dc, c, r, col, m, filled, cf, dip, cLast);
            }

            // ---- borders
            foreach (var c in cells)
            {
                int r = c.Address.RowNumber, col = c.Address.ColumnNumber;
                if (!visRows.Contains(r) || !visCols.Contains(col)) continue;
                DrawBorders(dc, c, r, col, wb);
            }

            // ---- comment markers / filter buttons
            foreach (var c in cells)
            {
                if (!c.HasComment) continue;
                int r = c.Address.RowNumber, col = c.Address.ColumnNumber;
                if (!visRows.Contains(r) || !visCols.Contains(col)) continue;
                double x = ColX(col) + Layout.Cols.Size(col) * Zoom - 1, y = RowY(r);
                var tri = new StreamGeometry();
                using (var g = tri.Open()) { g.BeginFigure(new Point(x - 6, y), true, true); g.LineTo(new Point(x, y), true, false); g.LineTo(new Point(x, y + 6), true, false); }
                dc.DrawGeometry(Brushes.Red, null, tri);
            }
            DrawFilterButtons(dc, ws, visRows, visCols);
            DrawObjects(dc);

            // ---- selection
            DrawSelection(dc);
            if (CopyMarquee.HasValue) DrawMarquee(dc, CopyMarquee.Value);
            if (_fillDrag.HasValue) { var fr = CellRectPx(_fillDrag.Value); dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70)), 1) { DashStyle = DashStyles.Dash }, new Rect(fr.X - 1, fr.Y - 1, fr.Width + 1, fr.Height + 1)); }
            if (_moveTarget.HasValue) { var fr = CellRectPx(_moveTarget.Value); dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)), 2) { DashStyle = DashStyles.Dot }, fr); }
            if (_refHighlight != null)
            {
                int i = 0;
                foreach (var rr in _refHighlight)
                {
                    var col = RefColors[i++ % RefColors.Length];
                    var rp = CellRectPx(rr);
                    dc.DrawRectangle(null, new Pen(B(col), 1.5), new Rect(rp.X - 1, rp.Y - 1, rp.Width + 1, rp.Height + 1));
                }
            }

            // ---- frozen pane lines
            if (FrozenRows > 0) dc.DrawLine(FreezePen, new Point(hx, Math.Floor(FrozenBottom) - 0.5), new Point(W, Math.Floor(FrozenBottom) - 0.5));
            if (FrozenCols > 0) dc.DrawLine(FreezePen, new Point(Math.Floor(FrozenRight) - 0.5, hy), new Point(Math.Floor(FrozenRight) - 0.5, H));
            dc.Pop();

            if (ShowHeadings) DrawHeaders(dc, W, H);
        }

        IEnumerable<(int, int)> RowBands()
        {
            if (FrozenRows > 0) yield return (1, FrozenRows);
            var scrolled = RowLanes.Where(l => l.Index > FrozenRows).ToList();
            if (scrolled.Count > 0) yield return (scrolled[0].Index, scrolled[^1].Index);
        }

        IEnumerable<(int, int)> ColBands(int scanFrom)
        {
            if (FrozenCols > 0) yield return (1, FrozenCols);
            var scrolled = ColLanes.Where(l => l.Index > FrozenCols).ToList();
            if (scrolled.Count > 0) yield return (Math.Max(FrozenCols + 1, Math.Min(scanFrom, scrolled[0].Index)), scrolled[^1].Index);
        }

        internal static Color? FillOf(IXLStyle st, XLWorkbook wb)
        {
            try
            {
                var f = st.Fill;
                if (f.PatternType == XLFillPatternValues.None) return null;
                if (f.PatternType == XLFillPatternValues.Solid) return XlColors.ToWpf(f.BackgroundColor, wb);
                // patterns: blend pattern and background so it reads as a tint
                var bg = XlColors.ToWpf(f.BackgroundColor, wb) ?? Colors.White;
                var pc = XlColors.ToWpf(f.PatternColor, wb) ?? Colors.Black;
                return Color.FromRgb((byte)((bg.R * 3 + pc.R) / 4), (byte)((bg.G * 3 + pc.G) / 4), (byte)((bg.B * 3 + pc.B) / 4));
            }
            catch { return null; }
        }

        static readonly Color[] RefColors = { Color.FromRgb(0x00, 0x00, 0xFF), Color.FromRgb(0x00, 0x80, 0x00), Color.FromRgb(0x99, 0x00, 0xCC), Color.FromRgb(0x80, 0x00, 0x00), Color.FromRgb(0x00, 0xCC, 0x33), Color.FromRgb(0xFF, 0x66, 0x00) };
        List<CellRect> _refHighlight;
        public void HighlightRefs(List<CellRect> refs) { _refHighlight = refs; InvalidateVisual(); }

        /// <summary>The text shown in a cell (formatted value, or the formula with Show Formulas).</summary>
        public string DisplayText(IXLCell c, out bool isNumber, out bool isError, out bool isBool)
        {
            isNumber = isError = isBool = false;
            if (ShowFormulas && c.HasFormula) return "=" + c.FormulaA1;
            XLCellValue v;
            try { v = c.Value; }
            catch { isError = true; return "#VALUE!"; }
            isNumber = v.IsNumber || v.IsDateTime || v.IsTimeSpan;
            isError = v.IsError;
            isBool = v.IsBoolean;
            if (v.IsBlank) return "";
            try { return c.GetFormattedString(); }
            catch { return v.ToString(CultureInfo.CurrentCulture); }
        }

        void DrawCellText(DrawingContext dc, IXLCell c, int r, int col, CellRect? merge, HashSet<(int, int)> filled, CondFormatCache cf, double dip, int cLast)
        {
            var st = c.Style;
            var text = DisplayText(c, out bool isNum, out bool isErr, out bool isBool);
            if (string.IsNullOrEmpty(text)) return;
            var font = st.Font;
            var fontColor = XlColors.ToWpf(font.FontColor, Book.Wb, true) ?? Colors.Black;
            var nf = st.NumberFormat.Format ?? "";
            if (isNum && nf.Contains("[Red]") && SafeNumber(c) < 0) fontColor = Colors.Red;
            var cfr = cf.For(c);
            if (cfr.Font.HasValue) fontColor = cfr.Font.Value;
            double sizePx = Units.PtToPx(font.FontSize > 0 ? font.FontSize : 11) * Zoom;
            var face = Face(font.FontName, font.Bold || cfr.Bold, font.Italic);
            var al = st.Alignment;
            bool wrap = al.WrapText;
            var h = al.Horizontal;
            if (h == XLAlignmentHorizontalValues.General) h = isNum ? XLAlignmentHorizontalValues.Right : (isErr || isBool) ? XLAlignmentHorizontalValues.Center : XLAlignmentHorizontalValues.Left;
            if (ShowFormulas && c.HasFormula) h = XLAlignmentHorizontalValues.Left;

            Rect cell = merge.HasValue ? CellRectPx(merge.Value) : new Rect(ColX(col), RowY(r), Layout.Cols.Size(col) * Zoom, Layout.Rows.Size(r) * Zoom);
            if (cell.Width <= 1 || cell.Height <= 1) return;
            double pad = 2.5 * Zoom + al.Indent * 9 * Zoom;

            if (cfr.DataBar.HasValue)
            {
                var (frac, barColor) = cfr.DataBar.Value;
                var br = new LinearGradientBrush(barColor, Color.FromArgb(0x30, barColor.R, barColor.G, barColor.B), 0);
                dc.DrawRectangle(br, null, new Rect(cell.X + 1, cell.Y + 2, Math.Max(0, (cell.Width - 3) * frac), cell.Height - 4));
            }

            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, sizePx, B(fontColor), dip);
            if (font.Underline != XLFontUnderlineValues.None || font.Strikethrough)
            {
                var deco = new TextDecorationCollection();
                if (font.Underline != XLFontUnderlineValues.None) deco.Add(TextDecorations.Underline);
                if (font.Strikethrough) deco.Add(TextDecorations.Strikethrough);
                ft.SetTextDecorations(deco);
            }
            ft.Trimming = TextTrimming.None;

            // numbers that don't fit become ### (General numbers lose decimals first)
            if (isNum && !wrap && ft.Width > cell.Width - pad - 1)
            {
                string shorter = null;
                if (string.IsNullOrEmpty(nf) || nf == "General")
                {
                    var n = SafeNumber(c);
                    for (int d = 10; d >= 1 && shorter == null; d--)
                    {
                        var s = n.ToString("G" + d, CultureInfo.CurrentCulture);
                        var t2 = new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, sizePx, B(fontColor), dip);
                        if (t2.Width <= cell.Width - pad - 1) { shorter = s; ft = t2; }
                    }
                }
                if (shorter == null)
                {
                    var hash = new FormattedText("#", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, sizePx, B(fontColor), dip);
                    int count = Math.Max(1, (int)((cell.Width - 2) / Math.Max(1, hash.Width)));
                    ft = new FormattedText(new string('#', count), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, sizePx, B(fontColor), dip);
                    h = XLAlignmentHorizontalValues.Left;
                    pad = 1;
                }
            }

            Rect clip = cell;
            if (wrap || h == XLAlignmentHorizontalValues.Justify || h == XLAlignmentHorizontalValues.Distributed)
            {
                ft.MaxTextWidth = Math.Max(1, cell.Width - pad * 2);
                ft.MaxTextHeight = Math.Max(1, cell.Height);
            }
            else if (!isNum && !merge.HasValue && ft.Width + pad * 2 > cell.Width)
            {
                // overflow into empty neighbours (left text goes right, right text goes left, centred both ways)
                double right = cell.Right, left = cell.Left;
                if (h == XLAlignmentHorizontalValues.Left || h == XLAlignmentHorizontalValues.Center || h == XLAlignmentHorizontalValues.CenterContinuous)
                {
                    int n = col + 1;
                    while (right - cell.Left < ft.Width + pad * 2 && n <= cLast + 1 && !filled.Contains((r, n)) && Layout.MergeAt(r, n) == null) { right += Layout.Cols.Size(n) * Zoom; n++; }
                }
                if (h == XLAlignmentHorizontalValues.Right || h == XLAlignmentHorizontalValues.Center || h == XLAlignmentHorizontalValues.CenterContinuous)
                {
                    int n = col - 1;
                    while (cell.Right - left < ft.Width + pad * 2 && n >= 1 && !filled.Contains((r, n)) && Layout.MergeAt(r, n) == null) { left -= Layout.Cols.Size(n) * Zoom; n--; }
                }
                clip = new Rect(left, cell.Top, right - left, cell.Height);
                if (h == XLAlignmentHorizontalValues.Center) { cell = new Rect(cell.X - (ft.Width - cell.Width) / 2 - pad, cell.Y, ft.Width + pad * 2, cell.Height); }
            }

            double x = h switch
            {
                XLAlignmentHorizontalValues.Right => cell.Right - pad - ft.Width - 1,
                XLAlignmentHorizontalValues.Center or XLAlignmentHorizontalValues.CenterContinuous => cell.X + (cell.Width - ft.Width) / 2,
                _ => cell.X + pad
            };
            if (wrap) { ft.TextAlignment = h == XLAlignmentHorizontalValues.Right ? TextAlignment.Right : h == XLAlignmentHorizontalValues.Center ? TextAlignment.Center : h == XLAlignmentHorizontalValues.Justify ? TextAlignment.Justify : TextAlignment.Left; x = cell.X + pad; }
            double y = al.Vertical switch
            {
                XLAlignmentVerticalValues.Top => cell.Y + 1,
                XLAlignmentVerticalValues.Center => cell.Y + (cell.Height - ft.Height) / 2,
                _ => cell.Bottom - ft.Height - 1.5
            };
            if (ft.Height > cell.Height && !wrap) y = cell.Bottom - ft.Height;
            dc.PushClip(new RectangleGeometry(new Rect(clip.X, clip.Y, Math.Max(0, clip.Width - 1), Math.Max(0, clip.Height - 1))));
            dc.DrawText(ft, new Point(Math.Round(x), Math.Round(y)));
            dc.Pop();
        }

        static double SafeNumber(IXLCell c) { try { var v = c.Value; return v.IsNumber ? v.GetNumber() : v.IsDateTime ? v.GetDateTime().ToOADate() : 0; } catch { return 0; } }

        void DrawBorders(DrawingContext dc, IXLCell c, int r, int col, XLWorkbook wb)
        {
            IXLBorder b;
            try { b = c.Style.Border; } catch { return; }
            if (b.LeftBorder == XLBorderStyleValues.None && b.RightBorder == XLBorderStyleValues.None && b.TopBorder == XLBorderStyleValues.None && b.BottomBorder == XLBorderStyleValues.None && b.DiagonalBorder == XLBorderStyleValues.None) return;
            var rect = new Rect(ColX(col), RowY(r), Layout.Cols.Size(col) * Zoom, Layout.Rows.Size(r) * Zoom);
            double x1 = Math.Floor(rect.Left) - 0.5, x2 = Math.Floor(rect.Right) - 0.5, y1 = Math.Floor(rect.Top) - 0.5, y2 = Math.Floor(rect.Bottom) - 0.5;
            void Line(XLBorderStyleValues s, XLColor color, Point a, Point z)
            {
                if (s == XLBorderStyleValues.None) return;
                var pen = BorderPen(s, XlColors.ToWpf(color, wb, true) ?? Colors.Black);
                if (s == XLBorderStyleValues.Double)
                {
                    bool horiz = Math.Abs(a.Y - z.Y) < 0.1;
                    var o = horiz ? new Vector(0, 1) : new Vector(1, 0);
                    var thin = new Pen(pen.Brush, 1);
                    dc.DrawLine(thin, a - o, z - o); dc.DrawLine(thin, a + o, z + o);
                    return;
                }
                dc.DrawLine(pen, a, z);
            }
            Line(b.TopBorder, b.TopBorderColor, new Point(x1, y1), new Point(x2, y1));
            Line(b.BottomBorder, b.BottomBorderColor, new Point(x1, y2), new Point(x2, y2));
            Line(b.LeftBorder, b.LeftBorderColor, new Point(x1, y1), new Point(x1, y2));
            Line(b.RightBorder, b.RightBorderColor, new Point(x2, y1), new Point(x2, y2));
            if (b.DiagonalBorder != XLBorderStyleValues.None)
            {
                if (b.DiagonalDown) Line(b.DiagonalBorder, b.DiagonalBorderColor, new Point(x1, y1), new Point(x2, y2));
                if (b.DiagonalUp) Line(b.DiagonalBorder, b.DiagonalBorderColor, new Point(x1, y2), new Point(x2, y1));
            }
        }

        static Pen BorderPen(XLBorderStyleValues s, Color c)
        {
            var pen = new Pen(B(c), s switch
            {
                XLBorderStyleValues.Medium or XLBorderStyleValues.MediumDashed or XLBorderStyleValues.MediumDashDot or XLBorderStyleValues.MediumDashDotDot => 2,
                XLBorderStyleValues.Thick => 3,
                _ => 1
            });
            pen.DashStyle = s switch
            {
                XLBorderStyleValues.Dashed or XLBorderStyleValues.MediumDashed => new DashStyle(new double[] { 3, 1 }, 0),
                XLBorderStyleValues.Dotted or XLBorderStyleValues.Hair => new DashStyle(new double[] { 1, 1 }, 0),
                XLBorderStyleValues.DashDot or XLBorderStyleValues.MediumDashDot or XLBorderStyleValues.SlantDashDot => new DashStyle(new double[] { 3, 1, 1, 1 }, 0),
                XLBorderStyleValues.DashDotDot or XLBorderStyleValues.MediumDashDotDot => new DashStyle(new double[] { 3, 1, 1, 1, 1, 1 }, 0),
                _ => DashStyles.Solid
            };
            pen.Freeze();
            return pen;
        }

        void DrawSelection(DrawingContext dc)
        {
            var area = new Rect(HeaderW, HeaderH, Math.Max(0, ActualWidth - HeaderW), Math.Max(0, ActualHeight - HeaderH));
            dc.PushClip(new RectangleGeometry(area));
            var activeRect = CellRectPx(Layout.MergeAt(View.ActiveRow, View.ActiveCol) ?? new CellRect(View.ActiveRow, View.ActiveCol, View.ActiveRow, View.ActiveCol));
            foreach (var s in View.Selection)
            {
                if (s.Count == 1 && View.Selection.Count == 1) continue;
                var rp = CellRectPx(ClipToView(s));
                var g = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(rp), new RectangleGeometry(activeRect));
                dc.DrawGeometry(SelFill, null, g);
            }
            if (View.Selection.Count == 1 && Mode != EditMode.Edit && Mode != EditMode.Enter || Mode == EditMode.Point)
            {
                var sel = View.Selection[0];
                var rp = CellRectPx(ClipToView(sel));
                dc.DrawRectangle(null, SelPen, new Rect(Math.Floor(rp.X) - 0.5, Math.Floor(rp.Y) - 0.5, Math.Max(0, rp.Width), Math.Max(0, rp.Height)));
                if (Settings.Current.FillHandle && !sel.IsWholeColumns && !sel.IsWholeRows)
                {
                    var fh = FillHandleRect();
                    dc.DrawRectangle(Brushes.Black, new Pen(Brushes.White, 1), fh);
                }
            }
            else
            {
                dc.DrawRectangle(null, new Pen(Brushes.Black, 1), new Rect(Math.Floor(activeRect.X) - 0.5, Math.Floor(activeRect.Y) - 0.5, activeRect.Width, activeRect.Height));
            }
            dc.Pop();
        }

        CellRect ClipToView(CellRect r)
        {
            // huge selections (whole columns) only need drawing a little past the view
            int rMax = Math.Min(r.R2, (RowLanes.Count > 0 ? RowLanes[^1].Index : 1) + 1);
            int cMax = Math.Min(r.C2, (ColLanes.Count > 0 ? ColLanes[^1].Index : 1) + 1);
            int rMin = Math.Max(r.R1, Math.Max(1, (FrozenRows > 0 ? 1 : View.TopRow) - 1));
            int cMin = Math.Max(r.C1, Math.Max(1, (FrozenCols > 0 ? 1 : View.LeftCol) - 1));
            if (r.R1 < rMin && r.R1 > FrozenRows) rMin = Math.Max(r.R1, View.TopRow - 1); else rMin = r.R1;
            if (r.C1 < cMin && r.C1 > FrozenCols) cMin = Math.Max(r.C1, View.LeftCol - 1); else cMin = r.C1;
            if (rMax < rMin) rMax = rMin;
            if (cMax < cMin) cMax = cMin;
            return new CellRect(rMin, cMin, Math.Max(rMin, rMax), Math.Max(cMin, cMax));
        }

        Rect FillHandleRect()
        {
            var rp = CellRectPx(ClipToView(View.Selection[^1]));
            return new Rect(Math.Floor(rp.Right) - 3.5, Math.Floor(rp.Bottom) - 3.5, 6, 6);
        }

        void DrawMarquee(DrawingContext dc, CellRect r)
        {
            var rp = CellRectPx(ClipToView(r));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)), 1.5) { DashStyle = new DashStyle(new double[] { 3, 3 }, _antsOffset) };
            dc.PushClip(new RectangleGeometry(new Rect(HeaderW, HeaderH, Math.Max(0, ActualWidth - HeaderW), Math.Max(0, ActualHeight - HeaderH))));
            dc.DrawRectangle(null, pen, new Rect(rp.X - 1, rp.Y - 1, rp.Width + 1, rp.Height + 1));
            dc.Pop();
        }

        void DrawHeaders(DrawingContext dc, double W, double H)
        {
            double hx = HeaderW, hy = HeaderH;
            var dip = Dip;
            double fs = Math.Clamp(11 * 96 / 72.0 * Math.Clamp(Zoom, 0.6, 1.6), 9, 22);
            var selCols = new HashSet<int>(); var selRows = new HashSet<int>();
            var fullCols = new HashSet<int>(); var fullRows = new HashSet<int>();
            foreach (var s in View.Selection)
            {
                foreach (var l in ColLanes) if (l.Index >= s.C1 && l.Index <= s.C2) { selCols.Add(l.Index); if (s.IsWholeColumns) fullCols.Add(l.Index); }
                foreach (var l in RowLanes) if (l.Index >= s.R1 && l.Index <= s.R2) { selRows.Add(l.Index); if (s.IsWholeRows) fullRows.Add(l.Index); }
            }
            // column headers
            dc.DrawRectangle(HeaderBg, null, new Rect(hx, 0, W - hx, hy));
            foreach (var l in ColLanes)
            {
                if (l.Size <= 0) continue;
                var r = new Rect(l.Pos, 0, l.Size, hy);
                if (selCols.Contains(l.Index)) dc.DrawRectangle(fullCols.Contains(l.Index) ? HeaderAll : HeaderSel, null, r);
                double x = Math.Floor(l.Pos + l.Size) - 0.5;
                dc.DrawLine(HeaderLine, new Point(x, 2), new Point(x, hy));
                var ft = new FormattedText(A1.Col(l.Index), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, HeaderFace, fs, HeaderText, dip);
                if (ft.Width < l.Size - 2) dc.DrawText(ft, new Point(l.Pos + (l.Size - ft.Width) / 2, (hy - ft.Height) / 2));
            }
            dc.DrawLine(HeaderLine, new Point(hx, hy - 0.5), new Point(W, hy - 0.5));
            // row headers
            dc.DrawRectangle(HeaderBg, null, new Rect(0, hy, hx, H - hy));
            foreach (var l in RowLanes)
            {
                if (l.Size <= 0) continue;
                var r = new Rect(0, l.Pos, hx, l.Size);
                if (selRows.Contains(l.Index)) dc.DrawRectangle(fullRows.Contains(l.Index) ? HeaderAll : HeaderSel, null, r);
                double y = Math.Floor(l.Pos + l.Size) - 0.5;
                dc.DrawLine(HeaderLine, new Point(0, y), new Point(hx, y));
                var ft = new FormattedText(l.Index.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, HeaderFace, fs, HeaderText, dip);
                if (ft.Height < l.Size + 3) dc.DrawText(ft, new Point((hx - ft.Width) / 2, l.Pos + (l.Size - ft.Height) / 2));
            }
            dc.DrawLine(HeaderLine, new Point(hx - 0.5, hy), new Point(hx - 0.5, H));
            // select-all corner with its triangle
            dc.DrawRectangle(HeaderBg, null, new Rect(0, 0, hx, hy));
            var tri = new StreamGeometry();
            using (var g = tri.Open()) { g.BeginFigure(new Point(hx - 3, hy - 13), true, true); g.LineTo(new Point(hx - 3, hy - 3), true, false); g.LineTo(new Point(hx - 13, hy - 3), true, false); }
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0xB4, 0xC6, 0xDC)), null, tri);
            dc.DrawLine(HeaderLine, new Point(hx - 0.5, 0), new Point(hx - 0.5, hy));
            dc.DrawLine(HeaderLine, new Point(0, hy - 0.5), new Point(hx, hy - 0.5));
        }

        // ---- AutoFilter dropdown buttons
        public CellRect? FilterRange
        {
            get
            {
                try
                {
                    var af = Sheet.AutoFilter;
                    if (af == null || !af.IsEnabled || af.Range == null) return null;
                    var a = af.Range.RangeAddress;
                    return new CellRect(a.FirstAddress.RowNumber, a.FirstAddress.ColumnNumber, a.LastAddress.RowNumber, a.LastAddress.ColumnNumber);
                }
                catch { return null; }
            }
        }

        Rect FilterButtonRect(int row, int col)
        {
            double x = ColX(col) + Layout.Cols.Size(col) * Zoom, y = RowY(row) + Layout.Rows.Size(row) * Zoom;
            double s = Math.Min(16 * Math.Clamp(Zoom, 0.8, 1.5), Layout.Rows.Size(row) * Zoom - 1);
            return new Rect(x - s - 1, y - s - 1, s, s);
        }

        void DrawFilterButtons(DrawingContext dc, IXLWorksheet ws, HashSet<int> visRows, HashSet<int> visCols)
        {
            var fr = FilterRange;
            if (!fr.HasValue || !visRows.Contains(fr.Value.R1)) return;
            for (int c = fr.Value.C1; c <= fr.Value.C2; c++)
            {
                if (!visCols.Contains(c)) continue;
                var r = FilterButtonRect(fr.Value.R1, c);
                bool active = false;
                try { active = ws.AutoFilter.Column(c - fr.Value.C1 + 1).FilterType != XLFilterType.None; } catch { }
                dc.DrawRoundedRectangle(new LinearGradientBrush(Colors.White, Color.FromRgb(0xDD, 0xE6, 0xF2), 90), new Pen(new SolidColorBrush(Color.FromRgb(0x8C, 0xA3, 0xC0)), 1), r, 1.5, 1.5);
                var g = new StreamGeometry();
                using (var gc = g.Open())
                {
                    double cx = r.X + r.Width / 2 - (active ? 2.5 : 0), cy = r.Y + r.Height / 2 - 1;
                    gc.BeginFigure(new Point(cx - 3.5, cy - 1.5), true, true); gc.LineTo(new Point(cx + 3.5, cy - 1.5), true, false); gc.LineTo(new Point(cx, cy + 2.5), true, false);
                }
                dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x20, 0x30, 0x48)), null, g);
                if (active)
                {
                    var f = new StreamGeometry();
                    using (var gc = f.Open()) { double fx = r.Right - 6, fy = r.Y + 3; gc.BeginFigure(new Point(fx - 2.5, fy), true, true); gc.LineTo(new Point(fx + 2.5, fy), true, false); gc.LineTo(new Point(fx + 0.6, fy + 3), true, false); gc.LineTo(new Point(fx + 0.6, fy + 6), true, false); gc.LineTo(new Point(fx - 0.6, fy + 5.4), true, false); gc.LineTo(new Point(fx - 0.6, fy + 3), true, false); }
                    dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x1A, 0x57, 0xB5)), null, f);
                }
            }
        }

        // ===================================================================== pictures & charts
        /// <summary>A picture or chart floating over the cells.</summary>
        public class SheetObject
        {
            public ChartModel Chart;
            public IXLPicture Picture;
            public int Row, Col;
            public double OffX, OffY, W, H;
        }

        public Func<IEnumerable<ChartModel>> ChartsSource;
        public SheetObject Selected;
        public event Action<SheetObject> ObjectSelected;
        public event Action<SheetObject, int, int, double, double, double, double> ObjectMoved; // obj, row, col, offX, offY, w, h
        public event Action<SheetObject, Point> ObjectMenuRequested;
        readonly Dictionary<IXLPicture, System.Windows.Media.Imaging.BitmapSource> _pics = new();

        public List<SheetObject> Objects()
        {
            var list = new List<SheetObject>();
            try
            {
                foreach (var p in Sheet.Pictures)
                {
                    var tl = p.TopLeftCell;
                    if (tl == null) continue;
                    list.Add(new SheetObject { Picture = p, Row = tl.Address.RowNumber, Col = tl.Address.ColumnNumber, W = p.Width, H = p.Height });
                }
            }
            catch { }
            if (ChartsSource != null)
                foreach (var c in ChartsSource().Where(c => c.Sheet == Sheet.Name))
                    list.Add(new SheetObject { Chart = c, Row = c.Row, Col = c.Col, OffX = c.OffsetX, OffY = c.OffsetY, W = c.Width, H = c.Height });
            return list;
        }

        public Rect ObjectRect(SheetObject o) => new Rect(ColX(o.Col) + o.OffX * Zoom, RowY(o.Row) + o.OffY * Zoom, o.W * Zoom, o.H * Zoom);

        bool SameObject(SheetObject a, SheetObject b) => a != null && b != null && (a.Chart != null ? a.Chart == b.Chart : a.Picture == b.Picture);

        void DrawObjects(DrawingContext dc)
        {
            var objs = Objects();
            if (objs.Count == 0) return;
            foreach (var o in objs)
            {
                var r = ObjectRect(o);
                if (r.Right < HeaderW || r.Bottom < HeaderH || r.Left > ActualWidth || r.Top > ActualHeight) continue;
                if (o.Picture != null)
                {
                    if (!_pics.TryGetValue(o.Picture, out var bmp))
                    {
                        try
                        {
                            var bi = new System.Windows.Media.Imaging.BitmapImage();
                            bi.BeginInit(); bi.StreamSource = new System.IO.MemoryStream(o.Picture.ImageStream.ToArray()); bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bi.EndInit(); bi.Freeze();
                            bmp = bi;
                        }
                        catch { bmp = null; }
                        _pics[o.Picture] = bmp;
                    }
                    if (bmp != null) dc.DrawImage(bmp, r);
                }
                else
                {
                    dc.PushTransform(new TranslateTransform(r.X, r.Y));
                    dc.PushTransform(new ScaleTransform(Zoom, Zoom));
                    ChartRenderer.Draw(dc, o.Chart, Book.Wb, new Rect(0, 0, o.W, o.H));
                    dc.Pop(); dc.Pop();
                }
            }
            if (Selected != null)
            {
                var cur = objs.FirstOrDefault(o => SameObject(o, Selected));
                if (cur == null) { Selected = null; return; }
                if (_objDrag != null) cur = _objDrag;
                var r = ObjectRect(cur);
                dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0x7F, 0x9D, 0xB9)), 3), new Rect(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4));
                foreach (var h in Handles(r)) dc.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(0x5A, 0x7C, 0xA8)), 1), h, 3.5, 3.5);
            }
        }

        static Point[] Handles(Rect r) => new[] { r.TopLeft, new Point(r.X + r.Width / 2, r.Y), r.TopRight, new Point(r.Right, r.Y + r.Height / 2), r.BottomRight, new Point(r.X + r.Width / 2, r.Bottom), r.BottomLeft, new Point(r.X, r.Y + r.Height / 2) };

        SheetObject HitObject(Point p) => Objects().LastOrDefault(o => { var r = ObjectRect(o); r.Inflate(3, 3); return r.Contains(p); });

        SheetObject _objDrag;
        int _objHandle = -1;
        Point _objStart;
        SheetObject _objOrig;

        public void ClearObjectSelection() { if (Selected != null) { Selected = null; ObjectSelected?.Invoke(null); InvalidateVisual(); } }

        bool ObjectMouseDown(Point p, MouseButtonEventArgs e)
        {
            if (p.X < HeaderW || p.Y < HeaderH) return false;
            if (Selected != null)
            {
                var cur = Objects().FirstOrDefault(o => SameObject(o, Selected));
                if (cur != null)
                {
                    var hs = Handles(ObjectRect(cur));
                    for (int i = 0; i < hs.Length; i++)
                        if ((hs[i] - p).Length <= 6) { StartObjectDrag(cur, i, p); return true; }
                }
            }
            var hit = HitObject(p);
            if (hit == null) { ClearObjectSelection(); return false; }
            Selected = hit;
            ObjectSelected?.Invoke(hit);
            if (e.ClickCount == 2) { ObjectMenuRequested?.Invoke(hit, p); return true; }
            StartObjectDrag(hit, -1, p);
            InvalidateVisual();
            return true;
        }

        void StartObjectDrag(SheetObject o, int handle, Point p)
        {
            _objOrig = o; _objHandle = handle; _objStart = p;
            _objDrag = new SheetObject { Chart = o.Chart, Picture = o.Picture, Row = o.Row, Col = o.Col, OffX = o.OffX, OffY = o.OffY, W = o.W, H = o.H };
            _drag = Drag.Object;
            Focus();
            CaptureMouse();
        }

        void ObjectDragMove(Point p)
        {
            if (_objDrag == null) return;
            var o = _objOrig;
            var r0 = ObjectRect(o);
            double dx = p.X - _objStart.X, dy = p.Y - _objStart.Y;
            Rect r = r0;
            switch (_objHandle)
            {
                case -1: r.Offset(dx, dy); break;
                default:
                    double l = r0.Left, t = r0.Top, rr = r0.Right, b = r0.Bottom;
                    if (_objHandle is 0 or 6 or 7) l += dx;
                    if (_objHandle is 2 or 3 or 4) rr += dx;
                    if (_objHandle is 0 or 1 or 2) t += dy;
                    if (_objHandle is 4 or 5 or 6) b += dy;
                    r = new Rect(new Point(Math.Min(l, rr - 20), Math.Min(t, b - 20)), new Point(Math.Max(rr, l + 20), Math.Max(b, t + 20)));
                    break;
            }
            // back to an anchor cell + offset
            BuildLanes();
            var (row, col) = CellAt(new Point(Math.Max(HeaderW + 1, r.X), Math.Max(HeaderH + 1, r.Y)));
            if (r.X < HeaderW) col = Math.Max(1, col);
            _objDrag.Row = Math.Max(1, row); _objDrag.Col = Math.Max(1, col);
            _objDrag.OffX = Math.Max(0, (r.X - ColX(_objDrag.Col)) / Zoom);
            _objDrag.OffY = Math.Max(0, (r.Y - RowY(_objDrag.Row)) / Zoom);
            _objDrag.W = r.Width / Zoom; _objDrag.H = r.Height / Zoom;
            InvalidateVisual();
        }

        void ObjectDragEnd()
        {
            var d = _objDrag;
            _objDrag = null;
            if (d == null) return;
            if (d.Row == _objOrig.Row && d.Col == _objOrig.Col && Math.Abs(d.OffX - _objOrig.OffX) < 0.5 && Math.Abs(d.OffY - _objOrig.OffY) < 0.5 && Math.Abs(d.W - _objOrig.W) < 0.5 && Math.Abs(d.H - _objOrig.H) < 0.5) { InvalidateVisual(); return; }
            ObjectMoved?.Invoke(_objOrig, d.Row, d.Col, d.OffX, d.OffY, d.W, d.H);
            InvalidateVisual();
        }

        public void ForgetPictureCache() => _pics.Clear();

        // ===================================================================== mouse
        enum Drag { None, Select, ColHeaders, RowHeaders, ColResize, RowResize, Fill, Move, PointRef, Object }
        Drag _drag;
        int _resizeIndex; double _resizeStart, _resizeOrig;
        CellRect? _fillDrag, _moveTarget;
        CellRect _fillSource;
        Point _lastMouse;
        readonly DispatcherTimer _scrollTimer;
        int _pointAnchorRow, _pointAnchorCol;

        int ColBorderAt(double x)
        {
            if (_host.Editor.IsVisible && Mode != EditMode.Point) return 0;
            foreach (var l in ColLanes) { double e = l.Pos + l.Size; if (Math.Abs(x - e) <= 3 && x >= HeaderW) return l.Index; }
            return 0;
        }
        int RowBorderAt(double y)
        {
            foreach (var l in RowLanes) { double e = l.Pos + l.Size; if (Math.Abs(y - e) <= 2.5 && y >= HeaderH) return l.Index; }
            return 0;
        }

        bool OnSelectionBorder(Point p)
        {
            if (View.Selection.Count != 1) return false;
            var rp = CellRectPx(ClipToView(ActiveRect));
            if (FillHandleRect().Contains(p)) return false;
            var outer = new Rect(rp.X - 3, rp.Y - 3, rp.Width + 6, rp.Height + 6);
            var inner = new Rect(rp.X + 2, rp.Y + 2, Math.Max(0, rp.Width - 4), Math.Max(0, rp.Height - 4));
            return outer.Contains(p) && !inner.Contains(p);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var p = e.GetPosition(this);
            _lastMouse = p;
            if (_drag == Drag.None)
            {
                Cursor c = null;
                if (p.Y < HeaderH && p.X > HeaderW) c = ColBorderAt(p.X) > 0 ? Cursors.SizeWE : Cursors.Arrow;
                else if (p.X < HeaderW && p.Y > HeaderH) c = RowBorderAt(p.Y) > 0 ? Cursors.SizeNS : Cursors.Arrow;
                else if (p.X > HeaderW && p.Y > HeaderH && Mode == EditMode.Ready && HitObject(p) != null)
                {
                    c = Cursors.SizeAll;
                    if (Selected != null) { var cur = Objects().FirstOrDefault(o => SameObject(o, Selected)); if (cur != null) { var hs = Handles(ObjectRect(cur)); for (int i = 0; i < hs.Length; i++) if ((hs[i] - p).Length <= 6) c = i % 4 == 0 ? Cursors.SizeNWSE : i % 4 == 2 ? Cursors.SizeNESW : i % 2 == 1 && (i == 1 || i == 5) ? Cursors.SizeNS : Cursors.SizeWE; } }
                }
                else if (p.X > HeaderW && p.Y > HeaderH)
                {
                    if (Mode == EditMode.Ready && View.Selection.Count == 1 && Settings.Current.FillHandle && FillHandleRect().Contains(new Point(p.X, p.Y)) ) c = Cursors.Cross;
                    else if (Mode == EditMode.Ready && OnSelectionBorder(p)) c = Cursors.SizeAll;
                    else c = Cursors.Cross;
                    var (hr, hc) = CellAt(p, false);
                    if (hr > 0 && hc > 0 && FilterRange is CellRect fr && hr == fr.R1 && hc >= fr.C1 && hc <= fr.C2 && FilterButtonRect(hr, hc).Contains(p)) c = Cursors.Arrow;
                }
                Cursor = c;
                return;
            }
            if (e.LeftButton != MouseButtonState.Pressed) { EndDrag(); return; }
            if (_drag == Drag.Object) { ObjectDragMove(p); return; }
            ContinueDrag(p);
            bool outside = p.Y < HeaderH || p.Y > ActualHeight || p.X < HeaderW || p.X > ActualWidth;
            if (outside && (_drag == Drag.Select || _drag == Drag.Fill || _drag == Drag.ColHeaders || _drag == Drag.RowHeaders || _drag == Drag.PointRef || _drag == Drag.Move)) _scrollTimer.Start(); else _scrollTimer.Stop();
        }

        void ContinueDrag(Point p)
        {
            var (row, col) = CellAt(p);
            switch (_drag)
            {
                case Drag.Select:
                    if (row > 0 && col > 0) { Select(row, col, extend: true); }
                    break;
                case Drag.ColHeaders:
                    if (col > 0) { View.Selection[^1] = new CellRect(1, View.AnchorCol, SheetLimits.Rows, col); SelectionChanged?.Invoke(); InvalidateVisual(); }
                    break;
                case Drag.RowHeaders:
                    if (row > 0) { View.Selection[^1] = new CellRect(View.AnchorRow, 1, row, SheetLimits.Cols); SelectionChanged?.Invoke(); InvalidateVisual(); }
                    break;
                case Drag.ColResize:
                case Drag.RowResize:
                    InvalidateVisual();
                    var delta = (_drag == Drag.ColResize ? p.X : p.Y) - _resizeStart;
                    var px = Math.Max(0, _resizeOrig + delta / Zoom);
                    if (_drag == Drag.ColResize) Layout.Cols.Set(_resizeIndex, px); else Layout.Rows.Set(_resizeIndex, px);
                    ToolTipText?.Invoke(_drag == Drag.ColResize ? $"Width: {Units.PxToColWidth(px):0.00} ({px:0} pixels)" : $"Height: {Units.PxToPt(px):0.00} ({px:0} pixels)");
                    break;
                case Drag.Fill:
                    if (row > 0 && col > 0)
                    {
                        var s = _fillSource;
                        int dr = row > s.R2 ? row - s.R2 : row < s.R1 ? row - s.R1 : 0;
                        int dcl = col > s.C2 ? col - s.C2 : col < s.C1 ? col - s.C1 : 0;
                        CellRect t;
                        if (Math.Abs(dr) >= Math.Abs(dcl) && dr != 0) t = dr > 0 ? new CellRect(s.R1, s.C1, row, s.C2) : new CellRect(row, s.C1, s.R2, s.C2);
                        else if (dcl != 0) t = dcl > 0 ? new CellRect(s.R1, s.C1, s.R2, col) : new CellRect(s.R1, col, s.R2, s.C2);
                        else t = s;
                        _fillDrag = t;
                        InvalidateVisual();
                    }
                    break;
                case Drag.Move:
                    if (row > 0 && col > 0)
                    {
                        var s = ActiveRect;
                        int r1 = Math.Clamp(row - _moveOffR, 1, SheetLimits.Rows - s.Rows + 1), c1 = Math.Clamp(col - _moveOffC, 1, SheetLimits.Cols - s.Cols + 1);
                        _moveTarget = new CellRect(r1, c1, r1 + s.Rows - 1, c1 + s.Cols - 1);
                        InvalidateVisual();
                    }
                    break;
                case Drag.PointRef:
                    if (row > 0 && col > 0) PointRefChanged?.Invoke(new CellRect(_pointAnchorRow, _pointAnchorCol, row, col), true);
                    break;
            }
        }

        public event Action<string> ToolTipText;
        public event Action<CellRect, bool> PointRefChanged; // range, extending
        int _moveOffR, _moveOffC;

        void AutoScrollTick()
        {
            var p = _lastMouse;
            int dr = p.Y > ActualHeight ? 1 : p.Y < HeaderH ? -1 : 0;
            int dc = p.X > ActualWidth ? 1 : p.X < HeaderW ? -1 : 0;
            if (_drag == Drag.ColHeaders) dr = 0;
            if (_drag == Drag.RowHeaders) dc = 0;
            if (dr == 0 && dc == 0) { _scrollTimer.Stop(); return; }
            ScrollTo(dr != 0 ? View.TopRow + dr : (int?)null, dc != 0 ? View.LeftCol + dc : (int?)null);
            BuildLanes();
            var clamped = new Point(Math.Clamp(p.X, HeaderW + 1, ActualWidth - 1), Math.Clamp(p.Y, HeaderH + 1, ActualHeight - 1));
            ContinueDrag(clamped);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            var p = e.GetPosition(this);
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            BuildLanes();

            // formula editing: clicking a cell inserts a reference
            if ((Mode == EditMode.Enter || Mode == EditMode.Edit || Mode == EditMode.Point) && WantsReference?.Invoke() == true && p.X > HeaderW && p.Y > HeaderH)
            {
                var (rr, cc) = CellAt(p);
                _pointAnchorRow = rr; _pointAnchorCol = cc;
                _drag = Drag.PointRef;
                CaptureMouse();
                PointRefChanged?.Invoke(new CellRect(rr, cc, rr, cc), false);
                e.Handled = true;
                return;
            }
            if (Mode != EditMode.Ready) CommitRequested?.Invoke();
            Focus();
            if (Mode == EditMode.Ready && ObjectMouseDown(p, e)) { e.Handled = true; return; }

            if (p.X < HeaderW && p.Y < HeaderH) { SelectAll(); return; }
            if (p.Y < HeaderH)
            {
                int border = ColBorderAt(p.X);
                if (border > 0)
                {
                    if (e.ClickCount == 2) { AutoFitRequested?.Invoke(true, border); return; }
                    _drag = Drag.ColResize; _resizeIndex = border; _resizeStart = p.X; _resizeOrig = Layout.Cols.Size(border);
                    CaptureMouse(); return;
                }
                var (_, col) = CellAt(p);
                if (col <= 0) return;
                if (shift) { View.Selection[^1] = new CellRect(1, View.AnchorCol, SheetLimits.Rows, col); }
                else
                {
                    var r = new CellRect(1, col, SheetLimits.Rows, col);
                    if (ctrl) View.Selection.Add(r); else View.Selection = new List<CellRect> { r };
                    View.AnchorCol = View.ActiveCol = col; View.AnchorRow = View.ActiveRow = Math.Max(View.TopRow, 1);
                    if (FrozenRows > 0) View.ActiveRow = 1;
                }
                _drag = Drag.ColHeaders; CaptureMouse();
                SelectionChanged?.Invoke(); InvalidateVisual();
                return;
            }
            if (p.X < HeaderW)
            {
                int border = RowBorderAt(p.Y);
                if (border > 0)
                {
                    if (e.ClickCount == 2) { AutoFitRequested?.Invoke(false, border); return; }
                    _drag = Drag.RowResize; _resizeIndex = border; _resizeStart = p.Y; _resizeOrig = Layout.Rows.Size(border);
                    CaptureMouse(); return;
                }
                var (row, _) = CellAt(p);
                if (row <= 0) return;
                if (shift) { View.Selection[^1] = new CellRect(View.AnchorRow, 1, row, SheetLimits.Cols); }
                else
                {
                    var r = new CellRect(row, 1, row, SheetLimits.Cols);
                    if (ctrl) View.Selection.Add(r); else View.Selection = new List<CellRect> { r };
                    View.AnchorRow = View.ActiveRow = row; View.AnchorCol = View.ActiveCol = Math.Max(View.LeftCol, 1);
                    if (FrozenCols > 0) View.ActiveCol = 1;
                }
                _drag = Drag.RowHeaders; CaptureMouse();
                SelectionChanged?.Invoke(); InvalidateVisual();
                return;
            }

            // filter buttons
            var (hr, hc) = CellAt(p, false);
            if (hr > 0 && hc > 0 && FilterRange is CellRect fr && hr == fr.R1 && hc >= fr.C1 && hc <= fr.C2 && FilterButtonRect(hr, hc).Contains(p))
            {
                var br = FilterButtonRect(hr, hc);
                FilterButtonClicked?.Invoke(hr, hc, new Point(br.Left, br.Bottom));
                return;
            }

            // fill handle
            if (View.Selection.Count == 1 && Settings.Current.FillHandle && FillHandleRect().Contains(p))
            {
                _fillSource = ActiveRect;
                _fillDrag = _fillSource;
                _drag = Drag.Fill; CaptureMouse();
                return;
            }
            // drag the selection border to move cells
            if (OnSelectionBorder(p))
            {
                var (mr, mc) = CellAt(p);
                _moveOffR = Math.Clamp(mr - ActiveRect.R1, 0, ActiveRect.Rows - 1); _moveOffC = Math.Clamp(mc - ActiveRect.C1, 0, ActiveRect.Cols - 1);
                _drag = Drag.Move; CaptureMouse();
                return;
            }

            var (r0, c0) = CellAt(p);
            if (r0 <= 0 || c0 <= 0) return;
            if (e.ClickCount == 2)
            {
                if (ActiveCell.HasHyperlink && Keyboard.Modifiers == ModifierKeys.None) { }
                BeginEdit(null, EditMode.Edit, caretAtClick: true);
                return;
            }
            if (shift) Select(r0, c0, extend: true);
            else
            {
                Select(r0, c0, add: ctrl);
                try { if (ActiveCell.HasHyperlink && !ctrl) { HyperlinkClicked?.Invoke(r0, c0); return; } } catch { }
            }
            _drag = Drag.Select;
            CaptureMouse();
        }

        public event Action CommitRequested;
        public Func<bool> WantsReference;

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            EndDrag();
        }

        void EndDrag()
        {
            _scrollTimer.Stop();
            var d = _drag;
            _drag = Drag.None;
            if (IsMouseCaptured) ReleaseMouseCapture();
            switch (d)
            {
                case Drag.Object:
                    ObjectDragEnd();
                    break;
                case Drag.ColResize:
                case Drag.RowResize:
                    bool cols = d == Drag.ColResize;
                    var size = cols ? Layout.Cols.Size(_resizeIndex) : Layout.Rows.Size(_resizeIndex);
                    // resizing one of several selected whole columns/rows resizes them all
                    var idx = new List<int> { _resizeIndex };
                    foreach (var s in View.Selection)
                    {
                        if (cols && s.IsWholeColumns && _resizeIndex >= s.C1 && _resizeIndex <= s.C2) idx.AddRange(Enumerable.Range(s.C1, s.Cols));
                        if (!cols && s.IsWholeRows && _resizeIndex >= s.R1 && _resizeIndex <= s.R2) idx.AddRange(Enumerable.Range(s.R1, s.Rows));
                    }
                    ToolTipText?.Invoke(null);
                    SizeDragged?.Invoke(cols, idx.Distinct().ToList(), size);
                    break;
                case Drag.Fill:
                    var target = _fillDrag;
                    _fillDrag = null;
                    InvalidateVisual();
                    if (target.HasValue && (target.Value.Rows != _fillSource.Rows || target.Value.Cols != _fillSource.Cols))
                        FillDragged?.Invoke(_fillSource, target.Value, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
                    else if (target.HasValue && (target.Value.R1 > _fillSource.R1 || target.Value.C1 > _fillSource.C1))
                        FillDragged?.Invoke(_fillSource, target.Value, false);
                    break;
                case Drag.Move:
                    var mt = _moveTarget;
                    _moveTarget = null;
                    InvalidateVisual();
                    if (mt.HasValue && (mt.Value.R1 != ActiveRect.R1 || mt.Value.C1 != ActiveRect.C1))
                        MoveDragged?.Invoke(ActiveRect, mt.Value.R1, mt.Value.C1, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
                    break;
            }
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonDown(e);
            var p = e.GetPosition(this);
            if (Mode != EditMode.Ready) return;
            Focus();
            var hitObj = p.X > HeaderW && p.Y > HeaderH ? HitObject(p) : null;
            if (hitObj != null) { Selected = hitObj; ObjectSelected?.Invoke(hitObj); InvalidateVisual(); ObjectMenuRequested?.Invoke(hitObj, p); e.Handled = true; return; }
            var (r, c) = CellAt(p, false);
            if (p.Y < HeaderH && c > 0)
            {
                if (!View.Selection.Any(s => s.IsWholeColumns && c >= s.C1 && c <= s.C2)) { View.Selection = new List<CellRect> { new CellRect(1, c, SheetLimits.Rows, c) }; View.ActiveCol = View.AnchorCol = c; View.ActiveRow = View.AnchorRow = View.TopRow; }
            }
            else if (p.X < HeaderW && r > 0)
            {
                if (!View.Selection.Any(s => s.IsWholeRows && r >= s.R1 && r <= s.R2)) { View.Selection = new List<CellRect> { new CellRect(r, 1, r, SheetLimits.Cols) }; View.ActiveRow = View.AnchorRow = r; View.ActiveCol = View.AnchorCol = View.LeftCol; }
            }
            else if (r > 0 && c > 0 && !View.Selection.Any(s => s.Contains(r, c))) Select(r, c);
            SelectionChanged?.Invoke();
            InvalidateVisual();
            ContextMenuRequested?.Invoke(p);
            e.Handled = true;
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { ZoomRequested?.Invoke(e.Delta > 0 ? 0.15 : -0.15); e.Handled = true; return; }
            int lines = Math.Max(1, SystemParameters.WheelScrollLines);
            int step = -Math.Sign(e.Delta) * lines;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) ScrollTo(null, View.LeftCol + step);
            else ScrollTo(View.TopRow + step, null);
            e.Handled = true;
        }

        public event Action<double> ZoomRequested;

        // ===================================================================== editing
        string _editOriginal;
        public bool IsEditing => Mode != EditMode.Ready;

        /// <summary>Starts editing the active cell. <paramref name="initial"/> replaces the content (typing), null keeps it (F2).</summary>
        public void BeginEdit(string initial, EditMode mode, bool caretAtClick = false)
        {
            if (Sheet.IsProtected && !CellUnlocked()) { ProtectedEdit?.Invoke(); return; }
            var ed = _host.Editor;
            _editOriginal = CellInput.EditText(ActiveCell);
            Mode = mode;
            var st = ActiveCell.Style;
            ed.FontFamily = new FontFamily(string.IsNullOrEmpty(st.Font.FontName) ? "Calibri" : st.Font.FontName);
            ed.FontSize = Units.PtToPx(st.Font.FontSize > 0 ? st.Font.FontSize : 11) * Zoom;
            ed.FontWeight = st.Font.Bold ? FontWeights.Bold : FontWeights.Normal;
            ed.FontStyle = st.Font.Italic ? FontStyles.Italic : FontStyles.Normal;
            ed.Foreground = B(XlColors.ToWpf(st.Font.FontColor, Book.Wb, true) ?? Colors.Black);
            var fill = FillOf(st, Book.Wb);
            ed.Background = fill.HasValue ? B(fill.Value) : Brushes.White;
            ed.TextWrapping = st.Alignment.WrapText ? TextWrapping.Wrap : TextWrapping.NoWrap;
            ed.Text = initial ?? _editOriginal;
            PositionEditor();
            ed.Visibility = Visibility.Visible;
            ed.Focus();
            ed.CaretIndex = ed.Text.Length;
            ModeChanged?.Invoke();
            InvalidateVisual();
        }

        public event Action ProtectedEdit;
        bool CellUnlocked() { try { return !ActiveCell.Style.Protection.Locked; } catch { return false; } }

        public void PositionEditor()
        {
            var m = Layout.MergeAt(View.ActiveRow, View.ActiveCol) ?? new CellRect(View.ActiveRow, View.ActiveCol, View.ActiveRow, View.ActiveCol);
            var r = CellRectPx(m);
            _host.PlaceEditor(new Rect(r.X, r.Y, Math.Max(20, r.Width - 1), Math.Max(14, r.Height - 1)), _host.Editor.FontSize);
        }

        public string EditText { get => _host.Editor.Text; set { _host.Editor.Text = value; _host.Editor.CaretIndex = value.Length; } }
        public TextBox EditorBox => _host.Editor;

        public void EndEdit(bool commit)
        {
            if (Mode == EditMode.Ready) return;
            var text = _host.Editor.Text;
            Mode = EditMode.Ready;
            _host.Editor.Visibility = Visibility.Collapsed;
            _refHighlight = null;
            ModeChanged?.Invoke();
            Focus();
            if (commit && text != _editOriginal) EditCommitted?.Invoke(text);
            InvalidateVisual();
        }

        public event Action<KeyEventArgs> EditorKey;

        void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            EditorKey?.Invoke(e);
        }

        protected override void OnTextInput(TextCompositionEventArgs e)
        {
            base.OnTextInput(e);
            if (Mode != EditMode.Ready || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0]) || Selected != null) return;
            BeginEdit(e.Text, EditMode.Enter);
            e.Handled = true;
        }
    }

    /// <summary>Evaluates the common conditional formats (cell value rules, colour scales, data bars) for drawing.</summary>
    public class CondFormatCache
    {
        public struct Result { public Color? Fill, Font; public bool Bold; public (double, Color)? DataBar; }
        readonly IXLWorksheet _ws;
        readonly XLWorkbook _wb;
        readonly List<(CellRect Range, IXLConditionalFormat Cf)> _list = new();
        readonly Dictionary<IXLConditionalFormat, (double Min, double Max)> _stats = new();

        public CondFormatCache(IXLWorksheet ws, XLWorkbook wb)
        {
            _ws = ws; _wb = wb;
            try
            {
                foreach (var cf in ws.ConditionalFormats)
                    foreach (var r in cf.Ranges)
                    {
                        var a = r.RangeAddress;
                        _list.Add((new CellRect(a.FirstAddress.RowNumber, a.FirstAddress.ColumnNumber, a.LastAddress.RowNumber, a.LastAddress.ColumnNumber), cf));
                    }
            }
            catch { }
        }

        readonly Dictionary<(int, int), Result> _done = new();

        public Result For(IXLCell c)
        {
            if (_list.Count == 0) return new Result();
            var key = (c.Address.RowNumber, c.Address.ColumnNumber);
            if (_done.TryGetValue(key, out var cached)) return cached;
            var res = Compute(c);
            _done[key] = res;
            return res;
        }

        Result Compute(IXLCell c)
        {
            var res = new Result();
            int r = c.Address.RowNumber, col = c.Address.ColumnNumber;
            foreach (var (range, cf) in _list)
            {
                if (!range.Contains(r, col)) continue;
                try
                {
                    var v = c.Value;
                    double? num = v.IsNumber ? v.GetNumber() : v.IsDateTime ? v.GetDateTime().ToOADate() : (double?)null;
                    switch (cf.ConditionalFormatType)
                    {
                        case XLConditionalFormatType.CellIs:
                            if (CellIs(cf, v, num)) Apply(cf, ref res);
                            break;
                        case XLConditionalFormatType.ContainsText:
                        case XLConditionalFormatType.StartsWith:
                        case XLConditionalFormatType.EndsWith:
                        case XLConditionalFormatType.NotContainsText:
                            var t = c.GetFormattedString(); var val = Strip(cf.Values.Values.FirstOrDefault()?.Value ?? "");
                            bool hit = cf.ConditionalFormatType switch
                            {
                                XLConditionalFormatType.StartsWith => t.StartsWith(val, StringComparison.CurrentCultureIgnoreCase),
                                XLConditionalFormatType.EndsWith => t.EndsWith(val, StringComparison.CurrentCultureIgnoreCase),
                                XLConditionalFormatType.NotContainsText => t.IndexOf(val, StringComparison.CurrentCultureIgnoreCase) < 0,
                                _ => t.IndexOf(val, StringComparison.CurrentCultureIgnoreCase) >= 0
                            };
                            if (hit) Apply(cf, ref res);
                            break;
                        case XLConditionalFormatType.IsBlank: if (v.IsBlank) Apply(cf, ref res); break;
                        case XLConditionalFormatType.NotBlank: if (!v.IsBlank) Apply(cf, ref res); break;
                        case XLConditionalFormatType.IsError: if (v.IsError) Apply(cf, ref res); break;
                        case XLConditionalFormatType.IsDuplicate:
                        case XLConditionalFormatType.IsUnique:
                            int count = 0; var me = c.GetFormattedString();
                            foreach (var other in _ws.Range(range.R1, range.C1, range.R2, range.C2).CellsUsed()) if (other.GetFormattedString() == me) count++;
                            if ((cf.ConditionalFormatType == XLConditionalFormatType.IsDuplicate) == (count > 1)) Apply(cf, ref res);
                            break;
                        case XLConditionalFormatType.Top10:
                        case XLConditionalFormatType.AboveAverage:
                            if (num.HasValue && RankRule(cf, range, num.Value)) Apply(cf, ref res);
                            break;
                        case XLConditionalFormatType.ColorScale:
                            if (num.HasValue) res.Fill = Scale(cf, range, num.Value);
                            break;
                        case XLConditionalFormatType.DataBar:
                            if (num.HasValue)
                            {
                                var (min, max) = Stats(cf, range);
                                double frac = max > min ? (num.Value - min) / (max - min) : 1;
                                var color = XlColors.ToWpf(cf.Colors.Values.FirstOrDefault(), _wb) ?? Color.FromRgb(0x63, 0x8E, 0xC6);
                                res.DataBar = (Math.Clamp(0.1 + frac * 0.9, 0, 1), color);
                            }
                            break;
                    }
                }
                catch { }
            }
            return res;
        }

        static string Strip(string s) => s.Trim('"');

        bool CellIs(IXLConditionalFormat cf, XLCellValue v, double? num)
        {
            var vals = cf.Values.Values.Select(x => x.Value).ToList();
            double? A(int i) { if (i >= vals.Count) return null; var s = Strip(vals[i]); if (s.StartsWith("=")) s = s.Substring(1); try { var ev = _ws.Evaluate(s); if (ev.IsNumber) return ev.GetNumber(); } catch { } return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : (double?)null; }
            if (!num.HasValue)
            {
                if (v.IsText && cf.Operator == XLCFOperator.Equal) return vals.Count > 0 && Strip(vals[0]).Equals(v.GetText(), StringComparison.CurrentCultureIgnoreCase);
                if (v.IsText && cf.Operator == XLCFOperator.NotEqual) return vals.Count > 0 && !Strip(vals[0]).Equals(v.GetText(), StringComparison.CurrentCultureIgnoreCase);
                return false;
            }
            var a = A(0); var b = A(1);
            if (!a.HasValue) return false;
            double n = num.Value;
            return cf.Operator switch
            {
                XLCFOperator.Equal => n == a, XLCFOperator.NotEqual => n != a,
                XLCFOperator.GreaterThan => n > a, XLCFOperator.LessThan => n < a,
                XLCFOperator.EqualOrGreaterThan => n >= a, XLCFOperator.EqualOrLessThan => n <= a,
                XLCFOperator.Between => b.HasValue && n >= Math.Min(a.Value, b.Value) && n <= Math.Max(a.Value, b.Value),
                XLCFOperator.NotBetween => b.HasValue && (n < Math.Min(a.Value, b.Value) || n > Math.Max(a.Value, b.Value)),
                _ => false
            };
        }

        (double, double) Stats(IXLConditionalFormat cf, CellRect range)
        {
            if (_stats.TryGetValue(cf, out var s)) return s;
            double min = double.MaxValue, max = double.MinValue;
            foreach (var c in _ws.Range(range.R1, range.C1, range.R2, range.C2).CellsUsed())
            {
                try { var v = c.Value; if (v.IsNumber) { min = Math.Min(min, v.GetNumber()); max = Math.Max(max, v.GetNumber()); } } catch { }
            }
            if (min == double.MaxValue) { min = 0; max = 0; }
            _stats[cf] = (min, max);
            return (min, max);
        }

        bool RankRule(IXLConditionalFormat cf, CellRect range, double n)
        {
            var nums = new List<double>();
            foreach (var c in _ws.Range(range.R1, range.C1, range.R2, range.C2).CellsUsed()) { try { var v = c.Value; if (v.IsNumber) nums.Add(v.GetNumber()); } catch { } }
            if (nums.Count == 0) return false;
            if (cf.ConditionalFormatType == XLConditionalFormatType.AboveAverage)
            {
                var avg = nums.Average();
                return cf.Operator == XLCFOperator.LessThan || cf.Bottom ? n < avg : n > avg;
            }
            int rank = 10;
            try { rank = int.Parse(Strip(cf.Values.Values.FirstOrDefault()?.Value ?? "10")); } catch { }
            if (cf.Percent) rank = Math.Max(1, (int)Math.Round(nums.Count * rank / 100.0));
            var sorted = cf.Bottom ? nums.OrderBy(x => x).ToList() : nums.OrderByDescending(x => x).ToList();
            var edge = sorted[Math.Min(rank, sorted.Count) - 1];
            return cf.Bottom ? n <= edge : n >= edge;
        }

        Color? Scale(IXLConditionalFormat cf, CellRect range, double n)
        {
            var (min, max) = Stats(cf, range);
            var cols = cf.Colors.Values.Select(c => XlColors.ToWpf(c, _wb) ?? Colors.White).ToList();
            if (cols.Count < 2) return null;
            double t = max > min ? (n - min) / (max - min) : 0.5;
            if (cols.Count == 2) return Lerp(cols[0], cols[1], t);
            return t < 0.5 ? Lerp(cols[0], cols[1], t * 2) : Lerp(cols[1], cols[2], (t - 0.5) * 2);
        }

        static Color Lerp(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        void Apply(IXLConditionalFormat cf, ref Result res)
        {
            try
            {
                var st = cf.Style;
                if (st.Fill.PatternType != XLFillPatternValues.None)
                {
                    var f = XlColors.ToWpf(st.Fill.BackgroundColor, _wb) ?? XlColors.ToWpf(st.Fill.PatternColor, _wb);
                    if (f.HasValue) res.Fill = f;
                }
                var fc = XlColors.ToWpf(st.Font.FontColor, _wb);
                if (fc.HasValue && fc.Value != Colors.Black) res.Font = fc;
                if (st.Font.Bold) res.Bold = true;
            }
            catch { }
        }
    }
}
