using PokeTokenBar.Core.Usage;

namespace PokeTokenBar.Sidecar.Protocol;

/// <summary>
/// All three reporting windows from one scan. Returned together because they are derived from a
/// single pass over the transcripts, so splitting them into separate calls would rescan.
/// </summary>
public sealed record UsageResponse
{
    public required UsageTotals Today { get; init; }

    public required UsageTotals Week { get; init; }

    public required UsageTotals Month { get; init; }

    /// <summary>
    /// What the scan did and did not manage to read. Surfaced rather than swallowed so a host
    /// can show that a number is incomplete instead of quietly under-reporting.
    /// </summary>
    public required ScanReport Scan { get; init; }
}

/// <summary>Diagnostics from a transcript scan.</summary>
public sealed record ScanReport
{
    public required int FilesScanned { get; init; }

    public required int FilesSkipped { get; init; }

    public required long LinesTooLong { get; init; }

    public required long EntriesRejected { get; init; }

    public required long DuplicatesCollapsed { get; init; }

    public required int ElapsedMilliseconds { get; init; }

    /// <summary>True when anything was dropped, so the host need not interpret the counts.</summary>
    public bool Degraded => FilesSkipped > 0 || LinesTooLong > 0 || EntriesRejected > 0;
}

public sealed record SidecarInfoResponse
{
    public required string Version { get; init; }

    /// <summary>Whether any Claude transcript root exists on this machine.</summary>
    public required bool ClaudeTranscriptsPresent { get; init; }
}
