"""Generates the Valco Weave icon and the Vista "aurora" installer art.
Same recipe as Valco Express / Valco Code (tools/gen_assets.py there): a glossy
blue orb with a white glyph, and the blue/green aurora wizard bitmaps."""
import base64, io, os, sys
import cairosvg
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
A = os.path.join(ROOT, 'packaging', 'assets') + '/'
os.makedirs(A, exist_ok=True)

ICON = '''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="{s}" height="{s}">
<defs>
 <radialGradient id="disc" cx="50%" cy="72%" r="72%" fx="50%" fy="88%">
  <stop offset="0" stop-color="#8be8ff"/><stop offset=".5" stop-color="#2b8ad8"/><stop offset="1" stop-color="#0c3f8a"/></radialGradient>
 <linearGradient id="shine" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fff" stop-opacity=".85"/><stop offset="1" stop-color="#fff" stop-opacity=".08"/></linearGradient>
 <linearGradient id="page" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ffffff"/><stop offset="1" stop-color="#cfdcee"/></linearGradient>
 <linearGradient id="bar" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#7cc1f2"/><stop offset=".5" stop-color="#3d95dd"/><stop offset=".5" stop-color="#2a7fcc"/><stop offset="1" stop-color="#4aa0e4"/></linearGradient>
 <linearGradient id="pencil" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#fff2a8"/><stop offset=".55" stop-color="#f7c534"/><stop offset="1" stop-color="#e09e00"/></linearGradient>
 <linearGradient id="swoosh" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#b6ff7a" stop-opacity="0"/><stop offset=".5" stop-color="#b6ff7a" stop-opacity=".9"/><stop offset="1" stop-color="#3fd06b" stop-opacity="0"/></linearGradient>
</defs>
<circle cx="128" cy="128" r="124" fill="url(#disc)" stroke="#0a3470" stroke-width="5"/>
<path d="M22 178 C 90 146, 170 208, 240 146" stroke="url(#swoosh)" stroke-width="14" fill="none" stroke-linecap="round"/>
<path d="M14 116 C 18 52 68 8 128 8 C 188 8 238 52 242 116 C 196 94 60 94 14 116 Z" fill="url(#shine)"/>
<!-- web page -->
<g transform="translate(52 56)">
 <rect x="0" y="0" width="134" height="124" rx="8" fill="url(#page)" stroke="#3d6797" stroke-width="5"/>
 <path d="M2.5 26 V10 a7.5 7.5 0 0 1 7.5 -7.5 H124 a7.5 7.5 0 0 1 7.5 7.5 V26 Z" fill="url(#bar)"/>
 <line x1="2.5" y1="26" x2="131.5" y2="26" stroke="#3d6797" stroke-width="4"/>
 <circle cx="15" cy="14.5" r="4.5" fill="#fff" opacity=".9"/><circle cx="29" cy="14.5" r="4.5" fill="#fff" opacity=".7"/>
 <rect x="16" y="40" width="58" height="34" rx="3" fill="#bfe0fa" stroke="#6c97c4" stroke-width="3"/>
 <path d="M19 71 L38 52 L50 63 L58 56 L72 71 Z" fill="#6fcf5a"/>
 <path d="M86 44 H118 M86 56 H118 M86 68 H108 M16 90 H118 M16 102 H96" stroke="#8ea9c9" stroke-width="6" stroke-linecap="round"/>
</g>
<!-- pencil -->
<g transform="translate(174 104) rotate(45)">
 <rect x="-13" y="0" width="26" height="92" rx="3" fill="url(#pencil)" stroke="#9a6a00" stroke-width="4.5"/>
 <rect x="-13" y="-16" width="26" height="18" rx="4" fill="#e77c8e" stroke="#9a3446" stroke-width="4.5"/>
 <path d="M-13 92 L0 124 L13 92 Z" fill="#f6dfb8" stroke="#9a6a00" stroke-width="4.5" stroke-linejoin="round"/>
 <path d="M-4.5 113 L0 124 L4.5 113 Z" fill="#3a3a3a"/>
</g>
</svg>'''

def png(svg, w, h=None):
    return Image.open(io.BytesIO(cairosvg.svg2png(bytestring=svg.encode(), output_width=w, output_height=h or w))).convert('RGBA')

sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
imgs = [png(ICON.format(s=s), s) for s in sizes]
imgs[-1].save(A + 'ValcoWeave.ico', sizes=[(s, s) for s in sizes], append_images=imgs[:-1])
imgs[-1].save(A + 'ValcoWeave-256.png')
png(ICON.format(s=512), 512).save(A + 'ValcoWeave-512.png')

AURORA = '''
 <linearGradient id="bg" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#0d3c78"/><stop offset=".55" stop-color="#1b6dbd"/><stop offset="1" stop-color="#2aa6c4"/></linearGradient>
 <linearGradient id="w1" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#9dff6a" stop-opacity="0"/><stop offset=".45" stop-color="#9dff6a" stop-opacity=".75"/><stop offset="1" stop-color="#2fd0a8" stop-opacity="0"/></linearGradient>
 <linearGradient id="w2" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#ffffff" stop-opacity="0"/><stop offset=".5" stop-color="#ffffff" stop-opacity=".55"/><stop offset="1" stop-color="#ffffff" stop-opacity="0"/></linearGradient>
 <filter id="b"><feGaussianBlur stdDeviation="3"/></filter>
'''
icon_b64 = base64.b64encode(cairosvg.svg2png(bytestring=ICON.format(s=256).encode(), output_width=256, output_height=256)).decode()

DIALOG = f'''<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="493" height="312">
<defs>{AURORA}
 <linearGradient id="topglow" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fff" stop-opacity=".14"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></linearGradient>
 <linearGradient id="white" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ffffff"/><stop offset=".8" stop-color="#ffffff"/><stop offset="1" stop-color="#eef3f9"/></linearGradient>
</defs>
<rect width="493" height="312" fill="url(#white)"/>
<rect width="164" height="312" fill="url(#bg)"/>
<clipPath id="panel"><rect width="164" height="312"/></clipPath>
<g clip-path="url(#panel)">
<path d="M-20 205 C 40 160, 110 260, 190 185" stroke="url(#w1)" stroke-width="22" fill="none" filter="url(#b)"/>
<path d="M-20 222 C 50 190, 100 262, 190 205" stroke="url(#w2)" stroke-width="4" fill="none"/>
<path d="M-20 250 C 60 215, 120 290, 190 232" stroke="url(#w1)" stroke-width="10" fill="none" opacity=".6" filter="url(#b)"/>
<rect width="164" height="170" fill="url(#topglow)"/>
</g>
<image x="27" y="34" width="110" height="110" xlink:href="data:image/png;base64,{icon_b64}"/>
<text x="82" y="282" text-anchor="middle" font-family="Open Sans" font-weight="300" font-size="19" fill="#ffffff">Valco Weave</text>
<rect x="164" width="1" height="312" fill="#9fb6cf"/>
</svg>'''
BANNER = f'''<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="493" height="58">
<defs>{AURORA}
 <linearGradient id="fade" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#fff"/><stop offset=".55" stop-color="#fff"/><stop offset=".78" stop-color="#fff" stop-opacity="0"/></linearGradient>
</defs>
<rect width="493" height="58" fill="url(#bg)"/>
<path d="M250 50 C 320 20, 390 70, 500 25" stroke="url(#w1)" stroke-width="14" fill="none" filter="url(#b)"/>
<path d="M250 40 C 330 18, 400 60, 500 18" stroke="url(#w2)" stroke-width="3" fill="none"/>
<rect width="493" height="58" fill="url(#fade)"/>
<image x="437" y="5" width="48" height="48" xlink:href="data:image/png;base64,{icon_b64}"/>
<rect y="57" width="493" height="1" fill="#9fb6cf"/>
</svg>'''
png(DIALOG, 493, 312).convert('RGB').save(A + 'dialog.bmp')
png(BANNER, 493, 58).convert('RGB').save(A + 'banner.bmp')
print('assets ok ->', A)
