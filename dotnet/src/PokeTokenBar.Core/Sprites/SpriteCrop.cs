namespace PokeTokenBar.Core.Sprites;

/// <summary>
/// The opaque bounding box of a sprite, padded back to a square — for showing the creature
/// PokeAPI's sprite floats in a sea of transparent canvas rather than the canvas itself.
/// </summary>
public readonly record struct SpriteSquare
{
    public required int Left { get; init; }

    public required int Top { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    /// <summary>Side of the square the cropped rectangle is centred in.</summary>
    public required int Side { get; init; }

    /// <summary>Left padding inside the square; <see cref="PadY"/> is the top padding.</summary>
    public required int PadX { get; init; }

    public required int PadY { get; init; }
}

/// <summary>
/// Finds the crop a sprite needs. Pure geometry over an alpha channel, with no bitmap type of
/// its own, so every host crops through the same rule while still decoding and copying pixels
/// with its own imaging APIs — PokeTokenBar.Tray's <c>TrayIconRenderer</c> (Avalonia
/// <c>WriteableBitmap</c>) and the Unity companion's <c>CompanionTextures</c>
/// (<c>Texture2D</c>/<c>Color32</c>) both call <see cref="Compute"/>.
/// </summary>
public static class SpriteCrop
{
    /// <summary>
    /// <paramref name="alphaAt"/> returns the alpha byte at (x, y); 0 means transparent. A
    /// fully-transparent sprite (or a decoder that cannot report alpha) falls back to the full
    /// canvas rather than degenerating to an empty crop.
    /// </summary>
    public static SpriteSquare Compute(int width, int height, Func<int, int, byte> alphaAt)
    {
        ArgumentNullException.ThrowIfNull(alphaAt);

        var left = width;
        var right = -1;
        var top = height;
        var bottom = -1;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (alphaAt(x, y) == 0)
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

        return new SpriteSquare
        {
            Left = left,
            Top = top,
            Width = cropWidth,
            Height = cropHeight,
            Side = side,
            PadX = (side - cropWidth) / 2,
            PadY = (side - cropHeight) / 2,
        };
    }
}
