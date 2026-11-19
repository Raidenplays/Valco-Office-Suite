"""Valco Office suite mark: three glassy petals (blue, green, amber) fanning into a V.
Outputs (in assets/):
  ValcoOffice-mark.png  transparent mark, used inside the Valco button (orb)
  ValcoOffice.ico       glass badge icon for the suite as a whole
  ValcoOffice-256.png   badge preview
"""
import cairosvg, io, os
from PIL import Image

A = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'assets') + os.sep

DEFS = '''
 <linearGradient id="pb" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#9fd8ff"/><stop offset=".45" stop-color="#2f8be0"/><stop offset="1" stop-color="#0b3f94"/></linearGradient>
 <linearGradient id="pg" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#d4ff9a"/><stop offset=".5" stop-color="#5cc83a"/><stop offset="1" stop-color="#1d7a1f"/></linearGradient>
 <linearGradient id="pa" x1="1" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ffe9a0"/><stop offset=".45" stop-color="#ffb21e"/><stop offset="1" stop-color="#c76400"/></linearGradient>
 <linearGradient id="gl" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fff" stop-opacity=".85"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></linearGradient>
 <filter id="ds" x="-20%" y="-20%" width="140%" height="140%"><feGaussianBlur in="SourceAlpha" stdDeviation="4"/><feOffset dy="4"/><feComponentTransfer><feFuncA type="linear" slope=".45"/></feComponentTransfer><feMerge><feMergeNode/><feMergeNode in="SourceGraphic"/></feMerge></filter>
'''

# Petal: a leaf shape pointing from the base (128,214) upward; rotated for each colour.
PETAL = 'M128 214 C 78 172, 82 84, 128 30 C 174 84, 178 172, 128 214 Z'
SHINE = 'M128 50 C 108 84, 106 124, 112 150 C 120 120, 130 92, 140 62 Z'

def petal(rot, fill, stroke):
    return (f'<g transform="rotate({rot} 128 214)">'
            f'<path d="{PETAL}" fill="url(#{fill})" stroke="{stroke}" stroke-width="5" stroke-linejoin="round" opacity=".96"/>'
            f'<path d="{SHINE}" fill="url(#gl)"/></g>')

MARK = (petal(-30, 'pb', '#0a3478') + petal(30, 'pa', '#8a4600') + petal(0, 'pg', '#145a16')
        + '<circle cx="128" cy="210" r="13" fill="#ffffff" stroke="#3a5c8a" stroke-width="4"/>')

def mark_svg(s):
    return f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 6 256 236" width="{s}" height="{s}"><defs>{DEFS}</defs><g filter="url(#ds)">{MARK}</g></svg>'''

def badge_svg(s):
    return f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="{s}" height="{s}"><defs>{DEFS}
 <radialGradient id="disc" cx="50%" cy="70%" r="75%" fx="50%" fy="90%"><stop offset="0" stop-color="#ffffff"/><stop offset=".6" stop-color="#dfe9f6"/><stop offset="1" stop-color="#8aa6cc"/></radialGradient>
 <linearGradient id="top" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fff" stop-opacity=".9"/><stop offset="1" stop-color="#fff" stop-opacity=".05"/></linearGradient></defs>
<circle cx="128" cy="128" r="122" fill="url(#disc)" stroke="#3b5a86" stroke-width="6"/>
<g transform="translate(128 134) scale(.78) translate(-128 -124)"><g filter="url(#ds)">{MARK}</g></g>
<path d="M18 112 C 22 54 70 10 128 10 C 186 10 234 54 238 112 C 190 92 66 92 18 112 Z" fill="url(#top)"/>
</svg>'''

def png(svg, w):
    return Image.open(io.BytesIO(cairosvg.svg2png(bytestring=svg.encode(), output_width=w, output_height=w))).convert('RGBA')

if __name__ == '__main__':
    png(mark_svg(128), 128).save(A + 'ValcoOffice-mark.png')
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    imgs = [png(badge_svg(s), s) for s in sizes]
    imgs[-1].save(A + 'ValcoOffice.ico', sizes=[(s, s) for s in sizes], append_images=imgs[:-1])
    imgs[-1].save(A + 'ValcoOffice-256.png')
    print('ok')
