using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SS551.Core;
using SS551.Core.Model;

namespace SS551.App.ViewModels;

/// <summary>An ordered list of steps; edits go straight into the model list.</summary>
public sealed partial class MacroViewModel : ViewModelBase
{
    private readonly List<DeckAction> _model;
    private readonly Action _changed;

    public MacroViewModel(List<DeckAction> model, Action changed)
    {
        _model = model;
        _changed = changed;
        foreach (var action in model)
            Steps.Add(Wrap(action));
        Renumber();
    }

    public ObservableCollection<ActionViewModel> Steps { get; } = [];

    public bool IsEmpty => Steps.Count == 0;

    /// <summary>Short text for tiles: the first step, plus how many follow.</summary>
    public string Summary => Steps.Count switch
    {
        0 => "—",
        1 => Steps[0].Summary,
        _ => $"{Steps[0].Summary} +{Steps.Count - 1}",
    };

    [RelayCommand]
    private void AddStep()
    {
        var action = new DeckAction();
        _model.Add(action);
        Steps.Add(Wrap(action));
        Changed();
    }

    /// <summary>Replaces the macro with a single step launching the app.</summary>
    public void SetSingleApp(DesktopApp app)
    {
        _model.Clear();
        Steps.Clear();
        AddStep();
        Steps[0].SetApp(app);
    }

    [RelayCommand]
    private void RemoveStep(ActionViewModel step)
    {
        var i = Steps.IndexOf(step);
        if (i < 0)
            return;
        _model.RemoveAt(i);
        Steps.RemoveAt(i);
        Changed();
    }

    [RelayCommand]
    private void MoveUp(ActionViewModel step) => Move(step, -1);

    [RelayCommand]
    private void MoveDown(ActionViewModel step) => Move(step, +1);

    private void Move(ActionViewModel step, int delta)
    {
        var from = Steps.IndexOf(step);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= Steps.Count)
            return;
        (_model[from], _model[to]) = (_model[to], _model[from]);
        Steps.Move(from, to);
        Changed();
    }

    private ActionViewModel Wrap(DeckAction action)
    {
        var vm = new ActionViewModel(action, _changed);
        vm.PropertyChanged += OnStepChanged;
        return vm;
    }

    private void OnStepChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ActionViewModel.Summary))
            OnPropertyChanged(nameof(Summary));
    }

    private void Changed()
    {
        Renumber();
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Summary));
        _changed();
    }

    private void Renumber()
    {
        for (var i = 0; i < Steps.Count; i++)
            Steps[i].Number = i + 1;
    }
}

public sealed class HoldStageViewModel : ViewModelBase
{
    private readonly HoldStage _model;
    private readonly Action _changed;
    private int _number;
    private double _atSeconds;

    public HoldStageViewModel(HoldStage model, Action changed)
    {
        _model = model;
        _changed = changed;
        Actions = new MacroViewModel(model.Actions, changed);
    }

    public HoldStage Model => _model;
    public MacroViewModel Actions { get; }

    /// <summary>Hold time added on top of the previous stage.</summary>
    public decimal Seconds
    {
        get => (decimal)_model.Seconds;
        set
        {
            var v = (double)Math.Max(0.1m, value);
            if (_model.Seconds == v)
                return;
            _model.Seconds = v;
            OnPropertyChanged();
            _changed();
        }
    }

    public int Number
    {
        get => _number;
        set => SetProperty(ref _number, value);
    }

    /// <summary>Total hold time at which this stage fires.</summary>
    public double AtSeconds
    {
        get => _atSeconds;
        set => SetProperty(ref _atSeconds, value);
    }
}

/// <summary>Short press plus any number of accumulating long-press stages.</summary>
public sealed partial class PressViewModel : ViewModelBase
{
    private readonly PressConfig _model;
    private readonly Action _changed;

    public PressViewModel(PressConfig model, Action changed)
    {
        _model = model;
        _changed = changed;
        Short = new MacroViewModel(model.Actions, changed);
        Short.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MacroViewModel.Summary))
                OnPropertyChanged(nameof(Summary));
        };
        foreach (var stage in model.HoldStages)
            Stages.Add(Wrap(stage));
        Recalculate();
    }

    public MacroViewModel Short { get; }
    public ObservableCollection<HoldStageViewModel> Stages { get; } = [];

    private string _title = "Press";

    /// <summary>Section heading in the editor.</summary>
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }
    public bool HasStages => Stages.Count > 0;

    /// <summary>Short text for tiles, e.g. "Mute · 2 holds".</summary>
    public string Summary => Stages.Count switch
    {
        0 => Short.Summary,
        1 => $"{Short.Summary} · 1 hold",
        _ => $"{Short.Summary} · {Stages.Count} holds",
    };

    [RelayCommand]
    private void AddStage()
    {
        var stage = new HoldStage { Seconds = Stages.Count == 0 ? 1 : (double)Stages[^1].Seconds };
        _model.HoldStages.Add(stage);
        Stages.Add(Wrap(stage));
        Changed();
    }

    [RelayCommand]
    private void RemoveStage(HoldStageViewModel stage)
    {
        var i = Stages.IndexOf(stage);
        if (i < 0)
            return;
        _model.HoldStages.RemoveAt(i);
        Stages.RemoveAt(i);
        Changed();
    }

    private HoldStageViewModel Wrap(HoldStage stage)
    {
        var vm = new HoldStageViewModel(stage, _changed);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HoldStageViewModel.Seconds))
                Recalculate();
        };
        return vm;
    }

    private void Changed()
    {
        Recalculate();
        OnPropertyChanged(nameof(HasStages));
        OnPropertyChanged(nameof(Summary));
        _changed();
    }

    private void Recalculate()
    {
        double total = 0;
        for (var i = 0; i < Stages.Count; i++)
        {
            total += (double)Stages[i].Seconds;
            Stages[i].Number = i + 1;
            Stages[i].AtSeconds = total;
        }
    }
}
