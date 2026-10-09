#!/bin/bash

# BlueSky Runtime Launcher
# Quick script to launch standalone runtime for testing

RUNTIME_PATH="./BlueSkyRuntime/bin/Debug/net8.0/BlueSkyRuntime.dll"

echo "🚀 Launching BlueSky Runtime..."
echo ""

# Check if runtime exists
if [ ! -f "$RUNTIME_PATH" ]; then
    echo "❌ Runtime not found. Building..."
    cd BlueSkyRuntime
    dotnet build
    cd ..
fi

# Launch runtime with arguments
dotnet "$RUNTIME_PATH" "$@"
