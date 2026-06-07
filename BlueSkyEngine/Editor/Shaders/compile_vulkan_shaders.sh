#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

if ! command -v glslangValidator >/dev/null 2>&1; then
    echo "glslangValidator is required to compile Vulkan shaders." >&2
    exit 1
fi

glslangValidator -V simple_ui.vert.glsl -o simple_ui.vert.spv
glslangValidator -V simple_ui.frag.glsl -o simple_ui.frag.spv
