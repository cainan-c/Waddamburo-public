"""Keep Mesa's optional Unix disk cache disabled for the PS5 app target."""
from pathlib import Path
import shutil,subprocess,sys,tempfile
source,destination=map(Path,sys.argv[1:3]);destination.parent.mkdir(parents=True,exist_ok=True)
# Rename only the defining member. Other Mesa members retain their references,
# resolved by the console replacement in linux-optional.c at the final link.
with tempfile.TemporaryDirectory(prefix='mesa-cache-',dir=destination.parent) as tmp:
    subprocess.run(['llvm-ar-19','x',str(source.resolve()),'disk_cache_os.c.o'],cwd=tmp,check=True)
    member=Path(tmp)/'disk_cache_os.c.o'
    subprocess.run(['llvm-objcopy-19','--redefine-sym=disk_cache_enabled=waddamburo_mesa_disk_cache_enabled_unused',str(member)],check=True)
    shutil.copyfile(source,destination)
    subprocess.run(['llvm-ar-19','r',str(destination.resolve()),str(member)],check=True)
print('Mesa disk cache definition isolated; console replacement will disable it.')
