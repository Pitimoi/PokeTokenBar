using System.Text.Json;

namespace PokeTokenBar.Core.Usage;

/// <summary>Reads token counts out of untrusted JSON, rejecting values that cannot be real.</summary>
public static class TokenCount
{
    /// <summary>
    /// Largest accepted single-message count. Leaves room for ~9.2e9 entries before a
    /// <see cref="long"/> accumulator could overflow.
    /// </summary>
    public const long Implausible = 1_000_000_000;

    /// <summary>
    /// Reads <paramref name="name"/> from <paramref name="usage"/>. An absent field yields 0
    /// and succeeds; a present field that is non-numeric, negative, fractional, or above
    /// <see cref="Implausible"/> fails, so the caller drops the whole entry rather than
    /// clamping — a clamped value would go on to dominate every aggregate it reaches.
    /// </summary>
    public static bool TryRead(JsonElement usage, string name, out long value)
    {
        value = 0;

        if (!usage.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (field.ValueKind != JsonValueKind.Number || !field.TryGetInt64(out var raw))
        {
            return false;
        }

        if (raw < 0 || raw > Implausible)
        {
            return false;
        }

        value = raw;
        return true;
    }
}
