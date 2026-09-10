using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

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
    /// Crops to the opaque bounding box, pads it square, and resamples to the icon size with
    /// nearest-neighbour so pixel art stays crisp. Done by hand because Skia's resize only
    /// accepts decoded bitmaps, not writeable ones.
    /// </summary>
    private static unsafe WriteableBitmap CropAndScale(WriteableBitmap source)
    {
        using var input = source.Lock();
        var width = input.Size.Width;
        var height = input.Size.Height;
        var stride = input.RowBytes;
        var pixels = (byte*)input.Address;

        int left = width, top = height, right = -1, bottom = -1;
        for (var y = 0; y < height; y++)
        {
            var row = pixels + y * stride;
            for (var x = 0; x < width; x++)
            {
                // Alpha is the fourth byte in both Bgra8888 and Rgba8888.
                if (row[x * 4 + 3] == 0)
                {
                    continue;
                }

                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }

        if (right < left)
        {
            left = 0;
            top = 0;
            right = width - 1;
            bottom = height - 1;
        }

        var cropWidth = right - left + 1;
        var cropHeight = bottom - top + 1;
        var side = Math.Max(cropWidth, cropHeight);
        var padX = (side - cropWidth) / 2;
        var padY = (side - cropHeight) / 2;

        var result = new WriteableBitmap(new PixelSize(IconSize, IconSize), source.Dpi, source.Format, source.AlphaFormat);
        using var output = result.Lock();
        var destination = (byte*)output.Address;
        new Span<byte>(destination, output.RowBytes * IconSize).Clear();

        for (var dy = 0; dy < IconSize; dy++)
        {
            var sy = dy * side / IconSize - padY;
            if (sy < 0 || sy >= cropHeight)
            {
                continue;
            }

            var sourceRow = pixels + (top + sy) * stride;
            var targetRow = destination + dy * output.RowBytes;

            for (var dx = 0; dx < IconSize; dx++)
            {
                var sx = dx * side / IconSize - padX;
                if (sx < 0 || sx >= cropWidth)
                {
                    continue;
                }

                *(uint*)(targetRow + dx * 4) = *(uint*)(sourceRow + (left + sx) * 4);
            }
        }

        return result;
    }
}
