#!/usr/bin/env bash
set -euo pipefail
export DOTNET_ROOT=/opt/waddamburo-dotnet
export PATH="$DOTNET_ROOT:$PATH"
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
output="$root/out/build/network-checks"
mkdir -p "$output"
clang-19 -shared -fPIC -O1 "$root/tests/Waddamburo.Prospero.NetworkChecks/network-mock.c" -o "$output/libSceHttp.so"
for name in Net Ssl Sysmodule Random; do ln -sf libSceHttp.so "$output/libSce$name.so"; done
dotnet build "$root/tests/Waddamburo.Prospero.NetworkChecks/NetworkChecks.csproj" -c Release --nologo > "$output/managed-build.log" 2>&1 || { tail -20 "$output/managed-build.log"; exit 1; }
LD_LIBRARY_PATH="$output" dotnet "$root/tests/Waddamburo.Prospero.NetworkChecks/bin/Release/net10.0/NetworkChecks.dll"
