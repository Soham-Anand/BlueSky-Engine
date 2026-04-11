#!/bin/bash

echo "=== BlueSky Engine Build and Run ==="
echo ""

# Step 1: Compile shaders
echo "Step 1: Compiling shaders..."
./build-shaders.sh
if [ $? -ne 0 ]; then
    echo "❌ Shader compilation failed"
    exit 1
fi
echo ""

# Step 2: Build the project
echo "Step 2: Building project..."
dotnet build BlueSkyEngine.sln --configuration Debug
if [ $? -ne 0 ]; then
    echo "❌ Build failed"
    exit 1
fi
echo "✅ Build successful"
echo ""

# Step 3: Run the editor
echo "Step 3: Starting BlueSky Editor..."
echo ""
dotnet run --project BlueSkyEngine/BlueSkyEngine.csproj --configuration Debug
