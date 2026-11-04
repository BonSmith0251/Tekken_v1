#!/usr/bin/env python3
"""Audit/extract original character effects; does not invent move triggers or Unity defaults.

Requires the local t7extract checkout and its archive dependencies. Outputs remain local.
Default coverage is the character manager's stored references and their import closure.
--include-unbound also follows particle systems found in the character's source folder.
"""
from __future__ import annotations
import argparse
from collections import Counter, deque
import hashlib
import json
import math
from pathlib import Path
import re
import struct
import sys

NATIVE_STRUCTS = {"Vector": "<3f", "Vector2D": "<2f", "Vector4": "<4f",
                  "LinearColor": "<4f", "Rotator": "<3f", "Quat": "<4f",
                  "Guid": "<4I", "IntPoint": "<2i"}
DEPENDENCY_CLASSES = {"ParticleSystem", "Material", "MaterialInstanceConstant", "Texture2D",
                      "TextureCube", "StaticMesh", "SkeletalMesh", "VectorFieldStatic",
                      "Blueprint", "BlueprintGeneratedClass", "MaterialFunction",
                      "MaterialParameterCollection"}


def digest(data):
    return hashlib.sha256(data).hexdigest()


def write_safe(path, data):
    """Idempotent output; a differing prior file requires a new output directory."""
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists():
        if path.read_bytes() != data:
            raise FileExistsError(f"Refusing to replace different output: {path}; choose a new --out")
        return
    with path.open("xb") as handle:
        handle.write(data)


def write_json(path, value):
    write_safe(path, (json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + "\n").encode("utf-8"))


def object_path(package, index, seen=None):
    if not index:
        return None
    seen = set() if seen is None else seen
    if index in seen:
        raise ValueError("Cyclic object outer reference")
    seen.add(index)
    objects = package.exports if index > 0 else package.imports
    slot = index - 1 if index > 0 else -index - 1
    if slot < 0 or slot >= len(objects):
        raise ValueError(f"Out-of-range object reference {index}")
    obj = objects[slot]
    parent = object_path(package, obj.outer_index, seen)
    return (parent + "." if parent else "") + obj.object_name


def package_path(reference):
    """Resolve complete import outer chains, never basename-only matching."""
    path = reference.split(".", 1)[0]
    if path.startswith("/Game/"):
        return "TekkenGame/Content/" + path[len("/Game/"):] + ".uasset"
    if path.startswith("/Engine/"):
        return "Engine/Content/" + path[len("/Engine/"):] + ".uasset"
    return None


class Properties:
    """Bounded tagged-property decoder with explicit gaps and byte provenance."""
    def __init__(self, package):
        self.package = package
        self.tags = []
        self.path = []

    def reference(self, index):
        if index == 0:
            return None
        obj = self.package.exports[index - 1] if index > 0 else self.package.imports[-index - 1]
        return {"index": index, "kind": "export" if index > 0 else "import",
                "object": obj.object_name, "class": obj.class_name,
                "path": object_path(self.package, index)}

    def finite_float(self, reader):
        value = reader.f32()
        if not math.isfinite(value):
            raise ValueError("Non-finite serialized float")
        return value

    def read(self, reader, limit):
        output = {}
        for _ in range(100000):
            start = reader.pos
            if start + 8 > limit:
                raise ValueError("Missing property-list terminator within bound")
            name = reader.fname()
            if name == "None":
                return output
            typ, size, array_index = reader.fname(), reader.i32(), reader.i32()
            if size < 0:
                raise ValueError("Negative property size")
            struct_name = inner = ""
            boolean = False
            if typ == "StructProperty":
                struct_name = reader.fname()
                reader.skip(16)
            elif typ == "BoolProperty":
                boolean = bool(reader.u8())
            elif typ in ("ByteProperty", "EnumProperty"):
                reader.fname()
            elif typ in ("ArrayProperty", "SetProperty"):
                inner = reader.fname()
            elif typ == "MapProperty":
                reader.fname()
                reader.fname()
            if reader.u8():
                reader.skip(16)
            value_start, end = reader.pos, reader.pos + size
            if end > limit:
                raise ValueError(f"{name} crosses containing property/export bound")
            key = name if array_index == 0 else f"{name}[{array_index}]"
            self.path.append(key)
            tag = {"path": ".".join(self.path), "type": typ, "struct": struct_name or None,
                   "tagOffset": start, "valueOffset": value_start, "size": size}
            self.tags.append(tag)
            try:
                value = boolean if typ == "BoolProperty" else self.value(reader, typ, size, struct_name, inner, end)
                if reader.pos > end:
                    raise ValueError("Value exceeds declared property size")
                if reader.pos != end:
                    tag.update(status="partial", unreadBytes=end - reader.pos)
                else:
                    tag["status"] = "decoded"
            except NotImplementedError as error:
                tag.update(status="unsupported", reason=str(error))
                value = {"undecoded": True, "offset": value_start, "size": size,
                         "sha256": digest(reader.buf[value_start:end])}
            except Exception as error:
                tag.update(status="error", error=str(error))
                value = {"undecoded": True, "offset": value_start, "size": size,
                         "sha256": digest(reader.buf[value_start:end])}
            finally:
                self.path.pop()
                reader.pos = end
            output[key] = value
        raise ValueError("Property count limit exceeded")

    def value(self, reader, typ, size, struct_name, inner, end):
        if typ == "FloatProperty" and size == 4:
            return self.finite_float(reader)
        if typ == "IntProperty" and size == 4:
            return reader.i32()
        if typ == "NameProperty" and size == 8:
            return reader.fname()
        if typ == "StrProperty":
            return reader.fstring()
        if typ == "ObjectProperty" and size == 4:
            return self.reference(reader.i32())
        if typ in ("ByteProperty", "EnumProperty"):
            if size == 8:
                return reader.fname()
            if size == 1:
                return reader.u8()
        if typ == "StructProperty":
            return self.structure(reader, struct_name, end)
        if typ == "ArrayProperty":
            count = reader.i32()
            if not 0 <= count <= 1000000:
                raise ValueError("Implausible array count")
            structure = ""
            if inner == "StructProperty":
                reader.fname(); reader.fname(); reader.i32(); reader.i32()
                structure = reader.fname()
                reader.skip(16)
                if reader.u8():
                    reader.skip(16)
            values = []
            for index in range(count):
                self.path.append(f"[{index}]")
                try:
                    if inner == "StructProperty":
                        value = self.structure(reader, structure, end)
                    elif inner == "FloatProperty":
                        value = self.finite_float(reader)
                    elif inner == "IntProperty":
                        value = reader.i32()
                    elif inner == "ObjectProperty":
                        value = self.reference(reader.i32())
                    elif inner == "NameProperty":
                        value = reader.fname()
                    else:
                        raise NotImplementedError("Array element type " + inner)
                    if reader.pos > end:
                        raise ValueError("Array crosses property bound")
                    values.append(value)
                finally:
                    self.path.pop()
            return values
        raise NotImplementedError(typ + ("/" + inner if inner else ""))

    def structure(self, reader, name, end):
        if name in NATIVE_STRUCTS:
            fmt = NATIVE_STRUCTS[name]
            values = list(struct.unpack(fmt, reader.bytes(struct.calcsize(fmt))))
            if any(isinstance(v, float) and not math.isfinite(v) for v in values):
                raise ValueError("Non-finite native struct")
            return values
        if name == "Color":
            # Preserve serialized byte channels; do not normalize into a Unity color.
            b, g, r, a = reader.bytes(4)
            return {"B": b, "G": g, "R": r, "A": a}
        if name == "Box":
            return {"MinMax": list(struct.unpack("<6f", reader.bytes(24))), "IsValid": reader.u8()}
        return self.read(reader, end)


def references(value):
    if isinstance(value, dict):
        if value.get("kind") == "import" and "path" in value:
            yield value
        else:
            for child in value.values():
                yield from references(child)
    elif isinstance(value, list):
        for child in value:
            yield from references(child)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--workspace", type=Path, default=Path.cwd(), help="Workspace containing TEKKEN.7 and t7extract")
    parser.add_argument("--game", type=Path, help="Original game root or pak directory; defaults to WORKSPACE/TEKKEN.7")
    parser.add_argument("--t7extract", type=Path, help="Checkout containing the t7extract Python package")
    parser.add_argument("--character", required=True, help="Exact original character folder code, e.g. jin (not a guessed display-name alias)")
    parser.add_argument("--out", required=True, type=Path, help="Local evidence/output folder; differing existing files are never overwritten")
    parser.add_argument("--extract", action="store_true", help="Also copy original package bytes under OUT/packages preserving virtual paths")
    parser.add_argument("--include-common", action="store_true", help="Also inspect BP_CommonEffectManager and its stored particle bindings")
    parser.add_argument("--include-unbound", action="store_true", help="Also follow every particle-system package found in the character folder")
    args = parser.parse_args(argv)
    if not re.fullmatch(r"[A-Za-z0-9_-]+", args.character):
        parser.error("--character must be an original folder code")
    workspace = args.workspace.resolve()
    game = (args.game or workspace / "TEKKEN.7").resolve()
    checkout = (args.t7extract or workspace / "t7extract").resolve()
    sys.path.insert(0, str(checkout))
    from t7extract.cli import Source
    from t7extract.pak import TEKKEN_ASSET_KEY
    from t7extract.package import Package, header_size
    output = args.out.resolve()
    if output == game or game in output.parents:
        parser.error("--out must be outside the original game installation")
    prefix = f"TekkenGame/Content/Effect/TK75/Character/{args.character}/".lower()
    summary = {"schema": "tekken-character-effects/1", "characterCode": args.character,
               "sourceGame": str(game), "extractOriginalBytes": args.extract,
               "includeUnbound": args.include_unbound, "includeCommon": args.include_common,
               "scope": "Effective top-level paks using t7extract override priority; no recursive mods scan.",
               "dependencyTraversal": {
                   "includedImportClasses": sorted(DEPENDENCY_CLASSES),
                   "scope": "Package-import closure for the selected roots, restricted to includedImportClasses and resolvable /Game or /Engine asset paths.",
                   "completionMeaning": "dependencyResolutionComplete covers this selected import-class closure only; it does not mean all runtime, native-code or shader dependencies were recovered.",
                   "nativeScriptHandling": "/Script references are recorded as native references and are not extracted as asset packages.",
                   "candidateEvidenceScope": "Every character-folder ParticleSystem candidate has evidence, even when --include-unbound is off and that candidate is outside the selected closure.",
               },
               "limitations": ["EffectID is not a move ID. No attack/impact/staging timing or trigger mapping is inferred.",
                               "Folder membership and package imports do not prove runtime use.",
                               "Only serialized values are reported. Engine defaults and native export tails are not reconstructed.",
                               "Treat any subtree with unsupported/error/partial tags as untrusted decoding, including apparent property names/types encountered while parsing unsupported native structs.",
                               "Exit code 0 and zero extraction errors mean the selected dependency closure was read/resolved; they do not imply complete property decoding or runtime recovery."],
               "inventory": [], "managers": [], "commonBindings": [], "packages": [],
               "dependencies": [], "unresolved": [], "errors": []}
    with Source(str(game), TEKKEN_ASSET_KEY) as source:
        if not source.paks:
            parser.error("This provenance helper requires original pak archives, not an unpacked directory")
        entries = {path.lower(): entry for path, entry in source.paks.files.items()}
        summary["archives"] = [{"name": p.name, "bytes": Path(p.path).stat().st_size} for p in source.paks.paks]
        cache = {}

        def header(entry):
            data = entry.read_range(0, min(65536, entry.uncompressed_size), TEKKEN_ASSET_KEY)
            size = header_size(data)
            if size > len(data):
                data = entry.read_range(0, size, TEKKEN_ASSET_KEY)
            return Package(data, entry.path, entry.uncompressed_size)

        def load(entry):
            key = entry.path.lower()
            if key in cache:
                return cache[key]
            data = entry.read(TEKKEN_ASSET_KEY, verify=True)
            package = Package(data, entry.path)
            objects = []
            for index, exp in enumerate(package.exports, 1):
                props = Properties(package)
                reader = package.reader(exp.serial_offset)
                obj = {"index": index, "name": exp.object_name, "class": exp.class_name,
                       "outer": exp.outer_index, "offset": exp.serial_offset, "size": exp.serial_size}
                try:
                    obj["properties"] = props.read(reader, exp.serial_offset + exp.serial_size)
                    obj["unparsedExportTailBytes"] = exp.serial_offset + exp.serial_size - reader.pos
                except Exception as error:
                    obj["parseError"] = str(error)
                obj["tags"] = props.tags
                objects.append(obj)
            evidence = {"path": entry.path, "pak": entry.pak.name, "pakEntryOffset": entry.offset,
                        "size": len(data), "sha256": digest(data), "storedSha1": entry.sha1.hex(),
                        "storedPayloadVerified": True, "objects": objects,
                        "imports": [{"index": -i, "class": imp.class_name, "name": imp.object_name,
                                     "path": object_path(package, -i)} for i, imp in enumerate(package.imports, 1)]}
            relative = "evidence/" + Path(entry.path).stem + "-" + digest(entry.path.encode())[:12] + ".json"
            write_json(output / relative, evidence)
            node = {k: evidence[k] for k in ["path", "pak", "pakEntryOffset", "size", "sha256", "storedSha1", "storedPayloadVerified"]}
            node.update(evidence=relative, classes=dict(Counter(o["class"] for o in objects)),
                        propertyStatus=dict(Counter(t["status"] for o in objects for t in o["tags"])),
                        exportParseErrors=[{"object": o["name"], "error": o["parseError"]} for o in objects if "parseError" in o])
            cache[key] = (evidence, node)
            return evidence, node

        managers = []
        candidates = []
        for key, entry in sorted(entries.items()):
            if not key.startswith(prefix) or not key.endswith(".uasset"):
                continue
            try:
                pkg = header(entry)
                classes = Counter(e.class_name for e in pkg.exports)
                row = {"path": entry.path, "pak": entry.pak.name, "classes": dict(classes), "bytes": entry.uncompressed_size}
                if "ParticleSystem" in classes:
                    evidence, node = load(entry)
                    row.update(sha256=node["sha256"], storedPayloadVerified=True, evidence=node["evidence"], membership="character-folder candidate; binding not yet established")
                    candidates.append(entry.path)
                if "BlueprintGeneratedClass" in classes and any(e.object_name.startswith("Default__") and "effectmanager" in e.class_name.lower() for e in pkg.exports):
                    managers.append(entry.path)
                summary["inventory"].append(row)
            except Exception as error:
                summary["errors"].append({"path": entry.path, "stage": "character inventory", "error": str(error)})
        if not managers:
            summary["errors"].append({"stage": "manager discovery", "error": "No character effect-manager default object found in the exact character folder"})
        roots = set(managers)
        bound = set()
        character_bound, common_bound = set(), set()
        common_path = "TekkenGame/Content/Effect/TK75/Common/BP_CommonEffectManager.uasset"
        inspect = list(managers)
        if args.include_common:
            inspect.append(common_path)
            roots.add(common_path)
        for path in inspect:
            entry = entries.get(path.lower())
            if not entry:
                summary["unresolved"].append({"path": path, "reason": "manager package absent"})
                continue
            evidence, node = load(entry)
            for obj in evidence["objects"]:
                if not obj["name"].startswith("Default__"):
                    continue
                props = obj.get("properties", {})
                record = {"source": path, "sourceSha256": node["sha256"], "evidence": node["evidence"],
                          "export": obj["name"], "class": obj["class"], "properties": props,
                          "tags": obj["tags"], "effectIdMeaning": "Original manager record identifier; not a move ID"}
                summary["managers"].append(record)
                for ref in references(props):
                    dest = package_path(ref["path"])
                    if dest and ref["class"] in DEPENDENCY_CLASSES:
                        roots.add(dest)
                        if ref["class"] == "ParticleSystem":
                            bound.add(dest.lower())
                            (common_bound if path == common_path else character_bound).add(dest.lower())
                if path == common_path:
                    for field, ref in props.items():
                        if isinstance(ref, dict) and ref.get("class") == "ParticleSystem":
                            summary["commonBindings"].append({"role": field, "reference": ref,
                                                              "source": path, "sourceSha256": node["sha256"],
                                                              "tag": next((t for t in obj["tags"] if t["path"] == field), None)})
        for row in summary["inventory"]:
            if row.get("sha256"):
                row["membership"] = "explicit manager particle reference" if row["path"].lower() in bound else "character-folder candidate without an inspected default binding"
        if args.include_unbound:
            roots.update(candidates)
        queue, visited = deque(sorted(roots)), set()
        skipped_classes, included_classes = Counter(), Counter()
        native_count, native_paths = 0, set()
        while queue:
            path = queue.popleft()
            if path.lower() in visited:
                continue
            visited.add(path.lower())
            entry = entries.get(path.lower())
            if not entry:
                summary["unresolved"].append({"path": path, "reason": "no matching full virtual path in effective archives"})
                continue
            try:
                evidence, node = load(entry)
                node = dict(node)
                node["selectedRoot"] = path in roots
                if args.extract:
                    target = output / "packages" / Path(entry.path)
                    if not target.resolve().is_relative_to((output / "packages").resolve()):
                        raise ValueError("Unsafe virtual output path")
                    data = entry.read(TEKKEN_ASSET_KEY, verify=True)
                    assert digest(data) == node["sha256"]
                    write_safe(target, data)
                    node["extracted"] = target.relative_to(output).as_posix()
                summary["packages"].append(node)
                for imp in evidence["imports"]:
                    if imp["path"].startswith("/Script/"):
                        native_count += 1
                        native_paths.add(imp["path"])
                    if imp["class"] not in DEPENDENCY_CLASSES:
                        skipped_classes[imp["class"]] += 1
                        continue
                    included_classes[imp["class"]] += 1
                    dest = package_path(imp["path"])
                    summary["dependencies"].append({"source": entry.path, "target": dest, "class": imp["class"],
                                                    "reference": imp["path"], "importIndex": imp["index"],
                                                    "resolutionKind": "asset-package" if dest else "native-script-reference-not-extracted" if imp["path"].startswith("/Script/") else "non-asset-reference-not-extracted",
                                                    "evidenceKind": "package import; not proof of runtime use"})
                    if dest:
                        queue.append(dest)
            except FileExistsError:
                raise
            except Exception as error:
                summary["errors"].append({"path": path, "stage": "dependency extraction", "error": str(error)})
        summary["counts"] = {"characterFolderPackages": len(summary["inventory"]),
                             "characterParticleCandidates": len(candidates),
                             "characterManagers": len(managers),
                             "characterExplicitParticleReferences": len(character_bound),
                             "commonExplicitParticleReferences": len(common_bound),
                             "explicitParticleReferences": len(bound),
                             "commonBindings": len(summary["commonBindings"]),
                             "dependencyClosurePackages": len(summary["packages"]),
                             "unresolved": len(summary["unresolved"]), "errors": len(summary["errors"])}
        statuses = sum((Counter(p["propertyStatus"]) for p in summary["packages"]), Counter())
        summary["dependencyTraversal"].update(
            includedImportClassRecordCounts=dict(sorted(included_classes.items())),
            skippedImportClassRecordCounts=dict(sorted(skipped_classes.items())),
            skippedImportRecords=sum(skipped_classes.values()),
            nativeScriptImportRecords=native_count,
            distinctNativeScriptReferences=sorted(native_paths),
            countMeaning="Import record counts across selected closure packages, not unique asset counts. Native references may also be included in skipped-class counts.")
        summary["propertyStatus"] = dict(statuses)
        summary["propertyStatusScope"] = "Tagged properties in selected dependency-closure package exports only; character-folder candidate evidence outside that closure is not included. Native export tails are excluded from these counts."
        summary["dependencyResolutionComplete"] = not summary["errors"] and not summary["unresolved"]
        summary["taggedPropertyDecodingComplete"] = not any(statuses.get(k, 0) for k in ["error", "unsupported", "partial"]) and not any(p["exportParseErrors"] for p in summary["packages"])
        summary["nativeExportTailsFullyDecoded"] = False
        write_json(output / "character-effects.json", summary)
    print(json.dumps({"report": str(output / "character-effects.json"), **summary["counts"],
                      "dependencyResolutionComplete": summary["dependencyResolutionComplete"],
                      "taggedPropertyDecodingComplete": summary["taggedPropertyDecodingComplete"],
                      "propertyStatus": summary["propertyStatus"]}), flush=True)
    return 0 if summary["dependencyResolutionComplete"] else 2


if __name__ == "__main__":
    raise SystemExit(main())
