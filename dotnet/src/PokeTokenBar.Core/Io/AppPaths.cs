namespace PokeTokenBar.Core.Io;

/// <summary>
/// Where this application keeps its own data. Resolved here rather than from configuration:
/// the original honours a <c>PTB_STATE_DIR</c> environment variable with no validation, which
/// is a way to redirect where a process writes.
/// </summary>
public static class AppPaths
{
    private const string ApplicationFolder = "PokeTokenBar";

    /// <summary>
    /// Cached sprites. Not secret, and re-downloadable, so this is cache-class data rather than
    /// state — losing it costs a network round trip and nothing else.
    /// </summary>
    public static string SpriteCache => Path.Combine(DataRoot, "sprites");

    /// <summary>
    /// <c>%LOCALAPPDATA%</c> on Windows, <c>~/Library/Application Support</c> on macOS, and
    /// <c>$XDG_DATA_HOME</c> or <c>~/.local/share</c> on Linux.
    /// </summary>
    public static string DataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ApplicationFolder);

    /// <summary>Creates a directory if absent, returning it. Failure is surfaced, not swallowed.</summary>
    public static string EnsureDirectory(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        Directory.CreateDirectory(path);
        return path;
    }
}
