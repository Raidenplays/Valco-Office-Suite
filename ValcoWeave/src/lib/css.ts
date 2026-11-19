// CSS Designer model built on postcss (keeps the author's formatting for untouched rules).
import postcss, { type Rule, type Root, type AtRule } from 'postcss';

export interface Decl {
  prop: string;
  value: string;
}
export interface RuleInfo {
  idx: number;
  selector: string;
  selectors: string[];
  media: string | null;
  decls: Decl[];
  line: number;
}

function parse(text: string): Root | null {
  try {
    return postcss.parse(text);
  } catch {
    return null;
  }
}

function inKeyframes(r: Rule): boolean {
  let p = r.parent as AtRule | Root | undefined;
  while (p && p.type === 'atrule') {
    if (/keyframes$/i.test((p as AtRule).name)) return true;
    p = p.parent as AtRule | Root | undefined;
  }
  return false;
}

function mediaOf(r: Rule): string | null {
  const parts: string[] = [];
  let p = r.parent as AtRule | Root | undefined;
  while (p && p.type === 'atrule') {
    const a = p as AtRule;
    parts.unshift(`@${a.name} ${a.params}`);
    p = p.parent as AtRule | Root | undefined;
  }
  return parts.length ? parts.join(' ') : null;
}

export function parseRules(text: string): { rules: RuleInfo[]; error: string | null } {
  let root: Root;
  try {
    root = postcss.parse(text);
  } catch (e) {
    return { rules: [], error: (e as Error).message };
  }
  const rules: RuleInfo[] = [];
  let idx = 0;
  root.walkRules((r) => {
    const i = idx++;
    if (inKeyframes(r)) return;
    const decls: Decl[] = [];
    r.each((n) => {
      if (n.type === 'decl') decls.push({ prop: n.prop, value: n.value + (n.important ? ' !important' : '') });
    });
    rules.push({
      idx: i,
      selector: r.selector,
      selectors: r.selectors,
      media: mediaOf(r),
      decls,
      line: r.source?.start?.line ?? 0,
    });
  });
  return { rules, error: null };
}

function ruleAt(root: Root, idx: number): Rule | null {
  let i = 0;
  let hit: Rule | null = null;
  root.walkRules((r) => {
    if (i++ === idx) {
      hit = r;
      return false;
    }
  });
  return hit;
}

function splitImportant(v: string): { value: string; important: boolean } {
  const m = v.match(/^(.*?)\s*!important\s*$/i);
  return m ? { value: m[1], important: true } : { value: v.trim(), important: false };
}

/** Set, rename or remove (value === null) a declaration. */
export function setDecl(text: string, ruleIdx: number, prop: string, value: string | null, oldProp?: string): string {
  const root = parse(text);
  if (!root) return text;
  const r = ruleAt(root, ruleIdx);
  if (!r) return text;
  let target: postcss.Declaration | undefined;
  r.each((n) => {
    if (n.type === 'decl' && n.prop === (oldProp ?? prop)) target = n;
  });
  if (value === null) {
    target?.remove();
  } else {
    const { value: v, important } = splitImportant(value);
    if (target) {
      target.prop = prop;
      target.value = v;
      target.important = important;
    } else {
      r.append({ prop, value: v, important });
    }
  }
  return root.toString();
}

export function addRule(text: string, selector: string, media?: string | null): string {
  const root = parse(text) ?? postcss.root();
  const rule = postcss.rule({ selector });
  rule.raws.between = ' ';
  if (media) {
    const m = media.replace(/^@media\s*/i, '');
    let at: AtRule | undefined;
    root.walkAtRules('media', (a) => {
      if (a.params.trim() === m.trim()) at = a;
    });
    if (!at) {
      at = postcss.atRule({ name: 'media', params: m });
      root.append(at);
    }
    at.append(rule);
  } else {
    root.append(rule);
  }
  let out = root.toString();
  if (!out.endsWith('\n')) out += '\n';
  return out;
}

export function deleteRule(text: string, idx: number): string {
  const root = parse(text);
  if (!root) return text;
  ruleAt(root, idx)?.remove();
  return root.toString();
}

export function renameRule(text: string, idx: number, selector: string): string {
  const root = parse(text);
  if (!root) return text;
  const r = ruleAt(root, idx);
  if (r) r.selector = selector;
  return root.toString();
}

/** Every distinct @media condition, for the media-query bar. */
export function mediaWidths(texts: string[]): number[] {
  const set = new Set<number>();
  for (const t of texts) {
    const re = /@media[^{]*?\((?:min|max)-width\s*:\s*(\d+)px\)/gi;
    let m: RegExpExecArray | null;
    while ((m = re.exec(t))) set.add(parseInt(m[1], 10));
  }
  return [...set].sort((a, b) => a - b);
}

export const CSS_PROPS = [
  'align-content', 'align-items', 'align-self', 'animation', 'aspect-ratio', 'backdrop-filter', 'background',
  'background-color', 'background-image', 'background-position', 'background-repeat', 'background-size',
  'border', 'border-bottom', 'border-color', 'border-left', 'border-radius', 'border-right', 'border-style',
  'border-top', 'border-width', 'bottom', 'box-shadow', 'box-sizing', 'clear', 'color', 'column-gap', 'columns',
  'content', 'cursor', 'display', 'filter', 'flex', 'flex-basis', 'flex-direction', 'flex-grow', 'flex-shrink',
  'flex-wrap', 'float', 'font', 'font-family', 'font-size', 'font-style', 'font-weight', 'gap', 'grid',
  'grid-area', 'grid-column', 'grid-row', 'grid-template-areas', 'grid-template-columns', 'grid-template-rows',
  'height', 'inset', 'justify-content', 'justify-items', 'left', 'letter-spacing', 'line-height', 'list-style',
  'margin', 'margin-bottom', 'margin-left', 'margin-right', 'margin-top', 'max-height', 'max-width',
  'min-height', 'min-width', 'object-fit', 'opacity', 'order', 'outline', 'overflow', 'overflow-x',
  'overflow-y', 'padding', 'padding-bottom', 'padding-left', 'padding-right', 'padding-top', 'place-items',
  'pointer-events', 'position', 'right', 'row-gap', 'text-align', 'text-decoration', 'text-shadow',
  'text-transform', 'top', 'transform', 'transition', 'user-select', 'vertical-align', 'visibility',
  'white-space', 'width', 'word-break', 'z-index',
];

export function looksLikeColor(prop: string, v: string): boolean {
  return /color|background$|fill|stroke/.test(prop) && /^(#[0-9a-f]{3,8}|rgb|hsl|[a-z]+$)/i.test(v.trim());
}

/** Best-effort conversion of a CSS colour to #rrggbb for <input type=color>. */
export function toHex(v: string): string {
  const c = document.createElement('canvas').getContext('2d');
  if (!c) return '#000000';
  c.fillStyle = '#000';
  c.fillStyle = v.trim();
  const s = c.fillStyle as string;
  return s.startsWith('#') ? s : '#000000';
}
