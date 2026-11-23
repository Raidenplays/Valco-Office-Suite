#!/usr/bin/env python3
"""
Decides what a Valco Office release needs (run by .github/workflows/release.yml).

Every release's valco-office.json records, for each program, a hash of the source folders
it is built from (apps.json "sourceDirs") and the commit it was built at. This compares
those with the current checkout and writes ci-plan.json:

  - programs whose sources changed are rebuilt (they get a new version)
  - programs that didn't change are reused from the previous release, same file, same version,
    so Valco Office Update doesn't download them again
  - ValcoNet is also rebuilt when Mozilla has shipped a newer Firefox ESR (engine security fixes)
  - no changes at all: no release

usage: plan.py [--force] [--out ci-plan.json]
"""
import hashlib
import json
import os
import re
import subprocess
import sys
import urllib.request

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
REPO = os.environ.get('GITHUB_REPOSITORY', 'Raidenplays/Valco-Office-Suite')


def git(*args):
    return subprocess.run(['git', *args], cwd=ROOT, capture_output=True, text=True, check=True).stdout.strip()


def tree_hash(dirs):
    """A hash of the committed contents of these folders (git tree ids, so it doesn't depend on timestamps)."""
    h = hashlib.sha256()
    for d in sorted(dirs):
        d = d.replace('\\', '/').strip('/')
        try:
            h.update(f'{d}:{git("rev-parse", "HEAD:" + d)}\n'.encode())
        except subprocess.CalledProcessError:
            h.update(f'{d}:missing\n'.encode())
    return h.hexdigest()[:16]


def fetch(url, token=None, accept='application/json'):
    req = urllib.request.Request(url, headers={'User-Agent': 'valco-office-ci', 'Accept': accept})
    if token:
        req.add_header('Authorization', f'Bearer {token}')
    with urllib.request.urlopen(req, timeout=60) as r:
        return r.read()


def previous_release():
    """(tag, manifest) of the latest release, or (None, None) when nothing has been published yet."""
    token = os.environ.get('GH_TOKEN') or os.environ.get('GITHUB_TOKEN')
    try:
        rel = json.loads(fetch(f'https://api.github.com/repos/{REPO}/releases/latest', token))
    except Exception as e:
        print(f'No previous release ({e})')
        return None, None
    asset = next((a for a in rel.get('assets', []) if a['name'] == 'valco-office.json'), None)
    if not asset:
        print(f'Previous release {rel["tag_name"]} has no valco-office.json')
        return rel['tag_name'], None
    try:
        return rel['tag_name'], json.loads(fetch(asset['url'], token, 'application/octet-stream').decode('utf-8-sig'))
    except Exception as e:
        print(f'Could not read valco-office.json of {rel["tag_name"]}: {e}')
        return rel['tag_name'], None


def latest_esr():
    try:
        v = json.loads(fetch('https://product-details.mozilla.org/1.0/firefox_versions.json'))['FIREFOX_ESR']
        return re.sub(r'esr$', '', v)
    except Exception as e:
        print(f'Could not read the latest Firefox ESR version: {e}')
        return None


def changes_since(commit, dirs):
    """Commit subjects that touched these folders since the previous release (newest first)."""
    if not commit:
        return []
    try:
        git('cat-file', '-e', commit + '^{commit}')
    except subprocess.CalledProcessError:
        return []
    out = git('log', '--no-merges', '--format=%s', f'{commit}..HEAD', '--', *[d.replace('\\', '/') for d in dirs])
    subjects = []
    for s in out.splitlines():
        s = s.strip()
        if s and s not in subjects and not s.lower().startswith(('ci:', 'docs:', 'release ')):
            subjects.append(s)
    return subjects[:12]


def tidy(subject, name):
    """'Valco Word: fix the ruler' -> 'Fix the ruler' in Valco Word's list."""
    s = re.sub(r'^' + re.escape(name) + r'\s*:\s*', '', subject)
    return s[:1].upper() + s[1:]


def main():
    force = '--force' in sys.argv
    out_path = sys.argv[sys.argv.index('--out') + 1] if '--out' in sys.argv else os.path.join(ROOT, 'ci-plan.json')
    cfg = json.load(open(os.path.join(ROOT, 'ValcoSetup', 'apps.json'), encoding='utf-8-sig'))
    prev_tag, prev = previous_release()
    prev_apps = {a['id']: a for a in (prev or {}).get('apps', [])}
    prev_commit = (prev or {}).get('commit')
    esr = latest_esr()

    plan = {'commit': git('rev-parse', 'HEAD'), 'previousTag': prev_tag, 'previousCommit': prev_commit, 'apps': []}
    for a in cfg['apps']:
        dirs = a.get('sourceDirs') or []
        h = tree_hash(dirs)
        old = prev_apps.get(a['id'])
        reason = None
        if force:
            reason = 'full rebuild requested'
        elif old is None:
            reason = 'not in the previous release'
        elif not old.get('sourceHash'):
            reason = 'first automated build'
        elif old.get('sourceHash') != h:
            reason = 'source changed'
        elif a['id'] == 'net' and esr and old.get('engine') and old['engine'] != esr:
            reason = f'new Gecko engine {esr} (was {old["engine"]})'
        entry = {
            'id': a['id'], 'name': a['name'], 'runner': a.get('runner', 'windows'), 'sourceHash': h,
            'build': reason is not None, 'reason': reason or 'unchanged',
            'changes': [tidy(c, a['name']) for c in changes_since(prev_commit, dirs)] if reason else [],
        }
        if a['id'] == 'net' and reason and reason.startswith('new Gecko'):
            entry['changes'] = [f'Updated the Gecko engine to Firefox ESR {esr} (security fixes)'] + entry['changes']
        plan['apps'].append(entry)

    setup_hash = tree_hash(cfg.get('setupSourceDirs') or ['ValcoSetup'])
    plan['setupSourceHash'] = setup_hash
    plan['setupChanged'] = force or prev is None or (prev or {}).get('setupSourceHash') != setup_hash
    plan['setupChanges'] = [tidy(c, 'Valco Office Setup') for c in changes_since(prev_commit, cfg.get('setupSourceDirs') or ['ValcoSetup'])] if plan['setupChanged'] else []
    plan['release'] = plan['setupChanged'] or any(x['build'] for x in plan['apps'])

    with open(out_path, 'w', encoding='utf-8') as f:
        json.dump(plan, f, indent=2)

    for x in plan['apps']:
        print(f'  {x["name"]:<20} {"BUILD" if x["build"] else "reuse":<6} {x["reason"]}')
    print(f'  {"Valco Office Setup":<20} {"BUILD" if plan["setupChanged"] else "same":<6}')
    print('Release needed' if plan['release'] else 'Nothing changed since the last release')

    gh_out = os.environ.get('GITHUB_OUTPUT')
    if gh_out:
        with open(gh_out, 'a') as f:
            f.write(f'release={"true" if plan["release"] else "false"}\n')
            f.write(f'build_linux={"true" if any(x["build"] and x["runner"] == "linux" for x in plan["apps"]) else "false"}\n')
            f.write(f'windows_apps={" ".join(x["id"] for x in plan["apps"] if x["build"] and x["runner"] == "windows")}\n')
            f.write(f'reuse_apps={" ".join(x["id"] for x in plan["apps"] if not x["build"])}\n')
            f.write(f'previous_tag={prev_tag or ""}\n')


if __name__ == '__main__':
    main()
