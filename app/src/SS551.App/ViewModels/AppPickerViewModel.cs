using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SS551.Core;

namespace SS551.App.ViewModels;

/// <summary>An application chosen in the picker, with its icon resolved to a file.</summary>
public sealed record PickedApp(DesktopApp App, string? IconPath);

public sealed partial class AppItemViewModel(DesktopApp app, string? iconPath) : ViewModelBase
{
    public DesktopApp App => app;
    public string? IconPath => iconPath;
    public string Name => app.Name;
    public string? Comment => app.Comment;

    [ObservableProperty]
    public partial Bitmap? Icon { get; set; }
}

public sealed partial class AppPickerViewModel : ViewModelBase
{
    private const int IconSize = 32;
    private List<AppItemViewModel> _all = [];

    public ObservableCollection<AppItemViewModel> Apps { get; } = [];

    [ObservableProperty]
    public partial string Search { get; set; } = "";

    [ObservableProperty]
    public partial AppItemViewModel? Selected { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; } = true;

    partial void OnSearchChanged(string value) => Filter();

    public async Task LoadAsync()
    {
        var items = await Task.Run(() =>
        {
            var resolver = IconResolver.Default;
            return DesktopApps.LoadAll().Select(a => new AppItemViewModel(a, resolver.Resolve(a.Icon))).ToList();
        });
        _all = items;
        IsLoading = false;
        Filter();

        // Icons (some are SVG) render in the background so the list shows at once.
        _ = Task.Run(() =>
        {
            foreach (var item in items)
            {
                if (item.IconPath is null || KeyRenderer.RenderIconPng(item.IconPath, IconSize * 2) is not { } png)
                    continue;
                var bitmap = new Bitmap(new MemoryStream(png));
                Dispatcher.UIThread.Post(() => item.Icon = bitmap);
            }
        });
    }

    private void Filter()
    {
        var terms = Search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Apps.Clear();
        foreach (var item in _all)
        {
            if (terms.All(t => item.Name.Contains(t, StringComparison.CurrentCultureIgnoreCase)
                               || (item.Comment?.Contains(t, StringComparison.CurrentCultureIgnoreCase) ?? false)))
                Apps.Add(item);
        }
        Selected = Apps.FirstOrDefault();
    }
}
