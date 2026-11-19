using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ValcoWord
{
    /// <summary>Drop-down colour palette in the Office 2007 layout.</summary>
    public static class ColorPalette
    {
        static readonly string[] ThemeRow = { "FFFFFF", "000000", "EEECE1", "1F497D", "4F81BD", "C0504D", "9BBB59", "8064A2", "4BACC6", "F79646" };
        static readonly string[] Standard = { "C00000", "FF0000", "FFC000", "FFFF00", "92D050", "00B050", "00B0F0", "0070C0", "002060", "7030A0" };

        static Color Hex(string h) => Color.FromRgb(Convert.ToByte(h.Substring(0, 2), 16), Convert.ToByte(h.Substring(2, 2), 16), Convert.ToByte(h.Substring(4, 2), 16));
        static Color Tint(Color c, double t) => Color.FromRgb((byte)(c.R + (255 - c.R) * t), (byte)(c.G + (255 - c.G) * t), (byte)(c.B + (255 - c.B) * t));
        static Color Shade(Color c, double s) => Color.FromRgb((byte)(c.R * (1 - s)), (byte)(c.G * (1 - s)), (byte)(c.B * (1 - s)));

        public static void ShowText(FrameworkElement target, string noneLabel, Action<Color?> picked)
        {
            var rows = new List<Color[]>();
            var baseRow = ThemeRow.Select(Hex).ToArray();
            rows.Add(baseRow);
            foreach (var t in new[] { 0.8, 0.6, 0.4 }) rows.Add(baseRow.Select((c, i) => i == 0 ? Shade(c, 1 - t) : i == 1 ? Tint(c, 1 - t + 0.15) : Tint(c, t)).ToArray());
            foreach (var s in new[] { 0.25, 0.5 }) rows.Add(baseRow.Select((c, i) => i == 1 ? Tint(c, s) : Shade(c, s)).ToArray());
            Show(target, noneLabel, "Theme Colors", rows, Standard.Select(Hex).ToArray(), true, picked);
        }

        public static void ShowHighlight(FrameworkElement target, Action<Color?> picked)
        {
            var hl = Fmt.Highlights.Select(h => h.Color).ToArray();
            Show(target, "No Color", null, new List<Color[]> { hl.Take(5).ToArray(), hl.Skip(5).Take(5).ToArray(), hl.Skip(10).Take(5).ToArray() }, null, false, picked);
        }

        static void Show(FrameworkElement target, string noneLabel, string header, List<Color[]> rows, Color[] standard, bool more, Action<Color?> picked)
        {
            var popup = new Popup { PlacementTarget = target, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade };
            var sp = new StackPanel { Margin = new Thickness(3) };
            Button Item(string text, Action a)
            {
                var b = new Button { Content = text, Style = (Style)Application.Current.FindResource("FmtButton"), HorizontalContentAlignment = HorizontalAlignment.Left, Height = 24, Margin = new Thickness(0, 1, 0, 1) };
                b.Click += (s, e) => { popup.IsOpen = false; a(); };
                return b;
            }
            if (noneLabel != null) sp.Children.Add(Item(noneLabel, () => picked(null)));
            if (header != null) sp.Children.Add(Header(header));
            for (int r = 0; r < rows.Count; r++)
                sp.Children.Add(SwatchRow(rows[r], c => { popup.IsOpen = false; picked(c); }, r == 0 ? 4 : 0));
            if (standard != null)
            {
                sp.Children.Add(Header("Standard Colors"));
                sp.Children.Add(SwatchRow(standard, c => { popup.IsOpen = false; picked(c); }, 0));
            }
            if (more) sp.Children.Add(Item("More Colors...", () =>
            {
                var w = Window.GetWindow(target);
                var c = ColorDialog.Pick(w, Colors.Black);
                if (c.HasValue) picked(c);
            }));
            popup.Child = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x86, 0x86, 0x86)), BorderThickness = new Thickness(1),
                Child = sp, Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 2, Opacity = 0.35, BlurRadius = 5 }, Margin = new Thickness(0, 0, 5, 5)
            };
            popup.IsOpen = true;
        }

        static UIElement Header(string text) => new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xE7, 0xEE)), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(-3, 3, -3, 3),
            Child = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x42, 0x8B)) }
        };

        static UIElement SwatchRow(Color[] colors, Action<Color> click, double bottomGap)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 2, bottomGap) };
            foreach (var c in colors)
            {
                var col = c;
                var r = new Border
                {
                    Width = 15, Height = 15, Margin = new Thickness(1.5, 0.5, 1.5, 0.5), Background = new SolidColorBrush(c),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xC5, 0xC5, 0xC5)), BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
                    ToolTip = "#" + Fmt.Hex(c)
                };
                r.MouseEnter += (s, e) => r.BorderBrush = new SolidColorBrush(Color.FromRgb(0xF2, 0x94, 0x36));
                r.MouseLeave += (s, e) => r.BorderBrush = new SolidColorBrush(Color.FromRgb(0xC5, 0xC5, 0xC5));
                r.MouseLeftButtonUp += (s, e) => click(col);
                sp.Children.Add(r);
            }
            return sp;
        }
    }

    /// <summary>Hover grid for picking a table size, like Word's Insert Table button.</summary>
    public static class TableGridPicker
    {
        public static void Show(FrameworkElement target, Action<int, int> insert, Action moreDialog)
        {
            const int cols = 10, rows = 8;
            var popup = new Popup { PlacementTarget = target, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade };
            var label = new TextBlock { Text = "Insert Table", Margin = new Thickness(4, 3, 4, 4), FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x42, 0x8B)) };
            var grid = new UniformGrid { Rows = rows, Columns = cols, Margin = new Thickness(4, 0, 4, 4) };
            var cells = new Border[rows, cols];
            int hr = 0, hc = 0;
            Brush normal = Brushes.White, hot = new SolidColorBrush(Color.FromRgb(0xFF, 0xE2, 0x94));
            Brush nb = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)), hb = new SolidColorBrush(Color.FromRgb(0xF2, 0x94, 0x36));
            void Paint()
            {
                for (int r = 0; r < rows; r++) for (int c = 0; c < cols; c++)
                    {
                        bool on = r < hr && c < hc;
                        cells[r, c].Background = on ? hot : normal;
                        cells[r, c].BorderBrush = on ? hb : nb;
                    }
                label.Text = hr > 0 ? $"{hc}x{hr} Table" : "Insert Table";
            }
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int rr = r + 1, cc = c + 1;
                    var b = new Border { Width = 17, Height = 17, Margin = new Thickness(1), BorderThickness = new Thickness(1), Background = normal, BorderBrush = nb };
                    b.MouseEnter += (s, e) => { hr = rr; hc = cc; Paint(); };
                    b.MouseLeftButtonUp += (s, e) => { popup.IsOpen = false; insert(rr, cc); };
                    cells[r, c] = b;
                    grid.Children.Add(b);
                }
            var more = new Button { Content = "Insert Table...", Style = (Style)Application.Current.FindResource("FmtButton"), HorizontalContentAlignment = HorizontalAlignment.Left, Height = 24, Margin = new Thickness(3, 0, 3, 3) };
            more.Click += (s, e) => { popup.IsOpen = false; moreDialog(); };
            var sp = new StackPanel();
            sp.Children.Add(label); sp.Children.Add(grid); sp.Children.Add(more);
            popup.Child = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x86, 0x86, 0x86)), BorderThickness = new Thickness(1),
                Child = sp, Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 2, Opacity = 0.35, BlurRadius = 5 }, Margin = new Thickness(0, 0, 5, 5)
            };
            popup.IsOpen = true;
        }
    }

    /// <summary>Selection frame with a corner handle for resizing pictures.</summary>
    public class ImageResizeAdorner : Adorner
    {
        readonly VisualCollection _visuals;
        readonly Thumb _corner;
        readonly Image _img;
        public event Action Resized;

        public ImageResizeAdorner(Image img) : base(img)
        {
            _img = img;
            _visuals = new VisualCollection(this);
            _corner = new Thumb { Width = 9, Height = 9, Cursor = Cursors.SizeNWSE };
            _corner.Template = BuildThumbTemplate();
            _corner.DragDelta += (s, e) =>
            {
                double w = (double.IsNaN(_img.Width) ? _img.ActualWidth : _img.Width);
                double h = (double.IsNaN(_img.Height) ? _img.ActualHeight : _img.Height);
                if (w <= 0 || h <= 0) return;
                double ratio = h / w;
                double nw = Math.Max(16, w + e.HorizontalChange);
                _img.Width = nw;
                _img.Height = Math.Max(8, nw * ratio);
            };
            _corner.DragCompleted += (s, e) => Resized?.Invoke();
            _visuals.Add(_corner);
            IsHitTestVisible = true;
        }

        static ControlTemplate BuildThumbTemplate()
        {
            var t = new ControlTemplate(typeof(Thumb));
            var f = new FrameworkElementFactory(typeof(Border));
            f.SetValue(Border.BackgroundProperty, Brushes.White);
            f.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x2B, 0x56, 0x97)));
            f.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            t.VisualTree = f;
            return t;
        }

        protected override int VisualChildrenCount => _visuals.Count;
        protected override Visual GetVisualChild(int index) => _visuals[index];

        protected override Size ArrangeOverride(Size finalSize)
        {
            var sz = AdornedElement.RenderSize;
            _corner.Arrange(new Rect(sz.Width - 5, sz.Height - 5, 9, 9));
            return finalSize;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var sz = AdornedElement.RenderSize;
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x2B, 0x56, 0x97)), 1) { DashStyle = DashStyles.Dash };
            dc.DrawRectangle(null, pen, new Rect(-1, -1, sz.Width + 2, sz.Height + 2));
            foreach (var p in new[] { new Point(0, 0), new Point(sz.Width, 0), new Point(0, sz.Height) })
                dc.DrawRectangle(Brushes.White, new Pen(pen.Brush, 1), new Rect(p.X - 3, p.Y - 3, 6, 6));
        }
    }

    /// <summary>Text search over a FlowDocument, including matches that span formatting runs.</summary>
    public class TextSearch
    {
        readonly List<(TextPointer Start, int Offset, int Length)> _segs = new List<(TextPointer, int, int)>();
        readonly string _text;

        public TextSearch(FlowDocument doc)
        {
            var sb = new StringBuilder();
            Paragraph lastPara = null;
            var p = doc.ContentStart;
            while (p != null && p.CompareTo(doc.ContentEnd) < 0)
            {
                var ctx = p.GetPointerContext(LogicalDirection.Forward);
                if (ctx == TextPointerContext.Text)
                {
                    var para = p.Paragraph;
                    if (lastPara != null && para != lastPara) sb.Append('\n');
                    lastPara = para;
                    var t = p.GetTextInRun(LogicalDirection.Forward);
                    _segs.Add((p, sb.Length, t.Length));
                    sb.Append(t);
                    p = p.GetPositionAtOffset(t.Length);
                }
                else
                {
                    if (ctx == TextPointerContext.EmbeddedElement || p.GetAdjacentElement(LogicalDirection.Forward) is LineBreak)
                        sb.Append('\v');
                    p = p.GetNextContextPosition(LogicalDirection.Forward);
                }
            }
            _text = sb.ToString();
        }

        public string Text => _text;

        public TextPointer PointerAt(int index, LogicalDirection dir)
        {
            int lo = 0, hi = _segs.Count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (_segs[mid].Offset <= index) { found = mid; lo = mid + 1; } else hi = mid - 1;
            }
            if (found < 0) return _segs.Count > 0 ? _segs[0].Start : null;
            var s = _segs[found];
            int off = Math.Min(index - s.Offset, s.Length);
            return s.Start.GetPositionAtOffset(off, dir) ?? s.Start;
        }

        public int IndexOf(TextPointer ptr)
        {
            for (int i = 0; i < _segs.Count; i++)
            {
                var s = _segs[i];
                var end = s.Start.GetPositionAtOffset(s.Length);
                if (ptr.CompareTo(s.Start) <= 0) return s.Offset;
                if (end != null && ptr.CompareTo(end) <= 0) return s.Offset + s.Start.GetOffsetToPosition(ptr);
            }
            return _text.Length;
        }

        public static Regex BuildRegex(string query, bool matchCase, bool wholeWord)
        {
            var pat = Regex.Escape(query);
            if (wholeWord) pat = @"(?<![\w])" + pat + @"(?![\w])";
            return new Regex(pat, matchCase ? RegexOptions.None : RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        public TextRange Find(TextPointer from, string query, bool matchCase, bool wholeWord, bool up, out bool wrapped)
        {
            wrapped = false;
            if (string.IsNullOrEmpty(query)) return null;
            var rx = BuildRegex(query, matchCase, wholeWord);
            int start = from == null ? 0 : IndexOf(from);
            var all = rx.Matches(_text).Cast<Match>().Where(m => m.Length > 0 && !m.Value.Contains('\n')).ToList();
            if (all.Count == 0) return null;
            Match hit = up ? all.LastOrDefault(m => m.Index + m.Length <= start) : all.FirstOrDefault(m => m.Index >= start);
            if (hit == null) { wrapped = true; hit = up ? all.Last() : all.First(); }
            return new TextRange(PointerAt(hit.Index, LogicalDirection.Forward), PointerAt(hit.Index + hit.Length, LogicalDirection.Backward));
        }

        public List<TextRange> FindAll(string query, bool matchCase, bool wholeWord)
        {
            var rx = BuildRegex(query, matchCase, wholeWord);
            return rx.Matches(_text).Cast<Match>().Where(m => m.Length > 0 && !m.Value.Contains('\n'))
                .Select(m => new TextRange(PointerAt(m.Index, LogicalDirection.Forward), PointerAt(m.Index + m.Length, LogicalDirection.Backward))).ToList();
        }
    }
}
