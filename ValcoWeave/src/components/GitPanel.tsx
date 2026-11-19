// Source control panel: GitHub account, changes (staged / unstaged), commit,
// push / pull, history, and one-click "Publish to GitHub".
import { useCallback, useEffect, useRef, useState } from 'react';
import type { GitResult, Platform } from '../lib/platform';
import { basename, join } from '../lib/paths';
import { Field, Modal } from './ui';

interface Props {
  platform: Platform;
  root: string;
  siteName: string;
  refreshKey: number;
  onOpen(path: string): void;
  onSaveAll(): Promise<void>;
  flash(msg: string): void;
  ask(msg: string): Promise<boolean>;
}

interface Account {
  login: string;
  name: string | null;
  id: number;
  avatar_url: string;
}

interface Change {
  path: string;
  x: string; // index status
  y: string; // work-tree status
}

interface Commit {
  hash: string;
  subject: string;
  author: string;
  when: string;
}

const CLIENT_ID_KEY = 'weavecraft.github.clientId';

function parseStatus(out: string) {
  const lines = out.split('\n').filter(Boolean);
  let branch = 'main';
  let upstream: string | null = null;
  let ahead = 0;
  let behind = 0;
  let noCommits = false;
  const changes: Change[] = [];
  for (const l of lines) {
    if (l.startsWith('## ')) {
      const h = l.slice(3);
      if (h.startsWith('No commits yet on ')) {
        noCommits = true;
        branch = h.slice('No commits yet on '.length).trim();
        continue;
      }
      const m = h.match(/^([^.\s]+)(?:\.\.\.(\S+))?(?: \[(.*)\])?/);
      if (m) {
        branch = m[1];
        upstream = m[2] ?? null;
        const a = m[3]?.match(/ahead (\d+)/);
        const b = m[3]?.match(/behind (\d+)/);
        ahead = a ? +a[1] : 0;
        behind = b ? +b[1] : 0;
      }
      continue;
    }
    const x = l[0];
    const y = l[1];
    let path = l.slice(3);
    if (path.includes(' -> ')) path = path.split(' -> ')[1];
    path = path.replace(/^"(.*)"$/, '$1');
    changes.push({ path, x, y });
  }
  return { branch, upstream, ahead, behind, noCommits, changes };
}

const LETTER_TITLE: Record<string, string> = { M: 'Modified', A: 'Added', D: 'Deleted', R: 'Renamed', C: 'Copied', U: 'Conflict', '?': 'Untracked' };

export default function GitPanel(p: Props) {
  const git = p.platform.git;
  const gh = p.platform.github;
  const [account, setAccount] = useState<Account | null>(null);
  const [state, setState] = useState<'loading' | 'nogit' | 'norepo' | 'ready'>('loading');
  const [info, setInfo] = useState<ReturnType<typeof parseStatus> | null>(null);
  const [remote, setRemote] = useState<string | null>(null);
  const [log, setLog] = useState<Commit[]>([]);
  const [msg, setMsg] = useState('');
  const [busy, setBusy] = useState<string | null>(null);
  const [dialog, setDialog] = useState<null | 'signin' | 'publish'>(null);
  const alive = useRef(true);
  useEffect(() => () => void (alive.current = false), []);

  const run = useCallback(
    async (args: string[], auth = false): Promise<GitResult> => {
      if (!git) throw new Error('Git is only available in the desktop app');
      return git(p.root, args, auth);
    },
    [git, p.root],
  );
  const must = async (args: string[], auth = false) => {
    const r = await run(args, auth);
    if (r.code !== 0) throw new Error((r.stderr || r.stdout).trim().split('\n').slice(-3).join('\n') || `git ${args[0]} failed`);
    return r.stdout;
  };

  /* ------------------------------------------------------------ account */
  const loadAccount = useCallback(async () => {
    if (!gh) return;
    const tok = await gh.tokenGet().catch(() => null);
    if (!tok) {
      setAccount(null);
      return;
    }
    try {
      setAccount(await gh.api<Account>('GET', '/user'));
    } catch {
      setAccount(null);
    }
  }, [gh]);
  useEffect(() => {
    loadAccount();
  }, [loadAccount]);

  /* --------------------------------------------------------------- repo */
  const refresh = useCallback(async () => {
    if (!git) return;
    try {
      const top = await run(['rev-parse', '--show-toplevel']);
      if (top.code !== 0) {
        if (alive.current) setState('norepo');
        return;
      }
      const st = await run(['status', '--porcelain=v1', '-b', '-uall']);
      const parsed = parseStatus(st.stdout);
      const rem = await run(['remote', 'get-url', 'origin']);
      const lg = parsed.noCommits ? null : await run(['log', '-n', '40', '--pretty=format:%h%x1f%s%x1f%an%x1f%ar']);
      if (!alive.current) return;
      setInfo(parsed);
      setRemote(rem.code === 0 ? rem.stdout.trim() : null);
      setLog(
        (lg?.stdout ?? '')
          .split('\n')
          .filter(Boolean)
          .map((l) => {
            const [hash, subject, author, when] = l.split('\x1f');
            return { hash, subject, author, when };
          }),
      );
      setState('ready');
    } catch (e) {
      if (String(e).includes('GIT_NOT_FOUND')) setState('nogit');
      else p.flash(String(e));
    }
  }, [git, run, p]);
  useEffect(() => {
    refresh();
  }, [refresh, p.refreshKey]);

  const act = async (label: string, fn: () => Promise<void>) => {
    setBusy(label);
    try {
      await fn();
    } catch (e) {
      p.flash((e as Error).message ?? String(e));
    }
    setBusy(null);
    refresh();
  };

  /** Make sure commits have an author: use the GitHub account for this repo if git has none. */
  const ensureIdentity = async () => {
    const name = (await run(['config', 'user.name'])).stdout.trim();
    const email = (await run(['config', 'user.email'])).stdout.trim();
    if (name && email) return;
    if (!account) throw new Error('Git needs your name and email for commits. Sign in to GitHub (top of this panel) and Valco Weave will fill them in.');
    if (!name) await must(['config', 'user.name', account.name || account.login]);
    if (!email) await must(['config', 'user.email', `${account.id}+${account.login}@users.noreply.github.com`]);
  };

  const commit = (push: boolean) =>
    act(push ? 'Committing & pushing…' : 'Committing…', async () => {
      if (!msg.trim()) throw new Error('Type a commit message first');
      await p.onSaveAll();
      await ensureIdentity();
      const st = parseStatus((await run(['status', '--porcelain=v1', '-b', '-uall'])).stdout);
      const staged = st.changes.some((c) => c.x !== ' ' && c.x !== '?');
      if (!staged) {
        if (!st.changes.length) throw new Error('Nothing to commit: your files match the last commit');
        await must(['add', '-A']);
      }
      await must(['commit', '-m', msg.trim()]);
      setMsg('');
      if (push) await doPush();
      p.flash(push ? 'Committed and pushed' : 'Committed');
    });

  const doPush = async () => {
    const st = parseStatus((await run(['status', '--porcelain=v1', '-b'])).stdout);
    if (!(await run(['remote', 'get-url', 'origin'])).stdout.trim()) throw new Error('This repository has no remote yet. Use "Publish to GitHub".');
    await must(st.upstream ? ['push'] : ['push', '-u', 'origin', 'HEAD'], true);
  };

  const stage = (c: Change) => act('', () => must(['add', '--', c.path]).then(() => undefined));
  const unstage = (c: Change) => act('', () => must(['reset', '-q', 'HEAD', '--', c.path]).then(() => undefined).catch(() => must(['rm', '--cached', '-q', '--', c.path]).then(() => undefined)));
  const discard = async (c: Change) => {
    const untracked = c.x === '?';
    if (!(await p.ask(untracked ? `Delete the new file "${c.path}"? It has never been committed, so it can't be recovered.` : `Discard your changes to "${c.path}"? This puts back the last committed version.`))) return;
    act('', async () => {
      if (untracked) await must(['clean', '-f', '--', c.path]);
      else await must(['checkout', 'HEAD', '--', c.path]);
    });
  };

  const init = () =>
    act('Creating repository…', async () => {
      const r = await run(['init', '-b', 'main']);
      if (r.code !== 0) await must(['init']);
      await writeGitignore();
    });

  const writeGitignore = async () => {
    const gi = join(p.root, '.gitignore');
    if (!(await p.platform.exists(gi))) await p.platform.writeText(gi, '# Valco Weave\n.weavecraft/publish-state.json\n.DS_Store\nThumbs.db\n');
  };

  /* ------------------------------------------------------------- render */
  if (!git || !gh) {
    return <div className="empty-note">Git and GitHub need the Valco Weave desktop app. The browser version can't run Git.</div>;
  }

  const staged = info?.changes.filter((c) => c.x !== ' ' && c.x !== '?') ?? [];
  const unstaged = info?.changes.filter((c) => c.y !== ' ' || c.x === '?') ?? [];
  const isGitHub = remote ? /github\.com[/:]/i.test(remote) : false;
  const webUrl = remote && isGitHub ? remote.replace(/^git@github\.com:/, 'https://github.com/').replace(/\.git$/, '') : null;

  const row = (c: Change, isStaged: boolean) => {
    const letter = isStaged ? c.x : c.x === '?' ? 'U' : c.y;
    const title = c.x === '?' ? 'Untracked (new file)' : LETTER_TITLE[letter] ?? letter;
    return (
      <div key={(isStaged ? 's:' : 'u:') + c.path} className="git-row" title={c.path} onDoubleClick={() => letter !== 'D' && p.onOpen(join(p.root, c.path))}>
        <span className="git-name">{basename(c.path)}</span>
        <span className="git-dir">{c.path.includes('/') ? c.path.slice(0, c.path.lastIndexOf('/')) : ''}</span>
        <span className="grow" />
        {!isStaged && (
          <button className="icon-btn" title="Discard changes" onClick={() => discard(c)}>↺</button>
        )}
        <button className="icon-btn" title={isStaged ? 'Unstage' : 'Stage'} onClick={() => (isStaged ? unstage(c) : stage(c))}>{isStaged ? '−' : '+'}</button>
        <span className={'git-letter l-' + (c.x === '?' ? 'U' : letter)} title={title}>{c.x === '?' ? 'U' : letter}</span>
      </div>
    );
  };

  return (
    <div className="git">
      {/* account */}
      <div className="git-account">
        {account ? (
          <>
            <img src={account.avatar_url} alt="" className="git-avatar" />
            <div className="git-who">
              <b>{account.name || account.login}</b>
              <span className="muted">@{account.login}</span>
            </div>
            <span className="grow" />
            <button
              className="btn sm"
              onClick={async () => {
                if (!(await p.ask('Sign out of GitHub on this computer?'))) return;
                await gh.tokenClear();
                setAccount(null);
              }}
            >
              Sign out
            </button>
          </>
        ) : (
          <>
            <span className="git-gh">◉</span>
            <span className="muted">Not signed in to GitHub</span>
            <span className="grow" />
            <button className="btn sm primary" onClick={() => setDialog('signin')}>Sign in</button>
          </>
        )}
      </div>

      {state === 'loading' && <div className="empty-note">Checking for a Git repository…</div>}
      {state === 'nogit' && (
        <div className="empty-note">
          Git isn't installed on this computer. Install it from <a href="https://git-scm.com/downloads" onClick={(e) => { e.preventDefault(); p.platform.openExternal('https://git-scm.com/downloads'); }}>git-scm.com</a>, then press ⟳.
          <div style={{ marginTop: 8 }}>
            <button className="btn sm" onClick={refresh}>⟳ Check again</button>
          </div>
        </div>
      )}
      {state === 'norepo' && (
        <div className="git-empty">
          <p>This site isn't a Git repository yet.</p>
          <button className="btn primary" disabled={!!busy} onClick={() => (account ? setDialog('publish') : setDialog('signin'))}>⇪ Publish to GitHub…</button>
          <button className="btn" disabled={!!busy} onClick={init}>Initialize repository only</button>
          {busy && <p className="muted">{busy}</p>}
        </div>
      )}

      {state === 'ready' && info && (
        <>
          <div className="sec-head">
            <span title={info.upstream ?? 'no upstream'}>⑂ {info.branch}</span>
            {info.ahead > 0 && <span className="git-badge" title="Commits to push">↑{info.ahead}</span>}
            {info.behind > 0 && <span className="git-badge" title="Commits to pull">↓{info.behind}</span>}
            <span className="grow" />
            <button className="icon-btn" title="Pull" disabled={!!busy || !remote} onClick={() => act('Pulling…', () => must(['pull', '--ff-only'], true).then(() => p.flash('Up to date')))}>↓</button>
            <button className="icon-btn" title="Push" disabled={!!busy || !remote} onClick={() => act('Pushing…', () => doPush().then(() => p.flash('Pushed')))}>↑</button>
            <button className="icon-btn" title="Refresh" onClick={refresh}>⟳</button>
          </div>
          {remote ? (
            <div className="git-remote">
              {webUrl ? <a href={webUrl} onClick={(e) => { e.preventDefault(); p.platform.openExternal(webUrl); }}>↗ {webUrl.replace('https://github.com/', '')}</a> : <span className="muted">{remote}</span>}
            </div>
          ) : (
            <div className="git-remote">
              <span className="muted">No remote.</span>{' '}
              <button className="link-btn" onClick={() => (account ? setDialog('publish') : setDialog('signin'))}>Publish to GitHub…</button>
            </div>
          )}
          <div className="git-commit">
            <textarea
              className="inp"
              rows={3}
              placeholder={`Message (Ctrl+Enter to commit on "${info.branch}")`}
              value={msg}
              onChange={(e) => setMsg(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
                  e.preventDefault();
                  commit(false);
                }
              }}
            />
            <div className="git-commit-btns">
              <button className="btn primary" disabled={!!busy || !info.changes.length} onClick={() => commit(false)}>
                ✓ {staged.length ? 'Commit' : 'Commit all'}
              </button>
              <button className="btn" disabled={!!busy || !info.changes.length || !remote} onClick={() => commit(true)} title="Commit, then push to GitHub">
                Commit & Push
              </button>
            </div>
            {busy && <div className="muted small">{busy}</div>}
          </div>
          <div className="git-list">
            {staged.length > 0 && (
              <>
                <div className="git-group">
                  Staged changes <span className="git-count">{staged.length}</span>
                  <span className="grow" />
                  <button className="icon-btn" title="Unstage all" onClick={() => act('', () => must(['reset', '-q']).then(() => undefined))}>−</button>
                </div>
                {staged.map((c) => row(c, true))}
              </>
            )}
            <div className="git-group">
              Changes <span className="git-count">{unstaged.length}</span>
              <span className="grow" />
              {unstaged.length > 0 && <button className="icon-btn" title="Stage all" onClick={() => act('', () => must(['add', '-A']).then(() => undefined))}>+</button>}
            </div>
            {unstaged.map((c) => row(c, false))}
            {!info.changes.length && <div className="empty-note">No changes since the last commit.</div>}
            {log.length > 0 && (
              <>
                <div className="git-group">History</div>
                {log.map((c) => (
                  <div key={c.hash} className="git-log" title={`${c.hash} · ${c.author}`}>
                    <span className="git-dot" />
                    <span className="git-subject">{c.subject}</span>
                    <span className="muted small">{c.when}</span>
                  </div>
                ))}
              </>
            )}
          </div>
        </>
      )}

      {dialog === 'signin' && (
        <SignInDialog
          platform={p.platform}
          onDone={async () => {
            setDialog(null);
            await loadAccount();
          }}
          onClose={() => setDialog(null)}
        />
      )}
      {dialog === 'publish' && account && (
        <PublishRepoDialog
          defaultName={p.siteName.replace(/[^\w.-]+/g, '-').replace(/^-+|-+$/g, '') || 'my-site'}
          onClose={() => setDialog(null)}
          onCreate={(name, description, isPrivate) =>
            act('Publishing to GitHub…', async () => {
              setDialog(null);
              await p.onSaveAll();
              const repo = await gh.api<{ clone_url: string; html_url: string; full_name: string }>('POST', '/user/repos', { name, description, private: isPrivate, auto_init: false });
              const top = await run(['rev-parse', '--show-toplevel']);
              if (top.code !== 0) {
                const r = await run(['init', '-b', 'main']);
                if (r.code !== 0) await must(['init']);
              }
              await writeGitignore();
              await ensureIdentity();
              await must(['add', '-A']);
              const head = await run(['rev-parse', '--verify', 'HEAD']);
              const pending = (await run(['status', '--porcelain'])).stdout.trim();
              if (head.code !== 0 || pending) await must(['commit', '-m', head.code !== 0 ? 'Initial commit from Valco Weave' : 'Update from Valco Weave']);
              const hasOrigin = (await run(['remote', 'get-url', 'origin'])).code === 0;
              await must(hasOrigin ? ['remote', 'set-url', 'origin', repo.clone_url] : ['remote', 'add', 'origin', repo.clone_url]);
              await must(['push', '-u', 'origin', 'HEAD'], true);
              p.flash(`Published to github.com/${repo.full_name}`);
            })
          }
        />
      )}
    </div>
  );
}

/* --------------------------------------------------------------- sign in */

function SignInDialog(p: { platform: Platform; onDone(): void; onClose(): void }) {
  const gh = p.platform.github!;
  const [tab, setTab] = useState<'token' | 'browser'>(() => (localStorage.getItem(CLIENT_ID_KEY) ? 'browser' : 'token'));
  const [token, setToken] = useState('');
  const [clientId, setClientId] = useState(() => localStorage.getItem(CLIENT_ID_KEY) ?? '');
  const [code, setCode] = useState<{ user_code: string; verification_uri: string } | null>(null);
  const [status, setStatus] = useState('');
  const [busy, setBusy] = useState(false);
  const stop = useRef(false);
  useEffect(() => () => void (stop.current = true), []);

  const saveToken = async () => {
    setBusy(true);
    setStatus('Checking token…');
    try {
      await gh.tokenSet(token.trim());
      const me = await gh.api<{ login: string }>('GET', '/user');
      setStatus('Signed in as ' + me.login);
      p.onDone();
    } catch (e) {
      await gh.tokenClear().catch(() => {});
      setStatus('That token didn\'t work: ' + String(e));
    }
    setBusy(false);
  };

  const browserSignIn = async () => {
    setBusy(true);
    setStatus('');
    try {
      localStorage.setItem(CLIENT_ID_KEY, clientId.trim());
      const d = await gh.deviceStart(clientId.trim());
      setCode(d);
      await p.platform.openExternal(d.verification_uri).catch(() => {});
      let interval = Math.max(5, d.interval);
      const until = Date.now() + d.expires_in * 1000;
      while (!stop.current && Date.now() < until) {
        await new Promise((r) => setTimeout(r, interval * 1000));
        const r = await gh.devicePoll(clientId.trim(), d.device_code);
        if (r.status === 'ok') {
          p.onDone();
          return;
        }
        if (r.status === 'slow_down') interval += 5;
        else if (r.status !== 'pending') {
          setStatus(r.status === 'denied' ? 'Sign-in was cancelled on GitHub.' : r.message ?? 'Sign-in failed: ' + r.status);
          break;
        }
      }
    } catch (e) {
      setStatus(String(e));
    }
    setBusy(false);
  };

  return (
    <Modal title="Sign in to GitHub" onClose={p.onClose} width={500}>
      <div className="seg-group" style={{ alignSelf: 'flex-start' }}>
        <button className={'seg' + (tab === 'token' ? ' on' : '')} onClick={() => setTab('token')}>Personal access token</button>
        <button className={'seg' + (tab === 'browser' ? ' on' : '')} onClick={() => setTab('browser')}>Sign in with browser</button>
      </div>
      {tab === 'token' ? (
        <>
          <p className="small">
            1. <a href="https://github.com/settings/tokens/new?scopes=repo,read:user&description=Valco Weave" target="_blank" rel="noreferrer" onClick={(e) => { e.preventDefault(); p.platform.openExternal('https://github.com/settings/tokens/new?scopes=repo,read:user&description=Valco Weave'); }}>Create a token on GitHub</a> (the <code>repo</code> and <code>read:user</code> boxes are already ticked).
            <br />2. Copy it and paste it below. It's kept in Windows Credential Manager, never in a file.
          </p>
          <Field label="Token" wide>
            <input className="inp" type="password" autoFocus value={token} onChange={(e) => setToken(e.target.value)} placeholder="ghp_…" onKeyDown={(e) => e.key === 'Enter' && token.trim() && saveToken()} />
          </Field>
          <div className="row2">
            <span className="grow" />
            <button className="btn primary" disabled={busy || !token.trim()} onClick={saveToken}>Sign in</button>
          </div>
        </>
      ) : (
        <>
          <p className="small">
            Uses GitHub's device sign-in, like VS Code. It needs the Client ID of a GitHub OAuth app with <b>Device Flow</b> enabled
            (GitHub → Settings → Developer settings → OAuth Apps → New, then tick "Enable Device Flow"). You only set this up once.
          </p>
          <Field label="OAuth app Client ID" wide>
            <input className="inp" value={clientId} onChange={(e) => setClientId(e.target.value)} placeholder="Ov23li…" />
          </Field>
          {code ? (
            <div className="device-code">
              <div className="muted small">Enter this code on {code.verification_uri.replace('https://', '')}</div>
              <div className="code">{code.user_code}</div>
              <div className="row2">
                <button className="btn sm" onClick={() => navigator.clipboard?.writeText(code.user_code)}>Copy code</button>
                <button className="btn sm" onClick={() => p.platform.openExternal(code.verification_uri)}>Open GitHub</button>
                <span className="muted small">Waiting for you to approve…</span>
              </div>
            </div>
          ) : (
            <div className="row2">
              <span className="grow" />
              <button className="btn primary" disabled={busy || !clientId.trim()} onClick={browserSignIn}>Continue in browser</button>
            </div>
          )}
        </>
      )}
      {status && <div className="note">{status}</div>}
    </Modal>
  );
}

function PublishRepoDialog(p: { defaultName: string; onCreate(name: string, description: string, isPrivate: boolean): void; onClose(): void }) {
  const [name, setName] = useState(p.defaultName);
  const [desc, setDesc] = useState('');
  const [priv, setPriv] = useState(false);
  return (
    <Modal
      title="Publish to GitHub"
      onClose={p.onClose}
      width={460}
      footer={
        <>
          <button className="btn" onClick={p.onClose}>Cancel</button>
          <button className="btn primary" disabled={!/^[\w.-]+$/.test(name)} onClick={() => p.onCreate(name, desc, priv)}>Publish</button>
        </>
      }
    >
      <p className="small">Creates a new repository on your GitHub account, commits this site and pushes it.</p>
      <Field label="Repository name" wide>
        <input className="inp" autoFocus value={name} onChange={(e) => setName(e.target.value)} />
      </Field>
      <Field label="Description (optional)" wide>
        <input className="inp" value={desc} onChange={(e) => setDesc(e.target.value)} />
      </Field>
      <label className="field chk-field">
        <input type="radio" checked={!priv} onChange={() => setPriv(false)} />
        <span>Public — anyone can see it (needed for free GitHub Pages)</span>
      </label>
      <label className="field chk-field">
        <input type="radio" checked={priv} onChange={() => setPriv(true)} />
        <span>Private — only you and people you invite</span>
      </label>
    </Modal>
  );
}
