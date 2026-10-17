using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using KD.Avalonia.Rice.Controls;
using SS551.App.ViewModels;

namespace SS551.App.Views;

public partial class MainWindow : RiceWindow
{
    private static readonly FilePickerFileType Images = new("Images")
    {
        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.svg", "*.webp", "*.bmp", "*.gif"],
    };

    public MainWindow() => InitializeComponent();

    private async void OnBrowseIcon(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not AppearanceViewModel look)
            return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose key icon",
            AllowMultiple = false,
            FileTypeFilter = [Images, FilePickerFileTypes.All],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            look.IconPath = path;
    }

    /// <summary>Key: show only the app's icon and launch the app on a short press.</summary>
    private async void OnChooseAppForKey(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not KeyViewModel key || await AppPickerWindow.PickAsync(this) is not { } picked)
            return;
        key.Look.IconPath = picked.IconPath;
        key.Look.Title = "";
        key.Press.Short.SetSingleApp(picked.App);
    }

    private async void OnIconFromApp(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is AppearanceViewModel look && await AppPickerWindow.PickAsync(this) is { } picked)
            look.IconPath = picked.IconPath;
    }

    private async void OnChooseAppForStep(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ActionViewModel step && await AppPickerWindow.PickAsync(this) is { } picked)
            step.SetApp(picked.App);
    }

    private void OnClearIcon(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is AppearanceViewModel look)
            look.IconPath = null;
    }
}
