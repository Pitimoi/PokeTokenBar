using PokeTokenBar.Core.Companions;
using PokeTokenBar.Core.Sprites;
using PokeTokenBar.Core.Usage;

namespace PokeTokenBar.Tray;

internal sealed record UsageSnapshot
{
    public required UsageTotals Today { get; init; }

    public required UsageTotals Week { get; init; }

    public required UsageTotals Month { get; init; }

    public required Companion Companion { get; init; }

    public required IReadOnlyList<int> Evolutions { get; init; }

    public int? GraduatedSpeciesId { get; init; }

    public required int GraduatedCount { get; init; }

    /// <summary>Absolute path of the current form's static sprite, or null when unavailable.</summary>
    public string? SpritePath { get; init; }

    public required DateTimeOffset ScannedAt { get; init; }
}

/// <summary>
/// One scan of the Claude transcripts, rolled up into the three windows and fed to the companion.
/// Mirrors the sidecar's <c>UsageService</c> without its RPC payload types.
/// </summary>
internal sealed class UsageScanner
{
    private readonly CompanionStore _companions = new();
    private readonly SpriteCache _sprites = new();

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
        var todayTotals = UsageAggregator.ForDay(deduped, today);

        var update = CompanionKeeper.Apply(_companions.Load(), todayTotals);
        _companions.Save(update.State);
        var companion = update.State.ToCompanion();

        var sprite = await _sprites
            .GetAsync(new SpriteRequest { SpeciesId = companion.CurrentSpeciesId }, cancellationToken)
            .ConfigureAwait(false);

        return new UsageSnapshot
        {
            Today = todayTotals,
            Week = UsageAggregator.ForRange(deduped, LocalDay.For(UsagePeriods.StartOfWeek(now)), today),
            Month = UsageAggregator.ForRange(deduped, LocalDay.For(UsagePeriods.StartOfMonth(now)), today),
            Companion = companion,
            Evolutions = update.Evolutions,
            GraduatedSpeciesId = update.GraduatedSpeciesId,
            GraduatedCount = update.State.Graduated.Count,
            SpritePath = sprite.FileName is null ? null : Path.Combine(_sprites.Directory, sprite.FileName),
            ScannedAt = now,
        };
    }
}
