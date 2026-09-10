using System.Text.Json;
using System.Text.Json.Serialization;
using PokeTokenBar.Core.Companions;
using PokeTokenBar.Core.Io;

namespace PokeTokenBar.Tray;

/// <summary>What another tool needs to show the game — a status line, a prompt, a widget.</summary>
internal sealed record StatusDocument
{
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>The companion being raised, or null while eggs are on offer.</summary>
    public StatusCurrent? Current { get; init; }

    public required StatusBudget Budget { get; init; }

    public StatusSpecies? LastGraduated { get; init; }

    public required int GraduatedCount { get; init; }

    public required long TodayTokens { get; init; }

    public required double TodayCost { get; init; }
}

internal sealed record StatusBudget
{
    public required long Available { get; init; }

    public required long Earned { get; init; }

    public required long Spent { get; init; }

    public required long HatchPrice { get; init; }

    public required long ClickCost { get; init; }

    /// <summary>Eggs on offer; zero while a companion is active.</summary>
    public required int OfferCount { get; init; }

    public required bool CanHatch { get; init; }

    public required bool CanAdvance { get; init; }
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
            Current = snapshot.Current is { } current
                ? new StatusCurrent
                {
                    SpeciesId = current.SpeciesId,
                    Name = current.Name,
                    Color = current.Color,
                    Sprite = current.SpritePath,
                    Icon = current.IconPath,
                    Stage = companion.SafeStageIndex + 1,
                    TotalForms = companion.TotalForms,
                    Progress = companion.StageProgress,
                    TokensAtStage = companion.TokensAtStage,
                    StageThreshold = companion.StageThreshold,
                    Rarity = companion.Rarity.ToString(),
                }
                : null,
            Budget = new StatusBudget
            {
                Available = snapshot.Available,
                Earned = snapshot.Earned,
                Spent = snapshot.Spent,
                HatchPrice = CompanionEconomy.HatchPrice,
                ClickCost = CompanionEconomy.ClickCost,
                OfferCount = snapshot.OfferCount,
                CanHatch = snapshot.CanHatch,
                CanAdvance = snapshot.CanAdvance,
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
