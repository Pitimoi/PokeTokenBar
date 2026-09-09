using PokeTokenBar.Core.Io;
using PokeTokenBar.Core.Usage;

namespace PokeTokenBar.Core.Tests;

public sealed class ClaudeTranscriptReaderTests
{
    [Fact]
    public async Task CollapsesDuplicateTurns()
    {
        // Streaming logs the same turn repeatedly, so this is a correctness guard on the
        // totals, not a tidy-up.
        using var transcript = new TempTranscript(
            TempTranscript.UsageLine(input: 100, output: 10),
            TempTranscript.UsageLine(input: 100, output: 10),
            TempTranscript.UsageLine(input: 100, output: 10));

        var scan = await ClaudeTranscriptReader.ReadFileAsync(transcript.Path);

        Assert.Single(scan.Entries);
        Assert.Equal(110, scan.Entries[0].Total);
        Assert.Equal(2, scan.Stats.DuplicatesCollapsed);
    }

    [Fact]
    public async Task KeepsLargestTotalForRepeatedTurn()
    {
        // A resumed turn re-logs a fixed input with growing output; keeping the first
        // occurrence would report the partial response.
        using var transcript = new TempTranscript(
            TempTranscript.UsageLine(input: 100, output: 5),
            TempTranscript.UsageLine(input: 100, output: 900),
            TempTranscript.UsageLine(input: 100, output: 40));

        var scan = await ClaudeTranscriptReader.ReadFileAsync(transcript.Path);

        Assert.Single(scan.Entries);
        Assert.Equal(900, scan.Entries[0].Output);
    }

    [Fact]
    public async Task SeparateTurnsAreNotCollapsed()
    {
        using var transcript = new TempTranscript(
            TempTranscript.UsageLine(messageId: "msg_1", requestId: "req_1", input: 10),
            TempTranscript.UsageLine(messageId: "msg_2", requestId: "req_2", input: 20));

        var scan = await ClaudeTranscriptReader.ReadFileAsync(transcript.Path);

        Assert.Equal(2, scan.Entries.Count);
        Assert.Equal(30, scan.Entries.Sum(e => e.Total));
    }

    [Fact]
    public async Task TotalsExceedingInt32DoNotOverflow()
    {
        // A real corpus total exceeds int.MaxValue, so 32-bit accumulators would wrap here.
        var lines = Enumerable.Range(0, 8)
            .Select(i => TempTranscript.UsageLine(
                messageId: $"msg_{i}",
                requestId: $"req_{i}",
                cacheRead: TokenCount.Implausible))
            .ToArray();

        using var transcript = new TempTranscript(lines);

        var scan = await ClaudeTranscriptReader.ReadFileAsync(transcript.Path);
        var total = scan.Entries.Sum(e => e.Total);

        Assert.Equal(8L * TokenCount.Implausible, total);
        Assert.True(total > int.MaxValue, "fixture must exceed int.MaxValue to be meaningful");
    }

    [Theory]
    [InlineData("""{"input_tokens":-5,"output_tokens":1}""")]
    [InlineData("""{"input_tokens":1e30,"output_tokens":1}""")]
    [InlineData("""{"input_tokens":1000000001,"output_tokens":1}""")]
    [InlineData("""{"input_tokens":"120","output_tokens":1}""")]
    [InlineData("""{"input_tokens":{"nested":1},"output_tokens":1}""")]
    public async Task RejectsEntriesWithUnusableCounts(string usageObject)
    {
        using var transcript = new TempTranscript(TempTranscript.UsageLineRaw(usageObject));

        var scan = await ClaudeTranscriptReader.ReadFileAsync(transcript.Path);

        Assert.Empty(scan.Entries);
        Assert.Equal(1, scan.Stats.EntriesRejected);
    }

    [Fact]
    public async Task AbsentCountsAreTreatedAsZeroRatherThanRejected()
    {
        using var transcript = new TempTranscript(
            TempTranscript.UsageLineRaw("""{"output_tokens":7}"""));

        var scan = await ClaudeTranscriptReader.ReadFileAsync(transcript.Path);

        Assert.Single(scan.Entries);
        Assert.Equal(7, scan.Entries[0].Total);
        Assert.Equal(0, scan.Stats.EntriesRejected);
    }

    [Fact]
    public async Task OneBadLineDoesNotDiscardTheRest()
    {
        using var transcript = new TempTranscript(
            TempTranscript.UsageLine(messageId: "good_1", requestId: "r1", input: 11),
            """{"type":"assistant","usage":{ this is not json """,
            TempTranscript.UsageLine(messageId: "good_2", requestId: "r2", input: 22));

        var scan = await ClaudeTranscriptReader.ReadFileAsync(transcript.Path);

        Assert.Equal(2, scan.Entries.Count);
        Assert.Equal(33, scan.Entries.Sum(e => e.Total));
    }

    [Fact]
    public async Task IgnoresRecordsThatAreNotAssistantTurns()
    {
        using var transcript = new TempTranscript(
            """{"type":"user","timestamp":"2026-09-09T14:23:51.933Z","message":{"usage":{"input_tokens":999}}}""",
            TempTranscript.UsageLine(input: 5));

        var scan = await ClaudeTranscriptReader.ReadFileAsync(transcript.Path);

        Assert.Single(scan.Entries);
        Assert.Equal(5, scan.Entries[0].Total);
    }

    [Fact]
    public async Task RejectsRecordsWithoutAParsableTimestamp()
    {
        using var transcript = new TempTranscript(
            TempTranscript.UsageLineRaw("""{"input_tokens":5}""", timestamp: "not-a-date"));

        var scan = await ClaudeTranscriptReader.ReadFileAsync(transcript.Path);

        Assert.Empty(scan.Entries);
        Assert.Equal(1, scan.Stats.EntriesRejected);
    }

    [Fact]
    public async Task SanitizesModelNamesFromTheLog()
    {
        // The model name is attacker-influenced text that the host renders in a webview.
        using var transcript = new TempTranscript(
            TempTranscript.UsageLineRaw(
                """{"input_tokens":1}""",
                model: "<img src=x onerror=alert(1)>"));

        var scan = await ClaudeTranscriptReader.ReadFileAsync(transcript.Path);

        var model = Assert.Single(scan.Entries).Model;
        Assert.DoesNotContain("<", model, StringComparison.Ordinal);
        Assert.DoesNotContain(">", model, StringComparison.Ordinal);
        Assert.DoesNotContain("(", model, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreservesRealModelNamesExactly()
    {
        using var transcript = new TempTranscript(
            TempTranscript.UsageLine(model: "claude-opus-5", input: 1));

        var scan = await ClaudeTranscriptReader.ReadFileAsync(transcript.Path);

        Assert.Equal("claude-opus-5", Assert.Single(scan.Entries).Model);
    }

    [Fact]
    public async Task BucketsByLocalCalendarDay()
    {
        var instant = DateTimeOffset.Parse("2026-09-09T14:23:51.933Z", null);
        var expected = instant.ToLocalTime().ToString("yyyy-MM-dd", null);

        using var transcript = new TempTranscript(TempTranscript.UsageLine(input: 1));

        var scan = await ClaudeTranscriptReader.ReadFileAsync(transcript.Path);

        Assert.Equal(expected, Assert.Single(scan.Entries).LocalDay);
    }

    [Fact]
    public async Task DeduplicatesTheSameTurnAcrossFiles()
    {
        // The same turn is copied into more than one root by worktrees and Desktop sessions,
        // so per-file dedup alone would still double it.
        using var transcript = new TempTranscript(TempTranscript.UsageLine(input: 500));
        File.Copy(transcript.Path, Path.Combine(transcript.Directory, "copy.jsonl"));

        var scan = await ClaudeTranscriptReader.ReadDirectoryAsync(transcript.Directory);

        Assert.Single(scan.Entries);
        Assert.Equal(500, scan.Entries[0].Total);
        Assert.Equal(2, scan.Stats.FilesScanned);
    }

    [Fact]
    public async Task SkipsOversizedLinesButKeepsTheRestOfTheFile()
    {
        var padding = new string('p', 4096);
        using var transcript = new TempTranscript(
            TempTranscript.UsageLine(messageId: "small", requestId: "r1", input: 7),
            TempTranscript.UsageLineRaw(
                """{"input_tokens":999999}""",
                messageId: "huge",
                requestId: "r2",
                extraProperty: $"\"filler\":\"{padding}\""),
            TempTranscript.UsageLine(messageId: "small2", requestId: "r3", input: 8));

        var limits = new JsonlReadLimits { MaxLineBytes = 1024 };
        var scan = await ClaudeTranscriptReader.ReadFileAsync(transcript.Path, limits);

        Assert.Equal(1, scan.Stats.LinesTooLong);
        Assert.Equal(2, scan.Entries.Count);
        Assert.Equal(15, scan.Entries.Sum(e => e.Total));
    }

    [Fact]
    public async Task SkipsFilesOverTheSizeCapWithoutReadingThem()
    {
        using var transcript = new TempTranscript(TempTranscript.UsageLine(input: 100));

        var limits = new JsonlReadLimits { MaxFileBytes = 16 };
        var scan = await ClaudeTranscriptReader.ReadFileAsync(transcript.Path, limits);

        Assert.Empty(scan.Entries);
        Assert.Equal(1, scan.Stats.FilesSkipped);
        Assert.Equal(0, scan.Stats.FilesScanned);
    }

    [Fact]
    public async Task MissingPathIsSkippedNotThrown()
    {
        var scan = await ClaudeTranscriptReader.ReadFileAsync(Path.Combine(Path.GetTempPath(), "ptb-absent.jsonl"));

        Assert.Empty(scan.Entries);
        Assert.Equal(1, scan.Stats.FilesSkipped);
    }

    [Fact]
    public async Task MissingDirectoryYieldsNoEntries()
    {
        var scan = await ClaudeTranscriptReader.ReadDirectoryAsync(Path.Combine(Path.GetTempPath(), "ptb-absent-dir"));

        Assert.Empty(scan.Entries);
        Assert.Equal(0, scan.Stats.FilesScanned);
    }
}
