using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ValcoSpreadsheets.Grid;

namespace ValcoSpreadsheets
{
    /// <summary>A Vista dialog with the white content area and grey OK/Cancel strip.</summary>
    public class VDialog : VistaWindow
    {
        public VDialog(Window owner, string title, double width = 0)
        {
            Title = title;
            if (owner != null && owner.IsVisible) Owner = owner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            if (width > 0) { Width = width; SizeToContent = SizeToContent.Height; }
            else SizeToContent = SizeToContent.WidthAndHeight;
        }

        protected void Build(UIElement content, Func<bool> ok, string okText = "OK", params Button[] extra)
        {
            var buttons = new List<UIElement>(extra.Where(b => b != null));
            buttons.Add(UI.Btn(okText, (s, e) => { if (ok == null || ok()) DialogResult = true; }, true));
            buttons.Add(UI.Btn("Cancel", null, false, true));
            Content = UI.DialogLayout(content, UI.Strip(buttons.ToArray()));
        }

        public static TextBlock Label(string t, double top = 0) => new TextBlock { Text = t, Margin = new Thickness(0, top, 0, 3), TextWrapping = TextWrapping.Wrap };
        public static TextBlock Heading(string t) => new TextBlock { Text = t, Margin = new Thickness(0, 8, 0, 4), FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x39, 0x57)) };
        public static TextBox Box(string text = "", double width = double.NaN) => new TextBox { Text = text ?? "", Width = width, Height = 22, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
        public static CheckBox Chk(string t, bool on = false) => new CheckBox { Content = t, IsChecked = on, Margin = new Thickness(0, 3, 0, 3) };
        public static RadioButton Radio(string t, string group, bool on = false) => new RadioButton { Content = t, GroupName = group, IsChecked = on, Margin = new Thickness(0, 3, 12, 3) };
        public static ComboBox Combo(IEnumerable<string> items, int sel = 0, double width = double.NaN, bool editable = false)
        {
            var c = new ComboBox { ItemsSource = items.ToList(), SelectedIndex = sel, Width = width, Height = 22, Margin = new Thickness(0, 0, 0, 6), IsEditable = editable };
            return c;
        }
        public static StackPanel Row(params UIElement[] items) { var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) }; foreach (var i in items) sp.Children.Add(i); return sp; }
        public static GroupBox Group(string header, UIElement content) => new GroupBox { Header = header, Content = content, Padding = new Thickness(6), Margin = new Thickness(0, 4, 0, 6) };
    }

    /// <summary>One choice from a list of radio buttons (Insert / Delete cells, Zoom...).</summary>
    public class ChoiceDialog : VDialog
    {
        public static string LastCustom;
        readonly List<RadioButton> _radios = new();
        TextBox _custom;

        ChoiceDialog(Window owner, string title, string[] options, int selected, string customLabel, string customValue) : base(owner, title, 260)
        {
            var sp = new StackPanel();
            sp.Children.Add(Label(title + ":"));
            for (int i = 0; i < options.Length; i++) { var r = Radio(options[i], "c", i == selected); _radios.Add(r); sp.Children.Add(r); }
            if (customLabel != null)
            {
                var r = Radio(customLabel, "c", selected < 0); _radios.Add(r);
                _custom = Box(customValue, 60); _custom.Margin = new Thickness(4, 0, 0, 0);
                _custom.GotFocus += (s, e) => r.IsChecked = true;
                sp.Children.Add(Row(r, _custom, new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) }));
            }
            Build(sp, null);
        }

        public static int Ask(Window owner, string title, string[] options, int selected, string customLabel = null, string customValue = null)
        {
            var d = new ChoiceDialog(owner, title, options, selected, customLabel, customValue);
            if (d.ShowDialog() != true) return -1;
            LastCustom = d._custom?.Text;
            return d._radios.FindIndex(r => r.IsChecked == true);
        }
    }

    /// <summary>Pick one item from a list (Unhide sheet, Go To).</summary>
    public class ListDialog : VDialog
    {
        readonly ListBox _list;
        readonly TextBox _box;

        ListDialog(Window owner, string title, string prompt, List<string> items, string typeLabel) : base(owner, title, 300)
        {
            var sp = new StackPanel();
            sp.Children.Add(Label(prompt));
            _list = new ListBox { ItemsSource = items, Height = 150, Margin = new Thickness(0, 0, 0, 8) };
            sp.Children.Add(_list);
            if (typeLabel != null)
            {
                sp.Children.Add(Label(typeLabel));
                _box = Box();
                sp.Children.Add(_box);
                _list.SelectionChanged += (s, e) => { if (_list.SelectedItem is string t) _box.Text = t; };
            }
            else if (items.Count > 0) _list.SelectedIndex = 0;
            _list.MouseDoubleClick += (s, e) => { if (_list.SelectedItem != null) DialogResult = true; };
            Build(sp, () => _list.SelectedItem != null || !string.IsNullOrWhiteSpace(_box?.Text));
            Loaded += (s, e) => { if (_box != null) _box.Focus(); else _list.Focus(); };
        }

        public static string Ask(Window owner, string title, string prompt, List<string> items)
        {
            var d = new ListDialog(owner, title, prompt, items, null);
            return d.ShowDialog() == true ? d._list.SelectedItem as string : null;
        }

        public static string AskOrType(Window owner, string title, string prompt, List<string> items, string typeLabel)
        {
            var d = new ListDialog(owner, title, prompt, items, typeLabel);
            return d.ShowDialog() == true ? d._box.Text : null;
        }
    }

    /// <summary>Multi-line text (comments).</summary>
    public class TextDialog : VDialog
    {
        readonly TextBox _box;
        TextDialog(Window owner, string title, string prompt, string text) : base(owner, title, 340)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = prompt, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
            _box = new TextBox { Text = text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 110, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xE1)) };
            sp.Children.Add(_box);
            Build(sp, null);
            Loaded += (s, e) => { _box.Focus(); _box.CaretIndex = _box.Text.Length; };
        }
        public static string Ask(Window owner, string title, string prompt, string text)
        {
            var d = new TextDialog(owner, title, prompt, text);
            return d.ShowDialog() == true ? d._box.Text.Trim() : null;
        }
    }

    public class PasswordDialog : VDialog
    {
        readonly PasswordBox _pw;
        PasswordDialog(Window owner, string title, string prompt, bool allowEmpty) : base(owner, title, 300)
        {
            var sp = new StackPanel();
            sp.Children.Add(Label(prompt));
            _pw = new PasswordBox { Height = 22, VerticalContentAlignment = VerticalAlignment.Center };
            sp.Children.Add(_pw);
            sp.Children.Add(new TextBlock { Text = "Caution: If you lose or forget the password, it cannot be recovered.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 8, 0, 0), FontSize = 11 });
            Build(sp, () => allowEmpty || _pw.Password.Length > 0);
            Loaded += (s, e) => _pw.Focus();
        }
        public static string Ask(Window owner, string title, string prompt, bool allowEmpty = false)
        {
            var d = new PasswordDialog(owner, title, prompt, allowEmpty);
            return d.ShowDialog() == true ? d._pw.Password : null;
        }
    }

    /// <summary>Office 2007 colour picker: theme colours with tints, standard colours, More Colors.</summary>
    public static class ColorPicker
    {
        public static void Show(FrameworkElement target, string noneLabel, Action<Color?> picked)
        {
            var popup = new Popup { PlacementTarget = target, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
            var sp = new StackPanel { Margin = new Thickness(4) };
            Button Swatch(Color c, string tip)
            {
                var b = new Button { Width = 15, Height = 15, Margin = new Thickness(1.5), Background = new SolidColorBrush(c), BorderBrush = new SolidColorBrush(Color.FromRgb(0xC5, 0xC5, 0xC5)), ToolTip = tip, Focusable = false, Padding = new Thickness(0) };
                b.Template = SwatchTemplate();
                b.Click += (s, e) => { popup.IsOpen = false; picked(c); };
                return b;
            }
            var none = new Button { Content = noneLabel, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(0, 0, 0, 4), Style = RB.BtnStyle };
            none.Click += (s, e) => { popup.IsOpen = false; picked(null); };
            sp.Children.Add(none);
            sp.Children.Add(new TextBlock { Text = "Theme Colors", FontWeight = FontWeights.Bold, Margin = new Thickness(2, 2, 0, 2), Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x42, 0x8B)) });
            var themeRow = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var c in XlColors.ThemeRow) themeRow.Children.Add(Swatch(c, null));
            sp.Children.Add(themeRow);
            var tints = new[] { 0.8, 0.6, 0.4, -0.25, -0.5 };
            var tintGrid = new StackPanel { Margin = new Thickness(0, 3, 0, 0) };
            foreach (var t in tints)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var c in XlColors.ThemeRow) row.Children.Add(Swatch(XlColors.Shade(c, c == Colors.White && t > 0 ? -1 + t : c == Colors.Black && t < 0 ? -t : t), null));
                tintGrid.Children.Add(row);
            }
            sp.Children.Add(tintGrid);
            sp.Children.Add(new TextBlock { Text = "Standard Colors", FontWeight = FontWeights.Bold, Margin = new Thickness(2, 6, 0, 2), Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x42, 0x8B)) });
            var std = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var c in XlColors.Standard) std.Children.Add(Swatch(c, null));
            sp.Children.Add(std);
            var more = new Button { Content = "More Colors...", HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(0, 6, 0, 0), Style = RB.BtnStyle };
            more.Click += (s, e) =>
            {
                popup.IsOpen = false;
                using var cd = new System.Windows.Forms.ColorDialog { FullOpen = true };
                if (cd.ShowDialog() == System.Windows.Forms.DialogResult.OK) picked(Color.FromRgb(cd.Color.R, cd.Color.G, cd.Color.B));
            };
            sp.Children.Add(more);
            popup.Child = new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0x86, 0x9F, 0xC1)), BorderThickness = new Thickness(1), Child = sp, Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 2, Opacity = 0.35, BlurRadius = 5 }, Margin = new Thickness(0, 0, 5, 5) };
            popup.IsOpen = true;
        }

        static ControlTemplate _tpl;
        static ControlTemplate SwatchTemplate()
        {
            if (_tpl != null) return _tpl;
            var t = new ControlTemplate(typeof(Button));
            var b = new FrameworkElementFactory(typeof(Border), "B");
            b.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            b.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            b.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            t.VisualTree = b;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0xF2, 0x94, 0x36)), "B"));
            hover.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "B"));
            t.Triggers.Add(hover);
            _tpl = t;
            return t;
        }
    }

    /// <summary>Symbol picker (Insert → Symbol).</summary>
    public class SymbolDialog : VDialog
    {
        string _picked;
        SymbolDialog(Window owner) : base(owner, "Symbol", 470)
        {
            var sp = new StackPanel();
            var fontBox = Combo(new[] { "(normal text)", "Segoe UI Symbol", "Wingdings", "Symbol", "Webdings" }, 0, 200);
            sp.Children.Add(Row(new TextBlock { Text = "Font:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 6) }, fontBox));
            var wrap = new WrapPanel { Width = 430 };
            var preview = new TextBlock { FontSize = 11, Margin = new Thickness(0, 6, 0, 0), Foreground = Brushes.DimGray };
            void Fill()
            {
                wrap.Children.Clear();
                var family = fontBox.SelectedIndex <= 0 ? new FontFamily("Segoe UI") : new FontFamily((string)fontBox.SelectedItem);
                var chars = Enumerable.Range(0x21, 0x5E).Concat(Enumerable.Range(0xA1, 0x5F)).Concat(new[] { 0x2018, 0x2019, 0x201C, 0x201D, 0x2022, 0x2026, 0x2013, 0x2014, 0x2122, 0x20AC, 0x2190, 0x2191, 0x2192, 0x2193, 0x2194, 0x221E, 0x2248, 0x2260, 0x2264, 0x2265, 0x2211, 0x221A, 0x2206, 0x03A9, 0x03C0, 0x00B5, 0x2713, 0x2717, 0x2605, 0x2606, 0x263A, 0x2665, 0x2660, 0x2663, 0x2666 });
                foreach (var cp in chars)
                {
                    var s = char.ConvertFromUtf32(cp);
                    var b = new Button { Content = s, FontFamily = family, FontSize = 15, Width = 26, Height = 26, Margin = new Thickness(0.5), Padding = new Thickness(0), Background = Brushes.White };
                    b.Click += (o, e) => { _picked = s; preview.Text = $"Unicode character U+{cp:X4}"; };
                    b.MouseDoubleClick += (o, e) => { _picked = s; DialogResult = true; };
                    wrap.Children.Add(b);
                }
            }
            fontBox.SelectionChanged += (s, e) => Fill();
            Fill();
            sp.Children.Add(new ScrollViewer { Content = wrap, Height = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            sp.Children.Add(preview);
            Build(sp, () => _picked != null, "Insert");
        }
        public static string Ask(Window owner) { var d = new SymbolDialog(owner); return d.ShowDialog() == true ? d._picked : null; }
    }

    /// <summary>The Spelling dialog: Ignore Once / Ignore All / Add / Change / Change All.</summary>
    public class SpellingDialog : VDialog
    {
        public string Action = "Cancel";
        public string Replacement;

        public SpellingDialog(Window owner, string word, List<string> suggestions) : base(owner, "Spelling: English (Canada)", 470)
        {
            var g = new System.Windows.Controls.Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            var left = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            left.Children.Add(Label("Not in Dictionary:"));
            var box = new TextBox { Text = word, Height = 48, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Red, Margin = new Thickness(0, 0, 0, 8) };
            left.Children.Add(box);
            left.Children.Add(Label("Suggestions:"));
            var list = new ListBox { ItemsSource = suggestions.Count > 0 ? suggestions : new List<string> { "(No Suggestions)" }, Height = 100, SelectedIndex = 0, IsEnabled = suggestions.Count > 0 };
            left.Children.Add(list);
            g.Children.Add(left);
            var right = new StackPanel();
            System.Windows.Controls.Grid.SetColumn(right, 1);
            Button B(string text, string act, double top = 2)
            {
                var b = UI.Btn(text, (s, e) =>
                {
                    Action = act;
                    Replacement = box.Text != word ? box.Text : list.SelectedItem as string ?? word;
                    if (act.StartsWith("Change") && suggestions.Count == 0 && box.Text == word) return;
                    DialogResult = true;
                });
                b.Margin = new Thickness(0, top, 0, 2); b.HorizontalAlignment = HorizontalAlignment.Stretch;
                right.Children.Add(b);
                return b;
            }
            B("Ignore Once", "Ignore", 18); B("Ignore All", "IgnoreAll"); B("Add to Dictionary", "Add");
            B("Change", "Change", 40).IsDefault = suggestions.Count > 0; B("Change All", "ChangeAll");
            g.Children.Add(right);
            list.MouseDoubleClick += (s, e) => { if (list.SelectedItem is string sel && suggestions.Count > 0) { Action = "Change"; Replacement = sel; DialogResult = true; } };
            Content = UI.DialogLayout(g, UI.Strip(UI.Btn("Cancel", (s, e) => { Action = "Cancel"; DialogResult = false; }, false, true)));
        }
    }
}
