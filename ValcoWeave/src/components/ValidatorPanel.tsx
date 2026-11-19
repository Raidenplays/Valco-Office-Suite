import { useState } from 'react';
import type { W3cMessage, W3cResult } from '../lib/w3c';

export interface W3cSettings {
  validateOnSave: boolean;
  showWarnings: boolean;
  showInfo: boolean;
  notifications: boolean;
}

export interface FileResult {
  path: string;
  rel: string;
  result?: W3cResult;
  error?: string;
}

interface Props {
  results: FileResult[];
  busy: string | null;
  canValidate: boolean;
  onValidatePage(): void;
  onValidateSite(): void;
  settings: W3cSettings;
  setSettings(s: W3cSettings): void;
  onClear(): void;
  onJump(path: string, m: W3cMessage): void;
}

const ICON = { error: '⛔', warning: '⚠', info: 'ℹ' };

export default function ValidatorPanel(p: Props) {
  const [filter, setFilter] = useState<'all' | 'error' | 'warning'>('all');
  const totals = p.results.reduce(
    (t, r) => {
      for (const m of r.result?.messages ?? []) t[m.type]++;
      if (r.error) t.failed++;
      return t;
    },
    { error: 0, warning: 0, info: 0, failed: 0 },
  );
  const checked = p.results.filter((r) => r.result).length;

  return (
    <div className="w3c">
      <div className="w3c-bar">
        <button className="btn sm primary" disabled={!!p.busy || !p.canValidate} onClick={p.onValidatePage} title="Validate the current page or stylesheet (Shift+F6)">
          ✓ Validate this file
        </button>
        <button className="btn sm" disabled={!!p.busy} onClick={p.onValidateSite} title="Validate every HTML and CSS file in the site">
          Validate whole site
        </button>
        {p.results.length > 0 && <button className="btn sm" disabled={!!p.busy} onClick={p.onClear} title="Remove all W3C results and squiggles">✕ Clear</button>}
        {([
          ['validateOnSave', 'Validate on save', 'Re-check with W3C every time you save'],
          ['showWarnings', 'Warnings', 'Show W3C warnings'],
          ['showInfo', 'Info', 'Show W3C info messages'],
          ['notifications', 'Notifications', 'Pop up a message when validation finishes'],
        ] as [keyof W3cSettings, string, string][]).map(([k, label, tip]) => (
          <label key={k} className="mini-toggle" title={tip}>
            <input type="checkbox" checked={p.settings[k]} onChange={(e) => p.setSettings({ ...p.settings, [k]: e.target.checked })} /> {label}
          </label>
        ))}
        <span className="grow" />
        {p.results.length > 0 && (
          <div className="seg-group">
            {(['all', 'error', 'warning'] as const).map((f) => (
              <button key={f} className={'seg' + (filter === f ? ' on' : '')} onClick={() => setFilter(f)}>
                {f === 'all' ? 'All' : f === 'error' ? `Errors ${totals.error}` : `Warnings ${totals.warning}`}
              </button>
            ))}
          </div>
        )}
        <span className="w3c-by">validator.w3.org/nu</span>
      </div>
      {p.busy && <div className="empty-note">{p.busy}</div>}
      {!p.busy && p.results.length === 0 && (
        <div className="empty-note">
          Check your HTML and CSS against the official W3C validators. Your file is sent to the W3C Nu Html Checker (validator.w3.org) and problems are underlined right in your code. Shortcut: Shift+F6, or the W3C button in the status bar.
        </div>
      )}
      {!p.busy && checked > 0 && totals.error + totals.warning + totals.info === 0 && totals.failed === 0 && (
        <div className="empty-note ok">✓ No errors or warnings. {checked > 1 ? `All ${checked} files pass` : 'This file passes'} W3C validation.</div>
      )}
      <div className="w3c-list">
        {p.results.map((r) => {
          const msgs = (r.result?.messages ?? [])
            .filter((m) => (m.type !== 'warning' || p.settings.showWarnings) && (m.type !== 'info' || p.settings.showInfo))
            .filter((m) => filter === 'all' || m.type === filter);
          if (!r.error && !msgs.length && p.results.length > 1) return null;
          return (
            <div key={r.path} className="w3c-file">
              {p.results.length > 1 && (
                <div className="w3c-file-head">
                  {r.rel}
                  <span className="muted small">
                    {r.error ? ' — failed' : ` — ${r.result!.messages.filter((m) => m.type === 'error').length} errors, ${r.result!.messages.filter((m) => m.type === 'warning').length} warnings`}
                  </span>
                </div>
              )}
              {r.error && <div className="prob error">{r.error}</div>}
              {msgs.map((m, i) => (
                <div key={i} className={'w3c-msg ' + m.type} onClick={() => p.onJump(r.path, m)} title="Jump to this line">
                  <div className="w3c-top">
                    <span className="sev">{ICON[m.type]}</span>
                    <span className="w3c-kind">{m.type === 'error' ? 'Error' : m.type === 'warning' ? 'Warning' : 'Info'}</span>
                    <span className="w3c-text">{m.message}</span>
                    <span className="ln">Line {m.line}{m.col > 1 ? `, col ${m.col}` : ''}</span>
                  </div>
                  {m.extract && (
                    <pre className="w3c-extract">
                      {m.hiliteStart !== undefined && m.hiliteLength !== undefined ? (
                        <>
                          {m.extract.slice(0, m.hiliteStart)}
                          <mark>{m.extract.slice(m.hiliteStart, m.hiliteStart + m.hiliteLength)}</mark>
                          {m.extract.slice(m.hiliteStart + m.hiliteLength)}
                        </>
                      ) : (
                        m.extract
                      )}
                    </pre>
                  )}
                </div>
              ))}
            </div>
          );
        })}
      </div>
    </div>
  );
}
