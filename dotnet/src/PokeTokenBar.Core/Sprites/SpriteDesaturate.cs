namespace PokeTokenBar.Core.Sprites;

/// <summary>
/// The grey a sprite pixel becomes for a species whose line is still being raised, so the
/// Pokédex reads at a glance which entries are finished. Pure per-pixel arithmetic, with no
/// bitmap type of its own — PokeTokenBar.Tray's <c>CompanionWindow.Desaturate</c> (Avalonia
/// <c>WriteableBitmap</c>) and the Unity companion's <c>CompanionTextures</c>
/// (<c>Texture2D</c>/<c>Color32</c>) both call <see cref="Luminance"/> for it.
/// </summary>
public static class SpriteDesaturate
{
    /// <summary>Luminance-weighted grey for one pixel. Channel order does not matter for an average.</summary>
    public static byte Luminance(byte red, byte green, byte blue) =>
        (byte)(((red * 30) + (green * 59) + (blue * 11)) / 100);
}
