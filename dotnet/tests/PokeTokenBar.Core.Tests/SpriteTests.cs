using System.Net;
using System.Text;
using PokeTokenBar.Core.Sprites;

namespace PokeTokenBar.Core.Tests;

public sealed class SpriteSourceTests
{
    [Fact]
    public void BuildsStaticAndShinyUrls()
    {
        Assert.Equal(
            "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/25.png",
            SpriteSource.UrlFor(new SpriteRequest { SpeciesId = 25 })!.ToString());

        Assert.Equal(
            "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/shiny/25.png",
            SpriteSource.UrlFor(new SpriteRequest { SpeciesId = 25, Shiny = true })!.ToString());
    }

    [Fact]
    public void BuildsAnimatedUrls()
    {
        Assert.Contains(
            "versions/generation-v/black-white/animated/25.gif",
            SpriteSource.UrlFor(new SpriteRequest { SpeciesId = 25, Animated = true })!.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void EveryUrlIsHttpsOnThePinnedHost()
    {
        for (var id = 1; id <= SpriteSource.MaxSpeciesId; id += 37)
        {
            foreach (var animated in new[] { false, true })
            {
                foreach (var shiny in new[] { false, true })
                {
                    var url = SpriteSource.UrlFor(new SpriteRequest
                    {
                        SpeciesId = id,
                        Animated = animated,
                        Shiny = shiny,
                    });

                    if (url is null)
                    {
                        continue;
                    }

                    Assert.Equal("https", url.Scheme);
                    Assert.Equal(SpriteSource.Host, url.Host);
                }
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(SpriteSource.MaxSpeciesId + 1)]
    [InlineData(int.MaxValue)]
    public void RejectsSpeciesIdsOutOfRange(int speciesId)
    {
        var request = new SpriteRequest { SpeciesId = speciesId };

        Assert.Null(SpriteSource.UrlFor(request));
        Assert.Null(SpriteSource.FileNameFor(request));
    }

    [Fact]
    public void RejectsAnimatedBeyondTheGenerationsThatHaveIt()
    {
        var request = new SpriteRequest { SpeciesId = SpriteSource.MaxAnimatedSpeciesId + 1, Animated = true };

        Assert.Null(SpriteSource.UrlFor(request));
        // The same species is fine as a static sprite.
        Assert.NotNull(SpriteSource.UrlFor(request with { Animated = false }));
    }

    [Theory]
    [InlineData(25, false, false, "25-s.png")]
    [InlineData(25, true, false, "25-a.gif")]
    [InlineData(25, false, true, "25-shs.png")]
    [InlineData(25, true, true, "25-sha.gif")]
    public void FileNamesMatchTheOriginalScheme(int id, bool animated, bool shiny, string expected)
    {
        // Kept identical so an existing cache from the Swift app stays valid.
        var name = SpriteSource.FileNameFor(new SpriteRequest
        {
            SpeciesId = id,
            Animated = animated,
            Shiny = shiny,
        });

        Assert.Equal(expected, name);
    }

    [Fact]
    public void EveryGeneratedFileNameIsAcceptedByTheValidator()
    {
        for (var id = 1; id <= SpriteSource.MaxSpeciesId; id += 11)
        {
            foreach (var animated in new[] { false, true })
            {
                foreach (var shiny in new[] { false, true })
                {
                    var name = SpriteSource.FileNameFor(new SpriteRequest
                    {
                        SpeciesId = id,
                        Animated = animated,
                        Shiny = shiny,
                    });

                    if (name is not null)
                    {
                        Assert.True(SpriteSource.IsCacheFileName(name), $"validator rejected {name}");
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("..\\..\\windows\\system32\\cmd.exe")]
    [InlineData("25-s.png/../../evil")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("25-s.exe")]
    [InlineData("25-s.png.exe")]
    [InlineData("abc-s.png")]
    [InlineData("25-x.png")]
    [InlineData("")]
    [InlineData(null)]
    public void ValidatorRejectsAnythingItCouldNotHaveProduced(string? fileName)
    {
        // The host validates what the sidecar hands it before joining it to a directory, so a
        // bug on either side cannot become a path escape.
        Assert.False(SpriteSource.IsCacheFileName(fileName));
    }
}

public sealed class SpriteCacheTests
{
    private static readonly byte[] PngBytes = [0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2, 3, 4];

    [Fact]
    public async Task DownloadsThenServesFromDisk()
    {
        using var directory = new TempDirectory();
        var handler = new StubHandler(Image(PngBytes));
        var cache = new SpriteCache(new HttpClient(handler), directory.Path);

        var first = await cache.GetAsync(new SpriteRequest { SpeciesId = 25 });
        var second = await cache.GetAsync(new SpriteRequest { SpeciesId = 25 });

        Assert.Equal("25-s.png", first.FileName);
        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.Equal(1, handler.Calls);
        Assert.True(File.Exists(Path.Combine(directory.Path, "25-s.png")));
    }

    [Fact]
    public async Task RejectsANonImageResponse()
    {
        // A proxy or captive portal returning an HTML error page must not be cached as a sprite.
        using var directory = new TempDirectory();
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>nope</html>", Encoding.UTF8, "text/html"),
        };
        var cache = new SpriteCache(new HttpClient(new StubHandler(response)), directory.Path);

        var result = await cache.GetAsync(new SpriteRequest { SpeciesId = 25 });

        Assert.Null(result.FileName);
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task RejectsAResponseOverTheSizeCap()
    {
        using var directory = new TempDirectory();
        var oversized = Image(new byte[SpriteCache.MaxSpriteBytes + 1024]);
        var cache = new SpriteCache(new HttpClient(new StubHandler(oversized)), directory.Path);

        var result = await cache.GetAsync(new SpriteRequest { SpeciesId = 25 });

        Assert.Null(result.FileName);
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task RejectsAnOversizedBodyThatUnderstatesItsLength()
    {
        // Content-Length is a claim; the cap has to hold while reading too.
        using var directory = new TempDirectory();
        var lying = Image(new byte[SpriteCache.MaxSpriteBytes + 1024]);
        lying.Content.Headers.ContentLength = 128;
        var cache = new SpriteCache(new HttpClient(new StubHandler(lying)), directory.Path);

        var result = await cache.GetAsync(new SpriteRequest { SpeciesId = 25 });

        Assert.Null(result.FileName);
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task RejectsANonSuccessStatus()
    {
        using var directory = new TempDirectory();
        var missing = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new ByteArrayContent(PngBytes),
        };
        missing.Content.Headers.ContentType = new("image/png");
        var cache = new SpriteCache(new HttpClient(new StubHandler(missing)), directory.Path);

        Assert.Null((await cache.GetAsync(new SpriteRequest { SpeciesId = 25 })).FileName);
    }

    [Fact]
    public async Task DegradesRatherThanThrowingWhenOffline()
    {
        // A companion without artwork still has a name and a level.
        using var directory = new TempDirectory();
        var cache = new SpriteCache(new HttpClient(new ThrowingHandler()), directory.Path);

        var result = await cache.GetAsync(new SpriteRequest { SpeciesId = 25 });

        Assert.Null(result.FileName);
        Assert.Equal("network unavailable", result.Failure);
    }

    [Fact]
    public async Task LeavesNoTemporaryFilesBehind()
    {
        using var directory = new TempDirectory();
        var cache = new SpriteCache(new HttpClient(new StubHandler(Image(PngBytes))), directory.Path);

        await cache.GetAsync(new SpriteRequest { SpeciesId = 25 });

        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp-*"));
    }

    [Fact]
    public async Task NeverRequestsAnOutOfRangeSpecies()
    {
        using var directory = new TempDirectory();
        var handler = new StubHandler(Image(PngBytes));
        var cache = new SpriteCache(new HttpClient(handler), directory.Path);

        await cache.GetAsync(new SpriteRequest { SpeciesId = 99_999 });

        Assert.Equal(0, handler.Calls);
    }

    private static HttpResponseMessage Image(byte[] bytes)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes),
        };
        response.Content.Headers.ContentType = new("image/png");
        return response;
    }

    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public Uri? LastUrl { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastUrl = request.RequestUri;
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "ptb-sprites-" + Guid.NewGuid().ToString("N")[..10]);
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
