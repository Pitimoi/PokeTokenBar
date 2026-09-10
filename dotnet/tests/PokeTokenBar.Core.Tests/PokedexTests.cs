using System.Net;
using System.Text;
using PokeTokenBar.Core.Companions;
using PokeTokenBar.Core.Pokedex;

namespace PokeTokenBar.Core.Tests;

public sealed class PokeApiClientTests
{
    [Theory]
    [InlineData("https://pokeapi.co/api/v2/pokemon-species/25/", 25)]
    [InlineData("https://pokeapi.co/api/v2/evolution-chain/10/", 10)]
    [InlineData("https://pokeapi.co/api/v2/pokemon-species/133", 133)]
    public void ParsesTheTrailingIdOutOfAnApiUrl(string url, int expected)
    {
        // The id is parsed rather than the URL followed: a server-supplied URL is the input an
        // SSRF guard exists for, and an integer cannot redirect anything.
        Assert.Equal(expected, PokeApiClient.TrailingId(url));
    }

    [Theory]
    [InlineData("https://evil.example.com/api/v2/pokemon-species/25/", 25)]
    [InlineData("http://127.0.0.1:8080/25/", 25)]
    public void ParsingIgnoresTheHostEntirely(string url, int expected)
    {
        // Even a hostile URL yields only a number, which is then range-checked. Nothing about
        // the host survives to influence a request.
        Assert.Equal(expected, PokeApiClient.TrailingId(url));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("https://pokeapi.co/api/v2/pokemon-species/")]
    [InlineData("not a url at all")]
    public void RejectsUrlsWithNoUsableId(string? url)
    {
        Assert.Null(PokeApiClient.TrailingId(url));
    }

    [Fact]
    public async Task ReadsTheBaseSpeciesIndex()
    {
        var body = """
        {"data":{"pokemonspecies":[
          {"id":1,"capture_rate":45,"is_legendary":false,"is_mythical":false},
          {"id":10,"capture_rate":255,"is_legendary":false,"is_mythical":false},
          {"id":150,"capture_rate":3,"is_legendary":true,"is_mythical":false}
        ]}}
        """;

        var client = new PokeApiClient(new HttpClient(new StubHandler(Json(body))));
        var index = await client.GetBaseSpeciesAsync();

        Assert.Equal(3, index.Count);
        Assert.Equal(Rarity.Rare, index[0].Rarity);
        Assert.Equal(Rarity.Common, index[1].Rarity);
        Assert.Equal(Rarity.Legendary, index[2].Rarity);
    }

    [Fact]
    public async Task DropsIndexEntriesOutsideTheSpriteRange()
    {
        var body = """
        {"data":{"pokemonspecies":[
          {"id":1,"capture_rate":45},
          {"id":0,"capture_rate":45},
          {"id":9999,"capture_rate":45}
        ]}}
        """;

        var index = await new PokeApiClient(new HttpClient(new StubHandler(Json(body)))).GetBaseSpeciesAsync();

        Assert.Single(index);
        Assert.Equal(1, index[0].Id);
    }

    [Fact]
    public async Task WalksABranchingChainIntoSeparatePaths()
    {
        // Eevee's shape: one root, several leaves.
        var body = """
        {"chain":{"species":{"url":"https://pokeapi.co/api/v2/pokemon-species/133/"},"evolves_to":[
          {"species":{"url":"https://pokeapi.co/api/v2/pokemon-species/134/"},"evolves_to":[]},
          {"species":{"url":"https://pokeapi.co/api/v2/pokemon-species/135/"},"evolves_to":[]}
        ]}}
        """;

        var paths = await new PokeApiClient(new HttpClient(new StubHandler(Json(body)))).GetEvolutionPathsAsync(67);

        Assert.Equal(2, paths.Count);
        Assert.Equal([133, 134], paths[0]);
        Assert.Equal([133, 135], paths[1]);
    }

    [Fact]
    public async Task WalksALinearChain()
    {
        var body = """
        {"chain":{"species":{"url":"https://pokeapi.co/api/v2/pokemon-species/4/"},"evolves_to":[
          {"species":{"url":"https://pokeapi.co/api/v2/pokemon-species/5/"},"evolves_to":[
            {"species":{"url":"https://pokeapi.co/api/v2/pokemon-species/6/"},"evolves_to":[]}
          ]}
        ]}}
        """;

        var paths = await new PokeApiClient(new HttpClient(new StubHandler(Json(body)))).GetEvolutionPathsAsync(2);

        Assert.Single(paths);
        Assert.Equal([4, 5, 6], paths[0]);
    }

    [Fact]
    public async Task RejectsANonJsonResponse()
    {
        var html = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>captive portal</html>", Encoding.UTF8, "text/html"),
        };

        var index = await new PokeApiClient(new HttpClient(new StubHandler(html))).GetBaseSpeciesAsync();

        Assert.Empty(index);
    }

    [Fact]
    public async Task RejectsAResponseOverTheCap()
    {
        var huge = Json("{\"data\":{\"pokemonspecies\":[]}}");
        huge.Content = new ByteArrayContent(new byte[PokeApiClient.MaxResponseBytes + 1024]);
        huge.Content.Headers.ContentType = new("application/json");

        Assert.Empty(await new PokeApiClient(new HttpClient(new StubHandler(huge))).GetBaseSpeciesAsync());
    }

    [Fact]
    public async Task DegradesWhenOffline()
    {
        var client = new PokeApiClient(new HttpClient(new ThrowingHandler()));

        Assert.Empty(await client.GetBaseSpeciesAsync());
        Assert.Empty(await client.GetEvolutionPathsAsync(1));
        Assert.Null(await client.GetChainIdAsync(1));
    }

    [Fact]
    public async Task NeverRequestsAnOutOfRangeSpecies()
    {
        var handler = new StubHandler(Json("{}"));
        var client = new PokeApiClient(new HttpClient(handler));

        Assert.Null(await client.GetChainIdAsync(99_999));
        Assert.Null(await client.GetChainIdAsync(0));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task OnlyTalksToThePinnedHosts()
    {
        var handler = new StubHandler(() => Json("{\"data\":{\"pokemonspecies\":[]}}"));
        var client = new PokeApiClient(new HttpClient(handler));

        await client.GetBaseSpeciesAsync();
        await client.GetChainIdAsync(25);

        Assert.All(handler.Hosts, host =>
            Assert.True(
                host is PokeApiClient.GraphQlHost or PokeApiClient.RestHost,
                $"unexpected host {host}"));
        Assert.All(handler.Schemes, scheme => Assert.Equal("https", scheme));
    }


    [Fact]
    public async Task TruncatesAtASpeciesOutsideTheSpriteRangeRatherThanDiscardingTheLine()
    {
        // Chain 110: Teddiursa -> Ursaring -> Ursaluna. Ursaluna is 901, beyond the animated
        // sprite range, and dropping the branch made Teddiursa look single-form.
        var body = """
        {"chain":{"species":{"url":"https://pokeapi.co/api/v2/pokemon-species/216/"},"evolves_to":[
          {"species":{"url":"https://pokeapi.co/api/v2/pokemon-species/217/"},"evolves_to":[
            {"species":{"url":"https://pokeapi.co/api/v2/pokemon-species/901/"},"evolves_to":[]}
          ]}
        ]}}
        """;

        var paths = await new PokeApiClient(new HttpClient(new StubHandler(Json(body)))).GetEvolutionPathsAsync(110);

        Assert.Single(paths);
        Assert.Equal([216, 217], paths[0]);
    }

    [Fact]
    public async Task YieldsNothingWhenTheRootItselfIsOutOfRange()
    {
        var body = """
        {"chain":{"species":{"url":"https://pokeapi.co/api/v2/pokemon-species/906/"},"evolves_to":[]}}
        """;

        Assert.Empty(await new PokeApiClient(new HttpClient(new StubHandler(Json(body)))).GetEvolutionPathsAsync(1));
    }

    [Fact]
    public async Task KeepsOnlyTheInRangePartOfABranchingChain()
    {
        // Eevee: some branches are in range, others (Sylveon, 700) are not.
        var body = """
        {"chain":{"species":{"url":"https://pokeapi.co/api/v2/pokemon-species/133/"},"evolves_to":[
          {"species":{"url":"https://pokeapi.co/api/v2/pokemon-species/134/"},"evolves_to":[]},
          {"species":{"url":"https://pokeapi.co/api/v2/pokemon-species/700/"},"evolves_to":[]}
        ]}}
        """;

        var paths = await new PokeApiClient(new HttpClient(new StubHandler(Json(body)))).GetEvolutionPathsAsync(67);

        Assert.Equal(2, paths.Count);
        Assert.Equal([133, 134], paths[0]);
        // The out-of-range branch degrades to the root alone rather than vanishing.
        Assert.Equal([133], paths[1]);
    }

    private static HttpResponseMessage Json(string body)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        return response;
    }

    /// <summary>
    /// Builds a fresh response per call. The client disposes each response, so a handler that
    /// hands out one instance fails on the second request with ObjectDisposedException.
    /// </summary>
    private sealed class StubHandler(Func<HttpResponseMessage> factory) : HttpMessageHandler
    {
        public StubHandler(HttpResponseMessage single)
            : this(() => single)
        {
        }

        public int Calls { get; private set; }

        public List<string> Hosts { get; } = [];

        public List<string> Schemes { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            Hosts.Add(request.RequestUri!.Host);
            Schemes.Add(request.RequestUri!.Scheme);
            return Task.FromResult(factory());
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }
}

public sealed class SpeciesLibraryTests
{
    [Fact]
    public async Task FallsBackToBuiltInLinesWithNoNetwork()
    {
        // A companion must exist even with no network and no cache.
        using var directory = new TempPokedexDirectory();
        var library = new SpeciesLibrary(new PokeApiClient(new HttpClient(new OfflineHandler())), directory.Path);

        var line = await library.DrawAsync(12345);

        Assert.NotEmpty(line.SpeciesPath);
        Assert.Contains(line, EvolutionLines.All);
    }

    [Fact]
    public async Task TheSameSeedDrawsTheSameLine()
    {
        using var directory = new TempPokedexDirectory();
        var library = new SpeciesLibrary(new PokeApiClient(new HttpClient(new OfflineHandler())), directory.Path);

        var first = await library.DrawAsync(999);
        var second = await library.DrawAsync(999);

        Assert.Equal(first.SpeciesPath, second.SpeciesPath);
    }

    [Fact]
    public async Task DoesNotCacheATruncatedIndex()
    {
        // Caching a short index would narrow the pool permanently.
        using var directory = new TempPokedexDirectory();
        var body = """{"data":{"pokemonspecies":[{"id":1,"capture_rate":45}]}}""";
        var handler = new SingleResponseHandler(body);
        var library = new SpeciesLibrary(new PokeApiClient(new HttpClient(handler)), directory.Path);

        await library.DrawAsync(1);

        Assert.False(library.HasCachedIndex);
    }

    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }

    private sealed class SingleResponseHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    private sealed class TempPokedexDirectory : IDisposable
    {
        public TempPokedexDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "ptb-pokedex-" + Guid.NewGuid().ToString("N")[..10]);
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
