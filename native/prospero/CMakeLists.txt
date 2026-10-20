# The main native project includes this only for the PS5 target.
set(_deps "${CMAKE_SOURCE_DIR}/../.deps/prospero")
include_directories(SYSTEM "${_deps}/PS5_Mesa/include")

set(BGFX_LIBRARY_TYPE STATIC CACHE STRING "" FORCE)
set(BGFX_BUILD_TOOLS OFF CACHE BOOL "" FORCE)
set(BGFX_BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
set(BGFX_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(BGFX_CONFIG_VIDEO OFF CACHE BOOL "" FORCE)
set(BGFX_CONFIG_MULTITHREADED ON CACHE BOOL "" FORCE)
add_compile_definitions(BGFX_CONFIG_RENDERER_VULKAN=1 BGFX_CONFIG_MAX_TEXTURES=16384)
add_subdirectory("${_deps}/bgfx.cmake" "${CMAKE_BINARY_DIR}/bgfx")
target_compile_definitions(bgfx PRIVATE BGFX_PLATFORM_SUPPORTS_PSSL=0 BGFX_PLATFORM_SUPPORTS_SPIRV=1)

add_library(waddamburo_ps5 STATIC bridge.c audio.c display-refresh.c)

add_library(e_sqlite3 STATIC "${_deps}/sqlite-amalgamation-3500400/sqlite3.c" sqlite-trace.c)
target_include_directories(e_sqlite3 PRIVATE "${CMAKE_CURRENT_LIST_DIR}" "${_deps}/sqlite-amalgamation-3500400")
target_compile_definitions(e_sqlite3 PRIVATE SQLITE_THREADSAFE=1 SQLITE_CUSTOM_INCLUDE=sqlite-prospero-config.h SQLITE_ENABLE_COLUMN_METADATA=1
    SQLITE_ENABLE_FTS5=1 SQLITE_ENABLE_RTREE=1 SQLITE_ENABLE_PREUPDATE_HOOK=1 SQLITE_ENABLE_SESSION=1)
