#!/bin/bash

# BlueSky Engine Launch Script for macOS
# This script compiles shaders, builds the project, and launches the engine

set -e  # Exit on error

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Print colored message
print_step() {
    echo -e "${BLUE}==>${NC} ${GREEN}$1${NC}"
}

print_error() {
    echo -e "${RED}ERROR:${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}WARNING:${NC} $1"
}

# Check if running on macOS
if [[ "$OSTYPE" != "darwin"* ]]; then
    print_error "This script is designed for macOS only!"
    exit 1
fi

# Check for required tools
print_step "Checking prerequisites..."

if ! command -v xcrun &> /dev/null; then
    print_error "Xcode Command Line Tools not found!"
    echo "Install with: xcode-select --install"
    exit 1
fi

if ! command -v dotnet &> /dev/null; then
    print_error ".NET SDK not found!"
    echo "Download from: https://dotnet.microsoft.com/download"
    exit 1
fi

echo "  ✓ Xcode Command Line Tools"
echo "  ✓ .NET SDK $(dotnet --version)"

# Step 1: Compile Metal Shaders
print_step "Compiling Metal shaders..."

SHADER_DIR="BlueSkyEngine/Editor/Shaders"

if [ ! -d "$SHADER_DIR" ]; then
    print_error "Shader directory not found: $SHADER_DIR"
    exit 1
fi

cd "$SHADER_DIR"

# Compile viewport_3d.metal
if [ -f "viewport_3d.metal" ]; then
    echo "  Compiling viewport_3d.metal..."
    xcrun -sdk macosx metal -c viewport_3d.metal -o viewport_3d.air 2>&1 | grep -v "warning:" || true
    xcrun -sdk macosx metallib viewport_3d.air -o viewport_3d.metallib
    rm -f viewport_3d.air
    echo "  ✓ viewport_3d.metallib"
else
    print_warning "viewport_3d.metal not found, skipping..."
fi

# Compile simple_ui.metal
if [ -f "simple_ui.metal" ]; then
    echo "  Compiling simple_ui.metal..."
    xcrun -sdk macosx metal -c simple_ui.metal -o simple_ui.air 2>&1 | grep -v "warning:" || true
    xcrun -sdk macosx metallib simple_ui.air -o simple_ui.metallib
    rm -f simple_ui.air
    echo "  ✓ simple_ui.metallib"
else
    print_warning "simple_ui.metal not found, skipping..."
fi

# Compile horizon_lighting.metal
if [ -f "horizon_lighting.metal" ]; then
    echo "  Compiling horizon_lighting.metal..."
    xcrun -sdk macosx metal -c horizon_lighting.metal -o horizon_lighting.air 2>&1 | grep -v "warning:" || true
    xcrun -sdk macosx metallib horizon_lighting.air -o horizon_lighting.metallib
    rm -f horizon_lighting.air
    echo "  ✓ horizon_lighting.metallib"
else
    print_warning "horizon_lighting.metal not found, skipping..."
fi

# Compile pbr_optimized.metal
if [ -f "pbr_optimized.metal" ]; then
    echo "  Compiling pbr_optimized.metal..."
    xcrun -sdk macosx metal -c pbr_optimized.metal -o pbr_optimized.air 2>&1 | grep -v "warning:" || true
    xcrun -sdk macosx metallib pbr_optimized.air -o pbr_optimized.metallib
    rm -f pbr_optimized.air
    echo "  ✓ pbr_optimized.metallib"
else
    print_warning "pbr_optimized.metal not found, skipping..."
fi

cd - > /dev/null

# Step 2: Build the project
print_step "Building BlueSky Engine..."

if [ ! -f "BlueSkyEngine/BlueSkyEngine.csproj" ]; then
    print_error "Project file not found: BlueSkyEngine/BlueSkyEngine.csproj"
    exit 1
fi

dotnet build BlueSkyEngine/BlueSkyEngine.csproj --configuration Release --nologo --verbosity quiet

if [ $? -eq 0 ]; then
    echo "  ✓ Build successful"
else
    print_error "Build failed!"
    exit 1
fi

# Step 3: Launch the engine
print_step "Launching BlueSky Engine..."
echo ""
echo -e "${GREEN}╔════════════════════════════════════════╗${NC}"
echo -e "${GREEN}║                                        ║${NC}"
echo -e "${GREEN}║        BlueSky Engine Starting...      ║${NC}"
echo -e "${GREEN}║                                        ║${NC}"
echo -e "${GREEN}╚════════════════════════════════════════╝${NC}"
echo ""

dotnet run --project BlueSkyEngine/BlueSkyEngine.csproj --configuration Release --no-build
