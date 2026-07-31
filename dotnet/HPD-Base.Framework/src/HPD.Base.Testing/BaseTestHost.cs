using HPD.Base.Application.DependencyInjection;
using HPD.Base.Application.Hosting;
using HPD.Base.Application.Sessions;
using HPD.Base.Runtime;
using HPD.Base.Runtime.Events;
using HPD.Base.Policy;
using HPD.Base.Events;
using HPD.Base.Files.Policy;
using HPD.Base.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Base.Testing;

/// <summary>Owns a deterministic in-process HPD.BASE application host.</summary>
public sealed class BaseTestHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private BaseTestHost(
        ServiceProvider provider,
        BaseTestTimeProvider time,
        BaseTestFaults faults,
        BaseTestProbe probe,
        BaseTestPolicy policy)
    {
        _provider = provider;
        Time = time;
        Faults = faults;
        Probe = probe;
        Policy = policy;
    }

    public BaseTestTimeProvider Time { get; }
    public BaseTestFaults Faults { get; }
    public BaseTestProbe Probe { get; }
    public BaseTestPolicy Policy { get; }
    public HPDBaseInstalledFeatures Features =>
        _provider.GetRequiredService<HPDBaseInstalledFeatures>();

    public static ValueTask<BaseTestHost> CreateAsync(
        Action<HPDBaseApplicationBuilder> configure,
        DateTimeOffset? initialTime = null)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var services = new ServiceCollection();
        services.AddLogging();
        var time = new BaseTestTimeProvider(
            initialTime ?? new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        services.AddSingleton<TimeProvider>(time);
        services.AddHPDBase(configure);
        var policy = new BaseTestPolicy();
        services.AddSingleton(policy);
        services.Replace(
            ServiceDescriptor.Singleton<IPolicyEvaluator, BaseTestPolicyEvaluator>());
        services.Replace(
            ServiceDescriptor.Singleton<
                IFilePolicyOrchestrator,
                BaseTestFilePolicyOrchestrator>());
        var faults = new BaseTestFaults();
        services.AddSingleton(faults);
        services.AddSingleton<BaseTestProbe>();
        services.AddSingleton<IBaseCommittedMutationObserver>(
            provider => provider.GetRequiredService<BaseTestProbe>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IBaseApplicationInitializer,
                BaseTestStoreInitializer>());
        ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
        return ValueTask.FromResult(new BaseTestHost(
            provider,
            time,
            faults,
            provider.GetRequiredService<BaseTestProbe>(),
            policy));
    }

    public BaseSession Session(
        PrincipalContext principal,
        Action<BaseSessionOptions>? configure = null) =>
        _provider.GetRequiredService<IBaseSessionFactory>().For(principal, configure);

    public T GetRequiredService<T>() where T : notnull =>
        _provider.GetRequiredService<T>();

    /// <summary>
    /// Reads a bounded snapshot of the SQLite durable mutation journal.
    /// </summary>
    public async ValueTask<IReadOnlyList<BaseMutationJournalEntry>> JournalAsync(
        int maximum = 1_000,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, 1);
        ITransactionalMutationJournalStore journal =
            _provider.GetService<ITransactionalMutationJournalStore>()
            ?? throw new InvalidOperationException(
                "Journal inspection requires a transactional journal provider.");
        BaseMutationJournalBounds bounds =
            await journal.GetMutationJournalBoundsAsync(cancellationToken)
                .ConfigureAwait(false);
        var entries = new List<BaseMutationJournalEntry>(
            Math.Min(maximum, 256));
        var after = new BaseMutationJournalPosition(
            Math.Max(0, bounds.Earliest.Value - 1));

        while (entries.Count < maximum)
        {
            BaseMutationJournalPage page =
                await journal.ReadMutationJournalAsync(
                    new BaseMutationJournalReadRequest
                    {
                        After = after,
                        Through = bounds.HighWatermark,
                        Limit = Math.Min(256, maximum - entries.Count),
                    },
                    cancellationToken).ConfigureAwait(false);
            entries.AddRange(page.Entries);
            if (!page.HasMore || page.Entries.Length == 0)
                break;
            after = page.Entries[^1].Position;
        }

        return entries;
    }

    public ValueTask DisposeAsync() => _provider.DisposeAsync();

}
