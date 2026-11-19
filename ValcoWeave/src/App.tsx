import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { EditorView, ViewUpdate } from '@codemirror/view';
import { EditorView as EV } from '@codemirror/view';
import type { EditorState, Text, Transaction } from '@codemirror/state';
import { undo, redo } from '@codemirror/commands';
import { setDiagnostics } from '@codemirror/lint';

import { DEMO_SITE } from './lib/demo';
import { createPlatform, resetDemoSite, type FileEntry, type Platform } from './lib/platform';
import { basename, dirname, ext, isHtml, isImage, isInside, isTextFile, join, norm, relative, resolveUrl } from './lib/paths';
import {
  ancestors, attrPatch, buildMap, elementAt, escText, findByStart, findTag, getAttr, indentOf, indentSnippet, label,
  renamePatches, suggestSelector, type ElInfo, type HtmlMap, type Patch,
} from './lib/htmlmap';
import { invalidateAsset, renderDocument } from './lib/render';
import { lintHtml, type Problem } from './lib/lint';
import { makeState, langLabel, wrapCompartment, minimapCompartment, themeCompartment, editorTheme, minimap, type UpdateHandler } from './lib/editorSetup';
import { THEMES, MODES, SCHEMES, type Scheme, type ThemeName, type ThemeMode, type ModePref } from './lib/themes';
import { cssDiagnostics, formatDocument, type IntelliHost, type SiteInfo } from './lib/intellisense';
import Palette, { type PaletteItem } from './components/Palette';
import GitPanel from './components/GitPanel';
import ValidatorPanel, { type FileResult, type W3cSettings } from './components/ValidatorPanel';
import { setW3cIssues, clearW3cIssues, issuesFromMessages } from './lib/w3cMarks';
import { validate as w3cValidate, type W3cMessage } from './lib/w3c';
import { INSERTS, type InsertItem } from './lib/inserts';
import { mediaWidths } from './lib/css';
import { makeZip } from './lib/zip';
import type { DocTemplate } from './lib/templates';

import CodeEditor from './components/CodeEditor';
import { Ribbon, groupsFromMenu, type RibbonTabDef, type AppMenuDef } from './components/Ribbon';

interface UpdateInfo { ok: boolean; available: boolean; latest?: string; installed: string; error?: string }
const RELEASE_PAGE = 'https://github.com/Raidenplays/Valco-Office-Suite/releases/latest';
const INSTALLER_URL = RELEASE_PAGE + '/download/ValcoOffice-Setup.exe';
import DesignView, { type DesignApi } from './components/DesignView';
import PropertiesPanel from './components/PropertiesPanel';
import CssDesigner, { type CssSource } from './components/CssDesigner';
import FilesPanel from './components/FilesPanel';
import { DomPanel, InsertPanel, ProblemsPanel, SearchPanel, type InsertPos, type SearchHit } from './components/Panels';
import { AboutDialog, InsertDialog, Logo, SuiteMark, suiteMarkUrl, NewDocDialog, PromptDialog, PublishDialog, SiteDialog, type SiteConfig } from './components/Dialogs';
import { ContextMenu, type MenuItem } from './components/ui';

type ViewMode = 'code' | 'split' | 'design' | 'live';
type RightTab = 'files' | 'insert' | 'css' | 'dom' | 'git';
type BottomTab = 'props' | 'problems' | 'search' | 'w3c';

interface Buf {
  path: string;
  state: EditorState;
  saved: Text;
  dirty: boolean;
}

type Dialog =
  | { kind: 'new'; dir: string }
  | { kind: 'prompt'; title: string; label: string; value: string; ok?: string; onOk(v: string): void }
  | { kind: 'insert'; item: InsertItem; anchor: { start: number; pos: InsertPos } | null }
  | { kind: 'site' }
  | { kind: 'publish' }
  | { kind: 'about' };

const RECENT_KEY = 'weavecraft.recent';
const PREFS_KEY = 'weavecraft.prefs';
const DEVICE_WIDTHS: [string, number | null][] = [['Fit', null], ['Desktop 1280', 1280], ['Laptop 1024', 1024], ['Tablet 768', 768], ['Phone 414', 414], ['Phone 375', 375]];

function loadJson<T>(key: string, def: T): T {
  try {
    const v = localStorage.getItem(key);
    return v ? (JSON.parse(v) as T) : def;
  } catch {
    return def;
  }
}
function saveJson(key: string, v: unknown) {
  try {
    localStorage.setItem(key, JSON.stringify(v));
  } catch {
    /* ignore */
  }
}

/** Collect class names, ids and CSS custom properties from HTML or CSS text. */
function indexText(t: string, isCss: boolean, idx: { classes: Set<string>; ids: Set<string>; vars: Set<string> }) {
  if (t.length > 2_000_000) return;
  const css = isCss ? t : (t.match(/<style[^>]*>[\s\S]*?<\/style>/gi) ?? []).join('\n');
  const cleanCss = css.replace(/\/\*[\s\S]*?\*\//g, '');
  for (const m of cleanCss.matchAll(/(?:^|[^\w-])\.(-?[_a-zA-Z][\w-]*)(?=[^;{}]*\{)/gm)) idx.classes.add(m[1]);
  for (const m of cleanCss.matchAll(/#(-?[_a-zA-Z][\w-]*)(?=[^;{}]*\{)/g)) idx.ids.add(m[1]);
  for (const m of cleanCss.matchAll(/(--[\w-]+)\s*:/g)) idx.vars.add(m[1]);
  if (!isCss) {
    for (const m of t.matchAll(/\bclass\s*=\s*["']([^"']*)["']/gi)) for (const c of m[1].split(/\s+/)) if (/^-?[_a-zA-Z][\w-]*$/.test(c)) idx.classes.add(c);
    for (const m of t.matchAll(/\bid\s*=\s*["']([^"'\s]+)["']/gi)) idx.ids.add(m[1]);
  }
}

function diffPatch(a: string, b: string): Patch {
  let s = 0;
  while (s < a.length && s < b.length && a[s] === b[s]) s++;
  let e = 0;
  while (e < a.length - s && e < b.length - s && a[a.length - 1 - e] === b[b.length - 1 - e]) e++;
  return { from: s, to: a.length - e, insert: b.slice(s, b.length - e) };
}

/** Element at map index `id` in a browser-parsed copy of the page (for selector matching). */
function domElementFor(doc: Document, id: number): Element | null {
  let i = 0;
  let hit: Element | null = null;
  const walk = (n: ParentNode) => {
    for (const c of Array.from(n.children)) {
      if (hit) return;
      if (i++ === id) {
        hit = c;
        return;
      }
      walk(c);
      if (c instanceof HTMLTemplateElement) walk(c.content);
    }
  };
  walk(doc);
  return hit;
}

export default function App() {
  const [platform, setPlatform] = useState<Platform | null>(null);
  useEffect(() => {
    createPlatform().then(setPlatform);
  }, []);
  if (!platform) return <div className="boot"><Logo size={48} /></div>;
  return <Workspace platform={platform} />;
}

function Workspace({ platform }: { platform: Platform }) {
  const prefs = useMemo(() => {
    const p = loadJson(PREFS_KEY, { view: 'split' as ViewMode, splitDir: 'v' as 'v' | 'h', wrap: false, right: 260, bottom: 210, split: 0.5, rightW: 300, minimap: true, codeSize: 13, theme: 'vista' as ThemeName, mode: 'light' as ModePref, suite: 1 });
    // First run as Valco Weave: switch to the suite look (Windows Vista, light) like the other Valco apps.
    if ((p as { suite?: number }).suite !== 1) Object.assign(p, { theme: 'vista', mode: 'light', suite: 1 });
    return p;
  }, []);

  /* ------------------------------------------------------------- state */
  const [root, setRoot] = useState<string | null>(null);
  const [siteCfg, setSiteCfg] = useState<SiteConfig>({ name: '' });
  const [siteFiles, setSiteFiles] = useState<FileEntry[]>([]);
  const [filesKey, setFilesKey] = useState(0);
  const [published, setPublished] = useState<Record<string, number>>({});

  const [tabs, setTabs] = useState<string[]>([]);
  const [active, setActiveState] = useState<string | null>(null);
  const [codeTarget, setCodeTargetState] = useState<Record<string, string>>({});
  const [, setBufTick] = useState(0);

  const [view, setView] = useState<ViewMode>(prefs.view);
  const [splitDir, setSplitDir] = useState<'v' | 'h'>(prefs.splitDir);
  const [wrap, setWrap] = useState<boolean>(prefs.wrap);
  const [showMap, setShowMap] = useState<boolean>(prefs.minimap ?? true);
  const [codeSize, setCodeSize] = useState<number>(prefs.codeSize ?? 13);
  const [palette, setPalette] = useState<null | 'commands' | 'files'>(null);
  const [theme, setTheme] = useState<ThemeName>(prefs.theme ?? 'vista');
  const [w3cResults, setW3cResults] = useState<FileResult[]>([]);
  const [w3cBusy, setW3cBusy] = useState<string | null>(null);
  const [w3cSettings, setW3cSettings] = useState<W3cSettings>({ validateOnSave: false, showWarnings: true, showInfo: true, notifications: true, ...((prefs as { w3c?: W3cSettings }).w3c ?? {}) });
  const validateOnSave = w3cSettings.validateOnSave;
  const [w3cStatus, setW3cStatus] = useState<null | 'loading' | 'valid'>(null);
  const [scheme, setScheme] = useState<Scheme>((prefs as { scheme?: Scheme }).scheme ?? 'Blue');
  const [modePref, setModePref] = useState<ModePref>((prefs as { mode?: ModePref }).mode ?? 'light');
  const [systemDark, setSystemDark] = useState(() => window.matchMedia?.('(prefers-color-scheme: dark)').matches ?? true);
  useEffect(() => {
    const mq = window.matchMedia?.('(prefers-color-scheme: dark)');
    if (!mq) return;
    const h = () => setSystemDark(mq.matches);
    mq.addEventListener('change', h);
    return () => mq.removeEventListener('change', h);
  }, []);
  const mode: ThemeMode = modePref === 'system' ? (systemDark ? 'dark' : 'light') : modePref;
  const themeRef = useRef(theme);
  themeRef.current = theme;
  const modeRef = useRef(mode);
  modeRef.current = mode;
  const [width, setWidth] = useState<number | null>(null);
  const [splitRatio, setSplitRatio] = useState<number>(prefs.split);
  const [rightW, setRightW] = useState<number>(prefs.rightW);
  const [bottomH, setBottomH] = useState<number>(prefs.bottom);
  const [rightTab, setRightTab] = useState<RightTab>('files');
  const [bottomTab, setBottomTab] = useState<BottomTab>('props');

  const [map, setMap] = useState<HtmlMap | null>(null);
  const [problems, setProblems] = useState<Problem[]>([]);
  const [render, setRender] = useState<{ html: string; map: HtmlMap } | null>(null);
  const [renderBump, setRenderBump] = useState(0);
  const [selStart, setSelStartState] = useState<number | null>(null);
  const [editRequest, setEditRequest] = useState<{ id: number; nonce: number; selectAll?: boolean } | null>(null);
  const [cursor, setCursor] = useState({ line: 1, col: 1 });
  const [insertPos, setInsertPos] = useState<InsertPos>('after');
  const [cssCache, setCssCache] = useState<Record<string, string>>({});

  const [dialog, setDialog] = useState<Dialog | null>(null);
  const [menuOpen, setMenuOpen] = useState<string | null>(null);
  const [ctx, setCtx] = useState<{ x: number; y: number; items: MenuItem[] } | null>(null);
  const [toast, setToast] = useState<string | null>(null);
  const [recent, setRecent] = useState<string[]>(() => loadJson<string[]>(RECENT_KEY, []));

  /* -------------------------------------------------------------- refs */
  const viewRef = useRef<EditorView | null>(null);
  const designApi = useRef<DesignApi | null>(null);
  const buffers = useRef(new Map<string, Buf>());
  const activeRef = useRef<string | null>(null);
  const shownRef = useRef<string | null>(null);
  const selRef = useRef<number | null>(null);
  const mapRef = useRef<HtmlMap | null>(null);
  const applying = useRef(false);
  const lastFocus = useRef<'code' | 'design'>('design');
  const pendingEdit = useRef<{ start: number; selectAll: boolean } | null>(null);
  const editNonce = useRef(0);
  const reparseTimer = useRef<number>(0);
  const cursorTimer = useRef<number>(0);
  const prevUrls = useRef<string[]>([]);
  const rootRef = useRef<string | null>(null);
  rootRef.current = root;

  // handy for debugging and automated tests: window.__weavecraft.view()
  (window as unknown as { __weavecraft: unknown }).__weavecraft = { view: () => viewRef.current };
  const shownPath = active ? codeTarget[active] ?? active : null;
  shownRef.current = shownPath;
  activeRef.current = active;
  mapRef.current = map;

  useEffect(() => {
    saveJson(PREFS_KEY, { view, splitDir, wrap, split: splitRatio, rightW, bottom: bottomH, minimap: showMap, codeSize, theme, mode: modePref, scheme, w3c: w3cSettings, suite: 1 });
  }, [view, splitDir, wrap, splitRatio, rightW, bottomH, showMap, codeSize, theme, modePref, scheme, w3cSettings]);
  // Started from Explorer with a folder or a page: open it straight away.
  useEffect(() => {
    if (platform.kind !== 'tauri') return;
    (async () => {
      const { invoke } = await import('@tauri-apps/api/core');
      const arg = await invoke<string | null>('launch_arg').catch(() => null);
      if (!arg) return;
      const isDir = await platform.readDir(arg).then(() => true, () => false);
      if (isDir) return openSite(arg);
      const cut = Math.max(arg.lastIndexOf('\\'), arg.lastIndexOf('/'));
      if (cut > 0) {
        await openSite(arg.slice(0, cut));
        await openFile(arg);
      }
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  useEffect(() => {
    const name = active ? active.split(/[\\/]/).pop() : root ? siteCfg.name || root.split(/[\\/]/).pop() : '';
    const dirty = active && buffers.current.get(active)?.dirty ? '*' : '';
    document.title = name ? `${name}${dirty} - Valco Weave` : 'Valco Weave';
  });
  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    document.documentElement.dataset.mode = mode;
    document.documentElement.dataset.scheme = scheme.toLowerCase();
  }, [theme, mode, scheme]);
  useEffect(() => {
    document.documentElement.style.setProperty('--code-size', codeSize + 'px');
    viewRef.current?.requestMeasure();
  }, [codeSize]);

  // Valco Office Update: checks github.com/Raidenplays/Valco-Office-Suite once a day
  const [update, setUpdate] = useState<UpdateInfo | null>(null);
  const checkRef = useRef<(manual?: boolean) => void>(() => {});
  useEffect(() => {
    const last = Number(localStorage.getItem('valco.update.last') ?? 0);
    if (localStorage.getItem('valco.update.auto') === '0' || Date.now() - last < 20 * 3600 * 1000) return;
    const t = window.setTimeout(() => { localStorage.setItem('valco.update.last', String(Date.now())); checkRef.current(false); }, 8000);
    return () => window.clearTimeout(t);
  }, []);

  const flash = useCallback((msg: string) => {
    setToast(msg);
    window.clearTimeout((flash as unknown as { t?: number }).t);
    (flash as unknown as { t?: number }).t = window.setTimeout(() => setToast(null), 3200);
  }, []);

  const setSel = (s: number | null) => {
    selRef.current = s;
    setSelStartState(s);
  };

  /* ---------------------------------------------------------- buffers */
  const captureView = () => {
    const sp = shownRef.current;
    if (sp && viewRef.current) {
      const b = buffers.current.get(sp);
      if (b) b.state = viewRef.current.state;
    }
  };
  const stateOf = (path: string): EditorState | null => {
    if (path === shownRef.current && viewRef.current) return viewRef.current.state;
    return buffers.current.get(path)?.state ?? null;
  };
  const textOf = (path: string): string | null => stateOf(path)?.doc.toString() ?? null;

  const relTo = (path: string) => {
    const base = activeRef.current && isHtml(activeRef.current) ? dirname(activeRef.current) : rootRef.current ?? '/';
    return relative(base, path);
  };

  const scheduleReparse = () => {
    window.clearTimeout(reparseTimer.current);
    reparseTimer.current = window.setTimeout(reparse, 140);
  };

  const onBufferChanged = (path: string) => {
    const b = buffers.current.get(path);
    const st = stateOf(path);
    if (b && st) {
      const dirty = !st.doc.eq(b.saved);
      if (dirty !== b.dirty) {
        b.dirty = dirty;
        setBufTick((t) => t + 1);
      }
    }
    if (path === activeRef.current) scheduleReparse();
    else setRenderBump((x) => x + 1);
  };

  const updateHandler = useRef<UpdateHandler>(() => {});
  updateHandler.current = (u: ViewUpdate) => {
    if (u.view !== viewRef.current) return;
    const sp = shownRef.current;
    if (!sp) return;
    if (u.docChanged) {
      if (!applying.current && sp === activeRef.current && selRef.current !== null) {
        setSel(u.changes.mapPos(selRef.current, 1));
      }
      onBufferChanged(sp);
    }
    if (u.selectionSet || u.docChanged) {
      const head = u.state.selection.main.head;
      const line = u.state.doc.lineAt(head);
      setCursor({ line: line.number, col: head - line.from + 1 });
    }
    if (u.focusChanged && u.view.hasFocus) lastFocus.current = 'code';
    if (u.selectionSet && u.view.hasFocus && sp === activeRef.current && !u.docChanged) {
      window.clearTimeout(cursorTimer.current);
      cursorTimer.current = window.setTimeout(() => {
        const m = mapRef.current;
        const v = viewRef.current;
        if (!m || !v || m.source.length !== v.state.doc.length) return;
        const el = elementAt(m, v.state.selection.main.head);
        if (el && el.start !== selRef.current) setSel(el.start);
      }, 120);
    }
  };

  const ensureBuffer = async (path: string, initialText?: string): Promise<Buf | null> => {
    const have = buffers.current.get(path);
    if (have) return have;
    let text = initialText;
    if (text === undefined) {
      try {
        text = await platform.readText(path);
      } catch (e) {
        flash('Could not open ' + basename(path) + ': ' + String(e));
        return null;
      }
    }
    const state = makeState(text, path, updateHandler, wrap, intelliHost.current, showMap, themeRef.current, modeRef.current);
    const b: Buf = { path, state, saved: state.doc, dirty: false };
    buffers.current.set(path, b);
    return b;
  };

  /** Apply text patches to any buffer (shown or not), keeping undo history. */
  const applyChanges = (path: string, patches: Patch[], selectAfter?: number | null) => {
    const st = stateOf(path);
    if (!st || !patches.length) return;
    const changes = [...patches].sort((a, b) => a.from - b.from).map((p) => ({ from: p.from, to: p.to, insert: p.insert }));
    const tr: Transaction = st.update({ changes, userEvent: 'input.weavecraft' });
    if (path === shownRef.current && viewRef.current) {
      applying.current = true;
      viewRef.current.dispatch(tr);
      applying.current = false;
    } else {
      const b = buffers.current.get(path);
      if (!b) return;
      b.state = tr.state;
      onBufferChanged(path);
    }
    if (path === activeRef.current) {
      if (selectAfter !== undefined) setSel(selectAfter);
      else if (selRef.current !== null) setSel(tr.changes.mapPos(selRef.current, 1));
      scheduleReparse();
    }
  };

  const replaceAllText = async (path: string, text: string) => {
    const b = await ensureBuffer(path);
    if (!b) return;
    const cur = textOf(path) ?? '';
    if (cur === text) return;
    applyChanges(path, [diffPatch(cur, text)]);
  };

  /* --------------------------------------------------- tabs & files */
  const setActive = (p: string | null) => {
    captureView();
    setActiveState(p);
    activeRef.current = p;
    shownRef.current = p ? codeTarget[p] ?? p : null;
    setSel(null);
    setMap(null);
    setRender(null);
    setProblems([]);
    if (p) setTimeout(reparse, 0);
  };
  const setCodeTarget = (page: string, path: string | null) => {
    captureView();
    setCodeTargetState((c) => {
      const n = { ...c };
      if (!path || path === page) delete n[page];
      else n[page] = path;
      return n;
    });
    shownRef.current = path ?? page;
  };

  const openFile = async (path: string, opts?: { line?: number }) => {
    path = norm(path);
    if (!isTextFile(path)) {
      await platform.openExternal(path).catch((e) => flash(String(e)));
      return;
    }
    // A related file of the open page? show it inside the page tab
    if (!tabs.includes(path)) {
      const b = await ensureBuffer(path);
      if (!b) return;
      setTabs((t) => (t.includes(path) ? t : [...t, path]));
    }
    if (activeRef.current !== path) setActive(path);
    else if (codeTarget[path]) setCodeTarget(path, null);
    if (opts?.line) setTimeout(() => gotoLine(opts.line!), 60);
  };

  const gotoLine = (line: number, from?: number, to?: number) => {
    const v = viewRef.current;
    if (!v) return;
    const l = v.state.doc.line(Math.max(1, Math.min(line, v.state.doc.lines)));
    const a = from ?? l.from;
    const b = to ?? a;
    v.dispatch({ selection: { anchor: a, head: Math.min(b, v.state.doc.length) }, effects: EV.scrollIntoView(a, { y: 'center' }) });
    if (view === 'design' || view === 'live') setView('split');
    setTimeout(() => v.focus(), 0);
  };

  const closeTab = async (path: string) => {
    captureView();
    if (buffers.current.get(path)?.dirty && !(await platform.ask(`${basename(path)} has unsaved changes. Close it anyway?`))) return;
    const idx = tabs.indexOf(path);
    const next = tabs.filter((t) => t !== path);
    setTabs(next);
    const b = buffers.current.get(path);
    if (b?.dirty) {
      // discard
      buffers.current.delete(path);
    } else if (b && !Object.values(codeTarget).includes(path)) {
      buffers.current.delete(path);
    }
    setCodeTargetState((c) => {
      const n = { ...c };
      delete n[path];
      return n;
    });
    if (active === path) setActive(next[Math.min(idx, next.length - 1)] ?? null);
  };

  const refreshFiles = useCallback(async () => {
    const r = rootRef.current;
    if (!r) return;
    invalidateAsset();
    try {
      setSiteFiles(await platform.listAll(r));
    } catch {
      /* ignore */
    }
    setFilesKey((k) => k + 1);
    setRenderBump((x) => x + 1);
  }, [platform]);

  const saveBuffer = async (path: string) => {
    const b = buffers.current.get(path);
    const st = stateOf(path);
    if (!b || !st) return false;
    try {
      await platform.writeText(path, st.doc.toString());
      b.saved = st.doc;
      b.dirty = false;
      setBufTick((t) => t + 1);
      return true;
    } catch (e) {
      flash('Save failed: ' + String(e));
      return false;
    }
  };

  const save = async () => {
    if (!active) return;
    const targets = new Set([shownPath!, active, ...relatedFiles.filter((r) => buffers.current.get(r)?.dirty)]);
    let n = 0;
    for (const p of targets) if (buffers.current.get(p)?.dirty && (await saveBuffer(p))) n++;
    flash(n ? `Saved ${n} file${n > 1 ? 's' : ''}` : 'No changes to save');
    setTimeout(refreshFiles, 300);
    if (n && validateOnSave) setTimeout(() => validateCurrent(true), 400);
  };
  const saveAll = async () => {
    let n = 0;
    for (const [p, b] of buffers.current) if (b.dirty && (await saveBuffer(p))) n++;
    flash(n ? `Saved ${n} file${n > 1 ? 's' : ''}` : 'Everything is saved');
    setTimeout(refreshFiles, 300);
  };

  useEffect(() => {
    const h = (e: BeforeUnloadEvent) => {
      if ([...buffers.current.values()].some((b) => b.dirty)) {
        e.preventDefault();
        e.returnValue = '';
      }
    };
    window.addEventListener('beforeunload', h);
    platform.onCloseRequest?.(async () => {
      if (![...buffers.current.values()].some((b) => b.dirty)) return true;
      return platform.ask('You have unsaved changes. Quit Valco Weave and discard them?');
    });
    return () => window.removeEventListener('beforeunload', h);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  /* ---------------------------------------------------------- site */
  /** Back to the start page (Valco button > Start Page). */
  const closeSite = async () => {
    if ([...buffers.current.values()].some((b) => b.dirty) && !(await platform.ask('You have unsaved changes. Close the site and discard them?'))) return;
    buffers.current.clear();
    setTabs([]);
    setCodeTargetState({});
    setActive(null);
    setRoot(null);
    rootRef.current = null;
  };
  const exitApp = async () => {
    if (platform.kind === 'tauri') {
      const { getCurrentWindow } = await import('@tauri-apps/api/window');
      getCurrentWindow().close();
    } else window.close();
  };

  const openSite = async (r: string) => {
    if ([...buffers.current.values()].some((b) => b.dirty) && !(await platform.ask('You have unsaved changes. Open another site and discard them?'))) return;
    r = norm(r);
    buffers.current.clear();
    setTabs([]);
    setCodeTargetState({});
    setActive(null);
    setRoot(r);
    rootRef.current = r;
    let cfg: SiteConfig = { name: basename(r) || r };
    try {
      cfg = { ...cfg, ...JSON.parse(await platform.readText(join(r, '.weavecraft/site.json'))) };
    } catch {
      /* no config yet */
    }
    setSiteCfg(cfg);
    try {
      setPublished(JSON.parse(await platform.readText(join(r, '.weavecraft/publish-state.json'))));
    } catch {
      setPublished({});
    }
    let files: FileEntry[] = [];
    try {
      files = await platform.listAll(r);
    } catch (e) {
      flash(String(e));
    }
    setSiteFiles(files);
    setFilesKey((k) => k + 1);
    if (platform.kind === 'tauri' || r === platform.demoRoot) {
      const nr = [r, ...recent.filter((x) => x !== r)].slice(0, 8);
      setRecent(nr);
      saveJson(RECENT_KEY, nr);
    }
    const idx = files.find((f) => /^index\.html?$/i.test(f.name) && dirname(f.path) === r);
    if (idx) {
      const b = await ensureBuffer(idx.path);
      if (b) {
        setTabs([idx.path]);
        setActive(idx.path);
      }
    }
  };

  const pickSite = async () => {
    try {
      const r = await platform.pickFolder();
      if (r) await openSite(r);
    } catch (e) {
      if ((e as Error)?.name !== 'AbortError') flash(String(e));
    }
  };

  const createSampleSite = async () => {
    try {
      const parent = await platform.pickFolder();
      if (!parent) return;
      let dir = join(parent, 'maple-lane-bakery');
      let n = 2;
      while (await platform.exists(dir)) dir = join(parent, 'maple-lane-bakery-' + n++);
      for (const [rel, text] of Object.entries(DEMO_SITE)) await platform.writeText(join(dir, rel), text);
      await openSite(dir);
    } catch (e) {
      flash('Could not create the sample site: ' + String(e));
    }
  };

  const saveSiteCfg = async (c: SiteConfig) => {
    setSiteCfg(c);
    if (root) {
      await platform.mkdir(join(root, '.weavecraft'));
      await platform.writeText(join(root, '.weavecraft/site.json'), JSON.stringify(c, null, 2));
    }
  };

  /* -------------------------------------------------- parse & render */
  const reparse = () => {
    const a = activeRef.current;
    if (!a || !isHtml(a)) {
      setMap(null);
      setProblems([]);
      return;
    }
    const text = textOf(a);
    if (text === null) return;
    const { map: m } = buildMap(text);
    mapRef.current = m;
    setMap(m);
    const files = new Set(siteFilesRef.current.map((f) => f.path));
    for (const p of buffers.current.keys()) files.add(p);
    const probs = lintHtml(m, a, rootRef.current, rootRef.current ? files : null);
    try {
      for (const d of cssDiagnostics(text, true)) {
        probs.push({ ...d, line: text.slice(0, d.from).split('\n').length });
      }
      probs.sort((x, y) => x.from - y.from);
    } catch {
      /* CSS service hiccup: keep HTML problems */
    }
    setProblems(probs);
    const v = viewRef.current;
    if (v && shownRef.current === a && v.state.doc.length === text.length) {
      v.dispatch(
        setDiagnostics(
          v.state,
          probs.map((p) => ({ from: Math.min(p.from, text.length), to: Math.min(Math.max(p.to, p.from), text.length), severity: p.severity, message: p.message })),
        ),
      );
    }
  };
  const siteFilesRef = useRef<FileEntry[]>([]);
  siteFilesRef.current = siteFiles;
  useEffect(() => {
    if (activeRef.current) reparse();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [siteFiles]);

  const renderCtx = useMemo(
    () => ({
      readText: async (p: string) => {
        const t = textOf(p);
        if (t !== null) return t;
        try {
          return await platform.readText(p);
        } catch {
          return null;
        }
      },
      readBytes: async (p: string) => {
        try {
          return await platform.readBytes(p);
        } catch {
          return null;
        }
      },
    }),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [platform],
  );

  useEffect(() => {
    if (!map || !active || view === 'code') return;
    let cancelled = false;
    const t = window.setTimeout(async () => {
      const res = await renderDocument(map.source, view === 'live' ? 'live' : 'design', { ...renderCtx, docPath: active, siteRoot: root });
      if (cancelled) {
        res.urls.forEach((u) => URL.revokeObjectURL(u));
        return;
      }
      const old = prevUrls.current;
      prevUrls.current = res.urls;
      setTimeout(() => old.forEach((u) => URL.revokeObjectURL(u)), 4000);
      if (pendingEdit.current !== null) {
        const pe = pendingEdit.current;
        const el = findByStart(map, pe.start);
        pendingEdit.current = null;
        if (el) setEditRequest({ id: el.id, nonce: ++editNonce.current, selectAll: pe.selectAll });
      }
      setRender({ html: res.html, map });
    }, 30);
    return () => {
      cancelled = true;
      window.clearTimeout(t);
    };
  }, [map, view, renderBump, active, root, renderCtx, theme, mode]);

  // keep code view wrapping in sync
  useEffect(() => {
    const v = viewRef.current;
    const r = w3cResults.find((x) => x.path === shownPath);
    if (v && r?.result && v.state.doc.length) {
      const shown = r.result.messages.filter((m) => (m.type !== 'warning' || w3cSettings.showWarnings) && (m.type !== 'info' || w3cSettings.showInfo));
      if (r.result.checkedAt > Date.now() - 600_000) v.dispatch({ effects: setW3cIssues.of(issuesFromMessages(v.state.doc, shown)) });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [shownPath, w3cSettings.showWarnings, w3cSettings.showInfo]);
  useEffect(() => {
    viewRef.current?.dispatch({ effects: [wrapCompartment.reconfigure(wrap ? EV.lineWrapping : []), minimapCompartment.reconfigure(showMap ? minimap() : []), themeCompartment.reconfigure(editorTheme(theme, mode))] });
  }, [wrap, shownPath, showMap, theme, mode]);

  /* ------------------------------------------------- derived values */
  const selEl: ElInfo | null = map && selStart !== null ? findByStart(map, selStart) ?? elementAt(map, selStart) : null;
  const renderSelId = render && selStart !== null ? findByStart(render.map, selStart)?.id ?? elementAt(render.map, selStart)?.id ?? null : null;
  const pageDir = active ? dirname(active) : root ?? '/';
  const relFiles = useMemo(() => siteFiles.map((f) => relative(pageDir, f.path)), [siteFiles, pageDir]);
  const relImages = useMemo(() => siteFiles.filter((f) => isImage(f.path)).map((f) => relative(pageDir, f.path)), [siteFiles, pageDir]);
  const relPages = useMemo(() => siteFiles.filter((f) => isHtml(f.path)).map((f) => relative(pageDir, f.path)), [siteFiles, pageDir]);

  const relatedFiles: string[] = useMemo(() => {
    if (!map || !active) return [];
    const out: string[] = [];
    for (const e of map.els) {
      const ref = e.tag === 'link' && /stylesheet/i.test(getAttr(e, 'rel') ?? '') ? getAttr(e, 'href') : e.tag === 'script' ? getAttr(e, 'src') : null;
      if (!ref) continue;
      const p = resolveUrl(ref, dirname(active), root);
      if (p && !out.includes(p) && (siteFiles.some((f) => f.path === p) || buffers.current.has(p))) out.push(p);
    }
    return out;
  }, [map, active, root, siteFiles]);

  // CSS sources for the designer
  useEffect(() => {
    for (const p of relatedFiles) {
      if (ext(p) !== 'css' || buffers.current.has(p) || cssCache[p] !== undefined) continue;
      platform.readText(p).then(
        (t) => setCssCache((c) => ({ ...c, [p]: t })),
        () => setCssCache((c) => ({ ...c, [p]: '' })),
      );
    }
  }, [relatedFiles, cssCache, platform]);

  const cssSources: CssSource[] = useMemo(() => {
    if (!map) return [];
    const out: CssSource[] = [];
    let n = 0;
    for (const e of map.els) {
      if (e.tag === 'style' && e.hasLoc && e.hasEndTag) {
        n++;
        out.push({ key: 'style:' + e.start, label: `<style> #${n} (line ${e.line})`, kind: 'style', text: map.source.slice(e.innerStart, e.innerEnd) });
      }
    }
    for (const p of relatedFiles) {
      if (ext(p) !== 'css') continue;
      const t = textOf(p) ?? cssCache[p];
      if (t === undefined) continue;
      out.push({ key: 'file:' + p, label: relative(dirname(active!), p), kind: 'file', path: p, text: t });
    }
    return out;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [map, relatedFiles, cssCache, renderBump]);

  const classList = useMemo(() => {
    const s = new Set<string>();
    for (const src of cssSources) {
      const re = /\.(-?[_a-zA-Z][\w-]*)/g;
      let m: RegExpExecArray | null;
      while ((m = re.exec(src.text.replace(/\/\*[\s\S]*?\*\//g, '').replace(/:\s*[^;{}]+;/g, ';')))) s.add(m[1]);
    }
    return [...s].sort();
  }, [cssSources]);

  const domDoc = useMemo(() => (map ? new DOMParser().parseFromString(map.source, 'text/html') : null), [map]);
  const matcher = useMemo(() => {
    if (!domDoc || !selEl) return null;
    const el = domElementFor(domDoc, selEl.id);
    if (!el) return null;
    return (sel: string) => {
      try {
        return el.matches(sel.replace(/::?(before|after|placeholder|selection|marker|first-line|first-letter)\b/g, ''));
      } catch {
        return false;
      }
    };
  }, [domDoc, selEl]);

  const mediaBar = useMemo(() => mediaWidths(cssSources.map((s) => s.text)), [cssSources]);

  /* ---------------------------------------------------- page editing */
  const page = () => {
    const a = activeRef.current;
    const m = mapRef.current;
    if (!a || !m || !isHtml(a)) return null;
    const src = textOf(a);
    if (src === null || src !== m.source) {
      reparse();
      const m2 = mapRef.current;
      if (!m2 || textOf(a) !== m2.source) return null;
      return { path: a, map: m2, src: m2.source };
    }
    return { path: a, map: m, src };
  };
  const pageChange = (patches: (Patch | null)[], selectAfter?: number | null) => {
    const a = activeRef.current;
    if (!a) return;
    applyChanges(a, patches.filter(Boolean) as Patch[], selectAfter);
  };
  const currentSel = (): ElInfo | null => {
    const pg = page();
    if (!pg || selRef.current === null) return null;
    return findByStart(pg.map, selRef.current) ?? elementAt(pg.map, selRef.current);
  };

  const setAttr = (name: string, value: string | null) => {
    const pg = page();
    const el = currentSel();
    if (!pg || !el) return;
    pageChange([attrPatch(pg.src, el, name, value)]);
  };

  const renameEl = (tag: string) => {
    const pg = page();
    const el = currentSel();
    if (!pg || !el || tag === el.tag) return;
    pageChange(renamePatches(pg.src, el, tag), el.start);
  };

  const setInner = (text: string) => {
    const el = currentSel();
    if (!el) return;
    pageChange([{ from: el.innerStart, to: el.innerEnd, insert: text }], el.start);
  };

  /** Insert an HTML snippet relative to an element. Returns the new element's start offset. */
  const insertHtml = (html: string, anchor: ElInfo | null, pos: InsertPos, opts?: { edit?: boolean; head?: boolean }) => {
    const pg = page();
    if (!pg) return;
    const { src, map: m } = pg;
    if (opts?.head) {
      const head = findTag(m, 'head');
      if (head && head.hasLoc) anchor = head;
      pos = 'inside';
    }
    if (!anchor || anchor.tag === 'html' || anchor.tag === 'head' && !opts?.head) {
      anchor = findTag(m, 'body');
      pos = 'inside';
    }
    if (anchor && anchor.tag === 'body') pos = pos === 'wrap' || pos === 'before' || pos === 'after' ? 'inside' : pos;
    if (!anchor) {
      // fragment: append at end
      const at = src.length;
      const ins = (src.endsWith('\n') || !src ? '' : '\n') + html + '\n';
      pageChange([{ from: at, to: at, insert: ins }], at + (ins.length - html.length - 1));
      return;
    }
    if (anchor.isVoid && pos === 'inside') pos = 'after';
    const indent = indentOf(src, anchor.start);
    let patch: Patch;
    let newStart: number;
    if (pos === 'after') {
      const snip = indentSnippet(html, indent);
      patch = { from: anchor.end, to: anchor.end, insert: '\n' + indent + snip };
      newStart = anchor.end + 1 + indent.length;
    } else if (pos === 'before') {
      const snip = indentSnippet(html, indent);
      patch = { from: anchor.start, to: anchor.start, insert: snip + '\n' + indent };
      newStart = anchor.start;
    } else if (pos === 'inside') {
      let i = anchor.innerEnd;
      while (i > anchor.innerStart && (src[i - 1] === ' ' || src[i - 1] === '\t')) i--;
      const inner = indent + '  ';
      if (src[i - 1] === '\n' || i === anchor.innerStart) {
        const lead = i === anchor.innerStart ? '\n' : '';
        const snip = indentSnippet(html, inner);
        const tail = i === anchor.innerStart ? '\n' + indent : '\n';
        patch = { from: i, to: i, insert: lead + inner + snip + tail };
        newStart = i + lead.length + inner.length;
        if (i !== anchor.innerStart) {
          // keep the closing tag's indentation
          patch = { from: i, to: i, insert: inner + snip + '\n' };
          newStart = i + inner.length;
        }
      } else {
        patch = { from: anchor.innerEnd, to: anchor.innerEnd, insert: html };
        newStart = anchor.innerEnd;
      }
    } else {
      // wrap
      const w = buildMap(html).map;
      const first = w.els.find((e) => e.hasLoc && !e.isVoid && w.roots.includes(e.id)) ?? w.els.find((e) => e.hasLoc && !e.isVoid && e.depth > 0 && !['html', 'head', 'body'].includes(e.tag));
      if (!first || !first.hasEndTag) {
        insertHtml(html, anchor, 'after', opts);
        return;
      }
      const elText = src.slice(anchor.start, anchor.end);
      const wrapped = html.slice(0, first.innerStart).replace(/\s*$/, '') + '\n' + indent + '  ' + indentSnippet(elText, '  ') + '\n' + indent + html.slice(first.innerEnd).replace(/^\s*/, '');
      patch = { from: anchor.start, to: anchor.end, insert: indentSnippet(wrapped, indent) };
      newStart = anchor.start;
    }
    if (opts?.edit) pendingEdit.current = { start: newStart, selectAll: true };
    pageChange([patch], newStart);
  };

  const doInsertItem = (item: InsertItem, values: Record<string, string>, anchorInfo?: { start: number; pos: InsertPos } | null) => {
    const html = item.html(values);
    const isHead = item.cat === 'Head';
    const v = viewRef.current;
    if (!anchorInfo && lastFocus.current === 'code' && v && shownRef.current === activeRef.current && view !== 'design' && view !== 'live') {
      const { from, to } = v.state.selection.main;
      const insert = from !== to && insertPos === 'wrap' ? html.replace(/>([^<]*)</, (_m, _t) => '>' + v.state.sliceDoc(from, to) + '<') : html;
      applyChanges(shownRef.current!, [{ from, to, insert }]);
      v.focus();
      return;
    }
    const pg = page();
    if (!pg) return;
    let anchor: ElInfo | null = null;
    let pos = insertPos;
    if (anchorInfo) {
      anchor = findByStart(pg.map, anchorInfo.start);
      pos = anchorInfo.pos;
    } else if (selRef.current !== null) {
      anchor = currentSel();
    }
    insertHtml(html, anchor, pos, { head: isHead, edit: item.cat === 'Text' && !['br', 'ul', 'ol', 'blockquote', 'pre'].includes(item.id) });
  };

  const requestInsert = (item: InsertItem, anchorInfo: { start: number; pos: InsertPos } | null = null) => {
    if (!activeRef.current || !isHtml(activeRef.current)) {
      flash('Open an HTML page first');
      return;
    }
    if (item.fields?.length) setDialog({ kind: 'insert', item, anchor: anchorInfo });
    else doInsertItem(item, {}, anchorInfo);
  };

  const insertFileRef = (path: string, anchorInfo: { start: number; pos: InsertPos } | null) => {
    const r = relative(dirname(activeRef.current!), path);
    const pg = page();
    if (!pg) return;
    const e = ext(path);
    const anchor = anchorInfo ? findByStart(pg.map, anchorInfo.start) : currentSel();
    const pos = anchorInfo?.pos ?? insertPos;
    const name = basename(path).replace(/\.[^.]+$/, '').replace(/[-_]+/g, ' ');
    if (isImage(path)) insertHtml(`<img src="${r}" alt="${name}">`, anchor, pos);
    else if (e === 'css') insertHtml(`<link rel="stylesheet" href="${r}">`, null, 'inside', { head: true });
    else if (e === 'js') insertHtml(`<script src="${r}"></script>`, findTag(pg.map, 'body'), 'inside');
    else if (['mp4', 'webm', 'ogv'].includes(e)) insertHtml(`<video src="${r}" controls width="640"></video>`, anchor, pos);
    else if (['mp3', 'wav', 'ogg'].includes(e)) insertHtml(`<audio src="${r}" controls></audio>`, anchor, pos);
    else insertHtml(`<a href="${r}">${escText(name)}</a>`, anchor, pos);
  };

  const deleteSelected = () => {
    const pg = page();
    const el = currentSel();
    if (!pg || !el || !el.hasLoc || ['html', 'head', 'body'].includes(el.tag)) return;
    const { src } = pg;
    let from = el.start;
    let to = el.end;
    const ls = src.lastIndexOf('\n', from - 1) + 1;
    let le = src.indexOf('\n', to);
    if (le < 0) le = src.length;
    if (!src.slice(ls, from).trim() && !src.slice(to, le).trim()) {
      from = ls;
      to = Math.min(src.length, le + 1);
    }
    const parent = el.parent !== null ? pg.map.els[el.parent] : null;
    pageChange([{ from, to, insert: '' }], parent && parent.hasLoc ? parent.start : null);
  };

  const duplicateSelected = () => {
    const pg = page();
    const el = currentSel();
    if (!pg || !el || ['html', 'head', 'body'].includes(el.tag)) return;
    const indent = indentOf(pg.src, el.start);
    const text = pg.src.slice(el.start, el.end);
    pageChange([{ from: el.end, to: el.end, insert: '\n' + indent + text }], el.end + 1 + indent.length);
  };

  const moveSelected = (dir: -1 | 1) => {
    const pg = page();
    const el = currentSel();
    if (!pg || !el) return;
    const sibs = (el.parent === null ? pg.map.roots : pg.map.els[el.parent].children).map((i) => pg.map.els[i]).filter((e) => e.hasLoc);
    const i = sibs.indexOf(el);
    const other = sibs[i + dir];
    if (!other) return;
    const [a, b] = dir < 0 ? [other, el] : [el, other];
    const between = pg.src.slice(a.end, b.start);
    const ta = pg.src.slice(a.start, a.end);
    const tb = pg.src.slice(b.start, b.end);
    const newStart = dir < 0 ? a.start : a.start + tb.length + between.length;
    pageChange([{ from: a.start, to: b.end, insert: tb + between + ta }], newStart);
  };

  /* ---------------------------------------------------- table editing */
  const tableCtx = () => {
    const pg = page();
    const el = currentSel();
    if (!pg || !el) return null;
    const chain = ancestors(pg.map, el.id);
    const cell = [...chain].reverse().find((e) => e.tag === 'td' || e.tag === 'th');
    const row = [...chain].reverse().find((e) => e.tag === 'tr');
    const table = [...chain].reverse().find((e) => e.tag === 'table');
    if (!cell || !row || !table) return null;
    const isCell = (e: ElInfo) => e.tag === 'td' || e.tag === 'th';
    const rows = pg.map.els.filter((e) => e.tag === 'tr' && e.hasLoc && [...ancestors(pg.map, e.id)].reverse().find((a) => a.tag === 'table') === table);
    const cellsOf = (r: ElInfo) => r.children.map((c) => pg.map.els[c]).filter(isCell);
    const col = cellsOf(row).indexOf(cell);
    return { pg, cell, row, table, rows, cellsOf, col };
  };
  const tableOp = (op: 'row-above' | 'row-below' | 'col-left' | 'col-right' | 'del-row' | 'del-col') => {
    const t = tableCtx();
    if (!t) return;
    const { pg, row, rows, cellsOf, col } = t;
    const src = pg.src;
    if (op === 'row-above' || op === 'row-below') {
      const indent = indentOf(src, row.start);
      const cells = cellsOf(row).map((c) => `<${c.tag}>&nbsp;</${c.tag}>`).join('');
      const html = `${src.slice(row.start, row.startTagEnd)}${cells}</tr>`;
      if (op === 'row-below') pageChange([{ from: row.end, to: row.end, insert: '\n' + indent + html }], row.end + 1 + indent.length);
      else pageChange([{ from: row.start, to: row.start, insert: html + '\n' + indent }], row.start);
      return;
    }
    if (op === 'del-row') {
      setSel(row.start);
      setTimeout(deleteSelected, 0);
      return;
    }
    const patches: Patch[] = [];
    for (const r of rows) {
      const cells = cellsOf(r);
      if (!cells.length) continue;
      const target = cells[Math.min(col, cells.length - 1)];
      if (op === 'del-col') {
        if (cells.length > col) patches.push({ from: cells[col].start, to: cells[col].end, insert: '' });
      } else {
        const html = `<${target.tag}>&nbsp;</${target.tag}>`;
        const at = op === 'col-right' ? target.end : target.start;
        patches.push({ from: at, to: at, insert: html });
      }
    }
    pageChange(patches, t.table.start);
  };

  const selectParent = () => {
    const el = currentSel();
    if (el && el.parent !== null && map) setSel(map.els[el.parent].start);
  };

  const undoRedo = (which: 'undo' | 'redo') => {
    const a = activeRef.current;
    if (!a) return;
    const target = lastFocus.current === 'code' && shownRef.current ? shownRef.current : a;
    if (target === shownRef.current && viewRef.current) {
      (which === 'undo' ? undo : redo)(viewRef.current);
      return;
    }
    const b = buffers.current.get(target);
    if (!b) return;
    (which === 'undo' ? undo : redo)({
      state: b.state,
      dispatch: (tr: Transaction) => {
        b.state = tr.state;
        onBufferChanged(target);
      },
    });
  };

  /** Format command: rich text while editing, wrap the code selection, or act on the selected element. */
  const format = (tag: string) => {
    const api = designApi.current;
    const cmdMap: Record<string, [string, string?]> = {
      strong: ['bold'], em: ['italic'], u: ['underline'], s: ['strikeThrough'], ul: ['insertUnorderedList'], ol: ['insertOrderedList'],
      p: ['formatBlock', 'p'], h1: ['formatBlock', 'h1'], h2: ['formatBlock', 'h2'], h3: ['formatBlock', 'h3'], h4: ['formatBlock', 'h4'],
      h5: ['formatBlock', 'h5'], h6: ['formatBlock', 'h6'], pre: ['formatBlock', 'pre'], blockquote: ['formatBlock', 'blockquote'],
    };
    if (api?.isEditing() && cmdMap[tag]) {
      api.exec(cmdMap[tag][0], cmdMap[tag][1]);
      return;
    }
    const v = viewRef.current;
    if (v && lastFocus.current === 'code' && view !== 'design' && view !== 'live' && !v.state.selection.main.empty) {
      const { from, to } = v.state.selection.main;
      const inner = v.state.sliceDoc(from, to);
      const wrapped = tag === 'ul' || tag === 'ol' ? `<${tag}>\n  <li>${inner}</li>\n</${tag}>` : `<${tag}>${inner}</${tag}>`;
      applyChanges(shownRef.current!, [{ from, to, insert: wrapped }]);
      return;
    }
    const el = currentSel();
    if (!el) {
      flash('Select some text or an element first');
      return;
    }
    if (/^(p|h[1-6]|pre|blockquote|div)$/.test(tag) && /^(p|h[1-6]|pre|blockquote|div)$/.test(el.tag)) {
      renameEl(tag);
    } else if (!el.isVoid) {
      const pg = page()!;
      const inner = pg.src.slice(el.innerStart, el.innerEnd);
      pageChange([{ from: el.innerStart, to: el.innerEnd, insert: `<${tag}>${inner.trim()}</${tag}>` }], el.start);
    }
  };

  const makeLink = () => {
    setDialog({
      kind: 'prompt',
      title: 'Make Link',
      label: 'Link to (page, file or URL)',
      value: relPages[0] ?? 'https://',
      onOk: (href) => {
        setDialog(null);
        const api = designApi.current;
        if (api?.isEditing()) {
          api.exec('createLink', href);
          return;
        }
        const v = viewRef.current;
        if (v && lastFocus.current === 'code' && !v.state.selection.main.empty) {
          const { from, to } = v.state.selection.main;
          applyChanges(shownRef.current!, [{ from, to, insert: `<a href="${href}">${v.state.sliceDoc(from, to)}</a>` }]);
          return;
        }
        const el = currentSel();
        if (el?.tag === 'a') setAttr('href', href);
        else if (el && !el.isVoid) {
          const pg = page()!;
          const inner = pg.src.slice(el.innerStart, el.innerEnd);
          pageChange([{ from: el.innerStart, to: el.innerEnd, insert: `<a href="${href}">${inner.trim()}</a>` }], el.start);
        } else if (el?.tag === 'img') {
          const pg = page()!;
          pageChange([{ from: el.start, to: el.end, insert: `<a href="${href}">${pg.src.slice(el.start, el.end)}</a>` }], el.start);
        }
      },
    });
  };

  /* ----------------------------------------------------- page title */
  const titleEl = map ? findTag(map, 'title') : null;
  const titleText = titleEl && map ? map.source.slice(titleEl.innerStart, titleEl.innerEnd) : '';
  const setTitle = (t: string) => {
    const pg = page();
    if (!pg) return;
    const te = findTag(pg.map, 'title');
    if (te && te.hasLoc) pageChange([{ from: te.innerStart, to: te.innerEnd, insert: escText(t) }]);
    else insertHtml(`<title>${escText(t)}</title>`, null, 'inside', { head: true });
  };

  /* ------------------------------------------------------- file ops */
  const newFileIn = (dir: string) => setDialog({ kind: 'new', dir });

  const createFromTemplate = async (dir: string, tpl: DocTemplate, fileName: string, title: string) => {
    const path = join(dir, fileName);
    if (await platform.exists(path)) {
      flash(fileName + ' already exists');
      return;
    }
    try {
      for (const [rel, text] of Object.entries(tpl.extra ?? {})) {
        const p = join(dir, rel);
        if (!(await platform.exists(p))) await platform.writeText(p, text);
      }
      await platform.writeText(path, tpl.body(escText(title)));
      setDialog(null);
      await refreshFiles();
      await openFile(path);
    } catch (e) {
      flash('Could not create file: ' + String(e));
    }
  };

  const newFolderIn = (dir: string) =>
    setDialog({
      kind: 'prompt',
      title: 'New Folder',
      label: 'Folder name',
      value: 'images',
      ok: 'Create',
      onOk: async (name) => {
        setDialog(null);
        await platform.mkdir(join(dir, name)).catch((e) => flash(String(e)));
        refreshFiles();
      },
    });

  const renameEntry = (e: FileEntry) =>
    setDialog({
      kind: 'prompt',
      title: 'Rename',
      label: 'New name',
      value: e.name,
      ok: 'Rename',
      onOk: async (name) => {
        setDialog(null);
        const to = join(dirname(e.path), name);
        if (to === e.path) return;
        if (await platform.exists(to)) {
          flash(name + ' already exists');
          return;
        }
        try {
          captureView();
          await platform.rename(e.path, to);
          const moved = (p: string) => (isInside(e.path, p) ? to + p.slice(e.path.length) : p);
          const nb = new Map<string, Buf>();
          for (const [k, b] of buffers.current) nb.set(moved(k), { ...b, path: moved(k) });
          buffers.current = nb;
          setTabs((t) => t.map(moved));
          setCodeTargetState((c) => Object.fromEntries(Object.entries(c).map(([k, v]) => [moved(k), moved(v)])));
          if (activeRef.current) {
            const na = moved(activeRef.current);
            activeRef.current = na;
            setActiveState(na);
          }
          refreshFiles();
          flash(`Renamed to ${name}. Links in other pages were not changed.`);
        } catch (err) {
          flash('Rename failed: ' + String(err));
        }
      },
    });

  const deleteEntry = async (e: FileEntry) => {
    if (!(await platform.ask(`Delete ${e.isDir ? 'folder' : 'file'} "${e.name}"${e.isDir ? ' and everything in it' : ''}? This can't be undone.`))) return;
    try {
      await platform.remove(e.path);
      for (const t of tabs.filter((t) => isInside(e.path, t))) {
        buffers.current.delete(t);
      }
      const remaining = tabs.filter((t) => !isInside(e.path, t));
      setTabs(remaining);
      if (activeRef.current && isInside(e.path, activeRef.current)) setActive(remaining[0] ?? null);
      refreshFiles();
    } catch (err) {
      flash('Delete failed: ' + String(err));
    }
  };

  const preview = async (path?: string) => {
    const p = path ?? activeRef.current;
    if (!p) return;
    if (platform.kind === 'tauri') {
      // Save the page and its related files first, then open the real file.
      if (p === activeRef.current) {
        for (const f of [p, ...relatedFiles]) if (buffers.current.get(f)?.dirty) await saveBuffer(f);
      }
      await platform.openExternal(p).catch((e) => flash(String(e)));
      return;
    }
    const src = textOf(p) ?? (await platform.readText(p));
    const res = await renderDocument(src, 'live', { ...renderCtx, docPath: p, siteRoot: root });
    const url = URL.createObjectURL(new Blob([res.html], { type: 'text/html' }));
    let w: Window | null = null;
    try {
      w = window.open(url, '_blank');
    } catch {
      w = null;
    }
    if (!w) {
      if (p !== activeRef.current) await openFile(p);
      setView('live');
      flash('Pop-ups are blocked here, so the page is shown in Live view');
    }
  };

  const exportZip = async () => {
    if (!root) return;
    flash('Building ZIP…');
    try {
      const files = await platform.listAll(root);
      const enc = new TextEncoder();
      const entries = [];
      for (const f of files) {
        const t = buffers.current.get(f.path) ? textOf(f.path) : null;
        entries.push({ name: relative(root, f.path), data: t !== null ? enc.encode(t) : await platform.readBytes(f.path) });
      }
      const where = await platform.saveDownload((siteCfg.name || 'site').replace(/[^\w.-]+/g, '-') + '.zip', makeZip(entries));
      if (where) flash(`Exported ${entries.length} files`);
    } catch (e) {
      flash('Export failed: ' + String(e));
    }
  };

  /* ---------------------------------------------------- find in site */
  const searchSite = async (q: string, o: { regex: boolean; caseSensitive: boolean; htmlOnly: boolean }): Promise<SearchHit[]> => {
    if (!root) return [];
    const re = new RegExp(o.regex ? q : q.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), o.caseSensitive ? 'g' : 'gi');
    const hits: SearchHit[] = [];
    const files = (await platform.listAll(root)).filter((f) => isTextFile(f.path) && (!o.htmlOnly || isHtml(f.path)) && (f.size ?? 0) < 2_000_000);
    for (const f of files) {
      const text = textOf(f.path) ?? (await platform.readText(f.path).catch(() => ''));
      re.lastIndex = 0;
      let m: RegExpExecArray | null;
      while ((m = re.exec(text)) && hits.length < 1000) {
        if (!m[0].length) {
          re.lastIndex++;
          continue;
        }
        const ls = text.lastIndexOf('\n', m.index - 1) + 1;
        let le = text.indexOf('\n', m.index);
        if (le < 0) le = text.length;
        const line = text.slice(0, m.index).split('\n').length;
        hits.push({ path: f.path, rel: relative(root, f.path), line, col: m.index - ls + 1, text: text.slice(ls, le).trim().slice(0, 200), from: m.index, to: m.index + m[0].length });
      }
    }
    return hits;
  };

  const replaceSite = async (q: string, r: string, o: { regex: boolean; caseSensitive: boolean; htmlOnly: boolean }) => {
    if (!root) return 0;
    const re = new RegExp(o.regex ? q : q.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), o.caseSensitive ? 'g' : 'gi');
    let count = 0;
    const files = (await platform.listAll(root)).filter((f) => isTextFile(f.path) && (!o.htmlOnly || isHtml(f.path)));
    for (const f of files) {
      const text = textOf(f.path) ?? (await platform.readText(f.path).catch(() => null));
      if (text === null) continue;
      const n = (text.match(re) ?? []).length;
      if (!n) continue;
      const next = text.replace(re, o.regex ? r : r.replace(/\$/g, '$$$$'));
      count += n;
      if (buffers.current.has(f.path)) await replaceAllText(f.path, next);
      else await platform.writeText(f.path, next);
    }
    refreshFiles();
    return count;
  };

  /* ---------------------------------------------------- CSS designer */
  const cssChange = async (key: string, text: string) => {
    if (key.startsWith('file:')) {
      const p = key.slice(5);
      if (!buffers.current.has(p)) await ensureBuffer(p, cssCache[p]);
      await replaceAllText(p, text);
      setRenderBump((x) => x + 1);
      return;
    }
    const start = Number(key.slice(6));
    const pg = page();
    if (!pg) return;
    const el = findByStart(pg.map, start, 'style');
    if (!el) return;
    const cur = pg.src.slice(el.innerStart, el.innerEnd);
    const d = diffPatch(cur, text);
    pageChange([{ from: el.innerStart + d.from, to: el.innerStart + d.to, insert: d.insert }]);
  };

  const cssOpen = (key: string, line: number) => {
    if (!active) return;
    if (key.startsWith('file:')) {
      const p = key.slice(5);
      ensureBuffer(p, cssCache[p]).then(() => {
        setCodeTarget(active, p);
        setTimeout(() => gotoLine(line), 50);
      });
    } else {
      const start = Number(key.slice(6));
      const el = map ? findByStart(map, start, 'style') : null;
      setCodeTarget(active, null);
      if (el) setTimeout(() => gotoLine(el.line + line - 1), 50);
    }
  };

  const attachCss = () => {
    const cssFiles = siteFiles.filter((f) => ext(f.path) === 'css').map((f) => relative(pageDir, f.path));
    setDialog({
      kind: 'prompt',
      title: 'Attach Existing CSS File',
      label: cssFiles.length ? 'Stylesheet (' + cssFiles.join(', ') + ')' : 'Stylesheet path',
      value: cssFiles[0] ?? 'css/style.css',
      ok: 'Attach',
      onOk: (v) => {
        setDialog(null);
        insertHtml(`<link rel="stylesheet" href="${v}">`, null, 'inside', { head: true });
      },
    });
  };
  const newCssFile = () =>
    setDialog({
      kind: 'prompt',
      title: 'Create New CSS File',
      label: 'File name (relative to this page)',
      value: 'css/style.css',
      ok: 'Create & attach',
      onOk: async (v) => {
        setDialog(null);
        const p = join(pageDir, v);
        if (!(await platform.exists(p))) await platform.writeText(p, '/* ' + basename(p) + ' */\n');
        insertHtml(`<link rel="stylesheet" href="${v}">`, null, 'inside', { head: true });
        refreshFiles();
      },
    });
  const addStyleBlock = () => insertHtml('<style>\n\n</style>', null, 'inside', { head: true });

  /* ---------------------------------------------------- keyboard */
  /* ------------------------------------------------ IntelliSense host */
  const siteIndex = useRef<{ classes: Set<string>; ids: Set<string>; vars: Set<string> }>({ classes: new Set(), ids: new Set(), vars: new Set() });
  useEffect(() => {
    let stop = false;
    (async () => {
      const idx = { classes: new Set<string>(), ids: new Set<string>(), vars: new Set<string>() };
      const files = siteFiles.filter((f) => isHtml(f.path) || ext(f.path) === 'css').slice(0, 400);
      for (const f of files) {
        if (stop) return;
        const t = textOf(f.path) ?? (await platform.readText(f.path).catch(() => ''));
        indexText(t, ext(f.path) === 'css', idx);
      }
      if (!stop) siteIndex.current = idx;
    })();
    return () => {
      stop = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [siteFiles, platform]);

  const intelliHost = useRef<IntelliHost>(null as unknown as IntelliHost);
  intelliHost.current = intelliHost.current ?? {
    site: (): SiteInfo => hostSite.current(),
    goTo: (t) => hostGoTo.current(t),
  };
  const hostSite = useRef<() => SiteInfo>(() => ({ docPath: '', siteRoot: null, classes: [], ids: [], cssVars: [], files: [] }));
  hostSite.current = () => {
    const idx = { classes: new Set(siteIndex.current.classes), ids: new Set(siteIndex.current.ids), vars: new Set(siteIndex.current.vars) };
    const sp = shownRef.current;
    for (const p of [sp, activeRef.current, ...relatedFiles]) {
      if (!p) continue;
      const t = textOf(p);
      if (t) indexText(t, ext(p) === 'css', idx);
    }
    const files = new Set(siteFilesRef.current.map((f) => f.path));
    for (const p of buffers.current.keys()) files.add(p);
    return {
      docPath: sp ?? '',
      siteRoot: rootRef.current,
      classes: [...idx.classes].sort(),
      ids: [...idx.ids].sort(),
      cssVars: [...idx.vars].sort(),
      files: [...files],
    };
  };
  const hostGoTo = useRef<IntelliHost['goTo']>(() => {});
  hostGoTo.current = async (t) => {
    const sp = shownRef.current;
    if (!sp) return;
    if (t.kind === 'file') {
      const p = resolveUrl(t.value, dirname(sp), rootRef.current);
      if (!p) {
        if (/^https?:/i.test(t.value)) onDesignLink(t.value);
        return;
      }
      if (await platform.exists(p)) openFile(p);
      else flash(`${t.value} doesn't exist yet`);
      return;
    }
    const re = t.kind === 'class' ? new RegExp('\\.' + t.value.replace(/[-]/g, '\\-') + '(?![\\w-])') : new RegExp('#' + t.value + '(?![\\w-])');
    // 1) <style> blocks on this page, 2) the page's stylesheets, 3) any CSS file in the site
    const pageText = active && isHtml(active) ? textOf(active) : null;
    if (pageText && map) {
      for (const e of map.els) {
        if (e.tag !== 'style' || !e.hasEndTag) continue;
        const inner = pageText.slice(e.innerStart, e.innerEnd);
        const m = inner.replace(/\/\*[\s\S]*?\*\//g, (c) => ' '.repeat(c.length)).search(re);
        if (m >= 0) {
          const at = e.innerStart + m;
          setCodeTarget(active!, null);
          setTimeout(() => gotoLine(pageText.slice(0, at).split('\n').length, at, at + t.value.length + 1), 40);
          return;
        }
      }
    }
    const cssFiles = [...relatedFiles.filter((r) => ext(r) === 'css'), ...siteFilesRef.current.map((f) => f.path).filter((p) => ext(p) === 'css' && !relatedFiles.includes(p))];
    for (const p of cssFiles) {
      const txt = textOf(p) ?? (await platform.readText(p).catch(() => ''));
      const clean = txt.replace(/\/\*[\s\S]*?\*\//g, (c) => ' '.repeat(c.length));
      const m = clean.search(re);
      if (m < 0) continue;
      const line = txt.slice(0, m).split('\n').length;
      if (active && relatedFiles.includes(p)) {
        await ensureBuffer(p, cssCache[p]);
        setCodeTarget(active, p);
        setTimeout(() => gotoLine(line, m, m + t.value.length + 1), 50);
      } else {
        await openFile(p);
        setTimeout(() => gotoLine(line, m, m + t.value.length + 1), 90);
      }
      return;
    }
    flash(`No CSS rule for ${t.kind === 'class' ? '.' : '#'}${t.value} yet`);
  };

  const formatShown = (selectionOnly: boolean) => {
    const v = viewRef.current;
    const sp = shownRef.current;
    if (!v || !sp) return;
    if (!formatDocument(v, sp, selectionOnly)) flash(`Formatting isn't available for ${langLabel(sp)} files`);
  };

  const paletteCommands = (): PaletteItem[] => {
    const out: PaletteItem[] = [];
    const walk = (items: MenuItem[], prefix: string) => {
      for (const it of items) {
        if (it.separator || it.disabled) continue;
        if (it.submenu) walk(it.submenu, prefix + it.label + ': ');
        else if (it.action && it.label) out.push({ label: prefix + it.label, detail: it.shortcut ?? '', run: it.action });
      }
    };
    for (const [name, items] of Object.entries(menus)) walk(items, name + ': ');
    return out;
  };

  /* ------------------------------------------------- W3C validation */
  const w3cKind = (p: string): 'html' | 'css' | null => (isHtml(p) ? 'html' : ext(p) === 'css' ? 'css' : null);
  const validateFile = async (path: string): Promise<FileResult> => {
    const kind = w3cKind(path)!;
    const rel = rootRef.current ? relative(rootRef.current, path) : basename(path);
    try {
      const text = textOf(path) ?? (await platform.readText(path));
      return { path, rel, result: await w3cValidate(kind, text, platform.w3c) };
    } catch (e) {
      return { path, rel, error: (e as Error).message ?? String(e) };
    }
  };
  const validateCurrent = async (quiet = false) => {
    const p = shownRef.current;
    if (!p || !w3cKind(p)) {
      if (!quiet) flash('Open an HTML page or CSS file to validate it');
      return;
    }
    if (!quiet) setBottomTab('w3c');
    setW3cBusy(`Checking ${basename(p)} with the W3C validator…`);
    setW3cStatus('loading');
    const v0 = viewRef.current;
    if (v0 && shownRef.current === p) v0.dispatch({ effects: clearW3cIssues.of(null) });
    const r = await validateFile(p);
    setW3cResults([r]);
    setW3cBusy(null);
    if (r.error) {
      setW3cStatus(null);
      flash(r.error);
      return;
    }
    const shown = r.result!.messages.filter((m) => (m.type !== 'warning' || w3cSettings.showWarnings) && (m.type !== 'info' || w3cSettings.showInfo));
    const v = viewRef.current;
    if (v && shownRef.current === p) v.dispatch({ effects: setW3cIssues.of(issuesFromMessages(v.state.doc, shown)) });
    const errs = shown.filter((m) => m.type === 'error').length;
    const warns = shown.filter((m) => m.type === 'warning').length;
    if (!shown.length) {
      setW3cStatus('valid');
      setTimeout(() => setW3cStatus((s) => (s === 'valid' ? null : s)), 2000);
      if (w3cSettings.notifications) flash(`This ${w3cKind(p)!.toUpperCase()} file is valid!`);
    } else {
      setW3cStatus(null);
      if (w3cSettings.notifications) flash(`W3C: ${errs} error${errs === 1 ? '' : 's'} and ${warns} warning${warns === 1 ? '' : 's'} in ${basename(p)}`);
    }
  };
  const clearW3c = () => {
    setW3cResults([]);
    setW3cStatus(null);
    viewRef.current?.dispatch({ effects: clearW3cIssues.of(null) });
  };
  const validateSite = async () => {
    if (!rootRef.current) return;
    setBottomTab('w3c');
    const files = siteFilesRef.current.filter((f) => w3cKind(f.path)).map((f) => f.path).slice(0, 200);
    const out: FileResult[] = [];
    for (let i = 0; i < files.length; i++) {
      setW3cBusy(`Checking ${i + 1} of ${files.length}: ${relative(rootRef.current, files[i])}…`);
      out.push(await validateFile(files[i]));
      setW3cResults([...out]);
      if (i < files.length - 1) await new Promise((r) => setTimeout(r, 1100)); // be gentle with W3C's free service
    }
    setW3cBusy(null);
  };
  const jumpToW3c = async (path: string, m: W3cMessage) => {
    if (path !== shownRef.current) {
      if (activeRef.current && relatedFiles.includes(path)) {
        await ensureBuffer(path, cssCache[path]);
        setCodeTarget(activeRef.current, path);
      } else await openFile(path);
    }
    setTimeout(() => {
      const v = viewRef.current;
      if (!v) return;
      const line = v.state.doc.line(Math.max(1, Math.min(m.line, v.state.doc.lines)));
      const from = Math.min(line.to, line.from + Math.max(0, m.col - 1));
      gotoLine(m.line, from, m.endLine === m.line && m.endCol ? Math.min(line.to, line.from + m.endCol) : from);
    }, 80);
  };

  const setViewMode = (m: ViewMode) => {
    setView(m);
    if (m !== 'code') lastFocus.current = 'design';
  };

  const handleShortcut = (e: KeyboardEvent): boolean => {
    const mod = e.ctrlKey || e.metaKey;
    const k = e.key.toLowerCase();
    if (mod && k === 's') {
      if (e.shiftKey) saveAll();
      else save();
      return true;
    }
    if (mod && k === 'n' && !e.shiftKey) {
      if (root) newFileIn(root);
      return true;
    }
    if (mod && k === 'o') {
      pickSite();
      return true;
    }
    if (mod && k === 'w') {
      if (active) closeTab(active);
      return true;
    }
    if (mod && e.shiftKey && k === 'f') {
      setBottomTab('search');
      return true;
    }
    if (mod && e.shiftKey && k === 'g') {
      if (root) setRightTab('git');
      return true;
    }
    if (mod && e.shiftKey && k === 'p') {
      setPalette('commands');
      return true;
    }
    if (mod && !e.shiftKey && !e.altKey && k === 'p') {
      if (root) setPalette('files');
      return true;
    }
    if (mod && (e.key === '=' || e.key === '+')) {
      setCodeSize((z) => Math.min(28, z + 1));
      return true;
    }
    if (mod && e.key === '-') {
      setCodeSize((z) => Math.max(9, z - 1));
      return true;
    }
    if (mod && e.key === '0' && !e.altKey) {
      setCodeSize(13);
      return true;
    }
    if (mod && e.altKey && k === 't') {
      setModePref((p) => ((p === 'system' ? (systemDark ? 'dark' : 'light') : p) === 'dark' ? 'light' : 'dark'));
      return true;
    }
    if (e.key === 'F6' && e.shiftKey) {
      validateCurrent();
      return true;
    }
    if (e.key === 'F12') {
      preview();
      return true;
    }
    if (e.altKey && !mod && ['1', '2', '3', '4'].includes(e.key)) {
      setViewMode((['code', 'split', 'design', 'live'] as ViewMode[])[+e.key - 1]);
      return true;
    }
    return false;
  };

  const designKey = (e: KeyboardEvent, editing: boolean): boolean => {
    lastFocus.current = 'design';
    if (handleShortcut(e)) return true;
    if (editing) return false;
    const mod = e.ctrlKey || e.metaKey;
    const k = e.key.toLowerCase();
    if (mod && k === 'z') {
      undoRedo(e.shiftKey ? 'redo' : 'undo');
      return true;
    }
    if (mod && k === 'y') {
      undoRedo('redo');
      return true;
    }
    if (mod && k === 'd') {
      duplicateSelected();
      return true;
    }
    if (e.key === 'Delete' || e.key === 'Backspace') {
      deleteSelected();
      return true;
    }
    if (e.key === 'Escape') {
      selectParent();
      return true;
    }
    if (e.key === 'Enter' && renderSelIdRef.current !== null) {
      setEditRequest({ id: renderSelIdRef.current, nonce: ++editNonce.current });
      return true;
    }
    if (e.altKey && (e.key === 'ArrowUp' || e.key === 'ArrowDown')) {
      moveSelected(e.key === 'ArrowUp' ? -1 : 1);
      return true;
    }
    return false;
  };
  const renderSelIdRef = useRef<number | null>(null);
  renderSelIdRef.current = renderSelId;

  const shortcutRef = useRef(handleShortcut);
  shortcutRef.current = handleShortcut;
  useEffect(() => {
    const h = (e: KeyboardEvent) => {
      if (shortcutRef.current(e)) {
        e.preventDefault();
        e.stopPropagation();
      }
    };
    window.addEventListener('keydown', h, true);
    return () => window.removeEventListener('keydown', h, true);
  }, []);

  /* ------------------------------------------------- design callbacks */
  const onDesignSelect = (id: number | null) => {
    lastFocus.current = 'design';
    setMenuOpen(null);
    if (id === null || !render) {
      setSel(null);
      return;
    }
    const el = render.map.els[id];
    if (!el) return;
    setSel(el.start);
  };

  // Mirror the selected element into the code view (unless the user is typing there).
  useEffect(() => {
    const v = viewRef.current;
    if (!v || v.hasFocus || !map || selStart === null || shownRef.current !== activeRef.current) return;
    if (v.state.doc.length !== map.source.length) return;
    const el = findByStart(map, selStart);
    if (!el || !el.hasLoc) return;
    const cur = v.state.selection.main;
    if (cur.from === el.start && cur.to === el.startTagEnd) return;
    v.dispatch({ selection: { anchor: el.start, head: el.startTagEnd }, effects: EV.scrollIntoView(el.start, { y: 'center' }) });
  }, [selStart, map]);

  const onDesignCommit = (id: number, inner: string, how: 'blur' | 'enter') => {
    const pg = page();
    if (!pg || !render || render.map.source !== pg.src) return;
    const el = render.map.els[id];
    if (!el || !el.hasLoc) return;
    const patches: Patch[] = [{ from: el.innerStart, to: el.innerEnd, insert: inner }];
    let select = el.start;
    if (how === 'enter') {
      const tag = el.tag === 'li' ? 'li' : 'p';
      const indent = indentOf(pg.src, el.start);
      const delta = inner.length - (el.innerEnd - el.innerStart);
      patches.push({ from: el.end, to: el.end, insert: `\n${indent}<${tag}></${tag}>` });
      select = el.end + delta + 1 + indent.length;
      pendingEdit.current = { start: select, selectAll: false };
    }
    pageChange(patches, select);
  };

  const onDesignDrop = (dt: DataTransfer, targetId: number | null, before: boolean) => {
    const el = targetId !== null && render ? render.map.els[targetId] : null;
    const anchor = el && !['body', 'html'].includes(el.tag) ? { start: el.start, pos: (before ? 'before' : 'after') as InsertPos } : null;
    const insId = dt.getData('application/x-weavecraft-insert');
    if (insId) {
      const item = INSERTS.find((i) => i.id === insId);
      if (item) requestInsert(item, anchor);
      return;
    }
    const path = dt.getData('application/x-weavecraft-path');
    if (path) {
      insertFileRef(path, anchor);
      return;
    }
    const text = dt.getData('text/plain');
    if (text) insertHtml(`<p>${escText(text)}</p>`, anchor ? findByStart(page()!.map, anchor.start) : null, anchor?.pos ?? 'after');
  };

  const onDesignContext = (id: number, x: number, y: number) => {
    const el = render?.map.els[id];
    if (!el) return;
    setCtx({
      x,
      y,
      items: [
        { label: 'Edit text', shortcut: 'Enter', action: () => setEditRequest({ id, nonce: ++editNonce.current }), disabled: el.isVoid },
        { label: 'Select parent', shortcut: 'Esc', action: selectParent, disabled: el.parent === null },
        { separator: true },
        { label: 'Duplicate', shortcut: 'Ctrl+D', action: duplicateSelected },
        { label: 'Move up', shortcut: 'Alt+↑', action: () => moveSelected(-1) },
        { label: 'Move down', shortcut: 'Alt+↓', action: () => moveSelected(1) },
        { label: 'Wrap in <div>', action: () => insertHtml('<div>\n</div>', currentSel(), 'wrap') },
        ...(tableCtx()
          ? [
              {
                label: 'Table',
                submenu: [
                  { label: 'Insert row above', action: () => tableOp('row-above') },
                  { label: 'Insert row below', action: () => tableOp('row-below') },
                  { label: 'Insert column left', action: () => tableOp('col-left') },
                  { label: 'Insert column right', action: () => tableOp('col-right') },
                  { separator: true },
                  { label: 'Delete row', action: () => tableOp('del-row') },
                  { label: 'Delete column', action: () => tableOp('del-col') },
                ],
              },
            ]
          : []),
        { label: 'Make link…', action: makeLink },
        { separator: true },
        { label: 'Copy HTML', action: () => navigator.clipboard?.writeText(render!.map.source.slice(el.start, el.end)) },
        { label: 'Show in code', action: () => { if (active) setCodeTarget(active, null); if (view === 'design') setView('split'); setTimeout(() => gotoLine(el.line, el.start, el.startTagEnd), 30); } },
        { label: 'Style in CSS Designer', action: () => setRightTab('css') },
        { separator: true },
        { label: 'Delete', shortcut: 'Del', action: deleteSelected, disabled: ['html', 'head', 'body'].includes(el.tag) },
      ],
    });
  };

  const onDesignLink = (href: string) => {
    if (/^https?:/i.test(href)) {
      if (platform.kind === 'tauri') platform.openExternal(href);
      else window.open(href, '_blank', 'noopener');
      return;
    }
    if (!active) return;
    const p = resolveUrl(href, dirname(active), root);
    if (p && isHtml(p)) openFile(p);
  };

  /* ---------------------------------------------------------- menus */
  const hasPage = !!active && isHtml(active);
  const menus: Record<string, MenuItem[]> = {
    File: [
      { label: 'New…', shortcut: 'Ctrl+N', action: () => root && newFileIn(root), disabled: !root },
      { label: 'Open Site Folder…', shortcut: 'Ctrl+O', action: pickSite },
      ...(platform.demoRoot ? [{ label: 'Open Demo Site', action: () => openSite(platform.demoRoot!) }] : []),
      ...(platform.kind === 'tauri' ? [{ label: 'Create Sample Site…', action: createSampleSite }] : []),
      { label: 'Recent Sites', submenu: recent.length ? recent.map((r) => ({ label: r, action: () => openSite(r) })) : [{ label: '(none)', disabled: true }] },
      { separator: true },
      { label: 'Save', shortcut: 'Ctrl+S', action: save, disabled: !active },
      { label: 'Save All', shortcut: 'Ctrl+Shift+S', action: saveAll },
      { label: 'Close Tab', shortcut: 'Ctrl+W', action: () => active && closeTab(active), disabled: !active },
      { separator: true },
      { label: 'Preview in Browser', shortcut: 'F12', action: () => preview(), disabled: !hasPage },
      { label: 'Export Site as ZIP…', action: exportZip, disabled: !root },
      ...(platform.kind !== 'tauri' && root === platform.demoRoot ? [{ separator: true }, { label: 'Reset Demo Site', action: async () => { if (await platform.ask('Reset the demo site to its original files?')) { resetDemoSite(); location.reload(); } } }] : []),
    ],
    Edit: [
      { label: 'Undo', shortcut: 'Ctrl+Z', action: () => undoRedo('undo'), disabled: !active },
      { label: 'Redo', shortcut: 'Ctrl+Shift+Z', action: () => undoRedo('redo'), disabled: !active },
      { separator: true },
      { label: 'Select Parent Element', shortcut: 'Esc', action: selectParent, disabled: !selEl },
      { label: 'Duplicate Element', shortcut: 'Ctrl+D', action: duplicateSelected, disabled: !selEl },
      { label: 'Move Element Up', shortcut: 'Alt+↑', action: () => moveSelected(-1), disabled: !selEl },
      { label: 'Move Element Down', shortcut: 'Alt+↓', action: () => moveSelected(1), disabled: !selEl },
      { label: 'Delete Element', shortcut: 'Del', action: deleteSelected, disabled: !selEl },
      { separator: true },
      { label: 'Find in Site…', shortcut: 'Ctrl+Shift+F', action: () => setBottomTab('search'), disabled: !root },
      { separator: true },
      { label: 'Format Document', shortcut: 'Shift+Alt+F', action: () => formatShown(false), disabled: !active },
      { label: 'Format Selection', shortcut: 'Ctrl+K Ctrl+F', action: () => formatShown(true), disabled: !active },
      { label: 'Go to Line…', shortcut: 'Ctrl+G', action: () => viewRef.current && import('@codemirror/search').then((m) => m.gotoLine(viewRef.current!)), disabled: !active },
      { label: 'Trigger Suggestions', shortcut: 'Ctrl+Space', action: () => viewRef.current && import('@codemirror/autocomplete').then((m) => { viewRef.current!.focus(); m.startCompletion(viewRef.current!); }), disabled: !active },
    ],
    View: [
      { label: 'Code', shortcut: 'Alt+1', checked: view === 'code', action: () => setViewMode('code') },
      { label: 'Split', shortcut: 'Alt+2', checked: view === 'split', action: () => setViewMode('split') },
      { label: 'Design', shortcut: 'Alt+3', checked: view === 'design', action: () => setViewMode('design') },
      { label: 'Live', shortcut: 'Alt+4', checked: view === 'live', action: () => setViewMode('live') },
      { separator: true },
      { label: 'Split Vertically', checked: splitDir === 'v', action: () => setSplitDir('v') },
      { label: 'Split Horizontally', checked: splitDir === 'h', action: () => setSplitDir('h') },
      { label: 'Word Wrap', checked: wrap, action: () => setWrap(!wrap) },
      { label: 'Minimap', checked: showMap, action: () => setShowMap(!showMap) },
      { label: 'Theme', submenu: [...THEMES.map((t) => ({ label: t.name, checked: theme === t.id, action: () => setTheme(t.id) })), { separator: true }, ...MODES.map((m) => ({ label: m.name, checked: modePref === m.id, action: () => setModePref(m.id) }))] },
      { label: 'Color Scheme', submenu: SCHEMES.map((c) => ({ label: c, checked: scheme === c, action: () => setScheme(c) })) },
      { label: 'Toggle Light/Dark', shortcut: 'Ctrl+Alt+T', action: () => setModePref(mode === 'dark' ? 'light' : 'dark') },
      { label: 'Zoom In (code)', shortcut: 'Ctrl+=', action: () => setCodeSize((z) => Math.min(28, z + 1)) },
      { label: 'Zoom Out (code)', shortcut: 'Ctrl+-', action: () => setCodeSize((z) => Math.max(9, z - 1)) },
      { label: 'Reset Zoom', shortcut: 'Ctrl+0', action: () => setCodeSize(13) },
      { separator: true },
      { label: 'Command Palette…', shortcut: 'Ctrl+Shift+P', action: () => setPalette('commands') },
      { label: 'Go to File…', shortcut: 'Ctrl+P', action: () => setPalette('files'), disabled: !root },
      { separator: true },
      { label: 'Window Size', submenu: DEVICE_WIDTHS.map(([n, w]) => ({ label: n, checked: width === w, action: () => setWidth(w) })) },
      { label: 'Refresh Design View', action: refreshFiles },
    ],
    Insert: [
      ...(['Text', 'HTML', 'Layout', 'Media', 'Form', 'Head'] as const).map((cat) => ({
        label: cat,
        submenu: INSERTS.filter((i) => i.cat === cat).map((i) => ({ label: i.name, action: () => requestInsert(i), disabled: !hasPage })),
      })),
      { separator: true },
      { label: 'Insert Panel', action: () => setRightTab('insert') },
    ],
    Format: [
      { label: 'Paragraph', action: () => format('p'), disabled: !hasPage },
      ...[1, 2, 3, 4, 5, 6].map((n) => ({ label: 'Heading ' + n, action: () => format('h' + n), disabled: !hasPage })),
      { label: 'Preformatted', action: () => format('pre'), disabled: !hasPage },
      { separator: true },
      { label: 'Bold', shortcut: 'Ctrl+B', action: () => format('strong'), disabled: !hasPage },
      { label: 'Italic', shortcut: 'Ctrl+I', action: () => format('em'), disabled: !hasPage },
      { label: 'Underline', shortcut: 'Ctrl+U', action: () => format('u'), disabled: !hasPage },
      { label: 'Make Link…', action: makeLink, disabled: !hasPage },
      { separator: true },
      { label: 'Unordered List', action: () => format('ul'), disabled: !hasPage },
      { label: 'Ordered List', action: () => format('ol'), disabled: !hasPage },
      { separator: true },
      { label: 'CSS Designer', action: () => setRightTab('css') },
    ],
    Site: [
      { label: 'Site Setup…', action: () => setDialog({ kind: 'site' }), disabled: !root },
      { label: 'Publish…', action: () => setDialog({ kind: 'publish' }), disabled: !root },
      { label: 'Git & GitHub', shortcut: 'Ctrl+Shift+G', action: () => setRightTab('git'), disabled: !root },
      { separator: true },
      { label: 'Validate Current File (W3C)', shortcut: 'Shift+F6', action: () => validateCurrent(), disabled: !active },
      { label: 'Validate Whole Site (W3C)', action: validateSite, disabled: !root },
      { label: 'Export Site as ZIP…', action: exportZip, disabled: !root },
      { separator: true },
      { label: 'Refresh Files', action: refreshFiles, disabled: !root },
    ],
    Help: [
      { label: 'Keyboard Shortcuts', action: () => setDialog({ kind: 'about' }) },
      { separator: true },
      { label: 'About Valco Weave', action: () => setDialog({ kind: 'about' }) },
    ],
  };

  /* ------------------------------------------------------- ribbon */
  const find = (menu: string, label: string): MenuItem => menus[menu]?.find((m) => m.label === label) ?? { label, disabled: true };
  const installUpdates = async () => {
    setUpdate(null);
    const { invoke } = await import('@tauri-apps/api/core');
    const ok = await invoke<boolean>('valco_check_updates').catch(() => false);
    if (!ok) platform.openExternal(INSTALLER_URL).catch(() => {});
  };
  const checkForUpdates = async (manual = true) => {
    if (platform.kind !== 'tauri') { if (manual) platform.openExternal(RELEASE_PAGE).catch(() => {}); return; }
    const { invoke } = await import('@tauri-apps/api/core');
    const r = await invoke<UpdateInfo>('valco_update_check').catch(() => null);
    if (!r || !r.ok) { if (manual) flash("Valco Office Update couldn't reach the update server. Check your Internet connection."); return; }
    if (r.available) {
      if (!manual && localStorage.getItem('valco.update.skip') === r.latest) return;
      setUpdate(r);
    } else if (manual) flash(`Valco Office is up to date (version ${r.installed}).`);
  };
  checkRef.current = checkForUpdates;
  const ribbonTabs: RibbonTabDef[] = [
    {
      name: 'Home',
      groups: [
        { caption: 'Document', items: [find('File', 'New…'), find('File', 'Save'), find('File', 'Save All'), find('File', 'Close Tab')] },
        { caption: 'Undo', items: [find('Edit', 'Undo'), find('Edit', 'Redo')] },
        { caption: 'Text', items: [find('Format', 'Paragraph'), find('Format', 'Bold'), find('Format', 'Italic'), find('Format', 'Underline'), find('Format', 'Make Link…'), find('Format', 'Unordered List'), find('Format', 'Ordered List')] },
        { caption: 'Views', items: [find('View', 'Code'), find('View', 'Split'), find('View', 'Design'), find('View', 'Live')] },
        { caption: 'Editing', items: [find('Edit', 'Find in Site…'), find('Edit', 'Format Document'), find('Edit', 'Go to Line…')] },
        { caption: 'Preview', items: [find('File', 'Preview in Browser'), find('Site', 'Publish…')] },
      ],
    },
    { name: 'Insert', groups: [{ caption: 'Insert', items: (menus.Insert ?? []).filter((m) => m.submenu) }, { caption: 'Panels', items: [find('Insert', 'Insert Panel')] }] },
    { name: 'Format', groups: groupsFromMenu(menus.Format ?? [], ['Paragraph', 'Font', 'Lists', 'CSS']) },
    { name: 'Edit', groups: groupsFromMenu(menus.Edit ?? [], ['Undo', 'Element', 'Find', 'Code']) },
    { name: 'View', groups: groupsFromMenu(menus.View ?? [], ['Document Views', 'Layout & Themes', 'Navigate', 'Window', 'Window']) },
    { name: 'Site', groups: groupsFromMenu(menus.Site ?? [], ['Site', 'Validate & Export', 'Files']) },
    {
      name: 'Help',
      groups: [{ caption: 'Help', items: [{ label: 'Keyboard Shortcuts', action: () => setDialog({ kind: 'about' }) }, { label: 'Check for Updates', action: () => checkForUpdates(true) }, { label: 'About Valco Weave', action: () => setDialog({ kind: 'about' }) }] }],
    },
  ];
  const qatItems: MenuItem[] = [find('File', 'Save'), find('Edit', 'Undo'), find('Edit', 'Redo'), find('File', 'Preview in Browser')];
  const appMenu: AppMenuDef = {
    items: [
      { label: 'New', icon: 'new', action: () => root && newFileIn(root), disabled: !root },
      {
        label: 'Open', icon: 'open',
        sub: [
          { label: 'Site Folder', desc: 'Open a folder of web pages as your site.', icon: 'open', action: pickSite },
          ...(platform.kind === 'tauri' ? [{ label: 'Sample Site', desc: 'Create a sample site in a folder you pick.', icon: 'new', action: createSampleSite }] : []),
          ...(platform.demoRoot ? [{ label: 'Demo Site', desc: 'Open the demo site that comes with Valco Weave.', icon: 'site', action: () => openSite(platform.demoRoot!) }] : []),
        ],
      },
      { label: 'Save', icon: 'save', action: save, disabled: !active },
      { label: 'Save All', icon: 'saveall', action: saveAll },
      { label: '-', icon: '' },
      {
        label: 'Publish', icon: 'publish',
        sub: [
          { label: 'Publish', desc: 'Upload the site to your web server over FTP.', icon: 'publish', action: () => setDialog({ kind: 'publish' }) },
          { label: 'Export as ZIP', desc: 'Save the whole site as a .zip file.', icon: 'zip', action: exportZip },
          { label: 'Preview in Browser', desc: 'Open the current page in your web browser (F12).', icon: 'live', action: () => preview() },
        ],
      },
      { label: 'Site Setup', icon: 'site', action: () => setDialog({ kind: 'site' }) },
      { label: '-', icon: '' },
      { label: 'Close Site', icon: 'close', action: closeSite },
    ],
    recentTitle: 'Recent Sites',
    recent: recent.map((r) => ({ label: basename(r) || r, title: r, action: () => openSite(r) })),
    footer: [
      { label: 'Valco Weave Options', icon: 'options', action: () => setDialog({ kind: 'site' }) },
      { label: 'Exit Valco Weave', icon: 'exit', action: exitApp },
    ],
  };

  /* ------------------------------------------------------- splitters */
  const drag = (onMove: (dx: number, dy: number) => void) => (e: React.MouseEvent) => {
    e.preventDefault();
    const sx = e.clientX;
    const sy = e.clientY;
    document.body.classList.add('dragging');
    const mv = (ev: MouseEvent) => onMove(ev.clientX - sx, ev.clientY - sy);
    const up = () => {
      document.body.classList.remove('dragging');
      window.removeEventListener('mousemove', mv);
      window.removeEventListener('mouseup', up);
    };
    window.addEventListener('mousemove', mv);
    window.addEventListener('mouseup', up);
  };
  const editorArea = useRef<HTMLDivElement>(null);

  /* --------------------------------------------------------- render */
  const isPage = hasPage;
  const showCode = !isPage || view === 'code' || view === 'split';
  const showDesign = isPage && view !== 'code';
  const shownBuf = shownPath ? buffers.current.get(shownPath) : null;
  const crumbs = selEl && map ? ancestors(map, selEl.id) : [];

  if (!root) {
    return (
      <div className="app">
        <div className="welcome">
          <Logo size={88} />
          <h1>Valco Weave</h1>
          <p className="muted">Web design, the Vista way  ·  © 2026 ItsRaiden</p>
          <p className="suite-line"><SuiteMark size={18} /> Part of Valco Office</p>
          <div className="welcome-actions">
            <button className="btn primary big" onClick={pickSite}>📂 Open Site Folder</button>
            {platform.demoRoot && <button className="btn big" onClick={() => openSite(platform.demoRoot!)}>🧁 Try the Demo Site</button>}
            {platform.kind === 'tauri' && <button className="btn big" onClick={createSampleSite} title="Pick a folder; a sample site is created inside it">🧁 Create Sample Site…</button>}
          </div>
          <div className="theme-pick">
            <span className="muted">Theme</span>
            {THEMES.map((t) => (
              <button key={t.id} className={'theme-chip t-' + t.id + (theme === t.id ? ' on' : '')} onClick={() => setTheme(t.id)} title={t.description}>
                <span className="theme-swatch" />
                {t.name}
              </button>
            ))}
          </div>
          <div className="theme-pick">
            <span className="muted">Mode</span>
            {MODES.map((m) => (
              <button key={m.id} className={'theme-chip' + (modePref === m.id ? ' on' : '')} onClick={() => setModePref(m.id)}>
                {m.id === 'light' ? '☀' : m.id === 'dark' ? '☾' : '◐'} {m.name}
              </button>
            ))}
          </div>
          {recent.length > 0 && (
            <div className="recent">
              <h4>Recent sites</h4>
              {recent.map((r) => (
                <div key={r} className="recent-row" onClick={() => openSite(r)}>
                  🗂 {basename(r) || r} <span className="muted">{r}</span>
                </div>
              ))}
            </div>
          )}
          {platform.kind !== 'tauri' && (
            <p className="muted small">
              {platform.kind === 'fsa'
                ? 'Running in the browser: folders you open are edited in place on this computer.'
                : 'Running in the browser: the demo site is stored in this browser only. Use the desktop app to edit folders on disk.'}
            </p>
          )}
        </div>
        {toast && <div className="toast">{toast}</div>}
      </div>
    );
  }

  return (
    <div className="app" onMouseDown={() => menuOpen && setMenuOpen(null)}>
      {/* Ribbon, Valco button and Quick Access Toolbar (same design as Valco Word, Valco Express and Valco Code) */}
      <Ribbon tabs={ribbonTabs} appMenu={appMenu} qat={qatItems} right={<>
        <span className="site-name" title={root}>{siteCfg.name}</span>
        <button className="btn sm accent" onClick={() => setDialog({ kind: 'publish' })} title="Publish site">⇪ Publish</button>
      </>} />

      {update && (
        <div className="update-bar">
          <img src={suiteMarkUrl} alt="" />
          <span><b>An update for Valco Office is available.</b> Valco Office {update.latest} is ready to download (you have {update.installed}).</span>
          <span className="grow" />
          <button className="btn sm accent" onClick={installUpdates}>Install Updates</button>
          <button className="btn sm" onClick={() => { localStorage.setItem('valco.update.skip', update.latest ?? ''); setUpdate(null); }}>Skip This Version</button>
          <button className="btn sm" onClick={() => setUpdate(null)}>Remind Me Later</button>
        </div>
      )}
      <div className="workspace">
        <div className="main-col">
          {/* tabs */}
          <div className="tabs">
            {tabs.map((t) => (
              <div key={t} className={'tab' + (t === active ? ' on' : '')} onClick={() => t !== active && setActive(t)} onMouseDown={(e) => e.button === 1 && (e.preventDefault(), closeTab(t))} title={t}>
                <span>{basename(t)}</span>
                {buffers.current.get(t)?.dirty && <span className="dot">●</span>}
                <button className="x" onClick={(e) => { e.stopPropagation(); closeTab(t); }}>×</button>
              </div>
            ))}
            {!tabs.length && <div className="tab-empty muted">Double-click a file in the Files panel to open it.</div>}
          </div>

          {active && (
            <>
              {/* related files + document toolbar */}
              {isPage && (
                <div className="related">
                  <span className={'rel' + (shownPath === active ? ' on' : '')} onClick={() => setCodeTarget(active, null)}>
                    Source Code{buffers.current.get(active)?.dirty ? ' ●' : ''}
                  </span>
                  {relatedFiles.map((r) => (
                    <span
                      key={r}
                      className={'rel' + (shownPath === r ? ' on' : '')}
                      onClick={async () => {
                        await ensureBuffer(r, cssCache[r]);
                        setCodeTarget(active, r);
                        if (view === 'design' || view === 'live') setView('split');
                      }}
                      title={r}
                    >
                      {relative(dirname(active), r)}
                      {buffers.current.get(r)?.dirty ? ' ●' : ''}
                    </span>
                  ))}
                </div>
              )}
              <div className="doc-toolbar">
                {isPage && (
                  <div className="seg-group">
                    {(['code', 'split', 'design', 'live'] as ViewMode[]).map((m) => (
                      <button key={m} className={'seg' + (view === m ? ' on' : '')} onClick={() => setViewMode(m)} title={`${m[0].toUpperCase() + m.slice(1)} view (Alt+${['code', 'split', 'design', 'live'].indexOf(m) + 1})`}>
                        {m[0].toUpperCase() + m.slice(1)}
                      </button>
                    ))}
                  </div>
                )}
                {isPage && view === 'split' && (
                  <button className="icon-btn" title="Toggle split direction" onClick={() => setSplitDir(splitDir === 'v' ? 'h' : 'v')}>{splitDir === 'v' ? '⫴' : '⊟'}</button>
                )}
                {isPage && (
                  <label className="title-field">
                    Title
                    <TitleInput value={titleText} onCommit={setTitle} />
                  </label>
                )}
                <span className="grow" />
                {isPage && view !== 'code' && (
                  <select className="inp sm" value={width ?? ''} onChange={(e) => setWidth(e.target.value ? +e.target.value : null)} title="Design width">
                    {DEVICE_WIDTHS.map(([n, w]) => (
                      <option key={n} value={w ?? ''}>{n}</option>
                    ))}
                    {width && !DEVICE_WIDTHS.some(([, w]) => w === width) && <option value={width}>{width}px</option>}
                  </select>
                )}
                <button className={'icon-btn' + (wrap ? ' on' : '')} title="Word wrap" onClick={() => setWrap(!wrap)}>↩</button>
                {isPage && <button className="icon-btn" title="Refresh design view" onClick={refreshFiles}>⟳</button>}
                {shownPath && w3cKind(shownPath) && <button className="btn sm" onClick={() => validateCurrent()} title="Validate with W3C (Shift+F6)">✓ W3C</button>}
                {isPage && <button className="btn sm" onClick={() => preview()} title="Preview in browser (F12)">▶ Preview</button>}
              </div>
              {isPage && view !== 'code' && mediaBar.length > 0 && (
                <div className="mq-bar" title="Media query breakpoints from your CSS — click to resize the Design view">
                  <span className={'mq' + (width === null ? ' on' : '')} onClick={() => setWidth(null)}>Fit</span>
                  {mediaBar.map((w) => (
                    <span key={w} className={'mq' + (width === w ? ' on' : '')} onClick={() => setWidth(w)}>{w}px</span>
                  ))}
                </div>
              )}
            </>
          )}

          {/* editor area */}
          <div className={'editor-area ' + (splitDir === 'v' ? 'row' : 'col')} ref={editorArea}>
            <div
              className="pane code-pane"
              style={{ display: active && showCode ? undefined : 'none', flex: showDesign && showCode ? `0 0 ${splitRatio * 100}%` : '1 1 auto' }}
              onMouseDown={() => (lastFocus.current = 'code')}
            >
              <CodeEditor state={shownBuf?.state ?? null} viewRef={viewRef} visible={showCode} />
            </div>
            {active && showCode && showDesign && (
              <div
                className="splitter"
                onMouseDown={drag((dx, dy) => {
                  const box = editorArea.current!.getBoundingClientRect();
                  const base = splitRatio;
                  const n = splitDir === 'v' ? base + dx / box.width : base + dy / box.height;
                  setSplitRatio(Math.max(0.15, Math.min(0.85, n)));
                })}
              />
            )}
            {active && showDesign && (
              <div className="pane design-pane">
                <DesignView
                  html={render?.html ?? null}
                  mode={view === 'live' ? 'live' : 'design'}
                  width={width}
                  selectedId={renderSelId}
                  editRequest={editRequest}
                  onSelect={onDesignSelect}
                  onCommit={onDesignCommit}
                  onKey={designKey}
                  onDrop={onDesignDrop}
                  onContextMenu={onDesignContext}
                  onLink={onDesignLink}
                  apiRef={designApi}
                />
                {view === 'live' && <div className="live-badge">LIVE — scripts run, links open pages</div>}
              </div>
            )}
            {!active && (
              <div className="no-doc">
                <Logo size={56} />
                <p>Open a file from the Files panel, or create one.</p>
                <button className="btn primary" onClick={() => newFileIn(root)}>New document…</button>
              </div>
            )}
          </div>

          {/* bottom dock */}
          <div className="h-splitter" onMouseDown={(e) => { const h0 = bottomH; drag((_dx, dy) => setBottomH(Math.max(80, Math.min(640, h0 - dy))))(e); }} />
          <div className="bottom" style={{ height: bottomH }}>
            <div className="dock-tabs">
              {([['props', 'Properties'], ['problems', `Problems${problems.length ? ' (' + problems.length + ')' : ''}`], ['search', 'Find in Site'], ['w3c', `W3C Validator${w3cResults.length ? ' (' + w3cResults.reduce((n, r) => n + (r.result?.messages.filter((m) => m.type === 'error').length ?? 0), 0) + ')' : ''}`]] as [BottomTab, string][]).map(([k, l]) => (
                <button key={k} className={'dock-tab' + (bottomTab === k ? ' on' : '')} onClick={() => setBottomTab(k)}>{l}</button>
              ))}
            </div>
            <div className="dock-body">
              {bottomTab === 'props' && (
                isPage ? (
                  <PropertiesPanel
                    map={map}
                    el={selEl}
                    pages={relPages}
                    images={relImages}
                    files={relFiles}
                    classes={classList}
                    onSetAttr={setAttr}
                    onRename={renameEl}
                    onSetText={setInner}
                    onSelect={(id) => map && setSel(map.els[id].start)}
                    onCss={() => setRightTab('css')}
                  />
                ) : (
                  <div className="empty-note">{active ? `${langLabel(active)} file — properties are available for HTML pages.` : 'No document open.'}</div>
                )
              )}
              {bottomTab === 'problems' && (
                <ProblemsPanel
                  problems={problems}
                  onJump={(from, to) => {
                    if (!active) return;
                    if (shownPath !== active) setCodeTarget(active, null);
                    setTimeout(() => {
                      const v = viewRef.current;
                      if (!v) return;
                      const line = v.state.doc.lineAt(Math.min(from, v.state.doc.length)).number;
                      gotoLine(line, from, to);
                    }, 30);
                  }}
                />
              )}
              {bottomTab === 'w3c' && (
                <ValidatorPanel
                  results={w3cResults}
                  busy={w3cBusy}
                  canValidate={!!shownPath && !!w3cKind(shownPath)}
                  onValidatePage={() => validateCurrent()}
                  onValidateSite={validateSite}
                  settings={w3cSettings}
                  setSettings={setW3cSettings}
                  onClear={clearW3c}
                  onJump={jumpToW3c}
                />
              )}
              {bottomTab === 'search' && (
                <SearchPanel
                  ask={platform.ask}
                  onSearch={searchSite}
                  onReplaceAll={replaceSite}
                  onJump={async (h) => {
                    await openFile(h.path);
                    setTimeout(() => gotoLine(h.line, h.from, h.to), 80);
                  }}
                />
              )}
            </div>
          </div>
        </div>

        {/* right dock */}
        <div className="v-splitter" onMouseDown={(e) => { const w0 = rightW; drag((dx) => setRightW(Math.max(220, Math.min(640, w0 - dx))))(e); }} />
        <div className="right" style={{ width: rightW }}>
          <div className="dock-tabs">
            {([['files', 'Files'], ['insert', 'Insert'], ['css', 'CSS Designer'], ['dom', 'DOM'], ['git', 'Git']] as [RightTab, string][]).map(([k, l]) => (
              <button key={k} className={'dock-tab' + (rightTab === k ? ' on' : '')} onClick={() => setRightTab(k)}>{l}</button>
            ))}
          </div>
          <div className="dock-body">
            {rightTab === 'files' && (
              <FilesPanel
                platform={platform}
                root={root}
                siteName={siteCfg.name}
                refreshKey={filesKey}
                activePath={active}
                canInsert={isPage}
                relTo={relTo}
                onOpen={(p) => openFile(p)}
                onNewFile={newFileIn}
                onNewFolder={newFolderIn}
                onRename={renameEntry}
                onDelete={deleteEntry}
                onPreview={(p) => preview(p)}
                onInsert={(p) => insertFileRef(p, null)}
                onRefresh={refreshFiles}
              />
            )}
            {rightTab === 'insert' && <InsertPanel onInsert={(i) => requestInsert(i)} pos={insertPos} setPos={setInsertPos} enabled={isPage} />}
            {rightTab === 'css' &&
              (isPage ? (
                <CssDesigner
                  sources={cssSources}
                  matches={matcher}
                  elementLabel={selEl ? label(selEl) : null}
                  suggestion={selEl ? suggestSelector(selEl) : null}
                  onChange={cssChange}
                  onAttach={attachCss}
                  onNewFile={newCssFile}
                  onAddStyleBlock={addStyleBlock}
                  onOpen={cssOpen}
                />
              ) : (
                <div className="empty-note">Open an HTML page to use the CSS Designer.</div>
              ))}
            {rightTab === 'git' && (
              <GitPanel
                platform={platform}
                root={root}
                siteName={siteCfg.name}
                refreshKey={filesKey}
                onOpen={(p) => openFile(p)}
                onSaveAll={async () => {
                  for (const [p2, b] of buffers.current) if (b.dirty) await saveBuffer(p2);
                }}
                flash={flash}
                ask={platform.ask}
              />
            )}
            {rightTab === 'dom' && <DomPanel map={map} selectedId={selEl?.id ?? null} onSelect={(id) => map && (setSel(map.els[id].start), onDesignSelect(render ? findByStart(render.map, map.els[id].start)?.id ?? null : null))} />}
          </div>
        </div>
      </div>

      {/* status bar */}
      <div className="statusbar">
        <div className="crumbs">
          {crumbs.map((c) => (
            <span key={c.id} className={'crumb' + (c.id === selEl?.id ? ' on' : '')} onClick={() => setSel(c.start)}>
              &lt;{label(c)}&gt;
            </span>
          ))}
        </div>
        <span className="grow" />
        {shownPath && w3cKind(shownPath) && (
          <button className={'sb-btn' + (w3cStatus === 'valid' ? ' ok' : '')} onClick={() => validateCurrent()} disabled={w3cStatus === 'loading'} title="Start the W3C validation of this file (Shift+F6)">
            {w3cStatus === 'loading' ? <span className="spin">⟳</span> : '✓'} {w3cStatus === 'loading' ? 'Loading' : w3cStatus === 'valid' ? 'File is valid' : 'W3C validation'}
          </button>
        )}
        {w3cResults.some((r) => r.result?.messages.length) && w3cStatus !== 'loading' && (
          <button className="sb-btn" onClick={clearW3c} title="Clear all issues found by the W3C validator">✕ Clear W3C validation</button>
        )}
        {shownPath && <span>{langLabel(shownPath)}</span>}
        <button className="sb-btn" onClick={() => setModePref(mode === 'dark' ? 'light' : 'dark')} title="Switch light/dark (Ctrl+Alt+T)">{mode === 'dark' ? '☾ Dark' : '☀ Light'}</button>
        <span>Ln {cursor.line}, Col {cursor.col}</span>
        {isPage && view !== 'code' && <span>{width ? width + 'px' : 'Fit'}</span>}
        <span className="muted">{platform.kind === 'tauri' ? 'Desktop' : root.startsWith('fsa:') ? 'Browser (local folder)' : 'Browser (demo)'}</span>
      </div>

      {/* overlays */}
      {ctx && <ContextMenu x={ctx.x} y={ctx.y} items={ctx.items} onClose={() => setCtx(null)} />}
      {toast && <div className="toast">{toast}</div>}
      {dialog?.kind === 'new' && (
        <NewDocDialog folderLabel={relative(root, dialog.dir) === '.' ? siteCfg.name : relative(root, dialog.dir)} onCreate={(t, n, ti) => createFromTemplate(dialog.dir, t, n, ti)} onClose={() => setDialog(null)} />
      )}
      {dialog?.kind === 'prompt' && <PromptDialog {...dialog} onClose={() => setDialog(null)} />}
      {dialog?.kind === 'insert' && (
        <InsertDialog
          item={dialog.item}
          images={relImages}
          pages={relPages}
          files={relFiles}
          onOk={(v) => {
            const d = dialog;
            setDialog(null);
            doInsertItem(d.item, v, d.anchor);
          }}
          onClose={() => setDialog(null)}
        />
      )}
      {dialog?.kind === 'site' && (
        <SiteDialog
          root={root}
          config={siteCfg}
          platform={platform}
          onSave={async (c) => {
            await saveSiteCfg(c);
            setDialog(null);
            flash('Site settings saved');
          }}
          onClose={() => setDialog(null)}
        />
      )}
      {dialog?.kind === 'publish' && (
        <PublishDialog
          platform={platform}
          root={root}
          config={siteCfg}
          files={siteFiles}
          published={published}
          rel={(p) => relative(root, p)}
          onPublished={async (st) => {
            setPublished(st);
            try {
              await platform.mkdir(join(root, '.weavecraft'));
              await platform.writeText(join(root, '.weavecraft/publish-state.json'), JSON.stringify(st));
            } catch {
              /* ignore */
            }
          }}
          onEditSite={() => setDialog({ kind: 'site' })}
          onClose={() => setDialog(null)}
        />
      )}
      {dialog?.kind === 'about' && <AboutDialog onClose={() => setDialog(null)} />}
      {palette && (
        <Palette
          mode={palette}
          items={palette === 'commands' ? paletteCommands() : siteFiles.map((f) => ({ label: relative(root, f.path), detail: dirname(relative(root, f.path)) === '.' ? '' : dirname(relative(root, f.path)), run: () => openFile(f.path) }))}
          onClose={() => setPalette(null)}
        />
      )}
    </div>
  );
}

function TitleInput({ value, onCommit }: { value: string; onCommit(v: string): void }) {
  const [v, setV] = useState(value);
  const focused = useRef(false);
  useEffect(() => {
    if (!focused.current) setV(value);
  }, [value]);
  return (
    <input
      className="inp sm"
      value={v}
      onFocus={() => (focused.current = true)}
      onBlur={() => {
        focused.current = false;
        if (v !== value) onCommit(v);
      }}
      onChange={(e) => setV(e.target.value)}
      onKeyDown={(e) => {
        if (e.key === 'Enter') (e.target as HTMLInputElement).blur();
      }}
    />
  );
}
