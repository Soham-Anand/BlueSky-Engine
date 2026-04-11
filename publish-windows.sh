#!/bin/bash

echo "Publishing NotBSRenderer Test for Windows (x64)..."

dotnet publish BlueSky.RHI.Test/BlueSky.RHI.Test.csproj \
    -c Release \
    -r win-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=false \
    -o publish/windows-x64

echo ""
echo "Build complete! Output: publish/windows-x64/"
echo "Copy the entire folder to your Windows laptop and run BlueSky.RHI.Test.exe"
