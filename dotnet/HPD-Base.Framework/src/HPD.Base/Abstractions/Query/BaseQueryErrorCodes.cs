namespace HPD.Base;

/// <summary>Defines stable ordinary-query cursor failure codes.</summary>
public static class BaseQueryErrorCodes
{
    /// <summary>Identifies a malformed or unauthenticated cursor.</summary>
    public const string CursorInvalid = "base.query.cursor.invalid";
    /// <summary>Identifies a cursor bound to another caller or collection scope.</summary>
    public const string CursorScopeMismatch = "base.query.cursor.scopeMismatch";
    /// <summary>Identifies a cursor bound to another normalized query.</summary>
    public const string CursorQueryMismatch = "base.query.cursor.queryMismatch";
    /// <summary>Identifies an expired cursor or invalidated stable-history purge generation.</summary>
    public const string CursorExpired = "base.query.cursor.expired";
    /// <summary>Identifies an unsupported cursor format version.</summary>
    public const string CursorVersionUnsupported = "base.query.cursor.versionUnsupported";
    /// <summary>Identifies a cursor issued for another schema generation.</summary>
    public const string CursorSchemaChanged = "base.query.cursor.schemaChanged";
    /// <summary>Identifies a cursor issued before provider restoration.</summary>
    public const string CursorRestoreInvalidated = "base.query.cursor.restoreInvalidated";
    /// <summary>Identifies a cursor guarantee the current query can no longer provide.</summary>
    public const string CursorGuaranteeUnavailable = "base.query.cursor.guaranteeUnavailable";
    /// <summary>Identifies an unsupported or mismatched continuation direction.</summary>
    public const string CursorDirectionUnsupported = "base.query.cursor.directionUnsupported";
    /// <summary>Identifies ordering keys too large for a bounded cursor.</summary>
    public const string CursorKeyTooLarge = "base.query.cursor.keyTooLarge";
}
