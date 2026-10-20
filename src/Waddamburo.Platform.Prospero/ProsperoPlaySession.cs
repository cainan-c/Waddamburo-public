using Waddamburo.Catalog;
using Waddamburo.Game.Gameplay;

namespace Waddamburo.Platform.Prospero;

/// <summary>A clock and judgement adapter shared by the native host and host checks.</summary>
public sealed class ProsperoPlaySession
{
    private readonly GameplayTimeline _timeline;
    private TimeSpan _anchor;
    private TimeSpan _position;
    private TimeSpan? _lastClock;
    private bool _started;

    public ProsperoPlaySession(PlayableChart chart, TaikoCourse course, TimeSpan leadIn)
    {
        ArgumentNullException.ThrowIfNull(chart);
        Chart = chart;
        _timeline = new GameplayTimeline(chart.AuthoredOffset, leadIn);
        Judgements = new TaikoJudgementSession(chart, TaikoJudgementWindows.ForCourse(course),
            TimeSpan.FromMilliseconds(50));
    }

    public PlayableChart Chart { get; }
    public TaikoJudgementSession Judgements { get; }
    public bool IsPaused { get; private set; }
    public TimeSpan ChartTime => _timeline.ChartTime(_position);
    public TimeSpan TransportPosition => _position;
    public TimeSpan AudioStart => _timeline.AudioStart;

    /// <summary>Advance with a monotonic host clock, independent of the frame rate.</summary>
    public void Update(TimeSpan clock)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(clock, TimeSpan.Zero);
        if (_lastClock is { } previous && clock < previous)
            throw new ArgumentOutOfRangeException(nameof(clock), "The host clock must not go backwards.");
        _lastClock = clock;
        if (!_started)
        {
            _started = true;
            _anchor = clock;
        }
        if (!IsPaused)
        {
            _position = clock - _anchor;
            Judgements.AdvanceTo(ChartTime);
        }
    }

    public void SetPaused(bool paused, TimeSpan clock)
    {
        Update(clock);
        if (paused == IsPaused)
            return;
        if (!paused)
            _anchor = clock - _position;
        IsPaused = paused;
    }

    /// <summary>Submit only button down edges; held buttons do not generate repeated strikes.</summary>
    public void Submit(uint current, uint previous, TimeSpan clock)
    {
        Update(clock);
        if (IsPaused)
            return;
        var edges = current & ~previous;
        if ((edges & ProsperoPad.LeftDon) != 0) Judgements.SubmitInput(TaikoInputAction.LeftDon, ChartTime);
        if ((edges & ProsperoPad.RightDon) != 0) Judgements.SubmitInput(TaikoInputAction.RightDon, ChartTime);
        if ((edges & ProsperoPad.LeftKa) != 0) Judgements.SubmitInput(TaikoInputAction.LeftKa, ChartTime);
        if ((edges & ProsperoPad.RightKa) != 0) Judgements.SubmitInput(TaikoInputAction.RightKa, ChartTime);
    }
}

/// <summary>Platform-neutral drum bits. The native host maps the console's buttons to these.</summary>
public static class ProsperoPad
{
    public const uint LeftDon = 1;
    public const uint RightDon = 2;
    public const uint LeftKa = 4;
    public const uint RightKa = 8;
}
