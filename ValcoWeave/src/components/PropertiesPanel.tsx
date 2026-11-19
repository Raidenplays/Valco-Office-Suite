import { useState } from 'react';
import { getAttr, isBooleanAttr, label, type ElInfo, type HtmlMap } from '../lib/htmlmap';
import { CommitInput, Field } from './ui';

interface Props {
  map: HtmlMap | null;
  el: ElInfo | null;
  pages: string[];
  images: string[];
  files: string[];
  classes: string[];
  onSetAttr(name: string, value: string | null): void;
  onRename(tag: string): void;
  onSetText(text: string): void;
  onSelect(id: number): void;
  onCss(): void;
}

const BLOCK_FORMATS = ['p', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6', 'div', 'pre', 'blockquote', 'section', 'article', 'aside', 'header', 'footer', 'main', 'nav', 'span', 'strong', 'em'];

type Spec = [attr: string, label: string, kind?: 'page' | 'image' | 'file' | 'bool' | string[]];

const SPECIFIC: Record<string, Spec[]> = {
  a: [['href', 'Link', 'page'], ['target', 'Target', ['', '_blank', '_self', '_parent', '_top']], ['title', 'Title'], ['rel', 'Rel']],
  img: [['src', 'Src', 'image'], ['alt', 'Alt'], ['width', 'W'], ['height', 'H'], ['loading', 'Loading', ['', 'lazy', 'eager']], ['title', 'Title']],
  input: [['type', 'Type', ['text', 'email', 'password', 'number', 'tel', 'url', 'search', 'date', 'checkbox', 'radio', 'file', 'range', 'color', 'hidden', 'submit']], ['name', 'Name'], ['value', 'Value'], ['placeholder', 'Placeholder'], ['required', 'Required', 'bool'], ['disabled', 'Disabled', 'bool']],
  textarea: [['name', 'Name'], ['rows', 'Rows'], ['cols', 'Cols'], ['placeholder', 'Placeholder']],
  button: [['type', 'Type', ['button', 'submit', 'reset']], ['name', 'Name'], ['disabled', 'Disabled', 'bool']],
  form: [['action', 'Action', 'page'], ['method', 'Method', ['get', 'post']], ['enctype', 'Enctype']],
  label: [['for', 'For']],
  select: [['name', 'Name'], ['multiple', 'Multiple', 'bool']],
  option: [['value', 'Value'], ['selected', 'Selected', 'bool']],
  video: [['src', 'Src', 'file'], ['poster', 'Poster', 'image'], ['width', 'W'], ['height', 'H'], ['controls', 'Controls', 'bool'], ['autoplay', 'Autoplay', 'bool'], ['loop', 'Loop', 'bool'], ['muted', 'Muted', 'bool']],
  audio: [['src', 'Src', 'file'], ['controls', 'Controls', 'bool'], ['autoplay', 'Autoplay', 'bool'], ['loop', 'Loop', 'bool']],
  iframe: [['src', 'Src'], ['width', 'W'], ['height', 'H'], ['title', 'Title'], ['loading', 'Loading', ['', 'lazy', 'eager']]],
  link: [['rel', 'Rel'], ['href', 'Href', 'file'], ['media', 'Media']],
  script: [['src', 'Src', 'file'], ['type', 'Type'], ['defer', 'Defer', 'bool'], ['async', 'Async', 'bool']],
  meta: [['name', 'Name'], ['content', 'Content'], ['charset', 'Charset'], ['property', 'Property']],
  td: [['colspan', 'Colspan'], ['rowspan', 'Rowspan']],
  th: [['colspan', 'Colspan'], ['rowspan', 'Rowspan'], ['scope', 'Scope', ['', 'col', 'row']]],
  html: [['lang', 'Lang'], ['dir', 'Dir', ['', 'ltr', 'rtl']]],
  ol: [['start', 'Start'], ['type', 'Type', ['', '1', 'a', 'A', 'i', 'I']], ['reversed', 'Reversed', 'bool']],
  source: [['src', 'Src', 'file'], ['type', 'Type'], ['media', 'Media']],
};

export default function PropertiesPanel(p: Props) {
  const [newAttr, setNewAttr] = useState('');
  const el = p.el;
  if (!el) return <div className="empty-note">Select an element in Design view, the DOM panel or by placing the cursor in code.</div>;

  const textOnly = p.map && el.children.length === 0 && !el.isVoid && !['script', 'style', 'html', 'head', 'body'].includes(el.tag);
  const innerText = textOnly && p.map ? p.map.source.slice(el.innerStart, el.innerEnd) : '';
  const listFor = (k?: Spec[2]) => (k === 'page' ? p.pages : k === 'image' ? p.images : k === 'file' ? p.files : undefined);
  const specific = SPECIFIC[el.tag] ?? [];
  const shown = new Set(['id', 'class', 'style', ...specific.map((s) => s[0])]);

  const renderSpec = ([attr, lbl, kind]: Spec) => {
    const v = getAttr(el, attr);
    if (kind === 'bool') {
      return (
        <label key={attr} className="field chk-field">
          <input type="checkbox" checked={v !== null} onChange={(e) => p.onSetAttr(attr, e.target.checked ? '' : null)} />
          <span>{lbl}</span>
        </label>
      );
    }
    if (Array.isArray(kind)) {
      return (
        <Field key={attr} label={lbl}>
          <select className="inp" value={v ?? ''} onChange={(e) => p.onSetAttr(attr, e.target.value === '' ? null : e.target.value)}>
            {kind.map((o) => (
              <option key={o} value={o}>
                {o || '(none)'}
              </option>
            ))}
            {v && !kind.includes(v) && <option value={v}>{v}</option>}
          </select>
        </Field>
      );
    }
    return (
      <Field key={attr} label={lbl} wide={kind === 'page' || kind === 'image' || kind === 'file' || attr === 'alt' || attr === 'content'}>
        <CommitInput value={v ?? ''} list={listFor(kind)} onCommit={(nv) => p.onSetAttr(attr, nv === '' ? null : nv)} />
      </Field>
    );
  };

  return (
    <div className="props">
      <div className="props-head">
        <span className="tagchip">&lt;{el.tag}&gt;</span>
        <span className="muted">{label(el)}</span>
        {el.parent !== null && p.map && (
          <button className="link-btn" onClick={() => p.onSelect(el.parent!)} title="Select parent element">
            ↑ {p.map.els[el.parent].tag}
          </button>
        )}
        <span className="grow" />
        <button className="btn sm" onClick={p.onCss} title="Open CSS Designer for this element">
          CSS Designer
        </button>
      </div>
      <div className="props-grid">
        {BLOCK_FORMATS.includes(el.tag) && (
          <Field label="Format">
            <select className="inp" value={el.tag} onChange={(e) => p.onRename(e.target.value)}>
              {BLOCK_FORMATS.map((t) => (
                <option key={t} value={t}>
                  {t === 'p' ? 'Paragraph' : /^h\d$/.test(t) ? 'Heading ' + t[1] : t}
                </option>
              ))}
            </select>
          </Field>
        )}
        <Field label="ID">
          <CommitInput value={getAttr(el, 'id') ?? ''} onCommit={(v) => p.onSetAttr('id', v.trim() || null)} />
        </Field>
        <Field label="Class">
          <CommitInput value={getAttr(el, 'class') ?? ''} list={p.classes} onCommit={(v) => p.onSetAttr('class', v.trim() || null)} />
        </Field>
        {specific.map(renderSpec)}
        <Field label="Style" wide>
          <CommitInput value={getAttr(el, 'style') ?? ''} placeholder="inline CSS, e.g. color: red;" onCommit={(v) => p.onSetAttr('style', v.trim() || null)} />
        </Field>
        {textOnly && (
          <Field label="Text" wide>
            <CommitInput value={innerText} multiline onCommit={(v) => p.onSetText(v)} />
          </Field>
        )}
      </div>
      <details className="all-attrs" open={el.attrs.some((a) => !shown.has(a.name))}>
        <summary>All attributes ({el.attrs.length})</summary>
        <table>
          <tbody>
            {el.attrs.map((a) => (
              <tr key={a.name}>
                <td className="an">{a.name}</td>
                <td>
                  {isBooleanAttr(a.name) && a.value === '' ? (
                    <span className="muted">(boolean)</span>
                  ) : (
                    <CommitInput value={a.value} onCommit={(v) => p.onSetAttr(a.name, v)} />
                  )}
                </td>
                <td>
                  <button className="x" title="Remove attribute" onClick={() => p.onSetAttr(a.name, null)}>
                    ×
                  </button>
                </td>
              </tr>
            ))}
            <tr>
              <td colSpan={3}>
                <form
                  className="add-attr"
                  onSubmit={(e) => {
                    e.preventDefault();
                    const m = newAttr.match(/^\s*([A-Za-z_:][-A-Za-z0-9_:.]*)\s*(?:=\s*"?([^"]*)"?)?\s*$/);
                    if (m) {
                      p.onSetAttr(m[1], m[2] ?? '');
                      setNewAttr('');
                    }
                  }}
                >
                  <input className="inp" placeholder='add attribute, e.g. data-role="menu"' value={newAttr} onChange={(e) => setNewAttr(e.target.value)} />
                  <button className="btn sm" type="submit">
                    Add
                  </button>
                </form>
              </td>
            </tr>
          </tbody>
        </table>
      </details>
    </div>
  );
}
