# Ease headless test: builds a known scene, exports via the addon, prints proof.
#
# Run AFTER installing Blender (do NOT use brew — manual install):
#   /Applications/Blender.app/Contents/MacOS/Blender --background \
#       --python "/Users/sohamanand/BlueSky Engine/Blender/test_ease_export.py"
#
# Then decode the pack with the engine test suite (StrataPackTests) or:
#   BlueSkyEngine --export-sample-pack ... (reference only; live pack differs)
#
# Expected: default cube + red Principled (metal 0.5, rough 0.25) + generated
# 64x64 test image on Base Color → /tmp/ease_live.stratapack with 1 mesh,
# 1 surface, 2 textures (albedo + RMA).

import bpy
import os
import sys
import tempfile
from pathlib import Path

ADDON_PATH = Path(__file__).resolve().with_name("strata_pack_exporter.py")
OUT_PATH = str(Path(tempfile.gettempdir()) / "ease_live.stratapack")


def load_addon():
    import importlib.util
    spec = importlib.util.spec_from_file_location("strata_pack_exporter", str(ADDON_PATH))
    mod = importlib.util.module_from_spec(spec)
    sys.modules["strata_pack_exporter"] = mod
    spec.loader.exec_module(mod)
    return mod


def build_scene():
    # Clean slate.
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)

    # Cube with known transform (identity — world bake must equal local).
    bpy.ops.mesh.primitive_cube_add(size=2.0, location=(0, 0, 0))
    cube = bpy.context.active_object
    cube.name = "EaseCube"

    # Principled material with KNOWN values.
    mat = bpy.data.materials.new("EaseRed")
    # NOTE: use_nodes defaults True on new materials (setting it is
    # deprecated in Blender 5.x, removal scheduled for 6.0).
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (1.0, 0.0, 0.0, 1.0)
    bsdf.inputs["Metallic"].default_value = 0.5
    bsdf.inputs["Roughness"].default_value = 0.25

    # Generated 64x64 test image on Base Color (red gradient + green corner).
    img = bpy.data.images.new("EaseTest", 64, 64)
    px = []
    for y in range(64):
        for x in range(64):
            px += [x / 63.0, y / 63.0, 0.25, 1.0]
    img.pixels.foreach_set(px)
    img.file_format = 'PNG'
    img.pack()
    tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
    tex.image = img
    mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])

    cube.data.materials.append(mat)
    cube.select_set(True)
    bpy.context.view_layer.objects.active = cube
    return cube


def test_armature_pose_round_trip(mod):
    arm_data = bpy.data.armatures.new("EaseTestRig")
    arm_obj = bpy.data.objects.new("EaseTestRig", arm_data)
    bpy.context.collection.objects.link(arm_obj)
    bpy.ops.object.select_all(action='DESELECT')
    arm_obj.select_set(True)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode='EDIT')
    bone = arm_data.edit_bones.new("Root")
    bone.head = (0, 0, 0)
    bone.tail = (0, 0, 1)
    bpy.ops.object.mode_set(mode='OBJECT')

    mesh = bpy.data.meshes.new("EaseSkinnedMesh")
    mesh.from_pydata([(0, 0, 0), (1, 0, 0), (0, 1, 0)], [], [(0, 1, 2)])
    obj = bpy.data.objects.new("EaseSkinnedMesh", mesh)
    bpy.context.collection.objects.link(obj)
    modifier = obj.modifiers.new("Armature", 'ARMATURE')
    modifier.object = arm_obj
    pose_bone = arm_obj.pose.bones["Root"]
    pose_bone.rotation_euler[2] = 0.4
    bpy.context.view_layer.update()
    original = [list(row) for row in pose_bone.matrix_basis]

    warnings = []
    arm_info, saved = mod.neutralize_armature(obj, bpy.context.evaluated_depsgraph_get(), warnings)
    assert arm_info is not None and saved, f"armature not detected: {warnings}"
    assert abs(pose_bone.matrix_basis[0][0] - 1.0) < 1e-5, "pose did not return to bind basis"
    mod.restore_armature(obj, saved)
    restored = [list(row) for row in pose_bone.matrix_basis]
    assert all(abs(original[r][c] - restored[r][c]) < 1e-5
               for r in range(4) for c in range(4)), "pose was not restored"
    print("[EaseTest] armature pose neutralize/restore: PASSED")


def main():
    mod = load_addon()
    build_scene()
    mod.register()
    try:
        issues = mod.inspect_scene(bpy.context, use_selection=True)
        errors = [i for i in issues if i["severity"] == "ERROR"]
        assert not errors, f"preflight errors: {errors}"
        assert hasattr(bpy.types.Scene, "ease_settings"), "panel settings were not registered"
        print(f"[EaseTest] preflight={len(issues)} issue(s), no blocking errors")
    finally:
        mod.unregister()
    test_armature_pose_round_trip(mod)

    warnings = []
    # Call the core directly (no operator context needed).
    import types

    class FakeOp:
        use_selection = True
        global_scale = 1.0
        axis_forward = '-Z'
        axis_up = 'Y'

    class FakeContext:
        def __init__(self):
            self._ctx = bpy.context
            self.selected_objects = bpy.context.selected_objects
            self.scene = bpy.context.scene

        def evaluated_depsgraph_get(self):
            return bpy.context.evaluated_depsgraph_get()

    fake = FakeContext()
    mod.export_stratapack(FakeOp(), fake, OUT_PATH, warnings)
    print(f"[EaseTest] pack written: {OUT_PATH} ({os.path.getsize(OUT_PATH)} bytes)")
    for w in warnings:
        print(f"[EaseTest] WARNING: {w}")

    # Print the JSON header for eyeball verification.
    import struct
    import json
    data = open(OUT_PATH, "rb").read()
    assert data[:8] == b"STRATAPK", "bad magic"
    ver, hs, ps = struct.unpack("<IQQ", data[8:28])
    header = json.loads(data[28:28 + hs].decode())
    assert len(data) == 28 + hs + ps, "header/payload lengths do not match file size"
    print(f"[EaseTest] version={ver} meshes={len(header['meshes'])} "
          f"surfaces={len(header['surfaces'])} textures={len(header['textures'])}")
    for m in header["meshes"]:
        print(f"[EaseTest] mesh={m['name']} verts={m['vertexCount']} "
              f"idx={m['indexCount']} submeshes={len(m['submeshes'])}")
    for s in header["surfaces"]:
        print(f"[EaseTest] surface slot={s['slot']} metallic={s['metallic']} "
              f"roughness={s['roughness']}")
    print("[EaseTest] DONE")


main()
