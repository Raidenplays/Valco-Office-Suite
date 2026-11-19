using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ClosedXML.Excel;
using ValcoSpreadsheets.Grid;

namespace ValcoSpreadsheets
{
    /// <summary>Format Cells (Ctrl+1): Number, Alignment, Font, Border, Fill, Protection.</summary>
    public class FormatCellsDialog : VDialog
    {
        readonly XLWorkbook _wb;
        readonly IXLCell _cell;
        readonly List<Action<IXLStyle>> _changes = new();
        public readonly List<Action<IXLRange>> BorderChanges = new();

        // number
        ListBox _cats; ContentControl _catPanel; TextBlock _sample; string _numFmt; bool _numTouched;
        // alignment
        ComboBox _hAlign, _vAlign; TextBox _indent, _rotation; CheckBox _wrap, _shrink, _merge;
        // font
        ListBox _fontList, _styleList, _sizeList; TextBox _fontBox, _sizeBox; ComboBox _underline; CheckBox _strike, _super, _sub; Color? _fontColor; TextBlock _fontPreview; Border _fontColorBtn;
        // fill
        Color? _fill; bool _fillTouched; Border _fillSample;
        // protection
        CheckBox _locked, _hidden;

        public FormatCellsDialog(Window owner, XLWorkbook wb, IXLCell cell, string tab) : base(owner, "Format Cells", 520)
        {
            _wb = wb; _cell = cell;
            var st = cell.Style;
            var tabs = new TabControl { Height = 380 };
            tabs.Items.Add(new TabItem { Header = "Number", Content = NumberTab(st) });
            tabs.Items.Add(new TabItem { Header = "Alignment", Content = AlignmentTab(st) });
            tabs.Items.Add(new TabItem { Header = "Font", Content = FontTab(st) });
            tabs.Items.Add(new TabItem { Header = "Border", Content = BorderTab() });
            tabs.Items.Add(new TabItem { Header = "Fill", Content = FillTab(st) });
            tabs.Items.Add(new TabItem { Header = "Protection", Content = ProtectionTab(st) });
            var idx = new[] { "Number", "Alignment", "Font", "Border", "Fill", "Protection" }.ToList().IndexOf(tab ?? "Number");
            tabs.SelectedIndex = Math.Max(0, idx);
            Build(tabs, Collect);
        }

        public void ApplyTo(IXLStyle s) { foreach (var c in _changes) c(s); }
        public void ApplyBorders(IXLRange r) { foreach (var b in BorderChanges) b(r); }

        // ============================================================ Number
        static readonly (string Cat, string[] Formats)[] Cats =
        {
            ("General", new[] { "" }),
            ("Number", new[] { "0", "0.00", "#,##0", "#,##0.00", "0.00;[Red]0.00", "#,##0.00;[Red]-#,##0.00", "#,##0.00_);(#,##0.00)" }),
            ("Currency", new[] { "\"$\"#,##0.00", "\"$\"#,##0", "\"$\"#,##0.00;[Red]\"$\"#,##0.00", "\"$\"#,##0.00_);(\"$\"#,##0.00)", "[$€-2] #,##0.00", "[$£-809]#,##0.00" }),
            ("Accounting", new[] { "_(\"$\"* #,##0.00_);_(\"$\"* \\(#,##0.00\\);_(\"$\"* \"-\"??_);_(@_)", "_(* #,##0.00_);_(* \\(#,##0.00\\);_(* \"-\"??_);_(@_)", "_(\"$\"* #,##0_);_(\"$\"* \\(#,##0\\);_(\"$\"* \"-\"_);_(@_)" }),
            ("Date", new[] { "m/d/yyyy", "[$-F800]dddd, mmmm dd, yyyy", "m/d;@", "m/d/yy;@", "mm/dd/yy;@", "d-mmm;@", "d-mmm-yy;@", "dd-mmm-yy;@", "mmm-yy;@", "mmmm-yy;@", "mmmm d, yyyy;@", "m/d/yy h:mm;@", "yyyy-mm-dd" }),
            ("Time", new[] { "[$-F400]h:mm:ss AM/PM", "h:mm;@", "[$-409]h:mm AM/PM;@", "h:mm:ss;@", "[$-409]h:mm:ss AM/PM;@", "mm:ss.0;@", "[h]:mm:ss;@" }),
            ("Percentage", new[] { "0%", "0.00%", "0.0%" }),
            ("Fraction", new[] { "# ?/?", "# ??/??", "# ???/???", "# ?/2", "# ?/4", "# ?/8", "# ??/16", "# ?/10", "# ??/100" }),
            ("Scientific", new[] { "0.00E+00", "0.0E+00", "##0.0E+0" }),
            ("Text", new[] { "@" }),
            ("Special", new[] { "00000", "00000-0000", "[<=9999999]###-####;(###) ###-####", "000-000-000" }),
            ("Custom", new string[0]),
        };

        UIElement NumberTab(IXLStyle st)
        {
            var g = new System.Windows.Controls.Grid { Margin = new Thickness(8) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var left = new StackPanel();
            left.Children.Add(Label("Category:"));
            _cats = new ListBox { ItemsSource = Cats.Select(c => c.Cat).ToList(), Height = 290 };
            left.Children.Add(_cats);
            g.Children.Add(left);
            var right = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
            System.Windows.Controls.Grid.SetColumn(right, 1);
            right.Children.Add(Group("Sample", _sample = new TextBlock { MinHeight = 18 }));
            _catPanel = new ContentControl();
            right.Children.Add(_catPanel);
            g.Children.Add(right);
            _numFmt = st.NumberFormat.Format ?? "";
            if (string.IsNullOrEmpty(_numFmt) && st.NumberFormat.NumberFormatId > 0) _numFmt = BuiltIn(st.NumberFormat.NumberFormatId);
            int cat = Array.FindIndex(Cats, c => c.Formats.Contains(_numFmt));
            if (cat < 0) cat = string.IsNullOrEmpty(_numFmt) ? 0 : _numFmt.Contains("%") ? 6 : _numFmt.Contains("E+") ? 8 : _numFmt == "@" ? 9 : (_numFmt.Contains("y") || _numFmt.Contains("d")) ? 4 : _numFmt.Contains("h") ? 5 : 11;
            _cats.SelectionChanged += (s, e) => ShowCategory(_cats.SelectedIndex);
            _cats.SelectedIndex = cat;
            return g;
        }

        static string BuiltIn(int id) => id switch
        {
            1 => "0", 2 => "0.00", 3 => "#,##0", 4 => "#,##0.00", 9 => "0%", 10 => "0.00%", 11 => "0.00E+00", 12 => "# ?/?", 13 => "# ??/??",
            14 => "m/d/yyyy", 15 => "d-mmm-yy", 16 => "d-mmm", 17 => "mmm-yy", 18 => "h:mm AM/PM", 19 => "h:mm:ss AM/PM", 20 => "h:mm", 21 => "h:mm:ss", 22 => "m/d/yyyy h:mm", 49 => "@", _ => ""
        };

        void ShowCategory(int i)
        {
            if (i < 0) return;
            var (cat, formats) = Cats[i];
            var sp = new StackPanel();
            if (cat == "General")
            {
                sp.Children.Add(Label("General format cells have no specific number format.", 6));
                SetFmt("");
            }
            else if (cat == "Number" || cat == "Currency" || cat == "Percentage" || cat == "Scientific")
            {
                int dec = Math.Max(0, _numFmt.Contains('.') ? _numFmt.Split(';')[0].Split('.').Last().TakeWhile(ch => ch == '0').Count() : 0);
                if (!formats.Contains(_numFmt)) dec = 2;
                var decBox = Box(dec.ToString(), 50);
                var thousands = Chk("Use 1000 Separator (,)", _numFmt.Contains(","));
                var negative = new ListBox { ItemsSource = new[] { "-1234.10", "1234.10 (red)", "(1234.10)", "(1234.10) (red)" }, Height = 70, SelectedIndex = _numFmt.Contains("[Red]") ? 1 : _numFmt.Contains("(") ? 2 : 0 };
                void Rebuild()
                {
                    int d = int.TryParse(decBox.Text, out var v) ? Math.Clamp(v, 0, 30) : 2;
                    var body = (thousands.IsChecked == true || cat == "Currency" ? "#,##0" : "0") + (d > 0 ? "." + new string('0', d) : "");
                    if (cat == "Currency") body = "\"$\"" + body;
                    if (cat == "Percentage") body = "0" + (d > 0 ? "." + new string('0', d) : "") + "%";
                    if (cat == "Scientific") body = "0" + (d > 0 ? "." + new string('0', d) : "") + "E+00";
                    var neg = negative.SelectedIndex;
                    string f = cat is "Percentage" or "Scientific" ? body : neg switch { 1 => body + ";[Red]" + body, 2 => body + "_);(" + body + ")", 3 => body + "_);[Red](" + body + ")", _ => body };
                    SetFmt(f);
                }
                sp.Children.Add(Row(new TextBlock { Text = "Decimal places:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 6) }, decBox));
                decBox.TextChanged += (s, e) => Rebuild();
                if (cat == "Number") { sp.Children.Add(thousands); thousands.Click += (s, e) => Rebuild(); }
                if (cat is "Number" or "Currency")
                {
                    sp.Children.Add(Label("Negative numbers:", 6));
                    sp.Children.Add(negative);
                    negative.SelectionChanged += (s, e) => Rebuild();
                }
                if (!formats.Contains(_numFmt) || cat != CurrentCat()) Rebuild(); else UpdateSample();
            }
            else if (cat == "Custom")
            {
                sp.Children.Add(Label("Type:"));
                var box = Box(_numFmt);
                var all = Cats.SelectMany(c => c.Formats).Where(f => f.Length > 0).Distinct().ToList();
                var list = new ListBox { ItemsSource = all, Height = 180 };
                list.SelectionChanged += (s, e) => { if (list.SelectedItem is string f) box.Text = f; };
                box.TextChanged += (s, e) => SetFmt(box.Text);
                sp.Children.Add(box);
                sp.Children.Add(list);
            }
            else
            {
                sp.Children.Add(Label("Type:"));
                var list = new ListBox { ItemsSource = formats.Select(f => Preview(f, cat)).ToList(), Height = 200 };
                list.SelectionChanged += (s, e) => { if (list.SelectedIndex >= 0) SetFmt(formats[list.SelectedIndex]); };
                int sel = Array.IndexOf(formats, _numFmt);
                list.SelectedIndex = sel >= 0 ? sel : 0;
                sp.Children.Add(list);
                if (cat == "Text") sp.Children.Add(Label("Text format cells are treated as text even when a number is in the cell. The cell is displayed exactly as entered.", 6));
            }
            _catPanel.Content = sp;
        }

        string CurrentCat()
        {
            int cat = Array.FindIndex(Cats, c => c.Formats.Contains(_numFmt));
            return cat < 0 ? "" : Cats[cat].Cat;
        }

        string Preview(string fmt, string cat)
        {
            try
            {
                var c = new XLWorkbook().AddWorksheet("p").Cell(1, 1);
                if (cat == "Date" || cat == "Time") c.Value = new DateTime(2001, 3, 14, 13, 30, 55); else if (cat == "Text") c.Value = "Text"; else c.Value = 12345.678;
                c.Style.NumberFormat.Format = fmt;
                return c.GetFormattedString() + "        " + fmt;
            }
            catch { return fmt; }
        }

        void SetFmt(string f) { _numFmt = f; _numTouched = true; UpdateSample(); }

        void UpdateSample()
        {
            try
            {
                var c = new XLWorkbook().AddWorksheet("p").Cell(1, 1);
                var v = _cell.Value;
                if (v.IsBlank) c.Value = 0; else c.Value = v;
                c.Style.NumberFormat.Format = _numFmt;
                _sample.Text = c.GetFormattedString();
            }
            catch { _sample.Text = ""; }
        }

        // ============================================================ Alignment
        UIElement AlignmentTab(IXLStyle st)
        {
            var sp = new StackPanel { Margin = new Thickness(8) };
            var a = st.Alignment;
            var hs = new[] { "General", "Left (Indent)", "Center", "Right (Indent)", "Fill", "Justify", "Center Across Selection", "Distributed (Indent)" };
            var hv = new[] { XLAlignmentHorizontalValues.General, XLAlignmentHorizontalValues.Left, XLAlignmentHorizontalValues.Center, XLAlignmentHorizontalValues.Right, XLAlignmentHorizontalValues.Fill, XLAlignmentHorizontalValues.Justify, XLAlignmentHorizontalValues.CenterContinuous, XLAlignmentHorizontalValues.Distributed };
            var vs = new[] { "Top", "Center", "Bottom", "Justify", "Distributed" };
            var vv = new[] { XLAlignmentVerticalValues.Top, XLAlignmentVerticalValues.Center, XLAlignmentVerticalValues.Bottom, XLAlignmentVerticalValues.Justify, XLAlignmentVerticalValues.Distributed };
            _hAlign = Combo(hs, Math.Max(0, Array.IndexOf(hv, a.Horizontal)), 200);
            _vAlign = Combo(vs, Math.Max(0, Array.IndexOf(vv, a.Vertical)), 200);
            _indent = Box(a.Indent.ToString(), 50);
            var tc = new StackPanel();
            tc.Children.Add(Label("Horizontal:")); tc.Children.Add(Row(_hAlign, new TextBlock { Text = "  Indent:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 6) }, _indent));
            tc.Children.Add(Label("Vertical:")); tc.Children.Add(_vAlign);
            sp.Children.Add(Group("Text alignment", tc));
            _wrap = Chk("Wrap text", a.WrapText); _shrink = Chk("Shrink to fit", a.ShrinkToFit); _merge = Chk("Merge cells", _cell.IsMerged());
            var ctl = new StackPanel(); ctl.Children.Add(_wrap); ctl.Children.Add(_shrink); ctl.Children.Add(_merge);
            sp.Children.Add(Group("Text control", ctl));
            _rotation = Box(a.TextRotation.ToString(), 50);
            sp.Children.Add(Group("Orientation", Row(new TextBlock { Text = "Degrees:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 6) }, _rotation)));
            _hAlign.SelectionChanged += (s, e) => _changes.Add(x => x.Alignment.Horizontal = hv[_hAlign.SelectedIndex]);
            _vAlign.SelectionChanged += (s, e) => _changes.Add(x => x.Alignment.Vertical = vv[_vAlign.SelectedIndex]);
            return sp;
        }

        // ============================================================ Font
        UIElement FontTab(IXLStyle st)
        {
            var f = st.Font;
            var g = new System.Windows.Controls.Grid { Margin = new Thickness(8) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var fonts = Fonts.SystemFontFamilies.Select(x => x.Source).Distinct().OrderBy(x => x).ToList();
            var c1 = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            c1.Children.Add(Label("Font:")); c1.Children.Add(_fontBox = Box(f.FontName)); c1.Children.Add(_fontList = new ListBox { ItemsSource = fonts, Height = 120, SelectedItem = f.FontName });
            var c2 = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            var styles = new[] { "Regular", "Italic", "Bold", "Bold Italic" };
            c2.Children.Add(Label("Font style:")); c2.Children.Add(Box(styles[(f.Bold ? 2 : 0) + (f.Italic ? 1 : 0)])); c2.Children.Add(_styleList = new ListBox { ItemsSource = styles, Height = 120, SelectedIndex = (f.Bold ? 2 : 0) + (f.Italic ? 1 : 0) });
            var c3 = new StackPanel();
            var sizes = new[] { 8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72 }.Select(x => x.ToString()).ToList();
            c3.Children.Add(Label("Size:")); c3.Children.Add(_sizeBox = Box(f.FontSize.ToString(CultureInfo.CurrentCulture))); c3.Children.Add(_sizeList = new ListBox { ItemsSource = sizes, Height = 120, SelectedItem = f.FontSize.ToString(CultureInfo.InvariantCulture) });
            System.Windows.Controls.Grid.SetColumn(c2, 1); System.Windows.Controls.Grid.SetColumn(c3, 2);
            g.Children.Add(c1); g.Children.Add(c2); g.Children.Add(c3);
            _fontList.SelectionChanged += (s, e) => { if (_fontList.SelectedItem is string n) { _fontBox.Text = n; Preview(); } };
            _sizeList.SelectionChanged += (s, e) => { if (_sizeList.SelectedItem is string n) { _sizeBox.Text = n; Preview(); } };
            _styleList.SelectionChanged += (s, e) => { ((TextBox)c2.Children[1]).Text = _styleList.SelectedItem as string; Preview(); };
            _fontList.Loaded += (s, e) => _fontList.ScrollIntoView(_fontList.SelectedItem ?? "");

            var bottom = new System.Windows.Controls.Grid { Margin = new Thickness(0, 8, 0, 0) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            bottom.ColumnDefinitions.Add(new ColumnDefinition());
            var b1 = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            b1.Children.Add(Label("Underline:"));
            _underline = Combo(new[] { "None", "Single", "Double", "Single Accounting", "Double Accounting" }, f.Underline switch { XLFontUnderlineValues.Single => 1, XLFontUnderlineValues.Double => 2, XLFontUnderlineValues.SingleAccounting => 3, XLFontUnderlineValues.DoubleAccounting => 4, _ => 0 });
            b1.Children.Add(_underline);
            b1.Children.Add(Label("Effects:", 4));
            b1.Children.Add(_strike = Chk("Strikethrough", f.Strikethrough));
            b1.Children.Add(_super = Chk("Superscript", f.VerticalAlignment == XLFontVerticalTextAlignmentValues.Superscript));
            b1.Children.Add(_sub = Chk("Subscript", f.VerticalAlignment == XLFontVerticalTextAlignmentValues.Subscript));
            var b2 = new StackPanel();
            b2.Children.Add(Label("Color:"));
            _fontColor = XlColors.ToWpf(f.FontColor, _wb, true);
            _fontColorBtn = new Border { Width = 100, Height = 22, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Background = new SolidColorBrush(_fontColor ?? Colors.Black), Cursor = System.Windows.Input.Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left };
            _fontColorBtn.MouseLeftButtonUp += (s, e) => ColorPicker.Show(_fontColorBtn, "Automatic", c => { _fontColor = c; _fontColorBtn.Background = new SolidColorBrush(c ?? Colors.Black); _changes.Add(x => x.Font.FontColor = c.HasValue ? XlColors.FromWpf(c.Value) : XLColor.FromTheme(XLThemeColor.Text1)); Preview(); });
            b2.Children.Add(_fontColorBtn);
            b2.Children.Add(Label("Preview:", 8));
            b2.Children.Add(new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Height = 60, Child = _fontPreview = new TextBlock { Text = "AaBbCcYyZz", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
            System.Windows.Controls.Grid.SetColumn(b2, 1);
            bottom.Children.Add(b1); bottom.Children.Add(b2);
            System.Windows.Controls.Grid.SetRow(bottom, 1); System.Windows.Controls.Grid.SetColumnSpan(bottom, 3);
            g.Children.Add(bottom);
            Preview();
            return g;
        }

        void Preview()
        {
            if (_fontPreview == null) return;
            try { _fontPreview.FontFamily = new FontFamily(_fontBox.Text); } catch { }
            if (double.TryParse(_sizeBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var sz)) _fontPreview.FontSize = Math.Clamp(Units.PtToPx(sz), 6, 40);
            int si = _styleList.SelectedIndex;
            _fontPreview.FontWeight = si >= 2 ? FontWeights.Bold : FontWeights.Normal;
            _fontPreview.FontStyle = si % 2 == 1 ? FontStyles.Italic : FontStyles.Normal;
            _fontPreview.Foreground = new SolidColorBrush(_fontColor ?? Colors.Black);
        }

        // ============================================================ Border
        UIElement BorderTab()
        {
            var sp = new StackPanel { Margin = new Thickness(8) };
            var styleList = new ListBox { Height = 150, Width = 110 };
            var styles = new[] { XLBorderStyleValues.Thin, XLBorderStyleValues.Hair, XLBorderStyleValues.Dotted, XLBorderStyleValues.Dashed, XLBorderStyleValues.DashDot, XLBorderStyleValues.Medium, XLBorderStyleValues.MediumDashed, XLBorderStyleValues.Thick, XLBorderStyleValues.Double };
            foreach (var s in styles)
            {
                var pen = s switch { XLBorderStyleValues.Medium or XLBorderStyleValues.MediumDashed => 2.0, XLBorderStyleValues.Thick => 3.0, _ => 1.0 };
                var line = new Line { X1 = 0, X2 = 80, Y1 = 6, Y2 = 6, Stroke = Brushes.Black, StrokeThickness = pen, Margin = new Thickness(2, 3, 2, 3) };
                if (s is XLBorderStyleValues.Dotted or XLBorderStyleValues.Hair) line.StrokeDashArray = new DoubleCollection { 1, 1 };
                if (s is XLBorderStyleValues.Dashed or XLBorderStyleValues.MediumDashed) line.StrokeDashArray = new DoubleCollection { 3, 1 };
                if (s == XLBorderStyleValues.DashDot) line.StrokeDashArray = new DoubleCollection { 3, 1, 1, 1 };
                styleList.Items.Add(s == XLBorderStyleValues.Double ? (object)new StackPanel { Children = { new Line { X1 = 0, X2 = 80, Stroke = Brushes.Black, Y1 = 3, Y2 = 3 }, new Line { X1 = 0, X2 = 80, Stroke = Brushes.Black, Y1 = 2, Y2 = 2 } } } : line);
            }
            styleList.SelectedIndex = 0;
            Color color = Colors.Black;
            var colorBtn = new Border { Width = 90, Height = 22, Background = Brushes.Black, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left };
            colorBtn.MouseLeftButtonUp += (s, e) => ColorPicker.Show(colorBtn, "Automatic", c => { color = c ?? Colors.Black; colorBtn.Background = new SolidColorBrush(color); });
            var left = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
            left.Children.Add(Heading("Line")); left.Children.Add(Label("Style:")); left.Children.Add(styleList);
            left.Children.Add(Label("Color:", 6)); left.Children.Add(colorBtn);
            XLBorderStyleValues Cur() => styles[Math.Max(0, styleList.SelectedIndex)];
            XLColor CurC() => XlColors.FromWpf(color);
            Button Preset(string text, Action<IXLRange> a)
            {
                var b = UI.Btn(text, (s, e) => BorderChanges.Add(a));
                b.Margin = new Thickness(0, 0, 6, 6);
                return b;
            }
            var right = new StackPanel();
            right.Children.Add(Heading("Presets"));
            right.Children.Add(Row(
                Preset("None", r => { r.Style.Border.OutsideBorder = XLBorderStyleValues.None; r.Style.Border.InsideBorder = XLBorderStyleValues.None; }),
                Preset("Outline", r => { var st = Cur(); var c = CurC(); r.Style.Border.OutsideBorder = st; r.Style.Border.OutsideBorderColor = c; }),
                Preset("Inside", r => { var st = Cur(); var c = CurC(); r.Style.Border.InsideBorder = st; r.Style.Border.InsideBorderColor = c; })));
            right.Children.Add(Heading("Border"));
            right.Children.Add(Row(
                Preset("Top", r => { r.FirstRow().Style.Border.TopBorder = Cur(); r.FirstRow().Style.Border.TopBorderColor = CurC(); }),
                Preset("Bottom", r => { r.LastRow().Style.Border.BottomBorder = Cur(); r.LastRow().Style.Border.BottomBorderColor = CurC(); }),
                Preset("Left", r => { r.FirstColumn().Style.Border.LeftBorder = Cur(); r.FirstColumn().Style.Border.LeftBorderColor = CurC(); }),
                Preset("Right", r => { r.LastColumn().Style.Border.RightBorder = Cur(); r.LastColumn().Style.Border.RightBorderColor = CurC(); })));
            right.Children.Add(Row(
                Preset("Diagonal Up", r => { r.Style.Border.DiagonalUp = true; r.Style.Border.DiagonalBorder = Cur(); r.Style.Border.DiagonalBorderColor = CurC(); }),
                Preset("Diagonal Down", r => { r.Style.Border.DiagonalDown = true; r.Style.Border.DiagonalBorder = Cur(); r.Style.Border.DiagonalBorderColor = CurC(); })));
            right.Children.Add(new TextBlock { Text = "Choose a line style and colour, then click the presets or borders to apply them to the selected cells. They're applied when you click OK.", TextWrapping = TextWrapping.Wrap, Width = 260, Foreground = Brushes.DimGray, Margin = new Thickness(0, 10, 0, 0) });
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(left); row.Children.Add(right);
            sp.Children.Add(row);
            return sp;
        }

        // ============================================================ Fill
        UIElement FillTab(IXLStyle st)
        {
            var sp = new StackPanel { Margin = new Thickness(8) };
            _fill = SheetCanvas.FillOf(st, _wb);
            sp.Children.Add(Label("Background Color:"));
            var none = UI.Btn("No Color", (s, e) => { _fill = null; _fillTouched = true; _fillSample.Background = Brushes.White; });
            none.HorizontalAlignment = HorizontalAlignment.Left; none.Margin = new Thickness(0, 0, 0, 6);
            sp.Children.Add(none);
            var wrap = new WrapPanel { Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
            var all = XlColors.ThemeRow.ToList();
            foreach (var t in new[] { 0.8, 0.6, 0.4, -0.25, -0.5 }) all.AddRange(XlColors.ThemeRow.Select(c => XlColors.Shade(c, c == Colors.White ? -0.05 - (0.8 - t) * 0.3 : t)));
            all.AddRange(XlColors.Standard);
            foreach (var c in all)
            {
                var col = c;
                var b = new Border { Width = 18, Height = 18, Margin = new Thickness(1), Background = new SolidColorBrush(c), BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand };
                b.MouseLeftButtonUp += (s, e) => { _fill = col; _fillTouched = true; _fillSample.Background = new SolidColorBrush(col); };
                wrap.Children.Add(b);
            }
            sp.Children.Add(wrap);
            var more = UI.Btn("More Colors...", (s, e) =>
            {
                using var cd = new System.Windows.Forms.ColorDialog { FullOpen = true };
                if (cd.ShowDialog() == System.Windows.Forms.DialogResult.OK) { _fill = Color.FromRgb(cd.Color.R, cd.Color.G, cd.Color.B); _fillTouched = true; _fillSample.Background = new SolidColorBrush(_fill.Value); }
            });
            more.HorizontalAlignment = HorizontalAlignment.Left; more.Margin = new Thickness(0, 6, 0, 6);
            sp.Children.Add(more);
            sp.Children.Add(Label("Sample", 6));
            sp.Children.Add(_fillSample = new Border { Height = 40, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Background = new SolidColorBrush(_fill ?? Colors.White) });
            return sp;
        }

        // ============================================================ Protection
        UIElement ProtectionTab(IXLStyle st)
        {
            var sp = new StackPanel { Margin = new Thickness(8) };
            sp.Children.Add(_locked = Chk("Locked", st.Protection.Locked));
            sp.Children.Add(_hidden = Chk("Hidden", st.Protection.Hidden));
            sp.Children.Add(new TextBlock { Text = "Locking cells or hiding formulas has no effect until you protect the worksheet (Review tab, Changes group, Protect Sheet button).", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
            return sp;
        }

        // ============================================================ collect
        bool Collect()
        {
            var st = _cell.Style;
            if (_numTouched) { var f = _numFmt; _changes.Add(x => x.NumberFormat.Format = f); }
            // alignment
            if (int.TryParse(_indent.Text, out var ind) && ind != st.Alignment.Indent) _changes.Add(x => x.Alignment.Indent = Math.Clamp(ind, 0, 15));
            if (int.TryParse(_rotation.Text, out var rot) && rot != st.Alignment.TextRotation && (rot is >= -90 and <= 90 || rot == 255)) _changes.Add(x => x.Alignment.TextRotation = rot < 0 ? 90 - rot : rot);
            if (_wrap.IsChecked != st.Alignment.WrapText) { bool w = _wrap.IsChecked == true; _changes.Add(x => x.Alignment.WrapText = w); }
            if (_shrink.IsChecked != st.Alignment.ShrinkToFit) { bool w = _shrink.IsChecked == true; _changes.Add(x => x.Alignment.ShrinkToFit = w); }
            MergeRequested = _merge.IsChecked == true && !_cell.IsMerged() ? true : _merge.IsChecked == false && _cell.IsMerged() ? false : (bool?)null;
            // font
            var f0 = st.Font;
            if (_fontBox.Text != f0.FontName && !string.IsNullOrWhiteSpace(_fontBox.Text)) { var n = _fontBox.Text; _changes.Add(x => x.Font.FontName = n); }
            if (double.TryParse(_sizeBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var sz) && Math.Abs(sz - f0.FontSize) > 0.01 && sz >= 1 && sz <= 409) _changes.Add(x => x.Font.FontSize = sz);
            int si = _styleList.SelectedIndex;
            bool bold = si >= 2, italic = si % 2 == 1;
            if (bold != f0.Bold) _changes.Add(x => x.Font.Bold = bold);
            if (italic != f0.Italic) _changes.Add(x => x.Font.Italic = italic);
            var u = new[] { XLFontUnderlineValues.None, XLFontUnderlineValues.Single, XLFontUnderlineValues.Double, XLFontUnderlineValues.SingleAccounting, XLFontUnderlineValues.DoubleAccounting }[Math.Max(0, _underline.SelectedIndex)];
            if (u != f0.Underline) _changes.Add(x => x.Font.Underline = u);
            if (_strike.IsChecked != f0.Strikethrough) { bool b = _strike.IsChecked == true; _changes.Add(x => x.Font.Strikethrough = b); }
            var va = _super.IsChecked == true ? XLFontVerticalTextAlignmentValues.Superscript : _sub.IsChecked == true ? XLFontVerticalTextAlignmentValues.Subscript : XLFontVerticalTextAlignmentValues.Baseline;
            if (va != f0.VerticalAlignment) _changes.Add(x => x.Font.VerticalAlignment = va);
            // fill
            if (_fillTouched)
            {
                var c = _fill;
                _changes.Add(x => { if (c.HasValue) { x.Fill.PatternType = XLFillPatternValues.Solid; x.Fill.BackgroundColor = XlColors.FromWpf(c.Value); } else x.Fill.PatternType = XLFillPatternValues.None; });
            }
            // protection
            if (_locked.IsChecked != st.Protection.Locked) { bool l = _locked.IsChecked == true; _changes.Add(x => x.Protection.Locked = l); }
            if (_hidden.IsChecked != st.Protection.Hidden) { bool h = _hidden.IsChecked == true; _changes.Add(x => x.Protection.Hidden = h); }
            if (MergeRequested.HasValue) { bool m = MergeRequested.Value; BorderChanges.Add(r => { if (m) r.Merge(); else r.Unmerge(); }); }
            return true;
        }

        public bool? MergeRequested;
    }
}
