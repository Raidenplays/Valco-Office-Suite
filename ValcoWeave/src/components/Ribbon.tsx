// The Office 2007 Ribbon used across Valco Office (Valco Word, Valco Express, Valco Code are WPF; this is the
// same design in HTML for Valco Weave): round Valco button with the suite mark, Quick Access Toolbar, tabs,
// groups with captions, large and small buttons, Office-gold hover, Blue / Silver / Black schemes.
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { MenuList, type MenuItem } from './ui';
import { suiteMarkUrl } from './Dialogs';
import { RibbonIcon, iconFor } from './RibbonIcons';

export interface RibbonGroup { caption: string; items: MenuItem[] }
export interface RibbonTabDef { name: string; groups: RibbonGroup[] }

/** Turns a classic menu into ribbon groups: separators start new groups. */
export function groupsFromMenu(items: MenuItem[], captions: string[]): RibbonGroup[] {
  const groups: RibbonGroup[] = [];
  let cur: MenuItem[] = [];
  const flush = () => {
    if (cur.length) groups.push({ caption: captions[groups.length] ?? '', items: cur });
    cur = [];
  };
  for (const it of items) {
    if (it.separator) flush();
    else if (!it.heading) cur.push(it);
  }
  flush();
  return groups;
}

const LARGE = new Set([
  'New…', 'Open Site Folder…', 'Save', 'Preview in Browser', 'Undo', 'Find in Site…', 'Format Document',
  'Code', 'Split', 'Design', 'Live', 'Command Palette…', 'Site Setup…', 'Publish…', 'Validate Current File (W3C)',
  'CSS Designer', 'Insert Panel', 'Paragraph', 'Bold', 'Unordered List', 'About Valco Weave', 'Keyboard Shortcuts',
  'Text', 'HTML', 'Layout', 'Media', 'Form', 'Head', 'Theme', 'Color Scheme', 'Check for Updates',
]);

function clean(label: string) {
  return label.replace(/…$/, '').replace(/ \(W3C\)$/, '');
}

function Drop({ item, large, onDone }: { item: MenuItem; large: boolean; onDone(): void }) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const close = (e: MouseEvent) => { if (!ref.current?.contains(e.target as Node)) setOpen(false); };
    window.addEventListener('mousedown', close);
    return () => window.removeEventListener('mousedown', close);
  }, [open]);
  return (
    <div className="rb-drop" ref={ref}>
      <button className={'rb-btn ' + (large ? 'lg' : 'sm') + (open ? ' pressed' : '')} title={item.label} onMouseDown={(e) => { e.preventDefault(); setOpen(!open); }}>
        <RibbonIcon name={iconFor(item.label ?? '')} size={large ? 32 : 16} />
        <span className="rb-lbl">{clean(item.label ?? '')}<span className="rb-arrow">▾</span></span>
      </button>
      {open && (
        <div className="menu-pop rb-pop" onMouseDown={(e) => e.stopPropagation()}>
          <MenuList items={item.submenu ?? []} onDone={() => { setOpen(false); onDone(); }} />
        </div>
      )}
    </div>
  );
}

function Btn({ item, large }: { item: MenuItem; large: boolean }) {
  const tip = (item.label ?? '') + (item.shortcut ? ` (${item.shortcut})` : '');
  return (
    <button
      className={'rb-btn ' + (large ? 'lg' : 'sm') + (item.checked ? ' on' : '')}
      disabled={item.disabled}
      title={tip}
      onMouseDown={(e) => e.preventDefault()}
      onClick={() => item.action?.()}
    >
      <RibbonIcon name={iconFor(item.label ?? '')} size={large ? 32 : 16} />
      <span className="rb-lbl">{clean(item.label ?? '')}</span>
    </button>
  );
}

function Group({ g, onDone }: { g: RibbonGroup; onDone(): void }) {
  // large buttons first; the rest stacked three to a column, like Office
  const cells: ReactNode[] = [];
  let column: ReactNode[] = [];
  const flush = () => { if (column.length) cells.push(<div className="rb-col" key={'c' + cells.length}>{column}</div>); column = []; };
  g.items.forEach((it, i) => {
    const large = LARGE.has(it.label ?? '') || g.items.length === 1;
    const el = it.submenu ? <Drop key={i} item={it} large={large} onDone={onDone} /> : <Btn key={i} item={it} large={large} />;
    if (large) { flush(); cells.push(el); }
    else { column.push(el); if (column.length === 3) flush(); }
  });
  flush();
  return (
    <div className="rb-group">
      <div className="rb-group-body">{cells}</div>
      <div className="rb-caption">{g.caption}</div>
    </div>
  );
}

export interface AppMenuDef {
  items: { label: string; icon: string; action?: () => void; sub?: { label: string; desc: string; icon: string; action: () => void }[]; disabled?: boolean }[];
  recentTitle: string;
  recent: { label: string; title?: string; action: () => void }[];
  footer: { label: string; icon: string; action: () => void }[];
}

function AppMenu({ def, onClose }: { def: AppMenuDef; onClose(): void }) {
  const [sub, setSub] = useState<number | null>(null);
  const run = (f?: () => void) => { onClose(); f?.(); };
  const right = sub != null && def.items[sub]?.sub;
  return (
    <div className="rb-appmenu" onMouseDown={(e) => e.stopPropagation()}>
      <div className="rb-appmenu-body">
        <div className="rb-appmenu-left">
          {def.items.map((it, i) =>
            it.label === '-' ? <div key={i} className="rb-appmenu-sep" /> : (
              <button key={i} className="rb-appmenu-item" disabled={it.disabled} onMouseEnter={() => setSub(it.sub ? i : null)} onClick={() => (it.action ? run(it.action) : setSub(i))}>
                <RibbonIcon name={it.icon} size={32} />
                <span>{it.label}</span>
                {it.sub && <span className="rb-appmenu-arrow">▸</span>}
              </button>
            ),
          )}
        </div>
        <div className="rb-appmenu-right">
          {right ? (
            <>
              <div className="rb-appmenu-head">{def.items[sub!].label}</div>
              {right.map((s, i) => (
                <button key={i} className="rb-appmenu-sub" onClick={() => run(s.action)}>
                  <RibbonIcon name={s.icon} size={32} />
                  <span><b>{s.label}</b><br />{s.desc}</span>
                </button>
              ))}
            </>
          ) : (
            <>
              <div className="rb-appmenu-head">{def.recentTitle}</div>
              {def.recent.length === 0 && <div className="rb-appmenu-none">Nothing here yet.</div>}
              {def.recent.slice(0, 17).map((r, i) => (
                <button key={i} className="rb-appmenu-recent" title={r.title} onClick={() => run(r.action)}>
                  {i + 1}&nbsp;&nbsp;{r.label}
                </button>
              ))}
            </>
          )}
        </div>
      </div>
      <div className="rb-appmenu-foot">
        {def.footer.map((f, i) => (
          <button key={i} className="rb-appmenu-footbtn" onClick={() => run(f.action)}>
            <RibbonIcon name={f.icon} size={16} /> {f.label}
          </button>
        ))}
      </div>
    </div>
  );
}

export function Ribbon(p: {
  tabs: RibbonTabDef[];
  appMenu: AppMenuDef;
  qat: MenuItem[];
  right?: ReactNode;
}) {
  const [tab, setTab] = useState(() => localStorage.getItem('valco.ribbon.tab') ?? p.tabs[0]?.name);
  const [min, setMin] = useState(() => localStorage.getItem('valco.ribbon.min') === '1');
  const [peek, setPeek] = useState(false);
  const [orb, setOrb] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => { localStorage.setItem('valco.ribbon.tab', tab ?? ''); }, [tab]);
  useEffect(() => { localStorage.setItem('valco.ribbon.min', min ? '1' : '0'); }, [min]);
  useEffect(() => {
    if (!orb && !peek) return;
    const close = (e: MouseEvent) => { if (!ref.current?.contains(e.target as Node)) { setOrb(false); setPeek(false); } };
    window.addEventListener('mousedown', close);
    return () => window.removeEventListener('mousedown', close);
  }, [orb, peek]);
  const cur = p.tabs.find((t) => t.name === tab) ?? p.tabs[0];
  const showBody = !min || peek;
  return (
    <div className={'ribbon' + (min ? ' minimized' : '')} ref={ref} onMouseDown={(e) => e.stopPropagation()}>
      <div className="rb-strip">
        <button className={'rb-orb' + (orb ? ' open' : '')} title="Valco Button — open, save and publish, and everything else you can do with your site" onMouseDown={(e) => { e.preventDefault(); setOrb(!orb); }}>
          <img src={suiteMarkUrl} alt="" draggable={false} />
        </button>
        <div className="rb-qat">
          {p.qat.map((q, i) => (
            <button key={i} className="rb-qat-btn" disabled={q.disabled} title={(q.label ?? '') + (q.shortcut ? ` (${q.shortcut})` : '')} onMouseDown={(e) => e.preventDefault()} onClick={() => q.action?.()}>
              <RibbonIcon name={iconFor(q.label ?? '')} size={16} />
            </button>
          ))}
          <button className="rb-qat-btn more" title={min ? 'Restore the Ribbon' : 'Minimize the Ribbon'} onClick={() => { setMin(!min); setPeek(false); }}>{min ? '▾' : '▴'}</button>
        </div>
        <div className="rb-tabs">
          {p.tabs.map((t) => (
            <button
              key={t.name}
              className={'rb-tab' + (t.name === cur?.name && showBody ? ' on' : '')}
              onMouseDown={(e) => { e.preventDefault(); if (min) setPeek(t.name !== cur?.name || !peek); setTab(t.name); }}
              onDoubleClick={() => { setMin(!min); setPeek(false); }}
            >
              {t.name}
            </button>
          ))}
        </div>
        <span className="grow" />
        {p.right}
      </div>
      {showBody && cur && (
        <div className={'rb-body' + (peek ? ' peek' : '')}>
          {cur.groups.map((g, i) => <Group key={cur.name + i} g={g} onDone={() => setPeek(false)} />)}
        </div>
      )}
      {orb && <AppMenu def={p.appMenu} onClose={() => setOrb(false)} />}
    </div>
  );
}
