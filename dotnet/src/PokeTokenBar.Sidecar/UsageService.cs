using System.Diagnostics;
using System.Reflection;
using PokeTokenBar.Core.Companions;
using PokeTokenBar.Core.Io;
using PokeTokenBar.Core.Pokedex;
using PokeTokenBar.Core.Sprites;
using PokeTokenBar.Core.Usage;
using PokeTokenBar.Sidecar.Protocol;

namespace PokeTokenBar.Sidecar;

internal sealed class UsageService : IUsageService
{
    private readonly CompanionStore _companions = new();
    private readonly SpriteCache _sprites = new();
    private readonly SpeciesLibrary _library = new();

    /// <summary>How many collected species to fetch artwork for, newest first.</summary>
    private const int CollectionSpriteLimit = 60;


    public async ValueTask<UsageResponse> GetUsageAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();

        var entries = new List<UsageEntry>();
        var filesScanned = 0;
        var filesSkipped = 0;
        long linesTooLong = 0;
        long entriesRejected = 0;
        long duplicatesCollapsed = 0;

        // One scan feeds all three windows, bounded by the earliest of them.
        var since = UsagePeriods.ScanStart(now);

        foreach (var root in TranscriptRoots.Claude())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var scan = await ClaudeTranscriptReader
                .ReadDirectoryAsync(root, since, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            entries.AddRange(scan.Entries);
            filesScanned += scan.Stats.FilesScanned;
            filesSkipped += scan.Stats.FilesSkipped;
            linesTooLong += scan.Stats.LinesTooLong;
            entriesRejected += scan.Stats.EntriesRejected;
            duplicatesCollapsed += scan.Stats.DuplicatesCollapsed;
        }

        // Roots can overlap (a relocated config directory, a Desktop session), so the same turn
        // can arrive from two roots and has to be collapsed once more here.
        var deduped = entries
            .GroupBy(static e => e.Id, StringComparer.Ordinal)
            .Select(static group => group.MaxBy(static e => e.Total)!)
            .ToArray();

        var today = LocalDay.For(now);
        var todayTotals = UsageAggregator.ForDay(deduped, today);
        var companion = await AdvanceCompanionAsync(todayTotals, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        return new UsageResponse
        {
            Today = todayTotals,
            Companion = companion,
            Week = UsageAggregator.ForRange(deduped, LocalDay.For(UsagePeriods.StartOfWeek(now)), today),
            Month = UsageAggregator.ForRange(deduped, LocalDay.For(UsagePeriods.StartOfMonth(now)), today),
            Scan = new ScanReport
            {
                FilesScanned = filesScanned,
                FilesSkipped = filesSkipped,
                LinesTooLong = linesTooLong,
                EntriesRejected = entriesRejected,
                DuplicatesCollapsed = duplicatesCollapsed,
                ElapsedMilliseconds = (int)stopwatch.ElapsedMilliseconds,
            },
        };
    }

    public ValueTask<SidecarInfoResponse> GetInfoAsync(CancellationToken cancellationToken)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        var present = TranscriptRoots.Claude().Any(Directory.Exists);

        return ValueTask.FromResult(new SidecarInfoResponse
        {
            Version = version,
            ClaudeTranscriptsPresent = present,
        });
    }

    private async ValueTask<CompanionResponse> AdvanceCompanionAsync(
        UsageTotals today,
        CancellationToken cancellationToken)
    {
        // One gate around load, advance and save. Every window runs its own sidecar against
        // this same file; without it, two refreshes that interleave both read the same day
        // watermark and both apply the same token delta.
        using var gate = FileGate.Acquire(_companions.FilePath);

        var loaded = _companions.Load();

        // A brand new companion is hatched from the built-in lines so that it always exists;
        // replacing it with a real draw happens here, where awaiting is possible.
        if (_companions.HatchedFresh)
        {
            loaded = loaded.WithLine(await _library.DrawAsync(loaded.Seed, cancellationToken).ConfigureAwait(false));
        }

        // A path that failed to fetch earlier gets another attempt, so a transient network
        // failure does not leave a multi-form species stuck showing one form forever.
        else if (!loaded.PathResolved && loaded.SpeciesPath.Count > 0)
        {
            var resolved = await _library
                .ResolveAsync(loaded.SpeciesPath[0], loaded.Rarity, loaded.Seed, cancellationToken)
                .ConfigureAwait(false);
            if (resolved is not null)
            {
                loaded = loaded.WithResolvedPath(resolved);
            }
        }

        var update = CompanionKeeper.Apply(loaded, today);

        // Same again for the replacement after a graduation: Apply stays synchronous and
        // testable, and the network-backed draw is layered on top of its result.
        if (update.GraduatedSpeciesId is not null)
        {
            var drawn = await _library.DrawAsync(update.State.Seed, cancellationToken).ConfigureAwait(false);
            update = update with { State = update.State.WithLine(drawn) };
        }

        _companions.Save(update.State);

        var companion = update.State.ToCompanion();

        // Animated where the source has it, static otherwise. A missing sprite is not an error:
        // the companion still has a species, a stage, and progress to show.
        var sprite = await _sprites
            .GetAsync(new SpriteRequest { SpeciesId = companion.CurrentSpeciesId, Animated = true }, cancellationToken)
            .ConfigureAwait(false);

        if (sprite.FileName is null)
        {
            sprite = await _sprites
                .GetAsync(new SpriteRequest { SpeciesId = companion.CurrentSpeciesId }, cancellationToken)
                .ConfigureAwait(false);
        }

        // Names for every species the host might render: the current form, the line it is on,
        // and the collection. Gathered once here so the host never has to ask again.
        var mentioned = new HashSet<int>(companion.SpeciesPath) { companion.CurrentSpeciesId };
        mentioned.UnionWith(update.State.Graduated);

        // A collected mid-line form was never walked as part of a chain, so its name is
        // unknown. Filled a few at a time so a long collection converges over several
        // refreshes rather than stalling one.
        await _library.EnsureNamesAsync(mentioned, cancellationToken: cancellationToken).ConfigureAwait(false);

        var names = new Dictionary<int, string>();
        foreach (var id in mentioned)
        {
            var name = _library.NameFor(id);
            if (name is not null)
            {
                names[id] = name;
            }
        }

        // Static sprites for the collection: a quarter the size of the animated ones, and a
        // grid of animations would be noise rather than charm. Bounded because the collection
        // grows without limit and each miss is a request.
        var collectionSprites = new Dictionary<int, string>();
        foreach (var id in update.State.Graduated.Reverse().Take(CollectionSpriteLimit))
        {
            var art = await _sprites
                .GetAsync(new SpriteRequest { SpeciesId = id }, cancellationToken)
                .ConfigureAwait(false);

            if (art.FileName is not null)
            {
                collectionSprites[id] = art.FileName;
            }
        }

        return new CompanionResponse
        {
            SpeciesId = companion.CurrentSpeciesId,
            SpeciesName = names.GetValueOrDefault(companion.CurrentSpeciesId, string.Empty),
            StageIndex = companion.SafeStageIndex,
            TotalForms = companion.TotalForms,
            StageProgress = companion.StageProgress,
            TokensAtStage = companion.TokensAtStage,
            StageThreshold = companion.StageThreshold,
            Rarity = companion.Rarity.ToString(),
            ReachedForms = companion.ReachedForms,
            JustEvolved = update.Evolutions,
            JustGraduated = update.GraduatedSpeciesId,
            GraduatedCount = update.State.Graduated.Count,
            Graduated = update.State.Graduated,
            Names = names,
            CollectionSprites = collectionSprites,
            SpriteFileName = sprite.FileName,
            SpriteDirectory = _sprites.Directory,
        };
    }
}
