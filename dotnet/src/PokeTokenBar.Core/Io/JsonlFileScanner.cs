using System.Buffers;

namespace PokeTokenBar.Core.Io;

/// <summary>Receives one JSONL line as raw UTF-8, without its terminator.</summary>
/// <remarks>
/// Only valid for the duration of the call — it points into a pooled buffer that is reused
/// for the next line. Copy anything that needs to outlive the callback, and do not park the
/// memory in a field or a captured closure.
/// </remarks>
public delegate void JsonlLineHandler(ReadOnlyMemory<byte> line);

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

    /// <summary>
    /// Invokes <paramref name="onLine"/> for each line, never holding more than one line
    /// plus a fixed read chunk in memory. Unreadable files are skipped rather than thrown,
    /// because a transcript can be deleted or rotated mid-scan.
    /// </summary>
    public static JsonlScanResult Scan(string path, JsonlLineHandler onLine, JsonlReadLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(onLine);
        limits ??= JsonlReadLimits.Default;

        FileStream stream;
        try
        {
            // ReadWrite | Delete: these transcripts are appended to live by the tool that owns
            // them, so an exclusive open fails against the very files we most want to read.
            stream = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.ReadWrite | FileShare.Delete,
                Options = FileOptions.SequentialScan,
                BufferSize = ReadChunkBytes,
            });
        }
        catch (IOException)
        {
            return new JsonlScanResult { FileSkipped = true };
        }
        catch (UnauthorizedAccessException)
        {
            return new JsonlScanResult { FileSkipped = true };
        }

        using (stream)
        {
            if (stream.Length > limits.MaxFileBytes)
            {
                return new JsonlScanResult { FileSkipped = true };
            }

            return ScanStream(stream, onLine, limits);
        }
    }

    private static JsonlScanResult ScanStream(Stream stream, JsonlLineHandler onLine, JsonlReadLimits limits)
    {
        var chunk = ArrayPool<byte>.Shared.Rent(ReadChunkBytes);
        using var line = new LineAccumulator(limits.MaxLineBytes);
        long delivered = 0;
        long tooLong = 0;

        try
        {
            int read;
            while ((read = stream.Read(chunk, 0, ReadChunkBytes)) > 0)
            {
                var remaining = chunk.AsSpan(0, read);
                while (!remaining.IsEmpty)
                {
                    var newline = remaining.IndexOf((byte)'\n');
                    if (newline < 0)
                    {
                        line.Append(remaining);
                        break;
                    }

                    line.Append(remaining[..newline]);
                    Deliver(line, onLine, ref delivered, ref tooLong);
                    remaining = remaining[(newline + 1)..];
                }
            }

            // Trailing line with no terminator — a live-appended file usually ends this way.
            if (!line.IsEmpty)
            {
                Deliver(line, onLine, ref delivered, ref tooLong);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }

        return new JsonlScanResult { LinesDelivered = delivered, LinesTooLong = tooLong };
    }

    private static void Deliver(LineAccumulator line, JsonlLineHandler onLine, ref long delivered, ref long tooLong)
    {
        if (line.Overflowed)
        {
            tooLong++;
        }
        else
        {
            var content = line.Content;
            if (!content.IsEmpty)
            {
                onLine(content);
                delivered++;
            }
        }

        line.Reset();
    }

    /// <summary>
    /// Grows up to a ceiling, then latches into an overflowed state and stops retaining
    /// bytes — so an oversized line costs the cap, not the line's true length.
    /// </summary>
    private sealed class LineAccumulator : IDisposable
    {
        private readonly int _maxBytes;
        private byte[] _buffer;
        private int _length;

        public LineAccumulator(int maxBytes)
        {
            _maxBytes = maxBytes;
            _buffer = ArrayPool<byte>.Shared.Rent(Math.Min(ReadChunkBytes, maxBytes));
        }

        public bool Overflowed { get; private set; }

        public bool IsEmpty => _length == 0 && !Overflowed;

        /// <summary>The line without its terminator, with a trailing CR removed.</summary>
        public ReadOnlyMemory<byte> Content
        {
            get
            {
                var end = _length;
                if (end > 0 && _buffer[end - 1] == (byte)'\r')
                {
                    end--;
                }

                return _buffer.AsMemory(0, end);
            }
        }

        public void Append(ReadOnlySpan<byte> part)
        {
            if (Overflowed || part.IsEmpty)
            {
                return;
            }

            if (_length + part.Length > _maxBytes)
            {
                Overflowed = true;
                _length = 0;
                return;
            }

            EnsureCapacity(_length + part.Length);
            part.CopyTo(_buffer.AsSpan(_length));
            _length += part.Length;
        }

        public void Reset()
        {
            _length = 0;
            Overflowed = false;
        }

        public void Dispose() => ArrayPool<byte>.Shared.Return(_buffer);

        private void EnsureCapacity(int required)
        {
            if (_buffer.Length >= required)
            {
                return;
            }

            var grown = ArrayPool<byte>.Shared.Rent(Math.Min(Math.Max(required, _buffer.Length * 2), _maxBytes));
            _buffer.AsSpan(0, _length).CopyTo(grown);
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = grown;
        }
    }
}
