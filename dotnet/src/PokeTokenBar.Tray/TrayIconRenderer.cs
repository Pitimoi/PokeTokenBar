using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PokeTokenBar.Core.Sprites;

namespace PokeTokenBar.Tray;

/// <summary>
/// Turns a PokeAPI sprite into a tray icon. Sprites are 96x96 with the creature floating in a
/// sea of transparent padding, so shown as-is at tray size it is a smudge; cropping to the
/// opaque bounds first is what makes it legible.
/// </summary>
internal static class TrayIconRenderer
{
    private const int IconSize = 64;

    public static WindowIcon FromSprite(string path)
    {
        using var stream = File.OpenRead(path);
        using var source = WriteableBitmap.Decode(stream);
        using var icon = CropAndScale(source);
        return new WindowIcon(icon);
    }

    /// <summary>
    /// The sprite cropped to its opaque bounds and padded square, at native resolution — for
    /// showing the creature rather than the canvas it floats in.
    /// </summary>
    public static WriteableBitmap CropSquare(string path)
    {
        using var stream = File.OpenRead(path);
        using var source = WriteableBitmap.Decode(stream);
        return CropAndScale(source, targetSize: 0);
    }

    /// <summary>Writes the cropped, square icon as a PNG for tools that render images themselves.</summary>
    public static void SaveIcon(string spritePath, string destination)
    {
        using var stream = File.OpenRead(spritePath);
        using var source = WriteableBitmap.Decode(stream);
        using var icon = CropAndScale(source);
        icon.Save(destination);
    }

    /// <summary>A pokéball, for when no sprite is on disk yet.</summary>
    public static WindowIcon Placeholder()
    {
        using var target = new RenderTargetBitmap(new PixelSize(IconSize, IconSize));
        using (var context = target.CreateDrawingContext())
        {
            var center = new Point(IconSize / 2d, IconSize / 2d);
            const double radius = IconSize / 2d - 3;
            var outline = new Pen(Brushes.Black, 4);

            context.DrawEllipse(Brushes.White, outline, center, radius, radius);
            context.FillRectangle(Brushes.Black, new Rect(3, center.Y - 2, IconSize - 6, 4));
            context.DrawEllipse(Brushes.White, outline, center, 7, 7);
        }

        return new WindowIcon(target);
    }

    /// <summary>
    /// Crops to the opaque bounding box, pads it square, and resamples to
    /// <paramref name="targetSize"/> (zero keeps the native size) with nearest-neighbour so pixel
    /// art stays crisp. Done by hand because Skia's resize only accepts decoded bitmaps, not
    /// writeable ones.
    /// </summary>
    private static unsafe WriteableBitmap CropAndScale(WriteableBitmap source, int targetSize = IconSize)
    {
        using var input = source.Lock();
        var width = input.Size.Width;
        var height = input.Size.Height;
        var stride = input.RowBytes;
        var pixels = (byte*)input.Address;

        // Alpha is the fourth byte in both Bgra8888 and Rgba8888.
        var square = SpriteCrop.Compute(width, height, (x, y) => pixels[y * stride + x * 4 + 3]);
        var size = targetSize > 0 ? targetSize : square.Side;

        var result = new WriteableBitmap(new PixelSize(size, size), source.Dpi, source.Format, source.AlphaFormat);
        using var output = result.Lock();
        var destination = (byte*)output.Address;
        new Span<byte>(destination, output.RowBytes * size).Clear();

        for (var dy = 0; dy < size; dy++)
        {
            var sy = (dy * square.Side / size) - square.PadY;
            if (sy < 0 || sy >= square.Height)
            {
                continue;
            }

            var sourceRow = pixels + (square.Top + sy) * stride;
            var targetRow = destination + dy * output.RowBytes;

            for (var dx = 0; dx < size; dx++)
            {
                var sx = (dx * square.Side / size) - square.PadX;
                if (sx < 0 || sx >= square.Width)
                {
                    continue;
                }

                *(uint*)(targetRow + dx * 4) = *(uint*)(sourceRow + (square.Left + sx) * 4);
            }
        }

        return result;
    }
}
