# BlueSky Engine Ease — StrataPack exporter
#
# Install: Edit > Preferences > Add-ons > Install from Disk > this file.
# Use: 3D View > Sidebar > BlueSky for preview, checks, and export.
#
# Writes: magic STRATAPK (8B, no NUL — matches StrataPack.cs exactly) +
# u32 version(1) + u64 header_size + u64 payload_size + JSON + payload.
#
# Scope: static + ARMATURE-skinned meshes, Principled scalars + direct image links,
# Cycles-baked complex surface color graphs, numpy RMA pack, world-baked transforms.
# Not handled: vertex AO, procedural non-color channels, sequences/UDIM,
# lightmaps. Unsupported roughness/metallic graphs keep scalar fallbacks.

bl_info = {
    "name": "BlueSky Engine Ease",
    "author": "BlueSky Engine",
    "version": (0, 3, 0),
    "blender": (4, 0, 0),
    "location": "View3D > Sidebar > BlueSky",
    "description": "Bake Blender node colors, preview, validate, and export for BlueSky Engine",
    "category": "Import-Export",
}

import bpy
import json
import math
import os
import struct
import tempfile
import time
from bpy_extras.io_utils import ExportHelper, orientation_helper, axis_conversion
from mathutils import Matrix, Vector


# --------------------------------------------------------------------------
# Mesh gathering (evaluated depsgraph, triangulated, deduped)
# --------------------------------------------------------------------------

def gather_tris(mesh_eval):
    mesh_eval.calc_loop_triangles()
    uv_layer = mesh_eval.uv_layers.active
    groups = {}
    for tri in mesh_eval.loop_triangles:
        groups.setdefault(tri.material_index, []).append(tri)
    return groups, uv_layer


def emit_object(obj_eval, mesh_eval, global_matrix, flip_winding):
    """Returns (positions, normals, uvs, indices, submeshes, vindices). Bakes world transform.
    vindices parallels positions: original mesh vertex index per exported vertex
    (for skin-weight lookup; -1 if untraceable)."""
    groups, uv_layer = gather_tris(mesh_eval)
    nmat = global_matrix.to_3x3().normalized()

    positions, normals, uvs, indices = [], [], [], []
    vindices = []
    vert_map = {}
    submeshes = []

    for mat_index in sorted(groups.keys()):
        start = len(indices)
        for tri in groups[mat_index]:
            order = (2, 1, 0) if flip_winding else (0, 1, 2)
            corners = [(tri.loops[k], tri.vertices[k]) for k in order]
            for li, vi in corners:
                co = mesh_eval.vertices[vi].co
                # Blender UVs: origin bottom-left. Engine expects V=0 at top
                # (same convention as the OBJ path) — flip once, here.
                raw_uv = uv_layer.data[li].uv if uv_layer is not None else (0.0, 0.0)
                uv = (raw_uv[0], 1.0 - raw_uv[1])
                n = mesh_eval.loops[li].normal
                key = (round(co.x, 6), round(co.y, 6), round(co.z, 6),
                       round(uv[0], 6), round(uv[1], 6),
                       round(n.x, 5), round(n.y, 5), round(n.z, 5))
                idx = vert_map.get(key)
                if idx is None:
                    idx = len(positions)
                    vert_map[key] = idx
                    wp = global_matrix @ obj_eval.matrix_world @ co
                    wn = (nmat @ obj_eval.matrix_world.to_3x3() @ n).normalized()
                    positions.append((wp.x, wp.y, wp.z))
                    normals.append((wn.x, wn.y, wn.z))
                    uvs.append((uv[0], uv[1]))
                    vindices.append(vi)
                indices.append(idx)
        submeshes.append({"offset": start, "count": len(indices) - start,
                          "mat_index": mat_index})
    return positions, normals, uvs, indices, submeshes, vindices


# --------------------------------------------------------------------------
# Skeletal (armature + skin weights). Statics skip all of this.
# --------------------------------------------------------------------------

def neutralize_armature(obj, depsgraph, warnings):
    """If obj has an ARMATURE modifier, reset all pose bones to rest and
    update the depsgraph so to_mesh() yields the bind pose. Returns
    (arm_info, saved_pose); (None, None) for static objects. Restore with
    restore_armature (pose matrices are plain float lists)."""
    try:
        mods = obj.modifiers
    except Exception:
        return None, None
    arm_obj = None
    for m in mods:
        if m.type == 'ARMATURE' and m.object is not None:
            arm_obj = m.object
            break
    if arm_obj is None:
        return None, None
    saved = None
    try:
        # Pose data belongs to the armature object, not the skinned mesh.
        saved = {pb.name: [list(row) for row in pb.matrix_basis]
                 for pb in arm_obj.pose.bones}
        ident = Matrix.Identity(4)
        for pb in arm_obj.pose.bones:
            pb.matrix_basis = ident
        depsgraph.update()
        bones = []
        for b in arm_obj.data.bones:
            bones.append({"name": b.name,
                          "parent": b.parent.name if b.parent is not None else None})
        arm_info = {"armature": arm_obj.name, "bones": bones,
                    "obj_matrix": [list(r) for r in obj.matrix_world],
                    "arm_matrix": [list(r) for r in arm_obj.matrix_world]}
        return arm_info, saved
    except Exception as ex:
        # Restore even after a partial reset or depsgraph failure.
        restore_error = None
        if saved:
            restore_error = restore_armature(obj, saved)
            try:
                depsgraph.update()
            except Exception:
                pass
        detail = f"; pose restore also failed ({restore_error})" if restore_error else ""
        warnings.append(f"Object '{obj.name}': pose neutralize failed ({ex}){detail}; weights may mismatch posed mesh")
        if restore_error:
            raise RuntimeError(f"Could not restore '{obj.name}' armature pose: {restore_error}") from ex
        return None, None


def restore_armature(obj, saved_pose):
    if not saved_pose:
        return None
    try:
        arm_obj = obj.find_armature()
        if arm_obj is None:
            arm_obj = next((m.object for m in obj.modifiers
                            if m.type == 'ARMATURE' and m.object is not None), None)
        if arm_obj is None or arm_obj.pose is None:
            return "armature pose is unavailable"
        failed_bones = []
        for pb in arm_obj.pose.bones:
            m = saved_pose.get(pb.name)
            if m is not None:
                try:
                    pb.matrix_basis = Matrix([Vector(r) for r in m])
                except Exception:
                    failed_bones.append(pb.name)
        if failed_bones:
            return "could not restore pose for bone(s): " + ", ".join(failed_bones[:8])
    except Exception:
        return "could not restore the saved pose"
    return None


def emit_skin(obj, mesh_eval, vindices, arm_info, global_matrix, warnings, header, blobs, emit_blob):
    """Skin stream + skeleton record. Returns (skinOffset, skinSize, skinCount, skeletonIndex).
    All zeros when static. Bones: pack order == armature bone order; rest matrices
    in export space (global @ obj @ arm^-1 @ local), row-major; v1 consumers use
    names + weights (rigid mapping), rest is for future GPU skinning."""
    if arm_info is None:
        return 0, 0, 0, -1
    tag = f"Object '{obj.name}': "
    bones = arm_info["bones"]
    bidx = {b["name"]: i for i, b in enumerate(bones)}
    # Rest matrices in export space.
    obj_m = Matrix([Vector(r) for r in arm_info["obj_matrix"]])
    arm_m = Matrix([Vector(r) for r in arm_info["arm_matrix"]])
    try:
        to_export = global_matrix @ obj_m @ arm_m.inverted()
    except Exception:
        to_export = global_matrix @ obj_m
    pack_bones = [{"name": b["name"], "parent": -1, "rest": None} for b in bones]
    # Fill parents + rest matrices (needs armature datablock access).
    arm_obj = bpy.data.objects.get(arm_info["armature"])
    if arm_obj is not None:
        for i, b in enumerate(bones):
            bb = arm_obj.data.bones.get(b["name"])
            if bb is None:
                continue
            pack_bones[i]["parent"] = bidx.get(bb.parent.name, -1) if bb.parent is not None else -1
            try:
                rm = to_export @ bb.matrix_local
                pack_bones[i]["rest"] = [rm[r][c] for r in range(4) for c in range(4)]
            except Exception:
                pass
    for pb in pack_bones:
        if pb["rest"] is None:
            pb["rest"] = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]
    # Skeleton dedupe: shared armatures share one record.
    skel_idx = None
    for i, s in enumerate(header.setdefault("skeletons", [])):
        if s.get("armature") == arm_info["armature"]:
            skel_idx = i
            break
    if skel_idx is None:
        skel_idx = len(header["skeletons"])
        header["skeletons"].append({
            "name": arm_info["armature"], "armature": arm_info["armature"],
            "bones": [{"name": pb["name"], "parent": pb["parent"],
                       "restMatrix": pb["rest"]} for pb in pack_bones],
        })
    # Weights: original-mesh vertex groups → top-4 normalized.
    src_mesh = obj.data
    try:
        vgroups = src_mesh.vertices
    except Exception:
        vgroups = []
    unweighted = 0
    sblob = bytearray()
    for vi in vindices:
        inf = []
        try:
            if 0 <= vi < len(vgroups):
                for g in vgroups[vi].groups:
                    try:
                        gname = obj.vertex_groups[g.group].name
                    except Exception:
                        continue
                    if gname in bidx and g.weight > 0.0:
                        inf.append((float(g.weight), bidx[gname]))
        except Exception:
            pass
        inf.sort(reverse=True)
        inf = inf[:4]
        total = sum(w for w, _ in inf)
        if total <= 0.0:
            # Unweighted verts bind rigidly to the root bone.
            inf = [(1.0, 0)]
            total = 1.0
            unweighted += 1
        idxs = [bi for _, bi in inf] + [0] * (4 - len(inf))
        wgts = [w / total for w, _ in inf] + [0.0] * (4 - len(inf))
        for bi in idxs[:4]:
            sblob += struct.pack("<I", int(bi))
        for w in wgts[:4]:
            sblob += struct.pack("<f", float(w))
    soff, slen = emit_blob(bytes(sblob))
    if unweighted:
        warnings.append(tag + f"{unweighted} unweighted verts bound rigidly to root bone")
    warnings.append(tag + f"skeleton '{arm_info['armature']}' ({len(bones)} bones, {len(vindices)} skinned verts)")
    return soff, slen, len(vindices), skel_idx


# --------------------------------------------------------------------------
# Materials (Principled BSDF: scalars + direct image links)
# --------------------------------------------------------------------------

def socket_image(sock, warnings, tag):
    """Find feeding TEX_IMAGE nodes, walking through blend/convert chains.

    Returns (image, chain_desc, mapping). Direct links and one-level
    NormalMap unwraps are exact; deeper chains still yield the image but are
    WARNED as value-modified, with the chain path. mapping is the first
    MAPPING node met upstream ({location, scale}) or None.
    Returns (None, None, None) when unlinked, (None, 'procedural', None)
    when no image exists upstream at all.
    """
    if sock is None:
        return None, None, None
    if not sock.is_linked:
        return None, None, None

    # BFS upstream, depth-limited, tracking the path for warnings.
    seen = set()
    queue = [(sock.links[0].from_node, sock.links[0].from_socket.name,
              sock.links[0].from_node.name if hasattr(sock.links[0].from_node, 'name') else '?')]
    # Passthrough node types whose first color input preserves the image.
    passthrough = {'MIX_RGB', 'MIX', 'SEPARATE_RGB', 'COMBINE_RGB',
                   'RGBTOBW', 'INVERT', 'HUE_SAT', 'BRIGHTCONTRAST',
                   'MAPPING', 'TEX_COORD', 'NORMAL_MAP', 'BUMP',
                   'CURVE_RGB', 'VALTORGB', 'MATH'}
    depth = 0
    mapping = None
    while queue and depth < 5:
        nxt = []
        for node, out_name, path in queue:
            if id(node) in seen:
                continue
            seen.add(id(node))
            if node.type == 'MAPPING' and mapping is None:
                try:
                    loc = node.inputs.get('Location')
                    scl = node.inputs.get('Scale')
                    mapping = {
                        "location": list(loc.default_value)[:3] if loc else [0.0, 0.0, 0.0],
                        "scale": list(scl.default_value)[:3] if scl else [1.0, 1.0, 1.0],
                    }
                except Exception:
                    mapping = {"location": [0.0, 0.0, 0.0], "scale": [1.0, 1.0, 1.0]}
            if node.type == 'TEX_IMAGE' and node.image:
                vector_input = node.inputs.get('Vector')
                custom_vector = bool(vector_input and vector_input.is_linked)
                if (depth == 0 or (depth == 1 and 'NORMAL_MAP' in path)) and not custom_vector:
                    return node.image, 'direct', mapping
                warnings.append(
                    f"{tag}image '{node.image.name}' uses a modified color or coordinate chain ({path}) — values may differ from viewport")
                coord_path = path + '>Vector' if custom_vector else path
                return node.image, 'chain:' + coord_path, mapping
            if node.type in passthrough:
                for inp in node.inputs:
                    if inp.is_linked and inp.type in ('RGBA', 'VECTOR', 'VALUE'):
                        for link in inp.links:
                            fn = link.from_node
                            nxt.append((fn, link.from_socket.name,
                                        path + '>' + getattr(fn, 'name', fn.type)))
                        break
        queue = nxt
        depth += 1
    return None, 'procedural', mapping


def socket_scalar(sock, default):
    if sock.is_linked:
        return default, True
    v = sock.default_value
    try:
        return [float(x) for x in v], False
    except TypeError:
        return float(v), False


def gather_surface(mat, warnings, defer_albedo_warnings=False):
    """Blender-linear scalars + image refs. Procedural links fall back + warn."""
    tag = f"Material '{mat.name if mat else 'None'}': "
    blank = {"albedo": [0.5, 0.5, 0.5], "metallic": 0.0, "roughness": 0.6,
             "emissive": [0.0, 0.0, 0.0], "emissive_strength": 0.0,
             "clearcoat": 0.0, "clearcoat_tint": [1.0, 1.0, 1.0],
             "alpha": 1.0, "images": {},
             "needs_bake": False}
    if mat is None:
        warnings.append(tag + "empty slot, neutral gray fallback")
        return blank
    if not mat.use_nodes:
        warnings.append(tag + "no nodes, neutral fallback")
        return blank
    nodes = mat.node_tree.nodes
    bsdf = next((n for n in nodes if n.type == 'BSDF_PRINCIPLED'), None)
    output = next((n for n in nodes if n.type == 'OUTPUT_MATERIAL' and n.is_active_output), None)
    surface_socket = output.inputs.get('Surface') if output is not None else None
    surface_links = list(surface_socket.links) if surface_socket is not None else []
    simple_surface = (len(surface_links) == 1 and bsdf is not None and
                      surface_links[0].from_node == bsdf)
    if bsdf is None:
        blank["needs_bake"] = bool(surface_links)
        blank["albedo_warnings"] = [tag + "no Principled BSDF, neutral fallback"]
        if not defer_albedo_warnings:
            warnings.extend(blank["albedo_warnings"])
        return blank

    def inp(name):
        return bsdf.inputs.get(name)

    albedo_warnings = []
    albedo_img, albedo_kind, albedo_map = socket_image(inp('Base Color'), albedo_warnings, tag)
    albedo_val, _ = socket_scalar(inp('Base Color'), [0.8, 0.8, 0.8])
    metal_img, metal_kind, metal_map = socket_image(inp('Metallic'), warnings, tag)
    metal_val, _ = socket_scalar(inp('Metallic'), 0.0)
    rough_img, rough_kind, rough_map = socket_image(inp('Roughness'), warnings, tag)
    rough_val, _ = socket_scalar(inp('Roughness'), 0.6)
    emis_img, _, emis_map = socket_image(inp('Emission Color'), warnings, tag)
    emis_val, _ = socket_scalar(inp('Emission Color'), [0.0, 0.0, 0.0])
    emis_strength, _ = socket_scalar(inp('Emission Strength'), 1.0)
    # Coat weight: Blender 4.x 'Coat Weight', 3.x 'Clearcoat'. Any coat > 0
    # maps to the Strata Clearcoat bit (fixed full-strength in-shader).
    coat_inp = inp('Coat Weight') or inp('Clearcoat')
    coat_val = socket_scalar(coat_inp, 0.0)[0] if coat_inp is not None else 0.0
    coat_val = float(coat_val) if not isinstance(coat_val, list) else float(coat_val[0])
    clearcoat = 1.0 if coat_val > 0.01 else 0.0
    # Blender's Principled Coat Tint is already linear. This is a constant
    # material value in Strata; linked tint graphs fall back to white with a
    # clear warning instead of silently turning the coat black.
    coat_tint_inp = inp('Coat Tint') or inp('Clearcoat Tint')
    coat_tint_val, coat_tint_linked = socket_scalar(coat_tint_inp, [1.0, 1.0, 1.0]) \
        if coat_tint_inp is not None else ([1.0, 1.0, 1.0], False)
    if coat_tint_linked:
        warnings.append(tag + "linked Coat Tint graph is unsupported; white clearcoat tint used")
        coat_tint_val = [1.0, 1.0, 1.0]
    elif not isinstance(coat_tint_val, list):
        coat_tint_val = [coat_tint_val] * 3
    coat_tint = [max(0.0, min(1.0, float(coat_tint_val[i])))
                 for i in range(min(3, len(coat_tint_val)))]
    coat_tint += [1.0] * (3 - len(coat_tint))
    # No iridescence socket exists on Principled BSDF: film is enabled via
    # the Strata configurator (or Assemble directly), never from Blender.
    # Sheen: Blender 4.x 'Sheen Weight' (3.x 'Sheen'); best-effort names,
    # missing sockets safely read 0.
    sheen_inp = inp('Sheen Weight') or inp('Sheen')
    sheen_val = socket_scalar(sheen_inp, 0.0)[0] if sheen_inp is not None else 0.0
    sheen_val = float(sheen_val) if not isinstance(sheen_val, list) else float(sheen_val[0])
    sheen = 1.0 if sheen_val > 0.01 else 0.0
    # Anisotropy: Blender 4.x 'Anisotropic' (rotation/tangent ignored, v1
    # uses UV-gradient frame). Best-effort names, missing reads 0.
    aniso_inp = inp('Anisotropic') or inp('Anisotropy')
    aniso_val = socket_scalar(aniso_inp, 0.0)[0] if aniso_inp is not None else 0.0
    aniso_val = float(aniso_val) if not isinstance(aniso_val, list) else float(aniso_val[0])
    anisotropy = 1.0 if aniso_val > 0.01 else 0.0
    # Skin/hair: NO Principled auto-map (Subsurface exists but false-positives
    # on car paint; film has no socket at all). Both are configurator/
    # Assemble-only, like iridescence. Documented, deliberate.
    # Opacity: Principled Alpha < 1 (or Transmission > 0) marks glass.
    # Anything at/above 0.999 stays on the opaque path.
    alpha_inp = inp('Alpha')
    alpha_val = socket_scalar(alpha_inp, 1.0)[0] if alpha_inp is not None else 1.0
    alpha_val = float(alpha_val) if not isinstance(alpha_val, list) else float(alpha_val[0])
    trans_inp = inp('Transmission Weight') or inp('Transmission')
    trans_val = socket_scalar(trans_inp, 0.0)[0] if trans_inp is not None else 0.0
    trans_val = float(trans_val) if not isinstance(trans_val, list) else float(trans_val[0])
    if trans_val > 0.01:
        alpha_val = min(alpha_val, 1.0 - trans_val)
    alpha = max(0.0, min(1.0, alpha_val))
    norm_img, norm_kind, norm_map = socket_image(inp('Normal'), warnings, tag)
    if albedo_kind == 'procedural':
        albedo_warnings.append(tag + "procedural graph with no image upstream, scalar fallback used")
    for kind in (metal_kind, rough_kind):
        if kind == 'procedural':
            warnings.append(tag + "procedural graph with no image upstream, scalar fallback used")
            break
    if not defer_albedo_warnings:
        warnings.extend(albedo_warnings)
    # Per-slot manifest: exactly what this slot resolved to (paste on texture loss).
    warnings.append(
        tag + f"manifest albedo={'IMG:' + albedo_img.name if albedo_img else list(albedo_val)[:3]} "
        f"metallic={'IMG:' + metal_img.name if metal_img else metal_val} "
        f"roughness={'IMG:' + rough_img.name if rough_img else rough_val} "
        f"normal={'IMG:' + norm_img.name if norm_img else 'none'} "
        f"clearcoat={clearcoat} coatTint={coat_tint} alpha={alpha:.3f} "
        f"sheen={sheen} aniso={anisotropy}")

    images = {}
    if albedo_img:
        images['albedo'] = albedo_img
    if metal_img is not None:
        images['metallic'] = metal_img
    if rough_img is not None:
        images['roughness'] = rough_img
    if norm_img:
        images['normal'] = norm_img
    if emis_img:
        images['emissive'] = emis_img

    return {"albedo": list(albedo_val)[:3], "metallic": float(metal_val),
            "roughness": float(rough_val),
            "emissive": list(emis_val)[:3],
            "emissive_strength": float(emis_strength),
            "clearcoat": clearcoat, "clearcoat_tint": coat_tint,
            "alpha": alpha, "sheen": sheen,
            "anisotropy": anisotropy, "images": images,
            "needs_bake": (bool(surface_links) and
                           (not simple_surface or albedo_kind == 'procedural' or
                            (isinstance(albedo_kind, str) and albedo_kind.startswith('chain:')))),
            "albedo_warnings": albedo_warnings,
            "provenance": {
                "albedo": {"kind": albedo_kind, "mapping": albedo_map},
                "metallic": {"kind": metal_kind, "mapping": metal_map},
                "roughness": {"kind": rough_kind, "mapping": rough_map},
                "normal": {"kind": norm_kind, "mapping": norm_map},
            }}


# --------------------------------------------------------------------------
# Images → RGBA8 blobs (PIL for files, pixels buffer for generated/packed)
# --------------------------------------------------------------------------

class BakedTexture:
    """RGBA8 pixels captured from a temporary Cycles bake."""
    def __init__(self, name, width, height, rgba):
        self.name = name
        self.width = width
        self.height = height
        self.rgba = rgba


def image_source(img):
    """Provenance string: packed | generated | abspath | missing."""
    if isinstance(img, BakedTexture):
        return "baked"
    try:
        if img.packed_file is not None:
            return "packed"
        if img.filepath:
            path = bpy.path.abspath(img.filepath)
            return path if os.path.isfile(path) else "missing:" + img.filepath
        return "generated"
    except Exception:
        return "unknown"

def image_to_rgba8(img, warnings):
    """Returns (w, h, bytes) RGBA8 top-down, or None."""
    if isinstance(img, BakedTexture):
        return img.width, img.height, img.rgba
    import numpy as np
    try:
        if img.packed_file is None and img.filepath:
            path = bpy.path.abspath(img.filepath)
            if os.path.isfile(path):
                from PIL import Image
                im = Image.open(path).convert('RGBA')
                w, h = im.size
                px = np.asarray(im, dtype=np.uint8)
                if len(px.shape) == 2:
                    px = np.stack([px] * 4, axis=-1)
                return w, h, px.tobytes()
        # Packed / generated / dirty: Blender pixel buffer (bottom-up floats).
        w, h = img.size[0], img.size[1]
        if w <= 0 or h <= 0:
            return None
        buf = np.empty(w * h * 4, dtype=np.float32)
        img.pixels.foreach_get(buf)
        if img.alpha_mode == 'PREMUL':
            a = buf[3::4]
            nz = a > 1e-6
            for c in range(3):
                ch = buf[c::4]
                ch[nz] /= a[nz]
        px = (np.clip(buf, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8)
        px = px.reshape(h, w, 4)[::-1, :, :]
        return w, h, px.tobytes()
    except Exception as ex:
        warnings.append(f"Image '{img.name}': unreadable ({ex})")
        return None


def _baked_image_to_rgba8(image):
    """Convert Cycles' linear, bottom-up bake pixels to top-down sRGB bytes."""
    import numpy as np
    width, height = image.size
    pixels = np.empty(width * height * 4, dtype=np.float32)
    image.pixels.foreach_get(pixels)
    rgba = np.nan_to_num(pixels.reshape(height, width, 4), nan=0.0,
                         posinf=1.0, neginf=0.0)
    linear = np.clip(rgba[:, :, :3], 0.0, 1.0)
    srgb = np.where(linear <= 0.0031308, linear * 12.92,
                    1.055 * np.power(linear, 1.0 / 2.4) - 0.055)
    rgba[:, :, :3] = srgb
    rgba[:, :, 3] = np.clip(rgba[:, :, 3], 0.0, 1.0)
    return (np.clip(rgba * 255.0 + 0.5, 0.0, 255.0)
            .astype(np.uint8)[::-1, :, :].tobytes())


def _prepare_material_color_bake(material, temp_node_groups):
    """Turn connected surface BSDF color inputs into unlit emission nodes.

    This keeps color ramps, mixes, image chains, and nested node groups while
    removing lighting and BRDF response from the exported Base Color texture.
    """
    converted = set()

    def convert_tree(tree):
        tree_key = tree.as_pointer()
        if tree_key in converted:
            return
        converted.add(tree_key)
        for node in list(tree.nodes):
            if node.type == 'GROUP' and node.node_tree is not None:
                group_copy = node.node_tree.copy()
                node.node_tree = group_copy
                temp_node_groups.append(group_copy)
                convert_tree(group_copy)
                continue
            if node.type == 'EMISSION':
                continue
            shader_outputs = [output for output in node.outputs
                              if output.type == 'SHADER' and output.links]
            if not shader_outputs:
                continue
            is_colored_shader = (node.type.startswith('BSDF_') or
                                 node.type in {'SUBSURFACE_SCATTERING', 'BACKGROUND'})
            if not is_colored_shader:
                if node.type not in {'MIX_SHADER', 'ADD_SHADER', 'MIX', 'REROUTE', 'GROUP_INPUT'}:
                    raise RuntimeError(f"shader node '{node.name}' cannot be color-baked safely")
                continue
            color_input = (node.inputs.get('Base Color') or node.inputs.get('Color')
                           or node.inputs.get('Tint'))
            if color_input is None:
                raise RuntimeError(f"shader node '{node.name}' has no supported color input")

            emission = tree.nodes.new('ShaderNodeEmission')
            emission.name = f"{node.name}_EaseColor"
            emission.label = "Ease color capture"
            emission.location = (node.location.x + 180.0, node.location.y)
            if color_input.is_linked:
                for link in list(color_input.links):
                    tree.links.new(link.from_socket, emission.inputs['Color'])
            else:
                try:
                    emission.inputs['Color'].default_value = color_input.default_value
                except Exception:
                    emission.inputs['Color'].default_value = (0.8, 0.8, 0.8, 1.0)
            emission.inputs['Strength'].default_value = 1.0
            for output in shader_outputs:
                for link in list(output.links):
                    destination = link.to_socket
                    tree.links.remove(link)
                    tree.links.new(emission.outputs['Emission'], destination)
            tree.nodes.remove(node)

    if material.node_tree is not None:
        convert_tree(material.node_tree)


def bake_node_material_albedos(context, source_obj, mesh_eval, material_indices, resolution):
    """Bake complex material color graphs without changing the user's scene.

    A temporary evaluated-mesh copy and material copies are used as bake
    targets. If no UV map exists, a smart UV map is generated on that copy and
    transferred to mesh_eval, which is also the mesh used for pack generation.
    """
    if getattr(context, "mode", "OBJECT") != 'OBJECT':
        raise RuntimeError("Exit Edit Mode or another object mode before baking complex materials")
    if not getattr(getattr(bpy.app, "build_options", None), "cycles", False):
        raise RuntimeError("This Blender build does not include the Cycles bake engine")

    complex_indices = sorted(set(int(i) for i in material_indices
                                 if 0 <= int(i) < max(len(mesh_eval.materials), len(source_obj.material_slots))))
    if not complex_indices:
        return {}

    scene = context.scene
    view_layer = context.view_layer
    original_selected = tuple(context.selected_objects)
    original_active = view_layer.objects.active
    old_engine = scene.render.engine
    old_margin = scene.render.bake.margin
    old_margin_type = scene.render.bake.margin_type
    cycles = getattr(scene, "cycles", None)
    old_samples = getattr(cycles, "samples", None) if cycles is not None else None
    old_device = getattr(cycles, "device", None) if cycles is not None else None

    bake_mesh = None
    bake_obj = None
    temp_materials = []
    temp_node_groups = []
    temp_images = []
    scratch_image = None
    bake_results = {}
    temp_mode = False

    try:
        bake_mesh = mesh_eval.copy()
        bake_mesh.name = f"{source_obj.name}_EaseBakeMesh"
        bake_obj = bpy.data.objects.new(f"{source_obj.name}_EaseBake", bake_mesh)
        scene.collection.objects.link(bake_obj)
        bake_obj.matrix_world = source_obj.matrix_world.copy()

        # Keep one target image per complex material. Simpler slots share a
        # scratch target so Blender's per-material bake setup stays complete.
        slot_count = max(len(bake_mesh.materials), len(source_obj.material_slots))
        if slot_count == 0:
            raise RuntimeError("The material graph has no material slot to bake")
        targets_by_slot = {}
        for slot_index in range(slot_count):
            source_material = (bake_mesh.materials[slot_index]
                               if slot_index < len(bake_mesh.materials) else None)
            if source_material is None and slot_index < len(source_obj.material_slots):
                source_material = source_obj.material_slots[slot_index].material
            if source_material is None:
                material_copy = bpy.data.materials.new(
                    f"{source_obj.name}_EaseBakeFallback_{slot_index}")
                material_copy.use_nodes = True
            else:
                material_copy = source_material.copy()
                material_copy.name = f"{source_material.name}_EaseBake_{slot_index}"
            temp_materials.append(material_copy)
            if not material_copy.use_nodes:
                material_copy.use_nodes = True
            if material_copy.node_tree is None:
                raise RuntimeError(f"Could not prepare material slot {slot_index} for baking")
            if slot_index in complex_indices:
                _prepare_material_color_bake(material_copy, temp_node_groups)

            if slot_index in complex_indices:
                target = bpy.data.images.new(
                    f"{source_obj.name}_{material_copy.name}_EaseBake",
                    width=resolution, height=resolution, alpha=True, float_buffer=False)
                temp_images.append(target)
                targets_by_slot[slot_index] = target
            else:
                if scratch_image is None:
                    scratch_image = bpy.data.images.new(
                        f"{source_obj.name}_EaseBakeScratch",
                        width=resolution, height=resolution, alpha=True, float_buffer=False)
                    temp_images.append(scratch_image)
                target = scratch_image

            try:
                target.colorspace_settings.name = 'Non-Color'
            except Exception:
                pass
            nodes = material_copy.node_tree.nodes
            for node in nodes:
                node.select = False
            image_node = nodes.new('ShaderNodeTexImage')
            image_node.name = "BlueSky Ease Bake Target"
            image_node.label = "Temporary Ease Bake Target"
            image_node.image = target
            image_node.select = True
            nodes.active = image_node
            if slot_index < len(bake_mesh.materials):
                bake_mesh.materials[slot_index] = material_copy
            else:
                bake_mesh.materials.append(material_copy)

        # Bake operators use the active selected object. Preserve and restore
        # selection, active object, and render settings in the finally block.
        for selected in original_selected:
            selected.select_set(False)
        bake_obj.select_set(True)
        view_layer.objects.active = bake_obj
        temp_override = {"object": bake_obj, "active_object": bake_obj,
                         "selected_objects": [bake_obj],
                         "selected_editable_objects": [bake_obj]}
        window = getattr(context, "window", None)
        if window is not None:
            temp_override["window"] = window
        screen = getattr(context, "screen", None)
        view_area = next((area for area in screen.areas if area.type == 'VIEW_3D'), None) if screen else None
        if view_area is not None:
            window_region = next((region for region in view_area.regions
                                  if region.type == 'WINDOW'), None)
            if window_region is not None:
                temp_override.update(area=view_area, region=window_region)

        if bake_mesh.uv_layers.active is None:
            bake_uv = bake_mesh.uv_layers.new(name="BlueSkyEaseUV")
            bake_mesh.uv_layers.active = bake_uv
            with context.temp_override(**temp_override):
                if 'FINISHED' not in bpy.ops.object.mode_set(mode='EDIT'):
                    raise RuntimeError("Could not enter Edit Mode to create a temporary UV map")
                temp_mode = True
                bpy.ops.mesh.select_all(action='SELECT')
                unwrap_result = bpy.ops.uv.smart_project(island_margin=0.02,
                                                         scale_to_bounds=True)
                if 'FINISHED' not in unwrap_result:
                    raise RuntimeError("Blender could not create a smart UV map")
                if 'FINISHED' not in bpy.ops.object.mode_set(mode='OBJECT'):
                    raise RuntimeError("Could not finish temporary UV generation")
                temp_mode = False

            exported_uv = mesh_eval.uv_layers.new(name="BlueSkyEaseUV")
            for destination, source in zip(exported_uv.data, bake_uv.data):
                destination.uv = source.uv
            mesh_eval.uv_layers.active = exported_uv

        uv_layer = bake_mesh.uv_layers.active
        if uv_layer is None:
            raise RuntimeError("The mesh still has no active UV map after preparation")

        scene.render.engine = 'CYCLES'
        if cycles is not None:
            cycles.samples = 16
            try:
                cycles.device = 'CPU'
            except Exception:
                pass
        scene.render.bake.margin = min(16, max(4, resolution // 128))
        scene.render.bake.margin_type = 'EXTEND'
        with context.temp_override(**temp_override):
            bake_status = bpy.ops.object.bake(type='EMIT',
                                              target='IMAGE_TEXTURES', use_clear=True,
                                              margin=scene.render.bake.margin,
                                              uv_layer=uv_layer.name)
        if 'FINISHED' not in bake_status:
            raise RuntimeError("Blender did not finish the color bake")

        for slot_index, image in targets_by_slot.items():
            rgba = _baked_image_to_rgba8(image)
            bake_results[slot_index] = BakedTexture(
                image.name, image.size[0], image.size[1], rgba)
        return bake_results
    finally:
        if temp_mode and bake_obj is not None:
            try:
                with context.temp_override(**temp_override):
                    bpy.ops.object.mode_set(mode='OBJECT')
            except Exception:
                pass
        if bake_obj is not None:
            try:
                bpy.data.objects.remove(bake_obj, do_unlink=True)
            except Exception:
                pass
        if bake_mesh is not None:
            try:
                bpy.data.meshes.remove(bake_mesh)
            except Exception:
                pass
        for material in temp_materials:
            try:
                bpy.data.materials.remove(material, do_unlink=True)
            except Exception:
                pass
        for node_group in temp_node_groups:
            try:
                bpy.data.node_groups.remove(node_group, do_unlink=True)
            except Exception:
                pass
        for image in temp_images:
            try:
                bpy.data.images.remove(image, do_unlink=True)
            except Exception:
                pass
        for owner, attribute, value in (
                (scene.render, "engine", old_engine),
                (scene.render.bake, "margin", old_margin),
                (scene.render.bake, "margin_type", old_margin_type),
                (cycles, "samples", old_samples),
                (cycles, "device", old_device)):
            if owner is None or value is None:
                continue
            try:
                setattr(owner, attribute, value)
            except Exception:
                pass
        for selected in context.view_layer.objects:
            try:
                selected.select_set(False)
            except Exception:
                pass
        for selected in original_selected:
            try:
                if selected.name in context.view_layer.objects:
                    selected.select_set(True)
            except Exception:
                pass
        try:
            view_layer.objects.active = original_active
        except Exception:
            pass


def pack_rma_blob(metal_blob, rough_blob, w, h, metal_scalar, rough_scalar):
    """RMA blob: R=rough, G=metal, B=AO(255). Images win over scalars per-pixel."""
    import numpy as np
    n = w * h
    if metal_blob is not None:
        m = np.frombuffer(metal_blob[2], dtype=np.uint8).astype(np.float32)
        m = m.reshape(metal_blob[1], metal_blob[0], 4)[:, :, 0]
        from PIL import Image
        m = np.asarray(Image.fromarray(m.astype(np.uint8)).resize((w, h))).astype(np.float32)
    else:
        m = np.full((h, w), metal_scalar * 255.0, dtype=np.float32)
    if rough_blob is not None:
        r = np.frombuffer(rough_blob[2], dtype=np.uint8).astype(np.float32)
        r = r.reshape(rough_blob[1], rough_blob[0], 4)[:, :, 0]
        from PIL import Image
        r = np.asarray(Image.fromarray(r.astype(np.uint8)).resize((w, h))).astype(np.float32)
    else:
        r = np.full((h, w), rough_scalar * 255.0, dtype=np.float32)
    out = np.empty((h, w, 4), dtype=np.uint8)
    out[:, :, 0] = np.clip(r, 0, 255).astype(np.uint8)
    out[:, :, 1] = np.clip(m, 0, 255).astype(np.uint8)
    out[:, :, 2] = 255
    out[:, :, 3] = 255
    return out.tobytes()


# --------------------------------------------------------------------------
# Bent-normal baker (world-space hemisphere occlusion, "world" encoding)
# --------------------------------------------------------------------------

BENT_SIZE = 64   # bent texture resolution per slot
BENT_RAYS = 8    # hemisphere rays per vertex (deterministic Fibonacci set)


def bake_bent_blob(mesh_eval, positions, normals, uvs, indices, sub,
                   global_matrix, warnings):
    """Returns (w, h, bytes) RGBA8 world-space bent normals, or None.
    Per-vertex hemisphere occlusion via explicit world-space BVHTree
    (no API-space ambiguity), rasterized into the slot UVs, box-filled.
    Deterministic: fixed Fibonacci ray set rotated by vertex-index hash."""
    tag = "bent: "
    try:
        from mathutils.bvhtree import BVHTree
    except Exception as ex:
        warnings.append(tag + f"BVHTree unavailable ({ex}), bent skipped")
        return None
    try:
        start, count = sub["offset"], sub["count"]
        vert_ids = sorted({indices[start + i] for i in range(count)
                           if 0 <= start + i < len(indices)})
        if not vert_ids:
            return None
        # World/export-space polygons for the tree (matches baked verts).
        polys = []
        for tri in mesh_eval.loop_triangles:
            try:
                pts = [tuple(global_matrix @ mesh_eval.vertices[vi].co)
                       for vi in tri.vertices]
            except Exception:
                continue
            if len(pts) == 3:
                polys.append(pts)
        if not polys:
            warnings.append(tag + "no triangles, bent skipped")
            return None
        tree = BVHTree.FromPolygons(
            [list(c) for p in polys for c in p],
            [[3 * i, 3 * i + 1, 3 * i + 2] for i in range(len(polys))])
    except Exception as ex:
        warnings.append(tag + f"tree build failed ({ex}), bent skipped")
        return None
    # Fixed Fibonacci hemisphere (deterministic across runs/machines).
    fib = []
    golden = math.pi * (3.0 - math.sqrt(5.0))
    for k in range(BENT_RAYS):
        y = 1.0 - (k + 0.5) / BENT_RAYS  # 0..1 cosine band
        r = math.sqrt(max(0.0, 1.0 - y * y))
        a = golden * k
        fib.append((r * math.cos(a), r * math.sin(a), y))
    import time
    t0 = time.perf_counter()
    # Per-vertex bent direction in export space.
    bent_dirs = {}
    eps = 0.001
    for ei in vert_ids:
        p = positions[ei]
        n = normals[ei]
        nx, ny, nz = n
        nl = math.sqrt(nx * nx + ny * ny + nz * nz) or 1.0
        nx, ny, nz = nx / nl, ny / nl, nz / nl
        # Orthonormal basis around n (fixed up, deterministic).
        up = (0.0, 1.0, 0.0) if abs(ny) < 0.99 else (1.0, 0.0, 0.0)
        tx = (ny * up[2] - nz * up[1], nz * up[0] - nx * up[2], nx * up[1] - ny * up[0])
        tl = math.sqrt(tx[0] * tx[0] + tx[1] * tx[1] + tx[2] * tx[2]) or 1.0
        tx = (tx[0] / tl, tx[1] / tl, tx[2] / tl)
        bx = (ny * tx[2] - nz * tx[1], nz * tx[0] - nx * tx[2], nx * tx[1] - ny * tx[0])
        # Vertex-hash rotation of the fixed ray set (deterministic).
        rot = ((ei * 2654435761) % 1024) / 1024.0 * 2.0 * math.pi
        cr, sr = math.cos(rot), math.sin(rot)
        acc = [0.0, 0.0, 0.0]
        origin = (p[0] + nx * eps, p[1] + ny * eps, p[2] + nz * eps)
        for fx, fy, fz in fib:
            lx = fx * cr - fy * sr
            ly = fx * sr + fy * cr
            d = (tx[0] * lx + bx[0] * ly + nx * fz,
                 tx[1] * lx + bx[1] * ly + ny * fz,
                 tx[2] * lx + bx[2] * ly + nz * fz)
            try:
                hit = tree.ray_cast(origin, d)
            except Exception:
                hit = None
            if hit is None or hit[0] is None:
                acc[0] += d[0]
                acc[1] += d[1]
                acc[2] += d[2]
        al = math.sqrt(acc[0] * acc[0] + acc[1] * acc[1] + acc[2] * acc[2])
        if al < 1e-6:
            bent_dirs[ei] = (nx, ny, nz)  # fully occluded: geometric normal
        else:
            bent_dirs[ei] = (acc[0] / al, acc[1] / al, acc[2] / al)
    # Rasterize into slot UVs (accumulate + average), then box-fill empties.
    W = H = BENT_SIZE
    acc = [[[0.0, 0.0, 0.0, 0] for _ in range(W)] for _ in range(H)]
    for ei in vert_ids:
        u, v = uvs[ei]
        px = min(W - 1, max(0, int(u * W)))
        py = min(H - 1, max(0, int(v * H)))
        c = acc[py][px]
        d = bent_dirs[ei]
        c[0] += d[0]
        c[1] += d[1]
        c[2] += d[2]
        c[3] += 1
    grid = [[None for _ in range(W)] for _ in range(H)]
    for y in range(H):
        for x in range(W):
            c = acc[y][x]
            if c[3] > 0:
                l = math.sqrt(c[0] * c[0] + c[1] * c[1] + c[2] * c[2]) or 1.0
                grid[y][x] = (c[0] / l, c[1] / l, c[2] / l)
    for _ in range(W * H):
        changed = False
        for y in range(H):
            for x in range(W):
                if grid[y][x] is not None:
                    continue
                sx = sy = sz = cnt = 0.0
                if x > 0 and grid[y][x - 1] is not None:
                    g = grid[y][x - 1]
                    sx += g[0]
                    sy += g[1]
                    sz += g[2]
                    cnt += 1
                if x + 1 < W and grid[y][x + 1] is not None:
                    g = grid[y][x + 1]
                    sx += g[0]
                    sy += g[1]
                    sz += g[2]
                    cnt += 1
                if y > 0 and grid[y - 1][x] is not None:
                    g = grid[y - 1][x]
                    sx += g[0]
                    sy += g[1]
                    sz += g[2]
                    cnt += 1
                if y + 1 < H and grid[y + 1][x] is not None:
                    g = grid[y + 1][x]
                    sx += g[0]
                    sy += g[1]
                    sz += g[2]
                    cnt += 1
                if cnt > 0:
                    l = math.sqrt(sx * sx + sy * sy + sz * sz) or 1.0
                    grid[y][x] = (sx / l, sy / l, sz / l)
                    changed = True
        if not changed:
            break
    out = bytearray()
    for y in range(H):
        for x in range(W):
            g = grid[y][x] or (0.0, 0.0, 1.0)
            out += bytes((max(0, min(255, int((g[0] * 0.5 + 0.5) * 255))),
                          max(0, min(255, int((g[1] * 0.5 + 0.5) * 255))),
                          max(0, min(255, int((g[2] * 0.5 + 0.5) * 255))), 255))
    dt = time.perf_counter() - t0
    warnings.append(f"bent: slot baked {W}x{H} from {len(vert_ids)} verts "
                    f"({BENT_RAYS} rays each, {dt:.1f}s)")
    return W, H, bytes(out)


# --------------------------------------------------------------------------
# Phase-B light probes (analytic samples; C# owns the SH projection)
# --------------------------------------------------------------------------

PROBE_SAMPLES = 64  # fixed Fibonacci dirs (same estimator family as sky capture)
PROBE_PREFIX = "StrataProbe"


def bake_probes(context, global_matrix, warnings, header):
    """Scene empties named StrataProbe* → header['probes'].
    Each probe = 64 fixed Fibonacci direction samples with analytic scene
    lighting (world + suns + points/spots). Shadows ignored, falloff is
    energy/dist^2 with a 30cm floor, AREA lamps treated as points — all
    documented approximations. Deterministic: no RNG anywhere."""
    try:
        scene = context.scene
    except Exception:
        return
    empties = [o for o in scene.objects
               if o.type == 'EMPTY' and o.name.startswith(PROBE_PREFIX)]
    if not empties:
        return
    # World ambient (Background node; guarded fallbacks).
    world_col = (0.03, 0.03, 0.04)
    try:
        world = scene.world
        if world is not None and world.use_nodes:
            bg = next((n for n in world.node_tree.nodes if n.type == 'BACKGROUND'), None)
            if bg is not None:
                world_col = tuple(float(c) for c in bg.inputs[0].default_value[:3])
    except Exception:
        pass
    # Lamps: all suns + top-8 local lights by energy.
    suns = []
    locals_ = []
    try:
        for o in scene.objects:
            if o.type != 'LIGHT' or o.data is None:
                continue
            try:
                kind = o.data.type
                col = tuple(float(c) for c in o.data.color[:3])
                en = float(o.data.energy)
            except Exception:
                continue
            mw = o.matrix_world
            if kind == 'SUN':
                zc = mw.col[2]
                l = math.sqrt(zc[0] * zc[0] + zc[1] * zc[1] + zc[2] * zc[2]) or 1.0
                suns.append({"dir": (-zc[0] / l, -zc[1] / l, -zc[2] / l),
                             "color": col, "energy": en})
            elif kind in ('POINT', 'SPOT', 'AREA'):
                if kind == 'AREA':
                    warnings.append(f"Probe lamps: AREA '{o.name}' treated as point")
                locals_.append({"obj": o.name, "kind": kind, "color": col,
                                "energy": en,
                                "pos": (mw.col[3][0], mw.col[3][1], mw.col[3][2]),
                                "spot_dir": None, "spot_inner": 1.0, "spot_outer": -1.0,
                                "mw": [list(r) for r in mw]})
                if kind == 'SPOT':
                    try:
                        zc = mw.col[2]
                        l = math.sqrt(zc[0] * zc[0] + zc[1] * zc[1] + zc[2] * zc[2]) or 1.0
                        locals_[-1]["spot_dir"] = (-zc[0] / l, -zc[1] / l, -zc[2] / l)
                        size = float(o.data.spot_size)
                        blend = float(o.data.spot_blend)
                        locals_[-1]["spot_outer"] = math.cos(size * 0.5)
                        locals_[-1]["spot_inner"] = math.cos(size * 0.5 * (1.0 - blend))
                    except Exception:
                        pass
    except Exception as ex:
        warnings.append(f"Probe lamps: lamp scan failed ({ex})")
    locals_.sort(key=lambda e: e["energy"], reverse=True)
    if len(locals_) > 8:
        warnings.append(f"Probe lamps: {len(locals_)} local lights, top 8 by energy used")
        locals_ = locals_[:8]
    # Export-space conversions (global rotation/normalized for directions).
    try:
        nmat = global_matrix.to_3x3().normalized()
    except Exception:
        nmat = None

    def xform_dir(d):
        if nmat is None:
            return d
        v = nmat @ Vector(d)
        l = math.sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]) or 1.0
        return (v[0] / l, v[1] / l, v[2] / l)

    def xform_pos(p):
        try:
            v = global_matrix @ Vector(p)
            return (v[0], v[1], v[2])
        except Exception:
            return p

    suns_e = [{"dir": xform_dir(s["dir"]), "color": s["color"], "energy": s["energy"]}
              for s in suns]
    locals_e = []
    for e in locals_:
        pos = xform_pos(e["pos"])
        sd = xform_dir(e["spot_dir"]) if e["spot_dir"] is not None else None
        locals_e.append({"color": e["color"], "energy": e["energy"], "pos": pos,
                         "spot_dir": sd, "spot_inner": e["spot_inner"],
                         "spot_outer": e["spot_outer"]})
    # Fixed Fibonacci directions (matches the C# estimator family).
    dirs = []
    golden = math.pi * (3.0 - math.sqrt(5.0))
    for i in range(PROBE_SAMPLES):
        yy = 1.0 - (i + 0.5) * 2.0 / PROBE_SAMPLES
        r = math.sqrt(max(0.0, 1.0 - yy * yy))
        a = golden * i
        dirs.append((r * math.cos(a), yy, r * math.sin(a)))

    for o in empties:
        try:
            wp = o.matrix_world.col[3]
            pos = xform_pos((wp[0], wp[1], wp[2]))
            radius = float(o.empty_display_size)
            if radius <= 0.0:
                warnings.append(f"Probe '{o.name}': radius <= 0, skipped")
                continue
        except Exception as ex:
            warnings.append(f"Probe '{o.name}': transform failed ({ex}), skipped")
            continue
        sdirs = []
        scols = []
        for d in dirs:
            r = world_col[0]
            g = world_col[1]
            b = world_col[2]
            for s in suns_e:
                k = d[0] * s["dir"][0] + d[1] * s["dir"][1] + d[2] * s["dir"][2]
                if k > 0.0:
                    r += s["color"][0] * s["energy"] * k
                    g += s["color"][1] * s["energy"] * k
                    b += s["color"][2] * s["energy"] * k
            for e in locals_e:
                lx = e["pos"][0] - pos[0]
                ly = e["pos"][1] - pos[1]
                lz = e["pos"][2] - pos[2]
                dist2 = max(lx * lx + ly * ly + lz * lz, 0.09)
                att = e["energy"] / dist2
                if e["spot_dir"] is not None:
                    l = math.sqrt(dist2)
                    cos_a = -((lx * e["spot_dir"][0] + ly * e["spot_dir"][1] + lz * e["spot_dir"][2]) / l)
                    t = (cos_a - e["spot_outer"]) / max(e["spot_inner"] - e["spot_outer"], 1e-6)
                    t = max(0.0, min(1.0, t))
                    att *= t * t * (3.0 - 2.0 * t)
                # Local lamps read as position-dependent ambient (documented).
                r += e["color"][0] * att
                g += e["color"][1] * att
                b += e["color"][2] * att
            sdirs += [d[0], d[1], d[2]]
            scols += [max(0.0, r), max(0.0, g), max(0.0, b)]
        header.setdefault("probes", []).append({
            "name": o.name, "position": list(pos), "radius": radius,
            "sampleDirs": sdirs, "sampleColors": scols,
        })
        warnings.append(f"Probe '{o.name}': {PROBE_SAMPLES} samples at "
                        f"({pos[0]:.2f},{pos[1]:.2f},{pos[2]:.2f}) r={radius:.2f}")


# --------------------------------------------------------------------------
# Pack writer (matches StrataPack.cs layout)
# --------------------------------------------------------------------------

def write_pack(path, header, blobs):
    header_bytes = json.dumps(header, separators=(",", ":"), allow_nan=False).encode("utf-8")
    payload_size = sum(len(blob) for blob in blobs)
    target = os.path.abspath(bpy.path.abspath(path))
    directory = os.path.dirname(target)
    os.makedirs(directory, exist_ok=True)
    temp_path = None
    try:
        # Keep the temporary beside the target so os.replace is atomic on the
        # same volume. A failed export never destroys the previous good pack.
        with tempfile.NamedTemporaryFile(mode="wb", prefix=".ease-", suffix=".tmp",
                                         dir=directory, delete=False) as f:
            temp_path = f.name
            f.write(b"STRATAPK")  # 8 bytes exactly, like StrataPack.cs
            f.write(struct.pack("<I", 1))
            f.write(struct.pack("<Q", len(header_bytes)))
            f.write(struct.pack("<Q", payload_size))
            f.write(header_bytes)
            for blob in blobs:
                f.write(blob)
            f.flush()
            os.fsync(f.fileno())
        os.replace(temp_path, target)
    finally:
        if temp_path and os.path.exists(temp_path):
            try:
                os.remove(temp_path)
            except OSError:
                pass


# --------------------------------------------------------------------------
# Preflight report + polished workspace UI
# --------------------------------------------------------------------------

EXPORTABLE_TYPES = {'MESH', 'CURVE', 'SURFACE', 'FONT', 'META'}


def inspect_scene(context, use_selection=True, bake_complex=True):
    """Read-only validation. Returns actionable issue records; never edits Blender data."""
    objects = list(context.selected_objects) if use_selection else list(context.scene.objects)
    issues = []
    seen = set()
    try:
        import numpy  # noqa: F401
        numpy_available = True
    except ImportError:
        numpy_available = False
    try:
        import PIL  # noqa: F401
        pillow_available = True
    except ImportError:
        pillow_available = False
    cycles_available = bool(getattr(getattr(bpy.app, "build_options", None), "cycles", False))

    def add(severity, code, obj, message, suggestion=""):
        name = obj.name if obj is not None else ""
        key = (severity, code, name, message)
        if key in seen:
            return
        seen.add(key)
        issues.append({"severity": severity, "code": code, "object_name": name,
                       "message": message, "suggestion": suggestion})

    supported = [obj for obj in objects if obj.type in EXPORTABLE_TYPES]
    for obj in objects:
        if obj.type not in EXPORTABLE_TYPES:
            add('INFO', 'SKIPPED_OBJECT', obj,
                f"{obj.type.title()} object is not exported as mesh geometry.",
                "It will be left out of the pack.")

    if not objects:
        add('ERROR', 'NO_OBJECTS', None, "Nothing is selected for export." if use_selection else "The scene is empty.",
            "Select at least one mesh, curve, text, surface, or metaball.")
    elif not supported:
        add('ERROR', 'NO_MESHES', None, "No exportable geometry was found.",
            "Select a mesh or a Blender object that can be converted to a mesh.")
    if not numpy_available:
        add('ERROR', 'NUMPY_REQUIRED', None,
            "NumPy is missing from Blender's Python environment.",
            "Install NumPy into Blender's Python, restart Blender, and run Check again.")

    try:
        depsgraph = context.evaluated_depsgraph_get()
    except Exception as ex:
        add('ERROR', 'DEPSGRAPH', None, f"Blender could not evaluate the scene: {ex}",
            "Try updating the scene or reopen the file before exporting.")
        return issues

    for obj in supported:
        obj_eval = None
        mesh_eval = None
        try:
            obj_eval = obj.evaluated_get(depsgraph)
            mesh_eval = obj_eval.to_mesh()
            if mesh_eval is None:
                add('ERROR', 'MESH_EVALUATION', obj, "This object did not produce an exportable mesh.",
                    "Check its modifiers and object data, then try again.")
                continue

            mesh_eval.calc_loop_triangles()
            if not mesh_eval.vertices or not mesh_eval.loop_triangles:
                add('ERROR', 'EMPTY_MESH', obj, "This object has no triangles to export.",
                    "Add visible geometry or remove it from the export selection.")

            bad_position = next((v.index for v in mesh_eval.vertices
                                 if not all(math.isfinite(float(c)) for c in v.co)), None)
            if bad_position is not None:
                add('ERROR', 'INVALID_VERTEX', obj,
                    f"Vertex {bad_position} contains NaN or infinity.",
                    "Repair or remove invalid geometry before exporting.")
            bad_normal = next((i for i, loop in enumerate(mesh_eval.loops)
                               if not all(math.isfinite(float(c)) for c in loop.normal)), None)
            if bad_normal is not None:
                add('ERROR', 'INVALID_NORMAL', obj,
                    f"Loop normal {bad_normal} contains NaN or infinity.",
                    "Recalculate or repair the mesh normals before exporting.")

            if not mesh_eval.uv_layers.active:
                has_image_material = False
                has_complex_color = False
                for slot in obj.material_slots:
                    material_warnings = []
                    try:
                        surface = gather_surface(slot.material, material_warnings,
                                                 defer_albedo_warnings=True)
                        has_image_material |= bool(surface.get("images"))
                        has_complex_color |= bool(surface.get("needs_bake"))
                    except Exception:
                        continue
                if has_image_material and not (has_complex_color and bake_complex):
                    add('WARNING', 'MISSING_UV', obj,
                        "Image textures are connected but the mesh has no active UV map.",
                        "Create or select a UV map before exporting for correct texture placement.")
                elif has_complex_color and bake_complex:
                    add('INFO', 'BAKE_UV_GENERATION', obj,
                        "A temporary smart UV map will be generated for the node-material bake.",
                        "Ease keeps this generated UV map in the exported mesh without changing the Blender object.")
                else:
                    add('INFO', 'NO_UV', obj, "No active UV map; vertex UVs will default to zero.")
            else:
                uv_data = mesh_eval.uv_layers.active.data
                bad_uv = next((i for i, uv in enumerate(uv_data)
                               if not all(math.isfinite(float(c)) for c in uv.uv)), None)
                if bad_uv is not None:
                    add('ERROR', 'INVALID_UV', obj,
                        f"UV coordinate {bad_uv} contains NaN or infinity.",
                        "Repair the UV map before exporting.")

            if not obj.material_slots:
                add('WARNING', 'NO_MATERIALS', obj, "No material slots are assigned; Strata will use its neutral fallback.",
                    "Assign a Principled material if you want authored surface values.")

            face_materials = {tri.material_index for tri in mesh_eval.loop_triangles}
            for material_index in sorted(face_materials):
                mat = (obj.material_slots[material_index].material
                       if 0 <= material_index < len(obj.material_slots) else None)
                if mat is None:
                    add('WARNING', 'EMPTY_MATERIAL_SLOT', obj,
                        f"Polygon group {material_index} has no material; it exports with a neutral fallback.",
                        "Assign a material to this slot or remove the empty slot.")
                    continue

                material_warnings = []
                try:
                    surface = gather_surface(mat, material_warnings,
                                             defer_albedo_warnings=True)
                except Exception as ex:
                    add('WARNING', 'MATERIAL_READ', obj,
                        f"Material '{mat.name}' could not be inspected: {ex}",
                        "Check the material nodes and reconnect supported Principled inputs.")
                    continue

                if surface.get("needs_bake") and bake_complex:
                    if not cycles_available:
                        add('ERROR', 'CYCLES_REQUIRED', obj,
                            f"Material '{mat.name}' has a complex color graph, but Cycles is unavailable.",
                            "Use a Blender build with Cycles, or disable complex color baking to export a scalar fallback.")
                    elif getattr(context, "mode", "OBJECT") != 'OBJECT':
                        add('ERROR', 'BAKE_OBJECT_MODE', obj,
                            f"Material '{mat.name}' needs baking, which requires Object Mode.",
                            "Exit Edit Mode or the current object mode, then run Check again.")
                    else:
                        add('INFO', 'NODE_COLOR_BAKE', obj,
                            f"Material '{mat.name}' uses a complex Base Color graph; Ease will bake it to a texture.",
                            "The source material and object remain unchanged.")
                elif surface.get("needs_bake") and not bake_complex:
                    material_warnings.extend(surface.get("albedo_warnings", []))
                else:
                    material_warnings.extend(surface.get("albedo_warnings", []))

                for warning in material_warnings:
                    if "manifest albedo=" in warning:
                        continue
                    if ("empty slot" in warning or "neutral fallback" in warning or
                            "scalar fallback" in warning or "may differ from viewport" in warning):
                        add('WARNING', 'MATERIAL_FALLBACK', obj, warning,
                            "Review this material; unsupported node graphs export using scalar values or neutral defaults.")
                    else:
                        add('INFO', 'MATERIAL_NOTE', obj, warning)

                for role, image in surface.get("images", {}).items():
                    source = image_source(image)
                    if source.startswith("missing:"):
                        add('WARNING', 'MISSING_IMAGE', obj,
                            f"Texture '{image.name}' for {role} cannot be found on disk.",
                            "Pack the image into the .blend or relink it before exporting.")
                    uses_pillow = ((os.path.isfile(source) or role in {'metallic', 'roughness'})
                                   and not (role == 'albedo' and surface.get("needs_bake")
                                            and bake_complex))
                    if uses_pillow and not pillow_available:
                        add('ERROR', 'PILLOW_REQUIRED', obj,
                            f"Texture '{image.name}' for {role} needs Pillow to export correctly.",
                            "Install Pillow into Blender's Python, restart Blender, and run Check again.")
                    try:
                        if image.size[0] <= 0 or image.size[1] <= 0 or not image.has_data:
                            add('WARNING', 'EMPTY_IMAGE', obj,
                                f"Texture '{image.name}' has no loaded pixel data.",
                                "Reload or replace this image before exporting.")
                    except Exception:
                        add('WARNING', 'IMAGE_READ', obj,
                            f"Texture '{image.name}' could not be inspected.",
                            "Reload or replace this image before exporting.")

            matrix_values = [float(v) for row in obj.matrix_world for v in row]
            if not all(math.isfinite(v) for v in matrix_values):
                add('ERROR', 'INVALID_TRANSFORM', obj, "The object transform contains NaN or infinity.",
                    "Reset the invalid transform values before exporting.")
        except Exception as ex:
            add('ERROR', 'OBJECT_CHECK', obj, f"Preflight could not inspect this object: {ex}",
                "Check the object's modifiers and data, then run preflight again.")
        finally:
            if mesh_eval is not None and obj_eval is not None:
                try:
                    obj_eval.to_mesh_clear()
                except Exception:
                    pass

    severity_order = {'ERROR': 0, 'WARNING': 1, 'INFO': 2}
    issues.sort(key=lambda issue: (severity_order[issue["severity"]], issue["object_name"], issue["message"]))
    return issues


class EASE_PG_Issue(bpy.types.PropertyGroup):
    severity: bpy.props.EnumProperty(items=[
        ('ERROR', "Error", "Export must be fixed before continuing"),
        ('WARNING', "Warning", "Export will continue with a fallback or limitation"),
        ('INFO', "Info", "Additional export detail"),
    ])
    code: bpy.props.StringProperty()
    object_name: bpy.props.StringProperty()
    message: bpy.props.StringProperty()
    suggestion: bpy.props.StringProperty()


class EASE_PG_Settings(bpy.types.PropertyGroup):
    use_selection: bpy.props.BoolProperty(name="Selected Objects", default=True)
    global_scale: bpy.props.FloatProperty(name="Scale", default=1.0, min=0.001, max=1000.0)
    bake_complex_materials: bpy.props.BoolProperty(
        name="Bake complex color graphs", default=True,
        description="Bake unsupported Base Color node graphs to textures for export")
    bake_resolution: bpy.props.IntProperty(
        name="Texture Size", default=1024, min=256, max=4096, step=256,
        description="Resolution used for baked material color textures")
    issues: bpy.props.CollectionProperty(type=EASE_PG_Issue)
    issue_index: bpy.props.IntProperty(default=0)
    last_checked: bpy.props.StringProperty(default="")
    last_export: bpy.props.StringProperty(default="")


def store_issues(settings, issues):
    settings.issues.clear()
    for issue in issues[:100]:
        item = settings.issues.add()
        item.severity = issue["severity"]
        item.code = issue["code"]
        item.object_name = issue["object_name"]
        item.message = issue["message"]
        item.suggestion = issue["suggestion"]
    settings.last_checked = time.strftime("%H:%M:%S")


class EASE_OT_preflight(bpy.types.Operator):
    bl_idname = "ease.stratapack_preflight"
    bl_label = "Check Export"
    bl_description = "Inspect geometry, materials, UVs, and texture links without changing the scene"
    bl_options = {'REGISTER'}

    def execute(self, context):
        settings = context.scene.ease_settings
        issues = inspect_scene(context, settings.use_selection,
                               settings.bake_complex_materials)
        store_issues(settings, issues)
        errors = sum(issue["severity"] == 'ERROR' for issue in issues)
        warnings = sum(issue["severity"] == 'WARNING' for issue in issues)
        if errors:
            self.report({'ERROR'}, f"Preflight found {errors} error(s) and {warnings} warning(s). Review the BlueSky panel.")
        elif warnings:
            self.report({'WARNING'}, f"Ready with {warnings} warning(s). Review the BlueSky panel.")
        else:
            self.report({'INFO'}, "Preflight complete. The scene is ready to export.")
        return {'FINISHED'}


class EASE_OT_select_issue(bpy.types.Operator):
    bl_idname = "ease.select_issue_object"
    bl_label = "Select Object"
    bl_description = "Select and frame the object related to this report item"
    object_name: bpy.props.StringProperty()

    def execute(self, context):
        obj = context.scene.objects.get(self.object_name)
        if obj is None:
            self.report({'WARNING'}, "This object is no longer in the scene. Run Check Export again.")
            return {'CANCELLED'}
        try:
            for selected in context.selected_objects:
                selected.select_set(False)
            obj.select_set(True)
            context.view_layer.objects.active = obj
        except Exception as ex:
            self.report({'ERROR'}, f"Could not select '{obj.name}': {ex}")
            return {'CANCELLED'}

        for area in context.screen.areas:
            if area.type != 'VIEW_3D':
                continue
            region = next((r for r in area.regions if r.type == 'WINDOW'), None)
            if region is None:
                continue
            try:
                with context.temp_override(area=area, region=region):
                    bpy.ops.view3d.view_selected(use_all_regions=False)
            except Exception:
                pass
            break
        return {'FINISHED'}


class UI_UL_ease_issues(bpy.types.UIList):
    def draw_item(self, context, layout, data, item, icon, active_data,
                  active_propname, index):
        row = layout.row(align=True)
        severity_icon = 'ERROR' if item.severity == 'ERROR' else ('INFO' if item.severity == 'WARNING' else 'DOT')
        row.label(text="", icon=severity_icon)
        row.label(text=item.message)
        if item.object_name and context.scene.objects.get(item.object_name):
            op = row.operator(EASE_OT_select_issue.bl_idname, text="", icon='RESTRICT_SELECT_OFF')
            op.object_name = item.object_name

    def filter_items(self, context, data, propname):
        collection = getattr(data, propname)
        return [self.bitflag_filter_item] * len(collection), []


class EASE_OT_preview(bpy.types.Operator):
    bl_idname = "ease.preview_materials"
    bl_label = "Preview in Viewport"
    bl_description = "Frame the export set and show Blender's material preview"

    def execute(self, context):
        view_area = next((a for a in context.screen.areas if a.type == 'VIEW_3D'), None)
        if view_area is None:
            self.report({'WARNING'}, "Open a 3D Viewport to preview this export set.")
            return {'CANCELLED'}
        view_area.spaces.active.shading.type = 'MATERIAL'
        region = next((r for r in view_area.regions if r.type == 'WINDOW'), None)
        if region is not None:
            try:
                with context.temp_override(area=view_area, region=region):
                    if context.scene.ease_settings.use_selection:
                        bpy.ops.view3d.view_selected(use_all_regions=False)
                    else:
                        bpy.ops.view3d.view_all(center=False)
            except Exception:
                pass
        return {'FINISHED'}


class VIEW3D_PT_bluesky_ease(bpy.types.Panel):
    bl_label = "BlueSky Ease"
    bl_idname = "VIEW3D_PT_bluesky_ease"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = "BlueSky"
    bl_options = {'DEFAULT_CLOSED'}

    def draw(self, context):
        layout = self.layout
        settings = context.scene.ease_settings
        layout.use_property_split = True
        layout.use_property_decorate = False

        hero = layout.box()
        row = hero.row()
        row.label(text="STRATAPACK", icon='EXPORT')
        row.label(text="Ease")
        hero.label(text="A clear path from Blender scene to BlueSky.")

        setup = layout.box()
        setup.label(text="Export Set", icon='OUTLINER_COLLECTION')
        setup.prop(settings, "use_selection")
        setup.prop(settings, "global_scale")
        actions = setup.row(align=True)
        actions.operator(EASE_OT_preview.bl_idname, text="Preview", icon='SHADING_MATERIAL')
        actions.operator(EASE_OT_preflight.bl_idname, text="Check", icon='CHECKMARK')

        node_capture = layout.box()
        node_capture.label(text="Node Materials", icon='MATERIAL')
        node_capture.prop(settings, "bake_complex_materials")
        if settings.bake_complex_materials:
            node_capture.prop(settings, "bake_resolution")

        report = layout.box()
        counts = {severity: sum(i.severity == severity for i in settings.issues)
                  for severity in ('ERROR', 'WARNING', 'INFO')}
        if not settings.last_checked:
            report.label(text="Run a check before exporting.", icon='INFO')
        elif counts['ERROR']:
            report.label(text=f"Needs attention  ·  {counts['ERROR']} errors", icon='ERROR')
        elif counts['WARNING']:
            report.label(text=f"Ready with notes  ·  {counts['WARNING']} warnings", icon='INFO')
        else:
            report.label(text="Ready to export", icon='CHECKMARK')
        if settings.last_checked:
            report.label(text=f"Last checked {settings.last_checked}")
        if settings.issues:
            report.template_list("UI_UL_ease_issues", "ease_report", settings, "issues",
                                 settings, "issue_index", rows=5, maxrows=8)
            if 0 <= settings.issue_index < len(settings.issues):
                issue = settings.issues[settings.issue_index]
                if issue.suggestion:
                    report.label(text=issue.suggestion, icon='BLANK1')
        else:
            report.label(text="No issues to display yet.", icon='INFO')

        export_box = layout.box()
        export_box.label(text="Build Pack", icon='FILE_FOLDER')
        export = export_box.operator(EXPORT_OT_stratapack.bl_idname,
                                     text="Export StrataPack", icon='EXPORT')
        export.use_selection = settings.use_selection
        export.global_scale = settings.global_scale
        export.bake_complex_materials = settings.bake_complex_materials
        export.bake_resolution = settings.bake_resolution
        if settings.last_export:
            export_box.label(text=f"Last pack: {os.path.basename(settings.last_export)}", icon='FILE_TICK')


# --------------------------------------------------------------------------
# Operator
# --------------------------------------------------------------------------

@orientation_helper(axis_forward='-Z', axis_up='Y')
class EXPORT_OT_stratapack(bpy.types.Operator, ExportHelper):
    bl_idname = "export_scene.stratapack"
    bl_label = "Export StrataPack"
    filename_ext = ".stratapack"
    filter_glob: bpy.props.StringProperty(default="*.stratapack", options={'HIDDEN'})
    use_selection: bpy.props.BoolProperty(name="Selected Only", default=True)
    global_scale: bpy.props.FloatProperty(name="Scale", default=1.0, min=0.001, max=1000.0)
    bake_complex_materials: bpy.props.BoolProperty(default=True)
    bake_resolution: bpy.props.IntProperty(default=1024, min=256, max=4096, step=256)

    def execute(self, context):
        settings = context.scene.ease_settings
        issues = inspect_scene(context, self.use_selection,
                               self.bake_complex_materials)
        store_issues(settings, issues)
        errors = sum(issue["severity"] == 'ERROR' for issue in issues)
        if errors:
            self.report({'ERROR'}, f"Export stopped: fix the {errors} error(s) shown in the BlueSky panel.")
            return {'CANCELLED'}

        warnings = []
        window_manager = getattr(context, "window_manager", None)
        export_objects = context.selected_objects if self.use_selection else context.scene.objects
        progress_total = max(1, len(export_objects) + 1)
        try:
            if window_manager is not None:
                window_manager.progress_begin(0, progress_total)
            export_stratapack(self, context, self.filepath, warnings)
            settings.last_export = os.path.abspath(bpy.path.abspath(self.filepath))
        except Exception as ex:
            self.report({'ERROR'}, f"Export failed. The previous pack was preserved: {ex}")
            return {'CANCELLED'}
        finally:
            if window_manager is not None:
                try:
                    window_manager.progress_end()
                except Exception:
                    pass

        useful_warnings = [w for w in warnings
                           if "manifest albedo=" not in w and not w.startswith("skeleton '")]
        if useful_warnings:
            report_issues = [{"severity": issue.severity, "code": issue.code,
                              "object_name": issue.object_name, "message": issue.message,
                              "suggestion": issue.suggestion}
                             for issue in settings.issues]
            report_issues.extend({"severity": "WARNING", "code": "EXPORT_NOTE",
                                  "object_name": "", "message": note,
                                  "suggestion": "Review this export note before relying on the pack."}
                                 for note in useful_warnings)
            store_issues(settings, report_issues)
        if useful_warnings:
            self.report({'WARNING'}, f"Pack exported with {len(useful_warnings)} note(s). Review the BlueSky panel for details.")
        else:
            self.report({'INFO'}, f"StrataPack exported: {os.path.basename(settings.last_export)}")
        return {'FINISHED'}


def export_stratapack(op, context, filepath, warnings):
    global_matrix = Matrix.Scale(op.global_scale, 4) @ axis_conversion(
        to_forward=op.axis_forward, to_up=op.axis_up).to_4x4()
    flip_winding = global_matrix.determinant() < 0.0
    if flip_winding:
        warnings.append("Negative-determinant export matrix: winding flipped")

    depsgraph = context.evaluated_depsgraph_get()
    objs = ([o for o in context.selected_objects]
            if op.use_selection else list(context.scene.objects))

    header = {"meshes": [], "surfaces": [], "textures": [],
              "aoMaps": [], "bentMaps": [], "skeletons": []}
    blobs = []
    payload_size = [0]
    slot_counter = [0]

    def emit_blob(data: bytes):
        off = payload_size[0]
        payload_size[0] += len(data)
        blobs.append(data)
        return off, len(data)

    window_manager = getattr(context, "window_manager", None)
    for object_index, obj in enumerate(objs):
        if window_manager is not None:
            try:
                window_manager.progress_update(object_index)
            except Exception:
                pass
        if obj.type not in {'MESH', 'CURVE', 'SURFACE', 'FONT', 'META'}:
            continue
        # Skeletal: neutralize the armature pose BEFORE evaluation so the
        # exported mesh == rest pose (bind pose == weight source). Restored
        # in the finally below. Statics skip everything (arm_info None).
        arm_info, saved_pose = neutralize_armature(obj, depsgraph, warnings)
        obj_eval = None
        try:
            obj_eval = obj.evaluated_get(depsgraph)
            try:
                mesh_eval = obj_eval.to_mesh()
            except Exception as ex:
                warnings.append(f"Object '{obj.name}': to_mesh failed ({ex})")
                continue

            mesh_eval.calc_loop_triangles()
            material_indices = sorted({tri.material_index
                                       for tri in mesh_eval.loop_triangles})
            surfaces_by_material = {}
            complex_material_indices = []
            for material_index in material_indices:
                material = None
                try:
                    if 0 <= material_index < len(obj.material_slots):
                        material = obj.material_slots[material_index].material
                except Exception:
                    pass
                material_notes = []
                surface = gather_surface(material, material_notes,
                                         defer_albedo_warnings=True)
                surfaces_by_material[material_index] = surface
                warnings.extend(material_notes)
                if surface.get("needs_bake"):
                    complex_material_indices.append(material_index)
                else:
                    warnings.extend(surface.get("albedo_warnings", []))

            if complex_material_indices:
                if not getattr(op, "bake_complex_materials", True):
                    for material_index in complex_material_indices:
                        warnings.extend(surfaces_by_material[material_index]
                                        .get("albedo_warnings", []))
                else:
                    resolution = min(4096, max(256, int(getattr(op, "bake_resolution", 1024))))
                    try:
                        baked_albedos = bake_node_material_albedos(
                            context, obj_eval, mesh_eval, complex_material_indices, resolution)
                    except Exception as ex:
                        raise RuntimeError(
                            f"Object '{obj.name}': complex material color bake failed: {ex}") from ex
                    for material_index in complex_material_indices:
                        baked = baked_albedos.get(material_index)
                        if baked is None:
                            raise RuntimeError(
                                f"Object '{obj.name}': no baked color was produced for material slot {material_index}")
                        surface = surfaces_by_material[material_index]
                        surface.setdefault("images", {})["albedo"] = baked
                        surface.setdefault("provenance", {})["albedo"] = {
                            "kind": "baked", "mapping": None}

            positions, normals, uvs, indices, submeshes, vindices = emit_object(
                obj_eval, mesh_eval, global_matrix, flip_winding)
            try:
                active_uv = mesh_eval.uv_layers.active
                uv_name = active_uv.name if active_uv is not None else ""
            except Exception:
                uv_name = ""

            # Packed32 vertex blob.
            vblob = bytearray()
            for p, n, uv in zip(positions, normals, uvs):
                vblob += struct.pack("<3f", *p)
                vblob += struct.pack("<3f", *n)
                vblob += struct.pack("<2f", *uv)
            iblob = bytearray()
            for ii in indices:
                iblob += struct.pack("<I", ii)
            voff, vlen = emit_blob(bytes(vblob))
            ioff, ilen = emit_blob(bytes(iblob))

            mesh_rec = {"name": obj.name,
                        "vertexCount": len(positions),
                        "indexCount": len(indices),
                        "submeshes": [],
                        "vertexOffset": voff, "vertexSize": vlen,
                        "indexOffset": ioff, "indexSize": ilen,
                        "aoOffset": 0, "aoSize": 0, "aoCount": 0,
                        "skinOffset": 0, "skinSize": 0, "skinCount": 0,
                        "skeletonIndex": -1}
            # Skeletal skin stream + skeleton record (statics: all zeros).
            soff, slen, scount, skel_idx = emit_skin(
                obj, mesh_eval, vindices, arm_info, global_matrix,
                warnings, header, blobs, emit_blob)
            mesh_rec.update({"skinOffset": soff, "skinSize": slen,
                             "skinCount": scount, "skeletonIndex": skel_idx})

            for sub in submeshes:
                slot = slot_counter[0]
                slot_counter[0] += 1
                mat = None
                try:
                    if 0 <= sub["mat_index"] < len(obj.material_slots):
                        mat = obj.material_slots[sub["mat_index"]].material
                except Exception:
                    mat = None
                surf = surfaces_by_material.get(sub["mat_index"])
                if surf is None:
                    surf = gather_surface(mat, warnings)

                # Texture blobs: albedo (sRGB bytes kept), normal (linear),
                # RMA packed here (R=rough, G=metal, B=255).
                # uv/transform recorded per record (informational; renderer TBD).
                def _prov(role):
                    p = surf["provenance"].get(role, {})
                    return p.get("mapping")

                tex_recs = []
                src_entries = []
                alb = surf["images"].get('albedo')
                if alb is not None:
                    ar = image_to_rgba8(alb, warnings)
                    if ar is not None:
                        aw, ah, abytes = ar
                        aoff, alen = emit_blob(abytes)
                        amap = _prov("albedo")
                        tex_recs.append({"slot": slot, "role": "albedo",
                                         "colorspace": "srgb", "width": aw, "height": ah,
                                         "offset": aoff, "size": alen,
                                         "uv": uv_name, "transform": amap})
                        src_entries.append({"slot": slot, "socket": "Base Color",
                                            "image": alb.name,
                                            "file": image_source(alb),
                                            "uv": uv_name, "transform": amap})
                nrm = surf["images"].get('normal')
                nw = nh = 0
                nbytes = None
                if nrm is not None:
                    nr = image_to_rgba8(nrm, warnings)
                    if nr is not None:
                        nw, nh, nbytes = nr
                        noff, nlen = emit_blob(nbytes)
                        tex_recs.append({"slot": slot, "role": "normal",
                                         "colorspace": "linear", "width": nw, "height": nh,
                                         "offset": noff, "size": nlen,
                                         "uv": uv_name, "transform": _prov("normal")})
                        src_entries.append({"slot": slot, "socket": "Normal",
                                            "image": nrm.name,
                                            "file": image_source(nrm),
                                            "uv": uv_name, "transform": _prov("normal")})
                rw, rh = 64, 64
                if nbytes is not None:
                    rw, rh = nw, nh
                elif alb is not None:
                    ar2 = image_to_rgba8(alb, warnings)
                    if ar2 is not None:
                        rw, rh = ar2[0], ar2[1]
                metal_px = None
                if surf["images"].get('metallic') is not None:
                    metal_px = image_to_rgba8(surf["images"]['metallic'], warnings)
                rough_px = None
                if surf["images"].get('roughness') is not None:
                    rough_px = image_to_rgba8(surf["images"]['roughness'], warnings)
                rma = pack_rma_blob(metal_px, rough_px,
                                    rw, rh, surf["metallic"], surf["roughness"])
                if rma is not None:
                    roff, rlen = emit_blob(rma)
                    tex_recs.append({"slot": slot, "role": "rma",
                                     "colorspace": "linear", "width": rw, "height": rh,
                                     "offset": roff, "size": rlen,
                                     "uv": uv_name, "transform": None})
                header["textures"].extend(tex_recs)
                # Bent-normal map: hemisphere occlusion baked in export space,
                # world encoding, sampled with the mesh UV. Renderer TBD note
                # on other roles does not apply: t9 reads this directly.
                bent = bake_bent_blob(
                    mesh_eval, positions, normals, uvs, indices, sub,
                    global_matrix, warnings)
                if bent is not None:
                    bw, bh, bbytes = bent
                    boff, blen = emit_blob(bbytes)
                    # Canonical "bentMaps" key (matches StrataPack.PackHeader;
                    # the old "bent" key never decoded — case matters beyond case).
                    header["bentMaps"].append({"slot": slot, "role": "bent",
                        "colorspace": "linear", "width": bw, "height": bh,
                        "offset": boff, "size": blen, "encoding": "world"})
                if src_entries:
                    for e in src_entries:
                        e["material"] = mat.name if mat is not None else None
                    header.setdefault("sourcemap", []).extend(src_entries)

                mesh_rec["submeshes"].append(
                    {"offset": sub["offset"], "count": sub["count"], "slot": slot})
                header["surfaces"].append({
                    "slot": slot,
                    "albedoLinear": surf["albedo"],
                    "metallic": surf["metallic"], "roughness": surf["roughness"],
                    "emissiveLinear": surf["emissive"],
                    "emissiveIntensity": surf["emissive_strength"],
                    "clearcoat": surf.get("clearcoat", 0.0),
                    "clearcoatTintLinear": surf.get("clearcoat_tint", [1.0, 1.0, 1.0]),
                    "alpha": surf.get("alpha", 1.0),
                    "sheen": surf.get("sheen", 0.0),
                    "anisotropy": surf.get("anisotropy", 0.0),
                })

            header["meshes"].append(mesh_rec)
        finally:
            if obj_eval is not None:
                try:
                    obj_eval.to_mesh_clear()
                except Exception:
                    pass
            restore_error = restore_armature(obj, saved_pose)
            if restore_error:
                warnings.append(f"Object '{obj.name}': failed to restore armature pose ({restore_error})")
                raise RuntimeError(f"Could not restore '{obj.name}' armature pose: {restore_error}")

    # Phase-B probes: scene-level (not per-mesh), after the object loop.
    bake_probes(context, global_matrix, warnings, header)

    write_pack(filepath, header, blobs)
    if window_manager is not None:
        try:
            window_manager.progress_update(len(objs))
        except Exception:
            pass
    return {'FINISHED'}


def menu_func(self, context):
    self.layout.operator(EXPORT_OT_stratapack.bl_idname, text="StrataPack (.stratapack)")


def register():
    classes = (EASE_PG_Issue, EASE_PG_Settings, EASE_OT_preflight,
               EASE_OT_select_issue, UI_UL_ease_issues, EASE_OT_preview,
               EXPORT_OT_stratapack, VIEW3D_PT_bluesky_ease)
    registered = []
    scene_property_added = False
    menu_added = False
    try:
        for cls in classes:
            bpy.utils.register_class(cls)
            registered.append(cls)
        bpy.types.Scene.ease_settings = bpy.props.PointerProperty(type=EASE_PG_Settings)
        scene_property_added = True
        bpy.types.TOPBAR_MT_file_export.append(menu_func)
        menu_added = True
    except Exception:
        # Blender can reject registration when a class has a version-specific
        # property/API mismatch. Undo the partial setup so the add-on can be
        # fixed and enabled again without leaving stale panel classes behind.
        if menu_added:
            try:
                bpy.types.TOPBAR_MT_file_export.remove(menu_func)
            except (ValueError, AttributeError):
                pass
        if scene_property_added and hasattr(bpy.types.Scene, "ease_settings"):
            del bpy.types.Scene.ease_settings
        for cls in reversed(registered):
            try:
                bpy.utils.unregister_class(cls)
            except (RuntimeError, ValueError):
                pass
        raise


def unregister():
    try:
        bpy.types.TOPBAR_MT_file_export.remove(menu_func)
    except (ValueError, AttributeError):
        pass
    if hasattr(bpy.types.Scene, "ease_settings"):
        del bpy.types.Scene.ease_settings
    classes = (VIEW3D_PT_bluesky_ease, EXPORT_OT_stratapack,
               EASE_OT_preview, UI_UL_ease_issues, EASE_OT_select_issue,
               EASE_OT_preflight, EASE_PG_Settings, EASE_PG_Issue)
    for cls in classes:
        try:
            bpy.utils.unregister_class(cls)
        except (RuntimeError, ValueError):
            pass


if __name__ == "__main__":
    register()
