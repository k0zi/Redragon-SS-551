using Avalonia.Data.Converters;

namespace SS551.App.ViewModels;

public static class Converters
{
    /// <summary>Caption under the normal-look preview: "Off" for indicator keys.</summary>
    public static readonly IValueConverter OffLabel =
        new FuncValueConverter<bool, string>(hasState => hasState ? "Off" : "Preview");
}
