using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using SS551.Core;
using SS551.Core.Model;

namespace SS551.App.ViewModels;

/// <summary>Common part of everything that can be selected on the deck picture.</summary>
public abstract partial class ControlViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>True briefly while the physical control is used.</summary>
    [ObservableProperty]
    public partial bool IsActive { get; set; }

    public abstract string Name { get; }
}

/// <summary>Edits one key look (title, icon, colours) and keeps its preview image current.</summary>
public sealed partial class AppearanceViewModel : ViewModelBase
{
    private const int PreviewSize = 96;
    private readonly KeyAppearance _model;
    private readonly Action _changed;

    public AppearanceViewModel(KeyAppearance model, Action changed)
    {
        _model = model;
        _changed = changed;
        Preview = RenderPreview();
    }

    [ObservableProperty]
    public partial Bitmap Preview { get; private set; }

    public string Title
    {
        get => _model.Title;
        set => Update(_model.Title, value, v => _model.Title = v);
    }

    public string? IconPath
    {
        get => _model.IconPath;
        set => Update(_model.IconPath, string.IsNullOrWhiteSpace(value) ? null : value, v => _model.IconPath = v);
    }

    public Color BackgroundColor
    {
        get => ParseColor(_model.BackgroundColor, Colors.Black);
        set => Update(_model.BackgroundColor, ToHex(value), v => _model.BackgroundColor = v);
    }

    public Color TextColor
    {
        get => ParseColor(_model.TextColor, Colors.White);
        set => Update(_model.TextColor, ToHex(value), v => _model.TextColor = v);
    }

    /// <summary>Overwrites this look with another one (e.g. to start the "on" look from the normal one).</summary>
    public void CopyFrom(AppearanceViewModel other)
    {
        Title = other.Title;
        IconPath = other.IconPath;
        TextColor = other.TextColor;
    }

    private void Update<T>(T current, T value, Action<T> assign, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
            return;
        assign(value);
        OnPropertyChanged(name);
        Preview = RenderPreview();
        _changed();
    }

    private Bitmap RenderPreview()
    {
        using var stream = new MemoryStream(KeyRenderer.RenderPng(_model, PreviewSize));
        return new Bitmap(stream);
    }

    private static Color ParseColor(string text, Color fallback) => Color.TryParse(text, out var c) ? c : fallback;

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}

public sealed partial class KeyViewModel : ControlViewModel
{
    public static StateSource[] StateSources { get; } = Enum.GetValues<StateSource>();

    private readonly KeyConfig _model;
    private readonly Action<KeyViewModel> _imageChanged;

    /// <param name="imageChanged">Look or state settings changed: save and re-upload the key image.</param>
    /// <param name="actionsChanged">Only actions changed: save.</param>
    public KeyViewModel(int index, KeyConfig model, bool isOn, Action<KeyViewModel> imageChanged, Action actionsChanged)
    {
        Index = index;
        _model = model;
        _imageChanged = imageChanged;
        _isOn = isOn;
        Look = new AppearanceViewModel(model.Look, OnLookChanged);
        OnLook = new AppearanceViewModel(model.State.On, OnLookChanged);
        Press = new PressViewModel(model.Press, actionsChanged);
        OnPress = new PressViewModel(model.State.OnPress, actionsChanged) { Title = "Press while on" };
        UpdatePressTitle();
        Look.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppearanceViewModel.Preview))
                OnPropertyChanged(nameof(Preview));
        };
        OnLook.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppearanceViewModel.Preview))
                OnPropertyChanged(nameof(Preview));
        };
    }

    public int Index { get; }
    public override string Name => $"Key {Index + 1}";
    public AppearanceViewModel Look { get; }
    public AppearanceViewModel OnLook { get; }
    public PressViewModel Press { get; }
    public PressViewModel OnPress { get; }

    /// <summary>What the deck currently shows for this key.</summary>
    public Bitmap Preview => ShowsOnLook ? OnLook.Preview : Look.Preview;

    private bool ShowsOnLook => _isOn && HasState;

    private bool _isOn;

    /// <summary>Current state of an indicator key, as reported by the deck service.</summary>
    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (SetProperty(ref _isOn, value))
                OnPropertyChanged(nameof(Preview));
        }
    }

    public StateSource StateSource
    {
        get => _model.State.Source;
        set
        {
            if (_model.State.Source == value)
                return;
            var wasEnabled = HasState;
            _model.State.Source = value;
            // Start the "on" look from the normal one the first time a state is enabled.
            if (!wasEnabled && HasState && OnLook.Title == "" && OnLook.IconPath is null)
                OnLook.CopyFrom(Look);
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasState));
            OnPropertyChanged(nameof(IsCommandSource));
            OnPropertyChanged(nameof(StateHint));
            OnPropertyChanged(nameof(Preview));
            OnPropertyChanged(nameof(ShowOnPress));
            UpdatePressTitle();
            _imageChanged(this);
        }
    }

    public bool HasState => _model.HasState;
    public bool IsCommandSource => StateSource == StateSource.Command;

    public string StateHint => StateSource switch
    {
        StateSource.Toggle => "Flips on every short press. Starts off when the app starts.",
        StateSource.SpeakerMuted => "On while the default audio output is muted (checked every second and right after any input).",
        StateSource.MicMuted => "On while the default microphone is muted. Tip: use the MicMute media action to toggle it.",
        StateSource.Command => "The command is run periodically; exit code 0 means on. Example: pgrep -x obs",
        _ => "",
    };

    public string StateCommand
    {
        get => _model.State.Command;
        set
        {
            if (_model.State.Command == value)
                return;
            _model.State.Command = value;
            OnPropertyChanged();
            _imageChanged(this);
        }
    }

    public decimal IntervalSeconds
    {
        get => (decimal)_model.State.IntervalSeconds;
        set
        {
            var v = (double)Math.Max(0.2m, value);
            if (_model.State.IntervalSeconds == v)
                return;
            _model.State.IntervalSeconds = v;
            OnPropertyChanged();
            _imageChanged(this);
        }
    }

    public bool SeparateOnPress
    {
        get => _model.State.SeparateOnPress;
        set
        {
            if (_model.State.SeparateOnPress == value)
                return;
            _model.State.SeparateOnPress = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowOnPress));
            UpdatePressTitle();
            _imageChanged(this);
        }
    }

    public bool ShowOnPress => HasState && SeparateOnPress;

    private void UpdatePressTitle() => Press.Title = ShowOnPress ? "Press while off" : "Press";

    private void OnLookChanged() => _imageChanged(this);
}

public sealed class ButtonViewModel(int index, PressConfig model, Action changed) : ControlViewModel
{
    public override string Name => $"Button {index + 1}";
    public PressViewModel Press { get; } = new(model, changed);
}

public sealed class KnobViewModel(string name, KnobConfig model, Action changed) : ControlViewModel
{
    public override string Name => name;
    public MacroViewModel Left { get; } = new(model.Left, changed);
    public MacroViewModel Right { get; } = new(model.Right, changed);
    public PressViewModel Press { get; } = new(model.Press, changed);
}
