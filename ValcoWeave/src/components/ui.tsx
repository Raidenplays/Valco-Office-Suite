import { useEffect, useId, useRef, useState } from 'react';

/** Text input that commits on Enter / blur, and follows outside changes while not focused. */
export function CommitInput(props: {
  value: string;
  onCommit(v: string): void;
  placeholder?: string;
  list?: string[];
  className?: string;
  multiline?: boolean;
  autoFocus?: boolean;
  type?: string;
}) {
  const [v, setV] = useState(props.value);
  const focused = useRef(false);
  const listId = useId();
  useEffect(() => {
    if (!focused.current) setV(props.value);
  }, [props.value]);
  const commit = () => {
    if (v !== props.value) props.onCommit(v);
  };
  const common = {
    value: v,
    placeholder: props.placeholder,
    className: 'inp ' + (props.className ?? ''),
    autoFocus: props.autoFocus,
    onFocus: () => (focused.current = true),
    onBlur: () => {
      focused.current = false;
      commit();
    },
  };
  if (props.multiline) {
    return (
      <textarea
        {...common}
        rows={3}
        onChange={(e) => setV(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) commit();
          if (e.key === 'Escape') setV(props.value);
        }}
      />
    );
  }
  return (
    <>
      <input
        {...common}
        type={props.type ?? 'text'}
        list={props.list ? listId : undefined}
        spellCheck={false}
        onChange={(e) => setV(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === 'Enter') commit();
          if (e.key === 'Escape') {
            setV(props.value);
            (e.target as HTMLInputElement).blur();
          }
        }}
      />
      {props.list && (
        <datalist id={listId}>
          {props.list.slice(0, 400).map((o) => (
            <option key={o} value={o} />
          ))}
        </datalist>
      )}
    </>
  );
}

export function Field(props: { label: string; children: React.ReactNode; wide?: boolean }) {
  return (
    <label className={'field' + (props.wide ? ' wide' : '')}>
      <span>{props.label}</span>
      {props.children}
    </label>
  );
}

export function Modal(props: { title: string; onClose(): void; children: React.ReactNode; footer?: React.ReactNode; width?: number }) {
  useEffect(() => {
    const k = (e: KeyboardEvent) => {
      if (e.key === 'Escape') props.onClose();
    };
    window.addEventListener('keydown', k);
    return () => window.removeEventListener('keydown', k);
  }, [props]);
  return (
    <div className="modal-back" onMouseDown={(e) => e.target === e.currentTarget && props.onClose()}>
      <div className="modal" style={props.width ? { width: props.width } : undefined}>
        <div className="modal-title">
          {props.title}
          <button className="x" onClick={props.onClose} aria-label="Close">
            ×
          </button>
        </div>
        <div className="modal-body">{props.children}</div>
        {props.footer && <div className="modal-foot">{props.footer}</div>}
      </div>
    </div>
  );
}

export interface MenuItem {
  label?: string;
  shortcut?: string;
  action?: () => void;
  disabled?: boolean;
  checked?: boolean;
  separator?: boolean;
  submenu?: MenuItem[];
  /** Image shown in the menu's icon gutter. */
  icon?: string;
  /** Bold, non-clickable heading row. */
  heading?: boolean;
}

export function ContextMenu(props: { x: number; y: number; items: MenuItem[]; onClose(): void }) {
  const ref = useRef<HTMLDivElement>(null);
  const [pos, setPos] = useState({ x: props.x, y: props.y });
  useEffect(() => {
    const r = ref.current?.getBoundingClientRect();
    if (r) {
      setPos({
        x: Math.min(props.x, window.innerWidth - r.width - 4),
        y: Math.min(props.y, window.innerHeight - r.height - 4),
      });
    }
    const close = () => props.onClose();
    window.addEventListener('mousedown', close);
    window.addEventListener('blur', close);
    return () => {
      window.removeEventListener('mousedown', close);
      window.removeEventListener('blur', close);
    };
  }, [props]);
  return (
    <div className="menu-pop ctx" ref={ref} style={{ left: pos.x, top: pos.y }} onMouseDown={(e) => e.stopPropagation()}>
      <MenuList items={props.items} onDone={props.onClose} />
    </div>
  );
}

export function MenuList({ items, onDone }: { items: MenuItem[]; onDone(): void }) {
  return (
    <>
      {items.map((it, i) =>
        it.separator ? (
          <div key={i} className="menu-sep" />
        ) : it.submenu ? (
          <div key={i} className="menu-item has-sub">
            <span className="chk" />
            <span className="lbl">{it.label}</span>
            <span className="sc">▸</span>
            <div className="menu-pop sub">
              <MenuList items={it.submenu} onDone={onDone} />
            </div>
          </div>
        ) : (
          <div
            key={i}
            className={'menu-item' + (it.disabled ? ' disabled' : '') + (it.heading ? ' heading' : '')}
            onMouseDown={(e) => e.preventDefault()}
            onClick={() => {
              if (it.disabled) return;
              onDone();
              it.action?.();
            }}
          >
            <span className="chk">{it.icon ? <img src={it.icon} width={16} height={16} alt="" /> : it.checked ? '✓' : ''}</span>
            <span className="lbl" style={it.heading ? { fontWeight: 600 } : undefined}>{it.label}</span>
            <span className="sc">{it.shortcut ?? ''}</span>
          </div>
        ),
      )}
    </>
  );
}
