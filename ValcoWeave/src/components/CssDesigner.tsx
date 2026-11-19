import { useMemo, useState } from 'react';
import { addRule, CSS_PROPS, deleteRule, looksLikeColor, parseRules, renameRule, setDecl, toHex, type RuleInfo } from '../lib/css';
import { CommitInput } from './ui';

export interface CssSource {
  key: string;
  label: string;
  kind: 'file' | 'style';
  path?: string;
  text: string;
}

interface Props {
  sources: CssSource[];
  /** Does the selected element match this selector? null = no element selected. */
  matches: ((selector: string) => boolean) | null;
  elementLabel: string | null;
  suggestion: string | null;
  onChange(key: string, text: string): void;
  onAttach(): void;
  onNewFile(): void;
  onAddStyleBlock(): void;
  onOpen(key: string, line: number): void;
}

const QUICK = ['display', 'width', 'height', 'margin', 'padding', 'color', 'background-color', 'font-family', 'font-size', 'font-weight', 'text-align', 'line-height', 'border', 'border-radius', 'box-shadow', 'position'];

interface Row extends RuleInfo {
  key: string;
  srcLabel: string;
}

export default function CssDesigner(p: Props) {
  const [srcKey, setSrcKey] = useState<string>('all');
  const [media, setMedia] = useState<string>('*');
  const [current, setCurrent] = useState(true);
  const [sel, setSel] = useState<{ key: string; idx: number } | null>(null);
  const [showSet, setShowSet] = useState(false);
  const [filter, setFilter] = useState('');
  const [newSel, setNewSel] = useState<string | null>(null);
  const [newProp, setNewProp] = useState({ prop: '', value: '' });

  const parsed = useMemo(
    () =>
      p.sources.map((s) => {
        const r = parseRules(s.text);
        return { src: s, ...r };
      }),
    [p.sources],
  );

  const scoped = parsed.filter((x) => srcKey === 'all' || x.src.key === srcKey);
  const medias = [...new Set(scoped.flatMap((x) => x.rules.map((r) => r.media).filter(Boolean) as string[]))];

  const rows: Row[] = scoped.flatMap((x) =>
    x.rules.map((r) => ({ ...r, key: x.src.key, srcLabel: x.src.label })),
  );
  const visible = rows.filter((r) => {
    if (media === '' && r.media) return false;
    if (media !== '*' && media !== '' && r.media !== media) return false;
    if (filter && !r.selector.toLowerCase().includes(filter.toLowerCase())) return false;
    if (current && p.matches) return r.selectors.some((s) => p.matches!(s));
    return true;
  });
  const selected = sel ? rows.find((r) => r.key === sel.key && r.idx === sel.idx) ?? null : null;
  const textOf = (key: string) => p.sources.find((s) => s.key === key)?.text ?? '';
  const targetKey = srcKey !== 'all' ? srcKey : selected?.key ?? p.sources[0]?.key ?? null;

  const update = (key: string, fn: (t: string) => string) => p.onChange(key, fn(textOf(key)));

  const createRule = (selector: string) => {
    if (!targetKey || !selector.trim()) return;
    const before = parseRules(textOf(targetKey)).rules;
    const next = addRule(textOf(targetKey), selector.trim(), media !== '*' && media !== '' ? media : null);
    p.onChange(targetKey, next);
    const after = parseRules(next).rules;
    const added = after.find((r) => !before.some((b) => b.idx === r.idx && b.selector === r.selector)) ?? after[after.length - 1];
    if (added) setSel({ key: targetKey, idx: added.idx });
    setNewSel(null);
  };

  const declRows = selected
    ? (() => {
        const set = selected.decls;
        if (showSet) return set.map((d) => ({ ...d, isSet: true }));
        const props = [...set.map((d) => d.prop), ...QUICK.filter((q) => !set.some((d) => d.prop === q))];
        return props.map((prop) => {
          const d = set.find((x) => x.prop === prop);
          return { prop, value: d?.value ?? '', isSet: !!d };
        });
      })()
    : [];

  return (
    <div className="cssd">
      <section>
        <div className="sec-head">
          Sources
          <span className="grow" />
          <button className="icon-btn" title="Attach existing CSS file" onClick={p.onAttach}>⛓</button>
          <button className="icon-btn" title="Create new CSS file" onClick={p.onNewFile}>＋</button>
          <button className="icon-btn" title="Add a <style> block to this page" onClick={p.onAddStyleBlock}>{'{}'}</button>
        </div>
        <div className="list">
          <div className={'li' + (srcKey === 'all' ? ' on' : '')} onClick={() => setSrcKey('all')}>
            All sources
          </div>
          {parsed.map((x) => (
            <div key={x.src.key} className={'li' + (srcKey === x.src.key ? ' on' : '')} onClick={() => setSrcKey(x.src.key)} onDoubleClick={() => p.onOpen(x.src.key, 1)} title="Double-click to open">
              {x.src.kind === 'file' ? '📄 ' : '‹/› '}
              {x.src.label}
              {x.error && <span className="err-dot" title={x.error}>●</span>}
            </div>
          ))}
          {!p.sources.length && <div className="empty-note">No CSS yet. Add a file or a &lt;style&gt; block.</div>}
        </div>
      </section>

      <section>
        <div className="sec-head">@Media</div>
        <div className="list short">
          <div className={'li' + (media === '*' ? ' on' : '')} onClick={() => setMedia('*')}>Everything</div>
          <div className={'li' + (media === '' ? ' on' : '')} onClick={() => setMedia('')}>Global</div>
          {medias.map((m) => (
            <div key={m} className={'li' + (media === m ? ' on' : '')} onClick={() => setMedia(m)}>
              {m.replace(/^@media\s*/, '')}
            </div>
          ))}
        </div>
      </section>

      <section>
        <div className="sec-head">
          Selectors
          <span className="grow" />
          <label className="mini-toggle" title="Only rules that apply to the selected element">
            <input type="checkbox" checked={current} onChange={(e) => setCurrent(e.target.checked)} /> Current
          </label>
          <button className="icon-btn" title="Add selector" disabled={!targetKey} onClick={() => setNewSel(p.suggestion ?? '.new-class')}>＋</button>
          <button
            className="icon-btn"
            title="Delete selected rule"
            disabled={!selected}
            onClick={() => {
              if (!selected) return;
              update(selected.key, (t) => deleteRule(t, selected.idx));
              setSel(null);
            }}
          >
            −
          </button>
        </div>
        <input className="inp search" placeholder="Filter selectors" value={filter} onChange={(e) => setFilter(e.target.value)} />
        {newSel !== null && (
          <div className="new-sel">
            <CommitInput value={newSel} autoFocus onCommit={(v) => createRule(v)} />
            <button className="btn sm" onClick={() => createRule(newSel)}>Add</button>
          </div>
        )}
        <div className="list tall">
          {visible.map((r) => (
            <div
              key={r.key + ':' + r.idx}
              className={'li sel-row' + (selected === r || (sel?.key === r.key && sel.idx === r.idx) ? ' on' : '')}
              onClick={() => setSel({ key: r.key, idx: r.idx })}
              onDoubleClick={() => p.onOpen(r.key, r.line)}
            >
              <span className="selector">{r.selector}</span>
              {r.media && <span className="badge">{r.media.replace(/^@media\s*/, '')}</span>}
              {srcKey === 'all' && <span className="src-l">{r.srcLabel}</span>}
            </div>
          ))}
          {!visible.length && (
            <div className="empty-note">
              {current && p.matches ? `No rules target ${p.elementLabel ?? 'this element'} yet — press ＋ to add one.` : 'No selectors.'}
            </div>
          )}
        </div>
      </section>

      <section className="grow-sec">
        <div className="sec-head">
          Properties
          <span className="grow" />
          <label className="mini-toggle">
            <input type="checkbox" checked={showSet} onChange={(e) => setShowSet(e.target.checked)} /> Show set
          </label>
        </div>
        {!selected && <div className="empty-note">Pick a selector to edit its properties.</div>}
        {selected && (
          <div className="decls">
            <div className="rule-name">
              <CommitInput value={selected.selector} onCommit={(v) => v.trim() && update(selected.key, (t) => renameRule(t, selected.idx, v.trim()))} />
            </div>
            {declRows.map((d) => (
              <div key={d.prop} className={'decl' + (d.isSet ? ' set' : '')}>
                <span className="prop" title={d.prop}>{d.prop}</span>
                {looksLikeColor(d.prop, d.value || 'x') && (
                  <input
                    type="color"
                    className="swatch"
                    value={d.value ? toHex(d.value) : '#000000'}
                    onChange={(e) => update(selected.key, (t) => setDecl(t, selected.idx, d.prop, e.target.value))}
                  />
                )}
                <CommitInput
                  value={d.value}
                  placeholder="—"
                  onCommit={(v) => update(selected.key, (t) => setDecl(t, selected.idx, d.prop, v.trim() ? v.trim() : null))}
                />
                {d.isSet && (
                  <button className="x" title="Remove" onClick={() => update(selected.key, (t) => setDecl(t, selected.idx, d.prop, null))}>×</button>
                )}
              </div>
            ))}
            <form
              className="decl add"
              onSubmit={(e) => {
                e.preventDefault();
                if (!newProp.prop.trim() || !newProp.value.trim()) return;
                update(selected.key, (t) => setDecl(t, selected.idx, newProp.prop.trim(), newProp.value.trim()));
                setNewProp({ prop: '', value: '' });
              }}
            >
              <input className="inp" list="wc-css-props" placeholder="property" value={newProp.prop} onChange={(e) => setNewProp({ ...newProp, prop: e.target.value })} />
              <input className="inp" placeholder="value" value={newProp.value} onChange={(e) => setNewProp({ ...newProp, value: e.target.value })} />
              <button className="btn sm" type="submit">＋</button>
              <datalist id="wc-css-props">
                {CSS_PROPS.map((c) => (
                  <option key={c} value={c} />
                ))}
              </datalist>
            </form>
          </div>
        )}
      </section>
    </div>
  );
}
