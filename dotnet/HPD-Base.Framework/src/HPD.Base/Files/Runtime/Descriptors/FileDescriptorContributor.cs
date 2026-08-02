using Microsoft.Extensions.Options;

namespace HPD.Base;

internal sealed class FileDescriptorContributor : IBaseDescriptorContributor
{
    private readonly HPDBaseFilesOptions _options;

    /// <summary>Initializes a new instance.</summary>
    public FileDescriptorContributor(IOptions<HPDBaseFilesOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>Gets the ID.</summary>
    public string Id => FileModuleIds.Module;

    /// <summary>Executes the contribute operation.</summary>
    public void Contribute(IBaseDescriptorContributionBuilder builder)
    {
        var dtoIds = new[] { FileDtoIds.BucketDescriptor, FileDtoIds.ObjectRef, FileDtoIds.ObjectMetadata, FileDtoIds.ObjectUploadResult, FileDtoIds.ObjectListResult, FileDtoIds.ObjectEvent };
        foreach (var dtoId in dtoIds)
        {
            builder.AddDtoContract(new DtoContractDescriptor
            {
                Id = dtoId,
                ContractVersion = "1.0",
                JsonContextOwner = "HPD.Base",
                Visibility = VisibilityLevel.Public
            });
        }

        builder.AddModule(new BaseModuleDescriptor
        {
            Id = FileModuleIds.Module,
            Name = "HPD.Base",
            Kind = BaseModuleKind.Files,
            Version = "1.0.0",
            Status = _options.Enabled ? ModuleStatus.Installed : ModuleStatus.Disabled,
            Compatibility = new ModuleCompatibility { RequiresBaseContract = "1.0" },
            ContributedCapabilities = FeatureIds,
            ContributedDtoIds = dtoIds,
            ContributedEventTypes = EventTypes,
            ContributedHealthRefIds = [FileHealthIds.Registration, FileHealthIds.Provider, .. _options.Buckets.Select(bucket => FileHealthIds.Bucket(bucket.BucketId))],
            ContributedDiagnosticIds = DiagnosticIds,
            PublicConfig = _options.Buckets.Any(IsPublicSafe) ? BucketSummary(_options.Buckets.Where(IsPublicSafe), includeProvider: false) : null,
            AdminConfigSummary = BucketSummary(_options.Buckets, includeProvider: true),
            Visibility = VisibilityLevel.Public
        });

        builder.AddHealthRef(new HealthRefDescriptor
        {
            Id = FileHealthIds.Registration,
            Scope = HealthScope.Module,
            TargetRef = FileModuleIds.Module,
            Visibility = VisibilityLevel.Public
        });
        builder.AddHealthRef(new HealthRefDescriptor
        {
            Id = FileHealthIds.Provider,
            Scope = HealthScope.Dependency,
            TargetRef = FileModuleIds.Module,
            Visibility = VisibilityLevel.Admin
        });
        foreach (var bucket in _options.Buckets)
        {
            builder.AddHealthRef(new HealthRefDescriptor
            {
                Id = FileHealthIds.Bucket(bucket.BucketId),
                Scope = HealthScope.Dependency,
                TargetRef = bucket.BucketId.Value,
                Visibility = bucket.DescriptorVisibility
            });
        }

        foreach (var diagnosticId in DiagnosticIds)
        {
            builder.AddDiagnosticRef(new DiagnosticRefDescriptor
            {
                Id = diagnosticId,
                Visibility = VisibilityLevel.Admin
            });
        }

        foreach (var eventType in EventTypes)
        {
            builder.AddEventType(new EventTypeDescriptor
            {
                Type = eventType,
                EnvelopeVersion = "1.0",
                SchemaId = FileDtoIds.ObjectEvent,
                Visibility = VisibilityLevel.Admin
            });
        }

        builder.AddCapabilities(new CapabilityDescriptor
        {
            DescriptorVersion = "1.0",
            RuntimeId = FileModuleIds.Module,
            Families =
            [
                new CapabilityFamilyDescriptor
                {
                    FamilyId = BaseCapabilityFamilies.Files,
                    FamilyVersion = "1.0",
                    Status = _options.Enabled ? CapabilityStatus.Degraded : CapabilityStatus.Disabled,
                    OwnerModuleId = FileModuleIds.Module,
                    Visibility = VisibilityLevel.Public,
                    Features =
                    [
                        Feature(FileFeatureIds.Upload, write: true),
                        Feature(FileFeatureIds.Download, read: true),
                        Feature(FileFeatureIds.MetadataRead, read: true),
                        Feature(FileFeatureIds.Delete, write: true),
                        Feature(FileFeatureIds.List, read: true),
                        Feature(FileFeatureIds.BucketDescribe, read: true)
                    ]
                }
            ]
        });
    }

    private static Dictionary<string, System.Text.Json.JsonElement>? BucketSummary(IEnumerable<FileBucketDescriptor> source, bool includeProvider)
    {
        var buckets = source.ToArray();
        if (buckets.Length == 0)
            return null;

        var result = new Dictionary<string, System.Text.Json.JsonElement>
        {
            ["bucketIds"] = ToJsonArray(buckets.Select(static bucket => bucket.BucketId.Value))
        };

        if (includeProvider)
            result["providerRefs"] = ToJsonArray(buckets.Select(static bucket => bucket.ProviderRef?.Value).Where(static value => !string.IsNullOrWhiteSpace(value))!);

        return result;
    }

    private static System.Text.Json.JsonElement ToJsonArray(IEnumerable<string> values) =>
        System.Text.Json.JsonDocument.Parse("[\"" + string.Join("\",\"", values.Where(static value => !string.IsNullOrWhiteSpace(value)).Select(Escape)) + "\"]").RootElement.Clone();

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static bool IsPublicSafe(FileBucketDescriptor bucket) =>
        bucket.Visibility == FileBucketVisibility.PublicRead && bucket.DescriptorVisibility == VisibilityLevel.Public;

    private static CapabilityFeatureDescriptor Feature(string id, bool read = false, bool write = false) => new()
    {
        FeatureId = id,
        Version = "1.0",
        Status = CapabilityStatus.Degraded,
        SupportLevel = SupportLevel.Optional,
        Scope = CapabilityScope.Runtime,
        Constraints = new CapabilityConstraintSet
        {
            Files = new FileCapabilityConstraints
            {
                Read = read,
                Write = write,
                FeatureIds = [id]
            }
        },
        HealthRef = FileHealthIds.Provider,
        DiagnosticRefs = [FileDiagnosticIds.NoProvider, FileDiagnosticIds.PolicyUnavailable],
        Visibility = VisibilityLevel.Public
    };

    private static readonly string[] FeatureIds =
    [
        FileFeatureIds.Upload,
        FileFeatureIds.Download,
        FileFeatureIds.MetadataRead,
        FileFeatureIds.Delete,
        FileFeatureIds.List,
        FileFeatureIds.BucketDescribe
    ];

    private static readonly string[] DiagnosticIds =
    [
        FileDiagnosticIds.NoProvider,
        FileDiagnosticIds.PolicyUnavailable,
        FileDiagnosticIds.BucketDisabled,
        FileDiagnosticIds.InvalidKey,
        FileDiagnosticIds.ContentTypeRejected,
        FileDiagnosticIds.SizeExceeded,
        FileDiagnosticIds.ChecksumRejected,
        FileDiagnosticIds.ProviderSecretRedacted,
        FileDiagnosticIds.PublicBucketWarning
    ];

    private static readonly string[] EventTypes =
    [
        FileEventTypeNames.ObjectUploaded,
        FileEventTypeNames.ObjectMetadataUpdated,
        FileEventTypeNames.ObjectDeleted,
        FileEventTypeNames.ObjectAccessCreated,
        FileEventTypeNames.RecordAttachmentCreated,
        FileEventTypeNames.RecordAttachmentRemoved
    ];
}
