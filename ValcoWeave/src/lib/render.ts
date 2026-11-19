// Builds the document shown in Design / Live view: tags every element with its
// map id and swaps site-relative resources (CSS, images, fonts, scripts) for blob URLs,
// so pages render exactly as they would from disk — including unsaved CSS edits.
import { isElement, parseDoc, serializeDoc, walkElements, type P5Node } from './htmlmap';
import { dirname, mimeFor, resolveUrl, isImage } from './paths';

export interface RenderCtx {
  docPath: string;
  siteRoot: string | null;
  /** Text of a file, preferring unsaved editor buffers. */
  readText(path: string): Promise<string | null>;
  readBytes(path: string): Promise<Uint8Array | null>;
}

export interface RenderResult {
  html: string;
  urls: string[];
}

const binCache = new Map<string, string>();

export function invalidateAsset(path?: string) {
  if (!path) {
    for (const u of binCache.values()) URL.revokeObjectURL(u);
    binCache.clear();
    return;
  }
  const u = binCache.get(path);
  if (u) {
    URL.revokeObjectURL(u);
    binCache.delete(path);
  }
}

const ORIG = 'data-wc-o-';
export const NO_ATTR = '\u0000';

function setAttr(el: P5Node, name: string, value: string) {
  const a = el.attrs.find((x: { name: string }) => x.name === name);
  if (a) a.value = value;
  else el.attrs.push({ name, value });
}
function getAttr(el: P5Node, name: string): string | null {
  const a = el.attrs.find((x: { name: string }) => x.name === name);
  return a ? a.value : null;
}

export async function renderDocument(src: string, mode: 'design' | 'live', ctx: RenderCtx): Promise<RenderResult> {
  const doc = parseDoc(src);
  const urls: string[] = [];
  const docDir = dirname(ctx.docPath);

  const binUrl = async (path: string): Promise<string | null> => {
    const hit = binCache.get(path);
    if (hit) return hit;
    const bytes = await ctx.readBytes(path);
    if (!bytes) return null;
    const url = URL.createObjectURL(new Blob([bytes as BlobPart], { type: mimeFor(path) }));
    binCache.set(path, url);
    return url;
  };
  const textUrl = async (text: string, mime: string) => {
    const url = URL.createObjectURL(new Blob([text], { type: mime }));
    urls.push(url);
    return url;
  };

  const rewriteCss = async (css: string, cssDir: string, depth = 0): Promise<string> => {
    const re = /url\(\s*(['"]?)([^'")]+)\1\s*\)|@import\s+(['"])([^'"]+)\3/g;
    const jobs: Promise<[string, string]>[] = [];
    let m: RegExpExecArray | null;
    while ((m = re.exec(css))) {
      const whole = m[0];
      const isImport = !!m[4];
      const raw = (m[2] ?? m[4]).trim();
      const path = resolveUrl(raw, cssDir, ctx.siteRoot);
      if (!path) continue;
      jobs.push(
        (async (): Promise<[string, string]> => {
          if (/\.css$/i.test(path) && depth < 4 && (isImport || /@import/.test(css.slice(Math.max(0, css.indexOf(whole) - 10), css.indexOf(whole))))) {
            const t = await ctx.readText(path);
            if (t === null) return [whole, whole];
            const u = await textUrl(await rewriteCss(t, dirname(path), depth + 1), 'text/css');
            return [whole, isImport ? `@import "${u}"` : `url("${u}")`];
          }
          const u = await binUrl(path);
          return [whole, u ? (isImport ? `@import "${u}"` : `url("${u}")`) : whole];
        })(),
      );
    }
    let out = css;
    for (const [a, b] of await Promise.all(jobs)) out = out.split(a).join(b);
    return out;
  };

  const tasks: Promise<void>[] = [];
  let id = 0;
  const remember = (el: P5Node, name: string) => {
    if (mode !== 'design') return;
    const orig = getAttr(el, name);
    setAttr(el, ORIG + name, orig === null ? NO_ATTR : orig);
  };

  walkElements(doc, (el) => {
    const myId = id++;
    if (mode === 'design') el.attrs.push({ name: 'data-wc-id', value: String(myId) });
    const tag = el.tagName;

    const swapBin = (attr: string) => {
      const v = getAttr(el, attr);
      if (!v) return;
      const path = resolveUrl(v, docDir, ctx.siteRoot);
      if (!path) return;
      tasks.push(
        binUrl(path).then((u) => {
          if (u) {
            remember(el, attr);
            setAttr(el, attr, u);
          }
        }),
      );
    };

    if (['img', 'source', 'video', 'audio', 'embed', 'track', 'input'].includes(tag)) swapBin('src');
    if (tag === 'video') swapBin('poster');
    if (tag === 'object') swapBin('data');
    if ((tag === 'img' || tag === 'source') && getAttr(el, 'srcset')) {
      const v = getAttr(el, 'srcset')!;
      tasks.push(
        (async () => {
          const parts = await Promise.all(
            v.split(',').map(async (cand) => {
              const [u, ...rest] = cand.trim().split(/\s+/);
              const path = resolveUrl(u, docDir, ctx.siteRoot);
              const b = path ? await binUrl(path) : null;
              return [b ?? u, ...rest].join(' ');
            }),
          );
          remember(el, 'srcset');
          setAttr(el, 'srcset', parts.join(', '));
        })(),
      );
    }
    if (tag === 'link') {
      const rel = (getAttr(el, 'rel') ?? '').toLowerCase();
      const href = getAttr(el, 'href');
      const path = href ? resolveUrl(href, docDir, ctx.siteRoot) : null;
      if (path && rel.includes('stylesheet')) {
        tasks.push(
          (async () => {
            const t = await ctx.readText(path);
            if (t === null) return;
            const u = await textUrl(await rewriteCss(t, dirname(path)), 'text/css');
            remember(el, 'href');
            setAttr(el, 'href', u);
          })(),
        );
      } else if (path && rel.includes('icon') && isImage(path)) {
        swapBin('href');
      }
    }
    if (tag === 'script') {
      const s = getAttr(el, 'src');
      if (mode === 'design') {
        remember(el, 'type');
        setAttr(el, 'type', 'text/x-weavecraft-disabled');
      } else if (s) {
        const path = resolveUrl(s, docDir, ctx.siteRoot);
        if (path) {
          tasks.push(
            (async () => {
              const t = await ctx.readText(path);
              if (t === null) return;
              setAttr(el, 'src', await textUrl(t, 'text/javascript'));
            })(),
          );
        }
      }
    }
    const style = getAttr(el, 'style');
    if (style && /url\(/i.test(style)) {
      tasks.push(
        rewriteCss(style, docDir).then((s) => {
          remember(el, 'style');
          setAttr(el, 'style', s);
        }),
      );
    }
    if (tag === 'style') {
      for (const c of el.childNodes ?? []) {
        if (c.nodeName === '#text' && /url\(|@import/i.test(c.value)) {
          tasks.push(
            rewriteCss(c.value, docDir).then((s) => {
              c.value = s;
            }),
          );
        }
      }
    }
  });

  await Promise.all(tasks);
  return { html: serializeDoc(doc), urls };
}

/** Strip every editor-only attribute from a live DOM subtree (restoring originals). */
export function cleanEditorAttrs(root: Element) {
  const all = [root, ...Array.from(root.querySelectorAll('*'))];
  for (const el of all) {
    for (const a of Array.from(el.attributes)) {
      if (a.name.startsWith(ORIG)) {
        const real = a.name.slice(ORIG.length);
        if (a.value === NO_ATTR) el.removeAttribute(real);
        else el.setAttribute(real, a.value);
        el.removeAttribute(a.name);
      }
    }
    for (const a of Array.from(el.attributes)) {
      if (a.name.startsWith('data-wc-') || a.name === 'contenteditable' || a.name === 'spellcheck') {
        el.removeAttribute(a.name);
      }
    }
  }
}

export { isElement };
