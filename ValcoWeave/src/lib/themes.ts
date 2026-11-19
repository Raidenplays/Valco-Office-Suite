// App themes. The UI chrome is themed in styles.css via <html data-theme="…">;
// this file holds each theme's code-editor colours.
import { EditorView } from '@codemirror/view';
import type { Extension } from '@codemirror/state';
import { HighlightStyle, syntaxHighlighting } from '@codemirror/language';
import { tags as t } from '@lezer/highlight';
import { oneDark } from '@codemirror/theme-one-dark';

export type ThemeName = 'dark' | 'vista' | 'cs6' | '2077';
export type ThemeMode = 'light' | 'dark';
export type ModePref = 'system' | 'light' | 'dark';

export const MODES: { id: ModePref; name: string }[] = [
  { id: 'system', name: 'Match Windows' },
  { id: 'light', name: 'Light' },
  { id: 'dark', name: 'Dark' },
];

/** The look each theme had originally, used when nothing else is chosen. */
export const NATIVE_MODE: Record<ThemeName, ThemeMode> = { dark: 'dark', vista: 'light', cs6: 'light', '2077': 'dark' };

export const THEMES: { id: ThemeName; name: string; description: string }[] = [
  { id: 'vista', name: 'Windows Vista', description: 'Aero glass, like the rest of the Valco apps' },
  { id: 'dark', name: 'Modern', description: 'Flat workspace with a teal accent' },
  { id: 'cs6', name: 'Dreamweaver CS6', description: 'Classic light-grey 2012 workspace' },
  { id: '2077', name: '2077', description: 'Neon colours from the 2077 VS Code theme' },
];

const mono = "'JetBrains Mono', 'Fira Code', 'DejaVu Sans Mono', Consolas, monospace";

/* ---------------------------------------------------------------- dark */
const darkChrome = EditorView.theme(
  {
    '&': { height: '100%', fontSize: 'var(--code-size, 13px)', backgroundColor: '#1b1d23' },
    '.cm-scroller': { fontFamily: mono, lineHeight: '1.55' },
    '.cm-gutters': { backgroundColor: '#1b1d23', borderRight: '1px solid #2a2d35' },
    '.cm-activeLine': { backgroundColor: '#22252d' },
    '.cm-activeLineGutter': { backgroundColor: '#22252d' },
    '.cm-tooltip': { background: '#252830', border: '1px solid #3a3f4b', borderRadius: '6px', boxShadow: '0 8px 24px rgba(0,0,0,.45)', color: '#d6d9e0' },
    '.cm-tooltip-autocomplete ul li[aria-selected]': { background: '#04395e', color: '#fff' },
    '.cm-completionMatchedText': { color: '#2aaaff' },
    '.wc-doc a': { color: '#36c2b4' },
    '.wc-br0': { color: '#ffd700' },
    '.wc-br1': { color: '#da70d6' },
    '.wc-br2': { color: '#179fff' },
  },
  { dark: true },
);

/* -------------------------------------------- Vista (VS 2008-era code) */
const vistaChrome = EditorView.theme(
  {
    '&': { height: '100%', fontSize: 'var(--code-size, 13px)', backgroundColor: '#ffffff', color: '#000000' },
    '.cm-scroller': { fontFamily: "Consolas, 'Lucida Console', 'DejaVu Sans Mono', monospace", lineHeight: '1.45' },
    '.cm-content': { caretColor: '#000' },
    '.cm-cursor': { borderLeftColor: '#000' },
    '.cm-gutters': { backgroundColor: '#ffffff', color: '#2b91af', borderRight: '1px solid #e6e6e6' },
    '.cm-activeLine': { backgroundColor: 'transparent', boxShadow: 'inset 0 0 0 1px #eaeaf2' },
    '.cm-activeLineGutter': { backgroundColor: '#ffffff', color: '#000' },
    '&.cm-focused .cm-selectionBackground, .cm-selectionBackground, ::selection': { backgroundColor: '#add6ff !important' },
    '.cm-matchingBracket': { backgroundColor: '#dbe0e8', outline: '1px solid #b9b9b9' },
    '.cm-foldPlaceholder': { background: '#f0f0f0', border: '1px solid #a0a0a0', color: '#555' },
    '.cm-tooltip': { background: '#ffffff', border: '1px solid #979797', borderRadius: '0', boxShadow: '2px 2px 5px rgba(0,0,0,.3)', color: '#000' },
    '.cm-tooltip-autocomplete ul li[aria-selected]': { background: 'linear-gradient(#f1f8fd, #d5effc)', color: '#000', outline: '1px solid #99defd' },
    '.cm-completionMatchedText': { color: '#0e70c0' },
    '.cm-panels': { background: '#f0f0f0', color: '#000' },
    '.wc-doc a': { color: '#0066cc' },
    '.wc-br0': { color: '#0431fa' },
    '.wc-br1': { color: '#319331' },
    '.wc-br2': { color: '#7b3814' },
  },
  { dark: false },
);
const vistaHighlight = HighlightStyle.define([
  { tag: [t.tagName, t.angleBracket], color: '#800000' },
  { tag: t.attributeName, color: '#ff0000' },
  { tag: [t.attributeValue, t.string, t.special(t.string)], color: '#0000ff' },
  { tag: [t.comment, t.lineComment, t.blockComment], color: '#008000' },
  { tag: [t.keyword, t.controlKeyword, t.operatorKeyword, t.definitionKeyword, t.modifier, t.bool, t.null], color: '#0000ff' },
  { tag: [t.number, t.unit], color: '#000000' },
  { tag: [t.propertyName], color: '#ff0000' },
  { tag: [t.className, t.labelName], color: '#800000' },
  { tag: [t.typeName, t.namespace], color: '#2b91af' },
  { tag: [t.function(t.variableName), t.function(t.propertyName)], color: '#000000' },
  { tag: [t.variableName, t.definition(t.variableName)], color: '#000000' },
  { tag: [t.color, t.atom, t.constant(t.name)], color: '#0451a5' },
  { tag: t.processingInstruction, color: '#0000ff' },
  { tag: t.documentMeta, color: '#0000ff' },
  { tag: t.invalid, color: '#ff0000', textDecoration: 'underline wavy' },
]);

/* ------------------------------------------ Dreamweaver CS6 code view */
const cs6Chrome = EditorView.theme(
  {
    '&': { height: '100%', fontSize: 'var(--code-size, 13px)', backgroundColor: '#ffffff', color: '#000000' },
    '.cm-scroller': { fontFamily: "'Courier New', Consolas, 'DejaVu Sans Mono', monospace", lineHeight: '1.45' },
    '.cm-content': { caretColor: '#000' },
    '.cm-cursor': { borderLeftColor: '#000' },
    '.cm-gutters': { backgroundColor: '#f0f0f0', color: '#8c8c8c', borderRight: '1px solid #cfcfcf' },
    '.cm-activeLine': { backgroundColor: '#fafae0' },
    '.cm-activeLineGutter': { backgroundColor: '#e2e2e2', color: '#333' },
    '&.cm-focused .cm-selectionBackground, .cm-selectionBackground, ::selection': { backgroundColor: '#b5d5ff !important' },
    '.cm-matchingBracket': { backgroundColor: '#d9e4f5' },
    '.cm-foldPlaceholder': { background: '#e8e8e8', border: '1px solid #b0b0b0', color: '#555' },
    '.cm-tooltip': { background: '#ffffff', border: '1px solid #8c8c8c', borderRadius: '2px', boxShadow: '0 3px 8px rgba(0,0,0,.25)', color: '#000' },
    '.cm-tooltip-autocomplete ul li[aria-selected]': { background: '#3875d7', color: '#fff' },
    '.cm-completionMatchedText': { color: '#00008b' },
    '.cm-tooltip-autocomplete ul li[aria-selected] .cm-completionMatchedText': { color: '#fff' },
    '.cm-panels': { background: '#e5e5e5', color: '#000' },
    '.wc-doc a': { color: '#0b5394' },
    '.wc-br0': { color: '#000099' },
    '.wc-br1': { color: '#990099' },
    '.wc-br2': { color: '#006600' },
  },
  { dark: false },
);
const cs6Highlight = HighlightStyle.define([
  { tag: [t.tagName, t.angleBracket, t.attributeName], color: '#000099' },
  { tag: [t.attributeValue, t.string, t.special(t.string)], color: '#0000ff' },
  { tag: [t.comment, t.lineComment, t.blockComment], color: '#999999', fontStyle: 'italic' },
  { tag: [t.keyword, t.controlKeyword, t.operatorKeyword, t.definitionKeyword, t.modifier, t.bool, t.null], color: '#000099', fontWeight: 'bold' },
  { tag: [t.number, t.unit], color: '#ff0000' },
  { tag: [t.propertyName], color: '#009999' },
  { tag: [t.className, t.labelName, t.typeName], color: '#ff00ff' },
  { tag: [t.function(t.variableName), t.function(t.propertyName)], color: '#000000' },
  { tag: [t.color, t.atom, t.constant(t.name)], color: '#0000ff' },
  { tag: [t.processingInstruction, t.documentMeta], color: '#990000' },
  { tag: t.invalid, color: '#cc0000', textDecoration: 'underline wavy' },
]);

/* -------------------------------- 2077 (Endormi's VS Code theme, MIT) */
const chrome2077 = EditorView.theme(
  {
    '&': { height: '100%', fontSize: 'var(--code-size, 13px)', backgroundColor: '#030d22', color: '#fdfeff' },
    '.cm-scroller': { fontFamily: mono, lineHeight: '1.55' },
    '.cm-content': { caretColor: '#ee0077' },
    '.cm-cursor, .cm-dropCursor': { borderLeftColor: '#ee0077', borderLeftWidth: '2px' },
    '.cm-gutters': { backgroundColor: '#030d22', color: '#ee0077', borderRight: '1px solid #150f53' },
    '.cm-activeLine': { backgroundColor: '#1c1347' },
    '.cm-activeLineGutter': { backgroundColor: '#1c1347', color: '#47a1fa' },
    '&.cm-focused .cm-selectionBackground, .cm-selectionBackground, ::selection': { backgroundColor: '#310072 !important' },
    '.cm-selectionMatch': { backgroundColor: '#2a0a5e' },
    '.cm-matchingBracket, &.cm-focused .cm-matchingBracket': { backgroundColor: 'transparent', outline: '1px solid #ee0077', color: 'inherit' },
    '.cm-searchMatch': { backgroundColor: '#5c1a6e', outline: '1px solid #ff2cf1' },
    '.cm-foldPlaceholder': { background: '#1d1641', border: '1px solid #2b1f8a', color: '#0ef3ff' },
    '.cm-tooltip': { background: '#04112c', border: '1px solid #2b1f8a', borderRadius: '4px', boxShadow: '0 0 18px rgba(238,0,119,.25)', color: '#fdfeff' },
    '.cm-tooltip-autocomplete ul li[aria-selected]': { background: '#073170', color: '#fff' },
    '.cm-completionMatchedText': { color: '#ff2e97' },
    '.cm-panels': { background: '#0b082e', color: '#fdfeff' },
    '.cm-panels input, .cm-panels button': { background: '#0d0931', color: '#fdfeff', border: '1px solid #150f53' },
    '.wc-doc a': { color: '#0ef3ff' },
    '.wc-br0': { color: '#ffd400' },
    '.wc-br1': { color: '#ff2cf1' },
    '.wc-br2': { color: '#0ef3ff' },
  },
  { dark: true },
);
const highlight2077 = HighlightStyle.define([
  { tag: [t.tagName], color: '#ff2e97' },
  { tag: [t.angleBracket], color: '#ff2e97' },
  { tag: t.attributeName, color: '#ffd400' },
  { tag: [t.attributeValue, t.string, t.special(t.string)], color: '#0ef3ff' },
  { tag: [t.comment, t.lineComment, t.blockComment], color: '#0098df', fontStyle: 'italic' },
  { tag: [t.number, t.unit], color: '#ffd400' },
  { tag: [t.bool, t.null], color: '#ff2e97' },
  { tag: [t.keyword, t.controlKeyword, t.operatorKeyword, t.modifier], color: '#ff2cf1' },
  { tag: [t.definitionKeyword], color: '#ff1bf0' },
  { tag: [t.function(t.variableName), t.function(t.definition(t.variableName))], color: '#39c0ff' },
  { tag: [t.function(t.propertyName)], color: '#ffd400' },
  { tag: [t.className, t.typeName], color: '#ff2e97' },
  { tag: [t.propertyName], color: '#0ef3ff' },
  { tag: [t.variableName], color: '#fdfeff' },
  { tag: [t.definition(t.variableName)], color: '#ff2e97' },
  { tag: [t.color, t.atom, t.constant(t.name)], color: '#fd21ef' },
  { tag: [t.labelName], color: '#ffd400' },
  { tag: [t.processingInstruction, t.documentMeta], color: '#a83dff' },
  { tag: [t.heading], color: '#ff2e97', fontWeight: 'bold' },
  { tag: [t.link, t.url], color: '#ff22f0', textDecoration: 'underline' },
  { tag: [t.emphasis], fontStyle: 'italic', color: '#ff2e97' },
  { tag: [t.strong], fontWeight: 'bold', color: '#ff2e97' },
  { tag: t.invalid, color: '#9e0a52' },
]);

/* --------------------------------------------- light/dark counterparts */

interface Palette {
  dark: boolean;
  bg: string;
  fg: string;
  gutterBg: string;
  gutterFg: string;
  activeLine: string;
  selection: string;
  cursor: string;
  tooltipBg: string;
  tooltipBorder: string;
  pick: string;
  pickFg: string;
  matched: string;
  link: string;
  brackets: [string, string, string];
  font?: string;
}

function chrome(c: Palette) {
  return EditorView.theme(
    {
      '&': { height: '100%', fontSize: 'var(--code-size, 13px)', backgroundColor: c.bg, color: c.fg },
      '.cm-scroller': { fontFamily: c.font ?? mono, lineHeight: '1.5' },
      '.cm-content': { caretColor: c.cursor },
      '.cm-cursor, .cm-dropCursor': { borderLeftColor: c.cursor },
      '.cm-gutters': { backgroundColor: c.gutterBg, color: c.gutterFg, borderRight: `1px solid ${c.tooltipBorder}` },
      '.cm-activeLine': { backgroundColor: c.activeLine },
      '.cm-activeLineGutter': { backgroundColor: c.activeLine, color: c.fg },
      '&.cm-focused .cm-selectionBackground, .cm-selectionBackground, ::selection': { backgroundColor: c.selection + ' !important' },
      '.cm-matchingBracket, &.cm-focused .cm-matchingBracket': { backgroundColor: 'transparent', outline: `1px solid ${c.gutterFg}` },
      '.cm-foldPlaceholder': { background: c.tooltipBg, border: `1px solid ${c.tooltipBorder}`, color: c.fg },
      '.cm-tooltip': { background: c.tooltipBg, border: `1px solid ${c.tooltipBorder}`, borderRadius: '4px', boxShadow: '0 6px 18px rgba(0,0,0,.3)', color: c.fg },
      '.cm-tooltip-autocomplete ul li[aria-selected]': { background: c.pick, color: c.pickFg },
      '.cm-completionMatchedText': { color: c.matched },
      '.cm-panels': { background: c.gutterBg, color: c.fg },
      '.wc-doc a': { color: c.link },
      '.wc-br0': { color: c.brackets[0] },
      '.wc-br1': { color: c.brackets[1] },
      '.wc-br2': { color: c.brackets[2] },
    },
    { dark: c.dark },
  );
}

// Valco Weave Light (teal accent, GitHub-like code colours)
const weaveLight = [
  chrome({ dark: false, bg: '#ffffff', fg: '#1f2328', gutterBg: '#f7f8fa', gutterFg: '#8c959f', activeLine: '#f2f7f7', selection: '#bfe8e3', cursor: '#0f8a7e', tooltipBg: '#ffffff', tooltipBorder: '#d0d7de', pick: '#d8f3ef', pickFg: '#1f2328', matched: '#0f8a7e', link: '#0f8a7e', brackets: ['#b08800', '#8250df', '#0969da'] }),
  syntaxHighlighting(HighlightStyle.define([
    { tag: [t.tagName, t.angleBracket], color: '#116329' },
    { tag: t.attributeName, color: '#0550ae' },
    { tag: [t.attributeValue, t.string, t.special(t.string)], color: '#0a3069' },
    { tag: [t.comment, t.lineComment, t.blockComment], color: '#6e7781', fontStyle: 'italic' },
    { tag: [t.keyword, t.controlKeyword, t.operatorKeyword, t.definitionKeyword, t.modifier], color: '#cf222e' },
    { tag: [t.number, t.unit, t.bool, t.null, t.atom, t.color, t.constant(t.name)], color: '#0550ae' },
    { tag: [t.propertyName], color: '#0550ae' },
    { tag: [t.className, t.typeName], color: '#953800' },
    { tag: [t.function(t.variableName), t.function(t.propertyName)], color: '#8250df' },
    { tag: [t.processingInstruction, t.documentMeta], color: '#6e7781' },
    { tag: t.invalid, color: '#cf222e', textDecoration: 'underline wavy' },
  ])),
];

// Windows Vista dark: Aero black glass with Visual Studio dark code colours
const vistaDark = [
  chrome({ dark: true, bg: '#1e1e1e', fg: '#dcdcdc', gutterBg: '#1e1e1e', gutterFg: '#2b91af', activeLine: '#262a33', selection: '#264f78', cursor: '#dcdcdc', tooltipBg: '#252526', tooltipBorder: '#3f3f46', pick: '#0e639c', pickFg: '#ffffff', matched: '#4fc1ff', link: '#4fc1ff', brackets: ['#ffd700', '#da70d6', '#179fff'], font: "Consolas, 'Lucida Console', 'DejaVu Sans Mono', monospace" }),
  syntaxHighlighting(HighlightStyle.define([
    { tag: [t.tagName], color: '#569cd6' },
    { tag: [t.angleBracket], color: '#808080' },
    { tag: t.attributeName, color: '#9cdcfe' },
    { tag: [t.attributeValue, t.string, t.special(t.string)], color: '#ce9178' },
    { tag: [t.comment, t.lineComment, t.blockComment], color: '#57a64a' },
    { tag: [t.keyword, t.controlKeyword, t.operatorKeyword, t.definitionKeyword, t.modifier, t.bool, t.null], color: '#569cd6' },
    { tag: [t.number, t.unit], color: '#b5cea8' },
    { tag: [t.propertyName], color: '#9cdcfe' },
    { tag: [t.className, t.typeName], color: '#d7ba7d' },
    { tag: [t.function(t.variableName), t.function(t.propertyName)], color: '#dcdcaa' },
    { tag: [t.color, t.atom, t.constant(t.name)], color: '#ce9178' },
    { tag: [t.processingInstruction, t.documentMeta], color: '#9b9b9b' },
    { tag: t.invalid, color: '#f44747', textDecoration: 'underline wavy' },
  ])),
];

// Dreamweaver CS6 dark: CS6-era charcoal greys with Dreamweaver-style colours lifted for dark
const cs6Dark = [
  chrome({ dark: true, bg: '#2a2a2a', fg: '#e0e0e0', gutterBg: '#333333', gutterFg: '#8c8c8c', activeLine: '#323232', selection: '#30507e', cursor: '#ffffff', tooltipBg: '#3a3a3a', tooltipBorder: '#1e1e1e', pick: '#3875d7', pickFg: '#ffffff', matched: '#9ec2ff', link: '#7fb2ff', brackets: ['#8fb4ff', '#e48fe4', '#8fd18f'], font: "'Courier New', Consolas, 'DejaVu Sans Mono', monospace" }),
  syntaxHighlighting(HighlightStyle.define([
    { tag: [t.tagName, t.angleBracket, t.attributeName], color: '#7fa8ff' },
    { tag: [t.attributeValue, t.string, t.special(t.string)], color: '#f0b45c' },
    { tag: [t.comment, t.lineComment, t.blockComment], color: '#8a8a8a', fontStyle: 'italic' },
    { tag: [t.keyword, t.controlKeyword, t.operatorKeyword, t.definitionKeyword, t.modifier, t.bool, t.null], color: '#7fa8ff', fontWeight: 'bold' },
    { tag: [t.number, t.unit], color: '#ff7b7b' },
    { tag: [t.propertyName], color: '#5fd1c4' },
    { tag: [t.className, t.labelName, t.typeName], color: '#f08cf0' },
    { tag: [t.color, t.atom, t.constant(t.name)], color: '#f0b45c' },
    { tag: [t.processingInstruction, t.documentMeta], color: '#d08f8f' },
    { tag: t.invalid, color: '#ff6b6b', textDecoration: 'underline wavy' },
  ])),
];

// 2077 light: the same neon family, deepened so it reads on a pale background
const light2077 = [
  chrome({ dark: false, bg: '#fdf6fb', fg: '#1b1036', gutterBg: '#f7ecf5', gutterFg: '#d6006c', activeLine: '#f6e3f2', selection: '#ffc7e6', cursor: '#d6006c', tooltipBg: '#ffffff', tooltipBorder: '#e9b8d8', pick: '#ffe0f1', pickFg: '#1b1036', matched: '#d6006c', link: '#007c99', brackets: ['#a07800', '#c000b8', '#00809c'] }),
  syntaxHighlighting(HighlightStyle.define([
    { tag: [t.tagName, t.angleBracket], color: '#d6006c' },
    { tag: t.attributeName, color: '#9a7300' },
    { tag: [t.attributeValue, t.string, t.special(t.string)], color: '#00809c' },
    { tag: [t.comment, t.lineComment, t.blockComment], color: '#2f7fbf', fontStyle: 'italic' },
    { tag: [t.number, t.unit], color: '#9a7300' },
    { tag: [t.bool, t.null], color: '#d6006c' },
    { tag: [t.keyword, t.controlKeyword, t.operatorKeyword, t.modifier, t.definitionKeyword], color: '#b800b0' },
    { tag: [t.function(t.variableName), t.function(t.definition(t.variableName))], color: '#0070c9' },
    { tag: [t.function(t.propertyName)], color: '#9a7300' },
    { tag: [t.className, t.typeName], color: '#d6006c' },
    { tag: [t.propertyName], color: '#00809c' },
    { tag: [t.definition(t.variableName)], color: '#d6006c' },
    { tag: [t.color, t.atom, t.constant(t.name)], color: '#b000a8' },
    { tag: [t.processingInstruction, t.documentMeta], color: '#7a2bd6' },
    { tag: t.invalid, color: '#c4004f', textDecoration: 'underline wavy' },
  ])),
];

export function editorTheme(name: ThemeName, mode: ThemeMode = NATIVE_MODE[name]): Extension {
  if (name === '2077') return mode === 'dark' ? [chrome2077, syntaxHighlighting(highlight2077)] : light2077;
  if (name === 'vista') return mode === 'light' ? [vistaChrome, syntaxHighlighting(vistaHighlight)] : vistaDark;
  if (name === 'cs6') return mode === 'light' ? [cs6Chrome, syntaxHighlighting(cs6Highlight)] : cs6Dark;
  return mode === 'dark' ? [oneDark, darkChrome] : weaveLight;
}

/** Valco Office colour schemes (Office 2007's Blue, Silver and Black), shared with Valco Word and Valco Express.
 *  They recolour the Windows Vista theme's menu bar, command bar, panel headers and status bar. */
export type Scheme = 'Blue' | 'Silver' | 'Black';
export const SCHEMES: Scheme[] = ['Blue', 'Silver', 'Black'];
