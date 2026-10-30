using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CLEANit.Core.Storage;

namespace CLEANit.App.Controls;

/// <summary>Number that counts up smoothly to its value (overview statistics). Bytes are formatted as KB/MB/GB.</summary>
public class CounterText : TextBlock
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<CounterText, double>(nameof(Value));

    public static readonly StyledProperty<bool> IsBytesProperty =
        AvaloniaProperty.Register<CounterText, bool>(nameof(IsBytes));

    private const double DurationMs = 750;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private double _from;
    private double _shown;
    private DateTime _start;

    static CounterText()
    {
        ValueProperty.Changed.AddClassHandler<CounterText>((c, _) => c.Animate());
        IsBytesProperty.Changed.AddClassHandler<CounterText>((c, _) => c.Render(c.Value));
    }

    public CounterText()
    {
        _timer.Tick += (_, _) => Step();
        Render(0);
    }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool IsBytes
    {
        get => GetValue(IsBytesProperty);
        set => SetValue(IsBytesProperty, value);
    }

    private void Animate()
    {
        _from = _shown;
        _start = DateTime.UtcNow;
        _timer.Start();
    }

    private void Step()
    {
        var t = Math.Clamp((DateTime.UtcNow - _start).TotalMilliseconds / DurationMs, 0, 1);
        var eased = 1 - Math.Pow(1 - t, 3);
        Render(_from + (Value - _from) * eased);
        if (t >= 1)
        {
            _timer.Stop();
        }
    }

    private void Render(double value)
    {
        _shown = value;
        Text = IsBytes ? ByteSize.Format((long)value) : Math.Round(value).ToString("N0");
    }
}
