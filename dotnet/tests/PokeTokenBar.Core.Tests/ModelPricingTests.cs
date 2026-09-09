using PokeTokenBar.Core.Usage;

namespace PokeTokenBar.Core.Tests;

public sealed class ModelPricingTests
{
    [Fact]
    public void PricesAnExactTableMatch()
    {
        // 1M input at $5/Mtok plus 1M output at $25/Mtok.
        var cost = ModelPricing.Cost("claude-opus-4-8", 1_000_000, 1_000_000, 0, 0);

        Assert.Equal(30d, cost, precision: 6);
    }

    [Fact]
    public void PricesCacheTiersSeparately()
    {
        var cost = ModelPricing.Cost("claude-opus-4-8", 0, 0, 1_000_000, 1_000_000);

        Assert.Equal(6.75d, cost, precision: 6);
    }

    [Theory]
    [InlineData("claude-opus-5")]
    [InlineData("claude-opus-9-experimental")]
    public void FallsBackToTheFamilyWhenAVersionIsUnknown(string model)
    {
        // Without this a version bump would silently price at zero.
        var cost = ModelPricing.Cost(model, 1_000_000, 0, 0, 0);

        Assert.Equal(5d, cost, precision: 6);
    }

    [Theory]
    [InlineData("grok-4")]
    [InlineData("grok-codex-fast")]
    public void PricesGrokAtZeroEvenWhenTheNameLooksLikeAnotherFamily(string model)
    {
        // grok-codex-* would match the GPT family fallback and display an invented amount.
        // Grok reports its own charge, so the token rate must stay zero.
        Assert.Equal(0d, ModelPricing.Cost(model, 1_000_000, 1_000_000, 0, 0));
    }

    [Theory]
    [InlineData("antigravity/claude-sonnet-4-6")]
    [InlineData("antigravity/gemini-2.5-pro")]
    public void PricesAntigravityAtZeroDespiteAPriceableSuffix(string model)
    {
        // Subscription-billed with no per-token charge. The prefix is what makes these miss the
        // exact table, so without an explicit stop they would hit a family fallback.
        Assert.Equal(0d, ModelPricing.Cost(model, 1_000_000, 1_000_000, 0, 0));
    }

    [Fact]
    public void PricesAnUnrecognisedModelAtZeroRatherThanGuessing()
    {
        Assert.Equal(0d, ModelPricing.Cost("totally-unknown-model", 1_000_000, 1_000_000, 0, 0));
        Assert.Equal(ModelRate.Zero, ModelPricing.RateFor("totally-unknown-model"));
    }

    [Fact]
    public void PricesGeminiFamiliesButNotUnknownGeminiVariants()
    {
        Assert.True(ModelPricing.Cost("gemini-9-pro", 1_000_000, 0, 0, 0) > 0);
        Assert.True(ModelPricing.Cost("gemini-9-flash", 1_000_000, 0, 0, 0) > 0);
        Assert.Equal(0d, ModelPricing.Cost("gemini-9-ultra", 1_000_000, 0, 0, 0));
    }

    [Fact]
    public void CostIsLinearSoAggregatingBeforePricingIsEquivalent()
    {
        // The aggregator prices summed tokens rather than each entry; that is only valid
        // because rates are linear.
        var separate = ModelPricing.Cost("claude-opus-4-8", 1_000, 0, 0, 0)
            + ModelPricing.Cost("claude-opus-4-8", 2_000, 0, 0, 0);
        var combined = ModelPricing.Cost("claude-opus-4-8", 3_000, 0, 0, 0);

        Assert.Equal(combined, separate, precision: 12);
    }
}
