using Waddamburo.Catalog;

namespace Waddamburo.Platform.Prospero;

/// <summary>Synthetic input exercise; contains no original game assets or music.</summary>
public static class ProsperoDiagnosticChart
{
    public static PlayableChart Create() => new(
        new ChartKey(new SongKey(SongSourceKind.Tja, "prospero-diagnostic"), "oni"),
        TimeSpan.Zero,
        TimeSpan.FromSeconds(18),
        Enumerable.Range(0, 32).Select(index => new PlayableHitObject(
            TimeSpan.FromSeconds(1 + index * 0.5),
            index % 4 < 2 ? PlayableNoteKind.Don : PlayableNoteKind.Ka)),
        [new ChartTimingPoint(TimeSpan.Zero, 120, 4, 4)],
        [new ChartScrollPoint(TimeSpan.Zero, 1)],
        [new ChartEffectPoint(TimeSpan.Zero, false)],
        []);
}
