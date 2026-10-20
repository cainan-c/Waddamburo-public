# Waddamburo for PS5 homebrew

The `prospero` branch contains the full game with a native PS5 backend. The current
port has booted, rendered and played songs on **firmware 11.60**. It remains
experimental; hardware testing and further stability work are still needed.

Rendering uses bgfx with the pinned **PS5_Vulkan / Mesa RADV** driver and a direct
`VK_KHR_display` surface. Controllers, display refresh, keyboard, on-screen keyboard,
PNG decoding and AudioOut use PS5 services. Audio is mixed on a native thread.
The PS5 target does not restore or link SDL. Some shared managed API names still
contain `Sdl`; the desktop backend continues to use SDL.

## Build

Use Windows, PowerShell 7, Git and WSL 2. Run these commands from the repository
root on the `prospero` branch:

```powershell
./eng/prospero/setup.ps1 -InstallDotNet -InstallLinux
./eng/prospero/build.ps1 -ManagedOnly
./eng/prospero/build.ps1 -Output Folder
```

`-ManagedOnly` is an optional quick check; it does not produce a PS5 executable.
Setup creates an isolated Debian WSL distribution named `WaddamburoProspero`,
installs .NET SDK **10.0.201** on both hosts, and fetches pinned dependencies into
`.deps/prospero`. Linux compilation uses Clang/LLVM 19, CMake, Ninja, Meson and
Python 3. Internet access, installation privileges for WSL/Linux packages, and
space for the native SDK/Mesa build are required. If Windows asks for a restart
while enabling WSL, complete that before rerunning setup.

The full build writes **`out/prospero-game/module`** with `eboot.bin`, `sce_sys`
metadata and required `sce_module` companions. Add your arcade assets, then use
that folder as the input to your FPKG packaging workflow. The documented route
builds a module folder; FPKG creation and installation are separate steps.

See the [build reference](docs/development/prospero.md) for pinned SDK versions,
build internals, custom paths and regression checks. Desktop native downloads
from `eng/fetch-native.*` do not replace the PS5 native build.

## SDKs and tools

| Project | Role |
| --- | --- |
| [SharpProspero](https://github.com/SvenGDK/SharpProspero) | C# PS5 bindings, NativeAOT integration, ELF linker, module wrapping and metadata tools |
| [PS5 Payload SDK](https://github.com/mihawk-99/PS5_PayloadSDK) | Public PS5 native headers, compiler wrappers, C/C++ runtime and platform support |
| [PS5_Vulkan](https://github.com/mihawk-99/PS5_Vulkan) | Homebrew Vulkan integration, RADV build tooling and source-built runtime companion |
| [PS5_Mesa](https://github.com/mihawk-99/PS5_Mesa) | PS5 Mesa/RADV Vulkan driver implementation |
| [.NET SDK](https://github.com/dotnet/runtime) | .NET 10 NativeAOT compiler and runtime |
| bgfx, FreeType, FFmpeg, vgmstream and SQLite | Rendering, text, media decoding and local scores; see [third-party notices](THIRD_PARTY_NOTICES.md) |

Revisions are pinned in `eng/prospero/dependencies.json`; native source archives
are pinned and hash-checked by the build recipes. `eng/prospero/patch-sharp.py`
applies the port's required linker/runtime compatibility fixes automatically.
Do not replace the pinned SDK with an unpatched checkout. No official Sony SDK
is required by this build.

## Arcade assets and persistent state

Copy the **contents** of your arcade installation's `USRDIR/data` into `Data`
beside the generated `eboot.bin`:

```text
module/
  eboot.bin
  sce_sys/param.json
  sce_module/...
  Data/
    lumendata/packed/...
    don3d/...
    sound/...
    font/font.ttf
  custom_songs/                 # optional
```

An OTF or TTC font is also accepted. Preserve names and subdirectories, including
the capital `D` in `Data`; do not create `Data/data`. You do not need the original
`USRDIR/EBOOT.BIN`, `s357security.bin`, or sibling `cache`, `hash`, `install`,
`logs`, `mucha` and `updates` folders. No arcade assets or commercial fonts are
included in the port.

The packaged asset directory is **`/app0/Data`** and is read-only. Writable state
lives at **`/data/Waddamburo` on the PS5 SSD**, outside the installed package:

- Configuration, scores and writable application data use this home directory.
- bgfx shader cache: `/data/Waddamburo/cache/shaders/bgfx`.
- Startup exception report: `/data/Waddamburo/boot-error.txt`.

Optional custom songs are loaded from `/app0/custom_songs` in the package.
Options exits the startup error screen if required assets cannot be loaded.

## Controller defaults

Menus use gamepad input. Drum bindings are configurable in the game:

| Pad | DualSense buttons |
| --- | --- |
| Left Ka | D-pad Up / Left, L1, L2 |
| Left Don | D-pad Right / Down, L3 |
| Right Don | Square, Cross, R3 |
| Right Ka | Triangle, Circle, R1, R2 |

Options pauses during play. The console controller defaults are applied once;
subsequent custom bindings are preserved.

## Display and current limitations

**Graphics → Frame Limit** offers **60 FPS or 120 FPS**. VSync is always enabled.
Selecting 120 FPS requests 120 Hz output and falls back to 60 Hz if unsupported.
Exclusive Audio, Full Screen, VSync and Letterbox Size controls are omitted from
the console settings.

Online services, automatic updates and optional upscaling are disabled on PS5.
MIDI devices and the desktop folder picker are not exposed. Audio that needs
the desktop external G.719 decoder remains unsupported. Host regression checks
cover memory compatibility, input, assets and worker scheduling; they cannot
establish console graphics or kernel correctness. Include the build version and
relevant `[Waddamburo ...]` kernel-log lines when reporting a console crash.
