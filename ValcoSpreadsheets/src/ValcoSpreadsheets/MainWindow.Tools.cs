using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ClosedXML.Excel;
using Microsoft.Win32;
using ValcoSpreadsheets.Grid;
using ValcoSpreadsheets.Model;

namespace ValcoSpreadsheets
{
    public partial class MainWindow
    {
        // ===================================================================== Format Cells
        void FormatCells(string tab)
        {
            if (G.IsEditing) CommitEdit(0, 0);
            var d = new FormatCellsDialog(this, _book.Wb, G.ActiveCell, tab);
            if (d.ShowDialog() != true) return;
            StyleSelection("Format Cells", d.ApplyTo);
            if (d.BorderChanges.Count > 0)
                Change("Borders", () =>
                {
                    foreach (var r in G.View.Selection)
                    {
                        if (r.IsWholeColumns || r.IsWholeRows) continue;
                        d.ApplyBorders(WS.Range(r.R1, r.C1, r.R2, r.C2));
                    }
                });
            G.Relayout();
        }

        // ===================================================================== Find / Replace / Go To
        FindDialog _find;
        void ShowFind(bool replace)
        {
            if (_find == null || !_find.IsLoaded)
            {
                _find = new FindDialog(this);
                _find.FindNext += o => FindNext(o);
                _find.FindAllRequested += o => FindAll(o);
                _find.ReplaceOne += (o, w) => ReplaceOne(o, w);
                _find.ReplaceAll += (o, w) => ReplaceAll(o, w);
                _find.ResultPicked += (sheet, r, c) => GoTo(sheet, new CellRect(r, c, r, c));
                _find.Show();
            }
            _find.ShowTab(replace);
            _find.Activate();
        }

        IEnumerable<IXLWorksheet> SearchSheets(Ops.FindOptions o)
        {
            if (!o.WholeBook) { yield return WS; yield break; }
            int n = _book.Wb.Worksheets.Count;
            for (int k = 0; k < n; k++) yield return _book.Wb.Worksheet((SheetIdx - 1 + k) % n + 1);
        }

        bool FindNext(Ops.FindOptions o)
        {
            if (string.IsNullOrEmpty(o.What)) return false;
            int r0 = G.View.ActiveRow, c0 = G.View.ActiveCol;
            bool first = true;
            foreach (var ws in SearchSheets(o))
            {
                var hits = Ops.FindAll(ws, o);
                IXLCell next = null;
                if (first)
                {
                    next = hits.FirstOrDefault(c => o.ByColumns
                        ? c.Address.ColumnNumber > c0 || c.Address.ColumnNumber == c0 && c.Address.RowNumber > r0
                        : c.Address.RowNumber > r0 || c.Address.RowNumber == r0 && c.Address.ColumnNumber > c0);
                    if (next == null && !o.WholeBook) next = hits.FirstOrDefault();
                }
                else next = hits.FirstOrDefault();
                first = false;
                if (next != null) { GoTo(ws.Position, new CellRect(next.Address.RowNumber, next.Address.ColumnNumber, next.Address.RowNumber, next.Address.ColumnNumber)); return true; }
            }
            if (o.WholeBook)
            {
                var hits = Ops.FindAll(WS, o);
                if (hits.Count > 0) { var h = hits[0]; GoTo(SheetIdx, new CellRect(h.Address.RowNumber, h.Address.ColumnNumber, h.Address.RowNumber, h.Address.ColumnNumber)); return true; }
            }
            VistaDialog.Show(_find ?? (Window)this, "Valco Spreadsheets cannot find the data you're searching for.", "Valco Spreadsheets", MessageBoxButton.OK, DlgIcon.Info);
            return false;
        }

        void FindAll(Ops.FindOptions o)
        {
            var list = new List<(int Sheet, string SheetName, int Row, int Col, string Value)>();
            foreach (var ws in SearchSheets(o))
                foreach (var c in Ops.FindAll(ws, o).Take(5000))
                    list.Add((ws.Position, ws.Name, c.Address.RowNumber, c.Address.ColumnNumber, c.HasFormula ? "=" + c.FormulaA1 : c.GetFormattedString()));
            _find?.ShowResults(list);
            if (list.Count == 0) VistaDialog.Show(_find ?? (Window)this, "Valco Spreadsheets cannot find the data you're searching for.", "Valco Spreadsheets", MessageBoxButton.OK, DlgIcon.Info);
        }

        void ReplaceOne(Ops.FindOptions o, string with)
        {
            var c = G.ActiveCell;
            if (Ops.Matches(c, o))
            {
                var r = G.View.ActiveRow; var col = G.View.ActiveCol;
                Change("Replace", () => Ops.ReplaceIn(c, o, with), new List<CellRect> { new CellRect(r, col, r, col) });
            }
            FindNext(o);
        }

        void ReplaceAll(Ops.FindOptions o, string with)
        {
            int count = 0;
            var sheets = SearchSheets(o).ToList();
            Change("Replace All", () =>
            {
                foreach (var ws in sheets)
                    foreach (var c in Ops.FindAll(ws, o)) if (Ops.ReplaceIn(c, o, with)) count++;
            }, structure: true);
            VistaDialog.Show(_find ?? (Window)this, count == 0 ? "Valco Spreadsheets cannot find any data to replace. Check if your search formatting and criteria are defined correctly." : $"Valco Spreadsheets has completed its search and has made {count} replacements.", "Valco Spreadsheets", MessageBoxButton.OK, DlgIcon.Info);
        }

        void GoToDialogShow()
        {
            var names = _book.Wb.DefinedNames.Select(n => n.Name).OrderBy(n => n).ToList();
            var t = ListDialog.AskOrType(this, "Go To", "Go to:", names, "Reference:");
            if (string.IsNullOrWhiteSpace(t)) return;
            NameBox.Text = t;
            NameBox_KeyDown(NameBox, new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
        }

        void GoToSpecialDialog()
        {
            var opts = new[] { "Comments", "Constants", "Formulas", "Blanks", "Current region", "Last cell", "Visible cells only", "Data validation", "Conditional formats" };
            var pick = ChoiceDialog.Ask(this, "Go To Special", opts, 0);
            if (pick < 0) return;
            SelectSpecial(new[] { "Comments", "Constants", "Formulas", "Blanks", "Region", "Last", "Visible", "Validation", "CondFormats" }[pick]);
        }

        void SelectSpecial(string what)
        {
            var scope = G.ActiveRect.Count > 1 ? G.ActiveRect : new CellRect(1, 1, Math.Max(1, G.Layout.LastRow), Math.Max(1, G.Layout.LastCol));
            var range = WS.Range(scope.R1, scope.C1, scope.R2, scope.C2);
            IEnumerable<IXLCell> cells;
            switch (what)
            {
                case "Region": G.SelectRect(Ops.CurrentRegion(WS, G.View.ActiveRow, G.View.ActiveCol), G.View.ActiveRow, G.View.ActiveCol); return;
                case "Last": G.Select(G.Layout.LastRow, G.Layout.LastCol); G.EnsureVisible(G.Layout.LastRow, G.Layout.LastCol); return;
                case "Visible": return;
                case "Comments": cells = range.CellsUsed(XLCellsUsedOptions.Comments).Where(c => c.HasComment); break;
                case "Formulas": cells = range.CellsUsed(XLCellsUsedOptions.Contents).Where(c => c.HasFormula); break;
                case "Constants": cells = range.CellsUsed(XLCellsUsedOptions.Contents).Where(c => !c.HasFormula); break;
                case "Validation": cells = range.CellsUsed(XLCellsUsedOptions.DataValidation).Where(c => c.HasDataValidation); break;
                case "CondFormats": cells = range.CellsUsed(XLCellsUsedOptions.ConditionalFormats); break;
                case "Blanks":
                    var blanks = new List<IXLCell>();
                    for (int r = scope.R1; r <= scope.R2 && blanks.Count < 20000; r++) for (int c = scope.C1; c <= scope.C2; c++) if (WS.Cell(r, c).IsEmpty()) blanks.Add(WS.Cell(r, c));
                    cells = blanks; break;
                default: return;
            }
            var list = cells.Take(20000).Select(c => new CellRect(c.Address.RowNumber, c.Address.ColumnNumber, c.Address.RowNumber, c.Address.ColumnNumber)).ToList();
            if (list.Count == 0) { VistaDialog.Show(this, "No cells were found.", "Valco Spreadsheets"); return; }
            G.View.Selection = list;
            G.View.ActiveRow = G.View.AnchorRow = list[0].R1; G.View.ActiveCol = G.View.AnchorCol = list[0].C1;
            G.EnsureVisible(list[0].R1, list[0].C1);
            QueueSync();
        }

        // ===================================================================== functions
        void InsertFunction_Click(object sender, RoutedEventArgs e)
        {
            var d = new InsertFunctionDialog(this);
            if (d.ShowDialog() != true || d.Picked == null) return;
            InsertFunctionNamed(d.Picked.Name);
        }

        void InsertFunctionNamed(string name)
        {
            Functions.Used(name);
            var fn = Functions.ByName.TryGetValue(name, out var f) ? f : null;
            bool noArgs = fn != null && fn.Args.Length == 0;
            if (G.IsEditing)
            {
                var tb = FormulaBox.IsKeyboardFocused ? FormulaBox : G.EditorBox;
                InsertAtCaret(tb, name + "(" + (noArgs ? ")" : ""));
                tb.Focus();
                return;
            }
            // Function Arguments dialog, like Excel's
            var args = new FunctionArgumentsDialog(this, fn ?? new Functions.Fn(name, "", "", ""), r => { var ws = WS; return EvaluateText(r); });
            if (args.ShowDialog() != true) return;
            CommitText("=" + name + "(" + args.ArgumentText + ")", false);
        }

        string EvaluateText(string expr)
        {
            try
            {
                var v = WS.Evaluate(expr, A1.Cell(G.View.ActiveRow, G.View.ActiveCol));
                if (v.IsError) return v.ToString();
                return v.ToString(CultureInfo.CurrentCulture);
            }
            catch { return ""; }
        }

        void ErrorChecking()
        {
            var errors = WS.CellsUsed(XLCellsUsedOptions.Contents).Where(c => { try { return c.HasFormula && c.Value.IsError; } catch { return true; } })
                .OrderBy(c => c.Address.RowNumber).ThenBy(c => c.Address.ColumnNumber).ToList();
            if (errors.Count == 0) { VistaDialog.Show(this, "The error check has been completed for the entire sheet.", "Valco Spreadsheets"); return; }
            var c = errors.FirstOrDefault(x => x.Address.RowNumber > G.View.ActiveRow || x.Address.RowNumber == G.View.ActiveRow && x.Address.ColumnNumber > G.View.ActiveCol) ?? errors[0];
            GoTo(SheetIdx, new CellRect(c.Address.RowNumber, c.Address.ColumnNumber, c.Address.RowNumber, c.Address.ColumnNumber));
            string val; try { val = c.GetFormattedString(); } catch { val = "#VALUE!"; }
            string why = val switch
            {
                "#DIV/0!" => "The formula or function used is dividing by zero or empty cells.",
                "#NAME?" => "The formula contains unrecognized text, such as a misspelled function or a name that isn't defined.",
                "#REF!" => "The formula refers to cells that were deleted or replaced.",
                "#N/A" => "A value is not available to the function or formula.",
                "#NUM!" => "The formula or function contains invalid numeric values.",
                _ => "A value used in the formula is of the wrong data type."
            };
            VistaDialog.Show(this, $"Error in cell {A1.Cell(c.Address.RowNumber, c.Address.ColumnNumber)}\n=" + c.FormulaA1 + "\n\n" + val + ":  " + why + $"\n\n{errors.Count} cell(s) on this sheet have errors.", "Error Checking", MessageBoxButton.OK, DlgIcon.Warning);
        }

        void EvaluateFormulaDialog()
        {
            var c = G.ActiveCell;
            if (!c.HasFormula) { VistaDialog.Show(this, "The active cell doesn't contain a formula.", "Evaluate Formula"); return; }
            var refs = Functions.References("=" + c.FormulaA1);
            var lines = new List<string> { "Reference: " + QuoteSheet(WS.Name) + "!" + A1.Cell(G.View.ActiveRow, G.View.ActiveCol), "Evaluation: =" + c.FormulaA1, "" };
            foreach (var r in refs.Take(20))
            {
                string v;
                try { v = r.Count == 1 ? WS.Cell(r.R1, r.C1).GetFormattedString() : "(" + r.Count + " cells)"; } catch { v = "?"; }
                lines.Add($"   {r.Address} = {v}");
            }
            string result; try { result = c.GetFormattedString(); } catch { result = "#VALUE!"; }
            lines.Add("");
            lines.Add("Result: " + result);
            VistaDialog.Show(this, string.Join("\n", lines), "Evaluate Formula", MessageBoxButton.OK, DlgIcon.None);
        }

        // ===================================================================== series / data tools
        void SeriesDialogShow()
        {
            var d = new SeriesDialog(this, G.ActiveRect.Rows >= G.ActiveRect.Cols);
            if (d.ShowDialog() != true) return;
            var r = G.ActiveRect;
            if (r.IsWholeColumns) r = new CellRect(G.View.ActiveRow, r.C1, Math.Min(SheetLimits.Rows, G.View.ActiveRow + 10000), r.C2);
            Change("Series", () => Ops.FillSeries(WS, r, d.Columns, d.Growth, d.Step, d.Stop, d.Dates, d.DateUnit), new List<CellRect> { r });
        }

        void DataValidationDialog()
        {
            var c = G.ActiveCell;
            var d = new DataValidationDialog(this, c.HasDataValidation ? c.GetDataValidation() : null);
            if (d.ShowDialog() != true) return;
            Change("Data Validation", () =>
            {
                foreach (var r in G.View.Selection)
                {
                    if (r.IsWholeColumns || r.IsWholeRows) continue;
                    var range = WS.Range(r.R1, r.C1, r.R2, r.C2);
                    if (d.ClearAll) { foreach (var cell in range.Cells().Where(x => x.HasDataValidation)) cell.GetDataValidation().Clear(); continue; }
                    d.Apply(range.CreateDataValidation());
                }
            }, structure: true);
        }

        void GroupRows()
        {
            var s = G.ActiveRect;
            if (s.IsWholeColumns) Change("Group", () => WS.Columns(s.C1, s.C2).Group(), structure: true);
            else Change("Group", () => WS.Rows(s.R1, s.R2).Group(), structure: true);
        }

        void UngroupRows()
        {
            var s = G.ActiveRect;
            if (s.IsWholeColumns) Change("Ungroup", () => WS.Columns(s.C1, s.C2).Ungroup(), structure: true);
            else Change("Ungroup", () => WS.Rows(s.R1, s.R2).Ungroup(), structure: true);
        }

        void SubtotalDialog()
        {
            var r = SortRange(out bool header);
            if (!header || r.Rows < 3) { VistaDialog.Show(this, "Select a list with column labels in the first row, then try again.", "Subtotal", MessageBoxButton.OK, DlgIcon.Warning); return; }
            var cols = Enumerable.Range(r.C1, r.Cols).Select(c => WS.Cell(r.R1, c).GetFormattedString()).ToList();
            var d = new SubtotalDialogWindow(this, cols);
            if (d.ShowDialog() != true) return;
            int groupCol = r.C1 + d.GroupBy, fnNum = d.FunctionNumber;
            var sumCols = d.SumColumns.Select(i => r.C1 + i).ToList();
            Change("Subtotal", () =>
            {
                int row = r.R1 + 1, last = r.R2;
                string Key(int rr) => WS.Cell(rr, groupCol).GetFormattedString();
                int start = row;
                var totals = new List<int>();
                while (row <= last + 1)
                {
                    if (row > last || Key(row) != Key(start))
                    {
                        WS.Row(row).InsertRowsAbove(1);
                        WS.Cell(row, groupCol).Value = Key(start) + " " + d.FunctionName;
                        WS.Cell(row, groupCol).Style.Font.Bold = true;
                        foreach (var c in sumCols) { WS.Cell(row, c).FormulaA1 = $"SUBTOTAL({fnNum},{A1.Cell(start, c)}:{A1.Cell(row - 1, c)})"; WS.Cell(row, c).Style.Font.Bold = true; }
                        totals.Add(row);
                        last++;
                        row++;
                        start = row;
                        if (row > last) break;
                        continue;
                    }
                    row++;
                }
                int grand = last + 1;
                WS.Cell(grand, groupCol).Value = "Grand " + d.FunctionName;
                WS.Cell(grand, groupCol).Style.Font.Bold = true;
                foreach (var c in sumCols) { WS.Cell(grand, c).FormulaA1 = $"SUBTOTAL({fnNum},{A1.Cell(r.R1 + 1, c)}:{A1.Cell(last, c)})"; WS.Cell(grand, c).Style.Font.Bold = true; }
            }, structure: true);
        }

        void ImportText()
        {
            var dlg = new OpenFileDialog { Filter = "Text Files (*.prn;*.txt;*.csv)|*.prn;*.txt;*.csv;*.tsv|All Files (*.*)|*.*", Title = "Import Text File" };
            if (dlg.ShowDialog(this) != true) return;
            string text;
            try { text = Delimited.ReadText(dlg.FileName); } catch (Exception ex) { VistaDialog.Error(this, ex.Message); return; }
            char sep = dlg.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? ',' : text.Contains('\t') ? '\t' : ',';
            var rows = Delimited.Parse(text, sep);
            int r0 = G.View.ActiveRow, c0 = G.View.ActiveCol;
            int maxC = rows.Count == 0 ? 1 : rows.Max(x => x.Count);
            Change("Import Text", () =>
            {
                for (int i = 0; i < rows.Count; i++) for (int j = 0; j < rows[i].Count; j++) if (rows[i][j].Length > 0) CellInput.Apply(WS.Cell(r0 + i, c0 + j), rows[i][j], fromFile: true);
            }, new List<CellRect> { new CellRect(r0, c0, r0 + Math.Max(0, rows.Count - 1), c0 + maxC - 1) });
            G.SelectRect(new CellRect(r0, c0, r0 + Math.Max(0, rows.Count - 1), c0 + maxC - 1));
        }

        void InsertSymbol()
        {
            var s = SymbolDialog.Ask(this);
            if (s == null) return;
            if (G.IsEditing) { InsertAtCaret(FormulaBox.IsKeyboardFocused ? FormulaBox : G.EditorBox, s); return; }
            G.BeginEdit(CellInput.EditText(G.ActiveCell) + s, EditMode.Edit);
            _editSheet = SheetIdx;
        }

        void InsertPicture()
        {
            var dlg = new OpenFileDialog { Filter = "All Pictures (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp|All Files (*.*)|*.*", Title = "Insert Picture" };
            if (dlg.ShowDialog(this) != true) return;
            int r = G.View.ActiveRow, c = G.View.ActiveCol;
            var path = dlg.FileName;
            Change("Insert Picture", () =>
            {
                var pic = WS.AddPicture(path);
                pic.MoveTo(WS.Cell(r, c));
                // keep big pictures a sensible size
                if (pic.Width > 640) pic.Scale(640.0 / pic.Width);
            }, structure: true);
        }

        // ===================================================================== spelling
        TextBox _spellBox;
        readonly HashSet<string> _ignoreAll = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Checks the text cells of the sheet with the Windows spelling dictionaries.</summary>
        void SpellCheckSheet()
        {
            if (_spellBox == null)
            {
                _spellBox = new TextBox { Width = 600, Height = 30, Opacity = 0, IsHitTestVisible = false, Focusable = false, AcceptsReturn = true };
                SpellCheck.SetIsEnabled(_spellBox, true);
                Sheet.Overlay.Children.Add(_spellBox);
                System.Windows.Controls.Canvas.SetLeft(_spellBox, -2000);
            }
            var cells = WS.CellsUsed(XLCellsUsedOptions.Contents).Where(c => !c.HasFormula && c.Value.IsText)
                .OrderBy(c => c.Address.RowNumber).ThenBy(c => c.Address.ColumnNumber).ToList();
            int start = cells.FindIndex(c => c.Address.RowNumber > G.View.ActiveRow || c.Address.RowNumber == G.View.ActiveRow && c.Address.ColumnNumber >= G.View.ActiveCol);
            if (start < 0) start = 0;
            var order = cells.Skip(start).Concat(cells.Take(start)).ToList();
            var changeAll = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int changed = 0;
            foreach (var cell in order)
            {
                var text = cell.GetString();
                _spellBox.Text = text;
                _spellBox.UpdateLayout();
                int pos = 0;
                while (true)
                {
                    int idx = _spellBox.GetNextSpellingErrorCharacterIndex(pos, LogicalDirection.Forward);
                    if (idx < 0) break;
                    int len = _spellBox.GetSpellingErrorLength(idx);
                    var word = _spellBox.Text.Substring(idx, len);
                    pos = idx + len;
                    if (_ignoreAll.Contains(word)) continue;
                    string replacement = null;
                    if (changeAll.TryGetValue(word, out var ca)) replacement = ca;
                    else
                    {
                        var err = _spellBox.GetSpellingError(idx);
                        GoTo(SheetIdx, new CellRect(cell.Address.RowNumber, cell.Address.ColumnNumber, cell.Address.RowNumber, cell.Address.ColumnNumber));
                        var d = new SpellingDialog(this, word, err?.Suggestions.ToList() ?? new List<string>());
                        d.ShowDialog();
                        switch (d.Action)
                        {
                            case "Cancel": FinishSpelling(changed, false); return;
                            case "IgnoreAll": _ignoreAll.Add(word); continue;
                            case "Ignore": continue;
                            case "ChangeAll": changeAll[word] = d.Replacement; replacement = d.Replacement; break;
                            case "Change": replacement = d.Replacement; break;
                            case "Add": err?.IgnoreAll(); _ignoreAll.Add(word); continue;
                        }
                    }
                    if (replacement != null)
                    {
                        var newText = _spellBox.Text.Substring(0, idx) + replacement + _spellBox.Text.Substring(idx + len);
                        _spellBox.Text = newText;
                        _spellBox.UpdateLayout();
                        pos = idx + replacement.Length;
                        var c2 = cell; var t2 = newText;
                        Change("Spelling", () => c2.Value = t2, new List<CellRect> { new CellRect(c2.Address.RowNumber, c2.Address.ColumnNumber, c2.Address.RowNumber, c2.Address.ColumnNumber) });
                        changed++;
                    }
                }
            }
            FinishSpelling(changed, true);
        }

        void FinishSpelling(int changed, bool complete)
        {
            if (complete) VistaDialog.Show(this, "The spelling check is complete for the entire sheet." + (changed > 0 ? $"\n\n{changed} change(s) made." : ""), "Valco Spreadsheets");
            G.Focus();
        }

        // ===================================================================== conditional formatting
        ContextMenu CondFormatMenu()
        {
            MenuItem Rule(string text, Action a, string icon = null) => RB.MI(text, a, icon);
            var highlight = new MenuItem { Header = "Highlight Cells Rules", Icon = Ico("IconCondFormat") };
            highlight.Items.Add(Rule("Greater Than...", () => HighlightRule("Greater Than", "Format cells that are GREATER THAN:", (cf, v, _) => cf.WhenGreaterThan(v))));
            highlight.Items.Add(Rule("Less Than...", () => HighlightRule("Less Than", "Format cells that are LESS THAN:", (cf, v, _) => cf.WhenLessThan(v))));
            highlight.Items.Add(Rule("Between...", () => HighlightRule("Between", "Format cells that are BETWEEN:", (cf, v, v2) => cf.WhenBetween(v, v2), true)));
            highlight.Items.Add(Rule("Equal To...", () => HighlightRule("Equal To", "Format cells that are EQUAL TO:", (cf, v, _) => cf.WhenEquals(v))));
            highlight.Items.Add(Rule("Text that Contains...", () => HighlightRule("Text That Contains", "Format cells that contain the text:", (cf, v, _) => cf.WhenContains(v.Trim('"')))));
            highlight.Items.Add(Rule("Duplicate Values...", () => ApplyCf(cf => cf.WhenIsDuplicate(), PresetStyles[0].Apply)));
            var top = new MenuItem { Header = "Top/Bottom Rules" };
            top.Items.Add(Rule("Top 10 Items...", () => ApplyCf(cf => cf.WhenIsTop(10, XLTopBottomType.Items), PresetStyles[0].Apply)));
            top.Items.Add(Rule("Top 10 %...", () => ApplyCf(cf => cf.WhenIsTop(10, XLTopBottomType.Percent), PresetStyles[0].Apply)));
            top.Items.Add(Rule("Bottom 10 Items...", () => ApplyCf(cf => cf.WhenIsBottom(10, XLTopBottomType.Items), PresetStyles[0].Apply)));
            top.Items.Add(Rule("Bottom 10 %...", () => ApplyCf(cf => cf.WhenIsBottom(10, XLTopBottomType.Percent), PresetStyles[0].Apply)));
            var bars = new MenuItem { Header = "Data Bars" };
            foreach (var (n, c) in new[] { ("Blue Data Bar", "#638EC6"), ("Green Data Bar", "#63BE7B"), ("Red Data Bar", "#F8696B"), ("Orange Data Bar", "#FFB628"), ("Light Blue Data Bar", "#008AEF"), ("Purple Data Bar", "#D6007B") })
            {
                var col = XLColor.FromHtml(c);
                bars.Items.Add(Rule(n, () => ApplyCfRaw(cf => cf.DataBar(col).LowestValue().HighestValue())));
            }
            var scales = new MenuItem { Header = "Color Scales" };
            foreach (var (n, a, m, z) in new[] { ("Green - Yellow - Red", "#F8696B", "#FFEB84", "#63BE7B"), ("Red - Yellow - Green", "#63BE7B", "#FFEB84", "#F8696B"), ("Green - White - Red", "#F8696B", "#FFFFFF", "#63BE7B"), ("Blue - White - Red", "#F8696B", "#FFFFFF", "#5A8AC6") })
            {
                var lo = XLColor.FromHtml(a); var mid = XLColor.FromHtml(m); var hi = XLColor.FromHtml(z);
                scales.Items.Add(Rule(n, () => ApplyCfRaw(cf => cf.ColorScale().LowestValue(lo).Midpoint(XLCFContentType.Percentile, 50, mid).HighestValue(hi))));
            }
            foreach (var (n, a, z) in new[] { ("White - Green", "#FCFCFF", "#63BE7B"), ("White - Red", "#FCFCFF", "#F8696B"), ("Yellow - Green", "#FFEF9C", "#63BE7B") })
            {
                var lo = XLColor.FromHtml(a); var hi = XLColor.FromHtml(z);
                scales.Items.Add(Rule(n, () => ApplyCfRaw(cf => cf.ColorScale().LowestValue(lo).HighestValue(hi))));
            }
            var clear = new MenuItem { Header = "Clear Rules" };
            clear.Items.Add(Rule("Clear Rules from Selected Cells", ClearCfSelection));
            clear.Items.Add(Rule("Clear Rules from Entire Sheet", () => { var ws = WS; Change("Clear Rules", () => ws.ConditionalFormats.RemoveAll(), structure: true); }));
            return RB.Menu(highlight, top, bars, scales, new Separator(), clear);
        }

        public record CfPreset(string Name, Action<IXLStyle> Apply);
        static readonly CfPreset[] PresetStyles =
        {
            new("Light Red Fill with Dark Red Text", s => { s.Fill.BackgroundColor = XLColor.FromHtml("#FFC7CE"); s.Font.FontColor = XLColor.FromHtml("#9C0006"); }),
            new("Yellow Fill with Dark Yellow Text", s => { s.Fill.BackgroundColor = XLColor.FromHtml("#FFEB9C"); s.Font.FontColor = XLColor.FromHtml("#9C6500"); }),
            new("Green Fill with Dark Green Text", s => { s.Fill.BackgroundColor = XLColor.FromHtml("#C6EFCE"); s.Font.FontColor = XLColor.FromHtml("#006100"); }),
            new("Light Red Fill", s => s.Fill.BackgroundColor = XLColor.FromHtml("#FFC7CE")),
            new("Red Text", s => s.Font.FontColor = XLColor.FromHtml("#9C0006")),
            new("Red Border", s => s.Border.OutsideBorder = XLBorderStyleValues.Thin),
        };

        void HighlightRule(string title, string prompt, Func<IXLConditionalFormat, string, string, IXLStyle> rule, bool two = false)
        {
            var d = new CfRuleDialog(this, title, prompt, two, PresetStyles.Select(p => p.Name).ToList());
            if (d.ShowDialog() != true) return;
            var preset = PresetStyles[Math.Clamp(d.PresetIndex, 0, PresetStyles.Length - 1)];
            ApplyCf(cf => rule(cf, CfValue(d.Value1), CfValue(d.Value2)), preset.Apply);
        }

        static string CfValue(string v)
        {
            v = (v ?? "").Trim();
            if (v.StartsWith("=")) return v;
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.CurrentCulture, out var d)) return d.ToString(CultureInfo.InvariantCulture);
            return "\"" + v.Trim('"') + "\"";
        }

        void ApplyCf(Func<IXLConditionalFormat, IXLStyle> rule, Action<IXLStyle> style)
        {
            var sel = G.View.Selection.ToList();
            Change("Conditional Formatting", () =>
            {
                foreach (var r in sel)
                {
                    var rr = r.IsWholeColumns ? new CellRect(1, r.C1, Math.Max(1, G.Layout.LastRow), r.C2) : r;
                    var st = rule(WS.Range(rr.R1, rr.C1, rr.R2, rr.C2).AddConditionalFormat());
                    style(st);
                }
            }, structure: true);
        }

        void ApplyCfRaw(Action<IXLConditionalFormat> rule)
        {
            var sel = G.View.Selection.ToList();
            Change("Conditional Formatting", () =>
            {
                foreach (var r in sel)
                {
                    var rr = r.IsWholeColumns ? new CellRect(1, r.C1, Math.Max(1, G.Layout.LastRow), r.C2) : r;
                    rule(WS.Range(rr.R1, rr.C1, rr.R2, rr.C2).AddConditionalFormat());
                }
            }, structure: true);
        }

        void ClearCfSelection()
        {
            var sel = G.View.Selection.ToList();
            Change("Clear Rules", () =>
            {
                foreach (var cf in WS.ConditionalFormats.ToList())
                {
                    bool hit = cf.Ranges.Any(r => sel.Any(s => s.Intersects(Rect(r.AsRange()))));
                    if (hit) WS.ConditionalFormats.Remove(x => x == cf);
                }
            }, structure: true);
        }

        // ===================================================================== cell styles / tables
        public record CellStylePreset(string Group, string Name, Action<IXLStyle> Apply, Color Back, Color Fore);
        static readonly CellStylePreset[] CellStyles =
        {
            new("Good, Bad and Neutral", "Normal", s => { s.Fill.PatternType = XLFillPatternValues.None; s.Font.FontColor = XLColor.FromTheme(XLThemeColor.Text1); s.Font.Bold = false; s.Border.OutsideBorder = XLBorderStyleValues.None; }, Colors.White, Colors.Black),
            new("Good, Bad and Neutral", "Bad", s => { s.Fill.BackgroundColor = XLColor.FromHtml("#FFC7CE"); s.Font.FontColor = XLColor.FromHtml("#9C0006"); }, Color.FromRgb(0xFF, 0xC7, 0xCE), Color.FromRgb(0x9C, 0x00, 0x06)),
            new("Good, Bad and Neutral", "Good", s => { s.Fill.BackgroundColor = XLColor.FromHtml("#C6EFCE"); s.Font.FontColor = XLColor.FromHtml("#006100"); }, Color.FromRgb(0xC6, 0xEF, 0xCE), Color.FromRgb(0x00, 0x61, 0x00)),
            new("Good, Bad and Neutral", "Neutral", s => { s.Fill.BackgroundColor = XLColor.FromHtml("#FFEB9C"); s.Font.FontColor = XLColor.FromHtml("#9C6500"); }, Color.FromRgb(0xFF, 0xEB, 0x9C), Color.FromRgb(0x9C, 0x65, 0x00)),
            new("Data and Model", "Calculation", s => { s.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2"); s.Font.FontColor = XLColor.FromHtml("#FA7D00"); s.Font.Bold = true; s.Border.OutsideBorder = XLBorderStyleValues.Thin; s.Border.OutsideBorderColor = XLColor.FromHtml("#7F7F7F"); }, Color.FromRgb(0xF2, 0xF2, 0xF2), Color.FromRgb(0xFA, 0x7D, 0x00)),
            new("Data and Model", "Check Cell", s => { s.Fill.BackgroundColor = XLColor.FromHtml("#A5A5A5"); s.Font.FontColor = XLColor.White; s.Font.Bold = true; s.Border.OutsideBorder = XLBorderStyleValues.Double; s.Border.OutsideBorderColor = XLColor.FromHtml("#3F3F3F"); }, Color.FromRgb(0xA5, 0xA5, 0xA5), Colors.White),
            new("Data and Model", "Input", s => { s.Fill.BackgroundColor = XLColor.FromHtml("#FFCC99"); s.Font.FontColor = XLColor.FromHtml("#3F3F76"); s.Border.OutsideBorder = XLBorderStyleValues.Thin; s.Border.OutsideBorderColor = XLColor.FromHtml("#7F7F7F"); }, Color.FromRgb(0xFF, 0xCC, 0x99), Color.FromRgb(0x3F, 0x3F, 0x76)),
            new("Data and Model", "Output", s => { s.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2"); s.Font.FontColor = XLColor.FromHtml("#3F3F3F"); s.Font.Bold = true; s.Border.OutsideBorder = XLBorderStyleValues.Thin; s.Border.OutsideBorderColor = XLColor.FromHtml("#3F3F3F"); }, Color.FromRgb(0xF2, 0xF2, 0xF2), Color.FromRgb(0x3F, 0x3F, 0x3F)),
            new("Data and Model", "Note", s => { s.Fill.BackgroundColor = XLColor.FromHtml("#FFFFCC"); s.Border.OutsideBorder = XLBorderStyleValues.Thin; s.Border.OutsideBorderColor = XLColor.FromHtml("#B2B2B2"); }, Color.FromRgb(0xFF, 0xFF, 0xCC), Colors.Black),
            new("Data and Model", "Warning Text", s => s.Font.FontColor = XLColor.FromHtml("#FF0000"), Colors.White, Colors.Red),
            new("Titles and Headings", "Title", s => { s.Font.FontSize = 18; s.Font.Bold = true; s.Font.FontColor = XLColor.FromTheme(XLThemeColor.Text2); }, Colors.White, Color.FromRgb(0x1F, 0x49, 0x7D)),
            new("Titles and Headings", "Heading 1", s => { s.Font.FontSize = 15; s.Font.Bold = true; s.Font.FontColor = XLColor.FromTheme(XLThemeColor.Text2); s.Border.BottomBorder = XLBorderStyleValues.Thick; s.Border.BottomBorderColor = XLColor.FromTheme(XLThemeColor.Accent1); }, Colors.White, Color.FromRgb(0x1F, 0x49, 0x7D)),
            new("Titles and Headings", "Heading 2", s => { s.Font.FontSize = 13; s.Font.Bold = true; s.Font.FontColor = XLColor.FromTheme(XLThemeColor.Text2); s.Border.BottomBorder = XLBorderStyleValues.Thick; s.Border.BottomBorderColor = XLColor.FromTheme(XLThemeColor.Accent1, 0.5); }, Colors.White, Color.FromRgb(0x1F, 0x49, 0x7D)),
            new("Titles and Headings", "Heading 3", s => { s.Font.Bold = true; s.Font.FontColor = XLColor.FromTheme(XLThemeColor.Text2); s.Border.BottomBorder = XLBorderStyleValues.Medium; s.Border.BottomBorderColor = XLColor.FromTheme(XLThemeColor.Accent1, 0.4); }, Colors.White, Color.FromRgb(0x1F, 0x49, 0x7D)),
            new("Titles and Headings", "Total", s => { s.Font.Bold = true; s.Border.TopBorder = XLBorderStyleValues.Thin; s.Border.BottomBorder = XLBorderStyleValues.Double; s.Border.TopBorderColor = XLColor.FromTheme(XLThemeColor.Accent1); s.Border.BottomBorderColor = XLColor.FromTheme(XLThemeColor.Accent1); }, Colors.White, Colors.Black),
            new("Themed Cell Styles", "Accent1", s => { s.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent1); s.Font.FontColor = XLColor.White; }, Color.FromRgb(0x4F, 0x81, 0xBD), Colors.White),
            new("Themed Cell Styles", "Accent2", s => { s.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent2); s.Font.FontColor = XLColor.White; }, Color.FromRgb(0xC0, 0x50, 0x4D), Colors.White),
            new("Themed Cell Styles", "Accent3", s => { s.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent3); s.Font.FontColor = XLColor.White; }, Color.FromRgb(0x9B, 0xBB, 0x59), Colors.White),
            new("Themed Cell Styles", "Accent4", s => { s.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent4); s.Font.FontColor = XLColor.White; }, Color.FromRgb(0x80, 0x64, 0xA2), Colors.White),
            new("Themed Cell Styles", "Accent5", s => { s.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent5); s.Font.FontColor = XLColor.White; }, Color.FromRgb(0x4B, 0xAC, 0xC6), Colors.White),
            new("Themed Cell Styles", "Accent6", s => { s.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent6); s.Font.FontColor = XLColor.White; }, Color.FromRgb(0xF7, 0x96, 0x46), Colors.White),
            new("Themed Cell Styles", "20% - Accent1", s => { s.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent1, 0.8); }, Color.FromRgb(0xDC, 0xE6, 0xF1), Colors.Black),
            new("Themed Cell Styles", "20% - Accent2", s => { s.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent2, 0.8); }, Color.FromRgb(0xF2, 0xDC, 0xDB), Colors.Black),
            new("Themed Cell Styles", "20% - Accent3", s => { s.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent3, 0.8); }, Color.FromRgb(0xEB, 0xF1, 0xDE), Colors.Black),
            new("Themed Cell Styles", "20% - Accent4", s => { s.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent4, 0.8); }, Color.FromRgb(0xE4, 0xDF, 0xEC), Colors.Black),
            new("Themed Cell Styles", "20% - Accent5", s => { s.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent5, 0.8); }, Color.FromRgb(0xDA, 0xEE, 0xF3), Colors.Black),
            new("Themed Cell Styles", "20% - Accent6", s => { s.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent6, 0.8); }, Color.FromRgb(0xFD, 0xE9, 0xD9), Colors.Black),
            new("Number Format", "Comma", s => s.NumberFormat.Format = "_(* #,##0.00_);_(* \\(#,##0.00\\);_(* \"-\"??_);_(@_)", Colors.White, Colors.Black),
            new("Number Format", "Currency", s => s.NumberFormat.Format = "_(\"$\"* #,##0.00_);_(\"$\"* \\(#,##0.00\\);_(\"$\"* \"-\"??_);_(@_)", Colors.White, Colors.Black),
            new("Number Format", "Percent", s => s.NumberFormat.Format = "0%", Colors.White, Colors.Black),
        };

        void CellStylesGallery()
        {
            var tiles = new List<UIElement>();
            string group = null;
            foreach (var cs in CellStyles)
            {
                if (cs.Group != group)
                {
                    group = cs.Group;
                    tiles.Add(new TextBlock { Text = group, FontWeight = FontWeights.Bold, Width = 460, Margin = new Thickness(4, 6, 4, 2), Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x42, 0x8B)) });
                }
                var preset = cs;
                var visual = new Border { Background = new SolidColorBrush(cs.Back), BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Width = 84, Height = 24, Child = new TextBlock { Text = cs.Name, Foreground = new SolidColorBrush(cs.Fore), Margin = new Thickness(4, 3, 2, 2), FontSize = cs.Name.StartsWith("Title") ? 13 : 11, FontWeight = cs.Name.StartsWith("Head") || cs.Name == "Title" || cs.Name == "Total" ? FontWeights.Bold : FontWeights.Normal } };
                tiles.Add(RB.Tile(visual, cs.Name, () => StyleSelection("Cell Style", preset.Apply), 92, 30));
            }
            RB.ShowGallery(G, "Cell Styles", tiles, 480);
        }

        static readonly string[] TableThemes =
        {
            "TableStyleLight1", "TableStyleLight2", "TableStyleLight3", "TableStyleLight4", "TableStyleLight5", "TableStyleLight6", "TableStyleLight7",
            "TableStyleLight8", "TableStyleLight9", "TableStyleLight10", "TableStyleLight11", "TableStyleLight12", "TableStyleLight13", "TableStyleLight14",
            "TableStyleMedium1", "TableStyleMedium2", "TableStyleMedium3", "TableStyleMedium4", "TableStyleMedium5", "TableStyleMedium6", "TableStyleMedium7",
            "TableStyleMedium8", "TableStyleMedium9", "TableStyleMedium10", "TableStyleMedium11", "TableStyleMedium12", "TableStyleMedium13", "TableStyleMedium14",
            "TableStyleDark1", "TableStyleDark2", "TableStyleDark3", "TableStyleDark4", "TableStyleDark5", "TableStyleDark6", "TableStyleDark7",
        };

        void FormatAsTable(string theme)
        {
            if (theme == null)
            {
                var tiles = TableThemes.Select(t => (UIElement)RB.Tile(TablePreview(t), t.Replace("TableStyle", "Table Style "), () => FormatAsTable(t), 62, 48)).ToList();
                RB.ShowGallery(G, "Format as Table", tiles, 470);
                return;
            }
            var r = SortRange(out bool header);
            if (r.Count == 1 && WS.Cell(r.R1, r.C1).IsEmpty()) r = new CellRect(r.R1, r.C1, r.R1 + 4, r.C1 + 2);
            bool hasHeader = header || TableHeaderDialog(r);
            var ws = WS;
            var name = "Table" + (_book.Wb.Worksheets.Sum(w => w.Tables.Count()) + 1);
            Change("Format as Table", () =>
            {
                if (!hasHeader) { ws.Row(r.R1).InsertRowsAbove(1); for (int c = r.C1; c <= r.C2; c++) ws.Cell(r.R1, c).Value = "Column" + (c - r.C1 + 1); }
                var rr = hasHeader ? r : new CellRect(r.R1, r.C1, r.R2 + 1, r.C2);
                if (G.FilterRange.HasValue) { ws.AutoFilter.Clear(); ws.SetAutoFilter(false); }
                var t = ws.Range(rr.R1, rr.C1, rr.R2, rr.C2).CreateTable(name);
                t.Theme = XLTableTheme.FromName(theme);
                TableLook.Materialize(t, _book.Wb);
            }, structure: true);
        }

        bool TableHeaderDialog(CellRect r) =>
            VistaDialog.Show(this, $"Where is the data for your table?\n\n=${A1.Col(r.C1)}${r.R1}:${A1.Col(r.C2)}${r.R2}\n\nDoes the table have headers?", "Create Table", MessageBoxButton.YesNo, DlgIcon.Question) == MessageBoxResult.Yes;

        static UIElement TablePreview(string theme)
        {
            var (head, band, line, dark) = TableLook.Colors(theme);
            var g = new System.Windows.Controls.Grid { Width = 52, Height = 38 };
            for (int i = 0; i < 5; i++) g.RowDefinitions.Add(new RowDefinition());
            for (int i = 0; i < 5; i++)
            {
                var row = new Border { Background = new SolidColorBrush(i == 0 ? head : i % 2 == 1 ? band : (dark ? line : Colors.White)), BorderBrush = new SolidColorBrush(line), BorderThickness = new Thickness(0, 0, 0, 0.5) };
                System.Windows.Controls.Grid.SetRow(row, i);
                g.Children.Add(row);
            }
            return new Border { BorderBrush = new SolidColorBrush(line), BorderThickness = new Thickness(1), Child = g };
        }
    }

    /// <summary>
    /// Table styles: Excel draws table banding from the style name; ClosedXML keeps the name but doesn't fill the cells,
    /// so the colours are also written as cell formatting (which Excel shows the same way).
    /// </summary>
    public static class TableLook
    {
        static readonly Color[] Accents = { Color.FromRgb(0x00, 0x00, 0x00), Color.FromRgb(0x4F, 0x81, 0xBD), Color.FromRgb(0xC0, 0x50, 0x4D), Color.FromRgb(0x9B, 0xBB, 0x59), Color.FromRgb(0x80, 0x64, 0xA2), Color.FromRgb(0x4B, 0xAC, 0xC6), Color.FromRgb(0xF7, 0x96, 0x46) };

        public static (Color Head, Color Band, Color Line, bool Dark) Colors(string theme)
        {
            int n = int.TryParse(new string(theme.Where(char.IsDigit).ToArray()), out var v) ? v : 2;
            var accent = Accents[(n - 1) % 7];
            if (accent.R == 0 && accent.G == 0 && accent.B == 0) accent = Color.FromRgb(0x40, 0x40, 0x40);
            if (theme.Contains("Light")) return (n <= 7 ? Mix(accent, 0.15) : accent, Mix(accent, 0.22), accent, false);
            if (theme.Contains("Dark")) return (Color.FromRgb(0x10, 0x10, 0x10), Mix(accent, 0.75), Mix(accent, 0.6), true);
            return (accent, Mix(accent, 0.25), Mix(accent, 0.6), false);
        }

        static Color Mix(Color c, double amount) => Color.FromRgb((byte)(255 - (255 - c.R) * amount), (byte)(255 - (255 - c.G) * amount), (byte)(255 - (255 - c.B) * amount));

        public static void Materialize(IXLTable t, XLWorkbook wb)
        {
            var (head, band, line, dark) = Colors(t.Theme?.Name ?? "TableStyleMedium2");
            var a = t.RangeAddress;
            int r1 = a.FirstAddress.RowNumber, r2 = a.LastAddress.RowNumber, c1 = a.FirstAddress.ColumnNumber, c2 = a.LastAddress.ColumnNumber;
            var ws = t.Worksheet;
            var headRow = ws.Range(r1, c1, r1, c2);
            headRow.Style.Fill.BackgroundColor = XlColors.FromWpf(head);
            headRow.Style.Font.Bold = true;
            if (Brightness(head) < 0.55) headRow.Style.Font.FontColor = XLColor.White;
            for (int r = r1 + 1; r <= r2; r++)
            {
                var row = ws.Range(r, c1, r, c2);
                if ((r - r1) % 2 == 1) row.Style.Fill.BackgroundColor = XlColors.FromWpf(band);
                else if (dark) row.Style.Fill.BackgroundColor = XlColors.FromWpf(line);
                row.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                row.Style.Border.BottomBorderColor = XlColors.FromWpf(line);
            }
        }

        static double Brightness(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
    }
}
