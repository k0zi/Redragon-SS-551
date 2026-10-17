using SS551.Core.Model;

namespace SS551.Core;

/// <summary>Linux input event codes (linux/input-event-codes.h) and hotkey parsing.</summary>
public static class KeyCodes
{
    private static readonly Dictionary<string, ushort> Named = BuildTable();

    private static Dictionary<string, ushort> BuildTable()
    {
        var t = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["Esc"] = 1, ["Escape"] = 1,
            ["Minus"] = 12, ["-"] = 12, ["Equal"] = 13, ["="] = 13,
            ["Backspace"] = 14, ["Tab"] = 15,
            ["LeftBrace"] = 26, ["["] = 26, ["RightBrace"] = 27, ["]"] = 27,
            ["Enter"] = 28, ["Return"] = 28,
            ["Ctrl"] = 29, ["Control"] = 29, ["LeftCtrl"] = 29,
            ["Semicolon"] = 39, [";"] = 39, ["Apostrophe"] = 40, ["'"] = 40, ["Grave"] = 41, ["`"] = 41,
            ["Shift"] = 42, ["LeftShift"] = 42, ["Backslash"] = 43, ["\\"] = 43,
            ["Comma"] = 51, [","] = 51, ["Dot"] = 52, ["."] = 52, ["Slash"] = 53, ["/"] = 53,
            ["RightShift"] = 54, ["Alt"] = 56, ["LeftAlt"] = 56, ["Space"] = 57, ["CapsLock"] = 58,
            ["NumLock"] = 69, ["ScrollLock"] = 70,
            ["RightCtrl"] = 97, ["Print"] = 99, ["PrintScreen"] = 99, ["SysRq"] = 99,
            ["RightAlt"] = 100, ["AltGr"] = 100,
            ["Home"] = 102, ["Up"] = 103, ["PageUp"] = 104, ["Left"] = 105, ["Right"] = 106,
            ["End"] = 107, ["Down"] = 108, ["PageDown"] = 109, ["Insert"] = 110, ["Delete"] = 111, ["Del"] = 111,
            ["Mute"] = 113, ["VolumeDown"] = 114, ["VolumeUp"] = 115, ["Pause"] = 119,
            ["Super"] = 125, ["Meta"] = 125, ["Win"] = 125, ["LeftMeta"] = 125, ["RightMeta"] = 126, ["Menu"] = 139,
            ["NextSong"] = 163, ["PlayPause"] = 164, ["PreviousSong"] = 165, ["StopCD"] = 166,
        };
        // Number row 1..9 = 2..10, 0 = 11
        for (var i = 1; i <= 9; i++)
            t[i.ToString()] = (ushort)(1 + i);
        t["0"] = 11;
        // Letter rows follow the QWERTY scan-code layout.
        AddRow(t, "QWERTYUIOP", 16);
        AddRow(t, "ASDFGHJKL", 30);
        AddRow(t, "ZXCVBNM", 44);
        for (var i = 1; i <= 10; i++)
            t[$"F{i}"] = (ushort)(58 + i);
        t["F11"] = 87;
        t["F12"] = 88;
        for (var i = 13; i <= 24; i++)
            t[$"F{i}"] = (ushort)(183 + i - 13);
        return t;
    }

    private static void AddRow(Dictionary<string, ushort> t, string letters, ushort first)
    {
        for (var i = 0; i < letters.Length; i++)
            t[letters[i].ToString()] = (ushort)(first + i);
    }

    /// <summary>Parses "Ctrl+Shift+T" style text. Returns false if any part is unknown.</summary>
    public static bool TryParseHotkey(string text, out ushort[] codes)
    {
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        codes = new ushort[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!Named.TryGetValue(parts[i], out codes[i]))
                return false;
        }
        return parts.Length > 0;
    }

    public static ushort Media(MediaAction action) => action switch
    {
        MediaAction.PlayPause => 164,
        MediaAction.Next => 163,
        MediaAction.Previous => 165,
        MediaAction.Stop => 166,
        MediaAction.Mute => 113,
        MediaAction.VolumeUp => 115,
        MediaAction.VolumeDown => 114,
        MediaAction.MicMute => 248,
        _ => 0,
    };
}
