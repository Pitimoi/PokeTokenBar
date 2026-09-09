using System.Text;

namespace PokeTokenBar.Core.Usage;

/// <summary>
/// Makes strings taken from untrusted logs safe to hand to a host for display.
/// </summary>
/// <remarks>
/// The host renders in a webview, so a value carrying markup or control characters is an
/// injection vector. Sanitising here rather than only in the host means the guarantee holds
/// for every host, and does not depend on a renderer remembering to escape.
/// </remarks>
public static class DisplayText
{
    public const int DefaultMaxLength = 64;

    private const string Fallback = "unknown";

    /// <summary>
    /// Reduces a machine identifier (model name, provider id) to an allowlisted charset and
    /// caps its length. Real values — <c>claude-opus-5</c>, <c>gpt-5-codex</c> — pass through
    /// unchanged; anything else is replaced character-wise so the result stays inert in any
    /// rendering context.
    /// </summary>
    public static string SanitizeIdentifier(string? value, int maxLength = DefaultMaxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Fallback;
        }

        var source = value.AsSpan().Trim();
        if (source.Length > maxLength)
        {
            source = source[..maxLength];
        }

        var builder = new StringBuilder(source.Length);
        foreach (var c in source)
        {
            builder.Append(IsAllowed(c) ? c : '_');
        }

        var result = builder.ToString();
        return result.Length == 0 || result.All(static c => c == '_') ? Fallback : result;
    }

    private static bool IsAllowed(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or ':' or '/' or '@' or '+';
}
