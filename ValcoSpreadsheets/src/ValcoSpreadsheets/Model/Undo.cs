using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;

namespace ValcoSpreadsheets.Model
{
    /// <summary>A rectangle of cells (1-based, inclusive).</summary>
    public readonly struct CellRect
    {
        public readonly int R1, C1, R2, C2;
        public CellRect(int r1, int c1, int r2, int c2)
        {
            R1 = Math.Min(r1, r2); R2 = Math.Max(r1, r2); C1 = Math.Min(c1, c2); C2 = Math.Max(c1, c2);
        }
        public int Rows => R2 - R1 + 1;
        public int Cols => C2 - C1 + 1;
        public long Count => (long)Rows * Cols;
        public bool Contains(int r, int c) => r >= R1 && r <= R2 && c >= C1 && c <= C2;
        public bool Intersects(CellRect o) => !(o.R2 < R1 || o.R1 > R2 || o.C2 < C1 || o.C1 > C2);
        public bool IsWholeColumns => R1 == 1 && R2 == SheetLimits.Rows;
        public bool IsWholeRows => C1 == 1 && C2 == SheetLimits.Cols;
        public CellRect Union(CellRect o) => new CellRect(Math.Min(R1, o.R1), Math.Min(C1, o.C1), Math.Max(R2, o.R2), Math.Max(C2, o.C2));
        public string Address => R1 == R2 && C1 == C2 ? A1.Cell(R1, C1) : A1.Cell(R1, C1) + ":" + A1.Cell(R2, C2);
        public override string ToString() => Address;
    }

    public static class SheetLimits
    {
        public const int Rows = 1048576;
        public const int Cols = 16384;
    }

    /// <summary>A1-style addresses.</summary>
    public static class A1
    {
        public static string Col(int c)
        {
            var s = "";
            while (c > 0) { int m = (c - 1) % 26; s = (char)('A' + m) + s; c = (c - 1) / 26; }
            return s;
        }
        public static string Cell(int r, int c) => Col(c) + r;

        public static int ColNumber(string letters)
        {
            int n = 0;
            foreach (var ch in letters.ToUpperInvariant()) { if (ch < 'A' || ch > 'Z') return -1; n = n * 26 + (ch - 'A' + 1); }
            return n;
        }

        /// <summary>Parses A1, $A$1, A1:B5, A:A, 1:3 (no sheet name). Returns false when it isn't an address.</summary>
        public static bool TryParse(string text, out CellRect rect)
        {
            rect = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Replace("$", "").Trim().ToUpperInvariant();
            var parts = text.Split(':');
            if (parts.Length > 2) return false;
            if (!TryPart(parts[0], out int r1, out int c1)) return false;
            int r2 = r1, c2 = c1;
            if (parts.Length == 2 && !TryPart(parts[1], out r2, out c2)) return false;
            // whole columns (A:C) or rows (2:4)
            if (r1 == 0 && r2 == 0 && c1 > 0 && c2 > 0) { rect = new CellRect(1, c1, SheetLimits.Rows, c2); return true; }
            if (c1 == 0 && c2 == 0 && r1 > 0 && r2 > 0) { rect = new CellRect(r1, 1, r2, SheetLimits.Cols); return true; }
            if (r1 <= 0 || c1 <= 0 || r2 <= 0 || c2 <= 0) return false;
            if (r1 > SheetLimits.Rows || r2 > SheetLimits.Rows || c1 > SheetLimits.Cols || c2 > SheetLimits.Cols) return false;
            rect = new CellRect(r1, c1, r2, c2);
            return true;
        }

        static bool TryPart(string p, out int row, out int col)
        {
            row = 0; col = 0;
            int i = 0;
            while (i < p.Length && char.IsLetter(p[i])) i++;
            var letters = p.Substring(0, i);
            var digits = p.Substring(i);
            if (letters.Length > 3) return false;
            if (letters.Length > 0) { col = ColNumber(letters); if (col < 1) return false; }
            if (digits.Length > 0) { if (!int.TryParse(digits, out row) || row < 1) return false; }
            return letters.Length > 0 || digits.Length > 0;
        }
    }

    public abstract class UndoEntry
    {
        public string Name;
        public int SheetIndex;          // 1-based position of the sheet to show after undo/redo
        public List<CellRect> Selection; // selection to restore
        public abstract void Undo(Book b);
        public abstract void Redo(Book b);
        public virtual void Drop() { }
    }

    /// <summary>The state of some cell rectangles: values, formulas, formats and merges.</summary>
    class RangeState
    {
        readonly List<(int R, int C, string Formula, XLCellValue Value, int StyleRow)> _cells = new();
        readonly List<string> _merges = new();
        readonly IXLWorksheet _styles; // one cell per captured cell holds its style
        readonly List<CellRect> _rects;

        RangeState(IXLWorksheet styles, List<CellRect> rects) { _styles = styles; _rects = rects; }

        public static RangeState Capture(IXLWorksheet ws, List<CellRect> rects, IXLWorksheet styleStore)
        {
            var st = new RangeState(styleStore, rects);
            int n = 0;
            foreach (var rc in rects)
            {
                foreach (var cell in ws.Range(rc.R1, rc.C1, rc.R2, rc.C2).CellsUsed(XLCellsUsedOptions.Contents | XLCellsUsedOptions.NormalFormats))
                {
                    n++;
                    styleStore.Cell(n, 1).Style = cell.Style;
                    string f = cell.HasFormula ? cell.FormulaA1 : null;
                    st._cells.Add((cell.Address.RowNumber, cell.Address.ColumnNumber, f, f == null ? cell.Value : Blank.Value, n));
                }
                foreach (var m in ws.MergedRanges)
                {
                    var a = m.RangeAddress;
                    var mr = new CellRect(a.FirstAddress.RowNumber, a.FirstAddress.ColumnNumber, a.LastAddress.RowNumber, a.LastAddress.ColumnNumber);
                    if (rc.Intersects(mr)) st._merges.Add(mr.Address);
                }
            }
            return st;
        }

        public void Restore(IXLWorksheet ws)
        {
            foreach (var rc in _rects)
            {
                var range = ws.Range(rc.R1, rc.C1, rc.R2, rc.C2);
                foreach (var m in ws.MergedRanges.ToList())
                {
                    var a = m.RangeAddress;
                    var mr = new CellRect(a.FirstAddress.RowNumber, a.FirstAddress.ColumnNumber, a.LastAddress.RowNumber, a.LastAddress.ColumnNumber);
                    if (rc.Intersects(mr)) m.Unmerge();
                }
                foreach (var cell in range.CellsUsed(XLCellsUsedOptions.Contents | XLCellsUsedOptions.NormalFormats).ToList())
                    cell.Clear(XLClearOptions.Contents | XLClearOptions.NormalFormats);
            }
            foreach (var (r, c, f, v, sr) in _cells)
            {
                var cell = ws.Cell(r, c);
                cell.Style = _styles.Cell(sr, 1).Style;
                if (f != null) cell.FormulaA1 = f; else cell.Value = v;
            }
            foreach (var m in _merges.Distinct()) try { ws.Range(m).Merge(); } catch { }
        }
    }

    class RangeUndo : UndoEntry
    {
        public RangeState Before, After;
        public IXLWorksheet BeforeStore, AfterStore;
        public override void Undo(Book b) => Before.Restore(b.Wb.Worksheet(SheetIndex));
        public override void Redo(Book b) => After.Restore(b.Wb.Worksheet(SheetIndex));
        public override void Drop()
        {
            try { BeforeStore?.Delete(); } catch { }
            try { AfterStore?.Delete(); } catch { }
        }
    }

    class SnapshotUndo : UndoEntry
    {
        public byte[] Before, After;
        public override void Undo(Book b) => b.ReplaceWorkbook(new XLWorkbook(new MemoryStream(Before)));
        public override void Redo(Book b) => b.ReplaceWorkbook(new XLWorkbook(new MemoryStream(After)));
    }

    /// <summary>Charts live outside ClosedXML's workbook, so their changes are recorded as before/after copies.</summary>
    public class ChartUndo : UndoEntry
    {
        public List<ChartModel> Before, After;
        public override void Undo(Book b) => ChartStore.Set(b, Before.Select(c => c.Clone()).ToList());
        public override void Redo(Book b) => ChartStore.Set(b, After.Select(c => c.Clone()).ToList());
    }

    class SizeUndo : UndoEntry
    {
        public bool Columns;
        public List<(int Index, double Size, bool Hidden)> Before = new(), After = new();
        public override void Undo(Book b) => Apply(b, Before);
        public override void Redo(Book b) => Apply(b, After);
        void Apply(Book b, List<(int Index, double Size, bool Hidden)> list)
        {
            var ws = b.Wb.Worksheet(SheetIndex);
            foreach (var (i, s, h) in list)
            {
                if (Columns) { var c = ws.Column(i); c.Width = s; if (h) c.Hide(); else c.Unhide(); }
                else { var r = ws.Row(i); r.Height = s; if (h) r.Hide(); else r.Unhide(); }
            }
        }

        public static List<(int, double, bool)> Read(IXLWorksheet ws, bool columns, IEnumerable<int> indexes)
        {
            var l = new List<(int, double, bool)>();
            foreach (var i in indexes)
            {
                if (columns) { var c = ws.Column(i); l.Add((i, c.Width, c.IsHidden)); }
                else { var r = ws.Row(i); l.Add((i, r.Height, r.IsHidden)); }
            }
            return l;
        }
    }

    /// <summary>Undo/Redo for a book. Cell edits store only the affected cells; structural changes store the whole workbook.</summary>
    public class UndoStack
    {
        readonly Book _book;
        readonly List<UndoEntry> _undo = new(), _redo = new();
        XLWorkbook _store;
        int _storeSeq;
        const int Limit = 100;
        public event Action StackChanged;

        public UndoStack(Book book) { _book = book; }

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public IEnumerable<string> UndoNames => Enumerable.Reverse(_undo).Select(u => u.Name);
        public IEnumerable<string> RedoNames => Enumerable.Reverse(_redo).Select(u => u.Name);

        IXLWorksheet NewStoreSheet()
        {
            _store ??= new XLWorkbook();
            return _store.AddWorksheet("u" + (++_storeSeq));
        }

        static long UsedCells(IXLWorksheet ws, List<CellRect> rects)
        {
            long n = 0;
            foreach (var r in rects) { n += ws.Range(r.R1, r.C1, r.R2, r.C2).CellsUsed(XLCellsUsedOptions.Contents | XLCellsUsedOptions.NormalFormats).Count(); if (n > 150000) break; }
            return n;
        }

        /// <summary>
        /// Runs a change and records it. <paramref name="region"/> lists the cells it can touch; null means
        /// it may change anything (inserting rows, sorting, sheets...) so the whole workbook is recorded.
        /// </summary>
        public void Do(string name, int sheetIndex, List<CellRect> region, Action change, List<CellRect> selection = null)
        {
            var ws = _book.Wb.Worksheet(sheetIndex);
            UndoEntry entry;
            // whole rows/columns: format the used part + the row/column styles -> record everything
            if (region != null && (region.Any(r => r.Count > 4_000_000) || UsedCells(ws, region) > 150000)) region = null;
            if (region == null)
            {
                var before = Snapshot();
                change();
                entry = new SnapshotUndo { Before = before, After = Snapshot() };
            }
            else
            {
                var bs = NewStoreSheet();
                var before = RangeState.Capture(ws, region, bs);
                change();
                var asSheet = NewStoreSheet();
                var after = RangeState.Capture(_book.Wb.Worksheet(sheetIndex), region, asSheet);
                entry = new RangeUndo { Before = before, After = after, BeforeStore = bs, AfterStore = asSheet };
            }
            entry.Name = name; entry.SheetIndex = sheetIndex; entry.Selection = selection ?? region;
            Push(entry);
        }

        /// <summary>Records column widths / row heights / hiding.</summary>
        public void DoSizes(string name, int sheetIndex, bool columns, IEnumerable<int> indexes, Action change)
        {
            var ws = _book.Wb.Worksheet(sheetIndex);
            var list = indexes.Distinct().ToList();
            var e = new SizeUndo { Name = name, SheetIndex = sheetIndex, Columns = columns, Before = SizeUndo.Read(ws, columns, list) };
            change();
            e.After = SizeUndo.Read(_book.Wb.Worksheet(sheetIndex), columns, list);
            Push(e);
        }

        byte[] Snapshot()
        {
            var ms = new MemoryStream();
            _book.Wb.SaveAs(ms, new SaveOptions { EvaluateFormulasBeforeSaving = false, ValidatePackage = false });
            return ms.ToArray();
        }

        /// <summary>Records a change made outside the workbook (charts).</summary>
        public void Record(UndoEntry e) => Push(e);

        void Push(UndoEntry e)
        {
            _undo.Add(e);
            foreach (var r in _redo) r.Drop();
            _redo.Clear();
            while (_undo.Count > Limit) { _undo[0].Drop(); _undo.RemoveAt(0); }
            _book.RaiseChanged();
            StackChanged?.Invoke();
        }

        public UndoEntry UndoOne()
        {
            if (_undo.Count == 0) return null;
            var e = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);
            e.Undo(_book);
            _redo.Add(e);
            _book.RaiseChanged();
            StackChanged?.Invoke();
            return e;
        }

        public UndoEntry RedoOne()
        {
            if (_redo.Count == 0) return null;
            var e = _redo[^1];
            _redo.RemoveAt(_redo.Count - 1);
            e.Redo(_book);
            _undo.Add(e);
            _book.RaiseChanged();
            StackChanged?.Invoke();
            return e;
        }

        public void Clear()
        {
            foreach (var e in _undo.Concat(_redo)) e.Drop();
            _undo.Clear(); _redo.Clear();
            StackChanged?.Invoke();
        }
    }
}
