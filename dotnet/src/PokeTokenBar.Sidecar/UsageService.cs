using System.Diagnostics;
using System.Reflection;
using PokeTokenBar.Core.Usage;
using PokeTokenBar.Sidecar.Protocol;

namespace PokeTokenBar.Sidecar;

internal sealed class UsageService : IUsageService
{
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
        stopwatch.Stop();

        return new UsageResponse
        {
            Today = UsageAggregator.ForDay(deduped, today),
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
}
