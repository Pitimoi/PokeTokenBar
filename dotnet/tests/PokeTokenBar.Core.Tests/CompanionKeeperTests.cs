using PokeTokenBar.Core.Companions;

namespace PokeTokenBar.Core.Tests;

public sealed class EvolutionLinesTests
{
    [Fact]
    public void EverySpeciesIdIsInSpriteRange()
    {
        foreach (var line in EvolutionLines.All)
        {
            Assert.NotEmpty(line.SpeciesPath);
            Assert.All(line.SpeciesPath, id => Assert.InRange(id, 1, 1400));
        }
    }

    [Fact]
    public void TheSameSeedAlwaysDrawsTheSameLine()
    {
        // A restart must not redraw a different species.
        for (var seed = 0; seed < 500; seed += 37)
        {
            Assert.Equal(EvolutionLines.FromSeed(seed), EvolutionLines.FromSeed(seed));
        }
    }

    [Fact]
    public void DrawsSomethingForAnySeedIncludingNegatives()
    {
        foreach (var seed in new[] { int.MinValue, -1, 0, 1, int.MaxValue })
        {
            Assert.NotNull(EvolutionLines.FromSeed(seed));
            Assert.NotEmpty(EvolutionLines.FromSeed(seed).SpeciesPath);
        }
    }

    [Fact]
    public void CommonLinesAreDrawnMoreOftenThanLegendary()
    {
        var counts = new Dictionary<Rarity, int>();
        for (var seed = 0; seed < 20_000; seed++)
        {
            var rarity = EvolutionLines.FromSeed(seed).Rarity;
            counts[rarity] = counts.GetValueOrDefault(rarity) + 1;
        }

        Assert.True(counts[Rarity.Common] > counts.GetValueOrDefault(Rarity.Uncommon));
        Assert.True(counts.GetValueOrDefault(Rarity.Uncommon) > counts.GetValueOrDefault(Rarity.Legendary));
    }
}

public sealed class CompanionKeeperTests
{
    [Fact]
    public void FirstReadingOfADayCountsInFull()
    {
        var state = CompanionState.Hatch(1);

        var update = CompanionKeeper.Apply(state, "2026-09-09", 1_000_000);

        Assert.Equal(1_000_000, update.State.TokensAtStage);
        Assert.Equal("2026-09-09", update.State.WatermarkDay);
        Assert.Equal(1_000_000, update.State.WatermarkTokens);
    }

    [Fact]
    public void OnlyTheGrowthSinceTheLastReadingCounts()
    {
        // Readings are absolute totals; treating each as new usage would multiply progress by
        // the number of refreshes.
        var state = CompanionState.Hatch(1);

        var first = CompanionKeeper.Apply(state, "2026-09-09", 1_000_000);
        var second = CompanionKeeper.Apply(first.State, "2026-09-09", 1_500_000);

        Assert.Equal(1_500_000, second.State.TokensAtStage);
    }

    [Fact]
    public void ARefreshWithNoNewUsageChangesNothing()
    {
        var state = CompanionState.Hatch(1);
        var first = CompanionKeeper.Apply(state, "2026-09-09", 1_000_000);

        var second = CompanionKeeper.Apply(first.State, "2026-09-09", 1_000_000);

        Assert.Equal(first.State.TokensAtStage, second.State.TokensAtStage);
        Assert.Empty(second.Evolutions);
    }

    [Fact]
    public void ADayRolloverRestartsTheWatermarkWithoutLosingProgress()
    {
        // Today's total resets at midnight, so a stale watermark would compute a negative delta.
        var state = CompanionState.Hatch(1) with { IsEgg = false };
        var yesterday = CompanionKeeper.Apply(state, "2026-09-09", 5_000_000);

        var today = CompanionKeeper.Apply(yesterday.State, "2026-09-10", 2_000_000);

        Assert.Equal(7_000_000, today.State.TokensAtStage);
        Assert.Equal("2026-09-10", today.State.WatermarkDay);
    }

    [Fact]
    public void ProgressNeverMovesBackwards()
    {
        // A lower reading than the watermark — a deleted transcript, a clock change — must not
        // subtract progress.
        var state = CompanionState.Hatch(1);
        var first = CompanionKeeper.Apply(state, "2026-09-09", 5_000_000);

        var second = CompanionKeeper.Apply(first.State, "2026-09-09", 1_000);

        Assert.Equal(first.State.TokensAtStage, second.State.TokensAtStage);
    }

    [Fact]
    public void GraduationRecordsTheLineAndHatchesAReplacement()
    {
        var state = CompanionState.Hatch(1) with { IsEgg = false };
        var whole = PokemonBalance.GraduationTotal(state.Rarity);

        var update = CompanionKeeper.Apply(state, "2026-09-09", whole);

        Assert.NotNull(update.GraduatedSpeciesId);
        Assert.Contains(update.GraduatedSpeciesId!.Value, update.State.Graduated);
        // There is always a companion to show.
        Assert.NotEmpty(update.State.SpeciesPath);
        Assert.Equal(0, update.State.StageIndex);
    }

    [Fact]
    public void TheReplacementKeepsTheWatermarkSoProgressIsNotDoubleCounted()
    {
        var state = CompanionState.Hatch(1) with { IsEgg = false };
        var whole = PokemonBalance.GraduationTotal(state.Rarity);

        var update = CompanionKeeper.Apply(state, "2026-09-09", whole);
        var next = CompanionKeeper.Apply(update.State, "2026-09-09", whole);

        Assert.Equal(0, next.State.TokensAtStage);
    }
}

public sealed class CompanionStateSanitizerTests
{
    [Fact]
    public void RepairsAnOutOfRangeStage()
    {
        var state = CompanionState.Hatch(1) with { StageIndex = 99 };

        var repaired = state.Sanitized();

        Assert.InRange(repaired.StageIndex, 0, repaired.SpeciesPath.Count - 1);
    }

    [Fact]
    public void DropsImplausibleSpeciesIds()
    {
        var state = CompanionState.Hatch(1) with { SpeciesPath = [1, -5, 99_999, 3] };

        var repaired = state.Sanitized();

        Assert.Equal([1, 3], repaired.SpeciesPath);
    }

    [Fact]
    public void HatchesAfreshWhenEverySpeciesIdIsUnusable()
    {
        var state = CompanionState.Hatch(1) with { SpeciesPath = [-1, 0, 99_999] };

        var repaired = state.Sanitized();

        Assert.NotEmpty(repaired.SpeciesPath);
        Assert.All(repaired.SpeciesPath, id => Assert.InRange(id, 1, 1400));
    }

    [Fact]
    public void ClampsTokensThatExceedAWholeLine()
    {
        var state = CompanionState.Hatch(1) with { TokensAtStage = long.MaxValue };

        var repaired = state.Sanitized();

        Assert.InRange(repaired.TokensAtStage, 0, PokemonBalance.GraduationTotal(repaired.Rarity));
    }

    [Fact]
    public void ClampsNegativeTokens()
    {
        var state = CompanionState.Hatch(1) with { TokensAtStage = -500, WatermarkTokens = -1 };

        var repaired = state.Sanitized();

        Assert.Equal(0, repaired.TokensAtStage);
        Assert.Equal(0, repaired.WatermarkTokens);
    }

    [Fact]
    public void DiscardsAnImplausibleWatermarkDay()
    {
        var state = CompanionState.Hatch(1) with { WatermarkDay = new string('x', 5_000) };

        Assert.Equal(string.Empty, state.Sanitized().WatermarkDay);
    }
}

public sealed class CompanionStoreTests
{
    [Fact]
    public void RoundTripsThroughDisk()
    {
        using var directory = new TempStoreDirectory();
        var store = new CompanionStore(directory.File);
        var original = CompanionKeeper.Apply(CompanionState.Hatch(7), "2026-09-09", 3_000_000).State;

        store.Save(original);
        var loaded = new CompanionStore(directory.File).Load();

        Assert.Equal(original.SpeciesPath, loaded.SpeciesPath);
        Assert.Equal(original.TokensAtStage, loaded.TokensAtStage);
        Assert.Equal(original.Rarity, loaded.Rarity);
        Assert.Equal(original.WatermarkDay, loaded.WatermarkDay);
    }

    [Fact]
    public void PersistsRarityByNameSoReorderingCannotReinterpretIt()
    {
        // Rarity decides graduation cost; a numeric enum would silently change meaning if a
        // tier were ever inserted.
        using var directory = new TempStoreDirectory();
        var store = new CompanionStore(directory.File);
        store.Save(CompanionState.Hatch(1) with { Rarity = Rarity.Legendary });

        Assert.Contains("Legendary", File.ReadAllText(directory.File), StringComparison.Ordinal);
    }

    [Fact]
    public void SetsACorruptFileAsideAndStartsFresh()
    {
        using var directory = new TempStoreDirectory();
        File.WriteAllText(directory.File, "{ this is not json");
        var store = new CompanionStore(directory.File);

        var loaded = store.Load();

        Assert.True(store.RecoveredFromCorruption);
        Assert.NotEmpty(loaded.SpeciesPath);
        Assert.True(File.Exists(directory.File + ".corrupt"), "the bad file must be kept for inspection");
    }

    [Fact]
    public void HatchesWhenNoFileExistsYet()
    {
        using var directory = new TempStoreDirectory();
        var store = new CompanionStore(directory.File);

        var loaded = store.Load();

        Assert.False(store.RecoveredFromCorruption);
        Assert.NotEmpty(loaded.SpeciesPath);
    }

    [Fact]
    public void SanitizesOnLoadNotOnlyOnSave()
    {
        // A file already holding a bad value must not reload badly forever.
        using var directory = new TempStoreDirectory();
        File.WriteAllText(
            directory.File,
            """{"speciesPath":[1,2,3],"stageIndex":99,"tokensAtStage":-4,"rarity":"Common","seed":1,"watermarkDay":"","watermarkTokens":-9,"graduated":[]}""");

        var loaded = new CompanionStore(directory.File).Load();

        Assert.InRange(loaded.StageIndex, 0, 2);
        Assert.Equal(0, loaded.TokensAtStage);
        Assert.Equal(0, loaded.WatermarkTokens);
    }

    [Fact]
    public void LeavesNoTemporaryFileBehind()
    {
        using var directory = new TempStoreDirectory();
        var store = new CompanionStore(directory.File);

        store.Save(CompanionState.Hatch(1));

        Assert.False(File.Exists(directory.File + ".tmp"));
    }

    private sealed class TempStoreDirectory : IDisposable
    {
        public TempStoreDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), "ptb-store-" + Guid.NewGuid().ToString("N")[..10]);
            Directory.CreateDirectory(Root);
            File = Path.Combine(Root, "companion.json");
        }

        public string Root { get; }

        public string File { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}

public sealed class EggPhaseTests
{
    private static CompanionState FreshEgg() =>
        CompanionState.Hatch(1) with { SpeciesPath = [1, 2, 3], Rarity = Rarity.Common };

    [Fact]
    public void ANewCompanionStartsAsAnEgg()
    {
        Assert.True(CompanionState.Hatch(1).IsEgg);
    }

    [Fact]
    public void ADrawnLineArrivesAsAnEgg()
    {
        var state = CompanionState.Hatch(1) with { IsEgg = false };

        var replaced = state.WithLine(new EvolutionLine { SpeciesPath = [4, 5, 6], Rarity = Rarity.Common });

        Assert.True(replaced.IsEgg);
    }

    [Fact]
    public void ASaveWrittenBeforeEggsExistedReadsAsHatched()
    {
        // Defaulting to false would regress every established companion into an egg.
        using var directory = new TempEggDirectory();
        File.WriteAllText(
            directory.File,
            """{"speciesPath":[1,2,3],"stageIndex":1,"tokensAtStage":50,"rarity":"Common","seed":1,"watermarkDay":"","watermarkTokens":0,"graduated":[]}""");

        Assert.False(new CompanionStore(directory.File).Load().IsEgg);
    }

    [Fact]
    public void TokensBelowTheThresholdDoNotHatchIt()
    {
        var update = CompanionKeeper.Apply(FreshEgg(), "2026-09-10", PokemonBalance.EggHatchThreshold - 1);

        Assert.True(update.State.IsEgg);
        Assert.Null(update.HatchedSpeciesId);
        Assert.Empty(update.Evolutions);
    }

    [Fact]
    public void AnEggAbsorbsTokensWithoutGrowingAForm()
    {
        var update = CompanionKeeper.Apply(FreshEgg(), "2026-09-10", 1_000_000);

        Assert.Equal(1_000_000, update.State.TokensAtStage);
        Assert.Equal(0, update.State.StageIndex);
    }

    [Fact]
    public void ReachingTheThresholdHatchesAndRevealsTheSpecies()
    {
        var update = CompanionKeeper.Apply(FreshEgg(), "2026-09-10", PokemonBalance.EggHatchThreshold);

        Assert.False(update.State.IsEgg);
        Assert.Equal(1, update.HatchedSpeciesId);
        Assert.Equal(0, update.State.TokensAtStage);
    }

    [Fact]
    public void SurplusCarriesIntoTheHatchling()
    {
        // Nothing may be lost at the shell.
        var update = CompanionKeeper.Apply(
            FreshEgg(),
            "2026-09-10",
            PokemonBalance.EggHatchThreshold + 4_321);

        Assert.False(update.State.IsEgg);
        Assert.Equal(4_321, update.State.TokensAtStage);
    }

    [Fact]
    public void OneLargeBatchCanHatchAndEvolveTogether()
    {
        // Usage arrives in batches; stalling at the shell for a whole refresh would be wrong.
        var whole = PokemonBalance.EggHatchThreshold + PokemonBalance.GraduationTotal(Rarity.Common);

        var update = CompanionKeeper.Apply(FreshEgg(), "2026-09-10", whole);

        // It hatched, evolved through the line, graduated, and the replacement is a new egg.
        Assert.NotNull(update.HatchedSpeciesId);
        Assert.NotEmpty(update.Evolutions);
        Assert.NotNull(update.GraduatedSpeciesId);
        Assert.True(update.State.IsEgg);
    }

    [Fact]
    public void AnEggIsNotFedTwiceByTwoRefreshesOfTheSameTotal()
    {
        var first = CompanionKeeper.Apply(FreshEgg(), "2026-09-10", 2_000_000);
        var second = CompanionKeeper.Apply(first.State, "2026-09-10", 2_000_000);

        Assert.Equal(2_000_000, second.State.TokensAtStage);
        Assert.True(second.State.IsEgg);
    }

    [Fact]
    public void GraduationYieldsAnotherEgg()
    {
        var hatched = FreshEgg() with { IsEgg = false };
        var whole = PokemonBalance.GraduationTotal(Rarity.Common);

        var update = CompanionKeeper.Apply(hatched, "2026-09-10", whole);

        Assert.NotNull(update.GraduatedSpeciesId);
        Assert.True(update.State.IsEgg);
    }

    private sealed class TempEggDirectory : IDisposable
    {
        public TempEggDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), "ptb-egg-" + Guid.NewGuid().ToString("N")[..10]);
            Directory.CreateDirectory(Root);
            File = Path.Combine(Root, "companion.json");
        }

        public string Root { get; }

        public string File { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}

public sealed class EggPersistenceTests
{
    [Fact]
    public void AnEggFlagWrittenAsTrueIsReadBackAsTrue()
    {
        var root = Path.Combine(Path.GetTempPath(), "ptb-eggrt-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "companion.json");

        try
        {
            File.WriteAllText(
                file,
                """{"speciesPath":[133,134],"stageIndex":0,"tokensAtStage":1200000,"rarity":"Uncommon","seed":77,"watermarkDay":"2026-09-10","watermarkTokens":999999999,"graduated":[3],"pathResolved":true,"isEgg":true}""");

            Assert.True(new CompanionStore(file).Load().IsEgg);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AnEggWithNoNewTokensStaysAnEgg()
    {
        var egg = CompanionState.Hatch(5) with
        {
            SpeciesPath = [133, 134],
            Rarity = Rarity.Uncommon,
            TokensAtStage = 1_200_000,
            WatermarkDay = "2026-09-10",
            WatermarkTokens = 999_999_999,
        };

        // Today's total is below the watermark, so the delta is zero.
        var update = CompanionKeeper.Apply(egg, "2026-09-10", 174_000_000);

        Assert.True(update.State.IsEgg);
        Assert.Equal(1_200_000, update.State.TokensAtStage);
    }
}
