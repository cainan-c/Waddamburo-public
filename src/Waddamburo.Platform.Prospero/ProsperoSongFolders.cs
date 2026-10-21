using Waddamburo.Game.Flow;

namespace Waddamburo.Platform.Prospero;

/// <summary>Fixed writable song-library roots on the console's data mount.</summary>
public sealed record ProsperoSongFolders(string Tja, string Nijiiro, string Osu)
{
    public static ProsperoSongFolders Console { get; } = new("/data/Waddamburo/tja", "/data/Waddamburo/nijiiro", "/data/Waddamburo/osu");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Tja);
        Directory.CreateDirectory(Nijiiro);
        Directory.CreateDirectory(Osu);
    }

    /// <summary>Apply and persist the console paths while preserving unrelated settings.</summary>
    public ArcadeSettings LoadSettings(string configPath)
    {
        var settings = ArcadeSettings.LoadOrCreate(configPath);
        var preset = settings with { TjaFolder = Tja, NijiiroFolder = Nijiiro, OsuFolder = Osu };
        if (settings.TjaFolder != Tja || settings.NijiiroFolder != Nijiiro || settings.OsuFolder != Osu)
            ArcadeSettings.SaveMenuSettings(configPath, preset);
        return preset;
    }
}
