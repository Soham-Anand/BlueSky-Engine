#!/bin/bash
# BlueSkyEngine - Software Ray Tracing Shader Compilation (Linux/macOS)
# Compiles compute shaders for SM 5.0 (DX11 FL 11.0+)

echo "================================================================================"
echo "Compiling Software Ray Tracing Shaders (Shader Model 5.0)"
echo "================================================================================"
echo ""

SHADER_MODEL="cs_5_0"
OUTPUT_DIR="."

# Check for DXC (DirectX Shader Compiler)
if command -v dxc &> /dev/null; then
    echo "Using DXC (DirectX Shader Compiler)"
    COMPILER="dxc"
    COMPILER_FLAGS="-T $SHADER_MODEL -O3 -WX -Zpr"
    USE_DXC=1
elif command -v fxc &> /dev/null; then
    echo "Using FXC (Legacy Shader Compiler)"
    COMPILER="fxc"
    COMPILER_FLAGS="/T $SHADER_MODEL /O3 /WX /Zpr"
    USE_DXC=0
else
    echo "ERROR: No shader compiler found!"
    echo "Please install DirectX Shader Compiler (DXC)"
    echo ""
    echo "Installation:"
    echo "  Ubuntu/Debian: sudo apt install dxc"
    echo "  macOS: brew install dxc"
    echo "  Or download from: https://github.com/microsoft/DirectXShaderCompiler"
    exit 1
fi

echo ""
echo "[1/4] Compiling SoftwareRT_RayGen.hlsl..."
if [ $USE_DXC -eq 1 ]; then
    $COMPILER $COMPILER_FLAGS -E main -Fo SoftwareRT_RayGen_cs_sm50.cso SoftwareRT_RayGen.hlsl
else
    $COMPILER $COMPILER_FLAGS /E main /Fo SoftwareRT_RayGen_cs_sm50.cso SoftwareRT_RayGen.hlsl
fi
if [ $? -ne 0 ]; then
    echo "ERROR: Failed to compile SoftwareRT_RayGen.hlsl"
    exit 1
fi
echo "      Output: SoftwareRT_RayGen_cs_sm50.cso"

echo ""
echo "[2/4] Compiling SoftwareRT_Intersection.hlsl..."
if [ $USE_DXC -eq 1 ]; then
    $COMPILER $COMPILER_FLAGS -E main -Fo SoftwareRT_Intersection_cs_sm50.cso SoftwareRT_Intersection.hlsl
else
    $COMPILER $COMPILER_FLAGS /E main /Fo SoftwareRT_Intersection_cs_sm50.cso SoftwareRT_Intersection.hlsl
fi
if [ $? -ne 0 ]; then
    echo "ERROR: Failed to compile SoftwareRT_Intersection.hlsl"
    exit 1
fi
echo "      Output: SoftwareRT_Intersection_cs_sm50.cso"

echo ""
echo "[3/4] Compiling SoftwareRT_Shading.hlsl..."
if [ $USE_DXC -eq 1 ]; then
    $COMPILER $COMPILER_FLAGS -E main -Fo SoftwareRT_Shading_cs_sm50.cso SoftwareRT_Shading.hlsl
else
    $COMPILER $COMPILER_FLAGS /E main /Fo SoftwareRT_Shading_cs_sm50.cso SoftwareRT_Shading.hlsl
fi
if [ $? -ne 0 ]; then
    echo "ERROR: Failed to compile SoftwareRT_Shading.hlsl"
    exit 1
fi
echo "      Output: SoftwareRT_Shading_cs_sm50.cso"

echo ""
echo "[4/4] Compiling SoftwareRT_Denoise.hlsl..."
if [ $USE_DXC -eq 1 ]; then
    $COMPILER $COMPILER_FLAGS -E main -Fo SoftwareRT_Denoise_cs_sm50.cso SoftwareRT_Denoise.hlsl
else
    $COMPILER $COMPILER_FLAGS /E main /Fo SoftwareRT_Denoise_cs_sm50.cso SoftwareRT_Denoise.hlsl
fi
if [ $? -ne 0 ]; then
    echo "ERROR: Failed to compile SoftwareRT_Denoise.hlsl"
    exit 1
fi
echo "      Output: SoftwareRT_Denoise_cs_sm50.cso"

echo ""
echo "================================================================================"
echo "Compilation Complete!"
echo "================================================================================"
echo "All shaders compiled successfully for Shader Model 5.0"
echo ""
echo "Hardware Requirements:"
echo "  - Intel HD Graphics 4000+ (Ivy Bridge 2012+)"
echo "  - GeForce GTX 400+ (Fermi 2010+)"
echo "  - Radeon HD 5000+ (Evergreen 2009+)"
echo ""
echo "Fallback: Systems without SM 5.0 will use CPU culling"
echo "================================================================================"
