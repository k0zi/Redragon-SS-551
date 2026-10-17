using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SS551.Core;
using SS551.Core.Model;
using SS551.Native;

namespace SS551.App.ViewModels;

public sealed partial class PageItemViewModel(DeckPage model, Action changed) : ViewModelBase
{
    public DeckPage Model => model;

    public string Name
    {
        get => model.Name;
        set
        {
            if (model.Name == value)
                return;
            model.Name = value;
            OnPropertyChanged();
            changed();
        }
    }
}

public sealed partial class MainViewModel : ViewModelBase
{
    private const int MaxLogLines = 200;
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(400);

    private readonly DeckService _service;
    private readonly DispatcherTimer _saveTimer;
    private readonly HashSet<int> _dirtyKeys = [];
    private bool _refreshAllKeys;

    public MainViewModel(DeckService service)
    {
        _service = service;
        var profile = service.Profile;

        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => FlushChanges();

        Pages = new ObservableCollection<PageItemViewModel>(profile.Pages.Select(p => new PageItemViewModel(p, ScheduleSave)));
        Buttons = Enumerable.Range(0, Profile.ButtonCount)
            .Select(i => new ButtonViewModel(i, profile.Buttons[i], ScheduleSave)).ToArray();
        string[] knobNames = ["Big knob", "Left knob", "Right knob"];
        Knobs = Enumerable.Range(0, Profile.KnobCount)
            .Select(i => new KnobViewModel(knobNames[i], profile.Knobs[i], ScheduleSave)).ToArray();

        _brightness = profile.Brightness;
        SelectedPage = Pages[Math.Min(service.CurrentPage, Pages.Count - 1)];

        _service.ConnectionChanged += c => Dispatcher.UIThread.Post(() => UpdateStatus());
        _service.PageChanged += i => Dispatcher.UIThread.Post(() => SelectedPage = Pages[i]);
        _service.BrightnessChanged += b => Dispatcher.UIThread.Post(() =>
        {
            _brightness = b;
            OnPropertyChanged(nameof(Brightness));
            ScheduleSave();
        });
        _service.InputReceived += i => Dispatcher.UIThread.Post(() => OnInput(i));
        _service.KeyStateChanged += (page, key, on) => Dispatcher.UIThread.Post(() =>
        {
            if (SelectedPage is { } p && Pages.IndexOf(p) == page)
                Keys[key].IsOn = on;
        });
        _service.Log += m => Dispatcher.UIThread.Post(() => AddLog(m));

        if (!_service.HasVirtualKeyboard)
            AddLog("Virtual keyboard unavailable: hotkeys and media keys will not work (/dev/uinput not writable).");
        UpdateStatus();
    }

    public ObservableCollection<PageItemViewModel> Pages { get; }
    public ObservableCollection<KeyViewModel> Keys { get; } = [];
    public ButtonViewModel[] Buttons { get; }
    public KnobViewModel[] Knobs { get; }
    public ObservableCollection<string> Log { get; } = [];

    [ObservableProperty]
    public partial PageItemViewModel? SelectedPage { get; set; }

    [ObservableProperty]
    public partial ControlViewModel? Selected { get; set; }

    [ObservableProperty]
    public partial bool IsConnected { get; private set; }

    [ObservableProperty]
    public partial string StatusText { get; private set; } = "";

    private int _brightness;

    public int Brightness
    {
        get => _brightness;
        set
        {
            if (!SetProperty(ref _brightness, value))
                return;
            _service.SetBrightness(value);
            ScheduleSave();
        }
    }

    public bool CanRemovePage => Pages.Count > 1;

    partial void OnSelectedPageChanged(PageItemViewModel? value)
    {
        if (value is null)
            return;
        var selectedIndex = Selected is KeyViewModel k ? k.Index : (int?)null;

        Keys.Clear();
        for (var i = 0; i < Profile.KeyCount; i++)
        {
            var pageIndex = Pages.IndexOf(value);
            Keys.Add(new KeyViewModel(i, value.Model.Keys[i], _service.IsKeyOn(pageIndex, i), OnKeyChanged, ScheduleSave));
        }
        if (selectedIndex is { } idx)
            Select(Keys[idx]);

        _service.SetPage(Pages.IndexOf(value));
    }

    partial void OnSelectedChanged(ControlViewModel? oldValue, ControlViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.IsSelected = false;
        if (newValue is not null)
            newValue.IsSelected = true;
    }

    [RelayCommand]
    private void Select(ControlViewModel control) => Selected = control;

    [RelayCommand]
    private void AddPage()
    {
        var page = DeckPage.Create($"Page {Pages.Count + 1}");
        _service.Profile.Pages.Add(page);
        var item = new PageItemViewModel(page, ScheduleSave);
        Pages.Add(item);
        SelectedPage = item;
        OnPropertyChanged(nameof(CanRemovePage));
        ScheduleSave();
    }

    [RelayCommand]
    private void RemovePage()
    {
        if (SelectedPage is not { } page || Pages.Count <= 1)
            return;
        var index = Pages.IndexOf(page);
        _service.Profile.Pages.Remove(page.Model);
        Pages.Remove(page);
        SelectedPage = Pages[Math.Min(index, Pages.Count - 1)];
        _refreshAllKeys = true;
        _service.ResetStates();
        OnPropertyChanged(nameof(CanRemovePage));
        ScheduleSave();
    }

    [RelayCommand]
    private void ClearLog() => Log.Clear();

    private void OnKeyChanged(KeyViewModel key)
    {
        _dirtyKeys.Add(key.Index);
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        // Debounce: typing a title should not upload an image per keystroke.
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void FlushChanges()
    {
        _saveTimer.Stop();
        _service.Save();
        if (_refreshAllKeys || _dirtyKeys.Count > 0)
        {
            int[]? keys = _refreshAllKeys ? null : [.. _dirtyKeys];
            _dirtyKeys.Clear();
            _refreshAllKeys = false;
            Task.Run(() => _service.RefreshKeys(keys));
            // State sources or commands may have been edited.
            _service.RequestStatePoll();
        }
    }

    /// <summary>Writes pending edits immediately (on exit).</summary>
    public void Flush() => FlushChanges();

    private void OnInput(DeckInput input)
    {
        ControlViewModel control = input.Control switch
        {
            <= DeckControl.Key6 => Keys[input.Index],
            <= DeckControl.Button3 => Buttons[input.Index],
            _ => Knobs[input.Index],
        };

        switch (input.Kind)
        {
            case DeckInputKind.Pressed:
                control.IsActive = true;
                break;
            case DeckInputKind.Released:
                control.IsActive = false;
                break;
            default:
                control.IsActive = true;
                DispatcherTimer.RunOnce(() => control.IsActive = false, TimeSpan.FromMilliseconds(150));
                break;
        }

        AddLog($"{control.Name}: {input.Kind} (0x{input.RawCode:X2})");
    }

    private void AddLog(string message)
    {
        Log.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        while (Log.Count > MaxLogLines)
            Log.RemoveAt(Log.Count - 1);
    }

    private void UpdateStatus()
    {
        IsConnected = _service.IsConnected;
        StatusText = IsConnected ? $"Connected — {_service.Firmware}" : "Not connected";
    }
}
