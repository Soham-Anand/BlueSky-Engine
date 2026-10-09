# StrataPack Plan — Blender-Only Ingestion, Zero Handwritten Parsers

> **Doctrine:** BlueSky doesn't support arbitrary third-party asset semantics.
> It supports the BlueSky asset specification. Blender/Ease is the authoring
> compiler for that specification.

## 1. Vision

Today the engine carries ~4,800 lines of handwritten importers (FBX binary,
glTF, OBJ, MTL, FBX Video texture discovery) plus a second loophole OBJ loader.
Every format has its own semantics, its own bugs, its own drift. The cut:

```text
              BLENDER + EASE
                    │
                    │  author / bake / convert
                    ▼
             ┌──────────────┐
             │ .stratapack  │
             └──────┬───────┘
                    │
                    │ ONLY native import path
                    ▼
          StratapackImportHandler
                    │
          ┌─────────┴─────────┐
          ▼                   ▼
   .blueskyasset          Strata data
   Packed32 mesh          textures/materials
          │                   │
          └─────────┬─────────┘
                    ▼
             Liveries / Astra
```

Instead of FBX parser + GLTF parser + OBJ parser + MTL parser + texture
extraction + video extraction + skeletal conversion + material conversion,
the engine ends up with: **Ease → STRATAPACK → one deterministic importer.**

The strongest constraint: **Packed32 stays unchanged.** No simultaneous redesign
of mesh, material, texture, or renderer representations. New work is
decode-to-existing-representations, never a renderer rewrite.

Consequences (stated plainly): no more internet models — every mesh passes
through Blender. Existing `.blueskyasset` content keeps working. The addon must
be frictionless day one.

## 2. Container Spec (v1, static meshes)

```text
magic "STRATAPK" (8B) | uint32 version (=1) | uint64 header_size
| uint64 payload_size | JSON header | binary payload
```

JSON header: `meshes[] {name, vertexCount, indexCount,
submeshes[{offset, count, slot}]}`, `surfaces[] {slot, albedoLinear[3],
metallic, roughness, emissive...}`, `textures[] {slot, role, colorspace,
w, h, offset, size}`, plus Phase-A lighting samples (below). Payload:
Packed32 vertex/index blobs + RGBA8 texture blobs (RMA-packed where applicable).

### Container philosophy (locked)

`.stratapack` is a **transfer format, never a runtime asset.** Import converts
it into `.blueskyasset` (+ `Materials/StrataFiles/` + `Materials/Textures/` +
`strataSlot` links) and the pack itself does not stay in the project. The whole
reason Ease exists: slot assignment, texture fetching, and role classification
happen in Blender, so the engine never hunts for anything — it splits a
pre-decided layout into files it already understands.

Open format by policy: magic + version first, human-readable JSON header, no
encryption. Debuggability beats asset protection at this scale.

### Phase-A lighting samples (part of the v1 contract)

Ease bakes lighting and the pack carries it; the engine receives pre-lit data:

```text
Ease
 ├─ bake AO
 ├─ bake bent normals
 └─ write them into StrataPack
             ↓
StrataPack
 ├─ mesh
 ├─ surfaces
 ├─ textures
 ├─ AO
 └─ bent normals
             ↓
existing Tier-1 shader path
```

Schema rules (no ambiguity allowed):

* Texture-backed AO/bent travel as explicit blob arrays with full descriptors:
```text
aomap[]
    { slot, role, colorspace, w, h, offset, size }

bent[]
    { slot, role, colorspace, w, h, offset, size, encoding }
```
* `encoding` on every `bent[]` entry declares the normal space
  (`tangent` | `object` | `packed-xyz` …). The importer never guesses.
* Vertex-backed AO is a mesh attribute/stream on the mesh record — never
  disguised as a texture blob.
* AO feeds the existing RMA.B slot; bent normals feed the Tier-1 path when the
  `BentNormal` mask bit is set, with plain baked AO as the universal fallback.

### Decoder validation (all required, fail loudly, no recovery)

1. magic
2. supported version
3. header bounds
4. payload bounds
5. every blob's `offset + size` (overflow-safe arithmetic)
6. mesh index ranges
7. texture ranges
8. declared counts versus available data
9. duplicate/invalid slots
10. malformed JSON
11. integer overflow when calculating ranges
12. truncated file

Each check names the offending field. `.stratapack` is the one and only
engine import contract — a corrupt pack must never produce a wrong mesh.

## 3. BlueSky Engine Ease (Blender Addon, `Blender/`)

~200 lines, `ExportHelper` file browser, selected-objects only:

* **Mesh:** evaluated depsgraph → `to_mesh()` → `calc_loop_triangles()` →
  dedup on (pos, uv, loop-normal) → indexed tris, one primitive group per
  `material_index`. Never read `obj.data` directly (pre-modifier cage).
* **Materials:** Principled BSDF per slot → scalars direct; `TEX_IMAGE` links
  (unwrapping one `NormalMap` level) → image refs; procedural graphs fall back
  to scalar + warning.
* **Images:** referenced files copied as-is; packed/generated saved via
  `save_render`; `colorspace_settings` recorded per image. Raw `pixels` buffer
  only for channel repacking (ORM); `foreach_get`, never per-element loops.
* **Transform:** Z-up → Y-up via `axis_conversion` + scale, baked once at
  export; winding flip check on negative determinant; unapplied-transform and
  mirrored-object warnings.
* Writes single `.stratapack` (JSON header + concatenated blobs with offsets).
* **Bakes Phase-A lighting samples:** per-mesh AO (texture or vertex attribute
  — see schema rules in §2) + bent normals with declared encoding.

Gotchas ledger: units/scale_length, winding, modifiers (evaluated vs original),
loop-vs-vert indexing, Principled-only, sRGB vs Non-Color, slot None-checks,
UDIM/sequences skipped in v1 with warnings.

## 4. Engine Changes

**New:**
* `StrataPack.cs` — authoritative codec (modeled on `StrataCodec`), round-trip
  + all-12-validations test battery, checked-in sample pack fixture.
* `StratapackImportHandler(.stratapack)` — splits packs into the existing
  layout (`mesh.blueskyasset` + `Materials/StrataFiles/` +
  `Materials/Textures/` + `strataSlot{i}` links) via `StrataImportWriter`.
  Rejects non-pack files loudly.

**Deleted (16 files, ~4,800 lines):** all of `Animation/FBX/` (9, incl. the
`Video` texture extractor — "Video" is FBX-spec terminology for texture
containers, not video playback), all of `Animation/GLTF/` (5),
`Core/Assets/OBJParser.cs`, `Core/Assets/StrataMtlReader.cs`,
`Rendering/MeshLoader.cs` (closes the loophole second OBJ path).

**Repaired callers:** dispatcher keeps shell + `ScriptImportHandler`;
`AssetManager` direct-GLTF path dies; `SkeletalMeshImporter`/`SkeletalMeshEditor`
bone paths fall back to boneless-static; tests drop parser fixtures for
synthetic Packed32 + link round-trips + pack acceptance test.

**Keepers:** `StrataImportWriter.cs`, `Rendering/Strata/*`, `AssetLoader.cs`
(Packed32 reader), `ViewportRenderer.cs` mesh/Strata paths, `BlueAsset.cs`,
asset database/registry.

## 5. Lighting Phases (B/C) + Skeletal v2 (Extension, Not Started)

### Phase B — Ease probes into existing SH uniforms (decoupled, next after A)

```text
Sky capture ─────┐
                 ├──> existing SH uniforms
Ease probes ─────┘
```

The renderer never learns why coefficients exist: captured sky and authored
probes feed the same uniforms. No renderer changes required.

### Phase C — full lightmaps (parked)

Parked deliberately: second UV channels, 10–50× pack bloat, and streaming
complexity exceed what A/B need. Builds only when a scene demands it.

### Skeletal v2

Same container philosophy, additive arrays only:

```text
meshes[]  surfaces[]  textures[]
skeletons[]  weights[]  bindposes[]  animations[]
```

Static first, as agreed. Bones, weights, bind poses, clips land here when earned.

## 6. Build Order

1. `StrataPack.cs` + validation battery + fixture (codec first, like `.stratamat`)
2. `StratapackImportHandler` (split to existing layout + links; rejects loudly)
3. Ease addon in `Blender/` + sample pack + importer acceptance test
   (STATUS: addon written + pack layout proven against the C# decoder
   via stubbed-bpy validation and the checked-in ease_quad fixture;
   live-Blender run still required)
4. Delete 16 parser files + repair callers + suite green + zero warnings
5. Ease install/use docs

Each step ends: suite green, zero warnings, lecture delivered. Nothing committed
without instruction.
