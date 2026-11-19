using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ClosedXML.Excel;
using Microsoft.Win32;
using ValcoSpreadsheets.Grid;
using ValcoSpreadsheets.Model;

namespace ValcoSpreadsheets
{
    public partial class MainWindow : VistaWindow
    {
        Book _book;
        SheetCanvas G => Sheet.Canvas;
        IXLWorksheet WS => G.Sheet;
        int SheetIdx => G.SheetIndex;
        readonly DispatcherTimer _autoRecover;
        string _recoverPath;
        bool _syncing;

        public Book Book => _book;

        public MainWindow() : this(null) { }

        public MainWindow(Book book)
        {
            InitializeComponent();
            _book = book ?? new Book();
            if (book == null && Settings.Current.DefaultSheets != 3)
                _book.ReplaceWorkbook(Book.NewWorkbook(Math.Clamp(Settings.Current.DefaultSheets, 1, 255)));
            ApplyDefaultFont(_book);
            BuildRibbon();
            HookGrid();
            HookObjects();
            BuildTabNav();
            HScrollHost.Content = Sheet.HScroll;
            ShowSheet(ActiveSheetOf(_book));
            UpdateTitle();
            RestorePlacement();
            ShowBookMessages();

            PreviewKeyDown += Window_PreviewKeyDown;
            Drop += Window_Drop;
            Closing += Window_Closing;
            Activated += (s, e) => { if (!G.IsEditing) G.Focus(); };
            Loaded += (s, e) =>
            {
                G.Focus();
                Updates.CheckOnStartup(this, "sheets", "Valco Spreadsheets");
            };
            _autoRecover = new DispatcherTimer { Interval = TimeSpan.FromMinutes(Math.Max(1, Settings.Current.AutoRecoverMinutes)) };
            _autoRecover.Tick += (s, e) => SaveRecovery();
            if (Settings.Current.AutoRecover) _autoRecover.Start();
        }

        static void ApplyDefaultFont(Book b)
        {
            if (b.FilePath != null) return;
            try
            {
                if (Settings.Current.DefaultFont != "Calibri" || Math.Abs(Settings.Current.DefaultSize - 11) > 0.01)
                {
                    b.Wb.Style.Font.FontName = Settings.Current.DefaultFont;
                    b.Wb.Style.Font.FontSize = Settings.Current.DefaultSize;
                }
            }
            catch { }
        }

        static int ActiveSheetOf(Book b)
        {
            try
            {
                var active = b.Wb.Worksheets.FirstOrDefault(w => w.TabActive && w.Visibility == XLWorksheetVisibility.Visible);
                if (active != null) return active.Position;
                return b.Wb.Worksheets.First(w => w.Visibility == XLWorksheetVisibility.Visible).Position;
            }
            catch { return 1; }
        }

        // ===================================================================== grid wiring
        void HookGrid()
        {
            G.SelectionChanged += () => QueueSync();
            G.EditCommitted += text => CommitText(text, false);
            G.ModeChanged += OnModeChanged;
            G.EditorTextChanged += OnEditorTextChanged;
            G.EditorKey += Editor_Key;
            G.CommitRequested += () => CommitEdit(0, 0);
            G.WantsReference = FormulaWantsReference;
            G.PointRefChanged += InsertPointedRef;
            G.FillDragged += (src, dst, copy) => DoFill(src, dst, copy);
            G.MoveDragged += (src, r, c, copy) => MoveCells(src, r, c, copy);
            G.SizeDragged += (cols, idx, px) => ResizeLanes(cols, idx, px);
            G.AutoFitRequested += (cols, i) => { if (cols) AutoFitColumns(SelectedWholeColumns(i)); else AutoFitRows(SelectedWholeRows(i), force: true); };
            G.ContextMenuRequested += ShowCellMenu;
            G.FilterButtonClicked += (r, c, p) => ShowFilterMenu(r, c, p);
            G.HyperlinkClicked += FollowHyperlink;
            G.ZoomRequested += d => SetZoom(G.Zoom + d);
            G.ToolTipText += t => StatusMsg.Text = t ?? "";
            G.ProtectedEdit += () => VistaDialog.Show(this, "The cell or chart that you are trying to change is protected and therefore read-only.\n\nTo modify a protected cell or chart, first remove protection using the Unprotect Sheet command (Review tab, Changes group).", "Valco Spreadsheets", MessageBoxButton.OK, DlgIcon.Warning);
            FormulaBox.GotKeyboardFocus += FormulaBox_GotFocus;
            FormulaBox.TextChanged += FormulaBox_TextChanged;
            FormulaBox.PreviewKeyDown += FormulaBox_PreviewKeyDown;
            NameBox.KeyDown += NameBox_KeyDown;
            NameBox.DropDownOpened += (s, e) => NameBox.ItemsSource = _book.Wb.DefinedNames.Select(n => n.Name).Concat(WS.DefinedNames.Select(n => n.Name)).Distinct().OrderBy(n => n).ToList();
            NameBox.SelectionChanged += (s, e) => { if (NameBox.SelectedItem is string n && NameBox.IsDropDownOpen) GoToName(n); };
            _book.Undo.StackChanged += UpdateUndoButtons;
        }

        static bool IsInside(DependencyObject d, DependencyObject container)
        {
            while (d != null) { if (d == container) return true; d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d); }
            return false;
        }

        void ShowSheet(int index)
        {
            if (G.IsEditing) CommitEdit(0, 0);
            index = Math.Clamp(index, 1, _book.Wb.Worksheets.Count);
            G.Attach(_book, index);
            _book.ActiveSheetForExport = WS;
            BuildTabs();
            _zoomSync = true;
            ZoomSlider.Value = Math.Round(G.Zoom * 100);
            _zoomSync = false;
            ZoomText.Text = Math.Round(G.Zoom * 100) + "%";
            SyncSheetToggles();
            G.SetMarquee(_clip != null && _clip.Book == _book && _clip.Sheet == WS.Name ? _clip.Rect : (CellRect?)null);
            QueueSync();
        }

        /// <summary>After undo/redo of a structural change the workbook object is replaced.</summary>
        void RefreshAll(int? sheet = null)
        {
            int idx = Math.Clamp(sheet ?? SheetIdx, 1, _book.Wb.Worksheets.Count);
            G.Attach(_book, idx);
            G.Relayout();
            BuildTabs();
            QueueSync();
            UpdateTitle();
        }

        bool _syncQueued;
        void QueueSync()
        {
            if (_syncQueued) return;
            _syncQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { _syncQueued = false; SyncUi(); }));
        }

        void SyncUi()
        {
            if (_book == null) return;
            var sel = G.ActiveRect;
            // Name Box: a defined name that matches exactly, else the active cell
            string name = null;
            try
            {
                foreach (var dn in _book.Wb.DefinedNames)
                {
                    var r = dn.Ranges.FirstOrDefault();
                    if (r == null || r.Worksheet != WS) continue;
                    var a = r.RangeAddress;
                    if (a.FirstAddress.RowNumber == sel.R1 && a.FirstAddress.ColumnNumber == sel.C1 && a.LastAddress.RowNumber == sel.R2 && a.LastAddress.ColumnNumber == sel.C2) { name = dn.Name; break; }
                }
            }
            catch { }
            if (!NameBox.IsKeyboardFocusWithin) NameBox.Text = name ?? A1.Cell(G.View.ActiveRow, G.View.ActiveCol);
            if (!G.IsEditing && !FormulaBox.IsKeyboardFocused)
            {
                _syncing = true;
                FormulaBox.Text = WS.IsProtected && G.ActiveCell.Style.Protection.Hidden ? "" : CellInput.EditText(G.ActiveCell);
                _syncing = false;
            }
            SyncRibbon();
            UpdateStats();
        }

        void UpdateStats()
        {
            StatAvg.Text = StatCount.Text = StatSum.Text = "";
            if (G.View.Selection.Count == 1 && G.ActiveRect.Count == 1) return;
            double sum = 0; int nums = 0, count = 0;
            foreach (var (r, c) in G.SelectedCellsUsed().Take(500000))
            {
                count++;
                try
                {
                    var v = WS.Cell(r, c).Value;
                    if (v.IsNumber) { sum += v.GetNumber(); nums++; }
                    else if (v.IsDateTime) { sum += v.GetDateTime().ToOADate(); nums++; }
                }
                catch { }
            }
            if (count < 2) return;
            if (nums > 0)
            {
                StatAvg.Text = "Average: " + FormatStat(sum / nums);
                StatSum.Text = "Sum: " + FormatStat(sum);
            }
            StatCount.Text = "Count: " + count.ToString("N0", CultureInfo.CurrentCulture);
        }

        string FormatStat(double v)
        {
            try
            {
                var c = G.ActiveCell;
                var f = c.Style.NumberFormat.Format;
                if (!string.IsNullOrEmpty(f) && f != "General" && !f.Contains("%"))
                {
                    var tmp = new XLWorkbook().AddWorksheet("x").Cell(1, 1);
                    tmp.Value = v; tmp.Style.NumberFormat.Format = f;
                    return tmp.GetFormattedString();
                }
            }
            catch { }
            return Math.Round(v, 10).ToString("G10", CultureInfo.CurrentCulture);
        }

        void OnModeChanged()
        {
            ModeText.Text = G.Mode switch { EditMode.Enter => "Enter", EditMode.Edit => "Edit", EditMode.Point => "Point", _ => "Ready" };
            BtnFxCancel.IsEnabled = BtnFxEnter.IsEnabled = G.IsEditing;
            if (G.IsEditing && _editSheet == 0) _editSheet = SheetIdx;
            if (!G.IsEditing) { HideAutoComplete(); HideArgTip(); G.HighlightRefs(null); QueueSync(); }
            else
            {
                _syncing = true;
                FormulaBox.Text = G.EditText;
                _syncing = false;
            }
        }

        // ===================================================================== editing
        void OnEditorTextChanged(string text)
        {
            if (!_syncing)
            {
                _syncing = true;
                FormulaBox.Text = text;
                _syncing = false;
            }
            var box = FormulaBox.IsKeyboardFocused ? FormulaBox : G.EditorBox;
            if (text.StartsWith("="))
            {
                G.HighlightRefs(Functions.References(text));
                UpdateAutoComplete(box);
                UpdateArgTip(box);
                if (G.Mode == EditMode.Point && !_insertingRef) { G.Mode = EditMode.Enter; ModeText.Text = "Enter"; }
            }
            else { G.HighlightRefs(null); HideAutoComplete(); HideArgTip(); }
            if (G.Mode == EditMode.Enter) G.PositionEditor();
        }

        void FormulaBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (G.IsEditing) return;
            if (WS.IsProtected && !CellUnlocked()) return;
            var caret = FormulaBox.CaretIndex;
            G.BeginEdit(null, EditMode.Edit);
            FormulaBox.Focus();
            FormulaBox.CaretIndex = Math.Min(caret, FormulaBox.Text.Length);
        }

        bool CellUnlocked() { try { return !G.ActiveCell.Style.Protection.Locked; } catch { return false; } }

        void FormulaBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncing || !G.IsEditing) return;
            _syncing = true;
            var ed = G.EditorBox;
            ed.Text = FormulaBox.Text;
            _syncing = false;
        }

        void FormulaBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!G.IsEditing) return;
            if (HandleAutoCompleteKey(e)) return;
            switch (e.Key)
            {
                case Key.Enter:
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) { InsertAtCaret(FormulaBox, "\n"); e.Handled = true; return; }
                    CommitEdit(Settings.Current.MoveAfterEnter ? (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1) : 0, 0, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
                    e.Handled = true; break;
                case Key.Tab:
                    CommitEdit(0, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
                    e.Handled = true; break;
                case Key.Escape:
                    CancelEdit(); e.Handled = true; break;
                case Key.F4:
                    ToggleAbsolute(FormulaBox); e.Handled = true; break;
            }
        }

        static void InsertAtCaret(TextBox tb, string s)
        {
            int i = tb.CaretIndex;
            tb.Text = tb.Text.Insert(i, s);
            tb.CaretIndex = i + s.Length;
        }

        void Editor_Key(KeyEventArgs e)
        {
            var ed = G.EditorBox;
            if (HandleAutoCompleteKey(e)) return;
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            switch (key)
            {
                case Key.Enter:
                    if (alt) { InsertAtCaret(ed, "\n"); ed.TextWrapping = TextWrapping.Wrap; e.Handled = true; return; }
                    CommitEdit(Settings.Current.MoveAfterEnter ? (shift ? -1 : DirRow()) : 0, Settings.Current.MoveAfterEnter && !shift ? DirCol() : 0, ctrl);
                    e.Handled = true; return;
                case Key.Tab:
                    CommitEdit(0, shift ? -1 : 1);
                    e.Handled = true; return;
                case Key.Escape:
                    CancelEdit(); e.Handled = true; return;
                case Key.F2:
                    G.Mode = G.Mode == EditMode.Edit ? EditMode.Enter : EditMode.Edit;
                    ModeText.Text = G.Mode.ToString(); e.Handled = true; return;
                case Key.F4:
                    ToggleAbsolute(ed); e.Handled = true; return;
                case Key.Up: case Key.Down: case Key.Left: case Key.Right:
                    if (G.Mode == EditMode.Enter || G.Mode == EditMode.Point)
                    {
                        int dr = key == Key.Up ? -1 : key == Key.Down ? 1 : 0, dc = key == Key.Left ? -1 : key == Key.Right ? 1 : 0;
                        if (FormulaWantsReference() || G.Mode == EditMode.Point)
                        {
                            PointWithKeys(dr, dc, shift);
                            e.Handled = true; return;
                        }
                        CommitEdit(dr, dc);
                        e.Handled = true;
                    }
                    return;
                case Key.A:
                    if (ctrl && shift) { ShowArgumentNames(ed); e.Handled = true; }
                    else if (ctrl) { ed.SelectAll(); e.Handled = true; }
                    return;
                case Key.OemSemicolon:
                    if (ctrl) { InsertAtCaret(ed, shift ? DateTime.Now.ToString("t", CultureInfo.CurrentCulture) : DateTime.Today.ToString("d", CultureInfo.CurrentCulture)); e.Handled = true; }
                    return;
            }
        }

        int DirRow() => Settings.Current.EnterDirection switch { "Up" => -1, "Down" => 1, _ => 0 };
        int DirCol() => Settings.Current.EnterDirection switch { "Left" => -1, "Right" => 1, _ => 0 };

        bool _insertingRef;
        int _pointStart = -1, _pointLen;
        int _pointRow, _pointCol, _pointAnchorRow, _pointAnchorCol;

        /// <summary>Whether a click/arrow should insert a reference: editing a formula with the caret after an operator.</summary>
        bool FormulaWantsReference()
        {
            if (!G.IsEditing) return false;
            var tb = FormulaBox.IsKeyboardFocused ? FormulaBox : G.EditorBox;
            var t = tb.Text;
            if (!t.StartsWith("=")) return false;
            if (G.Mode == EditMode.Point && _pointStart >= 0) return true;
            int i = tb.CaretIndex - 1;
            while (i >= 0 && t[i] == ' ') i--;
            if (i < 0) return false;
            return "=+-*/^&(,:<>;".IndexOf(t[i]) >= 0;
        }

        void InsertPointedRef(CellRect r, bool extending)
        {
            var tb = FormulaBox.IsKeyboardFocused ? FormulaBox : G.EditorBox;
            string text = r.Count == 1 ? A1.Cell(r.R1, r.C1) : r.Address;
            if (G.SheetIndex != _editSheet && _editSheet > 0) text = QuoteSheet(WS.Name) + "!" + text;
            _insertingRef = true;
            if (G.Mode != EditMode.Point || _pointStart < 0 || (!extending && tb.CaretIndex != _pointStart + _pointLen))
            {
                _pointStart = tb.CaretIndex; _pointLen = 0;
            }
            var s = tb.Text;
            s = s.Remove(_pointStart, Math.Min(_pointLen, s.Length - _pointStart)).Insert(_pointStart, text);
            tb.Text = s;
            _pointLen = text.Length;
            tb.CaretIndex = _pointStart + _pointLen;
            G.Mode = EditMode.Point;
            ModeText.Text = "Point";
            _insertingRef = false;
            G.SetMarquee(r);
            tb.Focus();
        }

        void PointWithKeys(int dr, int dc, bool extend)
        {
            if (G.Mode != EditMode.Point || _pointStart < 0)
            {
                _pointRow = _pointAnchorRow = G.View.ActiveRow; _pointCol = _pointAnchorCol = G.View.ActiveCol;
                _pointStart = -1;
            }
            _pointRow = Math.Clamp(_pointRow + dr, 1, SheetLimits.Rows);
            _pointCol = Math.Clamp(_pointCol + dc, 1, SheetLimits.Cols);
            if (!extend) { _pointAnchorRow = _pointRow; _pointAnchorCol = _pointCol; }
            G.EnsureVisible(_pointRow, _pointCol);
            InsertPointedRef(new CellRect(_pointAnchorRow, _pointAnchorCol, _pointRow, _pointCol), _pointStart >= 0);
        }

        static string QuoteSheet(string name) => name.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_') ? "'" + name.Replace("'", "''") + "'" : name;

        int _editSheet;

        void ToggleAbsolute(TextBox tb)
        {
            // F4 cycles the reference at the caret: A1 -> $A$1 -> A$1 -> $A1 -> A1
            var t = tb.Text; int caret = tb.CaretIndex;
            var rx = new System.Text.RegularExpressions.Regex(@"\$?[A-Za-z]{1,3}\$?\d{1,7}");
            foreach (System.Text.RegularExpressions.Match m in rx.Matches(t))
            {
                if (caret < m.Index || caret > m.Index + m.Length) continue;
                var v = m.Value;
                var letters = new string(v.Where(char.IsLetter).ToArray()); var digits = new string(v.Where(char.IsDigit).ToArray());
                bool ac = v.StartsWith("$"), ar = v.IndexOf('$', 1) > 0;
                string next = (ac, ar) switch { (false, false) => "$" + letters + "$" + digits, (true, true) => letters + "$" + digits, (false, true) => "$" + letters + digits, _ => letters + digits };
                tb.Text = t.Substring(0, m.Index) + next + t.Substring(m.Index + m.Length);
                tb.CaretIndex = m.Index + next.Length;
                return;
            }
        }

        void CancelEdit()
        {
            _pointStart = -1;
            G.EndEdit(false);
            G.SetMarquee(_clip != null && _clip.Sheet == WS.Name ? _clip.Rect : (CellRect?)null);
            if (_editSheet > 0 && _editSheet != SheetIdx) ShowSheet(_editSheet);
            _editSheet = 0;
            G.Focus();
        }

        /// <summary>Ends editing, puts the text in the cell and moves the active cell.</summary>
        void CommitEdit(int dr, int dc, bool fillSelection = false)
        {
            if (!G.IsEditing) return;
            var text = G.EditText;
            if (text.StartsWith("=") && !text.StartsWith("==")) text = AutoCloseParens(text);
            if (text.StartsWith("=") && !FormulaOk(text.Substring(1), out var err))
            {
                VistaDialog.Show(this, "The formula you typed contains an error.\n\n" + err + "\n\nTo get assistance in entering a function, click Insert Function (fx) on the Formulas tab.", "Valco Spreadsheets", MessageBoxButton.OK, DlgIcon.Warning);
                (FormulaBox.IsKeyboardFocused ? (TextBox)FormulaBox : G.EditorBox).Focus();
                return;
            }
            _pointStart = -1;
            if (_editSheet > 0 && _editSheet != SheetIdx) ShowSheet(_editSheet);
            _editSheet = 0;
            G.EditorBox.Text = text;
            // EndEdit raises EditCommitted only when the text changed; handle Ctrl+Enter ourselves
            if (fillSelection && G.ActiveRect.Count > 1)
            {
                G.Mode = EditMode.Ready; G.EndEdit(false);
                CommitText(text, true);
            }
            else G.EndEdit(true);
            G.SetMarquee(_clip != null && _clip.Sheet == WS.Name ? _clip.Rect : (CellRect?)null);
            if (dr != 0 || dc != 0) G.MoveActive(dr, dc, true);
            G.Focus();
        }

        static string AutoCloseParens(string f)
        {
            int open = 0; bool inStr = false;
            foreach (var ch in f) { if (ch == '"') inStr = !inStr; else if (!inStr) { if (ch == '(') open++; else if (ch == ')') open--; } }
            if (inStr) f += "\"";
            return open > 0 ? f + new string(')', open) : f;
        }

        bool FormulaOk(string formula, out string error)
        {
            error = null;
            try
            {
                var test = new XLWorkbook().AddWorksheet("t").Cell(1, 1);
                test.FormulaA1 = formula;
                return true;
            }
            catch (Exception ex)
            {
                if (ex.GetType().Name.Contains("Pars") || ex is ArgumentException) { error = ex.Message; return false; }
                return true;
            }
        }

        /// <summary>Puts typed text into the active cell (or every selected cell for Ctrl+Enter), with undo.</summary>
        void CommitText(string text, bool fillSelection)
        {
            var ac = G.ActiveCell;
            int r0 = G.View.ActiveRow, c0 = G.View.ActiveCol;
            var region = fillSelection ? G.View.Selection.ToList() : new List<CellRect> { G.Layout.MergeAt(r0, c0) ?? new CellRect(r0, c0, r0, c0) };
            var label = "Typing '" + (text.Length > 20 ? text.Substring(0, 20) + "..." : text) + "' in " + A1.Cell(r0, c0);
            bool heights = false;
            try
            {
                if (!ValidateInput(ac, text)) return;
                _book.Undo.Do(label, SheetIdx, region, () =>
                {
                    CellInput.Apply(ac, text);
                    if (fillSelection)
                    {
                        string r1c1 = ac.HasFormula ? ac.FormulaR1C1 : null;
                        foreach (var rect in G.View.Selection)
                            for (int r = rect.R1; r <= Math.Min(rect.R2, rect.R1 + 100000); r++)
                                for (int c = rect.C1; c <= Math.Min(rect.C2, rect.C1 + 1000); c++)
                                {
                                    if (r == r0 && c == c0) continue;
                                    var cell = WS.Cell(r, c);
                                    if (r1c1 != null) cell.FormulaR1C1 = r1c1; else CellInput.Apply(cell, text);
                                }
                    }
                    if (text.StartsWith("=")) TrackFunctionUse(text);
                    heights = AutoFitRowsCore(region.SelectMany(rc => Enumerable.Range(rc.R1, Math.Min(rc.Rows, 2000))).Distinct());
                    if (text.StartsWith("=")) AutoFormatFormula(ac);
                }, region);
            }
            catch (Exception ex)
            {
                VistaDialog.Show(this, ex.Message, "Valco Spreadsheets", MessageBoxButton.OK, DlgIcon.Warning);
            }
            AfterChange(rowsChanged: heights || fillSelection);
        }

        /// <summary>A formula like =TODAY() or =A1 where A1 is a date gets the date format, like Excel.</summary>
        void AutoFormatFormula(IXLCell c)
        {
            try
            {
                if (c.Style.NumberFormat.NumberFormatId != 0 || !string.IsNullOrEmpty(c.Style.NumberFormat.Format)) return;
                var v = c.Value;
                if (v.IsDateTime) c.Style.NumberFormat.Format = c.FormulaA1.ToUpperInvariant().Contains("NOW") ? "m/d/yyyy h:mm" : "m/d/yyyy";
                else if (v.IsTimeSpan) c.Style.NumberFormat.Format = "h:mm";
            }
            catch { }
        }

        void TrackFunctionUse(string formula)
        {
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(formula, @"([A-Za-z][A-Za-z0-9.]*)\("))
                if (Functions.ByName.ContainsKey(m.Groups[1].Value)) Functions.Used(m.Groups[1].Value);
        }

        /// <summary>Data validation: stops values that break the cell's rule, like Excel's error alert.</summary>
        bool ValidateInput(IXLCell cell, string text)
        {
            try
            {
                if (!cell.HasDataValidation) return true;
                var dv = cell.GetDataValidation();
                if (dv.AllowedValues == XLAllowedValues.AnyValue || (dv.IgnoreBlanks && text.Length == 0)) return true;
                var (value, _) = CellInput.Parse(text);
                bool ok = true;
                double? num = value.IsNumber ? value.GetNumber() : value.IsDateTime ? value.GetDateTime().ToOADate() : (double?)null;
                double Arg(string s) { s = (s ?? "").TrimStart('='); try { var ev = WS.Evaluate(s); if (ev.IsNumber) return ev.GetNumber(); if (ev.IsDateTime) return ev.GetDateTime().ToOADate(); } catch { } return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0; }
                switch (dv.AllowedValues)
                {
                    case XLAllowedValues.List:
                        ok = ListItems(dv).Any(i => i.Equals(text, StringComparison.CurrentCultureIgnoreCase));
                        break;
                    case XLAllowedValues.WholeNumber:
                    case XLAllowedValues.Decimal:
                    case XLAllowedValues.Date:
                    case XLAllowedValues.Time:
                        if (!num.HasValue || (dv.AllowedValues == XLAllowedValues.WholeNumber && Math.Abs(num.Value % 1) > 1e-9)) { ok = false; break; }
                        double a = Arg(dv.MinValue), b = Arg(dv.MaxValue), n = num.Value;
                        ok = dv.Operator switch
                        {
                            XLOperator.Between => n >= a && n <= b, XLOperator.NotBetween => n < a || n > b,
                            XLOperator.EqualTo => n == a, XLOperator.NotEqualTo => n != a,
                            XLOperator.GreaterThan => n > a, XLOperator.LessThan => n < a,
                            XLOperator.EqualOrGreaterThan => n >= a, XLOperator.EqualOrLessThan => n <= a, _ => true
                        };
                        break;
                    case XLAllowedValues.TextLength:
                        double len = text.Length, la = Arg(dv.MinValue), lb = Arg(dv.MaxValue);
                        ok = dv.Operator switch { XLOperator.Between => len >= la && len <= lb, XLOperator.LessThan => len < la, XLOperator.GreaterThan => len > la, XLOperator.EqualTo => len == la, XLOperator.EqualOrLessThan => len <= la, XLOperator.EqualOrGreaterThan => len >= la, _ => true };
                        break;
                }
                if (ok || !dv.ShowErrorMessage) return true;
                var title = string.IsNullOrEmpty(dv.ErrorTitle) ? "Valco Spreadsheets" : dv.ErrorTitle;
                var msg = string.IsNullOrEmpty(dv.ErrorMessage) ? "The value you entered is not valid.\nA user has restricted values that can be entered into this cell." : dv.ErrorMessage;
                switch (dv.ErrorStyle)
                {
                    case XLErrorStyle.Information: return VistaDialog.Show(this, msg, title, MessageBoxButton.OKCancel, DlgIcon.Info) == MessageBoxResult.OK;
                    case XLErrorStyle.Warning: return VistaDialog.Show(this, msg + "\n\nContinue?", title, MessageBoxButton.YesNo, DlgIcon.Warning) == MessageBoxResult.Yes;
                    default: VistaDialog.Show(this, msg, title, MessageBoxButton.OK, DlgIcon.Error); return false;
                }
            }
            catch { return true; }
        }

        List<string> ListItems(IXLDataValidation dv)
        {
            var src = (dv.Value ?? dv.MinValue ?? "").Trim();
            if (src.StartsWith("=")) src = src.Substring(1);
            if (src.StartsWith("\"") && src.EndsWith("\"")) src = src.Trim('"');
            try
            {
                if (A1.TryParse(src, out var r) || src.Contains("!") || _book.Wb.DefinedNames.Contains(src))
                {
                    IXLRange range = src.Contains("!") || _book.Wb.DefinedNames.Contains(src) ? _book.Wb.Range(src) : WS.Range(src);
                    return range.Cells().Select(c => c.GetFormattedString()).Where(s => s.Length > 0).ToList();
                }
            }
            catch { }
            return src.Split(new[] { ',', ';' }).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        }

        /// <summary>Called after any change: redraws and refreshes the window.</summary>
        void AfterChange(bool rowsChanged = false, bool structure = false)
        {
            if (structure || rowsChanged) G.Relayout(); else G.Redraw();
            UpdateTitle();
            QueueSync();
        }

        // ===================================================================== keyboard
        void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var mods = Keyboard.Modifiers;
            bool ctrl = mods.HasFlag(ModifierKeys.Control), shift = mods.HasFlag(ModifierKeys.Shift), alt = mods.HasFlag(ModifierKeys.Alt);
            bool inGrid = G.IsKeyboardFocused;
            bool inText = Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is ComboBox || (Keyboard.FocusedElement is DependencyObject fe && UI.FindAncestor<ComboBox>(fe) != null);

            // shortcuts that work anywhere
            if (ctrl && !alt)
            {
                switch (key)
                {
                    case Key.N when !shift: Cmd(NewWorkbook); e.Handled = true; return;
                    case Key.O when !shift: Cmd(OpenFile); e.Handled = true; return;
                    case Key.S when !shift: Cmd(() => Save()); e.Handled = true; return;
                    case Key.P when !shift: Cmd(PrintDialogShow); e.Handled = true; return;
                    case Key.W when !shift: Cmd(Close); e.Handled = true; return;
                    case Key.F when !shift: Cmd(() => ShowFind(false)); e.Handled = true; return;
                    case Key.H when !shift: Cmd(() => ShowFind(true)); e.Handled = true; return;
                    case Key.G when !shift: Cmd(GoToDialogShow); e.Handled = true; return;
                    case Key.F1: Cmd(() => { Ribbon.ToggleMinimize(); Settings.Current.RibbonMinimized = Ribbon.Minimized; Settings.Save(); }); e.Handled = true; return;
                    case Key.U when shift: FxExpand_Click(null, null); e.Handled = true; return;
                    case Key.PageDown: Cmd(() => ShowSheet(NextVisibleSheet(SheetIdx, 1))); e.Handled = true; return;
                    case Key.PageUp: Cmd(() => ShowSheet(NextVisibleSheet(SheetIdx, -1))); e.Handled = true; return;
                }
            }
            if (key == Key.F12 && !ctrl) { Cmd(() => SaveAs()); e.Handled = true; return; }
            if (key == Key.F11 && shift) { Cmd(() => InsertSheet(SheetIdx)); e.Handled = true; return; }
            if (key == Key.F9) { Cmd(CalculateNow); e.Handled = true; return; }
            if (key == Key.F7 && !G.IsEditing) { Cmd(SpellCheckSheet); e.Handled = true; return; }
            if (key == Key.F1 && !ctrl) { ShowAbout(); e.Handled = true; return; }
            if (key == Key.F3 && shift && !G.IsEditing) { InsertFunction_Click(null, null); e.Handled = true; return; }

            if (!inGrid || G.IsEditing || inText) return;
            GridKey(e, key, ctrl, shift, alt);
        }

        void Cmd(Action a)
        {
            if (G.IsEditing) CommitEdit(0, 0);
            if (G.IsEditing) return;
            a();
        }

        int NextVisibleSheet(int from, int dir)
        {
            int n = _book.Wb.Worksheets.Count, i = from;
            for (int k = 0; k < n; k++)
            {
                i += dir;
                if (i < 1 || i > n) return from;
                if (_book.Wb.Worksheet(i).Visibility == XLWorksheetVisibility.Visible) return i;
            }
            return from;
        }

        void GridKey(KeyEventArgs e, Key key, bool ctrl, bool shift, bool alt)
        {
            if (G.Selected != null)
            {
                if (key == Key.Delete || key == Key.Back) { DeleteSelectedObject(); e.Handled = true; return; }
                if (key == Key.Escape) { G.ClearObjectSelection(); e.Handled = true; return; }
                G.ClearObjectSelection();
            }
            if (ctrl && alt && key == Key.V) { PasteSpecial(); e.Handled = true; return; }
            var v = G.View;
            int page = Math.Max(1, G.VisibleRowCount - 1);
            void Nav(int r, int c) { if (shift) G.Select(r, c, extend: true); else G.Select(r, c); G.EnsureVisible(r, c); }
            int curR = shift ? ExtentRow() : v.ActiveRow, curC = shift ? ExtentCol() : v.ActiveCol;
            switch (key)
            {
                case Key.Up: case Key.Down: case Key.Left: case Key.Right:
                    {
                        int dr = key == Key.Up ? -1 : key == Key.Down ? 1 : 0, dc = key == Key.Left ? -1 : key == Key.Right ? 1 : 0;
                        if (alt && key == Key.Down) { ShowPickList(); break; }
                        if (ctrl)
                        {
                            var (r, c) = Ops.Jump(WS, curR, curC, dr, dc, G.Layout.LastRow, G.Layout.LastCol);
                            Nav(r, c);
                        }
                        else if (shift)
                        {
                            int r = G.Layout.Rows.NextVisible(Math.Clamp(curR + dr, 1, SheetLimits.Rows), dr >= 0 ? 1 : -1, SheetLimits.Rows);
                            int c = G.Layout.Cols.NextVisible(Math.Clamp(curC + dc, 1, SheetLimits.Cols), dc >= 0 ? 1 : -1, SheetLimits.Cols);
                            Nav(r, c);
                        }
                        else G.MoveActive(dr, dc, false);
                        break;
                    }
                case Key.Enter:
                    if (Settings.Current.MoveAfterEnter) G.MoveActive(shift ? -DirRow() : DirRow(), shift ? -DirCol() : DirCol(), true);
                    break;
                case Key.Tab:
                    G.MoveActive(0, shift ? -1 : 1, true);
                    break;
                case Key.Home:
                    if (ctrl) Nav(G.FrozenRows > 0 ? G.FrozenRows + 1 : 1, G.FrozenCols > 0 ? G.FrozenCols + 1 : 1);
                    else Nav(curR, 1);
                    break;
                case Key.End:
                    if (ctrl) Nav(G.Layout.LastRow, G.Layout.LastCol);
                    break;
                case Key.PageDown:
                    if (alt) { G.ScrollTo(null, v.LeftCol + G.VisibleColCount); Nav(curR, Math.Min(SheetLimits.Cols, curC + G.VisibleColCount)); }
                    else { G.ScrollTo(v.TopRow + page, null); Nav(Math.Min(SheetLimits.Rows, curR + page), curC); }
                    break;
                case Key.PageUp:
                    if (alt) { G.ScrollTo(null, v.LeftCol - G.VisibleColCount); Nav(curR, Math.Max(1, curC - G.VisibleColCount)); }
                    else { G.ScrollTo(v.TopRow - page, null); Nav(Math.Max(1, curR - page), curC); }
                    break;
                case Key.Space:
                    if (ctrl && shift) G.SelectAll();
                    else if (ctrl) { var s = G.ActiveRect; G.SelectRect(new CellRect(1, s.C1, SheetLimits.Rows, s.C2), v.ActiveRow, v.ActiveCol); }
                    else if (shift) { var s = G.ActiveRect; G.SelectRect(new CellRect(s.R1, 1, s.R2, SheetLimits.Cols), v.ActiveRow, v.ActiveCol); }
                    else return;
                    break;
                case Key.F2:
                    G.BeginEdit(null, EditMode.Edit); _editSheet = SheetIdx;
                    break;
                case Key.Delete:
                    ClearSelection(XLClearOptions.Contents, "Clear");
                    break;
                case Key.Back:
                    G.BeginEdit("", EditMode.Enter); _editSheet = SheetIdx;
                    break;
                case Key.Escape:
                    if (_clip != null) { _clip = null; G.SetMarquee(null); }
                    break;
                case Key.Apps:
                    ShowCellMenu(new Point(G.CellRectPx(G.ActiveRect).Left + 10, G.CellRectPx(G.ActiveRect).Bottom));
                    break;
                case Key.F10 when shift:
                    ShowCellMenu(new Point(G.CellRectPx(G.ActiveRect).Left + 10, G.CellRectPx(G.ActiveRect).Bottom));
                    break;
                case Key.OemPlus when alt:
                    AutoSum("SUM");
                    break;
                default:
                    if (ctrl && !alt)
                    {
                        switch (key)
                        {
                            case Key.A: G.SelectRect(G.ActiveRect.Count == 1 && !WS.Cell(v.ActiveRow, v.ActiveCol).IsEmpty() ? Ops.CurrentRegion(WS, v.ActiveRow, v.ActiveCol) : new CellRect(1, 1, SheetLimits.Rows, SheetLimits.Cols), v.ActiveRow, v.ActiveCol); break;
                            case Key.C: CopySelection(false); break;
                            case Key.X: CopySelection(true); break;
                            case Key.V: if (shift) return; Paste(); break;
                            case Key.Z: UndoCmd(); break;
                            case Key.Y: RedoCmd(); break;
                            case Key.B: ToggleFont("Bold"); break;
                            case Key.I: ToggleFont("Italic"); break;
                            case Key.U: ToggleFont("Underline"); break;
                            case Key.D4 when shift: ApplyNumberFormat("\"$\"#,##0.00", "Currency"); break;
                            case Key.D5 when shift: ApplyNumberFormat("0%", "Percent"); break;
                            case Key.D1 when shift: ApplyNumberFormat("#,##0.00", "Number"); break;
                            case Key.D3 when shift: ApplyNumberFormat("d-mmm-yy", "Date"); break;
                            case Key.D5: ToggleFont("Strike"); break;
                            case Key.D1: FormatCells(null); break;
                            case Key.D: FillDirection("Down"); break;
                            case Key.R: FillDirection("Right"); break;
                            case Key.K: InsertHyperlink(); break;
                            case Key.T: case Key.L: FormatAsTable(null); break;
                            case Key.OemSemicolon: InsertNow(shift); break;
                            case Key.OemPlus: InsertCellsDialog(); break;
                            case Key.Add: InsertCellsDialog(); break;
                            case Key.OemMinus: case Key.Subtract: DeleteCellsDialog(); break;
                            case Key.D9: if (shift) UnhideRows(); else HideRows(); break;
                            case Key.D0: if (shift) UnhideCols(); else HideCols(); break;
                            case Key.OemTilde: G.ShowFormulas = !G.ShowFormulas; G.Redraw(); break;
                            default: return;
                        }
                    }
                    else return;
                    break;
            }
            e.Handled = true;
        }

        int ExtentRow() { var s = G.ActiveRect; return s.R1 == G.View.AnchorRow ? s.R2 : s.R1; }
        int ExtentCol() { var s = G.ActiveRect; return s.C1 == G.View.AnchorCol ? s.C2 : s.C1; }

        void InsertNow(bool time)
        {
            var t = time ? DateTime.Now.ToString("t", CultureInfo.CurrentCulture) : DateTime.Today.ToString("d", CultureInfo.CurrentCulture);
            G.BeginEdit(t, EditMode.Enter);
            _editSheet = SheetIdx;
        }

        // ===================================================================== undo
        void UndoCmd()
        {
            if (G.IsEditing) { CancelEdit(); return; }
            var e = _book.Undo.UndoOne();
            if (e == null) return;
            AfterUndo(e);
        }

        void RedoCmd()
        {
            if (G.IsEditing) return;
            var e = _book.Undo.RedoOne();
            if (e == null) return;
            AfterUndo(e);
        }

        void AfterUndo(UndoEntry e)
        {
            RefreshAll(e.SheetIndex);
            if (e.Selection != null && e.Selection.Count > 0 && e.Selection[0].Count < 50_000_000)
            {
                G.View.Selection = e.Selection.ToList();
                G.View.ActiveRow = G.View.AnchorRow = e.Selection[0].R1;
                G.View.ActiveCol = G.View.AnchorCol = e.Selection[0].C1;
                G.EnsureVisible(G.View.ActiveRow, G.View.ActiveCol);
            }
            QueueSync();
        }

        /// <summary>Runs a change on the selected cells with undo.</summary>
        void Change(string name, Action act, List<CellRect> region = null, bool structure = false)
        {
            if (G.IsEditing) CommitEdit(0, 0);
            if (WS.IsProtected && !structure && !SelectionUnlocked()) { G_ProtectedMessage(); return; }
            try
            {
                _book.Undo.Do(name, SheetIdx, structure ? null : (region ?? G.View.Selection.ToList()), act, G.View.Selection.ToList());
            }
            catch (Exception ex)
            {
                VistaDialog.Show(this, ex.Message, "Valco Spreadsheets", MessageBoxButton.OK, DlgIcon.Warning);
            }
            if (structure) RefreshAll(); else AfterChange(rowsChanged: true);
        }

        bool SelectionUnlocked()
        {
            try { return G.View.Selection.All(r => r.Count < 10000 && WS.Range(r.R1, r.C1, r.R2, r.C2).Cells().All(c => !c.Style.Protection.Locked)); }
            catch { return false; }
        }

        void G_ProtectedMessage() =>
            VistaDialog.Show(this, "The cell or chart that you are trying to change is protected and therefore read-only.\n\nTo modify a protected cell or chart, first remove protection using the Unprotect Sheet command (Review tab, Changes group).", "Valco Spreadsheets", MessageBoxButton.OK, DlgIcon.Warning);

        void UpdateUndoButtons()
        {
            if (BtnUndoQat != null) BtnUndoQat.IsEnabled = _book.Undo.CanUndo;
            if (BtnRedoQat != null) BtnRedoQat.IsEnabled = _book.Undo.CanRedo;
            UpdateTitle();
        }

        // ===================================================================== fill / move / sizes
        void DoFill(CellRect src, CellRect dst, bool copy)
        {
            if (dst.R1 > src.R1 || dst.C1 > src.C1 || dst.R2 < src.R2 || dst.C2 < src.C2)
            {
                // dragged back inside the selection: clear the cells left out (Excel erases them)
                var keep = dst; var region = new List<CellRect> { src };
                Change("Clear", () =>
                {
                    for (int r = src.R1; r <= src.R2; r++) for (int c = src.C1; c <= src.C2; c++)
                            if (!keep.Contains(r, c)) WS.Cell(r, c).Clear(XLClearOptions.Contents);
                }, region);
                G.SelectRect(keep);
                return;
            }
            Change("Fill", () => Ops.Fill(WS, src, dst, copy), new List<CellRect> { dst });
            G.SelectRect(dst, src.R1, src.C1);
        }

        void MoveCells(CellRect src, int row, int col, bool copy)
        {
            var dst = new CellRect(row, col, row + src.Rows - 1, col + src.Cols - 1);
            if (!copy && !WS.Range(dst.R1, dst.C1, dst.R2, dst.C2).IsEmpty() && !dst.Intersects(src))
                if (VistaDialog.Show(this, "Do you want to replace the contents of the destination cells?", "Valco Spreadsheets", MessageBoxButton.OKCancel, DlgIcon.Warning) != MessageBoxResult.OK) return;
            Change(copy ? "Drag and Drop" : "Drag and Drop", () =>
            {
                var tmp = new XLWorkbook().AddWorksheet("t");
                WS.Range(src.R1, src.C1, src.R2, src.C2).CopyTo(tmp.Cell(src.R1, src.C1));
                if (!copy) WS.Range(src.R1, src.C1, src.R2, src.C2).Clear(XLClearOptions.Contents | XLClearOptions.NormalFormats);
                tmp.Range(src.R1, src.C1, src.R2, src.C2).CopyTo(WS.Cell(row, col));
            }, new List<CellRect> { src, dst });
            G.SelectRect(dst);
        }

        void ResizeLanes(bool cols, List<int> idx, double px)
        {
            _book.Undo.DoSizes(cols ? "Column Width" : "Row Height", SheetIdx, cols, idx, () =>
            {
                foreach (var i in idx)
                {
                    if (cols) { if (px <= 0) WS.Column(i).Hide(); else { WS.Column(i).Unhide(); WS.Column(i).Width = Units.PxToColWidth(px); } }
                    else { if (px <= 0) WS.Row(i).Hide(); else { WS.Row(i).Unhide(); WS.Row(i).Height = Units.PxToPt(px); _autoRows.Remove((WS.Name, i)); } }
                }
            });
            AfterChange(structure: true);
        }

        List<int> SelectedWholeColumns(int i)
        {
            var s = G.View.Selection.FirstOrDefault(r => r.IsWholeColumns && i >= r.C1 && i <= r.C2);
            return s.Count > 0 && s.IsWholeColumns ? Enumerable.Range(s.C1, s.Cols).ToList() : new List<int> { i };
        }
        List<int> SelectedWholeRows(int i)
        {
            var s = G.View.Selection.FirstOrDefault(r => r.IsWholeRows && i >= r.R1 && i <= r.R2);
            return s.Count > 0 && s.IsWholeRows ? Enumerable.Range(s.R1, s.Rows).ToList() : new List<int> { i };
        }

        /// <summary>Column AutoFit: as wide as the widest value (measured the way the grid draws it).</summary>
        void AutoFitColumns(List<int> cols, CellRect? within = null)
        {
            var dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            _book.Undo.DoSizes("AutoFit Column Width", SheetIdx, true, cols, () =>
            {
                foreach (var c in cols)
                {
                    double max = 0;
                    var range = within.HasValue ? WS.Range(within.Value.R1, c, within.Value.R2, c) : WS.Column(c).AsRange();
                    foreach (var cell in range.CellsUsed(XLCellsUsedOptions.Contents))
                    {
                        if (G.Layout.MergeAt(cell.Address.RowNumber, c).HasValue) continue;
                        var text = G.DisplayText(cell, out _, out _, out _);
                        if (string.IsNullOrEmpty(text)) continue;
                        var f = cell.Style.Font;
                        var face = new Typeface(new FontFamily(f.FontName ?? "Calibri"), f.Italic ? FontStyles.Italic : FontStyles.Normal, f.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
                        foreach (var line in text.Split('\n'))
                        {
                            var ft = new FormattedText(line, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, Units.PtToPx(f.FontSize > 0 ? f.FontSize : 11), Brushes.Black, dip);
                            max = Math.Max(max, ft.WidthIncludingTrailingWhitespace + 7 + cell.Style.Alignment.Indent * 9);
                        }
                    }
                    if (max > 0) { WS.Column(c).Unhide(); WS.Column(c).Width = Math.Min(255, Units.PxToColWidth(Math.Ceiling(max))); }
                }
            });
            AfterChange(structure: true);
        }

        readonly HashSet<(string, int)> _autoRows = new();

        void AutoFitRows(IEnumerable<int> rows, bool force)
        {
            var list = rows.ToList();
            _book.Undo.DoSizes("AutoFit Row Height", SheetIdx, false, list, () =>
            {
                foreach (var r in list) _autoRows.Add((WS.Name, r));
                AutoFitRowsCore(list, force: true);
            });
            AfterChange(structure: true);
        }

        /// <summary>Grows rows for bigger fonts and wrapped text (rows the user sized by hand are left alone unless forced).</summary>
        bool AutoFitRowsCore(IEnumerable<int> rows, bool force = false)
        {
            bool changed = false;
            var dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            double def = WS.RowHeight;
            foreach (var r in rows)
            {
                var row = WS.Row(r);
                if (row.IsHidden) continue;
                double need = 0;
                foreach (var cell in row.CellsUsed(XLCellsUsedOptions.Contents | XLCellsUsedOptions.NormalFormats))
                {
                    var f = cell.Style.Font;
                    double size = f.FontSize > 0 ? f.FontSize : 11;
                    double h = size * 1.36;
                    if ((cell.Style.Alignment.WrapText || CellInput.EditText(cell).Contains('\n')) && !cell.IsEmpty() && !G.Layout.MergeAt(r, cell.Address.ColumnNumber).HasValue)
                    {
                        var text = G.DisplayText(cell, out _, out _, out _);
                        var face = new Typeface(new FontFamily(f.FontName ?? "Calibri"), f.Italic ? FontStyles.Italic : FontStyles.Normal, f.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
                        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, Units.PtToPx(size), Brushes.Black, dip) { MaxTextWidth = Math.Max(10, G.Layout.Cols.Size(cell.Address.ColumnNumber) - 6) };
                        h = Units.PxToPt(ft.Height) + 3;
                    }
                    need = Math.Max(need, h);
                }
                need = Math.Max(def, Math.Round(need * 4) / 4);
                bool auto = force || _autoRows.Contains((WS.Name, r)) || Math.Abs(row.Height - def) < 0.01;
                if (!auto) continue;
                if (Math.Abs(row.Height - need) > 0.2)
                {
                    row.Height = need;
                    changed = true;
                    if (Math.Abs(need - def) > 0.01) _autoRows.Add((WS.Name, r));
                }
            }
            return changed;
        }

        // ===================================================================== title, messages
        void UpdateTitle()
        {
            var name = _book.DisplayName;
            var mode = _book.Format == BookFormat.Xls ? "  [Compatibility Mode]" : _book.FilePath != null && IsReadOnlyFile(_book.FilePath) ? "  [Read-Only]" : "";
            Title = name + mode + " - Valco Spreadsheets";
        }

        static bool IsReadOnlyFile(string p) { try { return new FileInfo(p).IsReadOnly; } catch { return false; } }

        void ShowBookMessages()
        {
            if (_book.HasMacros)
                ShowMessageBar("Security Warning", "Macros have been disabled. They are kept when you save as a Macro-Enabled Workbook (.xlsm).", "IconWarning");
            else if (_book.UnkeptParts.Count > 0)
                ShowMessageBar("Compatibility", "This workbook has " + string.Join(", ", _book.UnkeptParts) + " that Valco Spreadsheets can't keep. Saving over this file would remove them, so Save asks for a new name.", "IconInfo");
            else if (_book.Format == BookFormat.Xls)
                ShowMessageBar("Compatibility Mode", "This workbook is in the Excel 97-2003 format and saves as an .xls file. Use Convert on the Valco menu to switch to the Excel Workbook format for charts and 1,048,576 rows.", "IconInfo");
            else MessageBar.Visibility = Visibility.Collapsed;
        }

        void ShowMessageBar(string title, string text, string icon)
        {
            MessageBarTitle.Text = title;
            MessageBarText.Text = text;
            MessageBarIcon.Source = UI.Img(icon);
            MessageBar.Visibility = Visibility.Visible;
        }

        void MessageBarClose_Click(object sender, RoutedEventArgs e) => MessageBar.Visibility = Visibility.Collapsed;

        // ===================================================================== formula bar buttons
        void FxCancel_Click(object sender, RoutedEventArgs e) => CancelEdit();
        void FxEnter_Click(object sender, RoutedEventArgs e) => CommitEdit(0, 0);

        bool _fxExpanded;
        void FxExpand_Click(object sender, RoutedEventArgs e)
        {
            _fxExpanded = !_fxExpanded;
            FormulaBox.Height = _fxExpanded ? 58 : 19;
            FormulaBox.AcceptsReturn = false;
            FormulaBox.TextWrapping = _fxExpanded ? TextWrapping.Wrap : TextWrapping.NoWrap;
            FxExpandGlyph.Data = Geometry.Parse(_fxExpanded ? "M0,3.5 L6,3.5 L3,0 Z" : "M0,0 L6,0 L3,3.5 Z");
        }

        // ===================================================================== Name Box
        void NameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { QueueSync(); G.Focus(); e.Handled = true; return; }
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            var t = (NameBox.Text ?? "").Trim();
            if (t.Length == 0) { G.Focus(); return; }
            if (GoToName(t)) return;
            if (A1.TryParse(t, out var rect)) { GoTo(SheetIdx, rect); return; }
            if (t.Contains("!"))
            {
                var parts = t.Split('!');
                var ws = _book.Wb.Worksheets.FirstOrDefault(w => w.Name.Equals(parts[0].Trim('\''), StringComparison.OrdinalIgnoreCase));
                if (ws != null && A1.TryParse(parts[1], out var r2)) { GoTo(ws.Position, r2); return; }
            }
            // a new name for the selection
            if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^[A-Za-z_\\][A-Za-z0-9_.]*$") && !A1.TryParse(t, out _))
            {
                var sel = G.ActiveRect;
                Change("Define Name", () => _book.Wb.DefinedNames.Add(t, WS.Range(sel.R1, sel.C1, sel.R2, sel.C2)), structure: true);
                G.Focus();
                return;
            }
            VistaDialog.Show(this, "Reference is not valid.", "Valco Spreadsheets", MessageBoxButton.OK, DlgIcon.Warning);
        }

        bool GoToName(string name)
        {
            try
            {
                if (!_book.Wb.DefinedNames.TryGetValue(name, out var dn) && !WS.DefinedNames.TryGetValue(name, out dn)) return false;
                var r = dn.Ranges.FirstOrDefault();
                if (r == null) return false;
                var a = r.RangeAddress;
                GoTo(r.Worksheet.Position, new CellRect(a.FirstAddress.RowNumber, a.FirstAddress.ColumnNumber, a.LastAddress.RowNumber, a.LastAddress.ColumnNumber));
                return true;
            }
            catch { return false; }
        }

        public void GoTo(int sheet, CellRect r)
        {
            if (sheet != SheetIdx) ShowSheet(sheet);
            G.SelectRect(r);
            G.EnsureVisible(r.R1, r.C1);
            G.Focus();
        }

        // ===================================================================== zoom
        bool _zoomSync;
        void SetZoom(double z)
        {
            z = Math.Clamp(Math.Round(z, 2), 0.1, 4);
            G.Zoom = z;
            _zoomSync = true;
            ZoomSlider.Value = Math.Round(z * 100);
            _zoomSync = false;
            ZoomText.Text = Math.Round(z * 100) + "%";
            G.Relayout();
            if (G.IsEditing) G.PositionEditor();
        }

        void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_zoomSync || Sheet == null || G.Book == null) return;
            // snap to 100% near the middle like Office
            var v = Math.Abs(e.NewValue - 100) < 4 ? 100 : e.NewValue;
            SetZoom(v / 100);
        }
        void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(Math.Round((G.Zoom + 0.1) * 10) / 10);
        void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(Math.Round((G.Zoom - 0.1) * 10) / 10);
        void ZoomDialog_Click(object sender, RoutedEventArgs e) => ZoomDialog();

        // ===================================================================== drag & drop / closing / placement
        void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
                foreach (var f in files) OpenPath(f);
        }

        void Window_Closing(object sender, CancelEventArgs e)
        {
            if (G.IsEditing) CommitEdit(0, 0);
            if (_book.Dirty && !ConfirmDiscard()) { e.Cancel = true; return; }
            SavePlacement();
            _autoRecover.Stop();
            DeleteRecovery();
        }

        /// <summary>Asks to save changes. False = cancel.</summary>
        bool ConfirmDiscard()
        {
            var r = VistaDialog.Show(this, $"Do you want to save the changes you made to '{_book.DisplayName}'?", "Valco Spreadsheets", MessageBoxButton.YesNoCancel, DlgIcon.Warning);
            if (r == MessageBoxResult.Cancel) return false;
            if (r == MessageBoxResult.Yes) return Save();
            return true;
        }

        void RestorePlacement()
        {
            var s = Settings.Current;
            Width = s.WinWidth; Height = s.WinHeight;
            if (!double.IsNaN(s.WinLeft) && Application.Current.Windows.Count <= 1)
            {
                Left = Math.Max(SystemParameters.VirtualScreenLeft, s.WinLeft);
                Top = Math.Max(SystemParameters.VirtualScreenTop, s.WinTop);
                WindowStartupLocation = WindowStartupLocation.Manual;
            }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (s.WinMax) WindowState = WindowState.Maximized;
        }

        void SavePlacement()
        {
            var s = Settings.Current;
            s.WinMax = WindowState == WindowState.Maximized;
            var r = RestoreBounds;
            if (!r.IsEmpty) { s.WinLeft = r.Left; s.WinTop = r.Top; s.WinWidth = r.Width; s.WinHeight = r.Height; }
            Settings.Save();
        }

        // ===================================================================== AutoRecover
        void SaveRecovery()
        {
            if (!_book.Dirty || G.IsEditing) return;
            try
            {
                Directory.CreateDirectory(Settings.AutoRecoverDir);
                _recoverPath ??= Path.Combine(Settings.AutoRecoverDir, Guid.NewGuid().ToString("N").Substring(0, 8) + " - " + Delimited.SafeSheetName(Path.GetFileNameWithoutExtension(_book.DisplayName)) + ".xlsx");
                using var ms = new MemoryStream();
                _book.Wb.SaveAs(ms, new SaveOptions { EvaluateFormulasBeforeSaving = false, ValidatePackage = false });
                File.WriteAllBytes(_recoverPath, ms.ToArray());
            }
            catch { }
        }

        void DeleteRecovery()
        {
            try { if (_recoverPath != null && File.Exists(_recoverPath)) File.Delete(_recoverPath); } catch { }
        }
    }
}
