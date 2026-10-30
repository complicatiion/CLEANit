using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace CLEANit.App.Services;

/// <summary>Renders a control to an image file (JPEG through SkiaSharp, which Avalonia already ships; PNG directly).</summary>
public static class ImageExport
{
    /// <summary>Renders <paramref name="visual"/> at the screen scaling on <paramref name="background"/> and saves it.</summary>
    public static void Save(Visual visual, double scaling, Color background, string path)
    {
        var size = visual.Bounds.Size;
        if (size.Width < 1 || size.Height < 1)
        {
            throw new InvalidOperationException("Nothing to export.");
        }

        scaling = Math.Clamp(scaling, 1, 4);
        var pixels = new PixelSize((int)Math.Ceiling(size.Width * scaling), (int)Math.Ceiling(size.Height * scaling));
        using var bitmap = new RenderTargetBitmap(pixels, new Vector(96 * scaling, 96 * scaling));
        bitmap.Render(visual);

        using var png = new MemoryStream();
        bitmap.Save(png);
        if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllBytes(path, png.ToArray());
            return;
        }

        // JPEG has no transparency: draw the rendering on the page background first.
        png.Position = 0;
        using var source = SKBitmap.Decode(png) ?? throw new InvalidOperationException("Rendering failed.");
        using var target = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(target))
        {
            canvas.Clear(new SKColor(background.R, background.G, background.B));
            canvas.DrawBitmap(source, 0, 0);
        }

        using var image = SKImage.FromBitmap(target);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 92);
        using var file = File.Create(path);
        data.SaveTo(file);
    }
}
