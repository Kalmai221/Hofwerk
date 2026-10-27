#!/usr/bin/env python3
"""Makes every icon of the project from the two logo files in this folder.

  python branding/make-icons.py

Needs Pillow (pip install pillow) and Microsoft Edge or Chrome, which draws the SVG. It writes:
  src/Hofwerk.WinUI/Assets/Hofwerk.png (256) and Hofwerk.ico
  src/Hofwerk.Linux/Assets/hofwerk.png (256) and Hofwerk.ico
  src/Hofwerk.Installer/Assets/Hofwerk.ico
  branding/preview.png (the sizes next to each other, to check how it reads when small)
Sizes up to 32 pixels use hofwerk-logo-small.svg, which has fewer and bolder parts.
"""
import os
import shutil
import subprocess
import sys
import tempfile

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
ICO_SIZES = [16, 24, 32, 48, 64, 128, 256]
BROWSERS = [
    r'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe',
    r'C:\Program Files\Microsoft\Edge\Application\msedge.exe',
    r'C:\Program Files\Google\Chrome\Application\chrome.exe',
    '/usr/bin/google-chrome', '/usr/bin/chromium', '/usr/bin/chromium-browser',
]


def render(svg_path, size, out_path):
    browser = next((b for b in BROWSERS if os.path.exists(b)), shutil.which('msedge') or shutil.which('chromium'))
    if not browser:
        sys.exit('Install Edge or Chrome: it is what draws the SVG.')
    with open(svg_path, encoding='utf-8') as f:
        svg = f.read()
    page = os.path.join(tempfile.mkdtemp(), 'logo.html')
    with open(page, 'w', encoding='utf-8') as f:
        f.write('<!doctype html><meta charset="utf-8"><style>html,body{margin:0;background:transparent}svg{display:block;width:%dpx;height:%dpx}</style>%s' % (size, size, svg))
    subprocess.run([browser, '--headless=new', '--disable-gpu', '--hide-scrollbars', '--default-background-color=00000000',
                    '--window-size=%d,%d' % (size, size), '--screenshot=' + out_path, 'file:///' + page.replace('\\', '/')],
                   check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def main():
    work = tempfile.mkdtemp()
    big = os.path.join(work, 'big.png')
    small = os.path.join(work, 'small.png')
    render(os.path.join(HERE, 'hofwerk-logo.svg'), 1024, big)
    render(os.path.join(HERE, 'hofwerk-logo-small.svg'), 256, small)
    big_img = Image.open(big).convert('RGBA')
    small_img = Image.open(small).convert('RGBA')

    frames = {}
    for size in ICO_SIZES:
        source = small_img if size <= 32 else big_img
        frames[size] = source.resize((size, size), Image.LANCZOS)

    # An ICO file keeps one picture per size, so each size can have its own drawing.
    icons = [
        os.path.join(ROOT, 'src', 'Hofwerk.WinUI', 'Assets', 'Hofwerk.ico'),
        os.path.join(ROOT, 'src', 'Hofwerk.Linux', 'Assets', 'Hofwerk.ico'),
        os.path.join(ROOT, 'src', 'Hofwerk.Installer', 'Assets', 'Hofwerk.ico'),
    ]
    for path in icons:
        frames[256].save(path, format='ICO', sizes=[(s, s) for s in ICO_SIZES], append_images=[frames[s] for s in ICO_SIZES[:-1]])
        print('wrote', os.path.relpath(path, ROOT))

    for path in (os.path.join(ROOT, 'src', 'Hofwerk.WinUI', 'Assets', 'Hofwerk.png'), os.path.join(ROOT, 'src', 'Hofwerk.Linux', 'Assets', 'hofwerk.png')):
        frames[256].save(path)
        print('wrote', os.path.relpath(path, ROOT))

    # a picture to look at: every size, shown at its real size and enlarged
    sheet = Image.new('RGBA', (620, 330), (32, 36, 44, 255))
    x = 20
    for size in ICO_SIZES[:-1]:
        sheet.paste(frames[size], (x, 20), frames[size])
        zoom = frames[size].resize((size * 3, size * 3), Image.NEAREST)
        if size <= 64:
            sheet.paste(zoom, (x, 120), zoom)
        x += size + 20
    sheet.paste(frames[256].resize((160, 160), Image.LANCZOS), (440, 20), frames[256].resize((160, 160), Image.LANCZOS))
    sheet.save(os.path.join(HERE, 'preview.png'))
    shutil.rmtree(work, ignore_errors=True)


if __name__ == '__main__':
    main()
