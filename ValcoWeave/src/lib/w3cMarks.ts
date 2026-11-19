// Inline W3C issues in the code view (like the VS Code "W3C Web Validator" extension):
// squiggles with hover messages that disappear once you edit the line they're on.
import { StateEffect, StateField, type Text } from '@codemirror/state';
import { Decoration, EditorView, hoverTooltip, type DecorationSet } from '@codemirror/view';
import type { W3cMessage } from './w3c';

export interface W3cIssue {
  from: number;
  to: number;
  type: W3cMessage['type'];
  message: string;
  line: number;
  lineText: string;
}

export const setW3cIssues = StateEffect.define<W3cIssue[]>();
export const clearW3cIssues = StateEffect.define<null>();

export function issuesFromMessages(doc: Text, messages: W3cMessage[]): W3cIssue[] {
  return messages.map((m) => {
    const ln = doc.line(Math.max(1, Math.min(m.line, doc.lines)));
    let from = ln.from + Math.max(0, (m.endLine === m.line || !m.endLine ? m.col : 1) - 1);
    let to = m.endLine === m.line && m.endCol ? ln.from + m.endCol : ln.to;
    from = Math.min(Math.max(ln.from, from), ln.to);
    to = Math.min(Math.max(from, to), ln.to);
    if (to <= from) {
      // underline the whole (trimmed) line when W3C gives no usable span
      const lead = ln.text.length - ln.text.trimStart().length;
      from = ln.from + lead;
      to = ln.to;
    }
    if (to <= from) to = Math.min(doc.length, from + 1);
    return { from, to, type: m.type, message: m.message, line: ln.number, lineText: ln.text };
  });
}

const marks = {
  error: Decoration.mark({ class: 'wc-w3c wc-w3c-error' }),
  warning: Decoration.mark({ class: 'wc-w3c wc-w3c-warning' }),
  info: Decoration.mark({ class: 'wc-w3c wc-w3c-info' }),
};

const field = StateField.define<W3cIssue[]>({
  create: () => [],
  update(issues, tr) {
    for (const e of tr.effects) {
      if (e.is(setW3cIssues)) return e.value;
      if (e.is(clearW3cIssues)) return [];
    }
    if (!tr.docChanged || !issues.length) return issues;
    // follow edits; drop an issue once its line has been changed (auto-clear)
    const out: W3cIssue[] = [];
    for (const i of issues) {
      const from = tr.changes.mapPos(i.from, 1);
      const to = tr.changes.mapPos(i.to, -1);
      const ln = tr.state.doc.lineAt(from);
      if (ln.text !== i.lineText) continue;
      out.push({ ...i, from, to: Math.max(from, to), line: ln.number });
    }
    return out;
  },
  provide: (f) =>
    EditorView.decorations.from(f, (issues): DecorationSet =>
      Decoration.set(
        issues.filter((i) => i.to > i.from).map((i) => marks[i.type].range(i.from, i.to)),
        true,
      ),
    ),
});

const hover = hoverTooltip((view, pos) => {
  const hits = view.state.field(field).filter((i) => pos >= i.from && pos <= i.to);
  if (!hits.length) return null;
  return {
    pos: Math.min(...hits.map((h) => h.from)),
    end: Math.max(...hits.map((h) => h.to)),
    above: true,
    create: () => {
      const dom = document.createElement('div');
      dom.className = 'wc-w3c-tip';
      for (const h of hits) {
        const row = document.createElement('div');
        row.className = 'wc-w3c-row ' + h.type;
        const tag = document.createElement('b');
        tag.textContent = h.type === 'error' ? 'W3C error' : h.type === 'warning' ? 'W3C warning' : 'W3C info';
        row.append(tag, document.createTextNode(' ' + h.message));
        dom.appendChild(row);
      }
      return { dom };
    },
  };
});

const theme = EditorView.baseTheme({
  '.wc-w3c': { backgroundRepeat: 'repeat-x', backgroundPosition: 'left bottom', paddingBottom: '1px' },
  '.wc-w3c-error': { backgroundImage: `url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='6' height='3'%3E%3Cpath d='m0 3 l2 -2 l1 0 l2 2 l1 0' stroke='%23e5484d' fill='none' stroke-width='.9'/%3E%3C/svg%3E")` },
  '.wc-w3c-warning': { backgroundImage: `url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='6' height='3'%3E%3Cpath d='m0 3 l2 -2 l1 0 l2 2 l1 0' stroke='%23e2a33b' fill='none' stroke-width='.9'/%3E%3C/svg%3E")` },
  '.wc-w3c-info': { backgroundImage: `url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='6' height='3'%3E%3Cpath d='m0 3 l2 -2 l1 0 l2 2 l1 0' stroke='%233b8eea' fill='none' stroke-width='.9'/%3E%3C/svg%3E")` },
  '.wc-w3c-tip': { padding: '4px 8px', maxWidth: '520px', fontFamily: 'system-ui, sans-serif', fontSize: '12.5px' },
  '.wc-w3c-row': { padding: '2px 0' },
  '.wc-w3c-row.error b': { color: '#e5484d' },
  '.wc-w3c-row.warning b': { color: '#e2a33b' },
  '.wc-w3c-row.info b': { color: '#3b8eea' },
});

export function w3cMarks() {
  return [field, hover, theme];
}

export function w3cIssueCount(view: EditorView | null): number {
  return view ? view.state.field(field, false)?.length ?? 0 : 0;
}
