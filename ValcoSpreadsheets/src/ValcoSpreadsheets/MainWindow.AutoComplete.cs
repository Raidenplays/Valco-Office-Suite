using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ValcoSpreadsheets.Model;

namespace ValcoSpreadsheets
{
    /// <summary>Formula AutoComplete (the function list while typing) and the argument ScreenTip, like Excel 2007.</summary>
    public partial class MainWindow
    {
        Popup _acPopup, _argPopup;
        ListBox _acList;
        TextBlock _acDesc, _argText;
        TextBox _acBox;

        void UpdateAutoComplete(TextBox box)
        {
            if (!Settings.Current.AutoComplete) return;
            var token = Functions.TokenAt(box.Text, box.CaretIndex);
            if (string.IsNullOrEmpty(token)) { HideAutoComplete(); return; }
            var matches = Functions.StartingWith(token).Select(f => f.Name).ToList();
            var names = _book.Wb.DefinedNames.Select(n => n.Name).Where(n => n.StartsWith(token, StringComparison.OrdinalIgnoreCase)).ToList();
            matches.AddRange(names);
            if (matches.Count == 0) { HideAutoComplete(); return; }
            if (_acPopup == null)
            {
                _acList = new ListBox { MaxHeight = 180, MinWidth = 140, BorderThickness = new Thickness(0), FontSize = 12 };
                _acDesc = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 260, Margin = new Thickness(6, 2, 6, 2), Foreground = Brushes.DimGray };
                _acList.SelectionChanged += (s, e) => { if (_acList.SelectedItem is string n && Functions.ByName.TryGetValue(n, out var f)) _acDesc.Text = f.Description; else _acDesc.Text = ""; };
                _acList.MouseDoubleClick += (s, e) => AcceptAutoComplete();
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Background = Brushes.White, Child = _acList });
                row.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(0x76, 0x76, 0x76)), BorderThickness = new Thickness(1), Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xE1)), Child = _acDesc, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4, 0, 0, 0) });
                _acPopup = new Popup { Child = row, StaysOpen = true, AllowsTransparency = true, Placement = PlacementMode.Bottom };
            }
            _acBox = box;
            _acList.ItemsSource = matches;
            _acList.SelectedIndex = 0;
            var r = box.GetRectFromCharacterIndex(Math.Max(0, box.CaretIndex - token.Length));
            _acPopup.PlacementTarget = box;
            _acPopup.Placement = PlacementMode.Relative;
            _acPopup.HorizontalOffset = r.Left;
            _acPopup.VerticalOffset = box.ActualHeight + 2;
            _acPopup.IsOpen = true;
            HideArgTip();
        }

        void HideAutoComplete() { if (_acPopup != null) _acPopup.IsOpen = false; }

        bool HandleAutoCompleteKey(KeyEventArgs e)
        {
            if (_acPopup == null || !_acPopup.IsOpen) return false;
            switch (e.Key)
            {
                case Key.Down: _acList.SelectedIndex = Math.Min(_acList.Items.Count - 1, _acList.SelectedIndex + 1); _acList.ScrollIntoView(_acList.SelectedItem); e.Handled = true; return true;
                case Key.Up: _acList.SelectedIndex = Math.Max(0, _acList.SelectedIndex - 1); _acList.ScrollIntoView(_acList.SelectedItem); e.Handled = true; return true;
                case Key.Tab: AcceptAutoComplete(); e.Handled = true; return true;
                case Key.Escape: HideAutoComplete(); e.Handled = true; return true;
            }
            return false;
        }

        void AcceptAutoComplete()
        {
            if (_acBox == null || !(_acList.SelectedItem is string name)) return;
            var token = Functions.TokenAt(_acBox.Text, _acBox.CaretIndex) ?? "";
            int start = _acBox.CaretIndex - token.Length;
            bool fn = Functions.ByName.ContainsKey(name);
            var insert = fn ? name + "(" : name;
            _acBox.Text = _acBox.Text.Remove(start, token.Length).Insert(start, insert);
            _acBox.CaretIndex = start + insert.Length;
            HideAutoComplete();
            _acBox.Focus();
            if (fn) UpdateArgTip(_acBox);
        }

        void UpdateArgTip(TextBox box)
        {
            var call = Functions.CallAt(box.Text, box.CaretIndex);
            if (call == null || !Functions.ByName.TryGetValue(call.Value.Name, out var fn) || (_acPopup != null && _acPopup.IsOpen)) { HideArgTip(); return; }
            if (_argPopup == null)
            {
                _argText = new TextBlock { Margin = new Thickness(5, 2, 5, 2), FontSize = 11.5 };
                _argPopup = new Popup { Child = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(0x76, 0x76, 0x76)), BorderThickness = new Thickness(1), Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xE1)), Child = _argText }, AllowsTransparency = true, StaysOpen = true };
            }
            _argText.Inlines.Clear();
            _argText.Inlines.Add(new Run(fn.Name + "("));
            var args = fn.Args.Split(',').Select(a => a.Trim()).ToList();
            for (int i = 0; i < args.Count; i++)
            {
                if (i > 0) _argText.Inlines.Add(new Run(", "));
                bool current = i == call.Value.Arg || (i == args.Count - 1 && args[i] == "..." && call.Value.Arg >= i);
                _argText.Inlines.Add(new Run(args[i]) { FontWeight = current ? FontWeights.Bold : FontWeights.Normal });
            }
            _argText.Inlines.Add(new Run(")"));
            _argPopup.PlacementTarget = box;
            _argPopup.Placement = PlacementMode.Relative;
            _argPopup.HorizontalOffset = 0;
            _argPopup.VerticalOffset = box.ActualHeight + 2;
            _argPopup.IsOpen = true;
        }

        void HideArgTip() { if (_argPopup != null) _argPopup.IsOpen = false; }

        /// <summary>Ctrl+Shift+A: types the argument names of the function at the caret.</summary>
        void ShowArgumentNames(TextBox box)
        {
            var call = Functions.CallAt(box.Text, box.CaretIndex);
            if (call == null || !Functions.ByName.TryGetValue(call.Value.Name, out var fn)) return;
            var names = string.Join(",", fn.Args.Split(',').Select(a => a.Trim().Trim('[', ']')).Where(a => a != "..."));
            InsertAtCaret(box, names + ")");
        }
    }
}
