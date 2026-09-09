using System.Globalization;
using SysIO = System.IO;

namespace PokeTokenBar.Core.Tests;

/// <summary>
/// Writes JSONL fixtures to a throwaway directory. Fixtures are built as raw text on
/// purpose: these tests exist to exercise malformed and hostile input, which a typed
/// serialiser would refuse to produce.
/// </summary>
internal sealed class TempTranscript : IDisposable
{
    public TempTranscript(params string[] lines)
        : this("session.jsonl", string.Join('\n', lines) + "\n")
    {
    }

    private TempTranscript(string fileName, string content)
    {
        Directory = SysIO.Path.Combine(
            SysIO.Path.GetTempPath(),
            "ptb-tests-" + Guid.NewGuid().ToString("N")[..12]);
        SysIO.Directory.CreateDirectory(Directory);
        Path = SysIO.Path.Combine(Directory, fileName);
        SysIO.File.WriteAllText(Path, content);
    }

    public string Directory { get; }

    public string Path { get; }

    public static TempTranscript Raw(string content) => new("session.jsonl", content);

    /// <summary>A well-formed assistant usage record.</summary>
    public static string UsageLine(
        string messageId = "msg_1",
        string requestId = "req_1",
        long input = 0,
        long output = 0,
        long cacheWrite = 0,
        long cacheRead = 0,
        string model = "claude-opus-5",
        string timestamp = "2026-09-09T14:23:51.933Z")
    {
        var usage = string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"input_tokens\":{input},\"output_tokens\":{output},\"cache_creation_input_tokens\":{cacheWrite},\"cache_read_input_tokens\":{cacheRead}}}");

        return UsageLineRaw(usage, messageId, requestId, model, timestamp);
    }

    /// <summary>An assistant record whose <c>usage</c> object is supplied verbatim.</summary>
    public static string UsageLineRaw(
        string usageObject,
        string messageId = "msg_1",
        string requestId = "req_1",
        string model = "claude-opus-5",
        string timestamp = "2026-09-09T14:23:51.933Z",
        string? extraProperty = null)
    {
        var extra = extraProperty is null ? string.Empty : "," + extraProperty;
        return $$"""
            {"type":"assistant","timestamp":"{{timestamp}}","requestId":"{{requestId}}","message":{"id":"{{messageId}}","model":"{{model}}","usage":{{usageObject}}}{{extra}}}
            """;
    }

    public void Dispose()
    {
        try
        {
            SysIO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // A throwaway directory that outlives the test run is not worth failing over.
        }
    }
}
