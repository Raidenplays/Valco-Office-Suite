// VS Code-style smarts for the code view, built on Microsoft's own HTML/CSS
// language services (the engine behind VS Code's IntelliSense):
// suggestions with docs, hover docs, CSS validation, formatting, linked tag
// rename, site-aware class/id/path/variable suggestions, go-to-definition,
// bracket colours, minimap, indent guides, colour swatches and multi-cursor keys.
import {
  getLanguageService as getHtmlService,
  TokenType,
  type CompletionItem,
  type CompletionList,
  type MarkupContent,
  type MarkedString,
  type Range as LspRange,
  type TextEdit,
} from 'vscode-html-languageservice';
import { getCSSLanguageService } from 'vscode-css-languageservice';
import { TextDocument } from 'vscode-languageserver-textdocument';
import { js as beautifyJs } from 'js-beautify';
import {
  autocompletion, completeAnyWord, snippet, startCompletion,
  type Completion, type CompletionContext, type CompletionResult, type CompletionSource,
} from '@codemirror/autocomplete';
import { localCompletionSource, scopeCompletionSource, snippets as jsSnippets } from '@codemirror/lang-javascript';
import { linter, type Diagnostic } from '@codemirror/lint';
import { gotoLine } from '@codemirror/search';
import { EditorSelection, EditorState, type Extension, type Text, type TransactionSpec } from '@codemirror/state';
import { Decoration, EditorView, ViewPlugin, hoverTooltip, keymap, type DecorationSet, type ViewUpdate } from '@codemirror/view';
import { showMinimap } from '@replit/codemirror-minimap';
import { indentationMarkers } from '@replit/codemirror-indentation-markers';
import { colorPicker } from '@replit/codemirror-css-color-picker';
import { dirname, ext, join, relative } from './paths';
import { w3cMarks } from './w3cMarks';

/* ------------------------------------------------------------------ setup */

const htmlLs = getHtmlService();
const cssLs = getCSSLanguageService();
cssLs.configure({ validate: true, lint: { emptyRules: 'warning', duplicateProperties: 'warning', unknownProperties: 'warning', zeroUnits: 'ignore', compatibleVendorPrefixes: 'ignore', vendorPrefix: 'ignore' } });

/** Live information about the site, supplied by the app. */
export interface SiteInfo {
  docPath: string;
  siteRoot: string | null;
  classes: string[];
  ids: string[];
  cssVars: string[];
  files: string[]; // absolute paths of every file in the site
}

export interface IntelliHost {
  site(): SiteInfo;
  /** Ctrl+click on a class, id or link. */
  goTo(target: { kind: 'class' | 'id' | 'file'; value: string; from: string }): void;
}

export type Kind = 'html' | 'css' | 'js' | 'other';
export function kindOf(path: string): Kind {
  const e = ext(path);
  if (['html', 'htm', 'xhtml', 'php'].includes(e)) return 'html';
  if (['css', 'scss', 'less'].includes(e)) return 'css';
  if (['js', 'mjs', 'cjs', 'jsx', 'ts', 'tsx'].includes(e)) return 'js';
  return 'other';
}

function textDoc(text: string, lang: string) {
  return TextDocument.create('file:///doc.' + lang, lang, 0, text);
}

/* ------------------------------------------------- embedded CSS in HTML */

interface CssRegion {
  from: number;
  to: number;
  attr: boolean;
}

/** <style> blocks and style="" attributes in an HTML document. */
function cssRegions(text: string): CssRegion[] {
  const out: CssRegion[] = [];
  const scanner = htmlLs.createScanner(text);
  let tok = scanner.scan();
  let lastTag = '';
  let lastAttr = '';
  while (tok !== TokenType.EOS) {
    switch (tok) {
      case TokenType.StartTag:
        lastTag = scanner.getTokenText().toLowerCase();
        break;
      case TokenType.Styles:
        out.push({ from: scanner.getTokenOffset(), to: scanner.getTokenEnd(), attr: false });
        break;
      case TokenType.AttributeName:
        lastAttr = scanner.getTokenText().toLowerCase();
        break;
      case TokenType.AttributeValue:
        if (lastAttr === 'style') {
          const s = scanner.getTokenOffset();
          const v = scanner.getTokenText();
          const q = v[0] === '"' || v[0] === "'" ? 1 : 0;
          out.push({ from: s + q, to: scanner.getTokenEnd() - (q && v.length > 1 && v[v.length - 1] === v[0] ? 1 : 0), attr: true });
        }
        break;
    }
    void lastTag;
    tok = scanner.scan();
  }
  return out;
}

/** A CSS document the same length as the HTML, blank outside the CSS regions (offsets line up). */
function virtualCss(text: string, regions: CssRegion[]): string {
  const chars = text.replace(/[^\n]/g, ' ').split('');
  for (const r of regions) {
    for (let i = r.from; i < r.to; i++) chars[i] = text[i];
    if (r.attr && r.from >= 2) {
      chars[r.from - 2] = '*';
      chars[r.from - 1] = '{';
      if (r.to < chars.length) chars[r.to] = '}';
    }
  }
  return chars.join('');
}

function scriptRegionAt(text: string, pos: number): boolean {
  const scanner = htmlLs.createScanner(text);
  let tok = scanner.scan();
  while (tok !== TokenType.EOS) {
    if (tok === TokenType.Script && pos >= scanner.getTokenOffset() && pos <= scanner.getTokenEnd()) return true;
    if (scanner.getTokenOffset() > pos) break;
    tok = scanner.scan();
  }
  return false;
}

/* --------------------------------------------------- LSP <-> CodeMirror */

function offsetOf(doc: Text, p: { line: number; character: number }): number {
  const line = doc.line(Math.min(doc.lines, p.line + 1));
  return Math.min(line.to, line.from + p.character);
}

function posOf(doc: Text, offset: number) {
  const line = doc.lineAt(offset);
  return { line: line.number - 1, character: offset - line.from };
}

const KIND: Record<number, string> = {
  1: 'text', 2: 'method', 3: 'function', 4: 'function', 5: 'property', 6: 'variable', 7: 'class', 8: 'interface', 9: 'namespace',
  10: 'property', 11: 'variable', 12: 'constant', 13: 'enum', 14: 'keyword', 15: 'text', 16: 'constant', 17: 'text', 18: 'variable',
  19: 'namespace', 20: 'enum', 21: 'constant', 22: 'type', 23: 'variable', 24: 'keyword', 25: 'type',
};

function lspSnippetToCm(s: string): string {
  return s.replace(/\$(\d+)/g, '${$1}').replace(/\$\{(\d+)\|([^,}]*)[^}]*\}/g, '${$1:$2}');
}

/** Tiny markdown renderer for docs (code, bold, links, paragraphs). */
export function renderMarkdown(md: string): HTMLElement {
  const root = document.createElement('div');
  root.className = 'wc-doc';
  const blocks = md.split(/```/);
  blocks.forEach((b, i) => {
    if (i % 2 === 1) {
      const pre = document.createElement('pre');
      pre.textContent = b.replace(/^[a-z]*\n/, '').trim();
      root.appendChild(pre);
      return;
    }
    for (const para of b.split(/\n{2,}/)) {
      if (!para.trim()) continue;
      const p = document.createElement('p');
      const html = para
        .replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&amp;/g, '&')
        .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
        .replace(/!\[([^\]]*)\]\((data:image\/[^)\s]+)\)/g, (_m, alt, src) => `<img class="wc-badge" alt="${alt.replace(/"/g, '')}" src="${src}">`)
        .replace(/\\([\\`*_{}[\]()#+\-.!|<>])/g, '$1')
        .replace(/`([^`]+)`/g, '<code>$1</code>')
        .replace(/\*\*([^*]+)\*\*/g, '<b>$1</b>')
        .replace(/(^|\s)_([^_]+)_(?=\s|$|[.,])/g, '$1<i>$2</i>')
        .replace(/\[([^\]]+)\]\((https?:[^)]+)\)/g, '<a href="$2" target="_blank" rel="noopener">$1</a>')
        .replace(/\n/g, '<br>');
      p.innerHTML = html;
      root.appendChild(p);
    }
  });
  return root;
}

function docText(d: string | MarkupContent | MarkedString | MarkedString[] | undefined | null): string {
  if (!d) return '';
  if (Array.isArray(d)) return d.map(docText).join('\n\n');
  if (typeof d === 'string') return d;
  if ('kind' in d) return d.value;
  if ('language' in d) return '```' + d.language + '\n' + d.value + '\n```';
  return '';
}

function toCompletions(list: CompletionList, state: EditorState, fallbackFrom: number): CompletionResult | null {
  if (!list.items.length) return null;
  const doc = state.doc;
  let from = fallbackFrom;
  const first = list.items.find((i) => i.textEdit && 'range' in i.textEdit);
  if (first?.textEdit && 'range' in first.textEdit) from = offsetOf(doc, first.textEdit.range.start);
  const options: Completion[] = list.items.map((it: CompletionItem, idx) => {
    const te = it.textEdit && 'range' in it.textEdit ? it.textEdit : null;
    const newText = te ? te.newText : it.insertText ?? it.label;
    const isSnippet = it.insertTextFormat === 2;
    const rangeFrom = te ? offsetOf(doc, te.range.start) : from;
    const info = docText(it.documentation as MarkupContent | string | undefined);
    return {
      label: it.label,
      detail: it.detail,
      type: KIND[it.kind ?? 1],
      boost: it.sortText ? -Math.min(99, idx / 10) : 0,
      info: info ? () => renderMarkdown(info) : undefined,
      apply: (view: EditorView, _c: Completion, _f: number, to: number) => {
        const a = Math.min(rangeFrom, to);
        if (isSnippet) {
          snippet(lspSnippetToCm(newText))(view, _c, a, to);
        } else {
          view.dispatch({ changes: { from: a, to, insert: newText }, selection: { anchor: a + newText.length }, userEvent: 'input.complete' });
        }
        if (it.command?.command === 'editor.action.triggerSuggest') setTimeout(() => startCompletion(view), 20);
      },
    };
  });
  return { from, options, validFor: /^[\w\-.#:@]*$/ };
}

/* ------------------------------------------------- completion sources */

function siteCompletions(host: IntelliHost, context: CompletionContext, kind: Kind): CompletionResult | null {
  const site = host.site();
  const { state, pos } = context;
  const line = state.doc.lineAt(pos);
  const before = line.text.slice(0, pos - line.from);

  if (kind === 'html') {
    // class="a b|"  id="|"
    const attr = before.match(/\b(class|id|href|src|srcset|action|poster|data)\s*=\s*["']([^"']*)$/i);
    if (attr) {
      const name = attr[1].toLowerCase();
      const value = attr[2];
      if (name === 'class') {
        const word = value.match(/[\w-]*$/)![0];
        return { from: pos - word.length, options: site.classes.map((c) => ({ label: c, type: 'class', detail: 'class from your CSS' })), validFor: /^[\w-]*$/ };
      }
      if (name === 'id') {
        return { from: pos - value.length, options: site.ids.map((c) => ({ label: c, type: 'constant', detail: 'id used in your CSS' })), validFor: /^[\w-]*$/ };
      }
      // paths, relative to this page
      return pathCompletions(site, pos - value.length, value);
    }
  }
  if (kind === 'css' || kind === 'html') {
    const u = before.match(/url\(\s*["']?([^"')]*)$/);
    if (u) return pathCompletions(site, pos - u[1].length, u[1]);
    const v = before.match(/var\(\s*(-[\w-]*)?$/);
    if (v) return { from: pos - (v[1]?.length ?? 0), options: site.cssVars.map((x) => ({ label: x, type: 'variable', detail: 'custom property' })) };
  }
  if (kind === 'css') {
    // selectors: .class / #id at the start of a selector
    const sel = before.match(/(^|[\s,>+~}(])([.#])([\w-]*)$/);
    if (sel && !/:[^;{}]*$/.test(before)) {
      const list = sel[2] === '.' ? site.classes : site.ids;
      return { from: pos - sel[3].length - 1, options: list.map((c) => ({ label: sel[2] + c, type: sel[2] === '.' ? 'class' : 'constant', detail: 'used in your pages' })), validFor: /^[.#]?[\w-]*$/ };
    }
  }
  if (kind === 'js' || kind === 'html') {
    const q = before.match(/(getElementById|querySelector(?:All)?|closest|matches)\(\s*["']([^"']*)$/);
    if (q) {
      const v = q[2];
      const byId = q[1] === 'getElementById';
      const opts = byId
        ? site.ids.map((i) => ({ label: i, type: 'constant' }))
        : [...site.ids.map((i) => ({ label: '#' + i, type: 'constant' })), ...site.classes.map((c) => ({ label: '.' + c, type: 'class' }))];
      const word = byId ? v : v.match(/[.#]?[\w-]*$/)![0];
      return { from: pos - word.length, options: opts, validFor: /^[.#]?[\w-]*$/ };
    }
  }
  return null;
}

function pathCompletions(site: SiteInfo, from: number, typed: string): CompletionResult | null {
  if (/^(https?:|mailto:|tel:|#|data:|\/\/)/i.test(typed)) return null;
  const base = dirname(site.docPath);
  const typedDir = typed.includes('/') ? typed.slice(0, typed.lastIndexOf('/') + 1) : '';
  const lookIn = typedDir.startsWith('/') && site.siteRoot ? join(site.siteRoot, typedDir) : join(base, typedDir);
  const seen = new Map<string, Completion>();
  for (const f of site.files) {
    if (f === site.docPath) continue;
    const rel = relative(lookIn, f);
    if (rel.startsWith('..')) {
      // still offer ../ entries at the top level
      if (typedDir === '' || typedDir.endsWith('../')) {
        const up = rel.match(/^(\.\.\/)+/)![0];
        if (!seen.has(up)) seen.set(up, { label: up, type: 'namespace', detail: 'parent folder' });
      }
      continue;
    }
    const seg = rel.split('/');
    const label = seg.length > 1 ? seg[0] + '/' : seg[0];
    if (!seen.has(label)) seen.set(label, { label, type: seg.length > 1 ? 'namespace' : 'text', detail: seg.length > 1 ? 'folder' : ext(f).toUpperCase() + ' file' });
  }
  const opts = [...seen.values()].map((c) =>
    c.label.endsWith('/') ? { ...c, apply: (v: EditorView, _c: Completion, a: number, b: number) => { v.dispatch({ changes: { from: a, to: b, insert: c.label }, selection: { anchor: a + c.label.length } }); setTimeout(() => startCompletion(v), 10); } } : c,
  );
  return { from: from + typedDir.length, options: opts, validFor: /^[\w\-. ]*$/ };
}

const htmlSnippets: Completion[] = [
  { label: '!', detail: 'HTML5 page', type: 'keyword', apply: snippet('<!DOCTYPE html>\n<html lang="${1:en}">\n<head>\n  <meta charset="UTF-8">\n  <meta name="viewport" content="width=device-width, initial-scale=1.0">\n  <title>${2:Document}</title>\n</head>\n<body>\n  ${0}\n</body>\n</html>') },
  { label: 'link:css', detail: 'stylesheet link', type: 'keyword', apply: snippet('<link rel="stylesheet" href="${1:css/style.css}">') },
  { label: 'script:src', detail: 'external script', type: 'keyword', apply: snippet('<script src="${1:js/main.js}"></script>') },
  { label: 'img:alt', detail: 'image with alt text', type: 'keyword', apply: snippet('<img src="${1}" alt="${2}">') },
  { label: 'a:blank', detail: 'link opening in a new tab', type: 'keyword', apply: snippet('<a href="${1:https://}" target="_blank" rel="noopener">${2:link text}</a>') },
  { label: 'meta:og', detail: 'Open Graph tags', type: 'keyword', apply: snippet('<meta property="og:title" content="${1}">\n<meta property="og:description" content="${2}">\n<meta property="og:image" content="${3}">') },
];

const cssSnippets: Completion[] = [
  { label: 'flex-center', detail: 'centre with flexbox', type: 'keyword', apply: snippet('display: flex;\nalign-items: center;\njustify-content: center;') },
  { label: 'grid-cols', detail: 'responsive grid', type: 'keyword', apply: snippet('display: grid;\ngrid-template-columns: repeat(auto-fill, minmax(${1:200px}, 1fr));\ngap: ${2:16px};') },
  { label: '@media', detail: 'media query', type: 'keyword', apply: snippet('@media (max-width: ${1:768px}) {\n  ${0}\n}') },
  { label: '@keyframes', detail: 'animation', type: 'keyword', apply: snippet('@keyframes ${1:name} {\n  from { ${2} }\n  to { ${3} }\n}') },
  { label: 'transition-all', detail: 'smooth transition', type: 'keyword', apply: snippet('transition: all ${1:0.2s} ease;') },
  { label: 'visually-hidden', detail: 'screen-reader only', type: 'keyword', apply: snippet('position: absolute;\nwidth: 1px;\nheight: 1px;\noverflow: hidden;\nclip: rect(0 0 0 0);\nwhite-space: nowrap;') },
];

const extraJsSnippets: Completion[] = [
  { label: 'log', detail: 'console.log', type: 'keyword', apply: snippet('console.log(${1});') },
  { label: 'qs', detail: 'document.querySelector', type: 'keyword', apply: snippet("document.querySelector('${1}')") },
  { label: 'qsa', detail: 'document.querySelectorAll', type: 'keyword', apply: snippet("document.querySelectorAll('${1}')") },
  { label: 'on', detail: 'addEventListener', type: 'keyword', apply: snippet("${1:element}.addEventListener('${2:click}', (event) => {\n\t${0}\n});") },
  { label: 'ready', detail: 'run when the page has loaded', type: 'keyword', apply: snippet("document.addEventListener('DOMContentLoaded', () => {\n\t${0}\n});") },
  { label: 'fetchjson', detail: 'fetch JSON', type: 'keyword', apply: snippet("const ${1:data} = await fetch('${2:/api}').then((r) => r.json());") },
];

/** Browser globals, copied one by one so a throwing getter can't break suggestions. */
const jsGlobals: CompletionSource = (() => {
  const safe: Record<string, unknown> = {};
  let o: object | null = globalThis;
  while (o && o !== Object.prototype) {
    for (const name of Object.getOwnPropertyNames(o)) {
      if (name in safe || !/^[A-Za-z_$][\w$]*$/.test(name)) continue;
      try {
        safe[name] = (globalThis as Record<string, unknown>)[name];
      } catch {
        /* skip */
      }
    }
    o = Object.getPrototypeOf(o);
  }
  const src = scopeCompletionSource(safe);
  return (ctx: CompletionContext) => {
    try {
      return src(ctx);
    } catch {
      return null;
    }
  };
})();

function jsSource(context: CompletionContext) {
  let localRes: CompletionResult | null = null;
  try {
    localRes = localCompletionSource(context);
  } catch {
    localRes = null;
  }
  return Promise.resolve(localRes).then((local) => {
    const glob = jsGlobals(context);
    return Promise.resolve(glob).catch(() => null).then((g) => {
      const word = context.matchBefore(/[\w$]*/);
      if (!word || (word.from === word.to && !context.explicit)) return local ?? g ?? null;
      const opts = [...(local?.options ?? []), ...(g?.options ?? []), ...jsSnippets, ...extraJsSnippets];
      return { from: local?.from ?? g?.from ?? word.from, options: opts, validFor: /^[\w$]*$/ };
    });
  });
}

function makeSource(host: IntelliHost, kind: Kind): CompletionSource {
  return async (context) => {
    const site = siteCompletions(host, context, kind);
    if (site) return site;
    const { state, pos } = context;
    const text = state.doc.toString();
    const word = context.matchBefore(/[\w\-.#@:!]*/);

    if (kind === 'css' || (kind === 'html' && cssRegions(text).some((r) => pos >= r.from && pos <= r.to))) {
      const vtext = kind === 'css' ? text : virtualCss(text, cssRegions(text));
      const d = textDoc(vtext, 'css');
      const list = cssLs.doComplete(d, posOf(state.doc, pos), cssLs.parseStylesheet(d), { completePropertyWithSemicolon: true, triggerPropertyValueCompletion: true });
      const res = toCompletions(list, state, word?.from ?? pos);
      if (res && kind === 'css' && word && word.from < word.to) res.options = [...res.options, ...cssSnippets];
      return res;
    }
    if (kind === 'html' && scriptRegionAt(text, pos)) return jsSource(context);
    if (kind === 'js') {
      const r = await jsSource(context);
      return r;
    }
    if (kind === 'html') {
      const d = textDoc(text, 'html');
      const list = await htmlLs.doComplete2(d, posOf(state.doc, pos), htmlLs.parseHTMLDocument(d), { resolveReference: (r, b) => r + b }, { attributeDefaultValue: 'doublequotes', hideAutoCompleteProposals: false });
      list.items = list.items.filter((i) => !(i.kind === 17 || i.kind === 19)); // paths come from siteCompletions
      const res = toCompletions(list, state, word?.from ?? pos);
      const typed = word ? state.sliceDoc(word.from, pos) : '';
      if (typed && !/[<="']/.test(state.sliceDoc(Math.max(0, (word?.from ?? pos) - 1), word?.from ?? pos))) {
        const snips = htmlSnippets.filter((s) => s.label.startsWith(typed));
        if (snips.length) return { from: word!.from, options: [...snips, ...(res?.options ?? [])], validFor: /^[\w\-:!]*$/ };
      }
      return res;
    }
    return completeAnyWord(context);
  };
}

/* ------------------------------------------------------------------ hover */

function makeHover(kind: Kind) {
  return hoverTooltip((view, pos) => {
    const text = view.state.doc.toString();
    let hover = null;
    if (kind === 'css' || (kind === 'html' && cssRegions(text).some((r) => pos >= r.from && pos <= r.to))) {
      const d = textDoc(kind === 'css' ? text : virtualCss(text, cssRegions(text)), 'css');
      hover = cssLs.doHover(d, posOf(view.state.doc, pos), cssLs.parseStylesheet(d));
    } else if (kind === 'html') {
      const d = textDoc(text, 'html');
      hover = htmlLs.doHover(d, posOf(view.state.doc, pos), htmlLs.parseHTMLDocument(d));
    }
    if (!hover) return null;
    const md = docText(hover.contents as MarkupContent);
    if (!md.trim()) return null;
    const r: LspRange | undefined = hover.range;
    return {
      pos: r ? offsetOf(view.state.doc, r.start) : pos,
      end: r ? offsetOf(view.state.doc, r.end) : pos,
      above: true,
      create: () => ({ dom: renderMarkdown(md) }),
    };
  }, { hoverTime: 350 });
}

/* ------------------------------------------------------- CSS diagnostics */

export function cssDiagnostics(text: string, isHtml: boolean): { from: number; to: number; severity: 'error' | 'warning' | 'info'; message: string }[] {
  const regions = isHtml ? cssRegions(text) : null;
  if (regions && !regions.length) return [];
  const d = textDoc(regions ? virtualCss(text, regions) : text, 'css');
  const ds = cssLs.doValidation(d, cssLs.parseStylesheet(d));
  return ds.map((x) => ({
    from: d.offsetAt(x.range.start),
    to: Math.max(d.offsetAt(x.range.end), d.offsetAt(x.range.start) + 1),
    severity: x.severity === 1 ? 'error' : x.severity === 2 ? 'warning' : 'info',
    message: 'CSS: ' + x.message,
  }));
}

/* ------------------------------------------------------------ formatting */

function applyEdits(view: EditorView, edits: TextEdit[]) {
  const doc = view.state.doc;
  const changes = edits.map((e) => ({ from: offsetOf(doc, e.range.start), to: offsetOf(doc, e.range.end), insert: e.newText }));
  if (changes.length) view.dispatch({ changes, userEvent: 'format' });
}

export function formatDocument(view: EditorView, path: string, selectionOnly = false): boolean {
  const kind = kindOf(path);
  const text = view.state.doc.toString();
  const sel = view.state.selection.main;
  const range = selectionOnly && !sel.empty ? { start: posOf(view.state.doc, sel.from), end: posOf(view.state.doc, sel.to) } : undefined;
  const opts = { tabSize: 2, insertSpaces: true };
  if (kind === 'html') {
    applyEdits(view, htmlLs.format(textDoc(text, 'html'), range, {
      ...opts, wrapLineLength: 120, unformatted: 'wbr', contentUnformatted: 'pre,code,textarea', indentInnerHtml: false,
      preserveNewLines: true, maxPreserveNewLines: 2, indentHandlebars: false, endWithNewline: true, extraLiners: 'head, body, /html',
      wrapAttributes: 'auto', templating: false,
    }));
    return true;
  }
  if (kind === 'css') {
    applyEdits(view, cssLs.format(textDoc(text, 'css'), range, { ...opts, newlineBetweenRules: true, newlineBetweenSelectors: true, spaceAroundSelectorSeparator: false }));
    return true;
  }
  if (kind === 'js' && ext(path) !== 'ts' && ext(path) !== 'tsx') {
    const src = range ? view.state.sliceDoc(sel.from, sel.to) : text;
    const out = beautifyJs(src, { indent_size: 2, preserve_newlines: true, max_preserve_newlines: 2, end_with_newline: !range });
    view.dispatch({ changes: { from: range ? sel.from : 0, to: range ? sel.to : text.length, insert: out }, userEvent: 'format' });
    return true;
  }
  return false;
}

/* ------------------------------------------------- linked tag renaming */

const linkedTags = EditorState.transactionFilter.of((tr) => {
  if (!tr.docChanged || !tr.isUserEvent('input') && !tr.isUserEvent('delete')) return tr;
  if (tr.startState.selection.ranges.length !== 1) return tr;
  let single: { fromA: number; toA: number; text: string } | null = null;
  let count = 0;
  tr.changes.iterChanges((fromA, toA, _fb, _tb, inserted) => {
    count++;
    single = { fromA, toA, text: inserted.toString() };
  });
  if (count !== 1 || !single) return tr;
  const { fromA, toA, text } = single as { fromA: number; toA: number; text: string };
  if (/[\s<>/="']/.test(text)) return tr;
  const old = tr.startState.doc.toString();
  if (old.length > 400_000) return tr;
  const d = textDoc(old, 'html');
  const ranges = htmlLs.findLinkedEditingRanges(d, d.positionAt(fromA), htmlLs.parseHTMLDocument(d));
  if (!ranges || ranges.length !== 2) return tr;
  const rs = ranges.map((r) => ({ from: d.offsetAt(r.start), to: d.offsetAt(r.end) }));
  const mine = rs.find((r) => fromA >= r.from && toA <= r.to);
  const other = rs.find((r) => r !== mine);
  if (!mine || !other) return tr;
  const rel = fromA - mine.from;
  const mirror = { from: other.from + rel, to: other.from + rel + (toA - fromA), insert: text };
  return [tr, { changes: mirror, sequential: false } as TransactionSpec];
});

/* ------------------------------------------------- bracket colouring */

// colours come from the active theme (.wc-br0/1/2)
const bracketMarks = [0, 1, 2].map((i) => Decoration.mark({ class: 'wc-br' + i }));

const bracketPairs = ViewPlugin.fromClass(
  class {
    decorations: DecorationSet;
    constructor(view: EditorView) {
      this.decorations = this.build(view);
    }
    update(u: ViewUpdate) {
      if (u.docChanged || u.viewportChanged) this.decorations = this.build(u.view);
    }
    build(view: EditorView): DecorationSet {
      const doc = view.state.doc;
      const vp = view.viewport;
      // depth at the start of the viewport (scan from the top; skip strings roughly)
      const scanFrom = Math.max(0, vp.from - 200_000);
      const head = doc.sliceString(scanFrom, vp.from);
      let depth = 0;
      for (const ch of head) {
        if (ch === '(' || ch === '[' || ch === '{') depth++;
        else if (ch === ')' || ch === ']' || ch === '}') depth = Math.max(0, depth - 1);
      }
      const marks = [];
      const body = doc.sliceString(vp.from, vp.to);
      for (let i = 0; i < body.length; i++) {
        const ch = body[i];
        if (ch === '(' || ch === '[' || ch === '{') {
          marks.push(bracketMarks[depth % 3].range(vp.from + i, vp.from + i + 1));
          depth++;
        } else if (ch === ')' || ch === ']' || ch === '}') {
          depth = Math.max(0, depth - 1);
          marks.push(bracketMarks[depth % 3].range(vp.from + i, vp.from + i + 1));
        }
      }
      return Decoration.set(marks);
    }
  },
  { decorations: (v) => v.decorations },
);

/* ------------------------------------------------ go to definition */

function goToDefinition(host: IntelliHost, kind: Kind) {
  return EditorView.domEventHandlers({
    mousedown(e, view) {
      if (!(e.ctrlKey || e.metaKey) || e.button !== 0) return false;
      const pos = view.posAtCoords({ x: e.clientX, y: e.clientY });
      if (pos === null) return false;
      const line = view.state.doc.lineAt(pos);
      const col = pos - line.from;
      const t = line.text;
      if (kind === 'html') {
        // find the attribute value that contains pos
        const re = /\b(class|id|href|src)\s*=\s*(["'])(.*?)\2/gi;
        let m: RegExpExecArray | null;
        while ((m = re.exec(t))) {
          const vs = m.index + m[0].indexOf(m[2]) + 1;
          const ve = vs + m[3].length;
          if (col < vs || col > ve) continue;
          const name = m[1].toLowerCase();
          if (name === 'class') {
            const left = m[3].slice(0, col - vs).match(/[\w-]*$/)![0];
            const right = m[3].slice(col - vs).match(/^[\w-]*/)![0];
            if (left + right) host.goTo({ kind: 'class', value: left + right, from: 'html' });
          } else if (name === 'id') host.goTo({ kind: 'id', value: m[3], from: 'html' });
          else host.goTo({ kind: 'file', value: m[3], from: 'html' });
          e.preventDefault();
          return true;
        }
      }
      if (kind === 'css') {
        const u = [...t.matchAll(/url\(\s*["']?([^"')]+)/g)].find((x) => col >= (x.index ?? 0) && col <= (x.index ?? 0) + x[0].length + 2);
        if (u) {
          host.goTo({ kind: 'file', value: u[1], from: 'css' });
          e.preventDefault();
          return true;
        }
      }
      return false;
    },
  });
}

/* ------------------------------------------------------- multi-cursor */

function addCursor(dir: -1 | 1) {
  return (view: EditorView) => {
    const s = view.state;
    const ranges = [...s.selection.ranges];
    const last = dir < 0 ? ranges[0] : ranges[ranges.length - 1];
    const line = s.doc.lineAt(last.head);
    const targetNo = line.number + dir;
    if (targetNo < 1 || targetNo > s.doc.lines) return true;
    const target = s.doc.line(targetNo);
    const col = Math.min(last.head - line.from, target.length);
    view.dispatch({ selection: EditorSelection.create([...ranges, EditorSelection.cursor(target.from + col)], ranges.length) });
    return true;
  };
}

/* ------------------------------------------------------------ public */

export const editorTheme = EditorView.theme({
  '.cm-tooltip.cm-tooltip-autocomplete > ul': { fontFamily: "'JetBrains Mono', 'Fira Code', Consolas, monospace", fontSize: '12.5px', maxHeight: '18em' },
  '.cm-completionDetail': { opacity: '.65', fontStyle: 'normal', marginLeft: '1em' },
  '.cm-completionInfo': { maxWidth: '420px', padding: '8px 10px' },
  '.wc-doc': { fontFamily: 'system-ui, sans-serif', fontSize: '12.5px', lineHeight: '1.45', maxWidth: '460px', padding: '2px 4px' },
  '.wc-doc p': { margin: '4px 0' },
  '.wc-doc code': { background: 'rgba(128,128,128,.16)', padding: '0 4px', borderRadius: '3px', fontSize: '11.5px' },
  '.wc-doc pre': { background: 'rgba(128,128,128,.16)', padding: '6px 8px', borderRadius: '4px', margin: '4px 0', whiteSpace: 'pre-wrap', fontSize: '11.5px' },
  '.wc-doc img.wc-badge': { height: '12px', verticalAlign: 'middle', marginRight: '4px' },
  '.cm-tooltip-hover': { padding: '6px 8px' },
  '.cm-completionMatchedText': { textDecoration: 'none', fontWeight: '700' },
  '.cm-completionIcon': { width: '1.3em', opacity: '1', paddingRight: '.5em', fontSize: '95%', textAlign: 'center' },
  '.cm-completionIcon-property:after': { content: "'◆'", color: '#75beff' },
  '.cm-completionIcon-class:after': { content: "'◈'", color: '#ee9d28' },
  '.cm-completionIcon-constant:after': { content: "'#'", color: '#4fc1ff' },
  '.cm-completionIcon-keyword:after': { content: "'❏'", color: '#c5c5c5' },
  '.cm-completionIcon-function:after, .cm-completionIcon-method:after': { content: "'ƒ'", color: '#b180d7' },
  '.cm-completionIcon-variable:after': { content: "'𝑥'", color: '#75beff' },
  '.cm-completionIcon-enum:after': { content: "'▤'", color: '#ee9d28' },
  '.cm-completionIcon-type:after, .cm-completionIcon-interface:after': { content: "'T'", color: '#75beff' },
  '.cm-completionIcon-namespace:after': { content: "'▸'", color: '#e8c06f' },
  '.cm-completionIcon-text:after': { content: "'≡'", color: '#a0a0a0' },
});

export function minimap(): Extension {
  return showMinimap.compute(['doc'], () => ({
    create: () => ({ dom: document.createElement('div') }),
    displayText: 'blocks',
    showOverlay: 'mouse-over',
  }));
}

export function intellisense(path: string, host: IntelliHost): Extension[] {
  const kind = kindOf(path);
  const out: Extension[] = [
    // override: our source handles every language, so the built-in ones don't double up
    autocompletion({ override: [makeSource(host, kind)], activateOnTyping: true, maxRenderedOptions: 80, icons: true, defaultKeymap: true, closeOnBlur: true }),
    makeHover(kind),
    bracketPairs,
    indentationMarkers({ thickness: 1, colors: { light: '#d0d0d0', dark: '#353a45', activeLight: '#a0a0a0', activeDark: '#5a6070' } }),
    goToDefinition(host, kind),
    w3cMarks(),
    EditorView.clickAddsSelectionRange.of((e) => e.altKey),
    keymap.of([
      { key: 'Shift-Alt-f', run: (v) => formatDocument(v, path), preventDefault: true },
      { key: 'Mod-k Mod-f', run: (v) => formatDocument(v, path, true), preventDefault: true },
      { key: 'Mod-g', run: gotoLine, preventDefault: true },
      { key: 'Mod-Alt-ArrowUp', run: addCursor(-1), preventDefault: true },
      { key: 'Mod-Alt-ArrowDown', run: addCursor(1), preventDefault: true },
      { key: 'Mod-Space', run: startCompletion, preventDefault: true },
    ]),
    editorTheme,
  ];
  if (kind === 'html') out.push(linkedTags);
  if (kind === 'css' || kind === 'html') out.push(colorPicker);
  if (kind === 'css') {
    out.push(linter((view) => cssDiagnostics(view.state.doc.toString(), false) as Diagnostic[], { delay: 400 }));
  }
  return out;
}

