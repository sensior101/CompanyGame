"""Read-only FBX round-trip checks, run with Blender --background --python.

No model is saved, exported, or modified on disk. Only the requested JSON report
is written. Full-pack validation expects eight individually authored buildings.
"""
import argparse
import json
import math
import sys
from pathlib import Path

import bpy


EXPECTED_IDS = {f"CB_{number:02d}" for number in range(1, 9)}
SIGNS = ("Sign_Main_01", "Sign_Main_02", "Sign_Corner", "Sign_Side")
REQUIRED = SIGNS + ("Door_Leaf_01", "Door_Leaf_02", "EntryAnchor", "InteriorSpawnAnchor")


def finite(values):
    return all(isinstance(v, (int, float)) and math.isfinite(v) for v in values)


def check(report, name, passed, detail=""):
    report["checks"].append({"name": name, "pass": bool(passed), "detail": detail})


def inside(path, directory):
    try:
        path.resolve().relative_to(directory.resolve())
        return True
    except ValueError:
        return False


def descendant(obj, ancestor):
    parent = obj.parent
    while parent is not None:
        if parent == ancestor:
            return True
        parent = parent.parent
    return False


def uv_area(layer, polygon):
    points = [layer.data[index].uv for index in polygon.loop_indices]
    if not all(finite(p) for p in points):
        return 0.0
    return abs(sum(a.x * b.y - b.x * a.y for a, b in zip(points, points[1:] + points[:1]))) * 0.5


def check_uv(report, obj, face_indices=None, normalized=False):
    mesh = obj.data
    if not mesh.uv_layers:
        check(report, "uv1:" + obj.name, False, "No first UV channel")
        return
    layer = mesh.uv_layers[0]
    polygons = list(mesh.polygons) if face_indices is None else [mesh.polygons[i] for i in face_indices]
    values = [layer.data[index].uv for polygon in polygons for index in polygon.loop_indices]
    valid = bool(values) and all(finite(point) for point in values)
    noncollapsed = valid and all(uv_area(layer, polygon) > 1e-10 for polygon in polygons)
    if normalized:
        valid = valid and all(-0.001 <= value <= 1.001 for point in values for value in point)
    check(report, "uv1:" + obj.name, valid and noncollapsed,
          f"First layer {layer.name}; {len(polygons)} checked faces; normalized={normalized}")


def material_inputs(material):
    node = next((n for n in material.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None) if material.use_nodes else None
    color = list(node.inputs["Base Color"].default_value) if node else list(material.diffuse_color)
    images = [n.image for n in material.node_tree.nodes if n.type == "TEX_IMAGE" and n.image] if material.use_nodes else []
    return color, images


def validate_materials(report, row, meshes, exports):
    specs = row.get("materials", [])
    if not isinstance(specs, list) or not specs:
        check(report, "material_metadata", False, "No material specifications")
        return set()
    by_name = {s.get("name"): s for s in specs if isinstance(s, dict) and s.get("name")}
    check(report, "material_metadata", len(by_name) == len(specs), "Material names must be present and unique")
    used = {slot.material.name: slot.material for obj in meshes for slot in obj.material_slots if slot.material}
    transparent = {name for name, spec in by_name.items() if spec.get("unitySurface") == "Transparent"}
    check(report, "glass_metadata", bool(transparent), "Transparent glass must be declared in the source catalog")
    check(report, "glass_used", bool(set(used) & transparent), "At least one declared glass material must occur in the FBX")

    for name, spec in by_name.items():
        color = spec.get("color", [])
        numbers = [spec.get("metallic", 0), spec.get("roughness", 0), spec.get("unityAlpha", 1)]
        valid = len(color) >= 3 and finite(color) and finite(numbers)
        check(report, "material_spec:" + name, valid, "Source colors and material parameters must be finite")
        if name in transparent:
            alpha = spec.get("unityAlpha", 1)
            check(report, "glass_alpha:" + name, finite([alpha]) and 0 < alpha < 1, "Unity alpha must be between zero and one")
        texture = spec.get("texture")
        if texture:
            path = (exports / texture).resolve()
            check(report, "texture_file:" + name, inside(path, exports / "Textures") and path.is_file(), str(path))

    for name, material in used.items():
        spec = by_name.get(name)
        check(report, "material_declared:" + name, spec is not None, "FBX material names must map to source metadata")
        if spec is None:
            continue
        color, images = material_inputs(material)
        expected = spec.get("color", [])
        valid = len(expected) >= 3 and finite(expected[:3]) and finite(color[:3])
        deviation = max(abs(a - b) for a, b in zip(color[:3], expected[:3])) if valid else None
        check(report, "material_color:" + name, deviation is not None and deviation <= 0.035,
              "Maximum linear RGB deviation from metadata: " + str(deviation))
        spec_texture = spec.get("texture")
        if spec_texture:
            expected_name = Path(spec_texture).name.casefold()
            check(report, "texture_connected:" + name,
                  any(Path(bpy.path.abspath(image.filepath)).name.casefold() == expected_name for image in images),
                  "Expected image node: " + expected_name)
        for image in images:
            path = Path(bpy.path.abspath(image.filepath)).resolve() if image.filepath else None
            check(report, "texture_reference:" + name + ":" + image.name,
                  image.packed_file is not None or (path is not None and path.is_file()),
                  "Packed image" if image.packed_file else str(path))
    return transparent


def validate_block(row, catalog_dir, exports):
    ident = row.get("id", "unknown")
    report = {"id": ident, "checks": []}
    fbx = (catalog_dir / row.get("fbx", "")).resolve()
    expected_path = (exports / (ident + ".fbx")).resolve()
    valid_path = fbx == expected_path and fbx.is_file()
    check(report, "fbx_file", valid_path, str(fbx))
    if not valid_path:
        report["pass"] = False
        return report
    try:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.context.scene.unit_settings.system = "METRIC"
        bpy.context.scene.unit_settings.scale_length = 1.0
        bpy.ops.import_scene.fbx(filepath=str(fbx), global_scale=1.0, use_custom_props=True, use_image_search=False)
        objects = list(bpy.context.scene.objects)
        meshes = [obj for obj in objects if obj.type == "MESH"]
        names = {obj.name for obj in objects}
        check(report, "meshes", bool(meshes), f"{len(meshes)} mesh objects")
        forbidden = [obj.name for obj in objects if obj.type in {"LIGHT", "CAMERA"} or
                     any(word in obj.name for word in ("Presentation_ONLY", "StudioGround", "OverviewCamera", "FrontFill"))]
        check(report, "presentation_excluded", not forbidden, ", ".join(forbidden))
        for name in REQUIRED:
            check(report, "node:" + name, name in names, "Required separate transform")
        for name in ("Door_Leaf_01", "Door_Leaf_02"):
            door = bpy.data.objects.get(name)
            children = [obj for obj in meshes if door is not None and descendant(obj, door)]
            check(report, "door_meshes:" + name, bool(children), "Door geometry must remain parented to the independent leaf")

        invalid_transforms = [obj.name for obj in objects if not finite([v for row_values in obj.matrix_world for v in row_values]) or
                              not finite(obj.scale) or abs(obj.matrix_world.determinant()) < 1e-12]
        check(report, "transforms_finite_nonsingular", not invalid_transforms, ", ".join(invalid_transforms))
        vertices = []
        bad_vertices = 0
        for obj in meshes:
            for vertex in obj.data.vertices:
                point = obj.matrix_world @ vertex.co
                if finite(point):
                    vertices.append(tuple(point))
                else:
                    bad_vertices += 1
        check(report, "finite_vertices", not bad_vertices and bool(vertices), f"{len(vertices)} finite, {bad_vertices} invalid vertices")
        if vertices:
            actual_min = [min(point[axis] for point in vertices) for axis in range(3)]
            actual_max = [max(point[axis] for point in vertices) for axis in range(3)]
            report["actualBounds"] = {"min": actual_min, "max": actual_max}
            metadata = row.get("exportBounds", {})
            expected_min, expected_max = metadata.get("min", []), metadata.get("max", [])
            valid = len(expected_min) == len(expected_max) == 3 and finite(expected_min + expected_max)
            valid = valid and all(b > a for a, b in zip(expected_min, expected_max))
            check(report, "bounds_metadata", valid, "Requires actual exported world bounds; nominal width/depth are not substituted")
            if valid:
                # Three centimetres or 0.2% allows serialization noise, not metre/centimetre scale errors.
                tolerance = [max(0.03, (b - a) * 0.002) for a, b in zip(expected_min, expected_max)]
                match = all(abs(a - b) <= t for a, b, t in zip(actual_min, expected_min, tolerance)) and \
                        all(abs(a - b) <= t for a, b, t in zip(actual_max, expected_max, tolerance))
                check(report, "bounds_and_metre_scale", match, "Blender-coordinate AABB compared with source exportBounds")
        triangle_count = sum(len(poly.vertices) - 2 for obj in meshes for poly in obj.data.polygons)
        check(report, "triangle_count", triangle_count == row.get("triangles"),
              f"Imported {triangle_count}; source {row.get('triangles')}")
        transparent = validate_materials(report, row, meshes, exports)
        textured = {spec.get("name") for spec in row.get("materials", []) if isinstance(spec, dict) and spec.get("texture")}
        for obj in meshes:
            mesh = obj.data
            check(report, "uv1_exists:" + obj.name, bool(mesh.uv_layers), "First UV channel is required")
            if mesh.uv_layers:
                check(report, "uv1_finite:" + obj.name, all(finite(loop.uv) for loop in mesh.uv_layers[0].data), "First UV channel values")
            target_faces = [poly.index for poly in mesh.polygons if poly.material_index < len(obj.material_slots) and
                            obj.material_slots[poly.material_index].material is not None and
                            obj.material_slots[poly.material_index].material.name in transparent | textured]
            if target_faces:
                check_uv(report, obj, target_faces)

        sign_specs = {sign.get("name"): sign for sign in row.get("signs", []) if isinstance(sign, dict)}
        check(report, "sign_metadata", set(SIGNS) <= set(sign_specs), "Four sign entries are required")
        for name in SIGNS:
            anchor = bpy.data.objects.get(name)
            boards = [obj for obj in meshes if obj.name.startswith(name + "_Board") and anchor is not None and descendant(obj, anchor)]
            check(report, "sign_board:" + name, len(boards) == 1, "Exactly one board must remain below its sign anchor")
            if not boards:
                continue
            board = boards[0]
            check_uv(report, board, normalized=True)
            spec = sign_specs.get(name, {})
            expected = spec.get("position", [])
            actual = list(anchor.matrix_world.translation)
            valid = len(expected) == 3 and finite(expected)
            check(report, "sign_position:" + name, valid and max(abs(a - b) for a, b in zip(actual, expected)) <= 0.02,
                  "World anchor position compared with source metadata")
            points = [anchor.matrix_world.inverted() @ board.matrix_world @ vertex.co for vertex in board.data.vertices]
            width = max(point.x for point in points) - min(point.x for point in points)
            height = max(point.z for point in points) - min(point.z for point in points)
            dims = [spec.get("width", 0), spec.get("height", 0)]
            valid = finite(dims) and all(value > 0 for value in dims)
            check(report, "sign_dimensions:" + name, valid and abs(width - dims[0]) <= 0.02 and abs(height - dims[1]) <= 0.02,
                  f"Local X/Z board dimensions: {width:.4f} × {height:.4f} m")
    except Exception as exception:
        check(report, "exception", False, f"{type(exception).__name__}: {exception}")
    report["pass"] = all(item["pass"] for item in report["checks"])
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--catalog", required=True, type=Path)
    parser.add_argument("--report", required=True, type=Path)
    parser.add_argument("--allow-partial", action="store_true", help="Validate only currently listed models; never claim full-pack completion")
    arguments = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    args = parser.parse_args(arguments)
    catalog_path = args.catalog.resolve()
    report_path = args.report.resolve()
    pack_root = catalog_path.parent.parent
    if not inside(report_path, pack_root / "QA"):
        raise ValueError("The report must be inside this pack's QA folder to avoid overwriting an asset or source file")
    report = {"packComplete": False, "scope": "partial" if args.allow_partial else "complete", "checks": [], "items": []}
    try:
        catalog = json.loads(catalog_path.read_text(encoding="utf-8-sig"))
        rows = catalog.get("items", [])
        ids = [row.get("id") for row in rows if isinstance(row, dict)]
        valid_ids = len(ids) == len(rows) and bool(ids) and len(set(ids)) == len(ids) and set(ids) <= EXPECTED_IDS
        report["packComplete"] = valid_ids and set(ids) == EXPECTED_IDS
        check(report, "catalog_ids", valid_ids, "Unique CB_01–CB_08 identifiers only")
        check(report, "catalog_scope", args.allow_partial or report["packComplete"], "Full-pack mode requires all eight individually authored models")
        check(report, "catalog_revision", all(isinstance(row, dict) and isinstance(row.get("revision"), int) and row["revision"] >= 2 for row in rows), "Revision 2 or later")
        if valid_ids:
            exports = (pack_root / "Exports").resolve()
            for row in rows:
                item = validate_block(row, catalog_path.parent, exports)
                report["items"].append(item)
                print("EXPORT_CHECK", item["id"], "PASS" if item["pass"] else "FAIL", flush=True)
    except Exception as exception:
        check(report, "catalog_exception", False, f"{type(exception).__name__}: {exception}")
    report["pass"] = bool(report["items"]) and all(item["pass"] for item in report["checks"] + report["items"])
    # Listing eight rows is not evidence that eight valid artifacts exist.
    report["packComplete"] = report["packComplete"] and report["pass"] and not args.allow_partial
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2, allow_nan=False), encoding="utf-8")
    print("EXPORT_VALIDATION", "PASS" if report["pass"] else "FAIL", "packComplete=" + str(report["packComplete"]), str(report_path), flush=True)
    if not report["pass"]:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
