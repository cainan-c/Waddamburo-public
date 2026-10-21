#!/usr/bin/env bash
set -euo pipefail
export DOTNET_ROOT=/opt/waddamburo-dotnet
export PATH="$DOTNET_ROOT:$PATH"
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
output="$root/out/build/upscale-checks"
mkdir -p "$output"
clang++-19 -shared -fPIC -O2 -std=c++17 -pthread -I"$root/native/texture/include" -I"$root/native/texture/third_party/bc7enc" "$root/native/texture/src/texture.cpp" "$root/native/texture/third_party/bc7enc/bc7enc.cpp" "$root/native/texture/third_party/bc7enc/bc7decomp.cpp" -o "$output/libwaddamburo_texture.so"
dotnet build "$root/tests/Waddamburo.Prospero.UpscaleChecks/UpscaleChecks.csproj" -c Release --nologo > "$output/managed-build.log" 2>&1 || { tail -20 "$output/managed-build.log"; exit 1; }
LD_LIBRARY_PATH="$output" dotnet "$root/tests/Waddamburo.Prospero.UpscaleChecks/bin/Release/net10.0/UpscaleChecks.dll"
