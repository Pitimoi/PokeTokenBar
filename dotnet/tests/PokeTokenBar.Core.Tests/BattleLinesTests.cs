using PokeTokenBar.Core.Companions;

namespace PokeTokenBar.Core.Tests;

public sealed class BattleLinesTests
{
    private static readonly string[] CharmanderMoves =
    [
        "Dragon Breath", "Dragon Rage", "Ember", "Fire Fang", "Fire Spin", "Flamethrower",
        "Flare Blitz", "Growl", "Scratch", "Slash", "Smokescreen",
    ];

    [Fact]
    public void SplitsTwoUsedItemsTwoHeldItemsAndSixMoves()
    {
        var lines = BattleLines.For(4, "Charmander", CharmanderMoves);

        Assert.Equal(10, lines.Count);
        Assert.Equal(2, lines.Count(static line => line.StartsWith("Claude used ", StringComparison.Ordinal)));
        Assert.Equal(2, lines.Count(static line => line.Contains(" ate its ", StringComparison.Ordinal)
                                                   || line.Contains(" hung on using its ", StringComparison.Ordinal)));

        var moves = lines.Where(static line => line.StartsWith("Charmander used ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(6, moves.Length);
        Assert.Equal(6, moves.Distinct().Count());
        Assert.All(moves, move => Assert.Contains(move["Charmander used ".Length..], CharmanderMoves));
    }

    [Fact]
    public void TheSameSpeciesAlwaysDrawsTheSameLines()
    {
        // A refresh that changes nothing must not rewrite the settings file.
        Assert.Equal(BattleLines.For(4, "Charmander", CharmanderMoves), BattleLines.For(4, "Charmander", CharmanderMoves));
    }

    [Fact]
    public void AnotherSpeciesDrawsOtherLines()
    {
        Assert.NotEqual(BattleLines.For(4, "x", CharmanderMoves), BattleLines.For(5, "x", CharmanderMoves));
    }

    [Fact]
    public void NeverUsesAndEatsTheSameBerry()
    {
        for (var speciesId = 1; speciesId <= 649; speciesId++)
        {
            var lines = BattleLines.For(speciesId, "x", CharmanderMoves);
            var eaten = lines
                .Where(static line => line.StartsWith("x ate its ", StringComparison.Ordinal))
                .Select(static line => line["x ate its ".Length..]);

            Assert.All(eaten, berry => Assert.DoesNotContain("Claude used " + berry + " on x", lines));
        }
    }

    [Fact]
    public void ASpeciesWithFewMovesUsesOnlyThose()
    {
        var lines = BattleLines.For(132, "Ditto", ["Transform"]);

        Assert.Equal(5, lines.Count);
        Assert.Single(lines, static line => line == "Ditto used Transform");
    }

    [Fact]
    public void FallsBackToGenericMovesWhenNoneAreKnown()
    {
        var lines = BattleLines.For(4, "Charmander", []);

        Assert.Equal(10, lines.Count);
        Assert.Equal(6, lines.Count(static line => line.StartsWith("Charmander used ", StringComparison.Ordinal)));
    }

    [Fact]
    public void NamesTheCompanionInEveryLineAndEndsWithoutPunctuation()
    {
        // The spinner appends its own ellipsis, so the games' "!" is left off.
        var lines = BattleLines.For(25, "<pkmn>", ["Thunder Shock"]);

        Assert.All(lines, line => Assert.Contains("<pkmn>", line, StringComparison.Ordinal));
        Assert.All(lines, line => Assert.False(line.EndsWith('!')));
    }
}
