// The Vista glass window frame used across the Valco apps (same look as Valco Express):
// a frosted-blue title bar with the app icon and glowing title, the three glossy
// caption buttons, and a 7px glass border around the window. Desktop only, and only
// for the Windows Vista theme; the other themes keep the normal Windows 11 frame.
import { useEffect, useState, type ReactNode } from 'react';
import { Logo } from './Dialogs';

const isTauri = typeof window !== 'undefined' && '__TAURI_INTERNALS__' in window;
// ?vistaframe shows the frame in a browser too (for screenshots).
const preview = typeof location !== 'undefined' && new URLSearchParams(location.search).has('vistaframe');
type Win = import('@tauri-apps/api/window').Window;

function useHtmlAttrs() {
  const read = () => ({ theme: document.documentElement.dataset.theme || 'vista', mode: document.documentElement.dataset.mode || 'light' });
  const [v, setV] = useState(read);
  useEffect(() => {
    const mo = new MutationObserver(() => setV(read()));
    mo.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme', 'data-mode'] });
    return () => mo.disconnect();
  }, []);
  return v;
}

function useDocTitle() {
  const [t, setT] = useState(document.title);
  useEffect(() => {
    const el = document.querySelector('title');
    if (!el) return;
    const mo = new MutationObserver(() => setT(document.title));
    mo.observe(el, { childList: true, characterData: true, subtree: true });
    return () => mo.disconnect();
  }, []);
  return t;
}

export function WindowFrame({ children }: { children: ReactNode }) {
  const { theme, mode } = useHtmlAttrs();
  const title = useDocTitle();
  const [win, setWin] = useState<Win | null>(null);
  const [max, setMax] = useState(false);
  const [active, setActive] = useState(true);
  const glass = (isTauri || preview) && theme === 'vista';

  useEffect(() => {
    if (!isTauri) return;
    let off: (() => void)[] = [];
    import('@tauri-apps/api/window').then(async ({ getCurrentWindow }) => {
      const w = getCurrentWindow();
      setWin(w);
      setMax(await w.isMaximized());
      off.push(await w.onResized(async () => setMax(await w.isMaximized())));
      off.push(await w.onFocusChanged(({ payload }) => setActive(payload)));
    });
    return () => { off.forEach((f) => f()); off = []; };
  }, []);

  // Glass frame for Vista; the normal frame (light or dark to match) for everything else.
  useEffect(() => {
    if (!win) return;
    win.setDecorations(!glass).catch(() => {});
    win.setTheme(mode === 'dark' ? 'dark' : 'light').catch(() => {});
  }, [win, glass, mode]);

  useEffect(() => {
    if (win) win.setTitle(title).catch(() => {});
  }, [win, title]);

  if (!glass) return <>{children}</>;
  return (
    <div className={'vframe' + (max ? ' max' : '') + (active ? '' : ' inactive') + (mode === 'dark' ? ' dark' : '')}>
      <div className="vframe-streak" />
      <div className="vframe-bar" data-tauri-drag-region onDoubleClick={() => win?.toggleMaximize()}>
        <span className="vframe-icon" data-tauri-drag-region><Logo size={16} /></span>
        <span className="vframe-title" data-tauri-drag-region>{title}</span>
        <div className="vframe-caps">
          <button className="vcap l" title="Minimize" onClick={() => win?.minimize()}>
            <svg width="10" height="5" viewBox="0 0 10 5"><path d="M0.5,1.5 H9.5 V4.5 H0.5 Z" /></svg>
          </button>
          <button className="vcap" title={max ? 'Restore Down' : 'Maximize'} onClick={() => win?.toggleMaximize()}>
            {max ? (
              <svg width="12" height="10" viewBox="0 0 12 10"><path d="M3.5,0.5 H11.5 V6.5 H9.5 V3 H3.5 Z M0.5,4 H8.5 V9.5 H0.5 Z M2.5,6.3 H6.5 V7.7 H2.5 Z" /></svg>
            ) : (
              <svg width="11" height="9" viewBox="0 0 11 9"><path d="M0.5,0.5 H10.5 V8.5 H0.5 Z M2.5,3.5 H8.5 V6.5 H2.5 Z" fillRule="evenodd" /></svg>
            )}
          </button>
          <button className="vcap close r" title="Close" onClick={() => win?.close()}>
            <svg width="10" height="10" viewBox="0 0 10 10"><path d="M0.4,1.6 L1.6,0.4 L5,3.8 L8.4,0.4 L9.6,1.6 L6.2,5 L9.6,8.4 L8.4,9.6 L5,6.2 L1.6,9.6 L0.4,8.4 L3.8,5 Z" /></svg>
          </button>
        </div>
      </div>
      <div className="vframe-client">{children}</div>
    </div>
  );
}
