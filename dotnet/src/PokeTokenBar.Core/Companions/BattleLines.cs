namespace PokeTokenBar.Core.Companions;

/// <summary>
/// Battle-message style lines about the companion, worded after the games' own battle text:
/// items Claude uses on it, held items it uses by itself, and moves it knows.
/// </summary>
/// <remarks>
/// The draw is seeded by the species alone, so the same companion always gets the same lines
/// and a refresh that changes nothing rewrites nothing. The games end these with "!"; it is
/// dropped because Claude Code's spinner appends its own ellipsis.
/// </remarks>
public static class BattleLines
{
    public const int UsedItemCount = 2;

    public const int HeldItemCount = 2;

    public const int MoveCount = 6;

    /// <summary>Healing berries, used by Claude or eaten by the holder alike.</summary>
    private static readonly string[] Berries =
    [
        "Cheri Berry", "Chesto Berry", "Pecha Berry", "Rawst Berry", "Aspear Berry", "Leppa Berry",
        "Oran Berry", "Persim Berry", "Lum Berry", "Sitrus Berry", "Figy Berry", "Wiki Berry",
        "Mago Berry", "Aguav Berry", "Iapapa Berry",
    ];

    /// <summary>Potions, status cures, revives and PP restorers, plus the berries.</summary>
    private static readonly string[] UsableItems =
    [
        "Potion", "Super Potion", "Hyper Potion", "Max Potion", "Full Restore", "Fresh Water",
        "Soda Pop", "Lemonade", "Moomoo Milk", "Antidote", "Burn Heal", "Ice Heal", "Awakening",
        "Paralyze Heal", "Full Heal", "Heal Powder", "Revive", "Max Revive", "Ether", "Elixir",
        .. Berries,
    ];

    /// <summary>The shortest of the games' held-item messages.</summary>
    private static readonly string[] HeldItemTemplates =
    [
        .. Berries.Select(static berry => "{pokemon} ate its " + berry),
        "{pokemon} hung on using its Focus Sash",
        "{pokemon} hung on using its Focus Band",
    ];

    /// <summary>Used when the species' own moves are not known yet.</summary>
    private static readonly string[] GenericMoves =
    [
        "Tackle", "Scratch", "Pound", "Growl", "Leer", "Tail Whip", "Quick Attack", "Headbutt",
        "Body Slam", "Take Down", "Protect", "Rest",
    ];

    /// <summary>
    /// <see cref="UsedItemCount"/> item lines, <see cref="HeldItemCount"/> held-item lines and
    /// up to <see cref="MoveCount"/> move lines — fewer when the species knows fewer moves.
    /// </summary>
    /// <param name="speciesId">Seeds the draw.</param>
    /// <param name="pokemon">How the companion is shown: an icon or a name.</param>
    /// <param name="moves">The species' level-up moves; empty falls back to generic ones.</param>
    public static IReadOnlyList<string> For(int speciesId, string pokemon, IReadOnlyList<string> moves)
    {
        ArgumentNullException.ThrowIfNull(pokemon);
        ArgumentNullException.ThrowIfNull(moves);

        var random = new Random(speciesId);
        var pool = moves.Count > 0 ? moves : GenericMoves;

        // Held first, so a berry the companion eats is not also one Claude uses on it.
        var held = Draw(HeldItemTemplates, HeldItemCount, random).ToArray();
        var usable = UsableItems.Where(item => !held.Any(line => line.EndsWith(" " + item, StringComparison.Ordinal))).ToArray();

        var lines = new List<string>(UsedItemCount + HeldItemCount + MoveCount);
        lines.AddRange(Draw(usable, UsedItemCount, random).Select(static item => "Claude used " + item + " on {pokemon}"));
        lines.AddRange(held);
        lines.AddRange(Draw(pool, MoveCount, random).Select(static move => "{pokemon} used " + move));

        return [.. lines.Select(line => line.Replace("{pokemon}", pokemon, StringComparison.Ordinal))];
    }

    /// <summary>Up to <paramref name="count"/> distinct entries, in drawn order.</summary>
    private static IEnumerable<string> Draw(IReadOnlyList<string> source, int count, Random random)
    {
        var shuffled = source.ToArray();
        random.Shuffle(shuffled);
        return shuffled.Take(count);
    }
}
