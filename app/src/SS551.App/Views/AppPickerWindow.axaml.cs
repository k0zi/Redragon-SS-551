using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using KD.Avalonia.Rice.Controls;
using SS551.App.ViewModels;

namespace SS551.App.Views;

public partial class AppPickerWindow : RiceWindow
{
    private readonly AppPickerViewModel _vm = new();

    public AppPickerWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Opened += async (_, _) =>
        {
            SearchBox.Focus();
            await _vm.LoadAsync();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                Choose();
            else if (e.Key == Key.Escape)
                Close(null);
        };
    }

    /// <summary>Shows the picker; returns null when cancelled.</summary>
    public static Task<PickedApp?> PickAsync(Window owner) => new AppPickerWindow().ShowDialog<PickedApp?>(owner);

    private void Choose()
    {
        if (_vm.Selected is { } item)
            Close(new PickedApp(item.App, item.IconPath));
    }

    private void OnChoose(object? sender, RoutedEventArgs e) => Choose();

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnDoubleTapped(object? sender, TappedEventArgs e) => Choose();
}
