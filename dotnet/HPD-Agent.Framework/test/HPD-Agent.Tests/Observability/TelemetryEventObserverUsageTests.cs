// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Observability;

public sealed class TelemetryEventObserverUsageTests
{
    [Fact]
    public async Task HandleAsync_MessageTurnFinishedWithUsage_RecordsTurnUsageMetrics()
    {
        var sourceName = $"HPD.Agent.TelemetryUsageTest.{Guid.NewGuid():N}";
        var measurements = new Dictionary<string, List<long>>();

        using var listener = CreateListener(sourceName, measurements);
        using var observer = new TelemetryEventObserver(sourceName);

        await observer.HandleAsync(new MessageTurnFinishedEvent(
            MessageTurnId: "turn-1",
            ConversationId: "conversation-1",
            AgentId: "agent-1",
            AgentName: "UsageAgent",
            Duration: TimeSpan.FromMilliseconds(42),
            Usage: new UsageDetails
            {
                InputTokenCount = 100,
                OutputTokenCount = 25,
                TotalTokenCount = 125,
                CachedInputTokenCount = 40,
                ReasoningTokenCount = 7
            }));

        measurements["agent.usage.input_tokens"].Should().Contain(100);
        measurements["agent.usage.output_tokens"].Should().Contain(25);
        measurements["agent.usage.total_tokens"].Should().Contain(125);
        measurements["agent.usage.cached_input_tokens"].Should().Contain(40);
        measurements["agent.usage.reasoning_tokens"].Should().Contain(7);
    }

    [Fact]
    public async Task HandleAsync_MessageTurnFinishedWithoutExplicitTotal_RecordsComputedTotalUsage()
    {
        var sourceName = $"HPD.Agent.TelemetryUsageTest.{Guid.NewGuid():N}";
        var measurements = new Dictionary<string, List<long>>();

        using var listener = CreateListener(sourceName, measurements);
        using var observer = new TelemetryEventObserver(sourceName);

        await observer.HandleAsync(new MessageTurnFinishedEvent(
            MessageTurnId: "turn-1",
            ConversationId: "conversation-1",
            AgentId: "agent-1",
            AgentName: "UsageAgent",
            Duration: TimeSpan.FromMilliseconds(42),
            Usage: new UsageDetails
            {
                InputTokenCount = 12,
                OutputTokenCount = 8
            }));

        measurements["agent.usage.total_tokens"].Should().Contain(20);
    }

    private static MeterListener CreateListener(
        string sourceName,
        Dictionary<string, List<long>> measurements)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == sourceName && instrument.Name.StartsWith("agent.usage.", StringComparison.Ordinal))
                meterListener.EnableMeasurementEvents(instrument);
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (!measurements.TryGetValue(instrument.Name, out var values))
            {
                values = [];
                measurements[instrument.Name] = values;
            }

            values.Add(measurement);
        });

        listener.Start();
        return listener;
    }
}
