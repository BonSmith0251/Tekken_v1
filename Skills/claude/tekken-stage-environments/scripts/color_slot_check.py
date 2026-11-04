"""Flag colour maps that are probably not colour: packed masks (empty red channel, data in G/B),
tangent-space normal maps (red/green near 128 with blue full or empty) and flat images.

  python color_slot_check.py            # every material in stages.json
  python color_slot_check.py stg01      # only materials stg01 draws

An explicit "parameter" source can still be really coloured (MI_PoolSticker is a cyan sticker);
judge those by name. Guessed sources (marked <<) are the usual culprits.
"""
import sys
import numpy as np
from PIL import Image
from _common import catalog, stage, slots, TEXTURES


def classify(path):
    a = np.asarray(Image.open(path).convert('RGB').reduce(8)).astype(float)
    mean = a.reshape(-1, 3).mean(0)
    if a[..., 0].max() < 4 and a[..., 1:].max() > 32:
        return 'packed mask (red empty)', mean
    if abs(mean[0] - 128) < 20 and abs(mean[1] - 128) < 25 and (mean[2] > 200 or mean[2] < 5):
        return 'normal map', mean
    if a.std() < 3:
        return 'flat', mean
    return '', mean


def main():
    cat = catalog()
    mats = {m['key']: m for m in cat['materials']}
    keys = set(mats)
    if len(sys.argv) > 1:
        meshes = {m['key']: m for m in cat['meshes']}
        keys = {s for n in stage(cat, sys.argv[1])['nodes'] if n['mesh'] for s in slots(n, meshes)}
    seen = {}
    for key in sorted(keys):
        m = mats.get(key)
        if not m or not m['base']:
            continue
        if m['base'] not in seen:
            try:
                seen[m['base']] = classify(TEXTURES / f"{m['base']}.png")
            except OSError:
                continue
        why, mean = seen[m['base']]
        guessed = '' if m['baseSource'].startswith('parameter') else ' <<'
        # Solid black/white fills are deliberate on named parameters (_ENV_black, glass); only a
        # guessed flat texture is suspicious.
        if why and not (why == 'flat' and not guessed):
            print(f"{m['path'].rsplit('/', 1)[-1][:40]:40} {m['base'][:38]:38} {why:24} "
                  f"mean={mean.round()} {m['baseSource'][:30]}{guessed}")


if __name__ == '__main__':
    main()
