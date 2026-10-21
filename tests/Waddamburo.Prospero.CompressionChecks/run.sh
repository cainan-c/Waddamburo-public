#!/bin/bash
set -euo pipefail
export DOTNET_ROOT=/opt/waddamburo-dotnet
export PATH="$DOTNET_ROOT:$PATH"
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
output="$root/out/build/compression-checks"
mkdir -p "$output"
runtime=$(python3 - "$root" <<'PY'
from pathlib import Path
import sys,json
root=Path(sys.argv[1]);data=json.loads((root/'src/Waddamburo.App.Prospero/obj/project.assets.json').read_text())
packages=next(iter(data['packageFolders']))
compiler=next(k for k in data['libraries'] if k.lower().startswith('microsoft.dotnet.ilcompiler/'))
print(Path(packages)/('microsoft.netcore.app.runtime.nativeaot.linux-x64/'+compiler.split('/')[1])/'runtimes/linux-x64/native')
PY
)
python3 "$root/eng/prospero/prepare-compression.py" "$runtime" "$output"
python3 - "$output" <<'PY'
from pathlib import Path
import gzip,zlib,sys
p=Path(sys.argv[1]);plain=bytes(range(256))*257
(p/'gzip.bin').write_bytes(gzip.compress(plain,mtime=0))
(p/'zlib.bin').write_bytes(zlib.compress(plain))
c=zlib.compressobj(wbits=-15);(p/'raw.bin').write_bytes(c.compress(plain)+c.flush())
PY
source="$root/tests/Waddamburo.Prospero.CompressionChecks/pal-compression-check.c"
clang-19 -O2 "$source" "$runtime/libSystem.IO.Compression.Native.a" "$runtime/libz.a" -Wl,--wrap=inflateInit2_ -o "$output/unisolated"
"$output/unisolated" "$output/gzip.bin" "$output/zlib.bin" "$output/raw.bin" --expect-conflict
clang-19 -O2 "$source" "$output/clr-compression.a" "$output/clr-zlib.a" -Wl,--wrap=inflateInit2_ -o "$output/isolated"
"$output/isolated" "$output/gzip.bin" "$output/zlib.bin" "$output/raw.bin"
