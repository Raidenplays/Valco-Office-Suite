using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ClosedXML.Excel;
using ValcoSpreadsheets.Grid;
using ValcoSpreadsheets.Model;

namespace ValcoSpreadsheets
{
    /// <summary>Charts and pictures on the sheet: insert, move/resize, the chart menu.</summary>
    public partial class MainWindow
    {
        void HookObjects()
        {
            G.ChartsSource = () => ChartStore.For(_book);
            G.ObjectMoved += (o, row, col, ox, oy, w, h) =>
            {
                if (o.Chart != null) ChangeCharts(o.Chart.Kind + " Chart", list => { var c = list.First(x => x == o.Chart); c.Row = row; c.Col = col; c.OffsetX = ox; c.OffsetY = oy; c.Width = w; c.Height = h; });
                else if (o.Picture != null)
                {
                    var pic = o.Picture; var ws = WS;
                    Change("Move Picture", () => { pic.MoveTo(ws.Cell(row, col), (int)ox, (int)oy); pic.WithSize((int)Math.Max(8, w), (int)Math.Max(8, h)); }, structure: true);
                    G.ForgetPictureCache();
                }
                G.InvalidateVisual();
            };
            G.ObjectMenuRequested += (o, p) => ShowObjectMenu(o, p);
            G.ObjectSelected += o => { StatusMsg.Text = o == null ? "" : o.Chart != null ? "Chart selected. Drag to move it, drag a handle to resize it, or right-click for chart options." : "Picture selected."; };
        }

        /// <summary>Changes the chart list with undo.</summary>
        void ChangeCharts(string name, Action<List<ChartModel>> change)
        {
            var list = ChartStore.For(_book);
            var before = list.Select(c => c.Clone()).ToList();
            change(list);
            var after = list.Select(c => c.Clone()).ToList();
            // the live objects are now the 'after' state; keep identities for the grid's selection
            _book.Undo.Record(new ChartUndo { Name = name, SheetIndex = SheetIdx, Before = before, After = after, Selection = G.View.Selection.ToList() });
            G.InvalidateVisual();
        }

        void InsertChart(ChartKind kind)
        {
            if (G.IsEditing) CommitEdit(0, 0);
            var sel = G.ActiveRect;
            var r = sel.Count == 1 ? Ops.CurrentRegion(WS, sel.R1, sel.C1) : sel;
            if (r.IsWholeColumns) r = new CellRect(1, r.C1, Math.Max(1, G.Layout.LastRow), r.C2);
            if (r.Count == 1 && WS.Cell(r.R1, r.C1).IsEmpty())
            {
                VistaDialog.Show(this, "Select the data you want to chart (for example a table with labels in the first row and column), then choose a chart type.", "Insert Chart", MessageBoxButton.OK, DlgIcon.Info);
                return;
            }
            var m = ChartIO.FromRange(WS, r, kind);
            // place it just right of the data, near the top of the view
            m.Row = Math.Max(G.View.TopRow, r.R1);
            m.Col = Math.Min(r.C2 + 2, Math.Max(G.View.LeftCol + 1, r.C2 + 2));
            ChangeCharts("Insert Chart", list => list.Add(m));
            G.Selected = new SheetCanvas.SheetObject { Chart = m };
            G.EnsureVisible(m.Row, m.Col);
            G.InvalidateVisual();
        }

        void DeleteSelectedObject()
        {
            var o = G.Selected;
            if (o == null) return;
            if (o.Chart != null) ChangeCharts("Delete Chart", list => list.Remove(o.Chart));
            else if (o.Picture != null) { var pic = o.Picture; Change("Delete Picture", () => pic.Delete(), structure: true); G.ForgetPictureCache(); }
            G.ClearObjectSelection();
        }

        void ShowObjectMenu(SheetCanvas.SheetObject o, Point p)
        {
            var m = new ContextMenu();
            if (o.Chart != null)
            {
                var ch = o.Chart;
                var type = new MenuItem { Header = "Change Chart Type", Icon = new Image { Source = UI.Img("IconColumnChart"), Width = 16, Height = 16 } };
                foreach (var (k, icon) in new[] { (ChartKind.Column, "IconColumnChart"), (ChartKind.Bar, "IconBarChart"), (ChartKind.Line, "IconLineChart"), (ChartKind.Pie, "IconPieChart"), (ChartKind.Area, "IconAreaChart"), (ChartKind.Scatter, "IconScatterChart"), (ChartKind.Doughnut, "IconDoughnutChart") })
                    type.Items.Add(RB.MI(k.ToString(), () => ChangeCharts("Change Chart Type", l => l.First(x => x == ch).Kind = k), icon, null, ch.Kind == k));
                type.Items.Add(new Separator());
                type.Items.Add(RB.MI("Stacked", () => ChangeCharts("Change Chart Type", l => { var c = l.First(x => x == ch); c.Stacked = !c.Stacked; c.Percent = false; }), null, null, ch.Stacked && !ch.Percent));
                type.Items.Add(RB.MI("100% Stacked", () => ChangeCharts("Change Chart Type", l => { var c = l.First(x => x == ch); c.Percent = !c.Percent; c.Stacked = c.Percent; }), null, null, ch.Percent));
                m.Items.Add(type);
                m.Items.Add(RB.MI("Select Data...", () => SelectChartData(ch), "IconTable"));
                m.Items.Add(RB.MI("Chart Title...", () =>
                {
                    var t = InputDialog.Ask(this, "Chart Title", "Title:", ch.Title ?? "");
                    if (t != null) ChangeCharts("Chart Title", l => l.First(x => x == ch).Title = t);
                }));
                var legend = new MenuItem { Header = "Legend" };
                foreach (var (label, pos) in new[] { ("None", (string)null), ("Right", "r"), ("Top", "t"), ("Left", "l"), ("Bottom", "b") })
                    legend.Items.Add(RB.MI(label, () => ChangeCharts("Legend", l => { var c = l.First(x => x == ch); c.ShowLegend = pos != null; if (pos != null) c.LegendPos = pos; }), null, null, pos == null ? !ch.ShowLegend : ch.ShowLegend && ch.LegendPos == pos));
                m.Items.Add(legend);
                m.Items.Add(RB.MI("Data Labels", () => ChangeCharts("Data Labels", l => { var c = l.First(x => x == ch); c.ShowDataLabels = !c.ShowDataLabels; }), null, null, ch.ShowDataLabels));
                m.Items.Add(RB.MI("Switch Row/Column", () => SwitchRowColumn(ch)));
                m.Items.Add(new Separator());
                m.Items.Add(RB.MI("Copy Chart as Picture", () => CopyChartPicture(ch)));
                m.Items.Add(RB.MI("Delete", DeleteSelectedObject, "IconDelete", "Del"));
            }
            else
            {
                m.Items.Add(RB.MI("Reset Size", () =>
                {
                    var pic = o.Picture;
                    Change("Reset Picture", () => pic.WithSize(pic.OriginalWidth, pic.OriginalHeight), structure: true);
                    G.ForgetPictureCache();
                }, "IconResetPicture"));
                m.Items.Add(RB.MI("Delete", DeleteSelectedObject, "IconDelete", "Del"));
            }
            m.PlacementTarget = G;
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.Relative;
            m.HorizontalOffset = p.X; m.VerticalOffset = p.Y;
            m.IsOpen = true;
        }

        void SelectChartData(ChartModel ch)
        {
            var current = ch.Series.FirstOrDefault()?.ValRef ?? "";
            var t = InputDialog.Ask(this, "Select Data Source", "Chart data range (for example Sheet1!$A$1:$C$6):", GuessRange(ch));
            if (string.IsNullOrWhiteSpace(t)) return;
            t = t.Trim().TrimStart('=');
            IXLWorksheet ws = WS; string addr = t;
            int bang = t.LastIndexOf('!');
            if (bang > 0) { var name = t.Substring(0, bang).Trim('\'').Replace("''", "'"); ws = _book.Wb.Worksheets.FirstOrDefault(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? WS; addr = t.Substring(bang + 1); }
            if (!A1.TryParse(addr, out var r)) { VistaDialog.Show(this, "Reference is not valid.", "Valco Spreadsheets", MessageBoxButton.OK, DlgIcon.Warning); return; }
            var fresh = ChartIO.FromRange(ws, r, ch.Kind);
            ChangeCharts("Select Data", l => l.First(x => x == ch).Series = fresh.Series);
        }

        static string GuessRange(ChartModel ch)
        {
            // the union of the series references, as one block
            var refs = ch.Series.SelectMany(s => new[] { s.NameRef, s.CatRef, s.ValRef }).Where(x => !string.IsNullOrEmpty(x)).ToList();
            if (refs.Count == 0) return "";
            string sheet = null; CellRect? all = null;
            foreach (var rf in refs)
            {
                int bang = rf.LastIndexOf('!');
                sheet ??= bang > 0 ? rf.Substring(0, bang) : null;
                if (A1.TryParse(bang > 0 ? rf.Substring(bang + 1) : rf, out var r)) all = all.HasValue ? all.Value.Union(r) : r;
            }
            return all.HasValue ? (sheet != null ? sheet + "!" : "") + "$" + A1.Col(all.Value.C1) + "$" + all.Value.R1 + ":$" + A1.Col(all.Value.C2) + "$" + all.Value.R2 : "";
        }

        void SwitchRowColumn(ChartModel ch)
        {
            var range = GuessRange(ch);
            int bang = range.LastIndexOf('!');
            var ws = WS;
            if (bang > 0) { var name = range.Substring(0, bang).Trim('\'').Replace("''", "'"); ws = _book.Wb.Worksheets.FirstOrDefault(w => w.Name == name) ?? WS; }
            if (!A1.TryParse(bang > 0 ? range.Substring(bang + 1) : range, out var r)) return;
            // rebuild with series in the other direction
            var byCols = ChartIO.FromRange(ws, r, ch.Kind);
            bool currentlyCols = ch.Series.Count > 0 && ch.Series[0].ValRef != null && ch.Series[0].ValRef.Split(':').Length == 2 && A1.TryParse(ch.Series[0].ValRef.Substring(ch.Series[0].ValRef.LastIndexOf('!') + 1), out var vr) && vr.Cols == 1;
            var swapped = Transposed(ws, r, ch.Kind, !currentlyCols);
            ChangeCharts("Switch Row/Column", l => l.First(x => x == ch).Series = swapped.Series);
        }

        static ChartModel Transposed(IXLWorksheet ws, CellRect r, ChartKind kind, bool seriesInColumns)
        {
            var m = ChartIO.FromRange(ws, r, kind);
            bool isCols = m.Series.Count > 0 && A1.TryParse(m.Series[0].ValRef.Substring(m.Series[0].ValRef.LastIndexOf('!') + 1), out var vr) && vr.Cols == 1;
            if (isCols == seriesInColumns) return m;
            // swap: every ValRef row/col pairing flips
            var n = new ChartModel { Kind = kind, Sheet = ws.Name };
            bool hasName = m.Series.All(s => s.NameRef != null), hasCat = m.Series.All(s => s.CatRef != null);
            int dr1 = r.R1 + (isCols ? (hasName ? 1 : 0) : (hasCat ? 1 : 0)), dc1 = r.C1 + (isCols ? (hasCat ? 1 : 0) : (hasName ? 1 : 0));
            string Ref(int r1, int c1, int r2, int c2) => ChartIO.Quote(ws.Name) + "!$" + A1.Col(c1) + "$" + r1 + (r1 == r2 && c1 == c2 ? "" : ":$" + A1.Col(c2) + "$" + r2);
            bool header = dr1 > r.R1, cats = dc1 > r.C1;
            if (seriesInColumns)
                for (int c = dc1; c <= r.C2; c++) n.Series.Add(new ChartSeries { NameRef = header ? Ref(r.R1, c, r.R1, c) : null, Name = header ? null : "Series" + (c - dc1 + 1), CatRef = cats ? Ref(dr1, r.C1, r.R2, r.C1) : null, ValRef = Ref(dr1, c, r.R2, c) });
            else
                for (int row = dr1; row <= r.R2; row++) n.Series.Add(new ChartSeries { NameRef = cats ? Ref(row, r.C1, row, r.C1) : null, Name = cats ? null : "Series" + (row - dr1 + 1), CatRef = header ? Ref(r.R1, dc1, r.R1, r.C2) : null, ValRef = Ref(row, dc1, row, r.C2) });
            return n;
        }

        void CopyChartPicture(ChartModel ch)
        {
            var dv = new System.Windows.Media.DrawingVisual();
            using (var dc = dv.RenderOpen()) ChartRenderer.Draw(dc, ch, _book.Wb, new Rect(0, 0, ch.Width, ch.Height));
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(ch.Width), (int)Math.Ceiling(ch.Height), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bmp.Render(dv);
            try { Clipboard.SetImage(bmp); } catch { }
        }
    }
}
