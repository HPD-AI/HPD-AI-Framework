namespace HPD.Base;

/// <summary>Represents a base collection kinds.</summary>
public static class BaseCollectionKinds
{
    /// <summary>Provides the document value.</summary>
    public const string Document = "document";
    /// <summary>Provides the key value value.</summary>
    public const string KeyValue = "keyValue";
    /// <summary>Provides the relational value.</summary>
    public const string Relational = "relational";
    /// <summary>Provides the event value.</summary>
    public const string Event = "event";
    /// <summary>Provides the custom value.</summary>
    public const string Custom = "custom";
}

/// <summary>Represents a base field types.</summary>
public static class BaseFieldTypes
{
    /// <summary>Provides the string value.</summary>
    public const string String = "string";
    /// <summary>Provides the boolean value.</summary>
    public const string Boolean = "boolean";
    /// <summary>Provides the integer value.</summary>
    public const string Integer = "integer";
    /// <summary>Provides the number value.</summary>
    public const string Number = "number";
    /// <summary>Provides the decimal value.</summary>
    public const string Decimal = "decimal";
    /// <summary>Provides the date time value.</summary>
    public const string DateTime = "dateTime";
    /// <summary>Provides the ID value.</summary>
    public const string Id = "id";
    /// <summary>Provides the object value.</summary>
    public const string Object = "object";
    /// <summary>Provides the array value.</summary>
    public const string Array = "array";
    /// <summary>Provides the file value.</summary>
    public const string File = "file";
    /// <summary>Provides the custom value.</summary>
    public const string Custom = "custom";
}

/// <summary>Represents a base field formats.</summary>
public static class BaseFieldFormats
{
    /// <summary>Provides the date value.</summary>
    public const string Date = "date";
    /// <summary>Provides the time value.</summary>
    public const string Time = "time";
    /// <summary>Provides the date time value.</summary>
    public const string DateTime = "dateTime";
    /// <summary>Provides the email value.</summary>
    public const string Email = "email";
    /// <summary>Provides the URI value.</summary>
    public const string Uri = "uri";
    /// <summary>Provides the uuid value.</summary>
    public const string Uuid = "uuid";
    /// <summary>Provides the JSON value.</summary>
    public const string Json = "json";
}

/// <summary>Represents a base error codes.</summary>
public static class BaseErrorCodes
{
    /// <summary>Provides the validation failed value.</summary>
    public const string ValidationFailed = "validation.failed";
    /// <summary>Provides the not found value.</summary>
    public const string NotFound = "notFound";
    /// <summary>Provides the conflict value.</summary>
    public const string Conflict = "conflict";
    /// <summary>Provides the policy denied value.</summary>
    public const string PolicyDenied = "policy.denied";
    /// <summary>Provides the unauthorized value.</summary>
    public const string Unauthorized = "unauthorized";
    /// <summary>Provides the unsupported value.</summary>
    public const string Unsupported = "unsupported";
    /// <summary>Provides the capability unavailable value.</summary>
    public const string CapabilityUnavailable = "capability.unavailable";
    /// <summary>Provides the store error value.</summary>
    public const string StoreError = "store.error";
}

/// <summary>Represents a base grant actions.</summary>
public static class BaseGrantActions
{
    /// <summary>Provides the read value.</summary>
    public const string Read = "read";
    /// <summary>Provides the list value.</summary>
    public const string List = "list";
    /// <summary>Provides the get value.</summary>
    public const string Get = "get";
    /// <summary>Provides the create value.</summary>
    public const string Create = "create";
    /// <summary>Provides the update value.</summary>
    public const string Update = "update";
    /// <summary>Provides the delete value.</summary>
    public const string Delete = "delete";
    /// <summary>Provides the manage value.</summary>
    public const string Manage = "manage";
    /// <summary>Provides the schema read value.</summary>
    public const string SchemaRead = "schema.read";
    /// <summary>Provides the schema write value.</summary>
    public const string SchemaWrite = "schema.write";
    /// <summary>Provides the file read value.</summary>
    public const string FileRead = "file.read";
    /// <summary>Provides the file write value.</summary>
    public const string FileWrite = "file.write";
}

/// <summary>Represents a base obligation kinds.</summary>
public static class BaseObligationKinds
{
    /// <summary>Provides the audit value.</summary>
    public const string Audit = "audit";
    /// <summary>Provides the redact value.</summary>
    public const string Redact = "redact";
    /// <summary>Provides the require tenant value.</summary>
    public const string RequireTenant = "requireTenant";
    /// <summary>Provides the require mfa value.</summary>
    public const string RequireMfa = "requireMfa";
}

/// <summary>Represents a base auth sources.</summary>
public static class BaseAuthSources
{
    /// <summary>Provides the anonymous value.</summary>
    public const string Anonymous = "anonymous";
    /// <summary>Provides the local value.</summary>
    public const string Local = "local";
    /// <summary>Provides the hpd auth value.</summary>
    public const string HpdAuth = "hpd.auth";
    /// <summary>Provides the service value.</summary>
    public const string Service = "service";
    /// <summary>Provides the system value.</summary>
    public const string System = "system";
}

/// <summary>Represents a base store kinds.</summary>
public static class BaseStoreKinds
{
    /// <summary>Provides the volatile value.</summary>
    public const string Volatile = "volatile";
    /// <summary>Provides the document value.</summary>
    public const string Document = "document";
    /// <summary>Provides the relational value.</summary>
    public const string Relational = "relational";
    /// <summary>Provides the key value value.</summary>
    public const string KeyValue = "keyValue";
    /// <summary>Provides the custom value.</summary>
    public const string Custom = "custom";
}
