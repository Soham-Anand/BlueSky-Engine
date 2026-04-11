#!/bin/bash
# Test file opening with BlueSky Editor

echo "=== Testing BlueSky Editor File Opening ==="
echo ""

# Get the path to BlueSky.Editor executable
EDITOR_PATH="BlueSky.Editor/bin/Release/net8.0/BlueSky.Editor"

if [ ! -f "$EDITOR_PATH" ]; then
    echo "Error: BlueSky.Editor not found at: $EDITOR_PATH"
    echo "Building project..."
    dotnet build -c Release
fi

if [ ! -f "$EDITOR_PATH" ]; then
    echo "Error: Still can't find BlueSky.Editor. Build may have failed."
    exit 1
fi

echo "Testing with command line arguments..."
echo ""

# Test 1: No arguments (should open project browser)
echo "Test 1: No arguments (should open project browser)"
echo "Command: $EDITOR_PATH"
echo "Expected: Opens ProjectBrowserWindow"
echo ""

# Test 2: With .bluescript file
echo "Test 2: With .bluescript file"
echo "Command: $EDITOR_PATH \"TestScript.bluescript\""
echo "Expected: Opens ScriptEditorWindow with TestScript"
echo ""

# Test 3: With non-existent file
echo "Test 3: With non-existent file"
echo "Command: $EDITOR_PATH \"nonexistent.bluescript\""
echo "Expected: Opens ScriptEditorWindow with new script (file doesn't exist)"
echo ""

# Test 4: With multiple files (should open first valid one)
echo "Test 4: With multiple files"
echo "Command: $EDITOR_PATH \"file1.bluescript\" \"file2.blueproject\""
echo "Expected: Opens first valid file found"
echo ""

# Test 5: With unsupported file type
echo "Test 5: With unsupported file type"
echo "Command: $EDITOR_PATH \"test.txt\""
echo "Expected: Opens ProjectBrowserWindow (no valid extension)"
echo ""

echo "=== Quick Test ==="
echo "Running: $EDITOR_PATH \"TestScript.bluescript\""
echo ""

# Make the editor executable
chmod +x "$EDITOR_PATH"

# Run the test
"$EDITOR_PATH" "TestScript.bluescript"

echo ""
echo "If the editor opened with TestScript loaded, the file opening works!"
echo "If it opened the project browser instead, check:"
echo "1. Command-line arguments are being passed correctly"
echo "2. File exists and has .bluescript extension"
echo "3. App.axaml.cs is handling the arguments properly"