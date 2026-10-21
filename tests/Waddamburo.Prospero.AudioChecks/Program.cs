using System.Runtime.InteropServices;
using Waddamburo.Platform.Sdl.Media;

int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); ++checks; Console.WriteLine("PASS " + message); }
var root = Directory.CreateTempSubdirectory("ps5-audio-routing-").FullName;
AppContext.SetSwitch("Waddamburo.Prospero", true);
try
{
    string path = Path.Combine(root, "test.nus3bank"); File.WriteAllBytes(path, "NUS3synthetic"u8.ToArray());
    Mock.Reset(0);
    using (var file = File.OpenRead(path))
    {
        using (var decoder = new NativeAudioDecoder(file, 48000, 2, 2, leaveOpen:true))
        {
            Check(Mock.Files()==1 && Mock.Callbacks()==0, "PS5 bank streams use filename-aware native decoding");
            Check(Mock.Index()==2 && decoder.Info.SampleRate==48000 && decoder.Info.Channels==2, "bank route retains subsong and output format");
            Check(decoder.Read(new float[32])==16, "native samples reach the managed reader");
            decoder.Seek(0); decoder.Cancel();
        }
        Check(file.CanRead && Mock.Destroyed()==1, "leaveOpen retains provider stream and decoder is destroyed once");
    }
    Mock.Reset(0);
    var owned = File.OpenRead(path);
    using (var decoder = new NativeAudioDecoder(owned)) Check(owned.CanRead, "owned provider stream stays open until decoder disposal");
    Check(!owned.CanRead && Mock.Destroyed()==1, "owned stream and native decoder are released together");
    Mock.Reset(1);
    owned = File.OpenRead(path);
    try { using var ignored = new NativeAudioDecoder(owned); throw new Exception("Expected native rejection"); }
    catch (NotSupportedException) { Check(!owned.CanRead, "unsupported bank reports native rejection and cleans up without desktop fallback"); }
    Mock.Reset(0);
    using (var memory = new MemoryStream(new byte[8]))
    using (var decoder = new NativeAudioDecoder(memory,leaveOpen:true))
        Check(Mock.Callbacks()==1 && Mock.Files()==0, "non-file provider streams retain callback decoding");
    Mock.Reset(0);
    using (var file = File.OpenRead(path))
    {
        file.Position=1;
        using var decoder = new NativeAudioDecoder(file,leaveOpen:true);
        Check(Mock.Callbacks()==1 && Mock.Files()==0, "positioned file streams retain callback semantics");
    }
    VgmstreamCli.CacheDirectory = Path.Combine(root,"must-not-be-created");
    Check(VgmstreamCli.TryDecode(path,0) is null && !Directory.Exists(VgmstreamCli.CacheDirectory), "PS5 desktop fallback returns before cache/hash/process work");
    AppContext.SetSwitch("Waddamburo.Prospero", false);
    Mock.Reset(0);
    using (var file = File.OpenRead(path))
    using (var decoder = new NativeAudioDecoder(file,leaveOpen:true))
        Check(Mock.Callbacks()==1 && Mock.Files()==0, "desktop stream decoding behavior is preserved");
}
finally
{
    AppContext.SetSwitch("Waddamburo.Prospero", false);
    if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.Ordinal)) throw new Exception("Unexpected temp path");
    Directory.Delete(root,true);
}
Console.WriteLine($"{checks} audio routing/lifetime checks passed (host native-service model).");
static partial class Mock
{
    [LibraryImport("waddamburo_media",EntryPoint="mock_reset")] internal static partial void Reset(int failed);
    [LibraryImport("waddamburo_media",EntryPoint="mock_files")] internal static partial int Files();
    [LibraryImport("waddamburo_media",EntryPoint="mock_callbacks")] internal static partial int Callbacks();
    [LibraryImport("waddamburo_media",EntryPoint="mock_destroyed")] internal static partial int Destroyed();
    [LibraryImport("waddamburo_media",EntryPoint="mock_index")] internal static partial int Index();
}
