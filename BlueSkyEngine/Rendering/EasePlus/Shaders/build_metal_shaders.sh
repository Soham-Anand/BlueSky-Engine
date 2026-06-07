#!/bin/bash
# ═══════════════════════════════════════════════════════════════════════════════
# EasePlus Metal Shader Build Script
# ═══════════════════════════════════════════════════════════════════════════════
# This script compiles all Metal shaders for the EasePlus renderer.
# Requires: Xcode with Metal toolchain installed
#
# Usage: ./build_metal_shaders.sh
# ═══════════════════════════════════════════════════════════════════════════════

set -e  # Exit on error

echo "╔══════════════════════════════════════════════════════════════╗"
echo "║         EasePlus Metal Shader Compilation                   ║"
echo "╚══════════════════════════════════════════════════════════════╝"
echo ""

# Check if Metal toolchain is available
if ! xcrun -sdk macosx metal --version &> /dev/null; then
    echo "❌ Metal toolchain not found!"
    echo ""
    echo "To install Metal toolchain:"
    echo "  1. Open Xcode"
    echo "  2. Go to Xcode > Settings > Platforms"
    echo "  3. Download 'Metal Toolchain' component"
    echo ""
    echo "Or run: xcodebuild -downloadComponent MetalToolchain"
    exit 1
fi

echo "✓ Metal toolchain found"
echo ""

# Shader list
SHADERS=(
    "easeplus_prepass"
    "easeplus_lighting"
    "easeplus_material"
    "easeplus_postfx"
    "easeplus_grid"
)

# Compile each shader
for shader in "${SHADERS[@]}"; do
    echo "Compiling ${shader}.metal..."
    
    # Step 1: Compile .metal to .air (Apple Intermediate Representation)
    xcrun -sdk macosx metal \
        -c "${shader}.metal" \
        -o "${shader}.air" \
        -std=metal3.0 \
        -O3 \
        -ffast-math \
        -mmacosx-version-min=11.0
    
    # Step 2: Link .air to .metallib (Metal Library)
    xcrun -sdk macosx metallib \
        "${shader}.air" \
        -o "${shader}.metallib"
    
    # Clean up intermediate .air file
    rm "${shader}.air"
    
    # Get file size
    size=$(du -h "${shader}.metallib" | cut -f1)
    echo "  ✓ ${shader}.metallib (${size})"
    echo ""
done

echo "╔══════════════════════════════════════════════════════════════╗"
echo "║  ✓ All shaders compiled successfully!                       ║"
echo "╚══════════════════════════════════════════════════════════════╝"
echo ""
echo "Compiled shaders:"
ls -lh *.metallib

echo ""
echo "To use these shaders, ensure they're copied to:"
echo "  - BlueSkyEngine/Rendering/EasePlus/Shaders/"
echo "  - Editor/Shaders/ (if using editor)"
