import { useState } from 'react';
import { INSERTS, INSERT_CATS, type InsertItem } from '../lib/inserts';
import { label, type HtmlMap } from '../lib/htmlmap';
import type { Problem } from '../lib/lint';

/* ------------------------------------------------------------ Insert panel */

export type InsertPos = 'after' | 'before' | 'inside' | 'wrap';

export function InsertPanel(p: { onInsert(item: InsertItem): void; pos: InsertPos; setPos(p: InsertPos): void; enabled: boolean }) {
  const [cat, setCat] = useState<InsertItem['cat']>('HTML');
  const [q, setQ] = useState('');
  const items = INSERTS.filter((i) => (q ? i.name.toLowerCase().includes(q.toLowerCase()) : i.cat === cat));
  return (
    <div className="insert">
      <div className="ins-top">
        <select className="inp" value={cat} onChange={(e) => setCat(e.target.value as InsertItem['cat'])}>
          {INSERT_CATS.map((c) => (
            <option key={c}>{c}</option>
          ))}
        </select>
        <input className="inp" placeholder="Search" value={q} onChange={(e) => setQ(e.target.value)} />
      </div>
      <div className="ins-pos" title="Where new elements go, relative to the selected element">
        {(['before', 'after', 'inside', 'wrap'] as InsertPos[]).map((x) => (
          <button key={x} className={'seg' + (p.pos === x ? ' on' : '')} onClick={() => p.setPos(x)}>
            {x === 'inside' ? 'Nest' : x[0].toUpperCase() + x.slice(1)}
          </button>
        ))}
      </div>
      {!p.enabled && <div className="empty-note">Open an HTML page to insert elements.</div>}
      <div className="ins-list">
        {items.map((it) => (
          <div
            key={it.id}
            className={'ins-item' + (p.enabled ? '' : ' disabled')}
            draggable={p.enabled}
            onDragStart={(e) => e.dataTransfer.setData('application/x-weavecraft-insert', it.id)}
            onClick={() => p.enabled && p.onInsert(it)}
            title={`Insert ${it.name} (or drag into Design view)`}
          >
            <span className="ins-icon">{it.icon}</span>
            {it.name}
          </div>
        ))}
      </div>
    </div>
  );
}

/* --------------------------------------------------------------- DOM panel */

export function DomPanel(p: { map: HtmlMap | null; selectedId: number | null; onSelect(id: number): void }) {
  const [closed, setClosed] = useState<Set<string>>(new Set());
  if (!p.map) return <div className="empty-note">Open an HTML page to see its structure.</div>;
  const map = p.map;
  const keyOf = (id: number) => {
    const parts: string[] = [];
    let c: number | null = id;
    while (c !== null) {
      const e: HtmlMap['els'][number] = map.els[c];
      const sib = e.parent === null ? map.roots : map.els[e.parent].children;
      parts.unshift(e.tag + sib.indexOf(c));
      c = e.parent;
    }
    return parts.join('/');
  };
  const rows: React.ReactNode[] = [];
  const walk = (ids: number[]) => {
    for (const id of ids) {
      const e = map.els[id];
      const k = keyOf(id);
      const isClosed = closed.has(k);
      rows.push(
        <div
          key={id}
          className={'tree-row dom' + (id === p.selectedId ? ' sel' : '')}
          style={{ paddingLeft: 6 + e.depth * 12 }}
          onClick={() => p.onSelect(id)}
        >
          <span
            className="twisty"
            onClick={(ev) => {
              ev.stopPropagation();
              setClosed((s) => {
                const n = new Set(s);
                if (n.has(k)) n.delete(k);
                else n.add(k);
                return n;
              });
            }}
          >
            {e.children.length ? (isClosed ? '▸' : '▾') : ''}
          </span>
          <span className="dom-tag">{label(e)}</span>
        </div>,
      );
      if (!isClosed) walk(e.children);
    }
  };
  walk(map.roots);
  return <div className="tree">{rows}</div>;
}

/* ---------------------------------------------------------- Problems panel */

export function ProblemsPanel(p: { problems: Problem[]; onJump(from: number, to: number): void }) {
  if (!p.problems.length) return <div className="empty-note ok">✓ No problems found in this page.</div>;
  return (
    <div className="problems">
      {p.problems.map((x, i) => (
        <div key={i} className={'prob ' + x.severity} onClick={() => p.onJump(x.from, x.to)}>
          <span className="sev">{x.severity === 'error' ? '⛔' : x.severity === 'warning' ? '⚠' : 'ℹ'}</span>
          <span className="ln">Line {x.line}</span>
          <span>{x.message}</span>
        </div>
      ))}
    </div>
  );
}

/* ------------------------------------------------------- Find in site panel */

export interface SearchHit {
  path: string;
  rel: string;
  line: number;
  col: number;
  text: string;
  from: number;
  to: number;
}

export function SearchPanel(p: {
  ask(message: string): Promise<boolean>;
  onSearch(q: string, opts: { regex: boolean; caseSensitive: boolean; htmlOnly: boolean }): Promise<SearchHit[]>;
  onReplaceAll(q: string, repl: string, opts: { regex: boolean; caseSensitive: boolean; htmlOnly: boolean }): Promise<number>;
  onJump(hit: SearchHit): void;
}) {
  const [q, setQ] = useState('');
  const [r, setR] = useState('');
  const [regex, setRegex] = useState(false);
  const [cs, setCs] = useState(false);
  const [htmlOnly, setHtmlOnly] = useState(false);
  const [hits, setHits] = useState<SearchHit[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState('');
  const opts = { regex, caseSensitive: cs, htmlOnly };
  const run = async () => {
    if (!q) return;
    setBusy(true);
    setMsg('');
    try {
      setHits(await p.onSearch(q, opts));
    } catch (e) {
      setMsg((e as Error).message);
    }
    setBusy(false);
  };
  return (
    <div className="search">
      <form
        className="search-bar"
        onSubmit={(e) => {
          e.preventDefault();
          run();
        }}
      >
        <input className="inp" placeholder="Find in site" value={q} onChange={(e) => setQ(e.target.value)} />
        <input className="inp" placeholder="Replace with" value={r} onChange={(e) => setR(e.target.value)} />
        <label className="mini-toggle"><input type="checkbox" checked={cs} onChange={(e) => setCs(e.target.checked)} />Aa</label>
        <label className="mini-toggle"><input type="checkbox" checked={regex} onChange={(e) => setRegex(e.target.checked)} />.*</label>
        <label className="mini-toggle"><input type="checkbox" checked={htmlOnly} onChange={(e) => setHtmlOnly(e.target.checked)} />HTML only</label>
        <button className="btn sm" type="submit" disabled={busy}>Find all</button>
        <button
          className="btn sm"
          type="button"
          disabled={busy || !q}
          onClick={async () => {
            if (!(await p.ask(`Replace every match of "${q}" across the site? Open files are changed in their tabs; other files are saved straight to disk.`))) return;
            setBusy(true);
            try {
              const n = await p.onReplaceAll(q, r, opts);
              setMsg(`Replaced ${n} match${n === 1 ? '' : 'es'}.`);
              setHits(await p.onSearch(q, opts));
            } catch (e) {
              setMsg((e as Error).message);
            }
            setBusy(false);
          }}
        >
          Replace all
        </button>
      </form>
      {msg && <div className="empty-note">{msg}</div>}
      <div className="hits">
        {hits && !hits.length && <div className="empty-note">No matches.</div>}
        {hits?.map((h, i) => (
          <div key={i} className="hit" onClick={() => p.onJump(h)}>
            <span className="hit-file">{h.rel}:{h.line}</span>
            <span className="hit-text">{h.text}</span>
          </div>
        ))}
        {hits && hits.length >= 1000 && <div className="empty-note">Showing the first 1000 matches.</div>}
      </div>
    </div>
  );
}
