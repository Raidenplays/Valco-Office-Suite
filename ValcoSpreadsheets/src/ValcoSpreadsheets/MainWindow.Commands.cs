using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClosedXML.Excel;
using ValcoSpreadsheets.Grid;
using ValcoSpreadsheets.Model;

namespace ValcoSpreadsheets
{
    /// <summary>What was last copied or cut (Excel's "marching ants" range).</summary>
    public class ClipRef
    {
        public Book Book;
        public string Sheet;
        public CellRect Rect;
        public bool Cut;
        public string Text;
    }

    public partial class MainWindow
    {
        static ClipRef _clip;

        // ===================================================================== styles on a selection
        /// <summary>
        /// Applies a style change to every selected rectangle. Whole columns/rows change the column/row style
        /// (so new cells get it too) instead of touching a million cells.
        /// </summary>
        void StyleSelection(string name, Action<IXLStyle> apply)
        {
            Change(name, () =>
            {
                foreach (var r in G.View.Selection) StyleRect(r, apply);
            });
        }

        void StyleRect(CellRect r, Action<IXLStyle> apply)
        {
            if (r.IsWholeColumns && r.IsWholeRows) { apply(WS.Style); return; }
            if (r.IsWholeColumns)
            {
                foreach (var c in WS.Columns(r.C1, r.C2)) apply(c.Style);
                return;
            }
            if (r.IsWholeRows)
            {
                foreach (var row in WS.Rows(r.R1, r.R2)) apply(row.Style);
                return;
            }
            apply(WS.Range(r.R1, r.C1, r.R2, r.C2).Style);
        }

        IXLStyle ActiveStyle => G.ActiveCell.Style;

        void ToggleFont(string what)
        {
            var f = ActiveStyle.Font;
            switch (what)
            {
                case "Bold": bool b = !f.Bold; StyleSelection("Bold", s => s.Font.Bold = b); break;
                case "Italic": bool i = !f.Italic; StyleSelection("Italic", s => s.Font.Italic = i); break;
                case "Underline": var u = f.Underline == XLFontUnderlineValues.None ? XLFontUnderlineValues.Single : XLFontUnderlineValues.None; StyleSelection("Underline", s => s.Font.Underline = u); break;
                case "DoubleUnderline": var du = f.Underline == XLFontUnderlineValues.Double ? XLFontUnderlineValues.None : XLFontUnderlineValues.Double; StyleSelection("Double Underline", s => s.Font.Underline = du); break;
                case "Strike": bool st = !f.Strikethrough; StyleSelection("Strikethrough", s => s.Font.Strikethrough = st); break;
            }
        }

        void SetFontName(string name) { if (!string.IsNullOrWhiteSpace(name)) StyleSelection("Font", s => s.Font.FontName = name); }
        void SetFontSize(double size) { if (size >= 1 && size <= 409) StyleSelection("Font Size", s => s.Font.FontSize = size); }

        static readonly double[] FontSizes = { 8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72 };
        void GrowFont(int dir)
        {
            double cur = ActiveStyle.Font.FontSize;
            double next = dir > 0 ? FontSizes.FirstOrDefault(s => s > cur + 0.01) : FontSizes.LastOrDefault(s => s < cur - 0.01);
            if (next <= 0) next = dir > 0 ? Math.Min(409, cur + 10) : Math.Max(1, cur - 1);
            SetFontSize(next);
        }

        void SetFontColor(Color? c) => StyleSelection("Font Color", s => s.Font.FontColor = c.HasValue ? XlColors.FromWpf(c.Value) : XLColor.FromTheme(XLThemeColor.Text1));
        void SetFillColor(Color? c) => StyleSelection("Fill Color", s =>
        {
            if (c.HasValue) { s.Fill.PatternType = XLFillPatternValues.Solid; s.Fill.BackgroundColor = XlColors.FromWpf(c.Value); }
            else s.Fill.PatternType = XLFillPatternValues.None;
        });

        void SetHAlign(XLAlignmentHorizontalValues h)
        {
            var cur = ActiveStyle.Alignment.Horizontal;
            var v = cur == h ? XLAlignmentHorizontalValues.General : h;
            StyleSelection("Alignment", s => s.Alignment.Horizontal = v);
        }
        void SetVAlign(XLAlignmentVerticalValues v) => StyleSelection("Alignment", s => s.Alignment.Vertical = v);
        void ToggleWrap() { bool w = !ActiveStyle.Alignment.WrapText; StyleSelection("Wrap Text", s => s.Alignment.WrapText = w); }
        void Indent(int d) => StyleSelection(d > 0 ? "Increase Indent" : "Decrease Indent", s =>
        {
            s.Alignment.Indent = Math.Clamp(s.Alignment.Indent + d, 0, 15);
            if (s.Alignment.Indent > 0 && (s.Alignment.Horizontal == XLAlignmentHorizontalValues.General || s.Alignment.Horizontal == XLAlignmentHorizontalValues.Center)) s.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        });
        void SetRotation(int deg) => StyleSelection("Orientation", s => s.Alignment.TextRotation = deg);

        void ApplyNumberFormat(string fmt, string name) => StyleSelection(name ?? "Number Format", s => s.NumberFormat.Format = fmt);

        void ChangeDecimals(int d)
        {
            var c = G.ActiveCell;
            var f = c.Style.NumberFormat.Format;
            if (string.IsNullOrEmpty(f) || f == "General")
            {
                // like Excel, start from the decimals the value shows now
                int dec = 0;
                try { var s = c.GetFormattedString(); int p = s.IndexOf(CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator, StringComparison.Ordinal); dec = p >= 0 ? s.Length - p - 1 : 0; } catch { }
                dec = Math.Max(0, dec + d);
                ApplyNumberFormat(dec == 0 ? "0" : "0." + new string('0', dec), "Number Format");
                return;
            }
            string nf = AdjustDecimals(f, d);
            ApplyNumberFormat(nf, "Number Format");
        }

        static string AdjustDecimals(string fmt, int d)
        {
            // works on each section of the format (positive;negative;zero)
            return string.Join(";", fmt.Split(';').Select(sec =>
            {
                int dot = sec.IndexOf('.');
                if (d > 0)
                {
                    if (dot >= 0) return sec.Insert(dot + 1, "0");
                    int last = sec.LastIndexOfAny(new[] { '0', '#' });
                    return last >= 0 ? sec.Insert(last + 1, ".0") : sec;
                }
                if (dot < 0) return sec;
                int zeros = 0; int i = dot + 1;
                while (i < sec.Length && (sec[i] == '0' || sec[i] == '#')) { zeros++; i++; }
                if (zeros <= 1) return sec.Remove(dot, 1 + zeros);
                return sec.Remove(dot + 1, 1);
            }));
        }

        // ===================================================================== borders
        public enum BorderKind { Bottom, Top, Left, Right, None, All, Outside, ThickBox, BottomDouble, ThickBottom, TopBottom, TopThickBottom, TopDoubleBottom }

        void ApplyBorder(BorderKind k, Color? color = null, XLBorderStyleValues? lineStyle = null)
        {
            var xc = color.HasValue ? XlColors.FromWpf(color.Value) : XLColor.Black;
            var thin = lineStyle ?? XLBorderStyleValues.Thin;
            Change("Borders", () =>
            {
                foreach (var r in G.View.Selection)
                {
                    if (r.IsWholeColumns || r.IsWholeRows) { StyleRect(r, s => { if (k == BorderKind.None) s.Border.OutsideBorder = XLBorderStyleValues.None; else { s.Border.BottomBorder = thin; s.Border.BottomBorderColor = xc; } }); continue; }
                    var range = WS.Range(r.R1, r.C1, r.R2, r.C2);
                    var b = range.Style.Border;
                    switch (k)
                    {
                        case BorderKind.None:
                            b.OutsideBorder = XLBorderStyleValues.None; b.InsideBorder = XLBorderStyleValues.None; b.DiagonalBorder = XLBorderStyleValues.None; break;
                        case BorderKind.All:
                            b.OutsideBorder = thin; b.InsideBorder = thin; b.OutsideBorderColor = xc; b.InsideBorderColor = xc; break;
                        case BorderKind.Outside:
                            b.OutsideBorder = thin; b.OutsideBorderColor = xc; break;
                        case BorderKind.ThickBox:
                            b.OutsideBorder = XLBorderStyleValues.Thick; b.OutsideBorderColor = xc; break;
                        case BorderKind.Bottom: Edge(range.LastRow(), "B", thin, xc); break;
                        case BorderKind.Top: Edge(range.FirstRow(), "T", thin, xc); break;
                        case BorderKind.Left: EdgeC(range.FirstColumn(), "L", thin, xc); break;
                        case BorderKind.Right: EdgeC(range.LastColumn(), "R", thin, xc); break;
                        case BorderKind.BottomDouble: Edge(range.LastRow(), "B", XLBorderStyleValues.Double, xc); break;
                        case BorderKind.ThickBottom: Edge(range.LastRow(), "B", XLBorderStyleValues.Thick, xc); break;
                        case BorderKind.TopBottom: Edge(range.FirstRow(), "T", thin, xc); Edge(range.LastRow(), "B", thin, xc); break;
                        case BorderKind.TopThickBottom: Edge(range.FirstRow(), "T", thin, xc); Edge(range.LastRow(), "B", XLBorderStyleValues.Thick, xc); break;
                        case BorderKind.TopDoubleBottom: Edge(range.FirstRow(), "T", thin, xc); Edge(range.LastRow(), "B", XLBorderStyleValues.Double, xc); break;
                    }
                }
            });
        }

        static void Edge(IXLRangeRow row, string side, XLBorderStyleValues st, XLColor c)
        {
            if (side == "B") { row.Style.Border.BottomBorder = st; row.Style.Border.BottomBorderColor = c; }
            else { row.Style.Border.TopBorder = st; row.Style.Border.TopBorderColor = c; }
        }
        static void EdgeC(IXLRangeColumn col, string side, XLBorderStyleValues st, XLColor c)
        {
            if (side == "L") { col.Style.Border.LeftBorder = st; col.Style.Border.LeftBorderColor = c; }
            else { col.Style.Border.RightBorder = st; col.Style.Border.RightBorderColor = c; }
        }

        // ===================================================================== merge
        void MergeCells(string how)
        {
            var sel = G.View.Selection.ToList();
            if (how != "Unmerge" && sel.Any(r => r.Count > 1))
            {
                // Excel keeps only the upper-left value
                bool multi = sel.Any(r => WS.Range(r.R1, r.C1, r.R2, r.C2).CellsUsed(XLCellsUsedOptions.Contents).Count() > 1);
                if (multi && VistaDialog.Show(this, "The selection contains multiple data values. Merging into one cell will keep the upper-left most data only.", "Valco Spreadsheets", MessageBoxButton.OKCancel, DlgIcon.Warning) != MessageBoxResult.OK) return;
            }
            Change(how == "Unmerge" ? "Unmerge Cells" : "Merge Cells", () =>
            {
                foreach (var r in sel)
                {
                    if (r.IsWholeColumns || r.IsWholeRows) continue;
                    var range = WS.Range(r.R1, r.C1, r.R2, r.C2);
                    if (how == "Unmerge") { foreach (var m in WS.MergedRanges.Where(m => r.Intersects(Rect(m))).ToList()) m.Unmerge(); continue; }
                    if (how == "Across")
                    {
                        for (int row = r.R1; row <= r.R2; row++)
                        {
                            var line = WS.Range(row, r.C1, row, r.C2);
                            foreach (var cell in line.CellsUsed(XLCellsUsedOptions.Contents).Skip(1).ToList()) cell.Clear(XLClearOptions.Contents);
                            line.Merge();
                        }
                        continue;
                    }
                    foreach (var cell in range.CellsUsed(XLCellsUsedOptions.Contents).Where(c => c.Address.RowNumber != r.R1 || c.Address.ColumnNumber != r.C1).ToList()) cell.Clear(XLClearOptions.Contents);
                    range.Merge();
                    if (how == "Center") range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
            }, sel);
            G.Relayout();
        }

        static CellRect Rect(IXLRange r) { var a = r.RangeAddress; return new CellRect(a.FirstAddress.RowNumber, a.FirstAddress.ColumnNumber, a.LastAddress.RowNumber, a.LastAddress.ColumnNumber); }

        // ===================================================================== clear
        void ClearSelection(XLClearOptions what, string name)
        {
            Change(name, () =>
            {
                foreach (var r in G.View.Selection)
                {
                    if (what == XLClearOptions.All || what.HasFlag(XLClearOptions.NormalFormats))
                    {
                        if (r.IsWholeColumns) foreach (var c in WS.Columns(r.C1, r.C2)) c.Style = WS.Style;
                        if (r.IsWholeRows) foreach (var row in WS.Rows(r.R1, r.R2)) row.Style = WS.Style;
                    }
                    var range = WS.Range(r.R1, r.C1, r.R2, r.C2);
                    foreach (var cell in range.CellsUsed(XLCellsUsedOptions.All).ToList())
                    {
                        cell.Clear(what);
                        if (what == XLClearOptions.All || what.HasFlag(XLClearOptions.NormalFormats)) cell.Style = WS.Style;
                    }
                    if (what == XLClearOptions.All) foreach (var m in WS.MergedRanges.Where(m => r.Intersects(Rect(m))).ToList()) m.Unmerge();
                }
            });
            G.Relayout();
        }

        // ===================================================================== clipboard
        void CopySelection(bool cut)
        {
            if (G.View.Selection.Count > 1)
            {
                VistaDialog.Show(this, "That command cannot be used on multiple selections.", "Valco Spreadsheets", MessageBoxButton.OK, DlgIcon.Warning);
                return;
            }
            var r = G.ActiveRect;
            // copying whole columns/rows copies the used part
            if (r.IsWholeColumns || r.IsWholeRows)
            {
                int lr = Math.Max(1, G.Layout.LastRow), lc = Math.Max(1, G.Layout.LastCol);
                r = new CellRect(r.R1, r.C1, r.IsWholeColumns ? Math.Max(r.R1, lr) : r.R2, r.IsWholeRows ? Math.Max(r.C1, lc) : r.C2);
            }
            var text = Ops.ToText(WS, r);
            _clip = new ClipRef { Book = _book, Sheet = WS.Name, Rect = r, Cut = cut, Text = text };
            SetClipboard(text);
            G.SetMarquee(r);
            StatusMsg.Text = cut ? "Select destination and press ENTER or choose Paste" : "Select destination and press ENTER or choose Paste";
        }

        static void SetClipboard(string text)
        {
            for (int i = 0; i < 5; i++)
            {
                try { Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, text), true); return; }
                catch { System.Threading.Thread.Sleep(40); }
            }
        }

        static string GetClipboardText()
        {
            for (int i = 0; i < 5; i++)
            {
                try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
                catch { System.Threading.Thread.Sleep(40); }
            }
            return null;
        }

        void Paste() => PasteAs(Ops.PasteWhat.All, false, false);

        void PasteAs(Ops.PasteWhat what, bool transpose, bool skipBlanks)
        {
            var clipText = GetClipboardText();
            int row = G.ActiveRect.R1, col = G.ActiveRect.C1;
            bool internalClip = _clip != null && _clip.Text == clipText && _clip.Book != null && _clip.Book.Wb.Worksheets.Any(w => w.Name == _clip.Sheet);
            if (internalClip)
            {
                var src = _clip.Rect;
                var fromWs = _clip.Book.Wb.Worksheet(_clip.Sheet);
                int rows = transpose ? src.Cols : src.Rows, cols = transpose ? src.Rows : src.Cols;
                // a selection that is a multiple of the copied size is filled with copies, like Excel
                var sel = G.ActiveRect;
                int repR = Math.Max(1, sel.Rows % rows == 0 ? sel.Rows / rows : 1), repC = Math.Max(1, sel.Cols % cols == 0 ? sel.Cols / cols : 1);
                if (sel.IsWholeColumns || sel.IsWholeRows) { repR = repC = 1; }
                var dst = new CellRect(row, col, row + rows * repR - 1, col + cols * repC - 1);
                if (dst.R2 > SheetLimits.Rows || dst.C2 > SheetLimits.Cols) { VistaDialog.Show(this, "The information cannot be pasted because the Copy area and the paste area are not the same size and shape.", "Valco Spreadsheets", MessageBoxButton.OK, DlgIcon.Warning); return; }
                var region = new List<CellRect> { dst };
                bool sameBook = _clip.Book == _book;
                bool sameSheet = sameBook && _clip.Sheet == WS.Name;
                if (_clip.Cut && sameSheet) region.Add(src);
                bool cut = _clip.Cut;
                Action act = () =>
                {
                    // copy through a scratch sheet so overlapping source/target work
                    var tmp = new XLWorkbook().AddWorksheet(fromWs.Name.Length > 0 ? "t" : "t");
                    fromWs.Range(src.R1, src.C1, src.R2, src.C2).CopyTo(tmp.Cell(src.R1, src.C1));
                    if (cut) fromWs.Range(src.R1, src.C1, src.R2, src.C2).Clear(XLClearOptions.Contents | XLClearOptions.NormalFormats);
                    for (int i = 0; i < repR; i++)
                        for (int j = 0; j < repC; j++)
                            Ops.PasteCells(tmp, src, WS, row + i * rows, col + j * cols, what, transpose, skipBlanks);
                };
                if (cut && !sameSheet)
                {
                    // the source is on another sheet/book: record the whole workbook
                    Change("Paste", act, structure: true);
                    if (!sameBook) _clip.Book.RaiseChanged();
                }
                else Change("Paste", act, region);
                if (cut) { _clip = null; G.SetMarquee(null); StatusMsg.Text = ""; }
                G.SelectRect(dst);
                G.Relayout();
                return;
            }
            if (string.IsNullOrEmpty(clipText)) return;
            var (tr, tc) = Ops.TextSize(clipText);
            var target = new CellRect(row, col, row + tr - 1, col + tc - 1);
            Change("Paste", () => Ops.PasteText(WS, row, col, clipText), new List<CellRect> { target });
            G.SelectRect(target);
            G.Relayout();
        }

        void PasteSpecial()
        {
            var d = new PasteSpecialDialog(this, _clip != null);
            if (d.ShowDialog() != true) return;
            PasteAs(d.What, d.Transpose, d.SkipBlanks);
        }

        // ===================================================================== fill / autosum
        void FillDirection(string dir)
        {
            var s = G.ActiveRect;
            CellRect src, dst = s;
            switch (dir)
            {
                case "Down": if (s.Rows == 1) { if (s.R1 == 1) return; dst = new CellRect(s.R1 - 1, s.C1, s.R2, s.C2); } src = new CellRect(dst.R1, dst.C1, dst.R1, dst.C2); break;
                case "Right": if (s.Cols == 1) { if (s.C1 == 1) return; dst = new CellRect(s.R1, s.C1 - 1, s.R2, s.C2); } src = new CellRect(dst.R1, dst.C1, dst.R2, dst.C1); break;
                case "Up": src = new CellRect(s.R2, s.C1, s.R2, s.C2); break;
                default: src = new CellRect(s.R1, s.C2, s.R2, s.C2); break;
            }
            if (src.Count == dst.Count) return;
            Change("Fill " + dir, () =>
            {
                if (dir == "Down" || dir == "Right") Ops.Fill(WS, src, dst, copyOnly: true);
                else
                {
                    // Up / Left: copy the edge row/column backwards
                    for (int r = dst.R1; r <= dst.R2; r++)
                        for (int c = dst.C1; c <= dst.C2; c++)
                        {
                            if (src.Contains(r, c)) continue;
                            var from = dir == "Up" ? WS.Cell(src.R1, c) : WS.Cell(r, src.C1);
                            from.CopyTo(WS.Cell(r, c));
                        }
                }
            }, new List<CellRect> { dst });
        }

        void AutoSum(string fn)
        {
            if (G.IsEditing) CommitEdit(0, 0);
            var s = G.ActiveRect;
            if (s.Count == 1)
            {
                var range = Ops.AutoSumRange(WS, s.R1, s.C1);
                string arg = range.HasValue ? range.Value.Address : "";
                G.BeginEdit("=" + fn + "(" + arg + ")", EditMode.Point);
                _editSheet = SheetIdx;
                var ed = G.EditorBox;
                ed.Select(fn.Length + 2, arg.Length);
                if (range.HasValue) G.SetMarquee(range.Value);
                return;
            }
            // a selected block: totals go under each column (or right of each row)
            Change("AutoSum", () =>
            {
                bool rowTotals = s.Cols > 1 && s.Rows == 1;
                if (!rowTotals)
                {
                    int target = s.R2 + 1;
                    bool lastRowEmpty = WS.Range(s.R2, s.C1, s.R2, s.C2).IsEmpty();
                    if (lastRowEmpty && s.Rows > 1) target = s.R2;
                    for (int c = s.C1; c <= s.C2; c++)
                        WS.Cell(target, c).FormulaA1 = $"{fn}({A1.Cell(s.R1, c)}:{A1.Cell(target - 1, c)})";
                }
                else
                {
                    int target = s.C2 + 1;
                    WS.Cell(s.R1, target).FormulaA1 = $"{fn}({A1.Cell(s.R1, s.C1)}:{A1.Cell(s.R1, s.C2)})";
                }
            }, new List<CellRect> { new CellRect(s.R1, s.C1, s.R2 + 1, s.C2 + 1) });
        }

        // ===================================================================== insert / delete
        void InsertCellsDialog()
        {
            var s = G.ActiveRect;
            if (s.IsWholeRows) { InsertRows(); return; }
            if (s.IsWholeColumns) { InsertCols(); return; }
            var choice = ChoiceDialog.Ask(this, "Insert", new[] { "Shift cells right", "Shift cells down", "Entire row", "Entire column" }, 1);
            switch (choice)
            {
                case 0: Change("Insert", () => WS.Range(s.R1, s.C1, s.R2, s.C2).InsertColumnsBefore(s.Cols), structure: true); break;
                case 1: Change("Insert", () => WS.Range(s.R1, s.C1, s.R2, s.C2).InsertRowsAbove(s.Rows), structure: true); break;
                case 2: InsertRows(); break;
                case 3: InsertCols(); break;
            }
        }

        void DeleteCellsDialog()
        {
            var s = G.ActiveRect;
            if (s.IsWholeRows) { DeleteRows(); return; }
            if (s.IsWholeColumns) { DeleteCols(); return; }
            var choice = ChoiceDialog.Ask(this, "Delete", new[] { "Shift cells left", "Shift cells up", "Entire row", "Entire column" }, 1);
            switch (choice)
            {
                case 0: Change("Delete", () => WS.Range(s.R1, s.C1, s.R2, s.C2).Delete(XLShiftDeletedCells.ShiftCellsLeft), structure: true); break;
                case 1: Change("Delete", () => WS.Range(s.R1, s.C1, s.R2, s.C2).Delete(XLShiftDeletedCells.ShiftCellsUp), structure: true); break;
                case 2: DeleteRows(); break;
                case 3: DeleteCols(); break;
            }
        }

        void InsertRows()
        {
            var rows = G.View.Selection.Select(r => (r.R1, r.Rows)).OrderByDescending(x => x.R1).ToList();
            Change("Insert Rows", () => { foreach (var (r1, n) in rows) WS.Row(r1).InsertRowsAbove(Math.Min(n, 10000)); }, structure: true);
        }

        void InsertCols()
        {
            var cols = G.View.Selection.Select(r => (r.C1, r.Cols)).OrderByDescending(x => x.C1).ToList();
            Change("Insert Columns", () => { foreach (var (c1, n) in cols) WS.Column(c1).InsertColumnsBefore(Math.Min(n, 1000)); }, structure: true);
        }

        void DeleteRows()
        {
            var rows = G.View.Selection.Select(r => (r.R1, r.R2)).OrderByDescending(x => x.R1).ToList();
            Change("Delete Rows", () => { foreach (var (r1, r2) in rows) WS.Rows(r1, Math.Min(r2, Math.Max(r1, G.Layout.LastRow + 1))).Delete(); }, structure: true);
        }

        void DeleteCols()
        {
            var cols = G.View.Selection.Select(r => (r.C1, r.C2)).OrderByDescending(x => x.C1).ToList();
            Change("Delete Columns", () => { foreach (var (c1, c2) in cols) WS.Columns(c1, Math.Min(c2, Math.Max(c1, G.Layout.LastCol + 1))).Delete(); }, structure: true);
        }

        void HideRows() => _book.Undo.DoSizes("Hide Rows", SheetIdx, false, SelRows(), () => { foreach (var r in SelRows()) WS.Row(r).Hide(); }) ;
        void UnhideRows() { var rows = SelRows(true); _book.Undo.DoSizes("Unhide Rows", SheetIdx, false, rows, () => { foreach (var r in rows) WS.Row(r).Unhide(); }); AfterChange(structure: true); }
        void HideCols() => _book.Undo.DoSizes("Hide Columns", SheetIdx, true, SelCols(), () => { foreach (var c in SelCols()) WS.Column(c).Hide(); });
        void UnhideCols() { var cols = SelCols(true); _book.Undo.DoSizes("Unhide Columns", SheetIdx, true, cols, () => { foreach (var c in cols) WS.Column(c).Unhide(); }); AfterChange(structure: true); }

        List<int> SelRows(bool includeAround = false)
        {
            var l = new List<int>();
            foreach (var r in G.View.Selection)
            {
                int r2 = Math.Min(r.R2, Math.Max(r.R1, G.Layout.LastRow + 200));
                l.AddRange(Enumerable.Range(r.R1, r2 - r.R1 + 1));
                if (includeAround && r.R1 > 1) l.Add(r.R1 - 1);
            }
            return l.Distinct().ToList();
        }

        List<int> SelCols(bool includeAround = false)
        {
            var l = new List<int>();
            foreach (var r in G.View.Selection)
            {
                int c2 = Math.Min(r.C2, Math.Max(r.C1, G.Layout.LastCol + 50));
                l.AddRange(Enumerable.Range(r.C1, c2 - r.C1 + 1));
                if (includeAround && r.C1 > 1) l.Add(r.C1 - 1);
            }
            return l.Distinct().ToList();
        }

        void HideAndRefresh(Action a) { a(); AfterChange(structure: true); }

        void SetColumnWidthDialog()
        {
            var cur = Units.PxToColWidth(G.Layout.Cols.Size(G.View.ActiveCol));
            var s = InputDialog.Ask(this, "Column Width", "Column width:", cur.ToString("0.##", CultureInfo.CurrentCulture));
            if (s == null || !double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out var w) || w < 0 || w > 255) return;
            var cols = SelCols();
            _book.Undo.DoSizes("Column Width", SheetIdx, true, cols, () => { foreach (var c in cols) { if (w == 0) WS.Column(c).Hide(); else { WS.Column(c).Unhide(); WS.Column(c).Width = w; } } });
            AfterChange(structure: true);
        }

        void SetRowHeightDialog()
        {
            var cur = Units.PxToPt(G.Layout.Rows.Size(G.View.ActiveRow));
            var s = InputDialog.Ask(this, "Row Height", "Row height:", cur.ToString("0.##", CultureInfo.CurrentCulture));
            if (s == null || !double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out var h) || h < 0 || h > 409) return;
            var rows = SelRows();
            _book.Undo.DoSizes("Row Height", SheetIdx, false, rows, () => { foreach (var r in rows) { if (h == 0) WS.Row(r).Hide(); else { WS.Row(r).Unhide(); WS.Row(r).Height = h; _autoRows.Remove((WS.Name, r)); } } });
            AfterChange(structure: true);
        }

        void SetDefaultWidthDialog()
        {
            var s = InputDialog.Ask(this, "Standard Width", "Standard column width:", WS.ColumnWidth.ToString("0.##", CultureInfo.CurrentCulture));
            if (s == null || !double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out var w) || w <= 0 || w > 255) return;
            Change("Standard Width", () => WS.ColumnWidth = w, structure: true);
        }

        // ===================================================================== sort / filter
        CellRect SortRange(out bool header)
        {
            var s = G.ActiveRect;
            if (s.Count == 1) s = Ops.CurrentRegion(WS, s.R1, s.C1);
            if (s.IsWholeColumns) s = new CellRect(1, s.C1, Math.Max(1, G.Layout.LastRow), s.C2);
            header = Ops.HasHeader(WS, s);
            return s;
        }

        void SortQuick(bool desc)
        {
            var r = SortRange(out bool header);
            if (r.Rows < 2) return;
            int col = Math.Clamp(G.View.ActiveCol, r.C1, r.C2);
            Change(desc ? "Sort Z to A" : "Sort A to Z", () => Ops.Sort(WS, r, header, new List<Ops.SortKey> { new Ops.SortKey { Column = col, Descending = desc } }, false), structure: true);
            G.SelectRect(r, G.View.ActiveRow, G.View.ActiveCol);
        }

        void SortDialogShow()
        {
            var r = SortRange(out bool header);
            G.SelectRect(r, G.View.ActiveRow, G.View.ActiveCol);
            var d = new SortDialog(this, WS, r, header, G.View.ActiveCol);
            if (d.ShowDialog() != true) return;
            Change("Sort", () => Ops.Sort(WS, r, d.HasHeader, d.Keys, d.MatchCase), structure: true);
            G.SelectRect(r, G.View.ActiveRow, G.View.ActiveCol);
        }

        void ToggleFilter()
        {
            if (G.FilterRange.HasValue)
            {
                Change("Filter", () => { WS.AutoFilter.Clear(); WS.SetAutoFilter(false); foreach (var row in WS.Rows().Where(r => r.IsHidden).ToList()) row.Unhide(); }, structure: true);
                return;
            }
            var r = SortRange(out _);
            if (r.Count == 1 && WS.Cell(r.R1, r.C1).IsEmpty())
            {
                VistaDialog.Show(this, "Filter can't be applied to an empty cell. Select a cell in a range of data and try again.", "Valco Spreadsheets", MessageBoxButton.OK, DlgIcon.Warning);
                return;
            }
            Change("Filter", () => WS.Range(r.R1, r.C1, r.R2, r.C2).SetAutoFilter(), structure: true);
        }

        void ClearFilters()
        {
            if (!G.FilterRange.HasValue) return;
            Change("Clear Filter", () =>
            {
                var fr = G.FilterRange.Value;
                for (int c = fr.C1; c <= fr.C2; c++) WS.AutoFilter.Column(c - fr.C1 + 1).Clear();
                foreach (var row in WS.Rows(fr.R1, fr.R2).Where(x => x.IsHidden).ToList()) row.Unhide();
            }, structure: true);
        }

        void ReapplyFilter() { if (G.FilterRange.HasValue) Change("Reapply", () => WS.AutoFilter.Reapply(), structure: true); }

        void ShowFilterMenu(int row, int col, Point p)
        {
            var fr = G.FilterRange;
            if (!fr.HasValue) return;
            var popup = new FilterPopup(this, WS, fr.Value, col);
            popup.Sort += desc =>
            {
                var r = new CellRect(fr.Value.R1, fr.Value.C1, Math.Max(fr.Value.R2, G.Layout.LastRow), fr.Value.C2);
                Change(desc ? "Sort Z to A" : "Sort A to Z", () => Ops.Sort(WS, r, true, new List<Ops.SortKey> { new Ops.SortKey { Column = col, Descending = desc } }, false), structure: true);
            };
            popup.Apply += (values, custom) =>
            {
                Change("Filter", () =>
                {
                    var fc = WS.AutoFilter.Column(col - fr.Value.C1 + 1);
                    fc.Clear();
                    if (custom != null) custom(fc);
                    else if (values != null) { bool first = true; foreach (var v in values) { if (first) fc.AddFilter(v); else fc.AddFilter(v); first = false; } }
                    WS.AutoFilter.Reapply();
                }, structure: true);
            };
            popup.Show(G, p);
        }

        // ===================================================================== hyperlinks / comments
        void FollowHyperlink(int r, int c)
        {
            try
            {
                var link = WS.Cell(r, c).GetHyperlink();
                if (link.IsExternal && link.ExternalAddress != null)
                {
                    var url = link.ExternalAddress.ToString();
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                else if (!string.IsNullOrEmpty(link.InternalAddress))
                {
                    var target = link.InternalAddress.Replace("'", "");
                    var parts = target.Split('!');
                    if (parts.Length == 2)
                    {
                        var ws = _book.Wb.Worksheets.FirstOrDefault(w => w.Name.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
                        if (ws != null && A1.TryParse(parts[1], out var rect)) GoTo(ws.Position, rect);
                    }
                    else if (A1.TryParse(target, out var rect2)) GoTo(SheetIdx, rect2);
                    else GoToName(target);
                }
            }
            catch (Exception ex) { VistaDialog.Show(this, "Cannot open the specified file.\n\n" + ex.Message, "Valco Spreadsheets", MessageBoxButton.OK, DlgIcon.Warning); }
        }

        void InsertHyperlink()
        {
            var c = G.ActiveCell;
            string text = c.IsEmpty() ? "" : c.GetFormattedString();
            string addr = "";
            try { if (c.HasHyperlink) { var h = c.GetHyperlink(); addr = h.IsExternal ? h.ExternalAddress?.ToString() : "#" + h.InternalAddress; } } catch { }
            var d = new HyperlinkDialog(this, text, addr, _book.Wb.Worksheets.Select(w => w.Name).ToList());
            if (d.ShowDialog() != true) return;
            Change("Hyperlink", () =>
            {
                if (string.IsNullOrWhiteSpace(d.Address)) { if (c.HasHyperlink) c.GetHyperlink().Delete(); return; }
                XLHyperlink link = d.Address.StartsWith("#") ? new XLHyperlink(d.Address.Substring(1)) : new XLHyperlink(new Uri(d.Address.Contains("://") || d.Address.StartsWith("mailto:") ? d.Address : (System.IO.Path.IsPathRooted(d.Address) ? new Uri(d.Address).AbsoluteUri : "http://" + d.Address)));
                if (!string.IsNullOrEmpty(d.Tip)) link.Tooltip = d.Tip;
                c.SetHyperlink(link);
                if (c.IsEmpty() || c.GetFormattedString() != d.Display) c.Value = string.IsNullOrEmpty(d.Display) ? d.Address : d.Display;
                c.Style.Font.FontColor = XLColor.FromTheme(XLThemeColor.Hyperlink);
                c.Style.Font.Underline = XLFontUnderlineValues.Single;
            }, new List<CellRect> { new CellRect(G.View.ActiveRow, G.View.ActiveCol, G.View.ActiveRow, G.View.ActiveCol) });
        }

        void EditComment()
        {
            var c = G.ActiveCell;
            string cur = "";
            try { if (c.HasComment) cur = c.GetComment().Text; } catch { }
            var author = Settings.EffectiveUserName;
            var text = TextDialog.Ask(this, c.HasComment ? "Edit Comment" : "New Comment", author + ":", cur.StartsWith(author + ":") ? cur.Substring(author.Length + 1).TrimStart() : cur);
            if (text == null) return;
            Change(c.HasComment ? "Edit Comment" : "Insert Comment", () =>
            {
                if (c.HasComment) c.Clear(XLClearOptions.Comments);
                if (text.Length == 0) return;
                var cm = c.CreateComment();
                cm.Author = author;
                cm.AddSignature();
                cm.AddText(text);
            }, new List<CellRect> { new CellRect(G.View.ActiveRow, G.View.ActiveCol, G.View.ActiveRow, G.View.ActiveCol) });
        }

        void DeleteComment()
        {
            Change("Delete Comment", () => { foreach (var r in G.View.Selection) foreach (var cell in WS.Range(r.R1, r.C1, r.R2, r.C2).CellsUsed(XLCellsUsedOptions.Comments).ToList()) cell.Clear(XLClearOptions.Comments); });
        }

        void NextComment(int dir)
        {
            var list = WS.CellsUsed(XLCellsUsedOptions.Comments).Where(c => c.HasComment).OrderBy(c => c.Address.RowNumber).ThenBy(c => c.Address.ColumnNumber).ToList();
            if (list.Count == 0) { VistaDialog.Show(this, "There are no comments on this sheet.", "Valco Spreadsheets"); return; }
            int cur = list.FindIndex(c => c.Address.RowNumber == G.View.ActiveRow && c.Address.ColumnNumber == G.View.ActiveCol);
            int next = cur < 0 ? (dir > 0 ? list.FindIndex(c => c.Address.RowNumber > G.View.ActiveRow || c.Address.RowNumber == G.View.ActiveRow && c.Address.ColumnNumber > G.View.ActiveCol) : list.FindLastIndex(c => c.Address.RowNumber < G.View.ActiveRow)) : cur + dir;
            if (next < 0 || next >= list.Count) next = dir > 0 ? 0 : list.Count - 1;
            var t = list[next];
            G.Select(t.Address.RowNumber, t.Address.ColumnNumber);
            G.EnsureVisible(t.Address.RowNumber, t.Address.ColumnNumber);
            ShowCommentTip(t);
        }

        void ShowCommentTip(IXLCell c)
        {
            try
            {
                var tip = new ToolTip { Content = new TextBlock { Text = c.GetComment().Text, TextWrapping = TextWrapping.Wrap, MaxWidth = 260 }, Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xE1)), PlacementTarget = G, Placement = System.Windows.Controls.Primitives.PlacementMode.Relative };
                var rp = G.CellRectPx(new CellRect(c.Address.RowNumber, c.Address.ColumnNumber, c.Address.RowNumber, c.Address.ColumnNumber));
                tip.HorizontalOffset = rp.Right + 8; tip.VerticalOffset = rp.Top;
                tip.IsOpen = true;
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                t.Tick += (s, e) => { tip.IsOpen = false; t.Stop(); };
                t.Start();
            }
            catch { }
        }

        // ===================================================================== data validation pick list (Alt+Down)
        void ShowPickList()
        {
            var c = G.ActiveCell;
            List<string> items = null;
            try { if (c.HasDataValidation && c.GetDataValidation().AllowedValues == XLAllowedValues.List) items = ListItems(c.GetDataValidation()); } catch { }
            if (items == null)
            {
                // "Pick From Drop-down List": the values above and below in this column
                var seen = new HashSet<string>();
                items = new List<string>();
                for (int r = G.View.ActiveRow - 1; r >= Math.Max(1, G.View.ActiveRow - 500) && !WS.Cell(r, G.View.ActiveCol).IsEmpty(); r--) { var s = WS.Cell(r, G.View.ActiveCol).GetFormattedString(); if (seen.Add(s)) items.Add(s); }
                for (int r = G.View.ActiveRow + 1; r <= G.View.ActiveRow + 500 && !WS.Cell(r, G.View.ActiveCol).IsEmpty(); r++) { var s = WS.Cell(r, G.View.ActiveCol).GetFormattedString(); if (seen.Add(s)) items.Add(s); }
                items.Sort(StringComparer.CurrentCultureIgnoreCase);
            }
            if (items.Count == 0) return;
            var lb = new ListBox { ItemsSource = items, MaxHeight = 200, MinWidth = Math.Max(80, G.Layout.Cols.Size(G.View.ActiveCol) * G.Zoom), BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray };
            var rp = G.CellRectPx(new CellRect(G.View.ActiveRow, G.View.ActiveCol, G.View.ActiveRow, G.View.ActiveCol));
            var pop = new System.Windows.Controls.Primitives.Popup { Child = lb, PlacementTarget = G, Placement = System.Windows.Controls.Primitives.PlacementMode.Relative, HorizontalOffset = rp.Left, VerticalOffset = rp.Bottom, StaysOpen = false };
            void Pick() { if (lb.SelectedItem is string s) { pop.IsOpen = false; CommitText(s, false); G.Focus(); } }
            lb.MouseUp += (s, e) => Pick();
            lb.KeyDown += (s, e) => { if (e.Key == Key.Enter) Pick(); if (e.Key == Key.Escape) { pop.IsOpen = false; G.Focus(); } };
            pop.IsOpen = true;
            lb.SelectedIndex = Math.Max(0, items.IndexOf(c.IsEmpty() ? "" : c.GetFormattedString()));
            lb.Focus();
        }

        // ===================================================================== context menus
        void ShowCellMenu(Point p)
        {
            var s = G.ActiveRect;
            var m = new ContextMenu();
            m.Items.Add(RB.MI("Cut", () => CopySelection(true), "IconCut", "Ctrl+X"));
            m.Items.Add(RB.MI("Copy", () => CopySelection(false), "IconCopy", "Ctrl+C"));
            m.Items.Add(RB.MI("Paste", Paste, "IconPaste", "Ctrl+V"));
            m.Items.Add(RB.MI("Paste Special...", PasteSpecial, "IconSpecialPaste"));
            m.Items.Add(new Separator());
            if (s.IsWholeColumns)
            {
                m.Items.Add(RB.MI("Insert", InsertCols, "IconInsertCols"));
                m.Items.Add(RB.MI("Delete", DeleteCols, "IconDeleteCols"));
                m.Items.Add(RB.MI("Clear Contents", () => ClearSelection(XLClearOptions.Contents, "Clear Contents"), "IconClear"));
                m.Items.Add(new Separator());
                m.Items.Add(RB.MI("Format Cells...", () => FormatCells(null), "IconFormatCells"));
                m.Items.Add(RB.MI("Column Width...", SetColumnWidthDialog, "IconColWidth"));
                m.Items.Add(RB.MI("Hide", () => HideAndRefresh(HideCols)));
                m.Items.Add(RB.MI("Unhide", UnhideCols));
            }
            else if (s.IsWholeRows)
            {
                m.Items.Add(RB.MI("Insert", InsertRows, "IconInsertRows"));
                m.Items.Add(RB.MI("Delete", DeleteRows, "IconDeleteRows"));
                m.Items.Add(RB.MI("Clear Contents", () => ClearSelection(XLClearOptions.Contents, "Clear Contents"), "IconClear"));
                m.Items.Add(new Separator());
                m.Items.Add(RB.MI("Format Cells...", () => FormatCells(null), "IconFormatCells"));
                m.Items.Add(RB.MI("Row Height...", SetRowHeightDialog, "IconRowHeight"));
                m.Items.Add(RB.MI("Hide", () => HideAndRefresh(HideRows)));
                m.Items.Add(RB.MI("Unhide", UnhideRows));
            }
            else
            {
                m.Items.Add(RB.MI("Insert...", InsertCellsDialog, "IconInsertCells"));
                m.Items.Add(RB.MI("Delete...", DeleteCellsDialog, "IconDeleteCells"));
                m.Items.Add(RB.MI("Clear Contents", () => ClearSelection(XLClearOptions.Contents, "Clear Contents"), "IconClear"));
                m.Items.Add(new Separator());
                var filter = new MenuItem { Header = "Filter" };
                filter.Items.Add(RB.MI("Reapply", ReapplyFilter));
                filter.Items.Add(RB.MI("Filter by Selected Cell's Value", FilterBySelected));
                m.Items.Add(filter);
                var sort = new MenuItem { Header = "Sort" };
                sort.Items.Add(RB.MI("Sort A to Z", () => SortQuick(false), "IconSortAZ"));
                sort.Items.Add(RB.MI("Sort Z to A", () => SortQuick(true), "IconSortZA"));
                sort.Items.Add(RB.MI("Custom Sort...", SortDialogShow, "IconSort"));
                m.Items.Add(sort);
                m.Items.Add(new Separator());
                bool hasComment = false; try { hasComment = G.ActiveCell.HasComment; } catch { }
                m.Items.Add(RB.MI(hasComment ? "Edit Comment" : "Insert Comment", EditComment, "IconComment"));
                if (hasComment) m.Items.Add(RB.MI("Delete Comment", DeleteComment, "IconDeleteComment"));
                m.Items.Add(new Separator());
                m.Items.Add(RB.MI("Format Cells...", () => FormatCells(null), "IconFormatCells", "Ctrl+1"));
                m.Items.Add(RB.MI("Pick From Drop-down List...", ShowPickList));
                m.Items.Add(RB.MI("Name a Range...", NewNameDialog, "IconNameManager"));
                m.Items.Add(RB.MI("Hyperlink...", InsertHyperlink, "IconLink", "Ctrl+K"));
            }
            m.PlacementTarget = G;
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.Relative;
            m.HorizontalOffset = p.X; m.VerticalOffset = p.Y;
            m.IsOpen = true;
        }

        void FilterBySelected()
        {
            var c = G.ActiveCell;
            var r = SortRange(out _);
            if (!G.FilterRange.HasValue) Change("Filter", () => WS.Range(r.R1, r.C1, r.R2, r.C2).SetAutoFilter(), structure: true);
            var fr = G.FilterRange;
            if (!fr.HasValue) return;
            var value = c.Value;
            Change("Filter", () => { var fc = WS.AutoFilter.Column(G.View.ActiveCol - fr.Value.C1 + 1); fc.Clear(); fc.AddFilter(value); WS.AutoFilter.Reapply(); }, structure: true);
        }

        // ===================================================================== names
        void NewNameDialog()
        {
            var sel = G.ActiveRect;
            var suggestion = "";
            try { var above = sel.R1 > 1 ? WS.Cell(sel.R1 - 1, sel.C1).GetString() : ""; suggestion = System.Text.RegularExpressions.Regex.Replace(above, @"[^A-Za-z0-9_]", "_"); } catch { }
            var name = InputDialog.Ask(this, "New Name", "Name (refers to " + QuoteSheet(WS.Name) + "!" + new CellRect(sel.R1, sel.C1, sel.R2, sel.C2).Address.Replace("", "") + "):", suggestion);
            if (string.IsNullOrWhiteSpace(name)) return;
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z_\\][A-Za-z0-9_.]*$") || A1.TryParse(name, out _))
            {
                VistaDialog.Show(this, "The name that you entered is not valid.\n\nMake sure the name begins with a letter or underscore, and has no spaces or cell-reference look.", "Valco Spreadsheets", MessageBoxButton.OK, DlgIcon.Warning);
                return;
            }
            Change("Define Name", () => _book.Wb.DefinedNames.Add(name, WS.Range(sel.R1, sel.C1, sel.R2, sel.C2)), structure: true);
        }

        void NameManager()
        {
            var d = new NameManagerDialog(this, _book.Wb);
            d.ShowDialog();
            if (d.Changed) { _book.RaiseChanged(); QueueSync(); }
        }

        void CreateNamesFromSelection()
        {
            var sel = G.ActiveRect;
            if (sel.Count < 2) return;
            bool top = Ops.HasHeader(WS, sel);
            Change("Create Names", () =>
            {
                if (top)
                    for (int c = sel.C1; c <= sel.C2; c++)
                    {
                        var n = System.Text.RegularExpressions.Regex.Replace(WS.Cell(sel.R1, c).GetString(), @"[^A-Za-z0-9_]", "_");
                        if (n.Length == 0 || char.IsDigit(n[0])) n = "_" + n;
                        if (!_book.Wb.DefinedNames.Contains(n)) _book.Wb.DefinedNames.Add(n, WS.Range(sel.R1 + 1, c, sel.R2, c));
                    }
                else
                    for (int r = sel.R1; r <= sel.R2; r++)
                    {
                        var n = System.Text.RegularExpressions.Regex.Replace(WS.Cell(r, sel.C1).GetString(), @"[^A-Za-z0-9_]", "_");
                        if (n.Length == 0 || char.IsDigit(n[0])) n = "_" + n;
                        if (!_book.Wb.DefinedNames.Contains(n)) _book.Wb.DefinedNames.Add(n, WS.Range(r, sel.C1 + 1, r, sel.C2));
                    }
            }, structure: true);
        }

        // ===================================================================== calculation
        void CalculateNow()
        {
            try { _book.Wb.RecalculateAllFormulas(); } catch { }
            G.Redraw();
            QueueSync();
        }

        // ===================================================================== remove duplicates / text to columns
        void RemoveDuplicates()
        {
            var r = SortRange(out bool header);
            G.SelectRect(r);
            int removed = 0;
            Change("Remove Duplicates", () =>
            {
                var seen = new HashSet<string>();
                var del = new List<int>();
                for (int row = header ? r.R1 + 1 : r.R1; row <= r.R2; row++)
                {
                    var key = string.Join("\u0001", Enumerable.Range(r.C1, r.Cols).Select(c => WS.Cell(row, c).GetFormattedString()));
                    if (!seen.Add(key)) del.Add(row);
                }
                removed = del.Count;
                foreach (var row in Enumerable.Reverse(del)) WS.Range(row, r.C1, row, r.C2).Delete(XLShiftDeletedCells.ShiftCellsUp);
            }, structure: true);
            VistaDialog.Show(this, removed == 0 ? "No duplicate values found." : $"{removed} duplicate values found and removed; {Math.Max(0, (header ? r.Rows - 1 : r.Rows) - removed)} unique values remain.", "Valco Spreadsheets");
        }

        void TextToColumns()
        {
            var s = G.ActiveRect;
            if (s.Cols > 1) { VistaDialog.Show(this, "Only one column can be converted at a time. The range can be many rows tall but no more than one column wide.", "Valco Spreadsheets", MessageBoxButton.OK, DlgIcon.Warning); return; }
            var d = new TextToColumnsDialog(this);
            if (d.ShowDialog() != true) return;
            int r2 = Math.Min(s.R2, G.Layout.LastRow);
            Change("Text to Columns", () =>
            {
                for (int r = s.R1; r <= r2; r++)
                {
                    var t = WS.Cell(r, s.C1).GetFormattedString();
                    if (t.Length == 0) continue;
                    var parts = d.Split(t);
                    for (int i = 0; i < parts.Count; i++) CellInput.Apply(WS.Cell(r, s.C1 + i), parts[i], fromFile: true);
                }
            }, new List<CellRect> { new CellRect(s.R1, s.C1, r2, Math.Min(SheetLimits.Cols, s.C1 + 60)) });
        }
    }
}
