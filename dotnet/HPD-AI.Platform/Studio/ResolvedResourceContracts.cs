using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace HPD.AI.Platform.Studio;

/// <summary>Contains one disclosed typed link captured with a resolved Studio resource.</summary>
public sealed class StudioResolvedLink
{
    private StudioResolvedLink(StudioResourceIdentity target, StudioLinkRelation relation, string label)
    { Target = target; Relation = relation; Label = label; }
    /// <summary>Gets the server-issued target identity.</summary>
    public StudioResourceIdentity Target { get; }
    /// <summary>Gets the graph-registered relation.</summary>
    public StudioLinkRelation Relation { get; }
    /// <summary>Gets the bounded disclosure-safe label.</summary>
    public string Label { get; }
    /// <summary>Creates one deeply owned resolved link.</summary>
    public static StudioResolvedLink Create(StudioResourceIdentity target, StudioLinkRelation relation, string label)
    {
        ArgumentNullException.ThrowIfNull(target); StudioContractValidation.Enum(relation);
        ArgumentException.ThrowIfNullOrWhiteSpace(label); if (Encoding.UTF8.GetByteCount(label) > 256 || label.Any(char.IsControl))
            throw new ArgumentException("Studio link label is invalid.", nameof(label));
        return new(target, relation, new string(label.AsSpan()));
    }
}

/// <summary>Contains the exact canonical destination route selected by a resource resolver.</summary>
public sealed class StudioResolvedRoute
{
    private StudioResolvedRoute(string pageId, ImmutableSortedDictionary<string, string> parameters,
        ImmutableSortedDictionary<string, string> query)
    { PageId = pageId; Parameters = parameters; Query = query; }
    /// <summary>Gets the registered destination page.</summary>
    public string PageId { get; }
    /// <summary>Gets canonical route parameters, including an opaque server-issued resource token.</summary>
    public ImmutableSortedDictionary<string, string> Parameters { get; }
    /// <summary>Gets canonical registered query values.</summary>
    public ImmutableSortedDictionary<string, string> Query { get; }
    /// <summary>Creates a validated canonical route projection.</summary>
    public static StudioResolvedRoute Create(string pageId, IEnumerable<KeyValuePair<string, string>> parameters,
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
public static class StudioResolvedResourceJson
{
    /// <summary>Encodes one bounded canonical resolved resource result.</summary>
    public static StudioCanonicalJson Encode(StudioResourceIdentity resource, StudioResolvedRoute route,
        IEnumerable<StudioResolvedLink> links, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(resource); ArgumentNullException.ThrowIfNull(route);
        ImmutableArray<StudioResolvedLink> owned = StudioContractValidation.Materialize(links, 128, true, nameof(links));
        var buffer = new System.Buffers.ArrayBufferWriter<byte>(); using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject(); writer.WriteString("kind", "resolved"); writer.WritePropertyName("links"); writer.WriteStartArray(); foreach (StudioResolvedLink link in owned)
        { writer.WriteStartObject(); writer.WriteString("label", link.Label); writer.WriteString("relation", Relation(link.Relation)); writer.WritePropertyName("target"); link.Target.WriteJson(writer); writer.WriteEndObject(); }
        writer.WriteEndArray(); writer.WritePropertyName("resource"); resource.WriteJson(writer);
        writer.WritePropertyName("route"); writer.WriteStartObject(); writer.WriteString("pageId", route.PageId);
        WriteMap(writer, "parameters", route.Parameters); WriteMap(writer, "query", route.Query); writer.WriteEndObject();
        writer.WriteEndObject(); writer.Flush(); return StudioCanonicalJson.Create(buffer.WrittenSpan, maximumBytes);
    }
    private static void WriteMap(Utf8JsonWriter writer, string name, IEnumerable<KeyValuePair<string, string>> values)
    { writer.WritePropertyName(name); writer.WriteStartObject(); foreach (var pair in values) writer.WriteString(pair.Key, pair.Value); writer.WriteEndObject(); }
    private static string Relation(StudioLinkRelation value) => value switch
    {
        StudioLinkRelation.Owns => "owns", StudioLinkRelation.ContainedBy => "containedBy", StudioLinkRelation.Affected => "affected",
        StudioLinkRelation.ProducedBy => "producedBy", StudioLinkRelation.ReceiptFor => "receiptFor", StudioLinkRelation.ScheduledBy => "scheduledBy",
        StudioLinkRelation.OccurrenceOf => "occurrenceOf", StudioLinkRelation.AttemptOf => "attemptOf", StudioLinkRelation.ChildOf => "childOf",
        StudioLinkRelation.References => "references", StudioLinkRelation.LifecycleOf => "lifecycleOf", StudioLinkRelation.Blocks => "blocks",
        StudioLinkRelation.AcknowledgedBy => "acknowledgedBy", StudioLinkRelation.IndexedBy => "indexedBy", StudioLinkRelation.StoredBy => "storedBy",
        StudioLinkRelation.AuthorizedBy => "authorizedBy", StudioLinkRelation.Diagnoses => "diagnoses", StudioLinkRelation.Remediates => "remediates",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}

/// <summary>Encodes server-issued resource identities for typed route resource parameters.</summary>
public static class StudioResourceRouteToken
{
    /// <summary>Encodes an outward identity as a bounded opaque route token.</summary>
    public static string Encode(StudioResourceIdentity resource)
    {
        ArgumentNullException.ThrowIfNull(resource); var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) { resource.WriteJson(writer); writer.Flush(); }
        return Convert.ToBase64String(buffer.WrittenSpan).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Decodes and fixed-time validates one active BASE resource route token.</summary>
    public static bool TryDecode(string? token, out StudioResourceIdentity? resource)
    {
        resource = null; if (string.IsNullOrWhiteSpace(token) || token.Length > 8_192) return false;
        try
        {
            string value = token.Replace('-', '+').Replace('_', '/'); value += new string('=', (4 - value.Length % 4) % 4);
            byte[] bytes = Convert.FromBase64String(value); using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
            JsonElement root = document.RootElement; if (root.ValueKind != JsonValueKind.Object) return false;
            string kind = Required(root, "kind"); string applicationId = Required(root, "applicationId");
            StudioResourceIdentity candidate = kind switch
            {
                "application" when Exact(root) => new StudioApplicationResource(applicationId),
                "module" when Exact(root, "moduleId", "moduleVersion") => new StudioModuleResource(applicationId, S("moduleId"), I("moduleVersion")),
                "collection" when Exact(root, "collectionId", "installedCollectionChecksum") => new StudioCollectionResource(applicationId, S("collectionId"), H("installedCollectionChecksum")),
                "record" when Exact(root, "collectionId", "installedCollectionChecksum", "recordId") => new StudioRecordResource(applicationId, S("collectionId"), H("installedCollectionChecksum"), S("recordId")),
                "relation" when Exact(root, "sourceCollectionId", "sourceRecordId", "fieldEdgeId", "targetCollectionId", "targetRecordId") => new StudioRelationResource(applicationId, S("sourceCollectionId"), S("sourceRecordId"), S("fieldEdgeId"), S("targetCollectionId"), S("targetRecordId")),
                "fileBucket" when Exact(root, "bucketId") => new StudioFileBucketResource(applicationId, S("bucketId")),
                "file" when Exact(root, "bucketId", "objectId") => new StudioFileResource(applicationId, S("bucketId"), S("objectId")),
                "registeredRead" when Exact(root, "readId", "version") => new StudioRegisteredReadResource(applicationId, S("readId"), I("version")),
                "selectionOperation" when Exact(root, "profileId", "version") => new StudioSelectionOperationResource(applicationId, S("profileId"), I("version")),
                "moduleMutation" when Exact(root, "operationId", "version") => new StudioModuleMutationResource(applicationId, S("operationId"), I("version")),
                "operationExecution" when Exact(root, "operationKind", "operationId", "requestIdentity") => new StudioOperationExecutionResource(applicationId, S("operationKind"), S("operationId"), S("requestIdentity")),
                "receipt" when Exact(root, "receiptKind", "operationId", "requestIdentity") => new StudioReceiptResource(applicationId, S("receiptKind"), S("operationId"), S("requestIdentity")),
                "activationDefinition" when Exact(root, "definitionId", "version") => new StudioActivationDefinitionResource(applicationId, S("definitionId"), I("version")),
                "activation" when Exact(root, "definitionId", "version", "activationId") => new StudioActivationResource(applicationId, S("definitionId"), I("version"), S("activationId")),
                "schedule" when Exact(root, "scheduleId", "version") => new StudioScheduleResource(applicationId, S("scheduleId"), I("version")),
                "occurrence" when Exact(root, "scheduleId", "version", "occurrenceId") => new StudioOccurrenceResource(applicationId, S("scheduleId"), I("version"), S("occurrenceId")),
                "activationAttempt" when Exact(root, "activationId", "positiveAttemptNumber") => new StudioActivationAttemptResource(applicationId, S("activationId"), I("positiveAttemptNumber")),
                "effect" when Exact(root, "activationId", "attemptNumber", "effectId") => new StudioEffectResource(applicationId, S("activationId"), I("attemptNumber"), S("effectId")),
                "executor" when Exact(root, "hostId", "processIncarnationId", "executorGeneration") => new StudioExecutorResource(applicationId, S("hostId"), S("processIncarnationId"), L("executorGeneration")),
                "subjectContract" when Exact(root, "contractId", "contractVersion") => new StudioSubjectContractResource(applicationId, S("contractId"), I("contractVersion")),
                "subject" when Exact(root, "contractId", "contractVersion", "protectedSubjectIdentity") => new StudioSubjectResource(applicationId, S("contractId"), I("contractVersion"), S("protectedSubjectIdentity")),
                "lifecycleConsumer" when Exact(root, "consumerId", "version", "contractId", "contractVersion") => new StudioLifecycleConsumerResource(applicationId, S("consumerId"), I("version"), S("contractId"), I("contractVersion")),
                "lifecycleCheckpoint" when Exact(root, "consumerId", "consumerVersion", "contractId", "contractVersion", "protectedScopeIdentity") => new StudioLifecycleCheckpointResource(applicationId, S("consumerId"), I("consumerVersion"), S("contractId"), I("contractVersion"), S("protectedScopeIdentity")),
                "retirementBarrier" when Exact(root, "authorityEpoch", "contractId", "contractVersion", "incarnation", "protectedSubjectIdentity") => new StudioRetirementBarrierResource(applicationId, S("contractId"), I("contractVersion"), S("protectedSubjectIdentity"), S("authorityEpoch"), S("incarnation")),
                "textIndex" when Exact(root, "collectionId", "indexId", "indexVersion") => new StudioTextIndexResource(applicationId, S("collectionId"), S("indexId"), I("indexVersion")),
                "vectorIndex" when Exact(root, "collectionId", "indexId", "indexVersion") => new StudioVectorIndexResource(applicationId, S("collectionId"), S("indexId"), I("indexVersion")),
                "searchRebuild" when Exact(root, "searchKind", "collectionId", "indexId", "indexVersion", "rebuildIdentity") => new StudioSearchRebuildResource(applicationId, S("searchKind"), S("collectionId"), S("indexId"), I("indexVersion"), S("rebuildIdentity")),
                "certificationReceipt" when Exact(root, "certificationKind", "providerId", "providerVersion", "contractChecksum") => new StudioCertificationReceiptResource(applicationId, S("certificationKind"), S("providerId"), I("providerVersion"), H("contractChecksum")),
                "policy" when Exact(root, "policyId", "version") => new StudioPolicyResource(applicationId, S("policyId"), I("version")),
                "grant" when Exact(root, "grantId", "version") => new StudioGrantResource(applicationId, S("grantId"), I("version")),
                "store" when Exact(root, "storeIdentity") => new StudioStoreResource(applicationId, S("storeIdentity")),
                "provider" when Exact(root, "storeIdentity", "providerId", "providerVersion") => new StudioProviderResource(applicationId, S("storeIdentity"), S("providerId"), I("providerVersion")),
                "schema" when Exact(root, "storeIdentity", "schemaGeneration") => new StudioSchemaResource(applicationId, S("storeIdentity"), L("schemaGeneration")),
                "migration" when Exact(root, "storeIdentity", "migrationId") => new StudioMigrationResource(applicationId, S("storeIdentity"), S("migrationId")),
                "backup" when Exact(root, "storeIdentity", "artifactId") => new StudioBackupResource(applicationId, S("storeIdentity"), S("artifactId")),
                "restore" when Exact(root, "storeIdentity", "restoreRequestIdentity") => new StudioRestoreResource(applicationId, S("storeIdentity"), S("restoreRequestIdentity")),
                "maintenance" when Exact(root, "storeIdentity", "maintenanceKind", "operationIdentity") => new StudioMaintenanceResource(applicationId, S("storeIdentity"), S("maintenanceKind"), S("operationIdentity")),
                "health" when Exact(root, "contributorId", "entryId") => new StudioHealthResource(applicationId, S("contributorId"), S("entryId")),
                "diagnostic" when Exact(root, "contributorId", "entryId") => new StudioDiagnosticResource(applicationId, S("contributorId"), S("entryId")),
                "quarantineItem" when Exact(root, "quarantineKind", "owningSubsystemId", "quarantineIdentity") => new StudioQuarantineItemResource(applicationId, S("quarantineKind"), S("owningSubsystemId"), S("quarantineIdentity")),
                "graphDefinition" when Exact(root, "graphId", "graphVersion") => new StudioGraphDefinitionResource(applicationId, S("graphId"), S("graphVersion")),
                "graphExecution" when Exact(root, "graphId", "graphVersion", "executionId") => new StudioGraphExecutionResource(applicationId, S("graphId"), S("graphVersion"), S("executionId")),
                "graphNode" when Exact(root, "graphId", "graphVersion", "executionId", "nodeId") => new StudioGraphNodeResource(applicationId, S("graphId"), S("graphVersion"), S("executionId"), S("nodeId")),
                "graphChannel" when Exact(root, "graphId", "graphVersion", "executionId", "channelId") => new StudioGraphChannelResource(applicationId, S("graphId"), S("graphVersion"), S("executionId"), S("channelId")),
                "graphCheckpoint" when Exact(root, "graphId", "graphVersion", "executionId", "checkpointId") => new StudioGraphCheckpointResource(applicationId, S("graphId"), S("graphVersion"), S("executionId"), S("checkpointId")),
                "agent" when Exact(root, "agentId") => new StudioAgentResource(applicationId, S("agentId")),
                "agentSession" when Exact(root, "sessionId") => new StudioAgentSessionResource(applicationId, S("sessionId")),
                "agentThread" when Exact(root, "sessionId", "threadId") => new StudioAgentThreadResource(applicationId, S("sessionId"), S("threadId")),
                "agentExecution" when Exact(root, "sessionId", "threadId", "threadExecutionId") => new StudioAgentExecutionResource(applicationId, S("sessionId"), S("threadId"), S("threadExecutionId")),
                "toolHarness" when Exact(root, "harnessId") => new StudioToolHarnessResource(applicationId, S("harnessId")),
                "agentTrace" when Exact(root, "traceId") => new StudioAgentTraceResource(applicationId, S("traceId")),
                "aiFunction" when Exact(root, "harnessId", "functionId") => new StudioAIFunctionResource(applicationId, S("harnessId"), S("functionId")),
                "aiFunctionAction" when Exact(root, "harnessId", "functionId", "actionId") =>
                    new StudioAIFunctionActionResource(applicationId, S("harnessId"), S("functionId"), S("actionId")),
                _ => throw new JsonException(),
            };
            string authority = Required(root, "authorityChecksum");
            if (authority.Length != 64 || authority.Any(static value => value is not (>= '0' and <= '9' or >= 'a' and <= 'f'))) return false;
            byte[] digest = Convert.FromHexString(authority);
            resource = StudioSha256.FixedTimeEquals(candidate.AuthorityChecksum, StudioSha256.FromDigest(digest)) ? candidate : null;
            if (resource is null || !StringComparer.Ordinal.Equals(token, Encode(resource))) { resource = null; return false; }
            return true;
            string S(string name) => Required(root, name);
            int I(string name) => RequiredInt(root, name);
            long L(string name) => RequiredLong(root, name);
            StudioSha256 H(string name) => RequiredChecksum(root, name);
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
    private static StudioSha256 RequiredChecksum(JsonElement root, string name)
    { string value = Required(root, name); return value.Length == 64 && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f')
        ? StudioSha256.FromDigest(Convert.FromHexString(value)) : throw new JsonException(); }
    private static bool Exact(JsonElement root, params string[] members)
    {
        string[] expected = ["applicationId", "authorityChecksum", "kind", .. members]; Array.Sort(expected, StringComparer.Ordinal);
        return root.EnumerateObject().Select(static value => value.Name).SequenceEqual(expected, StringComparer.Ordinal);
    }
}
