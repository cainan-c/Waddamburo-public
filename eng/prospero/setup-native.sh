#!/usr/bin/env bash
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
deps="$root/.deps/prospero"
downloads="$deps/downloads"
mkdir -p "$downloads"
fetch() {
    local url=$1 file=$2 digest=$3
    if [[ ! -f $file ]]; then curl -fL --retry 3 "$url" -o "$file"; fi
    printf '%s  %s\n' "$digest" "$file" | sha256sum --check --strict
}
if [[ ! -d $deps/bgfx.cmake ]]; then
    file="$downloads/bgfx.tar.gz"
    fetch 'https://github.com/bkaradzic/bgfx.cmake/releases/download/v1.161.9510-579/bgfx.cmake.v1.161.9510-579.tar.gz' "$file" '2b489206be79d0841009c15853aa8717749373528a694933b926b797b4cfc992'
    tar -xzf "$file" -C "$deps"
fi
if [[ ! -d $deps/sqlite-amalgamation-3500400 ]]; then
    file="$downloads/sqlite.zip"
    fetch 'https://www.sqlite.org/2025/sqlite-amalgamation-3500400.zip' "$file" '1d3049dd0f830a025a53105fc79fd2ab9431aea99e137809d064d8ee8356b032'
    unzip -q "$file" -d "$deps"
fi
bash "$deps/PS5_Vulkan/tools/setup-native-dependencies.sh"
chmod +x "$root/eng/prospero/ninja-limited.sh"
NINJA="$root/eng/prospero/ninja-limited.sh" bash "$deps/PS5_Vulkan/tools/build-radv.sh" release
