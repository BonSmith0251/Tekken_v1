"""List every material a stage draws, with the texture it got and how it was chosen.

  python stage_materials.py stg31            # all materials, guessed ones marked <<
  python stage_materials.py stg31 --suspect  # only guessed, untextured, transparent or overlay-picked

The "src" column is the exporter's baseSource: "parameter X" is an explicit colour parameter
(trust it); "parent material sampler", "unsuffixed texture", "albedo texture" are guesses
(check them); empty means no colour map (see note).
"""
import argparse, collections, re
from _common import catalog, stage, slots

ap = argparse.ArgumentParser()
ap.add_argument('stage'); ap.add_argument('--suspect', action='store_true'); ap.add_argument('--catalog')
a = ap.parse_args()
cat = catalog(a.catalog)
mats = {m['key']: m for m in cat['materials']}
meshes = {m['key']: m for m in cat['meshes']}
st = stage(cat, a.stage)
used = collections.Counter()
for n in st['nodes']:
    if n['mesh']:
        for s in slots(n, meshes):
            used[s] += 1
for key, count in used.most_common():
    m = mats.get(key)
    if not m:
        print(f'{count:4}  (no material slot -> Geometry.mat grey)')
        continue
    guessed = not m['baseSource'].startswith('parameter')
    overlay = bool(re.search(r'(moss|_b$|mat2|top |detail)', m['baseSource'].lower()))
    odd = guessed or not m['base'] or m['blend'] in ('translucent', 'additive', 'modulate') or overlay or m.get('hidden')
    if a.suspect and not odd:
        continue
    flag = ' <<' if guessed and m['base'] else ''
    print(f"{count:4}  {m['path'].rsplit('/', 1)[-1][:40]:40} {m['blend'][:5]:5} {'HIDDEN ' if m.get('hidden') else ''}"
          f"base={m['base'][:36]:36} src={m['baseSource'][:40]}{flag}")
    if a.suspect:
        print(f"        params={list(m['parameters'].items())[:6]} {m['note'][:80]}")
