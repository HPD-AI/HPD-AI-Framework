using System.Collections.Immutable;
using System.Security.Cryptography;
using HPD.AI.Platform;
using HPD.Base;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace HPD.Gateway.ControlPlane;

public sealed class GatewayManagementOptions
{
    private byte[]? _desiredStateTokenKey;
    private byte[]? _epochReservationKey;
    public string ManagementAuthorityId { get; set; } = "local";
    public GatewayAuthorityDurability RequiredDurability { get; set; } = GatewayAuthorityDurability.ProcessLocal;
    public int MaximumTargets { get; set; } = 4_096;
    public int MaximumCommandUtf8Bytes { get; set; } = 4 * 1024 * 1024;
    public int MaximumDeliveryAttempts { get; set; } = 8;
    public TimeSpan DeliveryClaimLease { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan AdministrativeClaimLease { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan ReconciliationInterval { get; set; } = TimeSpan.FromSeconds(5);
    public byte[]? DesiredStateTokenKey
    {
        get => _desiredStateTokenKey is null ? null : [.. _desiredStateTokenKey];
        set => _desiredStateTokenKey = value is null ? null : [.. value];
    }
    public byte[]? EpochReservationKey
    {
        get => _epochReservationKey is null ? null : [.. _epochReservationKey];
        set => _epochReservationKey = value is null ? null : [.. value];
    }

    internal byte[] GetTokenKey() => [.. _desiredStateTokenKey!];
    internal byte[] GetEpochReservationKey() => [.. _epochReservationKey!];
    internal string AuthorityStoreId { get; set; } = "hpd.base.inmemory.default";
}

internal sealed class GatewayManagementRuntimeOptions
{
    private readonly byte[] _desiredStateTokenKey;
    private readonly byte[] _epochReservationKey;

    internal GatewayManagementRuntimeOptions(GatewayManagementOptions options)
    {
        ManagementAuthorityId = options.ManagementAuthorityId;
        RequiredDurability = options.RequiredDurability;
        MaximumTargets = options.MaximumTargets;
        MaximumCommandUtf8Bytes = options.MaximumCommandUtf8Bytes;
        MaximumDeliveryAttempts = options.MaximumDeliveryAttempts;
        DeliveryClaimLease = options.DeliveryClaimLease;
        AdministrativeClaimLease = options.AdministrativeClaimLease;
        ReconciliationInterval = options.ReconciliationInterval;
        AuthorityStoreId = options.AuthorityStoreId;
        _desiredStateTokenKey = options.GetTokenKey();
        _epochReservationKey = options.GetEpochReservationKey();
    }

    internal string ManagementAuthorityId { get; }
    internal GatewayAuthorityDurability RequiredDurability { get; }
    internal int MaximumTargets { get; }
    internal int MaximumCommandUtf8Bytes { get; }
    internal int MaximumDeliveryAttempts { get; }
    internal TimeSpan DeliveryClaimLease { get; }
    internal TimeSpan AdministrativeClaimLease { get; }
    internal TimeSpan ReconciliationInterval { get; }
    internal string AuthorityStoreId { get; }
    internal byte[] GetTokenKey() => [.. _desiredStateTokenKey];
    internal byte[] GetEpochReservationKey() => [.. _epochReservationKey];
}

public sealed record GatewayAuthorityCapabilitySnapshot
{
    public required string ProviderId { get; init; }
    public required string StoreId { get; init; }
    public required string StoreKind { get; init; }
    public required string StoreVersion { get; init; }
    public required GatewayAuthorityDurability Durability { get; init; }
    public required int MaximumBatchOperations { get; init; }
    public required long MaximumCanonicalPayloadBytes { get; init; }
    public required int MaximumReceiptBytes { get; init; }
    public required bool BackupSupported { get; init; }
    public required bool RestoreSupported { get; init; }
    public required bool PurgeSupported { get; init; }
    public required ImmutableArray<string> CollectionIds { get; init; }
}

public sealed class GatewayControlPlaneBuilder
{
    internal GatewayControlPlaneBuilder(
        IServiceCollection services,
        GatewayControlPlaneRegistration registration,
        GatewayManagementOptions? managementOptions = null)
    {
        Services = services;
        Registration = registration;
        ManagementOptions = managementOptions;
    }

    public IServiceCollection Services { get; }
    internal GatewayControlPlaneRegistration Registration { get; }
    internal GatewayManagementOptions? ManagementOptions { get; private set; }

    public GatewayControlPlaneBuilder UseProcessLocalAuthority(
        Action<GatewayManagementOptions>? configure = null)
    {
        ConfigureAuthority(configure, configureBase: null, "hpd.base.inmemory.default");
        return this;
    }

    internal void ConfigureAuthority(
        Action<GatewayManagementOptions>? configure,
        Action<HPDBaseBuilder>? configureBase,
        string authorityStoreId)
    {
        if (Registration.AuthorityConfigured)
            throw new InvalidOperationException("The Gateway control-plane authority is already configured.");
        GatewayControlPlaneBuilder management =
            GatewayControlPlaneServiceCollectionExtensions.AddManagementCore(
                Services, configure, configureBase, authorityStoreId);
        ManagementOptions = management.ManagementOptions;
        Registration.AuthorityConfigured = true;
    }

    public GatewayControlPlaneBuilder AddAdminApi(Action<GatewayAdminApiOptions>? configure = null)
    {
        if (Registration.AdminOptions is not null)
            throw new InvalidOperationException("The Gateway Admin API is already configured.");
        var options = new GatewayAdminApiOptions();
        configure?.Invoke(options);
        options.ApplyAuthorizationPolicy();
        GatewayAdminComposition.AddAdminCore(Services);
        Registration.AdminOptions = options.Snapshot();
        return this;
    }

    /// <summary>Adds Gateway's immutable Studio contribution to the shared HPD Studio graph.</summary>
    public GatewayControlPlaneBuilder AddStudio()
    {
        if (Registration.StudioConfigured)
            throw new InvalidOperationException("Gateway Studio is already configured.");
        GatewayStudioComposition.AddGatewayStudioCore(Services.AddHPDAIPlatform());
        Registration.StudioConfigured = true;
        return this;
    }
}

public static class GatewayControlPlaneServiceCollectionExtensions
{
    public static IServiceCollection AddHpdGatewayControlPlane(
        this IServiceCollection services,
        Action<GatewayControlPlaneBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        if (services.Any(static descriptor => descriptor.ServiceType == typeof(GatewayControlPlaneRegistration)))
            throw new InvalidOperationException("HPD Gateway Control Plane is already registered.");

        var stagedServices = new ServiceCollection();
        foreach (ServiceDescriptor descriptor in services)
            stagedServices.Add(descriptor);
        var registration = new GatewayControlPlaneRegistration();
        var builder = new GatewayControlPlaneBuilder(stagedServices, registration);
        configure(builder);
        ValidateRegistration(registration);
        stagedServices.AddSingleton(registration.Freeze());
        Commit(services, stagedServices);
        return services;
    }

    private static void ValidateRegistration(GatewayControlPlaneRegistration registration)
    {
        if (!registration.AuthorityConfigured)
            throw new InvalidOperationException("Select exactly one explicit Gateway control-plane authority.");
        if (!registration.StudioConfigured)
            return;
        if (registration.AdminOptions is not { } admin)
            throw new InvalidOperationException("Gateway Studio requires the Gateway Admin API.");
    }

    private static void Commit(IServiceCollection destination, IServiceCollection staged)
    {
        ServiceDescriptor[] original = [.. destination];
        try
        {
            destination.Clear();
            foreach (ServiceDescriptor descriptor in staged)
                destination.Add(descriptor);
        }
        catch
        {
            destination.Clear();
            foreach (ServiceDescriptor descriptor in original)
                destination.Add(descriptor);
            throw;
        }
    }

    internal static GatewayControlPlaneBuilder AddManagementCore(
        this IServiceCollection services,
        Action<GatewayManagementOptions>? configure = null,
        Action<HPDBaseBuilder>? configureBase = null,
        string authorityStoreId = "hpd.base.inmemory.default")
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new GatewayManagementOptions();
        configure?.Invoke(options);
        options.AuthorityStoreId = authorityStoreId;
        ValidateOptions(options);
        if (options.DesiredStateTokenKey is null)
            options.DesiredStateTokenKey = RandomNumberGenerator.GetBytes(32);
        if (options.EpochReservationKey is null)
            options.EpochReservationKey = RandomNumberGenerator.GetBytes(32);

        services.AddHPDBase(builder =>
        {
            builder.ConfigureSchema(schema =>
            {
                schema.ApplicationId = "hpd.gateway.management.v1";
                if (configureBase is null)
                    schema.PlanProtectionKey = RandomNumberGenerator.GetBytes(32);
            });
            GatewayAuthoritySchema.AddTo(builder);
            if (configureBase is null)
                builder.ConfigureInMemoryStore(store => store.AllowClientRequestedIds = true);
            else
                configureBase(builder);
        });
        services.Replace(ServiceDescriptor.Singleton<IPolicyEvaluator>(new GatewayManagementBasePolicy()));
        services.AddSingleton(new GatewayManagementRuntimeOptions(options));
        services.TryAddSingleton<GatewayAuthorityRuntime>();
        services.TryAddSingleton<IGatewayAuthorityRuntime>(static provider =>
            provider.GetRequiredService<GatewayAuthorityRuntime>());
        services.TryAddSingleton<IGatewayManagementCommandCoordinator, GatewayManagementCommandCoordinator>();
        services.TryAddSingleton<IGatewayDeliveryCoordinator, GatewayDeliveryCoordinator>();
        services.TryAddSingleton<GatewayControlPlaneStartupCoordinator>();
        services.AddSingleton<IHostedService>(static provider =>
            provider.GetRequiredService<GatewayControlPlaneStartupCoordinator>());
        services.TryAddSingleton<GatewayManagementReconciliationWorker>();
        services.AddSingleton<IHostedService>(static provider =>
            provider.GetRequiredService<GatewayManagementReconciliationWorker>());
        services.TryAddSingleton<IGatewayManagementReader, GatewayManagementReader>();
        services.TryAddSingleton<IGatewayManagementApplication, GatewayManagementApplication>();
        services.TryAddSingleton<GatewayBackupSinkRegistry>();
        services.TryAddSingleton<IGatewayManagementAdministration, GatewayManagementAdministration>();
        services.TryAddSingleton<IGatewayManagementStatusReader, GatewayManagementStatusReader>();
        return new GatewayControlPlaneBuilder(services, new GatewayControlPlaneRegistration
        {
            AuthorityConfigured = true,
        }, options);
    }

    private static void ValidateOptions(GatewayManagementOptions options)
    {
        if (!GatewayAuthorityRecordIds.IsCanonicalComponent(options.ManagementAuthorityId))
            throw new ArgumentException("ManagementAuthorityId is invalid.", nameof(options));
        if (!Enum.IsDefined(options.RequiredDurability))
            throw new ArgumentOutOfRangeException(nameof(options));
        if (options.MaximumTargets is < 1 or > 65_536)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumTargets));
        if (options.MaximumCommandUtf8Bytes is < 1_024 or > 16 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumCommandUtf8Bytes));
        if (options.MaximumDeliveryAttempts is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumDeliveryAttempts));
        if (options.DeliveryClaimLease < TimeSpan.FromSeconds(1) ||
            options.DeliveryClaimLease > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(options.DeliveryClaimLease));
        if (options.AdministrativeClaimLease < TimeSpan.FromSeconds(5) ||
            options.AdministrativeClaimLease > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(options.AdministrativeClaimLease));
        if (options.ReconciliationInterval < TimeSpan.FromMilliseconds(100) ||
            options.ReconciliationInterval > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(options.ReconciliationInterval));
        if (options.DesiredStateTokenKey is { Length: not 32 })
            throw new ArgumentException("DesiredStateTokenKey must contain exactly 32 bytes.", nameof(options));
        if (options.EpochReservationKey is { Length: not 32 })
            throw new ArgumentException("EpochReservationKey must contain exactly 32 bytes.", nameof(options));
        if (options.RequiredDurability == GatewayAuthorityDurability.RestartDurable && options.DesiredStateTokenKey is null)
            throw new ArgumentException("Restart-durable management requires a stable DesiredStateTokenKey.", nameof(options));
        if (options.RequiredDurability == GatewayAuthorityDurability.RestartDurable && options.EpochReservationKey is null)
            throw new ArgumentException("Restart-durable management requires a stable EpochReservationKey.", nameof(options));
    }
}

internal sealed class GatewayManagementBasePolicy : IPolicyEvaluator
{
    internal const string TrustedSource = "hpd.gateway.management.internal.v1";

    public ValueTask<PolicyDecision> EvaluateAsync(
        PolicyEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool allowed = request.Principal.AuthenticationState == PrincipalAuthenticationState.System
            && StringComparer.Ordinal.Equals(request.Principal.AuthSource, TrustedSource);
        return ValueTask.FromResult(new PolicyDecision
        {
            Effect = allowed ? PolicyEffect.Allow : PolicyEffect.Deny,
            Outcome = allowed ? PolicyOutcome.Allowed : PolicyOutcome.Denied,
        });
    }
}

internal interface IGatewayAuthorityRuntime
{
    bool IsReady { get; }
    GatewayAuthorityCapabilitySnapshot? Capabilities { get; }
    ValueTask<GatewayAuthorityCapabilitySnapshot> InitializeAsync(CancellationToken cancellationToken = default);
}

internal sealed class GatewayAuthorityRuntime(
    IHPDBaseApplication application,
    IRecordStoreRegistry stores,
    HPDBaseInstalledFeatures installed,
    GatewayManagementRuntimeOptions options) : IGatewayAuthorityRuntime
{
    private readonly SemaphoreSlim _initialization = new(1, 1);
    private GatewayAuthorityCapabilitySnapshot? _capabilities;

    public bool IsReady => Volatile.Read(ref _capabilities) is not null;
    public GatewayAuthorityCapabilitySnapshot? Capabilities => Volatile.Read(ref _capabilities);

    public async ValueTask<GatewayAuthorityCapabilitySnapshot> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        GatewayAuthorityCapabilitySnapshot? existing = Capabilities;
        if (existing is not null)
            return existing;
        await _initialization.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            existing = Capabilities;
            if (existing is not null)
                return existing;
            var initialized = await application.InitializeAsync(cancellationToken).ConfigureAwait(false);
            if (!initialized.IsSuccess())
                throw new InvalidOperationException($"HPD.Base did not initialize the Gateway authority store: {initialized.Error?.Code ?? "unknown"}.");

            IRecordStore store = stores.GetStoreForCollection(GatewayAuthoritySchema.AcceptedRevisions)
                ?? throw new InvalidOperationException("The Gateway authority store is unavailable.");
            GatewayAuthorityCapabilitySnapshot snapshot = ValidateCapabilities(
                installed.Provider,
                store.Capabilities,
                options.RequiredDurability,
                installed.CollectionIds);
            Volatile.Write(ref _capabilities, snapshot);
            return snapshot;
        }
        finally
        {
            _initialization.Release();
        }
    }

    internal static GatewayAuthorityCapabilitySnapshot ValidateCapabilities(
        string providerId,
        StoreCapabilityDescriptor capability,
        GatewayAuthorityDurability required,
        IEnumerable<string> collectionIds)
    {
        StoreBatchCapability? batch = capability.Batch;
        AtomicRequestCapability? receipt = capability.AtomicRequest;
        RevisionCapability? revision = capability.Revision;
        bool common = capability.Read.Get
            && capability.Read.List
            && capability.Mutation.Create
            && capability.Mutation.Replace
            && capability.Mutation.AdministrativePurge
            && batch is { Ordered: true, CrossCollectionAtomic: true, ReadYourWrites: true }
            && batch.Modes.Contains(BaseRecordBatchExecutionMode.Atomic)
            && batch.MaxOperations >= 8
            && receipt is
            {
                Supported: true,
                DuplicateResultReplay: true,
                FingerprintConflictDetection: true,
            }
            && revision is { Supported: true, Replace: true }
            && capability.Upsert is { Atomic: true, ExpectedRevision: true, ExistenceConditions: true };
        if (!common)
            throw new InvalidOperationException("The selected HPD.Base provider does not satisfy the Gateway authority capability contract.");

        bool durable = batch!.Durable
            && receipt!.Durability == BaseAtomicRequestDurability.Durable
            && receipt.IndeterminateResolution
            && capability.Administration is
            {
                Backup: true,
                Validate: true,
                Restore: true,
                AdministrativePurge: true,
                Durable: true,
                RestoreRequiresExclusiveMaintenance: true,
            };
        if (required == GatewayAuthorityDurability.RestartDurable && !durable)
            throw new InvalidOperationException("A restart-durable Gateway authority was required, but the selected HPD.Base provider is process-local or lacks durable administration.");

        ImmutableArray<string> ids = collectionIds.Order(StringComparer.Ordinal).ToImmutableArray();
        ImmutableArray<string> expected = GatewayAuthoritySchema.CollectionIds;
        if (!ids.SequenceEqual(expected, StringComparer.Ordinal))
            throw new InvalidOperationException("The installed HPD.Base collection graph does not exactly match the Gateway authority schema.");

        return new GatewayAuthorityCapabilitySnapshot
        {
            ProviderId = providerId,
            StoreId = capability.StoreId,
            StoreKind = capability.StoreKind,
            StoreVersion = capability.StoreVersion,
            Durability = durable ? GatewayAuthorityDurability.RestartDurable : GatewayAuthorityDurability.ProcessLocal,
            MaximumBatchOperations = batch.MaxOperations,
            MaximumCanonicalPayloadBytes = batch.MaxCanonicalPayloadBytes,
            MaximumReceiptBytes = receipt!.MaxReceiptBytes,
            BackupSupported = capability.Administration?.Backup == true,
            RestoreSupported = capability.Administration?.Restore == true,
            PurgeSupported = capability.Administration?.AdministrativePurge == true,
            CollectionIds = ids,
        };
    }
}
