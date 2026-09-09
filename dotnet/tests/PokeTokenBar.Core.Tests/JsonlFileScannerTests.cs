using System.Buffers;
using System.Text;
using PokeTokenBar.Core.Io;

namespace PokeTokenBar.Core.Tests;

public sealed class JsonlFileScannerTests
{
    [Fact]
    public async Task DeliversEachLineWithoutTerminators()
    {
        using var transcript = TempTranscript.Raw("alpha\nbeta\ngamma\n");

        var lines = await CollectAsync(transcript.Path);

        Assert.Equal(["alpha", "beta", "gamma"], lines);
    }

    [Fact]
    public async Task HandlesCarriageReturnLineEndings()
    {
        using var transcript = TempTranscript.Raw("alpha\r\nbeta\r\n");

        var lines = await CollectAsync(transcript.Path);

        Assert.Equal(["alpha", "beta"], lines);
    }

    [Fact]
    public async Task DeliversTrailingLineWithoutNewline()
    {
        using var transcript = TempTranscript.Raw("alpha\nbeta");

        var lines = await CollectAsync(transcript.Path);

        Assert.Equal(["alpha", "beta"], lines);
    }

    [Fact]
    public async Task SkipsBlankLines()
    {
        using var transcript = TempTranscript.Raw("alpha\n\n\nbeta\n");

        var lines = await CollectAsync(transcript.Path);

        Assert.Equal(["alpha", "beta"], lines);
    }

    [Fact]
    public async Task ReadsLinesSpanningManyReadChunks()
    {
        // Forces the multi-segment path: one line far larger than the 64 KB read chunk but
        // still under the line cap.
        using var transcript = TempTranscript.Raw($"{new string('a', 200_000)}\nshort\n");

        var lines = await CollectAsync(transcript.Path);

        Assert.Equal(2, lines.Count);
        Assert.Equal(200_000, lines[0].Length);
        Assert.Equal("short", lines[1]);
    }

    [Fact]
    public async Task DiscardsOversizedLinesAndResumesAtTheNext()
    {
        using var transcript = TempTranscript.Raw($"ok\n{new string('x', 5000)}\nalso-ok\n");

        var lines = new List<string>();
        var result = await JsonlFileScanner.ScanAsync(
            transcript.Path,
            line => lines.Add(Decode(line)),
            new JsonlReadLimits { MaxLineBytes = 1024 });

        Assert.Equal(["ok", "also-ok"], lines);
        Assert.Equal(1, result.LinesTooLong);
        Assert.Equal(2, result.LinesDelivered);
    }

    [Fact]
    public async Task CountsAnUnterminatedOversizedTrailingLine()
    {
        using var transcript = TempTranscript.Raw($"ok\n{new string('x', 5000)}");

        var lines = new List<string>();
        var result = await JsonlFileScanner.ScanAsync(
            transcript.Path,
            line => lines.Add(Decode(line)),
            new JsonlReadLimits { MaxLineBytes = 1024 });

        Assert.Equal(["ok"], lines);
        Assert.Equal(1, result.LinesTooLong);
    }

    [Fact]
    public async Task AllocationStaysBoundedRegardlessOfFileSize()
    {
        // The point of streaming: 40 MB of content must not be materialised to read it.
        var path = Path.Combine(Path.GetTempPath(), "ptb-big-" + Guid.NewGuid().ToString("N")[..8] + ".jsonl");
        try
        {
            await File.WriteAllTextAsync(
                path,
                string.Concat(Enumerable.Repeat(new string('y', 1023) + "\n", 40_000)));

            var before = GC.GetTotalAllocatedBytes(precise: true);
            long delivered = 0;
            await JsonlFileScanner.ScanAsync(path, _ => delivered++);
            var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

            Assert.Equal(40_000, delivered);
            Assert.True(
                allocated < 8L * 1024 * 1024,
                $"scanning 40 MB allocated {allocated / 1024} KB; streaming should keep this small");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadsAFileThatIsOpenForWriting()
    {
        // The owning tool holds its transcript open; an exclusive open would fail on exactly
        // the files we most want to read.
        var path = Path.Combine(Path.GetTempPath(), "ptb-live-" + Guid.NewGuid().ToString("N")[..8] + ".jsonl");
        try
        {
            await using var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            writer.Write("alpha\nbeta\n"u8);
            await writer.FlushAsync();

            var lines = await CollectAsync(path);

            Assert.Equal(["alpha", "beta"], lines);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task MissingFileIsReportedAsSkipped()
    {
        var result = await JsonlFileScanner.ScanAsync(
            Path.Combine(Path.GetTempPath(), "ptb-absent-scan.jsonl"),
            _ => { });

        Assert.True(result.FileSkipped);
        Assert.Equal(0, result.LinesDelivered);
    }

    private static string Decode(ReadOnlySequence<byte> line) =>
        line.IsSingleSegment
            ? Encoding.UTF8.GetString(line.FirstSpan)
            : Encoding.UTF8.GetString(line.ToArray());

    private static async Task<List<string>> CollectAsync(string path, JsonlReadLimits? limits = null)
    {
        var lines = new List<string>();
        await JsonlFileScanner.ScanAsync(path, line => lines.Add(Decode(line)), limits);
        return lines;
    }
}
