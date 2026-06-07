#!/bin/bash
# Compile GPU-Driven Culling Compute Shaders
# Requires DirectX Shader Compiler (dxc) or fxc via Wine

echo "============================================================================"
echo "Compiling GPU-Driven Culling Shaders"
echo "============================================================================"
echo ""

# Try to find shader compiler
if command -v dxc &> /dev/null; then
    COMPILER="dxc"
    echo "Using DirectX Shader Compiler (dxc)"
elif command -v fxc &> /dev/null; then
    COMPILER="fxc"
    echo "Using fxc.exe"
else
    echo "ERROR: No shader compiler found (dxc or fxc)"
    echo "Please install DirectX Shader Compiler"
    exit 1
fi

echo ""
echo "[1/3] Compiling Frustum Culling Shader (SM 5.0)..."
if [ "$COMPILER" = "dxc" ]; then
    dxc -T cs_6_0 -E main -Fo FrustumCulling.cso FrustumCulling.hlsl
else
    fxc /T cs_5_0 /E main /Fo FrustumCulling.cso FrustumCulling.hlsl
fi

if [ $? -ne 0 ]; then
    echo "ERROR: Failed to compile FrustumCulling.hlsl"
    exit 1
fi
echo "  - FrustumCulling.cso generated"

echo ""
echo "[2/3] Compiling Occlusion Culling Shader (SM 5.0)..."
if [ "$COMPILER" = "dxc" ]; then
    dxc -T cs_6_0 -E main -Fo OcclusionCulling.cso OcclusionCulling.hlsl
else
    fxc /T cs_5_0 /E main /Fo OcclusionCulling.cso OcclusionCulling.hlsl
fi

if [ $? -ne 0 ]; then
    echo "ERROR: Failed to compile OcclusionCulling.hlsl"
    exit 1
fi
echo "  - OcclusionCulling.cso generated"

echo ""
echo "[3/3] Compiling Compact Args Shader (SM 5.0)..."
if [ "$COMPILER" = "dxc" ]; then
    dxc -T cs_6_0 -E main -Fo CompactIndirectArgs.cso CompactIndirectArgs.hlsl
else
    fxc /T cs_5_0 /E main /Fo CompactIndirectArgs.cso CompactIndirectArgs.hlsl
fi

if [ $? -ne 0 ]; then
    echo "ERROR: Failed to compile CompactIndirectArgs.hlsl"
    exit 1
fi
echo "  - CompactIndirectArgs.cso generated"

echo ""
echo "============================================================================"
echo "Compilation Complete!"
echo "============================================================================"
echo "Generated files:"
echo "  - FrustumCulling.cso (Shader Model 5.0/6.0 for DX11/DX12)"
echo "  - OcclusionCulling.cso (Shader Model 5.0/6.0 for DX11/DX12)"
echo "  - CompactIndirectArgs.cso (Shader Model 5.0/6.0 for DX11/DX12)"
echo ""
echo "These shaders enable GPU-driven culling for Nanite-level performance."
echo "============================================================================"
