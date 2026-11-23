// Recolours the suite's vector icons for the colour scheme in use, so they match the ribbon:
//   Blue, Silver, Black, Windows Vista  - the original Office 2007-style icons
//   Dreamweaver CS6                     - muted, mostly grey icons like CS6's
//   Modern Dark                         - light outline icons with teal and soft gold accents
//   2077                                - neon: cyan outlines, pink and yellow accents
// Every "Icon..." DrawingImage in the application's resources gets a recoloured copy at the top level
// (which wins over the merged dictionaries); RB buttons reference icons with DynamicResource, so they
// change on the spot. Valco Weave does the same mapping in src/lib/iconTheme.ts.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Valco.UI
{
    public static class IconTheme
    {
        static readonly Dictionary<string, DrawingImage> Originals = new Dictionary<string, DrawingImage>();
        static readonly HashSet<string> Overridden = new HashSet<string>();
        static readonly Dictionary<string, ImageSource> Cache = new Dictionary<string, ImageSource>();

        /// <summary>The colour mapping for a scheme, or null to keep the icons as they are.</summary>
        public static Func<Color, Color> MapFor(string scheme) => scheme switch
        {
            "Dreamweaver CS6" => Cs6,
            "Modern Dark" => Modern,
            "2077" => Neon,
            _ => null,
        };

        public static void Apply(string scheme)
        {
            var app = Application.Current;
            if (app == null) return;
            var r = app.Resources;
            CollectOriginals(r);
            foreach (var key in Overridden.ToList()) r.Remove(key);
            Overridden.Clear();
            var map = MapFor(scheme);
            if (map == null) return;
            foreach (var kv in Originals)
            {
                var ck = scheme + "|" + kv.Key;
                if (!Cache.TryGetValue(ck, out var img))
                {
                    try
                    {
                        var d = kv.Value.Drawing.Clone();
                        Recolor(d, map);
                        var di = new DrawingImage(d);
                        di.Freeze();
                        img = di;
                    }
                    catch { img = kv.Value; }
                    Cache[ck] = img;
                }
                r[kv.Key] = img;
                Overridden.Add(kv.Key);
            }
        }

        /// <summary>The icon as designed (before recolouring), or null if it isn't a suite icon.</summary>
        public static ImageSource Original(string key)
        {
            if (key == null) return null;
            if (Originals.Count == 0 && Application.Current != null) CollectOriginals(Application.Current.Resources);
            return Originals.TryGetValue(key, out var o) ? o : null;
        }

        /// <summary>Recolours any image (for icons an app builds itself); returns it unchanged for the Office schemes.</summary>
        public static ImageSource Themed(ImageSource src)
        {
            var map = MapFor(OfficeTheme.Current);
            if (map == null || !(src is DrawingImage di)) return src;
            var d = di.Drawing.Clone();
            Recolor(d, map);
            var o = new DrawingImage(d);
            o.Freeze();
            return o;
        }

        static void CollectOriginals(ResourceDictionary dict)
        {
            foreach (var md in dict.MergedDictionaries) CollectOriginals(md);
            if (ReferenceEquals(dict, Application.Current.Resources)) return; // the top level holds our recoloured copies
            foreach (var key in dict.Keys.OfType<string>())
            {
                if (!key.StartsWith("Icon", StringComparison.Ordinal)) continue;
                if (dict[key] is DrawingImage img) Originals[key] = img; // later dictionaries win, like resource lookup
            }
        }

        // ------------------------------------------------------------------ walking a drawing
        static void Recolor(Drawing d, Func<Color, Color> map)
        {
            switch (d)
            {
                case DrawingGroup g:
                    foreach (var c in g.Children) Recolor(c, map);
                    if (g.OpacityMask != null) g.OpacityMask = Brush(g.OpacityMask, c => c);
                    break;
                case GeometryDrawing gd:
                    if (gd.Brush != null) gd.Brush = Brush(gd.Brush, map);
                    if (gd.Pen?.Brush != null) { var p = gd.Pen.Clone(); p.Brush = Brush(p.Brush, map); gd.Pen = p; }
                    break;
                case GlyphRunDrawing gr:
                    if (gr.ForegroundBrush != null) gr.ForegroundBrush = Brush(gr.ForegroundBrush, map);
                    break;
            }
        }

        static Brush Brush(Brush b, Func<Color, Color> map)
        {
            switch (b)
            {
                case SolidColorBrush s:
                    return new SolidColorBrush(map(s.Color)) { Opacity = s.Opacity };
                case GradientBrush g:
                    var c = g.Clone();
                    foreach (var st in c.GradientStops) st.Color = map(st.Color);
                    return c;
                default:
                    return b;
            }
        }

        // ------------------------------------------------------------------ colour maps
        static void Hsl(Color c, out double h, out double s, out double l)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2; h = 0; s = 0;
            if (max == min) return;
            double d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h *= 60;
        }

        static double Lum(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

        static Color Mix(Color a, Color b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }

        /// <summary>Piecewise gradient through (position, colour) stops.</summary>
        static Color Ramp(double x, params (double At, Color C)[] stops)
        {
            if (x <= stops[0].At) return stops[0].C;
            for (int i = 1; i < stops.Length; i++)
                if (x <= stops[i].At) return Mix(stops[i - 1].C, stops[i].C, (x - stops[i - 1].At) / (stops[i].At - stops[i - 1].At));
            return stops[^1].C;
        }

        static Color Hex(string h) => (Color)ColorConverter.ConvertFromString(h);
        static Color KeepAlpha(Color src, Color c) => Color.FromArgb(src.A, c.R, c.G, c.B);
        static bool Warm(double h) => h >= 20 && h <= 70;

        static Color Cs6(Color c)
        {
            if (c.A == 0) return c;
            var grey = (byte)Math.Round(Lum(c) * 255);
            var g = Color.FromRgb(grey, grey, grey);
            // CS6's icons: mostly grey, a little of the original colour left, slightly cool
            var m = Mix(g, Color.FromRgb(c.R, c.G, c.B), 0.35);
            return KeepAlpha(c, Mix(m, Hex("#FF5A7FA8"), 0.06));
        }

        static readonly Color MOutline = Hex("#FFE4E7EE"), MMid = Hex("#FF9AA1AE"), MFill = Hex("#FF30343D"), MTeal = Hex("#FF36C2B4"), MGold = Hex("#FFE5C07B");
        static Color Modern(Color c)
        {
            if (c.A == 0) return c;
            Hsl(c, out var h, out var s, out var l);
            double lum = Lum(c);
            if (s > 0.35 && l > 0.15 && l < 0.92)
            {
                var accent = Warm(h) ? MGold : MTeal;
                // darker shades of a colour stay a bit darker, so gradients keep some depth
                return KeepAlpha(c, Mix(Mix(accent, MFill, 0.45), accent, Math.Clamp(lum * 1.3, 0, 1)));
            }
            // greys: dark outlines become light, white fills become the dark group colour
            return KeepAlpha(c, Ramp(lum, (0.0, MOutline), (0.55, MMid), (0.8, Mix(MMid, MFill, 0.6)), (1.0, MFill)));
        }

        static readonly Color NCyan = Hex("#FF2DE2E6"), NPink = Hex("#FFFF2592"), NYellow = Hex("#FFFFE600"), NFill = Hex("#FF2A1366"), NDeep = Hex("#FF160F45");
        static Color Neon(Color c)
        {
            if (c.A == 0) return c;
            Hsl(c, out var h, out var s, out var l);
            double lum = Lum(c);
            if (s > 0.35 && l > 0.15 && l < 0.92)
            {
                var accent = Warm(h) ? NYellow : NPink;
                return KeepAlpha(c, Mix(Mix(accent, NDeep, 0.5), accent, Math.Clamp(lum * 1.3, 0, 1)));
            }
            return KeepAlpha(c, Ramp(lum, (0.0, NCyan), (0.5, Mix(NCyan, NFill, 0.35)), (0.8, NFill), (1.0, NFill)));
        }
    }
}
