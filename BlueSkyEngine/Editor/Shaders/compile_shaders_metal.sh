#!/bin/bash
# ============================================================================
# Metal Shader Compilation Script (macOS only)
# Compiles Metal shaders to .metallib format for use in the engine
# ============================================================================

set -e  # Exit on error

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

echo "============================================================================"
echo "Metal Shader Compilation"
echo "============================================================================"
echo "Compiling Metal shaders to .metallib format..."
echo ""

# Check if we're on macOS
if [[ "$OSTYPE" != "darwin"* ]]; then
    echo "ERROR: Metal shader compilation is only supported on macOS"
    echo "This script requires the Metal compiler (xcrun) which is only available on macOS"
    exit 1
fi

# Check if xcrun is available
if ! command -v xcrun &> /dev/null; then
    echo "ERROR: xcrun not found. Please install Xcode Command Line Tools:"
    echo "  xcode-select --install"
    exit 1
fi

# ============================================================================
# Compile viewport_3d.metal
# ============================================================================
echo "[1/3] Compiling viewport_3d.metal..."
if [ -f "viewport_3d.metal" ]; then
    xcrun -sdk macosx metal -c viewport_3d.metal -o viewport_3d.air
    xcrun -sdk macosx metallib viewport_3d.air -o viewport_3d.metallib
    rm -f viewport_3d.air
    echo "  ✓ viewport_3d.metallib created"
else
    echo "  ✗ viewport_3d.metal not found"
    exit 1
fi

# ============================================================================
# Compile simple_ui.metal
# ============================================================================
echo "[2/3] Compiling simple_ui.metal..."
if [ -f "simple_ui.metal" ]; then
    xcrun -sdk macosx metal -c simple_ui.metal -o simple_ui.air
    xcrun -sdk macosx metallib simple_ui.air -o simple_ui.metallib
    rm -f simple_ui.air
    echo "  ✓ simple_ui.metallib created"
else
    echo "  ✗ simple_ui.metal not found"
    exit 1
fi

# ============================================================================
# Compile horizon_lighting.metal
# ============================================================================
echo "[3/3] Compiling horizon_lighting.metal..."
if [ -f "horizon_lighting.metal" ]; then
    xcrun -sdk macosx metal -c horizon_lighting.metal -o horizon_lighting.air
    xcrun -sdk macosx metallib horizon_lighting.air -o horizon_lighting.metallib
    rm -f horizon_lighting.air
    echo "  ✓ horizon_lighting.metallib created"
else
    echo "  ✗ horizon_lighting.metal not found"
    exit 1
fi

# ============================================================================
# Compile pbr_optimized.metal (optional)
# ============================================================================
echo "[4/6] Compiling pbr_optimized.metal..."
if [ -f "pbr_optimized.metal" ]; then
    xcrun -sdk macosx metal -c pbr_optimized.metal -o pbr_optimized.air
    xcrun -sdk macosx metallib pbr_optimized.air -o pbr_optimized.metallib
    rm -f pbr_optimized.air
    echo "  ✓ pbr_optimized.metallib created"
else
    echo "  ⊘ pbr_optimized.metal not found (optional)"
fi

# ============================================================================
# Compile PolarisUpscale.metal (AVX Ray Tracing edge-aware upscaler)
# ============================================================================
echo "[5/6] Compiling PolarisUpscale.metal..."
POLARIS_SRC="../../Rendering/Shaders/PolarisUpscale.metal"
if [ -f "$POLARIS_SRC" ]; then
    xcrun -sdk macosx metal -c "$POLARIS_SRC" -o PolarisUpscale.air
    xcrun -sdk macosx metallib PolarisUpscale.air -o PolarisUpscale.metallib
    rm -f PolarisUpscale.air
    echo "  ✓ PolarisUpscale.metallib created"
else
    echo "  ✗ PolarisUpscale.metal not found at $POLARIS_SRC"
    exit 1
fi

# ============================================================================
# Compile and copy EasePlus shaders
# ============================================================================
echo "[6/6] Compiling EasePlus shaders..."
EASEPLUS_DIR="../../Rendering/EasePlus/Shaders"
if [ -d "$EASEPLUS_DIR" ]; then
    (cd "$EASEPLUS_DIR" && chmod +x build_metal_shaders.sh && ./build_metal_shaders.sh)
    cp "$EASEPLUS_DIR"/*.metallib .
    echo "  ✓ EasePlus shaders compiled and copied successfully"
else
    echo "  ⚠ EasePlus shader directory not found at $EASEPLUS_DIR"
fi

echo ""
echo "============================================================================"
echo "Metal shader compilation complete!"
echo "============================================================================"
echo "Generated .metallib files:"
ls -lh *.metallib 2>/dev/null || echo "  (no .metallib files found)"
echo "============================================================================"
