using System.Collections.Immutable;
using System.Globalization;
using Waddamburo.Platform.Prospero.Native;

namespace Waddamburo.Platform.Sdl;

public enum SdlInputKind
{
    /// <summary>A key by its place on the keyboard (Code: the SDL scancode).</summary>
    Key,
    /// <summary>A controller button (Code: the SDL gamepad button).</summary>
    PadButton,
    /// <summary>A controller trigger pulled past its threshold (Code: the SDL gamepad axis).</summary>
    PadTrigger,
    /// <summary>A MIDI note-on from any device (Code: the note number).</summary>
    Midi,
}

/// <summary>
/// One physical input. <paramref name="Device"/>: a controller's number (1 = the first connected); 0
/// for keys and MIDI.
/// </summary>
public readonly record struct SdlInput(SdlInputKind Kind, int Code, int Device = 0)
{
    private const string ScancodePrefix = "KEY_", ButtonPrefix = "BUTTON_", AxisPrefix = "AXIS_";

    /// <summary>The input as config.cfg writes it: <c>d</c>, <c>pad1:dpad_left</c>, <c>midi:36</c>.</summary>
    public string Token => Kind switch
    {
        SdlInputKind.Key => name((NativeScancode)Code, ScancodePrefix),
        SdlInputKind.PadButton => $"pad{Device}:{name((NativeButton)Code, ButtonPrefix)}",
        SdlInputKind.PadTrigger => $"pad{Device}:{name((NativeAxis)Code, AxisPrefix)}",
        _ => string.Create(CultureInfo.InvariantCulture, $"midi:{Code}"),
    };

    public static bool TryParse(string token, out SdlInput input)
    {
        input = default;
        token = token.Trim();
        if (token.StartsWith("midi:", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(token.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var note) || note > 127)
                return false;
            input = new(SdlInputKind.Midi, note);
            return true;
        }
        if (token.StartsWith("pad", StringComparison.OrdinalIgnoreCase) && token.IndexOf(':') is var colon and > 3)
        {
            if (!int.TryParse(token.AsSpan(3, colon - 3), NumberStyles.None, CultureInfo.InvariantCulture, out var pad)
                || pad is < 1 or > SdlApplication.MaximumPads)
                return false;
            if (value<NativeButton>(ButtonPrefix, token[(colon + 1)..]) is { } button)
                input = new(SdlInputKind.PadButton, (int)button, pad);
            else if (value<NativeAxis>(AxisPrefix, token[(colon + 1)..])
                is { } axis and (NativeAxis.AXIS_LEFT_TRIGGER or NativeAxis.AXIS_RIGHT_TRIGGER))
                input = new(SdlInputKind.PadTrigger, (int)axis, pad);
            else
                return false;
            return true;
        }
        if (value<NativeScancode>(ScancodePrefix, token) is not { } scancode)
            return false;
        input = new(SdlInputKind.Key, (int)scancode);
        return true;
    }

    private static string name<T>(T value, string prefix) where T : struct, Enum =>
        value.ToString().Replace(prefix, "", StringComparison.Ordinal).ToLowerInvariant();

    // By name only: Enum.TryParse would also take a bare number.
    private static T? value<T>(string prefix, string text) where T : struct, Enum =>
        text.Length > 0 && !text.Contains(',') && Enum.TryParse<T>(prefix + text, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed) && parsed.ToString().StartsWith(prefix, StringComparison.Ordinal) ? parsed : null;
}

/// <summary>
/// Which physical inputs hit which drum pad. The pads keep the keys they always had as their ids
/// (<see cref="Pads"/>), so everything after the SDL boundary reads a bound input as that key; any
/// number of inputs can hit one pad.
/// </summary>
public sealed class SdlInputBindings
{
    /// <summary>The pads' ids: player 1's left ka, left don, right don, right ka, then player 2's.</summary>
    public static readonly ImmutableArray<SdlKeyboardKey> Pads =
    [
        SdlKeyboardKey.D, SdlKeyboardKey.F, SdlKeyboardKey.J, SdlKeyboardKey.K,
        SdlKeyboardKey.Z, SdlKeyboardKey.X, SdlKeyboardKey.C, SdlKeyboardKey.V,
    ];

    /// <summary>
    /// The buttons beside the drum (the game's menu keys: back, search, scores, practise, notices, restart,
    /// confirm), ids like the pads': a bound input reads as its button's key, and that key unbound is nothing.
    /// </summary>
    public static readonly ImmutableArray<SdlKeyboardKey> Buttons =
    [
        SdlKeyboardKey.Backspace, SdlKeyboardKey.Tab, SdlKeyboardKey.L, SdlKeyboardKey.P,
        SdlKeyboardKey.E, SdlKeyboardKey.Q, SdlKeyboardKey.Enter,
    ];

    /// <summary>Each pad on the key it is named after, each button on its own key.</summary>
    public static SdlInputBindings Keyboard { get; } = Parse([.. Pads.Select(static pad => pad.ToString())],
        ["backspace", "tab", "l", "p", "e", "q", "return"]);

    private readonly Dictionary<SdlInput, SdlKeyboardKey> _pads = [];
    private readonly Dictionary<SdlInput, SdlKeyboardKey> _buttons = [];

    /// <summary>
    /// Reads one comma-separated input list per pad, in <see cref="Pads"/> order. An input that is not
    /// understood is reported and skipped; one listed twice hits the first pad listing it.
    /// </summary>
    public static SdlInputBindings Parse(IReadOnlyList<string> pads, IReadOnlyList<string>? buttons = null, Action<string>? warn = null)
    {
        ArgumentNullException.ThrowIfNull(pads);
        var bindings = new SdlInputBindings();
        for (var button = 0; button < Math.Min(buttons?.Count ?? 0, Buttons.Length); button++)
            foreach (var token in buttons![button].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (SdlInput.TryParse(token, out var input))
                    bindings._buttons.TryAdd(input, Buttons[button]);
                else
                    warn?.Invoke($"unknown input '{token}'");
        for (var pad = 0; pad < Math.Min(pads.Count, Pads.Length); pad++)
            foreach (var token in pads[pad].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!SdlInput.TryParse(token, out var input))
                    warn?.Invoke($"unknown input '{token}'");
                else if (!bindings._pads.TryAdd(input, Pads[pad]))
                    warn?.Invoke($"input '{token}' is listed twice");
            }
        // Old desktop configs can contain only keyboard bindings. Keep those,
        // and supply controller defaults when no controller mapping exists.
        if (!bindings._pads.Keys.Any(static input => input.Kind is SdlInputKind.PadButton or SdlInputKind.PadTrigger))
        {
            for (var player = 1; player <= 2; player++)
            {
                string[][] defaults = [["dpad_up", "dpad_left", "left_shoulder", "left_trigger"],
                    ["dpad_right", "dpad_down", "left_stick"], ["west", "south", "right_stick"],
                    ["north", "east", "right_shoulder", "right_trigger"]];
                for (var pad = 0; pad < 4; pad++)
                    foreach (var token in defaults[pad])
                        if (SdlInput.TryParse($"pad{player}:{token}", out var input))
                            bindings._pads.TryAdd(input, Pads[(player - 1) * 4 + pad]);
            }
        }
        if (!bindings._buttons.Keys.Any(static input => input.Kind is SdlInputKind.PadButton or SdlInputKind.PadTrigger))
        {
            string[] defaults = ["east", "back", "north", "west", "left_shoulder", "right_shoulder", "south"];
            for (var button = 0; button < defaults.Length; button++)
                if (SdlInput.TryParse($"pad1:{defaults[button]}", out var input))
                    bindings._buttons.TryAdd(input, Buttons[button]);
        }
        return bindings;
    }

    /// <summary>The pad the input hits, or null when it is not bound.</summary>
    public SdlKeyboardKey? Pad(SdlInput input) => _pads.TryGetValue(input, out var pad) ? pad : null;

    /// <summary>The button the input presses (<see cref="Buttons"/>), or null when it is not bound to one.</summary>
    public SdlKeyboardKey? Button(SdlInput input) => _buttons.TryGetValue(input, out var button) ? button : null;
}
