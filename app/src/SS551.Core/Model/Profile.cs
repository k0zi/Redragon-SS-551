namespace SS551.Core.Model;

public enum ActionType
{
    None,
    Hotkey,
    Media,
    Command,
    Open,
    Page,
    Brightness,
    Delay,
    App,
    Backlight,
}

public enum MediaAction
{
    PlayPause,
    Next,
    Previous,
    Stop,
    Mute,
    VolumeUp,
    VolumeDown,
    MicMute,
}

/// <summary>Where a key's on/off state comes from.</summary>
public enum StateSource
{
    None,
    /// <summary>Flips on every short press.</summary>
    Toggle,
    /// <summary>Default audio output is muted (wpctl).</summary>
    SpeakerMuted,
    /// <summary>Default audio input is muted (wpctl).</summary>
    MicMuted,
    /// <summary>A shell command is polled; exit code 0 means on.</summary>
    Command,
}

/// <summary>Deck screen backlight: off blanks the LCD keys, the controls keep working.</summary>
public enum BacklightAction
{
    Toggle,
    Off,
    On,
}

public enum PageAction
{
    Next,
    Previous,
    GoTo,
}

/// <summary>One step of a macro.</summary>
public sealed class DeckAction
{
    public ActionType Type { get; set; }

    /// <summary>Hotkey: e.g. "Ctrl+Alt+T", "Super+E", "F5".</summary>
    public string Hotkey { get; set; } = "";

    public MediaAction Media { get; set; }

    /// <summary>Command: run with /bin/sh -c.</summary>
    public string Command { get; set; } = "";

    /// <summary>Open: URL, file or folder, passed to xdg-open.</summary>
    public string Target { get; set; } = "";

    public PageAction Page { get; set; }

    /// <summary>Page.GoTo: 0-based page index.</summary>
    public int PageIndex { get; set; }

    /// <summary>Brightness: change in percent, e.g. +10 or -10.</summary>
    public int BrightnessStep { get; set; } = 10;

    public BacklightAction Backlight { get; set; }

    /// <summary>Delay: wait before the next step, in milliseconds.</summary>
    public int DelayMs { get; set; } = 500;

    /// <summary>App: path of the application's .desktop file.</summary>
    public string AppDesktopFile { get; set; } = "";

    /// <summary>App: display name, for the editor.</summary>
    public string AppName { get; set; } = "";
}

/// <summary>A long-press stage: fires after the control was held this long.</summary>
public sealed class HoldStage
{
    /// <summary>
    /// Hold time in seconds, counted from the previous stage (or from the
    /// press for the first stage), so stages accumulate: 2 + 2 fires at 4 s.
    /// </summary>
    public double Seconds { get; set; } = 1;

    public List<DeckAction> Actions { get; set; } = [];
}

/// <summary>What a pressable control (key, button, knob push) does.</summary>
public sealed class PressConfig
{
    /// <summary>
    /// Short press. Runs on press when there are no hold stages; otherwise on
    /// release, and only if no hold stage was reached.
    /// </summary>
    public List<DeckAction> Actions { get; set; } = [];

    public List<HoldStage> HoldStages { get; set; } = [];
}

/// <summary>What a key face shows.</summary>
public sealed class KeyAppearance
{
    public string Title { get; set; } = "";
    public string? IconPath { get; set; }
    public string BackgroundColor { get; set; } = "#1E1E1E";
    public string TextColor { get; set; } = "#FFFFFF";

    public KeyAppearance Clone() => (KeyAppearance)MemberwiseClone();
}

/// <summary>Turns a key into a state indicator with a second look (and optionally actions) when on.</summary>
public sealed class StateConfig
{
    public StateSource Source { get; set; }

    /// <summary>Command source: run with /bin/sh -c; exit code 0 = on.</summary>
    public string Command { get; set; } = "";

    /// <summary>Command source: how often to run the command.</summary>
    public double IntervalSeconds { get; set; } = 2;

    public KeyAppearance On { get; set; } = new() { BackgroundColor = "#B71C1C" };

    /// <summary>When true, pressing the key while on runs <see cref="OnPress"/> instead of the normal press.</summary>
    public bool SeparateOnPress { get; set; }

    public PressConfig OnPress { get; set; } = new();
}

public sealed class KeyConfig
{
    public KeyAppearance Look { get; set; } = new();
    public PressConfig Press { get; set; } = new();
    public StateConfig State { get; set; } = new();

    public bool HasState => State.Source != StateSource.None;

    /// <summary>The look for the given state; the normal look when the key has no state.</summary>
    public KeyAppearance Appearance(bool on) => on && HasState ? State.On : Look;

    public PressConfig PressFor(bool on) => on && HasState && State.SeparateOnPress ? State.OnPress : Press;
}

public sealed class DeckPage
{
    public string Name { get; set; } = "Page";
    public List<KeyConfig> Keys { get; set; } = [];

    public static DeckPage Create(string name) => new()
    {
        Name = name,
        Keys = [.. Enumerable.Range(0, Profile.KeyCount).Select(_ => new KeyConfig())],
    };
}

public sealed class KnobConfig
{
    /// <summary>Runs once per counter-clockwise step.</summary>
    public List<DeckAction> Left { get; set; } = [];

    /// <summary>Runs once per clockwise step.</summary>
    public List<DeckAction> Right { get; set; } = [];

    public PressConfig Press { get; set; } = new();
}

public sealed class Profile
{
    public const int CurrentVersion = 3;
    public const int KeyCount = 6;
    public const int ButtonCount = 3;
    public const int KnobCount = 3;

    public int Version { get; set; } = CurrentVersion;
    public int Brightness { get; set; } = 70;
    public List<DeckPage> Pages { get; set; } = [];

    /// <summary>The 3 physical buttons below the LCD keys; shared by all pages.</summary>
    public List<PressConfig> Buttons { get; set; } = [];

    /// <summary>Big, left small, right small knob; shared by all pages.</summary>
    public List<KnobConfig> Knobs { get; set; } = [];

    /// <summary>Pads missing entries so every list has the expected length.</summary>
    public Profile Normalize()
    {
        if (Pages.Count == 0)
            Pages.Add(DeckPage.Create("Page 1"));
        foreach (var page in Pages)
            Pad(page.Keys, KeyCount);
        Pad(Buttons, ButtonCount);
        Pad(Knobs, KnobCount);
        Brightness = Math.Clamp(Brightness, 0, 100);
        Version = CurrentVersion;
        return this;
    }

    private static void Pad<T>(List<T> list, int count) where T : new()
    {
        while (list.Count < count)
            list.Add(new T());
        if (list.Count > count)
            list.RemoveRange(count, list.Count - count);
    }

    public static Profile CreateDefault()
    {
        var p = new Profile().Normalize();
        var keys = p.Pages[0].Keys;
        keys[0] = Key("Play", "#1B5E20", new() { Type = ActionType.Media, Media = MediaAction.PlayPause });
        keys[1] = Key("Prev", "#0D47A1", new() { Type = ActionType.Media, Media = MediaAction.Previous });
        keys[2] = Key("Next", "#0D47A1", new() { Type = ActionType.Media, Media = MediaAction.Next });
        keys[3] = Key("Mute", "#B71C1C", new() { Type = ActionType.Media, Media = MediaAction.Mute });
        keys[4] = Key("Terminal", "#333333", new() { Type = ActionType.Hotkey, Hotkey = "Ctrl+Alt+T" });
        keys[5] = Key("Files", "#E65100", new() { Type = ActionType.Open, Target = "~" });
        p.Knobs[0].Left = [new() { Type = ActionType.Media, Media = MediaAction.VolumeDown }];
        p.Knobs[0].Right = [new() { Type = ActionType.Media, Media = MediaAction.VolumeUp }];
        p.Knobs[0].Press.Actions = [new() { Type = ActionType.Media, Media = MediaAction.Mute }];
        p.Knobs[1].Left = [new() { Type = ActionType.Brightness, BrightnessStep = -10 }];
        p.Knobs[1].Right = [new() { Type = ActionType.Brightness, BrightnessStep = 10 }];
        p.Knobs[2].Left = [new() { Type = ActionType.Page, Page = PageAction.Previous }];
        p.Knobs[2].Right = [new() { Type = ActionType.Page, Page = PageAction.Next }];
        return p;

        static KeyConfig Key(string title, string color, DeckAction action) => new()
        {
            Look = new KeyAppearance { Title = title, BackgroundColor = color },
            Press = new PressConfig { Actions = [action] },
        };
    }
}
