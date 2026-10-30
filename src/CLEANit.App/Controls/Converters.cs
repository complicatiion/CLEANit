using Avalonia.Data.Converters;

namespace CLEANit.App.Controls;

/// <summary>Small value converters for the views.</summary>
public static class Converters
{
    /// <summary>Dims ignored rows.</summary>
    public static readonly IValueConverter IgnoredOpacity = new FuncValueConverter<bool, double>(ignored => ignored ? 0.55 : 1.0);
}
