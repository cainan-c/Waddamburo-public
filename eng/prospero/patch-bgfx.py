"""Adapt the pinned upstream bgfx renderer to linked RADV and SDL's PS5 surface."""
from pathlib import Path
import sys

root = Path(sys.argv[1])
allocator = root / 'bx/src/allocator.cpp'
allocator_source = allocator.read_text()
if '#if BX_PLATFORM_PS5\n#include <stdlib.h>' not in allocator_source:
    allocator.write_text(allocator_source.replace('#include <malloc.h>',
        '#if BX_PLATFORM_PS5\n#include <stdlib.h>\n#else\n#include <malloc.h>\n#endif'))
marker = root / '.waddamburo-ps5-v1'
# The statically linked driver uses a sentinel instead of a dlopen handle.
renderer_path = root / 'bgfx/src/renderer_vk.cpp'
renderer_source = renderer_path.read_text()
if 'static void waddamburoClose' not in renderer_source and marker.exists():
    renderer_source = renderer_source.replace('static void* waddamburoLookup',
        'static void waddamburoClose(void* library) {\n#if BX_PLATFORM_PS5\n'
        '    BX_UNUSED(library);\n#else\n    bx::dlclose(library);\n#endif\n}\n'
        'static void* waddamburoLookup')
    renderer_source = renderer_source.replace('bx::dlclose(m_vulkan1Dll)', 'waddamburoClose(m_vulkan1Dll)')
    renderer_path.write_text(renderer_source)
if marker.exists():
    raise SystemExit(0)

def edit(relative, transform):
    path = root / relative
    source = path.read_text()
    result = transform(source)
    if source == result:
        raise SystemExit(f'Pinned source did not match: {relative}')
    path.write_text(result)

def renderer(s):
    s = s.replace('#\tinclude "video_vk.h"', '#\tinclude "video_vk.h"\n'
        '#if BX_PLATFORM_PS5\n'
        'extern "C" PFN_vkVoidFunction waddamburo_ps5_vk_proc(const char* name);\n'
        'extern "C" VkResult waddamburo_ps5_vk_surface(void* window, VkInstance instance, const VkAllocationCallbacks* allocator, VkSurfaceKHR* surface);\n'
        '#endif\n'
        'static void* waddamburoLookup(void* library, const char* name) {\n'
        '#if BX_PLATFORM_PS5\n'
        '    BX_UNUSED(library); return (void*)waddamburo_ps5_vk_proc(name);\n'
        '#else\n'
        '    return bx::dlsym(library, name);\n'
        '#endif\n}\n')
    start = s.index('\t\t\tm_vulkan1Dll = bx::dlopen(')
    end = s.index('\n\t\t\tif (NULL == m_vulkan1Dll)', start)
    s = s[:start] + '#if BX_PLATFORM_PS5\n\t\t\tm_vulkan1Dll = (void*)1;\n#else\n' + s[start:end] + '\n#endif\n' + s[end:]
    s = s.replace('bx::dlsym(m_vulkan1Dll, #_func)', 'waddamburoLookup(m_vulkan1Dll, #_func)')
    s = s.replace('#\telif BX_PLATFORM_NX\n\t\t\tNN_vi_surface,', '#\telif BX_PLATFORM_PS5\n\t\t\tKHR_display,\n#\telif BX_PLATFORM_NX\n\t\t\tNN_vi_surface,')
    s = s.replace('#\telif BX_PLATFORM_NX\n\t\t{ VK_NN_VI_SURFACE_EXTENSION_NAME', '#\telif BX_PLATFORM_PS5\n\t\t{ VK_KHR_DISPLAY_EXTENSION_NAME, 1, true, false, true, Layer::Count },\n#\telif BX_PLATFORM_NX\n\t\t{ VK_NN_VI_SURFACE_EXTENSION_NAME')
    s = s.replace('#elif BX_PLATFORM_NX\n\t\treturn s_extension', '#elif BX_PLATFORM_PS5\n\t\treturn s_extension[Extension::KHR_display].m_supported;\n#elif BX_PLATFORM_NX\n\t\treturn s_extension')
    anchor = '#elif BX_PLATFORM_NX\n\t\tif (NULL != vkCreateViSurfaceNN)'
    assert anchor in s
    s = s.replace(anchor, '#elif BX_PLATFORM_PS5\n\t\tresult = waddamburo_ps5_vk_surface(m_nwh, instance, allocatorCb, &m_surface);\n' + anchor)
    assert 'KHR_display,' in s and 'VK_KHR_DISPLAY_EXTENSION_NAME' in s
    s = s.replace('static void* waddamburoLookup',
        'static void waddamburoClose(void* library) {\n#if BX_PLATFORM_PS5\n'
        '    BX_UNUSED(library);\n#else\n    bx::dlclose(library);\n#endif\n}\n'
        'static void* waddamburoLookup')
    s = s.replace('bx::dlclose(m_vulkan1Dll)', 'waddamburoClose(m_vulkan1Dll)')
    return s

edit('bgfx/src/renderer_vk.cpp', renderer)
# A PS5 is a POSIX platform in bx, but has no desktop GL/X11 library.
edit('cmake/bgfx/bgfx.cmake', lambda s: s.replace('if(UNIX\n', 'if(UNIX AND NOT PS5\n'))
marker.write_text('linked-radv + SDL_Vulkan_CreateSurface\n')
