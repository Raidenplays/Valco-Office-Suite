using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ValcoWord
{
    /// <summary>Horizontal ruler with draggable margins and paragraph indent markers.</summary>
    public class Ruler : FrameworkElement
    {
        public double Zoom = 1, PageLeft, PageWidth = 816, MarginLeft = 96, MarginRight = 96;
        public double LeftIndent, FirstIndent, RightIndent;
        public bool IndentsEnabled = true;

        public event Action<double, double, double> IndentsChanged;   // left, first (relative), right
        public event Action<double, double> MarginsChanged;            // left, right

        enum Drag { None, First, Hanging, LeftBox, Right, MarginL, MarginR }
        Drag _drag;
        double _dragStartX, _origA, _origB;
        // colours come from the colour scheme (ChromeTheme publishes Ruler.*), with the Office Blue ruler as the fallback
        static Brush R(string key, Brush fallback) => Application.Current?.TryFindResource(key) as Brush ?? fallback;
        static readonly Brush TickDefault = Freeze(new SolidColorBrush(Color.FromRgb(0x50, 0x5A, 0x66)));
        static readonly Pen MarkerPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x44, 0x58, 0x70)), 1));
        static readonly Brush MarkerFill = Freeze(new LinearGradientBrush(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xC9, 0xD6, 0xE6), 90));
        static readonly Brush MarginDefault = Freeze(new SolidColorBrush(Color.FromRgb(0xA9, 0xBF, 0xD9)));
        static readonly Brush BarDefault = Freeze(new LinearGradientBrush(Color.FromRgb(0xF4, 0xF7, 0xFB), Color.FromRgb(0xDC, 0xE5, 0xF0), 90));
        static readonly Brush EdgeDefault = Freeze(new SolidColorBrush(Color.FromRgb(0x8D, 0xA3, 0xBE)));
        Pen TickPen = new Pen(TickDefault, 1);
        static readonly Typeface Face = new Typeface("Segoe UI");

        static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

        public Ruler()
        {
            Height = 24;
            SnapsToDevicePixels = true;
            Valco.UI.OfficeTheme.Changed += InvalidateVisual;
            ToolTip = "";
            ToolTipOpening += (s, e) =>
            {
                var p = Mouse.GetPosition(this);
                ToolTip = HitTest(p.X, p.Y) switch
                {
                    Drag.First => "First Line Indent", Drag.Hanging => "Hanging Indent", Drag.LeftBox => "Left Indent",
                    Drag.Right => "Right Indent", Drag.MarginL => "Left Margin", Drag.MarginR => "Right Margin", _ => null
                };
                if (ToolTip == null) e.Handled = true;
            };
        }

        public void Refresh() => InvalidateVisual();

        double X(double px) => PageLeft + px * Zoom;
        double Px(double x) => (x - PageLeft) / Zoom;
        static double Snap(double px) => Math.Round(px / 6.0) * 6.0; // 1/16 inch

        protected override void OnRender(DrawingContext dc)
        {
            double h = ActualHeight;
            var BarBg = R("Ruler.Bar", BarDefault); var Edge = R("Ruler.Edge", EdgeDefault); var MarginFill = R("Ruler.Margin", MarginDefault);
            var tick = R("Ruler.Tick", TickDefault);
            if (TickPen.Brush != tick) TickPen = new Pen(tick, 1);
            dc.DrawRectangle(BarBg, null, new Rect(0, 0, ActualWidth, h));
            dc.DrawRectangle(Edge, null, new Rect(0, h - 1, ActualWidth, 1));
            double top = 4, bh = h - 9;
            // page strip
            dc.DrawRectangle(MarginFill, new Pen(Edge, 1), new Rect(X(0), top, PageWidth * Zoom, bh));
            dc.DrawRectangle(R("Ruler.Page", Brushes.White), null, new Rect(X(MarginLeft), top, (PageWidth - MarginLeft - MarginRight) * Zoom, bh));

            // ticks: origin at the left margin, numbers each inch
            double origin = MarginLeft;
            for (int dir = -1; dir <= 1; dir += 2)
            {
                for (int i = dir == 1 ? 0 : 1; ; i++)
                {
                    double px = origin + dir * i * 12; // eighth inch
                    if (px < 0 || px > PageWidth) break;
                    double x = Math.Round(X(px)) + 0.5;
                    if (i % 8 == 0)
                    {
                        if (i == 0) continue;
                        var ft = new FormattedText((i / 8).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 9, TickPen.Brush, 1.0);
                        dc.DrawText(ft, new Point(x - ft.Width / 2, top + (bh - ft.Height) / 2));
                    }
                    else if (i % 4 == 0) dc.DrawLine(TickPen, new Point(x, top + bh / 2 - 3), new Point(x, top + bh / 2 + 3));
                    else if (Zoom >= 0.75) dc.DrawLine(TickPen, new Point(x, top + bh / 2 - 1), new Point(x, top + bh / 2 + 1));
                }
            }

            if (!IndentsEnabled) return;
            double lx = X(MarginLeft + LeftIndent), fx = X(MarginLeft + LeftIndent + FirstIndent), rx = X(PageWidth - MarginRight - RightIndent);
            // first line (down triangle, top)
            dc.DrawGeometry(MarkerFill, MarkerPen, Tri(fx, top - 3, true));
            // hanging (up triangle) + left box
            dc.DrawGeometry(MarkerFill, MarkerPen, Tri(lx, top + bh - 5, false));
            dc.DrawRectangle(MarkerFill, MarkerPen, new Rect(Math.Round(lx - 4) + 0.5, top + bh + 1.5, 8, 4));
            // right indent
            dc.DrawGeometry(MarkerFill, MarkerPen, Tri(rx, top + bh - 5, false));
        }

        static Geometry Tri(double x, double y, bool down)
        {
            x = Math.Round(x) + 0.5;
            var g = down
                ? Geometry.Parse(string.Format(CultureInfo.InvariantCulture, "M{0},{1} L{2},{1} L{3},{4} Z", x - 4.5, y, x + 4.5, x, y + 6))
                : Geometry.Parse(string.Format(CultureInfo.InvariantCulture, "M{0},{1} L{2},{3} L{4},{3} Z", x, y, x + 4.5, y + 6, x - 4.5));
            g.Freeze();
            return g;
        }

        Drag HitTest(double x, double y)
        {
            double h = ActualHeight, top = 4, bh = h - 9;
            if (IndentsEnabled)
            {
                double lx = X(MarginLeft + LeftIndent), fx = X(MarginLeft + LeftIndent + FirstIndent), rx = X(PageWidth - MarginRight - RightIndent);
                if (y < top + 5 && Math.Abs(x - fx) <= 5) return Drag.First;
                if (y >= top + bh + 1 && Math.Abs(x - lx) <= 5) return Drag.LeftBox;
                if (y >= top + bh - 6 && Math.Abs(x - lx) <= 5) return Drag.Hanging;
                if (y >= top + bh - 6 && Math.Abs(x - rx) <= 5) return Drag.Right;
            }
            if (Math.Abs(x - X(MarginLeft)) <= 3) return Drag.MarginL;
            if (Math.Abs(x - X(PageWidth - MarginRight)) <= 3) return Drag.MarginR;
            return Drag.None;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var p = e.GetPosition(this);
            if (_drag == Drag.None)
            {
                var hit = HitTest(p.X, p.Y);
                Cursor = hit == Drag.MarginL || hit == Drag.MarginR ? Cursors.SizeWE : Cursors.Arrow;
                return;
            }
            double delta = Snap((p.X - _dragStartX) / Zoom);
            double body = PageWidth - MarginLeft - MarginRight;
            switch (_drag)
            {
                case Drag.First:
                    FirstIndent = Clamp(_origA + delta, -LeftIndent, body - LeftIndent - RightIndent - 24);
                    break;
                case Drag.Hanging: // moves left indent, keeps first line where it is
                    {
                        double newLeft = Clamp(_origA + delta, 0, body - RightIndent - 24);
                        FirstIndent = _origB - (newLeft - _origA);
                        LeftIndent = newLeft;
                        break;
                    }
                case Drag.LeftBox:
                    LeftIndent = Clamp(_origA + delta, Math.Max(0, -FirstIndent), body - RightIndent - 24);
                    break;
                case Drag.Right:
                    RightIndent = Clamp(_origA - delta, 0, body - LeftIndent - 24);
                    break;
                case Drag.MarginL:
                    MarginLeft = Clamp(_origA + delta, 0, PageWidth - MarginRight - 96);
                    break;
                case Drag.MarginR:
                    MarginRight = Clamp(_origA - delta, 0, PageWidth - MarginLeft - 96);
                    break;
            }
            InvalidateVisual();
        }

        static double Clamp(double v, double min, double max) => Math.Max(min, Math.Min(max, v));

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            var p = e.GetPosition(this);
            _drag = HitTest(p.X, p.Y);
            if (_drag == Drag.None) return;
            _dragStartX = p.X;
            switch (_drag)
            {
                case Drag.First: _origA = FirstIndent; break;
                case Drag.Hanging: _origA = LeftIndent; _origB = FirstIndent; break;
                case Drag.LeftBox: _origA = LeftIndent; break;
                case Drag.Right: _origA = RightIndent; break;
                case Drag.MarginL: _origA = MarginLeft; break;
                case Drag.MarginR: _origA = MarginRight; break;
            }
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (_drag == Drag.None) return;
            var d = _drag;
            _drag = Drag.None;
            ReleaseMouseCapture();
            if (d == Drag.MarginL || d == Drag.MarginR) MarginsChanged?.Invoke(MarginLeft, MarginRight);
            else IndentsChanged?.Invoke(LeftIndent, FirstIndent, RightIndent);
        }
    }
}
