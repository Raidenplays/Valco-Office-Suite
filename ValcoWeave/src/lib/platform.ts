// Platform layer: the desktop build talks to Rust commands; the web build uses the
// File System Access API (Chrome/Edge) or an in-browser demo site.
import { basename, dirname, join, norm } from './paths';
import { DEMO_SITE } from './demo';
import { fetchW3c } from './w3c';

export interface FileEntry {
  name: string;
  path: string;
  isDir: boolean;
  size?: number;
  mtime?: number;
}

export interface FtpConfig {
  host: string;
  port: number;
  user: string;
  remoteDir: string;
  secure: boolean; // explicit FTPS
  passive: boolean;
}

export interface PublishProgress {
  done: number;
  total: number;
  file: string;
  error?: string;
}

export interface GitResult {
  code: number;
  stdout: string;
  stderr: string;
}

export interface GitHubBridge {
  tokenGet(): Promise<string | null>;
  tokenSet(token: string): Promise<void>;
  tokenClear(): Promise<void>;
  deviceStart(clientId: string): Promise<{ device_code: string; user_code: string; verification_uri: string; expires_in: number; interval: number }>;
  devicePoll(clientId: string, deviceCode: string): Promise<{ status: string; message?: string }>;
  api<T = unknown>(method: string, path: string, body?: unknown): Promise<T>;
}

export interface Platform {
  kind: 'tauri' | 'fsa' | 'memory';
  pickFolder(): Promise<string | null>;
  readDir(path: string): Promise<FileEntry[]>;
  listAll(root: string): Promise<FileEntry[]>;
  readText(path: string): Promise<string>;
  readBytes(path: string): Promise<Uint8Array>;
  writeText(path: string, text: string): Promise<void>;
  writeBytes(path: string, bytes: Uint8Array): Promise<void>;
  mkdir(path: string): Promise<void>;
  rename(from: string, to: string): Promise<void>;
  remove(path: string): Promise<void>;
  exists(path: string): Promise<boolean>;
  openExternal(path: string): Promise<void>;
  saveDownload(name: string, bytes: Uint8Array): Promise<string | null>;
  /** Yes/no question (native dialog on desktop). */
  ask(message: string): Promise<boolean>;
  /** Desktop only: run a check before the window closes (return false to keep it open). */
  onCloseRequest?(check: () => Promise<boolean>): void;
  canPublish: boolean;
  /** Raw JSON from the W3C validators. */
  w3c(kind: 'html' | 'css', text: string): Promise<string>;
  /** Desktop only: git + GitHub. */
  git?(cwd: string, args: string[], auth?: boolean): Promise<GitResult>;
  github?: GitHubBridge;
  ftpTest?(cfg: FtpConfig, password: string): Promise<string>;
  ftpPublish?(
    cfg: FtpConfig,
    password: string,
    root: string,
    files: string[],
    onProgress: (p: PublishProgress) => void,
  ): Promise<void>;
  demoRoot?: string;
}

const sortEntries = (a: FileEntry, b: FileEntry) =>
  a.isDir === b.isDir ? a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }) : a.isDir ? -1 : 1;

/* ------------------------------------------------------------------ Tauri */

function isTauri(): boolean {
  return typeof window !== 'undefined' && '__TAURI_INTERNALS__' in window;
}

let closeHooked = false;

async function tauriPlatform(): Promise<Platform> {
  const { invoke } = await import('@tauri-apps/api/core');
  const { listen } = await import('@tauri-apps/api/event');
  const dialog = await import('@tauri-apps/plugin-dialog');
  const n = (e: FileEntry): FileEntry => ({ ...e, path: norm(e.path) });
  const writeBytes = async (path: string, bytes: Uint8Array) => {
    await invoke('fs_write_bytes', bytes, { headers: { 'x-path': encodeURIComponent(path) } });
  };
  return {
    kind: 'tauri',
    async pickFolder() {
      const r = await dialog.open({ directory: true, multiple: false, title: 'Choose site folder' });
      return typeof r === 'string' ? norm(r) : null;
    },
    async readDir(path) {
      const r = await invoke<FileEntry[]>('fs_read_dir', { path });
      return r.map(n).sort(sortEntries);
    },
    async listAll(root) {
      return (await invoke<FileEntry[]>('fs_list_all', { root })).map(n);
    },
    readText: (path) => invoke<string>('fs_read_text', { path }),
    async readBytes(path) {
      const buf = await invoke<ArrayBuffer>('fs_read_bytes', { path });
      return new Uint8Array(buf);
    },
    writeText: (path, text) => invoke('fs_write_text', { path, text }),
    writeBytes,
    mkdir: (path) => invoke('fs_mkdir', { path }),
    rename: (from, to) => invoke('fs_rename', { from, to }),
    remove: (path) => invoke('fs_remove', { path }),
    exists: (path) => invoke<boolean>('fs_exists', { path }),
    openExternal: (path) => invoke('open_external', { path }),
    async saveDownload(name, bytes) {
      const target = await dialog.save({ defaultPath: name, title: 'Save as' });
      if (!target) return null;
      await writeBytes(norm(target), bytes);
      return norm(target);
    },
    ask: (message) => dialog.ask(message, { title: 'Valco Weave', kind: 'warning' }),
    onCloseRequest(check) {
      if (closeHooked) return;
      closeHooked = true;
      import('@tauri-apps/api/window').then(({ getCurrentWindow }) => {
        getCurrentWindow().onCloseRequested(async (e) => {
          if (!(await check())) e.preventDefault();
        });
      });
    },
    canPublish: true,
    w3c: (kind, text) => invoke<string>('w3c_validate', { kind, text }),
    git: (cwd, args, auth) => invoke<GitResult>('git_exec', { cwd, args, auth: !!auth }),
    github: {
      tokenGet: () => invoke<string | null>('gh_token_get'),
      tokenSet: (token) => invoke('gh_token_set', { token }),
      tokenClear: () => invoke('gh_token_clear'),
      deviceStart: (clientId) => invoke('gh_device_start', { clientId }),
      devicePoll: (clientId, deviceCode) => invoke('gh_device_poll', { clientId, deviceCode }),
      api: (method, path, body) => invoke('gh_api', { method, path, body: body ?? null }),
    },
    ftpTest: (cfg, password) => invoke<string>('ftp_test', { cfg, password }),
    async ftpPublish(cfg, password, root, files, onProgress) {
      const un = await listen<PublishProgress>('publish-progress', (e) => onProgress(e.payload));
      try {
        await invoke('ftp_publish', { cfg, password, root, files });
      } finally {
        un();
      }
    },
  };
}

/* --------------------------------------------------------- Browser helpers */

function download(name: string, bytes: Uint8Array, mime = 'application/octet-stream') {
  const url = URL.createObjectURL(new Blob([bytes as BlobPart], { type: mime }));
  const a = document.createElement('a');
  a.href = url;
  a.download = name;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 5000);
}

async function openInNewTab(_path: string, html: string) {
  const url = URL.createObjectURL(new Blob([html], { type: 'text/html' }));
  window.open(url, '_blank');
}

/** In-page yes/no dialog (browser confirm() is blocked in sandboxed frames). */
function domAsk(message: string): Promise<boolean> {
  return new Promise((resolve) => {
    const back = document.createElement('div');
    back.className = 'modal-back';
    const box = document.createElement('div');
    box.className = 'modal';
    box.style.width = '420px';
    const title = document.createElement('div');
    title.className = 'modal-title';
    title.textContent = 'Valco Weave';
    const body = document.createElement('div');
    body.className = 'modal-body';
    body.textContent = message;
    const foot = document.createElement('div');
    foot.className = 'modal-foot';
    const no = document.createElement('button');
    no.className = 'btn';
    no.textContent = 'Cancel';
    const yes = document.createElement('button');
    yes.className = 'btn primary';
    yes.textContent = 'OK';
    foot.append(no, yes);
    box.append(title, body, foot);
    back.append(box);
    const done = (v: boolean) => {
      back.remove();
      window.removeEventListener('keydown', key, true);
      resolve(v);
    };
    const key = (e: KeyboardEvent) => {
      if (e.key === 'Escape') { e.stopPropagation(); done(false); }
      if (e.key === 'Enter') { e.stopPropagation(); done(true); }
    };
    no.onclick = () => done(false);
    yes.onclick = () => done(true);
    window.addEventListener('keydown', key, true);
    document.body.append(back);
    yes.focus();
  });
}

/* --------------------------------------------- File System Access API (web) */

type DirHandle = any; // eslint-disable-line @typescript-eslint/no-explicit-any

function fsaPlatform(memory: Platform): Platform {
  const roots = new Map<string, DirHandle>();
  const enc = new TextEncoder();
  const dec = new TextDecoder();

  // Paths look like "fsa:/RootName/sub/file.html"
  const split = (path: string) => {
    const p = norm(path).replace(/^fsa:\/+/, '');
    const parts = p.split('/').filter(Boolean);
    const root = roots.get(parts[0]);
    if (!root) throw new Error('Folder is no longer open: ' + parts[0]);
    return { root, parts: parts.slice(1) };
  };
  const dirHandle = async (path: string, create = false): Promise<DirHandle> => {
    const { root, parts } = split(path);
    let h = root;
    for (const p of parts) h = await h.getDirectoryHandle(p, { create });
    return h;
  };
  const fileHandle = async (path: string, create = false) => {
    const d = await dirHandle(dirname(path), create);
    return d.getFileHandle(basename(path), { create });
  };
  const isFsa = (p: string) => p.startsWith('fsa:');

  const self: Platform = {
    ...memory,
    kind: 'fsa',
    async pickFolder() {
      const w = window as any; // eslint-disable-line @typescript-eslint/no-explicit-any
      const h: DirHandle = await w.showDirectoryPicker({ mode: 'readwrite' });
      let name = h.name;
      let i = 2;
      while (roots.has(name) && roots.get(name) !== h) name = `${h.name}-${i++}`;
      roots.set(name, h);
      return 'fsa:/' + name;
    },
    async readDir(path) {
      if (!isFsa(path)) return memory.readDir(path);
      const d = await dirHandle(path);
      const out: FileEntry[] = [];
      for await (const [name, h] of d.entries()) {
        out.push({ name, path: join(path, name), isDir: h.kind === 'directory' });
      }
      return out.sort(sortEntries);
    },
    async listAll(root) {
      if (!isFsa(root)) return memory.listAll(root);
      const out: FileEntry[] = [];
      const walk = async (p: string, depth: number) => {
        if (depth > 12 || out.length > 20000) return;
        for (const e of await self.readDir(p)) {
          if (e.isDir) {
            if (['.git', 'node_modules', '.weavecraft'].includes(e.name)) continue;
            await walk(e.path, depth + 1);
          } else {
            const f = await (await fileHandle(e.path)).getFile();
            out.push({ ...e, size: f.size, mtime: f.lastModified });
          }
        }
      };
      await walk(root, 0);
      return out;
    },
    async readText(path) {
      if (!isFsa(path)) return memory.readText(path);
      return dec.decode(await self.readBytes(path));
    },
    async readBytes(path) {
      if (!isFsa(path)) return memory.readBytes(path);
      const f = await (await fileHandle(path)).getFile();
      return new Uint8Array(await f.arrayBuffer());
    },
    async writeText(path, text) {
      return self.writeBytes(path, enc.encode(text));
    },
    async writeBytes(path, bytes) {
      if (!isFsa(path)) return memory.writeBytes(path, bytes);
      const fh = await fileHandle(path, true);
      const w = await fh.createWritable();
      await w.write(bytes);
      await w.close();
    },
    async mkdir(path) {
      if (!isFsa(path)) return memory.mkdir(path);
      await dirHandle(path, true);
    },
    async rename(from, to) {
      if (!isFsa(from)) return memory.rename(from, to);
      const copy = async (a: string, b: string) => {
        let isDir = false;
        try {
          await dirHandle(a);
          isDir = true;
        } catch {
          /* file */
        }
        if (isDir) {
          await self.mkdir(b);
          for (const e of await self.readDir(a)) await copy(e.path, join(b, e.name));
        } else {
          await self.writeBytes(b, await self.readBytes(a));
        }
      };
      await copy(from, to);
      await self.remove(from);
    },
    async remove(path) {
      if (!isFsa(path)) return memory.remove(path);
      const d = await dirHandle(dirname(path));
      await d.removeEntry(basename(path), { recursive: true });
    },
    async exists(path) {
      if (!isFsa(path)) return memory.exists(path);
      try {
        await fileHandle(path);
        return true;
      } catch {
        try {
          await dirHandle(path);
          return true;
        } catch {
          return false;
        }
      }
    },
    async openExternal(path) {
      return openInNewTab(path, await self.readText(path));
    },
  };
  return self;
}

/* ------------------------------------------------------ In-memory demo site */

const MEM_KEY = 'weavecraft.memfs.v1';

function memoryPlatform(): Platform {
  const files = new Map<string, Uint8Array>();
  const dirs = new Set<string>(['/']);
  const enc = new TextEncoder();
  const dec = new TextDecoder();

  const addDirs = (p: string) => {
    let d = dirname(p);
    while (d && !dirs.has(d)) {
      dirs.add(d);
      d = dirname(d);
    }
  };
  const persist = () => {
    try {
      const obj: Record<string, string> = {};
      for (const [k, v] of files) {
        if (v.length > 300_000) continue;
        let s = '';
        for (let i = 0; i < v.length; i++) s += String.fromCharCode(v[i]);
        obj[k] = btoa(s);
      }
      localStorage.setItem(MEM_KEY, JSON.stringify({ files: obj, dirs: [...dirs] }));
    } catch {
      /* storage unavailable: keep in memory only */
    }
  };
  const load = () => {
    try {
      const raw = localStorage.getItem(MEM_KEY);
      if (!raw) return false;
      const data = JSON.parse(raw);
      for (const d of data.dirs ?? []) dirs.add(d);
      for (const [k, v] of Object.entries<string>(data.files ?? {})) {
        const s = atob(v);
        const u = new Uint8Array(s.length);
        for (let i = 0; i < s.length; i++) u[i] = s.charCodeAt(i);
        files.set(k, u);
      }
      return files.size > 0;
    } catch {
      return false;
    }
  };
  const seed = () => {
    for (const [p, text] of Object.entries(DEMO_SITE)) {
      const full = join('/demo-site', p);
      files.set(full, enc.encode(text));
      addDirs(full);
    }
    dirs.add('/demo-site');
  };
  if (!load()) seed();

  const self: Platform = {
    kind: 'memory',
    demoRoot: '/demo-site',
    async pickFolder() {
      return '/demo-site';
    },
    async readDir(path) {
      path = norm(path);
      const out = new Map<string, FileEntry>();
      for (const d of dirs) {
        if (d !== path && dirname(d) === path) out.set(d, { name: basename(d), path: d, isDir: true });
      }
      for (const [f, v] of files) {
        if (dirname(f) === path) out.set(f, { name: basename(f), path: f, isDir: false, size: v.length });
      }
      return [...out.values()].sort(sortEntries);
    },
    async listAll(root) {
      root = norm(root);
      return [...files.entries()]
        .filter(([f]) => f.startsWith(root + '/'))
        .map(([f, v]) => ({ name: basename(f), path: f, isDir: false, size: v.length }));
    },
    async readText(path) {
      return dec.decode(await self.readBytes(path));
    },
    async readBytes(path) {
      const f = files.get(norm(path));
      if (!f) throw new Error('File not found: ' + path);
      return f;
    },
    async writeText(path, text) {
      return self.writeBytes(path, enc.encode(text));
    },
    async writeBytes(path, bytes) {
      path = norm(path);
      files.set(path, bytes);
      addDirs(path);
      persist();
    },
    async mkdir(path) {
      path = norm(path);
      dirs.add(path);
      addDirs(path);
      persist();
    },
    async rename(from, to) {
      from = norm(from);
      to = norm(to);
      for (const [k, v] of [...files]) {
        if (k === from || k.startsWith(from + '/')) {
          files.delete(k);
          const nk = to + k.slice(from.length);
          files.set(nk, v);
          addDirs(nk);
        }
      }
      for (const d of [...dirs]) {
        if (d === from || d.startsWith(from + '/')) {
          dirs.delete(d);
          dirs.add(to + d.slice(from.length));
        }
      }
      addDirs(to);
      persist();
    },
    async remove(path) {
      path = norm(path);
      for (const k of [...files.keys()]) if (k === path || k.startsWith(path + '/')) files.delete(k);
      for (const d of [...dirs]) if (d === path || d.startsWith(path + '/')) dirs.delete(d);
      persist();
    },
    async exists(path) {
      path = norm(path);
      return files.has(path) || dirs.has(path);
    },
    async openExternal(path) {
      return openInNewTab(path, await self.readText(path));
    },
    async saveDownload(name, bytes) {
      download(name, bytes);
      return name;
    },
    ask: domAsk,
    w3c: (kind, text) => fetchW3c(kind, text),
    canPublish: false,
  };
  return self;
}

export function resetDemoSite() {
  try {
    localStorage.removeItem(MEM_KEY);
  } catch {
    /* ignore */
  }
}

export async function createPlatform(): Promise<Platform> {
  if (isTauri()) return tauriPlatform();
  const mem = memoryPlatform();
  // Automated tests can plug in a real git + fake GitHub here.
  const bridge = (window as unknown as { __wcTestBridge?: Pick<Platform, 'git' | 'github'> }).__wcTestBridge;
  if (bridge) Object.assign(mem, bridge);
  if (typeof (window as any).showDirectoryPicker === 'function' && window.self === window.top) { // eslint-disable-line @typescript-eslint/no-explicit-any
    return fsaPlatform(mem);
  }
  return mem;
}
