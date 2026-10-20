namespace Waddamburo.Platform.Prospero;

/// <summary>Read-only arcade assets beside the executable, with state on a writable mount.</summary>
public sealed record ProsperoAssetLayout(
    string Data, string Lumen, string Don, string Sound, string Font, string CustomSongs, string Home)
{
    public const string ConsoleApplicationRoot = "/app0";
    public const string ConsoleHome = "/data/Waddamburo";
    private static readonly string[] PreferredFonts = ["font.ttf", "font.otf", "font.ttc"];

    public static ProsperoAssetLayout Resolve(string applicationRoot, string home = ConsoleHome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        var root = Path.GetFullPath(applicationRoot);
        var data = Path.Combine(root, "Data");
        var lumen = Path.Combine(data, "lumendata", "packed");
        var don = Path.Combine(data, "don3d");
        var sound = Path.Combine(data, "sound");
        var fontRoot = Path.Combine(data, "font");
        foreach (var directory in new[] { data, lumen, don, sound, fontRoot })
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException($"Missing {directory}. Copy the contents of USRDIR/data into Data beside eboot.bin (no nested data folder).");
        var fonts = Directory.EnumerateFiles(fontRoot).Where(path =>
            Path.GetExtension(path).ToLowerInvariant() is ".ttf" or ".otf" or ".ttc")
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal).ToArray();
        var font = PreferredFonts
            .Select(name => Path.Combine(fontRoot, name)).FirstOrDefault(File.Exists)
            ?? fonts.FirstOrDefault()
            ?? throw new FileNotFoundException($"No TTF, OTF or TTC font in {fontRoot}.");
        return new(data, lumen, don, sound, font, Path.Combine(root, "custom_songs"), Path.GetFullPath(home));
    }
}
