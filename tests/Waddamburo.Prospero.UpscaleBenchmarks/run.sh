#!/usr/bin/env bash
set -euo pipefail
export DOTNET_ROOT=/opt/waddamburo-dotnet
export PATH="$DOTNET_ROOT:$PATH"
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
output="$root/out/build/upscale-benchmarks"
mkdir -p "$output"
project="$root/tests/Waddamburo.Prospero.UpscaleBenchmarks/UpscaleBenchmarks.csproj"
for profile in baseline ps5; do
 dotnet publish "$project" -c Release -r linux-x64 -p:CpuProfile="$profile" -o "$output/$profile" --nologo > "$output/$profile-build.log" 2>&1 || { tail -20 "$output/$profile-build.log"; exit 1; }
 "$output/$profile/UpscaleBenchmarks" "$profile" "$output/$profile.rgba" | tee "$output/$profile-run.log"
done
cmp "$output/baseline.rgba" "$output/ps5.rgba"
echo 'PASS both NativeAOT CPU profiles produce byte-identical model output (host benchmark, not PS5 timings).'
