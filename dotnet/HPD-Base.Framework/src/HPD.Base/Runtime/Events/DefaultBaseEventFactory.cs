
namespace HPD.Base;

internal sealed class DefaultBaseEventFactory : IBaseEventFactory
{
    public BaseRecordMutationEvent CreateRecordMutationEvent(
        BaseOperationKind operation,
        OperationContext context,
        PrincipalContext principal,
        CollectionDefinition collection,
        RecordEnvelope? before,
        RecordEnvelope? after,
        string[]? changedFields,
        string? committedEventId)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(collection);

        var record = after ?? before;
        return new BaseRecordMutationEvent
        {
            EventId = committedEventId ?? $"evt_{Guid.NewGuid():N}",
            Type = operation switch
            {
                BaseOperationKind.Create => BaseEventTypes.RecordCreated,
                BaseOperationKind.Patch => BaseEventTypes.RecordPatched,
                BaseOperationKind.Replace => BaseEventTypes.RecordUpdated,
                BaseOperationKind.Delete => BaseEventTypes.RecordDeleted,
                _ => "record.mutated"
            },
            SchemaVersion = BaseEventSchemaVersions.V1,
            Resource = new EventResource
            {
                Kind = EventResourceKind.Record,
                CollectionId = collection.Id,
                RecordId = record?.Id
            },
            Operation = operation,
            Timestamp = context.Now == default ? DateTimeOffset.UtcNow : context.Now,
            TenantId = context.TenantId,
            CorrelationId = context.CorrelationId,
            Principal = new EventPrincipalSummary
            {
                AuthenticationState = principal.AuthenticationState,
                SubjectId = principal.SubjectId,
                SubjectKind = principal.SubjectKind,
                TenantId = principal.CurrentTenantId,
                AuthSource = principal.AuthSource,
                IsAdmin = principal.AuthenticationState == PrincipalAuthenticationState.Admin,
                IsServicePrincipal = principal.SubjectKind == AccessSubjectKind.ServicePrincipal
            },
            ChangedFields = changedFields,
            Before = before is null ? null : Snapshot(before),
            After = after is null ? null : Snapshot(after),
            // The source snapshot remains internal and is never emitted directly. The
            // realtime layer reauthorizes and independently redacts it for each subscriber.
            Visibility = VisibilityLevel.Public
        };
    }

    private static RecordSnapshot Snapshot(RecordEnvelope record) => new()
    {
        CollectionId = record.CollectionId,
        Id = record.Id,
        Payload = record.Payload,
        Metadata = record.Metadata,
        IncludedFields = record.Payload.Kind == RecordPayloadKind.FieldMap
            ? record.Payload.Fields?.Keys.ToArray()
            : null,
        Redacted = record.Policy?.Redacted == true
    };
}
