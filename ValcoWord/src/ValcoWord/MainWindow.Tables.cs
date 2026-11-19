using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using Path = System.Windows.Shapes.Path;

namespace ValcoWord
{
    public partial class MainWindow
    {
        // =====================================================================================
        //  Table styles
        // =====================================================================================
        static readonly ConditionalWeakTable<Table, string> TableStyles = new ConditionalWeakTable<Table, string>();
        static readonly string[] TableStyleNames = { "Table Grid", "Plain Table", "Light Shading", "Light List", "Light Grid", "Medium Shading 1", "Medium Grid 1", "Colorful List", "Dark List" };
        int _tableAccent = 1;

        UIElement TableStylesStrip()
        {
            var wrap = new WrapPanel { Width = 330, Height = 60 };
            foreach (var n in TableStyleNames.Take(6))
            {
                var name = n;
                wrap.Children.Add(RB.Tile(TableStyleThumb(name, Th.Accent1, 46, 30), name, () => ApplyTableStyle(name), 52, 38));
            }
            var more = RB.Small(null, null, null, "More table styles");
            more.Content = new Path { Data = Geometry.Parse("M0,0 H6 M0,3 L6,3 L3,7 Z"), Stroke = Brushes.DimGray, Fill = Brushes.DimGray };
            more.Height = 60;
            more.Click += (s, e) => ShowTableStyleGallery(more);
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(0xA9, 0xC3, 0xE6)), BorderThickness = new Thickness(1), Background = Brushes.White, Child = wrap, Margin = new Thickness(0, 2, 0, 0) });
            sp.Children.Add(more);
            return sp;
        }

        void ShowTableStyleGallery(FrameworkElement target)
        {
            var tiles = new List<UIElement>();
            for (int a = 1; a <= 6; a++)
                foreach (var n in TableStyleNames)
                {
                    var name = n; int acc = a;
                    if ((name == "Table Grid" || name == "Plain Table") && a > 1) continue;
                    tiles.Add(RB.Tile(TableStyleThumb(name, Accents[a - 1], 52, 34), name + (name.StartsWith("Table") || name.StartsWith("Plain") ? "" : " - Accent " + a), () => { _tableAccent = acc; ApplyTableStyle(name); }, 58, 42));
                }
            RB.ShowGallery(target, "Table Styles", tiles, 560, new UIElement[] { RB.MenuButton("Clear Table Style", () => ApplyTableStyle("Plain Table"), "IconDelete") });
        }

        static UIElement TableStyleThumb(string style, Color accent, double w, double h)
        {
            var g = new Grid { Width = w, Height = h };
            for (int i = 0; i < 5; i++) g.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 5; i++) g.RowDefinitions.Add(new RowDefinition());
            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 5; c++)
                {
                    var (bg, border, thick, fg, _) = CellLook(style, accent, r, c, 5, 5, true, false, true, false, false, false);
                    var b = new Border { Background = bg ?? Brushes.White, BorderBrush = border ?? Brushes.Transparent, BorderThickness = new Thickness(Math.Min(1, thick.Left), Math.Min(1, thick.Top), Math.Min(1, thick.Right), Math.Min(1, thick.Bottom)) };
                    b.Child = new Rectangle { Height = 1.5, Width = w / 8, Fill = fg ?? Brushes.Gray, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetRow(b, r); Grid.SetColumn(b, c); g.Children.Add(b);
                }
            return g;
        }

        /// <summary>Background, border brush, border thickness, text colour and bold for one cell of a styled table.</summary>
        static (Brush Bg, Brush Border, Thickness Thick, Brush Fg, bool Bold) CellLook(string style, Color a, int r, int c, int rows, int cols, bool header, bool total, bool bandRows, bool firstCol, bool lastCol, bool bandCols)
        {
            bool isHeader = header && r == 0, isTotal = total && r == rows - 1, isFirst = firstCol && c == 0, isLast = lastCol && c == cols - 1;
            int bodyR = r - (header ? 1 : 0), bodyC = c - (firstCol ? 1 : 0);
            bool band = (bandRows && bodyR >= 0 && bodyR % 2 == 0 && !isTotal) || (bandCols && bodyC >= 0 && bodyC % 2 == 0);
            bool bold = isHeader || isTotal || isFirst || isLast;
            Brush light = Br(Lighter(a, .82)), lighter = Br(Lighter(a, .9)), acc = Br(a), black = Brushes.Black, white = Brushes.White;
            double t = 0.75;
            switch (style)
            {
                case "Table Grid": return (null, black, new Thickness(0.5), null, false);
                case "Plain Table": return (null, null, new Thickness(0), null, false);
                case "Light Shading":
                    return (band ? light : null, acc, new Thickness(0, r == 0 || isTotal ? 1.5 : 0, 0, isHeader || r == rows - 1 ? 1.5 : 0), Br(Darker2(a, .75)), bold);
                case "Light List":
                    return (isHeader ? acc : null, acc, new Thickness(c == 0 ? t : 0, r == 0 ? t : 0, c == cols - 1 ? t : 0, r == rows - 1 || isHeader ? t : 0), isHeader ? white : null, bold);
                case "Light Grid":
                    return (band ? light : null, acc, new Thickness(t, t, t, isHeader ? 2 : t), null, bold);
                case "Medium Shading 1":
                    return (isHeader ? acc : band ? light : null, Br(Lighter(a, .3)), new Thickness(c == 0 ? t : 0, r == 0 ? t : 0, c == cols - 1 ? t : 0, r == rows - 1 || isHeader ? t : 0), isHeader ? white : null, bold);
                case "Medium Grid 1":
                    return (band ? Br(Lighter(a, .65)) : lighter, Br(Lighter(a, .3)), new Thickness(t), null, bold);
                case "Colorful List":
                    return (isHeader ? Br(Darker2(Th.Accent2, .85)) : band ? light : lighter, white, new Thickness(0), isHeader ? white : null, bold);
                case "Dark List":
                    return (isHeader ? Br(Th.Dark2) : band ? Br(Darker2(a, .8)) : acc, white, new Thickness(0, 0, 0, isHeader ? 2 : 0), white, bold);
            }
            return (null, black, new Thickness(0.5), null, false);
        }

        void ApplyTableStyle(string style)
        {
            var cur = CurrentCell();
            if (cur == null) { Flash("Click inside a table first."); return; }
            var t = cur.Value.T;
            if (style == null) { if (!TableStyles.TryGetValue(t, out style)) return; }
            ApplyTableStyleTo(t, style, Accents[Math.Max(0, Math.Min(5, _tableAccent - 1))]);
        }

        void ApplyTableStyleTo(Table t, string style) => ApplyTableStyleTo(t, style, Th.Accent1);
        void ApplyTableStyleTo(Table t, string style, Color accent)
        {
            TableStyles.AddOrUpdate(t, style);
            var rows = t.RowGroups.SelectMany(g => g.Rows).ToList();
            int cols = Math.Max(1, t.Columns.Count);
            Editor.BeginChange();
            for (int r = 0; r < rows.Count; r++)
            {
                int c = 0;
                foreach (var cell in rows[r].Cells)
                {
                    var (bg, border, thick, fg, bold) = CellLook(style, accent, r, c, rows.Count, cols, CkHeaderRow?.IsChecked == true, CkTotalRow?.IsChecked == true, CkBandedRows?.IsChecked == true, CkFirstCol?.IsChecked == true, CkLastCol?.IsChecked == true, CkBandedCols?.IsChecked == true);
                    cell.Background = bg;
                    cell.BorderBrush = border ?? Brushes.Black;
                    cell.BorderThickness = thick;
                    foreach (var p in Fmt.AllParagraphs(cell.Blocks))
                    {
                        if (fg != null) p.Foreground = fg; else p.ClearValue(TextElement.ForegroundProperty);
                        p.FontWeight = bold ? FontWeights.Bold : FontWeights.Normal;
                    }
                    c += Math.Max(1, cell.ColumnSpan);
                }
            }
            Editor.EndChange();
            MarkDirty();
        }

        ContextMenu TableBordersMenu()
        {
            (string, string)[] items = { ("Bottom Border", "b"), ("Top Border", "t"), ("Left Border", "l"), ("Right Border", "r"), ("No Border", "none"), ("All Borders", "all"), ("Outside Borders", "out"), ("Inside Borders", "in"), ("Inside Horizontal Border", "ih"), ("Inside Vertical Border", "iv") };
            var m = RB.Menu();
            foreach (var (lab, k) in items) { var key = k; m.Items.Add(RB.MI(lab, () => SetCellBorders(key), "IconBorders")); }
            m.Items.Add(new Separator());
            m.Items.Add(RB.MI("View Gridlines", () => TableBorders_Click(null, null)));
            m.Items.Add(RB.MI("Borders and Shading...", BordersAndShadingDialog));
            return m;
        }

        /// <summary>Selected cells as a rectangle (row index, column index) inside the current table.</summary>
        List<(TableCell Cell, int Row, int Col)> SelectedCellGrid(out Table table)
        {
            table = null;
            var cur = CurrentCell();
            if (cur == null) return new List<(TableCell, int, int)>();
            table = cur.Value.T;
            var sel = SelectedCells().ToList();
            if (sel.Count == 0) sel.Add(cur.Value.C);
            var res = new List<(TableCell, int, int)>();
            var rows = table.RowGroups.SelectMany(g => g.Rows).ToList();
            for (int r = 0; r < rows.Count; r++)
            {
                int c = 0;
                foreach (var cell in rows[r].Cells) { if (sel.Contains(cell)) res.Add((cell, r, c)); c += Math.Max(1, cell.ColumnSpan); }
            }
            return res;
        }

        void SetCellBorders(string k)
        {
            var cells = SelectedCellGrid(out _);
            if (cells.Count == 0) { Flash("Click inside a table first."); return; }
            int r0 = cells.Min(c => c.Row), r1 = cells.Max(c => c.Row), c0 = cells.Min(c => c.Col), c1 = cells.Max(c => c.Col + Math.Max(1, c.Cell.ColumnSpan) - 1);
            Editor.BeginChange();
            foreach (var (cell, r, c) in cells)
            {
                var th = cell.BorderThickness; double w = 0.75;
                int ce = c + Math.Max(1, cell.ColumnSpan) - 1;
                switch (k)
                {
                    case "b": th.Bottom = w; break;
                    case "t": th.Top = w; break;
                    case "l": th.Left = w; break;
                    case "r": th.Right = w; break;
                    case "none": th = new Thickness(0); break;
                    case "all": th = new Thickness(w); break;
                    case "out": if (r == r0) th.Top = w; if (r == r1) th.Bottom = w; if (c == c0) th.Left = w; if (ce == c1) th.Right = w; break;
                    case "in": if (r != r0) th.Top = w; if (r != r1) th.Bottom = w; if (c != c0) th.Left = w; if (ce != c1) th.Right = w; break;
                    case "ih": if (r != r0) th.Top = w; if (r != r1) th.Bottom = w; break;
                    case "iv": if (c != c0) th.Left = w; if (ce != c1) th.Right = w; break;
                }
                cell.BorderThickness = th;
                if (cell.BorderBrush == null) cell.BorderBrush = Brushes.Black;
            }
            Editor.EndChange();
            MarkDirty();
        }

        // =====================================================================================
        //  Table layout commands
        // =====================================================================================
        void MergeCells()
        {
            var cells = SelectedCellGrid(out var t);
            if (cells.Count < 2) { Flash("Select two or more cells to merge."); return; }
            var rows = cells.Select(c => c.Row).Distinct().OrderBy(x => x).ToList();
            Editor.BeginChange();
            try
            {
                var first = cells.OrderBy(c => c.Row).ThenBy(c => c.Col).First();
                // horizontal merge within each row, then stack the other rows' text into the first cell
                foreach (var row in rows)
                {
                    var inRow = cells.Where(c => c.Row == row).OrderBy(c => c.Col).ToList();
                    var keep = inRow[0].Cell;
                    foreach (var (cell, _, _) in inRow.Skip(1))
                    {
                        MoveCellContent(cell, keep);
                        keep.ColumnSpan = Math.Max(1, keep.ColumnSpan) + Math.Max(1, cell.ColumnSpan);
                        ((TableRow)cell.Parent).Cells.Remove(cell);
                    }
                }
                if (rows.Count > 1)
                {
                    var target = first.Cell;
                    var allRows = t.RowGroups.SelectMany(g => g.Rows).ToList();
                    foreach (var row in rows.Skip(1))
                    {
                        var other = allRows[row].Cells.FirstOrDefault(c => cells.Any(x => x.Cell == c));
                        if (other == null) continue;
                        MoveCellContent(other, target);
                        allRows[row].Cells.Remove(other);
                        target.RowSpan = Math.Max(1, target.RowSpan) + 1;
                    }
                }
                SelectCell(first.Cell);
            }
            finally { Editor.EndChange(); }
            MarkDirty(); ScheduleLayout();
        }

        static void MoveCellContent(TableCell from, TableCell to)
        {
            bool hasText = Fmt.AllParagraphs(from.Blocks).Any(p => Spans.Text(p).Trim().Length > 0 || p.Inlines.Any(i => i is InlineUIContainer));
            if (!hasText) return;
            // drop trailing empty paragraph in the target
            if (to.Blocks.LastBlock is Paragraph lp && Spans.Text(lp).Length == 0 && to.Blocks.Count > 1) to.Blocks.Remove(lp);
            if (to.Blocks.Count == 1 && to.Blocks.FirstBlock is Paragraph fp && Spans.Text(fp).Length == 0) to.Blocks.Remove(fp);
            foreach (var b in from.Blocks.ToList()) { from.Blocks.Remove(b); to.Blocks.Add(b); }
        }

        void SplitCell()
        {
            var cur = CurrentCell();
            if (cur == null) { Flash("Click inside a table first."); return; }
            var (t, g, row, cell, col) = cur.Value;
            var d = new DialogBase(this, "Split Cells", 260);
            var cols = D.Box(Math.Max(2, cell.ColumnSpan).ToString(), 60);
            var rws = D.Box("1", 60);
            d.Content = UI.DialogLayout(D.Form(("Number of columns:", cols), ("Number of rows:", rws)), UI.Strip(UI.Btn("OK", (s, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
            if (d.ShowDialog() != true) return;
            int nc = Math.Max(1, (int)(D.ParseNum(cols.Text) ?? 2)), nr = Math.Max(1, (int)(D.ParseNum(rws.Text) ?? 1));
            Editor.BeginChange();
            try
            {
                int span = Math.Max(1, cell.ColumnSpan);
                if (nc > 1)
                {
                    if (nc <= span)
                    {
                        // split an already-merged cell back into its grid columns
                        int each = span / nc, extra = span % nc;
                        cell.ColumnSpan = each + (extra > 0 ? 1 : 0);
                        var after = cell;
                        for (int i = 1; i < nc; i++)
                        {
                            var nc2 = new TableCell(NewCellParagraph()) { BorderBrush = cell.BorderBrush, BorderThickness = cell.BorderThickness, Padding = cell.Padding, Background = cell.Background, ColumnSpan = each + (i < extra ? 1 : 0) };
                            row.Cells.Insert(row.Cells.IndexOf(after) + 1, nc2); after = nc2;
                        }
                    }
                    else
                    {
                        // add grid columns: split this column's width, widen the same column in other rows
                        int add = nc - span;
                        double w = t.Columns.Skip(col).Take(span).Sum(c => c.Width.IsAbsolute ? c.Width.Value : 100);
                        for (int i = 0; i < add; i++) t.Columns.Insert(col + span, new TableColumn());
                        for (int i = col; i < col + nc; i++) t.Columns[i].Width = new GridLength(Math.Floor(w / nc));
                        foreach (var other in t.RowGroups.SelectMany(x => x.Rows))
                        {
                            if (other == row) continue;
                            int cc = 0;
                            foreach (var oc in other.Cells) { int os = Math.Max(1, oc.ColumnSpan); if (cc <= col && col < cc + os) { oc.ColumnSpan = os + add; break; } cc += os; }
                        }
                        cell.ColumnSpan = 1;
                        var after = cell;
                        for (int i = 1; i < nc; i++)
                        {
                            var nc2 = new TableCell(NewCellParagraph()) { BorderBrush = cell.BorderBrush, BorderThickness = cell.BorderThickness, Padding = cell.Padding, Background = cell.Background };
                            row.Cells.Insert(row.Cells.IndexOf(after) + 1, nc2); after = nc2;
                        }
                    }
                }
                for (int i = 1; i < nr; i++)
                {
                    var nrow = NewRowLike(row);
                    g.Rows.Insert(g.Rows.IndexOf(row) + 1, nrow);
                }
            }
            finally { Editor.EndChange(); }
            MarkDirty(); ScheduleLayout();
        }

        void SplitTable()
        {
            var cur = CurrentCell();
            if (cur == null) { Flash("Click inside a table first."); return; }
            var (t, g, row, _, _) = cur.Value;
            int idx = g.Rows.IndexOf(row);
            if (idx == 0 && t.RowGroups.IndexOf(g) == 0) { Flash("Click in the row where you want the second table to start."); return; }
            Editor.BeginChange();
            var nt = new Table { CellSpacing = t.CellSpacing, Margin = t.Margin };
            foreach (var c in t.Columns) nt.Columns.Add(new TableColumn { Width = c.Width });
            var ng = new TableRowGroup();
            foreach (var r in g.Rows.Skip(idx).ToList()) { g.Rows.Remove(r); ng.Rows.Add(r); }
            nt.RowGroups.Add(ng);
            var gap = NewPara();
            t.SiblingBlocks.InsertAfter(t, gap);
            gap.SiblingBlocks.InsertAfter(gap, nt);
            Editor.EndChange();
            Editor.CaretPosition = gap.ContentStart;
            MarkDirty(); ScheduleLayout();
        }

        void CellMargins()
        {
            var cur = CurrentCell();
            if (cur == null) { Flash("Click inside a table first."); return; }
            var t = cur.Value.T;
            var pad = cur.Value.C.Padding;
            var d = new DialogBase(this, "Table Options", 320);
            var top = D.Box((pad.Top / 96).ToString("0.##", D.Inv), 60); var bottom = D.Box((pad.Bottom / 96).ToString("0.##", D.Inv), 60);
            var left = D.Box((pad.Left / 96).ToString("0.##", D.Inv), 60); var right = D.Box((pad.Right / 96).ToString("0.##", D.Inv), 60);
            var spacing = D.Box((t.CellSpacing / 96).ToString("0.##", D.Inv), 60);
            var sp = new StackPanel();
            sp.Children.Add(D.Group("Default cell margins (inches)", D.Form(("Top:", top), ("Bottom:", bottom), ("Left:", left), ("Right:", right))));
            sp.Children.Add(D.Group("Default cell spacing (inches)", D.Form(("Spacing:", spacing))));
            d.Content = UI.DialogLayout(sp, UI.Strip(UI.Btn("OK", (s, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
            if (d.ShowDialog() != true) return;
            double I(TextBox b) => Math.Max(0, D.ParseNum(b.Text) ?? 0) * 96;
            var np = new Thickness(I(left), I(top), I(right), I(bottom));
            Editor.BeginChange();
            foreach (var c in t.RowGroups.SelectMany(g => g.Rows).SelectMany(r => r.Cells)) c.Padding = np;
            t.CellSpacing = I(spacing);
            Editor.EndChange();
            MarkDirty(); ScheduleLayout();
        }

        void SortTable()
        {
            var cur = CurrentCell();
            if (cur == null) { Flash("Click inside a table first."); return; }
            var t = cur.Value.T;
            var g = cur.Value.G;
            var rows = g.Rows.ToList();
            int cols = rows.Max(r => r.Cells.Count);
            var d = new DialogBase(this, "Sort", 360);
            var header = new CheckBox { Content = "My list has a header row", IsChecked = rows.Count > 1 && rows[0].Cells.Any(c => Fmt.AllParagraphs(c.Blocks).Any(p => p.FontWeight == FontWeights.Bold)), Margin = new Thickness(0, 6, 0, 0) };
            var by = new ComboBox { Width = 160 };
            void FillBy() { by.Items.Clear(); for (int i = 0; i < cols; i++) by.Items.Add(header.IsChecked == true && rows[0].Cells.Count > i ? (Spans.Text(rows[0].Cells[i]).Trim() is string h && h.Length > 0 ? h : "Column " + (i + 1)) : "Column " + (i + 1)); by.SelectedIndex = Math.Min(cur.Value.Col, cols - 1); }
            header.Click += (s, e) => FillBy();
            FillBy();
            var type = new ComboBox { Width = 100, ItemsSource = new[] { "Text", "Number", "Date" }, SelectedIndex = 0 };
            var asc = new RadioButton { Content = "Ascending", IsChecked = true, Margin = new Thickness(0, 0, 10, 0) };
            var desc = new RadioButton { Content = "Descending" };
            var dir = new StackPanel { Orientation = Orientation.Horizontal }; dir.Children.Add(asc); dir.Children.Add(desc);
            var sp = new StackPanel(); sp.Children.Add(D.Form(("Sort by:", by), ("Type:", type), (null, dir))); sp.Children.Add(header);
            d.Content = UI.DialogLayout(sp, UI.Strip(UI.Btn("OK", (s, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
            if (d.ShowDialog() != true) return;
            int ci = Math.Max(0, by.SelectedIndex);
            var body = header.IsChecked == true ? rows.Skip(1).ToList() : rows;
            string Key(TableRow r) => ci < r.Cells.Count ? Spans.Text(r.Cells[ci]).Trim() : "";
            IEnumerable<TableRow> sorted;
            switch ((string)type.SelectedItem)
            {
                case "Number": sorted = body.OrderBy(r => double.TryParse(Key(r).Replace("$", "").Replace(",", ""), NumberStyles.Float, CultureInfo.CurrentCulture, out var v) ? v : double.MinValue); break;
                case "Date": sorted = body.OrderBy(r => DateTime.TryParse(Key(r), out var v) ? v : DateTime.MinValue); break;
                default: sorted = body.OrderBy(Key, StringComparer.CurrentCultureIgnoreCase); break;
            }
            var list = sorted.ToList();
            if (desc.IsChecked == true) list.Reverse();
            Editor.BeginChange();
            foreach (var r in body) g.Rows.Remove(r);
            foreach (var r in list) g.Rows.Add(r);
            Editor.EndChange();
            if (TableStyles.TryGetValue(t, out var st)) ApplyTableStyleTo(t, st);
            MarkDirty(); ScheduleLayout();
        }

        void RepeatHeaderRows()
        {
            var cells = SelectedCellGrid(out var t);
            if (t == null) { Flash("Click inside a table first."); return; }
            var rows = cells.Select(c => (TableRow)c.Cell.Parent).Distinct().ToList();
            bool on = !rows.All(r => (r.Tag as string) == "Header");
            foreach (var r in rows) r.Tag = on ? "Header" : null;
            MarkDirty();
            Flash(on ? "The selected rows will repeat at the top of each page." : "Header rows will no longer repeat.");
        }

        void ConvertTableToText()
        {
            var cur = CurrentCell();
            if (cur == null) { Flash("Click inside a table first."); return; }
            var t = cur.Value.T;
            var d = new DialogBase(this, "Convert Table To Text", 300);
            var tabs = new RadioButton { Content = "Tabs", IsChecked = true }; var commas = new RadioButton { Content = "Commas" }; var paras = new RadioButton { Content = "Paragraph marks" };
            var sp = new StackPanel(); sp.Children.Add(tabs); sp.Children.Add(commas); sp.Children.Add(paras);
            d.Content = UI.DialogLayout(D.Group("Separate text with", sp), UI.Strip(UI.Btn("OK", (s, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
            if (d.ShowDialog() != true) return;
            var blocks = new List<Block>();
            foreach (var r in t.RowGroups.SelectMany(g => g.Rows))
            {
                var texts = r.Cells.Select(c => string.Join(" ", Fmt.AllParagraphs(c.Blocks).Select(Spans.Text)).Trim()).ToList();
                if (paras.IsChecked == true) blocks.AddRange(texts.Select(x => (Block)NewPara(x)));
                else blocks.Add(NewPara(string.Join(commas.IsChecked == true ? ", " : "\t", texts)));
            }
            Editor.BeginChange();
            foreach (var b in blocks) t.SiblingBlocks.InsertBefore(t, b);
            t.SiblingBlocks.Remove(t);
            Editor.EndChange();
            MarkDirty(); ScheduleLayout();
        }

        void TableFormula()
        {
            var cur = CurrentCell();
            if (cur == null) { Flash("Click inside a table cell first."); return; }
            var (t, g, row, cell, col) = cur.Value;
            var rows = t.RowGroups.SelectMany(x => x.Rows).ToList();
            int ri = rows.IndexOf(row);
            double? Num(TableCell c) { var s = Spans.Text(c).Trim().Replace("$", "").Replace(",", "").Replace("%", ""); return double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out var v) ? v : (double?)null; }
            TableCell CellAt(int r, int c) { if (r < 0 || r >= rows.Count) return null; int cc = 0; foreach (var x in rows[r].Cells) { int sp2 = Math.Max(1, x.ColumnSpan); if (cc <= c && c < cc + sp2) return x; cc += sp2; } return null; }
            bool aboveHasNums = Enumerable.Range(0, ri).Any(r => CellAt(r, col) is TableCell c && Num(c).HasValue);
            var d = new DialogBase(this, "Formula", 340);
            var formula = D.Box(aboveHasNums ? "=SUM(ABOVE)" : "=SUM(LEFT)", 280);
            var fmt = new ComboBox { Width = 180, IsEditable = true, ItemsSource = new[] { "", "#,##0", "#,##0.00", "$#,##0.00", "0%", "0.00%" } };
            var paste = new ComboBox { Width = 180, ItemsSource = new[] { "SUM", "AVERAGE", "COUNT", "MAX", "MIN", "PRODUCT" } };
            paste.SelectionChanged += (s, e) => { if (paste.SelectedItem is string fn) formula.Text = $"={fn}({(aboveHasNums ? "ABOVE" : "LEFT")})"; };
            d.Content = UI.DialogLayout(D.Form(("Formula:", formula), ("Number format:", fmt), ("Paste function:", paste)), UI.Strip(UI.Btn("OK", (s, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
            if (d.ShowDialog() != true) return;
            var m = System.Text.RegularExpressions.Regex.Match(formula.Text.Replace(" ", "").ToUpperInvariant(), @"^=(SUM|AVERAGE|COUNT|MAX|MIN|PRODUCT)\((ABOVE|LEFT|BELOW|RIGHT)\)$");
            string result;
            if (!m.Success) result = "!Syntax Error";
            else
            {
                var vals = new List<double>();
                string dir = m.Groups[2].Value;
                if (dir == "ABOVE") for (int r = ri - 1; r >= 0; r--) { var c = CellAt(r, col); var v = c == null ? null : Num(c); if (v.HasValue) vals.Add(v.Value); else if (vals.Count > 0) break; }
                if (dir == "BELOW") for (int r = ri + 1; r < rows.Count; r++) { var c = CellAt(r, col); var v = c == null ? null : Num(c); if (v.HasValue) vals.Add(v.Value); else if (vals.Count > 0) break; }
                if (dir == "LEFT") for (int c = col - 1; c >= 0; c--) { var x = CellAt(ri, c); var v = x == null ? null : Num(x); if (v.HasValue) vals.Add(v.Value); else if (vals.Count > 0) break; }
                if (dir == "RIGHT") for (int c = col + 1; c < 64; c++) { var x = CellAt(ri, c); if (x == null) break; var v = Num(x); if (v.HasValue) vals.Add(v.Value); else if (vals.Count > 0) break; }
                double res = m.Groups[1].Value switch
                {
                    "SUM" => vals.Sum(), "AVERAGE" => vals.Count == 0 ? 0 : vals.Average(), "COUNT" => vals.Count, "MAX" => vals.Count == 0 ? 0 : vals.Max(),
                    "MIN" => vals.Count == 0 ? 0 : vals.Min(), _ => vals.Aggregate(1.0, (a, b) => a * b)
                };
                result = string.IsNullOrWhiteSpace(fmt.Text) ? res.ToString("0.##", CultureInfo.CurrentCulture) : res.ToString(fmt.Text, CultureInfo.CurrentCulture);
            }
            Editor.BeginChange();
            var p = Fmt.AllParagraphs(cell.Blocks).LastOrDefault() ?? NewCellParagraph();
            if (p.Parent == null) cell.Blocks.Add(p);
            var span = new Span(new Run(result)) { Tag = new FieldInfo { Code = formula.Text.Trim() + (string.IsNullOrWhiteSpace(fmt.Text) ? "" : $" \\# \"{fmt.Text}\"") } };
            p.Inlines.Add(span);
            Editor.EndChange();
            MarkDirty();
        }

        void SetColumnWidth(double inches)
        {
            var cur = CurrentCell();
            if (cur == null || inches <= 0) return;
            var (t, _, _, cell, col) = cur.Value;
            Editor.BeginChange();
            int span = Math.Max(1, cell.ColumnSpan);
            for (int i = col; i < Math.Min(t.Columns.Count, col + span); i++) t.Columns[i].Width = new GridLength(inches * 96 / span);
            Editor.EndChange();
            MarkDirty(); ScheduleLayout();
        }

        void SelectTablePart(string part)
        {
            var cur = CurrentCell();
            if (cur == null) { Flash("Click inside a table first."); return; }
            var (t, g, row, cell, col) = cur.Value;
            var rows = t.RowGroups.SelectMany(x => x.Rows).ToList();
            switch (part)
            {
                case "cell": Editor.Selection.Select(cell.ContentStart, cell.ContentEnd); break;
                case "row": Editor.Selection.Select(row.Cells[0].ContentStart, row.Cells[row.Cells.Count - 1].ContentEnd); break;
                case "table": Editor.Selection.Select(rows[0].Cells[0].ContentStart, rows[rows.Count - 1].Cells.Last().ContentEnd); break;
                case "col":
                    {
                        TableCell At(TableRow r) { int cc = 0; foreach (var x in r.Cells) { int s = Math.Max(1, x.ColumnSpan); if (cc <= col && col < cc + s) return x; cc += s; } return null; }
                        var first = At(rows[0]); var last = At(rows[rows.Count - 1]);
                        if (first != null && last != null) Editor.Selection.Select(first.ContentStart, last.ContentEnd);
                        break;
                    }
            }
            Editor.Focus();
        }

        void AutoFitTable(string mode)
        {
            var cur = CurrentCell();
            if (cur == null) { Flash("Click inside a table first."); return; }
            var t = cur.Value.T;
            int cols = t.Columns.Count;
            if (cols == 0) return;
            double body = _page.BodyWidthPx - 2;
            Editor.BeginChange();
            if (mode == "contents")
            {
                var need = new double[cols];
                foreach (var r in t.RowGroups.SelectMany(g => g.Rows))
                {
                    int c = 0;
                    foreach (var cell in r.Cells)
                    {
                        if (cell.ColumnSpan <= 1 && c < cols)
                            foreach (var p in Fmt.AllParagraphs(cell.Blocks))
                            {
                                var text = Spans.Text(p);
                                var ft = new FormattedText(text.Length == 0 ? " " : text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(p.FontFamily, p.FontStyle, p.FontWeight, FontStretches.Normal), p.FontSize, Brushes.Black, 1.0);
                                need[c] = Math.Max(need[c], ft.WidthIncludingTrailingWhitespace + cell.Padding.Left + cell.Padding.Right + 4);
                            }
                        c += Math.Max(1, cell.ColumnSpan);
                    }
                }
                double total = need.Sum();
                double scale = total > body ? body / total : 1;
                for (int i = 0; i < cols; i++) t.Columns[i].Width = new GridLength(Math.Max(24, Math.Floor(need[i] * scale)));
            }
            else for (int i = 0; i < cols; i++) t.Columns[i].Width = new GridLength(Math.Floor(body / cols));
            Editor.EndChange();
            MarkDirty(); ScheduleLayout();
        }

        void CellAlign(TextAlignment a)
        {
            var cells = SelectedCellGrid(out _);
            if (cells.Count == 0) { Flash("Click inside a table first."); return; }
            Editor.BeginChange();
            foreach (var p in cells.SelectMany(c => Fmt.AllParagraphs(c.Cell.Blocks))) p.TextAlignment = a;
            Editor.EndChange();
            MarkDirty();
        }

        // =====================================================================================
        //  Picture tools
        // =====================================================================================
        class PicState
        {
            public double Brightness, Contrast;
            public string Recolor = "", Style = "", Effect = "";
            public Color? Border;
            public double CropL, CropT, CropR, CropB; // fractions
        }
        static readonly ConditionalWeakTable<Image, PicState> PicStates = new ConditionalWeakTable<Image, PicState>();

        Image RequirePicture()
        {
            if (_selectedImage == null) Flash("Click a picture first.");
            return _selectedImage;
        }

        static BitmapSource OriginalOf(Image img)
        {
            var data = img.Tag as ImageData;
            if (data == null) { data = new ImageData(); img.Tag = data; }
            if (data.Original == null)
            {
                data.Original = img.Source as BitmapSource;
                data.OriginalWidth = img.Width; data.OriginalHeight = img.Height;
            }
            return data.Original;
        }

        ContextMenu AdjustMenu(string kind)
        {
            var m = RB.Menu();
            var st = _selectedImage != null && PicStates.TryGetValue(_selectedImage, out var s0) ? s0 : null;
            double cur = st == null ? 0 : kind == "brightness" ? st.Brightness : st.Contrast;
            foreach (var v in new[] { 40, 30, 20, 10, 0, -10, -20, -30, -40 })
            {
                double val = v / 100.0;
                m.Items.Add(RB.MI((v > 0 ? "+" : "") + v + " %" + (v == 0 ? " (Normal)" : ""), () => AdjustPicture(kind, val), null, null, Math.Abs(cur - val) < 0.001));
            }
            return m;
        }

        void AdjustPicture(string kind, double val)
        {
            var img = RequirePicture(); if (img == null) return;
            var st = PicStates.GetOrCreateValue(img);
            if (kind == "brightness") st.Brightness = val; else st.Contrast = val;
            RebuildPicture(img);
        }

        ContextMenu RecolorMenu()
        {
            var m = RB.Menu();
            m.Items.Add(RB.MI("No Recolor", () => SetRecolor("")));
            m.Items.Add(new Separator());
            foreach (var k in new[] { "Grayscale", "Sepia", "Washout", "Black and White" }) { var key = k; m.Items.Add(RB.MI(key, () => SetRecolor(key))); }
            m.Items.Add(new Separator());
            for (int i = 1; i <= 6; i++)
            {
                int a = i;
                m.Items.Add(RB.MI($"Accent color {a} Dark", () => SetRecolor("dark" + a)));
                m.Items.Add(RB.MI($"Accent color {a} Light", () => SetRecolor("light" + a)));
            }
            m.Items.Add(new Separator());
            m.Items.Add(RB.MI("Set Transparent Color (top-left pixel)", () => SetRecolor("transparent")));
            return m;
        }

        void SetRecolor(string k)
        {
            var img = RequirePicture(); if (img == null) return;
            PicStates.GetOrCreateValue(img).Recolor = k;
            RebuildPicture(img);
        }

        ContextMenu PictureEffectsMenu()
        {
            var m = RB.Menu();
            foreach (var (lab, key) in new[] { ("No Effect", ""), ("Shadow", "shadow"), ("Reflection", "reflection"), ("Glow", "glow"), ("Soft Edges", "soft"), ("Bevel", "bevel") })
            {
                var k = key;
                m.Items.Add(RB.MI(lab, () => ApplyPictureEffect(k == "" ? "none" : k), null, null, _selectedImage != null && PicStates.TryGetValue(_selectedImage, out var st) && st.Effect == k));
            }
            return m;
        }

        static readonly (string Name, string Desc)[] PictureStyles =
        {
            ("Simple Frame, White", "frame-white"), ("Simple Frame, Black", "frame-black"), ("Rounded Rectangle", "rounded"), ("Drop Shadow Rectangle", "shadow-rect"),
            ("Reflected Rounded Rectangle", "rounded-reflect"), ("Soft Edge Rectangle", "soft-rect"), ("Oval", "oval"), ("Metal Frame", "metal"), ("Beveled Matte, White", "matte"),
        };

        UIElement PictureStylesStrip()
        {
            var wrap = new WrapPanel { Width = 290, Height = 60 };
            foreach (var (name, key) in PictureStyles.Take(5))
            {
                var k = key;
                wrap.Children.Add(RB.Tile(PictureStyleThumb(k), name, () => ApplyPictureEffect("style:" + k), 54, 56));
            }
            var more = RB.Small(null, null, null, "More picture styles");
            more.Content = new Path { Data = Geometry.Parse("M0,0 H6 M0,3 L6,3 L3,7 Z"), Stroke = Brushes.DimGray, Fill = Brushes.DimGray };
            more.Height = 60;
            more.Click += (s, e) => RB.ShowGallery(more, "Picture Styles", PictureStyles.Select(p => { var k = p.Desc; return (UIElement)RB.Tile(PictureStyleThumb(k), p.Name, () => ApplyPictureEffect("style:" + k), 62, 60); }), 360,
                new UIElement[] { RB.MenuButton("Reset Picture Style", () => ApplyPictureEffect("style:"), "IconResetPicture") });
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(0xA9, 0xC3, 0xE6)), BorderThickness = new Thickness(1), Background = Brushes.White, Child = wrap, Margin = new Thickness(0, 2, 0, 0) });
            sp.Children.Add(more);
            return sp;
        }

        static UIElement PictureStyleThumb(string style)
        {
            var landscape = new DrawingBrush(new DrawingGroup
            {
                Children =
                {
                    new GeometryDrawing(new LinearGradientBrush(Color.FromRgb(0x9F, 0xD0, 0xF5), Color.FromRgb(0x4A, 0x90, 0xD0), 90), null, new RectangleGeometry(new Rect(0, 0, 40, 30))),
                    new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x5A, 0xA0, 0x3C)), null, Geometry.Parse("M0,30 L0,20 C10,14 22,24 40,16 V30 Z")),
                    new GeometryDrawing(Brushes.Gold, null, new EllipseGeometry(new Point(30, 8), 4, 4)),
                }
            });
            var r = StylizedFrame(new Rectangle { Width = 40, Height = 30, Fill = landscape }, style, 40, 30, true);
            return r;
        }

        /// <summary>Wraps a picture element in the visual treatment of a picture style.</summary>
        static FrameworkElement StylizedFrame(FrameworkElement pic, string style, double w, double h, bool thumb = false)
        {
            double m = thumb ? 3 : Math.Max(4, Math.Min(w, h) * 0.04);
            switch (style)
            {
                case "frame-white": return new Border { Background = Brushes.White, Padding = new Thickness(m * 1.6), BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0.5), Child = pic, Effect = new DropShadowEffect { ShadowDepth = 2, BlurRadius = 5, Opacity = .35 } };
                case "frame-black": return new Border { Background = Brushes.Black, Padding = new Thickness(m * 1.4), Child = pic };
                case "rounded": pic.Clip = new RectangleGeometry(new Rect(0, 0, w, h), m * 2.5, m * 2.5); return new Border { Child = pic };
                case "shadow-rect": return new Border { Child = pic, Effect = new DropShadowEffect { ShadowDepth = m * 0.8, BlurRadius = m * 2, Opacity = .5 } };
                case "rounded-reflect":
                    {
                        pic.Clip = new RectangleGeometry(new Rect(0, 0, w, h), m * 2.5, m * 2.5);
                        var refl = new Rectangle { Width = w, Height = h * 0.35, Fill = new VisualBrush(pic) { Stretch = Stretch.None, AlignmentY = AlignmentY.Bottom, Transform = new ScaleTransform(1, -1, 0, h * 0.175) },
                            OpacityMask = new LinearGradientBrush(Color.FromArgb(0x60, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 90), Margin = new Thickness(0, 2, 0, 0) };
                        var sp = new StackPanel(); sp.Children.Add(pic); sp.Children.Add(refl); return sp;
                    }
                case "soft-rect":
                    pic.OpacityMask = new RadialGradientBrush(new GradientStopCollection { new GradientStop(Colors.Black, 0.7), new GradientStop(Colors.Transparent, 1) }) { RadiusX = 0.62, RadiusY = 0.62 };
                    return new Border { Child = pic };
                case "oval": pic.Clip = new EllipseGeometry(new Rect(0, 0, w, h)); return new Border { Child = pic };
                case "metal":
                    return new Border { Padding = new Thickness(m * 1.2), Child = pic, CornerRadius = new CornerRadius(m),
                        Background = new LinearGradientBrush(new GradientStopCollection { new GradientStop(Color.FromRgb(0xF0, 0xF0, 0xF0), 0), new GradientStop(Color.FromRgb(0x90, 0x98, 0xA4), .5), new GradientStop(Color.FromRgb(0xD8, 0xDC, 0xE2), 1) }, 45) };
                case "matte":
                    return new Border { Padding = new Thickness(m * 2.2), Background = Brushes.White, BorderBrush = new LinearGradientBrush(Colors.White, Color.FromRgb(0xA0, 0xA0, 0xA0), 45), BorderThickness = new Thickness(m * 0.6), Child = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)), BorderThickness = new Thickness(0.75), Child = pic } };
                default: return new Border { Child = pic };
            }
        }

        void ApplyPictureEffect(string effect)
        {
            var img = RequirePicture(); if (img == null) return;
            var st = PicStates.GetOrCreateValue(img);
            if (effect.StartsWith("border:"))
            {
                var hex = effect.Substring(7);
                st.Border = hex.Length == 0 ? (Color?)null : StyleDef.C("#" + hex);
            }
            else if (effect.StartsWith("style:")) st.Style = effect.Substring(6);
            else st.Effect = effect == "none" ? "" : effect;
            RebuildPicture(img);
        }

        /// <summary>Re-renders a picture from its original pixels plus its adjustments, crop, colour and style.</summary>
        void RebuildPicture(Image img)
        {
            var orig = OriginalOf(img);
            if (orig == null) return;
            var data = (ImageData)img.Tag;
            var st = PicStates.GetOrCreateValue(img);
            // 1) crop
            BitmapSource src = orig;
            if (st.CropL + st.CropR + st.CropT + st.CropB > 0)
            {
                int x = (int)(orig.PixelWidth * st.CropL), y = (int)(orig.PixelHeight * st.CropT);
                int w = Math.Max(1, (int)(orig.PixelWidth * (1 - st.CropL - st.CropR))), h = Math.Max(1, (int)(orig.PixelHeight * (1 - st.CropT - st.CropB)));
                src = new CroppedBitmap(orig, new Int32Rect(x, y, Math.Min(w, orig.PixelWidth - x), Math.Min(h, orig.PixelHeight - y)));
            }
            // 2) pixel adjustments
            var fmt = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int pw = fmt.PixelWidth, ph = fmt.PixelHeight, stride = pw * 4;
            var px = new byte[stride * ph];
            fmt.CopyPixels(px, stride, 0);
            if (st.Brightness != 0 || st.Contrast != 0 || st.Recolor.Length > 0)
            {
                double b = st.Brightness * 255, c = 1 + st.Contrast * 1.5;
                Color tint = Colors.Black; bool light = false;
                if (st.Recolor.StartsWith("dark") || st.Recolor.StartsWith("light")) { light = st.Recolor.StartsWith("light"); tint = Accents[int.Parse(st.Recolor.Substring(light ? 5 : 4)) - 1]; }
                byte tb = px[0], tg = px[1], tr = px[2];
                for (int i = 0; i < px.Length; i += 4)
                {
                    double B = px[i], G = px[i + 1], R = px[i + 2];
                    switch (st.Recolor)
                    {
                        case "Grayscale": { double l = 0.299 * R + 0.587 * G + 0.114 * B; R = G = B = l; break; }
                        case "Sepia": { double l = 0.299 * R + 0.587 * G + 0.114 * B; R = l * 1.07 + 20; G = l * 0.95 + 8; B = l * 0.78; break; }
                        case "Washout": R = R * 0.35 + 166; G = G * 0.35 + 166; B = B * 0.35 + 166; break;
                        case "Black and White": { double l = 0.299 * R + 0.587 * G + 0.114 * B; R = G = B = l > 128 ? 255 : 0; break; }
                        case "transparent": if (px[i] == tb && px[i + 1] == tg && px[i + 2] == tr) px[i + 3] = 0; break;
                        default:
                            if (st.Recolor.Length > 0 && st.Recolor != "transparent")
                            {
                                double l = (0.299 * R + 0.587 * G + 0.114 * B) / 255;
                                if (light) { R = tint.R + (255 - tint.R) * l; G = tint.G + (255 - tint.G) * l; B = tint.B + (255 - tint.B) * l; }
                                else { R = tint.R * l; G = tint.G * l; B = tint.B * l; }
                            }
                            break;
                    }
                    R = (R - 128) * c + 128 + b; G = (G - 128) * c + 128 + b; B = (B - 128) * c + 128 + b;
                    px[i] = (byte)Math.Max(0, Math.Min(255, B)); px[i + 1] = (byte)Math.Max(0, Math.Min(255, G)); px[i + 2] = (byte)Math.Max(0, Math.Min(255, R));
                }
            }
            var adjusted = BitmapSource.Create(pw, ph, 96, 96, PixelFormats.Bgra32, null, px, stride);
            adjusted.Freeze();
            // 3) style + effect + border, rendered at the displayed size (2x for crispness)
            double dw = img.Width, dh = img.Height;
            if (double.IsNaN(dw) || dw <= 0) dw = pw;
            // keep the aspect ratio when the crop changed it
            double aspect = ph / (double)pw;
            if (Math.Abs(dh / dw - aspect) > 0.01) dh = Math.Round(dw * aspect);
            bool plain = st.Style.Length == 0 && st.Effect.Length == 0 && !st.Border.HasValue;
            BitmapSource final; byte[] bytes;
            if (plain)
            {
                final = adjusted;
                bytes = EncodePng(adjusted);
            }
            else
            {
                var pic = new Image { Source = adjusted, Width = dw, Height = dh, Stretch = Stretch.Fill };
                FrameworkElement el = StylizedFrame(pic, st.Style, dw, dh);
                if (st.Border.HasValue) el = new Border { BorderBrush = Br(st.Border.Value), BorderThickness = new Thickness(2.5), Child = el };
                switch (st.Effect)
                {
                    case "shadow": el.Effect = new DropShadowEffect { ShadowDepth = 5, BlurRadius = 8, Opacity = .5 }; break;
                    case "glow": el.Effect = new DropShadowEffect { ShadowDepth = 0, BlurRadius = 18, Opacity = .9, Color = Th.Accent1 }; break;
                    case "soft": el.OpacityMask = new RadialGradientBrush(new GradientStopCollection { new GradientStop(Colors.Black, 0.72), new GradientStop(Colors.Transparent, 1) }) { RadiusX = 0.6, RadiusY = 0.6 }; break;
                    case "bevel": el = new Border { BorderThickness = new Thickness(4), BorderBrush = new LinearGradientBrush(Color.FromRgb(0xF4, 0xF4, 0xF4), Color.FromRgb(0x70, 0x70, 0x70), 45), Child = el }; break;
                    case "reflection":
                        {
                            var holder = new StackPanel();
                            var top = el;
                            var refl = new Rectangle { Width = dw, Height = dh * 0.35, Fill = new VisualBrush(top) { Stretch = Stretch.None, AlignmentY = AlignmentY.Bottom, Transform = new ScaleTransform(1, -1, 0, dh * 0.175) },
                                OpacityMask = new LinearGradientBrush(Color.FromArgb(0x60, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 90), Margin = new Thickness(0, 2, 0, 0) };
                            holder.Children.Add(top); holder.Children.Add(refl);
                            el = holder;
                            break;
                        }
                }
                var host = new Grid { Background = Brushes.Transparent };
                double pad = st.Effect == "glow" ? 16 : st.Effect == "shadow" || st.Style == "shadow-rect" || st.Style == "frame-white" ? 10 : 2;
                el.Margin = new Thickness(pad);
                host.Children.Add(el);
                host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var size = host.DesiredSize;
                var (s, png) = RenderVisual(host, Math.Ceiling(size.Width), Math.Ceiling(size.Height));
                final = s; bytes = png;
                dw = size.Width; dh = size.Height;
            }
            data.Bytes = bytes; data.Ext = "png";
            img.Source = final;
            img.Width = Math.Round(dw); img.Height = Math.Round(dh);
            MarkDirty(); ScheduleLayout();
            SelectImage(img);
        }

        void CompressPictures()
        {
            var d = new DialogBase(this, "Compression Settings", 360);
            var selOnly = new CheckBox { Content = "Apply to selected pictures only", IsChecked = _selectedImage != null, IsEnabled = _selectedImage != null };
            var print = new RadioButton { Content = "Print (220 ppi): excellent quality on most printers", IsChecked = true };
            var screen = new RadioButton { Content = "Screen (150 ppi): good for web pages and projectors" };
            var email = new RadioButton { Content = "E-mail (96 ppi): minimize document size for sharing" };
            var deleteCrop = new CheckBox { Content = "Delete cropped areas of pictures", IsChecked = true, Margin = new Thickness(0, 6, 0, 0) };
            var sp = new StackPanel(); sp.Children.Add(selOnly);
            var tg = new StackPanel(); tg.Children.Add(print); tg.Children.Add(screen); tg.Children.Add(email);
            sp.Children.Add(D.Group("Target output", tg)); sp.Children.Add(deleteCrop);
            d.Content = UI.DialogLayout(sp, UI.Strip(UI.Btn("OK", (s, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
            if (d.ShowDialog() != true) return;
            double ppi = print.IsChecked == true ? 220 : screen.IsChecked == true ? 150 : 96;
            var images = selOnly.IsChecked == true && _selectedImage != null ? new List<Image> { _selectedImage } : AllDocImages().ToList();
            long before = 0, after = 0; int n = 0;
            foreach (var img in images)
            {
                if (!(img.Source is BitmapSource bs)) continue;
                var data = img.Tag as ImageData ?? new ImageData();
                before += data.Bytes?.Length ?? 0;
                double targetW = img.Width / 96 * ppi;
                BitmapSource outBmp = bs;
                if (bs.PixelWidth > targetW * 1.05)
                {
                    double scale = targetW / bs.PixelWidth;
                    outBmp = new TransformedBitmap(bs, new ScaleTransform(scale, scale));
                }
                bool opaque = !(bs.Format == PixelFormats.Bgra32 || bs.Format == PixelFormats.Pbgra32) || data.Ext == "jpeg";
                BitmapEncoder enc = opaque ? new JpegBitmapEncoder { QualityLevel = ppi >= 220 ? 90 : 80 } : (BitmapEncoder)new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(outBmp));
                using var ms = new MemoryStream(); enc.Save(ms);
                var bytes = ms.ToArray();
                if (data.Bytes != null && bytes.Length >= data.Bytes.Length) { after += data.Bytes.Length; continue; }
                data.Bytes = bytes; data.Ext = opaque ? "jpeg" : "png";
                if (deleteCrop.IsChecked == true) { data.Original = null; PicStates.Remove(img); }
                img.Tag = data;
                after += bytes.Length; n++;
            }
            MarkDirty();
            Flash(n == 0 ? "The pictures are already compressed." : $"Compressed {n} picture{(n == 1 ? "" : "s")}: {before / 1024:N0} KB → {after / 1024:N0} KB.");
        }

        IEnumerable<Image> AllDocImages()
        {
            foreach (var b in Fmt.AllBlocks(Editor.Document.Blocks))
            {
                if (b is BlockUIContainer bc && bc.Child is Image bi) yield return bi;
                if (b is Paragraph p)
                    foreach (var i in Fmt.Descendants(p))
                    {
                        if (i is InlineUIContainer c && c.Child is Image ii) yield return ii;
                        if (i is Floater f) foreach (var fb in f.Blocks.OfType<BlockUIContainer>()) if (fb.Child is Image fi) yield return fi;
                    }
            }
        }

        void ChangePicture()
        {
            var img = RequirePicture(); if (img == null) return;
            var dlg = new OpenFileDialog { Title = "Change Picture", Filter = "All Pictures|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.ico|All Files|*.*", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var bytes = File.ReadAllBytes(dlg.FileName);
                var src = LoadBitmap(bytes);
                var ext = System.IO.Path.GetExtension(dlg.FileName).TrimStart('.').ToLowerInvariant(); if (ext == "jpg") ext = "jpeg";
                double w = img.Width;
                img.Tag = new ImageData { Bytes = (ext == "png" || ext == "jpeg" || ext == "gif" || ext == "bmp") ? bytes : EncodePng(src), Ext = (ext == "png" || ext == "jpeg" || ext == "gif" || ext == "bmp") ? ext : "png" };
                img.Source = src;
                img.Width = w; img.Height = Math.Round(w * src.PixelHeight / (double)src.PixelWidth);
                var st = PicStates.TryGetValue(img, out var s0) ? s0 : null;
                if (st != null) { st.CropL = st.CropR = st.CropT = st.CropB = 0; RebuildPicture(img); }
                MarkDirty(); ScheduleLayout();
            }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message, "That picture couldn't be used."); }
        }

        void ResetPicture()
        {
            var img = RequirePicture(); if (img == null) return;
            var data = img.Tag as ImageData;
            if (data?.Original == null) { Flash("This picture hasn't been changed."); return; }
            img.Source = data.Original;
            img.Width = data.OriginalWidth > 0 ? data.OriginalWidth : data.Original.PixelWidth;
            img.Height = data.OriginalHeight > 0 ? data.OriginalHeight : data.Original.PixelHeight;
            data.Bytes = EncodePng(data.Original); data.Ext = "png";
            PicStates.Remove(img);
            img.LayoutTransform = Transform.Identity;
            MarkDirty(); ScheduleLayout(); SelectImage(img);
        }

        void CropPicture()
        {
            var img = RequirePicture(); if (img == null) return;
            var orig = OriginalOf(img);
            var st = PicStates.GetOrCreateValue(img);
            var d = new DialogBase(this, "Crop", 520);
            var l = D.Box((st.CropL * 100).ToString("0"), 50); var t = D.Box((st.CropT * 100).ToString("0"), 50);
            var r = D.Box((st.CropR * 100).ToString("0"), 50); var b = D.Box((st.CropB * 100).ToString("0"), 50);
            var canvas = new Canvas { Width = 260, Height = 200 };
            double sc = Math.Min(260.0 / orig.PixelWidth, 200.0 / orig.PixelHeight), iw = orig.PixelWidth * sc, ih = orig.PixelHeight * sc;
            canvas.Children.Add(new Image { Source = orig, Width = iw, Height = ih, Opacity = 0.45 });
            var keep = new Rectangle { Stroke = Brushes.Black, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 3, 2 }, Fill = new ImageBrush(orig) { ViewboxUnits = BrushMappingMode.RelativeToBoundingBox } };
            canvas.Children.Add(keep);
            void Refresh()
            {
                double L = Clamp(l), T = Clamp(t), R = Clamp(r), B = Clamp(b);
                if (L + R > 0.95) R = 0.95 - L; if (T + B > 0.95) B = 0.95 - T;
                keep.Width = iw * (1 - L - R); keep.Height = ih * (1 - T - B);
                Canvas.SetLeft(keep, iw * L); Canvas.SetTop(keep, ih * T);
                ((ImageBrush)keep.Fill).Viewbox = new Rect(L, T, 1 - L - R, 1 - T - B);
            }
            double Clamp(TextBox x) => Math.Max(0, Math.Min(0.9, (D.ParseNum(x.Text) ?? 0) / 100));
            foreach (var x in new[] { l, t, r, b }) x.TextChanged += (s, e) => Refresh();
            var form = D.Form(("Left (%):", l), ("Top (%):", t), ("Right (%):", r), ("Bottom (%):", b));
            var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(form);
            row.Children.Add(new Border { Margin = new Thickness(16, 0, 0, 0), BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Child = canvas });
            d.Content = UI.DialogLayout(row, UI.Strip(UI.Btn("OK", (s, e) => d.DialogResult = true, true), UI.Btn("Reset", (s, e) => { l.Text = t.Text = r.Text = b.Text = "0"; }), UI.Btn("Cancel", null, false, true)));
            d.Loaded += (s, e) => Refresh();
            if (d.ShowDialog() != true) return;
            double oldVisibleW = 1 - st.CropL - st.CropR;
            st.CropL = Clamp(l); st.CropT = Clamp(t); st.CropR = Clamp(r); st.CropB = Clamp(b);
            if (st.CropL + st.CropR > 0.95) st.CropR = 0.95 - st.CropL;
            if (st.CropT + st.CropB > 0.95) st.CropB = 0.95 - st.CropT;
            // keep the same scale: the displayed width shrinks with the crop
            img.Width = Math.Max(16, img.Width / Math.Max(0.05, oldVisibleW) * (1 - st.CropL - st.CropR));
            RebuildPicture(img);
        }

        void SetPictureSize(double? wIn, double? hIn)
        {
            var img = RequirePicture(); if (img == null) return;
            double aspect = img.Height / Math.Max(1, img.Width);
            if (wIn.HasValue && wIn > 0) { img.Width = wIn.Value * 96; img.Height = img.Width * aspect; }
            else if (hIn.HasValue && hIn > 0) { img.Height = hIn.Value * 96; img.Width = img.Height / aspect; }
            MarkDirty(); ScheduleLayout(); SelectImage(img);
        }
    }
}
