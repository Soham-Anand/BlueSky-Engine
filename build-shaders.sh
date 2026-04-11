#!/bin/bash

echo "=== Building Shaders ==="

# Metal shaders (macOS)
if [[ "$OSTYPE" == "darwin"* ]]; then
    echo "Building Metal shaders..."
    
    # UI shaders
    echo "  - Compiling simple_ui.metal..."
    mkdir -p BlueSkyEngine/Shaders
    xcrun -sdk macosx metal -c BlueSkyEngine/Editor/Shaders/simple_ui.metal -o BlueSkyEngine/Shaders/simple_ui.air
    xcrun -sdk macosx metallib BlueSkyEngine/Shaders/simple_ui.air -o BlueSkyEngine/Shaders/simple_ui.metallib
    rm BlueSkyEngine/Shaders/simple_ui.air
    
    # Viewport 3D shaders (sky + grid)
    echo "  - Compiling viewport_3d.metal..."
    xcrun -sdk macosx metal -c BlueSkyEngine/Editor/Shaders/viewport_3d.metal -o BlueSkyEngine/Shaders/viewport_3d.air
    xcrun -sdk macosx metallib BlueSkyEngine/Shaders/viewport_3d.air -o BlueSkyEngine/Shaders/viewport_3d.metallib
    rm BlueSkyEngine/Shaders/viewport_3d.air
    # Copy to Editor/Shaders for dev runs
    cp BlueSkyEngine/Shaders/viewport_3d.metallib BlueSkyEngine/Editor/Shaders/viewport_3d.metallib
    
    # PBR optimized shader (for old hardware)
    echo "  - Compiling pbr_optimized.metal..."
    xcrun -sdk macosx metal -c BlueSkyEngine/Editor/Shaders/pbr_optimized.metal -o BlueSkyEngine/Shaders/pbr_optimized.air
    xcrun -sdk macosx metallib BlueSkyEngine/Shaders/pbr_optimized.air -o BlueSkyEngine/Shaders/pbr_optimized.metallib
    rm BlueSkyEngine/Shaders/pbr_optimized.air
    # Copy to Editor/Shaders for dev runs
    cp BlueSkyEngine/Shaders/pbr_optimized.metallib BlueSkyEngine/Editor/Shaders/pbr_optimized.metallib
    
    # Horizon Lighting shader (feature-packed lighting system)
    echo "  - Compiling horizon_lighting.metal..."
    xcrun -sdk macosx metal -c BlueSkyEngine/Editor/Shaders/horizon_lighting.metal -o BlueSkyEngine/Shaders/horizon_lighting.air
    xcrun -sdk macosx metallib BlueSkyEngine/Shaders/horizon_lighting.air -o BlueSkyEngine/Shaders/horizon_lighting.metallib
    rm BlueSkyEngine/Shaders/horizon_lighting.air
    # Copy to Editor/Shaders for dev runs
    cp BlueSkyEngine/Shaders/horizon_lighting.metallib BlueSkyEngine/Editor/Shaders/horizon_lighting.metallib
    
    echo "✅ Metal shaders built"
fi

# HLSL shaders (Windows - requires fxc.exe from DirectX SDK)
if command -v fxc &> /dev/null; then
    echo "Building HLSL shaders..."
    
    # UI shaders
    echo "  - Compiling simple_ui.hlsl..."
    mkdir -p BlueSkyEngine/Shaders
    fxc /T vs_3_0 /E vs_ui /Fo BlueSkyEngine/Shaders/simple_ui.vs.cso BlueSkyEngine/Editor/Shaders/simple_ui.hlsl
    fxc /T ps_3_0 /E fs_ui /Fo BlueSkyEngine/Shaders/simple_ui.ps.cso BlueSkyEngine/Editor/Shaders/simple_ui.hlsl
    
    # Viewport 3D shaders
    echo "  - Compiling viewport_3d.hlsl..."
    fxc /T vs_3_0 /E vs_sky /Fo BlueSkyEngine/Shaders/viewport_sky.vs.cso BlueSkyEngine/Editor/Shaders/viewport_3d.hlsl
    fxc /T ps_3_0 /E fs_sky /Fo BlueSkyEngine/Shaders/viewport_sky.ps.cso BlueSkyEngine/Editor/Shaders/viewport_3d.hlsl
    
    fxc /T vs_3_0 /E vs_grid /Fo BlueSkyEngine/Shaders/viewport_grid.vs.cso BlueSkyEngine/Editor/Shaders/viewport_3d.hlsl
    fxc /T ps_3_0 /E fs_grid /Fo BlueSkyEngine/Shaders/viewport_grid.ps.cso BlueSkyEngine/Editor/Shaders/viewport_3d.hlsl
    
    fxc /T vs_3_0 /E vs_mesh /Fo BlueSkyEngine/Shaders/viewport_mesh.vs.cso BlueSkyEngine/Editor/Shaders/viewport_3d.hlsl
    fxc /T ps_3_0 /E fs_mesh /Fo BlueSkyEngine/Shaders/viewport_mesh.ps.cso BlueSkyEngine/Editor/Shaders/viewport_3d.hlsl
    fxc /T vs_3_0 /E vs_shadow /Fo BlueSkyEngine/Shaders/viewport_shadow.vs.cso BlueSkyEngine/Editor/Shaders/viewport_3d.hlsl
    fxc /T ps_3_0 /E fs_shadow /Fo BlueSkyEngine/Shaders/viewport_shadow.ps.cso BlueSkyEngine/Editor/Shaders/viewport_3d.hlsl
    
    # Copy to Editor/Shaders for dev runs
    cp BlueSkyEngine/Shaders/*.cso BlueSkyEngine/Editor/Shaders/
    
    echo "✅ HLSL shaders built"
else
    echo "⚠️  fxc not found - skipping HLSL shader compilation"
    echo "   Install DirectX SDK or Windows SDK to compile HLSL shaders"
fi

echo ""
echo "=== Shader Build Complete ==="
