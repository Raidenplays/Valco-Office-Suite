// W3C validation: turns the Nu HTML Checker and CSS Validator JSON into one list.
export interface W3cMessage {
  type: 'error' | 'warning' | 'info';
  line: number;
  col: number;
  endLine?: number;
  endCol?: number;
  message: string;
  extract?: string;
  hiliteStart?: number;
  hiliteLength?: number;
}

export interface W3cResult {
  messages: W3cMessage[];
  checkedAt: number;
}

/* eslint-disable @typescript-eslint/no-explicit-any */
export function parseNu(json: string): W3cMessage[] {
  const d = JSON.parse(json);
  return (d.messages ?? [])
    .filter((m: any) => m.type !== 'non-document-error' || m.message)
    .map((m: any): W3cMessage => ({
      type: m.type === 'error' || m.type === 'non-document-error' ? 'error' : m.subType === 'warning' ? 'warning' : 'info',
      line: m.lastLine ?? m.firstLine ?? 1,
      col: m.firstColumn ?? m.lastColumn ?? 1,
      endLine: m.lastLine,
      endCol: m.lastColumn,
      message: String(m.message ?? '').trim(),
      extract: m.extract,
      hiliteStart: m.hiliteStart,
      hiliteLength: m.hiliteLength,
    }));
}

export function parseCss(json: string): W3cMessage[] {
  const d = JSON.parse(json).cssvalidation ?? {};
  const out: W3cMessage[] = [];
  for (const e of d.errors ?? []) {
    out.push({
      type: 'error',
      line: e.line ?? 1,
      col: 1,
      message: [e.context ? `“${String(e.context).trim()}”: ` : '', String(e.message ?? e.type ?? 'Error').trim()].join(''),
    });
  }
  for (const w of d.warnings ?? []) {
    out.push({ type: (w.level ?? 0) > 1 ? 'info' : 'warning', line: w.line ?? 1, col: 1, message: String(w.message ?? w.type ?? 'Warning').trim() });
  }
  return out.sort((a, b) => a.line - b.line);
}

/** Validate with W3C. `call` does the HTTP request (Rust on desktop, fetch in the browser). */
export async function validate(kind: 'html' | 'css', text: string, call: (kind: 'html' | 'css', text: string) => Promise<string>): Promise<W3cResult> {
  // HTML fragments (no doctype) are wrapped in a minimal page first, then line numbers are shifted back.
  let offset = 0;
  let body = text;
  if (kind === 'html' && !/^\s*<!doctype html/i.test(text)) {
    let inner = text.trim();
    let header = '<!DOCTYPE html>\n<html lang="en">\n<head>\n<title>Partial HTML Document</title>\n</head>\n<body>\n';
    if (/^<body[\s>]/i.test(inner) && /<\/body>\s*$/i.test(inner)) header = '<!DOCTYPE html>\n<html lang="en">\n<head>\n<title>Partial HTML Document</title>\n</head>\n';
    else inner = text;
    offset = header.split('\n').length - 1;
    body = header + inner + '\n' + (header.endsWith('<body>\n') ? '</body>\n' : '') + '</html>\n';
  }
  const raw = await call(kind, body);
  let messages = parseNu(raw);
  if (offset) {
    const lines = text.split('\n').length;
    messages = messages
      .map((m) => ({ ...m, line: m.line - offset, endLine: m.endLine !== undefined ? m.endLine - offset : undefined }))
      .filter((m) => m.line >= 1 && m.line <= lines);
  }
  return { messages, checkedAt: Date.now() };
}

/** Browser fallback: the Nu checker allows cross-origin requests; the CSS validator usually doesn't. */
export async function fetchW3c(kind: 'html' | 'css', text: string): Promise<string> {
  try {
    const r = await fetch('https://validator.w3.org/nu/?out=json', { method: 'POST', headers: { 'Content-Type': `text/${kind}; charset=utf-8` }, body: text });
    if (r.status === 503) throw new Error('The W3C service is unavailable right now. Please try again later.');
    if (r.status === 429) throw new Error('The W3C validator is busy (too many requests). Wait a minute and try again.');
    if (!r.ok) throw new Error('The W3C validator answered with error ' + r.status);
    return await r.text();
  } catch (e) {
    if (e instanceof TypeError) throw new Error("Couldn't reach the W3C validator from here. The desktop app can always reach it; in the browser it needs an internet connection and may be blocked by the page's security settings.");
    throw e;
  }
}
