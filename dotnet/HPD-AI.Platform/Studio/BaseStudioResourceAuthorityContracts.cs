using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace HPD.AI.Platform.Studio;

/// <summary>Base type of the closed, server-issued Studio resource identity union.</summary>
public abstract record BaseStudioResourceIdentity
{
    private readonly ImmutableArray<ResourceMember> _members;

    private protected BaseStudioResourceIdentity(BaseStudioResourceKind kind, string applicationId,
        params ResourceMember[] members)
    {
        StudioContractValidation.Enum(kind); StudioContractValidation.Id(applicationId);
        Kind = kind; ApplicationId = Own(applicationId, nameof(applicationId));
        _members = [.. members];
        AuthorityChecksum = StudioCanonicalEncoding.Hash("base.studio.resource-identity.v1", writer =>
        {
            writer.Enum(kind); writer.String(ApplicationId); writer.Count(_members.Length);
            foreach (ResourceMember member in _members)
            {
                writer.String(member.Name);
                switch (member.Value)
                {
                    case string text: writer.Int32(1); writer.String(text); break;
                    case int number: writer.Int32(2); writer.Int32(number); break;
                    case long number: writer.Int32(3); writer.Int64(number); break;
                    case BaseStudioSha256 checksum: writer.Int32(4); writer.Checksum(checksum); break;
                    default: throw new InvalidOperationException("The resource identity member type is not closed.");
                }
            }
        });
    }

    /// <summary>Gets the exact resource discriminator.</summary>
    public BaseStudioResourceKind Kind { get; }
    /// <summary>Gets the owning application identity.</summary>
    public string ApplicationId { get; }
    /// <summary>Gets the purpose-bound resource identity checksum.</summary>
    public BaseStudioSha256 AuthorityChecksum { get; }

    internal void WriteJson(Utf8JsonWriter writer)
    {
        writer.WriteStartObject(); var values = new List<ResourceMember>(_members.Length + 3)
        { new("applicationId", ApplicationId), new("authorityChecksum", AuthorityChecksum), new("kind", Name(Kind)) };
        values.AddRange(_members);
        foreach (ResourceMember member in values.OrderBy(static value => value.Name, StringComparer.Ordinal))
        {
            switch (member.Value)
            {
                case string text: writer.WriteString(member.Name, text); break;
                case int number: writer.WriteNumber(member.Name, number); break;
                case long number: writer.WriteString(member.Name, number.ToString(CultureInfo.InvariantCulture)); break;
                case BaseStudioSha256 checksum: writer.WriteString(member.Name, Convert.ToHexString(checksum.ToArray()).ToLowerInvariant()); break;
            }
        }
        writer.WriteEndObject();
    }

    private protected static ResourceMember Text(string name, string value) => new(name, Own(value, name));
    private protected static ResourceMember Id(string name, string value) { StudioContractValidation.Id(value); return new(name, new string(value.AsSpan())); }
    private protected static ResourceMember Positive(string name, int value) => value > 0 ? new(name, value) : throw new ArgumentOutOfRangeException(name);
    private protected static ResourceMember Nonnegative(string name, long value) => value >= 0 ? new(name, value) : throw new ArgumentOutOfRangeException(name);
    private protected static ResourceMember Positive(string name, long value) => value > 0 ? new(name, value) : throw new ArgumentOutOfRangeException(name);
    private protected static ResourceMember Checksum(string name, BaseStudioSha256 value) => new(name, BaseStudioSha256.FromDigest(value?.ToArray() ?? throw new ArgumentNullException(name)));
    private static string Own(string value, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        if (Encoding.UTF8.GetByteCount(value) > 512 || value.Any(static character => char.IsControl(character)))
            throw new ArgumentException("Studio resource identity is invalid.", parameter);
        return new(value.AsSpan());
    }
    internal static string Name(BaseStudioResourceKind kind) => kind switch
    {
        BaseStudioResourceKind.Application => "application", BaseStudioResourceKind.Module => "module",
        BaseStudioResourceKind.Collection => "collection", BaseStudioResourceKind.Record => "record",
        BaseStudioResourceKind.Relation => "relation", BaseStudioResourceKind.FileBucket => "fileBucket",
        BaseStudioResourceKind.File => "file", BaseStudioResourceKind.RegisteredRead => "registeredRead",
        BaseStudioResourceKind.SelectionOperation => "selectionOperation", BaseStudioResourceKind.ModuleMutation => "moduleMutation",
        BaseStudioResourceKind.OperationExecution => "operationExecution", BaseStudioResourceKind.Receipt => "receipt",
        BaseStudioResourceKind.ActivationDefinition => "activationDefinition", BaseStudioResourceKind.Activation => "activation",
        BaseStudioResourceKind.Schedule => "schedule", BaseStudioResourceKind.Occurrence => "occurrence",
        BaseStudioResourceKind.ActivationAttempt => "activationAttempt", BaseStudioResourceKind.Effect => "effect",
        BaseStudioResourceKind.Executor => "executor", BaseStudioResourceKind.SubjectContract => "subjectContract",
        BaseStudioResourceKind.Subject => "subject", BaseStudioResourceKind.LifecycleConsumer => "lifecycleConsumer",
        BaseStudioResourceKind.LifecycleCheckpoint => "lifecycleCheckpoint", BaseStudioResourceKind.RetirementBarrier => "retirementBarrier",
        BaseStudioResourceKind.TextIndex => "textIndex", BaseStudioResourceKind.VectorIndex => "vectorIndex",
        BaseStudioResourceKind.SearchRebuild => "searchRebuild", BaseStudioResourceKind.CertificationReceipt => "certificationReceipt",
        BaseStudioResourceKind.Policy => "policy", BaseStudioResourceKind.Grant => "grant", BaseStudioResourceKind.Store => "store",
        BaseStudioResourceKind.Provider => "provider", BaseStudioResourceKind.Schema => "schema", BaseStudioResourceKind.Migration => "migration",
        BaseStudioResourceKind.Backup => "backup", BaseStudioResourceKind.Restore => "restore", BaseStudioResourceKind.Maintenance => "maintenance",
        BaseStudioResourceKind.Health => "health", BaseStudioResourceKind.Diagnostic => "diagnostic",
        BaseStudioResourceKind.QuarantineItem => "quarantineItem", BaseStudioResourceKind.GraphDefinition => "graphDefinition",
        BaseStudioResourceKind.GraphExecution => "graphExecution", BaseStudioResourceKind.GraphNode => "graphNode",
        BaseStudioResourceKind.GraphChannel => "graphChannel", BaseStudioResourceKind.GraphCheckpoint => "graphCheckpoint",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private protected readonly record struct ResourceMember(string Name, object Value);
}

/// <summary>Identifies the installed application.</summary>
public sealed record BaseStudioApplicationResource : BaseStudioResourceIdentity
{ public BaseStudioApplicationResource(string applicationId) : base(BaseStudioResourceKind.Application, applicationId) { } }
/// <summary>Identifies one installed module version.</summary>
public sealed record BaseStudioModuleResource : BaseStudioResourceIdentity
{ public BaseStudioModuleResource(string applicationId, string moduleId, int moduleVersion) : base(BaseStudioResourceKind.Module, applicationId, Id("moduleId", moduleId), Positive("moduleVersion", moduleVersion)) { ModuleId = moduleId; ModuleVersion = moduleVersion; } public string ModuleId { get; } public int ModuleVersion { get; } }
/// <summary>Identifies one collection version.</summary>
public sealed record BaseStudioCollectionResource : BaseStudioResourceIdentity
{ public BaseStudioCollectionResource(string applicationId, string collectionId, BaseStudioSha256 installedCollectionChecksum) : base(BaseStudioResourceKind.Collection, applicationId, Id("collectionId", collectionId), Checksum("installedCollectionChecksum", installedCollectionChecksum)) { CollectionId = collectionId; InstalledCollectionChecksum = BaseStudioSha256.FromDigest(installedCollectionChecksum.ToArray()); } public string CollectionId { get; } public BaseStudioSha256 InstalledCollectionChecksum { get; } }
/// <summary>Identifies one record in a collection version.</summary>
public sealed record BaseStudioRecordResource : BaseStudioResourceIdentity
{ public BaseStudioRecordResource(string applicationId, string collectionId, BaseStudioSha256 installedCollectionChecksum, string recordId) : base(BaseStudioResourceKind.Record, applicationId, Id("collectionId", collectionId), Checksum("installedCollectionChecksum", installedCollectionChecksum), Text("recordId", recordId)) { CollectionId = collectionId; InstalledCollectionChecksum = BaseStudioSha256.FromDigest(installedCollectionChecksum.ToArray()); RecordId = recordId; } public string CollectionId { get; } public BaseStudioSha256 InstalledCollectionChecksum { get; } public string RecordId { get; } }
/// <summary>Identifies one relation edge.</summary>
public sealed record BaseStudioRelationResource : BaseStudioResourceIdentity
{ public BaseStudioRelationResource(string applicationId, string sourceCollectionId, string sourceRecordId, string fieldEdgeId, string targetCollectionId, string targetRecordId) : base(BaseStudioResourceKind.Relation, applicationId, Id("sourceCollectionId", sourceCollectionId), Text("sourceRecordId", sourceRecordId), Id("fieldEdgeId", fieldEdgeId), Id("targetCollectionId", targetCollectionId), Text("targetRecordId", targetRecordId)) { } }
/// <summary>Identifies one file bucket.</summary>
public sealed record BaseStudioFileBucketResource : BaseStudioResourceIdentity
{ public BaseStudioFileBucketResource(string applicationId, string bucketId) : base(BaseStudioResourceKind.FileBucket, applicationId, Id("bucketId", bucketId)) { } }
/// <summary>Identifies one file object.</summary>
public sealed record BaseStudioFileResource : BaseStudioResourceIdentity
{ public BaseStudioFileResource(string applicationId, string bucketId, string objectId) : base(BaseStudioResourceKind.File, applicationId, Id("bucketId", bucketId), Text("objectId", objectId)) { } }
/// <summary>Identifies one registered read.</summary>
public sealed record BaseStudioRegisteredReadResource : BaseStudioResourceIdentity
{ public BaseStudioRegisteredReadResource(string applicationId, string readId, int version) : base(BaseStudioResourceKind.RegisteredRead, applicationId, Id("readId", readId), Positive("version", version)) { } }
/// <summary>Identifies one registered selection operation.</summary>
public sealed record BaseStudioSelectionOperationResource : BaseStudioResourceIdentity
{ public BaseStudioSelectionOperationResource(string applicationId, string profileId, int version) : base(BaseStudioResourceKind.SelectionOperation, applicationId, Id("profileId", profileId), Positive("version", version)) { } }
/// <summary>Identifies one registered module mutation.</summary>
public sealed record BaseStudioModuleMutationResource : BaseStudioResourceIdentity
{ public BaseStudioModuleMutationResource(string applicationId, string operationId, int version) : base(BaseStudioResourceKind.ModuleMutation, applicationId, Id("operationId", operationId), Positive("version", version)) { } }
/// <summary>Identifies one operation execution.</summary>
public sealed record BaseStudioOperationExecutionResource : BaseStudioResourceIdentity
{
    public BaseStudioOperationExecutionResource(string applicationId, string operationKind, string operationId, string requestIdentity)
        : base(BaseStudioResourceKind.OperationExecution, applicationId, Id("operationKind", operationKind),
            Id("operationId", operationId), Text("requestIdentity", requestIdentity))
    {
        OperationKind = new(operationKind.AsSpan());
        OperationId = new(operationId.AsSpan());
        RequestIdentity = new(requestIdentity.AsSpan());
    }

    /// <summary>Gets the closed operation family.</summary>
    public string OperationKind { get; }
    /// <summary>Gets the installed operation identity.</summary>
    public string OperationId { get; }
    /// <summary>Gets the exact receipt request identity.</summary>
    public string RequestIdentity { get; }
}
/// <summary>Identifies one receipt.</summary>
public sealed record BaseStudioReceiptResource : BaseStudioResourceIdentity
{ public BaseStudioReceiptResource(string applicationId, string receiptKind, string operationId, string requestIdentity) : base(BaseStudioResourceKind.Receipt, applicationId, Id("receiptKind", receiptKind), Id("operationId", operationId), Text("requestIdentity", requestIdentity)) { } }
/// <summary>Identifies one activation definition.</summary>
public sealed record BaseStudioActivationDefinitionResource : BaseStudioResourceIdentity
{ public BaseStudioActivationDefinitionResource(string applicationId, string definitionId, int version) : base(BaseStudioResourceKind.ActivationDefinition, applicationId, Id("definitionId", definitionId), Positive("version", version)) { } }
/// <summary>Identifies one activation.</summary>
public sealed record BaseStudioActivationResource : BaseStudioResourceIdentity
{ public BaseStudioActivationResource(string applicationId, string definitionId, int version, string activationId) : base(BaseStudioResourceKind.Activation, applicationId, Id("definitionId", definitionId), Positive("version", version), Text("activationId", activationId))
  { DefinitionId = definitionId; Version = version; ActivationId = activationId; }
  /// <summary>Gets the installed definition identity.</summary>
  public string DefinitionId { get; }
  /// <summary>Gets the installed definition version.</summary>
  public int Version { get; }
  /// <summary>Gets the durable activation identity.</summary>
  public string ActivationId { get; }
}
/// <summary>Identifies one schedule.</summary>
public sealed record BaseStudioScheduleResource : BaseStudioResourceIdentity
{ public BaseStudioScheduleResource(string applicationId, string scheduleId, int version) : base(BaseStudioResourceKind.Schedule, applicationId, Id("scheduleId", scheduleId), Positive("version", version)) { ScheduleId = scheduleId; Version = version; }
  /// <summary>Gets the installed schedule identity.</summary>
  public string ScheduleId { get; }
  /// <summary>Gets the installed schedule version.</summary>
  public int Version { get; } }
/// <summary>Identifies one schedule occurrence.</summary>
public sealed record BaseStudioOccurrenceResource : BaseStudioResourceIdentity
{ public BaseStudioOccurrenceResource(string applicationId, string scheduleId, int version, string occurrenceId) : base(BaseStudioResourceKind.Occurrence, applicationId, Id("scheduleId", scheduleId), Positive("version", version), Text("occurrenceId", occurrenceId)) { ScheduleId = scheduleId; Version = version; OccurrenceId = occurrenceId; }
  /// <summary>Gets the owning schedule identity.</summary>
  public string ScheduleId { get; }
  /// <summary>Gets the owning schedule version.</summary>
  public int Version { get; }
  /// <summary>Gets the occurrence identity.</summary>
  public string OccurrenceId { get; } }
/// <summary>Identifies one activation attempt.</summary>
public sealed record BaseStudioActivationAttemptResource : BaseStudioResourceIdentity
{ public BaseStudioActivationAttemptResource(string applicationId, string activationId, int positiveAttemptNumber) : base(BaseStudioResourceKind.ActivationAttempt, applicationId, Text("activationId", activationId), Positive("positiveAttemptNumber", positiveAttemptNumber)) { } }
/// <summary>Identifies one effect.</summary>
public sealed record BaseStudioEffectResource : BaseStudioResourceIdentity
{ public BaseStudioEffectResource(string applicationId, string activationId, int attemptNumber, string effectId) : base(BaseStudioResourceKind.Effect, applicationId, Text("activationId", activationId), Positive("attemptNumber", attemptNumber), Text("effectId", effectId)) { ActivationId = activationId; AttemptNumber = attemptNumber; EffectId = effectId; }
  /// <summary>Gets the owning activation identity.</summary>
  public string ActivationId { get; }
  /// <summary>Gets the owning attempt number.</summary>
  public int AttemptNumber { get; }
  /// <summary>Gets the opaque effect identity.</summary>
  public string EffectId { get; } }
/// <summary>Identifies one executor incarnation.</summary>
public sealed record BaseStudioExecutorResource : BaseStudioResourceIdentity
{ public BaseStudioExecutorResource(string applicationId, string hostId, string processIncarnationId, long executorGeneration) : base(BaseStudioResourceKind.Executor, applicationId, Text("hostId", hostId), Text("processIncarnationId", processIncarnationId), Positive("executorGeneration", executorGeneration)) { HostId = hostId; ProcessIncarnationId = processIncarnationId; ExecutorGeneration = executorGeneration; }
  /// <summary>Gets the host identity.</summary>
  public string HostId { get; }
  /// <summary>Gets the process-incarnation identity.</summary>
  public string ProcessIncarnationId { get; }
  /// <summary>Gets the executor generation.</summary>
  public long ExecutorGeneration { get; } }
/// <summary>Identifies one exported subject contract.</summary>
public sealed record BaseStudioSubjectContractResource : BaseStudioResourceIdentity
{ public BaseStudioSubjectContractResource(string applicationId, string contractId, int contractVersion) : base(BaseStudioResourceKind.SubjectContract, applicationId, Id("contractId", contractId), Positive("contractVersion", contractVersion)) { ContractId = contractId; ContractVersion = contractVersion; }
  /// <summary>Gets the exported contract identity.</summary>
  public string ContractId { get; }
  /// <summary>Gets the exported contract version.</summary>
  public int ContractVersion { get; } }
/// <summary>Identifies one protected subject.</summary>
public sealed record BaseStudioSubjectResource : BaseStudioResourceIdentity
{ public BaseStudioSubjectResource(string applicationId, string contractId, int contractVersion, string protectedSubjectIdentity) : base(BaseStudioResourceKind.Subject, applicationId, Id("contractId", contractId), Positive("contractVersion", contractVersion), Text("protectedSubjectIdentity", protectedSubjectIdentity)) { ContractId = contractId; ContractVersion = contractVersion; ProtectedSubjectIdentity = protectedSubjectIdentity; }
  /// <summary>Gets the exported contract identity.</summary>
  public string ContractId { get; }
  /// <summary>Gets the exported contract version.</summary>
  public int ContractVersion { get; }
  /// <summary>Gets the protected subject identity.</summary>
  public string ProtectedSubjectIdentity { get; } }
/// <summary>Identifies one lifecycle consumer definition.</summary>
public sealed record BaseStudioLifecycleConsumerResource : BaseStudioResourceIdentity
{ public BaseStudioLifecycleConsumerResource(string applicationId, string consumerId, int version, string contractId, int contractVersion) : base(BaseStudioResourceKind.LifecycleConsumer, applicationId, Id("consumerId", consumerId), Positive("version", version), Id("contractId", contractId), Positive("contractVersion", contractVersion))
  { ConsumerId = consumerId; Version = version; ContractId = contractId; ContractVersion = contractVersion; }
  public string ConsumerId { get; } public int Version { get; } public string ContractId { get; } public int ContractVersion { get; } }
/// <summary>Identifies one lifecycle checkpoint.</summary>
public sealed record BaseStudioLifecycleCheckpointResource : BaseStudioResourceIdentity
{ public BaseStudioLifecycleCheckpointResource(string applicationId, string consumerId, int consumerVersion, string contractId, int contractVersion, string protectedScopeIdentity) : base(BaseStudioResourceKind.LifecycleCheckpoint, applicationId, Id("consumerId", consumerId), Positive("consumerVersion", consumerVersion), Id("contractId", contractId), Positive("contractVersion", contractVersion), Text("protectedScopeIdentity", protectedScopeIdentity))
  { ConsumerId = consumerId; ConsumerVersion = consumerVersion; ContractId = contractId; ContractVersion = contractVersion; ProtectedScopeIdentity = protectedScopeIdentity; }
  public string ConsumerId { get; } public int ConsumerVersion { get; } public string ContractId { get; } public int ContractVersion { get; } public string ProtectedScopeIdentity { get; } }
/// <summary>Identifies one retirement barrier.</summary>
public sealed record BaseStudioRetirementBarrierResource : BaseStudioResourceIdentity
{ public BaseStudioRetirementBarrierResource(string applicationId, string contractId, int contractVersion, string protectedSubjectIdentity, string authorityEpoch, string incarnation) : base(BaseStudioResourceKind.RetirementBarrier, applicationId, Id("contractId", contractId), Positive("contractVersion", contractVersion), Text("protectedSubjectIdentity", protectedSubjectIdentity), Text("authorityEpoch", authorityEpoch), Text("incarnation", incarnation))
  { ContractId = contractId; ContractVersion = contractVersion; ProtectedSubjectIdentity = protectedSubjectIdentity; AuthorityEpoch = authorityEpoch; Incarnation = incarnation; }
  public string ContractId { get; } public int ContractVersion { get; } public string ProtectedSubjectIdentity { get; } public string AuthorityEpoch { get; } public string Incarnation { get; } }
/// <summary>Identifies one text index.</summary>
public sealed record BaseStudioTextIndexResource : BaseStudioResourceIdentity
{ public BaseStudioTextIndexResource(string applicationId, string collectionId, string indexId, int indexVersion) : base(BaseStudioResourceKind.TextIndex, applicationId, Id("collectionId", collectionId), Id("indexId", indexId), Positive("indexVersion", indexVersion)) { CollectionId = collectionId; IndexId = indexId; IndexVersion = indexVersion; }
  public string CollectionId { get; } public string IndexId { get; } public int IndexVersion { get; } }
/// <summary>Identifies one vector index.</summary>
public sealed record BaseStudioVectorIndexResource : BaseStudioResourceIdentity
{ public BaseStudioVectorIndexResource(string applicationId, string collectionId, string indexId, int indexVersion) : base(BaseStudioResourceKind.VectorIndex, applicationId, Id("collectionId", collectionId), Id("indexId", indexId), Positive("indexVersion", indexVersion)) { CollectionId = collectionId; IndexId = indexId; IndexVersion = indexVersion; }
  public string CollectionId { get; } public string IndexId { get; } public int IndexVersion { get; } }
/// <summary>Identifies one search rebuild.</summary>
public sealed record BaseStudioSearchRebuildResource : BaseStudioResourceIdentity
{ public BaseStudioSearchRebuildResource(string applicationId, string searchKind, string collectionId, string indexId, int indexVersion, string rebuildIdentity) : base(BaseStudioResourceKind.SearchRebuild, applicationId, Id("searchKind", searchKind), Id("collectionId", collectionId), Id("indexId", indexId), Positive("indexVersion", indexVersion), Text("rebuildIdentity", rebuildIdentity)) { SearchKind = searchKind; CollectionId = collectionId; IndexId = indexId; IndexVersion = indexVersion; RebuildIdentity = rebuildIdentity; }
  public string SearchKind { get; } public string CollectionId { get; } public string IndexId { get; } public int IndexVersion { get; } public string RebuildIdentity { get; } }
/// <summary>Identifies one policy.</summary>
public sealed record BaseStudioPolicyResource : BaseStudioResourceIdentity
{ public BaseStudioPolicyResource(string applicationId, string policyId, int version) : base(BaseStudioResourceKind.Policy, applicationId, Id("policyId", policyId), Positive("version", version)) { PolicyId = policyId; Version = version; } public string PolicyId { get; } public int Version { get; } }
/// <summary>Identifies one grant.</summary>
public sealed record BaseStudioGrantResource : BaseStudioResourceIdentity
{ public BaseStudioGrantResource(string applicationId, string grantId, int version) : base(BaseStudioResourceKind.Grant, applicationId, Id("grantId", grantId), Positive("version", version)) { GrantId = grantId; Version = version; } public string GrantId { get; } public int Version { get; } }
/// <summary>Identifies one store.</summary>
public sealed record BaseStudioStoreResource : BaseStudioResourceIdentity
{ public BaseStudioStoreResource(string applicationId, string storeIdentity) : base(BaseStudioResourceKind.Store, applicationId, Id("storeIdentity", storeIdentity)) { StoreIdentity = storeIdentity; } public string StoreIdentity { get; } }
/// <summary>Identifies one provider version.</summary>
public sealed record BaseStudioProviderResource : BaseStudioResourceIdentity
{ public BaseStudioProviderResource(string applicationId, string storeIdentity, string providerId, int providerVersion) : base(BaseStudioResourceKind.Provider, applicationId, Id("storeIdentity", storeIdentity), Id("providerId", providerId), Positive("providerVersion", providerVersion)) { StoreIdentity = storeIdentity; ProviderId = providerId; ProviderVersion = providerVersion; } public string StoreIdentity { get; } public string ProviderId { get; } public int ProviderVersion { get; } }
/// <summary>Identifies one provider certification receipt.</summary>
public sealed record BaseStudioCertificationReceiptResource : BaseStudioResourceIdentity
{ public BaseStudioCertificationReceiptResource(string applicationId, string certificationKind, string providerId, int providerVersion, BaseStudioSha256 contractChecksum) : base(BaseStudioResourceKind.CertificationReceipt, applicationId, Id("certificationKind", certificationKind), Id("providerId", providerId), Positive("providerVersion", providerVersion), Checksum("contractChecksum", contractChecksum)) { } }
/// <summary>Identifies one schema generation.</summary>
public sealed record BaseStudioSchemaResource : BaseStudioResourceIdentity
{ public BaseStudioSchemaResource(string applicationId, string storeIdentity, long schemaGeneration) : base(BaseStudioResourceKind.Schema, applicationId, Id("storeIdentity", storeIdentity), Nonnegative("schemaGeneration", schemaGeneration)) { StoreIdentity = storeIdentity; SchemaGeneration = schemaGeneration; } public string StoreIdentity { get; } public long SchemaGeneration { get; } }
/// <summary>Identifies one migration.</summary>
public sealed record BaseStudioMigrationResource : BaseStudioResourceIdentity
{ public BaseStudioMigrationResource(string applicationId, string storeIdentity, string migrationId) : base(BaseStudioResourceKind.Migration, applicationId, Id("storeIdentity", storeIdentity), Text("migrationId", migrationId)) { StoreIdentity = storeIdentity; MigrationId = migrationId; } public string StoreIdentity { get; } public string MigrationId { get; } }
/// <summary>Identifies one backup artifact.</summary>
public sealed record BaseStudioBackupResource : BaseStudioResourceIdentity
{ public BaseStudioBackupResource(string applicationId, string storeIdentity, string artifactId) : base(BaseStudioResourceKind.Backup, applicationId, Id("storeIdentity", storeIdentity), Text("artifactId", artifactId)) { StoreIdentity = storeIdentity; ArtifactId = artifactId; } public string StoreIdentity { get; } public string ArtifactId { get; } }
/// <summary>Identifies one restore request.</summary>
public sealed record BaseStudioRestoreResource : BaseStudioResourceIdentity
{ public BaseStudioRestoreResource(string applicationId, string storeIdentity, string restoreRequestIdentity) : base(BaseStudioResourceKind.Restore, applicationId, Id("storeIdentity", storeIdentity), Text("restoreRequestIdentity", restoreRequestIdentity)) { StoreIdentity = storeIdentity; RestoreRequestIdentity = restoreRequestIdentity; } public string StoreIdentity { get; } public string RestoreRequestIdentity { get; } }
/// <summary>Identifies one maintenance operation.</summary>
public sealed record BaseStudioMaintenanceResource : BaseStudioResourceIdentity
{ public BaseStudioMaintenanceResource(string applicationId, string storeIdentity, string maintenanceKind, string operationIdentity) : base(BaseStudioResourceKind.Maintenance, applicationId, Id("storeIdentity", storeIdentity), Id("maintenanceKind", maintenanceKind), Text("operationIdentity", operationIdentity)) { StoreIdentity = storeIdentity; MaintenanceKind = maintenanceKind; OperationIdentity = operationIdentity; } public string StoreIdentity { get; } public string MaintenanceKind { get; } public string OperationIdentity { get; } }
/// <summary>Identifies one health observation entry.</summary>
public sealed record BaseStudioHealthResource : BaseStudioResourceIdentity
{ public BaseStudioHealthResource(string applicationId, string contributorId, string entryId) : base(BaseStudioResourceKind.Health, applicationId, Id("contributorId", contributorId), Text("entryId", entryId)) { ContributorId = contributorId; EntryId = entryId; } public string ContributorId { get; } public string EntryId { get; } }
/// <summary>Identifies one diagnostic entry.</summary>
public sealed record BaseStudioDiagnosticResource : BaseStudioResourceIdentity
{ public BaseStudioDiagnosticResource(string applicationId, string contributorId, string entryId) : base(BaseStudioResourceKind.Diagnostic, applicationId, Id("contributorId", contributorId), Text("entryId", entryId)) { ContributorId = contributorId; EntryId = entryId; } public string ContributorId { get; } public string EntryId { get; } }
/// <summary>Identifies one quarantine item.</summary>
public sealed record BaseStudioQuarantineItemResource : BaseStudioResourceIdentity
{ public BaseStudioQuarantineItemResource(string applicationId, string quarantineKind, string owningSubsystemId, string quarantineIdentity) : base(BaseStudioResourceKind.QuarantineItem, applicationId, Id("quarantineKind", quarantineKind), Id("owningSubsystemId", owningSubsystemId), Text("quarantineIdentity", quarantineIdentity)) { } }
/// <summary>Identifies one graph definition.</summary>
public sealed record BaseStudioGraphDefinitionResource : BaseStudioResourceIdentity
{ public BaseStudioGraphDefinitionResource(string applicationId, string graphId, string graphVersion) : base(BaseStudioResourceKind.GraphDefinition, applicationId, Id("graphId", graphId), Text("graphVersion", graphVersion)) { GraphId = graphId; GraphVersion = graphVersion; } public string GraphId { get; } public string GraphVersion { get; } }
/// <summary>Identifies one graph execution.</summary>
public sealed record BaseStudioGraphExecutionResource : BaseStudioResourceIdentity
{ public BaseStudioGraphExecutionResource(string applicationId, string graphId, string graphVersion, string executionId) : base(BaseStudioResourceKind.GraphExecution, applicationId, Id("graphId", graphId), Text("graphVersion", graphVersion), Text("executionId", executionId)) { GraphId = graphId; GraphVersion = graphVersion; ExecutionId = executionId; } public string GraphId { get; } public string GraphVersion { get; } public string ExecutionId { get; } }
/// <summary>Identifies one graph node.</summary>
public sealed record BaseStudioGraphNodeResource : BaseStudioResourceIdentity
{ public BaseStudioGraphNodeResource(string applicationId, string graphId, string graphVersion, string executionId, string nodeId) : base(BaseStudioResourceKind.GraphNode, applicationId, Id("graphId", graphId), Text("graphVersion", graphVersion), Text("executionId", executionId), Id("nodeId", nodeId)) { } }
/// <summary>Identifies one graph channel.</summary>
public sealed record BaseStudioGraphChannelResource : BaseStudioResourceIdentity
{ public BaseStudioGraphChannelResource(string applicationId, string graphId, string graphVersion, string executionId, string channelId) : base(BaseStudioResourceKind.GraphChannel, applicationId, Id("graphId", graphId), Text("graphVersion", graphVersion), Text("executionId", executionId), Id("channelId", channelId)) { } }
/// <summary>Identifies one graph checkpoint.</summary>
public sealed record BaseStudioGraphCheckpointResource : BaseStudioResourceIdentity
{
    public BaseStudioGraphCheckpointResource(string applicationId, string graphId, string graphVersion,
        string executionId, string checkpointId)
        : base(BaseStudioResourceKind.GraphCheckpoint, applicationId, Id("graphId", graphId),
            Text("graphVersion", graphVersion), Text("executionId", executionId), Text("checkpointId", checkpointId))
    { GraphId = graphId; GraphVersion = graphVersion; ExecutionId = executionId; CheckpointId = checkpointId; }
    public string GraphId { get; }
    public string GraphVersion { get; }
    public string ExecutionId { get; }
    public string CheckpointId { get; }
}
