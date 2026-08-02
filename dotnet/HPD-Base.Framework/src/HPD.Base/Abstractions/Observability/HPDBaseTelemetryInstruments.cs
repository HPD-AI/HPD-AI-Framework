namespace HPD.Base;

/// <summary>
/// Defines stable BASE metric instrument names.
/// </summary>
public static class HPDBaseTelemetryInstruments
{
    /// <summary>Relational read/include attempt counter.</summary>
    public const string RelationalAttempts = "hpd.base.relational.attempts";
    /// <summary>Relational read/include duration histogram.</summary>
    public const string RelationalDuration = "hpd.base.relational.duration";
    /// <summary>Schema lifecycle attempt counter.</summary>
    public const string SchemaAttempts = "hpd.base.schema.attempts";
    /// <summary>Schema lifecycle duration histogram.</summary>
    public const string SchemaDuration = "hpd.base.schema.duration";
    /// <summary>Runtime operation counter.</summary>
    public const string RuntimeOperations = "hpd.base.runtime.operations";
    /// <summary>Runtime operation duration histogram.</summary>
    public const string RuntimeOperationDuration = "hpd.base.runtime.operation.duration";
    /// <summary>Runtime failure counter.</summary>
    public const string RuntimeFailures = "hpd.base.runtime.failures";
    /// <summary>Runtime policy evaluation counter.</summary>
    public const string RuntimePolicyEvaluations = "hpd.base.runtime.policy.evaluations";
    /// <summary>Runtime policy duration histogram.</summary>
    public const string RuntimePolicyDuration = "hpd.base.runtime.policy.duration";
    /// <summary>Runtime validation failure counter.</summary>
    public const string RuntimeValidationFailures = "hpd.base.runtime.validation.failures";
    /// <summary>Runtime store invocation counter.</summary>
    public const string RuntimeStoreInvocations = "hpd.base.runtime.store.invocations";
    /// <summary>Runtime store invocation duration histogram.</summary>
    public const string RuntimeStoreDuration = "hpd.base.runtime.store.duration";
    /// <summary>Runtime event dispatch counter.</summary>
    public const string RuntimeEventsDispatched = "hpd.base.runtime.events.dispatched";
    /// <summary>Runtime event dispatch duration histogram.</summary>
    public const string RuntimeEventsDispatchDuration = "hpd.base.runtime.events.dispatch.duration";
    /// <summary>Runtime health read counter.</summary>
    public const string RuntimeHealthReads = "hpd.base.runtime.health.reads";
    /// <summary>Runtime diagnostics read counter.</summary>
    public const string RuntimeDiagnosticsReads = "hpd.base.runtime.diagnostics.reads";

    /// <summary>Store operation counter.</summary>
    public const string StoreOperations = "hpd.base.store.operations";
    /// <summary>Store operation duration histogram.</summary>
    public const string StoreOperationDuration = "hpd.base.store.operation.duration";

    /// <summary>SQLite connections opened counter.</summary>
    public const string SqliteConnectionsOpened = "hpd.base.sqlite.connections.opened";
    /// <summary>SQLite connection open duration histogram.</summary>
    public const string SqliteConnectionOpenDuration = "hpd.base.sqlite.connection.open.duration";
    /// <summary>SQLite schema initialization counter.</summary>
    public const string SqliteSchemaInitializations = "hpd.base.sqlite.schema.initializations";
    /// <summary>SQLite missing provider-owned schema parts gauge.</summary>
    public const string SqliteSchemaMissingParts = "hpd.base.sqlite.schema.missing_parts";
    /// <summary>SQLite query plan counter.</summary>
    public const string SqliteQueryPlans = "hpd.base.sqlite.query.plans";
    /// <summary>SQLite error counter.</summary>
    public const string SqliteErrors = "hpd.base.sqlite.errors";

    /// <summary>Files operation counter.</summary>
    public const string FilesOperations = "hpd.base.files.operations";
    /// <summary>Files operation duration histogram.</summary>
    public const string FilesOperationDuration = "hpd.base.files.operation.duration";
    /// <summary>Files provider operation counter.</summary>
    public const string FilesProviderOperations = "hpd.base.files.provider.operations";
    /// <summary>Files provider operation duration histogram.</summary>
    public const string FilesProviderDuration = "hpd.base.files.provider.duration";
    /// <summary>Files upload bytes histogram.</summary>
    public const string FilesUploadBytes = "hpd.base.files.upload.bytes";
    /// <summary>Files download bytes histogram.</summary>
    public const string FilesDownloadBytes = "hpd.base.files.download.bytes";
    /// <summary>Files policy evaluation counter.</summary>
    public const string FilesPolicyEvaluations = "hpd.base.files.policy.evaluations";
    /// <summary>Files validation failure counter.</summary>
    public const string FilesValidationFailures = "hpd.base.files.validation.failures";

    /// <summary>Realtime active connections gauge.</summary>
    public const string RealtimeConnectionsActive = "hpd.base.realtime.connections.active";
    /// <summary>Realtime active channels gauge.</summary>
    public const string RealtimeChannelsActive = "hpd.base.realtime.channels.active";
    /// <summary>Realtime opened connections counter.</summary>
    public const string RealtimeConnectionsOpened = "hpd.base.realtime.connections.opened";
    /// <summary>Realtime closed connections counter.</summary>
    public const string RealtimeConnectionsClosed = "hpd.base.realtime.connections.closed";
    /// <summary>Realtime opened channels counter.</summary>
    public const string RealtimeChannelsOpened = "hpd.base.realtime.channels.opened";
    /// <summary>Realtime closed channels counter.</summary>
    public const string RealtimeChannelsClosed = "hpd.base.realtime.channels.closed";
    /// <summary>Realtime received messages counter.</summary>
    public const string RealtimeMessagesReceived = "hpd.base.realtime.messages.received";
    /// <summary>Realtime sent messages counter.</summary>
    public const string RealtimeMessagesSent = "hpd.base.realtime.messages.sent";
    /// <summary>Realtime projected events counter.</summary>
    public const string RealtimeEventsProjected = "hpd.base.realtime.events.projected";
    /// <summary>Realtime policy skips counter.</summary>
    public const string RealtimePolicySkips = "hpd.base.realtime.policy.skips";
    /// <summary>Realtime stream-open failure counter.</summary>
    public const string RealtimeStreamOpenFailures = "hpd.base.realtime.stream.open_failures";
    /// <summary>Realtime send failure counter.</summary>
    public const string RealtimeSendFailures = "hpd.base.realtime.send.failures";
    /// <summary>Realtime receive-idle timeout counter.</summary>
    public const string RealtimeReceiveIdleTimeouts = "hpd.base.realtime.receive_idle.timeouts";
    /// <summary>Realtime join-rate rejection counter.</summary>
    public const string RealtimeJoinRateRejections = "hpd.base.realtime.join.rate_rejections";
    /// <summary>Realtime slow-consumer termination counter.</summary>
    public const string RealtimeSlowConsumerTerminations = "hpd.base.realtime.consumer.slow_terminations";
    /// <summary>Realtime payload drop counter.</summary>
    public const string RealtimePayloadDrops = "hpd.base.realtime.payload.drops";
    /// <summary>Realtime durable journal read counter.</summary>
    public const string RealtimeDurableJournalReads = "hpd.base.realtime.durable.journal_reads";
    /// <summary>Realtime durable projected event counter.</summary>
    public const string RealtimeDurableEventsProjected = "hpd.base.realtime.durable.events_projected";
    /// <summary>Realtime durable cursor rejection counter.</summary>
    public const string RealtimeDurableCursorRejections = "hpd.base.realtime.durable.cursor_rejections";
    /// <summary>Realtime message bytes histogram.</summary>
    public const string RealtimeMessageBytes = "hpd.base.realtime.message.bytes";
    /// <summary>Realtime channel join duration histogram.</summary>
    public const string RealtimeJoinDuration = "hpd.base.realtime.join.duration";

    /// <summary>HPD.Auth principal map counter.</summary>
    public const string AuthPrincipalMaps = "hpd.base.auth.principal.maps";
    /// <summary>HPD.Auth principal map duration histogram.</summary>
    public const string AuthPrincipalMapDuration = "hpd.base.auth.principal_map.duration";
    /// <summary>HPD.Auth policy evaluation counter.</summary>
    public const string AuthPolicyEvaluations = "hpd.base.auth.policy.evaluations";
    /// <summary>HPD.Auth policy duration histogram.</summary>
    public const string AuthPolicyDuration = "hpd.base.auth.policy.duration";
    /// <summary>HPD.Auth policy denial counter.</summary>
    public const string AuthPolicyDenials = "hpd.base.auth.policy.denials";
    /// <summary>HPD.Auth grant provider call counter.</summary>
    public const string AuthGrantProviderCalls = "hpd.base.auth.grant_provider.calls";
    /// <summary>HPD.Auth grants matched histogram.</summary>
    public const string AuthGrantsMatched = "hpd.base.auth.grants.matched";
    /// <summary>HPD.Auth bypass counter.</summary>
    public const string AuthBypasses = "hpd.base.auth.bypasses";
}
