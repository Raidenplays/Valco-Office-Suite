// Recolours the ribbon's SVG icons for the theme in use, the same way ValcoCommon/IconTheme.cs does
// for the WPF apps: Dreamweaver CS6 gets muted grey icons, Modern (dark) light outline icons with
// teal and soft-gold accents, 2077 neon cyan outlines with pink and yellow accents.
// Vista keeps the original Office 2007-style icons.

type RGB = [number, number, number];
type Mapper = (c: RGB) => RGB;

const hex = (h: string): RGB => [parseInt(h.slice(1, 3), 16), parseInt(h.slice(3, 5), 16), parseInt(h.slice(5, 7), 16)];
const lum = ([r, g, b]: RGB) => (0.299 * r + 0.587 * g + 0.114 * b) / 255;
const mix = (a: RGB, b: RGB, t: number): RGB => {
  t = Math.min(1, Math.max(0, t));
  return [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t].map((v) => Math.round(v)) as RGB;
};
function hsl([r, g, b]: RGB): [number, number, number] {
  r /= 255; g /= 255; b /= 255;
  const max = Math.max(r, g, b), min = Math.min(r, g, b);
  const l = (max + min) / 2;
  if (max === min) return [0, 0, l];
  const d = max - min;
  const s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
  let h = max === r ? (g - b) / d + (g < b ? 6 : 0) : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
  return [h * 60, s, l];
}
function ramp(x: number, stops: [number, RGB][]): RGB {
  if (x <= stops[0][0]) return stops[0][1];
  for (let i = 1; i < stops.length; i++) if (x <= stops[i][0]) return mix(stops[i - 1][1], stops[i][1], (x - stops[i - 1][0]) / (stops[i][0] - stops[i - 1][0]));
  return stops[stops.length - 1][1];
}
const warm = (h: number) => h >= 20 && h <= 70;

const cs6: Mapper = (c) => {
  const g = Math.round(lum(c) * 255);
  return mix(mix([g, g, g], c, 0.35), hex('#5a7fa8'), 0.06);
};
function duo(outline: RGB, mid: RGB, fill: RGB, deep: RGB, accentWarm: RGB, accentCool: RGB, deepMix: number): Mapper {
  return (c) => {
    const [h, s, l] = hsl(c);
    const y = lum(c);
    if (s > 0.35 && l > 0.15 && l < 0.92) {
      const a = warm(h) ? accentWarm : accentCool;
      return mix(mix(a, deep, deepMix), a, Math.min(1, y * 1.3));
    }
    return ramp(y, [[0, outline], [0.55, mid], [0.8, mix(mid, fill, 0.6)], [1, fill]]);
  };
}
const modern = duo(hex('#e4e7ee'), hex('#9aa1ae'), hex('#30343d'), hex('#30343d'), hex('#e5c07b'), hex('#36c2b4'), 0.45);
const neon: Mapper = (() => {
  const cyan = hex('#2de2e6'), fill = hex('#2a1366'), deep = hex('#160f45');
  return (c: RGB) => {
    const [h, s, l] = hsl(c);
    const y = lum(c);
    if (s > 0.35 && l > 0.15 && l < 0.92) {
      const a = warm(h) ? hex('#ffe600') : hex('#ff2592');
      return mix(mix(a, deep, 0.5), a, Math.min(1, y * 1.3));
    }
    return ramp(y, [[0, cyan], [0.5, mix(cyan, fill, 0.35)], [0.8, fill], [1, fill]]);
  };
})();

/** The mapping for the current <html data-theme/data-mode>, or null for the original icons. */
export function currentIconMapper(): Mapper | null {
  const d = document.documentElement.dataset;
  if (d.theme === '2077') return neon;
  if (d.theme === 'dark' && d.mode === 'dark') return modern;
  if (d.theme === 'cs6') return d.mode === 'dark' ? modern : cs6;
  return null;
}

function parse(v: string): { rgb: RGB; a: string } | null {
  v = v.trim();
  let m = /^#([0-9a-f]{3})$/i.exec(v);
  if (m) return { rgb: m[1].split('').map((x) => parseInt(x + x, 16)) as RGB, a: '' };
  m = /^#([0-9a-f]{6})$/i.exec(v);
  if (m) return { rgb: hex(v), a: '' };
  m = /^rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*([\d.]+)\s*)?\)$/i.exec(v);
  if (m) return { rgb: [+m[1], +m[2], +m[3]], a: m[4] ?? '' };
  return null;
}
const out = ({ rgb, a }: { rgb: RGB; a: string }) => (a ? `rgba(${rgb.join(',')},${a})` : '#' + rgb.map((x) => x.toString(16).padStart(2, '0')).join(''));

const ATTRS = ['fill', 'stroke', 'stop-color'];

/** Recolours one icon's SVG in place; the original colours are kept in data-o-* so it can switch back. */
export function themeIcon(svg: SVGSVGElement | null) {
  if (!svg) return;
  const map = currentIconMapper();
  svg.querySelectorAll('*').forEach((el) => {
    for (const attr of ATTRS) {
      const key = 'data-o-' + attr;
      let orig = el.getAttribute(key);
      if (orig === null) {
        const v = el.getAttribute(attr);
        if (v === null) continue;
        el.setAttribute(key, v);
        orig = v;
      }
      const c = map ? parse(orig) : null;
      el.setAttribute(attr, c && map ? out({ rgb: map(c.rgb), a: c.a }) : orig);
    }
  });
}

// one observer for the whole page: icons subscribe and are recoloured when the theme changes
const subscribers = new Set<() => void>();
let observer: MutationObserver | null = null;
export function onThemeChange(fn: () => void) {
  subscribers.add(fn);
  if (!observer) {
    observer = new MutationObserver(() => subscribers.forEach((f) => f()));
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme', 'data-mode', 'data-scheme'] });
  }
  return () => { subscribers.delete(fn); };
}
