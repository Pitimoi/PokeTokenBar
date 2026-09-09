using System.Buffers;
using System.IO.Pipelines;

namespace PokeTokenBar.Core.Io;

/// <summary>Receives one JSONL line as raw UTF-8, without its terminator.</summary>
/// <remarks>
/// Valid only for the duration of the call — it points into pooled pipe buffers that are
/// recycled for the next line. Copy anything that needs to outlive the callback.
/// </remarks>
public delegate void JsonlLineHandler(ReadOnlySequence<byte> line);

/// <summary>Outcome of a single file scan. Counts are for diagnostics and tests.</summary>
public sealed record JsonlScanResult
{
    /// <summary>True when the file was not read at all — unreadable, or over the size cap.</summary>
    public bool FileSkipped { get; init; }

    public long LinesDelivered { get; init; }

    /// <summary>Lines discarded for exceeding <see cref="JsonlReadLimits.MaxLineBytes"/>.</summary>
    public long LinesTooLong { get; init; }
}

/// <summary>
/// Streams a JSONL file line by line with a hard memory bound, for logs written by other
/// tools and therefore treated as untrusted input.
/// </summary>
public static class JsonlFileScanner
{
    private const int ReadChunkBytes = 64 * 1024;

    private static readonly JsonlScanResult Skipped = new() { FileSkipped = true };

    /// <summary>
    /// Invokes <paramref name="onLine"/> for each line, never retaining more than one line
    /// plus a read chunk. Unreadable files are skipped rather than thrown, because a
    /// transcript can be rotated or deleted mid-scan.
    /// </summary>
    public static async ValueTask<JsonlScanResult> ScanAsync(
        string path,
        JsonlLineHandler onLine,
        JsonlReadLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(onLine);
        limits ??= JsonlReadLimits.Default;

        FileStream stream;
        try
        {
            stream = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                // The owning tool holds its transcript open for append, so an exclusive open
                // fails on exactly the newest and most interesting files.
                Share = FileShare.ReadWrite | FileShare.Delete,
                Options = FileOptions.SequentialScan,
            });
        }
        catch (IOException)
        {
            return Skipped;
        }
        catch (UnauthorizedAccessException)
        {
            return Skipped;
        }

        await using (stream.ConfigureAwait(false))
        {
            if (stream.Length > limits.MaxFileBytes)
            {
                return Skipped;
            }

            return await ReadLinesAsync(stream, onLine, limits, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask<JsonlScanResult> ReadLinesAsync(
        Stream stream,
        JsonlLineHandler onLine,
        JsonlReadLimits limits,
        CancellationToken cancellationToken)
    {
        var reader = PipeReader.Create(
            stream,
            new StreamPipeReaderOptions(bufferSize: ReadChunkBytes, leaveOpen: true));

        long delivered = 0;
        long tooLong = 0;
        var discarding = false;

        try
        {
            while (true)
            {
                var read = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                var buffer = read.Buffer;

                while (TrySliceLine(ref buffer, out var line))
                {
                    if (discarding)
                    {
                        // This terminator ends the line we gave up on, it does not start one.
                        discarding = false;
                        tooLong++;
                        continue;
                    }

                    // A small file arrives in a single read, so an oversized line can be
                    // fully terminated before the pending-remainder check below ever runs.
                    if (line.Length > limits.MaxLineBytes)
                    {
                        tooLong++;
                        continue;
                    }

                    if (!line.IsEmpty)
                    {
                        onLine(line);
                        delivered++;
                    }
                }

                // Nothing terminated in the remainder. Once it passes the cap it can never
                // become a deliverable line, so drop it rather than let the pipe grow it.
                if (discarding || buffer.Length > limits.MaxLineBytes)
                {
                    discarding = true;
                    buffer = buffer.Slice(buffer.End);
                }

                if (read.IsCompleted)
                {
                    if (discarding)
                    {
                        tooLong++;
                    }
                    else if (!buffer.IsEmpty)
                    {
                        // A live-appended transcript routinely ends mid-line.
                        var trailing = TrimCarriageReturn(buffer);
                        if (!trailing.IsEmpty)
                        {
                            onLine(trailing);
                            delivered++;
                        }
                    }

                    reader.AdvanceTo(buffer.End);
                    break;
                }

                reader.AdvanceTo(buffer.Start, buffer.End);
            }
        }
        finally
        {
            await reader.CompleteAsync().ConfigureAwait(false);
        }

        return new JsonlScanResult { LinesDelivered = delivered, LinesTooLong = tooLong };
    }

    private static bool TrySliceLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
    {
        var newline = buffer.PositionOf((byte)'\n');
        if (newline is null)
        {
            line = default;
            return false;
        }

        line = TrimCarriageReturn(buffer.Slice(0, newline.Value));
        buffer = buffer.Slice(buffer.GetPosition(1, newline.Value));
        return true;
    }

    private static ReadOnlySequence<byte> TrimCarriageReturn(ReadOnlySequence<byte> line)
    {
        if (line.IsEmpty)
        {
            return line;
        }

        var last = line.Slice(line.Length - 1);
        return last.FirstSpan[0] == (byte)'\r' ? line.Slice(0, line.Length - 1) : line;
    }
}
