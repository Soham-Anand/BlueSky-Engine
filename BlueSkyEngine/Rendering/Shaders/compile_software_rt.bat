@echo off
REM BlueSkyEngine - Software Ray Tracing Shader Compilation (Windows)
REM Compiles compute shaders for SM 5.0 (DX11 FL 11.0+)

echo ================================================================================
echo Compiling Software Ray Tracing Shaders (Shader Model 5.0)
echo ================================================================================
echo.

set DXC=dxc.exe
set FXC=fxc.exe
set SHADER_MODEL=cs_5_0
set OUTPUT_DIR=.

REM Check if DXC is available (modern compiler)
where %DXC% >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo Using DXC ^(DirectX Shader Compiler^)
    set COMPILER=%DXC%
    set COMPILER_FLAGS=-T %SHADER_MODEL% -O3 -WX -Zpr
) else (
    REM Fallback to FXC (legacy compiler)
    where %FXC% >nul 2>&1
    if %ERRORLEVEL% EQU 0 (
        echo Using FXC ^(Legacy Shader Compiler^)
        set COMPILER=%FXC%
        set COMPILER_FLAGS=/T %SHADER_MODEL% /O3 /WX /Zpr
    ) else (
        echo ERROR: No shader compiler found!
        echo Please install Windows SDK or DirectX SDK
        exit /b 1
    )
)

echo.
echo [1/4] Compiling SoftwareRT_RayGen.hlsl...
%COMPILER% %COMPILER_FLAGS% /E main /Fo SoftwareRT_RayGen_cs_sm50.cso SoftwareRT_RayGen.hlsl
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Failed to compile SoftwareRT_RayGen.hlsl
    exit /b 1
)
echo       Output: SoftwareRT_RayGen_cs_sm50.cso

echo.
echo [2/4] Compiling SoftwareRT_Intersection.hlsl...
%COMPILER% %COMPILER_FLAGS% /E main /Fo SoftwareRT_Intersection_cs_sm50.cso SoftwareRT_Intersection.hlsl
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Failed to compile SoftwareRT_Intersection.hlsl
    exit /b 1
)
echo       Output: SoftwareRT_Intersection_cs_sm50.cso

echo.
echo [3/4] Compiling SoftwareRT_Shading.hlsl...
%COMPILER% %COMPILER_FLAGS% /E main /Fo SoftwareRT_Shading_cs_sm50.cso SoftwareRT_Shading.hlsl
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Failed to compile SoftwareRT_Shading.hlsl
    exit /b 1
)
echo       Output: SoftwareRT_Shading_cs_sm50.cso

echo.
echo [4/4] Compiling SoftwareRT_Denoise.hlsl...
%COMPILER% %COMPILER_FLAGS% /E main /Fo SoftwareRT_Denoise_cs_sm50.cso SoftwareRT_Denoise.hlsl
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Failed to compile SoftwareRT_Denoise.hlsl
    exit /b 1
)
echo       Output: SoftwareRT_Denoise_cs_sm50.cso

echo.
echo ================================================================================
echo Compilation Complete!
echo ================================================================================
echo All shaders compiled successfully for Shader Model 5.0
echo.
echo Hardware Requirements:
echo   - Intel HD Graphics 4000+ ^(Ivy Bridge 2012+^)
echo   - GeForce GTX 400+ ^(Fermi 2010+^)
echo   - Radeon HD 5000+ ^(Evergreen 2009+^)
echo.
echo Fallback: Systems without SM 5.0 will use CPU culling
echo ================================================================================
