#!/usr/bin/env python3
"""Builds ValcoNet's binary branding from assets/ValcoNet.svg:
  assets/ValcoNet.ico              (16-256, launcher / shortcuts / engine exe)
  assets/ValcoNet-256.png
  assets/dialog.bmp, banner.bmp    (WiX UI art, suite spec)
  app/valconet/content/branding/icon*.png
  app/chrome-icons/main-window.ico, default.ico (Gecko window icons)
Needs rsvg-convert and Pillow."""
import io, os, subprocess
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SVG = os.path.join(ROOT, "assets", "ValcoNet.svg")


def render(size):
    png = subprocess.run(["rsvg-convert", "-w", str(size), "-h", str(size), SVG], check=True, capture_output=True).stdout
    return Image.open(io.BytesIO(png)).convert("RGBA")


def ico(path, sizes=(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)):
    imgs = [render(s) for s in sizes]
    imgs[-1].save(path, format="ICO", sizes=[(s, s) for s in sizes], append_images=imgs[:-1])


def font(size, weight=300):
    for f in ["/usr/share/fonts/truetype/google-fonts/ofl/opensans/OpenSans[wdth,wght].ttf",
              "/usr/share/fonts/truetype/dejavu/DejaVuSans-ExtraLight.ttf"]:
        if os.path.exists(f):
            ft = ImageFont.truetype(f, size)
            try:
                ft.set_variation_by_axes([100, weight])
            except Exception:
                pass
            return ft
    return ImageFont.load_default()


def aurora(w, h):
    """Vertical #0d3c78 -> #1b6dbd -> #2aa6c4 with green/white swooshes."""
    img = Image.new("RGB", (w, h))
    px = img.load()
    stops = [(0, (0x0d, 0x3c, 0x78)), (.55, (0x1b, 0x6d, 0xbd)), (1, (0x2a, 0xa6, 0xc4))]
    for y in range(h):
        t = y / max(1, h - 1)
        for i in range(len(stops) - 1):
            if stops[i][0] <= t <= stops[i + 1][0]:
                a, b = stops[i], stops[i + 1]
                k = (t - a[0]) / (b[0] - a[0])
                c = tuple(int(a[1][j] + (b[1][j] - a[1][j]) * k) for j in range(3))
                break
        for x in range(w):
            px[x, y] = c
    sw = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(sw)
    d.ellipse([-w * .6, h * .62, w * 1.5, h * 1.35], fill=(90, 200, 60, 150))
    d.ellipse([-w * .4, h * .70, w * 1.7, h * 1.45], fill=(30, 125, 20, 120))
    d.arc([-w * .5, h * .45, w * 1.6, h * 1.2], 200, 330, fill=(255, 255, 255, 110), width=3)
    sw = sw.filter(ImageFilter.GaussianBlur(2))
    img = img.convert("RGBA")
    img.alpha_composite(sw)
    return img


def main():
    os.makedirs(os.path.join(ROOT, "assets"), exist_ok=True)
    os.makedirs(os.path.join(ROOT, "app", "chrome-icons"), exist_ok=True)
    ico(os.path.join(ROOT, "assets", "ValcoNet.ico"))
    ico(os.path.join(ROOT, "app", "chrome-icons", "main-window.ico"))
    ico(os.path.join(ROOT, "app", "chrome-icons", "default.ico"))
    render(256).save(os.path.join(ROOT, "assets", "ValcoNet-256.png"))
    for s in (16, 32, 48, 64, 128):
        render(s).save(os.path.join(ROOT, "app", "valconet", "content", "branding", f"icon{s}.png"))

    # dialog.bmp 493x312: aurora panel on the left 164px, white on the right.
    dlg = Image.new("RGB", (493, 312), "white")
    panel = aurora(164, 312)
    icon = render(110)
    panel.alpha_composite(icon, (27, 70))
    d = ImageDraw.Draw(panel)
    f = font(25)
    tw = d.textlength("ValcoNet", font=f)
    d.text(((164 - tw) / 2, 192), "ValcoNet", font=f, fill="white")
    dlg.paste(panel.convert("RGB"), (0, 0))
    dlg.save(os.path.join(ROOT, "assets", "dialog.bmp"))

    # banner.bmp 493x58: white fading into aurora on the right, 48px icon at right.
    ban = Image.new("RGBA", (493, 58), "white")
    au = aurora(493, 58)
    mask = Image.linear_gradient("L").rotate(90, expand=True).resize((493, 58))
    mask = mask.point(lambda v: max(0, min(255, int((v - 110) * 2.2))))
    ban.paste(au, (0, 0), mask)
    ban.alpha_composite(render(48), (493 - 56, 5))
    ban.convert("RGB").save(os.path.join(ROOT, "assets", "banner.bmp"))
    print("branding written")


if __name__ == "__main__":
    main()
