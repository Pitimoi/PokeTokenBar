using System.Globalization;
using PokeTokenBar.Core.Companions;
using PokeTokenBar.Core.Io;
using PokeTokenBar.Core.Pokedex;
using PokeTokenBar.Core.Sprites;
using PokeTokenBar.Core.Usage;

namespace PokeTokenBar.Tray;

/// <summary>A species with everything a renderer might want for it, each part optional.</summary>
internal sealed record SpeciesInfo(int SpeciesId, string? Name, string? SpritePath, string? IconPath, string? Color);

internal sealed record UsageSnapshot
{
    public required UsageTotals Today { get; init; }

    public required UsageTotals Week { get; init; }

    public required UsageTotals Month { get; init; }

    /// <summary>The active companion, or null while eggs are on offer.</summary>
    public SpeciesInfo? Current { get; init; }

    /// <summary>Line position and progress; meaningful only when <see cref="Current"/> is set.</summary>
    public required Companion Companion { get; init; }

    public required long Available { get; init; }

    public required long Earned { get; init; }

    public required long Spent { get; init; }

    public required int OfferCount { get; init; }

    public bool CanHatch => Current is null && OfferCount > 0 && Available >= CompanionEconomy.HatchPrice;

    public bool CanAdvance => Current is not null && Available >= CompanionEconomy.ClickCost;

    /// <summary>Why the last action was refused, or null.</summary>
    public string? Refusal { get; init; }

    public required IReadOnlyList<int> Evolutions { get; init; }

    public int? HatchedSpeciesId { get; init; }

    public int? GraduatedSpeciesId { get; init; }

    public required int GraduatedCount { get; init; }

    /// <summary>Final form of the most recently completed line, if any.</summary>
    public SpeciesInfo? LastGraduated { get; init; }

    public required DateTimeOffset ScannedAt { get; init; }
}

/// <summary>
/// The game as the tray sees it: usage scanned into the budget, eggs chosen, growth bought.
/// Mirrors the sidecar's <c>UsageService</c> without its RPC payload types. The save file is
/// shared with the sidecar, so every write goes through the same file gate.
/// </summary>
internal sealed class CompanionService
{
    private readonly CompanionStore _companions = new();
    private readonly SpriteCache _sprites = new();
    private readonly SpeciesLibrary _library = new();

    private UsageTotals? _today;
    private UsageTotals? _week;
    private UsageTotals? _month;

    /// <summary>Rescans the transcripts and credits today's growth to the budget.</summary>
    public async ValueTask<UsageSnapshot> ScanAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;
        var since = UsagePeriods.ScanStart(now);
        var entries = new List<UsageEntry>();

        foreach (var root in TranscriptRoots.Claude())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scan = await ClaudeTranscriptReader
                .ReadDirectoryAsync(root, since, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            entries.AddRange(scan.Entries);
        }

        // Roots can overlap, so the same turn can arrive twice and is collapsed once more here.
        var deduped = entries
            .GroupBy(static e => e.Id, StringComparer.Ordinal)
            .Select(static group => group.MaxBy(static e => e.Total)!)
            .ToArray();

        var today = LocalDay.For(now);
        _today = UsageAggregator.ForDay(deduped, today);
        _week = UsageAggregator.ForRange(deduped, LocalDay.For(UsagePeriods.StartOfWeek(now)), today);
        _month = UsageAggregator.ForRange(deduped, LocalDay.For(UsagePeriods.StartOfMonth(now)), today);

        CompanionState credited;
        using (FileGate.Acquire(_companions.FilePath))
        {
            var loaded = await ResolveTruncatedPathAsync(_companions.Load(), cancellationToken).ConfigureAwait(false);
            credited = CompanionKeeper.CreditBudget(loaded, _today);
            _companions.Save(credited);
        }

        var result = new SpendResult { State = credited, Refusal = SpendRefusal.None };
        return await BuildAsync(result, hatched: null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Takes one of the offered eggs; it hatches on the spot.</summary>
    public async ValueTask<UsageSnapshot> ChooseEggAsync(int offerIndex, CancellationToken cancellationToken)
    {
        using var gate = FileGate.Acquire(_companions.FilePath);

        var spend = CompanionKeeper.ChooseEgg(_companions.Load(), offerIndex);
        int? hatched = null;

        if (spend.Accepted)
        {
            // The species is drawn here, not in the keeper: the draw needs the network.
            var line = await _library.DrawAsync(spend.ChosenSeed!.Value, cancellationToken).ConfigureAwait(false);
            var state = spend.State.WithLine(line);
            _companions.Save(state);
            spend = spend with { State = state };
            hatched = state.ToCompanion().CurrentSpeciesId;
        }

        return await BuildAsync(spend, hatched, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Spends one press of budget on the companion's growth.</summary>
    public async ValueTask<UsageSnapshot> AdvanceAsync(CancellationToken cancellationToken)
    {
        using var gate = FileGate.Acquire(_companions.FilePath);

        var loaded = await ResolveTruncatedPathAsync(_companions.Load(), cancellationToken).ConfigureAwait(false);
        var spend = CompanionKeeper.Advance(loaded);
        if (spend.Accepted)
        {
            _companions.Save(spend.State);
        }

        return await BuildAsync(spend, hatched: null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Re-attempts a line whose chain could not be fetched when it hatched.</summary>
    private async ValueTask<CompanionState> ResolveTruncatedPathAsync(CompanionState state, CancellationToken cancellationToken)
    {
        if (state.PathResolved || !state.HasCompanion)
        {
            return state;
        }

        var resolved = await _library
            .ResolveAsync(state.SpeciesPath[0], state.Rarity, state.Seed, cancellationToken)
            .ConfigureAwait(false);
        return resolved is null ? state : state.WithResolvedPath(resolved);
    }

    private async ValueTask<UsageSnapshot> BuildAsync(SpendResult spend, int? hatched, CancellationToken cancellationToken)
    {
        var state = spend.State;
        var companion = state.ToCompanion();
        var graduated = state.Graduated;

        var mentioned = new List<int>();
        if (state.HasCompanion)
        {
            mentioned.Add(companion.CurrentSpeciesId);
        }

        if (graduated.Count > 0)
        {
            mentioned.Add(graduated[^1]);
        }

        await _library.EnsureNamesAsync(mentioned, cancellationToken: cancellationToken).ConfigureAwait(false);

        var current = state.HasCompanion
            ? await DescribeAsync(companion.CurrentSpeciesId, cancellationToken).ConfigureAwait(false)
            : null;
        var last = graduated.Count == 0
            ? null
            : await DescribeAsync(graduated[^1], cancellationToken).ConfigureAwait(false);

        var day = LocalDay.Today();
        return new UsageSnapshot
        {
            Today = _today ?? UsageTotals.Empty(day, day),
            Week = _week ?? UsageTotals.Empty(day, day),
            Month = _month ?? UsageTotals.Empty(day, day),
            Current = current,
            Companion = companion,
            Available = state.Available,
            Earned = state.Earned,
            Spent = state.Spent,
            OfferCount = state.OfferSeeds?.Count ?? 0,
            Refusal = spend.Refusal == SpendRefusal.None ? null : Describe(spend.Refusal),
            Evolutions = spend.Evolutions,
            HatchedSpeciesId = hatched,
            GraduatedSpeciesId = spend.GraduatedSpeciesId,
            GraduatedCount = graduated.Count,
            LastGraduated = last,
            ScannedAt = DateTimeOffset.Now,
        };
    }

    private static string Describe(SpendRefusal refusal) => refusal switch
    {
        SpendRefusal.NotEnoughBudget => "Not enough budget yet",
        SpendRefusal.NoSuchEgg => "That egg is gone",
        SpendRefusal.AlreadyHasCompanion => "A companion is already active",
        SpendRefusal.NoCompanion => "No companion to feed",
        _ => refusal.ToString(),
    };

    private async ValueTask<SpeciesInfo> DescribeAsync(int speciesId, CancellationToken cancellationToken)
    {
        var sprite = await _sprites
            .GetAsync(new SpriteRequest { SpeciesId = speciesId }, cancellationToken)
            .ConfigureAwait(false);
        var path = sprite.FileName is null ? null : Path.Combine(_sprites.Directory, sprite.FileName);
        // The library hands back the API slug (pichu); a name is shown, so capitalise it.
        var name = _library.NameFor(speciesId) is { Length: > 0 } slug
            ? char.ToUpperInvariant(slug[0]) + slug[1..]
            : null;

        string? color = null;
        string? icon = null;
        if (path is not null)
        {
            try
            {
                color = SpriteColor.Dominant(path);
                icon = EnsureIcon(speciesId, path);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException)
            {
                // Unreadable sprite: the dot just goes uncoloured and there is no icon.
            }
        }

        return new SpeciesInfo(speciesId, name, path, icon, color);
    }

    private static string EnsureIcon(int speciesId, string spritePath)
    {
        var directory = AppPaths.EnsureDirectory(Path.Combine(AppPaths.DataRoot, "icons"));
        var icon = Path.Combine(directory, speciesId.ToString(CultureInfo.InvariantCulture) + ".png");
        if (!File.Exists(icon))
        {
            TrayIconRenderer.SaveIcon(spritePath, icon);
        }

        return icon;
    }
}
