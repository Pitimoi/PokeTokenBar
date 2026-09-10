using System.Globalization;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace PokeTokenBar.Tray;

/// <summary>Picks the colour that best stands for a sprite, for places that can show a dot but not a picture.</summary>
internal static class SpriteColor
{
    /// <summary>
    /// The most common colour after quantising, ignoring outlines and highlights: a plain
    /// average would drag every sprite towards the brown of its black outline.
    /// </summary>
    public static unsafe string? Dominant(string path)
    {
        using var stream = File.OpenRead(path);
        using var bitmap = WriteableBitmap.Decode(stream);
        using var buffer = bitmap.Lock();

        var bgra = buffer.Format == PixelFormats.Bgra8888;
        var premultiplied = bitmap.AlphaFormat == AlphaFormat.Premul;
        var pixels = (byte*)buffer.Address;

        var counts = new int[4096];
        var sums = new long[4096 * 3];

        for (var y = 0; y < buffer.Size.Height; y++)
        {
            var row = pixels + y * buffer.RowBytes;
            for (var x = 0; x < buffer.Size.Width; x++)
            {
                var p = row + x * 4;
                int a = p[3];
                if (a < 128)
                {
                    continue;
                }

                int r = bgra ? p[2] : p[0];
                int g = p[1];
                int b = bgra ? p[0] : p[2];
                if (premultiplied)
                {
                    r = r * 255 / a;
                    g = g * 255 / a;
                    b = b * 255 / a;
                }

                var luminance = (r * 299 + g * 587 + b * 114) / 1000;
                if (luminance is < 40 or > 240)
                {
                    continue;
                }

                var bucket = (r >> 4) << 8 | (g >> 4) << 4 | (b >> 4);
                counts[bucket]++;
                sums[bucket * 3] += r;
                sums[bucket * 3 + 1] += g;
                sums[bucket * 3 + 2] += b;
            }
        }

        var best = 0;
        for (var i = 1; i < counts.Length; i++)
        {
            if (counts[i] > counts[best])
            {
                best = i;
            }
        }

        if (counts[best] == 0)
        {
            return null;
        }

        var n = counts[best];
        return string.Create(
            CultureInfo.InvariantCulture,
            $"#{sums[best * 3] / n:X2}{sums[best * 3 + 1] / n:X2}{sums[best * 3 + 2] / n:X2}");
    }
}
