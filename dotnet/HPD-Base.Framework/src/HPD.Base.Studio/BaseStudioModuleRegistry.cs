using System.Collections.Immutable;
using System.Text;
using HPD.AI.Platform.Studio;

namespace HPD.Base.Studio;

/// <summary>Owns the fixed version-one BASE Studio module registry.</summary>
public static class BaseStudioModuleRegistry
{
    private const string ClientId = "base.control-plane";
    private const string InspectEndpoint = "base.studio.resource.inspect";
    private static readonly BaseStudioSha256 RuntimeAbi = Digest("base.studio.l41-runtime-abi.v1");
    private static readonly BaseStudioSha256 ClientContract = Digest("base.studio.base-client-contract.v1");
    private static readonly BaseStudioSha256 ClientOperations = Digest("base.studio.base-client-operations.v1");
    private static readonly BaseStudioSha256 ComponentAbi = Digest("base.studio.page-component-abi.v1");
    internal const string AutomationFactDescriptor = "{\"kind\":\"object\",\"properties\":[{\"name\":\"factChecksum\",\"wireName\":\"factChecksum\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"identity\",\"wireName\":\"identity\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"kind\",\"wireName\":\"kind\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"resourceToken\",\"wireName\":\"resourceToken\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"safeState\",\"wireName\":\"safeState\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}";
    internal const string SubjectFactDescriptor = "{\"kind\":\"object\",\"properties\":[{\"name\":\"contractId\",\"wireName\":\"contractId\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"contractVersion\",\"wireName\":\"contractVersion\",\"typeId\":\"base.studio.positive-number\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"factChecksum\",\"wireName\":\"factChecksum\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"identity\",\"wireName\":\"identity\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"resourceToken\",\"wireName\":\"resourceToken\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"safeState\",\"wireName\":\"safeState\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}";

    /// <summary>Creates BASE's authorization-neutral static edition asset contribution.</summary>
    public static BaseStudioEditionModuleAssetContribution CreateEditionAssetContribution()
    {
        BaseStudioFrontendExport frontend = CreateFrontend(Pages.Select(static value => value.Id));
        return BaseStudioEditionModuleAssetContribution.Create("base", 1, frontend.FrontendAbiChecksum, CreateAsset());
    }

    /// <summary>Creates BASE's complete immutable Studio contribution from finalized BASE graph authority.</summary>
    public static BaseStudioModuleRegistration Create(HPDBaseStudioAuthoritySnapshot authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        string applicationId = authority.ApplicationId;
        GrantCatalog grants = GrantCatalog.Create(authority);
        ImmutableArray<PageSpec> specs = Pages;
        ImmutableArray<CommandSpec> commandSpecs = [.. Commands.Where(command => authority.HasOperation(command.Id))];
        var commandsByPage = commandSpecs.GroupBy(static value => value.PageId, StringComparer.Ordinal)
            .ToDictionary(static value => value.Key, static value => value.OrderBy(item => item.Id, StringComparer.Ordinal).ToImmutableArray(), StringComparer.Ordinal);

        var views = ImmutableArray.CreateBuilder<BaseStudioViewRegistration>();
        var pages = ImmutableArray.CreateBuilder<BaseStudioPageRegistration>();
        foreach (PageSpec spec in specs.OrderBy(static value => value.Id, StringComparer.Ordinal))
        {
            ImmutableArray<CommandSpec> ownedCommands = commandsByPage.GetValueOrDefault(spec.Id, []);
            var sections = ImmutableArray.CreateBuilder<BaseStudioSectionRegistration>(spec.Sections.Length);
            for (int index = 0; index < spec.Sections.Length; index++)
            {
                string section = spec.Sections[index];
                ImmutableArray<SectionView> sectionViews = SectionViews(spec, section);
                string[] sectionCommands = index == spec.Sections.Length - 1
                    ? ownedCommands.Select(static value => value.Id).ToArray()
                    : [];
                foreach (SectionView view in sectionViews)
                {
                    views.Add(CreateView(view.Id, view.Resource,
                        spec.Landing ? BaseStudioResourceKind.Application : spec.PrimaryResource));
                }
                sections.Add(BaseStudioSectionRegistration.Create(
                    section, $"studio.section.{section}", index, SectionKind(section),
                    sectionViews.Select(static value => value.Id).OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
                    sectionCommands.OrderBy(static value => value, StringComparer.Ordinal).ToArray()));
            }

            BaseStudioPagePresentationRegistration presentation = BaseStudioPagePresentationRegistration.Create(
                spec.Id, 1, spec.Landing ? BaseStudioNavigationRole.AreaLanding : BaseStudioNavigationRole.Contextual,
                Workspace(spec.Kind), sections, null, null,
                spec.Id is "base.schema.detail" ? BaseStudioDraftRetentionClass.CurrentDocumentNavigation : BaseStudioDraftRetentionClass.None);
            pages.Add(BaseStudioPageRegistration.Create(
                spec.Id, 1, spec.Area, $"studio.page.{spec.Id}", Route(spec), spec.Kind, presentation,
                spec.Resources.OrderBy(static value => (byte)value), [InspectEndpoint],
                grants.Resource(spec.PrimaryResource), BaseStudioDisclosureClass.ProtectedValue));
        }

        BaseStudioPageRegistration[] orderedPages = pages.OrderBy(static value => value.PageId, StringComparer.Ordinal).ToArray();
        BaseStudioViewRegistration[] orderedViews = views.OrderBy(static value => value.ViewId, StringComparer.Ordinal).ToArray();
        BaseStudioCommandRegistration[] commands = commandSpecs.OrderBy(static value => value.Id, StringComparer.Ordinal)
            .Select(command => CreateCommand(command, grants)).ToArray();
        BaseStudioResourceRegistration[] resources = Enum.GetValues<BaseStudioResourceKind>()
            .Where(static value => value < BaseStudioResourceKind.GraphDefinition)
            .Select(value => BaseStudioResourceRegistration.Create(
                value, ResourceResolverId(value), [InspectEndpoint], grants.Resource(value),
                BaseStudioDisclosureClass.ProtectedValue)).ToArray();
        BaseStudioFrontendExport frontend = CreateFrontend(orderedPages.Select(static value => value.PageId));

        return BaseStudioModuleRegistration.CreateBase(
            applicationId, CreateAsset(), frontend, orderedPages, orderedViews, resources, commands, CreateLinks(),
            [BaseStudioFrameworkClientRegistration.Create(ClientId, 1, BaseStudioContractNecessity.Required,
                BaseStudioFrameworkClientProtocol.BaseL41DynamicMap, RuntimeAbi, ClientContract, ClientOperations,
                "base.studio.runtime", BaseStudioFrameworkClientTransportClass.SameOriginShellAuthenticated,
                orderedPages.Select(static page => page.PageId),
                BaseStudioFrameworkClientLimits.Create(512, 16_777_216, 16_777_216, 16,
                    TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)))],
            [grants.Module("base.studio.bootstrap.read")],
            BaseStudioModuleLimits.Create(64, 256, 128, 256, 256, 32));
    }

    private static BaseStudioAssetManifest CreateAsset()
    {
        const string assetPath = "base/53f8e0b4f2056ab2c410d456f4d3d4b3479c94ec2fc45df2f50d0b35f66be94f.js";
        using Stream stream = typeof(BaseStudioModuleRegistry).Assembly.GetManifestResourceStream("HPD.Base.Studio.Assets.base.js")
            ?? throw new InvalidOperationException("The prebuilt BASE Studio module asset is absent.");
        using var content = new MemoryStream();
        stream.CopyTo(content);
        return BaseStudioAssetManifest.Create(assetPath, BaseStudioModuleNecessity.Required,
            BaseStudioShellContract.Current,
            [BaseStudioAssetSource.Create(assetPath, BaseStudioAssetMediaType.JavaScriptModule, content.GetBuffer().AsSpan(0, checked((int)content.Length)))]);
    }

    private static BaseStudioFrontendExport CreateFrontend(IEnumerable<string> pageIds)
    {
        string[] pages = pageIds.Order(StringComparer.Ordinal).ToArray();
        BaseStudioFrameworkClientLimits limits = BaseStudioFrameworkClientLimits.Create(512, 16_777_216, 16_777_216, 16,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));
        return BaseStudioFrontendExport.Create("base", 1,
            [BaseStudioFrontendClientSlot.Create(ClientId, 1, BaseStudioFrameworkClientProtocol.BaseL41DynamicMap,
                RuntimeAbi, ClientContract, ClientOperations, "base.studio.runtime",
                BaseStudioFrameworkClientTransportClass.SameOriginShellAuthenticated, pages, limits)],
            pages.Select(static value => BaseStudioPageComponentBinding.Create(value, $"component.{value}", ComponentAbi)));
    }

    private static BaseStudioViewRegistration CreateView(string id, BaseStudioResourceKind kind, BaseStudioResourceKind requestKind)
    {
        string typePrefix = id.ToLowerInvariant();
        BaseStudioNamedTypeContract request = BaseStudioNamedTypeContract.Create(typePrefix + ".request",
            System.Text.Encoding.UTF8.GetBytes(ViewRequestDescriptor(id, requestKind)));
        BaseStudioNamedTypeContract item = id is "base.collection.detail.history.list" or "base.record.detail.history.list"
            ? BaseStudioNamedTypeContract.Create("base.studio.evidence.record-mutation.item",
                "{\"kind\":\"object\",\"properties\":[{\"name\":\"collectionId\",\"wireName\":\"collectionId\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"evidenceChecksum\",\"wireName\":\"evidenceChecksum\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"evidenceId\",\"wireName\":\"evidenceId\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"observedAtUtc\",\"wireName\":\"observedAtUtc\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"recordId\",\"wireName\":\"recordId\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"semanticKind\",\"wireName\":\"semanticKind\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}"u8)
            : id.StartsWith("base.search.query.", StringComparison.Ordinal)
            ? BaseStudioNamedTypeContract.Create(SearchQueryItemTypeId(id), Encoding.UTF8.GetBytes(SearchQueryItemDescriptor(id)))
            : id.StartsWith("base.overview.", StringComparison.Ordinal)
            ? BaseStudioNamedTypeContract.Create("base.studio.overview.value",
                "{\"kind\":\"object\",\"properties\":[{\"name\":\"applicationId\",\"wireName\":\"applicationId\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"contractVersion\",\"wireName\":\"contractVersion\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"diagnosticCount\",\"wireName\":\"diagnosticCount\",\"typeId\":\"base.studio.nonnegative-long\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"refreshedAtUtc\",\"wireName\":\"refreshedAtUtc\",\"typeId\":\"base.studio.optional-text\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"},{\"name\":\"runtimeId\",\"wireName\":\"runtimeId\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"viewId\",\"wireName\":\"viewId\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}"u8)
            : id == "base.semanticActivations.definitions.list"
            ? BaseStudioNamedTypeContract.Create("base.studio.semantic-definition-inspection.item",
                "{\"kind\":\"object\",\"properties\":[{\"name\":\"capturedAuthorityGeneration\",\"wireName\":\"capturedAuthorityGeneration\",\"typeId\":\"base.studio.nonnegative-long\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"},{\"name\":\"compactedCount\",\"wireName\":\"compactedCount\",\"typeId\":\"base.studio.nonnegative-long\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"definitionChecksum\",\"wireName\":\"definitionChecksum\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"hasMore\",\"wireName\":\"hasMore\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"id\",\"wireName\":\"id\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"inspectionState\",\"wireName\":\"inspectionState\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"kind\",\"wireName\":\"kind\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"liveCount\",\"wireName\":\"liveCount\",\"typeId\":\"base.studio.nonnegative-long\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"owningModuleId\",\"wireName\":\"owningModuleId\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"pageChecksum\",\"wireName\":\"pageChecksum\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"},{\"name\":\"retiredCount\",\"wireName\":\"retiredCount\",\"typeId\":\"base.studio.nonnegative-long\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"storeId\",\"wireName\":\"storeId\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"version\",\"wireName\":\"version\",\"typeId\":\"base.studio.nonnegative-long\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}"u8)
            : id.StartsWith("base.operations.definitions.", StringComparison.Ordinal)
            ? BaseStudioNamedTypeContract.Create("base.studio.installed-definition.item",
                "{\"kind\":\"object\",\"properties\":[{\"name\":\"definitionChecksum\",\"wireName\":\"definitionChecksum\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"id\",\"wireName\":\"id\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"kind\",\"wireName\":\"kind\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"owningModuleId\",\"wireName\":\"owningModuleId\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"version\",\"wireName\":\"version\",\"typeId\":\"base.studio.nonnegative-long\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}"u8)
            : id is "base.operations.executions.list" or "base.operations.receipts.list"
            ? BaseStudioNamedTypeContract.Create("base.studio.atomic-execution.item",
                "{\"kind\":\"object\",\"properties\":[{\"name\":\"expiresAtUtc\",\"wireName\":\"expiresAtUtc\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"identity\",\"wireName\":\"identity\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"requestFingerprint\",\"wireName\":\"requestFingerprint\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"resultKind\",\"wireName\":\"resultKind\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"structuralDigest\",\"wireName\":\"structuralDigest\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}"u8)
            : id.StartsWith("base.automation", StringComparison.Ordinal) || id.StartsWith("base.activation.detail.", StringComparison.Ordinal) ||
              id.StartsWith("base.schedule.detail.", StringComparison.Ordinal) || id.StartsWith("base.occurrence.detail.", StringComparison.Ordinal) ||
              id.StartsWith("base.effect.detail.", StringComparison.Ordinal) || id.StartsWith("base.executor.detail.", StringComparison.Ordinal)
            ? BaseStudioNamedTypeContract.Create(typePrefix + ".item",
                System.Text.Encoding.UTF8.GetBytes(AutomationItemDescriptor(kind)))
            : id.StartsWith("base.subjects.", StringComparison.Ordinal) || id.StartsWith("base.subjectContract.detail.", StringComparison.Ordinal) ||
              id.StartsWith("base.subject.detail.", StringComparison.Ordinal) || id.StartsWith("base.lifecycleConsumer.detail.", StringComparison.Ordinal) ||
              id.StartsWith("base.retirementBarrier.detail.", StringComparison.Ordinal)
            ? BaseStudioNamedTypeContract.Create(typePrefix + ".item", System.Text.Encoding.UTF8.GetBytes(SubjectItemDescriptor(kind)))
            : BaseStudioSecurityContracts.IsRegistrationView(id)
            ? BaseStudioNamedTypeContract.Create(typePrefix + ".item",
                Encoding.UTF8.GetBytes(BaseStudioSecurityContracts.ItemDescriptor(id)))
            : BaseStudioInfrastructureContracts.IsInventoryView(id)
            ? BaseStudioNamedTypeContract.Create(typePrefix + ".item",
                Encoding.UTF8.GetBytes(BaseStudioInfrastructureContracts.ItemDescriptor(id)))
            : BaseStudioDiagnosticsContracts.IsDiagnosticsView(id)
            ? BaseStudioNamedTypeContract.Create(typePrefix + ".item",
                Encoding.UTF8.GetBytes(BaseStudioDiagnosticsContracts.ItemDescriptor(id)))
            : BaseStudioNamedTypeContract.Create(typePrefix + ".item",
                "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":256,\"format\":\"studio-resource-summary\"}"u8);
        BaseStudioNamedTypeContract filter = BaseStudioNamedTypeContract.Create(typePrefix + ".filter.request",
            "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":128,\"format\":\"nfc-search\"}"u8);
        BaseStudioNamedTypeContract disclosure = BaseStudioNamedTypeContract.Create(typePrefix + ".disclosure",
            "{\"kind\":\"enum\",\"values\":[\"authorizedMetadata\",\"protectedValue\"]}"u8);
        bool list = id.EndsWith(".list", StringComparison.Ordinal);
        BaseStudioSortRegistration identitySort = BaseStudioSortRegistration.Create(id + ".sort.identity",
            [BaseStudioOrderMember.Create("identity", BaseStudioOrderDirection.Ascending,
                BaseStudioNullPlacement.ValuesThenMissingThenNull)]);
        BaseStudioViewPresentationRegistration presentation = BaseStudioViewPresentationRegistration.Create(
            id, null, null, BaseStudioEmptyStateKind.NoItems,
            BaseStudioActivityPolicy.Create(BaseStudioActivityPolicyKind.GovernedInvalidationRefresh, 10, 3, 32),
            BaseStudioPreferenceSchema.Create(id + ".preferences", 1,
                [BaseStudioPreferenceKind.Theme, BaseStudioPreferenceKind.Density, BaseStudioPreferenceKind.PreferredTab],
                4_096, TimeSpan.FromDays(180)));
        return BaseStudioViewRegistration.Create(id, 1, Producer(kind), request.TypeId, request.NodeChecksum,
            kind, item.TypeId, item.NodeChecksum, id + ".cursor",
            [BaseStudioOrderMember.Create("identity", BaseStudioOrderDirection.Ascending,
                BaseStudioNullPlacement.ValuesThenMissingThenNull)],
            list ? [BaseStudioFilterRegistration.Create(id + ".filter.safeLabel", filter.TypeId, filter.NodeChecksum)] : [],
            [identitySort], disclosure.NodeChecksum,
            1_048_576, 500, presentation);
    }

    internal static string AutomationItemDescriptor(BaseStudioResourceKind kind) => kind switch
    {
        BaseStudioResourceKind.Activation or BaseStudioResourceKind.ActivationAttempt => FactDescriptor("attemptNumber", "definitionId", "definitionVersion", "state"),
        BaseStudioResourceKind.Schedule => FactDescriptor("enabled", "scheduleId", "version"),
        BaseStudioResourceKind.Occurrence => FactDescriptor("activationId", "disposition", "occurrenceId", "scheduleId"),
        BaseStudioResourceKind.Effect => FactDescriptor("activationId", "attemptNumber", "effectId"),
        BaseStudioResourceKind.Executor => FactDescriptor("executorGeneration", "hostId", "processIncarnationId", "state"),
        BaseStudioResourceKind.Receipt => FactDescriptor("subjectIdentity", "transitionKind"),
        BaseStudioResourceKind.QuarantineItem => FactDescriptor("quarantineKind", "subjectIdentity"),
        _ => FactDescriptor("authorityState"),
    };

    internal static string SubjectItemDescriptor(BaseStudioResourceKind kind) => kind switch
    {
        BaseStudioResourceKind.SubjectContract => FactDescriptor("authorityEpoch", "contractId", "contractVersion", "publicationKind", "publicationPosition", "restoreEpoch", "stateGeneration"),
        BaseStudioResourceKind.Subject => FactDescriptor("contractId", "contractVersion", "createdJournalPosition", "incarnation", "protectedSubjectIdentity"),
        BaseStudioResourceKind.LifecycleConsumer => FactDescriptor("consumerChecksum", "consumerId", "consumerVersion", "contractId", "contractVersion", "deliveryEpoch", "projectionGeneration", "publishedGraphGeneration"),
        BaseStudioResourceKind.LifecycleCheckpoint => FactDescriptor("checkpointGeneration", "consumerId", "consumerVersion", "throughBoundary"),
        BaseStudioResourceKind.RetirementBarrier => FactDescriptor("barrierGeneration", "barrierState", "deadlineUtc", "requiredConsumerSetChecksum", "tombstoneSequence"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string FactDescriptor(params string[] semanticFields)
    {
        string[] fields = [.. new[] { "factChecksum", "identity", "resourceToken" }.Concat(semanticFields).Order(StringComparer.Ordinal)];
        return $"{{\"kind\":\"object\",\"properties\":[{string.Join(',', fields.Select(static name => $"{{\"name\":\"{name}\",\"wireName\":\"{name}\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}}"))}],\"additionalProperties\":false}}";
    }

    private static string ViewRequestDescriptor(string viewId, BaseStudioResourceKind kind)
    {
        string typeId = kind switch
        {
            BaseStudioResourceKind.Application => "base.studio.resource.application",
            BaseStudioResourceKind.Module => "base.studio.resource.module",
            BaseStudioResourceKind.Collection => "base.studio.resource.collection",
            BaseStudioResourceKind.Record => "base.studio.resource.record",
            _ => "base.studio.resource." + kind.ToString().ToLowerInvariant(),
        };
        if (viewId.StartsWith("base.policy.explain.", StringComparison.Ordinal))
            return $"{{\"kind\":\"object\",\"properties\":[{{\"name\":\"operationId\",\"wireName\":\"operationId\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}},{{\"name\":\"resource\",\"wireName\":\"resource\",\"typeId\":\"{typeId}\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}},{{\"name\":\"targetResourceKind\",\"wireName\":\"targetResourceKind\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}},{{\"name\":\"targetResourceToken\",\"wireName\":\"targetResourceToken\",\"typeId\":\"base.studio.resource-route-token\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}}],\"additionalProperties\":false}}";
        if (viewId.StartsWith("base.search.query.", StringComparison.Ordinal))
            return "{\"kind\":\"object\",\"properties\":[{\"name\":\"after\",\"wireName\":\"after\",\"typeId\":\"base.studio.optional-search-cursor\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"},{\"name\":\"filter\",\"wireName\":\"filter\",\"typeId\":\"base.studio.search-filter\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"},{\"name\":\"order\",\"wireName\":\"order\",\"typeId\":\"base.studio.search-order\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"query\",\"wireName\":\"query\",\"typeId\":\"base.studio.search-query\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"resource\",\"wireName\":\"resource\",\"typeId\":\"base.studio.resource.searchindex\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"take\",\"wireName\":\"take\",\"typeId\":\"base.studio.search-page-size\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}";
        return $"{{\"kind\":\"object\",\"properties\":[{{\"name\":\"resource\",\"wireName\":\"resource\",\"typeId\":\"{typeId}\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}}],\"additionalProperties\":false}}";
    }

    internal static string SearchQueryItemTypeId(string viewId) => viewId switch
    {
        "base.search.query.query.detail" => "base.studio.search.query-summary",
        "base.search.query.results.list" => "base.studio.search.query-result.item",
        "base.search.query.explanation.detail" => "base.studio.search.explanation",
        "base.search.query.evidence.detail" => "base.studio.search.evidence",
        _ => throw new ArgumentOutOfRangeException(nameof(viewId)),
    };

    internal static string SearchQueryItemDescriptor(string viewId) => viewId switch
    {
        "base.search.query.query.detail" => "{\"kind\":\"object\",\"properties\":[{\"name\":\"queryChecksum\",\"wireName\":\"queryChecksum\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"resourceToken\",\"wireName\":\"resourceToken\",\"typeId\":\"base.studio.resource-route-token\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"take\",\"wireName\":\"take\",\"typeId\":\"base.studio.search-page-size\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}",
        "base.search.query.results.list" => "{\"kind\":\"object\",\"properties\":[{\"name\":\"explanationChecksum\",\"wireName\":\"explanationChecksum\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"rank\",\"wireName\":\"rank\",\"typeId\":\"base.studio.positive-number\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"resourceToken\",\"wireName\":\"resourceToken\",\"typeId\":\"base.studio.resource-route-token\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"safeLabel\",\"wireName\":\"safeLabel\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"score\",\"wireName\":\"score\",\"typeId\":\"base.studio.search-score\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}",
        "base.search.query.explanation.detail" => "{\"kind\":\"object\",\"properties\":[{\"name\":\"kind\",\"wireName\":\"kind\",\"typeId\":\"base.studio.search-explanation-kind\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"reasonCode\",\"wireName\":\"reasonCode\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}",
        "base.search.query.evidence.detail" => "{\"kind\":\"object\",\"properties\":[{\"name\":\"consistencyToken\",\"wireName\":\"consistencyToken\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"queryChecksum\",\"wireName\":\"queryChecksum\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}",
        _ => throw new ArgumentOutOfRangeException(nameof(viewId)),
    };

    private static string Producer(BaseStudioResourceKind kind) => kind switch
    {
        BaseStudioResourceKind.Application or BaseStudioResourceKind.Module or BaseStudioResourceKind.RegisteredRead or
            BaseStudioResourceKind.SelectionOperation or BaseStudioResourceKind.ModuleMutation => "base.studio.graph",
        BaseStudioResourceKind.Collection or BaseStudioResourceKind.Record or BaseStudioResourceKind.Relation => "base.studio.records",
        BaseStudioResourceKind.FileBucket or BaseStudioResourceKind.File => "base.studio.files",
        BaseStudioResourceKind.OperationExecution or BaseStudioResourceKind.Receipt => "base.studio.evidence",
        BaseStudioResourceKind.ActivationDefinition or BaseStudioResourceKind.Activation or BaseStudioResourceKind.Schedule or
            BaseStudioResourceKind.Occurrence or BaseStudioResourceKind.ActivationAttempt or BaseStudioResourceKind.Effect or
            BaseStudioResourceKind.Executor => "base.studio.activations",
        BaseStudioResourceKind.SubjectContract or BaseStudioResourceKind.Subject or BaseStudioResourceKind.LifecycleConsumer or
            BaseStudioResourceKind.LifecycleCheckpoint => "base.studio.lifecycle",
        BaseStudioResourceKind.RetirementBarrier => "base.studio.retirement",
        BaseStudioResourceKind.TextIndex or BaseStudioResourceKind.VectorIndex or BaseStudioResourceKind.SearchRebuild => "base.studio.search",
        BaseStudioResourceKind.Policy or BaseStudioResourceKind.Grant => "base.studio.policy",
        BaseStudioResourceKind.Store or BaseStudioResourceKind.Provider or BaseStudioResourceKind.CertificationReceipt or
            BaseStudioResourceKind.Schema or BaseStudioResourceKind.Migration or BaseStudioResourceKind.Backup or
            BaseStudioResourceKind.Restore or BaseStudioResourceKind.Maintenance or BaseStudioResourceKind.QuarantineItem => "base.studio.administration",
        BaseStudioResourceKind.Health or BaseStudioResourceKind.Diagnostic => "base.studio.diagnostics",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static BaseStudioCommandRegistration CreateCommand(CommandSpec spec, GrantCatalog grants)
    {
        string typePrefix = spec.Id.ToLowerInvariant();
        BaseStudioNamedTypeContract input = BaseStudioNamedTypeContract.Create(typePrefix + ".input",
            Encoding.UTF8.GetBytes(spec.Id is "textIndex.rebuild" or "vectorIndex.rebuild" ? RebuildInputDescriptor(spec.Id)
                : spec.Id.StartsWith("retirement.", StringComparison.Ordinal) ? RetirementInputDescriptor(spec.Id)
                : "{\"kind\":\"object\",\"properties\":[],\"additionalProperties\":false}"));
        BaseStudioNamedTypeContract result = BaseStudioNamedTypeContract.Create(typePrefix + ".result",
            Encoding.UTF8.GetBytes(spec.Id is "textIndex.rebuild" or "vectorIndex.rebuild" ? RebuildResultDescriptor
                : spec.Id.StartsWith("retirement.", StringComparison.Ordinal) ? RetirementResultDescriptor
                : "{\"kind\":\"object\",\"properties\":[],\"additionalProperties\":false}"));
        BaseStudioFreshAuthenticationClass? fresh = spec.Action >= BaseStudioActionClass.Destructive
            ? BaseStudioFreshAuthenticationClass.MultiFactor : null;
        BaseStudioCommandAcknowledgementRequirement[] acknowledgements = spec.Action >= BaseStudioActionClass.OperationalTransition
            ? [BaseStudioCommandAcknowledgementRequirement.Create("confirm." + spec.Id, "impact." + spec.Id)] : [];
        return BaseStudioCommandRegistration.Create(spec.Id, 1, spec.Id, input.TypeId, input.NodeChecksum,
            result.TypeId, result.NodeChecksum, spec.Action, grants.Command(spec.Action), 1_048_576, 1_048_576, fresh, acknowledgements);
    }

    private static string RebuildInputDescriptor(string commandId) => commandId == "textIndex.rebuild"
        ? "{\"kind\":\"object\",\"properties\":[{\"name\":\"expectedGeneration\",\"wireName\":\"expectedGeneration\",\"typeId\":\"base.studio.positive-number\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"mode\",\"wireName\":\"mode\",\"typeId\":\"base.studio.rebuild-mode\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"previewChecksum\",\"wireName\":\"previewChecksum\",\"typeId\":\"base.studio.optional-sha256\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"},{\"name\":\"resourceToken\",\"wireName\":\"resourceToken\",\"typeId\":\"base.studio.resource-route-token\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}"
        : "{\"kind\":\"object\",\"properties\":[{\"name\":\"confirmation\",\"wireName\":\"confirmation\",\"typeId\":\"base.studio.optional-text\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"},{\"name\":\"expectedGeneration\",\"wireName\":\"expectedGeneration\",\"typeId\":\"base.studio.positive-number\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"expectedPurgeGeneration\",\"wireName\":\"expectedPurgeGeneration\",\"typeId\":\"base.studio.nonnegative-long\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"expectedStoreGeneration\",\"wireName\":\"expectedStoreGeneration\",\"typeId\":\"base.studio.positive-number\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"mode\",\"wireName\":\"mode\",\"typeId\":\"base.studio.rebuild-mode\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"previewChecksum\",\"wireName\":\"previewChecksum\",\"typeId\":\"base.studio.optional-sha256\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"},{\"name\":\"resourceToken\",\"wireName\":\"resourceToken\",\"typeId\":\"base.studio.resource-route-token\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}";
    private const string RebuildResultDescriptor = "{\"kind\":\"object\",\"properties\":[{\"name\":\"expiresAtUtc\",\"wireName\":\"expiresAtUtc\",\"typeId\":\"base.studio.optional-text\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"},{\"name\":\"mode\",\"wireName\":\"mode\",\"typeId\":\"base.studio.rebuild-mode\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"previewChecksum\",\"wireName\":\"previewChecksum\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"receiptChecksum\",\"wireName\":\"receiptChecksum\",\"typeId\":\"base.studio.optional-sha256\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"},{\"name\":\"resultingGeneration\",\"wireName\":\"resultingGeneration\",\"typeId\":\"base.studio.nonnegative-long\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}";
    internal static string RebuildInputDescriptorForRuntime(string commandId) => RebuildInputDescriptor(commandId);
    internal static string RebuildResultDescriptorForRuntime => RebuildResultDescriptor;
    internal static string RetirementInputDescriptorForRuntime(string commandId) => RetirementInputDescriptor(commandId);
    internal static string RetirementResultDescriptorForRuntime => RetirementResultDescriptor;

    private static string RetirementInputDescriptor(string commandId)
    {
        (string Name, string Type, bool Nullable)[] specific = commandId switch
        {
            "retirement.timeout" => [("expectedBarrierChecksum","base.studio.sha256",false),("expectedBarrierGeneration","base.studio.nonnegative-long",false)],
            "retirement.override" => [("changeReference","base.studio.text",false),("expectedBarrierChecksum","base.studio.sha256",false),("expectedBarrierGeneration","base.studio.nonnegative-long",false),("expectedTombstoneSequence","base.studio.nonnegative-long",false),("intent","base.studio.text",false)],
            "retirement.purge" => [("expectedBarrierChecksum","base.studio.sha256",false),("expectedBarrierGeneration","base.studio.nonnegative-long",false),("expectedPrivateRevision","base.studio.text",false),("expectedTombstoneSequence","base.studio.nonnegative-long",false)],
            "retirement.consumer.remove" => [("consumerId","base.studio.text",false),("consumerVersion","base.studio.positive-number",false),("expectedAcceptedSetChecksum","base.studio.sha256",false),("expectedConsumerChecksum","base.studio.sha256",false),("expectedGraphGeneration","base.studio.nonnegative-long",false)],
            _ => throw new ArgumentOutOfRangeException(nameof(commandId)),
        };
        (string Name,string Type,bool Nullable)[] fields = [.. specific, ("mode","base.studio.rebuild-mode",false), ("previewChecksum","base.studio.optional-sha256",true), ("resourceToken","base.studio.resource-route-token",false)];
        return $"{{\"kind\":\"object\",\"properties\":[{string.Join(',', fields.OrderBy(static value => value.Name, StringComparer.Ordinal).Select(static value => $"{{\"name\":\"{value.Name}\",\"wireName\":\"{value.Name}\",\"typeId\":\"{value.Type}\",\"required\":true,\"nullable\":{value.Nullable.ToString().ToLowerInvariant()},\"disclosureShape\":\"none\"}}"))}],\"additionalProperties\":false}}";
    }

    private const string RetirementResultDescriptor = "{\"kind\":\"object\",\"properties\":[{\"name\":\"expiresAtUtc\",\"wireName\":\"expiresAtUtc\",\"typeId\":\"base.studio.optional-text\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"},{\"name\":\"mode\",\"wireName\":\"mode\",\"typeId\":\"base.studio.rebuild-mode\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"previewChecksum\",\"wireName\":\"previewChecksum\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"receiptChecksum\",\"wireName\":\"receiptChecksum\",\"typeId\":\"base.studio.optional-sha256\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"},{\"name\":\"resultingGeneration\",\"wireName\":\"resultingGeneration\",\"typeId\":\"base.studio.nonnegative-long\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}";

    private static BaseStudioLinkRegistration[] CreateLinks()
    {
        (BaseStudioResourceKind Source, BaseStudioResourceKind Target, BaseStudioLinkRelation Relation)[] definitions =
        [
            (BaseStudioResourceKind.Application, BaseStudioResourceKind.Module, BaseStudioLinkRelation.Owns),
            (BaseStudioResourceKind.Application, BaseStudioResourceKind.Collection, BaseStudioLinkRelation.Owns),
            (BaseStudioResourceKind.Application, BaseStudioResourceKind.Store, BaseStudioLinkRelation.StoredBy),
            (BaseStudioResourceKind.Module, BaseStudioResourceKind.RegisteredRead, BaseStudioLinkRelation.Owns),
            (BaseStudioResourceKind.Module, BaseStudioResourceKind.SelectionOperation, BaseStudioLinkRelation.Owns),
            (BaseStudioResourceKind.Module, BaseStudioResourceKind.ModuleMutation, BaseStudioLinkRelation.Owns),
            (BaseStudioResourceKind.Collection, BaseStudioResourceKind.Record, BaseStudioLinkRelation.Owns),
            (BaseStudioResourceKind.Collection, BaseStudioResourceKind.Relation, BaseStudioLinkRelation.Owns),
            (BaseStudioResourceKind.Collection, BaseStudioResourceKind.FileBucket, BaseStudioLinkRelation.Owns),
            (BaseStudioResourceKind.Collection, BaseStudioResourceKind.TextIndex, BaseStudioLinkRelation.Owns),
            (BaseStudioResourceKind.Collection, BaseStudioResourceKind.VectorIndex, BaseStudioLinkRelation.Owns),
            (BaseStudioResourceKind.FileBucket, BaseStudioResourceKind.File, BaseStudioLinkRelation.Owns),
            (BaseStudioResourceKind.TextIndex, BaseStudioResourceKind.SearchRebuild, BaseStudioLinkRelation.Owns),
            (BaseStudioResourceKind.VectorIndex, BaseStudioResourceKind.SearchRebuild, BaseStudioLinkRelation.Owns),
            (BaseStudioResourceKind.SubjectContract, BaseStudioResourceKind.Subject, BaseStudioLinkRelation.Owns),
            (BaseStudioResourceKind.SubjectContract, BaseStudioResourceKind.LifecycleConsumer, BaseStudioLinkRelation.Owns),
            (BaseStudioResourceKind.Subject, BaseStudioResourceKind.RetirementBarrier, BaseStudioLinkRelation.Owns),
            (BaseStudioResourceKind.OperationExecution, BaseStudioResourceKind.Receipt, BaseStudioLinkRelation.ReceiptFor),
            (BaseStudioResourceKind.Provider, BaseStudioResourceKind.CertificationReceipt, BaseStudioLinkRelation.Owns),
            (BaseStudioResourceKind.Store, BaseStudioResourceKind.Provider, BaseStudioLinkRelation.StoredBy),
            (BaseStudioResourceKind.Store, BaseStudioResourceKind.Schema, BaseStudioLinkRelation.Owns),
            (BaseStudioResourceKind.Store, BaseStudioResourceKind.Backup, BaseStudioLinkRelation.Owns),
            (BaseStudioResourceKind.Store, BaseStudioResourceKind.Restore, BaseStudioLinkRelation.Owns),
            (BaseStudioResourceKind.Store, BaseStudioResourceKind.Maintenance, BaseStudioLinkRelation.Owns),
            (BaseStudioResourceKind.Maintenance, BaseStudioResourceKind.QuarantineItem, BaseStudioLinkRelation.Owns),
            (BaseStudioResourceKind.Health, BaseStudioResourceKind.Diagnostic, BaseStudioLinkRelation.Diagnoses),
        ];
        return definitions.OrderBy(static value => (byte)value.Source).ThenBy(static value => (byte)value.Relation)
            .ThenBy(static value => (byte)value.Target)
            .Select(static value => BaseStudioLinkRegistration.Create(value.Source, value.Target, value.Relation,
                ResourceResolverId(value.Target)))
            .ToArray();
    }

    private static string ResourceResolverId(BaseStudioResourceKind kind)
        => $"base.studio.resolve.{kind.ToString().ToLowerInvariant()}";

    private static BaseStudioRouteTemplate Route(PageSpec spec)
        => BaseStudioRouteTemplate.Create(spec.Id + ".route", spec.Route.Length == 0 ? [] : spec.Route.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(static value => value == ":resource"
                ? BaseStudioRouteSegment.Parameter("resource", BaseStudioRouteCodec.StudioResourceIdentity)
                : BaseStudioRouteSegment.Literal(value)));

    private static string ViewId(string pageId, string section)
        => $"{pageId}.{section}.{(ListSections.Contains(section) ? "list" : "detail")}";

    private static ImmutableArray<SectionView> SectionViews(PageSpec page, string section)
    {
        if (page.Id == "base.operations" && section == "definitions")
        {
            return
            [
                new("base.operations.definitions.registeredReads.list", BaseStudioResourceKind.RegisteredRead),
                new("base.operations.definitions.selectionOperations.list", BaseStudioResourceKind.SelectionOperation),
                new("base.operations.definitions.moduleMutations.list", BaseStudioResourceKind.ModuleMutation),
                new("base.operations.definitions.semanticActivations.list", BaseStudioResourceKind.Application),
            ];
        }

        if (page.Id == "base.semanticActivations" && section == "definitions")
            return [new("base.semanticActivations.definitions.list", BaseStudioResourceKind.Application)];

        BaseStudioResourceKind resource = (page.Id, section) switch
        {
            ("base.operations", "receipts") => BaseStudioResourceKind.Receipt,
            ("base.automation", "schedules") => BaseStudioResourceKind.Schedule,
            ("base.automation", "effects") => BaseStudioResourceKind.Effect,
            ("base.automation", "executors") => BaseStudioResourceKind.Executor,
            ("base.automation", "quarantine") => BaseStudioResourceKind.QuarantineItem,
            ("base.activation.detail", "attempts") => BaseStudioResourceKind.ActivationAttempt,
            ("base.activation.detail", "effect") => BaseStudioResourceKind.Effect,
            ("base.activation.detail", "receipts") => BaseStudioResourceKind.Receipt,
            ("base.schedule.detail", "occurrences") => BaseStudioResourceKind.Occurrence,
            ("base.occurrence.detail", "activation") => BaseStudioResourceKind.Activation,
            ("base.effect.detail", "executor") => BaseStudioResourceKind.Executor,
            ("base.executor.detail", "effects") => BaseStudioResourceKind.Effect,
            ("base.subjects", "contracts") => BaseStudioResourceKind.SubjectContract,
            ("base.subjects", "subjects") => BaseStudioResourceKind.Subject,
            ("base.subjects", "consumers") => BaseStudioResourceKind.LifecycleConsumer,
            ("base.subjects", "barriers") => BaseStudioResourceKind.RetirementBarrier,
            ("base.subjectContract.detail", "consumers") => BaseStudioResourceKind.LifecycleConsumer,
            ("base.subjectContract.detail", "lifecycle") => BaseStudioResourceKind.LifecycleCheckpoint,
            ("base.subjectContract.detail", "retirement") => BaseStudioResourceKind.RetirementBarrier,
            ("base.subject.detail", "delivery") => BaseStudioResourceKind.LifecycleCheckpoint,
            ("base.subject.detail", "retirement") => BaseStudioResourceKind.RetirementBarrier,
            ("base.lifecycleConsumer.detail", "checkpoint") => BaseStudioResourceKind.LifecycleCheckpoint,
            ("base.retirementBarrier.detail", "consumers") => BaseStudioResourceKind.LifecycleConsumer,
            ("base.search", "textIndexes") => BaseStudioResourceKind.TextIndex,
            ("base.search", "vectorIndexes") => BaseStudioResourceKind.VectorIndex,
            ("base.search", "rebuilds") => BaseStudioResourceKind.SearchRebuild,
            ("base.textIndex.detail", "rebuild") => BaseStudioResourceKind.SearchRebuild,
            ("base.vectorIndex.detail", "rebuild") => BaseStudioResourceKind.SearchRebuild,
            ("base.security", "grants") => BaseStudioResourceKind.Grant,
            ("base.infrastructure", "schemas") => BaseStudioResourceKind.Schema,
            ("base.infrastructure", "backups") => BaseStudioResourceKind.Backup,
            ("base.infrastructure", "maintenance") => BaseStudioResourceKind.Maintenance,
            ("base.infrastructure", "attention") => BaseStudioResourceKind.Maintenance,
            ("base.store.detail", "certification") => BaseStudioResourceKind.CertificationReceipt,
            ("base.store.detail", "health") => BaseStudioResourceKind.Health,
            ("base.store.detail", "quarantine") => BaseStudioResourceKind.QuarantineItem,
            ("base.store.detail", "maintenance") => BaseStudioResourceKind.Maintenance,
            ("base.store.detail", "recovery") => BaseStudioResourceKind.Restore,
            ("base.store.detail", "diagnostics") => BaseStudioResourceKind.Diagnostic,
            ("base.provider.detail", "certification") => BaseStudioResourceKind.CertificationReceipt,
            ("base.provider.detail", "health") => BaseStudioResourceKind.Health,
            ("base.provider.detail", "diagnostics") => BaseStudioResourceKind.Diagnostic,
            ("base.schema.detail", "plans") => BaseStudioResourceKind.Migration,
            ("base.diagnostics", "health") => BaseStudioResourceKind.Health,
            _ => page.PrimaryResource,
        };
        return [new SectionView(ViewId(page.Id, section), resource)];
    }

    private static BaseStudioSectionKind SectionKind(string section) => section switch
    {
        "summary" or "outcome" or "result" => BaseStudioSectionKind.Summary,
        "history" or "timeline" or "activity" => BaseStudioSectionKind.History,
        "evidence" or "accounting" or "diagnostics" => BaseStudioSectionKind.Evidence,
        "actions" or "remediation" => BaseStudioSectionKind.Actions,
        "configuration" or "schema" or "contract" or "recurrence" => BaseStudioSectionKind.Configuration,
        _ => BaseStudioSectionKind.CustomSemantic,
    };

    private static BaseStudioWorkspaceKind Workspace(BaseStudioPageKind kind) => kind switch
    {
        BaseStudioPageKind.Overview => BaseStudioWorkspaceKind.Landing,
        BaseStudioPageKind.Collection or BaseStudioPageKind.ResourceList => BaseStudioWorkspaceKind.Landing,
        BaseStudioPageKind.ResourceDetail or BaseStudioPageKind.Action => BaseStudioWorkspaceKind.Detail,
        BaseStudioPageKind.Timeline => BaseStudioWorkspaceKind.Timeline,
        BaseStudioPageKind.QueryTool => BaseStudioWorkspaceKind.QueryTool,
        BaseStudioPageKind.Diagnostics => BaseStudioWorkspaceKind.Diagnostics,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static BaseStudioSha256 Digest(string value) => BaseStudioSha256.Compute(Encoding.UTF8.GetBytes(value));

    private sealed class GrantCatalog
    {
        private static readonly string[] Required =
        [
            "base.studio.action.discover", "base.studio.action.execute", "base.studio.action.preview",
            "base.studio.bootstrap.read", "base.studio.diagnostics.inspect", "base.studio.invalidation.subscribe",
            "base.studio.receipt.discover", "base.studio.receipt.inspect", "base.studio.resource.discover",
            "base.studio.resource.inspect", "base.studio.resource.links", "base.studio.resource.search",
        ];
        private readonly string _applicationId;
        private readonly IReadOnlyDictionary<string, HPDBaseStudioGrantAuthority> _registrations;

        private GrantCatalog(string applicationId, IReadOnlyDictionary<string, HPDBaseStudioGrantAuthority> registrations)
        { _applicationId = applicationId; _registrations = registrations; }

        internal static GrantCatalog Create(HPDBaseStudioAuthoritySnapshot authority)
        {
            Dictionary<string, HPDBaseStudioGrantAuthority> registrations = authority.Grants
                .Where(static grant => grant.Version == 1 && Required.Contains(grant.Id, StringComparer.Ordinal))
                .ToDictionary(static grant => grant.Id, StringComparer.Ordinal);
            if (Required.Any(id => !registrations.ContainsKey(id)))
                throw new InvalidOperationException("base.studio.requiredGrantMissing");
            return new(authority.ApplicationId, registrations);
        }

        internal BaseStudioGrantRequirement Module(string operation)
            => Create(operation, null, false);

        internal BaseStudioGrantRequirement[] All => Required.Select(Module).ToArray();

        internal BaseStudioGrantRequirement[] Resource(BaseStudioResourceKind resource)
            => [Create("base.studio.resource.discover", resource, false),
                Create("base.studio.resource.inspect", resource, false)];

        internal BaseStudioGrantRequirement[] Command(BaseStudioActionClass action)
            => action == BaseStudioActionClass.Routine
                ? [Create("base.studio.action.discover", null, false), Create("base.studio.action.execute", null, true)]
                : [Create("base.studio.action.discover", null, false), Create("base.studio.action.execute", null, true),
                   Create("base.studio.action.preview", null, true)];

        private BaseStudioGrantRequirement Create(string operation, BaseStudioResourceKind? resource, bool underlying)
        {
            HPDBaseStudioGrantAuthority registration = _registrations[operation];
            AccessGrant grant = registration.GetStaticGrant()
                ?? throw new InvalidOperationException("base.studio.requiredGrantDynamic");
            if (!StringComparer.Ordinal.Equals(registration.OwningModuleId, "base") ||
                !StringComparer.Ordinal.Equals(registration.SourceContractId, "base.studio.fixed-grant") ||
                registration.SourceContractVersion != 1 ||
                !StringComparer.Ordinal.Equals(grant.Id, registration.Id) ||
                !StringComparer.Ordinal.Equals(grant.Action, operation) ||
                grant.Audience != HPDBaseEndpointAudience.ControlPlane ||
                !StringComparer.Ordinal.Equals(grant.ApplicationId, _applicationId) ||
                !StringComparer.Ordinal.Equals(grant.ModuleId, registration.OwningModuleId) ||
                grant.Effect != GrantEffect.Allow || grant.Condition is not null || grant.WriteCondition is not null ||
                grant.ExpiresAt is not null || grant.Scope.Kind != ResourceScopeKind.Runtime ||
                grant.Scope.CollectionId is not null || grant.Scope.RecordId is not null || grant.Scope.FieldPath is not null ||
                grant.Scope.VectorIndexId is not null || grant.Scope.SubjectContractId is not null ||
                grant.Scope.SubjectContractVersion is not null || grant.Scope.TenantId is not null || grant.Scope.ProjectId is not null)
                throw new InvalidOperationException("base.studio.requiredGrantSemanticsInvalid");
            return BaseStudioGrantRequirement.Create(registration.Id, registration.Version,
                BaseStudioSha256.FromDigest(registration.GetChecksum()), operation, "control-plane",
                SubjectKind(grant.Subject.Kind), grant.ApplicationId, grant.ModuleId, resource,
                BaseStudioProtectedScopeRule.Application, underlying);
        }

        private static string SubjectKind(AccessSubjectKind kind) => kind switch
        {
            AccessSubjectKind.Anonymous => "anonymous", AccessSubjectKind.Authenticated => "authenticated",
            AccessSubjectKind.User => "user", AccessSubjectKind.Role => "role", AccessSubjectKind.Team => "team",
            AccessSubjectKind.TeamRole => "team-role", AccessSubjectKind.Tenant => "tenant",
            AccessSubjectKind.ServicePrincipal => "service-principal", AccessSubjectKind.Admin => "admin",
            AccessSubjectKind.System => "system", AccessSubjectKind.HostDefined => "host-defined",
            _ => throw new InvalidOperationException("base.studio.requiredGrantSubjectInvalid"),
        };
    }

    private readonly record struct SectionView(string Id, BaseStudioResourceKind Resource);

    private static readonly HashSet<string> ListSections = new(StringComparer.Ordinal)
    {
        "attention", "activity", "modules", "collections", "files", "resources", "operations", "relations", "indexes",
        "records", "filters", "paging", "references", "history", "receipts", "objects", "definitions", "executions",
        "activations", "schedules", "effects", "executors", "quarantine", "attempts", "children", "occurrences", "work",
        "dependencies", "heartbeats", "contracts", "subjects", "consumers", "barriers", "acknowledgements", "publications",
        "textIndexes", "vectorIndexes", "rebuilds", "policies", "grants", "explanations", "stores", "schemas", "backups",
        "maintenance", "incidents", "health", "affectedResources", "results"
    };

    private static readonly ImmutableArray<PageSpec> Pages =
    [
        P("base.overview", BaseStudioArea.Overview, "", BaseStudioPageKind.Overview, true, BaseStudioResourceKind.Application, "summary", "attention", "activity"),
        P("base.data", BaseStudioArea.Data, "data", BaseStudioPageKind.Overview, true, BaseStudioResourceKind.Application, "summary", "modules", "collections", "files"),
        P("base.module.detail", BaseStudioArea.Data, "data/modules/:resource", BaseStudioPageKind.ResourceDetail, false, BaseStudioResourceKind.Module, "summary", "resources", "operations", "health"),
        P("base.collection.detail", BaseStudioArea.Data, "data/collections/:resource", BaseStudioPageKind.ResourceDetail, false, BaseStudioResourceKind.Collection, "summary", "schema", "relations", "indexes", "operations", "history"),
        P("base.collection.records", BaseStudioArea.Data, "data/collections/:resource/records", BaseStudioPageKind.Collection, false, BaseStudioResourceKind.Collection, "records", "filters", "paging"),
        P("base.record.detail", BaseStudioArea.Data, "data/records/:resource", BaseStudioPageKind.ResourceDetail, false, BaseStudioResourceKind.Record, "summary", "fields", "relations", "references", "history", "receipts", "search", "evidence"),
        P("base.fileBucket.detail", BaseStudioArea.Data, "data/file-buckets/:resource", BaseStudioPageKind.ResourceDetail, false, BaseStudioResourceKind.FileBucket, "summary", "objects", "classification", "retention", "history", "actions"),
        P("base.file.detail", BaseStudioArea.Data, "data/files/:resource", BaseStudioPageKind.ResourceDetail, false, BaseStudioResourceKind.File, "summary", "metadata", "relations", "history"),
        P("base.operations", BaseStudioArea.Operations, "operations", BaseStudioPageKind.Overview, true, BaseStudioResourceKind.OperationExecution, "definitions", "executions", "receipts"),
        PM("base.operation.definition", BaseStudioArea.Operations, "operations/definitions/:resource", BaseStudioPageKind.ResourceDetail,
            [BaseStudioResourceKind.RegisteredRead, BaseStudioResourceKind.SelectionOperation, BaseStudioResourceKind.ModuleMutation],
            "summary", "contract", "resources", "authorization", "limits", "executions"),
        P("base.operation.execution", BaseStudioArea.Operations, "operations/executions/:resource", BaseStudioPageKind.Timeline, false, BaseStudioResourceKind.OperationExecution, "outcome", "facts", "result", "decisions", "accounting", "timeline"),
        P("base.receipt.detail", BaseStudioArea.Operations, "operations/receipts/:resource", BaseStudioPageKind.Timeline, false, BaseStudioResourceKind.Receipt, "outcome", "facts", "result", "disclosure", "accounting", "timeline"),
        P("base.automation", BaseStudioArea.Automations, "automations", BaseStudioPageKind.Overview, true, BaseStudioResourceKind.Activation, "attention", "activations", "schedules", "effects", "executors", "quarantine"),
        P("base.semanticActivations", BaseStudioArea.Automations, "automations/semantic-activations", BaseStudioPageKind.ResourceList, false, BaseStudioResourceKind.Application, "definitions"),
        P("base.activation.detail", BaseStudioArea.Automations, "automations/activations/:resource", BaseStudioPageKind.Timeline, false, BaseStudioResourceKind.Activation, "summary", "inputResult", "occurrence", "attempts", "claim", "children", "effect", "receipts", "timeline", "evidence"),
        P("base.schedule.detail", BaseStudioArea.Automations, "automations/schedules/:resource", BaseStudioPageKind.ResourceDetail, false, BaseStudioResourceKind.Schedule, "summary", "recurrence", "timeAuthority", "misfireOverlap", "occurrences", "work", "history", "dependencies"),
        P("base.occurrence.detail", BaseStudioArea.Automations, "automations/occurrences/:resource", BaseStudioPageKind.Timeline, false, BaseStudioResourceKind.Occurrence, "summary", "disposition", "activation", "timeline", "evidence"),
        P("base.effect.detail", BaseStudioArea.Automations, "automations/effects/:resource", BaseStudioPageKind.Timeline, false, BaseStudioResourceKind.Effect, "summary", "executor", "outcome", "reconciliation", "timeline", "evidence"),
        P("base.executor.detail", BaseStudioArea.Automations, "automations/executors/:resource", BaseStudioPageKind.ResourceDetail, false, BaseStudioResourceKind.Executor, "summary", "incarnation", "heartbeats", "definitions", "effects", "history", "evidence"),
        P("base.subjects", BaseStudioArea.Subjects, "subjects", BaseStudioPageKind.Overview, true, BaseStudioResourceKind.SubjectContract, "contracts", "subjects", "consumers", "barriers"),
        P("base.subjectContract.detail", BaseStudioArea.Subjects, "subjects/contracts/:resource", BaseStudioPageKind.ResourceDetail, false, BaseStudioResourceKind.SubjectContract, "summary", "references", "consumers", "lifecycle", "retirement"),
        P("base.subject.detail", BaseStudioArea.Subjects, "subjects/instances/:resource", BaseStudioPageKind.Timeline, false, BaseStudioResourceKind.Subject, "summary", "references", "lifecycle", "delivery", "retirement", "acknowledgements", "purge", "timeline"),
        P("base.lifecycleConsumer.detail", BaseStudioArea.Subjects, "subjects/consumers/:resource", BaseStudioPageKind.ResourceDetail, false, BaseStudioResourceKind.LifecycleConsumer, "summary", "contractScope", "delivery", "checkpoint", "lagOvertake", "reconciliation", "history"),
        P("base.retirementBarrier.detail", BaseStudioArea.Subjects, "subjects/barriers/:resource", BaseStudioPageKind.Timeline, false, BaseStudioResourceKind.RetirementBarrier, "summary", "consumers", "evidence", "dispositions", "deadline", "override", "purge", "publications"),
        P("base.search", BaseStudioArea.Search, "search", BaseStudioPageKind.Overview, true, BaseStudioResourceKind.Application, "textIndexes", "vectorIndexes", "rebuilds", "attention"),
        P("base.textIndex.detail", BaseStudioArea.Search, "search/text/:resource", BaseStudioPageKind.ResourceDetail, false, BaseStudioResourceKind.TextIndex, "summary", "fieldsAnalyzer", "policyInfluence", "scoring", "freshness", "generations", "rebuild", "certification", "diagnostics"),
        P("base.vectorIndex.detail", BaseStudioArea.Search, "search/vector/:resource", BaseStudioPageKind.ResourceDetail, false, BaseStudioResourceKind.VectorIndex, "summary", "fieldsEmbedding", "policyInfluence", "distance", "freshness", "generations", "rebuild", "certification", "diagnostics"),
        PM("base.search.query", BaseStudioArea.Search, "search/query/:resource", BaseStudioPageKind.QueryTool, [BaseStudioResourceKind.TextIndex, BaseStudioResourceKind.VectorIndex], "query", "results", "explanation", "evidence"),
        P("base.rebuild.detail", BaseStudioArea.Search, "search/rebuilds/:resource", BaseStudioPageKind.Timeline, false, BaseStudioResourceKind.SearchRebuild, "summary", "sourceCoverage", "phases", "probe", "generations", "timeline"),
        P("base.security", BaseStudioArea.Security, "security", BaseStudioPageKind.Overview, true, BaseStudioResourceKind.Policy, "policies", "grants", "explanations", "disclosure"),
        P("base.policy.detail", BaseStudioArea.Security, "security/policies/:resource", BaseStudioPageKind.ResourceDetail, false, BaseStudioResourceKind.Policy, "summary", "composition", "constraints", "masks", "obligations", "history"),
        P("base.grant.detail", BaseStudioArea.Security, "security/grants/:resource", BaseStudioPageKind.ResourceDetail, false, BaseStudioResourceKind.Grant, "summary", "scope", "operations", "conditions", "history"),
        P("base.policy.explain", BaseStudioArea.Security, "security/explanations/:resource", BaseStudioPageKind.Diagnostics, false, BaseStudioResourceKind.Policy, "operation", "resource", "filters", "constraints", "masks", "disclosure", "decision"),
        P("base.infrastructure", BaseStudioArea.Infrastructure, "infrastructure", BaseStudioPageKind.Overview, true, BaseStudioResourceKind.Store, "stores", "schemas", "backups", "maintenance", "attention"),
        P("base.store.detail", BaseStudioArea.Infrastructure, "infrastructure/stores/:resource", BaseStudioPageKind.ResourceDetail, false, BaseStudioResourceKind.Store, "summary", "capabilities", "certification", "assets", "health", "retainedWork", "quarantine", "maintenance", "recovery", "diagnostics"),
        P("base.provider.detail", BaseStudioArea.Infrastructure, "infrastructure/providers/:resource", BaseStudioPageKind.ResourceDetail, false, BaseStudioResourceKind.Provider, "summary", "capability", "certification", "health", "diagnostics"),
        P("base.schema.detail", BaseStudioArea.Infrastructure, "infrastructure/schemas/:resource", BaseStudioPageKind.ResourceDetail, false, BaseStudioResourceKind.Schema, "summary", "graph", "drift", "plans", "history", "evidence"),
        P("base.migration.detail", BaseStudioArea.Infrastructure, "infrastructure/migrations/:resource", BaseStudioPageKind.Timeline, false, BaseStudioResourceKind.Migration, "summary", "plan", "compatibility", "progress", "history"),
        P("base.backup.detail", BaseStudioArea.Infrastructure, "infrastructure/backups/:resource", BaseStudioPageKind.ResourceDetail, false, BaseStudioResourceKind.Backup, "summary", "authentication", "contents", "compatibility", "history"),
        P("base.restore.detail", BaseStudioArea.Infrastructure, "infrastructure/restores/:resource", BaseStudioPageKind.Timeline, false, BaseStudioResourceKind.Restore, "summary", "artifact", "consequences", "progress", "newAuthority", "reconciliation"),
        PM("base.maintenance.detail", BaseStudioArea.Infrastructure, "infrastructure/maintenance/:resource", BaseStudioPageKind.Timeline,
            [BaseStudioResourceKind.Maintenance, BaseStudioResourceKind.QuarantineItem], "summary", "scope", "progress", "retainedWork", "history", "evidence"),
        P("base.diagnostics", BaseStudioArea.Diagnostics, "diagnostics", BaseStudioPageKind.Diagnostics, true, BaseStudioResourceKind.Diagnostic, "incidents", "health", "accounting"),
        P("base.health.detail", BaseStudioArea.Diagnostics, "diagnostics/health/:resource", BaseStudioPageKind.Diagnostics, false, BaseStudioResourceKind.Health, "summary", "dependencies", "history", "remediation"),
        P("base.diagnostic.detail", BaseStudioArea.Diagnostics, "diagnostics/events/:resource", BaseStudioPageKind.Diagnostics, false, BaseStudioResourceKind.Diagnostic, "summary", "correlation", "affectedResources", "accounting", "evidence"),
    ];

    private static readonly ImmutableArray<CommandSpec> Commands =
    [
        C("administration.purge", "base.store.detail", BaseStudioActionClass.Destructive), C("backup.create", "base.store.detail", BaseStudioActionClass.Maintenance),
        C("backup.restore", "base.backup.detail", BaseStudioActionClass.DisasterOrRecoveryDomain), C("backup.validate", "base.backup.detail", BaseStudioActionClass.OperationalTransition),
        C("file.delete", "base.file.detail", BaseStudioActionClass.Destructive), C("file.upload", "base.fileBucket.detail", BaseStudioActionClass.Routine),
        C("moduleMutation.execute", "base.operation.definition", BaseStudioActionClass.Routine), C("record.create", "base.collection.detail", BaseStudioActionClass.Routine),
        C("record.delete", "base.record.detail", BaseStudioActionClass.Destructive), C("record.patch", "base.record.detail", BaseStudioActionClass.Routine),
        C("record.replace", "base.record.detail", BaseStudioActionClass.Routine), C("record.upsert", "base.record.detail", BaseStudioActionClass.Routine),
        C("registeredSelection.execute", "base.collection.records", BaseStudioActionClass.OperationalTransition), C("schema.apply", "base.schema.detail", BaseStudioActionClass.DisasterOrRecoveryDomain),
        C("retirement.consumer.remove", "base.lifecycleConsumer.detail", BaseStudioActionClass.Destructive), C("retirement.override", "base.retirementBarrier.detail", BaseStudioActionClass.Destructive),
        C("retirement.purge", "base.retirementBarrier.detail", BaseStudioActionClass.Destructive), C("retirement.timeout", "base.retirementBarrier.detail", BaseStudioActionClass.OperationalTransition),
        C("schema.plan", "base.schema.detail", BaseStudioActionClass.OperationalTransition), C("schema.verify", "base.schema.detail", BaseStudioActionClass.OperationalTransition),
        C("textIndex.rebuild", "base.textIndex.detail", BaseStudioActionClass.Maintenance), C("vectorIndex.rebuild", "base.vectorIndex.detail", BaseStudioActionClass.Maintenance),
    ];

    private static PageSpec P(string id, BaseStudioArea area, string route, BaseStudioPageKind kind, bool landing,
        BaseStudioResourceKind resource, params string[] sections) => new(id, area, route, kind, landing, resource,
            landing && resource != BaseStudioResourceKind.Application
                ? [BaseStudioResourceKind.Application, resource]
                : [resource], [.. sections]);
    private static PageSpec PM(string id, BaseStudioArea area, string route, BaseStudioPageKind kind,
        ImmutableArray<BaseStudioResourceKind> resources, params string[] sections)
        => new(id, area, route, kind, false, resources[0], resources, [.. sections]);
    private static CommandSpec C(string id, string page, BaseStudioActionClass action) => new(id, page, action);

    private sealed record PageSpec(string Id, BaseStudioArea Area, string Route, BaseStudioPageKind Kind, bool Landing,
        BaseStudioResourceKind PrimaryResource, ImmutableArray<BaseStudioResourceKind> Resources, ImmutableArray<string> Sections);
    private sealed record CommandSpec(string Id, string PageId, BaseStudioActionClass Action);
}
