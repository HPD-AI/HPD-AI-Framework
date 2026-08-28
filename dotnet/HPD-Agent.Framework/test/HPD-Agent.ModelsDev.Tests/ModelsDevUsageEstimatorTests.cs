using FluentAssertions;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace HPD.Agent.ModelsDev.Tests;

public sealed class ModelsDevUsageEstimatorTests
{
    [Fact]
    public async Task Values_exclusive_standard_cached_and_reasoning_buckets()
    {
        var estimator = CreateEstimator(new ModelsDevCost
        {
            Input = 2m, Output = 10m, CacheRead = 1m, Reasoning = 12m
        });
        var usage = new UsageDetails
        {
            InputTokenCount = 1_000_000,
            CachedInputTokenCount = 200_000,
            OutputTokenCount = 500_000,
            ReasoningTokenCount = 100_000
        };

        var result = await estimator.ValueAsync(new(Input(usage), []));

        result.Status.Should().Be(ProviderUsageValuationStatus.Complete);
        result.Amount.Should().Be(7m);
        result.Components.Should().Contain(item => item.Category == "input" && item.Quantity == 800_000);
        result.Components.Sum(item => item.Amount).Should().Be(result.Amount);
    }

    [Fact]
    public async Task Tier_uses_greatest_threshold_strictly_below_original_input()
    {
        var estimator = CreateEstimator(new ModelsDevCost
        {
            Input = 1m, Output = 2m,
            Tiers =
            [
                new() { Tier = new() { Type = "context", Size = 100 }, Input = 3m, Output = 4m },
                new() { Tier = new() { Type = "context", Size = 200 }, Input = 5m, Output = 6m }
            ]
        });

        var result = await estimator.ValueAsync(new(Input(new UsageDetails { InputTokenCount = 200, OutputTokenCount = 10 }), []));

        result.Components.Single(item => item.Category == "input").RateAmount.Should().Be(3m);
    }

    [Fact]
    public async Task Output_only_tiered_usage_remains_unavailable_without_context_input()
    {
        var estimator = CreateEstimator(new ModelsDevCost
        {
            Input = 1m, Output = 2m,
            Tiers = [new() { Tier = new() { Type = "context", Size = 100 }, Input = 3m, Output = 4m }]
        });

        var result = await estimator.ValueAsync(new(Input(new UsageDetails { OutputTokenCount = 10 }), []));

        result.Status.Should().Be(ProviderUsageValuationStatus.Unavailable);
        result.Amount.Should().BeNull();
    }

    [Fact]
    public async Task Output_only_untiered_usage_is_partial()
    {
        var estimator = CreateEstimator(new ModelsDevCost { Input = 1m, Output = 2m });

        var result = await estimator.ValueAsync(new(Input(new UsageDetails { OutputTokenCount = 10 }), []));

        result.Status.Should().Be(ProviderUsageValuationStatus.Partial);
        result.Amount.Should().Be(0.00002m);
        result.Diagnostics.Should().Contain(item => item.Code == "input_count_missing");
    }

    [Fact]
    public async Task Contradictory_subdivisions_are_invalid_not_clamped()
    {
        var estimator = CreateEstimator(new ModelsDevCost { Input = 1m, Output = 2m, CacheRead = 1m });

        var result = await estimator.ValueAsync(new(Input(new UsageDetails
        {
            InputTokenCount = 10,
            CachedInputTokenCount = 11
        }), []));

        result.Status.Should().Be(ProviderUsageValuationStatus.InvalidUsage);
        result.Amount.Should().BeNull();
    }

    [Fact]
    public async Task Valuation_round_trips_with_explicit_models_dev_discriminators()
    {
        var estimator = CreateEstimator(new ModelsDevCost { Input = 1m, Output = 2m });
        var result = await estimator.ValueAsync(new(Input(new UsageDetails { InputTokenCount = 10 }), []));
        var options = ModelsDevValuationJson.CreateOptions();

        var json = JsonSerializer.Serialize(result, options);
        var restored = JsonSerializer.Deserialize<ProviderUsageValuation>(json, options);

        json.Should().Contain("\"$type\"").And.Contain("\"models_dev\"");
        restored!.Amount.Should().Be(result.Amount);
        restored.Provenance.Should().BeOfType<ModelsDevValuationProvenance>();
        restored.Details.Should().BeOfType<ModelsDevValuationDetails>();
    }

    private static ModelsDevUsageEstimator CreateEstimator(ModelsDevCost cost)
    {
        var database = new ModelsDevDatabase
        {
            Providers = new(StringComparer.OrdinalIgnoreCase)
            {
                ["openai"] = new ModelsDevProvider
                {
                    Models = new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["gpt-test"] = new ModelsDevModel { Cost = cost }
                    }
                }
            }
        };
        var snapshot = new ModelsDevCatalogSnapshot(database, DateTimeOffset.UnixEpoch, null,
            new string('a', 64), new Uri("https://models.dev/api.json"), ModelsDevCatalogOrigin.Supplied);
        return new(ModelsDevStore.FromSnapshot(snapshot));
    }

    private static ProviderUsageMeasurement Input(UsageDetails usage) => new(
        "event-1", "turn-1", 1, "operation-1", null, 1,
        ProviderOperationKind.ChatModelResponse, ProviderClientFamily.Chat,
        ProviderOperationOutcome.Succeeded, usage, "openai", "gpt-test", "response-1");
}
