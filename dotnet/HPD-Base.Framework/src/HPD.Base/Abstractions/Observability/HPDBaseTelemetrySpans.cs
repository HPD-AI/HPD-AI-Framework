namespace HPD.Base;

/// <summary>
/// Defines stable BASE span names.
/// </summary>
public static class HPDBaseTelemetrySpans
{
    /// <summary>Runtime list records span.</summary>
    public const string RuntimeRecordsList = "hpd.base.runtime.records.list";
    /// <summary>Runtime get record span.</summary>
    public const string RuntimeRecordsGet = "hpd.base.runtime.records.get";
    /// <summary>Runtime create record span.</summary>
    public const string RuntimeRecordsCreate = "hpd.base.runtime.records.create";
    /// <summary>Runtime patch record span.</summary>
    public const string RuntimeRecordsPatch = "hpd.base.runtime.records.patch";
    /// <summary>Runtime replace record span.</summary>
    public const string RuntimeRecordsReplace = "hpd.base.runtime.records.replace";
    /// <summary>Runtime delete record span.</summary>
    public const string RuntimeRecordsDelete = "hpd.base.runtime.records.delete";
    /// <summary>Runtime policy evaluation span.</summary>
    public const string RuntimePolicyEvaluate = "hpd.base.runtime.policy.evaluate";
    /// <summary>Runtime store invocation span.</summary>
    public const string RuntimeStoreInvoke = "hpd.base.runtime.store.invoke";
    /// <summary>Runtime mutation event dispatch span.</summary>
    public const string RuntimeEventsDispatch = "hpd.base.runtime.events.dispatch";
    /// <summary>Runtime schema read span.</summary>
    public const string RuntimeSchemaGet = "hpd.base.runtime.schema.get";
    /// <summary>Runtime collection schema read span.</summary>
    public const string RuntimeSchemaCollectionGet = "hpd.base.runtime.schema.collection.get";
    /// <summary>Runtime capabilities read span.</summary>
    public const string RuntimeCapabilitiesGet = "hpd.base.runtime.capabilities.get";
    /// <summary>Runtime manifest read span.</summary>
    public const string RuntimeDescriptorsManifestGet = "hpd.base.runtime.descriptors.manifest.get";
    /// <summary>Runtime expanded manifest read span.</summary>
    public const string RuntimeDescriptorsManifestExpand = "hpd.base.runtime.descriptors.manifest.expand";
    /// <summary>Runtime health read span.</summary>
    public const string RuntimeHealthGet = "hpd.base.runtime.health.get";
    /// <summary>Runtime diagnostics read span.</summary>
    public const string RuntimeDiagnosticsGet = "hpd.base.runtime.diagnostics.get";
    /// <summary>Runtime policy explain span.</summary>
    public const string RuntimePolicyExplain = "hpd.base.runtime.policy.explain";
    /// <summary>Runtime validation span.</summary>
    public const string RuntimeValidate = "hpd.base.runtime.validate";

    /// <summary>Universal store list span.</summary>
    public const string StoreList = "hpd.base.store.list";
    /// <summary>Universal store get span.</summary>
    public const string StoreGet = "hpd.base.store.get";
    /// <summary>Universal store create span.</summary>
    public const string StoreCreate = "hpd.base.store.create";
    /// <summary>Universal store patch span.</summary>
    public const string StorePatch = "hpd.base.store.patch";
    /// <summary>Universal store replace span.</summary>
    public const string StoreReplace = "hpd.base.store.replace";
    /// <summary>Universal store delete span.</summary>
    public const string StoreDelete = "hpd.base.store.delete";
    /// <summary>Universal store patch-if-revision span.</summary>
    public const string StorePatchIfRevision = "hpd.base.store.patch_if_revision";
    /// <summary>Universal store replace-if-revision span.</summary>
    public const string StoreReplaceIfRevision = "hpd.base.store.replace_if_revision";
    /// <summary>Universal store stream-open span.</summary>
    public const string StoreStreamOpen = "hpd.base.store.stream.open";

    /// <summary>SQLite connection open span.</summary>
    public const string SqliteConnectionOpen = "hpd.base.sqlite.connection.open";
    /// <summary>SQLite schema initialization span.</summary>
    public const string SqliteSchemaInitialize = "hpd.base.sqlite.schema.initialize";
    /// <summary>SQLite schema validation span.</summary>
    public const string SqliteSchemaValidate = "hpd.base.sqlite.schema.validate";
    /// <summary>SQLite query plan span.</summary>
    public const string SqliteQueryPlan = "hpd.base.sqlite.query.plan";
    /// <summary>SQLite transaction span.</summary>
    public const string SqliteTransaction = "hpd.base.sqlite.transaction";

    /// <summary>File upload span.</summary>
    public const string FilesObjectUpload = "hpd.base.files.object.upload";
    /// <summary>File download-open span.</summary>
    public const string FilesObjectDownloadOpen = "hpd.base.files.object.download.open";
    /// <summary>File metadata-read span.</summary>
    public const string FilesObjectMetadataGet = "hpd.base.files.object.metadata.get";
    /// <summary>File delete span.</summary>
    public const string FilesObjectDelete = "hpd.base.files.object.delete";
    /// <summary>File list span.</summary>
    public const string FilesObjectList = "hpd.base.files.object.list";
    /// <summary>File provider upload span.</summary>
    public const string FilesProviderUpload = "hpd.base.files.provider.upload";
    /// <summary>File provider download-open span.</summary>
    public const string FilesProviderDownloadOpen = "hpd.base.files.provider.download.open";
    /// <summary>File provider metadata-read span.</summary>
    public const string FilesProviderMetadataGet = "hpd.base.files.provider.metadata.get";
    /// <summary>File provider delete span.</summary>
    public const string FilesProviderDelete = "hpd.base.files.provider.delete";
    /// <summary>File provider list span.</summary>
    public const string FilesProviderList = "hpd.base.files.provider.list";

    /// <summary>Realtime WebSocket accept span.</summary>
    public const string RealtimeWebSocketAccept = "hpd.base.realtime.websocket.accept";
    /// <summary>Realtime connection startup span.</summary>
    public const string RealtimeConnection = "hpd.base.realtime.connection";
    /// <summary>Realtime channel join span.</summary>
    public const string RealtimeChannelJoin = "hpd.base.realtime.channel.join";
    /// <summary>Realtime channel leave span.</summary>
    public const string RealtimeChannelLeave = "hpd.base.realtime.channel.leave";
    /// <summary>Realtime event projection span.</summary>
    public const string RealtimeEventProject = "hpd.base.realtime.event.project";
    /// <summary>Realtime event send span.</summary>
    public const string RealtimeEventSend = "hpd.base.realtime.event.send";

    /// <summary>HPD.Auth principal mapping span.</summary>
    public const string AuthPrincipalMap = "hpd.base.auth.hpd_auth.principal.map";
    /// <summary>HPD.Auth principal enrichment span.</summary>
    public const string AuthPrincipalEnrich = "hpd.base.auth.hpd_auth.principal.enrich";
    /// <summary>HPD.Auth policy evaluation span.</summary>
    public const string AuthPolicyEvaluate = "hpd.base.auth.hpd_auth.policy.evaluate";
    /// <summary>HPD.Auth grant resolution span.</summary>
    public const string AuthGrantsResolve = "hpd.base.auth.hpd_auth.grants.resolve";
    /// <summary>HPD.Auth host integration check span.</summary>
    public const string AuthHostCheck = "hpd.base.auth.hpd_auth.host.check";
}
