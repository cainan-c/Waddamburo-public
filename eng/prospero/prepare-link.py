"""Stage native archives for SharpProspero; keep native and .NET libc ABIs separate."""
from pathlib import Path
import re
import shutil
import subprocess
import sys

root, build, sdk = map(Path, sys.argv[1:4])
stage = root / 'out/build/native/ps5/link'
stage.mkdir(parents=True, exist_ok=True)
platform = sdk / 'target/lib/libps5platform.a'
nm = shutil.which('llvm-nm-19') or shutil.which('llvm-nm')
objcopy = shutil.which('llvm-objcopy-19') or shutil.which('llvm-objcopy')
lld = shutil.which('ld.lld-19') or shutil.which('ld.lld')
if not all((nm, objcopy, lld)):
    raise SystemExit('llvm-nm, llvm-objcopy and ld.lld are required')
symbols = subprocess.check_output([nm, '--defined-only', '--format=posix', str(platform)], text=True)
defined = set(re.findall(r'^(\S+) [TWDBV] ', symbols, re.M))
# The CLR was compiled for Linux; SDL/bgfx/Mesa were compiled for the PS5 ABI.
# Use SharpProspero's device-alias mechanism to keep their POSIX structures,
# clock IDs, flags and pthread attributes out of the CLR's translating shims.
sharp = root / '.deps/prospero/SharpProspero'
subprocess.run(['python3', str(root / 'eng/prospero/patch-sharp.py'), str(sharp)], check=True)
tool_project = sharp / 'tools/SharpProspero.Bindings.Generator/SharpProspero.Bindings.Generator.csproj'
subprocess.run(['dotnet', 'build', str(tool_project), '-c', 'Release', '--nologo', '-v', 'quiet'], check=True)
tool = tool_project.parent / 'bin/Release/net10.0/sharpprospero-bindgen.dll'
probe = stage / 'compat-probe.o'
subprocess.run(['dotnet', str(tool), 'compat', '--out', str(probe)], check=True)
compat_symbols = subprocess.check_output([nm, '--defined-only', '--format=posix', str(probe)], text=True)
compat = set(re.findall(r'^(\S+) [TWDBV] ', compat_symbols, re.M))
compat.update(("mmap", "mprotect", "munmap", "madvise")) # public mappings live in gc-memory.o
catalog = (sharp / 'tools/SharpProspero.Prx/StubCatalog.cs').read_text()
published = set(re.findall(r'"([A-Za-z_][A-Za-z_0-9]*)"', catalog))
kernel_symbols = subprocess.check_output([nm, '--dynamic', '--defined-only', '--format=posix', str(sdk / 'target/lib/libkernel.so')], text=True)
kernel = set(re.findall(r'^(\S+) T ', kernel_symbols, re.M))
extra_kernel = sorted(kernel - published)
names = stage / 'kernel-extra.txt'
names.write_text(''.join(name + '\n' for name in extra_kernel))
kernel_stub = stage / 'kernel-extra.stub'
subprocess.run(['dotnet', str(tool), 'stub', '--lib', 'libkernel', '--names', str(names), '--out', str(kernel_stub)], check=True)
published.update(kernel)
libc_symbols = subprocess.check_output([nm, '--dynamic', '--defined-only', '--format=posix',
    str(sdk / 'target/lib/libSceLibcInternal.so')], text=True)
libc = set(re.findall(r'^(\S+) [TDBV] ', libc_symbols, re.M))
libc_names = stage / 'libc-extra.txt'
libc_names.write_text(''.join(name + '\n' for name in sorted(libc - published)))
libc_stub = stage / 'libc-extra.stub'
subprocess.run(['dotnet', str(tool), 'stub', '--lib', 'libSceLibcInternal', '--names',
    str(libc_names), '--out', str(libc_stub)], check=True)
published.update(libc)
device = {name: '__sp_device_' + name for name in compat & published if not name.startswith('__sp_')}
aliases = {}
for name in defined:
    if name.startswith('ps5_'):
        aliases[name[4:]] = name
    if name.startswith('__wrap_'):
        aliases[name[7:]] = name
        aliases['__real_' + name[7:]] = device.get(name[7:], name[7:])
for name, target in device.items():
    aliases.setdefault(name, target)
# Native libc++ and the CLR carry different libunwind builds. Keep each build's
# public and internal symbols together instead of selecting duplicate objects.
unwind_symbols = subprocess.check_output([nm, '--defined-only', '--extern-only', '--format=posix',
    str(sdk / 'target/lib/libunwind.a')], text=True)
for name in re.findall(r'^(\S+) [TWDBV] ', unwind_symbols, re.M):
    aliases[name] = 'waddamburo_native_' + name
sdk_libc_symbols = subprocess.check_output([nm, '--defined-only', '--extern-only', '--format=posix',
    str(sdk / 'target/lib/libc.a')], text=True)
for name in re.findall(r'^(\S+) [TWDBV] ', sdk_libc_symbols, re.M):
    aliases.setdefault(name, 'waddamburo_native_' + name)
mapping = stage / 'native-symbols.txt'
mapping.write_text(''.join(f'{old} {new}\n' for old, new in sorted(aliases.items())))

def one(pattern):
    files = list(build.glob(pattern))
    if len(files) != 1:
        raise SystemExit(f'Expected one native archive for {pattern}, found {files}')
    return files[0]

archives = [one('stage/Release/lib/libwaddamburo_' + name + '.a') for name in ('media', 'text', 'texture')]
archives += [one('**/' + name) for name in ('libe_sqlite3.a', 'libwaddamburo_ps5.a')]
archives += sorted((build / 'bgfx').rglob('*.a'))
archives += [build / 'ffmpeg/prefix/lib' / ('lib' + name + '.a') for name in ('avformat', 'avcodec', 'swresample', 'swscale', 'avutil', 'opus')]
archives += [build / 'freetype/prefix/lib/libfreetype.a', build / 'vgmstream/build/src/libvgmstream.a',
    build / 'vgmstream/build/dependencies/libg719_decode/libg719_decode.a']
archives += [sdk / 'target/lib' / name for name in ('libc++.a', 'libc++abi.a', 'libunwind.a', 'libc.a')]
archives += [platform]
# Realm .NET's matching wrapper/Core archives use the native PS5 ABI.
realm_build = build.parent / 'realm'
realm_archives = sorted(path for path in realm_build.rglob('*.a') if 'CMakeFiles' not in path.parts)
if not any(path.name == 'librealm-wrappers.a' for path in realm_archives):
    raise SystemExit('Build the pinned Realm backend with eng/prospero/build-realm.sh first.')
archives += realm_archives
linked = []
optional = stage / 'linux-optional.o'
subprocess.run(['clang-19', '-target', 'x86_64-linux-gnu', '-O2', '-fPIC', '-fno-stack-protector',
    '-c', str(root / 'native/prospero/linux-optional.c'), '-o', str(optional)], check=True)
linked.append(optional)
native_dup = stage / 'native-dup.o'
subprocess.run(['clang-19', '-target', 'x86_64-linux-gnu', '-O2', '-fPIC', '-c', str(root / 'native/prospero/dup.c'), '-o', str(native_dup)], check=True)
linked.append(native_dup)
native_gc = stage / 'gc-memory.o'
subprocess.run(['clang-19', '-target', 'x86_64-linux-gnu', '-O2', '-fPIC', '-fno-stack-protector', '-c', str(root / 'native/prospero/gc-memory.c'), '-o', str(native_gc)], check=True)
linked.append(native_gc)
for index, archive in enumerate(archives):
    if not archive.is_file():
        raise SystemExit(f'Missing {archive}')
    destination = stage / f'{index:02}-{archive.name}'
    archive_map = mapping
    if archive == sdk / 'target/lib/libc.a':
        private = dict(aliases)
        for name in re.findall(r'^(\S+) [TWDBV] ', sdk_libc_symbols, re.M):
            if aliases.get(name) in defined or name == 'dup':
                private[name] = 'waddamburo_unused_sdk_' + name
        archive_map = stage / 'sdk-libc-symbols.txt'
        archive_map.write_text(''.join(f'{old} {new}\n' for old, new in sorted(private.items())))
    subprocess.run([objcopy, '--strip-debug', '--redefine-syms=' + str(archive_map), str(archive), str(destination)], check=True)
    subprocess.run(['python3', str(root / 'eng/prospero/normalize-constructors.py'), str(destination)], check=True)
    linked.append(destination)

# Mesa's dispatch tables contain weak references: keep all driver entry points.
radv = root / '.deps/prospero/PS5_Vulkan/.deps/native/radv-release/lib/libvulkan_radeon.ps5.a'
cache_safe_radv = stage / 'radv-no-disk-cache.a'
subprocess.run(['python3', str(root / 'eng/prospero/disable-mesa-disk-cache.py'), str(radv), str(cache_safe_radv)], check=True)
radv = cache_safe_radv
whole = stage / 'radv.whole.o'
sections = stage / 'radv-sections.ld'
sections.write_text('''SECTIONS {
  .text : { *(.text .text.*) }
  .rodata : { *(.rodata .rodata.*) }
  .data.rel.ro : { *(.data.rel.ro .data.rel.ro.*) }
  .data : { *(.data .data.*) }
  .bss : { *(.bss .bss.*) *(COMMON) }
  .init_array : { *(SORT_BY_INIT_PRIORITY(.init_array.*)) *(.init_array) }
  .fini_array : { *(SORT_BY_INIT_PRIORITY(.fini_array.*)) *(.fini_array) }
  .eh_frame : { *(.eh_frame) }
}
''')
# Keep RADV as a mapped archive until the single final native partial link.
# Feeding a merged relocatable object back into lld would encounter multiple
# relocation tables for its merged sections; the custom final linker handles
# those tables, so merge only once, after all native C++ inputs are present.
driver = stage / 'radv-native.a'
subprocess.run([objcopy, '--strip-debug', '--redefine-syms=' + str(mapping), str(radv), str(driver)], check=True)
subprocess.run([objcopy, '--redefine-sym=sceVideoOutOpen=waddamburo_videoout_open', str(driver)], check=True)
subprocess.run(['python3', str(root / 'eng/prospero/normalize-constructors.py'), str(driver)], check=True)

resource = subprocess.check_output(['clang', '--print-resource-dir'], text=True).strip()
builtins = Path(resource) / 'lib/linux/libclang_rt.builtins-x86_64.a'
destination = stage / builtins.name
shutil.copyfile(builtins, destination)
linked.append(destination)

# Let ELF lld resolve native C++ COMDAT groups and their FDE/LSDA records
# together. Use actual AOT imports as roots and normal archive extraction;
# whole-archiving bimg's overlapping component archives duplicates miniz.
app_object = root / 'src/Waddamburo.App.Prospero/obj/Release/net10.0/linux-x64/native/Waddamburo.App.Prospero.o'
undefined = subprocess.check_output([nm, '--undefined-only', '--extern-only', '--format=posix', str(app_object)], text=True)
roots = sorted(set(re.findall(r'^(\S+) U ', undefined, re.M)))
combined = stage / 'native-engine.o'
objects = [path for path in linked if path.suffix == '.o']
libraries = [path for path in linked if path.suffix == '.a']
subprocess.run([lld, '-r', '-T', str(sections), *[arg for name in roots for arg in ('-u', name)],
    *map(str, objects), '--whole-archive', str(driver), '--no-whole-archive',
    '--start-group', *map(str, libraries), '--end-group',
    '-o', str(combined)], check=True)
linked = [combined]

# Desktop fallback types still carry the framework's optional TLS imports.
# Stage their native glue for linking; PS5 transports use console system services.
assets = root / 'src/Waddamburo.App.Prospero/obj/project.assets.json'
import json
restore = json.loads(assets.read_text())
packages = next(iter(restore['packageFolders']))
compiler_key = next(key for key in restore['libraries'] if key.lower().startswith('microsoft.dotnet.ilcompiler/'))
runtime_key = 'microsoft.netcore.app.runtime.nativeaot.linux-x64/' + compiler_key.split('/')[1]
runtime = Path(packages) / runtime_key.lower() / 'runtimes/linux-x64/native'
# The native engine and the CLR may carry different zlib implementations.
# Keep the CLR bridge, inflater, internal state and helpers in one symbol domain.
subprocess.run(['python3', str(root / 'eng/prospero/prepare-compression.py'), str(runtime), str(stage)], check=True)
linked.extend([stage / 'clr-compression.a', stage / 'clr-zlib.a'])
for name in ('libSystem.Security.Cryptography.Native.OpenSsl.a', 'libSystem.Net.Security.Native.a'):
    destination = stage / name
    shutil.copyfile(runtime / name, destination)
    linked.append(destination)

def host_path(path):
    s = str(path)
    if s.startswith('/mnt/') and len(s) > 7 and s[6] == '/':
        return s[5].upper() + ':/' + s[7:]
    return s

(stage.parent / 'engine-libraries.rsp').write_text(''.join(host_path(path) + '\n' for path in linked))
(stage.parent / 'engine-stubs.rsp').write_text(host_path(kernel_stub) + '\n' + host_path(libc_stub) + '\n')
print(f'Staged {len(linked)} native inputs for the PS5 link')
