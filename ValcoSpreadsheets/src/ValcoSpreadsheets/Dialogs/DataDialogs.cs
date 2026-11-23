using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ClosedXML.Excel;
using Valco.UI;
using ValcoSpreadsheets.Grid;
using ValcoSpreadsheets.Model;

namespace ValcoSpreadsheets
{
    /// <summary>Find and Replace (modeless, like Excel's).</summary>
    public class FindDialog : VistaWindow
    {
        public event Func<Ops.FindOptions, bool> FindNext;
        public event Action<Ops.FindOptions> FindAllRequested;
        public event Action<Ops.FindOptions, string> ReplaceOne, ReplaceAll;
        public event Action<int, int, int> ResultPicked;
        readonly TabControl _tabs;
        readonly ComboBox _what, _with, _within, _search, _lookIn;
        readonly CheckBox _case, _whole;
        readonly StackPanel _withRow;
        readonly Button _replaceBtn, _replaceAllBtn;
        readonly ListView _results;

        public FindDialog(Window owner)
        {
            Title = "Find and Replace";
            Owner = owner; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize;
            Width = 460; SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var sp = new StackPanel();
            _tabs = new TabControl { Margin = new Thickness(0, 0, 0, 6) };
            _tabs.Items.Add(new TabItem { Header = "Find" });
            _tabs.Items.Add(new TabItem { Header = "Replace" });
            _tabs.SelectionChanged += (s, e) => ShowTab(_tabs.SelectedIndex == 1);
            sp.Children.Add(_tabs);
            _what = new ComboBox { IsEditable = true, Width = 300, Height = 22 };
            _with = new ComboBox { IsEditable = true, Width = 300, Height = 22 };
            sp.Children.Add(VDialog.Row(new TextBlock { Text = "Find what:", Width = 80, VerticalAlignment = VerticalAlignment.Center }, _what));
            _withRow = VDialog.Row(new TextBlock { Text = "Replace with:", Width = 80, VerticalAlignment = VerticalAlignment.Center }, _with);
            sp.Children.Add(_withRow);
            _within = VDialog.Combo(new[] { "Sheet", "Workbook" }, 0, 100);
            _search = VDialog.Combo(new[] { "By Rows", "By Columns" }, 0, 100);
            _lookIn = VDialog.Combo(new[] { "Formulas", "Values" }, 0, 100);
            _case = VDialog.Chk("Match case"); _whole = VDialog.Chk("Match entire cell contents");
            var opts = new System.Windows.Controls.Grid();
            opts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) }); opts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) }); opts.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 3; i++) opts.RowDefinitions.Add(new RowDefinition());
            void Put(UIElement el, int r, int c) { System.Windows.Controls.Grid.SetRow(el, r); System.Windows.Controls.Grid.SetColumn(el, c); opts.Children.Add(el); }
            Put(new TextBlock { Text = "Within:", VerticalAlignment = VerticalAlignment.Center }, 0, 0); Put(_within, 0, 1); Put(_case, 0, 2);
            Put(new TextBlock { Text = "Search:", VerticalAlignment = VerticalAlignment.Center }, 1, 0); Put(_search, 1, 1); Put(_whole, 1, 2);
            Put(new TextBlock { Text = "Look in:", VerticalAlignment = VerticalAlignment.Center }, 2, 0); Put(_lookIn, 2, 1);
            sp.Children.Add(opts);
            _results = new ListView { Height = 120, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
            var gv = new GridView();
            gv.Columns.Add(new GridViewColumn { Header = "Sheet", Width = 90, DisplayMemberBinding = new System.Windows.Data.Binding("SheetName") });
            gv.Columns.Add(new GridViewColumn { Header = "Cell", Width = 60, DisplayMemberBinding = new System.Windows.Data.Binding("Cell") });
            gv.Columns.Add(new GridViewColumn { Header = "Value", Width = 240, DisplayMemberBinding = new System.Windows.Data.Binding("Value") });
            _results.View = gv;
            _results.SelectionChanged += (s, e) => { if (_results.SelectedItem is Hit h) ResultPicked?.Invoke(h.Sheet, h.Row, h.Col); };
            sp.Children.Add(_results);
            _replaceAllBtn = UI.Btn("Replace All", (s, e) => { Remember(); ReplaceAll?.Invoke(Options(), _with.Text); });
            _replaceBtn = UI.Btn("Replace", (s, e) => { Remember(); ReplaceOne?.Invoke(Options(), _with.Text); });
            var findAll = UI.Btn("Find All", (s, e) => { Remember(); FindAllRequested?.Invoke(Options()); });
            var next = UI.Btn("Find Next", (s, e) => { Remember(); FindNext?.Invoke(Options()); }, true);
            Content = UI.DialogLayout(sp, UI.Strip(_replaceAllBtn, _replaceBtn, findAll, next, UI.Btn("Close", (s, e) => Close(), false, true)));
            Loaded += (s, e) => _what.Focus();
        }

        void Remember()
        {
            if (!string.IsNullOrEmpty(_what.Text) && !_what.Items.Contains(_what.Text)) _what.Items.Insert(0, _what.Text);
        }

        public void ShowTab(bool replace)
        {
            _tabs.SelectedIndex = replace ? 1 : 0;
            _withRow.Visibility = _replaceBtn.Visibility = _replaceAllBtn.Visibility = replace ? Visibility.Visible : Visibility.Collapsed;
            if (replace) _lookIn.SelectedIndex = 0;
        }

        Ops.FindOptions Options() => new Ops.FindOptions
        {
            What = _what.Text ?? "", MatchCase = _case.IsChecked == true, WholeCell = _whole.IsChecked == true,
            InFormulas = _lookIn.SelectedIndex == 0, ByColumns = _search.SelectedIndex == 1, WholeBook = _within.SelectedIndex == 1
        };

        public class Hit { public int Sheet { get; set; } public string SheetName { get; set; } public int Row { get; set; } public int Col { get; set; } public string Cell { get; set; } public string Value { get; set; } }

        public void ShowResults(List<(int Sheet, string SheetName, int Row, int Col, string Value)> list)
        {
            _results.ItemsSource = list.Select(h => new Hit { Sheet = h.Sheet, SheetName = h.SheetName, Row = h.Row, Col = h.Col, Cell = "$" + A1.Col(h.Col) + "$" + h.Row, Value = h.Value }).ToList();
            _results.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>Insert Function: search, category, list, description.</summary>
    public class InsertFunctionDialog : VDialog
    {
        public Functions.Fn Picked;
        public InsertFunctionDialog(Window owner) : base(owner, "Insert Function", 470)
        {
            var sp = new StackPanel();
            sp.Children.Add(Label("Search for a function:"));
            var search = Box("Type a brief description of what you want to do and then click Go");
            search.GotFocus += (s, e) => { if (search.Text.StartsWith("Type a brief")) search.Text = ""; };
            var go = UI.Btn("Go", null); go.Margin = new Thickness(6, 0, 0, 6);
            var srow = new DockPanel(); DockPanel.SetDock(go, Dock.Right); srow.Children.Add(go); srow.Children.Add(search);
            sp.Children.Add(srow);
            var cat = Combo(Functions.Categories, 0, 200);
            sp.Children.Add(Row(new TextBlock { Text = "Or select a category:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 6) }, cat));
            sp.Children.Add(Label("Select a function:"));
            var list = new ListBox { Height = 160 };
            sp.Children.Add(list);
            var syntax = new TextBlock { FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 2), TextWrapping = TextWrapping.Wrap };
            var desc = new TextBlock { TextWrapping = TextWrapping.Wrap, MinHeight = 34 };
            sp.Children.Add(syntax); sp.Children.Add(desc);
            List<Functions.Fn> current = new();
            void Show(IEnumerable<Functions.Fn> fns) { current = fns.ToList(); list.ItemsSource = current.Select(f => f.Name).ToList(); list.SelectedIndex = current.Count > 0 ? 0 : -1; }
            cat.SelectionChanged += (s, e) =>
            {
                var c = Functions.Categories[cat.SelectedIndex];
                Show(c == "Most Recently Used" ? Functions.Recent.Where(Functions.ByName.ContainsKey).Select(n => Functions.ByName[n]) : c == "All" ? Functions.All.OrderBy(f => f.Name) : Functions.All.Where(f => f.Category == c).OrderBy(f => f.Name));
            };
            go.Click += (s, e) =>
            {
                var words = search.Text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                Show(Functions.All.Select(f => (f, score: words.Count(w => f.Name.ToLowerInvariant().Contains(w)) * 3 + words.Count(w => f.Description.ToLowerInvariant().Contains(w)))).Where(x => x.score > 0).OrderByDescending(x => x.score).Select(x => x.f));
            };
            search.KeyDown += (s, e) => { if (e.Key == Key.Enter) { go.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); e.Handled = true; } };
            list.SelectionChanged += (s, e) =>
            {
                Picked = list.SelectedIndex >= 0 ? current[list.SelectedIndex] : null;
                syntax.Text = Picked == null ? "" : $"{Picked.Name}({Picked.Args})";
                desc.Text = Picked?.Description ?? "";
            };
            list.MouseDoubleClick += (s, e) => { if (Picked != null) DialogResult = true; };
            cat.SelectedIndex = 0;
            Build(sp, () => Picked != null);
        }
    }

    /// <summary>Function Arguments: one box per argument with the live value, the description and the result.</summary>
    public class FunctionArgumentsDialog : VDialog
    {
        readonly List<TextBox> _boxes = new();
        public string ArgumentText => string.Join(",", TrimTrailing(_boxes.Select(b => b.Text.Trim()).ToList()));

        static List<string> TrimTrailing(List<string> l) { while (l.Count > 0 && l[^1].Length == 0) l.RemoveAt(l.Count - 1); return l; }

        public FunctionArgumentsDialog(Window owner, Functions.Fn fn, Func<string, string> evaluate) : base(owner, "Function Arguments", 470)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = fn.Name, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
            var args = fn.Args.Split(',').Select(a => a.Trim()).Where(a => a.Length > 0 && a != "...").ToList();
            if (args.Count == 0) sp.Children.Add(Label("This function takes no arguments."));
            var result = new TextBlock { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var a in args.Take(6))
            {
                var name = a.Trim('[', ']');
                var box = Box("", 220);
                var val = new TextBlock { Width = 140, Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 6), TextTrimming = TextTrimming.CharacterEllipsis };
                box.TextChanged += (s, e) =>
                {
                    val.Text = box.Text.Length > 0 ? "= " + evaluate(box.Text) : "";
                    var full = fn.Name + "(" + ArgumentText + ")";
                    result.Text = "Formula result = " + evaluate(full);
                };
                _boxes.Add(box);
                sp.Children.Add(Row(new TextBlock { Text = name, Width = 90, VerticalAlignment = VerticalAlignment.Center, FontWeight = a.StartsWith("[") ? FontWeights.Normal : FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis }, box, val));
            }
            sp.Children.Add(new TextBlock { Text = fn.Description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
            sp.Children.Add(result);
            Build(sp, null);
            Loaded += (s, e) => _boxes.FirstOrDefault()?.Focus();
        }
    }

    /// <summary>Sort: several levels, header option, case sensitivity.</summary>
    public class SortDialog : VDialog
    {
        public List<Ops.SortKey> Keys = new();
        public bool HasHeader, MatchCase;

        public SortDialog(Window owner, IXLWorksheet ws, CellRect r, bool header, int activeCol) : base(owner, "Sort", 520)
        {
            var sp = new StackPanel();
            var hdr = Chk("My data has headers", header);
            var caseChk = Chk("Case sensitive");
            var levels = new StackPanel();
            List<string> Names() => Enumerable.Range(r.C1, r.Cols).Select(c => hdr.IsChecked == true ? (ws.Cell(r.R1, c).GetFormattedString() is var s && s.Length > 0 ? s : "Column " + A1.Col(c)) : "Column " + A1.Col(c)).ToList();
            var rows = new List<(ComboBox Col, ComboBox Order)>();
            void AddLevel(int col)
            {
                var cb = Combo(Names(), Math.Clamp(col - r.C1, 0, r.Cols - 1), 200);
                var ob = Combo(new[] { "A to Z", "Z to A" }, 0, 100);
                rows.Add((cb, ob));
                levels.Children.Add(Row(new TextBlock { Text = rows.Count == 1 ? "Sort by" : "Then by", Width = 60, VerticalAlignment = VerticalAlignment.Center }, cb, new TextBlock { Text = "  Order", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 6) }, ob));
            }
            var add = UI.Btn("Add Level", (s, e) => AddLevel(r.C1)); add.Margin = new Thickness(0, 0, 6, 6);
            var del = UI.Btn("Delete Level", (s, e) => { if (rows.Count > 1) { rows.RemoveAt(rows.Count - 1); levels.Children.RemoveAt(levels.Children.Count - 1); } }); del.Margin = new Thickness(0, 0, 6, 6);
            sp.Children.Add(Row(add, del, new TextBlock { Width = 60 }, hdr));
            sp.Children.Add(levels);
            sp.Children.Add(caseChk);
            hdr.Click += (s, e) => { var n = Names(); foreach (var (c, _) in rows) { int i = c.SelectedIndex; c.ItemsSource = n; c.SelectedIndex = i; } };
            AddLevel(activeCol);
            Build(sp, () =>
            {
                HasHeader = hdr.IsChecked == true; MatchCase = caseChk.IsChecked == true;
                Keys = rows.Select(x => new Ops.SortKey { Column = r.C1 + Math.Max(0, x.Col.SelectedIndex), Descending = x.Order.SelectedIndex == 1 }).ToList();
                return true;
            });
        }
    }

    public class PasteSpecialDialog : VDialog
    {
        public Ops.PasteWhat What = Ops.PasteWhat.All;
        public bool Transpose, SkipBlanks;
        public PasteSpecialDialog(Window owner, bool cells) : base(owner, "Paste Special", 360)
        {
            var sp = new StackPanel();
            var opts = new[] { ("All", Ops.PasteWhat.All), ("Formulas", Ops.PasteWhat.Formulas), ("Values", Ops.PasteWhat.Values), ("Formats", Ops.PasteWhat.Formats), ("All except borders", Ops.PasteWhat.AllExceptBorders), ("Column widths", Ops.PasteWhat.ColumnWidths) };
            var radios = new List<RadioButton>();
            var g = new StackPanel();
            foreach (var (t, v) in opts) { var rb = Radio(t, "p", v == Ops.PasteWhat.All); rb.IsEnabled = cells || v == Ops.PasteWhat.All; radios.Add(rb); g.Children.Add(rb); }
            sp.Children.Add(Group("Paste", g));
            var skip = Chk("Skip blanks"); var tr = Chk("Transpose");
            skip.IsEnabled = tr.IsEnabled = cells;
            sp.Children.Add(Row(skip, new TextBlock { Width = 40 }, tr));
            if (!cells) sp.Children.Add(Label("The Clipboard has text from another program, so it's pasted as text.", 4));
            Build(sp, () => { What = opts[radios.FindIndex(r => r.IsChecked == true)].Item2; Transpose = tr.IsChecked == true; SkipBlanks = skip.IsChecked == true; return true; });
        }
    }

    public class ProtectSheetDialog : VDialog
    {
        public string Password;
        public bool SelectLocked = true, SelectUnlocked = true, FormatCells, InsertRows, DeleteRows, Sort, AutoFilter;
        public ProtectSheetDialog(Window owner) : base(owner, "Protect Sheet", 340)
        {
            var sp = new StackPanel();
            sp.Children.Add(Chk("Protect worksheet and contents of locked cells", true));
            sp.Children.Add(Label("Password to unprotect sheet:", 6));
            var pw = new PasswordBox { Height = 22, Margin = new Thickness(0, 0, 0, 8) };
            sp.Children.Add(pw);
            sp.Children.Add(Label("Allow all users of this worksheet to:"));
            var a = Chk("Select locked cells", true); var b = Chk("Select unlocked cells", true); var c = Chk("Format cells"); var d = Chk("Insert rows"); var e2 = Chk("Delete rows"); var f = Chk("Sort"); var g = Chk("Use AutoFilter");
            var box = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
            foreach (var x in new[] { a, b, c, d, e2, f, g }) box.Children.Add(x);
            sp.Children.Add(new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Padding = new Thickness(4), Child = box });
            Build(sp, () =>
            {
                if (pw.Password.Length > 0)
                {
                    var again = PasswordDialog.Ask(this, "Confirm Password", "Reenter password to proceed.");
                    if (again != pw.Password) { VistaDialog.Show(this, "The confirmation password is not identical.", "Valco Spreadsheets", MessageBoxButton.OK, DlgIcon.Warning); return false; }
                }
                Password = pw.Password; SelectLocked = a.IsChecked == true; SelectUnlocked = b.IsChecked == true; FormatCells = c.IsChecked == true; InsertRows = d.IsChecked == true; DeleteRows = e2.IsChecked == true; Sort = f.IsChecked == true; AutoFilter = g.IsChecked == true;
                return true;
            });
        }
    }

    public class MoveCopyDialog : VDialog
    {
        public Book TargetBook;
        public int BeforePosition;
        public bool Copy;
        public MoveCopyDialog(Window owner, List<Book> books, Book current, string sheet) : base(owner, "Move or Copy", 320)
        {
            var sp = new StackPanel();
            sp.Children.Add(Label("Move selected sheets"));
            sp.Children.Add(Label("To book:"));
            var names = books.Select(b => b.DisplayName).ToList(); names.Add("(new book)");
            var bookBox = Combo(names, books.IndexOf(current));
            sp.Children.Add(bookBox);
            sp.Children.Add(Label("Before sheet:"));
            var list = new ListBox { Height = 140 };
            sp.Children.Add(list);
            var copy = Chk("Create a copy");
            sp.Children.Add(copy);
            void Fill()
            {
                var b = bookBox.SelectedIndex < books.Count ? books[bookBox.SelectedIndex] : null;
                var items = b == null ? new List<string>() : b.Wb.Worksheets.Select(w => w.Name).ToList();
                items.Add("(move to end)");
                list.ItemsSource = items; list.SelectedIndex = 0;
            }
            bookBox.SelectionChanged += (s, e) => Fill();
            Fill();
            Build(sp, () =>
            {
                TargetBook = bookBox.SelectedIndex < books.Count ? books[bookBox.SelectedIndex] : null;
                BeforePosition = list.SelectedIndex + 1;
                Copy = copy.IsChecked == true;
                return true;
            });
        }
    }

    public class HyperlinkDialog : VDialog
    {
        public string Display, Address, Tip;
        public HyperlinkDialog(Window owner, string text, string address, List<string> sheets) : base(owner, "Insert Hyperlink", 440)
        {
            var sp = new StackPanel();
            var kind = Combo(new[] { "Existing File or Web Page", "Place in This Document", "E-mail Address" }, address != null && address.StartsWith("#") ? 1 : address != null && address.StartsWith("mailto:") ? 2 : 0, 260);
            sp.Children.Add(Row(new TextBlock { Text = "Link to:", Width = 110, VerticalAlignment = VerticalAlignment.Center }, kind));
            var disp = Box(text, 300);
            sp.Children.Add(Row(new TextBlock { Text = "Text to display:", Width = 110, VerticalAlignment = VerticalAlignment.Center }, disp));
            var addr = Box(address?.TrimStart('#') ?? "", 300);
            var addrLabel = new TextBlock { Text = "Address:", Width = 110, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(Row(addrLabel, addr));
            var sheetBox = Combo(sheets, 0, 200);
            var cellBox = Box("A1", 90);
            var placeRow = Row(new TextBlock { Text = "Cell reference:", Width = 110, VerticalAlignment = VerticalAlignment.Center }, sheetBox, new TextBlock { Text = " ! ", VerticalAlignment = VerticalAlignment.Center }, cellBox);
            sp.Children.Add(placeRow);
            var browse = UI.Btn("Browse...", (s, e) => { var d = new Microsoft.Win32.OpenFileDialog(); if (d.ShowDialog() == true) addr.Text = d.FileName; });
            browse.HorizontalAlignment = HorizontalAlignment.Left; browse.Margin = new Thickness(110, 0, 0, 6);
            sp.Children.Add(browse);
            var tip = Box("", 300);
            sp.Children.Add(Row(new TextBlock { Text = "ScreenTip:", Width = 110, VerticalAlignment = VerticalAlignment.Center }, tip));
            var remove = UI.Btn("Remove Link", (s, e) => { Address = ""; DialogResult = true; });
            void Mode()
            {
                placeRow.Visibility = kind.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
                addr.Visibility = addrLabel.Visibility = kind.SelectedIndex == 1 ? Visibility.Collapsed : Visibility.Visible;
                browse.Visibility = kind.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
                addrLabel.Text = kind.SelectedIndex == 2 ? "E-mail address:" : "Address:";
            }
            kind.SelectionChanged += (s, e) => Mode();
            Mode();
            if (address != null && address.StartsWith("#") && address.Contains("!")) { var p = address.Substring(1).Split('!'); sheetBox.SelectedItem = p[0].Trim('\''); cellBox.Text = p[1]; }
            Build(sp, () =>
            {
                Display = disp.Text; Tip = tip.Text;
                Address = kind.SelectedIndex == 1 ? "#'" + sheetBox.SelectedItem + "'!" + cellBox.Text : kind.SelectedIndex == 2 ? (addr.Text.StartsWith("mailto:") ? addr.Text : "mailto:" + addr.Text) : addr.Text.Trim();
                return true;
            }, "OK", address != null && address.Length > 0 ? remove : null);
            if (address == null || address.Length == 0) { }
        }
    }

    public class NameManagerDialog : VDialog
    {
        public bool Changed;
        public NameManagerDialog(Window owner, XLWorkbook wb) : base(owner, "Name Manager", 520)
        {
            var sp = new StackPanel();
            var list = new ListView { Height = 220 };
            var gv = new GridView();
            gv.Columns.Add(new GridViewColumn { Header = "Name", Width = 130, DisplayMemberBinding = new System.Windows.Data.Binding("Name") });
            gv.Columns.Add(new GridViewColumn { Header = "Refers To", Width = 230, DisplayMemberBinding = new System.Windows.Data.Binding("RefersTo") });
            gv.Columns.Add(new GridViewColumn { Header = "Scope", Width = 90, DisplayMemberBinding = new System.Windows.Data.Binding("Scope") });
            list.View = gv;
            void Fill() => list.ItemsSource = wb.DefinedNames.Select(n => new { n.Name, RefersTo = "=" + n.RefersTo, Scope = "Workbook" })
                .Concat(wb.Worksheets.SelectMany(w => w.DefinedNames.Select(n => new { n.Name, RefersTo = "=" + n.RefersTo, Scope = w.Name }))).ToList();
            Fill();
            var del = UI.Btn("Delete", (s, e) =>
            {
                if (list.SelectedItem == null) return;
                dynamic item = list.SelectedItem;
                string name = item.Name, scope = item.Scope;
                if (scope == "Workbook") wb.DefinedNames.Delete(name); else wb.Worksheet(scope).DefinedNames.Delete(name);
                Changed = true; Fill();
            });
            var edit = UI.Btn("Edit...", (s, e) =>
            {
                if (list.SelectedItem == null) return;
                dynamic item = list.SelectedItem;
                string name = item.Name, refers = item.RefersTo;
                var nr = InputDialog.Ask(this, "Edit Name", "Refers to:", refers);
                if (nr == null) return;
                try { if (item.Scope == "Workbook") { wb.DefinedNames.Delete(name); wb.DefinedNames.Add(name, nr.TrimStart('=')); } Changed = true; Fill(); }
                catch (Exception ex) { VistaDialog.Show(this, ex.Message, "Name Manager", MessageBoxButton.OK, DlgIcon.Warning); }
            });
            sp.Children.Add(Row(edit, del));
            sp.Children.Add(list);
            Content = UI.DialogLayout(sp, UI.Strip(UI.Btn("Close", (s, e) => Close(), true, true)));
        }
    }

    public class TextToColumnsDialog : VDialog
    {
        CheckBox _tab, _semi, _comma, _space, _other; TextBox _otherBox; RadioButton _fixed; TextBox _widths;
        public TextToColumnsDialog(Window owner) : base(owner, "Convert Text to Columns Wizard", 400)
        {
            var sp = new StackPanel();
            sp.Children.Add(Label("Choose how your text is separated:"));
            var delim = Radio("Delimited - characters such as commas or tabs separate each field", "t", true);
            _fixed = Radio("Fixed width - fields are aligned in columns with spaces between each field", "t");
            delim.Content = new TextBlock { Text = (string)delim.Content, TextWrapping = TextWrapping.Wrap, Width = 330 };
            _fixed.Content = new TextBlock { Text = "Fixed width - fields are aligned in columns with spaces between each field", TextWrapping = TextWrapping.Wrap, Width = 330 };
            sp.Children.Add(delim); sp.Children.Add(_fixed);
            var d = new StackPanel();
            d.Children.Add(Row(_tab = Chk("Tab", true), new TextBlock { Width = 12 }, _semi = Chk("Semicolon"), new TextBlock { Width = 12 }, _comma = Chk("Comma"), new TextBlock { Width = 12 }, _space = Chk("Space")));
            d.Children.Add(Row(_other = Chk("Other:"), _otherBox = Box("", 30)));
            sp.Children.Add(Group("Delimiters", d));
            sp.Children.Add(Label("Column widths for fixed width (e.g. 10, 8, 12):"));
            sp.Children.Add(_widths = Box("10, 10"));
            Build(sp, null, "Finish");
        }

        public List<string> Split(string t)
        {
            if (_fixed.IsChecked == true)
            {
                var widths = _widths.Text.Split(',').Select(s => int.TryParse(s.Trim(), out var w) ? w : 0).Where(w => w > 0).ToList();
                var parts = new List<string>(); int pos = 0;
                foreach (var w in widths) { if (pos >= t.Length) break; parts.Add(t.Substring(pos, Math.Min(w, t.Length - pos)).Trim()); pos += w; }
                if (pos < t.Length) parts.Add(t.Substring(pos).Trim());
                return parts;
            }
            var seps = new List<char>();
            if (_tab.IsChecked == true) seps.Add('\t'); if (_semi.IsChecked == true) seps.Add(';'); if (_comma.IsChecked == true) seps.Add(','); if (_space.IsChecked == true) seps.Add(' ');
            if (_other.IsChecked == true && _otherBox.Text.Length > 0) seps.Add(_otherBox.Text[0]);
            if (seps.Count == 0) return new List<string> { t };
            return t.Split(seps.ToArray()).Select(s => s.Trim()).ToList();
        }
    }

    public class SeriesDialog : VDialog
    {
        public bool Columns, Growth, Dates;
        public double Step = 1; public double? Stop; public string DateUnit = "Day";
        public SeriesDialog(Window owner, bool columns) : base(owner, "Series", 360)
        {
            var sp = new StackPanel();
            var rows = Radio("Rows", "s", !columns); var cols = Radio("Columns", "s", columns);
            var lin = Radio("Linear", "t", true); var gro = Radio("Growth", "t"); var date = Radio("Date", "t");
            var day = Radio("Day", "u", true); var wd = Radio("Weekday", "u"); var mo = Radio("Month", "u"); var yr = Radio("Year", "u");
            var a = new StackPanel(); a.Children.Add(rows); a.Children.Add(cols);
            var b = new StackPanel(); b.Children.Add(lin); b.Children.Add(gro); b.Children.Add(date);
            var c = new StackPanel(); c.Children.Add(day); c.Children.Add(wd); c.Children.Add(mo); c.Children.Add(yr);
            sp.Children.Add(Row(Group("Series in", a), new TextBlock { Width = 8 }, Group("Type", b), new TextBlock { Width = 8 }, Group("Date unit", c)));
            var step = Box("1", 70); var stop = Box("", 70);
            sp.Children.Add(Row(new TextBlock { Text = "Step value:", Width = 80, VerticalAlignment = VerticalAlignment.Center }, step, new TextBlock { Text = "   Stop value:", Width = 90, VerticalAlignment = VerticalAlignment.Center }, stop));
            Build(sp, () =>
            {
                Columns = cols.IsChecked == true; Growth = gro.IsChecked == true; Dates = date.IsChecked == true;
                DateUnit = wd.IsChecked == true ? "Weekday" : mo.IsChecked == true ? "Month" : yr.IsChecked == true ? "Year" : "Day";
                if (!double.TryParse(step.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out Step)) Step = 1;
                Stop = double.TryParse(stop.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var sv) ? sv : (double?)null;
                return true;
            });
        }
    }

    public class DataValidationDialog : VDialog
    {
        public bool ClearAll;
        readonly ComboBox _allow, _op, _style;
        readonly TextBox _min, _max, _source, _inTitle, _inMsg, _errTitle, _errMsg;
        readonly CheckBox _blank, _dropdown, _showIn, _showErr;

        public DataValidationDialog(Window owner, IXLDataValidation dv) : base(owner, "Data Validation", 440)
        {
            var tabs = new TabControl { Height = 300 };
            var s1 = new StackPanel { Margin = new Thickness(8) };
            var allows = new[] { "Any value", "Whole number", "Decimal", "List", "Date", "Time", "Text length" };
            _allow = Combo(allows, 0, 200);
            _op = Combo(new[] { "between", "not between", "equal to", "not equal to", "greater than", "less than", "greater than or equal to", "less than or equal to" }, 0, 200);
            _min = Box("", 200); _max = Box("", 200); _source = Box("", 300);
            _blank = Chk("Ignore blank", true); _dropdown = Chk("In-cell dropdown", true);
            s1.Children.Add(Label("Allow:")); s1.Children.Add(Row(_allow, new TextBlock { Width = 10 }, _blank));
            var opRow = Row(new TextBlock { Text = "Data:", Width = 60, VerticalAlignment = VerticalAlignment.Center }, _op);
            var minRow = Row(new TextBlock { Text = "Minimum:", Width = 60, VerticalAlignment = VerticalAlignment.Center }, _min);
            var maxRow = Row(new TextBlock { Text = "Maximum:", Width = 60, VerticalAlignment = VerticalAlignment.Center }, _max);
            var srcRow = Row(new TextBlock { Text = "Source:", Width = 60, VerticalAlignment = VerticalAlignment.Center }, _source);
            s1.Children.Add(opRow); s1.Children.Add(minRow); s1.Children.Add(maxRow); s1.Children.Add(srcRow); s1.Children.Add(_dropdown);
            void Mode()
            {
                bool list = _allow.SelectedIndex == 3, any = _allow.SelectedIndex == 0;
                opRow.Visibility = minRow.Visibility = any || list ? Visibility.Collapsed : Visibility.Visible;
                bool two = _op.SelectedIndex <= 1;
                maxRow.Visibility = any || list || !two ? Visibility.Collapsed : Visibility.Visible;
                ((TextBlock)minRow.Children[0]).Text = two ? "Minimum:" : "Value:";
                srcRow.Visibility = _dropdown.Visibility = list ? Visibility.Visible : Visibility.Collapsed;
            }
            _allow.SelectionChanged += (s, e) => Mode(); _op.SelectionChanged += (s, e) => Mode();
            tabs.Items.Add(new TabItem { Header = "Settings", Content = s1 });
            var s2 = new StackPanel { Margin = new Thickness(8) };
            _showIn = Chk("Show input message when cell is selected", true); _inTitle = Box(); _inMsg = new TextBox { Height = 90, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
            s2.Children.Add(_showIn); s2.Children.Add(Label("Title:")); s2.Children.Add(_inTitle); s2.Children.Add(Label("Input message:")); s2.Children.Add(_inMsg);
            tabs.Items.Add(new TabItem { Header = "Input Message", Content = s2 });
            var s3 = new StackPanel { Margin = new Thickness(8) };
            _showErr = Chk("Show error alert after invalid data is entered", true); _style = Combo(new[] { "Stop", "Warning", "Information" }, 0, 140); _errTitle = Box(); _errMsg = new TextBox { Height = 90, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
            s3.Children.Add(_showErr); s3.Children.Add(Label("Style:")); s3.Children.Add(_style); s3.Children.Add(Label("Title:")); s3.Children.Add(_errTitle); s3.Children.Add(Label("Error message:")); s3.Children.Add(_errMsg);
            tabs.Items.Add(new TabItem { Header = "Error Alert", Content = s3 });
            if (dv != null)
            {
                _allow.SelectedIndex = dv.AllowedValues switch { XLAllowedValues.WholeNumber => 1, XLAllowedValues.Decimal => 2, XLAllowedValues.List => 3, XLAllowedValues.Date => 4, XLAllowedValues.Time => 5, XLAllowedValues.TextLength => 6, _ => 0 };
                _op.SelectedIndex = Math.Max(0, Array.IndexOf(Ops_, dv.Operator));
                _min.Text = dv.MinValue; _max.Text = dv.MaxValue; _source.Text = dv.AllowedValues == XLAllowedValues.List ? dv.Value : "";
                _blank.IsChecked = dv.IgnoreBlanks; _dropdown.IsChecked = dv.InCellDropdown;
                _showIn.IsChecked = dv.ShowInputMessage; _inTitle.Text = dv.InputTitle; _inMsg.Text = dv.InputMessage;
                _showErr.IsChecked = dv.ShowErrorMessage; _errTitle.Text = dv.ErrorTitle; _errMsg.Text = dv.ErrorMessage;
                _style.SelectedIndex = dv.ErrorStyle == XLErrorStyle.Warning ? 1 : dv.ErrorStyle == XLErrorStyle.Information ? 2 : 0;
            }
            Mode();
            var clear = UI.Btn("Clear All", (s, e) => { ClearAll = true; DialogResult = true; });
            Build(tabs, null, "OK", clear);
        }

        static readonly XLOperator[] Ops_ = { XLOperator.Between, XLOperator.NotBetween, XLOperator.EqualTo, XLOperator.NotEqualTo, XLOperator.GreaterThan, XLOperator.LessThan, XLOperator.EqualOrGreaterThan, XLOperator.EqualOrLessThan };

        public void Apply(IXLDataValidation dv)
        {
            string V(string s) => s.Trim();
            switch (_allow.SelectedIndex)
            {
                case 0: dv.AllowedValues = XLAllowedValues.AnyValue; break;
                case 3:
                    var src = V(_source.Text);
                    if (src.StartsWith("=")) dv.List(src.Substring(1)); else dv.List("\"" + src.Replace(";", ",") + "\"", _dropdown.IsChecked == true);
                    dv.InCellDropdown = _dropdown.IsChecked == true;
                    break;
                default:
                    dv.AllowedValues = _allow.SelectedIndex switch { 1 => XLAllowedValues.WholeNumber, 2 => XLAllowedValues.Decimal, 4 => XLAllowedValues.Date, 5 => XLAllowedValues.Time, _ => XLAllowedValues.TextLength };
                    dv.Operator = Ops_[Math.Max(0, _op.SelectedIndex)];
                    dv.MinValue = V(_min.Text);
                    if (_op.SelectedIndex <= 1) dv.MaxValue = V(_max.Text);
                    break;
            }
            dv.IgnoreBlanks = _blank.IsChecked == true;
            dv.ShowInputMessage = _showIn.IsChecked == true; dv.InputTitle = _inTitle.Text; dv.InputMessage = _inMsg.Text;
            dv.ShowErrorMessage = _showErr.IsChecked == true; dv.ErrorTitle = _errTitle.Text; dv.ErrorMessage = _errMsg.Text;
            dv.ErrorStyle = _style.SelectedIndex == 1 ? XLErrorStyle.Warning : _style.SelectedIndex == 2 ? XLErrorStyle.Information : XLErrorStyle.Stop;
        }
    }

    public class SubtotalDialogWindow : VDialog
    {
        public int GroupBy, FunctionNumber = 9;
        public string FunctionName = "Total";
        public List<int> SumColumns = new();
        public SubtotalDialogWindow(Window owner, List<string> columns) : base(owner, "Subtotal", 320)
        {
            var sp = new StackPanel();
            sp.Children.Add(Label("At each change in:"));
            var by = Combo(columns, 0); sp.Children.Add(by);
            sp.Children.Add(Label("Use function:"));
            var fns = new[] { ("Sum", 9, "Total"), ("Count", 3, "Count"), ("Average", 1, "Average"), ("Max", 4, "Max"), ("Min", 5, "Min") };
            var fn = Combo(fns.Select(f => f.Item1), 0); sp.Children.Add(fn);
            sp.Children.Add(Label("Add subtotal to:"));
            var checks = new StackPanel();
            for (int i = 0; i < columns.Count; i++) checks.Children.Add(Chk(columns[i], i == columns.Count - 1));
            sp.Children.Add(new ScrollViewer { Content = checks, Height = 120, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            sp.Children.Add(Label("Your list is sorted by the grouping column first, so each group gets one subtotal.", 6));
            Build(sp, () =>
            {
                GroupBy = by.SelectedIndex; FunctionNumber = fns[fn.SelectedIndex].Item2; FunctionName = fns[fn.SelectedIndex].Item3;
                SumColumns = checks.Children.OfType<CheckBox>().Select((c, i) => (c, i)).Where(x => x.c.IsChecked == true).Select(x => x.i).ToList();
                return SumColumns.Count > 0;
            });
        }
    }

    public class CfRuleDialog : VDialog
    {
        public string Value1, Value2;
        public int PresetIndex;
        public CfRuleDialog(Window owner, string title, string prompt, bool two, List<string> presets) : base(owner, title, 420)
        {
            var sp = new StackPanel();
            sp.Children.Add(Label(prompt));
            var a = Box("", 130); var b = Box("", 130);
            var style = Combo(presets, 0, 230);
            if (two) sp.Children.Add(Row(a, new TextBlock { Text = "  and  ", VerticalAlignment = VerticalAlignment.Center }, b));
            else sp.Children.Add(Row(a));
            sp.Children.Add(Row(new TextBlock { Text = "with ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 6) }, style));
            Build(sp, () => { Value1 = a.Text; Value2 = b.Text; PresetIndex = style.SelectedIndex; return a.Text.Length > 0; });
            Loaded += (s, e) => a.Focus();
        }
    }

    public class PropertiesDialog : VDialog
    {
        public PropertiesDialog(Window owner, XLWorkbookProperties p) : base(owner, "Document Properties", 380)
        {
            var sp = new StackPanel();
            var fields = new (string Label, Func<string> Get, Action<string> Set)[]
            {
                ("Author:", () => p.Author, v => p.Author = v), ("Title:", () => p.Title, v => p.Title = v), ("Subject:", () => p.Subject, v => p.Subject = v),
                ("Keywords:", () => p.Keywords, v => p.Keywords = v), ("Category:", () => p.Category, v => p.Category = v), ("Status:", () => p.Status, v => p.Status = v),
                ("Comments:", () => p.Comments, v => p.Comments = v), ("Company:", () => p.Company, v => p.Company = v), ("Manager:", () => p.Manager, v => p.Manager = v),
            };
            var boxes = new List<TextBox>();
            foreach (var f in fields) { var b = Box(f.Get() ?? "", 240); boxes.Add(b); sp.Children.Add(Row(new TextBlock { Text = f.Label, Width = 80, VerticalAlignment = VerticalAlignment.Center }, b)); }
            Build(sp, () => { for (int i = 0; i < fields.Length; i++) fields[i].Set(boxes[i].Text); return true; });
        }
    }

    public class OptionsDialog : VDialog
    {
        public OptionsDialog(Window owner) : base(owner, "Valco Spreadsheets Options", 520)
        {
            var s = Settings.Current;
            var tabs = new TabControl { Height = 380 };
            var pop = new StackPanel { Margin = new Thickness(10) };
            pop.Children.Add(Heading("Top options for working with Valco Spreadsheets"));
            var scheme = Combo(OfficeTheme.Schemes, Math.Max(0, Array.IndexOf(OfficeTheme.Schemes, s.ColorScheme)), 130);
            pop.Children.Add(Row(new TextBlock { Text = "Color scheme:", Width = 160, VerticalAlignment = VerticalAlignment.Center }, scheme));
            pop.Children.Add(Heading("When creating new workbooks"));
            var font = Combo(Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(f => f), 0, 200, true); font.Text = s.DefaultFont;
            var size = Box(s.DefaultSize.ToString(CultureInfo.CurrentCulture), 60);
            var sheets = Box(s.DefaultSheets.ToString(), 60);
            pop.Children.Add(Row(new TextBlock { Text = "Use this font:", Width = 160, VerticalAlignment = VerticalAlignment.Center }, font));
            pop.Children.Add(Row(new TextBlock { Text = "Font size:", Width = 160, VerticalAlignment = VerticalAlignment.Center }, size));
            pop.Children.Add(Row(new TextBlock { Text = "Include this many sheets:", Width = 160, VerticalAlignment = VerticalAlignment.Center }, sheets));
            pop.Children.Add(Heading("Personalize your copy of Valco Spreadsheets"));
            var user = Box(s.UserName, 200);
            pop.Children.Add(Row(new TextBlock { Text = "User name:", Width = 160, VerticalAlignment = VerticalAlignment.Center }, user));
            tabs.Items.Add(new TabItem { Header = "Popular", Content = pop });

            var adv = new StackPanel { Margin = new Thickness(10) };
            adv.Children.Add(Heading("Editing options"));
            var move = Chk("After pressing Enter, move selection", s.MoveAfterEnter);
            var dir = Combo(new[] { "Down", "Right", "Up", "Left" }, Math.Max(0, Array.IndexOf(new[] { "Down", "Right", "Up", "Left" }, s.EnterDirection)), 90);
            adv.Children.Add(Row(move, new TextBlock { Text = "  Direction:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }, dir));
            var handle = Chk("Enable fill handle and cell drag-and-drop", s.FillHandle); adv.Children.Add(handle);
            var autoc = Chk("Enable Formula AutoComplete", s.AutoComplete); adv.Children.Add(autoc);
            adv.Children.Add(Heading("Display"));
            var fbar = Chk("Show formula bar", s.ShowFormulaBar); adv.Children.Add(fbar);
            var sbar = Chk("Show status bar", s.ShowStatusBar); adv.Children.Add(sbar);
            tabs.Items.Add(new TabItem { Header = "Advanced", Content = adv });

            var save = new StackPanel { Margin = new Thickness(10) };
            save.Children.Add(Heading("Save workbooks"));
            var ar = Chk("Save AutoRecover information every", s.AutoRecover);
            var mins = Box(s.AutoRecoverMinutes.ToString(), 40);
            save.Children.Add(Row(ar, mins, new TextBlock { Text = " minutes", VerticalAlignment = VerticalAlignment.Center }));
            var folder = Box(s.DefaultFolder, 260);
            save.Children.Add(Row(new TextBlock { Text = "Default file location:", Width = 140, VerticalAlignment = VerticalAlignment.Center }, folder));
            tabs.Items.Add(new TabItem { Header = "Save", Content = save });

            var res = new StackPanel { Margin = new Thickness(10) };
            res.Children.Add(Heading("Get updates"));
            var upd = Chk("Check for Valco Office updates automatically", Updates.AutoCheck);
            res.Children.Add(upd);
            var check = UI.Btn("Check for Updates", (o, e) => Updates.CheckNow(this, "sheets", "Valco Spreadsheets"));
            check.HorizontalAlignment = HorizontalAlignment.Left; check.Margin = new Thickness(0, 6, 0, 0);
            res.Children.Add(check);
            res.Children.Add(Heading("About Valco Spreadsheets"));
            res.Children.Add(Label("Valco Spreadsheets " + typeof(OptionsDialog).Assembly.GetName().Version?.ToString(3) + " — a spreadsheet program for Windows, from the Valco suite. © 2026 ItsRaiden"));
            tabs.Items.Add(new TabItem { Header = "Resources", Content = res });

            Build(tabs, () =>
            {
                s.ColorScheme = (string)scheme.SelectedItem;
                s.DefaultFont = string.IsNullOrWhiteSpace(font.Text) ? "Calibri" : font.Text.Trim();
                if (double.TryParse(size.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var sz) && sz >= 1 && sz <= 409) s.DefaultSize = sz;
                if (int.TryParse(sheets.Text, out var n) && n >= 1 && n <= 255) s.DefaultSheets = n;
                s.UserName = user.Text.Trim();
                s.MoveAfterEnter = move.IsChecked == true; s.EnterDirection = (string)dir.SelectedItem;
                s.FillHandle = handle.IsChecked == true; s.AutoComplete = autoc.IsChecked == true;
                s.ShowFormulaBar = fbar.IsChecked == true; s.ShowStatusBar = sbar.IsChecked == true;
                s.AutoRecover = ar.IsChecked == true; if (int.TryParse(mins.Text, out var m) && m >= 1 && m <= 120) s.AutoRecoverMinutes = m;
                s.DefaultFolder = folder.Text.Trim();
                Updates.AutoCheck = upd.IsChecked == true;
                Settings.Save();
                return true;
            });
        }
    }

    /// <summary>The AutoFilter drop-down: sort, value checklist with Select All, number/text filters.</summary>
    public class FilterPopup
    {
        public event Action<bool> Sort;
        public event Action<List<XLCellValue>, Action<IXLFilterColumn>> Apply;
        readonly Window _owner;
        readonly IXLWorksheet _ws;
        readonly CellRect _range;
        readonly int _col;

        public FilterPopup(Window owner, IXLWorksheet ws, CellRect range, int col) { _owner = owner; _ws = ws; _range = range; _col = col; }

        public void Show(FrameworkElement target, Point at)
        {
            var popup = new Popup { PlacementTarget = target, Placement = PlacementMode.Relative, HorizontalOffset = at.X, VerticalOffset = at.Y, StaysOpen = false, AllowsTransparency = true };
            var sp = new StackPanel { Width = 230, Margin = new Thickness(2) };
            Button Item(string text, string icon, Action a)
            {
                var b = new Button { Style = RB.BtnStyle, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(4, 3, 4, 3) };
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new Image { Source = icon != null ? UI.Img(icon) : null, Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0) });
                row.Children.Add(new TextBlock { Text = text });
                b.Content = row;
                b.Click += (s, e) => { popup.IsOpen = false; a(); };
                return b;
            }
            int lastRow = Math.Max(_range.R2, _ws.LastRowUsed()?.RowNumber() ?? _range.R2);
            var values = new SortedDictionary<string, XLCellValue>(StringComparer.CurrentCultureIgnoreCase);
            bool blanks = false, numeric = true;
            for (int r = _range.R1 + 1; r <= lastRow; r++)
            {
                var c = _ws.Cell(r, _col);
                if (c.IsEmpty()) { blanks = true; continue; }
                var v = Ops.SafeValue(c);
                if (!v.IsNumber && !v.IsDateTime) numeric = false;
                var key = c.GetFormattedString();
                if (!values.ContainsKey(key)) values[key] = v;
                if (values.Count > 10000) break;
            }
            sp.Children.Add(Item(numeric ? "Sort Smallest to Largest" : "Sort A to Z", "IconSortAZ", () => Sort?.Invoke(false)));
            sp.Children.Add(Item(numeric ? "Sort Largest to Smallest" : "Sort Z to A", "IconSortZA", () => Sort?.Invoke(true)));
            sp.Children.Add(new Separator());
            sp.Children.Add(Item("Clear Filter From \"" + _ws.Cell(_range.R1, _col).GetFormattedString() + "\"", "IconClearFilter", () => Apply?.Invoke(null, null)));
            sp.Children.Add(Item(numeric ? "Number Filters..." : "Text Filters...", "IconFilter", () => CustomFilter(numeric)));
            var checks = new StackPanel();
            var all = new CheckBox { Content = "(Select All)", IsChecked = true, Margin = new Thickness(2) };
            checks.Children.Add(all);
            var items = new List<(CheckBox Box, XLCellValue Value)>();
            foreach (var kv in values)
            {
                var cb = new CheckBox { Content = kv.Key, IsChecked = true, Margin = new Thickness(2) };
                items.Add((cb, kv.Value));
                checks.Children.Add(cb);
            }
            CheckBox blank = null;
            if (blanks) { blank = new CheckBox { Content = "(Blanks)", IsChecked = true, Margin = new Thickness(2) }; checks.Children.Add(blank); }
            all.Click += (s, e) => { foreach (var (b, _) in items) b.IsChecked = all.IsChecked; if (blank != null) blank.IsChecked = all.IsChecked; };
            sp.Children.Add(new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Margin = new Thickness(4), Child = new ScrollViewer { Content = checks, Height = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
            var ok = UI.Btn("OK", (s, e) =>
            {
                popup.IsOpen = false;
                if (items.All(i => i.Box.IsChecked == true) && (blank == null || blank.IsChecked == true)) { Apply?.Invoke(null, null); return; }
                var picked = items.Where(i => i.Box.IsChecked == true).Select(i => i.Value).ToList();
                if (blank?.IsChecked == true) picked.Add("");
                Apply?.Invoke(picked, null);
            }, true);
            var cancel = UI.Btn("Cancel", (s, e) => popup.IsOpen = false);
            sp.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(4), Children = { ok, cancel } });
            popup.Child = new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0x86, 0x9F, 0xC1)), BorderThickness = new Thickness(1), Child = sp };
            popup.IsOpen = true;
        }

        void CustomFilter(bool numeric)
        {
            var ops = numeric ? new[] { "equals", "does not equal", "is greater than", "is greater than or equal to", "is less than", "is less than or equal to" }
                              : new[] { "equals", "does not equal", "begins with", "ends with", "contains", "does not contain" };
            var d = new VDialog(_owner, "Custom AutoFilter", 380);
            var sp = new StackPanel();
            sp.Children.Add(VDialog.Label("Show rows where:"));
            var op = VDialog.Combo(ops, 0, 180); var val = VDialog.Box("", 150);
            sp.Children.Add(VDialog.Row(op, new TextBlock { Width = 8 }, val));
            d.Content = UI.DialogLayout(sp, UI.Strip(UI.Btn("OK", (s, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
            if (d.ShowDialog() != true || val.Text.Length == 0) return;
            var text = val.Text; int i = op.SelectedIndex;
            XLCellValue v = numeric && double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var num) ? num : text;
            Apply?.Invoke(null, fc =>
            {
                if (numeric)
                    switch (i) { case 0: fc.EqualTo(v); break; case 1: fc.NotEqualTo(v); break; case 2: fc.GreaterThan(v); break; case 3: fc.EqualOrGreaterThan(v); break; case 4: fc.LessThan(v); break; default: fc.EqualOrLessThan(v); break; }
                else
                    switch (i) { case 0: fc.EqualTo(text); break; case 1: fc.NotEqualTo(text); break; case 2: fc.BeginsWith(text); break; case 3: fc.EndsWith(text); break; case 4: fc.Contains(text); break; default: fc.NotContains(text); break; }
            });
        }
    }
}
