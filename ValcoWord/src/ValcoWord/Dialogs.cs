using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ValcoWord
{
    static class D
    {
        public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static double? ParseNum(string s)
        {
            if (s == null) return null;
            s = s.Trim().TrimEnd('"', '\'').Replace("in", "").Replace("pt", "").Replace("%", "").Replace(",", ".").Trim();
            return double.TryParse(s, NumberStyles.Float, Inv, out var d) ? d : (double?)null;
        }
        public static string In(double v) => v.ToString("0.##", Inv) + "\"";
        public static string Pt(double v) => v.ToString("0.#", Inv) + " pt";

        public static TextBox Box(string text, double width = 70) =>
            new TextBox { Text = text, Width = width, Height = 23, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };

        public static Grid Form(params (string Label, UIElement Control)[] rows)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < rows.Length; i++)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                if (rows[i].Label != null)
                {
                    var l = new TextBlock { Text = rows[i].Label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                    Grid.SetRow(l, i);
                    g.Children.Add(l);
                }
                var c = rows[i].Control;
                Grid.SetRow(c, i);
                Grid.SetColumn(c, rows[i].Label == null ? 0 : 1);
                if (rows[i].Label == null) Grid.SetColumnSpan(c, 2);
                if (c is FrameworkElement fe && fe.HorizontalAlignment == HorizontalAlignment.Stretch && !(c is Panel)) fe.HorizontalAlignment = HorizontalAlignment.Left;
                g.Children.Add(c);
            }
            return g;
        }

        public static GroupBox Group(string header, UIElement content) =>
            new GroupBox { Header = header, Content = content, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 8) };

        public static TextBlock SectionTitle(string text) => new TextBlock
        {
            Text = text, FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x33, 0x99)), Margin = new Thickness(0, 0, 0, 8)
        };
    }

    public class DialogBase : VistaWindow
    {
        public DialogBase(Window owner, string title, double width = 0)
        {
            Title = title;
            if (owner != null && owner.IsVisible) Owner = owner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            if (width > 0) { Width = width; SizeToContent = SizeToContent.Height; }
            else SizeToContent = SizeToContent.WidthAndHeight;
        }

        protected void Build(UIElement content, params Button[] buttons) => Content = UI.DialogLayout(content, UI.Strip(buttons));
        protected Button Ok(Func<bool> validate = null, string text = "OK") => UI.Btn(text, (s, e) => { if (validate == null || validate()) DialogResult = true; }, true);
        protected Button Cancel(string text = "Cancel") => UI.Btn(text, null, false, true);
    }

    // =====================================================================================
    public class FindReplaceDialog : VistaWindow
    {
        readonly MainWindow _main;
        readonly TextBox _find, _replace;
        readonly CheckBox _case, _whole;
        readonly RadioButton _up;
        readonly StackPanel _replaceRow;
        readonly Button _btnReplace, _btnAll;
        readonly TextBlock _msg;

        public string Query => _find.Text;
        public bool MatchCase => _case.IsChecked == true;
        public bool WholeWord => _whole.IsChecked == true;
        public bool Up => _up.IsChecked == true;

        public FindReplaceDialog(MainWindow main, bool replace, string initial)
        {
            _main = main;
            Owner = main;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = 470;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _find = D.Box(initial ?? "", 270);
            _replace = D.Box("", 270);
            _case = new CheckBox { Content = "Match _case", Margin = new Thickness(0, 6, 0, 2) };
            _whole = new CheckBox { Content = "Find whole words _only", Margin = new Thickness(0, 2, 0, 2) };
            _up = new RadioButton { Content = "_Up", Margin = new Thickness(0, 0, 10, 0) };
            var down = new RadioButton { Content = "_Down", IsChecked = true };
            var dir = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            dir.Children.Add(new TextBlock { Text = "Direction:", Margin = new Thickness(0, 0, 10, 0) });
            dir.Children.Add(_up); dir.Children.Add(down);
            _msg = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x33, 0x99)), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };

            var findRow = new StackPanel { Orientation = Orientation.Horizontal };
            findRow.Children.Add(new TextBlock { Text = "Fi_nd what:", Width = 85, VerticalAlignment = VerticalAlignment.Center });
            findRow.Children.Add(_find);
            _replaceRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            _replaceRow.Children.Add(new TextBlock { Text = "Re_place with:", Width = 85, VerticalAlignment = VerticalAlignment.Center });
            _replaceRow.Children.Add(_replace);
            var sp = new StackPanel();
            sp.Children.Add(findRow); sp.Children.Add(_replaceRow);
            sp.Children.Add(_case); sp.Children.Add(_whole); sp.Children.Add(dir); sp.Children.Add(_msg);

            var btnFind = UI.Btn("_Find Next", (s, e) => Report(_main.FindNext(Query, MatchCase, WholeWord, Up)), true);
            _btnReplace = UI.Btn("_Replace", (s, e) => Report(_main.ReplaceOne(Query, _replace.Text, MatchCase, WholeWord, Up)));
            _btnAll = UI.Btn("Replace _All", (s, e) =>
            {
                int n = _main.ReplaceAll(Query, _replace.Text, MatchCase, WholeWord);
                _msg.Text = n == 0 ? "No matches were found." : $"Done. Valco Word made {n} replacement{(n == 1 ? "" : "s")}.";
            });
            var close = UI.Btn("Close", (s, e) => Close(), false, true);
            Content = UI.DialogLayout(sp, UI.Strip(btnFind, _btnReplace, _btnAll, close));
            SetMode(replace);
            Loaded += (s, e) => { _find.Focus(); _find.SelectAll(); };
        }

        void Report(string msg) => _msg.Text = msg ?? "";

        public void SetMode(bool replace)
        {
            Title = replace ? "Replace" : "Find";
            var v = replace ? Visibility.Visible : Visibility.Collapsed;
            _replaceRow.Visibility = v; _btnReplace.Visibility = v; _btnAll.Visibility = v;
        }

        public void FocusFind(string text)
        {
            if (!string.IsNullOrEmpty(text)) _find.Text = text;
            Activate();
            _find.Focus();
            _find.SelectAll();
        }
    }

    // =====================================================================================
    public class FontResult
    {
        public string Family; public double? SizePt; public bool? Bold, Italic, Underline, Strike, Superscript, Subscript; public Color? Color;
    }

    public class FontDialog : DialogBase
    {
        readonly TextBox _fam, _style, _size;
        readonly ListBox _famList, _styleList, _sizeList;
        readonly CheckBox _u, _s, _sup, _sub;
        readonly TextBlock _preview;
        Color? _color;
        readonly Rectangle _swatch;
        public FontResult Result { get; private set; }

        public FontDialog(Window owner, FontResult cur) : base(owner, "Font")
        {
            var fonts = Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().OrderBy(f => f).ToList();
            _fam = D.Box(cur.Family ?? "", 190);
            _famList = new ListBox { Height = 140, Width = 190, ItemsSource = fonts };
            _famList.ItemTemplate = FontItemTemplate();
            VirtualizingPanel.SetIsVirtualizing(_famList, true);
            _style = D.Box(StyleName(cur), 120);
            _styleList = new ListBox { Height = 140, Width = 120, ItemsSource = new[] { "Regular", "Italic", "Bold", "Bold Italic" } };
            _size = D.Box(cur.SizePt?.ToString("0.#", D.Inv) ?? "", 60);
            _sizeList = new ListBox { Height = 140, Width = 60, ItemsSource = MainWindow.Sizes.Select(x => x.ToString(D.Inv)).ToList() };

            _famList.SelectionChanged += (s, e) => { if (_famList.SelectedItem is string f) { _fam.Text = f; UpdatePreview(); } };
            _styleList.SelectionChanged += (s, e) => { if (_styleList.SelectedItem is string f) { _style.Text = f; UpdatePreview(); } };
            _sizeList.SelectionChanged += (s, e) => { if (_sizeList.SelectedItem is string f) { _size.Text = f; UpdatePreview(); } };
            _fam.TextChanged += (s, e) =>
            {
                var m = fonts.FirstOrDefault(f => f.StartsWith(_fam.Text, StringComparison.OrdinalIgnoreCase));
                if (m != null) _famList.ScrollIntoView(m);
                UpdatePreview();
            };
            _size.TextChanged += (s, e) => UpdatePreview();
            if (cur.Family != null && fonts.Contains(cur.Family)) { _famList.SelectedItem = cur.Family; Dispatcher.BeginInvoke(new Action(() => _famList.ScrollIntoView(cur.Family))); }
            _styleList.SelectedItem = _style.Text == "" ? null : _style.Text;

            StackPanel Col(string label, TextBox box, ListBox list)
            {
                var sp = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
                sp.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 2) });
                sp.Children.Add(box); sp.Children.Add(list);
                return sp;
            }
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            top.Children.Add(Col("_Font:", _fam, _famList));
            top.Children.Add(Col("Font st_yle:", _style, _styleList));
            top.Children.Add(Col("_Size:", _size, _sizeList));

            _u = new CheckBox { Content = "_Underline", IsChecked = cur.Underline, IsThreeState = cur.Underline == null, Margin = new Thickness(0, 2, 18, 2) };
            _s = new CheckBox { Content = "Stri_kethrough", IsChecked = cur.Strike, IsThreeState = cur.Strike == null, Margin = new Thickness(0, 2, 18, 2) };
            _sup = new CheckBox { Content = "Su_perscript", IsChecked = cur.Superscript, IsThreeState = cur.Superscript == null, Margin = new Thickness(0, 2, 18, 2) };
            _sub = new CheckBox { Content = "Su_bscript", IsChecked = cur.Subscript, IsThreeState = cur.Subscript == null, Margin = new Thickness(0, 2, 18, 2) };
            _sup.Checked += (s, e) => _sub.IsChecked = false;
            _sub.Checked += (s, e) => _sup.IsChecked = false;
            foreach (var c in new[] { _u, _s, _sup, _sub }) { c.Checked += (s, e) => UpdatePreview(); c.Unchecked += (s, e) => UpdatePreview(); }
            var effects = new WrapPanel();
            effects.Children.Add(_u); effects.Children.Add(_s); effects.Children.Add(_sup); effects.Children.Add(_sub);

            _color = cur.Color;
            _swatch = new Rectangle { Width = 40, Height = 14, Fill = new SolidColorBrush(cur.Color ?? Colors.Black), Stroke = Brushes.Gray, Margin = new Thickness(0, 0, 6, 0) };
            var colorBtn = new Button { Style = (Style)FindResource("DialogButton"), Margin = new Thickness(0) };
            var cbsp = new StackPanel { Orientation = Orientation.Horizontal };
            cbsp.Children.Add(_swatch); cbsp.Children.Add(new TextBlock { Text = "Font c_olor..." });
            colorBtn.Content = cbsp;
            colorBtn.Click += (s, e) => ColorPalette.ShowText(colorBtn, "Automatic", c => { _color = c ?? Colors.Black; _swatch.Fill = new SolidColorBrush(_color.Value); UpdatePreview(); });
            var colorRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            colorRow.Children.Add(colorBtn);

            _preview = new TextBlock { Text = "AaBbYyZz — The quick brown fox", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var prevBox = new Border { Height = 64, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Background = Brushes.White, Child = _preview, Margin = new Thickness(0, 6, 0, 0), ClipToBounds = true };

            var sp = new StackPanel { Width = 420 };
            sp.Children.Add(top);
            sp.Children.Add(D.Group("Effects", effects));
            sp.Children.Add(colorRow);
            sp.Children.Add(new TextBlock { Text = "Preview", Margin = new Thickness(0, 8, 0, 0) });
            sp.Children.Add(prevBox);
            Build(sp, Ok(Accept), Cancel());
            UpdatePreview();
        }

        public static DataTemplate FontItemTemplate()
        {
            var t = new DataTemplate();
            var f = new FrameworkElementFactory(typeof(TextBlock));
            f.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
            f.SetBinding(TextBlock.FontFamilyProperty, new System.Windows.Data.Binding());
            f.SetValue(TextBlock.FontSizeProperty, 13.0);
            t.VisualTree = f;
            return t;
        }

        static string StyleName(FontResult c)
        {
            if (c.Bold == null || c.Italic == null) return "";
            return c.Bold == true ? (c.Italic == true ? "Bold Italic" : "Bold") : (c.Italic == true ? "Italic" : "Regular");
        }

        void UpdatePreview()
        {
            if (_preview == null) return;
            try { if (!string.IsNullOrWhiteSpace(_fam.Text)) _preview.FontFamily = new FontFamily(_fam.Text); } catch { }
            var sz = D.ParseNum(_size.Text);
            if (sz.HasValue && sz > 0 && sz <= 1638) _preview.FontSize = Math.Min(sz.Value * Fmt.PxPerPt, 40);
            var st = _style.Text;
            _preview.FontWeight = st.Contains("Bold") ? FontWeights.Bold : FontWeights.Normal;
            _preview.FontStyle = st.Contains("Italic") ? FontStyles.Italic : FontStyles.Normal;
            var deco = new TextDecorationCollection();
            if (_u?.IsChecked == true) deco.Add(TextDecorations.Underline[0]);
            if (_s?.IsChecked == true) deco.Add(TextDecorations.Strikethrough[0]);
            _preview.TextDecorations = deco;
            _preview.Foreground = new SolidColorBrush(_color ?? Colors.Black);
        }

        bool Accept()
        {
            var r = new FontResult();
            if (!string.IsNullOrWhiteSpace(_fam.Text)) r.Family = _fam.Text.Trim();
            if (!string.IsNullOrWhiteSpace(_size.Text))
            {
                var sz = D.ParseNum(_size.Text);
                if (!sz.HasValue || sz < 1 || sz > 1638) { VistaDialog.Show(this, "The size must be a number between 1 and 1638.", "Font", MessageBoxButton.OK, DlgIcon.Warning); return false; }
                r.SizePt = sz;
            }
            var st = _style.Text;
            if (st != "") { r.Bold = st.Contains("Bold"); r.Italic = st.Contains("Italic"); }
            r.Underline = _u.IsChecked; r.Strike = _s.IsChecked; r.Superscript = _sup.IsChecked; r.Subscript = _sub.IsChecked;
            r.Color = _color;
            Result = r;
            return true;
        }
    }

    // =====================================================================================
    public class ParaResult
    {
        public TextAlignment Align; public double LeftPx, RightPx, FirstPx, BeforePt, AfterPt; public double LineFactor = 1; public double? ExactPt; public bool PageBreakBefore;
    }

    public class ParagraphDialog : DialogBase
    {
        readonly ComboBox _align, _special, _line;
        readonly TextBox _left, _right, _by, _before, _after, _at;
        readonly CheckBox _pb;
        public ParaResult Result { get; private set; }

        public ParagraphDialog(Window owner, ParaResult cur) : base(owner, "Paragraph", 430)
        {
            _align = new ComboBox { Width = 120, ItemsSource = new[] { "Left", "Centered", "Right", "Justified" }, SelectedIndex = (int)AlignIndex(cur.Align) };
            _left = D.Box(D.In(cur.LeftPx / 96));
            _right = D.Box(D.In(cur.RightPx / 96));
            _special = new ComboBox { Width = 110, ItemsSource = new[] { "(none)", "First line", "Hanging" }, SelectedIndex = cur.FirstPx > 0.5 ? 1 : cur.FirstPx < -0.5 ? 2 : 0 };
            _by = D.Box(D.In(Math.Abs(cur.FirstPx) / 96));
            _before = D.Box(D.Pt(cur.BeforePt));
            _after = D.Box(D.Pt(cur.AfterPt));
            _line = new ComboBox { Width = 110, ItemsSource = new[] { "Single", "1.15", "1.5 lines", "Double", "Multiple", "Exactly" } };
            _at = D.Box("");
            if (cur.ExactPt.HasValue) { _line.SelectedIndex = 5; _at.Text = D.Pt(cur.ExactPt.Value); }
            else
            {
                var f = cur.LineFactor;
                _line.SelectedIndex = Math.Abs(f - 1) < .01 ? 0 : Math.Abs(f - 1.15) < .01 ? 1 : Math.Abs(f - 1.5) < .01 ? 2 : Math.Abs(f - 2) < .01 ? 3 : 4;
                _at.Text = f.ToString("0.##", D.Inv);
            }
            _line.SelectionChanged += (s, e) =>
            {
                _at.Text = _line.SelectedIndex switch { 0 => "1", 1 => "1.15", 2 => "1.5", 3 => "2", 4 => "3", 5 => "12 pt", _ => _at.Text };
            };
            _pb = new CheckBox { Content = "Page _break before", IsChecked = cur.PageBreakBefore, Margin = new Thickness(0, 4, 0, 0) };

            var indRow2 = new StackPanel { Orientation = Orientation.Horizontal };
            indRow2.Children.Add(_special); indRow2.Children.Add(new TextBlock { Text = "  By: ", VerticalAlignment = VerticalAlignment.Center }); indRow2.Children.Add(_by);
            var lineRow = new StackPanel { Orientation = Orientation.Horizontal };
            lineRow.Children.Add(_line); lineRow.Children.Add(new TextBlock { Text = "  At: ", VerticalAlignment = VerticalAlignment.Center }); lineRow.Children.Add(_at);

            var sp = new StackPanel();
            sp.Children.Add(D.Group("General", D.Form(("Alignment:", _align))));
            sp.Children.Add(D.Group("Indentation", D.Form(("Left:", _left), ("Right:", _right), ("Special:", indRow2))));
            sp.Children.Add(D.Group("Spacing", D.Form(("Before:", _before), ("After:", _after), ("Line spacing:", lineRow))));
            sp.Children.Add(D.Group("Pagination", _pb));
            Build(sp, Ok(Accept), Cancel());
        }

        static int AlignIndex(TextAlignment a) => a switch { TextAlignment.Center => 1, TextAlignment.Right => 2, TextAlignment.Justify => 3, _ => 0 };

        bool Accept()
        {
            double? l = D.ParseNum(_left.Text), r = D.ParseNum(_right.Text), by = D.ParseNum(_by.Text), b = D.ParseNum(_before.Text), a = D.ParseNum(_after.Text), at = D.ParseNum(_at.Text);
            if (l == null || r == null || by == null || b == null || a == null || (at == null && _line.SelectedIndex >= 4))
            {
                VistaDialog.Show(this, "Please enter numbers for the indents (in inches) and spacing (in points).", "Paragraph", MessageBoxButton.OK, DlgIcon.Warning);
                return false;
            }
            var res = new ParaResult
            {
                Align = _align.SelectedIndex switch { 1 => TextAlignment.Center, 2 => TextAlignment.Right, 3 => TextAlignment.Justify, _ => TextAlignment.Left },
                LeftPx = Math.Max(0, l.Value) * 96, RightPx = Math.Max(0, r.Value) * 96,
                FirstPx = _special.SelectedIndex == 1 ? by.Value * 96 : _special.SelectedIndex == 2 ? -by.Value * 96 : 0,
                BeforePt = Math.Max(0, b.Value), AfterPt = Math.Max(0, a.Value), PageBreakBefore = _pb.IsChecked == true
            };
            switch (_line.SelectedIndex)
            {
                case 0: res.LineFactor = 1; break;
                case 1: res.LineFactor = 1.15; break;
                case 2: res.LineFactor = 1.5; break;
                case 3: res.LineFactor = 2; break;
                case 4: res.LineFactor = Math.Max(0.5, Math.Min(10, at.Value)); break;
                case 5: res.ExactPt = Math.Max(1, at.Value); break;
            }
            if (res.FirstPx < 0 && res.LeftPx < -res.FirstPx) res.LeftPx = -res.FirstPx;
            Result = res;
            return true;
        }
    }

    // =====================================================================================
    public class PageSetupDialog : DialogBase
    {
        readonly ComboBox _paper;
        readonly RadioButton _portrait, _landscape;
        readonly TextBox _t, _b, _l, _r, _header;
        readonly CheckBox _numbers;
        readonly Canvas _preview;
        public PageSettings Result { get; private set; }

        public PageSetupDialog(Window owner, PageSettings cur) : base(owner, "Page Setup", 460)
        {
            _paper = new ComboBox { Width = 200, ItemsSource = PageSettings.Papers.Select(p => $"{p.Name}  ({p.W.ToString("0.##", D.Inv)}\" x {p.H.ToString("0.##", D.Inv)}\")").ToList() };
            _paper.SelectedIndex = Math.Max(0, Array.FindIndex(PageSettings.Papers, p => p.Name == cur.Paper));
            _portrait = new RadioButton { Content = "P_ortrait", IsChecked = !cur.Landscape, Margin = new Thickness(0, 0, 16, 0) };
            _landscape = new RadioButton { Content = "L_andscape", IsChecked = cur.Landscape };
            _t = D.Box(D.In(cur.MarginTop)); _b = D.Box(D.In(cur.MarginBottom)); _l = D.Box(D.In(cur.MarginLeft)); _r = D.Box(D.In(cur.MarginRight));
            _header = D.Box(cur.HeaderText ?? "", 260);
            _numbers = new CheckBox { Content = "Show _page numbers at the bottom", IsChecked = cur.PageNumbers, Margin = new Thickness(0, 4, 0, 0) };
            _preview = new Canvas { Width = 120, Height = 120 };

            var orient = new StackPanel { Orientation = Orientation.Horizontal };
            orient.Children.Add(_portrait); orient.Children.Add(_landscape);
            var margins = new Grid();
            for (int i = 0; i < 4; i++) margins.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            margins.RowDefinitions.Add(new RowDefinition()); margins.RowDefinitions.Add(new RowDefinition());
            void Put(string label, TextBox box, int r, int c)
            {
                var tb = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(c == 0 ? 0 : 16, 0, 8, 0) };
                Grid.SetRow(tb, r); Grid.SetColumn(tb, c); margins.Children.Add(tb);
                Grid.SetRow(box, r); Grid.SetColumn(box, c + 1); margins.Children.Add(box);
            }
            Put("_Top:", _t, 0, 0); Put("_Bottom:", _b, 0, 2); Put("_Left:", _l, 1, 0); Put("_Right:", _r, 1, 2);

            var left = new StackPanel();
            left.Children.Add(D.Group("Paper", D.Form(("Size:", _paper))));
            left.Children.Add(D.Group("Orientation", orient));
            left.Children.Add(D.Group("Margins (inches)", margins));
            left.Children.Add(D.Group("Header and footer", D.Form(("Header text:", _header), (null, _numbers))));
            var row = new DockPanel();
            var prevBox = new Border { Child = _preview, Margin = new Thickness(12, 8, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            DockPanel.SetDock(prevBox, Dock.Right);
            row.Children.Add(prevBox);
            row.Children.Add(left);

            foreach (var tb in new[] { _t, _b, _l, _r }) tb.TextChanged += (s, e) => DrawPreview();
            _paper.SelectionChanged += (s, e) => DrawPreview();
            _portrait.Checked += (s, e) => DrawPreview();
            _landscape.Checked += (s, e) => DrawPreview();

            var def = UI.Btn("Set As _Default", (s, e) =>
            {
                var p = Collect();
                if (p == null) return;
                if (VistaDialog.Confirm(this, "Use these page settings for all new documents?"))
                { Settings.Current.Page = p.Clone(); Settings.Save(); }
            });
            Build(row, def, Ok(() => (Result = Collect()) != null), Cancel());
            Loaded += (s, e) => DrawPreview();
        }

        PageSettings Collect()
        {
            double? t = D.ParseNum(_t.Text), b = D.ParseNum(_b.Text), l = D.ParseNum(_l.Text), r = D.ParseNum(_r.Text);
            var p = new PageSettings
            {
                Paper = PageSettings.Papers[Math.Max(0, _paper.SelectedIndex)].Name, Landscape = _landscape.IsChecked == true,
                HeaderText = _header.Text, PageNumbers = _numbers.IsChecked == true
            };
            if (t == null || b == null || l == null || r == null || t < 0 || b < 0 || l < 0 || r < 0 ||
                l + r > p.WidthIn - 1 || t + b > p.HeightIn - 1)
            {
                VistaDialog.Show(this, "The margins don't fit on this paper size. Check that each margin is a number of inches and leaves at least an inch of space for text.", "Page Setup", MessageBoxButton.OK, DlgIcon.Warning);
                return null;
            }
            p.MarginTop = t.Value; p.MarginBottom = b.Value; p.MarginLeft = l.Value; p.MarginRight = r.Value;
            return p;
        }

        void DrawPreview()
        {
            if (_preview == null || _paper == null) return;
            _preview.Children.Clear();
            var paper = PageSettings.Papers[Math.Max(0, _paper.SelectedIndex)];
            double w = _landscape.IsChecked == true ? paper.H : paper.W, h = _landscape.IsChecked == true ? paper.W : paper.H;
            double scale = 110 / Math.Max(w, h);
            double pw = w * scale, ph = h * scale, x0 = (120 - pw) / 2, y0 = (120 - ph) / 2;
            var sheet = new Rectangle { Width = pw, Height = ph, Fill = Brushes.White, Stroke = Brushes.Gray, Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 2, Opacity = 0.3, BlurRadius = 3 } };
            Canvas.SetLeft(sheet, x0); Canvas.SetTop(sheet, y0); _preview.Children.Add(sheet);
            double t = (D.ParseNum(_t.Text) ?? 1) * scale, b = (D.ParseNum(_b.Text) ?? 1) * scale, l = (D.ParseNum(_l.Text) ?? 1) * scale, r = (D.ParseNum(_r.Text) ?? 1) * scale;
            double bw = pw - l - r, bh = ph - t - b;
            if (bw <= 2 || bh <= 2) return;
            for (double y = 0; y < bh - 2; y += 4)
            {
                var line = new Rectangle { Width = y + 8 > bh ? bw * 0.6 : bw, Height = 1.5, Fill = new SolidColorBrush(Color.FromRgb(0x9A, 0xAE, 0xC6)) };
                Canvas.SetLeft(line, x0 + l); Canvas.SetTop(line, y0 + t + y); _preview.Children.Add(line);
            }
        }
    }

    // =====================================================================================
    public class InsertTableDialog : DialogBase
    {
        readonly TextBox _rows, _cols;
        readonly CheckBox _borders;
        public int Rows, Cols; public bool Borders;

        public InsertTableDialog(Window owner) : base(owner, "Insert Table", 300)
        {
            _cols = D.Box("3", 60); _rows = D.Box("3", 60);
            _borders = new CheckBox { Content = "Show _borders", IsChecked = true, Margin = new Thickness(0, 6, 0, 0) };
            var sp = new StackPanel();
            sp.Children.Add(D.Group("Table size", D.Form(("Number of _columns:", _cols), ("Number of _rows:", _rows))));
            sp.Children.Add(_borders);
            Build(sp, Ok(() =>
            {
                if (!int.TryParse(_rows.Text, out Rows) || !int.TryParse(_cols.Text, out Cols) || Rows < 1 || Cols < 1 || Rows > 500 || Cols > 63)
                { VistaDialog.Show(this, "Enter between 1 and 63 columns and between 1 and 500 rows.", "Insert Table", MessageBoxButton.OK, DlgIcon.Warning); return false; }
                Borders = _borders.IsChecked == true;
                return true;
            }), Cancel());
            Loaded += (s, e) => { _cols.Focus(); _cols.SelectAll(); };
        }
    }

    // =====================================================================================
    public class HyperlinkDialog : DialogBase
    {
        readonly TextBox _text, _url;
        public string LinkText => _text.Text;
        public string Url { get; private set; }
        public bool Remove { get; private set; }

        public HyperlinkDialog(Window owner, string text, string url, bool editing, bool textEditable) : base(owner, editing ? "Edit Hyperlink" : "Insert Hyperlink", 440)
        {
            _text = D.Box(text ?? "", 300); _text.IsEnabled = textEditable;
            _url = D.Box(url ?? "https://", 300);
            var sp = new StackPanel();
            sp.Children.Add(D.Form(("_Text to display:", _text), ("_Address:", _url)));
            sp.Children.Add(new TextBlock { Text = "Tip: hold Ctrl and click a link in the document to open it.", Foreground = Brushes.Gray, Margin = new Thickness(0, 8, 0, 0) });
            var buttons = new List<Button>();
            if (editing) buttons.Add(UI.Btn("_Remove Link", (s, e) => { Remove = true; DialogResult = true; }));
            buttons.Add(Ok(() =>
            {
                var u = _url.Text.Trim();
                if (u.Length > 0 && !u.Contains("://") && !u.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                    u = u.Contains("@") && !u.Contains("/") ? "mailto:" + u : "https://" + u;
                if (!Uri.TryCreate(u, UriKind.Absolute, out _))
                { VistaDialog.Show(this, "That doesn't look like a web address.", Title, MessageBoxButton.OK, DlgIcon.Warning); return false; }
                Url = u;
                if (string.IsNullOrWhiteSpace(_text.Text)) _text.Text = u;
                return true;
            }));
            buttons.Add(Cancel());
            Build(sp, buttons.ToArray());
            Loaded += (s, e) => { (textEditable && string.IsNullOrEmpty(text) ? _text : _url).Focus(); _url.SelectAll(); };
        }
    }

    // =====================================================================================
    public class DateTimeDialog : DialogBase
    {
        readonly ListBox _list;
        public string Value => _list.SelectedItem as string;

        public DateTimeDialog(Window owner) : base(owner, "Date and Time", 320)
        {
            var now = DateTime.Now;
            var fmts = new[] { "d", "D", "MMMM d, yyyy", "dddd, MMMM d, yyyy", "yyyy-MM-dd", "d MMMM yyyy", "MMM. d, yy", "MMMM yyyy", "t", "T", "g", "yyyy-MM-dd HH:mm" };
            _list = new ListBox { Height = 240, ItemsSource = fmts.Select(f => now.ToString(f, CultureInfo.CurrentCulture)).Distinct().ToList(), SelectedIndex = 2 };
            _list.MouseDoubleClick += (s, e) => { if (Value != null) DialogResult = true; };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = "_Available formats:", Margin = new Thickness(0, 0, 0, 4) });
            sp.Children.Add(_list);
            Build(sp, Ok(() => Value != null), Cancel());
            Loaded += (s, e) => _list.Focus();
        }
    }

    // =====================================================================================
    public class SymbolDialog : DialogBase
    {
        public event Action<string> Insert;
        static readonly string Symbols =
            "©®™§¶†‡•…‰′″‹›«»“”‘’–—¡¿€£¥¢¤°±×÷≠≈≤≥∞√∑∏∫∂∆∇µπΩαβγδεθλσφψω" +
            "←↑→↓↔↕⇐⇒⇔↵✓✔✗✘★☆♠♣♥♦♪♫☺☹☀☁☂☎✉✂✎✏⌘⌥⏎" +
            "½⅓⅔¼¾⅛⅜⅝⅞¹²³⁴⁰ⁿ₀₁₂₃ªº℃℉№℗℠Ⅰ Ⅱ Ⅲ Ⅳ Ⅴ".Replace(" ", "") +
            "ÀÁÂÃÄÅÆÇÈÉÊËÌÍÎÏÑÒÓÔÕÖØÙÚÛÜÝßàáâãäåæçèéêëìíîïñòóôõöøùúûüýÿŒœ";

        public SymbolDialog(Window owner) : base(owner, "Symbol")
        {
            var grid = new UniformGrid { Columns = 16 };
            var preview = new TextBlock { FontSize = 40, Width = 70, Height = 64, TextAlignment = TextAlignment.Center, Text = "©" };
            var name = new TextBlock { Foreground = Brushes.Gray, Margin = new Thickness(0, 6, 0, 0) };
            string selected = "©";
            var recent = Settings.Current.Recent;
            foreach (var ch in StringInfoElements(Symbols))
            {
                var s = ch;
                var b = new Button { Content = s, Width = 28, Height = 28, FontSize = 16, Padding = new Thickness(0), Margin = new Thickness(0), Style = (Style)FindResource("FmtButton"), ToolTip = $"U+{char.ConvertToUtf32(s, 0):X4}" };
                b.Click += (o, e) => { selected = s; preview.Text = s; name.Text = $"Character code: U+{char.ConvertToUtf32(s, 0):X4}"; };
                b.MouseDoubleClick += (o, e) => Insert?.Invoke(s);
                grid.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD8, 0xE2)), BorderThickness = new Thickness(0, 0, 1, 1), Child = b });
            }
            var right = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            right.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Background = Brushes.White, Child = preview });
            var sp = new DockPanel();
            DockPanel.SetDock(right, Dock.Right);
            sp.Children.Add(right);
            var left = new StackPanel();
            left.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(0xA0, 0xA8, 0xB4)), BorderThickness = new Thickness(1, 1, 0, 0), Background = Brushes.White, Child = grid });
            left.Children.Add(name);
            sp.Children.Add(left);
            Build(sp, UI.Btn("_Insert", (s, e) => Insert?.Invoke(selected), true), UI.Btn("Close", (s, e) => Close(), false, true));
        }

        static IEnumerable<string> StringInfoElements(string s)
        {
            var e = StringInfo.GetTextElementEnumerator(s);
            while (e.MoveNext()) yield return (string)e.Current;
        }
    }

    // =====================================================================================
    public class ZoomDialog : DialogBase
    {
        public double? Zoom; public string Mode;
        public ZoomDialog(Window owner, double current) : base(owner, "Zoom", 300)
        {
            var sp = new StackPanel();
            var opts = new (string Label, string Tag)[] { ("_200%", "2"), ("_150%", "1.5"), ("1_00%", "1"), ("_75%", "0.75"), ("_50%", "0.5"), ("Page _width", "width"), ("_Whole page", "page") };
            var radios = new List<RadioButton>();
            foreach (var o in opts)
            {
                var rb = new RadioButton { Content = o.Label, Tag = o.Tag, Margin = new Thickness(0, 2, 0, 2) };
                if (double.TryParse(o.Tag, NumberStyles.Float, D.Inv, out var z) && Math.Abs(z - current) < 0.001) rb.IsChecked = true;
                radios.Add(rb);
                sp.Children.Add(rb);
            }
            var pct = D.Box(Math.Round(current * 100).ToString(D.Inv) + "%", 70);
            pct.GotFocus += (s, e) => radios.ForEach(r => r.IsChecked = false);
            sp.Children.Add(D.Form(("_Percent:", pct)));
            Build(D.Group("Zoom to", sp), Ok(() =>
            {
                var sel = radios.FirstOrDefault(r => r.IsChecked == true);
                if (sel != null)
                {
                    var tag = (string)sel.Tag;
                    if (tag == "width" || tag == "page") Mode = tag; else Zoom = double.Parse(tag, D.Inv);
                    return true;
                }
                var v = D.ParseNum(pct.Text);
                if (v == null || v < 10 || v > 500) { VistaDialog.Show(this, "Enter a zoom between 10% and 500%.", "Zoom", MessageBoxButton.OK, DlgIcon.Warning); return false; }
                Zoom = v / 100;
                return true;
            }), Cancel());
        }
    }

    // =====================================================================================
    public class PictureSizeDialog : DialogBase
    {
        readonly TextBox _w, _h;
        readonly CheckBox _lock;
        public double W, H;
        public PictureSizeDialog(Window owner, double w, double h, double natW, double natH) : base(owner, "Picture Size", 300)
        {
            _w = D.Box(D.In(w / 96)); _h = D.Box(D.In(h / 96));
            _lock = new CheckBox { Content = "_Lock aspect ratio", IsChecked = true, Margin = new Thickness(0, 6, 0, 0) };
            double ratio = w > 0 ? h / w : 1;
            bool busy = false;
            _w.TextChanged += (s, e) => { if (busy || _lock.IsChecked != true) return; var v = D.ParseNum(_w.Text); if (v > 0) { busy = true; _h.Text = D.In(v.Value * ratio); busy = false; } };
            _h.TextChanged += (s, e) => { if (busy || _lock.IsChecked != true) return; var v = D.ParseNum(_h.Text); if (v > 0) { busy = true; _w.Text = D.In(v.Value / ratio); busy = false; } };
            var sp = new StackPanel();
            sp.Children.Add(D.Form(("_Width:", _w), ("_Height:", _h)));
            sp.Children.Add(_lock);
            var reset = UI.Btn("_Reset", (s, e) => { busy = true; _w.Text = D.In(natW / 96); _h.Text = D.In(natH / 96); busy = false; });
            Build(sp, reset, Ok(() =>
            {
                var a = D.ParseNum(_w.Text); var b = D.ParseNum(_h.Text);
                if (a == null || b == null || a <= 0 || b <= 0 || a > 22 || b > 22) { VistaDialog.Show(this, "Enter a size between 0 and 22 inches.", Title, MessageBoxButton.OK, DlgIcon.Warning); return false; }
                W = a.Value * 96; H = b.Value * 96; return true;
            }), Cancel());
        }
    }

    // =====================================================================================
    public class PropertiesDialog : DialogBase
    {
        readonly TextBox _title, _author;
        public string DocTitle => _title.Text;
        public string Author => _author.Text;

        public PropertiesDialog(Window owner, string path, string title, string author, IEnumerable<(string, string)> stats) : base(owner, "Document Properties", 420)
        {
            _title = D.Box(title ?? "", 270); _author = D.Box(author ?? "", 270);
            var sp = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            head.Children.Add(UI.Icon("IconDoc", 32));
            head.Children.Add(new TextBlock { Text = path == null ? "Not saved yet" : System.IO.Path.GetFileName(path), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
            sp.Children.Add(head);
            sp.Children.Add(D.Group("Summary", D.Form(("_Title:", _title), ("_Author:", _author))));
            var st = new List<(string, UIElement)>();
            if (path != null)
            {
                st.Add(("Location:", new TextBlock { Text = System.IO.Path.GetDirectoryName(path), TextWrapping = TextWrapping.Wrap, MaxWidth = 270 }));
                try
                {
                    var fi = new System.IO.FileInfo(path);
                    if (fi.Exists)
                    {
                        st.Add(("Size:", new TextBlock { Text = fi.Length < 1024 * 1024 ? $"{fi.Length / 1024.0:0.#} KB" : $"{fi.Length / 1048576.0:0.##} MB" }));
                        st.Add(("Modified:", new TextBlock { Text = fi.LastWriteTime.ToString("f") }));
                    }
                }
                catch { }
            }
            foreach (var (k, v) in stats) st.Add((k, new TextBlock { Text = v }));
            sp.Children.Add(D.Group("Statistics", D.Form(st.ToArray())));
            Build(sp, Ok(), Cancel());
        }
    }

    // =====================================================================================
    public class OptionsDialog : DialogBase
    {
        public OptionsDialog(Window owner) : base(owner, "Options", 420)
        {
            var s = Settings.Current;
            var font = new ComboBox { Width = 200, IsEditable = true, ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(f => f).ToList(), Text = s.DefaultFont };
            font.ItemTemplate = FontDialog.FontItemTemplate();
            var size = new ComboBox { Width = 70, IsEditable = true, ItemsSource = MainWindow.Sizes, Text = s.DefaultSizePt.ToString(D.Inv) };
            var spell = new CheckBox { Content = "Check _spelling as you type", IsChecked = s.SpellCheck };
            var insKey = new CheckBox { Content = "Use the _Insert key to control overtype mode", IsChecked = s.InsertKeyOvertype };
            var mins = D.Box(s.AutoRecoverMinutes.ToString(D.Inv), 50);
            var arRow = new StackPanel { Orientation = Orientation.Horizontal };
            arRow.Children.Add(mins); arRow.Children.Add(new TextBlock { Text = " minutes (0 turns it off)", VerticalAlignment = VerticalAlignment.Center });
            var sp = new StackPanel();
            sp.Children.Add(D.Group("New documents", D.Form(("Default _font:", font), ("Default si_ze:", size))));
            sp.Children.Add(D.Group("Editing", insKey));
            sp.Children.Add(D.Group("Proofing", spell));
            sp.Children.Add(D.Group("Save", D.Form(("Save AutoRecover info every:", arRow))));
            Build(sp, Ok(() =>
            {
                var sz = D.ParseNum(size.Text);
                if (sz == null || sz < 1 || sz > 400 || string.IsNullOrWhiteSpace(font.Text) || !int.TryParse(mins.Text, out var m) || m < 0)
                { VistaDialog.Show(this, "Check the font size and AutoRecover minutes.", "Options", MessageBoxButton.OK, DlgIcon.Warning); return false; }
                s.DefaultFont = font.Text.Trim(); s.DefaultSizePt = sz.Value; s.SpellCheck = spell.IsChecked == true; s.InsertKeyOvertype = insKey.IsChecked == true; s.AutoRecoverMinutes = m;
                Settings.Save();
                StyleDef.Init();
                return true;
            }), Cancel());
        }
    }

    // =====================================================================================
    public class ColorDialog : DialogBase
    {
        public Color Result;
        public ColorDialog(Window owner, Color start) : base(owner, "Colors", 360)
        {
            Result = start;
            var swatch = new Border { Width = 70, Height = 70, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Background = new SolidColorBrush(start) };
            var hex = D.Box(Fmt.Hex(start), 80);
            Slider S(byte v) => new Slider { Minimum = 0, Maximum = 255, Value = v, Width = 170, IsSnapToTickEnabled = true, TickFrequency = 1, Margin = new Thickness(0, 3, 0, 3) };
            var r = S(start.R); var g = S(start.G); var b = S(start.B);
            bool busy = false;
            void FromSliders()
            {
                if (busy) return;
                Result = Color.FromRgb((byte)r.Value, (byte)g.Value, (byte)b.Value);
                swatch.Background = new SolidColorBrush(Result);
                busy = true; hex.Text = Fmt.Hex(Result); busy = false;
            }
            r.ValueChanged += (s, e) => FromSliders(); g.ValueChanged += (s, e) => FromSliders(); b.ValueChanged += (s, e) => FromSliders();
            hex.TextChanged += (s, e) =>
            {
                if (busy) return;
                var t = hex.Text.Trim().TrimStart('#');
                if (t.Length == 6 && int.TryParse(t, NumberStyles.HexNumber, null, out var v))
                { busy = true; r.Value = (v >> 16) & 255; g.Value = (v >> 8) & 255; b.Value = v & 255; busy = false; FromSliders(); }
            };
            var dp = new DockPanel();
            DockPanel.SetDock(swatch, Dock.Right);
            swatch.Margin = new Thickness(12, 0, 0, 0);
            dp.Children.Add(swatch);
            dp.Children.Add(D.Form(("_Red:", r), ("_Green:", g), ("_Blue:", b), ("_Hex:", hex)));
            Build(dp, Ok(), Cancel());
        }

        public static Color? Pick(Window owner, Color start)
        {
            var d = new ColorDialog(owner, start);
            return d.ShowDialog() == true ? d.Result : (Color?)null;
        }
    }

    // =====================================================================================
    public static class InfoDialogs
    {
        public static void About(Window owner)
        {
            var d = new DialogBase(owner, "About Valco Word", 400);
            var sp = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new Image { Source = UI.AppIconImage, Width = 64, Height = 64, Margin = new Thickness(0, 0, 14, 0) });
            var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            t.Children.Add(new TextBlock { Text = "Valco Word", FontSize = 22, Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x33, 0x99)) });
            var ver = typeof(InfoDialogs).Assembly.GetName().Version;
            t.Children.Add(new TextBlock { Text = $"Version {ver.Major}.{ver.Minor}.{ver.Build}", Foreground = Brushes.Gray });
            head.Children.Add(t);
            sp.Children.Add(head);
            sp.Children.Add(new TextBlock { Text = "A word processor for Windows, from the Valco suite.\nOpens and saves Word documents (.docx), Rich Text (.rtf) and plain text.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) });
            sp.Children.Add(new TextBlock { Text = "© 2026 ItsRaiden", Margin = new Thickness(0, 10, 0, 0), Foreground = Brushes.Gray });
            d.Content = UI.DialogLayout(sp, UI.Strip(UI.Btn("OK", (s, e) => d.Close(), true, true)));
            d.ShowDialog();
        }

        public static void Shortcuts(Window owner)
        {
            var items = new (string, string)[]
            {
                ("Ctrl+N / Ctrl+O / Ctrl+S", "New, Open, Save"), ("F12", "Save As"), ("Ctrl+P", "Print"), ("Ctrl+W", "Close"),
                ("Ctrl+Z / Ctrl+Y", "Undo, Redo"), ("Ctrl+X / C / V", "Cut, Copy, Paste"), ("Ctrl+Shift+V", "Paste as plain text"),
                ("Ctrl+F / F3 / Ctrl+H", "Find, Find Next, Replace"), ("Ctrl+G", "Go to page"),
                ("Ctrl+B / I / U", "Bold, Italic, Underline"), ("Ctrl+= / Ctrl+Shift+=", "Subscript, Superscript"),
                ("Ctrl+] / Ctrl+[", "Grow / shrink font"), ("Ctrl+D", "Font dialog"), ("Ctrl+Space", "Clear formatting"),
                ("Ctrl+L / E / R / J", "Align left, center, right, justify"), ("Ctrl+1 / 2 / 5", "Single, double, 1.5 line spacing"),
                ("Ctrl+Alt+1 / 2 / 3", "Heading 1, 2, 3"), ("Ctrl+Shift+N", "Normal style"),
                ("Ctrl+Shift+L", "Bullets"), ("Ctrl+K", "Insert hyperlink"), ("Ctrl+Enter", "Page break"),
                ("F7", "Next spelling error"), ("Ctrl+Shift+G", "Word count"), ("Ctrl+Mouse wheel", "Zoom"), ("F11", "Full screen reading"),
            };
            var d = new DialogBase(owner, "Keyboard Shortcuts", 440);
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < items.Length; i++)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var k = new TextBlock { Text = items[i].Item1, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 2) };
                var v = new TextBlock { Text = items[i].Item2, Margin = new Thickness(0, 2, 0, 2) };
                Grid.SetRow(k, i); Grid.SetRow(v, i); Grid.SetColumn(v, 1);
                g.Children.Add(k); g.Children.Add(v);
            }
            d.Content = UI.DialogLayout(g, UI.Strip(UI.Btn("OK", (s, e) => d.Close(), true, true)));
            d.ShowDialog();
        }

        public static void WordCount(Window owner, IEnumerable<(string, string)> stats, string scope)
        {
            var d = new DialogBase(owner, "Word Count", 300);
            var sp = new StackPanel();
            sp.Children.Add(D.SectionTitle(scope));
            sp.Children.Add(D.Form(stats.Select(s => (s.Item1, (UIElement)new TextBlock { Text = s.Item2, Margin = new Thickness(0, 2, 0, 2), HorizontalAlignment = HorizontalAlignment.Right })).ToArray()));
            d.Content = UI.DialogLayout(sp, UI.Strip(UI.Btn("Close", (s, e) => d.Close(), true, true)));
            d.ShowDialog();
        }
    }
}
