"""Audit recorded original-package evidence and the existing Unity common library.

No archive extraction, Unity edits, trigger inference or visual-parity claim.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def audit(workspace, project, evidence, extra_package_roots=()):
    errors, checked = [], {}
    report = {"scope": "Recorded extraction consistency and Unity asset coverage only",
              "workspace": str(workspace), "project": str(project), "evidence": str(evidence),
              "originalArchivesRead": False, "originalRoleNamesDecoded": False,
              "originalDispatchVerified": False, "visualParityVerified": False,
              "errors": errors}

    def check(condition, message):
        if not condition:
            errors.append(message)

    def verify(path, expected):
        if not path.is_file():
            errors.append("Missing original package: " + str(path))
            return
        if path not in checked:
            checked[path] = hashlib.sha256(path.read_bytes()).hexdigest()
        check(checked[path] == expected, "SHA-256 mismatch: " + str(path))

    def inside(root, relative):
        path = (root / relative).resolve()
        if not path.is_relative_to(root.resolve()):
            raise ValueError("Path escapes input directory: " + str(relative))
        return path

    package_roots = [evidence / "packages", *extra_package_roots]
    report["packageRoots"] = [str(p) for p in package_roots]

    def verify_package(relative, expected):
        candidates = [inside(root, relative) for root in package_roots]
        present = [p for p in candidates if p.is_file()]
        if not present:
            errors.append("Missing original package in supplied roots: " + relative)
        for path in present:
            verify(path, expected)
        return present[0] if present else candidates[0]

    try:
        bindings = read_json(evidence / "bindings.json")
        catalog = read_json(evidence / "catalog.json")
        resources = project / "Assets/_Project/Resources/HitEffects"
        library = read_json(resources / "effects.json")
        manager = verify_package(bindings["manager"], bindings["managerSha256"])
        raw = manager.read_bytes()
        bound = {}
        for row in bindings["bindings"]:
            role = row["role"]
            check(role not in bound, "Duplicate manager role: " + role)
            bound[role] = row
            offset = row["offset"]
            valid = isinstance(offset, int) and 0 <= offset <= len(raw) - 4
            check(valid, "Invalid binding offset: " + role)
            if valid:
                check(struct.unpack_from("<i", raw, offset)[0] == row["index"],
                      "Binding index mismatch: " + role)
            verify_package(row["path"], row["sha256"])

        systems, exported = {}, {}
        for system in library["systems"]:
            name = system["name"]
            check(name not in systems, "Duplicate exported system: " + name)
            systems[name] = system
        for role in library["roles"]:
            name, system = role["role"], role["system"]
            check(name not in exported, "Duplicate exported role: " + name)
            exported[name] = system
            check(system in systems, "Role has no exported system: " + name)
            if name in bound:
                check(system == bound[name]["name"], "Role binding mismatch: " + name)
                if system in systems:
                    check(systems[system]["sha256"] == bound[name]["sha256"],
                          "Role source hash mismatch: " + name)

        textures, meshes = set(), set()
        for name, system in systems.items():
            matches = [r for r in catalog if r["name"] == name
                       and r["sha256"] == system["sha256"]]
            check(len(matches) == 1, "Source catalog match missing or ambiguous: " + name)
            if len(matches) == 1:
                verify_package(matches[0]["path"], system["sha256"])
            for emitter in system["emitters"]:
                for key, folder, extension, seen in (("texture", "Textures", ".png", textures),
                                                       ("mesh", "Meshes", ".json", meshes)):
                    asset = emitter.get(key)
                    if asset:
                        path = inside(resources / folder, asset + extension)
                        check(path.is_file(), "Missing Unity " + key + ": " + str(path))
                        seen.add(asset)

        report.update(managerSha256=bindings["managerSha256"],
                      managerBindings=len(bound), exportedRoles=len(exported),
                      exportedManagerRoles=len(bound.keys() & exported.keys()),
                      unexportedManagerRoles=sorted(bound.keys() - exported.keys()),
                      nonManagerAliases=[{"role": r, "system": exported[r],
                                          "triggerStatus": "not checked by this audit"}
                                         for r in sorted(exported.keys() - bound.keys())],
                      systems=len(systems), emitterRecords=sum(len(s["emitters"]) for s in systems.values()),
                      referencedTextures=len(textures), referencedMeshes=len(meshes),
                      conversionLimitations=library.get("limitations", []))
    except (OSError, ValueError, KeyError, TypeError, struct.error) as exc:
        errors.append(type(exc).__name__ + ": " + str(exc))
    report["packageFilesChecked"] = len(checked)
    report["passed"] = not errors
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--workspace", type=Path, required=True)
    parser.add_argument("--project", type=Path, default=Path("UnityRemaster/TekkenVersusRemaster"))
    parser.add_argument("--evidence", type=Path, default=Path("Artifacts/BattleEffects"))
    parser.add_argument("--package-root", type=Path, action="append", default=[],
                        help="Additional extracted package root containing TekkenGame/; repeatable, all copies hash-checked")
    parser.add_argument("--out", type=Path, required=True, help="New local JSON report; existing files are not overwritten")
    args = parser.parse_args()
    workspace = args.workspace.resolve()
    resolve = lambda path: (workspace / path).resolve()
    destination = resolve(args.out)
    if destination.exists():
        parser.error("Choose a new report path; output already exists: " + str(destination))
    result = audit(workspace, resolve(args.project), resolve(args.evidence),
                   [resolve(p) for p in args.package_root])
    destination.parent.mkdir(parents=True, exist_ok=True)
    with destination.open("x", encoding="utf-8") as stream:
        json.dump(result, stream, indent=2)
        stream.write("\n")
    print(json.dumps({key: result.get(key) for key in
                      ("passed", "managerBindings", "exportedManagerRoles", "exportedRoles", "systems", "errors")}))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
