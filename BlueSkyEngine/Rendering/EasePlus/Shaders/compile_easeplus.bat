@echo off
setlocal

rem Find fxc.exe in typical Windows SDK locations
set "FXC="
for /d %%d in ("%ProgramFiles(x86)%\Windows Kits\10\bin\*") do (
    if exist "%%d\x64\fxc.exe" (
        set "FXC=%%d\x64\fxc.exe"
    )
)

if "%FXC%"=="" (
    echo [Ease+ Shader Compiler] Warning: fxc.exe not found!
    echo Please run this from a Developer Command Prompt for VS.
    set "FXC=fxc.exe"
)

echo [Ease+ Shader Compiler] Using %FXC%

rem Pre-Pass
"%FXC%" /T vs_4_0 /E easeplus_vs_prepass /Fo easeplus_vs_prepass.cso easeplus_prepass.hlsl
"%FXC%" /T ps_4_0 /E easeplus_fs_prepass /Fo easeplus_fs_prepass.cso easeplus_prepass.hlsl

rem Lighting
"%FXC%" /T vs_4_0 /E easeplus_vs_fullscreen /Fo easeplus_vs_fullscreen.cso easeplus_lighting.hlsl
"%FXC%" /T ps_4_0 /E easeplus_fs_lighting /Fo easeplus_fs_lighting.cso easeplus_lighting.hlsl

rem Material
"%FXC%" /T vs_4_0 /E easeplus_vs_material /Fo easeplus_vs_material.cso easeplus_material.hlsl
"%FXC%" /T ps_4_0 /E easeplus_fs_material /Fo easeplus_fs_material.cso easeplus_material.hlsl

rem PostFX
"%FXC%" /T vs_4_0 /E easeplus_vs_postfx /Fo easeplus_vs_postfx.cso easeplus_postfx.hlsl
"%FXC%" /T ps_4_0 /E easeplus_fs_postfx /Fo easeplus_fs_postfx.cso easeplus_postfx.hlsl

echo [Ease+ Shader Compiler] Done compiling HLSL for DX11!

rem If on macOS with Xcode tools, compile Metal shaders too
xcrun -f metal >nul 2>&1
if %errorlevel% equ 0 (
    echo [Ease+ Shader Compiler] Compiling Metal shaders for macOS...
    xcrun -sdk macosx metal -c easeplus_prepass.metal -o easeplus_prepass.air
    xcrun -sdk macosx metallib easeplus_prepass.air -o easeplus_prepass.metallib
    
    xcrun -sdk macosx metal -c easeplus_lighting.metal -o easeplus_lighting.air
    xcrun -sdk macosx metallib easeplus_lighting.air -o easeplus_lighting.metallib
    
    xcrun -sdk macosx metal -c easeplus_material.metal -o easeplus_material.air
    xcrun -sdk macosx metallib easeplus_material.air -o easeplus_material.metallib
    
    xcrun -sdk macosx metal -c easeplus_postfx.metal -o easeplus_postfx.air
    xcrun -sdk macosx metallib easeplus_postfx.air -o easeplus_postfx.metallib
    
    del *.air
    echo [Ease+ Shader Compiler] Done compiling Metal shaders!
)

endlocal
