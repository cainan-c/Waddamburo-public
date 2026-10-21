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

## Explicit display selection (00.034.000)

Selecting 120 FPS calls `sceVideoOutConfigureOutput` with output mode 15 on the
existing driver-owned handle. The driver's cached startup mode enumeration and
idle vblank timings no longer veto this explicit request. A successful call is
treated as an accepted mode request, not proof of a measured HDMI refresh rate.
Only actual configuration/pacing errors trigger fallback or rollback, with their
return codes logged. Selecting 60 FPS restores output mode 1. VSync remains on.

`attribute3` stays `262208` (`0x40040`), including the driver's high-frame-rate
declaration bit `0x40`, without the alternate `0x80000` declaration. `build.ps1`
checks this metadata before compiling and after gathering the module folder.
Run `bash native/prospero/tests/run-display.sh` in the configured WSL build host
for transition, direct-request, refusal, rollback and metadata regression checks.
The explicit 120 Hz switch in v034 was confirmed working by the tester on a
PS5 Pro running firmware 13.60, as well as the earlier base PS5 on 11.60.

## SSD song-library presets (00.035.000)

`ProsperoSongFolders.Console` owns `/data/Waddamburo/tja`, `/data/Waddamburo/nijiiro` and `/data/Waddamburo/osu`.
The entry point creates all three before resolving packaged arcade assets, then
automatically applies and saves these paths to new or existing configurations.
Other configuration values are preserved and an unchanged config is not rewritten.
Existing packaged `/app0/custom_songs` files are not automatically moved; users
upgrading should copy those songs to `/data/Waddamburo/tja`. Custom libraries remain Home-mode
sources in the shared game catalog.

v036 adds the native backends for these presets, described below.

## G.719, Realm and online services (00.036.000)

The PS5 vgmstream build enables `USE_G719` and builds the separately linked
`libg719_decode` dependency at upstream revision
`da90ad8a676876c6c47889bcea6a753f9bbf7a73`. The native media capability query
admits Nijiiro libraries without looking for a desktop CLI. EOF seeks reset the
decoder before seeking. Encrypted Nijiiro tables/charts use the console
AES-256-CBC/PKCS7 implementation, with AES-NI acceleration and a portable fallback;
it is independent of Linux OpenSSL. The desktop decryptor is unchanged.

`realm-dotnet` is pinned to its 20.1.0 release,
`a4133ff59429949d30c6a7d184768c699bda5655`, and its recorded Core submodule
`3aba889bd9c93c91ea7906b9ec4b60a5c5dcab45`. `build-realm.sh` builds static PS5
wrapper/Core/ObjectStore/QueryParser libraries beside the native build directory.
The wrapper has matching C exports for the existing managed Realm package and
is included through `DirectPInvoke`. The port enables SDK BSD declarations and
the platform directory scanner, keeps C++ exceptions/RTTI, and disables database
encryption (the imported lazer library is read-only and normally unencrypted).

Native archive extraction now runs through an ELF partial link with the AOT
imports as roots. This resolves C++ COMDAT functions and their FDE/LSDA exception
records together, including the duplicate throwing/nonthrowing STL instantiations
introduced by Realm. The native and CLR libc/unwind symbol domains remain separate.
RADV is whole-archived during this one partial link to retain weak dispatch tables.

Online clients install `ProsperoHttpMessageHandler` in place of the desktop
handler. It loads PS5 Net/SSL/HTTP services, uses cancellation/timeout bounds,
limits request/response bodies to 32 MiB, and releases requests/contexts on error.
It does not silently follow redirects or forward bearer tokens to another host.
TLS chain/hostname/date checks remain on unless the user's existing explicit
`server_insecure` setting requests otherwise. A vendored Mozilla CA extract is
embedded, parsed into DER and imported into system TLS contexts. The SDK's
high-level accept-any-certificate client is not used.

Realtime notifications use the PS5 TCP/TLS stream and an RFC 6455 WebSocket
implementation, including upgrade validation, client masking with PS5 hardware
entropy, fragmentation, ping/pong, close and frame-size limits. This path currently
uses IPv4 DNS. Native TLS contexts outlive any blocked read/write during disposal.
The console no longer overwrites `Server` with null; configured/default endpoints
are honored, while `server =` remains an offline opt-out. Automatic updates stay off.

Additional host checks (run after dependencies have been fetched):

```bash
bash tests/Waddamburo.Prospero.G719Checks/run.sh
bash tests/Waddamburo.Prospero.RealmChecks/run.sh
bash tests/Waddamburo.Prospero.NetworkChecks/run.sh
```

The G.719 test decodes a synthetic zero-codeword BNSF/IS22 fixture and checks
bounded stereo float output plus post-EOF seek recovery; it is not an audio
conformance vector. Realm is tested through real .NET NativeAOT with a synthetic
lazer-shaped database and a check that its files remain unchanged. Network policy
and WebSocket protocol tests use host models of PS5 services, including certificate
rejection, cancellation, resource cleanup, limits and frame handling. AES checks
cover a NIST vector, both implementation paths and invalid PKCS7 padding.
These host tests cannot establish on-console TLS/service behavior or compatibility
with every real installation/database; report those separately after hardware tests.


## CLR compression isolation (00.040.000)

`prepare-compression.py` stages the selected .NET runtime pack's compression
bridge and zlib with every global zlib definition/reference renamed into the
`waddamburo_clr_zlib_` domain. The PS5 project removes the original `libz.a` and
`libSystem.IO.Compression.Native.a` from runtime inputs after pack resolution.
The engine manifest supplies `clr-compression.a` and `clr-zlib.a` instead. This
keeps CLR inflation state/helpers matched and avoids native engine interposition.

Run `bash tests/Waddamburo.Prospero.CompressionChecks/run.sh` on the configured
WSL host. It demonstrates an injected competing initializer returning
Z_STREAM_ERROR before isolation, then decodes gzip/zlib/raw fixtures through
the isolated real CLR PAL with small output buffers. This is a host collision
regression check; PS5 validation remains separate.


## Native bank stream routing (00.041.000)

PS5 provider FileStreams at position zero for nus3bank/nus3audio use the native
file decoder so vgmstream receives the filename and bank parser. The generic
callback entry point only probes FFmpeg. Native decoder ownership moves to the
outer stream decoder; the provider stream is retained until disposal unless
leaveOpen is set. Other/positioned streams retain callback behavior.

VgmstreamCli.TryDecode immediately returns null in console mode, before its
desktop SHA256 cache hash, Guid generation, cache writes and process invocation.
This removes the OpenSSL abort observed during preview startup in v040.
Run tests/Waddamburo.Prospero.AudioChecks/run.sh for host routing/lifetime checks.
The vgmstream-file-check.c helper accepts a user-supplied bank path and never
writes decoded music; no real bank belongs in the source or update archive.


## TJA fingerprint hashing (00.042.000)

TJA discovery and chart reload use Catalog.Sha256Hash, with the console SHA-256
provider registered before workers start. Previously these file fingerprints
called the Linux runtime SHA-256 provider and aborted in OpenSSL initialization
as soon as a TJA file was scanned. ChartHash.SetSha256Provider also registers
this shared provider. Desktop defaults and serialized chart identities are
unchanged. Cabinet IDs and avatar cache keys use the same platform provider.

Prospero.Checks exercises UTF-8/Shift-JIS TJA discovery and reload with the real
console implementation, comparing desktop digests and checking changed-file
rejection. All 45 checks pass on the host. The TJA provider suite passes 34 of
35 tests; DelayCheckSampleLoadsWithEveryNoteWhereItsCommentSays requires the
missing samples/delay-check TJA fixture (this checkout has only audio/README).


## Login diagnostics (00.043.000)

HttpClient timeouts are distinct from the player cancelling login/pairing: the
former now clears the pending operation and displays a retryable failure.
Certificate-import IOException and malformed JSON replies also reach the login
screen instead of silently faulting its background task. The PS5 HTTP handler
normalizes transport/CA IO failures to HttpRequestException for all its callers.

PuTTY output includes HTTP start/end, elapsed time, native stages and status.
Send failures include sceHttpGetLastErrno and sceHttpsGetSslError values. Logs
include method and origin only, without request paths/queries, headers or bodies.
Startup reports the configured server origin or explicit offline mode. Strict CA,
hostname and validity verification remain enabled by default. See v045 below
for the current native protocol negotiation policy.

The existing v042 capture has no login transport error, so v043 requires another
console login attempt to locate the underlying connection problem. Public health
and realtime discovery answered HTTP 200 from the development PC; that is not
evidence of console connectivity. Do not mark server login fixed until tested.

Run tests/Waddamburo.Prospero.NetworkChecks/run.sh for the 24 native service-model
checks and dotnet run --project tests/Waddamburo.Prospero.LoginChecks -c Release
for six actual login-flow checks using synthetic responses (no real login).


## Native CA import format (00.044.000)

The v043 console log isolated login, health and notice failures to CA import,
returning 0x8095f005 before sending a request. The port supplied raw DER records.
The native homebrew import path uses complete PEM records with CRLF framing:
https://github.com/e1z0/PS4-Games-Installer/blob/main/src/http.cpp
https://github.com/e1z0/PS4-Games-Installer/blob/main/src/https_trust.hpp
These references informed the format/lifetime correction; no source was copied.

ProsperoTrustRoots validates the embedded Base64 payloads and constructs complete
PEM records. It allocates a single unmanaged block containing the descriptor
array, pointer array and NUL-padded PEM bytes (NUL excluded from declared sizes).
Both sceHttpsLoadCert and sceSslLoadCert receive all 118 roots in one call. The
HTTP/WebSocket owner retains the block until native context teardown, including
partial-import failures. No raw stack pointers or movable managed buffers remain
registered after import. Existing CA/hostname/validity checks stay enabled.

The native service model now rejects DER and checks complete PEM framing and
retained pointer validity at send time. Independent host X509 parsing validates
every actual PEM record after a forced GC on both import routes. All 27 network
checks pass alongside 45 PS5 host checks. These are not native console tests;
another PS5 login attempt is required to confirm import and server connectivity.


## TLS selector compatibility (00.045.000)

Hardware v044 now imports all 118 CA roots successfully. Send then returns
0x8095f007, also reported by sceHttpGetLastErrno; TLS/verification details are
both zero. The failure is after trust import and before an HTTP response.

v045 removes the unverified SDK ordinal SceSslVersion.Tls12 override from both
HTTP and WebSocket TLS setup. The firmware negotiates its default protocols;
the port no longer claims that an explicit client TLS 1.2 floor is enforced.
CA, hostname, certificate validity and chain verification remain enabled by
default. No certificate-accepting callback, insecure retry or HTTP downgrade is
added. This is a targeted compatibility change, not hardware proof that the
version selector alone caused the observed invalid-value error.

Thirty network host checks pass, including actual ProsperoNetworkStream setup
against fake DNS/socket/TLS services, default protocol selection, full CA/name
verification, resource cleanup and reporting of the captured send error. The
previous model incorrectly asserted ordinal 5 as a verified protocol value;
that expectation is removed. PS5 login/handshake still requires console testing.


## CPU texture upscaler (00.046.000)

The user confirmed server login works with v045. The next captured crash was a
worker abort in InitializeOpenSSLShim -> SHA256 implementation constructor ->
UpscaleTool.hash -> TextureBake.run. Texture hashing now uses SharpProspero's
incremental SHA-256 in the PS5 target. Width/height/pixel order, uppercase hash
prefix and .bc7 keys are preserved so PC caches remain compatible.

The embedded Real-ESRGAN compact CPU model and native BC7 encoder are reused;
this is not a Vulkan neural-inference backend. Rendering still uses PS5_Vulkan.
On PS5, UpscaleCache uses ordinary file reads rather than memory mapping for
both new and reopened tar caches. All output stays below
/data/Waddamburo/cache/upscaled (textures.tar and used.txt).

Background Upscaling is no longer forcibly reset to zero at startup. The menu
and startup enforce Off/1/2 workers. Bake All Textures processes one source at
a time with at most two tile workers, without raising the fixed runtime worker
budget. PS5 tiles are 64 pixels rather than 256, reducing scratch planes. Live
and bake inference share a lock. Live work yields on pause and shutdown; baking
supports cancellation between tiles/layers, and shutdown waits before disposing
the shared model/cache. Cached originals remain usable if a texture fails.

Settings: turn Upscaled Textures on to display cached results. Choose Background
Upscaling (1 or 2) for live work, or run Bake All Textures to precompute the known
game textures. Baking is CPU-intensive; console throughput and a full bake remain
hardware acceptance checks. Existing pixel-art exclusions remain in effect.

Run tests/Waddamburo.Prospero.UpscaleChecks/run.sh. Twelve checks use the actual
embedded model and native BC7 implementation: hash identity, recorded movies,
3x inference, BC7 decoding, display-sized variants, alpha, cache reopen without
mapping, standard tar compatibility, 64-pixel tile seams versus desktop and early
cancellation, plus desktop pause/bake compatibility. All pass on the host
alongside 45 existing PS5 checks. The user subsequently confirmed v046 texture
upscaling works on PS5. Full-library bake coverage and throughput were not measured.


## NativeAOT CPU acceleration (00.047.000)

The v046 convolution disassembly contained scalar fmaf calls for every vector
FusedMultiplyAdd because NativeAOT targeted baseline x64 (four float lanes,
FMA unavailable). The PS5 app now sets IlcInstructionSet=avx2; the installed
.NET 10.0.5 compiler groups FMA with AVX2 and rejects the older separate fma
instruction-set spelling. Size optimization remains unchanged. Desktop build
settings, model, 64-pixel tile size, two-worker cap and cache keys are unchanged.

tests/Waddamburo.Prospero.UpscaleBenchmarks/run.sh builds the real model twice
with NativeAOT and identical size optimization: baseline and PS5 CPU profiles.
Three 8x8-input runs on the development host measured medians of 2477.53 ms and
40.83 ms (about 60.7x), with byte-identical 24x24 RGBA output. This is a small host
kernel benchmark, not console timing or full bake throughput. No GPU inference
is added. The PS5 bootstrap log reports float lanes/AVX2/FMA to identify its
compiled profile. Console stability and end-to-end speed still need testing.

Compiler option reference:
https://github.com/dotnet/runtime/blob/main/src/coreclr/nativeaot/docs/optimizing.md
Use the installed compiler help as authority for supported ISA group spellings.
