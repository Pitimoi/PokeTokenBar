using PokeTokenBar.Core.Usage;

// Temporary parity harness, replaced by the stdio protocol host next. The real protocol
// accepts no paths from its caller — see invariant 3 in dotnet/README.md.
if (args.Length == 0)
{
    Console.Error.WriteLine("usage: PokeTokenBar.Sidecar <transcript.jsonl|directory>");
    return 2;
}

var target = args[0];
var scan = Directory.Exists(target)
    ? await ClaudeTranscriptReader.ReadDirectoryAsync(target)
    : await ClaudeTranscriptReader.ReadFileAsync(target);

long input = 0, output = 0, cacheWrite = 0, cacheRead = 0;
foreach (var entry in scan.Entries)
{
    input += entry.Input;
    output += entry.Output;
    cacheWrite += entry.CacheWrite;
    cacheRead += entry.CacheRead;
}

Console.WriteLine($"unique entries: {scan.Entries.Count}");
Console.WriteLine($"input         : {input}");
Console.WriteLine($"output        : {output}");
Console.WriteLine($"cacheWrite    : {cacheWrite}");
Console.WriteLine($"cacheRead     : {cacheRead}");
Console.WriteLine($"TOTAL         : {input + output + cacheWrite + cacheRead}");
Console.WriteLine();
Console.WriteLine($"stats         : {scan.Stats}");
return 0;
