using System.Globalization;
using PokeTokenBar.Core.Companions;
using PokeTokenBar.Core.Io;
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

    public required Companion Companion { get; init; }

    public string? Name { get; init; }

    /// <summary>Absolute path of the current form's static sprite, or null when unavailable.</summary>
    public string? SpritePath { get; init; }

    /// <summary>Cropped square PNG of the current form, or null when unavailable.</summary>
    public string? IconPath { get; init; }

    /// <summary>Dominant sprite colour as <c>#RRGGBB</c>, or null without a sprite.</summary>
    public string? Color { get; init; }

    public required IReadOnlyList<int> Evolutions { get; init; }

    public int? GraduatedSpeciesId { get; init; }

    public required int GraduatedCount { get; init; }

    /// <summary>Final form of the most recently completed line, if any.</summary>
    public SpeciesInfo? LastGraduated { get; init; }

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
    private readonly SpeciesNames _names = new();

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

        var current = await DescribeAsync(companion.CurrentSpeciesId, cancellationToken).ConfigureAwait(false);
        var graduated = update.State.Graduated;
        var last = graduated.Count == 0
            ? null
            : await DescribeAsync(graduated[^1], cancellationToken).ConfigureAwait(false);

        return new UsageSnapshot
        {
            Today = todayTotals,
            Week = UsageAggregator.ForRange(deduped, LocalDay.For(UsagePeriods.StartOfWeek(now)), today),
            Month = UsageAggregator.ForRange(deduped, LocalDay.For(UsagePeriods.StartOfMonth(now)), today),
            Companion = companion,
            Name = current.Name,
            SpritePath = current.SpritePath,
            IconPath = current.IconPath,
            Color = current.Color,
            Evolutions = update.Evolutions,
            GraduatedSpeciesId = update.GraduatedSpeciesId,
            GraduatedCount = graduated.Count,
            LastGraduated = last,
            ScannedAt = now,
        };
    }

    private async ValueTask<SpeciesInfo> DescribeAsync(int speciesId, CancellationToken cancellationToken)
    {
        var sprite = await _sprites
            .GetAsync(new SpriteRequest { SpeciesId = speciesId }, cancellationToken)
            .ConfigureAwait(false);
        var path = sprite.FileName is null ? null : Path.Combine(_sprites.Directory, sprite.FileName);
        var name = await _names.GetAsync(speciesId, cancellationToken).ConfigureAwait(false);

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
