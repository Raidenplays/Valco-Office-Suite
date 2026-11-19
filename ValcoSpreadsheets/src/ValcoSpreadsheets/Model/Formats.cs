using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ClosedXML.Excel;

namespace ValcoSpreadsheets.Model
{
    /// <summary>CSV and tab-delimited text: one sheet, values typed the way Excel types them.</summary>
    public static class Delimited
    {
        public static XLWorkbook Import(string path, char sep)
        {
            var text = ReadText(path);
            if (sep == '\t' && !text.Contains('\t') && text.Contains(',')) sep = ',';
            var wb = new XLWorkbook();
            var ws = wb.AddWorksheet(SafeSheetName(Path.GetFileNameWithoutExtension(path)));
            int r = 1;
            foreach (var row in Parse(text, sep))
            {
                for (int c = 0; c < row.Count; c++)
                    if (row[c].Length > 0) CellInput.Apply(ws.Cell(r, c + 1), row[c], fromFile: true);
                r++;
            }
            return wb;
        }

        public static string ReadText(string path)
        {
            var bytes = File.ReadAllBytes(path);
            // UTF-8 (with or without BOM) unless it isn't valid UTF-8, then the Windows code page
            try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿'); }
            catch (DecoderFallbackException)
            {
                try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); return Encoding.GetEncoding(1252).GetString(bytes); }
                catch { return Encoding.Latin1.GetString(bytes); }
            }
        }

        public static List<List<string>> Parse(string text, char sep)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false, any = false;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (quoted)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                        else quoted = false;
                    }
                    else sb.Append(ch);
                    continue;
                }
                if (ch == '"' && sb.Length == 0) { quoted = true; any = true; }
                else if (ch == sep) { row.Add(sb.ToString()); sb.Clear(); any = true; }
                else if (ch == '\r' || ch == '\n')
                {
                    if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(sb.ToString()); sb.Clear();
                    rows.Add(row); row = new List<string>(); any = false;
                }
                else { sb.Append(ch); any = true; }
            }
            if (any || sb.Length > 0) { row.Add(sb.ToString()); rows.Add(row); }
            return rows;
        }

        public static void Export(IXLWorksheet ws, string path, char sep)
        {
            var sb = new StringBuilder();
            var used = ws.RangeUsed(XLCellsUsedOptions.Contents);
            if (used != null)
            {
                int r2 = used.LastRow().RowNumber(), c2 = used.LastColumn().ColumnNumber();
                for (int r = 1; r <= r2; r++)
                {
                    for (int c = 1; c <= c2; c++)
                    {
                        if (c > 1) sb.Append(sep);
                        var cell = ws.Cell(r, c);
                        string s;
                        try { s = cell.IsEmpty() ? "" : cell.GetFormattedString(); } catch { s = ""; }
                        sb.Append(Quote(s, sep));
                    }
                    sb.Append("\r\n");
                }
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        public static string Quote(string s, char sep)
        {
            if (s.IndexOf(sep) >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        public static string SafeSheetName(string name)
        {
            foreach (var ch in new[] { ':', '\\', '/', '?', '*', '[', ']' }) name = name.Replace(ch, '_');
            name = name.Trim('\'');
            if (name.Length > 31) name = name.Substring(0, 31);
            return string.IsNullOrWhiteSpace(name) ? "Sheet1" : name;
        }
    }

    /// <summary>Excel 97-2003 workbooks (.xls): values, number formats, column widths and merges via ExcelDataReader.</summary>
    public static class LegacyXls
    {
        public static XLWorkbook Import(string path)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var wb = new XLWorkbook();
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = ExcelDataReader.ExcelReaderFactory.CreateReader(fs);
            do
            {
                var name = Delimited.SafeSheetName(reader.Name ?? ("Sheet" + (wb.Worksheets.Count + 1)));
                while (wb.Worksheets.Any(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) name += "_";
                var ws = wb.AddWorksheet(name);
                int r = 0;
                while (reader.Read())
                {
                    r++;
                    for (int c = 0; c < reader.FieldCount; c++)
                    {
                        var v = reader.GetValue(c);
                        if (v == null) continue;
                        var cell = ws.Cell(r, c + 1);
                        switch (v)
                        {
                            case double d: cell.Value = d; break;
                            case int i: cell.Value = i; break;
                            case bool b: cell.Value = b; break;
                            case DateTime dt: cell.Value = dt; break;
                            case TimeSpan ts: cell.Value = ts; break;
                            default: cell.Value = v.ToString(); break;
                        }
                        try
                        {
                            var nf = reader.GetNumberFormatString(c);
                            if (!string.IsNullOrEmpty(nf) && nf != "General") cell.Style.NumberFormat.Format = nf;
                        }
                        catch { }
                    }
                }
                try
                {
                    for (int c = 0; c < reader.FieldCount; c++)
                    {
                        var w = reader.GetColumnWidth(c);
                        if (w > 0) ws.Column(c + 1).Width = w;
                    }
                }
                catch { }
                try
                {
                    if (reader.MergeCells != null)
                        foreach (var m in reader.MergeCells)
                            ws.Range(m.FromRow + 1, m.FromColumn + 1, m.ToRow + 1, m.ToColumn + 1).Merge();
                }
                catch { }
            } while (reader.NextResult());
            if (wb.Worksheets.Count == 0) wb.AddWorksheet("Sheet1");
            return wb;
        }
    }

    /// <summary>Turns what the user types into a cell value the way Excel does: numbers, percentages, currency, dates, times, TRUE/FALSE, formulas.</summary>
    public static class CellInput
    {
        public static CultureInfo Culture => CultureInfo.CurrentCulture;

        /// <summary>Puts typed text into a cell. Returns false when the formula couldn't be set.</summary>
        public static void Apply(IXLCell cell, string text, bool fromFile = false)
        {
            if (text == null) text = "";
            if (!fromFile && text.Length > 1 && text[0] == '=' )
            {
                cell.FormulaA1 = text.Substring(1);
                return;
            }
            if (!fromFile && text.StartsWith("'"))
            {
                cell.Value = text.Substring(1);
                return;
            }
            if (text.Length == 0) { cell.Value = Blank.Value; return; }
            var (value, format) = Parse(text, cell.Style.NumberFormat);
            bool general = IsGeneral(cell);
            cell.Value = value;
            if (format != null && general) cell.Style.NumberFormat.Format = format;
        }

        static bool IsGeneral(IXLCell cell) => cell.Style.NumberFormat.NumberFormatId == 0 && string.IsNullOrEmpty(cell.Style.NumberFormat.Format);

        /// <summary>Text cells formatted as Text (@) stay text.</summary>
        public static (XLCellValue Value, string Format) Parse(string text, IXLNumberFormat current = null)
        {
            if (current != null && (current.Format == "@" || current.NumberFormatId == 49)) return (text, null);
            var t = text.Trim();
            var ci = Culture;
            var nfi = ci.NumberFormat;
            if (t.Equals("TRUE", StringComparison.OrdinalIgnoreCase)) return (true, null);
            if (t.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) return (false, null);
            // percent
            if (t.EndsWith("%") && double.TryParse(t.TrimEnd('%').Trim(), NumberStyles.Float | NumberStyles.AllowThousands, ci, out var pct))
                return (pct / 100, t.Contains(nfi.NumberDecimalSeparator) ? "0.00%" : "0%");
            // plain number (and Excel-style negative in parentheses)
            if (double.TryParse(t, NumberStyles.Float | NumberStyles.AllowThousands, ci, out var num))
            {
                if (t.Contains(nfi.NumberGroupSeparator) && nfi.NumberGroupSeparator.Length > 0 && t.IndexOf(nfi.NumberGroupSeparator, StringComparison.Ordinal) > 0)
                    return (num, t.Contains(nfi.NumberDecimalSeparator) ? "#,##0.00" : "#,##0");
                return (num, null);
            }
            if (t.Length > 2 && t[0] == '(' && t[^1] == ')' && double.TryParse(t.Substring(1, t.Length - 2), NumberStyles.Float | NumberStyles.AllowThousands, ci, out var neg))
                return (-neg, null);
            // currency
            var sym = nfi.CurrencySymbol;
            if (t.Contains(sym) || t.Contains("$"))
            {
                var bare = t.Replace(sym, "").Replace("$", "").Trim();
                bool negative = bare.StartsWith("-") || (bare.StartsWith("(") && bare.EndsWith(")"));
                bare = bare.Trim('-', '(', ')').Trim();
                if (double.TryParse(bare, NumberStyles.Float | NumberStyles.AllowThousands, ci, out var money))
                    return (negative ? -money : money, bare.Contains(nfi.NumberDecimalSeparator) ? "\"$\"#,##0.00" : "\"$\"#,##0");
            }
            // scientific already handled; fractions like 1/2 are dates in Excel unless they start with 0 (0 1/2)
            if (t.StartsWith("0 ") && TryFraction(t.Substring(2), out var frac)) return (frac, "# ?/?");
            // time
            if (LooksLikeTime(t) && DateTime.TryParse(t, ci, DateTimeStyles.NoCurrentDateDefault | DateTimeStyles.AllowWhiteSpaces, out var tm) && tm.Date == DateTime.MinValue.Date)
                return (tm.TimeOfDay, t.Count(ch => ch == ':') == 2 ? "h:mm:ss" : (t.ToUpperInvariant().Contains("M") ? "h:mm AM/PM" : "h:mm"));
            // date
            if (LooksLikeDate(t) && DateTime.TryParse(t, ci, DateTimeStyles.AllowWhiteSpaces, out var dt))
            {
                if (dt.Year < 1900) return (t, null);
                string fmt = dt.TimeOfDay == TimeSpan.Zero ? (t.Any(char.IsLetter) ? "d-mmm-yy" : "m/d/yyyy") : "m/d/yyyy h:mm";
                if (t.Any(char.IsLetter) && !Regexes.HasYear.IsMatch(t)) fmt = "d-mmm";
                return (dt, fmt);
            }
            return (text, null);
        }

        static bool TryFraction(string s, out double v)
        {
            v = 0;
            var p = s.Split('/');
            if (p.Length != 2 || !double.TryParse(p[0], out var a) || !double.TryParse(p[1], out var b) || b == 0) return false;
            v = a / b; return true;
        }

        static bool LooksLikeTime(string t) => t.Contains(':') && t.Length <= 11 && char.IsDigit(t[0]);
        static bool LooksLikeDate(string t) =>
            t.Length >= 3 && t.Length <= 30 && (t.Count(ch => ch == '/' || ch == '-') >= 1 || Regexes.MonthName.IsMatch(t)) && t.Any(char.IsDigit);

        /// <summary>The text shown in the formula bar / in-cell editor for a cell.</summary>
        public static string EditText(IXLCell cell)
        {
            if (cell == null) return "";
            try
            {
                if (cell.HasFormula) return "=" + cell.FormulaA1;
                var v = cell.Value;
                var ci = Culture;
                switch (v.Type)
                {
                    case XLDataType.Blank: return "";
                    case XLDataType.Boolean: return v.GetBoolean() ? "TRUE" : "FALSE";
                    case XLDataType.Text:
                        var s = v.GetText();
                        // text that would be read back as a number keeps its apostrophe
                        if (s.Length > 0 && !(cell.Style.NumberFormat.Format == "@") && !(Parse(s).Value.IsText)) return "'" + s;
                        return s;
                    case XLDataType.Error: return v.GetError() switch
                    {
                        XLError.DivisionByZero => "#DIV/0!", XLError.NoValueAvailable => "#N/A", XLError.NameNotRecognized => "#NAME?",
                        XLError.NullValue => "#NULL!", XLError.NumberInvalid => "#NUM!", XLError.CellReference => "#REF!", _ => "#VALUE!"
                    };
                    case XLDataType.DateTime:
                        var dt = v.GetDateTime();
                        return dt.TimeOfDay == TimeSpan.Zero ? dt.ToString("d", ci) : dt.ToString("g", ci);
                    case XLDataType.TimeSpan:
                        var ts = v.GetTimeSpan();
                        return DateTime.Today.Add(ts).ToString("T", ci);
                    default:
                        var n = v.GetNumber();
                        var f = cell.Style.NumberFormat.Format ?? "";
                        if (f.Contains("%")) return (n * 100).ToString("0.##########", ci) + "%";
                        return n.ToString("0.###############", ci);
                }
            }
            catch { return ""; }
        }
    }

    static class Regexes
    {
        public static readonly System.Text.RegularExpressions.Regex MonthName = new(@"\b(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        public static readonly System.Text.RegularExpressions.Regex HasYear = new(@"\d{4}|\d{1,2}[/-]\d{1,2}[/-]\d{2}");
    }
}
