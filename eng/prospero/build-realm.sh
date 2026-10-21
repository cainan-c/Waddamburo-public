#!/usr/bin/env bash
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
source="$root/.deps/prospero/realm-dotnet"
engine=$(realpath "$1")
build="$(dirname "$engine")/realm"
revision=a4133ff59429949d30c6a7d184768c699bda5655
if [[ ! -d $source/.git ]]; then
    git clone --config core.autocrlf=false --config core.longpaths=true --no-checkout https://github.com/realm/realm-dotnet.git "$source"
    git -C "$source" checkout --detach "$revision"
fi
[[ $(git -C "$source" rev-parse HEAD) == "$revision" ]] || { echo 'Realm .NET revision differs from the pinned runtime.' >&2; exit 2; }
git -C "$source" submodule update --init --recursive --depth 1
python3 "$root/eng/prospero/patch-realm.py" "$source"
cmake -S "$source/wrappers" -B "$build" -G Ninja \
    -DCMAKE_TOOLCHAIN_FILE="$PS5_PAYLOAD_SDK/toolchain/prospero.cmake" \
    -DCMAKE_BUILD_TYPE=Release -DREALM_ENABLE_ENCRYPTION=OFF \
    -DREALM_BUILD_TESTS=OFF -DREALM_BUILD_LIB_ONLY=ON -DREALM_ENABLE_GEOSPATIAL=ON \
    -DCMAKE_CXX_FLAGS="-march=znver2 -ffunction-sections -fdata-sections -D_XOPEN_SOURCE=700 -include $root/native/prospero/realm-compat.h" \
    -DREALM_ENABLE_ASSERTIONS=ON
cmake --build "$build" --target realm-wrappers --parallel "${BUILD_JOBS:-4}"
