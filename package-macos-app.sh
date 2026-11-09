#!/bin/bash
# ==============================================================================
# BlueSky Engine — Avalanche macOS Application (.app) Packager CLI
# Usage: ./package-macos-app.sh [AppName] [BundleId] [StartupScenePath] [OutputDir] [SigningIdentity]
# ==============================================================================
set -e

REPO_ROOT="$(cd "$(dirname "$0")" && pwd)"
APP_NAME="${1:-BlueSkyGame}"
EXE_NAME="$(echo "$APP_NAME" | tr ' ' '_' | tr -cd '[:alnum:]_-')"
[ -z "$EXE_NAME" ] && EXE_NAME="BlueSkyGame"

SLUG="$(echo "$EXE_NAME" | tr '[:upper:]' '[:lower:]' | tr '_' '-')"
BUNDLE_ID="${2:-com.bluesky.$SLUG}"
STARTUP_SCENE="${3:-}"
OUT_DIR="${4:-$REPO_ROOT/Builds/MacOS}"
SIGNING_IDENTITY="${5:-${DEVELOPER_ID:-}}"
SIGNING_MODE="${SIGNING_MODE:-Auto}"

# Locate dotnet if installed
export PATH="/opt/homebrew/opt/dotnet@8/bin:/opt/homebrew/bin:/usr/local/share/dotnet:$HOME/.dotnet:$PATH"
if ! command -v dotnet >/dev/null 2>&1 && [ -d "/opt/homebrew/Cellar/dotnet@8" ]; then
    LATEST_DOTNET="$(ls -1d /opt/homebrew/Cellar/dotnet@8/*/libexec 2>/dev/null | tail -n 1)"
    [ -n "$LATEST_DOTNET" ] && export PATH="$LATEST_DOTNET:$PATH"
fi

if command -v dotnet >/dev/null 2>&1; then
    echo "==> [Avalanche] Building BlueSkyEngine & BlueSkyRuntime..."
    dotnet build "$REPO_ROOT/BlueSkyEngine/BlueSkyEngine.csproj" -v minimal
    dotnet build "$REPO_ROOT/BlueSkyRuntime/BlueSkyRuntime.csproj" -v minimal
    echo "==> [Avalanche] Running AvalanchePackager via BlueSkyEngine (--package-macos)..."
    dotnet "$REPO_ROOT/BlueSkyEngine/bin/Debug/net8.0/BlueSkyEngine.dll" --package-macos "$OUT_DIR" "$APP_NAME" "$STARTUP_SCENE" "$SIGNING_MODE" "$SIGNING_IDENTITY"
    exit 0
fi

echo "==> [Avalanche] Packaging '$APP_NAME.app' from compiled engine/runtime binaries..."

APP_BUNDLE="$OUT_DIR/$APP_NAME.app"
CONTENTS_DIR="$APP_BUNDLE/Contents"
MACOS_DIR="$CONTENTS_DIR/MacOS"
RESOURCES_DIR="$CONTENTS_DIR/Resources"
GAMEDATA_DIR="$RESOURCES_DIR/GameData"
GAMEASSETS_DIR="$GAMEDATA_DIR/Assets"
GAMECONFIG_DIR="$GAMEDATA_DIR/Config"

rm -rf "$APP_BUNDLE"
mkdir -p "$MACOS_DIR" "$RESOURCES_DIR" "$GAMEASSETS_DIR" "$GAMECONFIG_DIR"

# 1. Info.plist & PkgInfo
cat > "$CONTENTS_DIR/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>$APP_NAME</string>
    <key>CFBundleDisplayName</key>
    <string>$APP_NAME</string>
    <key>CFBundleIdentifier</key>
    <string>$BUNDLE_ID</string>
    <key>CFBundleVersion</key>
    <string>1.0.0</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0.0</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleSignature</key>
    <string>BSKY</string>
    <key>CFBundleExecutable</key>
    <string>$EXE_NAME</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon</string>
    <key>LSMinimumSystemVersion</key>
    <string>11.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSPrincipalClass</key>
    <string>NSApplication</string>
    <key>NSSupportsAutomaticGraphicsSwitching</key>
    <true/>
</dict>
</plist>
EOF
printf "APPLBSKY" > "$CONTENTS_DIR/PkgInfo"

# 2. Stage runtime & engine assemblies, native libraries, font, and Metal shaders
RUNTIME_BIN="$REPO_ROOT/BlueSkyRuntime/bin/Debug/net8.0"
ENGINE_BIN="$REPO_ROOT/BlueSkyEngine/bin/Debug/net8.0"

for SRC_DIR in "$RUNTIME_BIN" "$ENGINE_BIN"; do
    if [ -d "$SRC_DIR" ]; then
        cp -f "$SRC_DIR"/*.dll "$MACOS_DIR/" 2>/dev/null || true
        cp -f "$SRC_DIR"/*.json "$MACOS_DIR/" 2>/dev/null || true
        cp -f "$SRC_DIR"/*.dylib "$MACOS_DIR/" 2>/dev/null || true
        cp -f "$SRC_DIR"/*.ttf "$MACOS_DIR/" 2>/dev/null || true
        if [ -d "$SRC_DIR/runtimes/osx/native" ]; then
            mkdir -p "$MACOS_DIR/runtimes/osx/native"
            cp -f "$SRC_DIR/runtimes/osx/native"/* "$MACOS_DIR/runtimes/osx/native/" 2>/dev/null || true
            cp -f "$SRC_DIR/runtimes/osx/native/libjoltc.dylib" "$MACOS_DIR/libjoltc.dylib" 2>/dev/null || true
        fi
    fi
done

[ -f "$RUNTIME_BIN/BlueSkyRuntime" ] && cp -f "$RUNTIME_BIN/BlueSkyRuntime" "$MACOS_DIR/BlueSkyRuntimeBin" && chmod +x "$MACOS_DIR/BlueSkyRuntimeBin"
[ -f "$ENGINE_BIN/BlueSkyEngine" ] && cp -f "$ENGINE_BIN/BlueSkyEngine" "$MACOS_DIR/BlueSkyEngineBin" && chmod +x "$MACOS_DIR/BlueSkyEngineBin"
[ -f "$REPO_ROOT/BlueSkyEngine/roboto.ttf" ] && cp -f "$REPO_ROOT/BlueSkyEngine/roboto.ttf" "$MACOS_DIR/roboto.ttf" && cp -f "$REPO_ROOT/BlueSkyEngine/roboto.ttf" "$RESOURCES_DIR/roboto.ttf"

mkdir -p "$MACOS_DIR/Editor/Shaders" "$RESOURCES_DIR/Editor/Shaders"
if [ -d "$REPO_ROOT/BlueSkyEngine/Editor/Shaders" ]; then
    cp -f "$REPO_ROOT/BlueSkyEngine/Editor/Shaders"/*.metal* "$MACOS_DIR/Editor/Shaders/" 2>/dev/null || true
    cp -f "$REPO_ROOT/BlueSkyEngine/Editor/Shaders"/*.hlsl "$MACOS_DIR/Editor/Shaders/" 2>/dev/null || true
    cp -f "$REPO_ROOT/BlueSkyEngine/Editor/Shaders"/*.glsl "$MACOS_DIR/Editor/Shaders/" 2>/dev/null || true
    cp -f "$MACOS_DIR/Editor/Shaders"/* "$RESOURCES_DIR/Editor/Shaders/" 2>/dev/null || true
fi

# 3. Stage GameData (Project file, Config/EOS.ini, Assets, StartupScene)
printf '{ "version": "1.0", "packagedBy": "Avalanche" }\n' > "$GAMEDATA_DIR/$EXE_NAME.BlueSkyProj"
if [ -f "$REPO_ROOT/Config/EOS.ini" ]; then
    cp -f "$REPO_ROOT/Config/EOS.ini" "$GAMECONFIG_DIR/EOS.ini"
fi

if [ -d "$REPO_ROOT/Assets" ]; then
    cp -R "$REPO_ROOT/Assets/"* "$GAMEASSETS_DIR/" 2>/dev/null || true
fi
[ -f "$REPO_ROOT/car_system.tea" ] && cp -f "$REPO_ROOT/car_system.tea" "$GAMEASSETS_DIR/car_system.tea"

SCENE_REL="Assets/StartupScene.bsscene"
if [ -n "$STARTUP_SCENE" ] && [ -f "$STARTUP_SCENE" ]; then
    SCENE_BASE="$(basename "$STARTUP_SCENE")"
    cp -f "$STARTUP_SCENE" "$GAMEASSETS_DIR/$SCENE_BASE"
    SCENE_REL="Assets/$SCENE_BASE"
elif [ ! -f "$GAMEASSETS_DIR/StartupScene.bsscene" ]; then
    cat > "$GAMEASSETS_DIR/StartupScene.bsscene" <<EOF
{
  "Name": "$APP_NAME",
  "Version": "2.0",
  "Entities": [
    {
      "Id": 1,
      "Name": "MainObject",
      "Components": [
        {
          "\$type": "Transform",
          "Position": [0, 1, 0],
          "Rotation": [0, 0, 0, 1],
          "Scale": [1, 1, 1]
        }
      ]
    }
  ]
}
EOF
fi

# 4. Write avalanche.json Manifest
cat > "$RESOURCES_DIR/avalanche.json" <<EOF
{
  "appName": "$APP_NAME",
  "bundleIdentifier": "$BUNDLE_ID",
  "version": "1.0.0",
  "startupScene": "$SCENE_REL",
  "windowWidth": 1280,
  "windowHeight": 720,
  "fullscreen": false,
  "vsync": true,
  "showDiagnosticsHud": false,
  "targetPlatform": "macOS-arm64",
  "buildTimestampUtc": "$(date -u +"%Y-%m-%dT%H:%M:%SZ")"
}
EOF

# 5. Write Bundle Launcher Script
cat > "$MACOS_DIR/$EXE_NAME" <<'EOF'
#!/bin/bash
set -e

MACOS_DIR="$(cd "$(dirname "$0")" && pwd)"
CONTENTS_DIR="$(cd "$MACOS_DIR/.." && pwd)"
RESOURCES_DIR="$CONTENTS_DIR/Resources"
GAMEDATA_DIR="$RESOURCES_DIR/GameData"
MANIFEST_PATH="$RESOURCES_DIR/avalanche.json"

CLEAN_ARGS=()
for arg in "$@"; do
    if [[ "$arg" != -psn_* ]]; then
        CLEAN_ARGS+=("$arg")
    fi
done

if [ -z "$DOTNET_ROOT" ] || [ ! -x "$DOTNET_ROOT/dotnet" ]; then
    for candidate in \
        "/opt/homebrew/opt/dotnet@8/libexec" \
        "/opt/homebrew/opt/dotnet/libexec" \
        "/usr/local/share/dotnet" \
        "$HOME/.dotnet"; do
        if [ -x "$candidate/dotnet" ]; then
            export DOTNET_ROOT="$candidate"
            break
        fi
    done

    if [ -z "$DOTNET_ROOT" ] && [ -d "/opt/homebrew/Cellar/dotnet@8" ]; then
        LATEST_CELLAR="$(ls -1d /opt/homebrew/Cellar/dotnet@8/*/libexec 2>/dev/null | tail -n 1)"
        if [ -n "$LATEST_CELLAR" ] && [ -x "$LATEST_CELLAR/dotnet" ]; then
            export DOTNET_ROOT="$LATEST_CELLAR"
        fi
    fi
fi

if [ -n "$DOTNET_ROOT" ]; then
    export PATH="$DOTNET_ROOT:$PATH"
fi
export PATH="$PATH:/opt/homebrew/bin:/usr/local/bin:$HOME/.dotnet"
export DYLD_LIBRARY_PATH="$MACOS_DIR:$MACOS_DIR/runtimes/osx/native:${DYLD_LIBRARY_PATH}"

if [ -d "$GAMEDATA_DIR" ]; then
    cd "$GAMEDATA_DIR"
else
    cd "$MACOS_DIR"
fi

if command -v dotnet >/dev/null 2>&1 && [ -f "$MACOS_DIR/BlueSkyRuntime.dll" ]; then
    exec dotnet "$MACOS_DIR/BlueSkyRuntime.dll" --avalanche-manifest "$MANIFEST_PATH" "${CLEAN_ARGS[@]}"
elif [ -x "$MACOS_DIR/BlueSkyRuntimeBin" ]; then
    exec "$MACOS_DIR/BlueSkyRuntimeBin" --avalanche-manifest "$MANIFEST_PATH" "${CLEAN_ARGS[@]}"
elif command -v dotnet >/dev/null 2>&1 && [ -f "$MACOS_DIR/BlueSkyEngine.dll" ]; then
    exec dotnet "$MACOS_DIR/BlueSkyEngine.dll" --avalanche-manifest "$MANIFEST_PATH" "${CLEAN_ARGS[@]}"
elif [ -x "$MACOS_DIR/BlueSkyEngineBin" ]; then
    exec "$MACOS_DIR/BlueSkyEngineBin" --avalanche-manifest "$MANIFEST_PATH" "${CLEAN_ARGS[@]}"
else
    /usr/bin/osascript -e 'display alert "BlueSky Avalanche Runtime Error" message "Please install .NET 8 Runtime (brew install dotnet@8) to launch this application." as critical' >/dev/null 2>&1 || true
    exit 1
fi
EOF
chmod +x "$MACOS_DIR/$EXE_NAME"

# 6. Codesign the bundle (Developer ID with Hardened Runtime & Entitlements, or Ad-Hoc fallback)
cat > "$RESOURCES_DIR/entitlements.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>com.apple.security.cs.allow-jit</key>
    <true/>
    <key>com.apple.security.cs.allow-unsigned-executable-memory</key>
    <true/>
    <key>com.apple.security.cs.disable-library-validation</key>
    <true/>
    <key>com.apple.security.cs.allow-dyld-environment-variables</key>
    <true/>
</dict>
</plist>
EOF

if [ -x "/usr/bin/codesign" ]; then
    RESOLVED_IDENTITY="$SIGNING_IDENTITY"
    if [ -z "$RESOLVED_IDENTITY" ] && [ "$SIGNING_MODE" != "AdHoc" ] && [ -x "/usr/bin/security" ]; then
        # Only Developer ID certificates pass Gatekeeper; anything else falls back to ad-hoc.
        RESOLVED_IDENTITY=$(security find-identity -v -p codesigning | grep "Developer ID Application:" | head -n 1 | sed -E 's/.*"([^"]+)".*/\1/' || true)
    fi

    # Arrays keep identities and paths that contain spaces intact.
    if [ -n "$RESOLVED_IDENTITY" ] && [ "$RESOLVED_IDENTITY" != "-" ]; then
        echo "==> [Avalanche] Signing with Developer ID: '$RESOLVED_IDENTITY' (Hardened Runtime + Entitlements)"
        CODESIGN_ARGS=(--force --timestamp --options runtime --entitlements "$RESOURCES_DIR/entitlements.plist" --sign "$RESOLVED_IDENTITY")
    else
        echo "==> [Avalanche] Signing with Ad-Hoc code signature (-)"
        CODESIGN_ARGS=(--force --sign -)
    fi

    for f in "$MACOS_DIR"/*.dylib "$MACOS_DIR"/runtimes/osx/native/*.dylib "$MACOS_DIR"/*Bin "$MACOS_DIR/$EXE_NAME"; do
        [ -f "$f" ] && /usr/bin/codesign "${CODESIGN_ARGS[@]}" "$f" 2>/dev/null || true
    done
    /usr/bin/codesign --deep "${CODESIGN_ARGS[@]}" "$APP_BUNDLE" 2>/dev/null || true
fi

echo "✓ [Avalanche] Successfully packaged macOS App Bundle: $APP_BUNDLE"
