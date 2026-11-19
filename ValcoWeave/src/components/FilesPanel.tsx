import { useCallback, useEffect, useState } from 'react';
import type { FileEntry, Platform } from '../lib/platform';
import { ext, isHtml, isImage } from '../lib/paths';
import { ContextMenu, type MenuItem } from './ui';

interface Props {
  platform: Platform;
  root: string;
  siteName: string;
  refreshKey: number;
  activePath: string | null;
  canInsert: boolean;
  relTo(path: string): string;
  onOpen(path: string): void;
  onNewFile(dir: string): void;
  onNewFolder(dir: string): void;
  onRename(e: FileEntry): void;
  onDelete(e: FileEntry): void;
  onPreview(path: string): void;
  onInsert(path: string): void;
  onRefresh(): void;
}

const ICONS: Record<string, string> = {
  html: '🌐', htm: '🌐', css: '🎨', js: '📜', json: '{}', md: '📝', svg: '🖌', png: '🖼', jpg: '🖼', jpeg: '🖼', gif: '🖼', webp: '🖼', ico: '🖼',
  txt: '📄', php: '🐘', xml: '📄', mp4: '🎞', mp3: '🎵', woff: '🔤', woff2: '🔤', ttf: '🔤', pdf: '📕', zip: '🗜',
};

export default function FilesPanel(p: Props) {
  const [open, setOpen] = useState<Set<string>>(() => new Set([p.root]));
  const [kids, setKids] = useState<Map<string, FileEntry[]>>(new Map());
  const [selected, setSelected] = useState<string | null>(null);
  const [menu, setMenu] = useState<{ x: number; y: number; e: FileEntry } | null>(null);

  const load = useCallback(
    async (dir: string) => {
      try {
        const list = await p.platform.readDir(dir);
        setKids((m) => new Map(m).set(dir, list.filter((e) => e.name !== '.weavecraft' && e.name !== '.git' && e.name !== '.DS_Store')));
      } catch {
        setKids((m) => new Map(m).set(dir, []));
      }
    },
    [p.platform],
  );

  useEffect(() => {
    setOpen(new Set([p.root]));
    setKids(new Map());
  }, [p.root]);

  useEffect(() => {
    for (const d of open) load(d);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [p.refreshKey, p.root, load]);

  const toggle = (dir: string) => {
    setOpen((s) => {
      const n = new Set(s);
      if (n.has(dir)) n.delete(dir);
      else {
        n.add(dir);
        load(dir);
      }
      return n;
    });
  };

  const menuItems = (e: FileEntry): MenuItem[] => {
    const dir = e.isDir ? e.path : e.path.slice(0, e.path.lastIndexOf('/'));
    return [
      ...(e.isDir ? [] : [{ label: 'Open', action: () => p.onOpen(e.path) }]),
      ...(!e.isDir && isHtml(e.path) ? [{ label: 'Preview in browser', action: () => p.onPreview(e.path) }] : []),
      ...(!e.isDir && p.canInsert ? [{ label: isImage(e.path) ? 'Insert image into page' : ext(e.path) === 'css' ? 'Attach stylesheet to page' : 'Insert link into page', action: () => p.onInsert(e.path) }] : []),
      { separator: true },
      { label: 'New file…', action: () => p.onNewFile(dir) },
      { label: 'New folder…', action: () => p.onNewFolder(dir) },
      { separator: true },
      ...(e.path === p.root ? [] : [
        { label: 'Rename…', action: () => p.onRename(e) },
        { label: 'Delete', action: () => p.onDelete(e) },
      ]),
      { label: 'Copy relative path', action: () => navigator.clipboard?.writeText(p.relTo(e.path)) },
    ];
  };

  const renderDir = (dir: string, depth: number): React.ReactNode =>
    (kids.get(dir) ?? []).map((e) => (
      <div key={e.path}>
        <div
          className={'tree-row' + (e.path === p.activePath ? ' active' : '') + (e.path === selected ? ' sel' : '')}
          style={{ paddingLeft: 8 + depth * 14 }}
          draggable={!e.isDir}
          onDragStart={(ev) => {
            ev.dataTransfer.setData('application/x-weavecraft-path', e.path);
            ev.dataTransfer.setData('text/plain', p.relTo(e.path));
            ev.dataTransfer.effectAllowed = 'copy';
          }}
          onClick={() => {
            setSelected(e.path);
            if (e.isDir) toggle(e.path);
          }}
          onDoubleClick={() => !e.isDir && p.onOpen(e.path)}
          onContextMenu={(ev) => {
            ev.preventDefault();
            setSelected(e.path);
            setMenu({ x: ev.clientX, y: ev.clientY, e });
          }}
          title={e.path}
        >
          <span className="twisty">{e.isDir ? (open.has(e.path) ? '▾' : '▸') : ''}</span>
          <span className="ficon">{e.isDir ? (open.has(e.path) ? '📂' : '📁') : ICONS[ext(e.path)] ?? '📄'}</span>
          <span className="fname">{e.name}</span>
        </div>
        {e.isDir && open.has(e.path) && renderDir(e.path, depth + 1)}
      </div>
    ));

  const rootEntry: FileEntry = { name: p.siteName, path: p.root, isDir: true };
  const selectedDir = (() => {
    if (!selected) return p.root;
    const isDir = [...kids.values()].flat().find((x) => x.path === selected)?.isDir;
    return isDir ? selected : selected.slice(0, selected.lastIndexOf('/')) || p.root;
  })();

  return (
    <div className="files">
      <div className="sec-head">
        <span title={p.root}>🗂 {p.siteName}</span>
        <span className="grow" />
        <button className="icon-btn" title="New file" onClick={() => p.onNewFile(selectedDir)}>＋</button>
        <button className="icon-btn" title="New folder" onClick={() => p.onNewFolder(selectedDir)}>📁</button>
        <button className="icon-btn" title="Refresh" onClick={p.onRefresh}>⟳</button>
        <button className="icon-btn" title="Collapse all" onClick={() => setOpen(new Set([p.root]))}>⊟</button>
      </div>
      <div
        className="tree"
        onContextMenu={(ev) => {
          if (ev.target === ev.currentTarget) {
            ev.preventDefault();
            setMenu({ x: ev.clientX, y: ev.clientY, e: rootEntry });
          }
        }}
      >
        {renderDir(p.root, 0)}
      </div>
      {menu && <ContextMenu x={menu.x} y={menu.y} items={menuItems(menu.e)} onClose={() => setMenu(null)} />}
    </div>
  );
}
