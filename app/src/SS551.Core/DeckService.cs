using System.Collections.Concurrent;
using SS551.Core.Model;
using SS551.Native;

namespace SS551.Core;

/// <summary>
/// Keeps the deck connected (re-opening it after unplug), pushes the current
/// page's key images, sends heartbeats and turns deck input into actions.
/// Events are raised on the background device thread.
/// </summary>
public sealed class DeckService : IDisposable
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);

    private readonly ProfileStore _store;
    private readonly VirtualKeyboard? _keyboard;
    private readonly ActionRunner _runner;
    private readonly ManualResetEventSlim _stop = new();
    private readonly Lock _saveLock = new();
    private Thread? _thread;
    private volatile DeckDevice? _device;
    private int _page;

    // Not saved: the deck always starts lit, but an off backlight survives a reconnect.
    private readonly Lock _backlightLock = new();
    private volatile bool _backlightOn = true;

    // On/off state of indicator keys, by (page, key).
    private readonly ConcurrentDictionary<(int Page, int Key), bool> _states = new();
    private readonly SemaphoreSlim _pollSignal = new(0);
    private readonly CancellationTokenSource _stateCts = new();

    public DeckService(ProfileStore store)
    {
        _store = store;
        Profile = LoadProfile(store);
        _keyboard = VirtualKeyboard.TryOpen();
        _runner = new ActionRunner(_keyboard);
        _runner.Log += m => Log?.Invoke(m);
    }

    public Profile Profile { get; }
    public bool HasVirtualKeyboard => _keyboard is not null;
    public bool IsConnected => _device is not null;
    public string? Firmware => _device?.Firmware;

    public int CurrentPage => Volatile.Read(ref _page);

    public bool IsBacklightOn => _backlightOn;

    public event Action<bool>? ConnectionChanged;
    public event Action<DeckInput>? InputReceived;
    public event Action<int>? PageChanged;
    public event Action<int>? BrightnessChanged;
    public event Action<bool>? BacklightChanged;
    public event Action<string>? Log;

    /// <summary>(page, key, on): an indicator key changed state.</summary>
    public event Action<int, int, bool>? KeyStateChanged;

    public void Start()
    {
        if (_thread is not null)
            return;
        _thread = new Thread(DeviceLoop) { IsBackground = true, Name = "SS551 device" };
        _thread.Start();
        _ = Task.Run(() => StateLoopAsync(_stateCts.Token));
    }

    public bool IsKeyOn(int page, int key) => _states.TryGetValue((page, key), out var on) && on;

    /// <summary>Forgets all key states, e.g. after pages were removed and indices shifted.</summary>
    public void ResetStates()
    {
        _states.Clear();
        RequestStatePoll();
    }

    /// <summary>Re-checks polled key states now (e.g. after the state source was edited).</summary>
    public void RequestStatePoll() => _pollSignal.Release();

    private void SetKeyState(int page, int key, bool on)
    {
        var old = IsKeyOn(page, key);
        _states[(page, key)] = on;
        if (old == on)
            return;
        KeyStateChanged?.Invoke(page, key, on);
        if (page == CurrentPage)
            RefreshKeys([key]);
    }

    private async Task StateLoopAsync(CancellationToken ct)
    {
        var due = new Dictionary<(int, int), DateTime>();
        while (!ct.IsCancellationRequested)
        {
            bool forced;
            try
            {
                forced = await _pollSignal.WaitAsync(TimeSpan.FromMilliseconds(250), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            while (_pollSignal.Wait(0))
            {
                // Coalesce queued requests into this round.
            }

            try
            {
                var page = CurrentPage;
                var keys = Profile.Pages[page].Keys;
                var round = new StateProbe.Round();
                for (var i = 0; i < keys.Count; i++)
                {
                    var state = keys[i].State;
                    if (StateProbe.Interval(state) is not { } interval)
                        continue;
                    var now = DateTime.UtcNow;
                    if (!forced && due.TryGetValue((page, i), out var next) && now < next)
                        continue;
                    due[(page, i)] = now + interval;
                    if (await StateProbe.EvaluateAsync(state, round) is { } on)
                        SetKeyState(page, i, on);
                }
            }
            catch (Exception e)
            {
                // The profile may be edited concurrently; try again next round.
                Log?.Invoke($"State polling failed: {e.Message}");
            }
        }
    }

    /// <summary>Re-checks states shortly after input, once quickly and once after slower actions.</summary>
    private void PollSoon()
    {
        _ = Task.Delay(300).ContinueWith(_ => RequestStatePoll());
        _ = Task.Delay(1200).ContinueWith(_ => RequestStatePoll());
    }

    public void Save()
    {
        lock (_saveLock)
        {
            try
            {
                _store.Save(Profile);
            }
            catch (Exception e)
            {
                Log?.Invoke($"Saving profile failed: {e.Message}");
            }
        }
    }

    public void SetPage(int index)
    {
        index = Math.Clamp(index, 0, Profile.Pages.Count - 1);
        if (Interlocked.Exchange(ref _page, index) == index)
            return;
        PageChanged?.Invoke(index);
        RefreshKeys();
        RequestStatePoll();
    }

    /// <summary>Sets the brightness; changing it while the backlight is off turns it back on.</summary>
    public void SetBrightness(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        if (Profile.Brightness == percent)
            return;
        Profile.Brightness = percent;
        Try(d => d.SetBrightness(percent));
        BrightnessChanged?.Invoke(percent);
        SetBacklight(true);
    }

    /// <summary>Turns the LCD backlight off (blank keys) or back on; input keeps working either way.</summary>
    public void SetBacklight(bool on)
    {
        lock (_backlightLock)
        {
            if (_backlightOn == on)
                return;
            _backlightOn = on;
            Try(d => ApplyBacklight(d, on));
        }
        BacklightChanged?.Invoke(on);
    }

    private void ApplyBacklight(DeckDevice device, bool on)
    {
        if (!on)
        {
            device.ScreenOff();
            return;
        }
        // Restore what the panel showed before it went dark.
        device.WakeScreen();
        device.SetBrightness(Profile.Brightness);
        PushKeys(device);
    }

    /// <summary>Re-renders all keys of the current page (call after editing them).</summary>
    public void RefreshKeys(IEnumerable<int>? onlyKeys = null)
    {
        if (CurrentPage >= Profile.Pages.Count)
            Volatile.Write(ref _page, Profile.Pages.Count - 1);
        Try(d => PushKeys(d, onlyKeys));
    }

    private void PushKeys(DeckDevice device, IEnumerable<int>? onlyKeys = null)
    {
        var pageIndex = CurrentPage;
        var page = Profile.Pages[pageIndex];
        var indices = onlyKeys ?? Enumerable.Range(0, Profile.KeyCount);
        device.SetKeyImages(indices
            .Select(i => (i, KeyRenderer.RenderForDevice(page.Keys[i].Appearance(IsKeyOn(pageIndex, i)))))
            .ToList());
    }

    private void Try(Action<DeckDevice> command)
    {
        if (_device is not { } device)
            return;
        try
        {
            command(device);
        }
        catch (Exception e) when (e is DeckException or ObjectDisposedException)
        {
            Log?.Invoke($"Device write failed: {e.Message}");
        }
    }

    private void DeviceLoop()
    {
        string? lastError = null;
        while (!_stop.IsSet)
        {
            var device = DeckDevice.TryOpen(out var error);
            if (device is null)
            {
                if (error != lastError)
                    Log?.Invoke($"Waiting for device: {error}");
                lastError = error;
                _stop.Wait(ReconnectDelay);
                continue;
            }
            lastError = null;

            try
            {
                // The deck sends no input until it gets the session packet.
                device.StartSession();
                device.SetBrightness(Profile.Brightness);
                device.Clear();
                PushKeys(device);
                lock (_backlightLock)
                {
                    if (!_backlightOn)
                        device.ScreenOff();
                    _device = device;
                }
                Log?.Invoke($"Connected: {device.Firmware} (serial {device.Serial})");
                ConnectionChanged?.Invoke(true);

                var lastBeat = DateTime.UtcNow;
                while (!_stop.IsSet)
                {
                    if (device.ReadInput(250) is { } input)
                        Handle(input);
                    if (DateTime.UtcNow - lastBeat > HeartbeatInterval)
                    {
                        device.Heartbeat();
                        lastBeat = DateTime.UtcNow;
                    }
                }

                // Leave the deck blank instead of showing keys that no longer work.
                device.Clear();
            }
            catch (DeckException e)
            {
                Log?.Invoke($"Device lost: {e.Message}");
            }
            finally
            {
                foreach (var control in _held.Keys.ToList())
                    OnReleased(control, runShortPress: false);
                var wasConnected = _device is not null;
                _device = null;
                device.Dispose();
                if (wasConnected)
                    ConnectionChanged?.Invoke(false);
            }
        }
    }

    private void Handle(RawInput raw)
    {
        try
        {
            Dispatch(raw);
        }
        catch (Exception e) when (e is not DeckException)
        {
            // The UI may edit the profile (e.g. remove a page) concurrently;
            // never let that kill the device thread.
            Log?.Invoke($"Input handling failed: {e.Message}");
        }
    }

    private void Dispatch(RawInput raw)
    {
        if (DeckInput.Decode(raw.Code, raw.State) is not { } input)
        {
            Log?.Invoke($"Unknown input code 0x{raw.Code:X2} (state {raw.State})");
            return;
        }
        InputReceived?.Invoke(input);
        PollSoon();

        switch (input.Kind)
        {
            case DeckInputKind.Pressed:
                var (config, onShortPress) = PressConfigFor(input);
                OnPressed(input.Control, config, onShortPress);
                break;
            case DeckInputKind.Released:
                OnReleased(input.Control);
                break;
            case DeckInputKind.RotatedLeft:
                RunMacro([.. Profile.Knobs[input.Index].Left]);
                break;
            case DeckInputKind.RotatedRight:
                RunMacro([.. Profile.Knobs[input.Index].Right]);
                break;
        }
    }

    /// <summary>
    /// The press config for the control in its current state, plus what to do
    /// when the short press fires (flip a toggle key).
    /// </summary>
    private (PressConfig Config, Action? OnShortPress) PressConfigFor(DeckInput input)
    {
        switch (input.Control)
        {
            case <= DeckControl.Key6:
                var page = CurrentPage;
                var key = Profile.Pages[page].Keys[input.Index];
                var on = IsKeyOn(page, input.Index);
                Action? flip = key.State.Source == StateSource.Toggle
                    ? () => SetKeyState(page, input.Index, !IsKeyOn(page, input.Index))
                    : null;
                return (key.PressFor(on), flip);
            case <= DeckControl.Button3:
                return (Profile.Buttons[input.Index], null);
            default:
                return (Profile.Knobs[input.Index].Press, null);
        }
    }

    /// <summary>A control that is held down while it has long-press stages.</summary>
    private sealed class HeldControl(List<DeckAction> shortPress, Action? onShortPress)
    {
        public CancellationTokenSource Cancel { get; } = new();
        public List<DeckAction> ShortPress { get; } = shortPress;
        public Action? OnShortPress { get; } = onShortPress;
        public volatile bool StageReached;
    }

    // Only touched from the device thread.
    private readonly Dictionary<DeckControl, HeldControl> _held = [];

    private void OnPressed(DeckControl control, PressConfig config, Action? onShortPress)
    {
        // A missed release (e.g. reconnect) must not leave an old timer running.
        OnReleased(control, runShortPress: false);

        // Copy now: the UI may edit the profile while the control is held.
        List<DeckAction> shortPress = [.. config.Actions];
        var stages = config.HoldStages.Select(s => (s.Seconds, Actions: s.Actions.ToList())).ToList();
        if (stages.Count == 0)
        {
            onShortPress?.Invoke();
            RunMacro(shortPress);
            return;
        }

        var held = new HeldControl(shortPress, onShortPress);
        _held[control] = held;
        _ = RunHoldStagesAsync(held, stages);
    }

    private void OnReleased(DeckControl control, bool runShortPress = true)
    {
        if (!_held.Remove(control, out var held))
            return;
        held.Cancel.Cancel();
        if (runShortPress && !held.StageReached)
        {
            held.OnShortPress?.Invoke();
            RunMacro(held.ShortPress);
        }
    }

    private async Task RunHoldStagesAsync(HeldControl held, List<(double Seconds, List<DeckAction> Actions)> stages)
    {
        try
        {
            // Each stage's time counts from the previous stage, so they accumulate.
            foreach (var (seconds, actions) in stages)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(0.05, seconds)), held.Cancel.Token);
                held.StageReached = true;
                RunMacro(actions);
            }
        }
        catch (OperationCanceledException)
        {
            // Released before the next stage.
        }
    }

    /// <summary>Runs macro steps in order on the thread pool, keeping the read loop responsive.</summary>
    private void RunMacro(List<DeckAction> steps)
    {
        if (steps.Count == 0)
            return;
        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var step in steps)
                    await RunStepAsync(step);
            }
            catch (Exception e)
            {
                Log?.Invoke($"Macro failed: {e.Message}");
            }
        });
    }

    private async Task RunStepAsync(DeckAction step)
    {
        switch (step.Type)
        {
            case ActionType.None:
                break;
            case ActionType.Delay:
                await Task.Delay(Math.Max(0, step.DelayMs));
                break;
            case ActionType.Page:
                var n = Profile.Pages.Count;
                SetPage(step.Page switch
                {
                    PageAction.Next => (CurrentPage + 1) % n,
                    PageAction.Previous => (CurrentPage - 1 + n) % n,
                    _ => step.PageIndex,
                });
                break;
            case ActionType.Brightness:
                SetBrightness(Profile.Brightness + step.BrightnessStep);
                break;
            case ActionType.Backlight:
                SetBacklight(step.Backlight switch
                {
                    BacklightAction.Off => false,
                    BacklightAction.On => true,
                    _ => !IsBacklightOn,
                });
                break;
            default:
                _runner.Run(step);
                break;
        }
    }

    private static Profile LoadProfile(ProfileStore store)
    {
        try
        {
            return store.Load();
        }
        catch (Exception)
        {
            // A broken profile file should not keep the app from starting;
            // keep it aside instead of overwriting it on the next save.
            File.Move(store.Path, store.Path + ".broken", overwrite: true);
            return Profile.CreateDefault();
        }
    }

    public void Dispose()
    {
        _stateCts.Cancel();
        _stop.Set();
        _thread?.Join(TimeSpan.FromSeconds(3));
        _keyboard?.Dispose();
        _stop.Dispose();
    }
}
