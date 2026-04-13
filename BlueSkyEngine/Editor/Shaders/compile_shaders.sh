#!/bin/bash
# ============================================================================
# DirectX Shader Compilation Script (Cross-platform)
# Compiles HLSL shaders for multiple shader models (SM 2.0, 3.0, 4.0)
# ============================================================================

echo "Compiling DirectX shaders..."

# Check if running on Windows (via WSL or native)
if command -v fxc.exe &> /dev/null; then
    FXC="fxc.exe"
elif command -v wine &> /dev/null && [ -f "/usr/bin/fxc" ]; then
    FXC="wine fxc"
else
    echo "ERROR: fxc.exe not found."
    echo "On Windows: Install Windows SDK"
    echo "On Linux/Mac: Install Wine and Windows SDK, or compile on Windows"
    exit 1
fi

# ============================================================================
# Shader Model 2.0 (DirectX 9 - 2004+ hardware)
# ============================================================================
echo ""
echo "[1/3] Compiling Shader Model 2.0 (DX9 baseline)..."

$FXC /T vs_2_0 /E vs_sky /Fo viewport_3d_vs_sm20.cso viewport_3d.hlsl
$FXC /T ps_2_0 /E fs_sky /Fo viewport_3d_sky_ps_sm20.cso viewport_3d.hlsl

$FXC /T vs_2_0 /E vs_grid /Fo viewport_3d_grid_vs_sm20.cso viewport_3d.hlsl
$FXC /T ps_2_0 /E fs_grid /Fo viewport_3d_grid_ps_sm20.cso viewport_3d.hlsl

$FXC /T vs_2_0 /E vs_mesh /Fo viewport_3d_mesh_vs_sm20.cso viewport_3d.hlsl
$FXC /T ps_2_0 /E fs_mesh /Fo viewport_3d_mesh_ps_sm20.cso viewport_3d.hlsl

$FXC /T vs_2_0 /E vs_shadow /Fo viewport_3d_shadow_vs_sm20.cso viewport_3d.hlsl
$FXC /T ps_2_0 /E fs_shadow /Fo viewport_3d_shadow_ps_sm20.cso viewport_3d.hlsl

$FXC /T vs_2_0 /E vs_ui /Fo simple_ui_vs_sm20.cso simple_ui.hlsl
$FXC /T ps_2_0 /E fs_ui /Fo simple_ui_ps_sm20.cso simple_ui.hlsl

# ============================================================================
# Shader Model 3.0 (DirectX 9c - 2006+ hardware)
# ============================================================================
echo ""
echo "[2/3] Compiling Shader Model 3.0 (DX9c enhanced)..."

$FXC /T vs_3_0 /E vs_sky /Fo viewport_3d_vs_sm30.cso viewport_3d.hlsl
$FXC /T ps_3_0 /E fs_sky /Fo viewport_3d_sky_ps_sm30.cso viewport_3d.hlsl

$FXC /T vs_3_0 /E vs_grid /Fo viewport_3d_grid_vs_sm30.cso viewport_3d.hlsl
$FXC /T ps_3_0 /E fs_grid /Fo viewport_3d_grid_ps_sm30.cso viewport_3d.hlsl

$FXC /T vs_3_0 /E vs_mesh /Fo viewport_3d_mesh_vs_sm30.cso viewport_3d.hlsl
$FXC /T ps_3_0 /E fs_mesh /Fo viewport_3d_mesh_ps_sm30.cso viewport_3d.hlsl

$FXC /T vs_3_0 /E vs_shadow /Fo viewport_3d_shadow_vs_sm30.cso viewport_3d.hlsl
$FXC /T ps_3_0 /E fs_shadow /Fo viewport_3d_shadow_ps_sm30.cso viewport_3d.hlsl

$FXC /T vs_3_0 /E vs_ui /Fo simple_ui_vs_sm30.cso simple_ui.hlsl
$FXC /T ps_3_0 /E fs_ui /Fo simple_ui_ps_sm30.cso simple_ui.hlsl

# ============================================================================
# Shader Model 4.0 (DirectX 10 - 2007+ hardware)
# ============================================================================
echo ""
echo "[3/3] Compiling Shader Model 4.0 (DX10)..."

$FXC /T vs_4_0 /E vs_sky /Fo viewport_3d_vs_sm40.cso viewport_3d.hlsl
$FXC /T ps_4_0 /E fs_sky /Fo viewport_3d_sky_ps_sm40.cso viewport_3d.hlsl

$FXC /T vs_4_0 /E vs_grid /Fo viewport_3d_grid_vs_sm40.cso viewport_3d.hlsl
$FXC /T ps_4_0 /E fs_grid /Fo viewport_3d_grid_ps_sm40.cso viewport_3d.hlsl

$FXC /T vs_4_0 /E vs_mesh /Fo viewport_3d_mesh_vs_sm40.cso viewport_3d.hlsl
$FXC /T ps_4_0 /E fs_mesh /Fo viewport_3d_mesh_ps_sm40.cso viewport_3d.hlsl

$FXC /T vs_4_0 /E vs_shadow /Fo viewport_3d_shadow_vs_sm40.cso viewport_3d.hlsl
$FXC /T ps_4_0 /E fs_shadow /Fo viewport_3d_shadow_ps_sm40.cso viewport_3d.hlsl

$FXC /T vs_4_0 /E vs_ui /Fo simple_ui_vs_sm40.cso simple_ui.hlsl
$FXC /T ps_4_0 /E fs_ui /Fo simple_ui_ps_sm40.cso simple_ui.hlsl

echo ""
echo "============================================================================"
echo "Shader compilation complete!"
echo "============================================================================"
echo "Generated files:"
echo "  - *_sm20.cso (Shader Model 2.0 for DX9)"
echo "  - *_sm30.cso (Shader Model 3.0 for DX9c)"
echo "  - *_sm40.cso (Shader Model 4.0 for DX10)"
echo "============================================================================"
