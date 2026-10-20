using Waddamburo.Game.Flow;

namespace Waddamburo.Platform.Prospero;

public static class ProsperoControls
{
    public static ArcadeSettings Apply(ArcadeSettings settings)
    {
        string[][] buttons = [["dpad_up", "dpad_left", "left_shoulder", "left_trigger"],
            ["dpad_right", "dpad_down", "left_stick"], ["west", "south", "right_stick"],
            ["north", "east", "right_shoulder", "right_trigger"]];
        var controls = settings.Controls;
        for (var index = 0; index < controls.Count; index++)
        {
            // Replace controller mappings; keep keyboard/MIDI inputs on their pads.
            var kept = controls[index].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Where(static token => !token.StartsWith("pad", StringComparison.OrdinalIgnoreCase));
            if (index < 8)
            {
                int player = index / 4 + 1;
                kept = kept.Concat(buttons[index % 4].Select(button => $"pad{player}:{button}"));
            }
            controls = controls.With(index, string.Join(", ", kept));
        }
        return settings with { Controls = controls, PadMenusAsDrum = false };
    }
}
