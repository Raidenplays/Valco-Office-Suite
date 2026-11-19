using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ClosedXML.Excel;
using ValcoSpreadsheets.Model;

namespace ValcoSpreadsheets.Grid
{
    /// <summary>Sizes along one axis (rows or columns) in unzoomed pixels: a default size plus exceptions.</summary>
    public class Axis
    {
        public double Default = 20;
        readonly Dictionary<int, double> _custom = new();
        int[] _keys = Array.Empty<int>();
        double[] _cum = Array.Empty<double>();
        bool _dirty;

        public void Reset(double def) { Default = def; _custom.Clear(); _dirty = true; }
        public void Set(int i, double px) { if (Math.Abs(px - Default) < 0.01) _custom.Remove(i); else _custom[i] = px; _dirty = true; }
        public double Size(int i) => _custom.TryGetValue(i, out var s) ? s : Default;
        public bool IsHidden(int i) => _custom.TryGetValue(i, out var s) && s <= 0;

        void Build()
        {
            _keys = _custom.Keys.OrderBy(k => k).ToArray();
            _cum = new double[_keys.Length];
            double run = 0;
            for (int k = 0; k < _keys.Length; k++) { run += _custom[_keys[k]] - Default; _cum[k] = run; }
            _dirty = false;
        }

        /// <summary>Offset of the start of index i (1-based) from the start of index 1.</summary>
        public double Pos(int i)
        {
            if (_dirty) Build();
            int k = Array.BinarySearch(_keys, i);
            if (k < 0) k = ~k; // number of keys < i
            return (i - 1) * Default + (k > 0 ? _cum[k - 1] : 0);
        }

        /// <summary>Index whose span contains the offset.</summary>
        public int IndexAt(double offset, int max)
        {
            int lo = 1, hi = max;
            while (lo < hi)
            {
                int mid = lo + (hi - lo + 1) / 2;
                if (Pos(mid) <= offset) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        /// <summary>Next visible index at or after i (skips hidden rows/columns).</summary>
        public int NextVisible(int i, int dir, int max)
        {
            while (i >= 1 && i <= max && IsHidden(i)) i += dir;
            return Math.Clamp(i, 1, max);
        }
    }

    /// <summary>Per-sheet layout: column widths, row heights, merges and the row/column styles of empty cells.</summary>
    public class SheetLayout
    {
        public readonly Axis Cols = new Axis(), Rows = new Axis();
        public List<CellRect> Merges = new();
        public Dictionary<int, IXLStyle> ColStyles = new(), RowStyles = new();
        public int LastRow = 1, LastCol = 1;

        public void Rebuild(IXLWorksheet ws)
        {
            Cols.Reset(Units.ColWidthToPx(ws.ColumnWidth));
            Rows.Reset(Units.PtToPx(ws.RowHeight));
            ColStyles.Clear(); RowStyles.Clear();
            foreach (var c in ws.Columns())
            {
                int i = c.ColumnNumber();
                Cols.Set(i, c.IsHidden ? 0 : Units.ColWidthToPx(c.Width));
                if (HasLook(c.Style)) ColStyles[i] = c.Style;
            }
            foreach (var r in ws.Rows())
            {
                int i = r.RowNumber();
                Rows.Set(i, r.IsHidden ? 0 : Units.PtToPx(r.Height));
                if (HasLook(r.Style)) RowStyles[i] = r.Style;
            }
            Merges = ws.MergedRanges.Select(m => { var a = m.RangeAddress; return new CellRect(a.FirstAddress.RowNumber, a.FirstAddress.ColumnNumber, a.LastAddress.RowNumber, a.LastAddress.ColumnNumber); }).ToList();
            LastRow = Ops.LastRow(ws);
            LastCol = Ops.LastCol(ws);
        }

        static bool HasLook(IXLStyle s)
        {
            try { return s.Fill.PatternType != XLFillPatternValues.None || s.Border.BottomBorder != XLBorderStyleValues.None || s.Border.RightBorder != XLBorderStyleValues.None; }
            catch { return false; }
        }

        public CellRect? MergeAt(int r, int c)
        {
            foreach (var m in Merges) if (m.Contains(r, c)) return m;
            return null;
        }

        /// <summary>Grows a rectangle so it doesn't cut through merged cells.</summary>
        public CellRect ExpandToMerges(CellRect rect)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var m in Merges)
                    if (rect.Intersects(m))
                    {
                        var u = rect.Union(m);
                        if (u.R1 != rect.R1 || u.R2 != rect.R2 || u.C1 != rect.C1 || u.C2 != rect.C2) { rect = u; changed = true; }
                    }
            }
            return rect;
        }
    }

    /// <summary>Colours from ClosedXML (theme, indexed, ARGB) as WPF colours.</summary>
    public static class XlColors
    {
        static readonly Dictionary<XLThemeColor, Color> Office2007 = new()
        {
            [XLThemeColor.Background1] = Colors.White, [XLThemeColor.Text1] = Colors.Black,
            [XLThemeColor.Background2] = Color.FromRgb(0xEE, 0xEC, 0xE1), [XLThemeColor.Text2] = Color.FromRgb(0x1F, 0x49, 0x7D),
            [XLThemeColor.Accent1] = Color.FromRgb(0x4F, 0x81, 0xBD), [XLThemeColor.Accent2] = Color.FromRgb(0xC0, 0x50, 0x4D),
            [XLThemeColor.Accent3] = Color.FromRgb(0x9B, 0xBB, 0x59), [XLThemeColor.Accent4] = Color.FromRgb(0x80, 0x64, 0xA2),
            [XLThemeColor.Accent5] = Color.FromRgb(0x4B, 0xAC, 0xC6), [XLThemeColor.Accent6] = Color.FromRgb(0xF7, 0x96, 0x46),
            [XLThemeColor.Hyperlink] = Color.FromRgb(0x00, 0x00, 0xFF), [XLThemeColor.FollowedHyperlink] = Color.FromRgb(0x80, 0x00, 0x80),
        };

        public static Color? ToWpf(XLColor c, XLWorkbook wb, bool isFont = false)
        {
            if (c == null || !c.HasValue) return null;
            try
            {
                switch (c.ColorType)
                {
                    case XLColorType.Theme:
                        Color baseC;
                        try { var t = wb.Theme.ResolveThemeColor(c.ThemeColor).Color; baseC = Color.FromArgb(255, t.R, t.G, t.B); }
                        catch { baseC = Office2007.TryGetValue(c.ThemeColor, out var oc) ? oc : Colors.Black; }
                        return Tint(baseC, c.ThemeTint);
                    case XLColorType.Indexed:
                        if (c.Indexed == 64) return isFont ? Colors.Black : (Color?)null; // system foreground / background
                        if (c.Indexed == 65) return null;
                        goto default;
                    default:
                        var d = c.Color;
                        if (d.A == 0 && d.R == 0 && d.G == 0 && d.B == 0) return isFont ? Colors.Black : null;
                        return Color.FromArgb(255, d.R, d.G, d.B);
                }
            }
            catch { return isFont ? Colors.Black : null; }
        }

        static Color Tint(Color c, double tint)
        {
            if (Math.Abs(tint) < 0.0001) return c;
            RgbToHsl(c, out var h, out var s, out var l);
            l = tint < 0 ? l * (1 + tint) : l * (1 - tint) + (1 - (1 - tint));
            return HslToRgb(h, s, Math.Clamp(l, 0, 1));
        }

        static void RgbToHsl(Color c, out double h, out double s, out double l)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2;
            if (max == min) { h = s = 0; return; }
            double d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h /= 6;
        }

        static Color HslToRgb(double h, double s, double l)
        {
            double r, g, b;
            if (s == 0) r = g = b = l;
            else
            {
                double Hue(double p, double q, double t)
                {
                    if (t < 0) t += 1; if (t > 1) t -= 1;
                    if (t < 1.0 / 6) return p + (q - p) * 6 * t;
                    if (t < 0.5) return q;
                    if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
                    return p;
                }
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
                r = Hue(p, q, h + 1.0 / 3); g = Hue(p, q, h); b = Hue(p, q, h - 1.0 / 3);
            }
            return Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
        }

        public static XLColor FromWpf(Color c) => XLColor.FromArgb(c.R, c.G, c.B);

        /// <summary>The Office 2007 theme colour grid (10 columns x 6 rows) and standard colours, as the colour pickers show them.</summary>
        public static readonly Color[] ThemeRow =
        {
            Colors.White, Colors.Black, Color.FromRgb(0xEE, 0xEC, 0xE1), Color.FromRgb(0x1F, 0x49, 0x7D), Color.FromRgb(0x4F, 0x81, 0xBD),
            Color.FromRgb(0xC0, 0x50, 0x4D), Color.FromRgb(0x9B, 0xBB, 0x59), Color.FromRgb(0x80, 0x64, 0xA2), Color.FromRgb(0x4B, 0xAC, 0xC6), Color.FromRgb(0xF7, 0x96, 0x46)
        };
        public static readonly Color[] Standard =
        {
            Color.FromRgb(0xC0, 0x00, 0x00), Color.FromRgb(0xFF, 0x00, 0x00), Color.FromRgb(0xFF, 0xC0, 0x00), Color.FromRgb(0xFF, 0xFF, 0x00), Color.FromRgb(0x92, 0xD0, 0x50),
            Color.FromRgb(0x00, 0xB0, 0x50), Color.FromRgb(0x00, 0xB0, 0xF0), Color.FromRgb(0x00, 0x70, 0xC0), Color.FromRgb(0x00, 0x20, 0x60), Color.FromRgb(0x70, 0x30, 0xA0)
        };
        public static Color Shade(Color c, double tint) => Tint(c, tint);
    }
}
