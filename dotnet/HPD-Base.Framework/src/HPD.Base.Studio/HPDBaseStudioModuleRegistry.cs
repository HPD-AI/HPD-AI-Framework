using System.Collections.Immutable;
using System.Text;
using HPD.AI.Platform.Studio;

namespace HPD.Base.Studio;

/// <summary>Owns the fixed version-one BASE Studio module registry.</summary>
public static class HPDBaseStudioModuleRegistry
{
    private const string ClientId = "base.control-plane";
    private const string InspectEndpoint = "base.studio.resource.inspect";
    private static readonly StudioSha256 RuntimeAbi = Digest("base.studio.l41-runtime-abi.v1");
    private static readonly StudioSha256 ClientContract = Digest("base.studio.base-client-contract.v1");
    private static readonly StudioSha256 ClientOperations = Digest("base.studio.base-client-operations.v1");
    private static readonly StudioSha256 ComponentAbi = Digest("base.studio.page-component-abi.v1");
    internal const string AutomationFactDescriptor = "{\"kind\":\"object\",\"properties\":[{\"name\":\"factChecksum\",\"wireName\":\"factChecksum\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"identity\",\"wireName\":\"identity\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"kind\",\"wireName\":\"kind\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"resourceToken\",\"wireName\":\"resourceToken\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"safeState\",\"wireName\":\"safeState\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}";
    internal const string SubjectFactDescriptor = "{\"kind\":\"object\",\"properties\":[{\"name\":\"contractId\",\"wireName\":\"contractId\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"contractVersion\",\"wireName\":\"contractVersion\",\"typeId\":\"base.studio.positive-number\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"factChecksum\",\"wireName\":\"factChecksum\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"identity\",\"wireName\":\"identity\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"resourceToken\",\"wireName\":\"resourceToken\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"safeState\",\"wireName\":\"safeState\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}";

    /// <summary>Creates BASE's authorization-neutral static edition asset contribution.</summary>
    public static StudioEditionModuleAssetContribution CreateEditionAssetContribution()
    {
        StudioFrontendExport frontend = CreateFrontend(Pages.Select(static value => value.Id));
        return StudioEditionModuleAssetContribution.Create("base", 1, frontend.FrontendAbiChecksum, CreateAsset());
    }

    /// <summary>Creates BASE's complete immutable Studio contribution from finalized BASE graph authority.</summary>
    public static StudioModuleRegistration Create(HPDBaseStudioAuthoritySnapshot authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        string applicationId = authority.ApplicationId;
        GrantCatalog grants = GrantCatalog.Create(authority);
        ImmutableArray<PageSpec> specs = Pages;
        ImmutableArray<CommandSpec> commandSpecs = [.. Commands.Where(command => authority.HasOperation(command.Id))];
        var commandsByPage = commandSpecs.GroupBy(static value => value.PageId, StringComparer.Ordinal)
            .ToDictionary(static value => value.Key, static value => value.OrderBy(item => item.Id, StringComparer.Ordinal).ToImmutableArray(), StringComparer.Ordinal);

        var views = ImmutableArray.CreateBuilder<StudioViewRegistration>();
        var pages = ImmutableArray.CreateBuilder<StudioPageRegistration>();
        foreach (PageSpec spec in specs.OrderBy(static value => value.Id, StringComparer.Ordinal))
        {
            ImmutableArray<CommandSpec> ownedCommands = commandsByPage.GetValueOrDefault(spec.Id, []);
            var sections = ImmutableArray.CreateBuilder<StudioSectionRegistration>(spec.Sections.Length);
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
                        spec.Landing ? StudioResourceKind.Application : spec.PrimaryResource));
                }
                sections.Add(StudioSectionRegistration.Create(
                    section, $"studio.section.{section}", index, SectionKind(section),
                    sectionViews.Select(static value => value.Id).OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
                    sectionCommands.OrderBy(static value => value, StringComparer.Ordinal).ToArray()));
            }

            StudioPagePresentationRegistration presentation = StudioPagePresentationRegistration.Create(
                spec.Id, 1, spec.Landing ? StudioNavigationRole.AreaLanding : StudioNavigationRole.Contextual,
                Workspace(spec.Kind), sections, null, null,
                spec.Id is "base.schema.detail" ? StudioDraftRetentionClass.CurrentDocumentNavigation : StudioDraftRetentionClass.None);
            pages.Add(StudioPageRegistration.Create(
                spec.Id, 1, spec.Area, $"studio.page.{spec.Id}", Route(spec), spec.Kind, presentation,
                spec.Resources.OrderBy(static value => (byte)value), [InspectEndpoint],
                grants.Resource(spec.PrimaryResource), StudioDisclosureClass.ProtectedValue));
        }

        StudioPageRegistration[] orderedPages = pages.OrderBy(static value => value.PageId, StringComparer.Ordinal).ToArray();
        StudioViewRegistration[] orderedViews = views.OrderBy(static value => value.ViewId, StringComparer.Ordinal).ToArray();
        StudioCommandRegistration[] commands = commandSpecs.OrderBy(static value => value.Id, StringComparer.Ordinal)
            .Select(command => CreateCommand(command, grants)).ToArray();
        StudioResourceRegistration[] resources = Enum.GetValues<StudioResourceKind>()
            .Where(static value => value < StudioResourceKind.GraphDefinition)
            .Select(value => StudioResourceRegistration.Create(
                value, ResourceResolverId(value), [InspectEndpoint], grants.Resource(value),
                StudioDisclosureClass.ProtectedValue)).ToArray();
        StudioFrontendExport frontend = CreateFrontend(orderedPages.Select(static value => value.PageId));

        return StudioModuleRegistration.CreateBase(
            applicationId, CreateAsset(), frontend, orderedPages, orderedViews, resources, commands, CreateLinks(),
            [StudioFrameworkClientRegistration.Create(ClientId, 1, StudioContractNecessity.Required,
                StudioFrameworkClientProtocol.BaseL41DynamicMap, RuntimeAbi, ClientContract, ClientOperations,
                "base.studio.runtime", StudioFrameworkClientTransportClass.SameOriginShellAuthenticated,
                orderedPages.Select(static page => page.PageId),
                StudioFrameworkClientLimits.Create(512, 16_777_216, 16_777_216, 16,
                    TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)))],
            [grants.Module("base.studio.bootstrap.read")],
            StudioModuleLimits.Create(64, 256, 128, 256, 256, 32));
    }

    private static StudioAssetManifest CreateAsset()
    {
        const string assetPath = "base/6c6d65420bde4c64faaaea95ec0a74b36c719d66c0edd2eec1aa7d7f461873e5.js";
        using Stream stream = typeof(HPDBaseStudioModuleRegistry).Assembly.GetManifestResourceStream("HPD.Base.Studio.Assets.base.js")
            ?? throw new InvalidOperationException("The prebuilt BASE Studio module asset is absent.");
        using var content = new MemoryStream();
        stream.CopyTo(content);
        return StudioAssetManifest.Create(assetPath, StudioModuleNecessity.Required,
            StudioShellContract.Current,
            [StudioAssetSource.Create(assetPath, StudioAssetMediaType.JavaScriptModule, content.GetBuffer().AsSpan(0, checked((int)content.Length)))]);
    }

    private static StudioFrontendExport CreateFrontend(IEnumerable<string> pageIds)
    {
        string[] pages = pageIds.Order(StringComparer.Ordinal).ToArray();
        StudioFrameworkClientLimits limits = StudioFrameworkClientLimits.Create(512, 16_777_216, 16_777_216, 16,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));
        return StudioFrontendExport.Create("base", 1,
            [StudioFrontendClientSlot.Create(ClientId, 1, StudioFrameworkClientProtocol.BaseL41DynamicMap,
                RuntimeAbi, ClientContract, ClientOperations, "base.studio.runtime",
                StudioFrameworkClientTransportClass.SameOriginShellAuthenticated, pages, limits)],
            pages.Select(static value => StudioPageComponentBinding.Create(value, $"component.{value}", ComponentAbi)));
    }

    private static StudioViewRegistration CreateView(string id, StudioResourceKind kind, StudioResourceKind requestKind)
    {
        string typePrefix = id.ToLowerInvariant();
        StudioNamedTypeContract request = StudioNamedTypeContract.Create(typePrefix + ".request",
            System.Text.Encoding.UTF8.GetBytes(ViewRequestDescriptor(id, requestKind)));
        StudioNamedTypeContract item = id is "base.collection.detail.history.list" or "base.record.detail.history.list"
            ? StudioNamedTypeContract.Create("base.studio.evidence.record-mutation.item",
                "{\"kind\":\"object\",\"properties\":[{\"name\":\"collectionId\",\"wireName\":\"collectionId\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"evidenceChecksum\",\"wireName\":\"evidenceChecksum\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"evidenceId\",\"wireName\":\"evidenceId\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"observedAtUtc\",\"wireName\":\"observedAtUtc\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"recordId\",\"wireName\":\"recordId\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"semanticKind\",\"wireName\":\"semanticKind\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}"u8)
            : id.StartsWith("base.search.query.", StringComparison.Ordinal)
            ? StudioNamedTypeContract.Create(SearchQueryItemTypeId(id), Encoding.UTF8.GetBytes(SearchQueryItemDescriptor(id)))
            : id.StartsWith("base.overview.", StringComparison.Ordinal)
            ? StudioNamedTypeContract.Create("base.studio.overview.value",
                "{\"kind\":\"object\",\"properties\":[{\"name\":\"applicationId\",\"wireName\":\"applicationId\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"contractVersion\",\"wireName\":\"contractVersion\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"diagnosticCount\",\"wireName\":\"diagnosticCount\",\"typeId\":\"base.studio.nonnegative-long\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"refreshedAtUtc\",\"wireName\":\"refreshedAtUtc\",\"typeId\":\"base.studio.optional-text\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"},{\"name\":\"runtimeId\",\"wireName\":\"runtimeId\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"viewId\",\"wireName\":\"viewId\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}"u8)
            : id == "base.semanticActivations.definitions.list"
            ? StudioNamedTypeContract.Create("base.studio.semantic-definition-inspection.item",
                "{\"kind\":\"object\",\"properties\":[{\"name\":\"capturedAuthorityGeneration\",\"wireName\":\"capturedAuthorityGeneration\",\"typeId\":\"base.studio.nonnegative-long\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"},{\"name\":\"compactedCount\",\"wireName\":\"compactedCount\",\"typeId\":\"base.studio.nonnegative-long\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"definitionChecksum\",\"wireName\":\"definitionChecksum\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"hasMore\",\"wireName\":\"hasMore\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"id\",\"wireName\":\"id\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"inspectionState\",\"wireName\":\"inspectionState\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"kind\",\"wireName\":\"kind\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"liveCount\",\"wireName\":\"liveCount\",\"typeId\":\"base.studio.nonnegative-long\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"owningModuleId\",\"wireName\":\"owningModuleId\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"pageChecksum\",\"wireName\":\"pageChecksum\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"},{\"name\":\"retiredCount\",\"wireName\":\"retiredCount\",\"typeId\":\"base.studio.nonnegative-long\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"storeId\",\"wireName\":\"storeId\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"version\",\"wireName\":\"version\",\"typeId\":\"base.studio.nonnegative-long\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}"u8)
            : id.StartsWith("base.operations.definitions.", StringComparison.Ordinal)
            ? StudioNamedTypeContract.Create("base.studio.installed-definition.item",
                "{\"kind\":\"object\",\"properties\":[{\"name\":\"definitionChecksum\",\"wireName\":\"definitionChecksum\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"id\",\"wireName\":\"id\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"kind\",\"wireName\":\"kind\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"owningModuleId\",\"wireName\":\"owningModuleId\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"version\",\"wireName\":\"version\",\"typeId\":\"base.studio.nonnegative-long\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}"u8)
            : id is "base.operations.executions.list" or "base.operations.receipts.list"
            ? StudioNamedTypeContract.Create("base.studio.atomic-execution.item",
                "{\"kind\":\"object\",\"properties\":[{\"name\":\"expiresAtUtc\",\"wireName\":\"expiresAtUtc\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"identity\",\"wireName\":\"identity\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"requestFingerprint\",\"wireName\":\"requestFingerprint\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"resultKind\",\"wireName\":\"resultKind\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"structuralDigest\",\"wireName\":\"structuralDigest\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}"u8)
            : id.StartsWith("base.automation", StringComparison.Ordinal) || id.StartsWith("base.activation.detail.", StringComparison.Ordinal) ||
              id.StartsWith("base.schedule.detail.", StringComparison.Ordinal) || id.StartsWith("base.occurrence.detail.", StringComparison.Ordinal) ||
              id.StartsWith("base.effect.detail.", StringComparison.Ordinal) || id.StartsWith("base.executor.detail.", StringComparison.Ordinal)
            ? StudioNamedTypeContract.Create(typePrefix + ".item",
                System.Text.Encoding.UTF8.GetBytes(AutomationItemDescriptor(kind)))
            : id.StartsWith("base.subjects.", StringComparison.Ordinal) || id.StartsWith("base.subjectContract.detail.", StringComparison.Ordinal) ||
              id.StartsWith("base.subject.detail.", StringComparison.Ordinal) || id.StartsWith("base.lifecycleConsumer.detail.", StringComparison.Ordinal) ||
              id.StartsWith("base.retirementBarrier.detail.", StringComparison.Ordinal)
            ? StudioNamedTypeContract.Create(typePrefix + ".item", System.Text.Encoding.UTF8.GetBytes(SubjectItemDescriptor(kind)))
            : HPDBaseStudioSecurityContracts.IsRegistrationView(id)
            ? StudioNamedTypeContract.Create(typePrefix + ".item",
                Encoding.UTF8.GetBytes(HPDBaseStudioSecurityContracts.ItemDescriptor(id)))
            : HPDBaseStudioInfrastructureContracts.IsInventoryView(id)
            ? StudioNamedTypeContract.Create(typePrefix + ".item",
                Encoding.UTF8.GetBytes(HPDBaseStudioInfrastructureContracts.ItemDescriptor(id)))
            : HPDBaseStudioDiagnosticsContracts.IsDiagnosticsView(id)
            ? StudioNamedTypeContract.Create(typePrefix + ".item",
                Encoding.UTF8.GetBytes(HPDBaseStudioDiagnosticsContracts.ItemDescriptor(id)))
            : StudioNamedTypeContract.Create(typePrefix + ".item",
                "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":256,\"format\":\"studio-resource-summary\"}"u8);
        StudioNamedTypeContract filter = StudioNamedTypeContract.Create(typePrefix + ".filter.request",
            "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":128,\"format\":\"nfc-search\"}"u8);
        StudioNamedTypeContract disclosure = StudioNamedTypeContract.Create(typePrefix + ".disclosure",
            "{\"kind\":\"enum\",\"values\":[\"authorizedMetadata\",\"protectedValue\"]}"u8);
        bool list = id.EndsWith(".list", StringComparison.Ordinal);
        StudioSortRegistration identitySort = StudioSortRegistration.Create(id + ".sort.identity",
            [StudioOrderMember.Create("identity", StudioOrderDirection.Ascending,
                StudioNullPlacement.ValuesThenMissingThenNull)]);
        StudioViewPresentationRegistration presentation = StudioViewPresentationRegistration.Create(
            id, null, null, StudioEmptyStateKind.NoItems,
            StudioActivityPolicy.Create(StudioActivityPolicyKind.GovernedInvalidationRefresh, 10, 3, 32),
            StudioPreferenceSchema.Create(id + ".preferences", 1,
                [StudioPreferenceKind.Theme, StudioPreferenceKind.Density, StudioPreferenceKind.PreferredTab],
                4_096, TimeSpan.FromDays(180)));
        return StudioViewRegistration.Create(id, 1, Producer(kind), request.TypeId, request.NodeChecksum,
            kind, item.TypeId, item.NodeChecksum, id + ".cursor",
            [StudioOrderMember.Create("identity", StudioOrderDirection.Ascending,
                StudioNullPlacement.ValuesThenMissingThenNull)],
            list ? [StudioFilterRegistration.Create(id + ".filter.safeLabel", filter.TypeId, filter.NodeChecksum)] : [],
            [identitySort], disclosure.NodeChecksum,
            1_048_576, 500, presentation);
    }

    internal static string AutomationItemDescriptor(StudioResourceKind kind) => kind switch
    {
        StudioResourceKind.Activation or StudioResourceKind.ActivationAttempt => FactDescriptor("attemptNumber", "definitionId", "definitionVersion", "state"),
        StudioResourceKind.Schedule => FactDescriptor("enabled", "scheduleId", "version"),
        StudioResourceKind.Occurrence => FactDescriptor("activationId", "disposition", "occurrenceId", "scheduleId"),
        StudioResourceKind.Effect => FactDescriptor("activationId", "attemptNumber", "effectId"),
        StudioResourceKind.Executor => FactDescriptor("executorGeneration", "hostId", "processIncarnationId", "state"),
        StudioResourceKind.Receipt => FactDescriptor("subjectIdentity", "transitionKind"),
        StudioResourceKind.QuarantineItem => FactDescriptor("quarantineKind", "subjectIdentity"),
        _ => FactDescriptor("authorityState"),
    };

    internal static string SubjectItemDescriptor(StudioResourceKind kind) => kind switch
    {
        StudioResourceKind.SubjectContract => FactDescriptor("authorityEpoch", "contractId", "contractVersion", "publicationKind", "publicationPosition", "restoreEpoch", "stateGeneration"),
        StudioResourceKind.Subject => FactDescriptor("contractId", "contractVersion", "createdJournalPosition", "incarnation", "protectedSubjectIdentity"),
        StudioResourceKind.LifecycleConsumer => FactDescriptor("consumerChecksum", "consumerId", "consumerVersion", "contractId", "contractVersion", "deliveryEpoch", "projectionGeneration", "publishedGraphGeneration"),
        StudioResourceKind.LifecycleCheckpoint => FactDescriptor("checkpointGeneration", "consumerId", "consumerVersion", "throughBoundary"),
        StudioResourceKind.RetirementBarrier => FactDescriptor("barrierGeneration", "barrierState", "deadlineUtc", "requiredConsumerSetChecksum", "tombstoneSequence"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string FactDescriptor(params string[] semanticFields)
    {
        string[] fields = [.. new[] { "factChecksum", "identity", "resourceToken" }.Concat(semanticFields).Order(StringComparer.Ordinal)];
        return $"{{\"kind\":\"object\",\"properties\":[{string.Join(',', fields.Select(static name => $"{{\"name\":\"{name}\",\"wireName\":\"{name}\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}}"))}],\"additionalProperties\":false}}";
    }

    private static string ViewRequestDescriptor(string viewId, StudioResourceKind kind)
    {
        string typeId = kind switch
        {
            StudioResourceKind.Application => "base.studio.resource.application",
            StudioResourceKind.Module => "base.studio.resource.module",
            StudioResourceKind.Collection => "base.studio.resource.collection",
            StudioResourceKind.Record => "base.studio.resource.record",
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

    private static string Producer(StudioResourceKind kind) => kind switch
    {
        StudioResourceKind.Application or StudioResourceKind.Module or StudioResourceKind.RegisteredRead or
            StudioResourceKind.SelectionOperation or StudioResourceKind.ModuleMutation => "base.studio.graph",
        StudioResourceKind.Collection or StudioResourceKind.Record or StudioResourceKind.Relation => "base.studio.records",
        StudioResourceKind.FileBucket or StudioResourceKind.File => "base.studio.files",
        StudioResourceKind.OperationExecution or StudioResourceKind.Receipt => "base.studio.evidence",
        StudioResourceKind.ActivationDefinition or StudioResourceKind.Activation or StudioResourceKind.Schedule or
            StudioResourceKind.Occurrence or StudioResourceKind.ActivationAttempt or StudioResourceKind.Effect or
            StudioResourceKind.Executor => "base.studio.activations",
        StudioResourceKind.SubjectContract or StudioResourceKind.Subject or StudioResourceKind.LifecycleConsumer or
            StudioResourceKind.LifecycleCheckpoint => "base.studio.lifecycle",
        StudioResourceKind.RetirementBarrier => "base.studio.retirement",
        StudioResourceKind.TextIndex or StudioResourceKind.VectorIndex or StudioResourceKind.SearchRebuild => "base.studio.search",
        StudioResourceKind.Policy or StudioResourceKind.Grant => "base.studio.policy",
        StudioResourceKind.Store or StudioResourceKind.Provider or StudioResourceKind.CertificationReceipt or
            StudioResourceKind.Schema or StudioResourceKind.Migration or StudioResourceKind.Backup or
            StudioResourceKind.Restore or StudioResourceKind.Maintenance or StudioResourceKind.QuarantineItem => "base.studio.administration",
        StudioResourceKind.Health or StudioResourceKind.Diagnostic => "base.studio.diagnostics",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static StudioCommandRegistration CreateCommand(CommandSpec spec, GrantCatalog grants)
    {
        string typePrefix = spec.Id.ToLowerInvariant();
        StudioNamedTypeContract input = StudioNamedTypeContract.Create(typePrefix + ".input",
            Encoding.UTF8.GetBytes(spec.Id is "textIndex.rebuild" or "vectorIndex.rebuild" ? RebuildInputDescriptor(spec.Id)
                : spec.Id.StartsWith("retirement.", StringComparison.Ordinal) ? RetirementInputDescriptor(spec.Id)
                : "{\"kind\":\"object\",\"properties\":[],\"additionalProperties\":false}"));
        StudioNamedTypeContract result = StudioNamedTypeContract.Create(typePrefix + ".result",
            Encoding.UTF8.GetBytes(spec.Id is "textIndex.rebuild" or "vectorIndex.rebuild" ? RebuildResultDescriptor
                : spec.Id.StartsWith("retirement.", StringComparison.Ordinal) ? RetirementResultDescriptor
                : "{\"kind\":\"object\",\"properties\":[],\"additionalProperties\":false}"));
        StudioFreshAuthenticationClass? fresh = spec.Action >= StudioActionClass.Destructive
            ? StudioFreshAuthenticationClass.MultiFactor : null;
        StudioCommandAcknowledgementRequirement[] acknowledgements = spec.Action >= StudioActionClass.OperationalTransition
            ? [StudioCommandAcknowledgementRequirement.Create("confirm." + spec.Id, "impact." + spec.Id)] : [];
        return StudioCommandRegistration.Create(spec.Id, 1, spec.Id, input.TypeId, input.NodeChecksum,
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

    private static StudioLinkRegistration[] CreateLinks()
    {
        (StudioResourceKind Source, StudioResourceKind Target, StudioLinkRelation Relation)[] definitions =
        [
            (StudioResourceKind.Application, StudioResourceKind.Module, StudioLinkRelation.Owns),
            (StudioResourceKind.Application, StudioResourceKind.Collection, StudioLinkRelation.Owns),
            (StudioResourceKind.Application, StudioResourceKind.Store, StudioLinkRelation.StoredBy),
            (StudioResourceKind.Module, StudioResourceKind.RegisteredRead, StudioLinkRelation.Owns),
            (StudioResourceKind.Module, StudioResourceKind.SelectionOperation, StudioLinkRelation.Owns),
            (StudioResourceKind.Module, StudioResourceKind.ModuleMutation, StudioLinkRelation.Owns),
            (StudioResourceKind.Collection, StudioResourceKind.Record, StudioLinkRelation.Owns),
            (StudioResourceKind.Collection, StudioResourceKind.Relation, StudioLinkRelation.Owns),
            (StudioResourceKind.Collection, StudioResourceKind.FileBucket, StudioLinkRelation.Owns),
            (StudioResourceKind.Collection, StudioResourceKind.TextIndex, StudioLinkRelation.Owns),
            (StudioResourceKind.Collection, StudioResourceKind.VectorIndex, StudioLinkRelation.Owns),
            (StudioResourceKind.FileBucket, StudioResourceKind.File, StudioLinkRelation.Owns),
            (StudioResourceKind.TextIndex, StudioResourceKind.SearchRebuild, StudioLinkRelation.Owns),
            (StudioResourceKind.VectorIndex, StudioResourceKind.SearchRebuild, StudioLinkRelation.Owns),
            (StudioResourceKind.SubjectContract, StudioResourceKind.Subject, StudioLinkRelation.Owns),
            (StudioResourceKind.SubjectContract, StudioResourceKind.LifecycleConsumer, StudioLinkRelation.Owns),
            (StudioResourceKind.Subject, StudioResourceKind.RetirementBarrier, StudioLinkRelation.Owns),
            (StudioResourceKind.OperationExecution, StudioResourceKind.Receipt, StudioLinkRelation.ReceiptFor),
            (StudioResourceKind.Provider, StudioResourceKind.CertificationReceipt, StudioLinkRelation.Owns),
            (StudioResourceKind.Store, StudioResourceKind.Provider, StudioLinkRelation.StoredBy),
            (StudioResourceKind.Store, StudioResourceKind.Schema, StudioLinkRelation.Owns),
            (StudioResourceKind.Store, StudioResourceKind.Backup, StudioLinkRelation.Owns),
            (StudioResourceKind.Store, StudioResourceKind.Restore, StudioLinkRelation.Owns),
            (StudioResourceKind.Store, StudioResourceKind.Maintenance, StudioLinkRelation.Owns),
            (StudioResourceKind.Maintenance, StudioResourceKind.QuarantineItem, StudioLinkRelation.Owns),
            (StudioResourceKind.Health, StudioResourceKind.Diagnostic, StudioLinkRelation.Diagnoses),
        ];
        return definitions.OrderBy(static value => (byte)value.Source).ThenBy(static value => (byte)value.Relation)
            .ThenBy(static value => (byte)value.Target)
            .Select(static value => StudioLinkRegistration.Create(value.Source, value.Target, value.Relation,
                ResourceResolverId(value.Target)))
            .ToArray();
    }

    private static string ResourceResolverId(StudioResourceKind kind)
        => $"base.studio.resolve.{kind.ToString().ToLowerInvariant()}";

    private static StudioRouteTemplate Route(PageSpec spec)
        => StudioRouteTemplate.Create(spec.Id + ".route", spec.Route.Length == 0 ? [] : spec.Route.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(static value => value == ":resource"
                ? StudioRouteSegment.Parameter("resource", StudioRouteCodec.StudioResourceIdentity)
                : StudioRouteSegment.Literal(value)));

    private static string ViewId(string pageId, string section)
        => $"{pageId}.{section}.{(ListSections.Contains(section) ? "list" : "detail")}";

    private static ImmutableArray<SectionView> SectionViews(PageSpec page, string section)
    {
        if (page.Id == "base.operations" && section == "definitions")
        {
            return
            [
                new("base.operations.definitions.registeredReads.list", StudioResourceKind.RegisteredRead),
                new("base.operations.definitions.selectionOperations.list", StudioResourceKind.SelectionOperation),
                new("base.operations.definitions.moduleMutations.list", StudioResourceKind.ModuleMutation),
                new("base.operations.definitions.semanticActivations.list", StudioResourceKind.Application),
            ];
        }

        if (page.Id == "base.semanticActivations" && section == "definitions")
            return [new("base.semanticActivations.definitions.list", StudioResourceKind.Application)];

        StudioResourceKind resource = (page.Id, section) switch
        {
            ("base.operations", "receipts") => StudioResourceKind.Receipt,
            ("base.automation", "schedules") => StudioResourceKind.Schedule,
            ("base.automation", "effects") => StudioResourceKind.Effect,
            ("base.automation", "executors") => StudioResourceKind.Executor,
            ("base.automation", "quarantine") => StudioResourceKind.QuarantineItem,
            ("base.activation.detail", "attempts") => StudioResourceKind.ActivationAttempt,
            ("base.activation.detail", "effect") => StudioResourceKind.Effect,
            ("base.activation.detail", "receipts") => StudioResourceKind.Receipt,
            ("base.schedule.detail", "occurrences") => StudioResourceKind.Occurrence,
            ("base.occurrence.detail", "activation") => StudioResourceKind.Activation,
            ("base.effect.detail", "executor") => StudioResourceKind.Executor,
            ("base.executor.detail", "effects") => StudioResourceKind.Effect,
            ("base.subjects", "contracts") => StudioResourceKind.SubjectContract,
            ("base.subjects", "subjects") => StudioResourceKind.Subject,
            ("base.subjects", "consumers") => StudioResourceKind.LifecycleConsumer,
            ("base.subjects", "barriers") => StudioResourceKind.RetirementBarrier,
            ("base.subjectContract.detail", "consumers") => StudioResourceKind.LifecycleConsumer,
            ("base.subjectContract.detail", "lifecycle") => StudioResourceKind.LifecycleCheckpoint,
            ("base.subjectContract.detail", "retirement") => StudioResourceKind.RetirementBarrier,
            ("base.subject.detail", "delivery") => StudioResourceKind.LifecycleCheckpoint,
            ("base.subject.detail", "retirement") => StudioResourceKind.RetirementBarrier,
            ("base.lifecycleConsumer.detail", "checkpoint") => StudioResourceKind.LifecycleCheckpoint,
            ("base.retirementBarrier.detail", "consumers") => StudioResourceKind.LifecycleConsumer,
            ("base.search", "textIndexes") => StudioResourceKind.TextIndex,
            ("base.search", "vectorIndexes") => StudioResourceKind.VectorIndex,
            ("base.search", "rebuilds") => StudioResourceKind.SearchRebuild,
            ("base.textIndex.detail", "rebuild") => StudioResourceKind.SearchRebuild,
            ("base.vectorIndex.detail", "rebuild") => StudioResourceKind.SearchRebuild,
            ("base.security", "grants") => StudioResourceKind.Grant,
            ("base.infrastructure", "schemas") => StudioResourceKind.Schema,
            ("base.infrastructure", "backups") => StudioResourceKind.Backup,
            ("base.infrastructure", "maintenance") => StudioResourceKind.Maintenance,
            ("base.infrastructure", "attention") => StudioResourceKind.Maintenance,
            ("base.store.detail", "certification") => StudioResourceKind.CertificationReceipt,
            ("base.store.detail", "health") => StudioResourceKind.Health,
            ("base.store.detail", "quarantine") => StudioResourceKind.QuarantineItem,
            ("base.store.detail", "maintenance") => StudioResourceKind.Maintenance,
            ("base.store.detail", "recovery") => StudioResourceKind.Restore,
            ("base.store.detail", "diagnostics") => StudioResourceKind.Diagnostic,
            ("base.provider.detail", "certification") => StudioResourceKind.CertificationReceipt,
            ("base.provider.detail", "health") => StudioResourceKind.Health,
            ("base.provider.detail", "diagnostics") => StudioResourceKind.Diagnostic,
            ("base.schema.detail", "plans") => StudioResourceKind.Migration,
            ("base.diagnostics", "health") => StudioResourceKind.Health,
            _ => page.PrimaryResource,
        };
        return [new SectionView(ViewId(page.Id, section), resource)];
    }

    private static StudioSectionKind SectionKind(string section) => section switch
    {
        "summary" or "outcome" or "result" => StudioSectionKind.Summary,
        "history" or "timeline" or "activity" => StudioSectionKind.History,
        "evidence" or "accounting" or "diagnostics" => StudioSectionKind.Evidence,
        "actions" or "remediation" => StudioSectionKind.Actions,
        "configuration" or "schema" or "contract" or "recurrence" => StudioSectionKind.Configuration,
        _ => StudioSectionKind.CustomSemantic,
    };

    private static StudioWorkspaceKind Workspace(StudioPageKind kind) => kind switch
    {
        StudioPageKind.Overview => StudioWorkspaceKind.Landing,
        StudioPageKind.Collection or StudioPageKind.ResourceList => StudioWorkspaceKind.Landing,
        StudioPageKind.ResourceDetail or StudioPageKind.Action => StudioWorkspaceKind.Detail,
        StudioPageKind.Timeline => StudioWorkspaceKind.Timeline,
        StudioPageKind.QueryTool => StudioWorkspaceKind.QueryTool,
        StudioPageKind.Diagnostics => StudioWorkspaceKind.Diagnostics,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static StudioSha256 Digest(string value) => StudioSha256.Compute(Encoding.UTF8.GetBytes(value));

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

        internal StudioGrantRequirement Module(string operation)
            => Create(operation, null, false);

        internal StudioGrantRequirement[] All => Required.Select(Module).ToArray();

        internal StudioGrantRequirement[] Resource(StudioResourceKind resource)
            => [Create("base.studio.resource.discover", resource, false),
                Create("base.studio.resource.inspect", resource, false)];

        internal StudioGrantRequirement[] Command(StudioActionClass action)
            => action == StudioActionClass.Routine
                ? [Create("base.studio.action.discover", null, false), Create("base.studio.action.execute", null, true)]
                : [Create("base.studio.action.discover", null, false), Create("base.studio.action.execute", null, true),
                   Create("base.studio.action.preview", null, true)];

        private StudioGrantRequirement Create(string operation, StudioResourceKind? resource, bool underlying)
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
            return StudioGrantRequirement.Create(registration.Id, registration.Version,
                StudioSha256.FromDigest(registration.GetChecksum()), operation, "control-plane",
                SubjectKind(grant.Subject.Kind), grant.ApplicationId, grant.ModuleId, resource,
                StudioProtectedScopeRule.Application, underlying);
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

    private readonly record struct SectionView(string Id, StudioResourceKind Resource);

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
        P("base.overview", StudioArea.Overview, "", StudioPageKind.Overview, true, StudioResourceKind.Application, "summary", "attention", "activity"),
        P("base.data", StudioArea.Data, "data", StudioPageKind.Overview, true, StudioResourceKind.Application, "summary", "modules", "collections", "files"),
        P("base.module.detail", StudioArea.Data, "data/modules/:resource", StudioPageKind.ResourceDetail, false, StudioResourceKind.Module, "summary", "resources", "operations", "health"),
        P("base.collection.detail", StudioArea.Data, "data/collections/:resource", StudioPageKind.ResourceDetail, false, StudioResourceKind.Collection, "summary", "schema", "relations", "indexes", "operations", "history"),
        P("base.collection.records", StudioArea.Data, "data/collections/:resource/records", StudioPageKind.Collection, false, StudioResourceKind.Collection, "records", "filters", "paging"),
        P("base.record.detail", StudioArea.Data, "data/records/:resource", StudioPageKind.ResourceDetail, false, StudioResourceKind.Record, "summary", "fields", "relations", "references", "history", "receipts", "search", "evidence"),
        P("base.fileBucket.detail", StudioArea.Data, "data/file-buckets/:resource", StudioPageKind.ResourceDetail, false, StudioResourceKind.FileBucket, "summary", "objects", "classification", "retention", "history", "actions"),
        P("base.file.detail", StudioArea.Data, "data/files/:resource", StudioPageKind.ResourceDetail, false, StudioResourceKind.File, "summary", "metadata", "relations", "history"),
        P("base.operations", StudioArea.Operations, "operations", StudioPageKind.Overview, true, StudioResourceKind.OperationExecution, "definitions", "executions", "receipts"),
        PM("base.operation.definition", StudioArea.Operations, "operations/definitions/:resource", StudioPageKind.ResourceDetail,
            [StudioResourceKind.RegisteredRead, StudioResourceKind.SelectionOperation, StudioResourceKind.ModuleMutation],
            "summary", "contract", "resources", "authorization", "limits", "executions"),
        P("base.operation.execution", StudioArea.Operations, "operations/executions/:resource", StudioPageKind.Timeline, false, StudioResourceKind.OperationExecution, "outcome", "facts", "result", "decisions", "accounting", "timeline"),
        P("base.receipt.detail", StudioArea.Operations, "operations/receipts/:resource", StudioPageKind.Timeline, false, StudioResourceKind.Receipt, "outcome", "facts", "result", "disclosure", "accounting", "timeline"),
        P("base.automation", StudioArea.Automations, "automations", StudioPageKind.Overview, true, StudioResourceKind.Activation, "attention", "activations", "schedules", "effects", "executors", "quarantine"),
        P("base.semanticActivations", StudioArea.Automations, "automations/semantic-activations", StudioPageKind.ResourceList, false, StudioResourceKind.Application, "definitions"),
        P("base.activation.detail", StudioArea.Automations, "automations/activations/:resource", StudioPageKind.Timeline, false, StudioResourceKind.Activation, "summary", "inputResult", "occurrence", "attempts", "claim", "children", "effect", "receipts", "timeline", "evidence"),
        P("base.schedule.detail", StudioArea.Automations, "automations/schedules/:resource", StudioPageKind.ResourceDetail, false, StudioResourceKind.Schedule, "summary", "recurrence", "timeAuthority", "misfireOverlap", "occurrences", "work", "history", "dependencies"),
        P("base.occurrence.detail", StudioArea.Automations, "automations/occurrences/:resource", StudioPageKind.Timeline, false, StudioResourceKind.Occurrence, "summary", "disposition", "activation", "timeline", "evidence"),
        P("base.effect.detail", StudioArea.Automations, "automations/effects/:resource", StudioPageKind.Timeline, false, StudioResourceKind.Effect, "summary", "executor", "outcome", "reconciliation", "timeline", "evidence"),
        P("base.executor.detail", StudioArea.Automations, "automations/executors/:resource", StudioPageKind.ResourceDetail, false, StudioResourceKind.Executor, "summary", "incarnation", "heartbeats", "definitions", "effects", "history", "evidence"),
        P("base.subjects", StudioArea.Subjects, "subjects", StudioPageKind.Overview, true, StudioResourceKind.SubjectContract, "contracts", "subjects", "consumers", "barriers"),
        P("base.subjectContract.detail", StudioArea.Subjects, "subjects/contracts/:resource", StudioPageKind.ResourceDetail, false, StudioResourceKind.SubjectContract, "summary", "references", "consumers", "lifecycle", "retirement"),
        P("base.subject.detail", StudioArea.Subjects, "subjects/instances/:resource", StudioPageKind.Timeline, false, StudioResourceKind.Subject, "summary", "references", "lifecycle", "delivery", "retirement", "acknowledgements", "purge", "timeline"),
        P("base.lifecycleConsumer.detail", StudioArea.Subjects, "subjects/consumers/:resource", StudioPageKind.ResourceDetail, false, StudioResourceKind.LifecycleConsumer, "summary", "contractScope", "delivery", "checkpoint", "lagOvertake", "reconciliation", "history"),
        P("base.retirementBarrier.detail", StudioArea.Subjects, "subjects/barriers/:resource", StudioPageKind.Timeline, false, StudioResourceKind.RetirementBarrier, "summary", "consumers", "evidence", "dispositions", "deadline", "override", "purge", "publications"),
        P("base.search", StudioArea.Search, "search", StudioPageKind.Overview, true, StudioResourceKind.Application, "textIndexes", "vectorIndexes", "rebuilds", "attention"),
        P("base.textIndex.detail", StudioArea.Search, "search/text/:resource", StudioPageKind.ResourceDetail, false, StudioResourceKind.TextIndex, "summary", "fieldsAnalyzer", "policyInfluence", "scoring", "freshness", "generations", "rebuild", "certification", "diagnostics"),
        P("base.vectorIndex.detail", StudioArea.Search, "search/vector/:resource", StudioPageKind.ResourceDetail, false, StudioResourceKind.VectorIndex, "summary", "fieldsEmbedding", "policyInfluence", "distance", "freshness", "generations", "rebuild", "certification", "diagnostics"),
        PM("base.search.query", StudioArea.Search, "search/query/:resource", StudioPageKind.QueryTool, [StudioResourceKind.TextIndex, StudioResourceKind.VectorIndex], "query", "results", "explanation", "evidence"),
        P("base.rebuild.detail", StudioArea.Search, "search/rebuilds/:resource", StudioPageKind.Timeline, false, StudioResourceKind.SearchRebuild, "summary", "sourceCoverage", "phases", "probe", "generations", "timeline"),
        P("base.security", StudioArea.Security, "security", StudioPageKind.Overview, true, StudioResourceKind.Policy, "policies", "grants", "explanations", "disclosure"),
        P("base.policy.detail", StudioArea.Security, "security/policies/:resource", StudioPageKind.ResourceDetail, false, StudioResourceKind.Policy, "summary", "composition", "constraints", "masks", "obligations", "history"),
        P("base.grant.detail", StudioArea.Security, "security/grants/:resource", StudioPageKind.ResourceDetail, false, StudioResourceKind.Grant, "summary", "scope", "operations", "conditions", "history"),
        P("base.policy.explain", StudioArea.Security, "security/explanations/:resource", StudioPageKind.Diagnostics, false, StudioResourceKind.Policy, "operation", "resource", "filters", "constraints", "masks", "disclosure", "decision"),
        P("base.infrastructure", StudioArea.Infrastructure, "infrastructure", StudioPageKind.Overview, true, StudioResourceKind.Store, "stores", "schemas", "backups", "maintenance", "attention"),
        P("base.store.detail", StudioArea.Infrastructure, "infrastructure/stores/:resource", StudioPageKind.ResourceDetail, false, StudioResourceKind.Store, "summary", "capabilities", "certification", "assets", "health", "retainedWork", "quarantine", "maintenance", "recovery", "diagnostics"),
        P("base.provider.detail", StudioArea.Infrastructure, "infrastructure/providers/:resource", StudioPageKind.ResourceDetail, false, StudioResourceKind.Provider, "summary", "capability", "certification", "health", "diagnostics"),
        P("base.schema.detail", StudioArea.Infrastructure, "infrastructure/schemas/:resource", StudioPageKind.ResourceDetail, false, StudioResourceKind.Schema, "summary", "graph", "drift", "plans", "history", "evidence"),
        P("base.migration.detail", StudioArea.Infrastructure, "infrastructure/migrations/:resource", StudioPageKind.Timeline, false, StudioResourceKind.Migration, "summary", "plan", "compatibility", "progress", "history"),
        P("base.backup.detail", StudioArea.Infrastructure, "infrastructure/backups/:resource", StudioPageKind.ResourceDetail, false, StudioResourceKind.Backup, "summary", "authentication", "contents", "compatibility", "history"),
        P("base.restore.detail", StudioArea.Infrastructure, "infrastructure/restores/:resource", StudioPageKind.Timeline, false, StudioResourceKind.Restore, "summary", "artifact", "consequences", "progress", "newAuthority", "reconciliation"),
        PM("base.maintenance.detail", StudioArea.Infrastructure, "infrastructure/maintenance/:resource", StudioPageKind.Timeline,
            [StudioResourceKind.Maintenance, StudioResourceKind.QuarantineItem], "summary", "scope", "progress", "retainedWork", "history", "evidence"),
        P("base.diagnostics", StudioArea.Diagnostics, "diagnostics", StudioPageKind.Diagnostics, true, StudioResourceKind.Diagnostic, "incidents", "health", "accounting"),
        P("base.health.detail", StudioArea.Diagnostics, "diagnostics/health/:resource", StudioPageKind.Diagnostics, false, StudioResourceKind.Health, "summary", "dependencies", "history", "remediation"),
        P("base.diagnostic.detail", StudioArea.Diagnostics, "diagnostics/events/:resource", StudioPageKind.Diagnostics, false, StudioResourceKind.Diagnostic, "summary", "correlation", "affectedResources", "accounting", "evidence"),
    ];

    private static readonly ImmutableArray<CommandSpec> Commands =
    [
        C("administration.purge", "base.store.detail", StudioActionClass.Destructive), C("backup.create", "base.store.detail", StudioActionClass.Maintenance),
        C("backup.restore", "base.backup.detail", StudioActionClass.DisasterOrRecoveryDomain), C("backup.validate", "base.backup.detail", StudioActionClass.OperationalTransition),
        C("file.delete", "base.file.detail", StudioActionClass.Destructive), C("file.upload", "base.fileBucket.detail", StudioActionClass.Routine),
        C("moduleMutation.execute", "base.operation.definition", StudioActionClass.Routine), C("record.create", "base.collection.detail", StudioActionClass.Routine),
        C("record.delete", "base.record.detail", StudioActionClass.Destructive), C("record.patch", "base.record.detail", StudioActionClass.Routine),
        C("record.replace", "base.record.detail", StudioActionClass.Routine), C("record.upsert", "base.record.detail", StudioActionClass.Routine),
        C("registeredSelection.execute", "base.collection.records", StudioActionClass.OperationalTransition), C("schema.apply", "base.schema.detail", StudioActionClass.DisasterOrRecoveryDomain),
        C("retirement.consumer.remove", "base.lifecycleConsumer.detail", StudioActionClass.Destructive), C("retirement.override", "base.retirementBarrier.detail", StudioActionClass.Destructive),
        C("retirement.purge", "base.retirementBarrier.detail", StudioActionClass.Destructive), C("retirement.timeout", "base.retirementBarrier.detail", StudioActionClass.OperationalTransition),
        C("schema.plan", "base.schema.detail", StudioActionClass.OperationalTransition), C("schema.verify", "base.schema.detail", StudioActionClass.OperationalTransition),
        C("textIndex.rebuild", "base.textIndex.detail", StudioActionClass.Maintenance), C("vectorIndex.rebuild", "base.vectorIndex.detail", StudioActionClass.Maintenance),
    ];

    private static PageSpec P(string id, StudioArea area, string route, StudioPageKind kind, bool landing,
        StudioResourceKind resource, params string[] sections) => new(id, area, route, kind, landing, resource,
            landing && resource != StudioResourceKind.Application
                ? [StudioResourceKind.Application, resource]
                : [resource], [.. sections]);
    private static PageSpec PM(string id, StudioArea area, string route, StudioPageKind kind,
        ImmutableArray<StudioResourceKind> resources, params string[] sections)
        => new(id, area, route, kind, false, resources[0], resources, [.. sections]);
    private static CommandSpec C(string id, string page, StudioActionClass action) => new(id, page, action);

    private sealed record PageSpec(string Id, StudioArea Area, string Route, StudioPageKind Kind, bool Landing,
        StudioResourceKind PrimaryResource, ImmutableArray<StudioResourceKind> Resources, ImmutableArray<string> Sections);
    private sealed record CommandSpec(string Id, string PageId, StudioActionClass Action);
}
