namespace PokeTokenBar.Core.Usage;

/// <summary>
/// Where Claude Code transcripts live. Resolved here and never accepted from a caller: the
/// host reads editor configuration, and a repository can ship configuration, so a
/// caller-supplied root would be attacker-controlled input choosing what this process reads.
/// </summary>
/// <remarks>
/// Environment variables are deliberately not consulted. The Swift original honours
/// <c>CLAUDE_CONFIG_DIR</c>, and separately reads configuration through a login shell, which is
/// the mechanism that lets a line in a shell profile redirect what the app talks to. Supporting
/// a relocated config directory is a feature request, not a default.
/// </remarks>
public static class TranscriptRoots
{
    public static IReadOnlyList<string> Claude()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            return [];
        }

        return
        [
            Path.Combine(home, ".claude", "projects"),
            Path.Combine(home, ".config", "claude", "projects"),
        ];
    }
}
