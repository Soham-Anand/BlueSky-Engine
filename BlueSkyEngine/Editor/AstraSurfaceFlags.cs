using System;

namespace BlueSky.Editor;

/// <summary>
/// Single source of truth for Astra surface flags.
/// MUST mirror the #defines in viewport_3d.hlsl (ASTRA_TRANSPARENT, etc.).
/// </summary>
[Flags]
public enum AstraSurfaceFlags : uint
{
    Transparent    = 1u << 0,
    DoubleSided    = 1u << 1,
    ReceiveShadow  = 1u << 2,
    CastShadow     = 1u << 3,
    AlphaClip      = 1u << 4,
    Emissive       = 1u << 5,
    ClearCoat      = 1u << 6,
    VertexColor    = 1u << 7,
    Reflection     = 1u << 8,
    CustomLighting = 1u << 9,
    Wind           = 1u << 10,
    Decal          = 1u << 11,
    Terrain        = 1u << 12,
    UI             = 1u << 13,
    // Bits 14-16 form a 3-bit debug-view selector (transient, set per-draw by
    // the viewport when a debug view is active; never persisted in assets):
    // 0 = Lit, 1 = Normals, 2 = Unlit albedo, 3 = Shadow factor, 4 = AO factor,
    // 5 = sun-driven direct only, 6 = ambient/IBL only, 7 = sun NdotL.
    Debug          = 1u << 14,
    Reserved       = 1u << 15,
}
