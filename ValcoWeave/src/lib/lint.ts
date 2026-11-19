import { getAttr, type HtmlMap } from './htmlmap';
import { dirname, resolveUrl } from './paths';

export interface Problem {
  severity: 'error' | 'warning' | 'info';
  message: string;
  from: number;
  to: number;
  line: number;
}

const pretty = (code: string) => {
  const s = code.replace(/-/g, ' ');
  return s.charAt(0).toUpperCase() + s.slice(1);
};

function lineAt(src: string, off: number) {
  let n = 1;
  for (let i = 0; i < off && i < src.length; i++) if (src.charCodeAt(i) === 10) n++;
  return n;
}

export function lintHtml(map: HtmlMap, docPath: string, siteRoot: string | null, siteFiles: Set<string> | null): Problem[] {
  const src = map.source;
  const out: Problem[] = [];
  for (const p of map.problems) {
    out.push({ severity: 'warning', message: pretty(p.code), from: p.offset, to: p.offset + 1, line: p.line });
  }
  const ids = new Map<string, number>();
  const docDir = dirname(docPath);
  for (const e of map.els) {
    if (!e.hasLoc) continue;
    const at = (msg: string, sev: Problem['severity'] = 'warning') =>
      out.push({ severity: sev, message: msg, from: e.start, to: e.startTagEnd, line: e.line });
    const id = getAttr(e, 'id');
    if (id) {
      if (ids.has(id)) at(`Duplicate id "${id}"`, 'error');
      else ids.set(id, e.start);
    }
    if (e.tag === 'img' && getAttr(e, 'alt') === null) at('Image is missing alt text (accessibility)');
    if (e.tag === 'a' && getAttr(e, 'href') === '') at('Link has an empty href');
    if (siteFiles) {
      const refAttr = e.tag === 'a' || e.tag === 'link' ? 'href' : ['img', 'script', 'source', 'video', 'audio', 'iframe'].includes(e.tag) ? 'src' : null;
      const v = refAttr ? getAttr(e, refAttr) : null;
      if (v) {
        const path = resolveUrl(v, docDir, siteRoot);
        if (path && !siteFiles.has(path) && !siteFiles.has(path.replace(/\/$/, '') + '/index.html')) {
          at(`Broken link: "${v}" was not found in the site`);
        }
      }
    }
  }
  const html = map.els.find((e) => e.tag === 'html');
  if (html && html.hasLoc && getAttr(html, 'lang') === null) {
    out.push({ severity: 'info', message: 'Add a lang attribute to <html> (e.g. lang="en")', from: html.start, to: html.startTagEnd, line: html.line });
  }
  const title = map.els.find((e) => e.tag === 'title');
  if (!title && src.trim().length > 0) {
    out.push({ severity: 'info', message: 'Document has no <title>', from: 0, to: 0, line: 1 });
  }
  for (const p of out) if (!p.line) p.line = lineAt(src, p.from);
  return out.sort((a, b) => a.from - b.from);
}
