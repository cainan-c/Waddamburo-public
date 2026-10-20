"""Verify the PS5 target does not restore or link an SDL runtime."""
from pathlib import Path
import json,re,subprocess,sys
root=Path(sys.argv[1])
assets=json.loads((root/'src/Waddamburo.App.Prospero/obj/project.assets.json').read_text())
bad=[name for name in assets['libraries'] if 'sdl' in name.lower()]
assert not bad, f'SDL dependency restored: {bad}'
inputs=(root/'out/build/native/ps5/engine-libraries.rsp').read_text().splitlines()
assert not any('sdl' in Path(name).name.lower() for name in inputs), inputs
obj=root/'src/Waddamburo.App.Prospero/obj/Release/net10.0/linux-x64/native/Waddamburo.App.Prospero.o'
symbols=subprocess.check_output(['llvm-nm-19','--undefined-only',str(obj)],text=True)
assert not re.search(r'\bSDL_\w+',symbols), 'Application still imports SDL functions'
print('PASS no SDL NuGet dependency, native archive or application import.')
