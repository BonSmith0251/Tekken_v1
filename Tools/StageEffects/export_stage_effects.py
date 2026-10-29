"""Export the particle systems the original stages place (brazier fires, falling sand, dust, mist...)
for the Unity remaster.

Reads the effect names that Tools/import_museum_stages.py recorded on stage nodes ("effect" in
Assets/_Project/Stages/Source/stages.json), decodes each Cascade system with the battle-effect
decoder (Tools/HitEffects/export_hit_effects.py) straight from the game paks, and writes
Assets/_Project/Resources/StageEffects/{effects.json, Textures/, Meshes/}. The runtime plays them
with CascadeEffects (folder "StageEffects"); each system is its own role.

Usage (from the project root, after import_museum_stages.py):
  python Tools/StageEffects/export_stage_effects.py [stages.json] [output folder]
"""
import hashlib
import json
import sys
import types
from pathlib import Path

PROJECT = Path(__file__).resolve().parents[2]
WORKSPACE = PROJECT.parents[1]
sys.path[:0] = [str(WORKSPACE / "t7extract"), str(PROJECT / "Tools/HitEffects")]
import export_hit_effects as fx
from t7extract.cli import Source
from t7extract.pak import TEKKEN_ASSET_KEY
from t7extract.package import Package
from t7extract.texture import iter_textures, to_image
from t7extract.mesh import extract_mesh

OUT = PROJECT / "Assets/_Project/Resources/StageEffects"


class PakFile:
    """What the battle-effect decoder needs from a file: its stem and its bytes."""
    def __init__(self, item):
        self.item, self.stem = item, Path(item.path).stem

    def read_bytes(self):
        return self.item.read()

    def __str__(self):
        return self.item.path


_battle_material_info = fx.material_info
COMPOSITES = {}  # baked texture name -> (flame noise, shape mask)
SOFT_FADE = .3  # not recovered: how much of a soft particle the depth fade removes; set by eye
# Per-system colour overrides, by request. The source data is kept everywhere else.
# PS_EF_STG02_Mist (stg02, stg26) is HDR red-orange in the data, (2, 1, .75) x (4, 1, .1) ~ (8, 1, .08);
# how its stripped material used that colour is unknown, and here it read as a red overlay on the
# jungle. The user asked for it toned down so the jungle reads green (2026-10-26): pale green-white,
# same opacity and fade.
TINT_OVERRIDES = {"PS_EF_STG02_Mist": (.85, .92, .82)}


def bake_composites(index, t7, textures):
    """noise x mask, mask turned so the flame tip (stored pointing +U) points up."""
    from PIL import Image, ImageChops
    for name, (flame, mask) in COMPOSITES.items():
        if name in textures:
            continue
        for part in (flame, mask):
            fx.export_texture(part, index, t7, textures)
        if flame not in textures or mask not in textures:
            continue
        m = Image.open(OUT / "Textures" / f"{mask}.png").convert("L").rotate(90, expand=True)
        f = Image.open(OUT / "Textures" / f"{flame}.png").convert("L").resize(m.size)
        shape = ImageChops.multiply(f, m)
        Image.merge("RGBA", (shape, shape, shape, shape)).save(OUT / "Textures" / f"{name}.png")
        textures[name] = {"mask": False, "size": list(m.size)}


def stage_material_info(name, index, t7, cache):
    """The battle decoder reads only a material instance's own parameters. Stage fire instances
    (MI_EF_STG00_Fire01_KESHI_hk) set only distortion textures and an HDR add_color; their flame
    textures are fixed samplers in the parent material. Inherit those, and take add_color as the tint."""
    info = _battle_material_info(name, index, t7, cache)
    if info.get("stageResolved"):
        return info
    info["stageResolved"] = True
    # Soft particles: M_EF__DEPTH_* masters and DepthFade/PixelDepth parameters fade the sprite where
    # it meets geometry and near the camera. The Unity particle shader has no depth fade, so these
    # sprites (sand mist, falling-sand smoke) are drawn at SOFT_FADE opacity instead.
    if "DEPTH" in str(info.get("parent", "")).upper():
        info["softFade"] = True
    elif name in index:
        _, objects = fx.load(index[name], t7)
        for c, p in objects.values():
            for v in p.get("ScalarParameterValues", []) if c == "MaterialInstanceConstant" else []:
                if str(v.get("ParameterName", "")).lower() in ("depthfade", "pixeldepth"):
                    info["softFade"] = True
    if info.get("texture") or info.get("distortion"):
        return info
    parent = info.get("parent")
    if parent and parent != name and parent in index:
        pinfo = _battle_material_info(parent, index, t7, cache)
        _, pobjects = fx.load(index[parent], t7)
        textures = [i.object_name for i in t7.Package(index[parent].read_bytes(), str(index[parent])).imports
                    if i.class_name == "Texture2D" and "noi" not in i.object_name.lower()]
        masks = [t for t in textures if "mask" in t.lower()]
        flames = [t for t in textures if t not in masks]
        if flames and masks:
            # The master multiplies the tiling flame noise by a flame-shaped mask; bake that product.
            info["texture"] = flames[0] + "__x__" + masks[0]
            COMPOSITES[info["texture"]] = (flames[0], masks[0])
        else:
            info["texture"] = (flames or masks or [pinfo.get("texture")])[0]
    if name in index:
        _, objects = fx.load(index[name], t7)
        for c, p in objects.values():
            for v in p.get("VectorParameterValues", []) if c == "MaterialInstanceConstant" else []:
                if str(v.get("ParameterName", "")).lower() in ("add_color", "color", "base color", "basecolor"):
                    rgb = [float(x) for x in v["ParameterValue"][:3]]
                    peak = max(rgb) or 1
                    info["tint"] = [round(x / peak, 4) for x in rgb]
                    info["intensity"] = max(info["intensity"], peak)
    return info


def main():
    global OUT
    fx.material_info = stage_material_info
    if len(sys.argv) > 2:
        OUT = Path(sys.argv[2])
    fx.OUT = OUT
    path = Path(sys.argv[1]) if len(sys.argv) > 1 else PROJECT / "Assets/_Project/Stages/Source/stages.json"
    catalog = json.loads(path.read_text(encoding="utf-8"))
    wanted = sorted({n["effect"] for s in catalog["stages"] for n in s["nodes"] if n.get("effect")})
    source = Source(str(WORKSPACE / "TEKKEN.7"), TEKKEN_ASSET_KEY)
    # Effect assets first, so a name shared with another folder resolves to the effect's own file.
    items = sorted((i for i in source.items() if i.path.lower().endswith(".uasset")),
                   key=lambda i: (0 if "/effect/" in i.path.lower() else 1, i.path))
    index = fx.CaseIndex(PakFile(i) for i in items)
    t7 = types.SimpleNamespace(Package=Package, iter_textures=iter_textures, to_image=to_image, extract_mesh=extract_mesh)
    materials, textures, meshes, notes, systems, failed = {}, {}, {}, set(), [], {}
    for name in wanted:
        if name not in index:
            failed[name] = "package not found"
            continue
        try:
            _, objects = fx.load(index[name], t7)
            ps = next(p for c, p in objects.values() if c == "ParticleSystem")
            # An emitter switched off in Cascade has its Required module disabled (no material).
            emitters = [x for x in (fx.emitter(objects, e.split(":")[-1], index, t7, materials, notes)
                                    for e in ps.get("Emitters", [])) if x and "material" in x]
            bake_composites(index, t7, textures)
            for x in emitters:
                fx.export_texture(x.get("texture"), index, t7, textures)
                if x["kind"] == "mesh":
                    fx.export_mesh(x.get("mesh"), index, t7, meshes)
            systems.append({"name": name, "sha256": hashlib.sha256(index[name].read_bytes()).hexdigest(), "emitters": emitters})
        except Exception as ex:  # one undecodable system must not drop the rest
            failed[name] = f"{type(ex).__name__}: {ex}"
    for s in systems:
        for x in s["emitters"]:
            if x.get("texture") in textures:
                x["mask"] = textures[x["texture"]]["mask"]
            if s["name"] in TINT_OVERRIDES:
                rgb = list(TINT_OVERRIDES[s["name"]])
                for key in ("color",):
                    if key in x:
                        x[key] = {k: rgb + v[3:] for k, v in x[key].items()}
                if "colorScale" in x:
                    x["colorScale"] = [[row[0], 1, 1, 1] + row[4:] for row in x["colorScale"]]
                if "colorLife" in x:
                    x["colorLife"] = [[row[0]] + rgb + row[4:] for row in x["colorLife"]]
            if x.get("softFade") and x.get("blend") != "add":
                x["colorScale"] = [row[:4] + [row[4] * SOFT_FADE] for row in x.get("colorScale") or [[0, 1, 1, 1, 1], [1, 1, 1, 1, 1]]]
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "effects.json").write_text(json.dumps({
        "source": "ParticleSystemComponent templates placed in the stage maps (_geom, _reload, _demo, _effect)",
        "roles": [{"role": s["name"], "system": s["name"]} for s in systems],
        "systems": [{"name": s["name"], "sha256": s["sha256"], "emitters": [dict(fx.flatten(x), **({"subImages": x["subImages"]} if "subImages" in x else {}))
                                                                  for x in s["emitters"]]} for s in systems],
        "limitations": sorted(notes)}, indent=1))
    report = {"systems": len(systems), "wanted": len(wanted), "textures": len(textures), "meshes": len(meshes),
              "failed": failed, "limitations": sorted(notes)}
    (OUT / "report.json").write_text(json.dumps(report, indent=1))
    print(json.dumps(report, indent=1))


if __name__ == "__main__":
    main()
