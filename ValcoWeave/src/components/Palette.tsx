import { useEffect, useMemo, useRef, useState } from 'react';

export interface PaletteItem {
  label: string;
  detail?: string;
  run(): void;
}

/** VS Code-style fuzzy match: every typed character in order. Returns a score or -1. */
function fuzzy(q: string, s: string): { score: number; hits: number[] } | null {
  if (!q) return { score: 0, hits: [] };
  const lq = q.toLowerCase();
  const ls = s.toLowerCase();
  const hits: number[] = [];
  let score = 0;
  let last = -1;
  for (const ch of lq) {
    if (ch === ' ') continue;
    const i = ls.indexOf(ch, last + 1);
    if (i < 0) return null;
    score += i === last + 1 ? 5 : 1; // consecutive letters
    if (i === 0 || /[\s:/._-]/.test(s[i - 1])) score += 4; // word starts
    hits.push(i);
    last = i;
  }
  return { score: score - s.length * 0.01, hits };
}

export default function Palette(p: { mode: 'commands' | 'files'; items: PaletteItem[]; onClose(): void }) {
  const [q, setQ] = useState(p.mode === 'commands' ? '' : '');
  const [sel, setSel] = useState(0);
  const list = useRef<HTMLDivElement>(null);
  const matches = useMemo(() => {
    const out = p.items
      .map((it) => ({ it, m: fuzzy(q, p.mode === 'files' ? it.label : it.label) }))
      .filter((x) => x.m)
      .sort((a, b) => b.m!.score - a.m!.score)
      .slice(0, 200);
    return out;
  }, [q, p.items, p.mode]);

  useEffect(() => setSel(0), [q]);
  useEffect(() => {
    list.current?.querySelector('.pal-row.on')?.scrollIntoView({ block: 'nearest' });
  }, [sel]);

  const run = (i: number) => {
    const m = matches[i];
    if (!m) return;
    p.onClose();
    setTimeout(() => m.it.run(), 0);
  };

  const highlight = (s: string, hits: number[]) => {
    if (!hits.length) return s;
    const set = new Set(hits);
    return s.split('').map((c, i) => (set.has(i) ? <b key={i}>{c}</b> : c));
  };

  return (
    <div className="pal-back" onMouseDown={(e) => e.target === e.currentTarget && p.onClose()}>
      <div className="pal">
        <input
          className="inp pal-input"
          autoFocus
          placeholder={p.mode === 'commands' ? 'Type a command…' : 'Search files by name…'}
          value={q}
          onChange={(e) => setQ(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Escape') p.onClose();
            else if (e.key === 'ArrowDown') {
              e.preventDefault();
              setSel((s) => Math.min(matches.length - 1, s + 1));
            } else if (e.key === 'ArrowUp') {
              e.preventDefault();
              setSel((s) => Math.max(0, s - 1));
            } else if (e.key === 'Enter') run(sel);
          }}
        />
        <div className="pal-list" ref={list}>
          {matches.map(({ it, m }, i) => (
            <div key={it.label + i} className={'pal-row' + (i === sel ? ' on' : '')} onMouseEnter={() => setSel(i)} onMouseDown={(e) => { e.preventDefault(); run(i); }}>
              <span className="pal-label">{highlight(it.label, m!.hits)}</span>
              {it.detail && <span className="pal-detail">{it.detail}</span>}
            </div>
          ))}
          {!matches.length && <div className="empty-note">No matches</div>}
        </div>
      </div>
    </div>
  );
}
