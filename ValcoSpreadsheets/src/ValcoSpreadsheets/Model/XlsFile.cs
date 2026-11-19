using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ClosedXML.Excel;
using ClosedXML.Excel.Drawings;
using NPOI.HSSF.UserModel;
using NPOI.POIFS.FileSystem;
using NPOI.SS.Util;
using SS = NPOI.SS.UserModel;

namespace ValcoSpreadsheets.Model
{
    /// <summary>
    /// Excel 97-2003 workbooks (.xls, .xlt) through NPOI's HSSF (Apache 2.0).
    /// Opening turns the BIFF8 file into a ClosedXML workbook with its values, formulas, formatting,
    /// column widths, row heights, merges, frozen panes, comments, links, pictures, names, print setup,
    /// conditional formats and data validation. Saving does the same in reverse.
    /// </summary>
    public static class XlsFile
    {
        public const int MaxRows = 65536, MaxCols = 256;
        const short AutoFontColor = 0x7FFF;

        // ================================================================== opening
        public class ImportResult
        {
            public XLWorkbook Workbook;
            public byte[] VbaProject;
            public bool HasCharts;
        }

        public static ImportResult Import(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var ms = new MemoryStream();
            fs.CopyTo(ms);
            ms.Position = 0;
            var poifs = new POIFSFileSystem(ms);
            var res = new ImportResult { VbaProject = ExtractVba(poifs) };
            var hwb = new HSSFWorkbook(poifs.Root, true);
            var wb = new XLWorkbook();
            var pal = hwb.GetCustomPalette();

            // the workbook's default font becomes the default for new cells too (Arial 10 in most .xls files)
            try
            {
                var f0 = hwb.GetFontAt(0);
                wb.Style.Font.FontName = f0.FontName;
                wb.Style.Font.FontSize = f0.FontHeightInPoints;
            }
            catch { }

            for (int i = 0; i < hwb.NumberOfSheets; i++)
            {
                var hs = (HSSFSheet)hwb.GetSheetAt(i);
                var name = Delimited.SafeSheetName(hs.SheetName);
                while (wb.Worksheets.Any(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) name += "_";
                var ws = wb.AddWorksheet(name);
                try
                {
                    if (hwb.IsSheetVeryHidden(i)) ws.Visibility = XLWorksheetVisibility.VeryHidden;
                    else if (hwb.IsSheetHidden(i)) ws.Visibility = XLWorksheetVisibility.Hidden;
                }
                catch { }
            }
            // a workbook needs one visible sheet
            if (wb.Worksheets.Count == 0) wb.AddWorksheet("Sheet1");
            if (!wb.Worksheets.Any(w => w.Visibility == XLWorksheetVisibility.Visible)) wb.Worksheet(1).Visibility = XLWorksheetVisibility.Visible;

            for (int i = 0; i < hwb.NumberOfSheets; i++)
            {
                var hs = (HSSFSheet)hwb.GetSheetAt(i);
                var ws = wb.Worksheet(i + 1);
                ImportSheet(hwb, hs, ws, pal);
                try { if (HSSFChart.GetSheetCharts(hs).Length > 0) res.HasCharts = true; } catch { }
            }
            ImportNames(hwb, wb);
            try { wb.Worksheet(Math.Clamp(hwb.ActiveSheetIndex + 1, 1, wb.Worksheets.Count)).SetTabActive(); } catch { }
            res.Workbook = wb;
            return res;
        }

        static void ImportSheet(HSSFWorkbook hwb, HSSFSheet hs, IXLWorksheet ws, HSSFPalette pal)
        {
            // column widths (1/256 of a character, same as the .xlsx width) and hidden columns
            try { ws.ColumnWidth = hs.DefaultColumnWidth + 0.71; } catch { }
            try { ws.RowHeight = hs.DefaultRowHeightInPoints; } catch { }
            int lastCol = 0;
            for (int r = hs.FirstRowNum; r <= hs.LastRowNum; r++) { var row = hs.GetRow(r); if (row != null && row.LastCellNum > lastCol) lastCol = row.LastCellNum; }
            for (int c = 0; c < Math.Min(MaxCols, Math.Max(lastCol, 64)); c++)
            {
                try
                {
                    if (hs.IsColumnHidden(c)) ws.Column(c + 1).Hide();
                    var w = hs.GetColumnWidth(c);
                    if (w != hs.DefaultColumnWidth * 256) ws.Column(c + 1).Width = w / 256.0;
                }
                catch { }
            }

            var styleCells = new Dictionary<short, IXLCell>();
            for (int r = hs.FirstRowNum; r <= hs.LastRowNum; r++)
            {
                var row = hs.GetRow(r);
                if (row == null) continue;
                var xrow = ws.Row(r + 1);
                if (row.ZeroHeight) xrow.Hide();
                else if (row.Height != hs.DefaultRowHeight && row.Height > 0) xrow.Height = row.HeightInPoints;
                foreach (var hc in row.Cells)
                {
                    var cell = ws.Cell(r + 1, hc.ColumnIndex + 1);
                    ImportValue(hc, cell);
                    // formatting: built once per HSSF style, then copied
                    var st = hc.CellStyle;
                    if (st != null && st.Index != 15 && st.Index != 0)
                    {
                        if (styleCells.TryGetValue(st.Index, out var first)) cell.Style = first.Style;
                        else { ApplyStyle(hwb, st, cell.Style, pal); styleCells[st.Index] = cell; }
                    }
                    try
                    {
                        var cm = hc.CellComment;
                        if (cm?.String != null && !string.IsNullOrEmpty(cm.String.String))
                        {
                            var xc = cell.CreateComment();
                            if (!string.IsNullOrEmpty(cm.Author)) xc.Author = cm.Author;
                            xc.AddText(cm.String.String);
                        }
                    }
                    catch { }
                    try
                    {
                        var hl = hc.Hyperlink;
                        if (hl != null && !string.IsNullOrEmpty(hl.Address))
                        {
                            if (hl.Type == SS.HyperlinkType.Document) cell.SetHyperlink(new XLHyperlink(hl.Address.TrimStart('#')));
                            else if (Uri.TryCreate(hl.Address, UriKind.Absolute, out var u)) cell.SetHyperlink(new XLHyperlink(u));
                        }
                    }
                    catch { }
                }
            }

            foreach (var m in hs.MergedRegions)
            {
                try { ws.Range(m.FirstRow + 1, m.FirstColumn + 1, m.LastRow + 1, m.LastColumn + 1).Merge(false); } catch { }
            }

            try
            {
                var pane = hs.PaneInformation;
                if (pane != null && pane.IsFreezePane())
                {
                    if (pane.HorizontalSplitPosition > 0) ws.SheetView.SplitRow = pane.HorizontalSplitPosition;
                    if (pane.VerticalSplitPosition > 0) ws.SheetView.SplitColumn = pane.VerticalSplitPosition;
                }
            }
            catch { }
            try { ws.ShowGridLines = hs.DisplayGridlines; } catch { }
            try { if (hs.TabColorIndex != 0 && hs.TabColorIndex != 64) { var c = RgbOf(pal, hs.TabColorIndex); if (c != null) ws.SetTabColor(c); } } catch { }

            ImportPrintSetup(hs, ws);
            ImportPictures(hwb, hs, ws);
            ImportConditionalFormats(hwb, hs, ws, pal);
            ImportValidation(hs, ws);
        }

        static void ImportValue(SS.ICell hc, IXLCell cell)
        {
            try
            {
                switch (hc.CellType)
                {
                    case SS.CellType.Numeric: cell.Value = hc.NumericCellValue; break;
                    case SS.CellType.String: cell.Value = hc.StringCellValue ?? ""; break;
                    case SS.CellType.Boolean: cell.Value = hc.BooleanCellValue; break;
                    case SS.CellType.Error: cell.Value = ErrorFromCode(hc.ErrorCellValue); break;
                    case SS.CellType.Formula:
                        string f = null;
                        try { f = hc.CellFormula; } catch { }
                        bool set = false;
                        if (!string.IsNullOrEmpty(f))
                        {
                            try { cell.FormulaA1 = FutureFunctions(f, false); set = true; } catch { }
                        }
                        if (!set) ImportCached(hc, cell);
                        break;
                }
            }
            catch { }
        }

        /// <summary>
        /// Functions newer than Excel 2003 (IFERROR, SUMIFS, AVERAGEIF...) aren't in the BIFF8 function table:
        /// Excel stores them as "_xlfn." add-in names, so they are prefixed on save and the prefix is dropped on open.
        /// </summary>
        static string FutureFunctions(string f, bool toXls)
        {
            var sb = new StringBuilder(f.Length + 16);
            int i = 0;
            while (i < f.Length)
            {
                char ch = f[i];
                if (ch == '"' || ch == '\'')
                {
                    // string literal or quoted sheet name: copy as is
                    int j = i + 1;
                    while (j < f.Length) { if (f[j] == ch) { if (j + 1 < f.Length && f[j + 1] == ch) { j += 2; continue; } break; } j++; }
                    sb.Append(f, i, Math.Min(f.Length, j + 1) - i);
                    i = j + 1;
                    continue;
                }
                if (char.IsLetter(ch) || ch == '_')
                {
                    int j = i;
                    while (j < f.Length && (char.IsLetterOrDigit(f[j]) || f[j] == '_' || f[j] == '.')) j++;
                    var word = f.Substring(i, j - i);
                    bool isCall = j < f.Length && f[j] == '(' && (i == 0 || !(char.IsLetterOrDigit(f[i - 1]) || f[i - 1] == '!' || f[i - 1] == '$'));
                    if (isCall)
                    {
                        if (!toXls && word.StartsWith("_xlfn.", StringComparison.OrdinalIgnoreCase)) word = word.Substring(6);
                        else if (toXls && !word.StartsWith("_xlfn.", StringComparison.OrdinalIgnoreCase)
                                 && NPOI.SS.Formula.Function.FunctionMetadataRegistry.GetFunctionByName(word.ToUpperInvariant()) == null
                                 && Functions.ByName.ContainsKey(word))
                            word = "_xlfn." + word.ToUpperInvariant();
                    }
                    sb.Append(word);
                    i = j;
                    continue;
                }
                sb.Append(ch);
                i++;
            }
            return sb.ToString();
        }

        static void ImportCached(SS.ICell hc, IXLCell cell)
        {
            switch (hc.CachedFormulaResultType)
            {
                case SS.CellType.Numeric: cell.Value = hc.NumericCellValue; break;
                case SS.CellType.String: cell.Value = hc.StringCellValue ?? ""; break;
                case SS.CellType.Boolean: cell.Value = hc.BooleanCellValue; break;
                case SS.CellType.Error: cell.Value = ErrorFromCode(hc.ErrorCellValue); break;
            }
        }

        static void ApplyStyle(HSSFWorkbook hwb, SS.ICellStyle s, IXLStyle x, HSSFPalette pal)
        {
            // number format
            try
            {
                short idx = s.DataFormat;
                if (idx > 0 && idx < 50) x.NumberFormat.NumberFormatId = idx;
                else if (idx >= 50)
                {
                    var f = s.GetDataFormatString();
                    if (!string.IsNullOrEmpty(f) && f != "General") x.NumberFormat.Format = f;
                }
            }
            catch { }

            // font
            try
            {
                var f = (HSSFFont)s.GetFont(hwb);
                x.Font.FontName = f.FontName;
                x.Font.FontSize = f.FontHeightInPoints;
                x.Font.Bold = f.IsBold;
                x.Font.Italic = f.IsItalic;
                x.Font.Strikethrough = f.IsStrikeout;
                x.Font.Underline = f.Underline switch
                {
                    SS.FontUnderlineType.Single => XLFontUnderlineValues.Single,
                    SS.FontUnderlineType.Double => XLFontUnderlineValues.Double,
                    SS.FontUnderlineType.SingleAccounting => XLFontUnderlineValues.SingleAccounting,
                    SS.FontUnderlineType.DoubleAccounting => XLFontUnderlineValues.DoubleAccounting,
                    _ => XLFontUnderlineValues.None,
                };
                if (f.TypeOffset == SS.FontSuperScript.Super) x.Font.VerticalAlignment = XLFontVerticalTextAlignmentValues.Superscript;
                else if (f.TypeOffset == SS.FontSuperScript.Sub) x.Font.VerticalAlignment = XLFontVerticalTextAlignmentValues.Subscript;
                if (f.Color != AutoFontColor && f.Color != 8 && f.Color != 64)
                {
                    var c = RgbOf(pal, f.Color);
                    if (c != null) x.Font.FontColor = c;
                }
            }
            catch { }

            // fill
            try
            {
                var pat = (int)s.FillPattern;
                if (pat == 1)
                {
                    var c = RgbOf(pal, s.FillForegroundColor);
                    if (c != null) { x.Fill.PatternType = XLFillPatternValues.Solid; x.Fill.BackgroundColor = c; }
                }
                else if (pat > 1 && FillByCode.TryGetValue(pat, out var xp))
                {
                    x.Fill.PatternType = xp;
                    var fg = RgbOf(pal, s.FillForegroundColor); if (fg != null) x.Fill.PatternColor = fg;
                    var bg = RgbOf(pal, s.FillBackgroundColor); if (bg != null) x.Fill.BackgroundColor = bg;
                }
            }
            catch { }

            // borders
            try
            {
                SetBorder(s.BorderLeft, s.LeftBorderColor, v => x.Border.LeftBorder = v, c => x.Border.LeftBorderColor = c, pal);
                SetBorder(s.BorderRight, s.RightBorderColor, v => x.Border.RightBorder = v, c => x.Border.RightBorderColor = c, pal);
                SetBorder(s.BorderTop, s.TopBorderColor, v => x.Border.TopBorder = v, c => x.Border.TopBorderColor = c, pal);
                SetBorder(s.BorderBottom, s.BottomBorderColor, v => x.Border.BottomBorder = v, c => x.Border.BottomBorderColor = c, pal);
            }
            catch { }

            // alignment
            try
            {
                x.Alignment.Horizontal = s.Alignment switch
                {
                    SS.HorizontalAlignment.Left => XLAlignmentHorizontalValues.Left,
                    SS.HorizontalAlignment.Center => XLAlignmentHorizontalValues.Center,
                    SS.HorizontalAlignment.Right => XLAlignmentHorizontalValues.Right,
                    SS.HorizontalAlignment.Fill => XLAlignmentHorizontalValues.Fill,
                    SS.HorizontalAlignment.Justify => XLAlignmentHorizontalValues.Justify,
                    SS.HorizontalAlignment.CenterSelection => XLAlignmentHorizontalValues.CenterContinuous,
                    SS.HorizontalAlignment.Distributed => XLAlignmentHorizontalValues.Distributed,
                    _ => XLAlignmentHorizontalValues.General,
                };
                x.Alignment.Vertical = s.VerticalAlignment switch
                {
                    SS.VerticalAlignment.Top => XLAlignmentVerticalValues.Top,
                    SS.VerticalAlignment.Center => XLAlignmentVerticalValues.Center,
                    SS.VerticalAlignment.Justify => XLAlignmentVerticalValues.Justify,
                    SS.VerticalAlignment.Distributed => XLAlignmentVerticalValues.Distributed,
                    _ => XLAlignmentVerticalValues.Bottom,
                };
                x.Alignment.WrapText = s.WrapText;
                x.Alignment.ShrinkToFit = s.ShrinkToFit;
                if (s.Indention > 0) x.Alignment.Indent = s.Indention;
                short rot = s.Rotation;
                if (rot == 0xFF) x.Alignment.TextRotation = 255;
                else if (rot > 0 && rot <= 90) x.Alignment.TextRotation = rot;
                else if (rot < 0 && rot >= -90) x.Alignment.TextRotation = 90 - rot;
            }
            catch { }

            try { x.Protection.Locked = s.IsLocked; x.Protection.Hidden = s.IsHidden; } catch { }
        }

        static void SetBorder(SS.BorderStyle b, short color, Action<XLBorderStyleValues> set, Action<XLColor> setColor, HSSFPalette pal)
        {
            if (b == SS.BorderStyle.None) return;
            set(b == SS.BorderStyle.SlantedDashDot ? XLBorderStyleValues.SlantDashDot
                : Enum.TryParse<XLBorderStyleValues>(b.ToString(), out var v) ? v : XLBorderStyleValues.Thin);
            if (color != 64 && color != 8) { var c = RgbOf(pal, color); if (c != null) setColor(c); }
        }

        static XLColor RgbOf(HSSFPalette pal, short idx)
        {
            if (idx == 64 || idx == AutoFontColor) return null;
            var c = pal.GetColor(idx);
            if (c == null) return null;
            var t = c.RGB;
            return XLColor.FromArgb(t[0], t[1], t[2]);
        }

        static void ImportPrintSetup(HSSFSheet hs, IXLWorksheet ws)
        {
            try
            {
                var p = hs.PrintSetup;
                if (p.NoOrientation && p.PaperSize <= 0) return;
                ws.PageSetup.PageOrientation = p.Landscape ? XLPageOrientation.Landscape : XLPageOrientation.Portrait;
                if (Enum.IsDefined(typeof(XLPaperSize), (int)p.PaperSize) && p.PaperSize > 0) ws.PageSetup.PaperSize = (XLPaperSize)p.PaperSize;
                if (hs.FitToPage) ws.PageSetup.FitToPages(p.FitWidth, p.FitHeight);
                else if (p.Scale >= 10 && p.Scale <= 400) ws.PageSetup.Scale = p.Scale;
                ws.PageSetup.Margins.Left = hs.GetMargin(SS.MarginType.LeftMargin);
                ws.PageSetup.Margins.Right = hs.GetMargin(SS.MarginType.RightMargin);
                ws.PageSetup.Margins.Top = hs.GetMargin(SS.MarginType.TopMargin);
                ws.PageSetup.Margins.Bottom = hs.GetMargin(SS.MarginType.BottomMargin);
                ws.PageSetup.Margins.Header = p.HeaderMargin;
                ws.PageSetup.Margins.Footer = p.FooterMargin;
                ws.PageSetup.CenterHorizontally = hs.HorizontallyCenter;
                ws.PageSetup.CenterVertically = hs.VerticallyCenter;
                ws.PageSetup.ShowGridlines = hs.IsPrintGridlines;
            }
            catch { }
        }

        static void ImportPictures(HSSFWorkbook hwb, HSSFSheet hs, IXLWorksheet ws)
        {
            try
            {
                if (!(hs.DrawingPatriarch is HSSFPatriarch pat)) return;
                foreach (var shape in pat.Children)
                {
                    if (!(shape is HSSFPicture pic)) continue;
                    try
                    {
                        var data = pic.PictureData?.Data;
                        if (data == null || data.Length == 0) continue;
                        if (!(pic.Anchor is HSSFClientAnchor a)) continue;
                        var xp = ws.AddPicture(new MemoryStream(data));
                        int col1 = a.Col1, row1 = a.Row1, col2 = a.Col2, row2 = a.Row2;
                        double ox = ColPx(ws, col1) * a.Dx1 / 1024.0, oy = RowPx(ws, row1) * a.Dy1 / 256.0;
                        double w = -ox, h = -oy;
                        for (int c = col1; c < col2; c++) w += ColPx(ws, c);
                        for (int r = row1; r < row2; r++) h += RowPx(ws, r);
                        w += ColPx(ws, col2) * a.Dx2 / 1024.0;
                        h += RowPx(ws, row2) * a.Dy2 / 256.0;
                        xp.MoveTo(ws.Cell(row1 + 1, col1 + 1), (int)Math.Round(ox), (int)Math.Round(oy));
                        if (w > 4 && h > 4) xp.WithSize((int)Math.Round(w), (int)Math.Round(h));
                    }
                    catch { }
                }
            }
            catch { }
        }

        static double ColPx(IXLWorksheet ws, int col0) { var c = ws.Column(col0 + 1); return c.IsHidden ? 0 : Grid.Units.ColWidthToPx(c.Width); }
        static double RowPx(IXLWorksheet ws, int row0) { var r = ws.Row(row0 + 1); return r.IsHidden ? 0 : Grid.Units.PtToPx(r.Height); }

        static void ImportConditionalFormats(HSSFWorkbook hwb, HSSFSheet hs, IXLWorksheet ws, HSSFPalette pal)
        {
            try
            {
                var scf = hs.SheetConditionalFormatting;
                for (int i = 0; i < scf.NumConditionalFormattings; i++)
                {
                    var cf = scf.GetConditionalFormattingAt(i);
                    var ranges = cf.GetFormattingRanges();
                    for (int k = 0; k < cf.NumberOfRules; k++)
                    {
                        var rule = cf.GetRule(k);
                        foreach (var rg in ranges)
                        {
                            try
                            {
                                var range = ws.Range(rg.FirstRow + 1, rg.FirstColumn + 1, rg.LastRow + 1, rg.LastColumn + 1);
                                var add = range.AddConditionalFormat();
                                IXLStyle st;
                                if (rule.ConditionType == SS.ConditionType.Formula) st = add.WhenIsTrue("=" + rule.Formula1);
                                else
                                {
                                    string f1 = "=" + rule.Formula1, f2 = rule.Formula2 != null ? "=" + rule.Formula2 : null;
                                    st = rule.ComparisonOperation switch
                                    {
                                        SS.ComparisonOperator.Between => add.WhenBetween(f1, f2),
                                        SS.ComparisonOperator.NotBetween => add.WhenNotBetween(f1, f2),
                                        SS.ComparisonOperator.Equal => add.WhenEquals(f1),
                                        SS.ComparisonOperator.NotEqual => add.WhenNotEquals(f1),
                                        SS.ComparisonOperator.GreaterThan => add.WhenGreaterThan(f1),
                                        SS.ComparisonOperator.LessThan => add.WhenLessThan(f1),
                                        SS.ComparisonOperator.GreaterThanOrEqual => add.WhenEqualOrGreaterThan(f1),
                                        SS.ComparisonOperator.LessThanOrEqual => add.WhenEqualOrLessThan(f1),
                                        _ => null,
                                    };
                                }
                                if (st == null) continue;
                                var ff = rule.FontFormatting;
                                if (ff != null)
                                {
                                    if (ff.IsBold) st.Font.Bold = true;
                                    if (ff.IsItalic) st.Font.Italic = true;
                                    var fc = RgbOf(pal, ff.FontColorIndex); if (fc != null) st.Font.FontColor = fc;
                                }
                                var pf = rule.PatternFormatting;
                                if (pf != null)
                                {
                                    var bc = RgbOf(pal, pf.FillBackgroundColor);
                                    if (bc != null) { st.Fill.PatternType = XLFillPatternValues.Solid; st.Fill.BackgroundColor = bc; }
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
        }

        static void ImportValidation(HSSFSheet hs, IXLWorksheet ws)
        {
            try
            {
                foreach (var dv in hs.GetDataValidations())
                {
                    try
                    {
                        var con = dv.ValidationConstraint;
                        foreach (var rg in dv.Regions.CellRangeAddresses)
                        {
                            var v = ws.Range(rg.FirstRow + 1, rg.FirstColumn + 1, rg.LastRow + 1, rg.LastColumn + 1).CreateDataValidation();
                            v.IgnoreBlanks = dv.EmptyCellAllowed;
                            v.InCellDropdown = !dv.SuppressDropDownArrow;
                            v.ShowErrorMessage = dv.ShowErrorBox;
                            v.ShowInputMessage = dv.ShowPromptBox;
                            if (!string.IsNullOrEmpty(dv.ErrorBoxTitle)) v.ErrorTitle = dv.ErrorBoxTitle;
                            if (!string.IsNullOrEmpty(dv.ErrorBoxText)) v.ErrorMessage = dv.ErrorBoxText;
                            if (!string.IsNullOrEmpty(dv.PromptBoxTitle)) v.InputTitle = dv.PromptBoxTitle;
                            if (!string.IsNullOrEmpty(dv.PromptBoxText)) v.InputMessage = dv.PromptBoxText;
                            switch (con.GetValidationType())
                            {
                                case SS.ValidationType.LIST:
                                    var ex = con.ExplicitListValues;
                                    if (ex != null && ex.Length > 0) v.List("\"" + string.Join(",", ex) + "\"", true);
                                    else v.List("=" + con.Formula1, true);
                                    break;
                                case SS.ValidationType.INTEGER: SetNumeric(v.WholeNumber, con); break;
                                case SS.ValidationType.DECIMAL: SetNumeric(v.Decimal, con); break;
                                case SS.ValidationType.TEXT_LENGTH: SetNumeric(v.TextLength, con); break;
                                case SS.ValidationType.FORMULA: v.Custom("=" + con.Formula1); break;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        static void SetNumeric(IXLValidationCriteria n, SS.IDataValidationConstraint c)
        {
            string f1 = c.Formula1, f2 = c.Formula2;
            switch (c.Operator)
            {
                case SS.OperatorType.BETWEEN: n.Between(f1, f2); break;
                case SS.OperatorType.NOT_BETWEEN: n.NotBetween(f1, f2); break;
                case SS.OperatorType.EQUAL: n.EqualTo(f1); break;
                case SS.OperatorType.NOT_EQUAL: n.NotEqualTo(f1); break;
                case SS.OperatorType.GREATER_THAN: n.GreaterThan(f1); break;
                case SS.OperatorType.LESS_THAN: n.LessThan(f1); break;
                case SS.OperatorType.GREATER_OR_EQUAL: n.EqualOrGreaterThan(f1); break;
                case SS.OperatorType.LESS_OR_EQUAL: n.EqualOrLessThan(f1); break;
            }
        }

        static void ImportNames(HSSFWorkbook hwb, XLWorkbook wb)
        {
            for (int i = 0; i < hwb.NumberOfNames; i++)
            {
                try
                {
                    var n = hwb.GetNameAt(i);
                    if (n.IsFunctionName || string.IsNullOrEmpty(n.RefersToFormula)) continue;
                    var name = n.NameName;
                    var refers = n.RefersToFormula;
                    var ws = n.SheetIndex >= 0 && n.SheetIndex < wb.Worksheets.Count ? wb.Worksheet(n.SheetIndex + 1) : null;
                    if (name == "_FilterDatabase" || name == "_xlnm._FilterDatabase")
                    {
                        if (ws != null) ws.Range(StripSheet(refers)).SetAutoFilter();
                        continue;
                    }
                    if (name == "Print_Area" || name == "_xlnm.Print_Area")
                    {
                        if (ws != null) foreach (var part in refers.Split(',')) ws.PageSetup.PrintAreas.Add(StripSheet(part));
                        continue;
                    }
                    if (name == "Print_Titles" || name == "_xlnm.Print_Titles")
                    {
                        if (ws != null)
                            foreach (var part in refers.Split(','))
                            {
                                var p = StripSheet(part).Replace("$", "");
                                if (p.Length > 0 && char.IsDigit(p[0])) ws.PageSetup.SetRowsToRepeatAtTop(p);
                                else ws.PageSetup.SetColumnsToRepeatAtLeft(p);
                            }
                        continue;
                    }
                    if (name.StartsWith("_xlnm.") || name.Contains('\u0000')) continue;
                    if (ws != null) ws.DefinedNames.Add(name, refers);
                    else wb.DefinedNames.Add(name, refers);
                }
                catch { }
            }
        }

        static string StripSheet(string r)
        {
            int i = r.LastIndexOf('!');
            return (i >= 0 ? r.Substring(i + 1) : r).Trim();
        }

        /// <summary>The _VBA_PROJECT_CUR storage of an .xls is the same compound file as an .xlsm's vbaProject.bin.</summary>
        static byte[] ExtractVba(POIFSFileSystem fs)
        {
            try
            {
                if (!fs.Root.HasEntry("_VBA_PROJECT_CUR")) return null;
                var src = (DirectoryEntry)fs.Root.GetEntry("_VBA_PROJECT_CUR");
                var dst = new POIFSFileSystem();
                EntryUtils.CopyNodes(src, dst.Root);
                using var ms = new MemoryStream();
                dst.WriteFileSystem(ms);
                return ms.ToArray();
            }
            catch { return null; }
        }

        static XLError ErrorFromCode(byte code) => code switch
        {
            0x00 => XLError.NullValue,
            0x07 => XLError.DivisionByZero,
            0x0F => XLError.IncompatibleValue,
            0x17 => XLError.CellReference,
            0x1D => XLError.NameNotRecognized,
            0x24 => XLError.NumberInvalid,
            _ => XLError.NoValueAvailable,
        };

        static byte CodeFromError(XLError e) => e switch
        {
            XLError.NullValue => 0x00,
            XLError.DivisionByZero => 0x07,
            XLError.IncompatibleValue => 0x0F,
            XLError.CellReference => 0x17,
            XLError.NameNotRecognized => 0x1D,
            XLError.NumberInvalid => 0x24,
            _ => 0x2A,
        };

        // BIFF fill pattern numbers <-> ClosedXML patterns
        static readonly Dictionary<int, XLFillPatternValues> FillByCode = new()
        {
            [2] = XLFillPatternValues.MediumGray, [3] = XLFillPatternValues.DarkGray, [4] = XLFillPatternValues.LightGray,
            [5] = XLFillPatternValues.DarkHorizontal, [6] = XLFillPatternValues.DarkVertical, [7] = XLFillPatternValues.DarkDown,
            [8] = XLFillPatternValues.DarkUp, [9] = XLFillPatternValues.DarkGrid, [10] = XLFillPatternValues.DarkTrellis,
            [11] = XLFillPatternValues.LightHorizontal, [12] = XLFillPatternValues.LightVertical, [13] = XLFillPatternValues.LightDown,
            [14] = XLFillPatternValues.LightUp, [15] = XLFillPatternValues.LightGrid, [16] = XLFillPatternValues.LightTrellis,
            [17] = XLFillPatternValues.Gray125, [18] = XLFillPatternValues.Gray0625,
        };

        // ================================================================== compatibility check
        /// <summary>What an Excel 97-2003 file can't hold, worded like Excel 2007's Compatibility Checker. Empty when nothing is lost.</summary>
        public static List<string> CheckCompatibility(Book book)
        {
            var issues = new List<string>();
            var wb = book.Wb;
            int outside = 0;
            foreach (var ws in wb.Worksheets)
            {
                var last = ws.LastCellUsed(XLCellsUsedOptions.Contents);
                if (last != null && (last.Address.RowNumber > MaxRows || last.Address.ColumnNumber > MaxCols))
                    outside += ws.CellsUsed(XLCellsUsedOptions.Contents).Count(c => c.Address.RowNumber > MaxRows || c.Address.ColumnNumber > MaxCols);
            }
            if (outside > 0) issues.Add($"This workbook contains data in cells outside of the row and column limit of the selected file format (65,536 rows by 256 columns). Data beyond this grid will be lost ({outside:N0} cell{(outside == 1 ? "" : "s")}).");
            int charts = ChartStore.For(book).Count;
            if (charts > 0) issues.Add($"This workbook contains {charts} chart{(charts == 1 ? "" : "s")} that the Excel 97-2003 format can't keep in Valco Spreadsheets. They will be removed from the saved file.");
            if (book.HasMacros) issues.Add("This workbook contains a VB project. Macros are not saved in the Excel 97-2003 format by Valco Spreadsheets; save as an Excel Macro-Enabled Workbook (.xlsm) to keep them.");
            int cf = 0;
            foreach (var ws in wb.Worksheets)
                cf += ws.ConditionalFormats.Count(c => c.ConditionalFormatType != XLConditionalFormatType.CellIs && c.ConditionalFormatType != XLConditionalFormatType.Expression);
            if (cf > 0) issues.Add("Some cells contain conditional formatting (data bars, color scales, icon sets, top/bottom or duplicate rules) that is not supported in earlier versions of Excel. These rules will not be saved.");
            if (wb.Worksheets.Any(w => w.Tables.Any())) issues.Add("A table in this workbook will be converted to a normal range. Its data and formatting are kept.");
            if (wb.Worksheets.Any(w => w.SparklineGroups.Any())) issues.Add("This workbook contains sparklines. Sparklines will not be saved.");
            return issues;
        }

        // ================================================================== saving
        public static void Export(Book book, string path)
        {
            var wb = book.Wb;
            var hwb = new HSSFWorkbook();
            var ctx = new ExportContext(hwb, wb);

            // the default font (font 0) follows the workbook's default, so unformatted cells look the same
            try
            {
                var f0 = hwb.GetFontAt(0);
                f0.FontName = wb.Style.Font.FontName;
                f0.FontHeightInPoints = wb.Style.Font.FontSize;
            }
            catch { }

            // sheets first, so formulas can refer to any of them
            var sheets = new List<(IXLWorksheet ws, HSSFSheet hs)>();
            foreach (var ws in wb.Worksheets) sheets.Add((ws, (HSSFSheet)hwb.CreateSheet(ws.Name)));
            for (int i = 0; i < sheets.Count; i++)
            {
                var vis = sheets[i].ws.Visibility;
                if (vis == XLWorksheetVisibility.Hidden) hwb.SetSheetHidden(i, SS.SheetVisibility.Hidden);
                else if (vis == XLWorksheetVisibility.VeryHidden) hwb.SetSheetHidden(i, SS.SheetVisibility.VeryHidden);
            }
            ExportNames(wb, hwb);
            for (int i = 0; i < sheets.Count; i++) ExportSheet(ctx, sheets[i].ws, sheets[i].hs, i);

            try
            {
                int active = wb.Worksheets.ToList().FindIndex(w => w.TabActive);
                if (active < 0 || wb.Worksheet(active + 1).Visibility != XLWorksheetVisibility.Visible)
                    active = wb.Worksheets.ToList().FindIndex(w => w.Visibility == XLWorksheetVisibility.Visible);
                if (active >= 0) { hwb.SetActiveSheet(active); hwb.SetSelectedTab(active); hwb.FirstVisibleTab = 0; }
            }
            catch { }

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            hwb.Write(fs);
        }

        class ExportContext
        {
            public readonly HSSFWorkbook H;
            public readonly XLWorkbook Wb;
            public readonly HSSFPalette Pal;
            readonly Dictionary<int, short> _colors = new();
            readonly HashSet<short> _usedSlots = new();
            short _nextSlot = 63;
            public readonly Dictionary<string, SS.ICellStyle> Styles = new();
            public readonly Dictionary<string, SS.IFont> Fonts = new();
            public readonly SS.IDataFormat Formats;

            public ExportContext(HSSFWorkbook h, XLWorkbook wb)
            {
                H = h; Wb = wb; Pal = h.GetCustomPalette(); Formats = h.CreateDataFormat();
                _usedSlots.Add(8); _usedSlots.Add(9); // black and white stay put
            }

            /// <summary>An .xls has a 56-colour palette: exact colours take free slots, then the nearest colour is used.</summary>
            public short ColorIndex(byte r, byte g, byte b)
            {
                int key = (r << 16) | (g << 8) | b;
                if (_colors.TryGetValue(key, out var idx)) return idx;
                var found = Pal.FindColor(r, g, b);
                if (found != null && !_reassigned.Contains(found.Indexed)) { idx = found.Indexed; }
                else
                {
                    while (_nextSlot >= 10 && _usedSlots.Contains(_nextSlot)) _nextSlot--;
                    if (_nextSlot >= 10)
                    {
                        idx = _nextSlot--;
                        Pal.SetColorAtIndex(idx, r, g, b);
                        _reassigned.Add(idx);
                    }
                    else idx = Pal.FindSimilarColor(r, g, b).Indexed;
                }
                _usedSlots.Add(idx);
                _colors[key] = idx;
                return idx;
            }
            readonly HashSet<short> _reassigned = new();

            public short? ColorOf(XLColor c, bool isFont = false)
            {
                var rgb = Rgb(c, Wb, isFont);
                if (rgb == null) return null;
                return ColorIndex(rgb.Value.r, rgb.Value.g, rgb.Value.b);
            }
        }

        static void ExportSheet(ExportContext ctx, IXLWorksheet ws, HSSFSheet hs, int sheetIndex)
        {
            var hwb = ctx.H;
            // default sizes
            try { hs.DefaultRowHeightInPoints = (float)ws.RowHeight; } catch { }
            try { hs.DefaultColumnWidth = Math.Max(1, (int)Math.Round(ws.ColumnWidth - 0.71)); } catch { }

            // columns
            var lastCell = ws.LastCellUsed(XLCellsUsedOptions.All);
            int lastCol = Math.Min(MaxCols, Math.Max(lastCell?.Address.ColumnNumber ?? 0, 0) + 30);
            foreach (var col in ws.ColumnsUsed(XLCellsUsedOptions.All)) lastCol = Math.Max(lastCol, Math.Min(MaxCols, col.ColumnNumber()));
            for (int c = 1; c <= lastCol; c++)
            {
                var col = ws.Column(c);
                if (Math.Abs(col.Width - ws.ColumnWidth) > 0.01) hs.SetColumnWidth(c - 1, (int)Math.Clamp(Math.Round(col.Width * 256), 0, 255 * 256));
                if (col.IsHidden) hs.SetColumnHidden(c - 1, true);
            }

            // cells
            var rows = new Dictionary<int, SS.IRow>();
            SS.IRow RowAt(int r0) { if (!rows.TryGetValue(r0, out var row)) rows[r0] = row = hs.GetRow(r0) ?? hs.CreateRow(r0); return row; }
            foreach (var xr in ws.RowsUsed(XLCellsUsedOptions.All))
            {
                int r = xr.RowNumber();
                if (r > MaxRows) continue;
                bool custom = Math.Abs(xr.Height - ws.RowHeight) > 0.01;
                if (custom || xr.IsHidden)
                {
                    var hr = RowAt(r - 1);
                    if (custom) hr.HeightInPoints = (float)xr.Height;
                    if (xr.IsHidden) hr.ZeroHeight = true;
                }
            }
            // rows that are only resized or hidden (no cells)
            for (int r = 1; r <= Math.Min(MaxRows, (lastCell?.Address.RowNumber ?? 0) + 100); r++)
            {
                if (rows.ContainsKey(r - 1)) continue;
                var xr = ws.Row(r);
                bool custom = Math.Abs(xr.Height - ws.RowHeight) > 0.01;
                if (!custom && !xr.IsHidden) continue;
                var hr = RowAt(r - 1);
                if (custom) hr.HeightInPoints = (float)xr.Height;
                if (xr.IsHidden) hr.ZeroHeight = true;
            }

            HSSFPatriarch patriarch = null;
            HSSFPatriarch Patriarch() => patriarch ??= (HSSFPatriarch)hs.CreateDrawingPatriarch();

            foreach (var cell in ws.CellsUsed(XLCellsUsedOptions.All))
            {
                int r = cell.Address.RowNumber, c = cell.Address.ColumnNumber;
                if (r > MaxRows || c > MaxCols) continue;
                var hc = RowAt(r - 1).CreateCell(c - 1);
                try { hc.CellStyle = StyleFor(ctx, cell.Style); } catch { }
                ExportValue(cell, hc);
                if (cell.HasComment)
                {
                    try
                    {
                        var cm = cell.GetComment();
                        var anchor = new HSSFClientAnchor(0, 0, 0, 0, c, Math.Max(0, r - 2), Math.Min(MaxCols - 1, c + 2), Math.Min(MaxRows - 1, r + 3));
                        var hcm = Patriarch().CreateCellComment(anchor);
                        hcm.String = new HSSFRichTextString(cm.Text);
                        if (!string.IsNullOrEmpty(cm.Author)) hcm.Author = cm.Author;
                        hc.CellComment = hcm;
                    }
                    catch { }
                }
                if (cell.HasHyperlink)
                {
                    try
                    {
                        var hl = cell.GetHyperlink();
                        HSSFHyperlink link;
                        if (hl.IsExternal && hl.ExternalAddress != null)
                        {
                            var addr = hl.ExternalAddress.ToString();
                            link = new HSSFHyperlink(addr.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? SS.HyperlinkType.Email : hl.ExternalAddress.IsFile ? SS.HyperlinkType.File : SS.HyperlinkType.Url) { Address = addr };
                        }
                        else link = new HSSFHyperlink(SS.HyperlinkType.Document) { Address = hl.InternalAddress };
                        hc.Hyperlink = link;
                    }
                    catch { }
                }
            }

            foreach (var m in ws.MergedRanges)
            {
                var a = m.RangeAddress;
                int r1 = a.FirstAddress.RowNumber, c1 = a.FirstAddress.ColumnNumber, r2 = a.LastAddress.RowNumber, c2 = a.LastAddress.ColumnNumber;
                if (r1 > MaxRows || c1 > MaxCols) continue;
                try { hs.AddMergedRegion(new CellRangeAddress(r1 - 1, Math.Min(MaxRows, r2) - 1, c1 - 1, Math.Min(MaxCols, c2) - 1)); } catch { }
            }

            try
            {
                int sr = ws.SheetView.SplitRow, sc = ws.SheetView.SplitColumn;
                if (sr > 0 || sc > 0) hs.CreateFreezePane(Math.Min(sc, MaxCols - 1), Math.Min(sr, MaxRows - 1));
            }
            catch { }
            try { hs.DisplayGridlines = ws.ShowGridLines; } catch { }
            try { if (ws.TabColor != null && ws.TabColor.HasValue) { var t = ctx.ColorOf(ws.TabColor); if (t != null) hs.TabColorIndex = t.Value; } } catch { }
            try { if (ws.AutoFilter != null && ws.AutoFilter.IsEnabled && ws.AutoFilter.Range != null) hs.SetAutoFilter(CellRangeAddress.ValueOf(ws.AutoFilter.Range.RangeAddress.ToStringRelative())); } catch { }
            // tables become normal ranges with their own autofilter
            try { foreach (var t in ws.Tables) if (t.ShowAutoFilter && !(ws.AutoFilter?.IsEnabled ?? false)) { hs.SetAutoFilter(CellRangeAddress.ValueOf(t.RangeAddress.ToStringRelative())); break; } } catch { }

            hs.ForceFormulaRecalculation = true;
            ExportPrintSetup(ws, hs, hwb, sheetIndex);
            ExportPictures(ws, hs, hwb, Patriarch);
            ExportConditionalFormats(ctx, ws, hs);
            ExportValidation(ws, hs);
        }

        static void ExportValue(IXLCell cell, SS.ICell hc)
        {
            if (cell.HasFormula)
            {
                bool ok = false;
                try
                {
                    var f = cell.FormulaA1;
                    if (!string.IsNullOrEmpty(f)) { hc.SetCellFormula(FutureFunctions(f.StartsWith("=") ? f.Substring(1) : f, true)); ok = true; }
                }
                catch { }
                XLCellValue v;
                try { v = cell.Value; } catch { return; }
                if (ok)
                {
                    // the formula's last result, so viewers that don't recalculate still show it
                    try
                    {
                        switch (v.Type)
                        {
                            case XLDataType.Number: hc.SetCellValue(v.GetNumber()); break;
                            case XLDataType.DateTime: hc.SetCellValue(v.GetDateTime().ToOADate()); break;
                            case XLDataType.TimeSpan: hc.SetCellValue(v.GetTimeSpan().TotalDays); break;
                            case XLDataType.Boolean: hc.SetCellValue(v.GetBoolean()); break;
                            case XLDataType.Text: hc.SetCellValue(v.GetText()); break;
                            case XLDataType.Error: hc.SetCellErrorValue(CodeFromError(v.GetError())); break;
                        }
                    }
                    catch { }
                    return;
                }
                SetPlain(hc, v);
                return;
            }
            try { SetPlain(hc, cell.Value); } catch { }
        }

        static void SetPlain(SS.ICell hc, XLCellValue v)
        {
            switch (v.Type)
            {
                case XLDataType.Number: hc.SetCellValue(v.GetNumber()); break;
                case XLDataType.DateTime: hc.SetCellValue(v.GetDateTime().ToOADate()); break;
                case XLDataType.TimeSpan: hc.SetCellValue(v.GetTimeSpan().TotalDays); break;
                case XLDataType.Boolean: hc.SetCellValue(v.GetBoolean()); break;
                case XLDataType.Text:
                    var t = v.GetText();
                    hc.SetCellValue(t.Length > 32767 ? t.Substring(0, 32767) : t);
                    break;
                case XLDataType.Error: hc.SetCellErrorValue(CodeFromError(v.GetError())); break;
            }
        }

        static SS.ICellStyle StyleFor(ExportContext ctx, IXLStyle x)
        {
            var hwb = ctx.H;
            // the parts of a style that an .xls can hold, as a key, so equal styles share one XF record
            var font = FontFor(ctx, x.Font);
            short fmt = FormatFor(ctx, x.NumberFormat);
            short? fillFg = null, fillBg = null; int pattern = 0;
            if (x.Fill.PatternType == XLFillPatternValues.Solid) { fillFg = ctx.ColorOf(x.Fill.BackgroundColor); pattern = fillFg != null ? 1 : 0; }
            else if (x.Fill.PatternType != XLFillPatternValues.None)
            {
                pattern = FillByCode.FirstOrDefault(kv => kv.Value == x.Fill.PatternType).Key;
                fillFg = ctx.ColorOf(x.Fill.PatternColor, true);
                fillBg = ctx.ColorOf(x.Fill.BackgroundColor);
            }
            var b = x.Border;
            var bl = Border(b.LeftBorder); var br = Border(b.RightBorder); var bt = Border(b.TopBorder); var bb = Border(b.BottomBorder);
            short cl = bl == SS.BorderStyle.None ? (short)64 : ctx.ColorOf(b.LeftBorderColor, true) ?? 8;
            short cr = br == SS.BorderStyle.None ? (short)64 : ctx.ColorOf(b.RightBorderColor, true) ?? 8;
            short ct = bt == SS.BorderStyle.None ? (short)64 : ctx.ColorOf(b.TopBorderColor, true) ?? 8;
            short cb = bb == SS.BorderStyle.None ? (short)64 : ctx.ColorOf(b.BottomBorderColor, true) ?? 8;
            var al = x.Alignment;
            var ha = al.Horizontal switch
            {
                XLAlignmentHorizontalValues.Left => SS.HorizontalAlignment.Left,
                XLAlignmentHorizontalValues.Center => SS.HorizontalAlignment.Center,
                XLAlignmentHorizontalValues.Right => SS.HorizontalAlignment.Right,
                XLAlignmentHorizontalValues.Fill => SS.HorizontalAlignment.Fill,
                XLAlignmentHorizontalValues.Justify => SS.HorizontalAlignment.Justify,
                XLAlignmentHorizontalValues.CenterContinuous => SS.HorizontalAlignment.CenterSelection,
                XLAlignmentHorizontalValues.Distributed => SS.HorizontalAlignment.Distributed,
                _ => SS.HorizontalAlignment.General,
            };
            var va = al.Vertical switch
            {
                XLAlignmentVerticalValues.Top => SS.VerticalAlignment.Top,
                XLAlignmentVerticalValues.Center => SS.VerticalAlignment.Center,
                XLAlignmentVerticalValues.Justify => SS.VerticalAlignment.Justify,
                XLAlignmentVerticalValues.Distributed => SS.VerticalAlignment.Distributed,
                _ => SS.VerticalAlignment.Bottom,
            };
            int tr = al.TextRotation;
            short rot = tr == 255 ? (short)0xFF : tr > 90 && tr <= 180 ? (short)(90 - tr) : (short)Math.Clamp(tr, 0, 90);
            short indent = (short)Math.Clamp(al.Indent, 0, 15);
            bool locked = x.Protection.Locked, hidden = x.Protection.Hidden;

            var key = string.Join("|", font.Index, fmt, pattern, fillFg, fillBg, bl, br, bt, bb, cl, cr, ct, cb, ha, va, al.WrapText, al.ShrinkToFit, rot, indent, locked, hidden);
            if (ctx.Styles.TryGetValue(key, out var s)) return s;
            if (ctx.Styles.Count >= 3900) return hwb.GetCellStyleAt(15); // an .xls holds about 4,000 cell formats

            s = hwb.CreateCellStyle();
            s.SetFont(font);
            s.DataFormat = fmt;
            if (pattern > 0)
            {
                s.FillPattern = (SS.FillPattern)pattern;
                if (fillFg != null) s.FillForegroundColor = fillFg.Value;
                if (fillBg != null) s.FillBackgroundColor = fillBg.Value;
            }
            s.BorderLeft = bl; s.BorderRight = br; s.BorderTop = bt; s.BorderBottom = bb;
            s.LeftBorderColor = cl; s.RightBorderColor = cr; s.TopBorderColor = ct; s.BottomBorderColor = cb;
            s.Alignment = ha; s.VerticalAlignment = va;
            s.WrapText = al.WrapText; s.ShrinkToFit = al.ShrinkToFit;
            s.Rotation = rot; s.Indention = indent;
            s.IsLocked = locked; s.IsHidden = hidden;
            ctx.Styles[key] = s;
            return s;
        }

        static SS.IFont FontFor(ExportContext ctx, IXLFont f)
        {
            short? color = ctx.ColorOf(f.FontColor, true);
            if (color == 8) color = null; // black = automatic
            var under = f.Underline switch
            {
                XLFontUnderlineValues.Single => SS.FontUnderlineType.Single,
                XLFontUnderlineValues.Double => SS.FontUnderlineType.Double,
                XLFontUnderlineValues.SingleAccounting => SS.FontUnderlineType.SingleAccounting,
                XLFontUnderlineValues.DoubleAccounting => SS.FontUnderlineType.DoubleAccounting,
                _ => SS.FontUnderlineType.None,
            };
            var offset = f.VerticalAlignment switch
            {
                XLFontVerticalTextAlignmentValues.Superscript => SS.FontSuperScript.Super,
                XLFontVerticalTextAlignmentValues.Subscript => SS.FontSuperScript.Sub,
                _ => SS.FontSuperScript.None,
            };
            var key = string.Join("|", f.FontName, f.FontSize, f.Bold, f.Italic, f.Strikethrough, under, offset, color);
            if (ctx.Fonts.TryGetValue(key, out var hf)) return hf;
            // font 0 is the workbook default: reuse it for unformatted text
            var f0 = ctx.H.GetFontAt(0);
            if (ctx.Fonts.Count == 0 && f0.FontName == f.FontName && Math.Abs(f0.FontHeightInPoints - f.FontSize) < 0.01 && !f.Bold && !f.Italic && !f.Strikethrough && under == SS.FontUnderlineType.None && offset == SS.FontSuperScript.None && color == null)
            {
                ctx.Fonts[key] = f0;
                return f0;
            }
            hf = ctx.H.CreateFont();
            hf.FontName = f.FontName;
            hf.FontHeightInPoints = f.FontSize;
            hf.IsBold = f.Bold;
            hf.IsItalic = f.Italic;
            hf.IsStrikeout = f.Strikethrough;
            hf.Underline = under;
            hf.TypeOffset = offset;
            hf.Color = color ?? AutoFontColor;
            ctx.Fonts[key] = hf;
            return hf;
        }

        static short FormatFor(ExportContext ctx, IXLNumberFormat nf)
        {
            var s = nf.Format;
            if (string.IsNullOrEmpty(s))
            {
                int id = nf.NumberFormatId;
                if (id >= 0 && id <= 49) return (short)id;
                return 0;
            }
            if (s == "General") return 0;
            try { return ctx.Formats.GetFormat(s); } catch { return 0; }
        }

        static SS.BorderStyle Border(XLBorderStyleValues v) => v switch
        {
            XLBorderStyleValues.None => SS.BorderStyle.None,
            XLBorderStyleValues.SlantDashDot => SS.BorderStyle.SlantedDashDot,
            _ => Enum.TryParse<SS.BorderStyle>(v.ToString(), out var b) ? b : SS.BorderStyle.Thin,
        };

        static void ExportNames(XLWorkbook wb, HSSFWorkbook hwb)
        {
            void Add(string name, string refers, int sheet)
            {
                try
                {
                    var n = hwb.CreateName();
                    n.NameName = name;
                    if (sheet >= 0) n.SheetIndex = sheet;
                    n.RefersToFormula = refers.TrimStart('=');
                }
                catch { }
            }
            foreach (var dn in wb.DefinedNames) Add(dn.Name, dn.RefersTo, -1);
            int i = 0;
            foreach (var ws in wb.Worksheets)
            {
                foreach (var dn in ws.DefinedNames) Add(dn.Name, dn.RefersTo, i);
                i++;
            }
        }

        static void ExportPrintSetup(IXLWorksheet ws, HSSFSheet hs, HSSFWorkbook hwb, int sheetIndex)
        {
            try
            {
                var ps = ws.PageSetup;
                var p = hs.PrintSetup;
                p.ValidSettings = false; // (the BIFF bit means "settings not valid")
                p.NoOrientation = false;
                p.Landscape = ps.PageOrientation == XLPageOrientation.Landscape;
                if ((int)ps.PaperSize > 0) p.PaperSize = (short)(int)ps.PaperSize;
                if (ps.PagesWide > 0 || ps.PagesTall > 0)
                {
                    hs.FitToPage = true;
                    p.FitWidth = (short)Math.Max(0, ps.PagesWide);
                    p.FitHeight = (short)Math.Max(0, ps.PagesTall);
                }
                else if (ps.Scale > 0) p.Scale = (short)ps.Scale;
                hs.SetMargin(SS.MarginType.LeftMargin, ps.Margins.Left);
                hs.SetMargin(SS.MarginType.RightMargin, ps.Margins.Right);
                hs.SetMargin(SS.MarginType.TopMargin, ps.Margins.Top);
                hs.SetMargin(SS.MarginType.BottomMargin, ps.Margins.Bottom);
                p.HeaderMargin = ps.Margins.Header;
                p.FooterMargin = ps.Margins.Footer;
                hs.HorizontallyCenter = ps.CenterHorizontally;
                hs.VerticallyCenter = ps.CenterVertically;
                hs.IsPrintGridlines = ps.ShowGridlines;
                var areas = ps.PrintAreas.Select(a => a.RangeAddress.ToStringFixed()).ToList();
                if (areas.Count > 0) hwb.SetPrintArea(sheetIndex, string.Join(",", areas));
                if (ps.FirstRowToRepeatAtTop > 0) hs.RepeatingRows = new CellRangeAddress(ps.FirstRowToRepeatAtTop - 1, ps.LastRowToRepeatAtTop - 1, -1, -1);
                if (ps.FirstColumnToRepeatAtLeft > 0) hs.RepeatingColumns = new CellRangeAddress(-1, -1, ps.FirstColumnToRepeatAtLeft - 1, ps.LastColumnToRepeatAtLeft - 1);
            }
            catch { }
        }

        static void ExportPictures(IXLWorksheet ws, HSSFSheet hs, HSSFWorkbook hwb, Func<HSSFPatriarch> patriarch)
        {
            foreach (var pic in ws.Pictures)
            {
                try
                {
                    var type = pic.Format switch
                    {
                        XLPictureFormat.Png => SS.PictureType.PNG,
                        XLPictureFormat.Jpeg => SS.PictureType.JPEG,
                        XLPictureFormat.Emf => SS.PictureType.EMF,
                        XLPictureFormat.Wmf => SS.PictureType.WMF,
                        XLPictureFormat.Bmp => SS.PictureType.DIB,
                        _ => SS.PictureType.None,
                    };
                    byte[] data;
                    using (var ms = new MemoryStream()) { pic.ImageStream.Position = 0; pic.ImageStream.CopyTo(ms); data = ms.ToArray(); }
                    if (type == SS.PictureType.None || type == SS.PictureType.DIB) { data = ToPng(data); type = SS.PictureType.PNG; if (data == null) continue; }
                    int idx = hwb.AddPicture(data, type);

                    var tl = pic.TopLeftCell;
                    var off = pic.GetOffset(XLMarkerPosition.TopLeft);
                    int col1 = tl.Address.ColumnNumber - 1, row1 = tl.Address.RowNumber - 1;
                    double ox = off.X, oy = off.Y;
                    // walk to the bottom-right corner using the real column widths and row heights
                    double right = ox + pic.Width, bottom = oy + pic.Height;
                    int col2 = col1, row2 = row1;
                    while (col2 < MaxCols - 1 && right > ColPx(ws, col2)) { right -= ColPx(ws, col2); col2++; }
                    while (row2 < MaxRows - 1 && bottom > RowPx(ws, row2)) { bottom -= RowPx(ws, row2); row2++; }
                    int dx1 = Frac(ox, ColPx(ws, col1), 1023), dy1 = Frac(oy, RowPx(ws, row1), 255);
                    int dx2 = Frac(right, ColPx(ws, col2), 1023), dy2 = Frac(bottom, RowPx(ws, row2), 255);
                    var anchor = new HSSFClientAnchor(dx1, dy1, dx2, dy2, col1, row1, col2, row2) { AnchorType = SS.AnchorType.MoveDontResize };
                    patriarch().CreatePicture(anchor, idx);
                }
                catch { }
            }
        }

        static int Frac(double px, double size, int max) => size <= 0 ? 0 : (int)Math.Clamp(Math.Round(px / size * (max + 1)), 0, max);

        /// <summary>GIF, BMP and TIFF pictures are stored as PNG (BIFF8 only holds PNG, JPEG, EMF, WMF and DIB).</summary>
        static byte[] ToPng(byte[] data)
        {
            try
            {
                using var img = SixLabors.ImageSharp.Image.Load(data);
                using var ms = new MemoryStream();
                SixLabors.ImageSharp.ImageExtensions.SaveAsPng(img, ms);
                return ms.ToArray();
            }
            catch { return null; }
        }

        static void ExportConditionalFormats(ExportContext ctx, IXLWorksheet ws, HSSFSheet hs)
        {
            var scf = hs.SheetConditionalFormatting;
            foreach (var cf in ws.ConditionalFormats)
            {
                try
                {
                    SS.IConditionalFormattingRule rule;
                    string V(int i) => cf.Values.TryGetValue(i, out var f) ? f.Value?.TrimStart('=') : null;
                    if (cf.ConditionalFormatType == XLConditionalFormatType.Expression)
                        rule = scf.CreateConditionalFormattingRule(V(1));
                    else if (cf.ConditionalFormatType == XLConditionalFormatType.CellIs)
                    {
                        var op = cf.Operator switch
                        {
                            XLCFOperator.Between => SS.ComparisonOperator.Between,
                            XLCFOperator.NotBetween => SS.ComparisonOperator.NotBetween,
                            XLCFOperator.Equal => SS.ComparisonOperator.Equal,
                            XLCFOperator.NotEqual => SS.ComparisonOperator.NotEqual,
                            XLCFOperator.GreaterThan => SS.ComparisonOperator.GreaterThan,
                            XLCFOperator.LessThan => SS.ComparisonOperator.LessThan,
                            XLCFOperator.EqualOrGreaterThan => SS.ComparisonOperator.GreaterThanOrEqual,
                            XLCFOperator.EqualOrLessThan => SS.ComparisonOperator.LessThanOrEqual,
                            _ => SS.ComparisonOperator.NoComparison,
                        };
                        if (op == SS.ComparisonOperator.NoComparison) continue;
                        rule = op == SS.ComparisonOperator.Between || op == SS.ComparisonOperator.NotBetween
                            ? scf.CreateConditionalFormattingRule(op, V(1), V(2))
                            : scf.CreateConditionalFormattingRule(op, V(1));
                    }
                    else continue;

                    var st = cf.Style;
                    var ff = rule.CreateFontFormatting();
                    ff.SetFontStyle(st.Font.Italic, st.Font.Bold);
                    var fc = ctx.ColorOf(st.Font.FontColor, true);
                    if (fc != null && fc != 8) ff.FontColorIndex = fc.Value;
                    if (st.Fill.PatternType != XLFillPatternValues.None)
                    {
                        var bg = ctx.ColorOf(st.Fill.BackgroundColor);
                        if (bg != null)
                        {
                            var pf = rule.CreatePatternFormatting();
                            pf.FillBackgroundColor = bg.Value;
                            pf.FillPattern = SS.FillPattern.SolidForeground;
                        }
                    }
                    var regions = cf.Ranges.Select(r => r.RangeAddress)
                        .Where(a => a.FirstAddress.RowNumber <= MaxRows && a.FirstAddress.ColumnNumber <= MaxCols)
                        .Select(a => new CellRangeAddress(a.FirstAddress.RowNumber - 1, Math.Min(MaxRows, a.LastAddress.RowNumber) - 1, a.FirstAddress.ColumnNumber - 1, Math.Min(MaxCols, a.LastAddress.ColumnNumber) - 1))
                        .ToArray();
                    if (regions.Length > 0) scf.AddConditionalFormatting(regions, rule);
                }
                catch { }
            }
        }

        static void ExportValidation(IXLWorksheet ws, HSSFSheet hs)
        {
            var helper = hs.GetDataValidationHelper();
            foreach (var dv in ws.DataValidations)
            {
                try
                {
                    SS.IDataValidationConstraint con;
                    string v1 = dv.MinValue?.TrimStart('='), v2 = dv.MaxValue?.TrimStart('=');
                    int op = dv.Operator switch
                    {
                        XLOperator.Between => SS.OperatorType.BETWEEN,
                        XLOperator.NotBetween => SS.OperatorType.NOT_BETWEEN,
                        XLOperator.EqualTo => SS.OperatorType.EQUAL,
                        XLOperator.NotEqualTo => SS.OperatorType.NOT_EQUAL,
                        XLOperator.GreaterThan => SS.OperatorType.GREATER_THAN,
                        XLOperator.LessThan => SS.OperatorType.LESS_THAN,
                        XLOperator.EqualOrGreaterThan => SS.OperatorType.GREATER_OR_EQUAL,
                        XLOperator.EqualOrLessThan => SS.OperatorType.LESS_OR_EQUAL,
                        _ => SS.OperatorType.BETWEEN,
                    };
                    bool two = op == SS.OperatorType.BETWEEN || op == SS.OperatorType.NOT_BETWEEN;
                    switch (dv.AllowedValues)
                    {
                        case XLAllowedValues.List:
                            var src = dv.Value ?? v1 ?? "";
                            if (src.StartsWith("\"")) con = helper.CreateExplicitListConstraint(src.Trim('"').Split(','));
                            else con = helper.CreateFormulaListConstraint(src.TrimStart('='));
                            break;
                        case XLAllowedValues.WholeNumber: con = helper.CreateintConstraint(op, v1, two ? v2 : null); break;
                        case XLAllowedValues.Decimal: con = helper.CreateDecimalConstraint(op, v1, two ? v2 : null); break;
                        case XLAllowedValues.TextLength: con = helper.CreateTextLengthConstraint(op, v1, two ? v2 : null); break;
                        case XLAllowedValues.Date: con = helper.CreateDateConstraint(op, v1, two ? v2 : null, null); break;
                        case XLAllowedValues.Time: con = helper.CreateTimeConstraint(op, v1, two ? v2 : null); break;
                        case XLAllowedValues.Custom: con = helper.CreateCustomConstraint((dv.Value ?? v1 ?? "").TrimStart('=')); break;
                        default: continue;
                    }
                    var regions = new CellRangeAddressList();
                    foreach (var r in dv.Ranges)
                    {
                        var a = r.RangeAddress;
                        if (a.FirstAddress.RowNumber > MaxRows || a.FirstAddress.ColumnNumber > MaxCols) continue;
                        regions.AddCellRangeAddress(a.FirstAddress.RowNumber - 1, a.FirstAddress.ColumnNumber - 1, Math.Min(MaxRows, a.LastAddress.RowNumber) - 1, Math.Min(MaxCols, a.LastAddress.ColumnNumber) - 1);
                    }
                    if (regions.CountRanges() == 0) continue;
                    var hv = helper.CreateValidation(con, regions);
                    hv.EmptyCellAllowed = dv.IgnoreBlanks;
                    hv.SuppressDropDownArrow = !dv.InCellDropdown;
                    hv.ShowErrorBox = dv.ShowErrorMessage;
                    hv.ShowPromptBox = dv.ShowInputMessage;
                    if (!string.IsNullOrEmpty(dv.ErrorMessage) || !string.IsNullOrEmpty(dv.ErrorTitle)) hv.CreateErrorBox(dv.ErrorTitle ?? "", dv.ErrorMessage ?? "");
                    if (!string.IsNullOrEmpty(dv.InputMessage) || !string.IsNullOrEmpty(dv.InputTitle)) hv.CreatePromptBox(dv.InputTitle ?? "", dv.InputMessage ?? "");
                    hs.AddValidationData(hv);
                }
                catch { }
            }
        }

        // ================================================================== colours
        static readonly Dictionary<XLThemeColor, (byte r, byte g, byte b)> Office2007 = new()
        {
            [XLThemeColor.Background1] = (255, 255, 255), [XLThemeColor.Text1] = (0, 0, 0),
            [XLThemeColor.Background2] = (0xEE, 0xEC, 0xE1), [XLThemeColor.Text2] = (0x1F, 0x49, 0x7D),
            [XLThemeColor.Accent1] = (0x4F, 0x81, 0xBD), [XLThemeColor.Accent2] = (0xC0, 0x50, 0x4D),
            [XLThemeColor.Accent3] = (0x9B, 0xBB, 0x59), [XLThemeColor.Accent4] = (0x80, 0x64, 0xA2),
            [XLThemeColor.Accent5] = (0x4B, 0xAC, 0xC6), [XLThemeColor.Accent6] = (0xF7, 0x96, 0x46),
            [XLThemeColor.Hyperlink] = (0, 0, 255), [XLThemeColor.FollowedHyperlink] = (0x80, 0, 0x80),
        };

        /// <summary>An XLColor as plain RGB (theme colours resolved and tinted). Null for "automatic".</summary>
        public static (byte r, byte g, byte b)? Rgb(XLColor c, XLWorkbook wb, bool isFont = false)
        {
            if (c == null || !c.HasValue) return null;
            try
            {
                switch (c.ColorType)
                {
                    case XLColorType.Theme:
                        (byte r, byte g, byte b) bc;
                        try { var t = wb.Theme.ResolveThemeColor(c.ThemeColor).Color; bc = (t.R, t.G, t.B); }
                        catch { bc = Office2007.TryGetValue(c.ThemeColor, out var oc) ? oc : ((byte)0, (byte)0, (byte)0); }
                        return Tint(bc, c.ThemeTint);
                    case XLColorType.Indexed:
                        if (c.Indexed == 64 || c.Indexed == 65) return null;
                        goto default;
                    default:
                        var d = c.Color;
                        if (d.A == 0 && d.R == 0 && d.G == 0 && d.B == 0) return null;
                        return (d.R, d.G, d.B);
                }
            }
            catch { return null; }
        }

        static (byte, byte, byte) Tint((byte r, byte g, byte b) c, double tint)
        {
            if (Math.Abs(tint) < 0.0001) return c;
            double r = c.r / 255.0, g = c.g / 255.0, b = c.b / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double h = 0, s = 0, l = (max + min) / 2;
            if (max != min)
            {
                double d = max - min;
                s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
                if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
                else if (max == g) h = (b - r) / d + 2;
                else h = (r - g) / d + 4;
                h /= 6;
            }
            l = tint < 0 ? l * (1 + tint) : l * (1 - tint) + tint;
            l = Math.Clamp(l, 0, 1);
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
            double Hue(double t) { if (t < 0) t += 1; if (t > 1) t -= 1; if (t < 1.0 / 6) return p + (q - p) * 6 * t; if (t < 0.5) return q; if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6; return p; }
            double rr = s == 0 ? l : Hue(h + 1.0 / 3), gg = s == 0 ? l : Hue(h), bb = s == 0 ? l : Hue(h - 1.0 / 3);
            return ((byte)Math.Round(rr * 255), (byte)Math.Round(gg * 255), (byte)Math.Round(bb * 255));
        }
    }
}
