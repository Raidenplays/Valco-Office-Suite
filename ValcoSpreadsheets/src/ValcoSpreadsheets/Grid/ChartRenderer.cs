using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ClosedXML.Excel;
using ValcoSpreadsheets.Model;

namespace ValcoSpreadsheets
{
    /// <summary>Draws a chart in the Office 2007 default style (white chart area, grey gridlines, glossy-free flat series colours).</summary>
    public static class ChartRenderer
    {
        static readonly Typeface Face = new Typeface("Calibri");
        static readonly Typeface Bold = new Typeface(new FontFamily("Calibri"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        static Color Pal(int i) { var h = ChartPalette.Hex[i % ChartPalette.Hex.Length]; return Color.FromRgb(Convert.ToByte(h.Substring(0, 2), 16), Convert.ToByte(h.Substring(2, 2), 16), Convert.ToByte(h.Substring(4, 2), 16)); }

        static FormattedText T(string s, double size, Brush b, bool bold = false) =>
            new FormattedText(s ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, bold ? Bold : Face, size, b, 1.0);

        public static void Draw(DrawingContext dc, ChartModel m, XLWorkbook wb, Rect r)
        {
            dc.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(0x86, 0x86, 0x86)), 1), r);
            if (r.Width < 40 || r.Height < 40) return;
            dc.PushClip(new RectangleGeometry(r));
            ChartData data;
            try { data = ChartData.Read(m, wb); } catch { data = new ChartData(); }
            var inner = new Rect(r.X + 8, r.Y + 8, r.Width - 16, r.Height - 16);
            // title
            string title = m.Title;
            if (string.IsNullOrEmpty(title) && data.Series.Count == 1 && (m.Kind == ChartKind.Pie || m.Kind == ChartKind.Doughnut)) title = data.Series[0].Name;
            if (!string.IsNullOrEmpty(title))
            {
                var tt = T(title, 18, Brushes.Black, true);
                tt.MaxTextWidth = Math.Max(10, inner.Width); tt.TextAlignment = TextAlignment.Center; tt.MaxLineCount = 2;
                dc.DrawText(tt, new Point(inner.X, inner.Y));
                inner = new Rect(inner.X, inner.Y + tt.Height + 6, inner.Width, Math.Max(10, inner.Height - tt.Height - 6));
            }
            // legend
            bool pie = m.Kind == ChartKind.Pie || m.Kind == ChartKind.Doughnut;
            var legendItems = pie ? data.Categories.Select((c, i) => (c, Pal(i))).ToList() : data.Series.Select((s, i) => (s.Name, Pal(i))).ToList();
            if (m.ShowLegend && legendItems.Count > 0)
            {
                bool side = m.LegendPos == "r" || m.LegendPos == "l";
                var texts = legendItems.Select(l => T(l.Item1, 12, Brushes.Black)).ToList();
                if (side)
                {
                    double w = Math.Min(inner.Width * 0.4, texts.Max(t => t.Width) + 22);
                    double h = texts.Count * 18;
                    double lx = m.LegendPos == "r" ? inner.Right - w : inner.X, ly = inner.Y + Math.Max(0, (inner.Height - h) / 2);
                    for (int i = 0; i < texts.Count && ly + i * 18 < inner.Bottom; i++)
                    {
                        dc.DrawRectangle(new SolidColorBrush(legendItems[i].Item2), null, new Rect(lx, ly + i * 18 + 4, 8, 8));
                        texts[i].MaxTextWidth = Math.Max(10, w - 14); texts[i].Trimming = TextTrimming.CharacterEllipsis; texts[i].MaxLineCount = 1;
                        dc.DrawText(texts[i], new Point(lx + 12, ly + i * 18));
                    }
                    inner = m.LegendPos == "r" ? new Rect(inner.X, inner.Y, Math.Max(10, inner.Width - w - 8), inner.Height) : new Rect(inner.X + w + 8, inner.Y, Math.Max(10, inner.Width - w - 8), inner.Height);
                }
                else
                {
                    double total = texts.Sum(t => t.Width + 26);
                    double lx = inner.X + Math.Max(0, (inner.Width - total) / 2), ly = m.LegendPos == "t" ? inner.Y : inner.Bottom - 16;
                    for (int i = 0; i < texts.Count; i++)
                    {
                        dc.DrawRectangle(new SolidColorBrush(legendItems[i].Item2), null, new Rect(lx, ly + 4, 8, 8));
                        dc.DrawText(texts[i], new Point(lx + 12, ly));
                        lx += texts[i].Width + 26;
                    }
                    inner = m.LegendPos == "t" ? new Rect(inner.X, inner.Y + 22, inner.Width, Math.Max(10, inner.Height - 22)) : new Rect(inner.X, inner.Y, inner.Width, Math.Max(10, inner.Height - 22));
                }
            }
            if (data.Series.Count == 0 || data.Series.All(s => s.Values.All(v => !v.HasValue)))
            {
                dc.Pop();
                return;
            }
            if (pie) DrawPie(dc, m, data, inner);
            else DrawAxes(dc, m, data, inner);
            dc.Pop();
        }

        static void DrawPie(DrawingContext dc, ChartModel m, ChartData d, Rect r)
        {
            var vals = d.Series[0].Values.Select(v => Math.Max(0, v ?? 0)).ToList();
            double total = vals.Sum();
            if (total <= 0) return;
            double rad = Math.Min(r.Width, r.Height) / 2 - 4;
            var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
            double a = -90;
            var white = new Pen(Brushes.White, 1.5);
            for (int i = 0; i < vals.Count; i++)
            {
                double sweep = vals[i] / total * 360;
                if (sweep <= 0) continue;
                var g = new StreamGeometry();
                using (var gc = g.Open())
                {
                    Point P(double deg, double rr) => new Point(c.X + rr * Math.Cos(deg * Math.PI / 180), c.Y + rr * Math.Sin(deg * Math.PI / 180));
                    double hole = m.Kind == ChartKind.Doughnut ? rad * 0.5 : 0;
                    if (sweep >= 359.99)
                    {
                        gc.BeginFigure(P(a, rad), true, true);
                        gc.ArcTo(P(a + 180, rad), new Size(rad, rad), 0, false, SweepDirection.Clockwise, true, false);
                        gc.ArcTo(P(a + 359.9, rad), new Size(rad, rad), 0, false, SweepDirection.Clockwise, true, false);
                    }
                    else if (hole > 0)
                    {
                        gc.BeginFigure(P(a, hole), true, true);
                        gc.LineTo(P(a, rad), true, false);
                        gc.ArcTo(P(a + sweep, rad), new Size(rad, rad), 0, sweep > 180, SweepDirection.Clockwise, true, false);
                        gc.LineTo(P(a + sweep, hole), true, false);
                        gc.ArcTo(P(a, hole), new Size(hole, hole), 0, sweep > 180, SweepDirection.Counterclockwise, true, false);
                    }
                    else
                    {
                        gc.BeginFigure(c, true, true);
                        gc.LineTo(P(a, rad), true, false);
                        gc.ArcTo(P(a + sweep, rad), new Size(rad, rad), 0, sweep > 180, SweepDirection.Clockwise, true, false);
                    }
                }
                dc.DrawGeometry(new SolidColorBrush(Pal(i)), white, g);
                if (m.ShowDataLabels)
                {
                    double mid = (a + sweep / 2) * Math.PI / 180;
                    var t = T((vals[i] / total).ToString("0%", CultureInfo.CurrentCulture), 11, Brushes.White, true);
                    dc.DrawText(t, new Point(c.X + rad * 0.65 * Math.Cos(mid) - t.Width / 2, c.Y + rad * 0.65 * Math.Sin(mid) - t.Height / 2));
                }
                a += sweep;
            }
        }

        static void DrawAxes(DrawingContext dc, ChartModel m, ChartData d, Rect r)
        {
            bool bar = m.Kind == ChartKind.Bar;
            bool scatter = m.Kind == ChartKind.Scatter;
            int n = d.Series.Max(s => s.Values.Count);
            // value range
            double min = 0, max = 0;
            if (m.Stacked && !m.Percent)
            {
                for (int i = 0; i < n; i++)
                {
                    double pos = 0, neg = 0;
                    foreach (var s in d.Series) { var v = i < s.Values.Count ? s.Values[i] ?? 0 : 0; if (v >= 0) pos += v; else neg += v; }
                    max = Math.Max(max, pos); min = Math.Min(min, neg);
                }
            }
            else if (m.Percent) { min = 0; max = 1; }
            else
            {
                var all = d.Series.SelectMany(s => s.Values).Where(v => v.HasValue).Select(v => v.Value).ToList();
                if (all.Count > 0) { min = Math.Min(0, all.Min()); max = Math.Max(0, all.Max()); }
                if (scatter && all.Count > 0 && all.Min() > 0) min = 0;
            }
            if (max == min) max = min + 1;
            var (lo, hi, step) = NiceScale(min, max);
            var labelBrush = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40));
            string Fmt(double v) => m.Percent ? v.ToString("0%", CultureInfo.CurrentCulture) : Math.Abs(step) >= 1 ? v.ToString("#,##0", CultureInfo.CurrentCulture) : v.ToString("0.###", CultureInfo.CurrentCulture);
            // label widths
            var valLabels = new List<(double V, FormattedText T)>();
            for (double v = lo; v <= hi + step / 2; v += step) valLabels.Add((v, T(Fmt(v), 11, labelBrush)));
            double valW = valLabels.Max(x => x.T.Width) + 6;
            // category labels
            List<string> cats;
            double xmin = 0, xmax = 0, xstep = 1;
            if (scatter)
            {
                var xs = d.Series.SelectMany(s => (s.X ?? new List<double?>()).Where(v => v.HasValue).Select(v => v.Value)).ToList();
                if (xs.Count == 0) xs = Enumerable.Range(1, n).Select(i => (double)i).ToList();
                (xmin, xmax, xstep) = NiceScale(Math.Min(0, xs.Min()), xs.Max());
                cats = new List<string>();
                for (double v = xmin; v <= xmax + xstep / 2; v += xstep) cats.Add(v.ToString("0.###", CultureInfo.CurrentCulture));
            }
            else cats = d.Categories.Take(n).ToList();
            double catH = 18;
            Rect plot = bar
                ? new Rect(r.X + Math.Min(r.Width * 0.35, cats.Select(c => T(c, 11, labelBrush).Width).DefaultIfEmpty(0).Max() + 8), r.Y, 0, 0)
                : new Rect(r.X + valW, r.Y + 4, 0, 0);
            plot.Width = Math.Max(10, r.Right - plot.X - 6);
            plot.Height = Math.Max(10, r.Bottom - plot.Y - catH - 2);
            var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9)), 1);
            var axisPen = new Pen(new SolidColorBrush(Color.FromRgb(0x86, 0x86, 0x86)), 1);
            double V(double v) => bar ? plot.X + (v - lo) / (hi - lo) * plot.Width : plot.Bottom - (v - lo) / (hi - lo) * plot.Height;
            // gridlines + value labels
            foreach (var (v, t) in valLabels)
            {
                double p = V(v);
                if (bar) { dc.DrawLine(gridPen, new Point(p, plot.Y), new Point(p, plot.Bottom)); dc.DrawText(t, new Point(p - t.Width / 2, plot.Bottom + 2)); }
                else { dc.DrawLine(gridPen, new Point(plot.X, Math.Round(p) + 0.5), new Point(plot.Right, Math.Round(p) + 0.5)); dc.DrawText(t, new Point(plot.X - t.Width - 4, p - t.Height / 2)); }
            }
            double zero = V(Math.Clamp(0, lo, hi));
            if (bar) dc.DrawLine(axisPen, new Point(zero, plot.Y), new Point(zero, plot.Bottom));
            else dc.DrawLine(axisPen, new Point(plot.X, Math.Round(zero) + 0.5), new Point(plot.Right, Math.Round(zero) + 0.5));
            dc.DrawLine(axisPen, bar ? new Point(plot.X, plot.Y) : new Point(plot.X, plot.Y), bar ? new Point(plot.X, plot.Bottom) : new Point(plot.X, plot.Bottom));

            if (scatter)
            {
                double X(double v) => plot.X + (v - xmin) / (xmax - xmin) * plot.Width;
                for (double v = xmin; v <= xmax + xstep / 2; v += xstep)
                {
                    var t = T(v.ToString("0.###", CultureInfo.CurrentCulture), 11, labelBrush);
                    dc.DrawText(t, new Point(X(v) - t.Width / 2, plot.Bottom + 2));
                }
                for (int si = 0; si < d.Series.Count; si++)
                {
                    var s = d.Series[si];
                    var brush = new SolidColorBrush(Pal(si));
                    for (int i = 0; i < s.Values.Count; i++)
                    {
                        if (!s.Values[i].HasValue) continue;
                        double xv = s.X != null && i < s.X.Count && s.X[i].HasValue ? s.X[i].Value : i + 1;
                        dc.DrawEllipse(brush, new Pen(Brushes.White, 0.5), new Point(X(xv), V(s.Values[i].Value)), 3.5, 3.5);
                    }
                }
                return;
            }

            // category labels
            double band = (bar ? plot.Height : plot.Width) / Math.Max(1, n);
            int every = Math.Max(1, (int)Math.Ceiling(n / Math.Max(1.0, (bar ? plot.Height / 16 : plot.Width / 46))));
            for (int i = 0; i < n; i += every)
            {
                var t = T(i < cats.Count ? cats[i] : (i + 1).ToString(), 11, labelBrush);
                t.MaxLineCount = 1; t.Trimming = TextTrimming.CharacterEllipsis;
                if (bar) { t.MaxTextWidth = Math.Max(10, plot.X - r.X - 4); dc.DrawText(t, new Point(plot.X - Math.Min(t.Width, t.MaxTextWidth) - 4, plot.Bottom - (i + 0.5) * band - t.Height / 2)); }
                else { t.MaxTextWidth = Math.Max(10, band * every); double cx = m.Kind == ChartKind.Area || m.Kind == ChartKind.Line ? plot.X + (i + 0.5) * band : plot.X + (i + 0.5) * band; dc.DrawText(t, new Point(cx - Math.Min(t.Width, t.MaxTextWidth) / 2, plot.Bottom + 3)); }
            }

            int sc = d.Series.Count;
            switch (m.Kind)
            {
                case ChartKind.Column:
                case ChartKind.Bar:
                    {
                        double gap = band * 0.3, inner = band - gap;
                        double w = m.Stacked ? inner : inner / sc;
                        var pos = new double[n]; var neg = new double[n];
                        for (int si = 0; si < sc; si++)
                        {
                            var s = d.Series[si];
                            var brush = new LinearGradientBrush(Lighter(Pal(si)), Pal(si), bar ? 90 : 0);
                            for (int i = 0; i < s.Values.Count; i++)
                            {
                                double v = s.Values[i] ?? 0;
                                if (m.Percent) { double tot = d.Series.Sum(x => Math.Abs(i < x.Values.Count ? x.Values[i] ?? 0 : 0)); v = tot == 0 ? 0 : v / tot; }
                                double start = m.Stacked ? (v >= 0 ? pos[i] : neg[i]) : 0, end = start + v;
                                if (m.Stacked) { if (v >= 0) pos[i] = end; else neg[i] = end; }
                                double off = gap / 2 + (m.Stacked ? 0 : w * si);
                                Rect rc = bar
                                    ? new Rect(Math.Min(V(start), V(end)), plot.Bottom - (i + 1) * band + off, Math.Abs(V(end) - V(start)), w)
                                    : new Rect(plot.X + i * band + off, Math.Min(V(start), V(end)), w, Math.Abs(V(end) - V(start)));
                                dc.DrawRectangle(brush, null, rc);
                                if (m.ShowDataLabels && s.Values[i].HasValue)
                                {
                                    var t = T(Fmt(s.Values[i].Value), 10, labelBrush);
                                    dc.DrawText(t, bar ? new Point(rc.Right + 2, rc.Y + (rc.Height - t.Height) / 2) : new Point(rc.X + (rc.Width - t.Width) / 2, rc.Y - t.Height));
                                }
                            }
                        }
                        break;
                    }
                case ChartKind.Line:
                case ChartKind.Area:
                    {
                        var acc = new double[n];
                        for (int si = 0; si < sc; si++)
                        {
                            var s = d.Series[si];
                            var pts = new List<Point?>();
                            for (int i = 0; i < n; i++)
                            {
                                if (i >= s.Values.Count || !s.Values[i].HasValue) { pts.Add(null); continue; }
                                double v = s.Values[i].Value;
                                if (m.Stacked) { acc[i] += v; v = acc[i]; }
                                pts.Add(new Point(plot.X + (i + 0.5) * band, V(v)));
                            }
                            var col = Pal(si);
                            if (m.Kind == ChartKind.Area)
                            {
                                var g = new StreamGeometry();
                                var valid = pts.Where(p => p.HasValue).Select(p => p.Value).ToList();
                                if (valid.Count < 2) continue;
                                using (var gc = g.Open())
                                {
                                    gc.BeginFigure(new Point(valid[0].X, zero), true, true);
                                    foreach (var p in valid) gc.LineTo(p, true, false);
                                    gc.LineTo(new Point(valid[^1].X, zero), true, false);
                                }
                                dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(m.Stacked ? (byte)255 : (byte)200, col.R, col.G, col.B)), null, g);
                            }
                            else
                            {
                                var pen = new Pen(new SolidColorBrush(col), 2.25) { LineJoin = PenLineJoin.Round };
                                for (int i = 1; i < pts.Count; i++) if (pts[i].HasValue && pts[i - 1].HasValue) dc.DrawLine(pen, pts[i - 1].Value, pts[i].Value);
                                foreach (var p in pts.Where(x => x.HasValue)) dc.DrawEllipse(new SolidColorBrush(col), new Pen(Brushes.White, 0.8), p.Value, 3.5, 3.5);
                            }
                            if (m.ShowDataLabels)
                                for (int i = 0; i < pts.Count; i++)
                                    if (pts[i].HasValue) { var t = T(Fmt(s.Values[i].Value), 10, labelBrush); dc.DrawText(t, new Point(pts[i].Value.X - t.Width / 2, pts[i].Value.Y - t.Height - 3)); }
                        }
                        break;
                    }
            }
        }

        static Color Lighter(Color c) => Color.FromRgb((byte)(c.R + (255 - c.R) * 0.25), (byte)(c.G + (255 - c.G) * 0.25), (byte)(c.B + (255 - c.B) * 0.25));

        /// <summary>Round axis bounds and a step like Excel's automatic scale.</summary>
        public static (double Lo, double Hi, double Step) NiceScale(double min, double max)
        {
            double range = max - min;
            if (range <= 0) range = Math.Abs(max) > 0 ? Math.Abs(max) : 1;
            double raw = range / 6;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double norm = raw / mag;
            double step = (norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 2.5 ? 2.5 : norm <= 5 ? 5 : 10) * mag;
            double lo = Math.Floor(min / step) * step, hi = Math.Ceiling(max / step) * step;
            if (hi == lo) hi = lo + step;
            return (lo, hi, step);
        }
    }
}
