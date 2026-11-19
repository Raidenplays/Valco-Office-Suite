// Path helpers. Internally every path uses forward slashes.

export function norm(p: string): string {
  p = p.replace(/\\/g, '/');
  const prefixMatch = p.match(/^([a-z]+:\/+|[A-Za-z]:\/|\/)/);
  const prefix = prefixMatch ? prefixMatch[0] : '';
  const rest = p.slice(prefix.length);
  const out: string[] = [];
  for (const seg of rest.split('/')) {
    if (!seg || seg === '.') continue;
    if (seg === '..') {
      if (out.length) out.pop();
      continue;
    }
    out.push(seg);
  }
  return prefix + out.join('/');
}

export function join(...parts: string[]): string {
  return norm(parts.filter(Boolean).join('/'));
}

export function dirname(p: string): string {
  p = norm(p);
  const i = p.lastIndexOf('/');
  if (i < 0) return '';
  if (i === 0) return '/';
  const d = p.slice(0, i);
  return /^[A-Za-z]:$/.test(d) ? d + '/' : d;
}

export function basename(p: string): string {
  p = norm(p);
  return p.slice(p.lastIndexOf('/') + 1);
}

export function ext(p: string): string {
  const b = basename(p);
  const i = b.lastIndexOf('.');
  return i > 0 ? b.slice(i + 1).toLowerCase() : '';
}

/** Relative path from directory `fromDir` to file `to`. */
export function relative(fromDir: string, to: string): string {
  const a = norm(fromDir).split('/').filter(Boolean);
  const b = norm(to).split('/').filter(Boolean);
  let i = 0;
  while (i < a.length && i < b.length && a[i] === b[i]) i++;
  const up = a.slice(i).map(() => '..');
  return [...up, ...b.slice(i)].join('/') || '.';
}

export function isInside(root: string, p: string): boolean {
  const r = norm(root).replace(/\/$/, '');
  const n = norm(p);
  return n === r || n.startsWith(r + '/');
}

/** True for URLs that point off-site or aren't files at all. */
export function isExternalUrl(u: string): boolean {
  return /^(?:[a-z][a-z0-9+.-]*:|\/\/|#)/i.test(u.trim()) || u.trim() === '';
}

/** Resolve a URL as written in a page to a site file path, or null for external URLs. */
export function resolveUrl(url: string, fromDir: string, siteRoot: string | null): string | null {
  url = url.trim();
  if (isExternalUrl(url)) return null;
  let clean = url.split('#')[0].split('?')[0];
  try {
    clean = decodeURIComponent(clean);
  } catch {
    /* keep raw */
  }
  if (!clean) return null;
  if (clean.startsWith('/')) return join(siteRoot ?? fromDir, clean);
  return join(fromDir, clean);
}

export const TEXT_EXT = new Set([
  'html', 'htm', 'xhtml', 'css', 'scss', 'less', 'js', 'mjs', 'cjs', 'ts', 'jsx', 'tsx', 'json',
  'md', 'markdown', 'txt', 'xml', 'svg', 'php', 'yml', 'yaml', 'toml', 'csv', 'htaccess', 'ini', 'env',
]);
export const IMAGE_EXT = new Set(['png', 'jpg', 'jpeg', 'gif', 'webp', 'svg', 'ico', 'bmp', 'avif']);

export function isTextFile(p: string): boolean {
  const e = ext(p);
  return TEXT_EXT.has(e) || e === '' || basename(p).startsWith('.');
}
export function isHtml(p: string): boolean {
  return ['html', 'htm', 'xhtml', 'php'].includes(ext(p));
}
export function isImage(p: string): boolean {
  return IMAGE_EXT.has(ext(p));
}

export function mimeFor(p: string): string {
  const e = ext(p);
  const map: Record<string, string> = {
    html: 'text/html', htm: 'text/html', css: 'text/css', js: 'text/javascript', mjs: 'text/javascript',
    json: 'application/json', svg: 'image/svg+xml', png: 'image/png', jpg: 'image/jpeg', jpeg: 'image/jpeg',
    gif: 'image/gif', webp: 'image/webp', ico: 'image/x-icon', bmp: 'image/bmp', avif: 'image/avif',
    woff: 'font/woff', woff2: 'font/woff2', ttf: 'font/ttf', otf: 'font/otf', mp4: 'video/mp4',
    webm: 'video/webm', mp3: 'audio/mpeg', ogg: 'audio/ogg', wav: 'audio/wav', txt: 'text/plain',
    xml: 'application/xml', pdf: 'application/pdf',
  };
  return map[e] ?? 'application/octet-stream';
}
