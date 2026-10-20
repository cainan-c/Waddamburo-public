using SharpProspero.Input;
using Keyboard = Waddamburo.Platform.Prospero.Native.NativeKeyboard;
using SharpProspero.Interop.Pad;
using SharpProspero.Interop.Keyboard;
using SharpProspero.Interop.Dialog;
using SharpProspero.Platform;
using Waddamburo.Platform.Sdl;

namespace Waddamburo.Platform.Prospero.Native;

internal readonly record struct NativeInputChange(SdlInput Input, bool Down, ulong Timestamp);

internal sealed class NativeInput : IDisposable
{
    private GamePadSet? _pads;
    private Keyboard? _keyboard;
    private TextInputDialog? _textDialog;
    private readonly HashSet<SdlInput> _held = [];
    private readonly Dictionary<int,int> _slots = [];
    private ulong _nextRefresh;
    private string _text = "";
    private bool _typing;
    private static readonly (ScePadButton Flag, NativeButton Code)[] Buttons =
    [
        (ScePadButton.Cross, NativeButton.BUTTON_SOUTH), (ScePadButton.Circle, NativeButton.BUTTON_EAST),
        (ScePadButton.Square, NativeButton.BUTTON_WEST), (ScePadButton.Triangle, NativeButton.BUTTON_NORTH),
        (ScePadButton.Options, NativeButton.BUTTON_START), (ScePadButton.TouchPad, NativeButton.BUTTON_BACK),
        (ScePadButton.L3, NativeButton.BUTTON_LEFT_STICK), (ScePadButton.R3, NativeButton.BUTTON_RIGHT_STICK),
        (ScePadButton.L1, NativeButton.BUTTON_LEFT_SHOULDER), (ScePadButton.R1, NativeButton.BUTTON_RIGHT_SHOULDER),
        (ScePadButton.Up, NativeButton.BUTTON_DPAD_UP), (ScePadButton.Down, NativeButton.BUTTON_DPAD_DOWN),
        (ScePadButton.Left, NativeButton.BUTTON_DPAD_LEFT), (ScePadButton.Right, NativeButton.BUTTON_DPAD_RIGHT),
    ];

    internal void Open()
    {
        _pads = GamePadSet.OpenForSignedInUsers(readUserNames: false);
        try { _keyboard = Keyboard.Open(); }
        catch (Exception error) { NativeHost.Log("USB keyboard unavailable: " + error.Message); }
    }

    internal IReadOnlyList<NativeInputChange> Poll()
    {
        var now = NativeHost.GetTicksNS();
        var current = new HashSet<SdlInput>();
        if (_pads is not null)
        {
            if (now >= _nextRefresh)
            {
                _pads.Refresh();
                _nextRefresh = now + 1_000_000_000;
                foreach (var user in _slots.Keys.ToArray())
                    if (_pads.ForUser(user) is null) _slots.Remove(user);
            }
            _pads.ReadAll();
            foreach (var player in _pads)
            {
                var state = player.State;
                if (!state.IsConnected) { _slots.Remove(player.UserId); continue; }
                if ((state.Buttons & ScePadButton.Intercepted) != 0) continue;
                if (!_slots.TryGetValue(player.UserId, out var slot))
                {
                    slot = Enumerable.Range(1,4).FirstOrDefault(n => !_slots.ContainsValue(n));
                    if (slot == 0) continue;
                    _slots.Add(player.UserId, slot);
                    NativeHost.Log($"Controller assigned to player {slot}.");
                }

                foreach (var (flag, code) in Buttons)
                    if (state.IsPressed(flag)) current.Add(new(SdlInputKind.PadButton, (int)code, slot));
                Trigger(current, slot, NativeAxis.AXIS_LEFT_TRIGGER, state.LeftTrigger);
                Trigger(current, slot, NativeAxis.AXIS_RIGHT_TRIGGER, state.RightTrigger);
            }
        }
        if (_keyboard is not null)
        {
            var state = _keyboard.Read();
            foreach (var key in state.Keys)
            {
                if (key == 0) continue;
                var input = new SdlInput(SdlInputKind.Key, key);
                current.Add(input);
                if (_typing && _textDialog is null && !_held.Contains(input))
                {
                    var shift = (state.Modifiers & (KeyModifier.LeftShift | KeyModifier.RightShift)) != 0;
                    _text += TypedCharacter(key, shift);
                }
            }
            if ((state.Modifiers & (KeyModifier.LeftShift | KeyModifier.RightShift)) != 0)
                current.Add(new(SdlInputKind.Key, (int)NativeScancode.KEY_LSHIFT));
        }
        if (_textDialog is not null && _textDialog.Update() == TextInputState.Finished)
        {
            if (_textDialog.EndStatus == ImeDialogEndStatus.Ok) _text += _textDialog.Text;
            _textDialog.Dispose(); _textDialog = null;
        }
        var changes = new List<NativeInputChange>();
        foreach (var input in _held) if (!current.Contains(input)) changes.Add(new(input, false, now));
        foreach (var input in current) if (!_held.Contains(input)) changes.Add(new(input, true, now));
        _held.Clear(); _held.UnionWith(current);
        return changes;
    }

    private void Trigger(HashSet<SdlInput> current, int slot, NativeAxis axis, byte value)
    {
        var input = new SdlInput(SdlInputKind.PadTrigger, (int)axis, slot);
        if (value >= (_held.Contains(input) ? 94 : 125)) current.Add(input);
    }

    internal void StartTextInput()
    {
        _typing = true; _text = "";
        try { _textDialog ??= TextInputDialog.Open("Waddamburo", maxLength: 256); }
        catch (Exception error) { NativeHost.Log("On-screen keyboard unavailable: " + error.Message); }
    }
    internal void StopTextInput() { _typing = false; _textDialog?.Dispose(); _textDialog = null; _text = ""; }
    internal string TakeText() { var value = _text; _text = ""; return value; }

    internal static SdlKeyboardKey KeyFromUsage(int code) => code switch
    {
        >=4 and <=29 => SdlKeyboardKey.A + code - 4,
        >=30 and <=38 => SdlKeyboardKey.Digit1 + code - 30,
        39 => SdlKeyboardKey.Digit0, 40 => SdlKeyboardKey.Enter, 41 => SdlKeyboardKey.Escape,
        42 => SdlKeyboardKey.Backspace, 43 => SdlKeyboardKey.Tab, 44 => SdlKeyboardKey.Space,
        75 => SdlKeyboardKey.PageUp, 76 => SdlKeyboardKey.Delete, 78 => SdlKeyboardKey.PageDown,
        79 => SdlKeyboardKey.Right, 80 => SdlKeyboardKey.Left, 81 => SdlKeyboardKey.Down, 82 => SdlKeyboardKey.Up,
        58 => SdlKeyboardKey.F1, 59 => SdlKeyboardKey.F2, 62 => SdlKeyboardKey.F5, 65 => SdlKeyboardKey.F8,
        225 or 229 => SdlKeyboardKey.Shift, _ => 0,
    };
    private static string TypedCharacter(int key, bool shift) => key switch
    {
        >=4 and <=29 => ((char)((shift ? 'A' : 'a') + key - 4)).ToString(),
        >=30 and <=38 => (shift ? "!@#$%^&*("[key-30] : (char)('1'+key-30)).ToString(),
        39 => shift ? ")" : "0", 44 => " ", 45 => shift ? "_" : "-", 46 => shift ? "+" : "=",
        54 => shift ? "<" : ",", 55 => shift ? ">" : ".", 56 => shift ? "?" : "/", _ => "",
    };
    public void Dispose() { StopTextInput(); _keyboard?.Dispose(); _pads?.Dispose(); }
}
