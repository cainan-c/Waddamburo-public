using Waddamburo.Game.Flow;

namespace Waddamburo.Platform.Prospero;

public static class ProsperoDisplay
{
    public static ArcadeSettings Normalize(ArcadeSettings settings) => settings with
    {
        FpsCap = settings.FpsCap == 120 ? 120 : 60,
        RefreshRate = settings.FpsCap == 120 ? 120 : 60,
        Vsync = true, Fullscreen = true, ExclusiveFullscreen = false,
        AudioExclusive = false, LetterboxSize = 100, LetterboxX = 50, LetterboxY = 50,
    };
}
