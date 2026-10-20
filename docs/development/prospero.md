# PS5 homebrew build reference

See [the PS5 guide](../../README.PS5.md) for runtime behavior, controller defaults
and asset placement. The port has been exercised on firmware **11.60**, including
rendering and song playback; it remains experimental.

## Build host and setup

The supported scripted workflow uses Windows, Git, PowerShell **7** and WSL **2**.
Run from the repository root on the `prospero` branch:

```powershell
./eng/prospero/setup.ps1 -InstallDotNet -InstallLinux
./eng/prospero/build.ps1 -ManagedOnly
./eng/prospero/build.ps1 -Output Folder
```

The isolated Debian distribution is named `WaddamburoProspero`. Setup leaves the
system-wide WSL default unchanged. To use another Debian-based distribution,
pass `-WslDistribution NAME` to **both** setup and build. `-InstallLinux` runs
package installation as root in that distribution. A recent Debian package set
providing Clang/LLVM 19 is required.

Windows .NET SDK **10.0.201** is installed in `.deps/prospero/dotnet`; the Linux
copy is `/opt/waddamburo-dotnet`. Linux prerequisites include Clang/LLVM/LLD 19,
CMake, Ninja, Meson, Python 3, Make, Git, curl/wget, unzip, rsync, pkg-config,
Bison, Flex, SPIR-V tools, and Mesa's build dependencies. The exact package list
is in [`setup-linux.sh`](../../eng/prospero/setup-linux.sh).

Toolchain clones and downloads live under `.deps/prospero` and are ignored by Git.
The first native build compiles the public SDK and Mesa/RADV; allow substantial
disk space and build time. The default native build moves to
`/opt/waddamburo-build/native` when that directory's SDK parent is available and
writable, keeping compiler output on WSL's Linux filesystem. Artifacts needed
by the PS5 linker are staged back under `out/build/native/ps5` in the checkout.
Keep each checkout's native build directory separate if building several copies;
`WADDAMBURO_NATIVE_BUILD` overrides the CMake build directory and `BUILD_JOBS`
controls native build parallelism (default four).

The Linux scripts can also be used on a native Linux host with equivalent tools
and .NET installed, but the Windows/WSL route is the exercised workflow. A managed
build alone is not a console build. Do not use desktop prebuilt native libraries
from `eng/fetch-native.*` for PS5.

## Pinned public SDKs

[`dependencies.json`](../../eng/prospero/dependencies.json) records:

| Project | Revision |
| --- | --- |
| [SharpProspero](https://github.com/SvenGDK/SharpProspero) | `5dfdb4b0b166e83dc9ae275001afb695f12c7174` |
| [PS5_PayloadSDK](https://github.com/mihawk-99/PS5_PayloadSDK) | `b83202be73e930050e00de0f1b4d9ec46c0391bf` |
| [PS5_Mesa](https://github.com/mihawk-99/PS5_Mesa) | `1ba9874f478657ccf6da72ce55f0260164cc0bd0` |
| [PS5_Vulkan](https://github.com/mihawk-99/PS5_Vulkan) | `f410e67a0241b3e8edcdbcd2a9b285dfc4a412ed` |

SharpProspero supplies .NET bindings, the custom linker and module tools.
PS5_PayloadSDK supplies native headers, compiler wrappers and platform support.
PS5_Vulkan builds and integrates the PS5_Mesa RADV driver. Its source-generated
`libc.prx` runtime companion is built and checked by the console build; no module
extracted from a retail game is required. No official Sony SDK is required.

Additional native dependencies include bgfx/bx/bimg, FreeType, FFmpeg, vgmstream
and SQLite. Source versions and archive hashes are recorded in
[`setup-native.sh`](../../eng/prospero/setup-native.sh) and the native CMake
recipes. Their existing [third-party notices](../../THIRD_PARTY_NOTICES.md) apply;
consult each SDK/driver repository's own license when redistributing binaries.

## Compile, link and package

1. `build.ps1` runs host checks and builds the managed console application.
2. `build-native.sh` builds the PS5 SDK, release RADV and native engine libraries.
3. Linux .NET NativeAOT emits `Waddamburo.App.Prospero.o`. The Linux final link
   cannot resolve PS5 service imports; that expected failure is accepted only
   after a fresh, nonempty application object has been produced. Consult
   `managed-object.log` in the native build directory for this stage's output.
4. `prepare-link.py` patches SharpProspero, separates native PS5 and Linux CLR
   libc ABIs, stages engine libraries/stubs and builds the patched linker.
5. SharpProspero gathers the matching NativeAOT runtime archives, links
   `eboot.bin`, checks runtime companions and metadata, and wraps the modules.

The `Folder` output is **`out/prospero-game/module`**. Add the contents of your own
`USRDIR/data` under its `Data` directory, then create/install an FPKG with your
homebrew packaging workflow. FPKG tooling is a separate prerequisite for that
step. Keep the generated `sce_sys` and `sce_module` directories with `eboot.bin`.
The default local title ID is `PPSA99328`.

The console application is `src/Waddamburo.App.Prospero`; the console solution
is `Waddamburo.Prospero.slnx`. `eng/prospero/check-no-sdl.py` verifies the restored
dependency graph, staged native inputs and application object contain no SDL
runtime/imports. Some reused managed API names retain `Sdl` for compatibility.

## Runtime compatibility changes

`patch-sharp.py` persists the required changes to the pinned SDK: ELF relocation
and import fixes, libc/FILE handling, filesystem compatibility and Linux-to-PS5
memory shims. The build applies these patches; a stock unpatched SDK is not an
equivalent runtime. `prepare-link.py` also stages the native GC compatibility
code and the VideoOut interception used to select the display refresh rate.

PS5 uses 16 KiB pages while the Linux NativeAOT runtime assumes 4 KiB pages.
`native/prospero/gc-memory.c` preserves neighboring live bytes, clears committed
memory, serializes mapping changes and tracks physical owners across virtual
mapping trims/splits. The 1.5 GiB GC arena uses CPU-cached direct memory with a
1 GiB managed heap limit, leaving the much smaller flexible pool for other
runtime/native resources. GC and finalizers remain enabled; collections use
workstation, stop-the-world GC. Eight managed workers are warmed before asset
loading and retained to bound stack/thread allocation pressure.

Shader cache writes go to `/data/Waddamburo/cache/shaders/bgfx`. RADV's Unix
disk-cache route is disabled by the build recipe. Other writable application
state stays under `/data/Waddamburo`, separate from read-only `/app0/Data`.

## Checks

`build.ps1 -ManagedOnly` runs `tests/Waddamburo.Prospero.Checks` and compiles the
managed console target. The full build also runs the no-SDL check.

For the actual GC mapping contracts and NativeAOT stress test, run in the selected
WSL distribution **after the full build has built the patched binding tool**:

```bash
export DOTNET_ROOT=/opt/waddamburo-dotnet
export PATH="$DOTNET_ROOT:$PATH"
cd /mnt/c/path/to/Waddamburo-public
bash tests/Waddamburo.Prospero.Gc/run.sh
dotnet publish tests/Waddamburo.Prospero.WorkerChecks/WorkerChecks.csproj \
  -c Release -r linux-x64 -o out/build/worker-checks
./out/build/worker-checks/WorkerChecks
```

The GC harness uses the emitted SDK compatibility code and production GC shim
against a simulated 16 KiB kernel, followed by real NativeAOT collections and
finalizers. See its [README](../../tests/Waddamburo.Prospero.Gc/README.md) for
the additional 768 MiB heap-capacity check. The worker check exercises warmup,
bounded asynchronous decode/upload work and worker retention. Native audio,
SQLite, stream and in-memory font harnesses are also included in the test tree.

Host checks cannot verify the console GPU, driver, kernel resource limits or
display. On hardware, test title/attract flow, menu navigation, song previews,
several complete songs and repeated transitions, results, saved scores after
restart, shader-cache persistence, and both 60/120 FPS display settings.
