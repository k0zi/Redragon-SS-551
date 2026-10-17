namespace SS551.Native;

public enum DeckControl
{
    Key1, Key2, Key3, Key4, Key5, Key6,
    Button1, Button2, Button3,
    BigKnob, LeftKnob, RightKnob,
}

public enum DeckInputKind
{
    Pressed,
    Released,
    RotatedLeft,
    RotatedRight,
}

/// <summary>Event code ([9]) and state ([10]) of an "ACK..OK" input report.</summary>
public readonly record struct RawInput(byte Code, byte State);

/// <summary>A decoded input event from the deck.</summary>
public readonly record struct DeckInput(DeckControl Control, DeckInputKind Kind, byte RawCode)
{
    public bool IsKey => Control <= DeckControl.Key6;

    /// <summary>0-based index of a key (0..5), button (0..2) or knob (0..2).</summary>
    public int Index => Control switch
    {
        <= DeckControl.Key6 => Control - DeckControl.Key1,
        <= DeckControl.Button3 => Control - DeckControl.Button1,
        _ => Control - DeckControl.BigKnob,
    };

    /// <summary>
    /// Maps the event code byte ([9]) and state byte ([10]) of an
    /// "ACK..OK" report. See docs/protocol.md for the verified codes; the
    /// lower code of each knob pair is a counter-clockwise step.
    /// </summary>
    public static DeckInput? Decode(byte code, byte state)
    {
        var press = state != 0 ? DeckInputKind.Pressed : DeckInputKind.Released;
        return code switch
        {
            >= 0x01 and <= 0x06 => new DeckInput(DeckControl.Key1 + (code - 1), press, code),
            0x25 => new DeckInput(DeckControl.Button1, press, code),
            0x30 => new DeckInput(DeckControl.Button2, press, code),
            0x31 => new DeckInput(DeckControl.Button3, press, code),
            0x50 => new DeckInput(DeckControl.BigKnob, DeckInputKind.RotatedLeft, code),
            0x51 => new DeckInput(DeckControl.BigKnob, DeckInputKind.RotatedRight, code),
            0x90 => new DeckInput(DeckControl.LeftKnob, DeckInputKind.RotatedLeft, code),
            0x91 => new DeckInput(DeckControl.LeftKnob, DeckInputKind.RotatedRight, code),
            0x60 => new DeckInput(DeckControl.RightKnob, DeckInputKind.RotatedLeft, code),
            0x61 => new DeckInput(DeckControl.RightKnob, DeckInputKind.RotatedRight, code),
            // Knob pushes
            0x35 => new DeckInput(DeckControl.BigKnob, press, code),
            0x33 => new DeckInput(DeckControl.LeftKnob, press, code),
            0x34 => new DeckInput(DeckControl.RightKnob, press, code),
            _ => null,
        };
    }
}
