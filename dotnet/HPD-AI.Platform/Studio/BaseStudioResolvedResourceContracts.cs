using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace HPD.AI.Platform.Studio;

/// <summary>Contains one disclosed typed link captured with a resolved Studio resource.</summary>
public sealed class BaseStudioResolvedLink
{
    private BaseStudioResolvedLink(BaseStudioResourceIdentity target, BaseStudioLinkRelation relation, string label)
    { Target = target; Relation = relation; Label = label; }
    /// <summary>Gets the server-issued target identity.</summary>
    public BaseStudioResourceIdentity Target { get; }
    /// <summary>Gets the graph-registered relation.</summary>
    public BaseStudioLinkRelation Relation { get; }
    /// <summary>Gets the bounded disclosure-safe label.</summary>
    public string Label { get; }
    /// <summary>Creates one deeply owned resolved link.</summary>
    public static BaseStudioResolvedLink Create(BaseStudioResourceIdentity target, BaseStudioLinkRelation relation, string label)
    {
        ArgumentNullException.ThrowIfNull(target); StudioContractValidation.Enum(relation);
        ArgumentException.ThrowIfNullOrWhiteSpace(label); if (Encoding.UTF8.GetByteCount(label) > 256 || label.Any(char.IsControl))
            throw new ArgumentException("Studio link label is invalid.", nameof(label));
        return new(target, relation, new string(label.AsSpan()));
    }
}

/// <summary>Contains the exact canonical destination route selected by a resource resolver.</summary>
public sealed class BaseStudioResolvedRoute
{
    private BaseStudioResolvedRoute(string pageId, ImmutableSortedDictionary<string, string> parameters,
        ImmutableSortedDictionary<string, string> query)
    { PageId = pageId; Parameters = parameters; Query = query; }
    /// <summary>Gets the registered destination page.</summary>
    public string PageId { get; }
    /// <summary>Gets canonical route parameters, including an opaque server-issued resource token.</summary>
    public ImmutableSortedDictionary<string, string> Parameters { get; }
    /// <summary>Gets canonical registered query values.</summary>
    public ImmutableSortedDictionary<string, string> Query { get; }
    /// <summary>Creates a validated canonical route projection.</summary>
    public static BaseStudioResolvedRoute Create(string pageId, IEnumerable<KeyValuePair<string, string>> parameters,
        IEnumerable<KeyValuePair<string, string>>? query = null)
    {
        StudioContractValidation.Id(pageId);
        return new(pageId, Own(parameters, 16, nameof(parameters)), Own(query ?? [], 16, nameof(query)));
    }
    private static ImmutableSortedDictionary<string, string> Own(IEnumerable<KeyValuePair<string, string>> source, int maximum, string parameter)
    {
        ArgumentNullException.ThrowIfNull(source, parameter); var builder = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var pair in source)
        {
            if (builder.Count == maximum || !builder.TryAdd(Valid(pair.Key), Valid(pair.Value)))
                throw new ArgumentException("Studio route state is not canonical.", parameter);
        }
        return builder.ToImmutable();
        static string Valid(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value); if (Encoding.UTF8.GetByteCount(value) > 2_048 || value.Any(char.IsControl))
                throw new ArgumentException("Studio route state is invalid."); return new(value.AsSpan());
        }
    }
}

/// <summary>Encodes exact successful resolver output. Absence is represented only by non-enumerating HTTP 404.</summary>
public static class BaseStudioResolvedResourceJson
{
    /// <summary>Encodes one bounded canonical resolved resource result.</summary>
    public static BaseStudioCanonicalJson Encode(BaseStudioResourceIdentity resource, BaseStudioResolvedRoute route,
        IEnumerable<BaseStudioResolvedLink> links, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(resource); ArgumentNullException.ThrowIfNull(route);
        ImmutableArray<BaseStudioResolvedLink> owned = StudioContractValidation.Materialize(links, 128, true, nameof(links));
        var buffer = new System.Buffers.ArrayBufferWriter<byte>(); using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject(); writer.WriteString("kind", "resolved"); writer.WritePropertyName("links"); writer.WriteStartArray(); foreach (BaseStudioResolvedLink link in owned)
        { writer.WriteStartObject(); writer.WriteString("label", link.Label); writer.WriteString("relation", Relation(link.Relation)); writer.WritePropertyName("target"); link.Target.WriteJson(writer); writer.WriteEndObject(); }
        writer.WriteEndArray(); writer.WritePropertyName("resource"); resource.WriteJson(writer);
        writer.WritePropertyName("route"); writer.WriteStartObject(); writer.WriteString("pageId", route.PageId);
        WriteMap(writer, "parameters", route.Parameters); WriteMap(writer, "query", route.Query); writer.WriteEndObject();
        writer.WriteEndObject(); writer.Flush(); return BaseStudioCanonicalJson.Create(buffer.WrittenSpan, maximumBytes);
    }
    private static void WriteMap(Utf8JsonWriter writer, string name, IEnumerable<KeyValuePair<string, string>> values)
    { writer.WritePropertyName(name); writer.WriteStartObject(); foreach (var pair in values) writer.WriteString(pair.Key, pair.Value); writer.WriteEndObject(); }
    private static string Relation(BaseStudioLinkRelation value) => value switch
    {
        BaseStudioLinkRelation.Owns => "owns", BaseStudioLinkRelation.ContainedBy => "containedBy", BaseStudioLinkRelation.Affected => "affected",
        BaseStudioLinkRelation.ProducedBy => "producedBy", BaseStudioLinkRelation.ReceiptFor => "receiptFor", BaseStudioLinkRelation.ScheduledBy => "scheduledBy",
        BaseStudioLinkRelation.OccurrenceOf => "occurrenceOf", BaseStudioLinkRelation.AttemptOf => "attemptOf", BaseStudioLinkRelation.ChildOf => "childOf",
        BaseStudioLinkRelation.References => "references", BaseStudioLinkRelation.LifecycleOf => "lifecycleOf", BaseStudioLinkRelation.Blocks => "blocks",
        BaseStudioLinkRelation.AcknowledgedBy => "acknowledgedBy", BaseStudioLinkRelation.IndexedBy => "indexedBy", BaseStudioLinkRelation.StoredBy => "storedBy",
        BaseStudioLinkRelation.AuthorizedBy => "authorizedBy", BaseStudioLinkRelation.Diagnoses => "diagnoses", BaseStudioLinkRelation.Remediates => "remediates",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}

/// <summary>Encodes server-issued resource identities for typed route resource parameters.</summary>
public static class BaseStudioResourceRouteToken
{
    /// <summary>Encodes an outward identity as a bounded opaque route token.</summary>
    public static string Encode(BaseStudioResourceIdentity resource)
    {
        ArgumentNullException.ThrowIfNull(resource); var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) { resource.WriteJson(writer); writer.Flush(); }
        return Convert.ToBase64String(buffer.WrittenSpan).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Decodes and fixed-time validates one active BASE resource route token.</summary>
    public static bool TryDecode(string? token, out BaseStudioResourceIdentity? resource)
    {
        resource = null; if (string.IsNullOrWhiteSpace(token) || token.Length > 8_192) return false;
        try
        {
            string value = token.Replace('-', '+').Replace('_', '/'); value += new string('=', (4 - value.Length % 4) % 4);
            byte[] bytes = Convert.FromBase64String(value); using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
            JsonElement root = document.RootElement; if (root.ValueKind != JsonValueKind.Object) return false;
            string kind = Required(root, "kind"); string applicationId = Required(root, "applicationId");
            BaseStudioResourceIdentity candidate = kind switch
            {
                "application" when Exact(root) => new BaseStudioApplicationResource(applicationId),
                "module" when Exact(root, "moduleId", "moduleVersion") => new BaseStudioModuleResource(applicationId, S("moduleId"), I("moduleVersion")),
                "collection" when Exact(root, "collectionId", "installedCollectionChecksum") => new BaseStudioCollectionResource(applicationId, S("collectionId"), H("installedCollectionChecksum")),
                "record" when Exact(root, "collectionId", "installedCollectionChecksum", "recordId") => new BaseStudioRecordResource(applicationId, S("collectionId"), H("installedCollectionChecksum"), S("recordId")),
                "relation" when Exact(root, "sourceCollectionId", "sourceRecordId", "fieldEdgeId", "targetCollectionId", "targetRecordId") => new BaseStudioRelationResource(applicationId, S("sourceCollectionId"), S("sourceRecordId"), S("fieldEdgeId"), S("targetCollectionId"), S("targetRecordId")),
                "fileBucket" when Exact(root, "bucketId") => new BaseStudioFileBucketResource(applicationId, S("bucketId")),
                "file" when Exact(root, "bucketId", "objectId") => new BaseStudioFileResource(applicationId, S("bucketId"), S("objectId")),
                "registeredRead" when Exact(root, "readId", "version") => new BaseStudioRegisteredReadResource(applicationId, S("readId"), I("version")),
                "selectionOperation" when Exact(root, "profileId", "version") => new BaseStudioSelectionOperationResource(applicationId, S("profileId"), I("version")),
                "moduleMutation" when Exact(root, "operationId", "version") => new BaseStudioModuleMutationResource(applicationId, S("operationId"), I("version")),
                "operationExecution" when Exact(root, "operationKind", "operationId", "requestIdentity") => new BaseStudioOperationExecutionResource(applicationId, S("operationKind"), S("operationId"), S("requestIdentity")),
                "receipt" when Exact(root, "receiptKind", "operationId", "requestIdentity") => new BaseStudioReceiptResource(applicationId, S("receiptKind"), S("operationId"), S("requestIdentity")),
                "activationDefinition" when Exact(root, "definitionId", "version") => new BaseStudioActivationDefinitionResource(applicationId, S("definitionId"), I("version")),
                "activation" when Exact(root, "definitionId", "version", "activationId") => new BaseStudioActivationResource(applicationId, S("definitionId"), I("version"), S("activationId")),
                "schedule" when Exact(root, "scheduleId", "version") => new BaseStudioScheduleResource(applicationId, S("scheduleId"), I("version")),
                "occurrence" when Exact(root, "scheduleId", "version", "occurrenceId") => new BaseStudioOccurrenceResource(applicationId, S("scheduleId"), I("version"), S("occurrenceId")),
                "activationAttempt" when Exact(root, "activationId", "positiveAttemptNumber") => new BaseStudioActivationAttemptResource(applicationId, S("activationId"), I("positiveAttemptNumber")),
                "effect" when Exact(root, "activationId", "attemptNumber", "effectId") => new BaseStudioEffectResource(applicationId, S("activationId"), I("attemptNumber"), S("effectId")),
                "executor" when Exact(root, "hostId", "processIncarnationId", "executorGeneration") => new BaseStudioExecutorResource(applicationId, S("hostId"), S("processIncarnationId"), L("executorGeneration")),
                "subjectContract" when Exact(root, "contractId", "contractVersion") => new BaseStudioSubjectContractResource(applicationId, S("contractId"), I("contractVersion")),
                "subject" when Exact(root, "contractId", "contractVersion", "protectedSubjectIdentity") => new BaseStudioSubjectResource(applicationId, S("contractId"), I("contractVersion"), S("protectedSubjectIdentity")),
                "lifecycleConsumer" when Exact(root, "consumerId", "version", "contractId", "contractVersion") => new BaseStudioLifecycleConsumerResource(applicationId, S("consumerId"), I("version"), S("contractId"), I("contractVersion")),
                "lifecycleCheckpoint" when Exact(root, "consumerId", "consumerVersion", "contractId", "contractVersion", "protectedScopeIdentity") => new BaseStudioLifecycleCheckpointResource(applicationId, S("consumerId"), I("consumerVersion"), S("contractId"), I("contractVersion"), S("protectedScopeIdentity")),
                "retirementBarrier" when Exact(root, "authorityEpoch", "contractId", "contractVersion", "incarnation", "protectedSubjectIdentity") => new BaseStudioRetirementBarrierResource(applicationId, S("contractId"), I("contractVersion"), S("protectedSubjectIdentity"), S("authorityEpoch"), S("incarnation")),
                "textIndex" when Exact(root, "collectionId", "indexId", "indexVersion") => new BaseStudioTextIndexResource(applicationId, S("collectionId"), S("indexId"), I("indexVersion")),
                "vectorIndex" when Exact(root, "collectionId", "indexId", "indexVersion") => new BaseStudioVectorIndexResource(applicationId, S("collectionId"), S("indexId"), I("indexVersion")),
                "searchRebuild" when Exact(root, "searchKind", "collectionId", "indexId", "indexVersion", "rebuildIdentity") => new BaseStudioSearchRebuildResource(applicationId, S("searchKind"), S("collectionId"), S("indexId"), I("indexVersion"), S("rebuildIdentity")),
                "certificationReceipt" when Exact(root, "certificationKind", "providerId", "providerVersion", "contractChecksum") => new BaseStudioCertificationReceiptResource(applicationId, S("certificationKind"), S("providerId"), I("providerVersion"), H("contractChecksum")),
                "policy" when Exact(root, "policyId", "version") => new BaseStudioPolicyResource(applicationId, S("policyId"), I("version")),
                "grant" when Exact(root, "grantId", "version") => new BaseStudioGrantResource(applicationId, S("grantId"), I("version")),
                "store" when Exact(root, "storeIdentity") => new BaseStudioStoreResource(applicationId, S("storeIdentity")),
                "provider" when Exact(root, "storeIdentity", "providerId", "providerVersion") => new BaseStudioProviderResource(applicationId, S("storeIdentity"), S("providerId"), I("providerVersion")),
                "schema" when Exact(root, "storeIdentity", "schemaGeneration") => new BaseStudioSchemaResource(applicationId, S("storeIdentity"), L("schemaGeneration")),
                "migration" when Exact(root, "storeIdentity", "migrationId") => new BaseStudioMigrationResource(applicationId, S("storeIdentity"), S("migrationId")),
                "backup" when Exact(root, "storeIdentity", "artifactId") => new BaseStudioBackupResource(applicationId, S("storeIdentity"), S("artifactId")),
                "restore" when Exact(root, "storeIdentity", "restoreRequestIdentity") => new BaseStudioRestoreResource(applicationId, S("storeIdentity"), S("restoreRequestIdentity")),
                "maintenance" when Exact(root, "storeIdentity", "maintenanceKind", "operationIdentity") => new BaseStudioMaintenanceResource(applicationId, S("storeIdentity"), S("maintenanceKind"), S("operationIdentity")),
                "health" when Exact(root, "contributorId", "entryId") => new BaseStudioHealthResource(applicationId, S("contributorId"), S("entryId")),
                "diagnostic" when Exact(root, "contributorId", "entryId") => new BaseStudioDiagnosticResource(applicationId, S("contributorId"), S("entryId")),
                "quarantineItem" when Exact(root, "quarantineKind", "owningSubsystemId", "quarantineIdentity") => new BaseStudioQuarantineItemResource(applicationId, S("quarantineKind"), S("owningSubsystemId"), S("quarantineIdentity")),
                "graphDefinition" when Exact(root, "graphId", "graphVersion") => new BaseStudioGraphDefinitionResource(applicationId, S("graphId"), S("graphVersion")),
                "graphExecution" when Exact(root, "graphId", "graphVersion", "executionId") => new BaseStudioGraphExecutionResource(applicationId, S("graphId"), S("graphVersion"), S("executionId")),
                "graphNode" when Exact(root, "graphId", "graphVersion", "executionId", "nodeId") => new BaseStudioGraphNodeResource(applicationId, S("graphId"), S("graphVersion"), S("executionId"), S("nodeId")),
                "graphChannel" when Exact(root, "graphId", "graphVersion", "executionId", "channelId") => new BaseStudioGraphChannelResource(applicationId, S("graphId"), S("graphVersion"), S("executionId"), S("channelId")),
                "graphCheckpoint" when Exact(root, "graphId", "graphVersion", "executionId", "checkpointId") => new BaseStudioGraphCheckpointResource(applicationId, S("graphId"), S("graphVersion"), S("executionId"), S("checkpointId")),
                _ => throw new JsonException(),
            };
            string authority = Required(root, "authorityChecksum");
            if (authority.Length != 64 || authority.Any(static value => value is not (>= '0' and <= '9' or >= 'a' and <= 'f'))) return false;
            byte[] digest = Convert.FromHexString(authority);
            resource = BaseStudioSha256.FixedTimeEquals(candidate.AuthorityChecksum, BaseStudioSha256.FromDigest(digest)) ? candidate : null;
            if (resource is null || !StringComparer.Ordinal.Equals(token, Encode(resource))) { resource = null; return false; }
            return true;
            string S(string name) => Required(root, name);
            int I(string name) => RequiredInt(root, name);
            long L(string name) => RequiredLong(root, name);
            BaseStudioSha256 H(string name) => RequiredChecksum(root, name);
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException or OverflowException) { return false; }
    }

    private static string Required(JsonElement root, string name)
    { JsonElement value = root.GetProperty(name); return value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new JsonException(); }
    private static int RequiredInt(JsonElement root, string name)
    { JsonElement value = root.GetProperty(name); return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result) ? result : throw new JsonException(); }
    private static long RequiredLong(JsonElement root, string name)
    { JsonElement value = root.GetProperty(name); return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(),
        System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long result) ? result : throw new JsonException(); }
    private static BaseStudioSha256 RequiredChecksum(JsonElement root, string name)
    { string value = Required(root, name); return value.Length == 64 && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f')
        ? BaseStudioSha256.FromDigest(Convert.FromHexString(value)) : throw new JsonException(); }
    private static bool Exact(JsonElement root, params string[] members)
    {
        string[] expected = ["applicationId", "authorityChecksum", "kind", .. members]; Array.Sort(expected, StringComparer.Ordinal);
        return root.EnumerateObject().Select(static value => value.Name).SequenceEqual(expected, StringComparer.Ordinal);
    }
}
