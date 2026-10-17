using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using SS551.App.ViewModels;
using SS551.App.Views;
using SS551.Core;

namespace SS551.App;

public partial class App : Application
{
    private DeckService? _service;
    private MainViewModel? _viewModel;
    private MainWindow? _window;
    private bool _quitting;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The app keeps running in the tray when the window is closed.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _service = new DeckService(new ProfileStore());
            _viewModel = new MainViewModel(_service);
            _window = new MainWindow { DataContext = _viewModel };
            _window.Closing += (_, e) =>
            {
                if (_quitting)
                    return;
                e.Cancel = true;
                _window.Hide();
            };

            SetupTray(desktop);
            desktop.Exit += (_, _) => Shutdown();

            if (!(desktop.Args ?? []).Contains("--hidden"))
                _window.Show();
            _service.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTray(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var show = new NativeMenuItem("Show");
        show.Click += (_, _) => ShowWindow();
        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) =>
        {
            _quitting = true;
            desktop.Shutdown();
        };

        var tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://ss551/Assets/icon.png"))),
            ToolTipText = "Redragon SS-551",
            Menu = new NativeMenu { Items = { show, new NativeMenuItemSeparator(), quit } },
        };
        tray.Clicked += (_, _) => ShowWindow();
        TrayIcon.SetIcons(this, [tray]);
    }

    private void ShowWindow()
    {
        if (_window is null)
            return;
        _window.Show();
        _window.Activate();
    }

    private void Shutdown()
    {
        _viewModel?.Flush();
        _service?.Dispose();
    }
}
