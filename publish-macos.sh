#!/bin/bash

echo "Publishing NotBSRenderer Test for macOS (arm64)..."

dotnet publish BlueSky.RHI.Test/BlueSky.RHI.Test.csproj \
    -c Release \
    -r osx-arm64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=false \
    -o publish/macos-arm64

echo ""
echo "Build complete! Output: publish/macos-arm64/"
echo "Run: ./publish/macos-arm64/BlueSky.RHI.Test"
