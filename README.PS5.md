# Waddamburo for PS5 homebrew

The `prospero` branch contains the full game with a native PS5 backend. The current
port has booted, rendered and played songs on **firmware 11.60**. It remains
experimental; hardware testing and further stability work are still needed.
The v034 120 Hz output switch was also confirmed on PS5 Pro firmware 13.60.
TJA scanning/loading uses the console SHA-256 provider as of v042, preserving
existing chart fingerprints while avoiding Linux OpenSSL initialization.
Login timeouts/certificate errors are displayed and PS5 HTTP stages are logged.
As of v044, HTTPS and WebSocket trust import uses complete PEM CA records with
native context lifetime storage. v045 uses firmware TLS negotiation defaults,
retaining certificate verification. Server login was confirmed working with v045.

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

Starting with v035, the game creates and presets these SSD library folders on
startup, including for existing configurations. No manual config edit is needed:

| Library | SSD folder | Current PS5 support |
| --- | --- | --- |
| TJA | `/data/Waddamburo/tja` | Put each song's `.tja` and referenced audio here, optionally in category/song subfolders; use Home mode |
| Nijiiro | `/data/Waddamburo/nijiiro` | Native G.719 decoding and console AES-256 asset decryption are included in v036 |
| osu! | `/data/Waddamburo/osu` | osu!lazer `client.realm` plus `files/`; matching Realm native backend included in v036 |

The game automatically writes `tja_folder`, `nijiiro_folder` and `osu_folder` to
`/data/Waddamburo/config.cfg`; these fixed console roots replace older configured
paths. Existing files in `/app0/custom_songs` are not moved: copy those songs to
`/data/Waddamburo/tja` when upgrading. `.osu`/`.osz` files are not scanned as loose charts by
the current osu!lazer provider. The v036 build includes the native G.719 and Realm backends. These paths are
still scanned as Home-mode libraries.

Options exits the startup error screen if required arcade assets cannot be loaded.

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
Selecting 120 FPS directly requests VideoOut's 120 Hz mode, even if the driver's
startup enumeration omitted it. An accepted request is retained without an idle
vblank timing veto. An actual VideoOut refusal falls back to 60 Hz. Package metadata
declares high-frame-rate support with `attribute3 = 262208` (`0x40040`); the build
checks this in both the source and generated `param.json`.
Exclusive Audio, Full Screen, VSync and Letterbox Size controls are omitted from
the console settings.

v036 enables the configured TaikOnline server through the console HTTP/TLS
services, including score/login requests and realtime WebSocket notifications.
It respects `server` and `server_insecure` in `config.cfg`; the normal default is
`https://taikonline.com/`, and an explicit empty `server =` keeps offline mode.
TLS verifies the certificate chain, hostname and validity dates by default using
the bundled Mozilla CA roots and a minimum TLS version of 1.2. The realtime
socket transport currently resolves IPv4 addresses. Automatic application updates
and optional upscaling remain disabled. MIDI and the desktop folder picker are
not exposed. Host checks and PS5 linking have passed for these new backends;
real console song libraries, HTTPS and notifications still need hardware testing. Include the build version and
relevant `[Waddamburo ...]` kernel-log lines when reporting a console crash.

## Nijiiro and osu! library layout (v036)

Keep the Nijiiro installation's original hierarchy below its preset root:

```text
/data/Waddamburo/nijiiro/
  Data/x64/
    datatable/musicinfo.json    # or the installation's packed .bin
    datatable/wordlist.json
    fumen/<song-id>/...
    sound/song_<song-id>.nus3bank
  Executable/Release/bnusio.dll # needed for encrypted installation data

/data/Waddamburo/osu/
  client.realm
  files/...
```

The loader reads the installation's own loader file as data; it does not execute
the Windows DLL. G.719 decoding runs in-process through vgmstream rather than a
desktop executable. For osu!, close lazer before copying a consistent database
and its content-addressed `files` store. The reader opens `client.realm` read-only;
the PS5 native Realm build omits database encryption. Loose `.osu`/`.osz` archives
still need an import implementation; this backend reads existing lazer libraries.

Nijiiro/Realm and network support are newly integrated in v036. The v034/v035
console test results do not validate those added features.

## Library root correction (v037)

All three custom libraries now live under the application home:
`/data/Waddamburo/tja`, `/data/Waddamburo/nijiiro`, `/data/Waddamburo/osu`.
Startup creates any missing directories, including a missing parent, and saves
the corrected paths into existing configurations. Existing files are preserved.
If you already copied songs to the old top-level `/data/tja`, `/data/nijiiro` or
`/data/osu` directories, move their contents into the corresponding directories
under `/data/Waddamburo`. The game does not relocate files automatically.

## Nijiiro diagnostics (v038)

The settings error distinguishes an unreadable DLL, a readable DLL without the
supported 64-hex key strings, and keys that fail to decrypt the current file.
The kernel log includes canonical paths, byte counts and FNV1a64 fingerprints
for the tables and loader, plus key-candidate counts; keys are never logged.
A file being present in a file manager does not establish that the game read it.
Compare these log values with the PC copy before attributing a failure to PS5
file access. This diagnostic update does not add another key source or establish
that every bnusio.dll contains usable decryption keys.

## User-supplied Nijiiro keys (v039)

`/data/Waddamburo/config.cfg` now accepts `nijiiro_datatable_key` and
`nijiiro_fumen_key`. Startup adds empty entries to old configurations. Enter each
key as exactly 64 hex characters, without quotes, and restart the game. Datatable
is used for both musicinfo and wordlist; fumen is used for charts. A configured
key takes priority over loader discovery for that file type. With both keys set,
bnusio.dll is not required for decryption. Empty values retain loader discovery.
Malformed values are rejected without echoing their contents. Configured keys
are redacted in settings diagnostics and retained when menu settings are saved.
No real keys are supplied in source, tests, templates or build artifacts.


## PS5 texture upscaling (v046)

In Settings, enable **Upscaled Textures**. Select **Background Upscaling** with
1 or 2 workers for live processing, or use **Bake All Textures** to prepare the
cache. The embedded Real-ESRGAN model runs on the CPU; the renderer remains
PS5_Vulkan. Pixel-art exclusions are retained. Caching is automatic under
`/data/Waddamburo/cache/upscaled/` (`textures.tar` and `used.txt`), with the same
hashes and tar format as the PC cache. No model download or config editing is
needed. Texture upscaling was confirmed working on PS5 with v046.


The v047 PS5 build enables AVX2/FMA CPU acceleration for the neural model. It
keeps the same model, quality, cache and worker limits. A small NativeAOT host
benchmark improved from 2478 ms to 41 ms with identical pixels; PS5 throughput
has not been measured. Existing cached textures do not need rebuilding.
