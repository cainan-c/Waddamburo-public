using System.Runtime.InteropServices;
using System.Text;
using SharpProspero.Application;
using SharpProspero.Graphics;
using SharpProspero.Interop.Pad;
using Waddamburo.App.Flow;
using Waddamburo.Catalog;
using Waddamburo.Game.Flow;
using Waddamburo.Game.Scores;
using Waddamburo.Platform.Prospero;
using Waddamburo.Platform.Sdl;
using Waddamburo.Platform.Sdl.Media;

namespace Waddamburo.App.Prospero;

internal static partial class Program
{
    [LibraryImport("waddamburo_ps5", EntryPoint = "waddamburo_ps5_init", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void InitializePlatform(string title);

    [LibraryImport("waddamburo_ps5", EntryPoint = "waddamburo_ps5_log", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void Log(string message);

    private sealed class KernelWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value) => Log(value.ToString());
        public override void Write(string? value) { if (value is not null) Log(value); }
        public override void WriteLine(string? value) => Log(value ?? "");
    }
    [LibraryImport("waddamburo_ps5", EntryPoint = "waddamburo_gc_memory_self_test")]
    private static partial int CheckGcMemory();

    [LibraryImport("waddamburo_ps5", EntryPoint = "waddamburo_runtime_memory_report")]
    private static partial void ReportRuntimeMemory();

    [LibraryImport("waddamburo_media", EntryPoint = "waddamburo_media_has_g719")]
    private static partial uint HasG719();

    private static void Main()
    {
        // Set writers before the Console getters try to open Linux standard handles.
        AppContext.SetSwitch("Waddamburo.Prospero", true);
        var log = new KernelWriter();
        Console.SetOut(log);
        Console.SetError(log);
        InitializePlatform("Waddamburo");
        AppContext.SetSwitch("Waddamburo.Prospero.G719", HasG719() != 0);
        Waddamburo.Game.Online.RealtimeConnection.PlatformConnectFactory = Waddamburo.Platform.Prospero.Native.ProsperoWebSocket.ConnectAsync;
        Waddamburo.Game.Scores.ScoreClient.PlatformHandlerFactory = static insecure => new Waddamburo.Platform.Prospero.Native.ProsperoHttpMessageHandler(insecure);
        Waddamburo.Providers.Stock.NijiiroCatalogProvider.PlatformDecryptor = Waddamburo.Platform.Prospero.Native.ProsperoAes256.DecryptCbc;
        ChartHash.SetSha256Provider(static bytes => SharpProspero.Security.Sha256.Hash(bytes));
        try
        {
            ReportRuntimeMemory();
            ProsperoThreadPool.Initialize();
            Log($"PS5 worker pool ready: {ProsperoThreadPool.Workers} warmed workers, growth capped; build v047.");
            Log($"CPU SIMD profile: float lanes={System.Numerics.Vector<float>.Count}, AVX2={System.Runtime.Intrinsics.X86.Avx2.IsSupported}, FMA={System.Runtime.Intrinsics.X86.Fma.IsSupported}.");
            var songFolders = ProsperoSongFolders.Console;
            songFolders.EnsureCreated();
            Log($"Song library folders ready: {songFolders.Tja}, {songFolders.Nijiiro}, {songFolders.Osu}.");
            // PS5 denies chdir for this application. Every console path is absolute.
            Log("Resolving arcade assets under /app0/Data");
            var assets = ProsperoAssetLayout.Resolve(ProsperoAssetLayout.ConsoleApplicationRoot);
            Log("Arcade asset layout resolved; opening state at " + assets.Home);
            Directory.CreateDirectory(assets.Home);
            int memoryCheck = CheckGcMemory();
            if (memoryCheck != 0)
                throw new InvalidOperationException($"PS5 GC memory self-test failed at stage {memoryCheck}.");
            Log("PS5 GC memory self-test passed: partial-page zeroing, neighboring data and mixed-page commit.");
            var config = Path.Combine(assets.Home, ArcadeSettings.FileName);
            ArcadeSettings Settings() => ProsperoDisplay.Normalize(songFolders.LoadSettings(config)) with
            {
                AutoUpdate = false,
                Fullscreen = true,
                Renderer = "vulkan",
                CacheFolder = Path.Combine(assets.Home, "cache"),
                UpscaleThreads = Math.Clamp(songFolders.LoadSettings(config).UpscaleThreads, 0, 2),
            };
            Environment.SetEnvironmentVariable("WADDAMBURO_RENDERER", "vulkan");
            VgmstreamCli.GameFolder = ProsperoAssetLayout.ConsoleApplicationRoot;
            VgmstreamCli.CacheDirectory = Path.Combine(assets.Home, "cache", "vgmstream");
            do
            {
                Log("Loading settings and accounts");
                var settings = Settings();
                Log(settings.Server is { } server
                    ? $"Online server: {server.GetLeftPart(UriPartial.Authority)}; home mode={settings.Home}."
                    : "Online server disabled by config (server is empty).");
                var controlsMarker = Path.Combine(assets.Home, "controller-layout-v2.txt");
                if (!File.Exists(controlsMarker))
                {
                    settings = ProsperoControls.Apply(settings);
                    ArcadeSettings.SaveMenuSettings(config, settings);
                    File.WriteAllText(controlsMarker, "Taiko controller layout applied.\n");
                    Log("PS5 drum controller layout applied and saved.");
                }
                Strings.Use(settings.Language);
                var accounts = AccountBook.Load(assets.Home);
                Log("Starting game shell");
                GameShell.Run(new GameOptions(
                    assets.Lumen, assets.Don, 1920, 1080, null, null, null,
                    SdlKeyboardTimeline.Empty, songFolders.Tja, assets.Font, null, assets.Sound,
                    Arcade: settings, ArcadePath: config, Accounts: accounts,
                    ScoresPath: Path.Combine(assets.Home, ScoreStore.FileName), Fullscreen: true));
            } while (GameShell.RestartRequested);
        }
        catch (Exception exception)
        {
            Log("Startup exception: " + exception);
            try
            {
                Directory.CreateDirectory(ProsperoAssetLayout.ConsoleHome);
                File.WriteAllText(Path.Combine(ProsperoAssetLayout.ConsoleHome, "boot-error.txt"), exception.ToString());
            }
            catch (Exception loggingError) { Log("Could not save boot-error.txt: " + loggingError.Message); }
            using var error = new BootError(exception.Message);
            error.Run();
        }
        ProcessExit.Exit();
    }
}

internal sealed class BootError(string message) : ProsperoApp
{
    protected override void OnFrame(FrameContext context)
    {
        var surface = context.Surface;
        surface.Clear(Color.FromRgb(24, 18, 24));
        surface.DrawTextCentered("WADDAMBURO - STARTUP ERROR", 100, 3, Color.White);
        surface.DrawText("Arcade assets: /app0/Data", 80, 200, 2, Color.White);
        var lines = message.Replace('\n', ' ').Chunk(100).Select(chars => new string(chars));
        int y = 300;
        foreach (var line in lines.Take(12))
        {
            surface.DrawText(line, 80, y, 2, Color.White);
            y += 40;
        }
        surface.DrawText("Copy USRDIR/data contents into Data beside eboot.bin.", 80, 900, 2, Color.White);
        surface.DrawText("Options: exit. Log: /data/Waddamburo/boot-error.txt", 80, 960, 2, Color.White);
        if (context.Pressed(ScePadButton.Options)) context.RequestExit();
    }
}
