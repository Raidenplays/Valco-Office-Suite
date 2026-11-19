// Maps an HTML source string to an element table with exact source offsets,
// so visual edits can be applied as minimal text patches (formatting is kept).
import { parse, serialize } from 'parse5';

/* eslint-disable @typescript-eslint/no-explicit-any */
export type P5Node = any;

export interface Attr {
  name: string;
  value: string;
}

export interface ElInfo {
  id: number;
  tag: string;
  attrs: Attr[];
  /** Whole element range in source. */
  start: number;
  end: number;
  /** Inner content range [innerStart, innerEnd). */
  innerStart: number;
  innerEnd: number;
  startTagEnd: number;
  attrLocs: Record<string, { start: number; end: number }>;
  hasLoc: boolean;
  hasEndTag: boolean;
  isVoid: boolean;
  parent: number | null;
  children: number[];
  depth: number;
  line: number;
}

export interface ParseProblem {
  code: string;
  offset: number;
  line: number;
  col: number;
}

export interface HtmlMap {
  els: ElInfo[];
  roots: number[];
  problems: ParseProblem[];
  source: string;
}

export const VOID_TAGS = new Set([
  'area', 'base', 'br', 'col', 'embed', 'hr', 'img', 'input', 'link', 'meta', 'param', 'source', 'track', 'wbr',
]);

export function isElement(n: P5Node): boolean {
  return n && typeof n.tagName === 'string';
}

function kids(n: P5Node): P5Node[] {
  if (n.nodeName === 'template' && n.content) return n.content.childNodes ?? [];
  return n.childNodes ?? [];
}

/** Walk elements in document (pre-)order. */
export function walkElements(n: P5Node, fn: (el: P5Node, depth: number) => void, depth = 0) {
  for (const c of kids(n)) {
    if (isElement(c)) {
      fn(c, depth);
      walkElements(c, fn, depth + 1);
    }
  }
}

export function parseDoc(src: string, problems?: ParseProblem[]): P5Node {
  return parse(src, {
    sourceCodeLocationInfo: true,
    onParseError: problems
      ? (e: any) => problems.push({ code: e.code, offset: e.startOffset, line: e.startLine, col: e.startCol })
      : undefined,
  } as any);
}

export function buildMap(src: string): { map: HtmlMap; doc: P5Node } {
  const problems: ParseProblem[] = [];
  const doc = parseDoc(src, problems);
  const els: ElInfo[] = [];
  const roots: number[] = [];

  const visit = (n: P5Node, parent: number | null, depth: number) => {
    for (const c of kids(n)) {
      if (!isElement(c)) continue;
      const id = els.length;
      const loc = c.sourceCodeLocation;
      const info: ElInfo = {
        id,
        tag: c.tagName,
        attrs: (c.attrs ?? []).map((a: Attr) => ({ name: a.name, value: a.value })),
        start: 0,
        end: 0,
        innerStart: 0,
        innerEnd: 0,
        startTagEnd: 0,
        attrLocs: {},
        hasLoc: !!loc,
        hasEndTag: !!loc?.endTag,
        isVoid: VOID_TAGS.has(c.tagName),
        parent,
        children: [],
        depth,
        line: loc?.startLine ?? 0,
      };
      els.push(info);
      c.__wcId = id;
      if (parent === null) roots.push(id);
      else els[parent].children.push(id);
      if (loc) {
        info.start = loc.startOffset;
        info.end = loc.endOffset;
        const st = loc.startTag ?? { startOffset: loc.startOffset, endOffset: loc.endOffset };
        info.startTagEnd = st.endOffset;
        info.innerStart = st.endOffset;
        info.innerEnd = loc.endTag ? loc.endTag.startOffset : loc.endOffset;
        for (const [k, v] of Object.entries<any>(loc.attrs ?? {})) {
          info.attrLocs[k] = { start: v.startOffset, end: v.endOffset };
        }
      }
      visit(c, id, depth + 1);
    }
  };
  visit(doc, null, 0);

  // Implicit elements (html/head/body the author left out) get ranges from their children.
  for (let i = els.length - 1; i >= 0; i--) {
    const e = els[i];
    if (e.hasLoc) continue;
    const ch = e.children.map((c) => els[c]).filter((c) => c.hasLoc || c.end > 0);
    if (ch.length) {
      e.start = Math.min(...ch.map((c) => c.start));
      e.end = Math.max(...ch.map((c) => c.end));
    } else {
      // Empty implicit element: place it at the end of its parent or document.
      e.start = e.end = src.length;
    }
    e.innerStart = e.startTagEnd = e.start;
    e.innerEnd = e.end;
  }
  // body implicit but has text after children -> extend to EOF for insertion purposes
  return { map: { els, roots, problems, source: src }, doc };
}

/** Deepest element whose range contains `offset`. */
export function elementAt(map: HtmlMap, offset: number): ElInfo | null {
  let best: ElInfo | null = null;
  for (const e of map.els) {
    if (!e.hasLoc) continue;
    if (offset >= e.start && offset <= e.end && (!best || e.depth >= best.depth)) best = e;
  }
  return best;
}

export function findByStart(map: HtmlMap, start: number, tag?: string): ElInfo | null {
  let hit: ElInfo | null = null;
  for (const e of map.els) {
    if (e.hasLoc && e.start === start && (!tag || e.tag === tag)) {
      if (!hit || e.depth > hit.depth) hit = e;
    }
  }
  return hit;
}

export function ancestors(map: HtmlMap, id: number): ElInfo[] {
  const out: ElInfo[] = [];
  let cur: number | null = id;
  while (cur !== null) {
    out.unshift(map.els[cur]);
    cur = map.els[cur].parent;
  }
  return out;
}

export function getAttr(e: ElInfo, name: string): string | null {
  const a = e.attrs.find((x) => x.name === name);
  return a ? a.value : null;
}

export function findTag(map: HtmlMap, tag: string): ElInfo | null {
  return map.els.find((e) => e.tag === tag) ?? null;
}

export function escAttr(v: string): string {
  return v.replace(/&/g, '&amp;').replace(/"/g, '&quot;');
}
export function escText(v: string): string {
  return v.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

export interface Patch {
  from: number;
  to: number;
  insert: string;
}

/** Patch that sets (or removes, when value is null) one attribute, touching only that attribute. */
export function attrPatch(src: string, e: ElInfo, name: string, value: string | null): Patch | null {
  if (!e.hasLoc) return null;
  const loc = e.attrLocs[name];
  const text = value === null ? '' : value === '' && BOOLEAN_ATTRS.has(name) ? name : `${name}="${escAttr(value)}"`;
  if (loc) {
    if (value === null) {
      // remove the attribute and the whitespace before it
      let from = loc.start;
      while (from > e.start && /\s/.test(src[from - 1])) from--;
      return { from, to: loc.end, insert: '' };
    }
    return { from: loc.start, to: loc.end, insert: text };
  }
  if (value === null) return null;
  // Add: before the closing ">" or "/>" of the start tag
  let at = e.startTagEnd - 1;
  if (src[at - 1] === '/') at--;
  while (at > e.start && /\s/.test(src[at - 1])) at--;
  return { from: at, to: at, insert: ' ' + text };
}

const BOOLEAN_ATTRS = new Set([
  'disabled', 'checked', 'readonly', 'required', 'hidden', 'autoplay', 'controls', 'loop', 'muted',
  'multiple', 'selected', 'async', 'defer', 'novalidate', 'autofocus', 'open', 'playsinline',
]);
export function isBooleanAttr(n: string) {
  return BOOLEAN_ATTRS.has(n);
}

/** Patches that rename an element's tag (start + end tag). Returned in descending order. */
export function renamePatches(src: string, e: ElInfo, newTag: string): Patch[] {
  if (!e.hasLoc) return [];
  const out: Patch[] = [];
  if (e.hasEndTag) {
    const endTagStart = e.innerEnd;
    const m = src.slice(endTagStart).match(/^<\/\s*([A-Za-z0-9-]+)/);
    if (m) out.push({ from: endTagStart + 2, to: endTagStart + 2 + m[1].length, insert: newTag });
  }
  const m2 = src.slice(e.start).match(/^<([A-Za-z0-9-]+)/);
  if (m2) out.push({ from: e.start + 1, to: e.start + 1 + m2[1].length, insert: newTag });
  return out;
}

/** Leading indentation of the line the element starts on. */
export function indentOf(src: string, offset: number): string {
  const ls = src.lastIndexOf('\n', offset - 1) + 1;
  const m = src.slice(ls, offset).match(/^[ \t]*/);
  return m ? m[0] : '';
}

/** Re-indent a (possibly multi-line) snippet so it can be inserted at the given indent. */
export function indentSnippet(snippet: string, indent: string): string {
  return snippet
    .split('\n')
    .map((l, i) => (i === 0 || !l.trim() ? l : indent + l))
    .join('\n');
}

export function serializeDoc(doc: P5Node): string {
  return serialize(doc);
}

/** Pick a short CSS selector suggestion for an element. */
export function suggestSelector(e: ElInfo): string {
  const id = getAttr(e, 'id');
  if (id) return '#' + id;
  const cls = getAttr(e, 'class')?.trim().split(/\s+/).filter(Boolean);
  if (cls && cls.length) return '.' + cls[0];
  return e.tag;
}

export function label(e: ElInfo): string {
  let s = e.tag;
  const id = getAttr(e, 'id');
  if (id) s += '#' + id;
  const cls = getAttr(e, 'class')?.trim().split(/\s+/).filter(Boolean);
  if (cls?.length) s += '.' + cls.join('.');
  return s;
}
