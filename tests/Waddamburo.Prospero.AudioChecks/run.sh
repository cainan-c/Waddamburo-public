#!/bin/bash
set -euo pipefail
export DOTNET_ROOT=/opt/waddamburo-dotnet
export PATH="$DOTNET_ROOT:$PATH"
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
output="$root/out/build/audio-checks"
mkdir -p "$output"
clang-19 -shared -fPIC -O1 -I "$root/native/media/include" "$root/tests/Waddamburo.Prospero.AudioChecks/audio-mock.c" -o "$output/libwaddamburo_media.so"
dotnet build "$root/tests/Waddamburo.Prospero.AudioChecks/AudioChecks.csproj" -c Release --nologo > "$output/managed-build.log" 2>&1 || { tail -20 "$output/managed-build.log"; exit 1; }
LD_LIBRARY_PATH="$output" dotnet "$root/tests/Waddamburo.Prospero.AudioChecks/bin/Release/net10.0/AudioChecks.dll"
