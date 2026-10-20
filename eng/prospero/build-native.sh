#!/usr/bin/env bash
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
deps="$root/.deps/prospero"
bash "$root/eng/prospero/setup-native.sh"
export PS5_PAYLOAD_SDK="${PS5_PAYLOAD_SDK:-$deps/PS5_Vulkan/.deps/native/ps5-payload-sdk}"
[[ -x "$PS5_PAYLOAD_SDK/bin/prospero-clang" ]] || { echo 'Build the pinned public PS5 SDK first.' >&2; exit 2; }
default_build="$root/out/build/native/ps5"
if [[ -d /opt/waddamburo-build/sdk && -w /opt/waddamburo-build/sdk ]]; then
    if [[ $PS5_PAYLOAD_SDK != /opt/waddamburo-build/sdk ]]; then
        rsync -rlp --checksum "$PS5_PAYLOAD_SDK/" /opt/waddamburo-build/sdk/
    fi
    export PS5_PAYLOAD_SDK=/opt/waddamburo-build/sdk
    default_build=/opt/waddamburo-build/native
fi
python3 "$root/eng/prospero/patch-bgfx.py" "$deps/bgfx.cmake"
build="${WADDAMBURO_NATIVE_BUILD:-$default_build}"
cmake -S "$root/native" -B "$build" -G Ninja \
    -DCMAKE_TOOLCHAIN_FILE="$root/native/prospero/toolchain.cmake" \
    -DCMAKE_BUILD_TYPE=Release -DWADDAMBURO_PROSPERO=ON -DWADDAMBURO_PACKAGE=ON \
    -DWADDAMBURO_BUILD_BGFX=OFF -DBUILD_TESTING=OFF
cmake --build "$build" --parallel "${BUILD_JOBS:-4}"
echo "Native engine archives built in $build"
# NativeAOT emits the application's object on Linux. A final Linux executable
# cannot link PS5 service imports; the PS5 linker consumes the completed object.
if [[ -x /opt/waddamburo-dotnet/dotnet ]]; then
    export DOTNET_ROOT=/opt/waddamburo-dotnet
    export PATH="$DOTNET_ROOT:$PATH"
fi
project="$root/src/Waddamburo.App.Prospero"
object="$project/obj/Release/net10.0/linux-x64/native/Waddamburo.App.Prospero.o"
rm -f -- "$object"
dotnet publish "$project" -c Release -r linux-x64 -p:SharpProsperoRoot="$deps/SharpProspero" --nologo >"$build/managed-object.log" 2>&1 || {
    [[ -s $object ]] || { tail -40 "$build/managed-object.log" >&2; exit 1; }
}
python3 "$root/eng/prospero/prepare-link.py" "$root" "$build" "$PS5_PAYLOAD_SDK"

python3 "$root/eng/prospero/check-no-sdl.py" "$root"
