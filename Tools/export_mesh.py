"""Convert a supported cooked mesh to a static reference-pose OBJ for Unity.

Uses the existing Asset Workshop decoder. No bones, skin weights, UVs,
textures, materials, or animation are recovered by this geometry exporter.
"""
import argparse
import hashlib
import json
from pathlib import Path
import sys


def export(item, output):
    from t7extract.package import Package
    from t7extract.mesh import extract_mesh, MAX_PACKAGE_BYTES
    if item.size > MAX_PACKAGE_BYTES:
        raise ValueError("Package exceeds the 64 MiB geometry limit")
    raw = item.read()
    package = Package(raw, item.path)
    meshes = [i for i, e in enumerate(package.exports) if e.class_name in ("SkeletalMesh", "StaticMesh")]
    if len(meshes) != 1:
        raise ValueError("Select a package containing exactly one supported mesh export")
    mesh = extract_mesh(package, meshes[0])
    output = Path(output)
    output.parent.mkdir(parents=True, exist_ok=True)
    if output.exists() or output.with_suffix(".source.json").exists():
        raise FileExistsError("Choose a new output name; existing exports are preserved")
    indices = mesh["indices"]
    sections = mesh.get("sections") or [{"first_index": 0, "index_count": len(indices)}]
    count = 0
    with output.open("x", encoding="utf-8", newline="\n") as stream:
        stream.write("# Static reference pose only; UE centimeters converted to meters\n")
        stream.write("o " + output.stem + "\n")
        for x, y, z in mesh["positions"]:
            stream.write(f"v {y / 100:.8g} {z / 100:.8g} {x / 100:.8g}\n")
        for section in sections:
            if section.get("disabled"):
                continue
            start, length = section["first_index"], section["index_count"]
            for i in range(start, start + length, 3):
                a, b, c = indices[i:i + 3]
                stream.write(f"f {a + 1} {b + 1} {c + 1}\n")
                count += 1
    manifest = {"source": item.path, "source_sha256": hashlib.sha256(raw).hexdigest(),
                "obj_sha256": hashlib.sha256(output.read_bytes()).hexdigest(),
                "vertices": mesh["vertex_count"], "triangles": count,
                "coordinate_mapping": "OBJ x=UE y/100, y=UE z/100, z=UE x/100",
                "reference_pose_only": True, "has_rig": False, "has_uvs": False,
                "notes": "Geometry reference. Retargeted animation, rig, UVs and materials require separate conversion."}
    output.with_suffix(".source.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("asset", help="Exact virtual .uasset path")
    parser.add_argument("output", type=Path)
    parser.add_argument("--tool", type=Path, help="Folder containing the t7extract Python package")
    parser.add_argument("--source", help="Original game folder or pak")
    args = parser.parse_args()
    configuration = Path(__file__).with_name("source-paths.json")
    defaults = json.loads(configuration.read_text()) if configuration.exists() else {}
    tool = args.tool or defaults.get("asset_workshop")
    source = args.source or defaults.get("game")
    if not tool or not source:
        parser.error("Set --tool and --source, or edit Tools/source-paths.json")
    sys.path.insert(0, str(tool))
    from t7extract.cli import Source
    from t7extract.pak import TEKKEN_ASSET_KEY
    with Source(str(source), TEKKEN_ASSET_KEY) as archive:
        print(json.dumps(export(archive.get(args.asset), args.output), indent=2))


if __name__ == "__main__":
    main()
