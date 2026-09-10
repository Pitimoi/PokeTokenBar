using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using PokeTokenBar.Core.Io;

namespace PokeTokenBar.Tray;

/// <summary>
/// Publishes Claude Code spinner verbs about the companion as a settings fragment,
/// <c>claude-settings.json</c> beside <c>status.json</c>. Claude Code loads it with
/// <c>claude --settings &lt;path&gt;</c>; the user's own settings file is never touched.
/// </summary>
internal static class SpinnerVerbs
{
    /// <summary>
    /// The icon is a kitty Unicode placeholder for the image <c>scripts/claude-statusline/progress.sh</c>
    /// transmits under this id on every status-line refresh; the two must agree. Known glitch,
    /// accepted on purpose: the spinner's shimmer re-colours the verb per character, so while it
    /// animates the escape sequence shows as text and the sprite only appears on plain frames
    /// (<c>prefersReducedMotion</c> in Claude Code avoids it).
    /// </summary>
    private const int IconImageId = 200;

    private static readonly string[] Templates =
    [
        "{icon} getting fed",
        "Warming up {icon} egg",
        "Playing with {icon}",
        "Training {icon}",
        "{icon} gaining experience",
        "Petting {icon}",
    ];

    /// <summary>While eggs are on offer and nothing has hatched yet.</summary>
    private static readonly string[] EggTemplates =
    [
        "Keeping the eggs warm",
        "Saving up for an egg",
        "Eyeing the eggs",
        "Turning the eggs",
    ];

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string FilePath => Path.Combine(AppPaths.DataRoot, "claude-settings.json");

    /// <summary>Returns true when the file was rewritten.</summary>
    public static bool Update(UsageSnapshot snapshot)
    {
        string[] verbs;
        if (snapshot.Current is { } current)
        {
            var icon = current.IconPath is null
                ? "#" + current.SpeciesId.ToString(CultureInfo.InvariantCulture)
                : Placeholder(IconImageId);
            verbs = Templates.Select(template => template.Replace("{icon}", icon, StringComparison.Ordinal)).ToArray();
        }
        else
        {
            verbs = EggTemplates;
        }

        var document = new JsonObject
        {
            ["spinnerVerbs"] = new JsonObject
            {
                ["mode"] = "replace",
                ["verbs"] = new JsonArray(verbs.Select(static verb => (JsonNode)JsonValue.Create(verb)).ToArray()),
            },
        };
        var json = document.ToJsonString(WriteOptions) + Environment.NewLine;

        if (File.Exists(FilePath) && string.Equals(File.ReadAllText(FilePath), json, StringComparison.Ordinal))
        {
            return false;
        }

        AppPaths.EnsureDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, FilePath, overwrite: true);
        return true;
    }

    /// <summary>Two placeholder cells (row 0, columns 0 and 1) whose foreground colour names the image.</summary>
    private static string Placeholder(int imageId) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"[38;5;{imageId}m\U0010EEEE̅̅\U0010EEEE̅̍[39m");
}
