@echo off
REM ============================================================================
REM BlueSky Engine — DirectX HLSL Shader Compilation Script
REM ============================================================================
REM Compiles all viewport shaders from viewport_3d.hlsl and simple_ui.hlsl
REM into .cso (Compiled Shader Object) files for the DX11 RHI backend.
REM
REM NAMING CONVENTION: {entryPoint}.cso
REM   This matches ViewportRenderer.GetCSOFileName() exactly.
REM
REM REQUIREMENTS: fxc.exe from the Windows SDK
REM   Install from: https://developer.microsoft.com/en-us/windows/downloads/windows-sdk/
REM ============================================================================

echo.
echo ╔══════════════════════════════════════════════════════════════╗
echo ║     BlueSky Engine — HLSL Shader Compilation (SM 4.0)       ║
echo ╚══════════════════════════════════════════════════════════════╝
echo.

REM Check if fxc.exe is available
where fxc.exe >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] fxc.exe not found in PATH.
    echo.
    echo Try one of these:
    echo   1. Open a "Developer Command Prompt for VS" ^(has fxc in PATH^)
    echo   2. Add Windows SDK bin to PATH, e.g.:
    echo      set PATH=%%PATH%%;C:\Program Files ^(x86^)\Windows Kits\10\bin\10.0.22621.0\x64
    echo   3. Install Windows SDK from:
    echo      https://developer.microsoft.com/en-us/windows/downloads/windows-sdk/
    echo.
    exit /b 1
)

set HLSL_VIEWPORT=viewport_3d.hlsl
set HLSL_UI=simple_ui.hlsl
set SM_VS=vs_4_0
set SM_PS=ps_4_0
set ERRORS=0

REM ============================================================================
REM Viewport Shaders (viewport_3d.hlsl)
REM ============================================================================

echo [1/7] Compiling Sky shaders...
fxc.exe /nologo /T %SM_VS% /E vs_sky /Fo vs_sky.cso %HLSL_VIEWPORT%
if %ERRORLEVEL% NEQ 0 set /a ERRORS+=1
fxc.exe /nologo /T %SM_PS% /E fs_sky /Fo fs_sky.cso %HLSL_VIEWPORT%
if %ERRORLEVEL% NEQ 0 set /a ERRORS+=1

echo [2/7] Compiling Grid shaders...
fxc.exe /nologo /T %SM_VS% /E vs_grid /Fo vs_grid.cso %HLSL_VIEWPORT%
if %ERRORLEVEL% NEQ 0 set /a ERRORS+=1
fxc.exe /nologo /T %SM_PS% /E fs_grid /Fo fs_grid.cso %HLSL_VIEWPORT%
if %ERRORLEVEL% NEQ 0 set /a ERRORS+=1

echo [3/7] Compiling Mesh shaders...
fxc.exe /nologo /T %SM_VS% /E vs_mesh /Fo vs_mesh.cso %HLSL_VIEWPORT%
if %ERRORLEVEL% NEQ 0 set /a ERRORS+=1
fxc.exe /nologo /T %SM_PS% /E fs_mesh /Fo fs_mesh.cso %HLSL_VIEWPORT%
if %ERRORLEVEL% NEQ 0 set /a ERRORS+=1

echo [4/7] Compiling Shadow shaders...
fxc.exe /nologo /T %SM_VS% /E vs_shadow /Fo vs_shadow.cso %HLSL_VIEWPORT%
if %ERRORLEVEL% NEQ 0 set /a ERRORS+=1
fxc.exe /nologo /T %SM_PS% /E fs_shadow /Fo fs_shadow.cso %HLSL_VIEWPORT%
if %ERRORLEVEL% NEQ 0 set /a ERRORS+=1

echo [5/7] Compiling Gizmo shaders...
fxc.exe /nologo /T %SM_VS% /E vs_gizmo /Fo vs_gizmo.cso %HLSL_VIEWPORT%
if %ERRORLEVEL% NEQ 0 set /a ERRORS+=1
fxc.exe /nologo /T %SM_PS% /E fs_gizmo /Fo fs_gizmo.cso %HLSL_VIEWPORT%
if %ERRORLEVEL% NEQ 0 set /a ERRORS+=1

echo [6/7] Compiling Wireframe shader...
fxc.exe /nologo /T %SM_PS% /E fs_wireframe /Fo fs_wireframe.cso %HLSL_VIEWPORT%
if %ERRORLEVEL% NEQ 0 set /a ERRORS+=1

REM ============================================================================
REM UI Shaders (simple_ui.hlsl)
REM ============================================================================

echo [7/7] Compiling UI shaders...
fxc.exe /nologo /T %SM_VS% /E vs_ui /Fo vs_ui.cso %HLSL_UI%
if %ERRORLEVEL% NEQ 0 set /a ERRORS+=1
fxc.exe /nologo /T %SM_PS% /E fs_ui /Fo fs_ui.cso %HLSL_UI%
if %ERRORLEVEL% NEQ 0 set /a ERRORS+=1

REM ============================================================================
REM Summary
REM ============================================================================
echo.
echo ============================================================================
if %ERRORS% EQU 0 (
    echo   [OK] All shaders compiled successfully!
) else (
    echo   [WARNING] %ERRORS% shader(s) failed to compile.
)
echo ============================================================================
echo.
echo Generated .cso files:
echo   vs_sky.cso, fs_sky.cso           — Procedural sky
echo   vs_grid.cso, fs_grid.cso         — Infinite grid
echo   vs_mesh.cso, fs_mesh.cso         — PBR mesh rendering
echo   vs_shadow.cso, fs_shadow.cso     — Shadow map pass
echo   vs_gizmo.cso, fs_gizmo.cso       — Editor gizmos
echo   fs_wireframe.cso                 — Wireframe overlay
echo   vs_ui.cso, fs_ui.cso             — UI rendering
echo.
echo Place these in: BlueSkyEngine/Editor/Shaders/
echo ============================================================================
