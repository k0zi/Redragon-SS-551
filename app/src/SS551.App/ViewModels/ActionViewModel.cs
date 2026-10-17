using SS551.Core;
using SS551.Core.Model;

namespace SS551.App.ViewModels;

/// <summary>Edits one <see cref="DeckAction"/> in place and reports every change.</summary>
public sealed class ActionViewModel(DeckAction model, Action changed) : ViewModelBase
{
    public DeckAction Model => model;

    public static ActionType[] ActionTypes { get; } = Enum.GetValues<ActionType>();
    public static MediaAction[] MediaActions { get; } = Enum.GetValues<MediaAction>();
    public static PageAction[] PageActions { get; } = Enum.GetValues<PageAction>();

    public ActionType Type
    {
        get => model.Type;
        set
        {
            if (Set(model.Type, value, v => model.Type = v))
            {
                OnPropertyChanged(nameof(IsHotkey));
                OnPropertyChanged(nameof(IsMedia));
                OnPropertyChanged(nameof(IsCommand));
                OnPropertyChanged(nameof(IsOpen));
                OnPropertyChanged(nameof(IsPage));
                OnPropertyChanged(nameof(IsBrightness));
                OnPropertyChanged(nameof(IsDelay));
                OnPropertyChanged(nameof(IsApp));
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public bool IsHotkey => Type == ActionType.Hotkey;
    public bool IsMedia => Type == ActionType.Media;
    public bool IsCommand => Type == ActionType.Command;
    public bool IsOpen => Type == ActionType.Open;
    public bool IsPage => Type == ActionType.Page;
    public bool IsBrightness => Type == ActionType.Brightness;
    public bool IsDelay => Type == ActionType.Delay;
    public bool IsApp => Type == ActionType.App;

    public string AppName => string.IsNullOrEmpty(model.AppName) ? "No application chosen" : model.AppName;

    /// <summary>Makes this step launch the given application.</summary>
    public void SetApp(DesktopApp app)
    {
        model.AppDesktopFile = app.DesktopFile;
        model.AppName = app.Name;
        OnPropertyChanged(nameof(AppName));
        Type = ActionType.App;
        OnPropertyChanged(nameof(Summary));
        changed();
    }

    private int _number;

    /// <summary>1-based position in the macro, maintained by <see cref="MacroViewModel"/>.</summary>
    public int Number
    {
        get => _number;
        set => SetProperty(ref _number, value);
    }

    public string Hotkey
    {
        get => model.Hotkey;
        set
        {
            if (Set(model.Hotkey, value, v => model.Hotkey = v))
            {
                OnPropertyChanged(nameof(HotkeyError));
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public string? HotkeyError =>
        string.IsNullOrWhiteSpace(Hotkey) || KeyCodes.TryParseHotkey(Hotkey, out _)
            ? null
            : "Unknown key name. Examples: Ctrl+Alt+T, Super+E, Shift+F5";

    public MediaAction Media
    {
        get => model.Media;
        set
        {
            if (Set(model.Media, value, v => model.Media = v))
                OnPropertyChanged(nameof(Summary));
        }
    }

    public string Command
    {
        get => model.Command;
        set => Set(model.Command, value, v => model.Command = v);
    }

    public string Target
    {
        get => model.Target;
        set => Set(model.Target, value, v => model.Target = v);
    }

    public PageAction Page
    {
        get => model.Page;
        set
        {
            if (Set(model.Page, value, v => model.Page = v))
            {
                OnPropertyChanged(nameof(IsPageGoTo));
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public bool IsPageGoTo => Page == PageAction.GoTo;

    /// <summary>1-based page number for the UI.</summary>
    public decimal PageNumber
    {
        get => model.PageIndex + 1;
        set
        {
            if (Set(model.PageIndex, (int)Math.Max(1, value) - 1, v => model.PageIndex = v))
                OnPropertyChanged(nameof(Summary));
        }
    }

    public decimal BrightnessStep
    {
        get => model.BrightnessStep;
        set
        {
            if (Set(model.BrightnessStep, (int)value, v => model.BrightnessStep = v))
                OnPropertyChanged(nameof(Summary));
        }
    }

    public decimal DelayMs
    {
        get => model.DelayMs;
        set
        {
            if (Set(model.DelayMs, (int)Math.Max(0, value), v => model.DelayMs = v))
                OnPropertyChanged(nameof(Summary));
        }
    }

    /// <summary>Short text shown on knob/button tiles.</summary>
    public string Summary => Type switch
    {
        ActionType.None => "—",
        ActionType.Hotkey => Hotkey,
        ActionType.Media => Media.ToString(),
        ActionType.Page => Page switch
        {
            PageAction.Next => "Next page",
            PageAction.Previous => "Previous page",
            _ => $"Page {PageNumber}",
        },
        ActionType.Brightness => $"Brightness {BrightnessStep:+0;-0}%",
        ActionType.Delay => $"Wait {DelayMs} ms",
        ActionType.App => string.IsNullOrEmpty(model.AppName) ? "App" : model.AppName,
        _ => Type.ToString(),
    };

    private bool Set<T>(T current, T value, Action<T> assign, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
            return false;
        assign(value);
        OnPropertyChanged(name);
        changed();
        return true;
    }
}
