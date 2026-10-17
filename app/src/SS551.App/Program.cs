using Avalonia;
using Avalonia.Media;
using KD.Avalonia.Rice;
using KD.Avalonia.Rice.Options;
using KD.Avalonia.Rice.Theming;
using System;
using System.Linq;

namespace SS551.App;

sealed class Program
{
    // Red accent on near-black, like the deck itself.
    private static readonly RiceTheme Redragon = new("redragon", "Redragon",
        Light: new RicePalette(Color.Parse("#D32F2F"), Color.Parse("#F4F4F6"), Color.Parse("#FFFFFF"), Color.Parse("#1B1B1F")),
        Dark: new RicePalette(Color.Parse("#E53935"), Color.Parse("#121214"), Color.Parse("#1A1A1E"), Color.Parse("#EDEDF0")));

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    /// <summary>Set while this process is the running instance.</summary>
    public static SingleInstance? Instance { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        // A second launch only brings the running instance's window to the front.
        using var instance = SingleInstance.TryAcquire(show: !args.Contains("--hidden"));
        if (instance is null)
            return;
        Instance = instance;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .UseRice(o =>
            {
                o.AppInfo = new RiceAppInfo
                {
                    AppId = "ss551",
                    Name = "Redragon SS-551",
                    Author = "David Kozma",
                    Description = "Linux driver and configuration app for the Redragon SS-551 stream deck.",
                    IconUri = "avares://ss551/Assets/icon.png",
                };
                o.SupportedThemes = [Redragon, .. BuiltInThemes.All];
                o.DefaultThemeId = Redragon.Id;
            })
            .LogToTrace();
}
