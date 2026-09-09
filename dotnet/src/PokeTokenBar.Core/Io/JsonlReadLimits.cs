namespace PokeTokenBar.Core.Io;

/// <summary>
/// Caps applied when scanning JSONL written by other tools. Both are load-bearing:
/// <see cref="MaxFileBytes"/> bounds total work, while <see cref="MaxLineBytes"/> is what
/// bounds memory — one pathological line in an otherwise ordinary file would otherwise be
/// materialised whole. Defaults leave roughly an order of magnitude of headroom over the
/// largest values seen in practice.
/// </summary>
public sealed record JsonlReadLimits
{
    public static JsonlReadLimits Default { get; } = new();

    public long MaxFileBytes { get; init; } = 512L * 1024 * 1024;

    public int MaxLineBytes { get; init; } = 4 * 1024 * 1024;
}
