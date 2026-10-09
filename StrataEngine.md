# StrataEngine — High-Level Materials on Older PCs

> **Doctrine:** every system earns a name when it earns sole ownership of its pixels.
> Generic filenames are how accretion happens. `Strata` owns every surface pixel.

## 1. Why Strata Was Built

The engine didn't grow — it accreted. Fifteen commits, 115k lines, systems marked
"Complete" that were TODO scaffolding underneath. Every visual defect traced to one
root cause: **no single piece of the pipeline owned correctness end to end.**

Measured symptoms (all verified in-tree before the amputation):

* **Washed-out colors** — LDR swapchain, lifted blacks (`AmbientPacked(0.15,0.18,0.22)`),
  shadow floor `mix(0.35,1.0,shadow)`, sun `*3.5` into in-shader curves, `pow(1/2.2)` approx.
* **Sky light not applied** — sky was a draw-only background; ambient was hardcoded
  hemisphere constants. The disconnected `LightProbeSystem` / `IrradianceVolume`
  prototypes had zero render-path callers and were removed; EasePlus SH sampling was a `TODO`.
* **True colors missing** — four different tonemaps (Metal ACES vs HLSL Reinhard vs
  Narkowicz ×2) + stray warm multiplies + split albedo colorspace (textures decoded
  as sRGB, uniform factors passed raw).
* **Dead weight** — a second deferred renderer (EasePlus, never tested, only a goal),
  three overlapping particle systems, material resolution through five paths depending
  on which importer you used.

The fix is structural, not incremental: strip to bedrock fully understood
(the April hardcoded-clay shader), then rebuild each system with sole ownership.
No compatibility layers, no slot plumbing, no legacy importers — every one of those
is a second owner of the same pixel.

## 2. Phase 1 — Amputation (COMPLETE)

Target state: today's architecture rendering like commit `4d3b99a` (pre-material days).

**Deleted:**
* Asset system — `MaterialAsset.cs`, `Rendering/Materials/`, `MaterialEditor.cs`,
  `MTLParser.cs`, `TextureImporter.cs`, `TextureLoader.cs`, `PBRLighting.cs`,
  legacy GLTF material path, `LoadMTL`, `MaterialProfiler.cs`
* EasePlus renderer — entire tree, metallibs, build targets, `--ease` flag
* Slots + component — `MaterialAssetId`, `Get/SetMaterialSlot`, `MaterialComponent`,
  all scene slot fields, mesh-editor slot UI, import dialog checkbox
* Importer DTOs — `ExtractMaterial`, all `MaterialData` structs (renamed to
  `Surface*` where the concept is mesh organization, not materials)
* Shaders — material structs/texture sampling/emissive/alpha branches removed from
  `viewport_3d` (Metal+HLSL) and `horizon_lighting`; unused prototype shaders were
  removed; four tonemaps replaced by one

**Interim state (no material engine yet):**
* Every mesh renders historic orange clay `(0.95, 0.5, 0.2)` via one global
  `AstraSurface` constant — full GGX lighting, shadows, hemisphere + sky ambient
  stay live, so lighting response is provable.
* One output curve engine-wide: Khronos PBR Neutral + exact sRGB encode,
  identical math Metal/HLSL.
* `grep -ri material` → zero code identifiers outside diagnostics, file-format
  keys/nodes, and two enum members kept for serialized numbering.
* Engine 0 warnings / 0 errors, Runtime 0/0, full test suite green.

## 3. Target Hardware Constraint

Primary dev machine is a **Mac M4** (Apple Silicon, TBDR, unified memory,
Metal 3) — the HD3000 discipline below is kept as the *floor*, not the ceiling:
everything must clear HD3000-class budgets, and the headroom on M4 buys 
higher resolutions and the Tier-2 lobes, not sloppiness.

**Intel HD 3000 class (DX10.1, 12 EUs, shared DDR3): maximize perceptual material
richness while minimizing texture bandwidth, dependent reads, divergence, and ALU.**

Hard budget: **one extra texture fetch max** per material feature. Anything needing
looped dependent reads is banned (POM/relief with shadows, SSAO, SSR, cascades,
MSAA, volumetrics, multi-BSDF stacking). This matches exactly what Unreal
BaseScalability-0, Unity URP Low, and Frostbite Low cut — the proven low-end set.

Frame gates at 720p: minimum 45 FPS (22.22 ms), target 60 FPS (16.67 ms).

## 4. Strata Architecture

```text
.stratamat
    ↓
Feature Mask (uint32 bitmask)
    ↓
Offline specialization (one compiled variant per mask value; dead lobes
compiled out, never branched at runtime)
    ↓
Astra surface shader
    ├─ Tier 1 core (nearly free)
    │   ├─ baked AO (universal fallback, packed in RMA.B)
    │   ├─ bent normal (OPTIONAL baked channel only)
    │   ├─ RNM detail normal blend (+1 packed fetch, distance-faded)
    │   ├─ wrapped diffuse + thickness tint (~3 ALU)
    │   ├─ Toksvig specular AA (baked mips, ~2 ALU)
    │   ├─ bump-offset parallax (1 reused fetch + 4 ALU; steep/POM banned)
    │   └─ screen-door transparency (1 compare + discard, keeps depth-write)
    │
    └─ Tier 2 optional (flag-gated, hero materials)
        ├─ clearcoat (single Blinn lobe, reuses base normal)
        ├─ sheen (Fresnel retro-reflection, ~5 ALU)
        ├─ anisotropy (Kajiya-Kay shifted tangents; needs tangent bake)
        └─ iridescence (64×1 hue LUT by NdotV)
```

### 4.1 `.stratamat` Container

```text
.stratamat
├── magic
├── format_version
├── header_size
├── payload_size
├── JSON header      (describes what the payload means)
└── binary payload   (textures / blobs)
```

Rules:
* JSON describes, binary contains. Never the reverse.
* Explicit colorspace intent per slot: `albedo → srgb`, `normal/metallic/
  roughness/ao → linear`, `emissive → srgb`.
* The runtime NEVER decides colorspace from the slot name. The importer decides
  once; `.stratamat` preserves the decision.
* Old formats (V1 BSAS, V2 loose JSON, `.mat`/`.mtl` sidecars) fail loudly.
  No silent conversion, no parallel system.

### 4.2 Feature Mask

```cpp
enum class EStrataFeature : uint32
{
    BakedAO        = 1 << 0,
    BentNormal     = 1 << 1,   // optional baked channel; AO is the fallback
    DetailNormal   = 1 << 2,
    WrappedDiffuse = 1 << 3,
    Toksvig        = 1 << 4,
    BumpOffset     = 1 << 5,
    Clearcoat      = 1 << 6,
    Sheen          = 1 << 7,
    Anisotropy     = 1 << 8,
    Iridescence    = 1 << 9,
    ScreenDoor     = 1 << 10
};
```

Placement: `Custom1.x` of the 112-byte `AstraSurface` uniform (16 free bytes,
zero layout change). Read once per draw — no per-pixel divergence.

### 4.3 Output Chain (StrataOutput)

```text
linear light → exposure → saturation-preserving compression → PBR Neutral
→ 16³ display-referred LUT (16 KB) → luma/chroma adjust
→ temporal blue-noise dither → sRGB OETF → LDR
```

Notes: LUT is display-referred (after encode), dither is absolutely last.
FP16 buffers, RGBM intermediates, and half-res HDR stay experimental paths —
the baseline is LDR-max. (Terminology: sRGB EOTF decodes input;
the output stage needs the OETF/encode.)

### 4.4 Sky-Captured IBL

Sky renders to a 64px cubemap only when sun/sky params change. Diffuse from
SH coefficients as uniforms (replaces the hardcoded hemisphere); specular from
a 2-mip blurred cubemap. Ground-bounce tint comes from the lower hemisphere —
biomes actually differ.

## 5. Reuse Inventory (what survived the amputation for Strata)

* Packed32 + UV varyings; correct sRGB/data texture split + cache + defaults
* GLTF texture extraction incl. RMA swizzle (emissive to be re-added)
* Sun + point/spot loop, hemisphere + sky-env specular, 4-tap PCF shadows
* SH/CPU probe bakers (unwired — Strata wires them as the IBL feed)
* ~60 free bytes in `AstraSurface` (Custom1, Custom0.yzw, UVScale/Offset,
  Shininess, Emissive block)
* Frame-time harness (`UIPerformanceMonitor`, avg/best-1%/worst-1%)

Still to build: tangent bake, detail/RNM sampling, wrap+thickness, Toksvig
mips, bump-offset, screen-door alpha, clearcoat/sheen/KK lobes, capsule AO,
GPU-time/draw/read/VRAM/screenshot benchmark gaps.

## 6. Benchmark Gates (hard)

Recorded per step, separately: GPU frame time, CPU frame time, material/shader
time, draw calls, texture reads, dependent texture reads, shader instruction
count, VRAM usage, 1% low FPS, screenshot. A technique ships only with numbers
(RNM +6 ALU for +0.2 ms with massive close-up gain = ship; anything else = cut).

Status: harness implemented (`Rendering/Strata/StrataBenchmark.cs`) — CPU frame
+ mesh/shadow/submit sections, mesh draws/tris/texture binds, texture bytes,
avg/best-1%/worst-1%, console + `Benchmarks/strata_bench.log` reports,
Metal blit-capture screenshots (F12) as dependency-free BMP, static shader
fetch-site census. Instruction counts remain manual (offline shader analysis).

## 7. Proof Sequence

1. Orange clay + correct lighting (done — Phase 1)
2. Grey dielectric + baked AO + detail normals + Neutral + LUT
3. Clearcoat + sheen + iridescence flags → showroom hero vehicle
4. 45–60 FPS locked on HD3000 @ 720p → environments → characters
   (skin LUT + Kajiya hair last — hardest, most ALU)

## 8. BlueSky Systems Registry

Use these names consistently in user-facing docs, editor labels, logs, and
benchmarks. A codename identifies a system; it does not claim that the system
is complete. Use subsystem names in code namespaces and source folders after a
full migration; keep ordinary implementation type names descriptive.

| Area | Canonical name | State |
|---|---|---|
| Rendering | **BSR** — BlueSky Rendering | Selected; the editor renderer type is now `BSRRenderer`. |
| Lighting | **Horizon** | The viewport consumes ECS point, spot, and directional lights through the Horizon shader path. Clustered culling and the contact-shadow/volumetric passes remain incomplete. |
| Networking | **LHO** — Let's Hop On | Selected for a rewrite; current networking implementation is legacy work, not the rewrite. |
| Materials | **Strata** | Active material and pack work. |
| CPU ray tracing | **Polaris** | Existing implementation; benchmark claims still need reproducible evidence. |
| Scripting | **TeaScript** | Existing language and interpreter. |
| AI | **Overthinking** | Existing AI subsystem. |
| Audio | **Echo** | Canonical name; the main system class and source file now use `Echo`. Playback backend remains incomplete. |
| Physics | **Airborne** | Canonical name; code namespace and source folder now use Airborne. Jolt remains the external physics implementation. |
| Animation | **Motif** | Canonical name; animation code now uses the `BlueSky.Motif` namespace and `Motif/` source folder. |
| Core runtime infrastructure | **Keystone** | Canonical umbrella for ECS, scenes, and platform services. |
| Terrain | **Terra** | Canonical name for terrain authoring and runtime. |
| Post-processing | **Afterglow** | Canonical name for screen-space and final-image effects. |
| Asset authoring/import | **Ease** | Existing Blender addon name; `.stratapack` is its transfer format, not a separate engine subsystem. |
| Particles | **Vectra** | Canonical future system name; prior particle source was removed. |
| Editor | **BlueSky Editor** | Keep descriptive; it is already clear and does not need another codename. |
| Standalone runtime | **BlueSky Runtime** | Keep descriptive; implementation is still a prototype. |

All names in this registry are canonical. A codename describes subsystem
identity, not completion. Do not label a subsystem complete based on its name
or folder alone. Some implementation APIs still carry their old technical
names; the migration audit must list those explicitly rather than imply a
deep rename is complete.

## 9. StrataPack + Ease (Content Pipeline Direction)

Blender-only ingestion via the **BlueSky Engine Ease** addon; zero handwritten
parsers. See `StrataPackPlan.md` for the full spec. Key locked decisions:

* `.stratamat` / `.stratapack` are **transfer formats, never runtime assets** —
  import converts to `.blueskyasset` (+ slot links); no sidecar files — skin,
  skeleton, and AO streams stay in the pack and are sliced from it at read
  time via pack-relative references (`sourcePack` + offsets).
* Open format by policy (magic + version + human-readable JSON header).
* **Phase-A lighting samples are part of the v1 pack contract:** Ease-baked AO
  + bent normals travel as explicit `aomap[]` / `bent[]` blob arrays (with
  declared encodings — the importer never guesses); vertex AO is a mesh
  attribute, never a disguised texture. AO feeds RMA.B; bent normals ride the
  `BentNormal` mask bit with plain AO as fallback.
* **Phase B:** Ease SH probes feed the same SH uniforms as sky capture —
  renderer agnostic to coefficient origin. **Phase C** (full lightmaps) parked.
* Single `.stratapack` binary per export; Packed32 mesh layout unchanged.

---

*Willpower works when aimed at benchmarks, not vibes. Every step above ends
with the suite green, zero warnings, and numbers — or it doesn't ship.*
