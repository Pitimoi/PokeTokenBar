using System.Globalization;
using System.Text.Json;
using PokeTokenBar.Core.Io;

namespace PokeTokenBar.Core.Usage;

/// <summary>Per-scan diagnostics, kept because silent drops are how parsers rot unnoticed.</summary>
public sealed record TranscriptScanStats
{
    public int FilesScanned { get; init; }

    public int FilesSkipped { get; init; }

    public long LinesTooLong { get; init; }

    /// <summary>Lines that looked like usage but failed validation — bad numbers or timestamps.</summary>
    public long EntriesRejected { get; init; }

    /// <summary>Duplicate turns collapsed by the deduplication key.</summary>
    public long DuplicatesCollapsed { get; init; }
}

public sealed record TranscriptScan
{
    public required IReadOnlyList<UsageEntry> Entries { get; init; }

    public required TranscriptScanStats Stats { get; init; }
}

/// <summary>
/// Reads Claude Code session transcripts (<c>~/.claude/projects/**/*.jsonl</c>) into
/// normalised usage entries.
/// </summary>
public static class ClaudeTranscriptReader
{
    // Substring gate before the JSON parser runs: only about one line in eight carries usage.
    private static readonly byte[] UsageMarker = "\"usage\""u8.ToArray();
    private static readonly byte[] AssistantMarker = "\"assistant\""u8.ToArray();

    /// <summary>
    /// Reads every <c>*.jsonl</c> under <paramref name="root"/>, optionally restricted to
    /// files modified at or after <paramref name="modifiedSince"/>.
    /// </summary>
    /// <remarks>
    /// Deduplication is global across files, not per file: the same turn appears in more than
    /// one root when worktrees or Desktop sessions are in play.
    /// </remarks>
    public static TranscriptScan ReadDirectory(
        string root,
        DateTimeOffset? modifiedSince = null,
        JsonlReadLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        return ReadFiles(EnumerateTranscripts(root, modifiedSince), limits);
    }

    /// <summary>Reads a single transcript. Deduplication still applies within the file.</summary>
    public static TranscriptScan ReadFile(string path, JsonlReadLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        return ReadFiles([path], limits);
    }

    private static TranscriptScan ReadFiles(IEnumerable<string> files, JsonlReadLimits? limits)
    {
        var deduped = new Dictionary<string, UsageEntry>(StringComparer.Ordinal);
        var scanned = 0;
        var skipped = 0;
        long tooLong = 0;
        long rejected = 0;
        long duplicates = 0;

        foreach (var file in files)
        {
            var result = JsonlFileScanner.Scan(file, line =>
            {
                if (!TryParseLine(line, out var entry))
                {
                    // Only lines that advertised usage and then failed validation are
                    // rejections; the rest are ordinary non-usage records.
                    if (LooksLikeUsage(line))
                    {
                        rejected++;
                    }

                    return;
                }

                if (KeepLarger(deduped, entry))
                {
                    duplicates++;
                }
            }, limits);

            if (result.FileSkipped)
            {
                skipped++;
            }
            else
            {
                scanned++;
            }

            tooLong += result.LinesTooLong;
        }

        return new TranscriptScan
        {
            Entries = deduped.Values.ToArray(),
            Stats = new TranscriptScanStats
            {
                FilesScanned = scanned,
                FilesSkipped = skipped,
                LinesTooLong = tooLong,
                EntriesRejected = rejected,
                DuplicatesCollapsed = duplicates,
            },
        };
    }

    /// <summary>
    /// Keeps the entry with the larger total for a given key, returning true when it
    /// collapsed a duplicate. Largest rather than first: a resumed turn is re-logged with
    /// fixed input but growing output, so first-wins reports the partial response.
    /// </summary>
    private static bool KeepLarger(Dictionary<string, UsageEntry> deduped, UsageEntry entry)
    {
        if (!deduped.TryGetValue(entry.Id, out var existing))
        {
            deduped[entry.Id] = entry;
            return false;
        }

        if (entry.Total > existing.Total)
        {
            deduped[entry.Id] = entry;
        }

        return true;
    }

    private static bool LooksLikeUsage(ReadOnlyMemory<byte> line)
    {
        var span = line.Span;
        return span.IndexOf(UsageMarker) >= 0 && span.IndexOf(AssistantMarker) >= 0;
    }

    private static bool TryParseLine(ReadOnlyMemory<byte> line, out UsageEntry entry)
    {
        entry = null!;

        if (!LooksLikeUsage(line))
        {
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryGetString(root, "type", out var type)
                || !string.Equals(type, "assistant", StringComparison.Ordinal)
                || !root.TryGetProperty("message", out var message)
                || message.ValueKind != JsonValueKind.Object
                || !message.TryGetProperty("usage", out var usage)
                || usage.ValueKind != JsonValueKind.Object
                || !TryGetString(root, "timestamp", out var timestamp)
                || !TryParseTimestamp(timestamp, out var when))
            {
                return false;
            }

            // Any unusable counter drops the whole entry — a partially-valid turn would
            // report a total that is quietly wrong.
            if (!TokenCount.TryRead(usage, "input_tokens", out var input)
                || !TokenCount.TryRead(usage, "output_tokens", out var output)
                || !TokenCount.TryRead(usage, "cache_creation_input_tokens", out var cacheWrite)
                || !TokenCount.TryRead(usage, "cache_read_input_tokens", out var cacheRead))
            {
                return false;
            }

            TryGetString(message, "id", out var messageId);
            TryGetString(root, "requestId", out var requestId);

            entry = new UsageEntry
            {
                Id = string.Concat(messageId, "|", requestId),
                Timestamp = when,
                LocalDay = LocalDayOf(when),
                Model = DisplayText.SanitizeIdentifier(TryGetString(message, "model", out var model) ? model : null),
                Input = input,
                Output = output,
                CacheWrite = cacheWrite,
                CacheRead = cacheRead,
            };

            return true;
        }
    }

    private static bool TryGetString(JsonElement parent, string name, out string value)
    {
        value = string.Empty;
        if (!parent.TryGetProperty(name, out var field) || field.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = field.GetString() ?? string.Empty;
        return value.Length > 0;
    }

    private static bool TryParseTimestamp(string value, out DateTimeOffset when) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out when);

    /// <summary>
    /// Buckets by local calendar day, matching the original. Timestamps are logged in UTC, so
    /// converting is what makes "today" mean the user's today rather than UTC's.
    /// </summary>
    private static string LocalDayOf(DateTimeOffset when) =>
        when.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static IEnumerable<string> EnumerateTranscripts(string root, DateTimeOffset? modifiedSince)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        foreach (var file in Directory.EnumerateFiles(root, "*.jsonl", options))
        {
            if (modifiedSince is null)
            {
                yield return file;
                continue;
            }

            DateTimeOffset written;
            try
            {
                written = File.GetLastWriteTimeUtc(file);
            }
            catch (IOException)
            {
                continue;
            }

            if (written >= modifiedSince.Value)
            {
                yield return file;
            }
        }
    }
}
