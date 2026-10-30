using System.Globalization;

namespace CLEANit.Core.Storage;

public static class ByteSize
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(long bytes)
    {
        double value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{value:0} {Units[unit]}"
            : string.Format(CultureInfo.CurrentCulture, "{0:0.##} {1}", value, Units[unit]);
    }
}
