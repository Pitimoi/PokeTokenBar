namespace PokeTokenBar.Core.Io;

/// <summary>
/// A cross-process lock around a file, so several sidecars can share one piece of state.
/// </summary>
/// <remarks>
/// Every VS Code window runs its own extension host and therefore its own sidecar, all reading
/// and writing the same companion file. Sequential refreshes already converge, because the day
/// watermark lives in that shared file — but two that interleave both read the same watermark
/// and both apply the same token delta, so progress advances twice for usage that happened
/// once. Holding this gate across the whole read-modify-write makes the sequence atomic.
///
/// A separate lock file is used rather than the state file itself, so a crash mid-write cannot
/// leave the state unreadable, and so the state file can still be replaced atomically.
/// </remarks>
public sealed class FileGate : IDisposable
{
    private readonly FileStream? _stream;

    private FileGate(FileStream? stream) => _stream = stream;

    /// <summary>True when the lock was actually taken. False means it proceeded unguarded.</summary>
    public bool Held => _stream is not null;

    /// <summary>
    /// Waits up to <paramref name="timeout"/> for exclusive access.
    /// </summary>
    /// <remarks>
    /// On timeout it returns an unheld gate rather than throwing. A companion that advances
    /// slightly wrong is a better outcome than a refresh that fails, and the alternative —
    /// blocking forever on a stale lock from a killed process — is worse than both.
    /// </remarks>
    public static FileGate Acquire(string path, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        var lockPath = path + ".lock";

        while (true)
        {
            try
            {
                AppPaths.EnsureDirectory(Path.GetDirectoryName(lockPath)!);

                // DeleteOnClose keeps the directory tidy and, more usefully, means a crashed
                // holder's lock disappears with its handle rather than blocking everyone.
                return new FileGate(new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.DeleteOnClose));
            }
            catch (IOException)
            {
                if (DateTimeOffset.UtcNow >= deadline)
                {
                    return new FileGate(null);
                }

                Thread.Sleep(25);
            }
            catch (UnauthorizedAccessException)
            {
                return new FileGate(null);
            }
        }
    }

    public void Dispose() => _stream?.Dispose();
}
