"""Re-read common manager bindings from installed Tekken pak archives.

Uses the installed character-effects skill's bounded property decoder, not Unity
defaults or cached role names. Extracts the manager and directly bound systems;
this is not the full dependency closure or native gameplay dispatch recovery.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
from pathlib import Path
import sys

MANAGER = "TekkenGame/Content/Effect/TK75/Common/BP_CommonEffectManager.uasset"


def sha(data):
    return hashlib.sha256(data).hexdigest()


def compare_cached(fresh, cached):
    errors = []
    if cached.get("manager") != MANAGER or cached.get("managerSha256") != fresh["manager"]["sha256"]:
        errors.append("Cached manager identity/hash differs from original archive")
    actual = {r["role"]: r for r in fresh["bindings"]}
    old = {}
    for row in cached.get("bindings", []):
        role = row["role"]
        if role in old:
            errors.append("Duplicate cached role: " + role)
        old[role] = row
    for role in sorted(actual.keys() | old.keys()):
        if role not in actual or role not in old:
            errors.append("Cached/original role set differs: " + role)
            continue
        for field in ("name", "path", "sha256", "offset", "index"):
            if actual[role][field] != old[role].get(field):
                errors.append("Cached/original binding differs: " + role + "." + field)
    return errors


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--workspace", required=True, type=Path)
    parser.add_argument("--project", type=Path, default=Path("UnityRemaster/TekkenVersusRemaster"))
    parser.add_argument("--game", type=Path)
    parser.add_argument("--t7extract", type=Path)
    parser.add_argument("--decoder", type=Path, help="inspect_character_effects.py from the character-effects skill")
    parser.add_argument("--cached-bindings", type=Path, help="Optional cached bindings.json to compare; never treated as source")
    parser.add_argument("--out", required=True, type=Path, help="New local evidence directory")
    args = parser.parse_args()
    root = args.workspace.resolve()
    resolve = lambda p: (root / p).resolve()
    out = resolve(args.out)
    if out.exists():
        parser.error("Choose a new output directory: " + str(out))
    config = json.loads((resolve(args.project) / "Tools/source-paths.json").read_text(encoding="utf-8-sig"))
    game = resolve(args.game or Path(config["game"]))
    extractor = resolve(args.t7extract or Path(config["asset_workshop"]))
    decoder = resolve(args.decoder) if args.decoder else Path(__file__).resolve().parents[2] / "tekken-character-battle-effects/scripts/inspect_character_effects.py"
    if not decoder.is_file():
        parser.error("Bounded decoder missing; provide --decoder: " + str(decoder))
    sys.path.insert(0, str(extractor))
    from t7extract.cli import Source
    from t7extract.package import Package
    from t7extract.pak import TEKKEN_ASSET_KEY
    spec = importlib.util.spec_from_file_location("original_effect_properties", decoder)
    reader_module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(reader_module)
    out.mkdir(parents=True)
    report = {"schema": "tekken-original-common-verification/1",
              "generatedUtc": datetime.now(timezone.utc).isoformat(), "game": str(game),
              "scope": "Effective top-level installed pak entries; manager and direct particle bindings only",
              "archiveSelection": "t7extract.PakSet: base then _P patches, lexicographic within group; not runtime mount-order proof",
              "decoder": {"path": str(decoder), "sha256": sha(decoder.read_bytes())},
              "extractorSources": {name: sha((extractor / "t7extract" / name).read_bytes())
                                   for name in ("pak.py", "package.py")},
              "originalDispatchVerified": False, "visualParityVerified": False,
              "bindings": [], "packages": [], "errors": []}

    def write_json(name, value):
        (out / name).write_text(json.dumps(value, indent=2, allow_nan=False) + "\n", encoding="utf-8")

    try:
        with Source(str(game), TEKKEN_ASSET_KEY) as source:
            if not source.paks:
                raise ValueError("Original pak archives required; unpacked folders are not accepted")
            entries = {path.casefold(): entry for path, entry in source.paks.files.items()}
            report["archives"] = [{"path": p.path, "bytes": Path(p.path).stat().st_size,
                                   "mtimeNs": Path(p.path).stat().st_mtime_ns} for p in source.paks.paks]
            pak_dirs = {Path(p.path).parent for p in source.paks.paks}
            report["excludedNestedPaks"] = sorted({str(p) for d in pak_dirs for p in d.rglob("*.pak") if p.parent != d})
            cache = {}

            def load(path):
                if path.casefold() in cache:
                    return cache[path.casefold()]
                entry = entries[path.casefold()]
                data = entry.read(TEKKEN_ASSET_KEY, verify=True)
                package = Package(data, entry.path)
                target = (out / "packages" / entry.path).resolve()
                if not target.is_relative_to((out / "packages").resolve()):
                    raise ValueError("Unsafe archive path")
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(data)
                node = {"path": entry.path, "pak": entry.pak.path, "pakEntryOffset": entry.offset,
                        "storedSha1": entry.sha1.hex(), "storedPayloadVerified": True,
                        "sha256": sha(data), "size": len(data)}
                report["packages"].append(node)
                cache[path.casefold()] = (package, node)
                return package, node

            package, node = load(MANAGER)
            report["manager"] = node
            defaults = [e for e in package.exports if e.object_name == "Default__BP_CommonEffectManager_C"]
            if len(defaults) != 1:
                raise ValueError("Expected one exact common-manager default object")
            exp = defaults[0]
            decoder_instance = reader_module.Properties(package)
            props = decoder_instance.read(package.reader(exp.serial_offset), exp.serial_offset + exp.serial_size)
            write_json("manager-defaults.json", {"export": exp.object_name, "offset": exp.serial_offset,
                                                "size": exp.serial_size, "properties": props,
                                                "tags": decoder_instance.tags})
            for field, ref in props.items():
                if not isinstance(ref, dict) or ref.get("class") != "ParticleSystem":
                    continue
                path = reader_module.package_path(ref["path"])
                if not path:
                    raise ValueError("Unresolved particle package: " + field)
                tags = [t for t in decoder_instance.tags if t["path"] == field and t["type"] == "ObjectProperty"]
                if len(tags) != 1 or tags[0].get("status") != "decoded":
                    raise ValueError("Binding property not decoded unambiguously: " + field)
                target_package, target = load(path)
                if not any(e.object_name == ref["object"] and e.class_name == "ParticleSystem" for e in target_package.exports):
                    raise ValueError("Particle export missing: " + field)
                report["bindings"].append({"role": field, "name": ref["object"], "path": path,
                                           "sha256": target["sha256"], "offset": tags[0]["valueOffset"],
                                           "index": ref["index"], "export": exp.object_name,
                                           "tagOffset": tags[0]["tagOffset"], "reference": ref["path"]})
            if not report["bindings"]:
                raise ValueError("No direct common particle bindings decoded")
            report["managerDecodeGaps"] = [t for t in decoder_instance.tags if t.get("status") != "decoded"]
            if args.cached_bindings:
                cached = json.loads(resolve(args.cached_bindings).read_text(encoding="utf-8-sig"))
                report["errors"].extend(compare_cached(report, cached))
                report["cachedBindingsMatch"] = not report["errors"]
    except Exception as exc:
        report["errors"].append(type(exc).__name__ + ": " + str(exc))
    report["originalBindingsVerified"] = bool(report["bindings"]) and not report["errors"]
    report["passed"] = not report["errors"]
    write_json("original-common.json", report)
    print(json.dumps({"passed": report["passed"], "bindings": len(report["bindings"]), "errors": report["errors"]}))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
