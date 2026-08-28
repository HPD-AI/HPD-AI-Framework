using FluentAssertions;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Core;

public sealed class ProviderUsageValuationTests
{
    [Fact]
    public void Collector_deduplicates_orders_and_rejects_post_freeze_usage()
    {
        var collector = new MessageTurnUsageCollector("turn-1");
        var later = Measurement("source-2", "response-2") with { ThreadSequenceNumber = 2 };
        var earlier = Measurement("source-1", "response-1") with { ThreadSequenceNumber = 1 };

        collector.TryAcceptCommitted(later).Should().BeTrue();
        collector.TryAcceptCommitted(earlier).Should().BeTrue();
        collector.TryAcceptCommitted(earlier).Should().BeFalse();
        collector.TryAcceptCommitted(earlier with { SourceEventId = "source-duplicate-operation" }).Should().BeFalse();
        collector.Freeze().Operations.Select(item => item.SourceEventId).Should().ContainInOrder("source-1", "source-2");

        var action = () => collector.TryAcceptCommitted(Measurement("source-3", "response-3"));
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Usage_accumulator_honors_delta_snapshot_and_final_only_semantics()
    {
        var delta = new ProviderUsageAccumulator(UsageUpdateSemantics.Delta);
        delta.Observe(new UsageDetails { InputTokenCount = 2 });
        delta.Observe(new UsageDetails { InputTokenCount = 3 });
        delta.Usage!.InputTokenCount.Should().Be(5);

        var snapshot = new ProviderUsageAccumulator(UsageUpdateSemantics.CumulativeSnapshot);
        snapshot.Observe(new UsageDetails { InputTokenCount = 2 });
        snapshot.Observe(new UsageDetails { InputTokenCount = 3 });
        snapshot.Usage!.InputTokenCount.Should().Be(3);

        var finalOnly = new ProviderUsageAccumulator(UsageUpdateSemantics.FinalOnly);
        finalOnly.Observe(new UsageDetails { InputTokenCount = 2 });
        var action = () => finalOnly.Observe(new UsageDetails { InputTokenCount = 3 });
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Custom_adapter_declaration_precedes_the_shipped_catalog()
    {
        var declaration = new ProviderStreamingUsageSemanticsDeclaration(
            ProviderClientFamily.Chat, UsageUpdateSemantics.Delta,
            "custom.adapter", "custom.adapter.delta.fixture");

        ProviderStreamingUsageSemanticsCatalog.Resolve(
                "not-shipped", ProviderClientFamily.Chat, declaration)
            .Should().Be(UsageUpdateSemantics.Delta);
    }

    [Theory]
    [InlineData(ProviderOperationKind.ImageGeneration, ProviderClientFamily.ImageGeneration)]
    [InlineData(ProviderOperationKind.Embeddings, ProviderClientFamily.Embeddings)]
    [InlineData(ProviderOperationKind.HostedFileOperation, ProviderClientFamily.HostedFiles)]
    [InlineData(ProviderOperationKind.VoiceActivityDetection, ProviderClientFamily.VoiceActivityDetection)]
    [InlineData(ProviderOperationKind.EndOfTurnDetection, ProviderClientFamily.EndOfTurnDetection)]
    public async Task Shared_provider_boundary_terminalizes_every_non_model_family(
        ProviderOperationKind kind,
        ProviderClientFamily family)
    {
        var collector = new MessageTurnUsageCollector("turn-boundary");
        collector.ConfigureCommitter((terminal, _) =>
        {
            var usageEvent = (ProviderOperationUsageEvent)terminal with { ThreadSequenceNumber = 1 };
            collector.TryAcceptCommitted(new(
                usageEvent.EventId, usageEvent.MessageTurnId, usageEvent.ThreadSequenceNumber,
                usageEvent.OperationId, usageEvent.LogicalOperationId, usageEvent.Attempt,
                usageEvent.OperationKind, usageEvent.Family, usageEvent.Outcome, usageEvent.Usage,
                usageEvent.ProviderKey, usageEvent.ModelId, usageEvent.ResponseId));
            return ValueTask.FromResult<AgentEvent>(usageEvent);
        });
        using var scope = ProviderOperationAccountingScope.Push(collector);

        var result = await ProviderOperationAccounting.ExecuteAsync(
            kind, family, "test-provider", "test-model",
            () => Task.FromResult("response"),
            _ => new UsageDetails { TotalTokenCount = 1 },
            value => value);

        result.Should().Be("response");
        var measurement = (await collector.CloseAsync()).Operations.Should().ContainSingle().Subject;
        measurement.OperationKind.Should().Be(kind);
        measurement.Family.Should().Be(family);
        measurement.Outcome.Should().Be(ProviderOperationOutcome.Succeeded);
    }

    [Fact]
    public async Task Cancelled_close_can_be_retried_after_registered_attempt_finishes()
    {
        var collector = new MessageTurnUsageCollector("turn-1");
        var attempt = Attempt("operation-1");
        collector.RegisterAttempt(attempt);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await collector.CloseAsync(cancellation.Token));

        collector.TryAcceptCommitted(Measurement("source-1", "response-1") with
        {
            OperationId = attempt.OperationId
        }).Should().BeTrue();
        (await collector.CloseAsync()).Operations.Should().ContainSingle();
    }

    [Fact]
    public async Task Inherited_background_context_cannot_observe_collector_after_close_and_nested_scope_restores_outer()
    {
        var outer = new MessageTurnUsageCollector("outer-turn");
        var collector = new MessageTurnUsageCollector("turn-1");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<MessageTurnUsageCollector?> inherited;
        using (ProviderOperationAccountingScope.Push(outer))
        {
            using (ProviderOperationAccountingScope.Push(collector))
            {
                inherited = Task.Run(async () =>
                {
                    await release.Task;
                    return ProviderOperationAccountingScope.Current;
                });
            }
            ProviderOperationAccountingScope.Current.Should().BeSameAs(outer);
        }

        ProviderOperationAccountingScope.Current.Should().BeNull();
        await collector.CloseAsync();
        release.SetResult();
        (await inherited).Should().BeNull();
        var action = () => collector.RegisterAttempt(Attempt("late-operation"));
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Provider_reported_authority_requires_correlated_observation()
    {
        var measurement = Measurement("source-1", "response-1");
        var authority = new ProviderReportedUsageValuationAuthority();

        var result = await authority.ValueAsync(new(measurement,
        [
            new ProviderReportedMonetaryObservation(0.25m, "USD", "openai", "response-1", "usage.cost")
        ]));

        result.AuthorityKind.Should().Be(ProviderUsageValuationAuthorityKind.ProviderReported);
        result.Status.Should().Be(ProviderUsageValuationStatus.Complete);
        result.Amount.Should().Be(0.25m);
        result.Provenance.Should().BeOfType<ProviderReportedValuationProvenance>();
    }

    [Fact]
    public void Projector_selects_only_one_authority_per_source_and_never_mixes_currencies()
    {
        var summary = new MessageTurnUsageSummary([Measurement("source-1", "response-1"), Measurement("source-2", "response-2")]);
        var valuations = new[]
        {
            Valuation("source-1", "invoice", ProviderUsageValuationAuthorityKind.InvoiceReconciled, 1m, "USD"),
            Valuation("source-1", "provider", ProviderUsageValuationAuthorityKind.ProviderReported, 2m, "USD"),
            Valuation("source-2", "provider", ProviderUsageValuationAuthorityKind.ProviderReported, 3m, "EUR")
        };

        var projection = ProviderUsageValuationProjector.ProjectPreferred(summary, valuations);

        projection.SelectedValuations.Should().HaveCount(2);
        projection.KnownAmountsByCurrency.Should().BeEquivalentTo(new Dictionary<string, decimal>
        {
            ["USD"] = 1m,
            ["EUR"] = 3m
        });
    }

    private static ProviderUsageMeasurement Measurement(string sourceEventId, string responseId) => new(
        sourceEventId, "turn-1", 1, $"operation-{sourceEventId}", null, 1,
        ProviderOperationKind.ChatModelResponse, ProviderClientFamily.Chat,
        ProviderOperationOutcome.Succeeded, new UsageDetails { InputTokenCount = 1 },
        "openai", "gpt-test", responseId);

    private static ProviderOperationAttempt Attempt(string operationId) => new(
        operationId, null, 1, ProviderOperationKind.ChatModelResponse,
        ProviderClientFamily.Chat, "openai", "gpt-test");

    private static ProviderUsageValuation Valuation(string source, string authority,
        ProviderUsageValuationAuthorityKind kind, decimal amount, string currency) => new(
        source, authority, kind, ProviderUsageValuationStatus.Complete, amount, currency,
        [new("amount", 1, "operation", amount, currency, 1, "operation", amount, null)], [],
        kind switch
        {
            ProviderUsageValuationAuthorityKind.ProviderReported => new ProviderReportedValuationProvenance("openai", null, "cost"),
            ProviderUsageValuationAuthorityKind.InvoiceReconciled => new InvoiceValuationProvenance("invoice-1", "line-1"),
            ProviderUsageValuationAuthorityKind.ContractRate => new ContractValuationProvenance("contract-1", "v1"),
            _ => new AuthorityAttemptValuationProvenance(authority, "v1", "test")
        },
        null, []);
}
