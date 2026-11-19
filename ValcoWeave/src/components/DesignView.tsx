import { useEffect, useRef, useState, type MutableRefObject } from 'react';
import { cleanEditorAttrs } from '../lib/render';

export interface DesignApi {
  /** Run a rich-text command while editing text. Returns false when not editing. */
  exec(cmd: string, value?: string): boolean;
  isEditing(): boolean;
  stopEditing(): void;
  focus(): void;
}

interface Props {
  html: string | null;
  mode: 'design' | 'live';
  width: number | null;
  selectedId: number | null;
  editRequest: { id: number; nonce: number; selectAll?: boolean } | null;
  onSelect(id: number | null): void;
  onCommit(id: number, inner: string, how: 'blur' | 'enter'): void;
  onKey(e: KeyboardEvent, editing: boolean): boolean;
  onDrop(dt: DataTransfer, targetId: number | null, before: boolean): void;
  onContextMenu(id: number, x: number, y: number): void;
  onLink(href: string): void;
  apiRef: MutableRefObject<DesignApi | null>;
}

const EDITOR_CSS = `
[data-wc-hover]{outline:1px dashed rgba(54,194,180,.95)!important;outline-offset:-1px}
[data-wc-sel]{outline:2px solid #36c2b4!important;outline-offset:-2px}
[contenteditable="true"]{outline:2px solid #f5a623!important;outline-offset:-2px;cursor:text}
[data-wc-drop="before"]{box-shadow:inset 0 3px 0 #f5a623!important}
[data-wc-drop="after"]{box-shadow:inset 0 -3px 0 #f5a623!important}
html{cursor:default}
`;

const ENTER_SPLITS = new Set(['p', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6', 'li']);
const VOID = new Set(['img', 'br', 'hr', 'input', 'meta', 'link', 'source', 'area', 'col', 'embed', 'wbr', 'track']);

function cleanInner(el: HTMLElement): string {
  const c = el.cloneNode(true) as HTMLElement;
  cleanEditorAttrs(c);
  const s = c.innerHTML
    .replace(/<(\/?)b>/g, '<$1strong>')
    .replace(/<(\/?)i>/g, '<$1em>');
  return s === '<br>' ? '' : s;
}

export default function DesignView(p: Props) {
  const frames = [useRef<HTMLIFrameElement>(null), useRef<HTMLIFrameElement>(null)];
  const [active, setActive] = useState(0);
  const activeRef = useRef(0);
  const props = useRef(p);
  props.current = p;
  const editing = useRef<{ el: HTMLElement; id: number; snapshot: string } | null>(null);
  const pendingSwap = useRef(0);
  const loading = useRef(false);
  const handledEdit = useRef(-1);

  const doc = () => frames[activeRef.current].current?.contentDocument ?? null;

  const applySelection = (d: Document | null) => {
    if (!d || props.current.mode !== 'design') return;
    d.querySelectorAll('[data-wc-sel]').forEach((e) => e.removeAttribute('data-wc-sel'));
    const id = props.current.selectedId;
    if (id === null) return;
    const el = d.querySelector(`[data-wc-id="${id}"]`) as HTMLElement | null;
    if (el && el.tagName !== 'HTML' && el.tagName !== 'BODY') {
      el.setAttribute('data-wc-sel', '');
      const r = el.getBoundingClientRect();
      const vh = d.defaultView?.innerHeight ?? 0;
      if (r.bottom < 0 || r.top > vh) el.scrollIntoView({ block: 'center' });
    }
  };

  const commit = (how: 'blur' | 'enter') => {
    const ed = editing.current;
    if (!ed) return;
    editing.current = null;
    ed.el.removeAttribute('contenteditable');
    ed.el.removeAttribute('spellcheck');
    const inner = cleanInner(ed.el);
    if (inner !== ed.snapshot || how === 'enter') props.current.onCommit(ed.id, inner, how);
  };

  const startEdit = (el: HTMLElement, caretEnd: boolean, selectAll = false) => {
    if (VOID.has(el.tagName.toLowerCase()) || el.tagName === 'HTML' || el.tagName === 'HEAD') return;
    if (editing.current?.el === el) return;
    if (editing.current) commit('blur');
    const id = Number(el.getAttribute('data-wc-id'));
    editing.current = { el, id, snapshot: cleanInner(el) };
    el.setAttribute('contenteditable', 'true');
    el.setAttribute('spellcheck', 'true');
    el.focus();
    if (caretEnd || selectAll) {
      const d = el.ownerDocument;
      const sel = d.getSelection();
      const range = d.createRange();
      range.selectNodeContents(el);
      if (!selectAll) range.collapse(false);
      sel?.removeAllRanges();
      sel?.addRange(range);
    }
    el.addEventListener('blur', () => commit('blur'), { once: true });
  };

  const setup = (frame: HTMLIFrameElement) => {
    const d = frame.contentDocument;
    if (!d) return;
    const mode = props.current.mode;
    const idOf = (t: EventTarget | null): HTMLElement | null => {
      const n = t as Node | null;
      const el = (n && n.nodeType === 1 ? (n as Element) : n?.parentElement)?.closest?.('[data-wc-id]');
      return (el as HTMLElement) ?? null;
    };

    if (mode === 'live') {
      d.addEventListener('click', (e) => {
        const a = (e.target as Element)?.closest?.('a[href]');
        if (a) {
          e.preventDefault();
          props.current.onLink(a.getAttribute('href') ?? '');
        }
      });
      d.addEventListener('submit', (e) => e.preventDefault());
      d.addEventListener('keydown', (e) => {
        if (props.current.onKey(e, false)) e.preventDefault();
      });
      return;
    }

    const style = d.createElement('style');
    style.setAttribute('data-wc-editor', '');
    // selection colour follows the app theme's accent
    const accent = getComputedStyle(document.documentElement).getPropertyValue('--accent').trim() || '#36c2b4';
    style.textContent = EDITOR_CSS.replace(/#36c2b4/g, accent).replace(/rgba\(54,194,180,\.95\)/g, accent);
    (d.head ?? d.documentElement).appendChild(style);

    let hover: HTMLElement | null = null;
    d.addEventListener('mouseover', (e) => {
      const el = idOf(e.target);
      if (el === hover) return;
      hover?.removeAttribute('data-wc-hover');
      hover = el && el.tagName !== 'BODY' && el.tagName !== 'HTML' ? el : null;
      hover?.setAttribute('data-wc-hover', '');
    });
    d.addEventListener('mouseleave', () => {
      hover?.removeAttribute('data-wc-hover');
      hover = null;
    });
    d.addEventListener('click', (e) => {
      const el = idOf(e.target);
      if (editing.current && el && editing.current.el.contains(e.target as Node)) return;
      e.preventDefault();
      props.current.onSelect(el ? Number(el.getAttribute('data-wc-id')) : null);
    });
    d.addEventListener('dblclick', (e) => {
      const el = idOf(e.target);
      if (el && !editing.current) startEdit(el, false);
    });
    d.addEventListener('submit', (e) => e.preventDefault());
    d.addEventListener('keydown', (e) => {
      const ed = editing.current;
      if (ed) {
        if (e.key === 'Escape') {
          e.preventDefault();
          ed.el.blur();
          return;
        }
        if (e.key === 'Enter' && !e.shiftKey && ENTER_SPLITS.has(ed.el.tagName.toLowerCase())) {
          e.preventDefault();
          commit('enter');
          return;
        }
        if ((e.ctrlKey || e.metaKey) && ['b', 'i', 'u', 'z', 'y', 'a', 'c', 'v', 'x'].includes(e.key.toLowerCase())) return;
        if (props.current.onKey(e, true)) e.preventDefault();
        return;
      }
      if (props.current.onKey(e, false)) e.preventDefault();
    });
    d.addEventListener('contextmenu', (e) => {
      if (editing.current) return;
      const el = idOf(e.target);
      if (!el) return;
      e.preventDefault();
      const id = Number(el.getAttribute('data-wc-id'));
      props.current.onSelect(id);
      const r = frame.getBoundingClientRect();
      props.current.onContextMenu(id, r.left + e.clientX, r.top + e.clientY);
    });
    let dropEl: HTMLElement | null = null;
    const clearDrop = () => {
      dropEl?.removeAttribute('data-wc-drop');
      dropEl = null;
    };
    const dropPos = (e: DragEvent) => {
      const el = idOf(e.target);
      if (!el) return { el: null, before: false };
      const r = el.getBoundingClientRect();
      return { el, before: e.clientY < r.top + r.height / 2 };
    };
    d.addEventListener('dragover', (e) => {
      e.preventDefault();
      const { el, before } = dropPos(e);
      if (el !== dropEl) clearDrop();
      if (el && el.tagName !== 'BODY' && el.tagName !== 'HTML') {
        dropEl = el;
        el.setAttribute('data-wc-drop', before ? 'before' : 'after');
      }
    });
    d.addEventListener('dragleave', (e) => {
      if (!e.relatedTarget) clearDrop();
    });
    d.addEventListener('drop', (e) => {
      e.preventDefault();
      const { el, before } = dropPos(e);
      clearDrop();
      if (!e.dataTransfer) return;
      const id = el ? Number(el.getAttribute('data-wc-id')) : null;
      props.current.onDrop(e.dataTransfer, id, before);
    });
  };

  // Load new HTML into the hidden frame, then swap: no white flash between renders.
  useEffect(() => {
    if (p.html === null) return;
    if (editing.current) return; // don't yank the page out from under an in-progress edit
    const target = 1 - activeRef.current;
    const f = frames[target].current;
    if (!f) return;
    const token = ++pendingSwap.current;
    loading.current = true;
    f.onload = () => {
      if (token !== pendingSwap.current) return;
      loading.current = false;
      const old = frames[activeRef.current].current?.contentWindow;
      const sx = old?.scrollX ?? 0;
      const sy = old?.scrollY ?? 0;
      setup(f);
      f.contentWindow?.scrollTo(sx, sy);
      activeRef.current = target;
      setActive(target);
      applySelection(f.contentDocument);
      const req = props.current.editRequest;
      if (req && req.nonce !== handledEdit.current) {
        handledEdit.current = req.nonce;
        const el = f.contentDocument?.querySelector(`[data-wc-id="${req.id}"]`) as HTMLElement | null;
        if (el) setTimeout(() => startEdit(el, true, req.selectAll), 0);
      }
    };
    f.srcdoc = p.html;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [p.html, p.mode]);

  useEffect(() => {
    const req = p.editRequest;
    if (!req || loading.current || req.nonce === handledEdit.current) return;
    handledEdit.current = req.nonce;
    const el = doc()?.querySelector(`[data-wc-id="${req.id}"]`) as HTMLElement | null;
    if (el) startEdit(el, true, req.selectAll);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [p.editRequest?.nonce]);

  useEffect(() => {
    applySelection(doc());
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [p.selectedId]);

  p.apiRef.current = {
    exec(cmd, value) {
      const d = doc();
      if (!editing.current || !d) return false;
      editing.current.el.focus();
      d.execCommand(cmd, false, value);
      return true;
    },
    isEditing: () => !!editing.current,
    stopEditing() {
      editing.current?.el.blur();
    },
    focus() {
      frames[activeRef.current].current?.contentWindow?.focus();
    },
  };

  const w = p.width;
  return (
    <div className="design-wrap">
      <div className="design-stage" style={w ? { width: w + 'px' } : undefined}>
        {[0, 1].map((i) => (
          <iframe
            key={i}
            ref={frames[i]}
            title={i === active ? 'Design view' : 'Design buffer'}
            className={'design-frame' + (i === active ? '' : ' hidden-frame')}
          />
        ))}
      </div>
    </div>
  );
}
