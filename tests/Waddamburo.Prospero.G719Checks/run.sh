#!/usr/bin/env bash
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
source="$root/out/source/vgmstream-09c9f40caae4747e44b6a993b3d5b654cef4d1f7"
build="${WADDAMBURO_G719_HOST_BUILD:-$root/out/build/g719-host}"
cmake -S "$source" -B "$build" -G Ninja -DCMAKE_BUILD_TYPE=Release \
    -DBUILD_CLI=OFF -DBUILD_V123=OFF -DBUILD_AUDACIOUS=OFF -DBUILD_SHARED_LIBS=OFF \
    -DUSE_G719=ON -DUSE_G7221=ON -DUSE_FFMPEG=OFF -DUSE_MPEG=OFF -DUSE_VORBIS=OFF \
    -DUSE_ATRAC9=OFF -DUSE_CELT=OFF -DUSE_SPEEX=OFF
cmake --build "$build" --target libvgmstream -j4
cmake -DG719_ENABLED=ON -DG719_BUILD_DIR="$build" -P "$root/native/vgmstream/build-g719.cmake"
clang-19 -O1 -g -I "$source/src" "$root/tests/Waddamburo.Prospero.G719Checks/g719-check.c" \
    "$build/src/libvgmstream.a" "$build/dependencies/libg719_decode/libg719_decode.a" -lm -o "$build/g719-check"
"$build/g719-check" "$build/synthetic-is22.bnsf"
