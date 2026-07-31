using Microsoft.Extensions.Options;

namespace HPD.Base;

internal sealed class DefaultBaseDescriptorRegistry : IBaseDescriptorRegistry
{
    private readonly HPDBaseRuntimeOptions _options;
    private readonly IEnumerable<IBaseDescriptorContributor> _contributors;
    private readonly IEnumerable<IBaseDescriptorValidator> _validators;
    private readonly IBaseCapabilityValidator _capabilityValidator;
    private readonly IRecordStoreRegistry _stores;
    private BaseDescriptorSnapshot? _current;

    public DefaultBaseDescriptorRegistry(
        IOptions<HPDBaseRuntimeOptions> options,
        IEnumerable<IBaseDescriptorContributor> contributors,
        IEnumerable<IBaseDescriptorValidator> validators,
        IBaseCapabilityValidator capabilityValidator,
        IRecordStoreRegistry stores)
    {
        _options = options.Value;
        _contributors = contributors;
        _validators = validators;
        _capabilityValidator = capabilityValidator;
        _stores = stores;
    }

    public BaseDescriptorSnapshot Current => _current ??= CreateSnapshot();

    public ValueTask<BaseDescriptorSnapshot> RebuildAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _current = CreateSnapshot();
        return ValueTask.FromResult(_current);
    }

    private BaseDescriptorSnapshot CreateSnapshot()
    {
        var contributions = new DefaultBaseDescriptorContributionBuilder();
        foreach (var contributor in _contributors)
        {
            contributor.Contribute(contributions);
        }

        var collections = MergeCollections(contributions);
        var capabilities = MergeCapabilities(contributions);
        var health = contributions.Health;
        var diagnostics = contributions.Diagnostics;

        var manifest = new BaseManifest
        {
            ManifestVersion = _options.ManifestVersion,
            ContractVersion = _options.Compatibility.BaseContractVersion,
            Runtime = _options.Runtime,
            Compatibility = _options.Compatibility,
            Collections = collections.Length == 0 ? null : collections.Select(collection => new CollectionSummaryDescriptor
            {
                Id = collection.Id,
                Name = collection.Name,
                DisplayName = collection.DisplayName,
                Kind = collection.Kind,
                Enabled = collection.Enabled,
                Exposed = collection.Exposed,
                SchemaRef = collection.SchemaVersion,
                RequiredFeatureIds = collection.RequiredCapabilities,
                Visibility = collection.Visibility?.Visibility ?? VisibilityLevel.Public
            }).ToArray(),
            Capabilities = new CapabilitySummaryDescriptor
            {
                DescriptorVersion = capabilities.DescriptorVersion,
                RuntimeId = capabilities.RuntimeId,
                FamilyIds = capabilities.Families.Select(family => family.FamilyId).ToArray(),
                FeatureIds = capabilities.Families
                    .SelectMany(family => family.Features ?? [])
                    .Select(feature => feature.FeatureId)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()
            },
            Modules = NullIfEmpty(contributions.Modules),
            Projections = NullIfEmpty(contributions.Projections),
            DtoContracts = NullIfEmpty(contributions.DtoContracts),
            EventTypes = NullIfEmpty(contributions.EventTypes),
            HealthRefs = NullIfEmpty(contributions.HealthRefs),
            DiagnosticRefs = NullIfEmpty(contributions.DiagnosticRefs),
            Visibility = VisibilityLevel.Internal,
            GeneratedAt = DateTimeOffset.UnixEpoch
        };

        var schema = new SchemaMetadata
        {
            RuntimeId = _options.Runtime.Id,
            ContractVersion = _options.Compatibility.BaseContractVersion,
            Visibility = VisibilityLevel.Internal,
            Collections = NullIfEmpty(collections)
        };

        var snapshot = new BaseDescriptorSnapshot(
            manifest,
            schema,
            capabilities,
            health,
            diagnostics,
            BaseRuntimeValidationResult.Success);

        var validation = MergeValidation(_validators.Select(validator => validator.Validate(snapshot))
            .Append(_capabilityValidator.ValidateCapabilities(snapshot))
            .Append(ValidateRuntimeOptions()));
        return new BaseDescriptorSnapshot(manifest, schema, capabilities, health, diagnostics, validation);
    }

    private BaseRuntimeValidationResult ValidateRuntimeOptions()
    {
        if (_options.Events.PublishFailureMode != BaseEventPublishFailureMode.RequireEnqueue)
        {
            return BaseRuntimeValidationResult.Success;
        }

        var registrations = _stores.GetRegistrations();
        if (registrations.Length > 0
            && registrations.All(registration => registration.Store is ITransactionalMutationJournalStore))
        {
            return BaseRuntimeValidationResult.Success;
        }

        return new BaseRuntimeValidationResult
        {
            Succeeded = false,
            Issues =
            [
                new BaseRuntimeValidationIssue
                {
                    Severity = BaseRuntimeValidationSeverity.Fatal,
                    Kind = BaseRuntimeValidationFailureKind.InvalidConfiguration,
                    Code = "base.runtime.events.transactionalJournalRequired",
                    Message = "RequireEnqueue requires every registered mutation store to support transactional mutation journaling.",
                    TargetPath = "events.publishFailureMode"
                }
            ]
        };
    }

    private CapabilityDescriptor MergeCapabilities(DefaultBaseDescriptorContributionBuilder contributions)
    {
        var families = contributions.Capabilities
            .SelectMany(capability => capability.Families)
            .ToArray();

        return new CapabilityDescriptor
        {
            DescriptorVersion = _options.ManifestVersion,
            RuntimeId = _options.Runtime.Id,
            Families = families
        };
    }

    private static CollectionDefinition[] MergeCollections(DefaultBaseDescriptorContributionBuilder contributions) =>
        [.. contributions.Schemas.SelectMany(schema => schema.Collections ?? []), .. contributions.Collections];

    private static T[]? NullIfEmpty<T>(T[] items) => items.Length == 0 ? null : items;

    private static BaseRuntimeValidationResult MergeValidation(IEnumerable<BaseRuntimeValidationResult> results)
    {
        var issues = results.SelectMany(result => result.Issues ?? []).ToArray();
        return new BaseRuntimeValidationResult
        {
            Succeeded = !issues.Any(issue => issue.Severity == BaseRuntimeValidationSeverity.Fatal),
            Issues = issues.Length == 0 ? null : issues
        };
    }
}
