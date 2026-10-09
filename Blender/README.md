# BlueSky Engine Ease — StrataPack Workspace

Ease provides a dedicated Blender workspace panel for preparing, previewing,
checking, and exporting a scene as a `.stratapack` for BlueSky Engine.

## Install

1. Blender → Edit > Preferences > Add-ons → Install from Disk.
2. Choose `Blender/BlueSkyEngineEase.zip` and enable **BlueSky Engine Ease**.
   You can also install `Blender/strata_pack_exporter.py` directly.
3. In the 3D View, open the **BlueSky** tab in the right sidebar (`N`).

Requires Blender 4.0+ and NumPy in Blender's Python environment. Pillow is
needed to export file-backed image textures and resize image-based metal or
roughness maps. These dependencies must be available to Blender's own Python.

## Workflow

1. Choose selected objects or the full scene and set the export scale.
2. Use **Preview** to frame the export set in Blender's Material Preview.
3. Keep **Bake complex color graphs** enabled for procedural, mixed, grouped,
   or otherwise unsupported material color nodes. Ease uses Cycles to bake the
   evaluated surface color; set the texture size to balance detail and pack size.
   If a mesh has no UVs, Ease creates a temporary smart UV map in the exported
   mesh only.
4. Run **Check**. The report identifies errors, warnings, and information.
   Use the object button beside a report item to select and frame its source.
5. Fix issues in Blender and check again. Ease never silently edits your scene.
6. Export. If a required color bake fails, export stops with a reason. If a
   write fails, the previous pack is preserved; successful packs
   are written atomically.

The familiar **File > Export > StrataPack** entry remains available.

## What Check covers

- Empty export sets and objects that cannot produce triangles
- Missing active UVs when image textures are connected
- Empty material slots and unsupported material channels that fall back to
  scalar or neutral values
- Complex Base Color graphs that will be baked, including whether Cycles or
  Object Mode is missing
- Missing or empty texture images
- Non-finite object transforms

Checks are read-only. Warnings describe export behavior; use the report to jump
to affected objects and decide what to change.

## Current format support

- Packed32 meshes with positions, normals, UVs, indices, and material groups
- Principled surface scalars and supported image links
- Cycles-baked Base Color from mixed, procedural, and grouped shader graphs
- RGBA8 albedo, normal, and packed RMA textures
- Evaluated mesh output, including curves, text, surfaces, and metaballs
- Optional skeletal streams, bent data, and scene lighting probes where the
  source data and current pack contract support them

Complex color graphs are baked into temporary textures without replacing the
source material. Other unsupported channels, such as procedural roughness,
still use the documented scalar fallback and appear as report notes.

## Engine side

Import the `.stratapack` in the editor (or drop it in Assets and reimport): one
mesh `.blueskyasset` plus `Materials/StrataFiles/*.stratamat`,
`Materials/Textures/*`, and `strataSlot{i}` links. Multi-mesh packs and corrupt
packs fail with a reason rather than producing partial imports.

Validate packs with the engine diagnostic suite, which decodes checked-in
fixtures and Python-written fixtures.
