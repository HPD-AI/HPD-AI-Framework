using System.Collections.Immutable;
using HPD.Agent.Tests.TestToolHarnesses;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Skills;

public sealed class SkillCatalogTests
{
    [Fact]
    public async Task Reload_PublishesNewEpochWithoutChangingExistingLease()
    {
        var first = Snapshot(0);
        using var catalog = new SkillCatalog(first, (epoch, _) => ValueTask.FromResult(Snapshot(epoch)));
        using var oldLease = catalog.Acquire();

        var result = await catalog.ReloadAsync(new SkillReloadRequest());
        using var newLease = catalog.Acquire();

        Assert.True(result.Published);
        Assert.Equal(0, oldLease.Snapshot.Epoch);
        Assert.Equal(1, newLease.Snapshot.Epoch);
        Assert.NotSame(oldLease.Snapshot.Graph, newLease.Snapshot.Graph);
    }

    [Fact]
    public async Task ReloadFailure_RetainsCurrentSnapshot()
    {
        using var catalog = new SkillCatalog(
            Snapshot(7),
            (_, _) => ValueTask.FromException<SkillCatalogSnapshot>(new InvalidOperationException("invalid package")));

        var result = await catalog.ReloadAsync(new SkillReloadRequest("source change"));
        using var lease = catalog.Acquire();

        Assert.False(result.Published);
        Assert.Equal(7, result.Epoch);
        Assert.Equal(7, lease.Snapshot.Epoch);
        Assert.Contains("invalid package", result.Error);
    }

    [Fact]
    public async Task ConcurrentAcquire_SeesOnlyCompleteEpochs()
    {
        using var catalog = new SkillCatalog(
            Snapshot(0),
            async (epoch, cancellationToken) =>
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                return Snapshot(epoch);
            });
        var observed = new System.Collections.Concurrent.ConcurrentBag<long>();
        var readers = Enumerable.Range(0, 50).Select(async _ =>
        {
            await Task.Yield();
            using var lease = catalog.Acquire();
            observed.Add(lease.Snapshot.Epoch);
            Assert.Equal(lease.Snapshot.Functions.Length, lease.Snapshot.Graph.Nodes.Count);
        });

        await Task.WhenAll(readers.Append(catalog.ReloadAsync(new SkillReloadRequest()).AsTask()));

        Assert.All(observed, epoch => Assert.Contains(epoch, new long[] { 0, 1 }));
    }

    private static SkillCatalogSnapshot Snapshot(long epoch)
    {
        var builder = new AgentBuilder().WithToolHarness<CombinedCapabilitiesTools>();
        var factory = Assert.Single(
            builder._selectedToolHarnessFactories,
            candidate => candidate.Name == nameof(CombinedCapabilitiesTools));
        var functions = factory.CreateFunctions(new CombinedCapabilitiesTools(), null, null)
            .Where(function => function.AdditionalProperties?.ContainsKey(
                HPDCapabilityMetadata.AdditionalPropertiesKey) == true)
            .ToImmutableArray();
        return new SkillCatalogSnapshot
        {
            Epoch = epoch,
            Functions = functions,
            Graph = CapabilityGraph.CreateFromFunctions(functions),
            Skills = ImmutableDictionary<CapabilityId, SkillDescriptor>.Empty
        };
    }
}
