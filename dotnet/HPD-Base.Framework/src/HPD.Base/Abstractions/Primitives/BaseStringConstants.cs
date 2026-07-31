namespace HPD.Base;

public static class BaseCollectionKinds
{
    public const string Document = "document";
    public const string KeyValue = "keyValue";
    public const string Relational = "relational";
    public const string Event = "event";
    public const string Custom = "custom";
}

public static class BaseFieldTypes
{
    public const string String = "string";
    public const string Boolean = "boolean";
    public const string Integer = "integer";
    public const string Number = "number";
    public const string Decimal = "decimal";
    public const string DateTime = "dateTime";
    public const string Id = "id";
    public const string Object = "object";
    public const string Array = "array";
    public const string File = "file";
    public const string Custom = "custom";
}

public static class BaseFieldFormats
{
    public const string Date = "date";
    public const string Time = "time";
    public const string DateTime = "dateTime";
    public const string Email = "email";
    public const string Uri = "uri";
    public const string Uuid = "uuid";
    public const string Json = "json";
}

public static class BaseErrorCodes
{
    public const string ValidationFailed = "validation.failed";
    public const string NotFound = "notFound";
    public const string Conflict = "conflict";
    public const string PolicyDenied = "policy.denied";
    public const string Unauthorized = "unauthorized";
    public const string Unsupported = "unsupported";
    public const string CapabilityUnavailable = "capability.unavailable";
    public const string StoreError = "store.error";
}

public static class BaseGrantActions
{
    public const string Read = "read";
    public const string List = "list";
    public const string Get = "get";
    public const string Create = "create";
    public const string Update = "update";
    public const string Delete = "delete";
    public const string Manage = "manage";
    public const string SchemaRead = "schema.read";
    public const string SchemaWrite = "schema.write";
    public const string FileRead = "file.read";
    public const string FileWrite = "file.write";
}

public static class BaseObligationKinds
{
    public const string Audit = "audit";
    public const string Redact = "redact";
    public const string RequireTenant = "requireTenant";
    public const string RequireMfa = "requireMfa";
}

public static class BaseAuthSources
{
    public const string Anonymous = "anonymous";
    public const string Local = "local";
    public const string HpdAuth = "hpd.auth";
    public const string Service = "service";
    public const string System = "system";
}

public static class BaseStoreKinds
{
    public const string Volatile = "volatile";
    public const string Document = "document";
    public const string Relational = "relational";
    public const string KeyValue = "keyValue";
    public const string Custom = "custom";
}
