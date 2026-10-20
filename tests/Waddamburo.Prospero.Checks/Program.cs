using Waddamburo.Catalog;
using Waddamburo.Platform.Prospero;

using Waddamburo.Platform.Sdl;

var checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    checks++;
    Console.WriteLine($"PASS {description}");
}
ProsperoPlaySession Session(TimeSpan? offset = null) => new(new PlayableChart(
    new ChartKey(new SongKey(SongSourceKind.Tja, "synthetic"), "oni"),
    offset ?? TimeSpan.Zero, TimeSpan.FromSeconds(3),
    [new PlayableHitObject(TimeSpan.FromSeconds(1), PlayableNoteKind.Don),
     new PlayableHitObject(TimeSpan.FromSeconds(1.5), PlayableNoteKind.Don)],
    [new ChartTimingPoint(TimeSpan.Zero, 120, 4, 4)],
    [new ChartScrollPoint(TimeSpan.Zero, 1)],
    [new ChartEffectPoint(TimeSpan.Zero, false)], []), TaikoCourse.Oni, TimeSpan.Zero);
TimeSpan T(double seconds) => TimeSpan.FromSeconds(seconds);

var play = Session();
play.Update(T(100));
play.Submit(ProsperoPad.LeftDon, 0, T(101));
Check(play.Judgements.IsJudged(0) && !play.Judgements.IsMissed(0), "native Don edge reaches the upstream judgement engine");
play.Submit(ProsperoPad.LeftDon, ProsperoPad.LeftDon, T(101.5));
Check(!play.Judgements.IsJudged(1), "held Don does not retrigger the next note");
play.Submit(0, ProsperoPad.LeftDon, T(101.51));
play.Submit(ProsperoPad.RightDon, 0, T(101.52));
Check(play.Judgements.IsJudged(1) && !play.Judgements.IsMissed(1), "release and right Don produce a fresh strike");

var paused = Session();
paused.Update(T(0));
paused.SetPaused(true, T(0.5));
paused.Submit(ProsperoPad.LeftDon, 0, T(15));
Check(paused.ChartTime == T(0.5) && !paused.Judgements.IsJudged(0), "pause freezes chart time and rejects input");
paused.SetPaused(false, T(20));
paused.Submit(ProsperoPad.LeftDon, 0, T(20.5));
Check(paused.ChartTime == T(1) && !paused.Judgements.IsMissed(0), "resume excludes the entire paused duration");
bool refused = false;
try { paused.Update(T(19)); } catch (ArgumentOutOfRangeException) { refused = true; }
Check(refused, "backwards clocks are rejected");

var offset = Session(T(-2));
offset.Update(T(40));
Check(offset.AudioStart == TimeSpan.Zero && offset.ChartTime == T(-2), "negative authored offset expands the pre-roll");
var missed = Session();
missed.Update(T(0));
missed.Update(T(3));
Check(missed.Judgements.IsComplete && missed.Judgements.IsMissed(0) && missed.Judgements.IsMissed(1),
    "large frame gaps time out unhit notes");
Check(ProsperoDiagnosticChart.Create().NoteCount == 32, "bundled exercise has 32 synthetic notes");
var temporary = Path.Combine(Path.GetTempPath(), "WaddamburoProspero-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
try
{
    bool rejected = false;
    try { ProsperoAssetLayout.Resolve(temporary, Path.Combine(temporary, "state")); }
    catch (DirectoryNotFoundException) { rejected = true; }
    Check(rejected, "missing packaged Data folder is reported");
    foreach (var folder in new[] { "lumendata/packed", "don3d", "sound", "font" })
        Directory.CreateDirectory(Path.Combine(temporary, "Data", folder));
    File.WriteAllBytes(Path.Combine(temporary, "Data", "font", "font.ttf"), []);
    var assets = ProsperoAssetLayout.Resolve(temporary, Path.Combine(temporary, "state"));
    Check(assets.Lumen == Path.Combine(temporary, "Data", "lumendata", "packed"), "Data directly contains lumendata, without a nested data folder");
    Check(assets.Font == Path.Combine(temporary, "Data", "font", "font.ttf"), "packaged title font resolves from Data/font");
    Check(!Directory.Exists(assets.Home), "resolving read-only assets does not create or move save files");
    var shaderRoot = Path.Combine(temporary, "shaders");
    var shaderCache = new Waddamburo.Platform.Prospero.Native.ShaderDiskCache(shaderRoot);
    byte[] shaderBytes = [4, 8, 15, 16, 23, 42];
    shaderCache.Write(17, shaderBytes);
    shaderCache = new(shaderRoot);
    Check(shaderCache.ReadSize(17) == shaderBytes.Length, "shader cache survives reopening");
    byte[] restored = new byte[shaderBytes.Length];
    Check(shaderCache.TryRead(17, restored) && restored.AsSpan().SequenceEqual(shaderBytes), "shader cache restores exact bytes");
    var cachedFile = Path.Combine(shaderRoot, "0000000000000011.bin");
    byte[] corrupted = File.ReadAllBytes(cachedFile); corrupted[^1] ^= 1; File.WriteAllBytes(cachedFile, corrupted);
    Check(!shaderCache.TryRead(17, restored), "corrupt shader cache payload is rejected");
    File.WriteAllBytes(cachedFile, [0, 1, 2]);
    Check(shaderCache.ReadSize(17) == 0, "interrupted shader cache writes become cache misses");
}
finally
{
    var safeRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (Path.GetFullPath(temporary).StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase))
        Directory.Delete(temporary, recursive: true);
}
var hashChart = ProsperoDiagnosticChart.Create();
var expectedHash = ChartHash.Compute(hashChart, TaikoCourse.Oni);
ChartHash.SetSha256Provider(static bytes => SharpProspero.Security.Sha256.Hash(bytes));
Check(ChartHash.Compute(hashChart, TaikoCourse.Oni) == expectedHash, "PS5 managed SHA-256 preserves the desktop chart identity");
ChartHash.SetSha256Provider(static bytes => System.Security.Cryptography.SHA256.HashData(bytes));
var settings = new Waddamburo.Game.Flow.ArcadeSettings();
var rejectedControls = new List<string>();
var nativeBindings = Waddamburo.Platform.Sdl.SdlInputBindings.Parse(settings.Controls, settings.Buttons, rejectedControls.Add);
Check(rejectedControls.Count == 0, "native controls accept the existing default configuration");
Check(Waddamburo.Platform.Sdl.SdlInput.TryParse("pad1:south", out var cross)
    && nativeBindings.Pad(cross) == Waddamburo.Platform.Sdl.SdlKeyboardKey.J,
    "native Cross button reaches right Don");
Check(Waddamburo.Platform.Sdl.SdlInput.TryParse("pad2:left_trigger", out var leftTrigger)
    && leftTrigger.Token == "pad2:left_trigger", "native trigger bindings round-trip");
Check(Waddamburo.Platform.Prospero.Native.NativeInput.KeyFromUsage(9) == Waddamburo.Platform.Sdl.SdlKeyboardKey.F,
    "native USB keyboard usage F reaches the engine's left Don key");
AppContext.SetSwitch("Waddamburo.Prospero", true);
Check(Waddamburo.Platform.Sdl.Media.VgmstreamCli.CacheDirectory == "/data/Waddamburo/cache/vgmstream",
    "PS5 audio cache does not perform desktop home discovery");
Check(Waddamburo.Game.Flow.ArcadeSettings.UserCacheFolder == "/data/Waddamburo/cache",
    "PS5 game cache stays on the writable data mount");
AppContext.SetSwitch("Waddamburo.Prospero", false);
if (Environment.GetEnvironmentVariable("WADDAMBURO_PREFETCH_TEST_ROOT") is { Length: > 0 } fixtureRoot)
{
    AppContext.SetSwitch("Waddamburo.Prospero", true);
    try
    {
        var fixtureMovies = Directory.EnumerateFiles(fixtureRoot, "*.ddp", SearchOption.AllDirectories)
            .Select(static path => new FileInfo(path)).Where(static file => file.Length is > 64000 and < 2000000)
            .OrderBy(static file => file.Length).Take(24)
            .SelectMany(file => Waddamburo.Formats.Ddp.DdpArchive.Open(File.ReadAllBytes(file.FullName)).Index.Movies
                .Where(static movie => movie.TextureEnd > movie.TextureBegin)
                .Select(movie => (ArchiveId: Path.GetRelativePath(fixtureRoot, file.FullName), MovieId: movie.Name)))
            .Distinct().Take(3).ToArray();
        Check(fixtureMovies.Length == 3, "three user-owned textured movie fixtures are available");
        var source = new Waddamburo.Game.Scenes.DirectoryLumenMovieContentSource(fixtureRoot);
        source.Prefetch(fixtureMovies);
        Check(SpinWait.SpinUntil(() => source.DecodedPrefetches.Count() == 2, TimeSpan.FromSeconds(15)), "console prefetch fills two upload slots");
        Thread.Sleep(150);
        Check(source.DecodedPrefetches.Count() == 2 && !source.PrefetchComplete, "prefetch applies backpressure until pixel uploads release buffers");
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!source.PrefetchComplete && DateTime.UtcNow < deadline)
        {
            foreach (var content in source.DecodedPrefetches)
            {
                content.ReleaseDecodedTexturePixels();
                source.NotifyUploaded(content);
            }
            Thread.Sleep(10);
        }
        Check(source.PrefetchComplete && source.DecodedPrefetches.Count() == 3, "draining GPU upload slots completes the queued movies");
        var loaded = source.LoadAsync(fixtureMovies[0].ArchiveId, fixtureMovies[0].MovieId, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        Check(source.DecodedPrefetches.Contains(loaded), "scene loading retains prefetched content for transition uploads");
        source.Forget(source.DecodedPrefetches.ToArray());
        Check(!source.DecodedPrefetches.Any(), "scene activation can release every completed prefetch reference");
        source.Prefetch([]);
    }
    finally { AppContext.SetSwitch("Waddamburo.Prospero", false); }
}
SdlInput.TryParse("pad1:south", out var ps5Cross);
SdlInput.TryParse("pad1:right_shoulder", out var r1);
var keyboardOnly = SdlInputBindings.Parse(["d", "f", "j", "k"], ["backspace"]);
Check(keyboardOnly.Pad(ps5Cross) == SdlKeyboardKey.J && keyboardOnly.Pad(r1) == SdlKeyboardKey.K,
    "keyboard-only configs gain PS5 Don/Ka bindings");
Check(keyboardOnly.Button(ps5Cross) == SdlKeyboardKey.Enter && SdlApplication.GamepadName(ps5Cross) == "Cross",
    "PS5 confirm binding and PlayStation button label are available");
var customPad = SdlInputBindings.Parse(["pad1:south", "", "", ""]);
Check(customPad.Pad(ps5Cross) == SdlKeyboardKey.D && customPad.Pad(r1) is null,
    "existing custom controller mappings are preserved");
var exactLayout = ProsperoControls.Apply(new Waddamburo.Game.Flow.ArcadeSettings());
var exactBindings = SdlInputBindings.Parse(exactLayout.Controls, exactLayout.Buttons);
string[][] expectedLayout = [["dpad_up", "dpad_left", "left_shoulder", "left_trigger"],
    ["dpad_right", "dpad_down", "left_stick"], ["west", "south", "right_stick"],
    ["north", "east", "right_shoulder", "right_trigger"]];
for (var player = 1; player <= 2; player++)
{
    bool correct = true;
    for (var pad = 0; pad < 4; pad++)
        foreach (var button in expectedLayout[pad])
            correct &= SdlInput.TryParse($"pad{player}:{button}", out var input)
                && exactBindings.Pad(input) == SdlInputBindings.Pads[(player - 1) * 4 + pad];
    Check(correct, $"player {player}: all 14 screenshot bindings match the requested drum layout");
}
Check(!exactLayout.PadMenusAsDrum && exactLayout.Controls[0].Contains("midi:31") && exactLayout.Controls[0].Contains("d,"),
    "controller profile uses gamepad menus and retains keyboard/MIDI bindings");
Check(ProsperoControls.Apply(exactLayout).Controls.Equals(exactLayout.Controls),
    "controller layout migration is idempotent");
var consoleDisplay = ProsperoDisplay.Normalize(new Waddamburo.Game.Flow.ArcadeSettings
    { FpsCap = 0, Vsync = false, Fullscreen = false, AudioExclusive = true, LetterboxSize = 70 });
Check(consoleDisplay.FpsCap == 60 && consoleDisplay.RefreshRate == 60 && consoleDisplay.Vsync
    && consoleDisplay.Fullscreen && !consoleDisplay.AudioExclusive && consoleDisplay.LetterboxSize == 100,
    "PS5 display defaults to 60 FPS, VSync and full-size fullscreen output");
var highDisplay = ProsperoDisplay.Normalize(consoleDisplay with { FpsCap = 120 });
Check(highDisplay.FpsCap == 120 && highDisplay.RefreshRate == 120 && highDisplay.Vsync,
    "120 FPS selects 120 Hz while retaining VSync");
Check(ProsperoDisplay.Normalize(consoleDisplay with { FpsCap = 144 }).FpsCap == 60,
    "old unlimited/unsupported frame-cap values normalize to the supported 60 FPS option");
Console.WriteLine($"{checks} checks passed. These are host checks, not console validation.");
