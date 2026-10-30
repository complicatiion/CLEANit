using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CLEANit.App.ViewModels;

namespace CLEANit.App.Controls;

/// <summary>Squarified treemap of folder sizes; click a folder tile to open it.</summary>
public sealed class TreemapControl : Control
{
    public static readonly StyledProperty<IEnumerable?> ItemsProperty =
        AvaloniaProperty.Register<TreemapControl, IEnumerable?>(nameof(Items));

    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<TreemapControl, ICommand?>(nameof(Command));

    private static readonly Color[] Palette =
    [
        Color.Parse("#E91E63"), Color.Parse("#9C27B0"), Color.Parse("#5727B0"), Color.Parse("#272AB0"),
        Color.Parse("#276BB0"), Color.Parse("#57ACDC"), Color.Parse("#57DCBE"), Color.Parse("#60C689")
    ];

    private readonly List<(Rect Rect, UsageRowViewModel Item)> _tiles = [];
    private UsageRowViewModel? _hover;

    static TreemapControl()
    {
        AffectsRender<TreemapControl>(ItemsProperty);
        ItemsProperty.Changed.AddClassHandler<TreemapControl>((t, e) =>
        {
            if (e.OldValue is INotifyCollectionChanged old)
            {
                old.CollectionChanged -= t.OnItemsChanged;
            }

            if (e.NewValue is INotifyCollectionChanged now)
            {
                now.CollectionChanged += t.OnItemsChanged;
            }
        });
    }

    public TreemapControl()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public IEnumerable? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    // The view model outlives the view: subscribe only while the control is shown.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Items is INotifyCollectionChanged items)
        {
            items.CollectionChanged -= OnItemsChanged;
            items.CollectionChanged += OnItemsChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (Items is INotifyCollectionChanged items)
        {
            items.CollectionChanged -= OnItemsChanged;
        }
    }

    public override void Render(DrawingContext context)
    {
        _tiles.Clear();
        var items = (Items?.OfType<UsageRowViewModel>() ?? []).Where(i => i.Item.Bytes > 0).Take(200).ToList();
        var bounds = new Rect(Bounds.Size);
        if (items.Count == 0 || bounds.Width < 10 || bounds.Height < 10)
        {
            return;
        }

        Layout(items, bounds);
        var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
        for (var i = 0; i < _tiles.Count; i++)
        {
            var (rect, item) = _tiles[i];
            var inner = rect.Deflate(1.5);
            if (inner.Width <= 0 || inner.Height <= 0)
            {
                continue;
            }

            var color = Palette[i % Palette.Length];
            var opacity = ReferenceEquals(item, _hover) ? 1.0 : item.IsFolder ? 0.85 : 0.55;
            context.DrawRectangle(new SolidColorBrush(color, opacity), null, inner, 4, 4);
            if (inner.Width > 70 && inner.Height > 34)
            {
                var label = new FormattedText($"{item.Name}\n{item.Size}", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 12,
                    Brushes.White) { MaxTextWidth = inner.Width - 12, MaxTextHeight = inner.Height - 8, Trimming = TextTrimming.CharacterEllipsis };
                context.DrawText(label, new Point(inner.X + 6, inner.Y + 4));
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var hit = Hit(e.GetPosition(this));
        if (!ReferenceEquals(hit, _hover))
        {
            _hover = hit;
            ToolTip.SetTip(this, hit is null ? null : $"{hit.Name} · {hit.Size} · {hit.PercentText}");
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return; // right click opens the context menu (export), it must not drill down
        }

        var hit = Hit(e.GetPosition(this));
        if (hit is { IsFolder: true } && Command?.CanExecute(hit) == true)
        {
            Command.Execute(hit);
        }
    }

    private UsageRowViewModel? Hit(Point point) => _tiles.FirstOrDefault(t => t.Rect.Contains(point)).Item;

    /// <summary>Squarified layout (Bruls, Huizing, van Wijk).</summary>
    private void Layout(List<UsageRowViewModel> items, Rect bounds)
    {
        var total = items.Sum(i => (double)i.Item.Bytes);
        var scale = bounds.Width * bounds.Height / total;
        var queue = new Queue<(UsageRowViewModel Item, double Area)>(items
            .OrderByDescending(i => i.Item.Bytes)
            .Select(i => (i, i.Item.Bytes * scale)));
        var free = bounds;
        while (queue.Count > 0)
        {
            var side = Math.Min(free.Width, free.Height);
            var row = new List<(UsageRowViewModel Item, double Area)> { queue.Dequeue() };
            while (queue.Count > 0)
            {
                var next = queue.Peek();
                if (Worst(row, side) >= Worst([.. row, next], side))
                {
                    row.Add(queue.Dequeue());
                }
                else
                {
                    break;
                }
            }

            free = Place(row, free);
        }
    }

    private static double Worst(List<(UsageRowViewModel Item, double Area)> row, double side)
    {
        var sum = row.Sum(r => r.Area);
        var max = row.Max(r => r.Area);
        var min = row.Min(r => r.Area);
        var s2 = side * side;
        var sum2 = sum * sum;
        return Math.Max(s2 * max / sum2, sum2 / (s2 * Math.Max(min, 1e-9)));
    }

    private Rect Place(List<(UsageRowViewModel Item, double Area)> row, Rect free)
    {
        var sum = row.Sum(r => r.Area);
        if (free.Width >= free.Height)
        {
            var width = sum / Math.Max(free.Height, 1e-9);
            var y = free.Y;
            foreach (var (item, area) in row)
            {
                var h = area / Math.Max(width, 1e-9);
                _tiles.Add((new Rect(free.X, y, width, h), item));
                y += h;
            }

            return new Rect(free.X + width, free.Y, Math.Max(0, free.Width - width), free.Height);
        }

        var height = sum / Math.Max(free.Width, 1e-9);
        var x = free.X;
        foreach (var (item, area) in row)
        {
            var w = area / Math.Max(height, 1e-9);
            _tiles.Add((new Rect(x, free.Y, w, height), item));
            x += w;
        }

        return new Rect(free.X, free.Y + height, free.Width, Math.Max(0, free.Height - height));
    }
}
