"""Convert LLVM's legacy ELF ctor/dtor arrays, preserving their execution order."""
from pathlib import Path
import struct
import subprocess
import sys

path = Path(sys.argv[1])
data = bytearray(path.read_bytes())
renames = set()

def elf(base, length):
    if data[base:base + 6] != b'\x7fELF\x02\x01':
        return
    shoff = struct.unpack_from('<Q', data, base + 40)[0]
    entsize, count, strings = struct.unpack_from('<HHH', data, base + 58)
    if not count or strings >= count:
        raise ValueError('Unsupported extended ELF section table')
    sections = [struct.unpack_from('<IIQQQQIIQQ', data, base + shoff + i * entsize) for i in range(count)]
    names = sections[strings]
    table = data[base + names[4]:base + names[4] + names[5]]
    for index, sec in enumerate(sections):
        end = table.index(0, sec[0])
        name = bytes(table[sec[0]:end]).decode()
        prefix = next((p for p in ('.ctors', '.dtors') if name == p or name.startswith(p + '.')), None)
        if not prefix:
            continue
        new = '.init_array' if prefix == '.ctors' else '.fini_array'
        if name != prefix:
            new += '.' + str(65535 - int(name[len(prefix) + 1:]))
        renames.add((name, new))
        offset, size = sec[4:6]
        if size % 8:
            raise ValueError('Misaligned constructor array')
        entries = [data[base + offset + n:base + offset + n + 8] for n in range(0, size, 8)]
        data[base + offset:base + offset + size] = b''.join(reversed(entries))
        for reloc in sections:
            if reloc[1] not in (4, 9) or reloc[7] != index:
                continue
            for n in range(0, reloc[5], reloc[9]):
                at = base + reloc[4] + n
                old = struct.unpack_from('<Q', data, at)[0]
                struct.pack_into('<Q', data, at, size - 8 - old)

if data[:8] == b'!<arch>\n':
    cursor = 8
    while cursor < len(data):
        size = int(data[cursor + 48:cursor + 58])
        elf(cursor + 60, size)
        cursor += 60 + size + size % 2
else:
    elf(0, len(data))
if renames:
    path.write_bytes(data)
    args = ['llvm-objcopy-19']
    for old, new in sorted(renames):
        args += ['--rename-section', old + '=' + new]
    subprocess.run([*args, str(path)], check=True)
