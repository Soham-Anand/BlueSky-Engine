#!/bin/bash
# Setup file associations for BlueSky Editor on macOS/Linux

echo "=== BlueSky Editor File Association Setup ==="
echo ""

# Get the path to BlueSky.Editor executable
EDITOR_PATH="$(pwd)/BlueSky.Editor/bin/Release/net8.0/BlueSky.Editor"

if [ ! -f "$EDITOR_PATH" ]; then
    echo "Error: BlueSky.Editor not found at: $EDITOR_PATH"
    echo "Please build the project first: dotnet build -c Release"
    exit 1
fi

echo "Found BlueSky.Editor at: $EDITOR_PATH"
echo ""

# Create desktop entry for Linux
if [[ "$OSTYPE" == "linux-gnu"* ]]; then
    echo "Setting up for Linux..."
    
    # Create .desktop file
    DESKTOP_FILE="$HOME/.local/share/applications/bluesky-editor.desktop"
    
    cat > "$DESKTOP_FILE" << EOF
[Desktop Entry]
Name=BlueSky Editor
Comment=BlueSky Game Engine Editor
Exec=$EDITOR_PATH %F
Icon=bluesky
Terminal=false
Type=Application
MimeType=application/x-bluescript;application/x-blueproject;application/x-blueasset
Categories=Development;Game;
StartupNotify=true
EOF
    
    # Update MIME database
    echo "application/x-bluescript;BlueScript file" > "$HOME/.local/share/mime/packages/bluescript.xml"
    echo "application/x-blueproject;BlueProject file" > "$HOME/.local/share/mime/packages/blueproject.xml"
    echo "application/x-blueasset;BlueAsset file" > "$HOME/.local/share/mime/packages/blueasset.xml"
    
    update-desktop-database "$HOME/.local/share/applications"
    update-mime-database "$HOME/.local/share/mime"
    
    echo "✓ Linux file associations set up"
    
elif [[ "$OSTYPE" == "darwin"* ]]; then
    echo "Setting up for macOS..."
    
    # Create AppleScript to handle file opening
    SCRIPT_DIR="$HOME/Library/Application Scripts/com.bluesky.editor"
    mkdir -p "$SCRIPT_DIR"
    
    cat > "$SCRIPT_DIR/open-bluescript.scpt" << 'EOF'
on open theFiles
    repeat with aFile in theFiles
        set filePath to POSIX path of aFile
        do shell script "\"" & "$EDITOR_PATH" & "\" \"" & filePath & "\""
    end repeat
end open
EOF
    
    # Create Info.plist for the app (simplified)
    echo "Note: For full macOS integration, you need to:"
    echo "1. Create a proper .app bundle"
    echo "2. Set CFBundleDocumentTypes in Info.plist"
    echo "3. Use: open -a BlueSky.Editor test.bluescript"
    echo ""
    echo "For now, you can use: $EDITOR_PATH \"test.bluescript\""
    
else
    echo "Unsupported OS: $OSTYPE"
    echo "Please manually associate .bluescript files with:"
    echo "  $EDITOR_PATH \"%1\""
fi

echo ""
echo "=== Setup Complete ==="
echo ""
echo "You can now:"
echo "1. Double-click .bluescript files to open in BlueSky Editor"
echo "2. Use command line: $EDITOR_PATH \"file.bluescript\""
echo "3. Drag files onto the BlueSky.Editor executable"
echo ""
echo "Test with: $EDITOR_PATH \"TestScript.bluescript\""