using System.Globalization;

namespace PokeTokenBar.Core.Usage;

/// <summary>
/// The single definition of a usage day. Bucketing and "today" must agree exactly, so both
/// come from here — two call sites formatting their own would drift silently.
/// </summary>
public static class LocalDay
{
    public const string Format = "yyyy-MM-dd";

    /// <summary>
    /// The local calendar day containing <paramref name="instant"/>. Transcripts record UTC,
    /// so converting is what makes a day mean the user's day rather than UTC's.
    /// </summary>
    public static string For(DateTimeOffset instant) =>
        instant.ToLocalTime().ToString(Format, CultureInfo.InvariantCulture);

    public static string Today() => For(DateTimeOffset.Now);
}
