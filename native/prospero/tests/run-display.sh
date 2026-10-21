#!/usr/bin/env bash
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../../.." && pwd)
output="$root/out/build/display-regression"
include="${PS5_PAYLOAD_SDK:-$root/.deps/prospero/PS5_Vulkan/.deps/native/ps5-payload-sdk}/target/include"
mkdir -p "$output"
clang-19 -std=c11 -Wall -Wextra -Werror -Wno-unused-parameter -fsanitize=address,undefined \
    -I "$include" "$root/native/prospero/display-refresh.c" \
    "$root/native/prospero/tests/display-refresh-test.c" -o "$output/display-refresh-test"
"$output/display-refresh-test"
python3 - "$root" <<'PY'
from pathlib import Path
import json, sys
root=Path(sys.argv[1])
param=json.loads((root/'src/Waddamburo.App.Prospero/sce_sys/param.json').read_text())
assert param['attribute3'] & 0xc0040 == 0x40040
print('PASS param.json declares high-frame-rate output (0x40040) without the alternate VRR-required bit')
PY
