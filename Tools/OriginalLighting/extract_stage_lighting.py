"""Read original cooked stage lighting; does not edit Unity scenes or game files.

Requires the workspace's t7extract package. All intensities remain native values.
"""
import argparse
import hashlib
import json
import struct
import sys
from pathlib import Path

sys.dont_write_bytecode = True
WORKSPACE = Path(__file__).resolve().parents[4]
PROJECT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(WORKSPACE / "t7extract"))
from t7extract.cli import Source
from t7extract.pak import TEKKEN_ASSET_KEY
from t7extract.package import Package, Reader
from t7extract.texture import read_texture, to_dds, to_image


def reference(package, index):
    if not index:
        return None
    obj = package.exports[index - 1] if index > 0 else package.imports[-index - 1]
    parent = reference(package, obj.outer_index)
    return (parent + "." if parent else "") + obj.object_name


def properties(package, reader):
    values, tags = {}, {}
    while True:
        name = reader.fname()
        if name == "None":
            return values, tags
        kind, size, array_index = reader.fname(), reader.i32(), reader.i32()
        assert size >= 0
        meta = {"type": kind, "size": size, "arrayIndex": array_index}
        if kind == "StructProperty":
            meta["struct"] = reader.fname()
            reader.skip(16)
        elif kind == "BoolProperty":
            boolean = bool(reader.u8())
        elif kind in ("ByteProperty", "EnumProperty"):
            meta["enum"] = reader.fname()
        elif kind in ("ArrayProperty", "SetProperty"):
            meta["innerType"] = reader.fname()
        elif kind == "MapProperty":
            meta["innerType"], meta["valueType"] = reader.fname(), reader.fname()
        if reader.u8():
            reader.skip(16)
        start, end = reader.pos, reader.pos + size
        assert end <= len(reader.buf)
        meta["valueOffset"] = start
        raw = reader.buf[start:end]
        value = {"undecodedHex": raw.hex()}
        if kind == "BoolProperty":
            value = boolean
        elif kind == "FloatProperty" and size == 4:
            value = reader.f32()
        elif kind in ("IntProperty", "ObjectProperty") and size == 4:
            value = reader.i32()
            if kind == "ObjectProperty":
                value = {"index": value, "object": reference(package, value)}
        elif kind == "NameProperty" or (kind in ("ByteProperty", "EnumProperty") and size == 8):
            value = reader.fname()
        elif kind == "ByteProperty" and size == 1:
            value = reader.u8()
        elif kind == "StrProperty":
            value = reader.fstring()
        elif kind == "StructProperty":
            typ = meta["struct"]
            if typ in ("Vector", "Rotator") and size == 12:
                labels = ("x", "y", "z") if typ == "Vector" else ("pitch", "yaw", "roll")
                value = dict(zip(labels, struct.unpack("<3f", raw)))
            elif typ == "LinearColor" and size == 16:
                value = dict(zip(("r", "g", "b", "a"), struct.unpack("<4f", raw)))
            elif typ == "Color" and size == 4:
                b, g, r, a = raw
                value = {"r": r, "g": g, "b": b, "a": a, "encoding": "FColor bytes (BGRA storage)"}
            elif typ == "Guid" and size == 16:
                value = raw.hex()
            elif typ in ("PostProcessSettings", "CharacterPostEffectParameter", "ActorTickFunction"):
                nested = Reader(reader.buf[:end], start, reader.names)
                value, meta["children"] = properties(package, nested)
                assert nested.pos == end, (name, nested.pos, end)
        elif kind == "ArrayProperty" and meta["innerType"] in ("ObjectProperty", "NameProperty"):
            count = reader.i32()
            assert 0 <= count < 100000
            value = []
            for _ in range(count):
                if meta["innerType"] == "NameProperty":
                    value.append(reader.fname())
                else:
                    index = reader.i32()
                    value.append({"index": index, "object": reference(package, index)})
            assert reader.pos == end
        key = name if not array_index else f"{name}[{array_index}]"
        values[key], tags[key] = value, meta
        reader.pos = end


def snapshot(raw, path):
    package = Package(raw, path)
    records = []
    for index, export in enumerate(package.exports, 1):
        relevant = any(word in export.class_name for word in (
            "Light", "Fog", "PostProcess", "ReflectionCapture", "StageParameter", "MugenFar"))
        if not (relevant or export.class_name in ("SceneComponent", "Actor", "WorldSettings")):
            continue
        if export.class_name in ("BlueprintGeneratedClass", "Function", "Texture2D", "TextureCube"):
            continue
        reader = package.reader(export.serial_offset)
        values, tags = properties(package, reader)
        assert reader.pos <= export.serial_offset + export.serial_size
        records.append({"exportIndex": index, "object": reference(package, index),
                        "class": export.class_name, "template": reference(package, export.template_index),
                        "serialOffset": export.serial_offset, "properties": values, "propertyTags": tags})
    return {"path": path, "sha256": hashlib.sha256(raw).hexdigest(), "bytes": len(raw),
            "objects": records}, package


def main():
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument("--stage", default="stg29")
    args = parser.parse_args()
    assert args.stage.startswith("stg") and args.stage[3:].isalnum()
    output = PROJECT / "Validation/OriginalLighting"
    output.mkdir(parents=True, exist_ok=True)
    paths = [f"TekkenGame/Content/Maps/{args.stage}_light.umap",
             "TekkenGame/Content/PostProcess/Common/BP_LightControl.uasset",
             "TekkenGame/Content/Character/CharacterPostEffect/BP_StageParameter.uasset",
             "TekkenGame/Content/Stage/Common/Mugen/BP_MugenFar.uasset"]
    result = {"stage": args.stage, "source": "installed original game archives",
              "liveMemoryCapture": False, "unitySettingsModified": False,
              "conventions": {"positions": "Unreal centimeters", "rotations": "Unreal pitch/yaw/roll degrees; relative to AttachParent",
                              "intensity": "original serialized units; no HDRP lux conversion assumed",
                              "missingProperties": "inherited/default, NOT zero or false",
                              "postProcess": "serialized values are not necessarily active; check bOverride flags and Blueprint control"},
              "packages": []}
    with Source(str(WORKSPACE / "TEKKEN.7"), TEKKEN_ASSET_KEY) as source:
        for path in paths:
            raw = source.get(path).read()
            record, package = snapshot(raw, path)
            cached = WORKSPACE / "decrypted/scripts/packages" / path
            record["existingExtractionMatchesOriginal"] = cached.exists() and cached.read_bytes() == raw
            result["packages"].append(record)
            print(path, len(record["objects"]), "objects", flush=True)
            if path.endswith("_light.umap"):
                cubes = [o["properties"]["Cubemap"]["object"] for o in record["objects"]
                         if o["class"] == "SkyLightComponent" and "Cubemap" in o["properties"]]
        result["skyCubemaps"] = []
        for obj in cubes:
            path = "TekkenGame/Content/" + obj.split(".")[0].removeprefix("/Game/") + ".uasset"
            raw = source.get(path).read()
            package = Package(raw, path)
            texture = read_texture(package, next(e for e in package.exports if e.class_name == "TextureCube"))
            folder = WORKSPACE / "decrypted/lighting"
            folder.mkdir(exist_ok=True)
            dds = folder / (texture.name + ".dds")
            dds.write_bytes(to_dds(texture))  # Retain the original half-float HDR pixels.
            # Captured-cube alpha is auxiliary data (often zero), not transparency.
            # Convert to RGB before resampling; encode linear HDR RGB as sRGB for inspection.
            import numpy as np
            from PIL import Image
            if texture.pixel_format == "PF_FloatRGBA":
                rgb = np.frombuffer(texture.mips[0].data, "<f2").reshape(texture.slices, texture.height, texture.width, 4)[..., :3].astype(np.float32)
                assert np.isfinite(rgb).all()
                rgb = np.clip(rgb, 0, 1)
                rgb = np.where(rgb <= .0031308, 12.92 * rgb, 1.055 * rgb ** (1 / 2.4) - .055)
                strip = np.concatenate(list(rgb), axis=1)
                preview = Image.fromarray(np.rint(strip * 255).astype(np.uint8))
            else:
                preview = to_image(texture).convert("RGB")
            preview.thumbnail((1536, 256))
            preview.save(output / (args.stage + "-sky-preview.png"))
            result["skyCubemaps"].append({"path": path, "sha256": hashlib.sha256(raw).hexdigest(),
                "faceWidth": texture.width, "faceHeight": texture.height, "faces": texture.slices,
                "format": texture.pixel_format, "dds": str(dds.relative_to(WORKSPACE)),
                "ddsSha256": hashlib.sha256(dds.read_bytes()).hexdigest(),
                "preview": "clamped sRGB inspection preview, auxiliary alpha ignored; use DDS for HDR lighting"})
    target = output / (args.stage + "-lighting.json")
    target.write_text(json.dumps(result, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    print(target, flush=True)


if __name__ == "__main__":
    main()
