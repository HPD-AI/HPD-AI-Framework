using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace HPD.AI.Platform.Studio;

/// <summary>Base type of the closed, server-issued Studio resource identity union.</summary>
public abstract record StudioResourceIdentity
{
    private readonly ImmutableArray<ResourceMember> _members;

    private protected StudioResourceIdentity(StudioResourceKind kind, string applicationId,
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
                    case StudioSha256 checksum: writer.Int32(4); writer.Checksum(checksum); break;
                    default: throw new InvalidOperationException("The resource identity member type is not closed.");
                }
            }
        });
    }

    /// <summary>Gets the exact resource discriminator.</summary>
    public StudioResourceKind Kind { get; }
    /// <summary>Gets the owning application identity.</summary>
    public string ApplicationId { get; }
    /// <summary>Gets the purpose-bound resource identity checksum.</summary>
    public StudioSha256 AuthorityChecksum { get; }

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
                case StudioSha256 checksum: writer.WriteString(member.Name, Convert.ToHexString(checksum.ToArray()).ToLowerInvariant()); break;
            }
        }
        writer.WriteEndObject();
    }

    private protected static ResourceMember Text(string name, string value) => new(name, Own(value, name));
    private protected static ResourceMember Id(string name, string value) { StudioContractValidation.Id(value); return new(name, new string(value.AsSpan())); }
    private protected static ResourceMember Positive(string name, int value) => value > 0 ? new(name, value) : throw new ArgumentOutOfRangeException(name);
    private protected static ResourceMember Nonnegative(string name, long value) => value >= 0 ? new(name, value) : throw new ArgumentOutOfRangeException(name);
    private protected static ResourceMember Positive(string name, long value) => value > 0 ? new(name, value) : throw new ArgumentOutOfRangeException(name);
    private protected static ResourceMember Checksum(string name, StudioSha256 value) => new(name, StudioSha256.FromDigest(value?.ToArray() ?? throw new ArgumentNullException(name)));
    private static string Own(string value, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        if (Encoding.UTF8.GetByteCount(value) > 512 || value.Any(static character => char.IsControl(character)))
            throw new ArgumentException("Studio resource identity is invalid.", parameter);
        return new(value.AsSpan());
    }
    internal static string Name(StudioResourceKind kind) => kind switch
    {
        StudioResourceKind.Application => "application", StudioResourceKind.Module => "module",
        StudioResourceKind.Collection => "collection", StudioResourceKind.Record => "record",
        StudioResourceKind.Relation => "relation", StudioResourceKind.FileBucket => "fileBucket",
        StudioResourceKind.File => "file", StudioResourceKind.RegisteredRead => "registeredRead",
        StudioResourceKind.SelectionOperation => "selectionOperation", StudioResourceKind.ModuleMutation => "moduleMutation",
        StudioResourceKind.OperationExecution => "operationExecution", StudioResourceKind.Receipt => "receipt",
        StudioResourceKind.ActivationDefinition => "activationDefinition", StudioResourceKind.Activation => "activation",
        StudioResourceKind.Schedule => "schedule", StudioResourceKind.Occurrence => "occurrence",
        StudioResourceKind.ActivationAttempt => "activationAttempt", StudioResourceKind.Effect => "effect",
        StudioResourceKind.Executor => "executor", StudioResourceKind.SubjectContract => "subjectContract",
        StudioResourceKind.Subject => "subject", StudioResourceKind.LifecycleConsumer => "lifecycleConsumer",
        StudioResourceKind.LifecycleCheckpoint => "lifecycleCheckpoint", StudioResourceKind.RetirementBarrier => "retirementBarrier",
        StudioResourceKind.TextIndex => "textIndex", StudioResourceKind.VectorIndex => "vectorIndex",
        StudioResourceKind.SearchRebuild => "searchRebuild", StudioResourceKind.CertificationReceipt => "certificationReceipt",
        StudioResourceKind.Policy => "policy", StudioResourceKind.Grant => "grant", StudioResourceKind.Store => "store",
        StudioResourceKind.Provider => "provider", StudioResourceKind.Schema => "schema", StudioResourceKind.Migration => "migration",
        StudioResourceKind.Backup => "backup", StudioResourceKind.Restore => "restore", StudioResourceKind.Maintenance => "maintenance",
        StudioResourceKind.Health => "health", StudioResourceKind.Diagnostic => "diagnostic",
        StudioResourceKind.QuarantineItem => "quarantineItem", StudioResourceKind.GraphDefinition => "graphDefinition",
        StudioResourceKind.GraphExecution => "graphExecution", StudioResourceKind.GraphNode => "graphNode",
        StudioResourceKind.GraphChannel => "graphChannel", StudioResourceKind.GraphCheckpoint => "graphCheckpoint",
        StudioResourceKind.Agent => "agent", StudioResourceKind.AgentSession => "agentSession",
        StudioResourceKind.AgentThread => "agentThread", StudioResourceKind.AgentExecution => "agentExecution",
        StudioResourceKind.ToolHarness => "toolHarness", StudioResourceKind.AgentTrace => "agentTrace",
        StudioResourceKind.AiFunction => "aiFunction", StudioResourceKind.AiFunctionAction => "aiFunctionAction",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private protected readonly record struct ResourceMember(string Name, object Value);
}

/// <summary>Identifies the installed application.</summary>
public sealed record StudioApplicationResource : StudioResourceIdentity
{ public StudioApplicationResource(string applicationId) : base(StudioResourceKind.Application, applicationId) { } }
/// <summary>Identifies one installed module version.</summary>
public sealed record StudioModuleResource : StudioResourceIdentity
{ public StudioModuleResource(string applicationId, string moduleId, int moduleVersion) : base(StudioResourceKind.Module, applicationId, Id("moduleId", moduleId), Positive("moduleVersion", moduleVersion)) { ModuleId = moduleId; ModuleVersion = moduleVersion; } public string ModuleId { get; } public int ModuleVersion { get; } }
/// <summary>Identifies one collection version.</summary>
public sealed record StudioCollectionResource : StudioResourceIdentity
{ public StudioCollectionResource(string applicationId, string collectionId, StudioSha256 installedCollectionChecksum) : base(StudioResourceKind.Collection, applicationId, Id("collectionId", collectionId), Checksum("installedCollectionChecksum", installedCollectionChecksum)) { CollectionId = collectionId; InstalledCollectionChecksum = StudioSha256.FromDigest(installedCollectionChecksum.ToArray()); } public string CollectionId { get; } public StudioSha256 InstalledCollectionChecksum { get; } }
/// <summary>Identifies one record in a collection version.</summary>
public sealed record StudioRecordResource : StudioResourceIdentity
{ public StudioRecordResource(string applicationId, string collectionId, StudioSha256 installedCollectionChecksum, string recordId) : base(StudioResourceKind.Record, applicationId, Id("collectionId", collectionId), Checksum("installedCollectionChecksum", installedCollectionChecksum), Text("recordId", recordId)) { CollectionId = collectionId; InstalledCollectionChecksum = StudioSha256.FromDigest(installedCollectionChecksum.ToArray()); RecordId = recordId; } public string CollectionId { get; } public StudioSha256 InstalledCollectionChecksum { get; } public string RecordId { get; } }
/// <summary>Identifies one relation edge.</summary>
public sealed record StudioRelationResource : StudioResourceIdentity
{ public StudioRelationResource(string applicationId, string sourceCollectionId, string sourceRecordId, string fieldEdgeId, string targetCollectionId, string targetRecordId) : base(StudioResourceKind.Relation, applicationId, Id("sourceCollectionId", sourceCollectionId), Text("sourceRecordId", sourceRecordId), Id("fieldEdgeId", fieldEdgeId), Id("targetCollectionId", targetCollectionId), Text("targetRecordId", targetRecordId)) { } }
/// <summary>Identifies one file bucket.</summary>
public sealed record StudioFileBucketResource : StudioResourceIdentity
{ public StudioFileBucketResource(string applicationId, string bucketId) : base(StudioResourceKind.FileBucket, applicationId, Id("bucketId", bucketId)) { } }
/// <summary>Identifies one file object.</summary>
public sealed record StudioFileResource : StudioResourceIdentity
{ public StudioFileResource(string applicationId, string bucketId, string objectId) : base(StudioResourceKind.File, applicationId, Id("bucketId", bucketId), Text("objectId", objectId)) { } }
/// <summary>Identifies one registered read.</summary>
public sealed record StudioRegisteredReadResource : StudioResourceIdentity
{ public StudioRegisteredReadResource(string applicationId, string readId, int version) : base(StudioResourceKind.RegisteredRead, applicationId, Id("readId", readId), Positive("version", version)) { } }
/// <summary>Identifies one registered selection operation.</summary>
public sealed record StudioSelectionOperationResource : StudioResourceIdentity
{ public StudioSelectionOperationResource(string applicationId, string profileId, int version) : base(StudioResourceKind.SelectionOperation, applicationId, Id("profileId", profileId), Positive("version", version)) { } }
/// <summary>Identifies one registered module mutation.</summary>
public sealed record StudioModuleMutationResource : StudioResourceIdentity
{ public StudioModuleMutationResource(string applicationId, string operationId, int version) : base(StudioResourceKind.ModuleMutation, applicationId, Id("operationId", operationId), Positive("version", version)) { } }
/// <summary>Identifies one operation execution.</summary>
public sealed record StudioOperationExecutionResource : StudioResourceIdentity
{
    public StudioOperationExecutionResource(string applicationId, string operationKind, string operationId, string requestIdentity)
        : base(StudioResourceKind.OperationExecution, applicationId, Id("operationKind", operationKind),
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
public sealed record StudioReceiptResource : StudioResourceIdentity
{ public StudioReceiptResource(string applicationId, string receiptKind, string operationId, string requestIdentity) : base(StudioResourceKind.Receipt, applicationId, Id("receiptKind", receiptKind), Id("operationId", operationId), Text("requestIdentity", requestIdentity)) { } }
/// <summary>Identifies one activation definition.</summary>
public sealed record StudioActivationDefinitionResource : StudioResourceIdentity
{ public StudioActivationDefinitionResource(string applicationId, string definitionId, int version) : base(StudioResourceKind.ActivationDefinition, applicationId, Id("definitionId", definitionId), Positive("version", version)) { } }
/// <summary>Identifies one activation.</summary>
public sealed record StudioActivationResource : StudioResourceIdentity
{ public StudioActivationResource(string applicationId, string definitionId, int version, string activationId) : base(StudioResourceKind.Activation, applicationId, Id("definitionId", definitionId), Positive("version", version), Text("activationId", activationId))
  { DefinitionId = definitionId; Version = version; ActivationId = activationId; }
  /// <summary>Gets the installed definition identity.</summary>
  public string DefinitionId { get; }
  /// <summary>Gets the installed definition version.</summary>
  public int Version { get; }
  /// <summary>Gets the durable activation identity.</summary>
  public string ActivationId { get; }
}
/// <summary>Identifies one schedule.</summary>
public sealed record StudioScheduleResource : StudioResourceIdentity
{ public StudioScheduleResource(string applicationId, string scheduleId, int version) : base(StudioResourceKind.Schedule, applicationId, Id("scheduleId", scheduleId), Positive("version", version)) { ScheduleId = scheduleId; Version = version; }
  /// <summary>Gets the installed schedule identity.</summary>
  public string ScheduleId { get; }
  /// <summary>Gets the installed schedule version.</summary>
  public int Version { get; } }
/// <summary>Identifies one schedule occurrence.</summary>
public sealed record StudioOccurrenceResource : StudioResourceIdentity
{ public StudioOccurrenceResource(string applicationId, string scheduleId, int version, string occurrenceId) : base(StudioResourceKind.Occurrence, applicationId, Id("scheduleId", scheduleId), Positive("version", version), Text("occurrenceId", occurrenceId)) { ScheduleId = scheduleId; Version = version; OccurrenceId = occurrenceId; }
  /// <summary>Gets the owning schedule identity.</summary>
  public string ScheduleId { get; }
  /// <summary>Gets the owning schedule version.</summary>
  public int Version { get; }
  /// <summary>Gets the occurrence identity.</summary>
  public string OccurrenceId { get; } }
/// <summary>Identifies one activation attempt.</summary>
public sealed record StudioActivationAttemptResource : StudioResourceIdentity
{ public StudioActivationAttemptResource(string applicationId, string activationId, int positiveAttemptNumber) : base(StudioResourceKind.ActivationAttempt, applicationId, Text("activationId", activationId), Positive("positiveAttemptNumber", positiveAttemptNumber)) { } }
/// <summary>Identifies one effect.</summary>
public sealed record StudioEffectResource : StudioResourceIdentity
{ public StudioEffectResource(string applicationId, string activationId, int attemptNumber, string effectId) : base(StudioResourceKind.Effect, applicationId, Text("activationId", activationId), Positive("attemptNumber", attemptNumber), Text("effectId", effectId)) { ActivationId = activationId; AttemptNumber = attemptNumber; EffectId = effectId; }
  /// <summary>Gets the owning activation identity.</summary>
  public string ActivationId { get; }
  /// <summary>Gets the owning attempt number.</summary>
  public int AttemptNumber { get; }
  /// <summary>Gets the opaque effect identity.</summary>
  public string EffectId { get; } }
/// <summary>Identifies one executor incarnation.</summary>
public sealed record StudioExecutorResource : StudioResourceIdentity
{ public StudioExecutorResource(string applicationId, string hostId, string processIncarnationId, long executorGeneration) : base(StudioResourceKind.Executor, applicationId, Text("hostId", hostId), Text("processIncarnationId", processIncarnationId), Positive("executorGeneration", executorGeneration)) { HostId = hostId; ProcessIncarnationId = processIncarnationId; ExecutorGeneration = executorGeneration; }
  /// <summary>Gets the host identity.</summary>
  public string HostId { get; }
  /// <summary>Gets the process-incarnation identity.</summary>
  public string ProcessIncarnationId { get; }
  /// <summary>Gets the executor generation.</summary>
  public long ExecutorGeneration { get; } }
/// <summary>Identifies one exported subject contract.</summary>
public sealed record StudioSubjectContractResource : StudioResourceIdentity
{ public StudioSubjectContractResource(string applicationId, string contractId, int contractVersion) : base(StudioResourceKind.SubjectContract, applicationId, Id("contractId", contractId), Positive("contractVersion", contractVersion)) { ContractId = contractId; ContractVersion = contractVersion; }
  /// <summary>Gets the exported contract identity.</summary>
  public string ContractId { get; }
  /// <summary>Gets the exported contract version.</summary>
  public int ContractVersion { get; } }
/// <summary>Identifies one protected subject.</summary>
public sealed record StudioSubjectResource : StudioResourceIdentity
{ public StudioSubjectResource(string applicationId, string contractId, int contractVersion, string protectedSubjectIdentity) : base(StudioResourceKind.Subject, applicationId, Id("contractId", contractId), Positive("contractVersion", contractVersion), Text("protectedSubjectIdentity", protectedSubjectIdentity)) { ContractId = contractId; ContractVersion = contractVersion; ProtectedSubjectIdentity = protectedSubjectIdentity; }
  /// <summary>Gets the exported contract identity.</summary>
  public string ContractId { get; }
  /// <summary>Gets the exported contract version.</summary>
  public int ContractVersion { get; }
  /// <summary>Gets the protected subject identity.</summary>
  public string ProtectedSubjectIdentity { get; } }
/// <summary>Identifies one lifecycle consumer definition.</summary>
public sealed record StudioLifecycleConsumerResource : StudioResourceIdentity
{ public StudioLifecycleConsumerResource(string applicationId, string consumerId, int version, string contractId, int contractVersion) : base(StudioResourceKind.LifecycleConsumer, applicationId, Id("consumerId", consumerId), Positive("version", version), Id("contractId", contractId), Positive("contractVersion", contractVersion))
  { ConsumerId = consumerId; Version = version; ContractId = contractId; ContractVersion = contractVersion; }
  public string ConsumerId { get; } public int Version { get; } public string ContractId { get; } public int ContractVersion { get; } }
/// <summary>Identifies one lifecycle checkpoint.</summary>
public sealed record StudioLifecycleCheckpointResource : StudioResourceIdentity
{ public StudioLifecycleCheckpointResource(string applicationId, string consumerId, int consumerVersion, string contractId, int contractVersion, string protectedScopeIdentity) : base(StudioResourceKind.LifecycleCheckpoint, applicationId, Id("consumerId", consumerId), Positive("consumerVersion", consumerVersion), Id("contractId", contractId), Positive("contractVersion", contractVersion), Text("protectedScopeIdentity", protectedScopeIdentity))
  { ConsumerId = consumerId; ConsumerVersion = consumerVersion; ContractId = contractId; ContractVersion = contractVersion; ProtectedScopeIdentity = protectedScopeIdentity; }
  public string ConsumerId { get; } public int ConsumerVersion { get; } public string ContractId { get; } public int ContractVersion { get; } public string ProtectedScopeIdentity { get; } }
/// <summary>Identifies one retirement barrier.</summary>
public sealed record StudioRetirementBarrierResource : StudioResourceIdentity
{ public StudioRetirementBarrierResource(string applicationId, string contractId, int contractVersion, string protectedSubjectIdentity, string authorityEpoch, string incarnation) : base(StudioResourceKind.RetirementBarrier, applicationId, Id("contractId", contractId), Positive("contractVersion", contractVersion), Text("protectedSubjectIdentity", protectedSubjectIdentity), Text("authorityEpoch", authorityEpoch), Text("incarnation", incarnation))
  { ContractId = contractId; ContractVersion = contractVersion; ProtectedSubjectIdentity = protectedSubjectIdentity; AuthorityEpoch = authorityEpoch; Incarnation = incarnation; }
  public string ContractId { get; } public int ContractVersion { get; } public string ProtectedSubjectIdentity { get; } public string AuthorityEpoch { get; } public string Incarnation { get; } }
/// <summary>Identifies one text index.</summary>
public sealed record StudioTextIndexResource : StudioResourceIdentity
{ public StudioTextIndexResource(string applicationId, string collectionId, string indexId, int indexVersion) : base(StudioResourceKind.TextIndex, applicationId, Id("collectionId", collectionId), Id("indexId", indexId), Positive("indexVersion", indexVersion)) { CollectionId = collectionId; IndexId = indexId; IndexVersion = indexVersion; }
  public string CollectionId { get; } public string IndexId { get; } public int IndexVersion { get; } }
/// <summary>Identifies one vector index.</summary>
public sealed record StudioVectorIndexResource : StudioResourceIdentity
{ public StudioVectorIndexResource(string applicationId, string collectionId, string indexId, int indexVersion) : base(StudioResourceKind.VectorIndex, applicationId, Id("collectionId", collectionId), Id("indexId", indexId), Positive("indexVersion", indexVersion)) { CollectionId = collectionId; IndexId = indexId; IndexVersion = indexVersion; }
  public string CollectionId { get; } public string IndexId { get; } public int IndexVersion { get; } }
/// <summary>Identifies one search rebuild.</summary>
public sealed record StudioSearchRebuildResource : StudioResourceIdentity
{ public StudioSearchRebuildResource(string applicationId, string searchKind, string collectionId, string indexId, int indexVersion, string rebuildIdentity) : base(StudioResourceKind.SearchRebuild, applicationId, Id("searchKind", searchKind), Id("collectionId", collectionId), Id("indexId", indexId), Positive("indexVersion", indexVersion), Text("rebuildIdentity", rebuildIdentity)) { SearchKind = searchKind; CollectionId = collectionId; IndexId = indexId; IndexVersion = indexVersion; RebuildIdentity = rebuildIdentity; }
  public string SearchKind { get; } public string CollectionId { get; } public string IndexId { get; } public int IndexVersion { get; } public string RebuildIdentity { get; } }
/// <summary>Identifies one policy.</summary>
public sealed record StudioPolicyResource : StudioResourceIdentity
{ public StudioPolicyResource(string applicationId, string policyId, int version) : base(StudioResourceKind.Policy, applicationId, Id("policyId", policyId), Positive("version", version)) { PolicyId = policyId; Version = version; } public string PolicyId { get; } public int Version { get; } }
/// <summary>Identifies one grant.</summary>
public sealed record StudioGrantResource : StudioResourceIdentity
{ public StudioGrantResource(string applicationId, string grantId, int version) : base(StudioResourceKind.Grant, applicationId, Id("grantId", grantId), Positive("version", version)) { GrantId = grantId; Version = version; } public string GrantId { get; } public int Version { get; } }
/// <summary>Identifies one store.</summary>
public sealed record StudioStoreResource : StudioResourceIdentity
{ public StudioStoreResource(string applicationId, string storeIdentity) : base(StudioResourceKind.Store, applicationId, Id("storeIdentity", storeIdentity)) { StoreIdentity = storeIdentity; } public string StoreIdentity { get; } }
/// <summary>Identifies one provider version.</summary>
public sealed record StudioProviderResource : StudioResourceIdentity
{ public StudioProviderResource(string applicationId, string storeIdentity, string providerId, int providerVersion) : base(StudioResourceKind.Provider, applicationId, Id("storeIdentity", storeIdentity), Id("providerId", providerId), Positive("providerVersion", providerVersion)) { StoreIdentity = storeIdentity; ProviderId = providerId; ProviderVersion = providerVersion; } public string StoreIdentity { get; } public string ProviderId { get; } public int ProviderVersion { get; } }
/// <summary>Identifies one provider certification receipt.</summary>
public sealed record StudioCertificationReceiptResource : StudioResourceIdentity
{ public StudioCertificationReceiptResource(string applicationId, string certificationKind, string providerId, int providerVersion, StudioSha256 contractChecksum) : base(StudioResourceKind.CertificationReceipt, applicationId, Id("certificationKind", certificationKind), Id("providerId", providerId), Positive("providerVersion", providerVersion), Checksum("contractChecksum", contractChecksum)) { } }
/// <summary>Identifies one schema generation.</summary>
public sealed record StudioSchemaResource : StudioResourceIdentity
{ public StudioSchemaResource(string applicationId, string storeIdentity, long schemaGeneration) : base(StudioResourceKind.Schema, applicationId, Id("storeIdentity", storeIdentity), Nonnegative("schemaGeneration", schemaGeneration)) { StoreIdentity = storeIdentity; SchemaGeneration = schemaGeneration; } public string StoreIdentity { get; } public long SchemaGeneration { get; } }
/// <summary>Identifies one migration.</summary>
public sealed record StudioMigrationResource : StudioResourceIdentity
{ public StudioMigrationResource(string applicationId, string storeIdentity, string migrationId) : base(StudioResourceKind.Migration, applicationId, Id("storeIdentity", storeIdentity), Text("migrationId", migrationId)) { StoreIdentity = storeIdentity; MigrationId = migrationId; } public string StoreIdentity { get; } public string MigrationId { get; } }
/// <summary>Identifies one backup artifact.</summary>
public sealed record StudioBackupResource : StudioResourceIdentity
{ public StudioBackupResource(string applicationId, string storeIdentity, string artifactId) : base(StudioResourceKind.Backup, applicationId, Id("storeIdentity", storeIdentity), Text("artifactId", artifactId)) { StoreIdentity = storeIdentity; ArtifactId = artifactId; } public string StoreIdentity { get; } public string ArtifactId { get; } }
/// <summary>Identifies one restore request.</summary>
public sealed record StudioRestoreResource : StudioResourceIdentity
{ public StudioRestoreResource(string applicationId, string storeIdentity, string restoreRequestIdentity) : base(StudioResourceKind.Restore, applicationId, Id("storeIdentity", storeIdentity), Text("restoreRequestIdentity", restoreRequestIdentity)) { StoreIdentity = storeIdentity; RestoreRequestIdentity = restoreRequestIdentity; } public string StoreIdentity { get; } public string RestoreRequestIdentity { get; } }
/// <summary>Identifies one maintenance operation.</summary>
public sealed record StudioMaintenanceResource : StudioResourceIdentity
{ public StudioMaintenanceResource(string applicationId, string storeIdentity, string maintenanceKind, string operationIdentity) : base(StudioResourceKind.Maintenance, applicationId, Id("storeIdentity", storeIdentity), Id("maintenanceKind", maintenanceKind), Text("operationIdentity", operationIdentity)) { StoreIdentity = storeIdentity; MaintenanceKind = maintenanceKind; OperationIdentity = operationIdentity; } public string StoreIdentity { get; } public string MaintenanceKind { get; } public string OperationIdentity { get; } }
/// <summary>Identifies one health observation entry.</summary>
public sealed record StudioHealthResource : StudioResourceIdentity
{ public StudioHealthResource(string applicationId, string contributorId, string entryId) : base(StudioResourceKind.Health, applicationId, Id("contributorId", contributorId), Text("entryId", entryId)) { ContributorId = contributorId; EntryId = entryId; } public string ContributorId { get; } public string EntryId { get; } }
/// <summary>Identifies one diagnostic entry.</summary>
public sealed record StudioDiagnosticResource : StudioResourceIdentity
{ public StudioDiagnosticResource(string applicationId, string contributorId, string entryId) : base(StudioResourceKind.Diagnostic, applicationId, Id("contributorId", contributorId), Text("entryId", entryId)) { ContributorId = contributorId; EntryId = entryId; } public string ContributorId { get; } public string EntryId { get; } }
/// <summary>Identifies one quarantine item.</summary>
public sealed record StudioQuarantineItemResource : StudioResourceIdentity
{ public StudioQuarantineItemResource(string applicationId, string quarantineKind, string owningSubsystemId, string quarantineIdentity) : base(StudioResourceKind.QuarantineItem, applicationId, Id("quarantineKind", quarantineKind), Id("owningSubsystemId", owningSubsystemId), Text("quarantineIdentity", quarantineIdentity)) { } }
/// <summary>Identifies one graph definition.</summary>
public sealed record StudioGraphDefinitionResource : StudioResourceIdentity
{ public StudioGraphDefinitionResource(string applicationId, string graphId, string graphVersion) : base(StudioResourceKind.GraphDefinition, applicationId, Id("graphId", graphId), Text("graphVersion", graphVersion)) { GraphId = graphId; GraphVersion = graphVersion; } public string GraphId { get; } public string GraphVersion { get; } }
/// <summary>Identifies one graph execution.</summary>
public sealed record StudioGraphExecutionResource : StudioResourceIdentity
{ public StudioGraphExecutionResource(string applicationId, string graphId, string graphVersion, string executionId) : base(StudioResourceKind.GraphExecution, applicationId, Id("graphId", graphId), Text("graphVersion", graphVersion), Text("executionId", executionId)) { GraphId = graphId; GraphVersion = graphVersion; ExecutionId = executionId; } public string GraphId { get; } public string GraphVersion { get; } public string ExecutionId { get; } }
/// <summary>Identifies one graph node.</summary>
public sealed record StudioGraphNodeResource : StudioResourceIdentity
{ public StudioGraphNodeResource(string applicationId, string graphId, string graphVersion, string executionId, string nodeId) : base(StudioResourceKind.GraphNode, applicationId, Id("graphId", graphId), Text("graphVersion", graphVersion), Text("executionId", executionId), Id("nodeId", nodeId)) { } }
/// <summary>Identifies one graph channel.</summary>
public sealed record StudioGraphChannelResource : StudioResourceIdentity
{ public StudioGraphChannelResource(string applicationId, string graphId, string graphVersion, string executionId, string channelId) : base(StudioResourceKind.GraphChannel, applicationId, Id("graphId", graphId), Text("graphVersion", graphVersion), Text("executionId", executionId), Id("channelId", channelId)) { } }
/// <summary>Identifies one graph checkpoint.</summary>
public sealed record StudioGraphCheckpointResource : StudioResourceIdentity
{
    public StudioGraphCheckpointResource(string applicationId, string graphId, string graphVersion,
        string executionId, string checkpointId)
        : base(StudioResourceKind.GraphCheckpoint, applicationId, Id("graphId", graphId),
            Text("graphVersion", graphVersion), Text("executionId", executionId), Text("checkpointId", checkpointId))
    { GraphId = graphId; GraphVersion = graphVersion; ExecutionId = executionId; CheckpointId = checkpointId; }
    public string GraphId { get; }
    public string GraphVersion { get; }
    public string ExecutionId { get; }
    public string CheckpointId { get; }
}

/// <summary>Identifies one stored Agent definition.</summary>
public sealed record StudioAgentResource : StudioResourceIdentity
{
    public StudioAgentResource(string applicationId, string agentId)
        : base(StudioResourceKind.Agent, applicationId, Id("agentId", agentId)) { AgentId = agentId; }
    public string AgentId { get; }
}

/// <summary>Identifies one Agent runtime session.</summary>
public sealed record StudioAgentSessionResource : StudioResourceIdentity
{
    public StudioAgentSessionResource(string applicationId, string sessionId)
        : base(StudioResourceKind.AgentSession, applicationId, Text("sessionId", sessionId)) { SessionId = sessionId; }
    public string SessionId { get; }
}

/// <summary>Identifies one durable Agent thread in a session.</summary>
public sealed record StudioAgentThreadResource : StudioResourceIdentity
{
    public StudioAgentThreadResource(string applicationId, string sessionId, string threadId)
        : base(StudioResourceKind.AgentThread, applicationId, Text("sessionId", sessionId), Text("threadId", threadId))
    { SessionId = sessionId; ThreadId = threadId; }
    public string SessionId { get; }
    public string ThreadId { get; }
}

/// <summary>Identifies one thread execution.</summary>
public sealed record StudioAgentExecutionResource : StudioResourceIdentity
{
    public StudioAgentExecutionResource(string applicationId, string sessionId, string threadId, string threadExecutionId)
        : base(StudioResourceKind.AgentExecution, applicationId, Text("sessionId", sessionId), Text("threadId", threadId),
            Text("threadExecutionId", threadExecutionId))
    { SessionId = sessionId; ThreadId = threadId; ThreadExecutionId = threadExecutionId; }
    public string SessionId { get; }
    public string ThreadId { get; }
    public string ThreadExecutionId { get; }
}

/// <summary>Identifies one generated or host-owned ToolHarness.</summary>
public sealed record StudioToolHarnessResource : StudioResourceIdentity
{
    public StudioToolHarnessResource(string applicationId, string harnessId)
        : base(StudioResourceKind.ToolHarness, applicationId, Id("harnessId", harnessId)) { HarnessId = harnessId; }
    public string HarnessId { get; }
}

/// <summary>Identifies one runtime trace.</summary>
public sealed record StudioAgentTraceResource : StudioResourceIdentity
{
    public StudioAgentTraceResource(string applicationId, string traceId)
        : base(StudioResourceKind.AgentTrace, applicationId, Text("traceId", traceId)) { TraceId = traceId; }
    public string TraceId { get; }
}

/// <summary>Identifies one model-facing AIFunction owned by a ToolHarness.</summary>
public sealed record StudioAIFunctionResource : StudioResourceIdentity
{
    public StudioAIFunctionResource(string applicationId, string harnessId, string functionId)
        : base(StudioResourceKind.AiFunction, applicationId, Text("harnessId", harnessId), Id("functionId", functionId))
    { HarnessId = harnessId; FunctionId = functionId; }
    public string HarnessId { get; }
    public string FunctionId { get; }
}

/// <summary>Identifies one statically declared action branch inside an AIFunction.</summary>
public sealed record StudioAIFunctionActionResource : StudioResourceIdentity
{
    public StudioAIFunctionActionResource(string applicationId, string harnessId, string functionId, string actionId)
        : base(StudioResourceKind.AiFunctionAction, applicationId, Text("harnessId", harnessId),
            Id("functionId", functionId), Id("actionId", actionId))
    { HarnessId = harnessId; FunctionId = functionId; ActionId = actionId; }
    public string HarnessId { get; }
    public string FunctionId { get; }
    public string ActionId { get; }
}
