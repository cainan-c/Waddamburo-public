using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Waddamburo.Platform.Prospero.Native;

internal static partial class NativeHost
{
    private const string Library = "waddamburo_ps5";
    [LibraryImport(Library, EntryPoint = "waddamburo_ps5_display_size")]
    internal static partial int DisplaySize(out int width, out int height);
    [LibraryImport(Library, EntryPoint = "waddamburo_ps5_log", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void Log(string message);
    internal static void GetDisplaySize(out int width, out int height)
    {
        int result = DisplaySize(out width, out height);
        if (result < 0 || width <= 0 || height <= 0)
            throw new InvalidOperationException($"PS5 display discovery failed: {result}.");
    }
    [LibraryImport(Library, EntryPoint = "waddamburo_ps5_set_refresh_rate")]
    internal static partial int SetRefreshRate(int fps);

    internal static ulong GetTicksNS() => (ulong)((decimal)Stopwatch.GetTimestamp() * 1_000_000_000 / Stopwatch.Frequency);
}

internal static partial class NativeAudio
{
    private const string Library = "waddamburo_ps5";
    [LibraryImport(Library, EntryPoint = "waddamburo_audio_open")]
    internal static partial int Open(int frames);
    [LibraryImport(Library, EntryPoint = "waddamburo_audio_close")]
    internal static partial void Close();
    [LibraryImport(Library, EntryPoint = "waddamburo_audio_pause")]
    internal static partial void Pause(int paused);
    [LibraryImport(Library, EntryPoint = "waddamburo_audio_stream_create")]
    internal static partial nint Create();
    [LibraryImport(Library, EntryPoint = "waddamburo_audio_stream_destroy")]
    internal static partial void Destroy(nint stream);
    [LibraryImport(Library, EntryPoint = "waddamburo_audio_stream_queue")]
    internal static partial int Queue(nint stream, nint data, int bytes);
    [LibraryImport(Library, EntryPoint = "waddamburo_audio_stream_queued")]
    internal static partial int Queued(nint stream);
    [LibraryImport(Library, EntryPoint = "waddamburo_audio_stream_clear")]
    internal static partial void Clear(nint stream);
    [LibraryImport(Library, EntryPoint = "waddamburo_audio_stream_lock")]
    internal static partial void Lock(nint stream);
    [LibraryImport(Library, EntryPoint = "waddamburo_audio_stream_unlock")]
    internal static partial void Unlock(nint stream);
}
