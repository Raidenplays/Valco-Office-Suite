import logoUrl from '../assets/valco-weave.png';
import suiteMarkUrl from '../assets/valco-office-mark.png';
import suiteBadgeUrl from '../assets/valco-office-badge.png';
export { suiteMarkUrl };
export const SUITE_APPS = 'Valco Word · Valco Express · Valco Code · Valco Weave';
import { useState } from 'react';
import { Field, Modal } from './ui';
import { TEMPLATES, type DocTemplate } from '../lib/templates';
import type { InsertItem } from '../lib/inserts';
import type { FileEntry, FtpConfig, Platform, PublishProgress } from '../lib/platform';

export interface SiteConfig {
  name: string;
  ftp?: FtpConfig;
}

/* ---------------------------------------------------------------- Prompt */

export function PromptDialog(p: { title: string; label: string; value: string; ok?: string; onOk(v: string): void; onClose(): void }) {
  const [v, setV] = useState(p.value);
  return (
    <Modal
      title={p.title}
      onClose={p.onClose}
      width={420}
      footer={
        <>
          <button className="btn" onClick={p.onClose}>Cancel</button>
          <button className="btn primary" onClick={() => v.trim() && p.onOk(v.trim())}>{p.ok ?? 'OK'}</button>
        </>
      }
    >
      <Field label={p.label} wide>
        <input
          className="inp"
          autoFocus
          value={v}
          onChange={(e) => setV(e.target.value)}
          onFocus={(e) => {
            const dot = e.target.value.lastIndexOf('.');
            e.target.setSelectionRange(0, dot > 0 ? dot : e.target.value.length);
          }}
          onKeyDown={(e) => e.key === 'Enter' && v.trim() && p.onOk(v.trim())}
        />
      </Field>
    </Modal>
  );
}

/* ------------------------------------------------------------ New document */

export function NewDocDialog(p: { folderLabel: string; onCreate(t: DocTemplate, fileName: string, title: string): void; onClose(): void }) {
  const [tpl, setTpl] = useState<DocTemplate>(TEMPLATES[0]);
  const [title, setTitle] = useState('Untitled Page');
  const [name, setName] = useState('untitled.html');
  const pick = (t: DocTemplate) => {
    setTpl(t);
    setName((n) => n.replace(/\.[^.]*$/, '') + '.' + t.ext);
  };
  const create = () => {
    let n = name.trim();
    if (!n) return;
    if (!/\.[a-z0-9]+$/i.test(n)) n += '.' + tpl.ext;
    p.onCreate(tpl, n, title.trim() || 'Untitled Page');
  };
  return (
    <Modal
      title="New Document"
      onClose={p.onClose}
      width={640}
      footer={
        <>
          <span className="muted grow">Saved in {p.folderLabel}</span>
          <button className="btn" onClick={p.onClose}>Cancel</button>
          <button className="btn primary" onClick={create}>Create</button>
        </>
      }
    >
      <div className="tpl-grid">
        {TEMPLATES.map((t) => (
          <div key={t.id} className={'tpl' + (t === tpl ? ' on' : '')} onClick={() => pick(t)} onDoubleClick={create}>
            <div className="tpl-icon">{t.ext === 'html' ? '🌐' : t.ext === 'css' ? '🎨' : t.ext === 'js' ? '📜' : '📝'}</div>
            <div className="tpl-name">{t.name}</div>
            <div className="tpl-desc">{t.description}</div>
          </div>
        ))}
      </div>
      <div className="row2">
        <Field label="File name" wide>
          <input className="inp" value={name} onChange={(e) => setName(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && create()} />
        </Field>
        {tpl.ext === 'html' && (
          <Field label="Page title" wide>
            <input className="inp" value={title} onChange={(e) => setTitle(e.target.value)} />
          </Field>
        )}
      </div>
    </Modal>
  );
}

/* --------------------------------------------------------- Insert fields */

export function InsertDialog(p: { item: InsertItem; images: string[]; pages: string[]; files: string[]; onOk(v: Record<string, string>): void; onClose(): void }) {
  const [vals, setVals] = useState<Record<string, string>>(() => Object.fromEntries((p.item.fields ?? []).map((f) => [f.key, f.def ?? (f.kind === 'image' ? p.images[0] ?? '' : '')])));
  const ok = () => p.onOk(vals);
  return (
    <Modal
      title={'Insert ' + p.item.name}
      onClose={p.onClose}
      width={460}
      footer={
        <>
          <button className="btn" onClick={p.onClose}>Cancel</button>
          <button className="btn primary" onClick={ok}>Insert</button>
        </>
      }
    >
      {(p.item.fields ?? []).map((f, i) => {
        const list = f.kind === 'image' ? p.images : f.kind === 'page' ? p.pages : f.kind === 'file' ? p.files : null;
        return (
          <Field key={f.key} label={f.label} wide>
            <input
              className="inp"
              autoFocus={i === 0}
              type={f.kind === 'number' ? 'number' : 'text'}
              list={list ? 'ins-' + f.key : undefined}
              value={vals[f.key] ?? ''}
              onChange={(e) => setVals({ ...vals, [f.key]: e.target.value })}
              onKeyDown={(e) => e.key === 'Enter' && ok()}
            />
            {list && (
              <datalist id={'ins-' + f.key}>
                {list.map((o) => (
                  <option key={o} value={o} />
                ))}
              </datalist>
            )}
          </Field>
        );
      })}
      {(p.item.fields ?? []).some((f) => f.kind === 'image') && vals.src && /\.(png|jpe?g|gif|webp|svg)$/i.test(vals.src) && (
        <div className="muted small">Tip: you can also drag images from the Files panel straight onto the page.</div>
      )}
    </Modal>
  );
}

/* ------------------------------------------------------------- Site setup */

export function SiteDialog(p: { root: string; config: SiteConfig; platform: Platform; onSave(c: SiteConfig): void; onClose(): void }) {
  const [c, setC] = useState<SiteConfig>(p.config);
  const ftp: FtpConfig = c.ftp ?? { host: '', port: 21, user: '', remoteDir: '/public_html', secure: true, passive: true };
  const setFtp = (patch: Partial<FtpConfig>) => setC({ ...c, ftp: { ...ftp, ...patch } });
  const [pw, setPw] = useState('');
  const [test, setTest] = useState('');
  return (
    <Modal
      title="Site Setup"
      onClose={p.onClose}
      width={520}
      footer={
        <>
          <button className="btn" onClick={p.onClose}>Cancel</button>
          <button className="btn primary" onClick={() => p.onSave(c)}>Save</button>
        </>
      }
    >
      <Field label="Site name" wide>
        <input className="inp" value={c.name} onChange={(e) => setC({ ...c, name: e.target.value })} />
      </Field>
      <Field label="Local site folder" wide>
        <input className="inp" value={p.root} readOnly />
      </Field>
      <h4 className="dlg-h">Remote server (FTP / FTPS)</h4>
      {!p.platform.canPublish && <div className="note">Publishing to a server needs the desktop app. In the browser you can use File › Export Site as ZIP.</div>}
      <div className="row2">
        <Field label="Host" wide>
          <input className="inp" placeholder="ftp.example.com" value={ftp.host} onChange={(e) => setFtp({ host: e.target.value })} />
        </Field>
        <Field label="Port">
          <input className="inp" type="number" value={ftp.port} onChange={(e) => setFtp({ port: +e.target.value || 21 })} />
        </Field>
      </div>
      <div className="row2">
        <Field label="Username" wide>
          <input className="inp" value={ftp.user} onChange={(e) => setFtp({ user: e.target.value })} />
        </Field>
        <Field label="Remote folder" wide>
          <input className="inp" value={ftp.remoteDir} onChange={(e) => setFtp({ remoteDir: e.target.value })} />
        </Field>
      </div>
      <div className="row2">
        <label className="field chk-field"><input type="checkbox" checked={ftp.secure} onChange={(e) => setFtp({ secure: e.target.checked })} /><span>Use FTPS (explicit TLS)</span></label>
        <label className="field chk-field"><input type="checkbox" checked={ftp.passive} onChange={(e) => setFtp({ passive: e.target.checked })} /><span>Passive mode</span></label>
      </div>
      {p.platform.canPublish && (
        <div className="row2">
          <Field label="Password (only for the test, never saved)" wide>
            <input className="inp" type="password" value={pw} onChange={(e) => setPw(e.target.value)} />
          </Field>
          <button
            className="btn"
            disabled={!ftp.host}
            onClick={async () => {
              setTest('Connecting…');
              try {
                setTest('✓ ' + (await p.platform.ftpTest!(ftp, pw)));
              } catch (e) {
                setTest('✗ ' + String(e));
              }
            }}
          >
            Test
          </button>
        </div>
      )}
      {test && <div className={'note ' + (test.startsWith('✓') ? 'ok' : '')}>{test}</div>}
    </Modal>
  );
}

/* ---------------------------------------------------------------- Publish */

export function PublishDialog(p: {
  platform: Platform;
  root: string;
  config: SiteConfig;
  files: FileEntry[];
  published: Record<string, number>;
  rel(path: string): string;
  onPublished(state: Record<string, number>): void;
  onEditSite(): void;
  onClose(): void;
}) {
  const [pw, setPw] = useState('');
  const [onlyChanged, setOnlyChanged] = useState(Object.keys(p.published).length > 0);
  const [prog, setProg] = useState<PublishProgress | null>(null);
  const [log, setLog] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const ftp = p.config.ftp;
  const candidates = p.files.filter((f) => !onlyChanged || p.published[p.rel(f.path)] !== f.mtime);

  const go = async () => {
    if (!ftp || !p.platform.ftpPublish) return;
    setBusy(true);
    setLog([]);
    const ok: Record<string, number> = { ...p.published };
    const byRel = new Map(candidates.map((f) => [p.rel(f.path), f]));
    try {
      await p.platform.ftpPublish(ftp, pw, p.root, [...byRel.keys()], (e) => {
        setProg(e);
        if (e.error) setLog((l) => [...l, `✗ ${e.file}: ${e.error}`]);
        else if (e.file) {
          setLog((l) => [...l.slice(-300), `✓ ${e.file}`]);
          const f = byRel.get(e.file);
          if (f?.mtime) ok[e.file] = f.mtime;
        }
      });
      setLog((l) => [...l, 'Done.']);
    } catch (e) {
      setLog((l) => [...l, '✗ ' + String(e)]);
    }
    p.onPublished(ok);
    setBusy(false);
  };

  return (
    <Modal
      title="Publish Site"
      onClose={() => !busy && p.onClose()}
      width={560}
      footer={
        <>
          <button className="btn" disabled={busy} onClick={p.onClose}>Close</button>
          <button className="btn primary" disabled={busy || !ftp?.host || !candidates.length || !p.platform.canPublish} onClick={go}>
            {busy ? 'Publishing…' : `Publish ${candidates.length} file${candidates.length === 1 ? '' : 's'}`}
          </button>
        </>
      }
    >
      {!p.platform.canPublish && <div className="note">Publishing to a server needs the desktop app. Use File › Export Site as ZIP and upload it with your host's file manager.</div>}
      {!ftp?.host ? (
        <div className="note">
          No server set up yet. <button className="link-btn" onClick={p.onEditSite}>Open Site Setup</button>
        </div>
      ) : (
        <>
          <div className="pub-target">
            {ftp.secure ? 'ftps' : 'ftp'}://{ftp.user ? ftp.user + '@' : ''}
            {ftp.host}:{ftp.port}
            {ftp.remoteDir} <button className="link-btn" onClick={p.onEditSite}>Edit</button>
          </div>
          <Field label="Password" wide>
            <input className="inp" type="password" autoFocus value={pw} onChange={(e) => setPw(e.target.value)} />
          </Field>
          <label className="field chk-field">
            <input type="checkbox" checked={onlyChanged} onChange={(e) => setOnlyChanged(e.target.checked)} />
            <span>Only files changed since the last publish</span>
          </label>
        </>
      )}
      {prog && (
        <div className="progress">
          <div className="bar" style={{ width: `${prog.total ? (prog.done / prog.total) * 100 : 0}%` }} />
          <span>
            {prog.done}/{prog.total}
          </span>
        </div>
      )}
      {log.length > 0 && <pre className="pub-log">{log.join('\n')}</pre>}
    </Modal>
  );
}

/* ------------------------------------------------------------------ About */

export function AboutDialog(p: { onClose(): void }) {
  return (
    <Modal title="About Valco Weave" onClose={p.onClose} width={460}>
      <div className="about">
        <Logo size={64} />
        <h2>Valco Weave 0.5</h2>
        <p className="about-tag">Web design, the Vista way</p>
        <p>A visual web editor for Windows, from the Valco suite. Code, Split, Design and Live views; CSS Designer; FTP publishing.</p>
        <div className="suite-card">
          <img src={suiteBadgeUrl} width={44} height={44} alt="" />
          <div>
            <b>Part of Valco Office</b>
            <div className="muted">{SUITE_APPS}</div>
          </div>
        </div>
        <p className="muted">© 2026 ItsRaiden. MIT licensed.</p>
        <h4 className="dlg-h">Shortcuts</h4>
        <table className="keys">
          <tbody>
            {[
              ['Ctrl+N', 'New document'], ['Ctrl+O', 'Open site folder'], ['Ctrl+S / Ctrl+Shift+S', 'Save / Save all'], ['Ctrl+W', 'Close tab'],
              ['Alt+1…4', 'Code / Split / Design / Live'], ['F12', 'Preview in browser'], ['Ctrl+F', 'Find in page (code)'], ['Ctrl+Shift+F', 'Find in site'],
              ['Double-click', 'Edit text in Design view'], ['Enter (editing)', 'New paragraph / list item'], ['Del', 'Delete selected element'],
              ['Ctrl+D', 'Duplicate element (Design) / next match (Code)'], ['Ctrl+Shift+P', 'Command Palette'], ['Ctrl+P', 'Go to file'], ['Ctrl+Space', 'Show suggestions'], ['Shift+Alt+F', 'Format document'], ['Ctrl+click', 'Go to CSS rule'], ['Ctrl+G', 'Go to line'], ['Alt+↑ / Alt+↓', 'Move element up / down'], ['Esc', 'Select parent element'], ['Tab (code)', 'Expand Emmet abbreviation'],
            ].map(([k, v]) => (
              <tr key={k}>
                <td><kbd>{k}</kbd></td>
                <td>{v}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Modal>
  );
}

export function Logo({ size = 22 }: { size?: number }) {
  return <img src={logoUrl} width={size} height={size} alt="" aria-hidden draggable={false} style={{ display: 'inline-block', verticalAlign: 'middle' }} />;
}

/** The Valco Office suite mark (three petals), as on the Valco button in every Valco app. */
export function SuiteMark({ size = 18 }: { size?: number }) {
  return <img src={suiteMarkUrl} width={size} height={size} alt="" aria-hidden draggable={false} style={{ display: 'inline-block', verticalAlign: 'middle' }} />;
}
