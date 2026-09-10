using System.Text.Json;
using System.Text.Json.Serialization;
using PokeTokenBar.Core.Io;

namespace PokeTokenBar.Tray;

/// <summary>What another tool needs to show the companion — a status line, a prompt, a widget.</summary>
internal sealed record StatusDocument
{
    public required DateTimeOffset UpdatedAt { get; init; }

    public required StatusCurrent Current { get; init; }

    public StatusSpecies? LastGraduated { get; init; }

    public required int GraduatedCount { get; init; }

    public required long TodayTokens { get; init; }

    public required double TodayCost { get; init; }
}

internal sealed record StatusSpecies
{
    public required int SpeciesId { get; init; }

    public string? Name { get; init; }

    /// <summary>Dominant sprite colour as <c>#RRGGBB</c>, for renderers that can only show a dot.</summary>
    public string? Color { get; init; }

    public string? Sprite { get; init; }

    /// <summary>Cropped square PNG, for renderers that can show a small image.</summary>
    public string? Icon { get; init; }
}

internal sealed record StatusCurrent
{
    public required int SpeciesId { get; init; }

    public string? Name { get; init; }

    public string? Color { get; init; }

    public string? Sprite { get; init; }

    public string? Icon { get; init; }

    public required int Stage { get; init; }

    public required int TotalForms { get; init; }

    /// <summary>0..1 through the current form.</summary>
    public required double Progress { get; init; }

    public required long TokensAtStage { get; init; }

    public required long StageThreshold { get; init; }

    public required string Rarity { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(StatusDocument))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class StatusJsonContext : JsonSerializerContext
{
}

/// <summary>Publishes the snapshot as <c>status.json</c> beside the companion state, atomically.</summary>
internal static class StatusExport
{
    public static string FilePath => Path.Combine(AppPaths.DataRoot, "status.json");

    public static void Write(UsageSnapshot snapshot)
    {
        var companion = snapshot.Companion;
        var document = new StatusDocument
        {
            UpdatedAt = snapshot.ScannedAt,
            Current = new StatusCurrent
            {
                SpeciesId = companion.CurrentSpeciesId,
                Name = snapshot.Name,
                Color = snapshot.Color,
                Sprite = snapshot.SpritePath,
                Icon = snapshot.IconPath,
                Stage = companion.SafeStageIndex + 1,
                TotalForms = companion.TotalForms,
                Progress = companion.StageProgress,
                TokensAtStage = companion.TokensAtStage,
                StageThreshold = companion.StageThreshold,
                Rarity = companion.Rarity.ToString(),
            },
            LastGraduated = snapshot.LastGraduated is { } last
                ? new StatusSpecies { SpeciesId = last.SpeciesId, Name = last.Name, Color = last.Color, Sprite = last.SpritePath, Icon = last.IconPath }
                : null,
            GraduatedCount = snapshot.GraduatedCount,
            TodayTokens = snapshot.Today.Total,
            TodayCost = snapshot.Today.Cost,
        };

        AppPaths.EnsureDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(document, StatusJsonContext.Default.StatusDocument));
        File.Move(temporary, FilePath, overwrite: true);
    }
}
