using System.Globalization;

namespace PokeTokenBar.Tray;

internal static class TokenFormat
{
    /// <summary>Compact count for a menu or tooltip: 1.2K, 3.4M, 1.1B.</summary>
    public static string Compact(long total)
    {
        if (total < 0)
        {
            return "—";
        }

        return total switch
        {
            >= 1_000_000_000 => Scaled(total, 1_000_000_000, "B"),
            >= 1_000_000 => Scaled(total, 1_000_000, "M"),
            >= 1_000 => Scaled(total, 1_000, "K"),
            _ => total.ToString(CultureInfo.InvariantCulture),
        };
    }

    public static string Cost(double usd) => usd.ToString("$0.00", CultureInfo.InvariantCulture);

    private static string Scaled(long total, long unit, string suffix) =>
        (total / (double)unit).ToString("0.0", CultureInfo.InvariantCulture) + suffix;
}
