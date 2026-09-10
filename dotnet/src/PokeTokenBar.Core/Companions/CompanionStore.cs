using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PokeTokenBar.Core.Io;
using SysIO = System.IO;

namespace PokeTokenBar.Core.Companions;

/// <summary>
/// Persists the companion beside the sprite cache.
/// </summary>
/// <remarks>
/// A corrupt file is set aside rather than deleted, so it can be inspected instead of
/// vanishing. Every load is sanitised, because clamping only on write leaves an already-bad
/// file to reload badly forever.
/// </remarks>
public sealed class CompanionStore
{
    private readonly string _path;
    private readonly JsonTypeInfo<CompanionState> _typeInfo;

    public CompanionStore(string? path = null, JsonTypeInfo<CompanionState>? typeInfo = null)
    {
        _path = path ?? SysIO.Path.Combine(AppPaths.DataRoot, "companion.json");

        // The JsonTypeInfo overloads, not the JsonSerializerOptions ones: handing options that
        // merely carry a generated resolver to the generic Serialize<T> is still flagged
        // RequiresDynamicCode, so it would not survive an AOT publish.
        _typeInfo = typeInfo ?? CompanionJsonContext.Default.CompanionState;
    }

    public string FilePath => _path;

    /// <summary>True when the last load found an unusable file and set it aside.</summary>
    public bool RecoveredFromCorruption { get; private set; }

    /// <summary>
    /// True when the last load produced a brand new companion rather than reading one. The
    /// caller uses this to replace the built-in fallback line with a real one.
    /// </summary>
    public bool HatchedFresh { get; private set; }

    public CompanionState Load()
    {
        RecoveredFromCorruption = false;
        HatchedFresh = false;

        if (!SysIO.File.Exists(_path))
        {
            HatchedFresh = true;
            return CompanionKeeper.Hatch();
        }

        try
        {
            var json = SysIO.File.ReadAllText(_path);
            var state = JsonSerializer.Deserialize(json, _typeInfo);
            return state is null ? Quarantine() : state.Sanitized();
        }
        catch (JsonException)
        {
            return Quarantine();
        }
        catch (IOException)
        {
            // Unreadable for a transient reason. Do not destroy it; start fresh in memory only.
            HatchedFresh = true;
            return CompanionKeeper.Hatch();
        }
    }

    public void Save(CompanionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        AppPaths.EnsureDirectory(SysIO.Path.GetDirectoryName(_path)!);

        var json = JsonSerializer.Serialize(state.Sanitized(), _typeInfo);
        var temporary = _path + ".tmp";
        SysIO.File.WriteAllText(temporary, json);
        SysIO.File.Move(temporary, _path, overwrite: true);
    }

    private CompanionState Quarantine()
    {
        RecoveredFromCorruption = true;
        HatchedFresh = true;

        try
        {
            SysIO.File.Move(_path, _path + ".corrupt", overwrite: true);
        }
        catch (IOException)
        {
            // Keeping the bad file in place beats failing the load over it.
        }

        return CompanionKeeper.Hatch();
    }
}
