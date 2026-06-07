#!/bin/bash
# ═══════════════════════════════════════════════════════════════════════════
# BlueSky Engine - Skeletal Animation Shader Compilation Script
# ═══════════════════════════════════════════════════════════════════════════
# Compiles Metal shader for skeletal mesh rendering.
# Run this after modifying skeletal_mesh.metal

set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

echo "═══════════════════════════════════════════════════════════════"
echo "  Compiling Skeletal Animation Shader"
echo "═══════════════════════════════════════════════════════════════"
echo ""

# Check if Metal compiler is available
if ! command -v xcrun &> /dev/null; then
    echo "ERROR: xcrun not found. Are you on macOS with Xcode installed?"
    exit 1
fi

# Compile Metal shader to AIR (Apple Intermediate Representation)
echo "Step 1: Compiling skeletal_mesh.metal → skeletal_mesh.air..."
xcrun -sdk macosx metal \
    -c skeletal_mesh.metal \
    -o skeletal_mesh.air \
    -std=macos-metal2.4 \
    -O3 \
    -ffast-math

if [ $? -ne 0 ]; then
    echo "ERROR: Metal compilation failed"
    exit 1
fi

echo "✓ Compiled to AIR"
echo ""

# Link AIR to metallib (Metal library)
echo "Step 2: Linking skeletal_mesh.air → skeletal_mesh.metallib..."
xcrun -sdk macosx metallib \
    skeletal_mesh.air \
    -o skeletal_mesh.metallib

if [ $? -ne 0 ]; then
    echo "ERROR: Metal library creation failed"
    exit 1
fi

echo "✓ Created Metal library"
echo ""

# Clean up intermediate files
rm -f skeletal_mesh.air

# Show file size
SIZE=$(du -h skeletal_mesh.metallib | cut -f1)
echo "═══════════════════════════════════════════════════════════════"
echo "  Compilation Complete!"
echo "═══════════════════════════════════════════════════════════════"
echo "Output: skeletal_mesh.metallib ($SIZE)"
echo ""
echo "The shader is now ready to use with SkeletalMeshRenderer."
echo ""
