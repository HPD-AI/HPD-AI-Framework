using HPD.AI.Platform;
using HPD.AI.Platform.Studio;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace HPD.Base.Studio.Tests;

/// <summary>Verifies BASE's fixed immutable Studio module contribution.</summary>
public sealed class BaseStudioModuleRegistryTests
{
    /// <summary>Proves Search publishes lossless closed L49 query authority rather than an expression parser.</summary>
    [Fact]
    public void Search_runtime_uses_closed_recursive_and_finite_query_nodes()
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddHPDBase(builder => ConfigureBase(builder, includeAllGrants: true));
        services.AddHPDAIPlatform().AddBaseStudio(static _ => new MissingPrincipalResolver());
        using ServiceProvider provider = services.BuildServiceProvider();
        BaseStudioModuleRuntimeContribution runtime = Assert.Single(provider.GetRequiredService<BaseStudioRuntimeCatalog>().Contributions);

        BaseStudioNamedTypeContract query = runtime.Types.Single(static value => value.TypeId == "base.studio.search-query");
        using JsonDocument queryNode = JsonDocument.Parse(query.GetCanonicalDescriptor());
        string[] tags = queryNode.RootElement.GetProperty("variants").EnumerateArray().Select(static value => value.GetProperty("tag").GetString()!).ToArray();
        Assert.Equal(["and", "field", "not", "or", "phrase", "prefix", "term", "vector"], tags);
        Assert.DoesNotContain(runtime.Types, static value => value.TypeId == "base.studio.search-query-kind");

        BaseStudioNamedTypeContract component = runtime.Types.Single(static value => value.TypeId == "base.studio.search-vector-component");
        using JsonDocument componentNode = JsonDocument.Parse(component.GetCanonicalDescriptor());
        Assert.Equal("floating", componentNode.RootElement.GetProperty("kind").GetString());
        Assert.Equal("binary32", componentNode.RootElement.GetProperty("precision").GetString());
        Assert.True(componentNode.RootElement.GetProperty("finiteOnly").GetBoolean());

        BaseStudioNamedTypeContract score = runtime.Types.Single(static value => value.TypeId == "base.studio.search-score");
        using JsonDocument scoreNode = JsonDocument.Parse(score.GetCanonicalDescriptor());
        Assert.Equal(["text", "vector"], scoreNode.RootElement.GetProperty("variants").EnumerateArray().Select(static value => value.GetProperty("tag").GetString()!).ToArray());
        Assert.Contains(runtime.Methods, static value => value.RegisteredMethodId == "base.studio.view.base.search.query.results.list");
        Assert.Contains(runtime.Types, static value => value.TypeId == "base.studio.search.query-summary");
        Assert.Contains(runtime.Types, static value => value.TypeId == "base.studio.search.query-result.item");
        Assert.Contains(runtime.Types, static value => value.TypeId == "base.studio.search.explanation");
        Assert.Contains(runtime.Types, static value => value.TypeId == "base.studio.search.evidence");
        Assert.Equal(4, runtime.Types.Count(static value => value.TypeId.StartsWith("base.search.query.", StringComparison.Ordinal) && value.TypeId.EndsWith(".current", StringComparison.Ordinal)));
    }

    /// <summary>Proves the built-in provider enforces exact and max-plus-one dynamic-authority evidence bounds.</summary>
    [Fact]
    public async Task InMemory_dynamic_store_authority_is_bounded_and_canonical()
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddHPDBase(builder => ConfigureBase(builder, includeAllGrants: true));
        using ServiceProvider provider = services.BuildServiceProvider();
        IBaseStudioDynamicStoreAuthoritySource source = Assert.IsAssignableFrom<IBaseStudioDynamicStoreAuthoritySource>(
            provider.GetRequiredService<IAtomicRecordStore>());
        var broad = new BaseStudioDynamicStoreAuthorityRequest { ApplicationId = "sample.application", MaximumEvidenceBytes = 4_096,
            MaximumTransientBytes = 4_096, Deadline = TimeSpan.FromSeconds(1) };
        OperationResult<BaseStudioDynamicStoreAuthority> captured = await source.CaptureStudioDynamicStoreAuthorityAsync(broad);
        Assert.True(captured.IsSuccess()); Assert.True(BaseStudioDynamicStoreAuthorityContract.IsValidResult(broad, captured.Value));
        byte[] tamperedChecksum = captured.Value!.EvidenceChecksum.ToArray(); tamperedChecksum[0] ^= 0xff;
        Assert.False(BaseStudioDynamicStoreAuthorityContract.IsValidResult(broad, captured.Value with { EvidenceChecksum = [.. tamperedChecksum] }));
        Assert.False(BaseStudioDynamicStoreAuthorityContract.IsValidResult(broad, captured.Value with
        { Accounting = captured.Value.Accounting with { AuthorityReads = 2 } }));
        Assert.False(BaseStudioDynamicStoreAuthorityContract.IsValidResult(broad, captured.Value with { ApplicationId = "other.application" }));
        int exact = captured.Value!.Accounting.EvidenceBytes;
        var exactRequest = broad with { MaximumEvidenceBytes = exact, MaximumTransientBytes = exact };
        Assert.True((await source.CaptureStudioDynamicStoreAuthorityAsync(exactRequest)).IsSuccess());
        Assert.False((await source.CaptureStudioDynamicStoreAuthorityAsync(exactRequest with { MaximumEvidenceBytes = exact - 1 })).IsSuccess());
    }

    /// <summary>Proves an earlier DI registration cannot substitute the installed provider's store authority.</summary>
    [Fact]
    public void Earlier_store_contract_registration_fails_before_provider_installation()
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton<IAtomicRecordStore>(_ => throw new InvalidOperationException("hostile"));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            services.AddHPDBase(builder => ConfigureBase(builder, includeAllGrants: true)));

        Assert.Equal("base.store.authorityAmbiguous", error.Message);
    }

    /// <summary>Proves empty provider-neutral control inspection is bounded and canonically validated.</summary>
    [Fact]
    public async Task InMemory_control_inspection_returns_a_valid_empty_page()
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddHPDBase(builder => ConfigureBase(builder, includeAllGrants: true));
        using ServiceProvider provider = services.BuildServiceProvider();
        var store = Assert.IsAssignableFrom<IBaseStudioControlInspectionStore>(provider.GetRequiredService<IAtomicRecordStore>());
        var request = new BaseStudioControlInspectionRequest { ApplicationId = "sample.application",
            Kind = BaseStudioControlFactKind.Activation, Take = 10, ProtectedScopeChecksum = [.. new byte[32]],
            Limits = new BaseStudioControlInspectionLimits { MaximumItems = 10, MaximumRowsRead = 11,
                MaximumEvidenceBytes = 16_384, MaximumTransientBytes = 16_384, Deadline = TimeSpan.FromSeconds(1) } };

        OperationResult<BaseStudioControlInspectionPage> result = await new DefaultBaseStudioControlInspectionRuntime()
            .ReadAsync(store, request);

        Assert.True(result.IsSuccess()); Assert.NotNull(result.Value); Assert.Empty(result.Value.Items);
        Assert.True(BaseStudioControlInspectionContract.IsValidResult(request, result.Value));
    }

    /// <summary>Proves lifecycle and retirement inspection never bypass the bounded provider-neutral seam.</summary>
    [Theory]
    [InlineData(BaseStudioControlFactKind.LifecycleConsumer)]
    [InlineData(BaseStudioControlFactKind.LifecycleCheckpoint)]
    [InlineData(BaseStudioControlFactKind.RetirementBarrier)]
    public async Task InMemory_lifecycle_control_inspection_is_canonical(BaseStudioControlFactKind kind)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddHPDBase(builder => ConfigureBase(builder, includeAllGrants: true));
        using ServiceProvider provider = services.BuildServiceProvider();
        var store = Assert.IsAssignableFrom<IBaseStudioControlInspectionStore>(provider.GetRequiredService<IAtomicRecordStore>());
        var request = new BaseStudioControlInspectionRequest { ApplicationId = "sample.application", Kind = kind, Take = 10,
            ProtectedScopeChecksum = [.. new byte[32]], Limits = new() { MaximumItems = 10, MaximumRowsRead = 11,
                MaximumEvidenceBytes = 16_384, MaximumTransientBytes = 16_384, Deadline = TimeSpan.FromSeconds(1) } };
        OperationResult<BaseStudioControlInspectionPage> result = await store.ReadStudioControlFactsAsync(request);
        Assert.True(result.IsSuccess()); Assert.NotNull(result.Value); Assert.True(BaseStudioControlInspectionContract.IsValidResult(request, result.Value));
    }

    /// <summary>Proves a replacement installation receipt creates a distinct graph-owned Studio authority.</summary>
    [Fact]
    public void Replacement_store_installation_changes_studio_owner_authority()
    {
        static ServiceProvider Build()
        { var services = new ServiceCollection(); services.AddLogging(); services.AddHPDBase(builder => ConfigureBase(builder, includeAllGrants: true)); return services.BuildServiceProvider(); }
        using ServiceProvider first = Build(); using ServiceProvider second = Build();
        byte[] one = first.GetRequiredService<HPDBaseStudioAuthoritySnapshot>().GetChecksum();
        byte[] two = second.GetRequiredService<HPDBaseStudioAuthoritySnapshot>().GetChecksum();
        Assert.False(System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(one, two));
    }

    /// <summary>Proves a nested provider-limit substitution changes the graph-owned capability checksum.</summary>
    [Fact]
    public void Provider_capability_checksum_binds_nested_limits()
    {
        HPDBaseStoreProvider original = InMemoryProviderInstaller.Create(null);
        var descriptor = new BaseStoreProviderDescriptor
        {
            Kind = original.Kind, ProtocolVersion = original.ProtocolVersion, Capabilities = original.Capabilities,
            RegistrationIds = original.RegistrationIds.ToArray(), StorageProtectionCapabilities = original.StorageProtectionCapabilities.ToArray(),
            MaximumBinaryFieldBytes = original.MaximumBinaryFieldBytes - 1, SubjectReferences = original.SubjectReferences,
            SubjectLifecycle = original.SubjectLifecycle, SubjectRetirement = original.SubjectRetirement,
            ModuleMutations = original.ModuleMutations,
            Activations = original.Activations, TextSearch = original.TextSearch,
            SemanticActivations = original.SemanticActivations,
            SemanticActivationCertification = original.SemanticActivationCertification,
        };
        HPDBaseStoreProvider substituted = HPDBaseStoreProviderFactory.Create(descriptor, original.Installer);
        Assert.False(System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            original.StudioCapabilityChecksum, substituted.StudioCapabilityChecksum));
    }

    /// <summary>Proves non-cooperative dynamic authority work is bounded, retained, and released only on termination.</summary>
    [Fact]
    public async Task Noncooperative_store_authority_is_retained_after_deadline()
    {
        var source = new NoncooperativeAuthoritySource(); var late = new BaseStudioLateWorkRegistry();
        var request = new BaseStudioDynamicStoreAuthorityRequest { ApplicationId = "sample.application", MaximumEvidenceBytes = 64,
            MaximumTransientBytes = 64, Deadline = TimeSpan.FromMilliseconds(10) };
        Assert.Null(await BaseStudioBootstrapRuntime.CaptureStoreAsync(source, late, request, CancellationToken.None));
        Assert.Equal(1, late.OutstandingCount); source.Complete();
        await SpinWaitAsync(() => late.OutstandingCount == 0, TimeSpan.FromSeconds(1));
        Assert.Equal(0, late.OutstandingCount);
    }

    private static async Task SpinWaitAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition() && DateTimeOffset.UtcNow < deadline) await Task.Delay(10);
    }

    private sealed class NoncooperativeAuthoritySource : IBaseStudioDynamicStoreAuthoritySource
    {
        private readonly TaskCompletionSource<OperationResult<BaseStudioDynamicStoreAuthority>> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<OperationResult<BaseStudioDynamicStoreAuthority>> CaptureStudioDynamicStoreAuthorityAsync(
            BaseStudioDynamicStoreAuthorityRequest request, CancellationToken cancellationToken = default) => new(_completion.Task);
        internal void Complete() => _completion.TrySetResult(OperationResults.StoreError<BaseStudioDynamicStoreAuthority>(new BaseError
        { Code = "test.complete", Message = "Completed.", Category = ErrorCategory.Store }));
    }


    /// <summary>Proves the complete fixed page, area, resource, and command inventory is installed.</summary>
    [Fact]
    public void Fixed_registry_is_complete_and_graph_owned()
    {
        BaseStudioModuleRegistration module = BaseStudioModuleRegistry.Create(Snapshot());

        Assert.Equal("base", module.Identity.ModuleId);
        Assert.Equal(BaseStudioModuleClass.Base, module.ModuleClass);
        Assert.Equal(44, module.Pages.Length);
        Assert.Equal(9, module.Pages.Count(static page => page.Presentation.NavigationRole == BaseStudioNavigationRole.AreaLanding));
        Assert.True(module.Resources.Length >= 30);
        Assert.Equal(3, module.Commands.Length);
        Assert.Equal(module.Pages.Sum(static page => page.Presentation.Sections.Sum(static section => section.ViewIds.Length)), module.Views.Length);
        Assert.True(module.Links.Length >= 25);
        Assert.Contains(module.Pages, static page => page.PageId == "base.automation");
        Assert.Contains(module.Pages, static page => page.PageId == "base.semanticActivations");
        Assert.Contains(module.Pages, static page => page.PageId == "base.subjects");
        Assert.Contains(module.Pages, static page => page.PageId == "base.search");
        Assert.Contains(module.Resources, static resource => resource.Kind == BaseStudioResourceKind.RetirementBarrier);
        Assert.Single(module.Clients);
        Assert.Single(module.Grants);
        Assert.Equal("base.studio.bootstrap.read", module.Grants[0].OperationId);
        Assert.All(module.Pages, static page => Assert.Equal(2, page.Grants.Length));
        Assert.All(module.Resources, static resource => Assert.Equal(2, resource.Grants.Length));
        Assert.All(module.Commands, static command =>
        {
            Assert.InRange(command.Grants.Length, 2, 3);
            Assert.False(command.Grants.Single(static grant => grant.OperationId == "base.studio.action.discover").RequiresUnderlyingOperationGrant);
            Assert.True(command.Grants.Single(static grant => grant.OperationId == "base.studio.action.execute").RequiresUnderlyingOperationGrant);
        });
        Assert.Equal("base.control-plane", module.Clients[0].ClientId);
        Assert.Empty(module.Pages.Single(static page => page.PageId == "base.overview").Route.Segments);
        Assert.Contains(module.Pages, static page => page.PageId == "base.executor.detail");
        Assert.DoesNotContain(module.Commands, static command => command.CommandId == "backup.restore");
        Assert.Contains(module.Commands, static command => command.CommandId == "schema.apply" &&
            command.ActionClass == BaseStudioActionClass.DisasterOrRecoveryDomain);
        Assert.DoesNotContain(module.Views, static view => view.ProducerId == "base.studio.runtime");
        Assert.All(module.Views, static view => Assert.Single(view.Sorts));
        Assert.All(module.Views.Where(static view => view.ViewId.EndsWith(".list", StringComparison.Ordinal)),
            static view => Assert.Single(view.Filters));
    }

    /// <summary>Proves retirement commands use closed CAS-bound semantic graphs and never expose worker acknowledgements.</summary>
    [Fact]
    public void Retirement_command_nodes_are_exact_and_exclude_worker_operations()
    {
        string timeout = BaseStudioModuleRegistry.RetirementInputDescriptorForRuntime("retirement.timeout");
        string @override = BaseStudioModuleRegistry.RetirementInputDescriptorForRuntime("retirement.override");
        string purge = BaseStudioModuleRegistry.RetirementInputDescriptorForRuntime("retirement.purge");
        string removal = BaseStudioModuleRegistry.RetirementInputDescriptorForRuntime("retirement.consumer.remove");

        Assert.Contains("expectedBarrierGeneration", timeout, StringComparison.Ordinal);
        Assert.Contains("expectedBarrierChecksum", timeout, StringComparison.Ordinal);
        Assert.Contains("expectedTombstoneSequence", @override, StringComparison.Ordinal);
        Assert.Contains("changeReference", @override, StringComparison.Ordinal);
        Assert.Contains("expectedPrivateRevision", purge, StringComparison.Ordinal);
        Assert.Contains("expectedAcceptedSetChecksum", removal, StringComparison.Ordinal);
        Assert.Contains("expectedConsumerChecksum", removal, StringComparison.Ordinal);
        Assert.Contains("expectedGraphGeneration", removal, StringComparison.Ordinal);
        Assert.All(new[] { timeout, @override, purge, removal }, descriptor =>
        {
            Assert.Contains("\"additionalProperties\":false", descriptor, StringComparison.Ordinal);
            Assert.Contains("previewChecksum", descriptor, StringComparison.Ordinal);
            Assert.Contains("resourceToken", descriptor, StringComparison.Ordinal);
        });
        Assert.Throws<ArgumentOutOfRangeException>(() => BaseStudioModuleRegistry.RetirementInputDescriptorForRuntime("retirement.acknowledge"));
        Assert.Throws<ArgumentOutOfRangeException>(() => BaseStudioModuleRegistry.RetirementInputDescriptorForRuntime("lifecycle.checkpoint.advance"));
    }

    /// <summary>Proves an exact installed retirement capability discloses four reviewed commands and eight executable methods.</summary>
    [Fact]
    public void Retirement_capability_discloses_only_exact_reviewed_command_producers()
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddHPDBase(builder => ConfigureBase(builder, includeAllGrants: true));
        services.AddHPDAIPlatform().AddBaseStudio(static _ => new MissingPrincipalResolver(),
            static options => options.Mode = BaseStudioMode.Operate);
        using ServiceProvider provider = services.BuildServiceProvider();
        HPDBaseStudioAuthoritySnapshot installed = provider.GetRequiredService<HPDBaseStudioAuthoritySnapshot>();
        HPDBaseInstalledFeatures features = provider.GetRequiredService<HPDBaseInstalledFeatures>();
        string[] retirement = ["retirement.consumer.remove", "retirement.override", "retirement.purge", "retirement.timeout"];
        var authority = new HPDBaseStudioAuthoritySnapshot(installed.ApplicationId, installed.PolicyOwnerGeneration,
            installed.GetPolicyOwnerChecksum(), provider.GetRequiredService<BaseLogicalSchema>(),
            installed.OperationIds.Concat(retirement), installed.Policies, installed.Grants, installed.Definitions,
            features.StoreProvider, features.StoreReceipt);
        BaseStudioModuleRegistration module = BaseStudioModuleRegistry.Create(authority);
        IBaseStudioModuleRuntimeContributionFactory factory = provider.GetServices<IBaseStudioModuleRuntimeContributionFactory>()
            .Single(static value => value.ModuleId == "base");
        BaseStudioModuleRuntimeContribution runtime = factory.Create(module);

        Assert.Equal(retirement, module.Commands.Where(static value => value.CommandId.StartsWith("retirement.", StringComparison.Ordinal))
            .Select(static value => value.CommandId).ToArray());
        Assert.Equal(8, runtime.Methods.Count(static value => value.OwningPageOrCommandId.StartsWith("retirement.", StringComparison.Ordinal) &&
            value.Kind is BaseStudioMethodKind.Preview or BaseStudioMethodKind.Execute));
        Assert.Equal(8, runtime.Producers.Count(static value => value.RegisteredMethodId.Contains("retirement.", StringComparison.Ordinal) &&
            value is BaseStudioCommandPreviewProducerBinding or BaseStudioCommandExecuteProducerBinding));
        Assert.All(module.Commands.Where(static value => value.CommandId.StartsWith("retirement.", StringComparison.Ordinal)), command =>
        {
            Assert.Equal(command.ActionClass >= BaseStudioActionClass.Destructive ? BaseStudioFreshAuthenticationClass.MultiFactor : null,
                command.FreshAuthentication);
            Assert.Single(command.Acknowledgements);
            Assert.Equal("confirm." + command.CommandId, command.Acknowledgements[0].PurposeId);
        });
        Assert.DoesNotContain(module.Commands, static value => value.CommandId.Contains("acknowledge", StringComparison.Ordinal));
        Assert.DoesNotContain(module.Commands, static value => value.CommandId.Contains("checkpoint", StringComparison.Ordinal));
    }

    /// <summary>Proves SI&amp;D sections bind distinct typed facts and Policy Explain requires an operator query.</summary>
    [Fact]
    public void Security_infrastructure_and_diagnostics_are_not_summary_flattened()
    {
        BaseStudioModuleRegistration module = BaseStudioModuleRegistry.Create(Snapshot());
        BaseStudioViewRegistration[] governed = module.Views.Where(static view =>
            view.ViewId.StartsWith("base.security.", StringComparison.Ordinal) ||
            view.ViewId.StartsWith("base.policy.", StringComparison.Ordinal) ||
            view.ViewId.StartsWith("base.grant.", StringComparison.Ordinal) ||
            view.ViewId.StartsWith("base.infrastructure.", StringComparison.Ordinal) ||
            view.ViewId.StartsWith("base.schema.", StringComparison.Ordinal) ||
            view.ViewId.StartsWith("base.migration.", StringComparison.Ordinal) ||
            view.ViewId.StartsWith("base.backup.", StringComparison.Ordinal) ||
            view.ViewId.StartsWith("base.restore.", StringComparison.Ordinal) ||
            view.ViewId.StartsWith("base.maintenance.", StringComparison.Ordinal) ||
            view.ViewId.StartsWith("base.diagnostics.", StringComparison.Ordinal) ||
            view.ViewId.StartsWith("base.health.", StringComparison.Ordinal) ||
            view.ViewId.StartsWith("base.diagnostic.", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(governed);
        Assert.Equal(governed.Length, governed.Select(static view => view.ItemNodeId).Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(governed, static view => view.ItemNodeId == "base.studio.safe-authority.item" ||
            view.ItemNodeId.EndsWith("resource-summary", StringComparison.Ordinal));
        Assert.All(module.Views.Where(static view => view.ViewId.StartsWith("base.policy.explain.", StringComparison.Ordinal)),
            static view => Assert.Equal("base.policy.explain", view.ViewId[.."base.policy.explain".Length]));
    }

    /// <summary>Proves the .NET manifest binds the checked-in executable Svelte module and static ABI.</summary>
    [Fact]
    public void Prebuilt_frontend_asset_and_descriptor_correspond_exactly()
    {
        BaseStudioModuleRegistration module = BaseStudioModuleRegistry.Create(Snapshot());
        BaseStudioAssetEntry asset = Assert.Single(module.Asset.Assets);
        using Stream stream = typeof(BaseStudioModuleRegistry).Assembly.GetManifestResourceStream("HPD.Base.Studio.Assets.base.js")!;
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);

        Assert.Equal("base/53f8e0b4f2056ab2c410d456f4d3d4b3479c94ec2fc45df2f50d0b35f66be94f.js", asset.Path);
        Assert.Equal(bytes.Length, asset.Length);
        Assert.Equal(asset.Digest.ToArray(), SHA256.HashData(bytes.GetBuffer().AsSpan(0, checked((int)bytes.Length))));
        Assert.Equal("d6fc224d5225b56bef0f1aaf4a9e1e2b68cf0ede72093d0bfb853797935af544",
            Convert.ToHexString(module.Frontend.FrontendAbiChecksum.ToArray()).ToLowerInvariant());
        Assert.Equal(module.Pages.Select(static page => page.PageId),
            module.Frontend.Components.Select(static component => component.PageId));
    }

    /// <summary>Proves the navigable embedded-resource links cannot target an unregistered kind.</summary>
    [Fact]
    public void Typed_link_graph_has_complete_registered_endpoints()
    {
        BaseStudioModuleRegistration module = BaseStudioModuleRegistry.Create(Snapshot());
        var kinds = module.Resources.Select(static resource => resource.Kind).ToHashSet();

        Assert.All(module.Links, link =>
        {
            Assert.Contains(link.SourceKind, kinds);
            Assert.Contains(link.TargetKind, kinds);
            BaseStudioResourceRegistration target = Assert.Single(module.Resources.Where(
                resource => resource.Kind == link.TargetKind));
            Assert.Equal(target.ResolverId, link.ResolverId);
        });
        Assert.Contains(module.Links, static link => link.SourceKind == BaseStudioResourceKind.SubjectContract && link.TargetKind == BaseStudioResourceKind.LifecycleConsumer);
        Assert.Contains(module.Links, static link => link.SourceKind == BaseStudioResourceKind.Subject && link.TargetKind == BaseStudioResourceKind.RetirementBarrier);
        Assert.Contains(module.Links, static link => link.SourceKind == BaseStudioResourceKind.Maintenance &&
            link.TargetKind == BaseStudioResourceKind.QuarantineItem);
    }

    /// <summary>Proves repeated construction yields the same immutable registration checksum.</summary>
    [Fact]
    public void Registry_checksum_is_deterministic()
    {
        HPDBaseStudioAuthoritySnapshot authority = Snapshot();
        BaseStudioModuleRegistration first = BaseStudioModuleRegistry.Create(authority);
        BaseStudioModuleRegistration second = BaseStudioModuleRegistry.Create(authority);

        Assert.True(BaseStudioSha256.FixedTimeEquals(first.Identity.Checksum, second.Identity.Checksum));
        Assert.NotSame(first.Pages[0], second.Pages[0]);
    }

    /// <summary>Proves Studio receives immutable policy registration semantics without evaluator objects.</summary>
    [Fact]
    public void Policy_authority_snapshot_is_exact_and_defensively_owned()
    {
        var services = new ServiceCollection();
        services.AddHPDBase(builder =>
        {
            ConfigureBase(builder, includeAllGrants: true);
            builder.AddPolicyAuthority(new BasePolicyAuthorityDefinition
            {
                Id = "sample.policy", Version = 3, OwningModuleId = "sample", EvaluatorContractId = "sample.policy-evaluator",
                EvaluatorContractVersion = 2, CompositionOrder = 7,
            }, new AllowPolicyEvaluator());
        });
        using ServiceProvider provider = services.BuildServiceProvider();
        HPDBaseStudioPolicyAuthority policy = Assert.Single(provider.GetRequiredService<HPDBaseStudioAuthoritySnapshot>().Policies);
        Assert.Equal(("sample.policy", 3, "sample", "sample.policy-evaluator", 2, 7),
            (policy.Id, policy.Version, policy.OwningModuleId, policy.EvaluatorContractId, policy.EvaluatorContractVersion, policy.CompositionOrder));
        Assert.Equal(32, policy.RegistrationChecksum.Length);
    }

    /// <summary>Proves the extension installs the graph contribution without legacy module metadata.</summary>
    [Fact]
    public void AddBaseStudio_installs_only_the_immutable_contribution()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHPDBase(builder => ConfigureBase(builder, includeAllGrants: true));
        services.AddHPDAIPlatform().AddBaseStudio(static _ => new MissingPrincipalResolver());
        using ServiceProvider provider = services.BuildServiceProvider();

        BaseStudioApplicationGraph graph = provider.GetRequiredService<BaseStudioApplicationGraphProvider>().GetRequiredGraph();
        BaseStudioRuntimeCatalog runtime = provider.GetRequiredService<BaseStudioRuntimeCatalog>();

        Assert.Equal("sample.application", graph.ApplicationId);
        Assert.Single(graph.Modules);
        BaseStudioModuleRuntimeContribution contribution = Assert.Single(runtime.Contributions);
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.overview.summary.detail");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.resolve.application");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.resolve.collection");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.resolve.record");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.resolve.module");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.data.collections.list");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.module.detail.operations.list");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.collection.records.records.list");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.operations.definitions.registeredReads.list");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.operations.definitions.selectionOperations.list");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.operations.definitions.moduleMutations.list");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.operations.executions.list");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.operations.receipts.list");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.resolve.operationexecution");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.collection.detail.history.list");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.record.detail.history.list");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.security.policies.list");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.policy.detail.constraints.detail");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.grant.detail.conditions.detail");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.resolve.policy");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.resolve.grant");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.policy.explain.decision.detail");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.infrastructure.stores.list");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.store.detail.retainedWork.detail");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.provider.detail.certification.detail");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.schema.detail.drift.detail");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.migration.detail.plan.detail");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.backup.detail.authentication.detail");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.restore.detail.newAuthority.detail");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.maintenance.detail.progress.detail");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.diagnostics.incidents.list");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.health.detail.dependencies.list");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.diagnostic.detail.evidence.detail");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.resolve.health");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.resolve.diagnostic");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.search.query.query.detail");
        Assert.Contains(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.search.query.results.list");
        Assert.DoesNotContain(contribution.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.record.detail.receipts.list");
    }

    /// <summary>Proves Automations pages bind section-owned nodes and executable resolvers without a generic fact ABI.</summary>
    [Fact]
    public void Automations_presentation_has_complete_runtime_correspondence()
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddHPDBase(builder => ConfigureBase(builder, includeAllGrants: true));
        services.AddHPDAIPlatform().AddBaseStudio(static _ => new MissingPrincipalResolver());
        using ServiceProvider provider = services.BuildServiceProvider();
        BaseStudioApplicationGraph graph = provider.GetRequiredService<BaseStudioApplicationGraphProvider>().GetRequiredGraph();
        BaseStudioModuleRegistration module = Assert.Single(graph.Modules);
        BaseStudioModuleRuntimeContribution runtime = Assert.Single(provider.GetRequiredService<BaseStudioRuntimeCatalog>().Contributions);
        Assert.Equal(7, module.Pages.Count(static page => page.Area == BaseStudioArea.Automations));
        Assert.DoesNotContain(module.Views, static view => view.ItemNodeId == "base.studio.automation-fact.item");
        Assert.Contains(runtime.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.automation.activations.list");
        Assert.Contains(runtime.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.activation.detail.summary.detail");
        Assert.Contains(runtime.Methods, static method => method.RegisteredMethodId == "base.studio.resolve.activation");
        Assert.Contains(runtime.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.semanticActivations.definitions.list");
        BaseStudioViewRegistration semanticView = module.Views.Single(static view => view.ViewId == "base.semanticActivations.definitions.list");
        Assert.Equal("base.studio.semantic-definition-inspection.item", semanticView.ItemNodeId);
        Assert.Contains(runtime.Types, static type => type.TypeId == "base.studio.semantic-definition-inspection.current");
        Assert.All(module.Views.Where(static view => view.ViewId.StartsWith("base.automation", StringComparison.Ordinal) ||
            view.ViewId.StartsWith("base.activation.detail", StringComparison.Ordinal)), static view => Assert.EndsWith(".item", view.ItemNodeId));
    }

    /// <summary>Proves subject, lifecycle, and retirement sections bind their exact authority-owned nodes and resolvers.</summary>
    [Fact]
    public void Subjects_presentation_has_complete_runtime_correspondence()
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddHPDBase(builder => ConfigureBase(builder, includeAllGrants: true));
        services.AddHPDAIPlatform().AddBaseStudio(static _ => new MissingPrincipalResolver());
        using ServiceProvider provider = services.BuildServiceProvider();
        BaseStudioApplicationGraph graph = provider.GetRequiredService<BaseStudioApplicationGraphProvider>().GetRequiredGraph();
        BaseStudioModuleRegistration module = Assert.Single(graph.Modules);
        BaseStudioModuleRuntimeContribution runtime = Assert.Single(provider.GetRequiredService<BaseStudioRuntimeCatalog>().Contributions);
        Assert.Equal(5, module.Pages.Count(static page => page.Area == BaseStudioArea.Subjects));
        Assert.DoesNotContain(module.Views, static view => view.ItemNodeId == "base.studio.subject-fact.item");
        Assert.Contains(runtime.Methods, static method => method.RegisteredMethodId == "base.studio.resolve.subjectcontract");
        Assert.Contains(runtime.Methods, static method => method.RegisteredMethodId == "base.studio.resolve.subject");
        Assert.Contains(runtime.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.subjects.contracts.list");
        Assert.Contains(runtime.Methods, static method => method.RegisteredMethodId == "base.studio.resolve.lifecycleconsumer");
        Assert.Contains(runtime.Methods, static method => method.RegisteredMethodId == "base.studio.resolve.retirementbarrier");
        Assert.Contains(runtime.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.lifecycleConsumer.detail.checkpoint.detail");
        Assert.Contains(runtime.Methods, static method => method.RegisteredMethodId == "base.studio.view.base.retirementBarrier.detail.summary.detail");
    }

    /// <summary>Proves an omitted or wrong-version fixed grant fails readiness before graph publication.</summary>
    [Fact]
    public void Missing_or_substituted_fixed_grant_fails_closed()
    {
        Assert.Throws<InvalidOperationException>(() => BaseStudioModuleRegistry.Create(Snapshot("base.studio.resource.inspect")));

        var services = new ServiceCollection();
        services.AddHPDBase(builder => ConfigureBase(builder, includeAllGrants: true,
            substitutedId: "base.studio.resource.inspect"));
        using ServiceProvider provider = services.BuildServiceProvider();
        HPDBaseStudioAuthoritySnapshot substituted = provider.GetRequiredService<HPDBaseStudioAuthoritySnapshot>();
        Assert.Throws<InvalidOperationException>(() => BaseStudioModuleRegistry.Create(substituted));

        var semanticServices = new ServiceCollection();
        semanticServices.AddHPDBase(builder => ConfigureBase(builder, includeAllGrants: true,
            invalidSemanticsId: "base.studio.resource.inspect"));
        using ServiceProvider semanticProvider = semanticServices.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => BaseStudioModuleRegistry.Create(
            semanticProvider.GetRequiredService<HPDBaseStudioAuthoritySnapshot>()));
    }

    /// <summary>Proves grant checksum bytes are defensively copied into every registration requirement.</summary>
    [Fact]
    public void Installed_grant_checksum_is_bound_and_defensively_owned()
    {
        HPDBaseStudioAuthoritySnapshot authority = Snapshot();
        HPDBaseStudioGrantAuthority installed = authority.Grants.Single(static grant => grant.Id == "base.studio.resource.inspect");
        byte[] original = installed.GetChecksum();
        BaseStudioModuleRegistration module = BaseStudioModuleRegistry.Create(authority);
        original[0] ^= 0xff;

        BaseStudioGrantRequirement requirement = module.Resources[0].Grants.Single(static grant => grant.OperationId == "base.studio.resource.inspect");
        Assert.Equal(installed.GetChecksum(), requirement.RegistrationChecksum.ToArray());
        Assert.NotEqual(original, requirement.RegistrationChecksum.ToArray());
    }

    private static HPDBaseStudioAuthoritySnapshot Snapshot(string? omitted = null)
    {
        var services = new ServiceCollection();
        services.AddHPDBase(builder => ConfigureBase(builder, includeAllGrants: true, omittedId: omitted));
        using ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<HPDBaseStudioAuthoritySnapshot>();
    }

    private static BaseStudioSha256 Digest(byte value) => BaseStudioSha256.FromDigest(Enumerable.Repeat(value, 32).ToArray());

    private static void ConfigureBase(HPDBaseBuilder builder, bool includeAllGrants,
        string? omittedId = null, string? substitutedId = null, string? invalidSemanticsId = null)
    {
        builder.ConfigureSchema(static options => options.ApplicationId = "sample.application");
        if (!includeAllGrants) return;
        foreach (string id in FixedGrantIds.Where(id => !StringComparer.Ordinal.Equals(id, omittedId)))
        {
            int version = StringComparer.Ordinal.Equals(id, substitutedId) ? 2 : 1;
            builder.AddStaticGrantAuthority(new BaseGrantAuthorityDefinition
            {
                Id = id, Version = version, OwningModuleId = "base",
                SourceContractId = "base.studio.fixed-grant", SourceContractVersion = 1,
            }, new AccessGrant
            {
                Id = id, ApplicationId = "sample.application", ModuleId = "base",
                Audience = StringComparer.Ordinal.Equals(id, invalidSemanticsId)
                    ? HPDBaseEndpointAudience.Application : HPDBaseEndpointAudience.ControlPlane,
                Subject = new AccessSubject { Kind = AccessSubjectKind.User, Id = "operator" },
                Action = id, Scope = new ResourceScope { Kind = ResourceScopeKind.Runtime },
            });
        }
    }

    private static readonly string[] FixedGrantIds =
    [
        "base.studio.action.discover", "base.studio.action.execute", "base.studio.action.preview",
        "base.studio.bootstrap.read", "base.studio.diagnostics.inspect", "base.studio.invalidation.subscribe",
        "base.studio.receipt.discover", "base.studio.receipt.inspect", "base.studio.resource.discover",
        "base.studio.resource.inspect", "base.studio.resource.links", "base.studio.resource.search",
    ];

    private sealed class MissingPrincipalResolver : IBaseStudioPrincipalContextResolver
    {
        public ValueTask<PrincipalContext?> ResolveAsync(Microsoft.AspNetCore.Http.HttpContext httpContext,
            BaseStudioSessionObservation session, CancellationToken cancellationToken)
            => ValueTask.FromResult<PrincipalContext?>(null);
        public ValueTask<BaseOwnedSubjectScopeEvidence?> ResolveScopeAsync(Microsoft.AspNetCore.Http.HttpContext httpContext,
            BaseStudioSessionObservation session, CancellationToken cancellationToken)
            => ValueTask.FromResult<BaseOwnedSubjectScopeEvidence?>(null);
    }

    private sealed class AllowPolicyEvaluator : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new PolicyDecision { Effect = PolicyEffect.Allow, Outcome = PolicyOutcome.Allowed,
                Audit = new PolicyAuditInfo { MatchedGrantIds = [] } });
    }
}
