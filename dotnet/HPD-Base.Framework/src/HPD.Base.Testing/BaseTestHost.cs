using HPD.Base;
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

    /// <summary>Gets the time.</summary>
    public BaseTestTimeProvider Time { get; }
    /// <summary>Gets the faults.</summary>
    public BaseTestFaults Faults { get; }
    /// <summary>Gets the probe.</summary>
    public BaseTestProbe Probe { get; }
    /// <summary>Gets the policy.</summary>
    public BaseTestPolicy Policy { get; }
    /// <summary>Gets the features.</summary>
    public HPDBaseInstalledFeatures Features =>
        _provider.GetRequiredService<HPDBaseInstalledFeatures>();

    /// <summary>Executes the create async operation.</summary>
    public static async ValueTask<BaseTestHost> CreateAsync(
        Action<HPDBaseBuilder> configure,
        DateTimeOffset? initialTime = null)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var services = new ServiceCollection();
        services.AddLogging();
        var time = new BaseTestTimeProvider(
            initialTime ?? new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        services.AddSingleton<TimeProvider>(time);
        var policy = new BaseTestPolicy();
        services.AddSingleton(policy);
        services.AddHPDBase(builder =>
        {
            configure(builder);
            builder.AddPolicyAuthority(
                new BasePolicyAuthorityDefinition
                {
                    Id = "hpd.base.testing.policy",
                    Version = 1,
                    OwningModuleId = "hpd.base.testing",
                    EvaluatorContractId = "hpd.base.testing.policy-evaluator",
                    EvaluatorContractVersion = 1,
                    CompositionOrder = 0,
                },
                new BaseTestPolicyEvaluator(policy));
            builder.ConfigureSchema(options =>
                options.PlanProtectionKey = Enumerable.Repeat((byte)0xA7, 32).ToArray());
        });
        services.Replace(
            ServiceDescriptor.Singleton<
                IFilePolicyOrchestrator,
                BaseTestFilePolicyOrchestrator>());
        var faults = new BaseTestFaults();
        services.AddSingleton(faults);
        services.AddSingleton<BaseTestProbe>();
        services.AddSingleton<IBaseCommittedMutationObserver>(
            provider => provider.GetRequiredService<BaseTestProbe>());
        services.AddSingleton<BaseTestStoreInitializer>();
        ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
        HPDBaseInstalledFeatures features = provider.GetRequiredService<HPDBaseInstalledFeatures>();
        if (string.Equals(features.Provider, "sqlite", StringComparison.Ordinal))
        {
            IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
            OperationResult<BaseSchemaPlan> plan = await schemas.PlanAsync(
                new BaseSchemaPlanRequest { StoreId = "sqlite" }).ConfigureAwait(false);
            if (!plan.IsSuccess() || plan.Value is null)
                throw new InvalidOperationException(
                    $"The HPD.BASE test schema could not be planned ({plan.Error?.Code ?? "unknown"}).");
            OperationResult<BaseSchemaApplyResult> applied = await schemas.ApplyAsync(
                new BaseSchemaApplyRequest
                {
                    ProtectedArtifact = plan.Value.ProtectedArtifact,
                    AllowDestructive = true,
                }).ConfigureAwait(false);
            if (!applied.IsSuccess())
                throw new InvalidOperationException(
                    $"The HPD.BASE test schema could not be applied ({applied.Error?.Code ?? "unknown"}).");
        }
        OperationResult<BaseApplicationReadiness> initialized = await provider
            .GetRequiredService<IHPDBaseApplication>()
            .InitializeAsync()
            .ConfigureAwait(false);
        if (!initialized.IsSuccess())
            throw new InvalidOperationException("The HPD.BASE test host failed to initialize.");
        provider.GetRequiredService<BaseTestStoreInitializer>().Initialize();
        return new BaseTestHost(
            provider,
            time,
            faults,
            provider.GetRequiredService<BaseTestProbe>(),
            policy);
    }

    /// <summary>Executes the session operation.</summary>
    public BaseSession Session(
        PrincipalContext principal,
        Action<BaseSessionOptions>? configure = null) =>
        _provider.GetRequiredService<IBaseSessionFactory>().For(principal, configure);

    /// <summary>Executes the get required service operation.</summary>
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

    /// <summary>Executes the dispose async operation.</summary>
    public ValueTask DisposeAsync() => _provider.DisposeAsync();

}
