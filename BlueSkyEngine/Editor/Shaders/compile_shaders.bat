@echo off
REM ============================================================================
REM DirectX Shader Compilation Script
REM Compiles HLSL shaders for multiple shader models (SM 2.0, 3.0, 4.0)
REM ============================================================================

echo Compiling DirectX shaders...

REM Check if fxc.exe is available
where fxc.exe >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: fxc.exe not found. Please install Windows SDK.
    echo Download from: https://developer.microsoft.com/en-us/windows/downloads/windows-sdk/
    exit /b 1
)

REM ============================================================================
REM Shader Model 2.0 (DirectX 9 - 2004+ hardware)
REM ============================================================================
echo.
echo [1/3] Compiling Shader Model 2.0 (DX9 baseline)...

fxc.exe /T vs_2_0 /E vs_sky /Fo viewport_3d_vs_sm20.cso viewport_3d.hlsl
fxc.exe /T ps_2_0 /E fs_sky /Fo viewport_3d_sky_ps_sm20.cso viewport_3d.hlsl

fxc.exe /T vs_2_0 /E vs_grid /Fo viewport_3d_grid_vs_sm20.cso viewport_3d.hlsl
fxc.exe /T ps_2_0 /E fs_grid /Fo viewport_3d_grid_ps_sm20.cso viewport_3d.hlsl

fxc.exe /T vs_2_0 /E vs_mesh /Fo viewport_3d_mesh_vs_sm20.cso viewport_3d.hlsl
fxc.exe /T ps_2_0 /E fs_mesh /Fo viewport_3d_mesh_ps_sm20.cso viewport_3d.hlsl

fxc.exe /T vs_2_0 /E vs_shadow /Fo viewport_3d_shadow_vs_sm20.cso viewport_3d.hlsl
fxc.exe /T ps_2_0 /E fs_shadow /Fo viewport_3d_shadow_ps_sm20.cso viewport_3d.hlsl

fxc.exe /T vs_2_0 /E vs_ui /Fo simple_ui_vs_sm20.cso simple_ui.hlsl
fxc.exe /T ps_2_0 /E fs_ui /Fo simple_ui_ps_sm20.cso simple_ui.hlsl

REM ============================================================================
REM Shader Model 3.0 (DirectX 9c - 2006+ hardware)
REM ============================================================================
echo.
echo [2/3] Compiling Shader Model 3.0 (DX9c enhanced)...

fxc.exe /T vs_3_0 /E vs_sky /Fo viewport_3d_vs_sm30.cso viewport_3d.hlsl
fxc.exe /T ps_3_0 /E fs_sky /Fo viewport_3d_sky_ps_sm30.cso viewport_3d.hlsl

fxc.exe /T vs_3_0 /E vs_grid /Fo viewport_3d_grid_vs_sm30.cso viewport_3d.hlsl
fxc.exe /T ps_3_0 /E fs_grid /Fo viewport_3d_grid_ps_sm30.cso viewport_3d.hlsl

fxc.exe /T vs_3_0 /E vs_mesh /Fo viewport_3d_mesh_vs_sm30.cso viewport_3d.hlsl
fxc.exe /T ps_3_0 /E fs_mesh /Fo viewport_3d_mesh_ps_sm30.cso viewport_3d.hlsl

fxc.exe /T vs_3_0 /E vs_shadow /Fo viewport_3d_shadow_vs_sm30.cso viewport_3d.hlsl
fxc.exe /T ps_3_0 /E fs_shadow /Fo viewport_3d_shadow_ps_sm30.cso viewport_3d.hlsl

fxc.exe /T vs_3_0 /E vs_ui /Fo simple_ui_vs_sm30.cso simple_ui.hlsl
fxc.exe /T ps_3_0 /E fs_ui /Fo simple_ui_ps_sm30.cso simple_ui.hlsl

REM ============================================================================
REM Shader Model 4.0 (DirectX 10 - 2007+ hardware)
REM ============================================================================
echo.
echo [3/3] Compiling Shader Model 4.0 (DX10)...

fxc.exe /T vs_4_0 /E vs_sky /Fo viewport_3d_vs_sm40.cso viewport_3d.hlsl
fxc.exe /T ps_4_0 /E fs_sky /Fo viewport_3d_sky_ps_sm40.cso viewport_3d.hlsl

fxc.exe /T vs_4_0 /E vs_grid /Fo viewport_3d_grid_vs_sm40.cso viewport_3d.hlsl
fxc.exe /T ps_4_0 /E fs_grid /Fo viewport_3d_grid_ps_sm40.cso viewport_3d.hlsl

fxc.exe /T vs_4_0 /E vs_mesh /Fo viewport_3d_mesh_vs_sm40.cso viewport_3d.hlsl
fxc.exe /T ps_4_0 /E fs_mesh /Fo viewport_3d_mesh_ps_sm40.cso viewport_3d.hlsl

fxc.exe /T vs_4_0 /E vs_shadow /Fo viewport_3d_shadow_vs_sm40.cso viewport_3d.hlsl
fxc.exe /T ps_4_0 /E fs_shadow /Fo viewport_3d_shadow_ps_sm40.cso viewport_3d.hlsl

fxc.exe /T vs_4_0 /E vs_ui /Fo simple_ui_vs_sm40.cso simple_ui.hlsl
fxc.exe /T ps_4_0 /E fs_ui /Fo simple_ui_ps_sm40.cso simple_ui.hlsl

echo.
echo ============================================================================
echo Shader compilation complete!
echo ============================================================================
echo Generated files:
echo   - *_sm20.cso (Shader Model 2.0 for DX9)
echo   - *_sm30.cso (Shader Model 3.0 for DX9c)
echo   - *_sm40.cso (Shader Model 4.0 for DX10)
echo ============================================================================
