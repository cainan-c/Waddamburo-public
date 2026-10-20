#!/bin/bash
set -eu
tests=$(cd "$(dirname "$0")" && pwd)
root=$(cd "$tests/../.." && pwd)
output="$root/out/build/gc-regression"
mkdir -p "$output"
cd "$output"
dotnet "$root/.deps/prospero/SharpProspero/tools/SharpProspero.Bindings.Generator/bin/Release/net10.0/sharpprospero-bindgen.dll" compat --out gc-arena-compat.o
cp "$tests/extract-gc-shims.py" .
python3 extract-gc-shims.py
clang-19 -shared -fPIC -O2 -Wl,-Bsymbolic-functions -o libgc-arena-test.so gc-arena-shims.S "$tests/gc-arena-kernel.c" "$root/native/prospero/gc-memory.c" -ldl
clang-19 -O1 -g -pthread "$tests/gc-arena-contract.c" -L. -lgc-arena-test -Wl,-rpath,"$output" -o gc-arena-contract
./gc-arena-contract
dotnet publish "$tests/Stress.csproj" -c Release -r linux-x64 --nologo -o "$output/stress"
timeout 100 env LD_LIBRARY_PATH="$output" LD_PRELOAD="$output/libgc-arena-test.so" "$output/stress/Stress"
