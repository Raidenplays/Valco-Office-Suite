# Valco Spreadsheets app icon: the Valco glossy blue disc with a white worksheet (grid, green header, bars).
# Writes assets/ValcoSpreadsheets.ico and assets/ValcoSpreadsheets-256.png.  Needs cairosvg + Pillow.
import cairosvg, io, os
from PIL import Image

A = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'assets') + os.sep

ICON = '''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="{s}" height="{s}">
<defs>
 <radialGradient id="disc" cx="50%" cy="72%" r="72%" fx="50%" fy="88%">
  <stop offset="0" stop-color="#8be8ff"/><stop offset=".5" stop-color="#2b8ad8"/><stop offset="1" stop-color="#0c3f8a"/></radialGradient>
 <linearGradient id="shine" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fff" stop-opacity=".85"/><stop offset="1" stop-color="#fff" stop-opacity=".08"/></linearGradient>
 <linearGradient id="sheet" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ffffff"/><stop offset="1" stop-color="#d6e2f1"/></linearGradient>
 <linearGradient id="head" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#b9f08c"/><stop offset="1" stop-color="#3f9d2a"/></linearGradient>
 <linearGradient id="bar" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ffe58a"/><stop offset="1" stop-color="#e09e00"/></linearGradient>
 <linearGradient id="swoosh" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#b6ff7a" stop-opacity="0"/><stop offset=".5" stop-color="#b6ff7a" stop-opacity=".9"/><stop offset="1" stop-color="#3fd06b" stop-opacity="0"/></linearGradient>
</defs>
<circle cx="128" cy="128" r="124" fill="url(#disc)" stroke="#0a3470" stroke-width="5"/>
<path d="M22 178 C 90 146, 170 208, 240 146" stroke="url(#swoosh)" stroke-width="14" fill="none" stroke-linecap="round"/>
<path d="M14 116 C 18 52 68 8 128 8 C 188 8 238 52 242 116 C 196 94 60 94 14 116 Z" fill="url(#shine)"/>
<g transform="translate(50 52)">
 <rect x="0" y="0" width="156" height="152" rx="6" fill="url(#sheet)" stroke="#3d6797" stroke-width="5"/>
 <rect x="2.5" y="2.5" width="151" height="30" rx="4" fill="url(#head)"/>
 <path d="M0 32 H156 M0 62 H156 M0 92 H156 M0 122 H156 M52 0 V152 M104 0 V152" stroke="#7d9cc4" stroke-width="4"/>
 <rect x="0" y="0" width="156" height="152" rx="6" fill="none" stroke="#3d6797" stroke-width="5"/>
</g>
<g transform="translate(118 112)">
 <rect x="0" y="54" width="22" height="46" fill="url(#bar)" stroke="#9a6a00" stroke-width="4"/>
 <rect x="30" y="26" width="22" height="74" fill="#5fb3ff" stroke="#1a57b5" stroke-width="4"/>
 <rect x="60" y="0" width="22" height="100" fill="url(#head)" stroke="#2a7a18" stroke-width="4"/>
</g>
</svg>'''

def png(svg, w):
    return Image.open(io.BytesIO(cairosvg.svg2png(bytestring=svg.encode(), output_width=w, output_height=w))).convert('RGBA')

if __name__ == '__main__':
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    imgs = [png(ICON.format(s=s), s) for s in sizes]
    imgs[-1].save(A + 'ValcoSpreadsheets.ico', sizes=[(s, s) for s in sizes], append_images=imgs[:-1])
    imgs[-1].save(A + 'ValcoSpreadsheets-256.png')
    print('ok')
