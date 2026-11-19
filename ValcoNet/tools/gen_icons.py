#!/usr/bin/env python3
"""Generates ValcoNet's SVG icon set (original artwork, Vista glossy style) and the
app logo. Run from the repo root: python3 tools/gen_icons.py
Writes app/valconet/skin/icons/*.svg and assets/ValcoNet.svg."""
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "app", "valconet", "skin", "icons")
os.makedirs(OUT, exist_ok=True)


def svg(w, h, body, defs=""):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}">'
            f'<defs>{defs}</defs>{body}</svg>\n')


def lin(id_, stops, x1=0, y1=0, x2=0, y2=1):
    s = "".join(f'<stop offset="{o}" stop-color="{c}"{"" if a is None else f" stop-opacity=\"{a}\""}/>'
                for o, c, a in [(t + (None,))[:3] for t in stops])
    return f'<linearGradient id="{id_}" x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}">{s}</linearGradient>'


def rad(id_, stops, cx=.5, cy=.5, r=.5, fx=None, fy=None):
    s = "".join(f'<stop offset="{o}" stop-color="{c}"{"" if a is None else f" stop-opacity=\"{a}\""}/>'
                for o, c, a in [(t + (None,))[:3] for t in stops])
    f = f' fx="{fx}" fy="{fy}"' if fx is not None else ""
    return f'<radialGradient id="{id_}" cx="{cx}" cy="{cy}" r="{r}"{f}>{s}</radialGradient>'


ICONS = {}

# --- navigation -----------------------------------------------------------
ARROW = ('<path d="M{a}" fill="#fff" stroke="#123a73" stroke-opacity=".55" stroke-width="1" stroke-linejoin="round"/>')
ICONS["arrow-back"] = svg(16, 16, ARROW.format(a="2.5 8 L8 2.8 L8 5.8 L13.5 5.8 L13.5 10.2 L8 10.2 L8 13.2 Z"))
ICONS["arrow-forward"] = svg(16, 16, ARROW.format(a="13.5 8 L8 2.8 L8 5.8 L2.5 5.8 L2.5 10.2 L8 10.2 L8 13.2 Z"))

ICONS["refresh"] = svg(16, 16, """
<path d="M13.2 6.3A5.6 5.6 0 0 0 3 5.2" fill="none" stroke="url(#g)" stroke-width="2.4" stroke-linecap="round"/>
<path d="M14.6 2.6 L14.3 8 L9.2 6.6 Z" fill="#2f9d24" stroke="#1d6a14" stroke-width=".6" stroke-linejoin="round"/>
<path d="M2.8 9.7A5.6 5.6 0 0 0 13 10.8" fill="none" stroke="url(#g)" stroke-width="2.4" stroke-linecap="round"/>
<path d="M1.4 13.4 L1.7 8 L6.8 9.4 Z" fill="#2f9d24" stroke="#1d6a14" stroke-width=".6" stroke-linejoin="round"/>
""", lin("g", [(0, "#8fe07a"), (1, "#2b8a1f")]))

ICONS["stop"] = svg(16, 16, """
<path d="M3 4.6 4.6 3 8 6.4 11.4 3 13 4.6 9.6 8 13 11.4 11.4 13 8 9.6 4.6 13 3 11.4 6.4 8Z" fill="url(#g)" stroke="#8d1b0d" stroke-width=".8" stroke-linejoin="round"/>
<path d="M4.2 4.4 4.6 4 8 7.3 11.4 4 11.8 4.4 8.4 7.8 Z" fill="#fff" opacity=".55"/>
""", lin("g", [(0, "#ff8a6e"), (.5, "#e2381c"), (1, "#b3200e")]))

ICONS["search"] = svg(16, 16, """
<path d="M9.6 10 L13.6 14" stroke="#3b3b3b" stroke-width="3" stroke-linecap="round"/>
<path d="M9.8 10.2 L13.4 13.8" stroke="#9a9a9a" stroke-width="1" stroke-linecap="round"/>
<circle cx="6.5" cy="6.5" r="4.6" fill="url(#lens)" stroke="#55606e" stroke-width="1.3"/>
<ellipse cx="5.4" cy="4.9" rx="2.4" ry="1.4" fill="#fff" opacity=".8"/>
""", rad("lens", [(0, "#ffffff"), (.6, "#bfe3fb"), (1, "#5ea9e0")], .45, .4, .6))

STAR = "M8 1.2 9.9 5.6 14.7 6 11.1 9.1 12.2 13.8 8 11.3 3.8 13.8 4.9 9.1 1.3 6 6.1 5.6Z"
STAR_DEFS = lin("s", [(0, "#fff6a8"), (.45, "#ffd23a"), (1, "#f09a0a")])
ICONS["fav-star"] = svg(16, 16, f"""
<path d="{STAR}" fill="url(#s)" stroke="#b86a00" stroke-width=".9" stroke-linejoin="round"/>
<path d="M8 2.8 9.2 5.9 12.5 6.3 8.3 7.4Z" fill="#fff" opacity=".6"/>
""", STAR_DEFS)
ICONS["fav-add"] = svg(16, 16, f"""
<g transform="translate(-1 -1) scale(.9)"><path d="{STAR}" fill="url(#s)" stroke="#b86a00" stroke-width="1" stroke-linejoin="round"/></g>
<path d="M8.6 10.6 H11.2 V8 L15.4 12.2 L11.2 16.2 V13.6 H8.6Z" fill="url(#ga)" stroke="#1f6e14" stroke-width=".8" stroke-linejoin="round"/>
""", STAR_DEFS + lin("ga", [(0, "#a6ec84"), (1, "#3a9c25")]))

# --- command bar ------------------------------------------------------------
ICONS["home"] = svg(16, 16, """
<path d="M3 8 V14.5 H13 V8" fill="url(#w)" stroke="#7a6a50" stroke-width=".9"/>
<rect x="6.6" y="10" width="2.8" height="4.5" fill="#3d76c2" stroke="#24518f" stroke-width=".6"/>
<rect x="10.2" y="9.4" width="2" height="2" fill="#bfe1ff" stroke="#6c7f95" stroke-width=".5"/>
<path d="M1 8.6 8 2 15 8.6 13.6 9.8 8 4.6 2.4 9.8Z" fill="url(#r)" stroke="#7d1c0c" stroke-width=".8" stroke-linejoin="round"/>
<rect x="11" y="2.6" width="1.8" height="3.4" fill="#9a4b2c" stroke="#6b2f18" stroke-width=".5"/>
""", lin("w", [(0, "#fffdf4"), (1, "#e8dcc0")]) + lin("r", [(0, "#ff7b5a"), (1, "#c13417")]))

ICONS["feeds"] = svg(16, 16, """
<rect x="1.5" y="1.5" width="13" height="13" rx="2.6" fill="url(#o)" stroke="#b5520b" stroke-width="1"/>
<path d="M2.6 2.6 H13.4 V7 Q8 6 2.6 9Z" fill="#fff" opacity=".28"/>
<circle cx="5" cy="11" r="1.4" fill="#fff"/>
<path d="M4 7.2a4.8 4.8 0 0 1 4.8 4.8" fill="none" stroke="#fff" stroke-width="1.6" stroke-linecap="round"/>
<path d="M4 4.3a7.7 7.7 0 0 1 7.7 7.7" fill="none" stroke="#fff" stroke-width="1.6" stroke-linecap="round"/>
""", lin("o", [(0, "#ffc06a"), (.5, "#f7851d"), (1, "#e2620a")]))

ICONS["feeds-off"] = svg(16, 16, """
<rect x="1.5" y="1.5" width="13" height="13" rx="2.6" fill="url(#o)" stroke="#8b8f96" stroke-width="1"/>
<circle cx="5" cy="11" r="1.4" fill="#fff"/>
<path d="M4 7.2a4.8 4.8 0 0 1 4.8 4.8" fill="none" stroke="#fff" stroke-width="1.6" stroke-linecap="round"/>
<path d="M4 4.3a7.7 7.7 0 0 1 7.7 7.7" fill="none" stroke="#fff" stroke-width="1.6" stroke-linecap="round"/>
""", lin("o", [(0, "#dfe2e6"), (1, "#a7acb4")]))

ICONS["mail"] = svg(16, 16, """
<rect x="1.5" y="3.5" width="13" height="9.5" rx="1" fill="url(#e)" stroke="#5a6f8c" stroke-width=".9"/>
<path d="M1.8 4 8 9 14.2 4" fill="none" stroke="#5a6f8c" stroke-width=".9" stroke-linejoin="round"/>
<path d="M1.8 12.6 6.4 8 M14.2 12.6 9.6 8" stroke="#8ea0b8" stroke-width=".7"/>
""", lin("e", [(0, "#ffffff"), (1, "#dbe6f3")]))

ICONS["print"] = svg(16, 16, """
<rect x="4" y="1.5" width="8" height="5" fill="#fff" stroke="#7c8594" stroke-width=".8"/>
<rect x="1.5" y="6" width="13" height="6" rx="1.4" fill="url(#p)" stroke="#4d5664" stroke-width=".9"/>
<rect x="4" y="10" width="8" height="4.5" fill="#fff" stroke="#7c8594" stroke-width=".8"/>
<path d="M5.4 11.7 H10.6 M5.4 13.1 H9.4" stroke="#7aa0d0" stroke-width=".7"/>
<circle cx="12.4" cy="8" r=".7" fill="#4fd14a"/>
""", lin("p", [(0, "#eef1f5"), (1, "#a4adbb")]))

ICONS["page"] = svg(16, 16, """
<path d="M3 1.5 H10 L13 4.5 V14.5 H3Z" fill="url(#pg)" stroke="#6b7a90" stroke-width=".9" stroke-linejoin="round"/>
<path d="M10 1.5 V4.5 H13" fill="#d6e3f3" stroke="#6b7a90" stroke-width=".8" stroke-linejoin="round"/>
<path d="M5 7 H11 M5 9 H11 M5 11 H9.5" stroke="#4e86c8" stroke-width=".9"/>
""", lin("pg", [(0, "#ffffff"), (1, "#e3ebf5")]))

ICONS["safety"] = svg(16, 16, """
<path d="M8 1.3 L14 3.4 V7.6 C14 11.2 11.4 13.6 8 14.8 C4.6 13.6 2 11.2 2 7.6 V3.4Z" fill="url(#sh)" stroke="#1f4f8d" stroke-width=".9" stroke-linejoin="round"/>
<path d="M8 1.3 V14.8 C11.4 13.6 14 11.2 14 7.6 V3.4Z" fill="#3dae2b" opacity=".85"/>
<path d="M2.6 3.8 8 2 8 8 2.6 8Z" fill="#fff" opacity=".35"/>
<path d="M5 8 7.2 10.2 11.2 5.8" fill="none" stroke="#fff" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/>
""", lin("sh", [(0, "#7fbdf2"), (1, "#2a6cc0")]))

GEAR = ("M7 1h2l.4 1.8 1.5.6 1.6-1 1.4 1.4-1 1.6.6 1.5L15 7v2l-1.8.4-.6 1.5 1 1.6-1.4 1.4-1.6-1-1.5.6L9 15H7"
        "l-.4-1.8-1.5-.6-1.6 1-1.4-1.4 1-1.6-.6-1.5L1 9V7l1.8-.4.6-1.5-1-1.6 1.4-1.4 1.6 1 1.5-.6Z")
ICONS["tools"] = svg(16, 16, f"""
<path d="{GEAR}" fill="url(#gr)" stroke="#47505c" stroke-width=".8" stroke-linejoin="round"/>
<circle cx="8" cy="8" r="2.3" fill="#fff" stroke="#47505c" stroke-width=".8"/>
""", lin("gr", [(0, "#e9edf2"), (1, "#8d97a6")]))

ICONS["help"] = svg(16, 16, """
<circle cx="8" cy="8" r="6.6" fill="url(#h)" stroke="#16468e" stroke-width=".9"/>
<ellipse cx="8" cy="5" rx="4.6" ry="2.8" fill="#fff" opacity=".35"/>
<path d="M5.8 6.2a2.2 2.2 0 1 1 3.3 1.9c-.7.4-1.1.8-1.1 1.7" fill="none" stroke="#fff" stroke-width="1.7" stroke-linecap="round"/>
<circle cx="8" cy="11.7" r="1" fill="#fff"/>
""", rad("h", [(0, "#8fd0ff"), (.6, "#2f86dd"), (1, "#1a54a8")], .4, .3, .8))

ICONS["quicktabs"] = svg(16, 16, """
<g fill="url(#q)" stroke="#45648f" stroke-width=".8">
<rect x="1.5" y="2.5" width="5.5" height="4.5" rx=".6"/><rect x="9" y="2.5" width="5.5" height="4.5" rx=".6"/>
<rect x="1.5" y="9" width="5.5" height="4.5" rx=".6"/><rect x="9" y="9" width="5.5" height="4.5" rx=".6"/></g>
""", lin("q", [(0, "#ffffff"), (1, "#bcd3ee")]))

ICONS["tablist"] = svg(16, 16, """
<rect x="1.5" y="2.5" width="9" height="11" rx=".8" fill="url(#q)" stroke="#45648f" stroke-width=".8"/>
<path d="M3.3 5.3 H8.7 M3.3 7.8 H8.7 M3.3 10.3 H7" stroke="#4e86c8" stroke-width="1"/>
<path d="M11.5 7 H15.5 L13.5 10Z" fill="#33445c"/>
""", lin("q", [(0, "#ffffff"), (1, "#d5e3f4")]))

ICONS["dropdown"] = svg(7, 4, '<path d="M0 0H7L3.5 4Z" fill="#1c2a3b"/>')
ICONS["dropdown-light"] = svg(7, 4, '<path d="M0 0H7L3.5 4Z" fill="#fff"/>')

ICONS["lock"] = svg(16, 16, """
<path d="M5 7V5a3 3 0 0 1 6 0v2" fill="none" stroke="#7f8a98" stroke-width="1.8"/>
<rect x="3" y="7" width="10" height="7.5" rx="1.2" fill="url(#l)" stroke="#9a6a07" stroke-width=".9"/>
<rect x="7.3" y="9.3" width="1.4" height="3" rx=".6" fill="#7a5205"/>
""", lin("l", [(0, "#ffe68a"), (1, "#e3a417")]))

ICONS["zone-internet"] = svg(16, 16, """
<circle cx="8" cy="8" r="6.6" fill="url(#oc)" stroke="#1d5a9c" stroke-width=".8"/>
<path d="M4 4.6c1.4-.6 2.6.2 2.4 1.3-.2 1.2-1.8 1.1-1.6 2.6.2 1.3 1.6 1.4 1.4 2.8-.2 1-1.3 1.6-2.2 1.2A6.6 6.6 0 0 1 4 4.6Z
M9.2 2.2c1 .6.8 1.6 1.8 2s2.4-.4 2.8.6c.3.8-.6 1.6-1.6 1.8-1.1.2-1.4 1.4-.6 2.2.9.9.4 2.5-.6 3.2A6.6 6.6 0 0 0 9.2 2.2Z" fill="url(#ld)"/>
<ellipse cx="7" cy="4.6" rx="4.2" ry="2.4" fill="#fff" opacity=".35"/>
""", rad("oc", [(0, "#a9dcff"), (1, "#2a7fd2")], .4, .35, .7) + lin("ld", [(0, "#8fe07a"), (1, "#2d8a24")]))

ICONS["zone-computer"] = svg(16, 16, """
<rect x="1.5" y="2" width="13" height="9" rx="1" fill="#3a4250" stroke="#262c35" stroke-width=".8"/>
<rect x="2.6" y="3.1" width="10.8" height="6.8" fill="url(#sc)"/>
<path d="M6 11 H10 L10.6 13 H5.4Z" fill="#9aa3b0"/><rect x="4" y="13" width="8" height="1.5" rx=".6" fill="#6c7584"/>
""", lin("sc", [(0, "#8cd2ff"), (1, "#2c76c8")]))

ICONS["zone-intranet"] = svg(16, 16, """
<rect x="2" y="2" width="12" height="9" rx="1" fill="url(#bd)" stroke="#5b6a80" stroke-width=".8"/>
<path d="M4 5h2M4 8h2M7 5h2M7 8h2M10 5h2M10 8h2" stroke="#4e86c8" stroke-width="1.2"/>
<path d="M8 11v2M3 13.5h10" stroke="#5b6a80" stroke-width="1.2"/>
""", lin("bd", [(0, "#ffffff"), (1, "#cfdcec")]))

ICONS["page-generic"] = svg(16, 16, """
<path d="M3 1.5 H10 L13 4.5 V14.5 H3Z" fill="#fff" stroke="#7d8a9c" stroke-width=".9" stroke-linejoin="round"/>
<path d="M10 1.5 V4.5 H13" fill="#e5ecf5" stroke="#7d8a9c" stroke-width=".8"/>
<circle cx="8" cy="9.4" r="3" fill="url(#oc)" stroke="#1d5a9c" stroke-width=".6"/>
""", rad("oc", [(0, "#a9dcff"), (1, "#2a7fd2")], .4, .35, .7))

ICONS["pin"] = svg(16, 16, """
<path d="M14.5 6 H8 V2.5 L1.5 8 L8 13.5 V10 H14.5Z" fill="url(#ga)" stroke="#1f6e14" stroke-width=".9" stroke-linejoin="round"/>
<path d="M2.8 8 8 3.6V6.4H14V8Z" fill="#fff" opacity=".35"/>
""", lin("ga", [(0, "#a6ec84"), (1, "#3a9c25")]))

ICONS["close-small"] = svg(10, 10, '<path d="M1.5 1.5 8.5 8.5M8.5 1.5 1.5 8.5" stroke="#5b6574" stroke-width="1.6" stroke-linecap="round"/>')
ICONS["close-small-hot"] = svg(10, 10, '<path d="M1.5 1.5 8.5 8.5M8.5 1.5 1.5 8.5" stroke="#fff" stroke-width="1.6" stroke-linecap="round"/>')

# Caption glyphs: white with a dark keyline, like Vista's.
CAP = '<path d="{d}" fill="#fff" stroke="#1b2a3f" stroke-opacity=".75" stroke-width="1" paint-order="stroke" stroke-linejoin="round"/>'
ICONS["cap-min"] = svg(12, 10, CAP.format(d="M2 6.5 H10 V8.5 H2Z"))
ICONS["cap-max"] = svg(12, 10, '<path d="M2 1.5 H10 V8.5 H2Z M3.6 3.6 V7 H8.4 V3.6Z" fill="#fff" fill-rule="evenodd" stroke="#1b2a3f" stroke-opacity=".75" stroke-width="1" paint-order="stroke"/>')
ICONS["cap-restore"] = svg(12, 10, '<path d="M4 1 H11 V6 H9.6 V2.4 H4Z M1 3.6 H8 V9 H1Z M2.4 5.2 V7.6 H6.6 V5.2Z" fill="#fff" fill-rule="evenodd" stroke="#1b2a3f" stroke-opacity=".75" stroke-width=".9" paint-order="stroke"/>')
ICONS["cap-close"] = svg(12, 10, CAP.format(d="M2.2 1 4.4 1 6 2.9 7.6 1 9.8 1 7.2 5 9.8 9 7.6 9 6 7.1 4.4 9 2.2 9 4.8 5Z"))

ICONS["folder"] = svg(16, 16, """
<path d="M1.5 3.5 H6 L7.5 5 H14.5 V13.5 H1.5Z" fill="#e9b949" stroke="#a87a17" stroke-width=".8" stroke-linejoin="round"/>
<path d="M1.5 6.5 H14.5 V13.5 H1.5Z" fill="url(#f)" stroke="#a87a17" stroke-width=".8"/>
""", lin("f", [(0, "#ffe9a6"), (1, "#f2c24e")]))
ICONS["folder-open"] = svg(16, 16, """
<path d="M1.5 3.5 H6 L7.5 5 H13.5 V7 H1.5Z" fill="#e9b949" stroke="#a87a17" stroke-width=".8"/>
<path d="M1.5 13.5 3.4 7 H15.4 L13.5 13.5Z" fill="url(#f)" stroke="#a87a17" stroke-width=".8" stroke-linejoin="round"/>
""", lin("f", [(0, "#fff1c4"), (1, "#f2c24e")]))
ICONS["history-day"] = svg(16, 16, """
<rect x="2" y="3" width="12" height="11" rx="1" fill="#fff" stroke="#5b6a80" stroke-width=".8"/>
<rect x="2" y="3" width="12" height="3" fill="url(#c)" stroke="#5b6a80" stroke-width=".8"/>
<path d="M5 2v2.5M11 2v2.5" stroke="#3a4250" stroke-width="1.2" stroke-linecap="round"/>
<path d="M4.5 8.5h2M7.5 8.5h2M10.5 8.5h1M4.5 11h2M7.5 11h2" stroke="#7aa0d0" stroke-width="1.1"/>
""", lin("c", [(0, "#ff8a6e"), (1, "#d23d22")]))
ICONS["new-tab"] = svg(16, 16, """
<path d="M3.5 2.5 H9.5 L12.5 5.5 V13.5 H3.5Z" fill="#fff" stroke="#7d8a9c" stroke-width=".8" stroke-linejoin="round"/>
<path d="M9.5 2.5 V5.5 H12.5" fill="#e5ecf5" stroke="#7d8a9c" stroke-width=".7"/>
""")
ICONS["inprivate"] = svg(16, 16, """
<rect x="1" y="3" width="14" height="10" rx="2" fill="url(#ip)" stroke="#123f80" stroke-width=".8"/>
<path d="M3.4 7.4c1.2-1.4 3-1.4 4.6 0 1.6-1.4 3.4-1.4 4.6 0-.4 1.8-1.8 2.4-3 2.2-.8-.1-1.2-.7-1.6-.7s-.8.6-1.6.7c-1.2.2-2.6-.4-3-2.2Z" fill="#fff"/>
""", lin("ip", [(0, "#6fb0f2"), (1, "#1d5fb4")]))
ICONS["popup-blocked"] = svg(16, 16, """
<rect x="1.5" y="2.5" width="11" height="9" rx="1" fill="#fff" stroke="#6b7a90" stroke-width=".8"/>
<rect x="1.5" y="2.5" width="11" height="2.4" fill="#4e86c8"/>
<circle cx="11.5" cy="11.5" r="3.8" fill="#d8351b" stroke="#8d1b0d" stroke-width=".7"/>
<path d="M9.6 11.5 H13.4" stroke="#fff" stroke-width="1.6"/>
""")
ICONS["info"] = svg(16, 16, """
<circle cx="8" cy="8" r="6.6" fill="url(#h)" stroke="#16468e" stroke-width=".9"/>
<rect x="7.1" y="6.6" width="1.8" height="5" rx=".6" fill="#fff"/><circle cx="8" cy="4.6" r="1.1" fill="#fff"/>
""", rad("h", [(0, "#8fd0ff"), (.6, "#2f86dd"), (1, "#1a54a8")], .4, .3, .8))

ICONS["menu-check"] = svg(16, 16, '<path d="M4 8.2 6.8 11 12.2 4.8" fill="none" stroke="#0c2d61" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/>')
ICONS["menu-radio"] = svg(16, 16, '<circle cx="8" cy="8" r="3" fill="url(#rb)" stroke="#0c2d61" stroke-width=".8"/>', rad("rb", [(0, "#9fd0ff"), (1, "#14457f")], .4, .35, .7))

# --- Error-page illustrations (replace the engine's mascot art) ------------------------
ILLU_ERROR = svg(200, 200, """
<circle cx="100" cy="96" r="80" fill="url(#oc)" stroke="#1d5a9c" stroke-width="3"/>
<path d="M50 60c18-8 34 3 31 17-3 16-24 14-21 34 3 17 21 18 18 36-3 13-17 21-29 16A80 80 0 0 1 50 60Z
M114 18c13 8 10 21 23 26s31-5 36 8c4 10-8 21-21 23-14 3-18 18-8 28 12 12 5 32-8 41A80 80 0 0 0 114 18Z" fill="url(#ld)"/>
<ellipse cx="88" cy="50" rx="54" ry="28" fill="#fff" opacity=".35"/>
<circle cx="148" cy="146" r="34" fill="url(#rd)" stroke="#fff" stroke-width="5"/>
<path d="M134 132 162 160M162 132 134 160" stroke="#fff" stroke-width="9" stroke-linecap="round"/>
""", rad("oc", [(0, "#a9dcff"), (1, "#2a7fd2")], .4, .35, .7) + lin("ld", [(0, "#8fe07a"), (1, "#2d8a24")])
   + lin("rd", [(0, "#ff7f63"), (1, "#c22a10")]))
ILLU_SECURITY = svg(200, 200, """
<path d="M100 14 172 40V92C172 140 140 170 100 186 60 170 28 140 28 92V40Z" fill="url(#sh)" stroke="#7d1c0c" stroke-width="4" stroke-linejoin="round"/>
<path d="M36 46 100 22V100H36Z" fill="#fff" opacity=".28"/>
<path d="M76 70 124 118M124 70 76 118" stroke="#fff" stroke-width="15" stroke-linecap="round"/>
""", lin("sh", [(0, "#ff8a6e"), (.5, "#e2381c"), (1, "#a91c08")]))

# --- App logo: glossy suite disc, green swoosh, white globe glyph ------------------------
LOGO = svg(256, 256, """
<circle cx="128" cy="128" r="124" fill="url(#disc)" stroke="#0a3470" stroke-width="6"/>
<path d="M14 168 C70 120 170 112 246 150 C236 196 198 232 150 244 C108 210 60 186 14 168Z" fill="url(#sw)" opacity=".92"/>
<g fill="none" stroke="#fff" stroke-width="11" stroke-linecap="round" opacity=".97">
  <circle cx="128" cy="124" r="66"/>
  <ellipse cx="128" cy="124" rx="30" ry="66"/>
  <path d="M62 124 H194 M72 90 H184 M72 158 H184"/>
</g>
<ellipse cx="128" cy="64" rx="98" ry="54" fill="url(#shine)"/>
""", rad("disc", [(0, "#8be8ff"), (.55, "#2b8ad8"), (1, "#0c3f8a")], .42, .32, .78)
    + lin("sw", [(0, "#c7f59a"), (.5, "#58c22c"), (1, "#1f7d12")], 0, 0, 1, 1)
    + lin("shine", [(0, "#fff", .85), (1, "#fff", .05)]))

if __name__ == "__main__":
    for name, data in ICONS.items():
        with open(os.path.join(OUT, name + ".svg"), "w") as f:
            f.write(data)
    with open(os.path.join(OUT, "illu-error.svg"), "w") as f:
        f.write(ILLU_ERROR)
    with open(os.path.join(OUT, "illu-security.svg"), "w") as f:
        f.write(ILLU_SECURITY)
    with open(os.path.join(OUT, "valconet.svg"), "w") as f:
        f.write(LOGO)
    os.makedirs(os.path.join(ROOT, "assets"), exist_ok=True)
    with open(os.path.join(ROOT, "assets", "ValcoNet.svg"), "w") as f:
        f.write(LOGO)
    print(f"wrote {len(ICONS) + 1} icons")
