"""Small PS5 changes to the pinned Realm .NET 20.1.0 native sources."""
from pathlib import Path
import sys
root=Path(sys.argv[1])
p=root/'wrappers/src/CMakeLists.txt'
s=p.read_text(encoding='utf-8').replace('add_library(realm-wrappers SHARED','add_library(realm-wrappers STATIC')
p.write_text(s,encoding='utf-8')
p=root/'wrappers/realm-core/src/realm/util/file.cpp'
s=p.read_text(encoding='utf-8').replace('    !defined(__EMSCRIPTEN__)\n#error "readdir()',
    '    !defined(__EMSCRIPTEN__) && !defined(__PROSPERO__)\n#error "readdir()')
p.write_text(s,encoding='utf-8')
