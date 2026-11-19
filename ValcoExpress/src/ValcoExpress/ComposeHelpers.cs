using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace ValcoExpress
{
    /// <summary>Recipient text box with address-book autocomplete.</summary>
    public class AddressBox : TextBox
    {
        readonly Popup _pop;
        readonly ListBox _list;
        bool _suppress;

        public AddressBox()
        {
            VerticalContentAlignment = VerticalAlignment.Center;
            Height = 23;
            _list = new ListBox { MaxHeight = 180, MinWidth = 280, BorderThickness = new Thickness(0), Focusable = false };
            _pop = new Popup
            {
                Child = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(0x82, 0x87, 0x90)), BorderThickness = new Thickness(1), Background = Brushes.White, Child = _list },
                PlacementTarget = this, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = false
            };
            _list.PreviewMouseLeftButtonUp += (s, e) => Accept();
            TextChanged += (s, e) => Suggest();
            PreviewKeyDown += OnKey;
            LostKeyboardFocus += (s, e) => { if (!_list.IsMouseOver) _pop.IsOpen = false; };
        }

        string Token(out int start)
        {
            int caret = CaretIndex;
            start = 0;
            if (caret <= 0) return "";
            int sep = Text.LastIndexOfAny(new[] { ',', ';' }, caret - 1);
            start = sep + 1;
            return Text.Substring(start, caret - start).Trim();
        }

        void Suggest()
        {
            if (_suppress) return;
            var tok = Token(out _);
            if (tok.Length < 1) { _pop.IsOpen = false; return; }
            var items = AddressBook.Search(tok).Select(c => c.Display).Distinct().Take(8).ToList();
            if (items.Count == 0) { _pop.IsOpen = false; return; }
            _list.ItemsSource = items;
            _list.SelectedIndex = 0;
            _pop.IsOpen = true;
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (!_pop.IsOpen) return;
            switch (e.Key)
            {
                case Key.Down: _list.SelectedIndex = Math.Min(_list.Items.Count - 1, _list.SelectedIndex + 1); e.Handled = true; break;
                case Key.Up: _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1); e.Handled = true; break;
                case Key.Enter: case Key.Tab: Accept(); e.Handled = true; break;
                case Key.Escape: _pop.IsOpen = false; e.Handled = true; break;
            }
        }

        void Accept()
        {
            if (!(_list.SelectedItem is string sel)) return;
            Token(out int start);
            int caret = CaretIndex;
            _suppress = true;
            var insert = sel + "; ";
            Text = Text.Substring(0, start) + (start > 0 ? " " : "") + insert + Text.Substring(caret).TrimStart(' ', ';', ',');
            CaretIndex = Math.Min(Text.Length, start + (start > 0 ? 1 : 0) + insert.Length);
            _suppress = false;
            _pop.IsOpen = false;
            Focus();
        }

        public void Append(string address)
        {
            var t = Text.Trim().TrimEnd(';', ',').Trim();
            Text = t.Length == 0 ? address + "; " : t + "; " + address + "; ";
        }
    }

    /// <summary>Converts the compose editor's FlowDocument to HTML.</summary>
    public static class HtmlWriter
    {
        static string E(string s) => WebUtility.HtmlEncode(s ?? "");

        public static string ToHtml(FlowDocument doc)
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head>");
            sb.Append($"<body><div style=\"font-family:'{E(doc.FontFamily.Source)}';font-size:{Pt(doc.FontSize)}pt\">");
            foreach (var b in doc.Blocks) Block(b, sb);
            sb.Append("</div></body></html>");
            return sb.ToString();
        }

        static string Pt(double px) => Math.Round(px * 0.75, 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

        static void Block(Block b, StringBuilder sb)
        {
            switch (b)
            {
                case Paragraph p:
                    sb.Append("<div").Append(BlockStyle(p)).Append('>');
                    if (!p.Inlines.Any()) sb.Append("<br>");
                    else
                    {
                        foreach (var i in p.Inlines) Inline(i, sb);
                        if (p.Inlines.LastInline is LineBreak) sb.Append("<br>");
                    }
                    sb.Append("</div>");
                    break;
                case List l:
                    bool ordered = l.MarkerStyle == TextMarkerStyle.Decimal || l.MarkerStyle == TextMarkerStyle.LowerLatin ||
                                   l.MarkerStyle == TextMarkerStyle.UpperLatin || l.MarkerStyle == TextMarkerStyle.LowerRoman ||
                                   l.MarkerStyle == TextMarkerStyle.UpperRoman;
                    var tag = ordered ? "ol" : "ul";
                    sb.Append('<').Append(tag).Append(" style=\"margin-top:0;margin-bottom:0\">");
                    foreach (var li in l.ListItems)
                    {
                        sb.Append("<li>");
                        foreach (var cb in li.Blocks)
                        {
                            if (cb is Paragraph lp) { foreach (var i in lp.Inlines) Inline(i, sb); }
                            else Block(cb, sb);
                        }
                        sb.Append("</li>");
                    }
                    sb.Append("</").Append(tag).Append('>');
                    break;
                case Section s:
                    bool quote = s.BorderThickness.Left > 0;
                    sb.Append(quote ? "<blockquote style=\"margin:0 0 0 5px;padding-left:5px;border-left:2px solid #000000\">" : "<div>");
                    foreach (var cb in s.Blocks) Block(cb, sb);
                    sb.Append(quote ? "</blockquote>" : "</div>");
                    break;
                case BlockUIContainer _:
                    break;
                default:
                    sb.Append("<div>").Append(E(new TextRange(b.ContentStart, b.ContentEnd).Text)).Append("</div>");
                    break;
            }
        }

        static string BlockStyle(Paragraph p)
        {
            var css = new StringBuilder();
            switch (p.TextAlignment)
            {
                case TextAlignment.Center: css.Append("text-align:center;"); break;
                case TextAlignment.Right: css.Append("text-align:right;"); break;
                case TextAlignment.Justify: css.Append("text-align:justify;"); break;
            }
            if (p.Margin.Left > 0.5) css.Append($"margin-left:{Math.Round(p.Margin.Left)}px;");
            if (p.TextIndent > 0.5) css.Append($"text-indent:{Math.Round(p.TextIndent)}px;");
            var extra = Diff(p);
            css.Append(extra);
            return css.Length == 0 ? "" : $" style=\"{css}\"";
        }

        static void Inline(Inline i, StringBuilder sb)
        {
            switch (i)
            {
                case Run r:
                    sb.Append(Wrap(r, E(r.Text).Replace("\r\n", "<br>").Replace("\n", "<br>")));
                    break;
                case LineBreak _:
                    sb.Append("<br>");
                    break;
                case Hyperlink h:
                    var inner = new StringBuilder();
                    foreach (var c in h.Inlines) Inline(c, inner);
                    sb.Append($"<a href=\"{E(h.NavigateUri?.ToString())}\">").Append(inner).Append("</a>");
                    break;
                case Span s:
                    var sub = new StringBuilder();
                    foreach (var c in s.Inlines) Inline(c, sub);
                    sb.Append(Wrap(s, sub.ToString()));
                    break;
            }
        }

        static string Wrap(Inline e, string inner)
        {
            var css = Diff(e);
            if (e.TextDecorations != null && e.TextDecorations.Count > 0 && !(e is Hyperlink))
                css += e.TextDecorations.Any(d => d.Location == TextDecorationLocation.Strikethrough) ? "text-decoration:line-through;" : "text-decoration:underline;";
            return css.Length == 0 ? inner : $"<span style=\"{css}\">{inner}</span>";
        }

        static string Diff(TextElement e)
        {
            var parent = e.Parent as DependencyObject;
            if (parent == null) return "";
            var css = new StringBuilder();
            var ff = (FontFamily)parent.GetValue(TextElement.FontFamilyProperty);
            if (ff != null && e.FontFamily != null && ff.Source != e.FontFamily.Source) css.Append($"font-family:'{E(e.FontFamily.Source)}';");
            var fs = (double)parent.GetValue(TextElement.FontSizeProperty);
            if (Math.Abs(fs - e.FontSize) > 0.1) css.Append($"font-size:{Pt(e.FontSize)}pt;");
            var fw = (FontWeight)parent.GetValue(TextElement.FontWeightProperty);
            if (fw != e.FontWeight) css.Append(e.FontWeight.ToOpenTypeWeight() >= 600 ? "font-weight:bold;" : "font-weight:normal;");
            var st = (FontStyle)parent.GetValue(TextElement.FontStyleProperty);
            if (st != e.FontStyle) css.Append(e.FontStyle == FontStyles.Normal ? "font-style:normal;" : "font-style:italic;");
            var pf = parent.GetValue(TextElement.ForegroundProperty) as SolidColorBrush;
            var ef = e.Foreground as SolidColorBrush;
            if (ef != null && (pf == null || pf.Color != ef.Color)) css.Append($"color:#{ef.Color.R:X2}{ef.Color.G:X2}{ef.Color.B:X2};");
            var bg = e.Background as SolidColorBrush;
            if (bg != null && bg.Color.A > 0) css.Append($"background-color:#{bg.Color.R:X2}{bg.Color.G:X2}{bg.Color.B:X2};");
            return css.ToString();
        }
    }
}
