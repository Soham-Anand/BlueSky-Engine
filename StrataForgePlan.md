# BlueSky Forge Plan — Strata as the runtime + Forge asset hospital

STATUS: PLAN ONLY. NOT STARTED. NOTHING BELOW HAS BEEN EXECUTED.
Decided: 2026-09-26. Awaiting explicit "go" (Phase 1 first).

## North star
Strata stops being "the editor's material panel" and becomes THE material
runtime both the Engine and the game Runtime own. BlueSky Forge (C++,
maximum performance) becomes the asset hospital: open a `.stratapack`,
SEE the mesh with its material mapping, fix what's wrong, repack — and
the engine trusts what Forge writes.

## Locked decisions
- Forge language: C++ (CMake + Dear ImGui; Metal backend on macOS,
  DX11 on Windows, mirroring the RHI split).
- Forge shape: GUI browser with a real 3D mapping viewer.
- Strata ownership: NEW shared `BlueSky.Strata` C# class library
  referenced by both Engine and Runtime (decided by agent, user may veto).
- Vectra: untouched placeholder. No Vectra authoring in Forge v1.

## User's definition of "fixing" (drives Phase 3 scope)
Visualizing the meshes and checking material mapping:
which slot covers which triangles, right textures in right roles,
UV sanity, missing/orphan slot detection.

## Phase 1 — Strata becomes runtime-owned (C#)
Move into `BlueSky.Strata`: `StrataMaterial`, `StrataCodec`, `StrataPack`,
`StrataImporter`, `StrataSurfaceUpload` (BuildParams only — F9 demo stays
editor-side), `StrataSkyCapture`, `StrataLut`, `StratapackImportHandler`,
`StrataImportWriter`. Engine + Runtime reference it; kill the Exe-to-Exe
reference. `ViewportRenderer`, Strata panel, orange-clay fallback stay.
DONE WHEN: 0 warnings, suite green, headless GTRR32 import byte-identical.

## Phase 2 — Freeze the contract (C#, tiny, load-bearing)
Byte-level conformance fixtures: sample pack, `ease_live_cube`,
`.stratamat` set covering clearcoat + alpha + legacy-missing-key.
This is the law C++ must obey. Prevents format drift (#1 two-language risk).

## Phase 3 — Forge + mapping viewer (C++, the heart of "fixing")
- V3a mapping-debug views: slot→submesh coverage in flat saturated
  slot colors; per-slot inspector (texture role thumbs, UV layer +
  transform provenance, scalars); missing-link / orphan-slot detection
  with loud rows; UV checker overlay.
- V3b lit view: lightweight C++ PBR (albedo + normal + RMA, one sun +
  ambient). Deliberately APPROXIMATE — pixel truth stays in the engine
  (F8/F9 screenshot loop). Forge answers "is the mapping right".
- Repack: edit scalars, reassign slots, validate (port of the 12
  validations), write pack.
- GATE: Forge output byte-comparable to C# output on all Phase-2 fixtures.

## Phase 4 — Performance core (C++, the reason it's C++)
Texture crunch (mips, BCn via basisu), mesh optimization (meshoptimizer:
vertex cache, overdraw, quantization), batch CLI mode for headless farms.

## Phase 5 — Wire-up
Forge-written packs import into Engine + Runtime headless-green.
User screenshot is the final proof.

## Open details (flagged, NOT assumed)
- Exact Packed32 32-byte vertex layout mirrored in C++ (positions /
  normals / UVs confirmed present; byte map verified at implementation
  time against the C# reader).
- Viewer shading approximate BY DESIGN — no parity chasing.
- V1 non-goals: Vectra authoring, format v2, auto-fix without approval
  (Forge suggests, user approves).

## Execution order
Phase 1 first on "go". Each phase ends green or work stops.
