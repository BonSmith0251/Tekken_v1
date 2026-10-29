"""Convert an inspected character's original Cascade systems for the Unity viewer.

Consumes the battle-effects skill's hashed original-package evidence. Numeric
manager bindings stay separate from preview roles; this tool infers no triggers.
The existing Cascade conversion is reused as an explicitly documented adaptation.
"""
from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import importlib.util
import json
from pathlib import Path
import sys
import types

PROJECT = Path(__file__).resolve().parents[2]


def sha(data):
    return hashlib.sha256(data).hexdigest()


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, allow_nan=False) + "\n", encoding="utf-8")


def source_package(ref):
    value = ref.split(".", 1)[0]
    if value.startswith("/Game/"):
        return "TekkenGame/Content/" + value[6:] + ".uasset"
    if value.startswith("/Engine/"):
        return "Engine/Content/" + value[8:] + ".uasset"
    return None


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--evidence", required=True, type=Path, help="Folder containing character-effects.json and extracted packages")
    p.add_argument("--t7extract", required=True, type=Path)
    p.add_argument("--out", required=True, type=Path, help="Dedicated character resource folder; generated files may be updated")
    args = p.parse_args()
    root, out = args.evidence.resolve(), args.out.resolve()
    summary = json.loads((root / "character-effects.json").read_text(encoding="utf-8"))
    character = summary["characterCode"]
    sys.path.insert(0, str(args.t7extract.resolve()))
    from t7extract.package import Package
    from t7extract.texture import iter_textures, to_image
    from t7extract.mesh import extract_mesh
    t7 = types.SimpleNamespace(Package=Package, iter_textures=iter_textures, to_image=to_image, extract_mesh=extract_mesh)
    common_path = PROJECT / "Tools/HitEffects/export_hit_effects.py"
    spec = importlib.util.spec_from_file_location("shared_cascade_converter", common_path)
    common = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(common)
    common.OUT = out

    nodes = {n["path"].lower(): n for n in summary["packages"]}
    evidence = {}
    paths = {}
    for key, node in nodes.items():
        path = root / node["extracted"]
        if sha(path.read_bytes()) != node["sha256"]:
            raise ValueError("Original payload hash mismatch: " + node["path"])
        paths[key] = path
        evidence[key] = json.loads((root / node["evidence"]).read_text(encoding="utf-8"))

    # The shared converter accepts short import names. Permit that adapter only
    # for names with one full package path in this closure. A duplicated name (Alisa's
    # PS_EF_asa_punch_korea exists in her folder and in Common) is marked ambiguous and
    # left out of the short-name index; any reference to it still fails in convert().
    by_stem = {}
    for key, path in paths.items():
        stem = path.stem.lower()
        by_stem[stem] = None if stem in by_stem else key
    index = common.CaseIndex(path for key, path in paths.items() if by_stem[path.stem.lower()] == key)
    decoded_cache, used_packages, rejected_fields = {}, set(), []
    untrusted = object()

    def convert(value, package_key, export_name, field=""):
        if isinstance(value, dict):
            if value.get("undecoded"):
                rejected_fields.append({"package": nodes[package_key]["path"], "export": export_name, "field": field,
                                        "offset": value.get("offset"), "size": value.get("size"), "status": "untrusted; omitted from adaptation"})
                return untrusted
            if "kind" in value and "index" in value and "path" in value:
                if value["kind"] == "export":
                    return "export:e" + str(value["index"])
                target = source_package(value["path"])
                if target:
                    key = target.lower()
                    if key not in paths or by_stem.get(Path(target).stem.lower()) != key:
                        raise ValueError("Unresolved or ambiguous full import path: " + value["path"])
                return "import:" + value["object"]
            result = {}
            for k, v in value.items():
                converted = convert(v, package_key, export_name, field + "." + k)
                if converted is not untrusted:
                    result[k] = converted
            return result
        if isinstance(value, list):
            result = []
            for i, v in enumerate(value):
                converted = convert(v, package_key, export_name, field + "[" + str(i) + "]")
                if converted is not untrusted:
                    result.append(converted)
            return result
        return value

    def load(path, unused):
        key = next(k for k, v in paths.items() if v == path)
        used_packages.add(key)
        if key not in decoded_cache:
            ev = evidence[key]
            objects = {}
            for obj in ev["objects"]:
                objects["e" + str(obj["index"])] = (obj["class"], convert(obj.get("properties", {}), key, obj["name"]))
            decoded_cache[key] = (Package(path.read_bytes(), nodes[key]["path"]), objects)
        return decoded_cache[key]

    common.load = load
    limitations = {
        "Native move/impact triggers, attachment dispatch and stop rules are not inferred by this export; roles are manual-preview identifiers.",
        "Unity renders the first stored LOD only; all original LOD/module records remain in conversion-report.json and hashed evidence.",
        "Shared Cascade conversion samples distributions/curves, applies its documented caps and supplies rendering defaults for absent properties; these are adaptations, not original serialized values.",
        "Cooked shader expressions/native behavior are not recovered; shared shader brightness, alpha erosion and material selection remain approximations.",
        "Original raw properties and hashes remain the authority; runtime JSON is a unit/basis-converted rendering adaptation.",
    }
    effects, candidates, ledger = [], [], []
    materials, textures, meshes = {}, {}, {}
    all_notes = set(limitations)
    rows = [r for r in summary["inventory"] if r.get("classes", {}).get("ParticleSystem")]
    for row in rows:
        key = row["path"].lower()
        node, ev = nodes[key], evidence[key]
        bad = [t for o in ev["objects"] for t in o["tags"] if t.get("status") != "decoded"]
        if bad or node.get("exportParseErrors"):
            raise ValueError("Character particle properties are not fully decoded: " + row["path"])
        _, objects = load(paths[key], t7)
        system_obj = next(o for o in ev["objects"] if o["class"] == "ParticleSystem")
        system = system_obj["name"]
        props = objects["e" + str(system_obj["index"])][1]
        converted_emitters, system_notes = [], set()
        for ref in props.get("Emitters", []):
            ename = ref.split(":")[-1]
            original_emitter = next(o for o in ev["objects"] if "e" + str(o["index"]) == ename)
            emitted = common.emitter(objects, ename, index, t7, materials, system_notes)
            emitter_props = objects[ename][1]
            lods = [objects[r.split(":")[-1]][1] for r in emitter_props.get("LODLevels", [])]
            original_lods = []
            for lod in original_emitter["properties"].get("LODLevels", []):
                lo = ev["objects"][lod["index"] - 1]
                linked = [lo["properties"].get("RequiredModule"), lo["properties"].get("SpawnModule"),
                          lo["properties"].get("TypeDataModule")] + lo["properties"].get("Modules", [])
                original_lods.append({"lod": lo, "modules": [ev["objects"][m["index"] - 1] for m in linked if m and m.get("kind") == "export"]})
            entry = {"system": system, "emitter": original_emitter, "lods": original_lods, "runtimeIncluded": emitted is not None}
            if emitted is None:
                ledger.append(entry)
                continue
            emitted["name"] = original_emitter["properties"].get("EmitterName", original_emitter["name"])
            first = lods[0]
            required = objects[first["RequiredModule"].split(":")[-1]][1]
            linked_modules = [objects[r.split(":")[-1]] for r in first.get("Modules", []) if r]
            if "bUseLocalSpace" in required:
                emitted["simulationLocal"] = required["bUseLocalSpace"]
            # Retain data for explicit runtime support; the existing shared exporter
            # used to drop these fields. Their original values remain above.
            if "subImages" in emitted:
                emitted["subImageInterpolation"] = required.get("InterpolationMethod", "")
            for cls, module in linked_modules:
                if module.get("bEnabled") is False:
                    continue
                if cls == "ParticleModuleSubUV":
                    emitted["subImageIndex"] = common.sampled(module.get("SubImageIndex"), objects, 1)
                if cls == "PM_CharacterBone":
                    locations = module.get("SourceLocations", [])
                    emitted["boneNames"] = [x["BoneSocketName"] for x in locations]
                    emitted["boneOffsets"] = [v for x in locations for v in common.metres(x.get("Offset", [0, 0, 0]))]
                    emitted["boneLocationOffset"] = common.metres(common.mid(module.get("LocationOffset"), objects, 0, 3))
                    emitted["boneRotationOffset"] = common.mid(module.get("RotationOffset"), objects, 0, 3)
                    if "bUpdatePositionEachFrame" in module:
                        emitted["boneUpdatePositionEachFrame"] = module["bUpdatePositionEachFrame"]
                    if "SourceType" in module:
                        emitted["boneSourceType"] = module["SourceType"]
                    system_notes.add("PM_CharacterBone source names/offsets are preserved; original native selection and rotation semantics require runtime validation")
            if emitted.get("distortion"):
                system_notes.add("refraction-only emitters are skipped by the Unity runtime")
            common.export_texture(emitted.get("texture"), index, t7, textures)
            if emitted.get("texture") and emitted["texture"] not in textures:
                raise ValueError("Missing converted texture: " + emitted["texture"])
            if emitted["kind"] == "mesh":
                common.export_mesh(emitted.get("mesh"), index, t7, meshes)
                if emitted.get("mesh") not in meshes:
                    raise ValueError("Missing converted mesh: " + str(emitted.get("mesh")))
            for asset in (emitted.get("texture"), emitted.get("mesh"), emitted.get("material")):
                if asset and by_stem.get(asset.lower()):
                    used_packages.add(by_stem[asset.lower()])
            emitted["mask"] = textures.get(emitted.get("texture"), {}).get("mask", False)
            flat = common.flatten(emitted)
            for field in ("subImages", "subImageInterpolation", "boneNames", "boneOffsets", "boneLocationOffset", "boneRotationOffset", "boneUpdatePositionEachFrame", "boneSourceType", "simulationLocal"):
                if field in emitted:
                    flat[field] = emitted[field]
            if "subImageIndex" in emitted:
                flat["subImageIndex"] = [v for pair in emitted["subImageIndex"] for v in pair]
            entry["runtime"] = flat
            entry["runtimeSupport"] = {
                "subUv": "interpolated adjacent source atlas frames" if emitted.get("subImageInterpolation") == "PSUVIM_Linear_Blend"
                         else "source atlas frame index" if emitted.get("subImageInterpolation") == "PSUVIM_Linear"
                         else "emitter omitted: unsupported interpolation mode " + emitted["subImageInterpolation"] if emitted.get("subImageInterpolation")
                         else "no serialized atlas grid",
                "simulationSpace": "local, explicitly serialized" if emitted.get("simulationLocal") else "world adaptation; property absent" if "simulationLocal" not in emitted else "world, explicitly serialized",
                "boneAttachment": "exact named bone at spawn; missing names are skipped with a warning" if emitted.get("boneNames") else "no character-bone module",
                "boneLimitations": "Native selection/rotation and nonzero bone-local offset semantics are unresolved; socket lookup and update-each-frame behavior are not implemented" if emitted.get("boneNames") else "",
            }
            if emitted.get("subImages") and emitted.get("subImageInterpolation") not in ("PSUVIM_Linear", "PSUVIM_Linear_Blend"):
                entry["runtimeIncluded"] = False
                entry["runtimeSkippedReason"] = "Unsupported SubUV interpolation; source records and converted data retained, renderer omits this emitter"
            if emitted.get("boneUpdatePositionEachFrame"):
                system_notes.add("PM_CharacterBone update-each-frame is not implemented; PatoLight's missing Head_Socket is skipped, not replaced by a guessed head bone")
            if emitted.get("boneNames"):
                system_notes.add("PM_CharacterBone native rotation, selection and nonzero bone-local offset semantics are unresolved; only exact named-bone spawn placement is supported")
            ledger.append(entry)
            converted_emitters.append(flat)
        if "module SubUV is not reproduced" in system_notes:
            system_notes.discard("module SubUV is not reproduced")
            unsupported_modes = {e.get("subImageInterpolation", "absent") for e in converted_emitters if e.get("subImages")
                                 and e.get("subImageInterpolation") not in ("PSUVIM_Linear", "PSUVIM_Linear_Blend")}
            for mode in unsupported_modes:
                system_notes.add("Emitters using SubUV interpolation " + mode + " are omitted by the renderer; exported grid/index data do not recover original random-frame selection")
        effects.append({"name": system, "sha256": node["sha256"], "sourcePackage": node["path"], "emitters": converted_emitters,
                        "limitations": sorted(system_notes)})
        candidates.append({"role": system, "system": system, "sourcePackage": node["path"], "sourceSha256": node["sha256"],
                           "managerBound": False, "previewOnly": True})
        all_notes.update(system_notes)

    bindings, unconverted = [], []
    for manager in summary["managers"]:
        if "/character/" + character.lower() + "/" not in manager["source"].lower():
            continue
        for effect in manager["properties"].get("Effects", []):
            ref = effect.get("ParticleSystem")
            if not ref:
                continue
            package = source_package(ref["path"])
            candidate = next((c for c in candidates if c["sourcePackage"].lower() == package.lower()), None)
            if candidate is None:
                # Bound outside the character folder (Kazuya's story-mode beam hit, Alisa's Common
                # bomb/punch): kept as a record, not converted, never played under a guessed role.
                unconverted.append({"effectId": effect["EffectID"], "sourcePackage": package,
                                    "managerPackage": manager["source"], "managerSha256": manager["sourceSha256"],
                                    "bindingStatus": "package binding outside the character folder; not converted"})
                continue
            candidate["managerBound"] = True
            bindings.append({"effectId": effect["EffectID"], "role": candidate["role"], "system": candidate["system"],
                             "sourcePackage": package, "sourceSha256": candidate["sourceSha256"],
                             "managerPackage": manager["source"], "managerSha256": manager["sourceSha256"],
                             "bindingStatus": "package binding; native trigger unresolved"})
    library = {"source": "Original " + character + " character-folder packages; package-backed bindings in registry.json",
               "roles": [{"role": c["role"], "system": c["system"]} for c in candidates], "systems": effects,
               "limitations": sorted(all_notes)}
    registry = {"schema": "tekken-character-effect-registry/1", "characterCode": character, "bindings": bindings,
                "unconvertedBindings": unconverted, "candidates": candidates, "nativeTriggerMappings": [], "limitations": sorted(all_notes)}
    report = {"schema": "tekken-character-effect-conversion/1", "characterCode": character,
              "sourceReport": str(root / "character-effects.json"), "sourceReportSha256": sha((root / "character-effects.json").read_bytes()),
              "converter": str(Path(__file__).resolve()), "sharedConverterSha256": sha(common_path.read_bytes()),
              "sourcePackages": [nodes[k] for k in sorted(used_packages)], "emitters": ledger,
              "materialSources": [evidence[k] for k in sorted(used_packages)
                                  if any(o["class"] in ("Material", "MaterialInstanceConstant") for o in evidence[k]["objects"])],
              "materialAdaptations": materials, "textures": textures, "meshes": meshes,
              "rejectedFields": rejected_fields, "limitations": sorted(all_notes)}
    write_json(out / "effects.json", library)
    write_json(out / "registry.json", registry)
    write_json(out / "conversion-report.json", report)
    result = {"out": str(out), "systems": len(effects), "emitters": sum(len(s["emitters"]) for s in effects),
              "bindings": bindings, "textures": len(textures), "meshes": len(meshes), "rejectedFields": len(rejected_fields),
              "limitations": sorted(all_notes)}
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
