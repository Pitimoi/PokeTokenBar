using System.Text;
using PokeTokenBar.Core.Io;

namespace PokeTokenBar.Core.Tests;

public sealed class JsonlFileScannerTests
{
    [Fact]
    public void DeliversEachLineWithoutTerminators()
    {
        using var transcript = TempTranscript.Raw("alpha\nbeta\ngamma\n");

        var lines = Collect(transcript.Path);

        Assert.Equal(["alpha", "beta", "gamma"], lines);
    }

    [Fact]
    public void HandlesCarriageReturnLineEndings()
    {
        using var transcript = TempTranscript.Raw("alpha\r\nbeta\r\n");

        var lines = Collect(transcript.Path);

        Assert.Equal(["alpha", "beta"], lines);
    }

    [Fact]
    public void DeliversTrailingLineWithoutNewline()
    {
        // A transcript being appended to live routinely ends mid-line.
        using var transcript = TempTranscript.Raw("alpha\nbeta");

        var lines = Collect(transcript.Path);

        Assert.Equal(["alpha", "beta"], lines);
    }

    [Fact]
    public void SkipsBlankLines()
    {
        using var transcript = TempTranscript.Raw("alpha\n\n\nbeta\n");

        var lines = Collect(transcript.Path);

        Assert.Equal(["alpha", "beta"], lines);
    }

    [Fact]
    public void ReadsLinesSpanningManyReadChunks()
    {
        // Exercises the accumulator's growth path: one line far larger than the 64 KB
        // read chunk, still under the line cap.
        var long1 = new string('a', 200_000);
        using var transcript = TempTranscript.Raw($"{long1}\nshort\n");

        var lines = Collect(transcript.Path);

        Assert.Equal(2, lines.Count);
        Assert.Equal(200_000, lines[0].Length);
        Assert.Equal("short", lines[1]);
    }

    [Fact]
    public void DiscardsOversizedLinesAndResumesAtTheNext()
    {
        using var transcript = TempTranscript.Raw($"ok\n{new string('x', 5000)}\nalso-ok\n");

        var lines = new List<string>();
        var result = JsonlFileScanner.Scan(
            transcript.Path,
            line => lines.Add(Encoding.UTF8.GetString(line.Span)),
            new JsonlReadLimits { MaxLineBytes = 1024 });

        Assert.Equal(["ok", "also-ok"], lines);
        Assert.Equal(1, result.LinesTooLong);
        Assert.Equal(2, result.LinesDelivered);
    }

    [Fact]
    public void PeakMemoryStaysBoundedByTheLineCap()
    {
        // The point of streaming: a file far larger than the cap must not be materialised.
        // 40 MB of content, 1 KB lines, read with a 64 KB cap on retained line length.
        var line = new string('y', 1023);
        var path = Path.Combine(Path.GetTempPath(), "ptb-big-" + Guid.NewGuid().ToString("N")[..8] + ".jsonl");
        try
        {
            using (var writer = new StreamWriter(path))
            {
                for (var i = 0; i < 40_000; i++)
                {
                    writer.Write(line);
                    writer.Write('\n');
                }
            }

            var before = GC.GetTotalAllocatedBytes(precise: true);
            long delivered = 0;
            JsonlFileScanner.Scan(path, _ => delivered++);
            var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

            Assert.Equal(40_000, delivered);
            Assert.True(
                allocated < 8L * 1024 * 1024,
                $"scanning 40 MB allocated {allocated / (1024 * 1024)} MB; streaming should keep this small");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadsAFileThatIsOpenForWriting()
    {
        // Claude Code holds its transcript open; an exclusive open would fail on exactly the
        // files we most want to read.
        var path = Path.Combine(Path.GetTempPath(), "ptb-live-" + Guid.NewGuid().ToString("N")[..8] + ".jsonl");
        try
        {
            using var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            writer.Write("alpha\nbeta\n"u8);
            writer.Flush();

            var lines = Collect(path);

            Assert.Equal(["alpha", "beta"], lines);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static List<string> Collect(string path, JsonlReadLimits? limits = null)
    {
        var lines = new List<string>();
        JsonlFileScanner.Scan(path, line => lines.Add(Encoding.UTF8.GetString(line.Span)), limits);
        return lines;
    }
}
