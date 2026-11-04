"""Contact sheets and before/after strips from Validation/Stages captures.

  python contact_sheet.py                       # contact-env-1..3.png from env_scene-<stg>.png
  python contact_sheet.py --wide                # contact-wide-1..3.png from <stg>.png
  python contact_sheet.py --compare before-sublevels stg01 stg08a   # <folder>/x vs current, both views
"""
import argparse
from PIL import Image, ImageDraw
from _common import PROJECT

V = PROJECT / 'Validation/Stages'


def tile(path, w, h, label=None):
    im = Image.open(path).convert('RGB').resize((w, h))
    if label:
        d = ImageDraw.Draw(im)
        d.rectangle((0, 0, 7 * len(label) + 6, 16), fill=(0, 0, 0))
        d.text((3, 2), label, fill=(255, 255, 0))
    return im


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--wide', action='store_true')
    ap.add_argument('--compare', nargs='+')
    a = ap.parse_args()
    if a.compare:
        folder, names = a.compare[0], a.compare[1:]
        rows = [(n, pre) for n in names for pre in ('env_scene-', '')]
        sheet = Image.new('RGB', (1280, 360 * len(rows)))
        for i, (n, pre) in enumerate(rows):
            sheet.paste(tile(V / folder / f'{pre}{n}.png', 640, 360, 'before ' + n), (0, i * 360))
            sheet.paste(tile(V / f'{pre}{n}.png', 640, 360, 'after ' + n), (640, i * 360))
        out = V / f"compare-{'-'.join(names)}.png"
        sheet.save(out)
        print(out)
        return
    names = (PROJECT / 'Assets/_Project/Resources/MuseumStages/StageNames.txt').read_text().split()
    pre, kind = ('', 'wide') if a.wide else ('env_scene-', 'env')
    for part in range(0, len(names), 12):
        chunk = names[part:part + 12]
        sheet = Image.new('RGB', (1280, (len(chunk) + 2) // 3 * 240))
        for i, n in enumerate(chunk):
            sheet.paste(tile(V / f'{pre}{n}.png', 426, 240, n), ((i % 3) * 427, (i // 3) * 240))
        out = V / f'contact-{kind}-{part // 12 + 1}.png'
        sheet.save(out)
        print(out)


if __name__ == '__main__':
    main()
