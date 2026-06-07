@echo off
REM Compile GPU-Driven Culling Compute Shaders
REM Generates multiple shader model variants for hardware compatibility
REM Requires DirectX Shader Compiler (fxc.exe) in PATH

echo ============================================================================
echo Compiling GPU-Driven Culling Shaders (Multi-Target)
echo ============================================================================
echo.

REM Check if fxc.exe is available
where fxc.exe >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: fxc.exe not found in PATH
    echo Please install Windows SDK or add fxc.exe to PATH
    pause
    exit /b 1
)

echo IMPORTANT: Shader Model Compatibility
echo ======================================
echo SM 4.0: Intel HD (Sandy Bridge i5-2410M), GeForce 8/9, Radeon HD 2000/3000
echo SM 5.0: Intel HD 4000+, GeForce GTX 400+, Radeon HD 5000+
echo.
echo Compute shaders require SM 5.0 (DX11 FL 11.0+)
echo Older hardware will use CPU culling fallback
echo.

echo [1/1] Compiling Frustum Culling Shader (SM 5.0 only)...
fxc.exe /T cs_5_0 /E main /Fo FrustumCulling_cs_sm50.cso FrustumCulling.hlsl
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Failed to compile FrustumCulling.hlsl
    pause
    exit /b 1
)
echo   - FrustumCulling_cs_sm50.cso generated

echo.
echo [2/3] Compiling Occlusion Culling Shader (SM 5.0 only)...
fxc.exe /T cs_5_0 /E main /Fo OcclusionCulling_cs_sm50.cso OcclusionCulling.hlsl
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Failed to compile OcclusionCulling.hlsl
    pause
    exit /b 1
)
echo   - OcclusionCulling_cs_sm50.cso generated

echo.
echo [3/3] Compiling Compact Args Shader (SM 5.0 only)...
fxc.exe /T cs_5_0 /E main /Fo CompactIndirectArgs_cs_sm50.cso CompactIndirectArgs.hlsl
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Failed to compile CompactIndirectArgs.hlsl
    pause
    exit /b 1
)
echo   - CompactIndirectArgs_cs_sm50.cso generated

echo.
echo ============================================================================
echo Compilation Complete!
echo ============================================================================
echo Generated files:
echo   - FrustumCulling_cs_sm50.cso (Shader Model 5.0 for DX11 FL 11.0+)
echo   - OcclusionCulling_cs_sm50.cso (Shader Model 5.0 for DX11 FL 11.0+)
echo   - CompactIndirectArgs_cs_sm50.cso (Shader Model 5.0 for DX11 FL 11.0+)
echo.
echo Hardware Compatibility:
echo   SM 5.0: Intel HD 4000+ (2012+), GTX 400+ (2010+), Radeon HD 5000+ (2009+)
echo   SM 4.0: Falls back to CPU culling (SmartCullingSystem)
echo.
echo These shaders enable GPU-driven culling for Nanite-level performance.
echo Older hardware (i5-2410M, etc.) will use optimized CPU culling.
echo ============================================================================
pause
