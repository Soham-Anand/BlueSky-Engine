#!/bin/bash
echo "Testing BlueSky Editor File Opening..."
echo ""

# Build the project first
echo "Building project..."
dotnet build -c Release

echo ""
echo "Test 1: Open project browser (no args)"
./BlueSky.Editor/bin/Release/net8.0/BlueSky.Editor

echo ""
echo "Test 2: Open a .bluescript file"
./BlueSky.Editor/bin/Release/net8.0/BlueSky.Editor TestScript.bluescript

echo ""
echo "Test 3: Open with non-existent file (should create new)"
./BlueSky.Editor/bin/Release/net8.0/BlueSky.Editor "nonexistent.bluescript"

echo ""
echo "Test complete!"