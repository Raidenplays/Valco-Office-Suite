import { basicSetup } from 'codemirror';
import { EditorState, Compartment, type Extension } from '@codemirror/state';
import { EditorView, keymap, type ViewUpdate } from '@codemirror/view';
import { indentWithTab } from '@codemirror/commands';
import { html } from '@codemirror/lang-html';
import { css } from '@codemirror/lang-css';
import { javascript } from '@codemirror/lang-javascript';
import { json } from '@codemirror/lang-json';
import { markdown } from '@codemirror/lang-markdown';
import { lintGutter } from '@codemirror/lint';
import { abbreviationTracker, expandAbbreviation, emmetConfig, EmmetKnownSyntax } from '@emmetio/codemirror6-plugin';
import { ext } from './paths';
import { intellisense, minimap, type IntelliHost } from './intellisense';
import { editorTheme, type ThemeName, type ThemeMode } from './themes';
export { editorTheme };

export const wrapCompartment = new Compartment();
export const minimapCompartment = new Compartment();
export const themeCompartment = new Compartment();
export { minimap };

export type UpdateHandler = (u: ViewUpdate) => void;

export function langLabel(path: string): string {
  const e = ext(path);
  const m: Record<string, string> = {
    html: 'HTML', htm: 'HTML', php: 'PHP', css: 'CSS', scss: 'SCSS', js: 'JavaScript', mjs: 'JavaScript',
    ts: 'TypeScript', jsx: 'JSX', tsx: 'TSX', json: 'JSON', md: 'Markdown', svg: 'SVG', xml: 'XML',
  };
  return m[e] ?? 'Plain text';
}

function langExt(path: string): Extension[] {
  const e = ext(path);
  if (['html', 'htm', 'xhtml', 'php', 'svg', 'xml'].includes(e)) {
    return [html({ autoCloseTags: true }), emmetConfig.of({ syntax: EmmetKnownSyntax.html }), abbreviationTracker(), keymap.of([{ key: 'Tab', run: expandAbbreviation }])];
  }
  if (['css', 'scss', 'less'].includes(e)) {
    return [css(), emmetConfig.of({ syntax: EmmetKnownSyntax.css }), abbreviationTracker(), keymap.of([{ key: 'Tab', run: expandAbbreviation }])];
  }
  if (['js', 'mjs', 'cjs'].includes(e)) return [javascript()];
  if (e === 'jsx') return [javascript({ jsx: true })];
  if (e === 'ts') return [javascript({ typescript: true })];
  if (e === 'tsx') return [javascript({ typescript: true, jsx: true })];
  if (e === 'json') return [json()];
  if (e === 'md' || e === 'markdown') return [markdown()];
  return [];
}


export function makeState(doc: string, path: string, onUpdate: { current: UpdateHandler }, wrap: boolean, host: IntelliHost, showMap: boolean, themeName: ThemeName, mode: ThemeMode): EditorState {
  return EditorState.create({
    doc,
    extensions: [
      ...langExt(path),
      ...intellisense(path, host),
      basicSetup,
      minimapCompartment.of(showMap ? minimap() : []),
      keymap.of([indentWithTab]),
      themeCompartment.of(editorTheme(themeName, mode)),
      lintGutter(),
      wrapCompartment.of(wrap ? EditorView.lineWrapping : []),
      EditorState.tabSize.of(2),
      EditorView.updateListener.of((u) => onUpdate.current(u)),
    ],
  });
}
