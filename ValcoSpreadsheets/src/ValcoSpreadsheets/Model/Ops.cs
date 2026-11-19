using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace ValcoSpreadsheets.Model
{
    /// <summary>Worksheet operations that don't need the UI: fill series, clipboard text, find, AutoSum, sorting.</summary>
    public static class Ops
    {
        // =========================================================== fill handle / Fill Down / Series
        static readonly string[][] Lists =
        {
            CultureInfo.CurrentCulture.DateTimeFormat.MonthNames.Where(s => s.Length > 0).ToArray(),
            CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedMonthNames.Where(s => s.Length > 0).ToArray(),
            CultureInfo.CurrentCulture.DateTimeFormat.DayNames,
            CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames,
            new[] { "Q1", "Q2", "Q3", "Q4" },
        };
        static readonly Regex TrailingNumber = new(@"^(.*?)(\d+)(\D*)$");

        /// <summary>
        /// Fills <paramref name="target"/> (which contains <paramref name="source"/> and extends it in one direction)
        /// the way Excel's fill handle does: numbers/dates continue a series, text with a number counts up,
        /// month and day names continue, formulas are copied with relative references adjusted.
        /// <paramref name="copyOnly"/> copies the cells instead (Ctrl while dragging).
        /// </summary>
        public static void Fill(IXLWorksheet ws, CellRect source, CellRect target, bool copyOnly = false)
        {
            bool vertical = target.Rows != source.Rows;
            bool forward = vertical ? target.R2 > source.R2 : target.C2 > source.C2;
            int lanes = vertical ? source.Cols : source.Rows;
            int n = vertical ? source.Rows : source.Cols;
            int extra = vertical ? target.Rows - source.Rows : target.Cols - source.Cols;
            for (int lane = 0; lane < lanes; lane++)
            {
                IXLCell Src(int i)
                {
                    // i = 0..n-1 in fill order
                    int idx = forward ? i : n - 1 - i;
                    return vertical ? ws.Cell(source.R1 + idx, source.C1 + lane) : ws.Cell(source.R1 + lane, source.C1 + idx);
                }
                IXLCell Dst(int k)
                {
                    // k = 0..extra-1, moving away from the source
                    return vertical
                        ? ws.Cell(forward ? source.R2 + 1 + k : source.R1 - 1 - k, source.C1 + lane)
                        : ws.Cell(source.R1 + lane, forward ? source.C2 + 1 + k : source.C1 - 1 - k);
                }
                var src = Enumerable.Range(0, n).Select(Src).ToList();
                var series = copyOnly ? null : SeriesFor(src);
                for (int k = 0; k < extra; k++)
                {
                    var s = src[k % n];
                    var d = Dst(k);
                    if (series != null)
                    {
                        d.Style = s.Style;
                        d.Value = series(n + k);
                    }
                    else s.CopyTo(d);
                }
            }
        }

        /// <summary>Returns a function giving the value at position i of the series the cells start, or null to copy.</summary>
        static Func<int, XLCellValue> SeriesFor(List<IXLCell> src)
        {
            if (src.Any(c => c.HasFormula)) return null;
            var vals = src.Select(c => c.Value).ToList();
            int n = vals.Count;
            if (vals.All(v => v.IsNumber))
            {
                if (n == 1) return null; // a single number is copied (Ctrl+drag makes a series in Excel; Fill > Series does it here)
                var (a, b) = Trend(vals.Select(v => v.GetNumber()).ToList());
                return i => a + b * i;
            }
            if (vals.All(v => v.IsDateTime))
            {
                var days = vals.Select(v => v.GetDateTime().ToOADate()).ToList();
                if (n == 1) return i => DateTime.FromOADate(days[0] + i);
                // whole months apart -> keep counting months
                var d0 = vals[0].GetDateTime(); var d1 = vals[1].GetDateTime();
                int months = (d1.Year - d0.Year) * 12 + d1.Month - d0.Month;
                if (months != 0 && d0.Day == d1.Day) return i => d0.AddMonths(months * i);
                var (a, b) = Trend(days);
                return i => DateTime.FromOADate(Math.Round(a + b * i, 6));
            }
            if (vals.All(v => v.IsText))
            {
                var texts = vals.Select(v => v.GetText()).ToList();
                foreach (var list in Lists)
                {
                    var idx = texts.Select(t => Array.FindIndex(list, x => x.Equals(t, StringComparison.CurrentCultureIgnoreCase))).ToList();
                    if (idx.All(i => i >= 0))
                    {
                        int step = n > 1 ? idx[1] - idx[0] : 1;
                        if (step == 0) step = 1;
                        bool upper = texts[0].ToUpperInvariant() == texts[0] && texts[0].Length > 1;
                        return i => { var s = list[((idx[0] + step * i) % list.Length + list.Length) % list.Length]; return upper ? s.ToUpperInvariant() : s; };
                    }
                }
                var ms = texts.Select(t => TrailingNumber.Match(t)).ToList();
                if (ms.All(m => m.Success) && ms.Select(m => m.Groups[1].Value + "|" + m.Groups[3].Value).Distinct().Count() == 1)
                {
                    var nums = ms.Select(m => long.Parse(m.Groups[2].Value)).ToList();
                    long step = n > 1 ? nums[1] - nums[0] : 1;
                    var prefix = ms[0].Groups[1].Value; var suffix = ms[0].Groups[3].Value;
                    int width = ms[0].Groups[2].Value.Length;
                    return i => prefix + Math.Max(0, nums[0] + step * i).ToString(new string('0', ms[0].Groups[2].Value.StartsWith("0") ? width : 1)) + suffix;
                }
            }
            return null;
        }

        static (double A, double B) Trend(List<double> y)
        {
            int n = y.Count;
            double sx = 0, sy = 0, sxx = 0, sxy = 0;
            for (int i = 0; i < n; i++) { sx += i; sy += y[i]; sxx += i * i; sxy += i * y[i]; }
            double b = (n * sxy - sx * sy) / (n * sxx - sx * sx);
            double a = (sy - b * sx) / n;
            return (a, b);
        }

        /// <summary>Home → Fill → Series: a linear or growth series with a step and optional stop value.</summary>
        public static void FillSeries(IXLWorksheet ws, CellRect rect, bool columns, bool growth, double step, double? stop, bool dates, string dateUnit)
        {
            int lanes = columns ? rect.Cols : rect.Rows, len = columns ? rect.Rows : rect.Cols;
            for (int lane = 0; lane < lanes; lane++)
            {
                IXLCell At(int i) => columns ? ws.Cell(rect.R1 + i, rect.C1 + lane) : ws.Cell(rect.R1 + lane, rect.C1 + i);
                var first = At(0);
                if (dates && first.Value.IsDateTime)
                {
                    var d = first.Value.GetDateTime();
                    for (int i = 1; i < len; i++)
                    {
                        d = dateUnit switch { "Month" => d.AddMonths((int)step), "Year" => d.AddYears((int)step), "Weekday" => NextWeekday(d, (int)step), _ => d.AddDays(step) };
                        if (stop.HasValue && d.ToOADate() > stop.Value) break;
                        At(i).Style = first.Style; At(i).Value = d;
                    }
                    continue;
                }
                if (!first.Value.IsNumber) continue;
                double v = first.Value.GetNumber();
                for (int i = 1; i < len; i++)
                {
                    v = growth ? v * step : v + step;
                    if (stop.HasValue && (step >= 0 || growth ? v > stop.Value : v < stop.Value)) break;
                    At(i).Style = first.Style; At(i).Value = v;
                }
            }
        }

        static DateTime NextWeekday(DateTime d, int step)
        {
            for (int i = 0; i < Math.Max(1, step); i++)
            {
                d = d.AddDays(1);
                while (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) d = d.AddDays(1);
            }
            return d;
        }

        // =========================================================== clipboard text
        public static string ToText(IXLWorksheet ws, CellRect r)
        {
            var sb = new StringBuilder();
            int r2 = Math.Min(r.R2, LastRow(ws)), c2 = Math.Min(r.C2, LastCol(ws));
            for (int row = r.R1; row <= Math.Max(r.R1, r2); row++)
            {
                for (int col = r.C1; col <= Math.Max(r.C1, c2); col++)
                {
                    if (col > r.C1) sb.Append('\t');
                    string s = "";
                    try { var c = ws.Cell(row, col); if (!c.IsEmpty()) s = c.GetFormattedString(); } catch { }
                    if (s.Contains('\t') || s.Contains('\n') || s.Contains('"')) s = "\"" + s.Replace("\"", "\"\"") + "\"";
                    sb.Append(s);
                }
                sb.Append("\r\n");
            }
            return sb.ToString();
        }

        public static int LastRow(IXLWorksheet ws) => ws.LastRowUsed(XLCellsUsedOptions.All)?.RowNumber() ?? 1;
        public static int LastCol(IXLWorksheet ws) => ws.LastColumnUsed(XLCellsUsedOptions.All)?.ColumnNumber() ?? 1;

        /// <summary>Pastes plain text (tab separated, like Excel puts on the Clipboard). Returns the pasted area.</summary>
        public static CellRect PasteText(IXLWorksheet ws, int row, int col, string text)
        {
            var rows = Delimited.Parse(text.TrimEnd('\r', '\n'), '\t');
            int maxCols = Math.Max(1, rows.Count == 0 ? 1 : rows.Max(r => r.Count));
            for (int i = 0; i < rows.Count; i++)
                for (int j = 0; j < rows[i].Count; j++)
                    CellInput.Apply(ws.Cell(row + i, col + j), rows[i][j]);
            return new CellRect(row, col, row + Math.Max(0, rows.Count - 1), col + maxCols - 1);
        }

        public static (int Rows, int Cols) TextSize(string text)
        {
            var rows = Delimited.Parse(text.TrimEnd('\r', '\n'), '\t');
            return (Math.Max(1, rows.Count), Math.Max(1, rows.Count == 0 ? 1 : rows.Max(r => r.Count)));
        }

        public enum PasteWhat { All, Formulas, Values, Formats, AllExceptBorders, ColumnWidths }

        /// <summary>Copies cells between sheets/workbooks like Excel's Paste / Paste Special.</summary>
        public static void PasteCells(IXLWorksheet from, CellRect src, IXLWorksheet to, int row, int col, PasteWhat what, bool transpose, bool skipBlanks)
        {
            if (what == PasteWhat.All && !transpose && !skipBlanks)
            {
                from.Range(src.R1, src.C1, src.R2, src.C2).CopyTo(to.Cell(row, col));
                return;
            }
            if (what == PasteWhat.ColumnWidths)
            {
                for (int c = 0; c < src.Cols; c++) to.Column(col + c).Width = from.Column(src.C1 + c).Width;
                return;
            }
            for (int r = 0; r < src.Rows; r++)
                for (int c = 0; c < src.Cols; c++)
                {
                    var s = from.Cell(src.R1 + r, src.C1 + c);
                    if (skipBlanks && s.IsEmpty()) continue;
                    var d = transpose ? to.Cell(row + c, col + r) : to.Cell(row + r, col + c);
                    switch (what)
                    {
                        case PasteWhat.Values:
                            d.Value = SafeValue(s);
                            break;
                        case PasteWhat.Formulas:
                            // R1C1 keeps references relative to the new position, and the target keeps its formatting
                            if (s.HasFormula) d.FormulaR1C1 = s.FormulaR1C1;
                            else d.Value = s.Value;
                            break;
                        case PasteWhat.Formats:
                            d.Style = s.Style;
                            break;
                        default:
                            if (!transpose) s.CopyTo(d);
                            else { d.Style = s.Style; if (s.HasFormula) d.FormulaR1C1 = s.FormulaR1C1; else d.Value = s.Value; }
                            if (what == PasteWhat.AllExceptBorders) d.Style.Border.OutsideBorder = XLBorderStyleValues.None;
                            break;
                    }
                }
        }

        public static XLCellValue SafeValue(IXLCell c)
        {
            try { return c.Value; } catch { return XLError.IncompatibleValue; }
        }

        // =========================================================== AutoSum
        /// <summary>The range AutoSum would add up for a cell: numbers above it, otherwise to its left.</summary>
        public static CellRect? AutoSumRange(IXLWorksheet ws, int row, int col)
        {
            bool IsNum(int r, int c) { if (r < 1 || c < 1) return false; var v = SafeValue(ws.Cell(r, c)); return v.IsNumber || v.IsDateTime; }
            int r1 = row - 1;
            while (r1 >= 1 && ws.Cell(r1, col).IsEmpty()) r1--;
            if (r1 >= 1 && IsNum(r1, col))
            {
                int top = r1;
                while (top - 1 >= 1 && (IsNum(top - 1, col) || ws.Cell(top - 1, col).IsEmpty() && IsNum(top - 2, col))) top--;
                return new CellRect(top, col, row - 1, col);
            }
            int c1 = col - 1;
            while (c1 >= 1 && ws.Cell(row, c1).IsEmpty()) c1--;
            if (c1 >= 1 && IsNum(row, c1))
            {
                int left = c1;
                while (left - 1 >= 1 && IsNum(row, left - 1)) left--;
                return new CellRect(row, left, row, col - 1);
            }
            return null;
        }

        // =========================================================== region / navigation
        /// <summary>The block of non-empty cells around a cell (Ctrl+A, sorting, filters, Format as Table).</summary>
        public static CellRect CurrentRegion(IXLWorksheet ws, int row, int col)
        {
            try
            {
                var r = ws.Cell(row, col).CurrentRegion;
                var a = r.RangeAddress;
                return new CellRect(a.FirstAddress.RowNumber, a.FirstAddress.ColumnNumber, a.LastAddress.RowNumber, a.LastAddress.ColumnNumber);
            }
            catch { return new CellRect(row, col, row, col); }
        }

        /// <summary>Ctrl+arrow: jumps to the edge of the data block in a direction.</summary>
        public static (int Row, int Col) Jump(IXLWorksheet ws, int row, int col, int dr, int dc, int lastRow, int lastCol)
        {
            bool Empty(int r, int c) => ws.Cell(r, c).IsEmpty();
            int maxR = SheetLimits.Rows, maxC = SheetLimits.Cols;
            int r0 = row, c0 = col;
            bool InBounds(int r, int c) => r >= 1 && c >= 1 && r <= maxR && c <= maxC;
            int nr = row + dr, nc = col + dc;
            if (!InBounds(nr, nc)) return (row, col);
            bool curEmpty = Empty(row, col), nextEmpty = Empty(nr, nc);
            if (!curEmpty && !nextEmpty)
            {
                // run to the last filled cell
                while (InBounds(nr + dr, nc + dc) && !Empty(nr + dr, nc + dc)) { nr += dr; nc += dc; if (Math.Abs(nr - r0) > 200000) break; }
                return (nr, nc);
            }
            // skip empties to the next filled cell (or the sheet edge; stop past the data so we don't walk a million rows)
            while (InBounds(nr, nc) && Empty(nr, nc))
            {
                if ((dr > 0 && nr > lastRow) || (dc > 0 && nc > lastCol)) return (dr > 0 ? maxR : nr, dc > 0 ? maxC : nc);
                nr += dr; nc += dc;
            }
            if (!InBounds(nr, nc)) return (Math.Clamp(nr, 1, maxR), Math.Clamp(nc, 1, maxC));
            return (nr, nc);
        }

        // =========================================================== find
        public class FindOptions
        {
            public string What = "";
            public bool MatchCase, WholeCell, InFormulas = true, ByColumns, WholeBook;
        }

        public static bool Matches(IXLCell c, FindOptions o)
        {
            string s;
            try { s = o.InFormulas && c.HasFormula ? "=" + c.FormulaA1 : c.GetFormattedString(); } catch { return false; }
            var cmp = o.MatchCase ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase;
            if (o.What.Contains('*') || o.What.Contains('?'))
            {
                var pattern = "^" + Regex.Escape(o.What).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                if (!o.WholeCell) pattern = pattern.Trim('^', '$');
                return Regex.IsMatch(s, pattern, o.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
            }
            return o.WholeCell ? s.Equals(o.What, cmp) : s.IndexOf(o.What, cmp) >= 0;
        }

        public static List<IXLCell> FindAll(IXLWorksheet ws, FindOptions o)
        {
            var cells = ws.CellsUsed(XLCellsUsedOptions.Contents).Where(c => Matches(c, o));
            return (o.ByColumns ? cells.OrderBy(c => c.Address.ColumnNumber).ThenBy(c => c.Address.RowNumber)
                                : cells.OrderBy(c => c.Address.RowNumber).ThenBy(c => c.Address.ColumnNumber)).ToList();
        }

        public static bool ReplaceIn(IXLCell c, FindOptions o, string with)
        {
            if (!Matches(c, o)) return false;
            if (c.HasFormula)
            {
                c.FormulaA1 = ReplaceText(c.FormulaA1, o, with);
                return true;
            }
            var v = c.Value;
            if (!v.IsText && o.WholeCell) { CellInput.Apply(c, with); return true; }
            var edit = CellInput.EditText(c);
            CellInput.Apply(c, ReplaceText(edit, o, with));
            return true;
        }

        static string ReplaceText(string s, FindOptions o, string with)
        {
            if (o.WholeCell) return with;
            var pattern = Regex.Escape(o.What).Replace("\\*", ".*").Replace("\\?", ".");
            return Regex.Replace(s, pattern, with.Replace("$", "$$"), o.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
        }

        // =========================================================== sort
        /// <summary>Whether the first row of a block looks like headers (text over numbers/dates, or bold).</summary>
        public static bool HasHeader(IXLWorksheet ws, CellRect r)
        {
            if (r.Rows < 2) return false;
            int textOverOther = 0, bold = 0;
            for (int c = r.C1; c <= r.C2; c++)
            {
                var h = ws.Cell(r.R1, c); var d = ws.Cell(r.R1 + 1, c);
                var hv = SafeValue(h); var dv = SafeValue(d);
                if (hv.IsText && !dv.IsText && !dv.IsBlank) textOverOther++;
                if (h.Style.Font.Bold && !d.Style.Font.Bold) bold++;
            }
            return textOverOther > 0 || bold > 0;
        }

        public class SortKey { public int Column; public bool Descending; }

        /// <summary>Sorts the rows of a block by several columns (Data → Sort).</summary>
        public static void Sort(IXLWorksheet ws, CellRect r, bool header, List<SortKey> keys, bool matchCase)
        {
            int top = header ? r.R1 + 1 : r.R1;
            if (top > r.R2 || keys.Count == 0) return;
            var range = ws.Range(top, r.C1, r.R2, r.C2);
            var spec = string.Join(", ", keys.Select(k => (k.Column - r.C1 + 1) + (k.Descending ? " DESC" : " ASC")));
            range.Sort(spec, XLSortOrder.Ascending, matchCase, true);
        }
    }
}
