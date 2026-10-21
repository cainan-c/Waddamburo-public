"""Give the CLR compression bridge its matching, privately named zlib implementation."""
from pathlib import Path
import re, shutil, subprocess, sys

runtime, stage = map(Path, sys.argv[1:])
stage.mkdir(parents=True, exist_ok=True)
nm = shutil.which('llvm-nm-19') or shutil.which('llvm-nm')
objcopy = shutil.which('llvm-objcopy-19') or shutil.which('llvm-objcopy')
if not nm or not objcopy:
    raise SystemExit('LLVM archive tools are required for CLR compression isolation.')
symbols = subprocess.check_output([nm, '--defined-only', '--extern-only', '--format=posix', str(runtime / 'libz.a')], text=True)
names = sorted(set(re.findall(r'^(\S+) \S ', symbols, re.M)))
if 'inflateInit2_' not in names or 'inflate' not in names:
    raise SystemExit('The selected CLR runtime pack does not contain its expected zlib implementation.')
mapping = stage / 'clr-zlib-symbols.txt'
mapping.write_text(''.join(f'{name} waddamburo_clr_zlib_{name}\n' for name in names))
for source, target in [('libz.a', 'clr-zlib.a'), ('libSystem.IO.Compression.Native.a', 'clr-compression.a')]:
    subprocess.run([objcopy, '--strip-debug', '--redefine-syms=' + str(mapping), str(runtime / source), str(stage / target)], check=True)
imports = subprocess.check_output([nm, '--undefined-only', '--extern-only', '--format=posix', str(stage / 'clr-compression.a')], text=True)
unwanted = [name for name in names if re.search(r'^' + re.escape(name) + r' U ', imports, re.M)]
if unwanted:
    raise SystemExit('CLR compression retains unisolated zlib imports.')
print(f'Isolated {len(names)} CLR zlib definitions and matching compression-bridge imports.')
