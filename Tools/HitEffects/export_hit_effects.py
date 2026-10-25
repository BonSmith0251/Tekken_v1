"""Export Tekken 7's common battle effects (Cascade particle systems) for the Unity remaster.

The native TekkenCommonEffectManager spawns these systems; BP_CommonEffectManager only names
which system plays for each role (HitFX, GuardFX, CriticalHit3FX, BlastFX, ...). This tool reads
those roles, decodes every emitter's modules from their baked lookup tables or curve objects,
and writes one JSON the runtime rebuilds with Unity particle systems, plus the textures (PNG)
and static meshes (JSON) the emitters use.

Units and axes are converted here: UE centimetres -> metres, and UE (X forward, Y right, Z up)
-> Unity (x right, y up, z forward), so an effect's local +z is the UE local +X.
Cascade sprite sizes are half-extents; the exporter writes full sizes.

Usage (from the project root; the source is a folder unpacked with t7extract):
  python -m t7extract unpack <game> "*/Effect/TK75/Common/*" -o <src>
  python -m t7extract unpack <game> "*/Effect/TK75/Character/mrx/Mesh/SM_EF_MRX_*" -o <src>
  python Tools/HitEffects/export_hit_effects.py --source <src> --t7extract <asset_workshop>
"""
import argparse
import hashlib
import json
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Assets/_Project/Resources/HitEffects"
MANAGER = "TekkenGame/Content/Effect/TK75/Common/BP_CommonEffectManager.uasset"
# Roles the remaster drives; the manager holds the system for each.
ROLES = ["HitFX", "CriticalHit3FX", "GuardFX", "PierceFX", "BlastFX", "SmokeFX", "RageFX", "RageAuraFX"]
SAMPLES = 8          # Unity gradients hold at most eight keys
MAX_PARTICLES = 600  # GPU emitters burst up to 2000; CPU particles are capped (documented below)
NATIVE = {"Vector": "<3f", "Vector2D": "<2f", "LinearColor": "<4f", "Rotator": "<3f",
          "Guid": "<4I", "IntPoint": "<2i", "Vector4": "<4f", "Quat": "<4f"}


# ---------------------------------------------------------------- package properties
class Props:
    def __init__(self, pkg):
        self.p = pkg

    def ref(self, i):
        if i == 0:
            return None
        if i > 0:
            return "export:" + self.p.exports[i - 1].object_name
        return "import:" + self.p.imports[-i - 1].object_name

    def read(self, r):
        out = {}
        while True:
            name = r.fname()
            if name == "None":
                return out
            typ, size, idx = r.fname(), r.i32(), r.i32()
            sname = inner = ""
            bval = 0
            if typ == "StructProperty":
                sname = r.fname(); r.skip(16)
            elif typ == "BoolProperty":
                bval = r.u8()
            elif typ in ("ByteProperty", "EnumProperty"):
                r.fname()
            elif typ in ("ArrayProperty", "SetProperty"):
                inner = r.fname()
            elif typ == "MapProperty":
                r.fname(); r.fname()
            if r.u8():
                r.skip(16)
            stop = r.pos + size
            try:
                v = bool(bval) if typ == "BoolProperty" else self.value(r, typ, size, sname, inner)
            except Exception:
                v = None  # an unneeded native struct; the tag size still bounds it
            r.pos = stop
            out[name if idx == 0 else f"{name}[{idx}]"] = v

    def value(self, r, typ, size, sname, inner):
        if typ == "FloatProperty": return r.f32()
        if typ == "IntProperty": return r.i32()
        if typ == "NameProperty": return r.fname()
        if typ == "StrProperty": return r.fstring()
        if typ == "ObjectProperty": return self.ref(r.i32())
        if typ in ("ByteProperty", "EnumProperty"): return r.fname() if size == 8 else r.u8()
        if typ == "StructProperty": return self.struct(r, sname)
        if typ == "ArrayProperty":
            n = r.i32()
            if inner == "StructProperty":
                r.fname(); r.fname(); r.i32(); r.i32(); s = r.fname(); r.skip(16)
                if r.u8(): r.skip(16)
                return [self.struct(r, s) for _ in range(n)]
            if inner == "FloatProperty": return [r.f32() for _ in range(n)]
            if inner == "ObjectProperty": return [self.ref(r.i32()) for _ in range(n)]
            if inner == "IntProperty": return [r.i32() for _ in range(n)]
            if inner == "NameProperty": return [r.fname() for _ in range(n)]
        return None

    def struct(self, r, sname):
        if sname in NATIVE:
            f = NATIVE[sname]
            return list(struct.unpack(f, r.bytes(struct.calcsize(f))))
        if sname == "Color":
            b, g, rr, a = r.bytes(4)
            return [rr / 255, g / 255, b / 255, a / 255]
        if sname == "Box":
            v = list(struct.unpack("<6f", r.bytes(24))); r.u8()
            return v
        return self.read(r)


def load(path, t7):
    pkg = t7.Package(path.read_bytes(), str(path))
    reader = Props(pkg)
    objects = {}
    for e in pkg.exports:
        cls = reader.ref(e.class_index) or ""
        objects[e.object_name] = (cls.split(":")[-1], reader.read(pkg.reader(e.serial_offset)))
    return pkg, objects


# ---------------------------------------------------------------- distributions
def lerp(a, b, t):
    return [x + (y - x) * t for x, y in zip(a, b)]


def curve(points, t, width):
    if not points:
        return [0.0] * width
    def out(p):
        v = p["OutVal"]
        return list(v) if isinstance(v, list) else [v]
    if t <= points[0]["InVal"]:
        return out(points[0])
    for a, b in zip(points, points[1:]):
        if t <= b["InVal"]:
            span = b["InVal"] - a["InVal"]
            if a.get("InterpMode") == "CIM_Constant" or span <= 0:
                return out(a)
            return lerp(out(a), out(b), (t - a["InVal"]) / span)
    return out(points[-1])


def evaluate(raw, objects, t, width):
    """(low, high) of a Cascade raw distribution at time t."""
    if not isinstance(raw, dict):
        return [0.0] * width, [0.0] * width
    table = raw.get("Table") or {}
    values = table.get("Values")
    if values:
        op, stride = table.get("Op", 1), table.get("EntryStride", width)
        count = max(1, table.get("EntryCount", 1))
        # Constant tables store SubEntryStride 0: one entry is then the whole stride.
        sub = table.get("SubEntryStride") or (stride // 2 if op in (2, 3) else stride)
        x = (t - table.get("TimeBias", 0.0)) * table.get("TimeScale", 0.0)
        i0 = int(min(max(x, 0), count - 1)); i1 = min(i0 + 1, count - 1)
        f = min(max(x - i0, 0.0), 1.0)
        def entry(i):
            e = values[i * stride:(i + 1) * stride]
            if op in (2, 3):
                return e[:sub], e[sub:2 * sub]
            return e[:sub], e[:sub]
        (lo0, hi0), (lo1, hi1) = entry(i0), entry(i1)
        lo, hi = lerp(lo0, lo1, f), lerp(hi0, hi1, f)
        return pad(lo, width), pad(hi, width)
    ref = raw.get("Distribution")
    if ref and ref.split(":")[-1] in objects:
        cls, p = objects[ref.split(":")[-1]]
        if cls in ("DistributionFloatConstant", "DistributionVectorConstant"):
            v = p.get("Constant", 0.0)
            v = pad(v if isinstance(v, list) else [v], width)
            return v, v
        if cls in ("DistributionFloatUniform", "DistributionVectorUniform"):
            lo, hi = p.get("Min", 0.0), p.get("Max", 0.0)
            return pad(lo if isinstance(lo, list) else [lo], width), pad(hi if isinstance(hi, list) else [hi], width)
        if cls in ("DistributionFloatConstantCurve", "DistributionVectorConstantCurve"):
            v = pad(curve(p.get("ConstantCurve", {}).get("Points", []), t, width), width)
            return v, v
    v = raw.get("MinValueVec") or [raw.get("MinValue", 0.0)]
    w = raw.get("MaxValueVec") or [raw.get("MaxValue", 0.0)]
    return pad(v, width), pad(w, width)


def pad(v, width):
    v = list(v)
    if len(v) == 1 and width > 1:
        v = v * width
    return (v + [0.0] * width)[:width]


def mid(raw, objects, t, width):
    lo, hi = evaluate(raw, objects, t, width)
    return [(a + b) * .5 for a, b in zip(lo, hi)]


def sampled(raw, objects, width):
    return [[round(i / (SAMPLES - 1), 4)] + [round(x, 5) for x in mid(raw, objects, i / (SAMPLES - 1), width)]
            for i in range(SAMPLES)]


def unity(v):
    """UE (X forward, Y right, Z up) -> Unity (x right, y up, z forward)."""
    return [v[1], v[2], v[0]]


def metres(v):
    return [round(x * .01, 5) for x in unity(v)]


# ---------------------------------------------------------------- materials, textures, meshes
def material_info(name, index, t7, cache):
    if name in cache:
        return cache[name]
    path = index.get(name)
    info = {"texture": None, "blend": "alpha", "intensity": 1.0, "scroll": [0, 0], "tiling": [1, 1], "distortion": False}
    if path is None:
        cache[name] = info
        return info
    _, objects = load(path, t7)
    cls, p = next((c, q) for c, q in objects.values() if c in ("MaterialInstanceConstant", "Material"))
    parent = (p.get("Parent") or name).split(":")[-1]
    blend = (p.get("BasePropertyOverrides") or {}).get("BlendMode") if isinstance(p.get("BasePropertyOverrides"), dict) else p.get("BlendMode")
    if blend == "BLEND_Additive" or (blend is None and "_ADD" in parent.upper()):
        info["blend"] = "add"
    textures = [(t.get("ParameterName", ""), (t.get("ParameterValue") or "").split(":")[-1])
                for t in p.get("TextureParameterValues", []) if t.get("ParameterValue")]
    base = [v for k, v in textures if not any(s in k for s in ("Dist", "Flow", "Normal"))
            and not v.startswith("T_EF_NOR")]
    info["texture"] = base[0] if base else None
    info["distortion"] = not base and any(v.startswith("T_EF_NOR") for _, v in textures)
    for s in p.get("ScalarParameterValues", []):
        if s.get("ParameterName") in ("Glare Power", "Color Power", "ColorPower"):
            info["intensity"] = max(info["intensity"], float(s.get("ParameterValue", 1.0)))
        if s.get("ParameterName") == "Alpha Clip":
            info["alphaClip"] = float(s.get("ParameterValue", 0.0))
    for v in p.get("VectorParameterValues", []):
        if v.get("ParameterName") == "BaseTexScroll":
            info["scroll"] = [round(x, 4) for x in v["ParameterValue"][:2]]
        if v.get("ParameterName") == "BaseTexTilling":
            info["tiling"] = [round(x, 4) for x in v["ParameterValue"][:2]]
    info["parent"] = parent
    cache[name] = info
    return info


def export_texture(name, index, t7, written):
    if not name or name in written or name not in index:
        return
    pkg = t7.Package(index[name].read_bytes(), str(index[name]))
    for _, tex in t7.iter_textures(pkg):
        if isinstance(tex, Exception):
            raise tex
        image = t7.to_image(tex)
        (OUT / "Textures").mkdir(parents=True, exist_ok=True)
        image.save(OUT / "Textures" / f"{name}.png")
        # A texture without real alpha is a grey mask: the shader then reads coverage from red.
        alpha = image.getchannel("A").getextrema() if "A" in image.mode else (255, 255)
        written[name] = {"mask": alpha[0] == 255, "size": list(image.size)}
        return


def export_mesh(name, index, t7, written):
    if not name or name in written or name not in index:
        return
    import numpy as np
    pkg = t7.Package(index[name].read_bytes(), str(index[name]))
    i = next(k for k, e in enumerate(pkg.exports) if e.class_name == "StaticMesh")
    mesh = t7.extract_mesh(pkg, i, full=True)
    rig = mesh["rig"]
    raw = np.frombuffer(rig["vertex_data"], np.uint8).reshape(len(mesh["positions"]), rig["stride"])
    uv_offset = 16 if rig["high_precision_tangents"] else 8
    size = 8 if rig["full_precision_uvs"] else 4
    uv = np.ascontiguousarray(raw[:, uv_offset:uv_offset + size]).view("<f4" if size == 8 else "<f2").reshape(-1, 2)
    data = {"positions": [round(x, 5) for p in mesh["positions"] for x in metres(p)],
            "uv": [round(float(x), 5) for x in uv.reshape(-1)],
            "indices": mesh["indices"]}
    (OUT / "Meshes").mkdir(parents=True, exist_ok=True)
    (OUT / "Meshes" / f"{name}.json").write_text(json.dumps(data, separators=(",", ":")))
    written[name] = len(mesh["positions"])


# ---------------------------------------------------------------- emitters
def emitter(objects, ename, index, t7, materials, notes):
    cls, e = objects[ename]
    lod = objects[e["LODLevels"][0].split(":")[-1]][1]
    if lod.get("bEnabled") is False:
        return None
    modules = [objects[lod["RequiredModule"].split(":")[-1]], objects[lod["SpawnModule"].split(":")[-1]]]
    modules += [objects[m.split(":")[-1]] for m in lod.get("Modules", []) if m]
    out = {"name": e.get("EmitterName", ename), "kind": "sprite", "align": "view"}
    typedata = lod.get("TypeDataModule")
    if typedata:
        tcls, tp = objects[typedata.split(":")[-1]]
        if tcls == "ParticleModuleTypeDataMesh":
            out["kind"] = "mesh"; out["mesh"] = (tp.get("Mesh") or "").split(":")[-1]
        elif tcls == "ParticleModuleTypeDataGpu":
            out["gpu"] = True
    for mcls, m in modules:
        if m.get("bEnabled") is False:
            continue
        t = mcls.replace("ParticleModule", "")
        if t == "Required":
            mat = (m.get("Material") or "").split(":")[-1]
            out["material"] = mat
            out.update({k: v for k, v in material_info(mat, index, t7, materials).items() if k != "parent"})
            if m.get("ScreenAlignment") == "PSA_Velocity": out["align"] = "velocity"
            out["duration"] = m.get("EmitterDuration", 1.0)
            out["loops"] = m.get("EmitterLoops", 0)
            out["delay"] = m.get("EmitterDelay", 0.0)
            if m.get("SubImages_Horizontal", 1) > 1 or m.get("SubImages_Vertical", 1) > 1:
                out["subImages"] = [m.get("SubImages_Horizontal", 1), m.get("SubImages_Vertical", 1)]
        elif t == "Spawn":
            rate = mid(m.get("Rate"), objects, 0, 1)[0] * mid(m.get("RateScale"), objects, 0, 1)[0]
            out["rate"] = round(rate, 3)
            out["bursts"] = [[b.get("Count", 0), b.get("CountLow", -1), b.get("Time", 0.0)] for b in m.get("BurstList", [])]
        elif t == "Lifetime":
            lo, hi = evaluate(m.get("Lifetime"), objects, 0, 1); out["life"] = [lo[0], hi[0]]
        elif t in ("Size", "Size_Seeded"):
            lo, hi = evaluate(m.get("StartSize"), objects, 0, 3)
            # Cascade sprite sizes are half extents; mesh sizes are mesh scales.
            k = 1.0 if out["kind"] == "mesh" else 2.0
            out["size"] = {"min": [round(x * k * (.01 if out["kind"] != "mesh" else 1), 5) for x in lo],
                           "max": [round(x * k * (.01 if out["kind"] != "mesh" else 1), 5) for x in hi]}
        elif t == "SizeMultiplyLife":
            out["sizeLife"] = sampled(m.get("LifeMultiplier"), objects, 3)
        elif t == "SizeScale":
            out["sizeScale"] = mid(m.get("SizeScale"), objects, 0, 3)
        elif t == "SizeScaleBySpeed":
            out["sizeBySpeed"] = {"scale": m.get("SpeedScale", [1, 1]), "max": m.get("MaxScale", [1, 1])}
        elif t in ("Color", "Color_Seeded"):
            lo, hi = evaluate(m.get("StartColor"), objects, 0, 3)
            alo, ahi = evaluate(m.get("StartAlpha"), objects, 0, 1)
            out["color"] = {"min": lo + alo, "max": hi + ahi}
        elif t == "ColorOverLife":
            rgb = sampled(m.get("ColorOverLife"), objects, 3); a = sampled(m.get("AlphaOverLife"), objects, 1)
            out["colorLife"] = [r + [x[1]] for r, x in zip(rgb, a)]
        elif t == "ColorScaleOverLife":
            rgb = sampled(m.get("ColorScaleOverLife"), objects, 3); a = sampled(m.get("AlphaScaleOverLife"), objects, 1)
            out["colorScale"] = [r + [x[1]] for r, x in zip(rgb, a)]
        elif t == "Velocity":
            lo, hi = evaluate(m.get("StartVelocity"), objects, 0, 3)
            rlo, rhi = evaluate(m.get("StartVelocityRadial"), objects, 0, 1)
            out["velocity"] = {"min": metres(lo), "max": metres(hi), "radial": [rlo[0] * .01, rhi[0] * .01]}
        elif t == "VelocityCone":
            alo, ahi = evaluate(m.get("Angle"), objects, 0, 1)
            vlo, vhi = evaluate(m.get("Velocity"), objects, 0, 1)
            # Cascade cone angle: 1.0 = 180 degrees.
            out["cone"] = {"angle": [alo[0] * 180, ahi[0] * 180], "speed": [vlo[0] * .01, vhi[0] * .01],
                           "dir": unity(m.get("Direction", [0, 0, 1]))}
        elif t == "VelocityOverLifetime":
            out["velocityLife"] = sampled(m.get("VelOverLife"), objects, 3)
        elif t in ("AccelerationConstant", "Acceleration"):
            lo, hi = evaluate(m.get("Acceleration"), objects, 0, 3) if "Acceleration" in m and isinstance(m["Acceleration"], dict) else ([0, 0, 0], [0, 0, 0])
            if t == "AccelerationConstant" and isinstance(m.get("Acceleration"), list):
                lo = hi = m["Acceleration"]
            out["accel"] = metres([(a + b) * .5 for a, b in zip(lo, hi)])
        elif t == "AccelerationDrag":
            out["drag"] = [[x[0], x[1]] for x in sampled(m.get("DragCoefficientRaw") or m.get("DragCoefficient"), objects, 1)]
        elif t in ("LocationPrimitiveSphere", "LocationPrimitiveSphere_Seeded"):
            r = mid(m.get("StartRadius"), objects, 0, 1)[0]
            out["shape"] = {"type": "sphere", "radius": r * .01,
                            "offset": metres(mid(m.get("StartLocation"), objects, 0, 3)),
                            "velocity": m.get("Velocity", False),
                            "velocityScale": mid(m.get("VelocityScale"), objects, 0, 1)[0]}
        elif t == "LocationPrimitiveCylinder":
            out["shape"] = {"type": "cylinder", "radius": mid(m.get("StartRadius"), objects, 0, 1)[0] * .01,
                            "height": mid(m.get("StartHeight"), objects, 0, 1)[0] * .01,
                            "offset": metres(mid(m.get("StartLocation"), objects, 0, 3)),
                            "velocity": m.get("Velocity", False),
                            "velocityScale": mid(m.get("VelocityScale"), objects, 0, 1)[0],
                            "axis": m.get("HeightAxis", "PMLPC_HEIGHTAXIS_Z")}
        elif t == "Location":
            lo, hi = evaluate(m.get("StartLocation"), objects, 0, 3)
            out["location"] = {"min": metres(lo), "max": metres(hi)}
        elif t in ("Rotation", "Rotation_Seeded"):
            lo, hi = evaluate(m.get("StartRotation"), objects, 0, 1); out["rotation"] = [lo[0], hi[0]]
        elif t in ("RotationRate", "RotationRate_Seeded"):
            lo, hi = evaluate(m.get("StartRotationRate"), objects, 0, 1); out["rotationRate"] = [lo[0], hi[0]]
        elif t == "MeshRotation":
            lo, hi = evaluate(m.get("StartRotation"), objects, 0, 3)
            out["meshRotation"] = {"min": lo, "max": hi}
        elif t == "MeshRotationRate":
            lo, hi = evaluate(m.get("StartRotationRate"), objects, 0, 3)
            out["meshRotationRate"] = {"min": lo, "max": hi}
        elif t == "EmitterMeshRotation" or t == "EmitterRotation":
            out["emitterRotation"] = m.get("Rotation", [0, 0, 0])
        elif t == "CameraOffset":
            out["cameraOffset"] = mid(m.get("CameraOffset"), objects, 0, 1)[0] * .01
        elif t == "Light":
            out["light"] = {"color": mid(m.get("ColorScaleOverLife"), objects, 0, 3),
                            "brightness": mid(m.get("BrightnessOverLife"), objects, 0, 1)[0],
                            "radiusScale": mid(m.get("RadiusScale"), objects, 0, 1)[0],
                            "exponent": mid(m.get("LightExponent"), objects, 0, 1)[0]}
        elif t == "FloorHeight" or t.startswith("PM_FloorHeight"):
            out["floor"] = True
        elif t == "OrientationAxisLock":
            out["axisLock"] = m.get("LockAxisFlags", "EPAL_Z")
        elif t.startswith("PM_CharacterBone"):
            out["bones"] = True
        elif t == "VectorFieldLocal":
            # The field asset is not decoded: keep its strength (cm/s) and tightness (per-frame pull
            # toward the field velocity) so the runtime can approximate it with drag plus noise.
            out["vectorField"] = [m.get("Intensity", 1.0) * .01, m.get("Tightness", 0.0)]
            notes.add("vector fields are approximated: tightness as drag, intensity as noise (field shapes not decoded)")
        elif t.startswith("VectorField"):
            pass
        elif t == "ParameterDynamic":
            # The effect materials' maths nodes are stripped in the cooked game, so only the
            # parameter names survive. AlphaClip (named, or the third slot where names are
            # missing) is kept as a per-particle erosion curve; the runtime applies the inferred
            # alpha ^ max(1, clip); a static material "Alpha Clip" is applied as
            # saturate(alpha * (1 + clip) - clip). Scroll/distortion drivers are not reproduced.
            params = m.get("DynamicParams", [])
            index = next((i for i, q in enumerate(params) if q.get("ParamName") == "AlphaClip"), None)
            if index is None and len(params) > 2 and not any(q.get("ParamName") for q in params):
                index = 2
            if index is not None:
                out["alphaClipLife"] = [[x[0], x[1]] for x in sampled(params[index].get("ParamValue"), objects, 1)]
            notes.add("dynamic material parameters other than AlphaClip (scroll, distortion) are not reproduced")
        elif t in ("TypeDataGpu", "TypeDataMesh"):
            pass
        else:
            notes.add(f"module {t} is not reproduced")
    for b in out.get("bursts", []):
        if b[0] > MAX_PARTICLES:
            notes.add(f"bursts above {MAX_PARTICLES} particles are capped (GPU emitters spawn up to 2000)")
            b[0] = MAX_PARTICLES
    return out


def flatten(x):
    """Unity's JsonUtility reads no nested arrays or maps: curves become flat strided arrays
    (sizeLife/velocityLife t,x,y,z; colorLife/colorScale t,r,g,b,a; drag t,v; bursts count,low,time)."""
    f = lambda rows: [round(v, 5) for row in rows for v in row]
    out = {k: x[k] for k in ("name", "kind", "align", "material", "texture", "mesh", "blend", "intensity",
                             "scroll", "tiling", "distortion", "mask", "duration", "loops", "delay", "rate",
                             "life", "rotation", "rotationRate", "accel", "cameraOffset", "floor", "bones",
                             "gpu", "emitterRotation", "vectorField", "alphaClip") if k in x}
    out["axisLock"] = x.get("axisLock", "")
    out["bursts"] = f(x.get("bursts", []))
    if "size" in x: out["sizeMin"], out["sizeMax"] = x["size"]["min"], x["size"]["max"]
    for key in ("sizeLife", "colorLife", "colorScale", "velocityLife", "drag", "alphaClipLife"):
        if key in x: out[key] = f(x[key])
    if "color" in x: out["colorMin"], out["colorMax"] = x["color"]["min"], x["color"]["max"]
    if "velocity" in x:
        out["velocityMin"], out["velocityMax"], out["radial"] = x["velocity"]["min"], x["velocity"]["max"], x["velocity"]["radial"]
    if "cone" in x:
        out["coneAngle"], out["coneSpeed"], out["coneDir"] = x["cone"]["angle"], x["cone"]["speed"], x["cone"]["dir"]
    if "shape" in x:
        s = x["shape"]
        out.update(shape=s["type"], shapeRadius=s["radius"], shapeHeight=s.get("height", 0), shapeOffset=s["offset"],
                   shapeVelocity=bool(s.get("velocity")), shapeVelocityScale=s.get("velocityScale", 0))
    if "location" in x: out["locationMin"], out["locationMax"] = x["location"]["min"], x["location"]["max"]
    if "meshRotation" in x: out["meshRotationMin"], out["meshRotationMax"] = x["meshRotation"]["min"], x["meshRotation"]["max"]
    if "meshRotationRate" in x:
        out["meshRotationRateMin"], out["meshRotationRateMax"] = x["meshRotationRate"]["min"], x["meshRotationRate"]["max"]
    if "light" in x:
        l = x["light"]
        out.update(lightColor=l["color"], lightBrightness=l["brightness"], lightRadiusScale=l["radiusScale"], hasLight=True)
    if "sizeBySpeed" in x: out["sizeBySpeed"] = list(x["sizeBySpeed"]["scale"]) + list(x["sizeBySpeed"]["max"])
    return out


def main():
    a = argparse.ArgumentParser()
    a.add_argument("--source", required=True, help="folder unpacked with t7extract (contains TekkenGame/)")
    a.add_argument("--t7extract", required=True, help="t7extract checkout (asset_workshop in Tools/source-paths.json)")
    args = a.parse_args()
    sys.path.insert(0, args.t7extract)
    import types
    from t7extract.package import Package
    from t7extract.texture import iter_textures, to_image
    from t7extract.mesh import extract_mesh
    t7 = types.SimpleNamespace(Package=Package, iter_textures=iter_textures, to_image=to_image, extract_mesh=extract_mesh)
    source = Path(args.source)
    index = {p.stem: p for p in source.rglob("*.uasset")}
    _, manager = load(source / MANAGER, t7)
    defaults = next(p for c, p in manager.values() if c.endswith("BP_CommonEffectManager_C"))
    materials, textures, meshes, notes, effects, roles, hashes = {}, {}, {}, set(), {}, {}, {}
    for role in ROLES:
        system = (defaults.get(role) or "").split(":")[-1]
        roles[role] = system
        if system in effects or system not in index:
            continue
        hashes[system] = hashlib.sha256(index[system].read_bytes()).hexdigest()
        _, objects = load(index[system], t7)
        ps = next(p for c, p in objects.values() if c == "ParticleSystem")
        emitters = [x for x in (emitter(objects, e.split(":")[-1], index, t7, materials, notes)
                                for e in ps.get("Emitters", [])) if x]
        for x in emitters:
            if x.get("distortion"):
                notes.add("refraction-only emitters (normal-map distortion) are skipped")
            export_texture(x.get("texture"), index, t7, textures)
            if x["kind"] == "mesh":
                export_mesh(x.get("mesh"), index, t7, meshes)
        effects[system] = {"emitters": emitters}
    for e in effects.values():
        for x in e["emitters"]:
            if x.get("texture") in textures:
                x["mask"] = textures[x["texture"]]["mask"]
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "effects.json").write_text(json.dumps({
        "source": "BP_CommonEffectManager defaults; Effect/TK75/Common particle systems",
        "roles": [{"role": r, "system": s} for r, s in roles.items()],
        "systems": [{"name": n, "sha256": hashes[n], "emitters": [flatten(x) for x in e["emitters"]]}
                    for n, e in effects.items()],
        "limitations": sorted(notes)}, indent=1))
    print(json.dumps({"roles": roles, "textures": len(textures), "meshes": len(meshes), "limitations": sorted(notes)}, indent=1))


if __name__ == "__main__":
    main()
