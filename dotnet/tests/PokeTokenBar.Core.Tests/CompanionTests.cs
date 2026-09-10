using PokeTokenBar.Core.Companions;

namespace PokeTokenBar.Core.Tests;

public sealed class RarityTests
{
    [Fact]
    public void SortRankAscendsByValue()
    {
        // Pinned because a guarantee compares ranks: reversing this order would let a promised
        // "rare or better" silently accept a common.
        Assert.True(Rarity.Common.SortRank() < Rarity.Uncommon.SortRank());
        Assert.True(Rarity.Uncommon.SortRank() < Rarity.Rare.SortRank());
        Assert.True(Rarity.Rare.SortRank() < Rarity.Legendary.SortRank());
    }

    [Theory]
    [InlineData(3, Rarity.Rare)]
    [InlineData(45, Rarity.Rare)]
    [InlineData(46, Rarity.Uncommon)]
    [InlineData(120, Rarity.Uncommon)]
    [InlineData(121, Rarity.Common)]
    [InlineData(255, Rarity.Common)]
    public void ClassifiesByCaptureRate(int captureRate, Rarity expected)
    {
        Assert.Equal(expected, RarityRules.From(captureRate, isLegendary: false, isMythical: false));
    }

    [Fact]
    public void LegendaryFlagsOutrankCaptureRate()
    {
        Assert.Equal(Rarity.Legendary, RarityRules.From(255, isLegendary: true, isMythical: false));
        Assert.Equal(Rarity.Legendary, RarityRules.From(255, isLegendary: false, isMythical: true));
    }

    [Fact]
    public void LegendaryHasNoCaptureRateCeiling()
    {
        // It is decided by flags, so no capture rate implies it.
        Assert.Null(Rarity.Legendary.CaptureRateCeiling());
        Assert.False(Rarity.Legendary.Includes(1));
    }

    [Fact]
    public void EveryLegendaryStillPassesTheRareFilter()
    {
        // Legendaries all sit at capture rate 45 or below, which is why "rare or better"
        // filters include them despite the missing ceiling.
        Assert.True(Rarity.Rare.Includes(45));
    }
}

public sealed class PokemonBalanceTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(9)]
    public void StageCostsSumToTheGraduationTotal(int forms)
    {
        // The identity that makes graduation effort equal across lines of the same rarity,
        // whatever their length. Rounding can drift by at most one token per stage.
        foreach (var rarity in Enum.GetValues<Rarity>())
        {
            var sum = 0L;
            for (var stage = 0; stage < forms; stage++)
            {
                sum += PokemonBalance.PhaseThreshold(rarity, forms, stage);
            }

            var total = PokemonBalance.GraduationTotal(rarity);
            Assert.InRange(sum, total - forms, total + forms);
        }
    }

    [Fact]
    public void LaterFormsCostMore()
    {
        var first = PokemonBalance.PhaseThreshold(Rarity.Common, totalForms: 3, stageIndex: 0);
        var second = PokemonBalance.PhaseThreshold(Rarity.Common, totalForms: 3, stageIndex: 1);
        var third = PokemonBalance.PhaseThreshold(Rarity.Common, totalForms: 3, stageIndex: 2);

        Assert.True(first < second);
        Assert.True(second < third);
    }

    [Fact]
    public void RarerLinesCostMore()
    {
        var previous = 0L;
        foreach (var rarity in new[] { Rarity.Common, Rarity.Uncommon, Rarity.Rare, Rarity.Legendary })
        {
            var total = PokemonBalance.GraduationTotal(rarity);
            Assert.True(total > previous, $"{rarity} must exceed the previous tier");
            previous = total;
        }
    }

    [Fact]
    public void LegendaryTotalExceedsInt32()
    {
        // Why these are long: the constant itself does not fit in an int.
        Assert.True(PokemonBalance.GraduationTotal(Rarity.Legendary) > int.MaxValue);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-99)]
    public void TreatsNonsensicalFormCountsAsSingleForm(int forms)
    {
        // Corrupt or older on-disk state must not divide by zero.
        var threshold = PokemonBalance.PhaseThreshold(Rarity.Common, forms, stageIndex: 0);

        Assert.Equal(PokemonBalance.GraduationTotal(Rarity.Common), threshold);
    }
}

public sealed class CompanionProgressionTests
{
    private static Companion Fresh(int forms = 3, Rarity rarity = Rarity.Common) =>
        Companion.Hatch(Enumerable.Range(1, forms).ToArray(), rarity);

    [Fact]
    public void StartsOnItsFirstForm()
    {
        var companion = Fresh();

        Assert.Equal(1, companion.CurrentSpeciesId);
        Assert.Equal(0, companion.StageIndex);
        Assert.False(companion.IsFinalStage);
        Assert.Equal(0d, companion.StageProgress);
        Assert.Equal([1], companion.ReachedForms);
    }

    [Fact]
    public void EvolvesOnReachingTheThreshold()
    {
        var companion = Fresh();

        var advance = CompanionProgression.Apply(companion, companion.StageThreshold);

        Assert.Equal([2], advance.Evolutions);
        Assert.Equal(2, advance.Companion.CurrentSpeciesId);
        Assert.Equal([1, 2], advance.Companion.ReachedForms);
    }

    [Fact]
    public void DoesNotEvolveJustBelowTheThreshold()
    {
        var companion = Fresh();

        var advance = CompanionProgression.Apply(companion, companion.StageThreshold - 1);

        Assert.Empty(advance.Evolutions);
        Assert.Equal(1, advance.Companion.CurrentSpeciesId);
    }

    [Fact]
    public void CarriesSurplusIntoTheNextForm()
    {
        // No tokens may be lost at a boundary.
        var companion = Fresh();

        var advance = CompanionProgression.Apply(companion, companion.StageThreshold + 1234);

        Assert.Equal(1234, advance.Companion.TokensAtStage);
    }

    [Fact]
    public void CrossesSeveralFormsInOneBatch()
    {
        // Usage arrives in batches after a refresh interval, and a large batch legitimately
        // spans more than one threshold.
        var companion = Fresh();
        var wholeLine = PokemonBalance.GraduationTotal(Rarity.Common);

        var advance = CompanionProgression.Apply(companion, wholeLine);

        Assert.Equal([2, 3], advance.Evolutions);
        Assert.True(advance.Companion.IsFinalStage);
        Assert.True(advance.Companion.HasGraduated);
        Assert.True(advance.Graduated);
    }

    [Fact]
    public void StopsAtTheFinalFormRatherThanRunningOffThePath()
    {
        var companion = Fresh(forms: 2);

        var advance = CompanionProgression.Apply(companion, PokemonBalance.GraduationTotal(Rarity.Common) * 10);

        Assert.Equal(2, advance.Companion.CurrentSpeciesId);
        Assert.Equal(1, advance.Companion.StageIndex);
        Assert.True(advance.Graduated);
    }

    [Fact]
    public void IgnoresNegativeTokenBatches()
    {
        var companion = Fresh() with { TokensAtStage = 500 };

        var advance = CompanionProgression.Apply(companion, -10_000);

        Assert.Equal(500, advance.Companion.TokensAtStage);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(99)]
    public void ClampsACorruptStageIntoThePath(int corruptStage)
    {
        // On-disk state can be corrupt or from an older layout; a companion with no displayable
        // form is worse than one showing a form it has reached.
        var companion = Fresh() with { StageIndex = corruptStage };

        Assert.InRange(companion.SafeStageIndex, 0, companion.TotalForms - 1);
        Assert.Contains(companion.CurrentSpeciesId, companion.SpeciesPath);
        Assert.NotEmpty(companion.ReachedForms);

        var advance = CompanionProgression.Apply(companion, 0);
        Assert.InRange(advance.Companion.StageIndex, 0, companion.TotalForms - 1);
    }

    [Fact]
    public void SurvivesAnEmptySpeciesPath()
    {
        var companion = Companion.Hatch([], Rarity.Common);

        Assert.Equal(1, companion.TotalForms);
        Assert.True(companion.IsFinalStage);
        Assert.Equal(0, companion.CurrentSpeciesId);
    }

    [Fact]
    public void ProgressIsBoundedEvenWhenTokensExceedTheThreshold()
    {
        var companion = Fresh(forms: 1) with { TokensAtStage = long.MaxValue / 2 };

        Assert.InRange(companion.StageProgress, 0d, 1d);
    }

    [Fact]
    public void SingleFormLineGraduatesAtTheFullTotal()
    {
        var companion = Fresh(forms: 1, rarity: Rarity.Legendary);

        Assert.Equal(PokemonBalance.GraduationTotal(Rarity.Legendary), companion.StageThreshold);

        var advance = CompanionProgression.Apply(companion, companion.StageThreshold);
        Assert.True(advance.Graduated);
        Assert.Empty(advance.Evolutions);
    }
}

public sealed class PendingSpeciesIdTests
{
    private static Companion Fresh(int forms = 3, Rarity rarity = Rarity.Common) =>
        Companion.Hatch(Enumerable.Range(1, forms).ToArray(), rarity);

    [Fact]
    public void TheFirstFormOfAFreshLineIsPending()
    {
        Assert.Equal(1, Fresh().PendingSpeciesId);
    }

    [Fact]
    public void AMidLineFormStaysPendingRegardlessOfEarlierFormsAlreadyReached()
    {
        var companion = Fresh() with { StageIndex = 1 };

        Assert.Equal(2, companion.PendingSpeciesId);
    }

    [Fact]
    public void TheFinalFormIsPendingUntilItsThresholdIsMet()
    {
        var companion = Fresh() with
        {
            StageIndex = 2,
            TokensAtStage = PokemonBalance.PhaseThreshold(Rarity.Common, 3, 2) - 1,
        };

        Assert.False(companion.HasGraduated);
        Assert.Equal(3, companion.PendingSpeciesId);
    }

    [Fact]
    public void NothingIsPendingOnceTheLineHasGraduated()
    {
        var companion = Fresh() with
        {
            StageIndex = 2,
            TokensAtStage = PokemonBalance.PhaseThreshold(Rarity.Common, 3, 2),
        };

        Assert.True(companion.HasGraduated);
        Assert.Null(companion.PendingSpeciesId);
    }

    [Fact]
    public void NothingIsPendingWithoutAnActiveLine()
    {
        var companion = new Companion { SpeciesPath = [], StageIndex = 0, TokensAtStage = 0, Rarity = Rarity.Common };

        Assert.Null(companion.PendingSpeciesId);
    }
}
