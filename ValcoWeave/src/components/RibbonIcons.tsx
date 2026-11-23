// Vista-style ribbon icons drawn in SVG (same palette as the WPF suite icons in ValcoCommon/SuiteIcons.xaml:
// glossy blue arrows, gold folders, white pages with blue lines, green "go" and red "stop").
import { useEffect, useId, useLayoutEffect, useRef, type CSSProperties, type ReactNode } from 'react';
import { onThemeChange, themeIcon } from '../lib/iconTheme';

const BLUE = ['#92ceff', '#2b79d8', '#1a57b5'];
const GREEN = ['#c2f59b', '#46b62c', '#2a8a18'];
const RED = ['#ffa594', '#e5331e', '#b4150a'];
const GOLD = ['#fff2a8', '#f7c534', '#e09e00'];

/** Picks an icon for a command by its label. */
export function iconFor(label: string): string {
  const l = label.toLowerCase();
  const rules: [RegExp, string][] = [
    [/^new|create sample|initiali[sz]e/, 'new'], [/open site|open demo|recent|folder/, 'open'], [/save all/, 'saveall'], [/^save/, 'save'],
    [/close tab|close/, 'close'], [/preview|live/, 'live'], [/zip|export/, 'zip'], [/^undo/, 'undo'], [/^redo/, 'redo'],
    [/parent|move element up|move up/, 'up'], [/move element down|move down/, 'down'], [/duplicate/, 'copy'], [/delete|reset demo/, 'delete'],
    [/find|search/, 'find'], [/format/, 'format'], [/go to line/, 'goto'], [/go to file|palette|command/, 'palette'], [/suggest/, 'suggest'],
    [/^code$/, 'code'], [/^split|split vert|split hori/, 'split'], [/^design$/, 'design'], [/wrap/, 'wrap'], [/minimap/, 'minimap'],
    [/theme|scheme|light\/dark/, 'theme'], [/window size|width/, 'window'], [/refresh|update/, 'refresh'],
    [/^text$|paragraph|preformatted/, 'para'], [/heading/, 'heading'], [/^html$|head$/, 'html'], [/layout/, 'layout'], [/media|image|picture/, 'media'], [/form/, 'form'],
    [/insert panel/, 'insert'], [/bold/, 'bold'], [/italic/, 'italic'], [/underline/, 'underline'], [/link/, 'link'],
    [/unordered|bullet/, 'ul'], [/ordered|number/, 'ol'], [/css/, 'css'], [/site setup|setup/, 'site'], [/publish/, 'publish'],
    [/git/, 'git'], [/validate|w3c/, 'validate'], [/about|keyboard|shortcut|help/, 'help'], [/exit/, 'exit'], [/option|setting/, 'options'],
  ];
  for (const [re, name] of rules) if (re.test(l)) return name;
  return 'blank';
}

function Grad({ id, c, vertical = true }: { id: string; c: string[]; vertical?: boolean }) {
  return (
    <linearGradient id={id} x1="0" y1="0" x2={vertical ? '0' : '1'} y2={vertical ? '1' : '0'}>
      <stop offset="0" stopColor={c[0]} />
      <stop offset="0.55" stopColor={c[1]} />
      <stop offset="1" stopColor={c[2]} />
    </linearGradient>
  );
}

const Page = ({ g }: { g: string }) => (
  <>
    <path d="M7 3 H20 L26 9 V29 H7 Z" fill={`url(#${g}p)`} stroke="#6f86a6" strokeWidth="1" />
    <path d="M20 3 V9 H26" fill="#e3ebf5" stroke="#6f86a6" strokeWidth="1" />
  </>
);
const Lines = ({ y = 12, n = 5, x1 = 10, x2 = 23 }: { y?: number; n?: number; x1?: number; x2?: number }) => (
  <g stroke="#5b8bd0" strokeWidth="1.5">{Array.from({ length: n }, (_, i) => <line key={i} x1={x1} x2={i % 2 ? x2 - 4 : x2} y1={y + i * 3.2} y2={y + i * 3.2} />)}</g>
);
const Arrow = ({ g, d }: { g: string; d: string }) => <path d={d} fill={`url(#${g}b)`} stroke="#1a4f9a" strokeWidth="1" strokeLinejoin="round" />;
const Folder = ({ g }: { g: string }) => (
  <>
    <path d="M3 8 H12 L14 10 H28 V27 H3 Z" fill="#dda83a" stroke="#a87a1c" />
    <path d="M3 13 H29 L27 27 H3 Z" fill={`url(#${g}f)`} stroke="#a87a1c" />
  </>
);
const Disk = ({ g, x = 4, y = 4, s = 24 }: { g: string; x?: number; y?: number; s?: number }) => (
  <g transform={`translate(${x} ${y}) scale(${s / 24})`}>
    <rect x="0.5" y="0.5" width="23" height="23" rx="2" fill={`url(#${g}b)`} stroke="#1a4f9a" />
    <rect x="5" y="1" width="14" height="9" fill="#fff" stroke="#8aa6c8" />
    <rect x="6" y="14" width="12" height="9" fill="#dfe8f3" stroke="#8aa6c8" />
    <rect x="14" y="2.5" width="3" height="6" fill="#5b8bd0" />
  </g>
);
const Glass = ({ x = 4, y = 4 }: { x?: number; y?: number }) => (
  <g transform={`translate(${x} ${y})`}>
    <line x1="13" y1="13" x2="22" y2="22" stroke="#4d4d4d" strokeWidth="4" strokeLinecap="round" />
    <circle cx="9" cy="9" r="7.5" fill="rgba(190,226,248,.75)" stroke="#3b5a86" strokeWidth="2" />
    <ellipse cx="7" cy="6.5" rx="3.5" ry="2" fill="#fff" opacity=".8" />
  </g>
);
const Glyph = ({ t, style }: { t: string; style?: CSSProperties }) => (
  <text x="16" y="24" textAnchor="middle" fontFamily="Times New Roman, serif" fontWeight="bold" fontSize="22" style={{ fill: 'var(--rb-glyph, #1e2a36)', ...style }}>{t}</text>
);

/** themed: recolour for the current theme (ribbon groups and the Quick Access Toolbar); menus keep the original colours. */
export function RibbonIcon({ name, size = 16, themed = false }: { name: string; size?: number; themed?: boolean }) {
  const g = useId().replace(/:/g, '');
  const ref = useRef<SVGSVGElement>(null);
  useLayoutEffect(() => { if (themed) themeIcon(ref.current); });
  useEffect(() => (themed ? onThemeChange(() => themeIcon(ref.current)) : undefined), [themed]);
  let body: ReactNode;
  switch (name) {
    case 'new': body = <><Page g={g} /><path d="M24 2 l1.4 3.2 3.4.3-2.6 2.2.8 3.3-3-1.8-3 1.8.8-3.3-2.6-2.2 3.4-.3z" fill="#ffd34d" stroke="#c48a00" strokeWidth=".6" /></>; break;
    case 'open': body = <><Folder g={g} /><Arrow g={g} d="M17 4 h7 v-2 l5 5 -5 5 v-2 h-7 z" /></>; break;
    case 'save': body = <Disk g={g} />; break;
    case 'saveall': body = <><Disk g={g} x={8} y={2} s={20} /><Disk g={g} x={2} y={9} s={20} /></>; break;
    case 'close': case 'exit': body = <><Page g={g} /><path d="M10 14.5 L12.5 12 16 15.5 19.5 12 22 14.5 18.5 18 22 21.5 19.5 24 16 20.5 12.5 24 10 21.5 13.5 18Z" fill={`url(#${g}r)`} stroke="#8a1006" strokeWidth=".8" /></>; break;
    case 'live': body = <><circle cx="16" cy="16" r="13" fill={`url(#${g}b)`} stroke="#1a4f9a" /><path d="M6 13 q5-6 9-3 t5 6 q-4 1-6 5 -6-1-8-8z M18 6 q5 1 8 6 -4 0-5-2z" fill={`url(#${g}g)`} /><ellipse cx="12" cy="9" rx="7" ry="4" fill="#fff" opacity=".35" /></>; break;
    case 'zip': body = <><Folder g={g} /><rect x="14" y="13" width="4" height="14" fill="#7a7a7a" /><path d="M14 15h4M14 18h4M14 21h4M14 24h4" stroke="#ddd" /></>; break;
    case 'undo': body = <Arrow g={g} d="M13 5 L3 13 13 21 V16 Q22 15 27 25 27 11 13 10 Z" />; break;
    case 'redo': body = <Arrow g={g} d="M19 5 L29 13 19 21 V16 Q10 15 5 25 5 11 19 10 Z" />; break;
    case 'up': body = <Arrow g={g} d="M16 3 L28 16 H20 V29 H12 V16 H4 Z" />; break;
    case 'down': body = <Arrow g={g} d="M16 29 L28 16 H20 V3 H12 V16 H4 Z" />; break;
    case 'copy': body = <><g transform="translate(-3 -1) scale(.85)"><Page g={g} /></g><g transform="translate(6 5) scale(.85)"><Page g={g} /><Lines y={12} n={4} /></g></>; break;
    case 'delete': body = <path d="M5 9 L9 5 16 12 23 5 27 9 20 16 27 23 23 27 16 20 9 27 5 23 12 16Z" fill={`url(#${g}r)`} stroke="#8a1006" />; break;
    case 'find': body = <><Page g={g} /><Lines /><Glass x={8} y={8} /></>; break;
    case 'format': body = <><Page g={g} /><Lines /><path d="M16 26 l10-10 3 3 -10 10 h-3z" fill={`url(#${g}y)`} stroke="#a07000" /></>; break;
    case 'goto': body = <><Page g={g} /><Lines /><Arrow g={g} d="M2 17 h8 v-4 l7 6 -7 6 v-4 h-8z" /></>; break;
    case 'palette': body = <><rect x="2" y="5" width="28" height="22" rx="2" fill="#20262e" stroke="#5a6470" /><path d="M6 11 l4 3 -4 3" stroke="#7fe08a" strokeWidth="2" fill="none" /><path d="M12 18 h7" stroke="#e6e6e6" strokeWidth="2" /><rect x="2" y="5" width="28" height="4" fill={`url(#${g}b)`} /></>; break;
    case 'suggest': body = <><rect x="3" y="4" width="26" height="24" rx="2" fill="#fff" stroke="#6f86a6" /><rect x="5" y="9" width="22" height="5" fill="#cfe3fb" /><Lines y={18} n={3} x1={7} x2={25} /><path d="M7 7 h10" stroke="#1e2a36" strokeWidth="1.5" /></>; break;
    case 'code': body = <><Page g={g} /><path d="M12 13 l-4 4 4 4 M20 13 l4 4 -4 4 M17.5 11 l-3 12" stroke="#2b79d8" strokeWidth="2" fill="none" /></>; break;
    case 'split': body = <><rect x="3" y="5" width="26" height="22" fill="#fff" stroke="#6f86a6" /><rect x="3" y="5" width="13" height="22" fill="#eef4fb" stroke="#6f86a6" /><path d="M6 11 l-1.5 2 1.5 2 M10 11 l1.5 2 -1.5 2" stroke="#2b79d8" strokeWidth="1.4" fill="none" /><rect x="19" y="9" width="7" height="5" fill={`url(#${g}g)`} /><Lines y={17} n={3} x1={19} x2={27} /></>; break;
    case 'design': body = <><rect x="3" y="5" width="26" height="22" fill="#fff" stroke="#6f86a6" /><rect x="3" y="5" width="26" height="4" fill={`url(#${g}b)`} /><rect x="6" y="12" width="9" height="7" fill={`url(#${g}g)`} /><Lines y={13} n={4} x1={18} x2={27} /><Lines y={22} n={2} x1={6} x2={27} /></>; break;
    case 'wrap': body = <><Lines y={8} n={2} x1={4} x2={28} /><Arrow g={g} d="M4 17 H22 Q28 17 28 21.5 28 26 22 26 H16 V29 L10 24 16 19 V22 H22 Q24 22 24 21.5 24 21 22 21 H4Z" /></>; break;
    case 'minimap': body = <><rect x="4" y="3" width="24" height="26" fill="#fff" stroke="#6f86a6" /><Lines y={6} n={7} x1={6} x2={20} /><rect x="21" y="8" width="5" height="9" fill="rgba(43,121,216,.35)" stroke="#2b79d8" /></>; break;
    case 'theme': body = <><circle cx="16" cy="16" r="12" fill="#f4f4f4" stroke="#7a7a7a" /><circle cx="11" cy="11" r="3" fill={`url(#${g}b)`} /><circle cx="19" cy="9.5" r="3" fill={`url(#${g}r)`} /><circle cx="22" cy="17" r="3" fill={`url(#${g}y)`} /><circle cx="11" cy="20" r="3" fill={`url(#${g}g)`} /></>; break;
    case 'window': body = <><rect x="2" y="6" width="28" height="20" rx="2" fill="#fff" stroke="#3b5a86" /><rect x="2" y="6" width="28" height="5" fill={`url(#${g}b)`} /><path d="M8 19 h16 M8 19 l3-3 M8 19 l3 3 M24 19 l-3-3 M24 19 l-3 3" stroke="#1e2a36" strokeWidth="1.5" fill="none" /></>; break;
    case 'refresh': body = <><Arrow g={g} d="M16 4 A12 12 0 0 1 27 12 L30 11 26 19 19 14 23 13 A8 8 0 0 0 9 12 L5 10 A12 12 0 0 1 16 4Z" /><path d="M16 28 A12 12 0 0 1 5 20 L2 21 6 13 13 18 9 19 A8 8 0 0 0 23 20 L27 22 A12 12 0 0 1 16 28Z" fill={`url(#${g}g)`} stroke="#2a7a18" /></>; break;
    case 'para': body = <><Lines y={7} n={7} x1={4} x2={28} /></>; break;
    case 'heading': body = <><Glyph t="H" /><Lines y={27} n={1} x1={6} x2={26} /></>; break;
    case 'html': body = <><Page g={g} /><path d="M13 13 l-4 4 4 4 M19 13 l4 4 -4 4" stroke="#e5331e" strokeWidth="2" fill="none" /></>; break;
    case 'layout': body = <><rect x="3" y="4" width="26" height="24" fill="#fff" stroke="#6f86a6" /><rect x="3" y="4" width="26" height="6" fill={`url(#${g}b)`} /><rect x="5" y="12" width="7" height="14" fill="#dfe8f3" stroke="#8aa6c8" /><Lines y={14} n={4} x1={14} x2={27} /></>; break;
    case 'media': body = <><rect x="3" y="5" width="26" height="22" fill="#fff" stroke="#6f86a6" /><rect x="5" y="7" width="22" height="18" fill={`url(#${g}s)`} /><path d="M5 25 L13 15 19 21 22 18 27 25Z" fill={`url(#${g}g)`} /><circle cx="22" cy="11" r="2.5" fill="#ffe36a" /></>; break;
    case 'form': body = <><rect x="3" y="4" width="26" height="24" fill="#fff" stroke="#6f86a6" /><rect x="6" y="8" width="20" height="5" fill="#fff" stroke="#7a8da8" /><rect x="6" y="16" width="20" height="5" fill="#fff" stroke="#7a8da8" /><rect x="16" y="23" width="10" height="4" rx="1" fill={`url(#${g}b)`} /></>; break;
    case 'insert': body = <><rect x="4" y="4" width="24" height="24" rx="3" fill="#fff" stroke="#6f86a6" /><path d="M16 9 V23 M9 16 H23" stroke={`url(#${g}gh)`} strokeWidth="5" /><path d="M16 9 V23 M9 16 H23" stroke="#2a8a18" strokeWidth="1" opacity=".4" /></>; break;
    case 'bold': body = <Glyph t="B" />; break;
    case 'italic': body = <Glyph t="I" style={{ fontStyle: 'italic' }} />; break;
    case 'underline': body = <><Glyph t="U" /><path d="M8 28 H24" style={{ stroke: 'var(--rb-glyph, #1e2a36)' }} strokeWidth="2" /></>; break;
    case 'link': body = <><circle cx="16" cy="16" r="12" fill={`url(#${g}b)`} stroke="#1a4f9a" /><path d="M9 18 a3 3 0 0 1 0-4 l3-3 a3 3 0 0 1 4 0 M23 14 a3 3 0 0 1 0 4 l-3 3 a3 3 0 0 1 -4 0 M13 19 l6-6" stroke="#fff" strokeWidth="2.2" fill="none" strokeLinecap="round" /></>; break;
    case 'ul': body = <><g fill="#2b79d8">{[7, 15, 23].map((y) => <rect key={y} x="4" y={y - 2} width="4" height="4" />)}</g><Lines y={7} n={1} x1={11} x2={28} /><Lines y={15} n={1} x1={11} x2={28} /><Lines y={23} n={1} x1={11} x2={28} /></>; break;
    case 'ol': body = <><g fontSize="7" fontFamily="Segoe UI" fill="#1e2a36">{[1, 2, 3].map((n, i) => <text key={n} x="3" y={10 + i * 8}>{n}.</text>)}</g><Lines y={7} n={1} x1={11} x2={28} /><Lines y={15} n={1} x1={11} x2={28} /><Lines y={23} n={1} x1={11} x2={28} /></>; break;
    case 'css': body = <><Page g={g} /><text x="16.5" y="23" textAnchor="middle" fontSize="9" fontWeight="bold" fontFamily="Segoe UI" fill="#2b79d8">CSS</text></>; break;
    case 'site': body = <><Folder g={g} /><circle cx="22" cy="21" r="7" fill={`url(#${g}b)`} stroke="#1a4f9a" /><path d="M17 20 q3-3 5-1 t4 2 M20 25 q2-2 5-1" stroke={`url(#${g}g)`} strokeWidth="2" fill="none" /></>; break;
    case 'publish': body = <><circle cx="16" cy="18" r="11" fill={`url(#${g}b)`} stroke="#1a4f9a" /><path d="M9 15 q4-5 8-2 t5 6 q-4 1-6 5 -6-1-7-9z" fill={`url(#${g}g)`} /><path d="M16 1 l7 7 h-4 v7 h-6 v-7 h-4z" fill={`url(#${g}y)`} stroke="#a07000" /></>; break;
    case 'git': body = <><circle cx="9" cy="7" r="3.5" fill={`url(#${g}r)`} stroke="#8a1006" /><circle cx="9" cy="25" r="3.5" fill={`url(#${g}r)`} stroke="#8a1006" /><circle cx="23" cy="10" r="3.5" fill={`url(#${g}r)`} stroke="#8a1006" /><path d="M9 10.5 V21.5 M23 13.5 C23 19 9 17 9 21.5" stroke="#5a2a20" strokeWidth="2" fill="none" /></>; break;
    case 'validate': body = <><Page g={g} /><path d="M10 18 l4 4 9-10" stroke={`url(#${g}g)`} strokeWidth="4" fill="none" strokeLinecap="round" strokeLinejoin="round" /></>; break;
    case 'help': body = <><circle cx="16" cy="16" r="13" fill={`url(#${g}b)`} stroke="#1a4f9a" /><text x="16" y="23" textAnchor="middle" fontSize="18" fontWeight="bold" fontFamily="Segoe UI" fill="#fff">?</text><ellipse cx="13" cy="9" rx="7" ry="4" fill="#fff" opacity=".35" /></>; break;
    case 'options': body = <><Page g={g} /><circle cx="16" cy="19" r="6" fill="none" stroke="#5a6470" strokeWidth="3" strokeDasharray="3 2" /><circle cx="16" cy="19" r="3" fill="#8aa6c8" /></>; break;
    default: body = <rect x="8" y="8" width="16" height="16" rx="2" fill="#f4f7fb" stroke="#9aaecb" />;
  }
  return (
    <svg ref={ref} width={size} height={size} viewBox="0 0 32 32" className="rb-ico" aria-hidden>
      <defs>
        <linearGradient id={g + 'p'} x1="0" y1="0" x2="0" y2="1"><stop offset="0" stopColor="#ffffff" /><stop offset="1" stopColor="#dfe8f3" /></linearGradient>
        <Grad id={g + 'b'} c={BLUE} />
        <Grad id={g + 'g'} c={GREEN} />
        <Grad id={g + 'gh'} c={GREEN} vertical={false} />
        <Grad id={g + 'r'} c={RED} />
        <Grad id={g + 'y'} c={GOLD} />
        <linearGradient id={g + 'f'} x1="0" y1="0" x2="0" y2="1"><stop offset="0" stopColor="#fff0bc" /><stop offset="1" stopColor="#f5cb62" /></linearGradient>
        <linearGradient id={g + 's'} x1="0" y1="0" x2="0" y2="1"><stop offset="0" stopColor="#7cc4f5" /><stop offset="1" stopColor="#d9f0ff" /></linearGradient>
      </defs>
      {body}
    </svg>
  );
}
