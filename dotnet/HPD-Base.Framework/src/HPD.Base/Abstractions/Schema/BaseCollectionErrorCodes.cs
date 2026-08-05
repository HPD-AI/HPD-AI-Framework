namespace HPD.Base;

/// <summary>Defines stable collection mutation-mode and purge failures.</summary>
public static class BaseCollectionErrorCodes
{
    /// <summary>The collection mutation mode is invalid.</summary>
    public const string MutationModeInvalid = "base.collection.mutationMode.invalid";
    /// <summary>An update was attempted against append-only history.</summary>
    public const string AppendOnlyUpdateForbidden = "base.collection.appendOnly.updateForbidden";
    /// <summary>An ordinary delete was attempted against append-only history.</summary>
    public const string AppendOnlyDeleteForbidden = "base.collection.appendOnly.deleteForbidden";
    /// <summary>An ordinary mutation was attempted against a read-only collection.</summary>
    public const string ReadOnlyMutationForbidden = "base.collection.readOnly.mutationForbidden";
    /// <summary>The collection does not support administrative purge.</summary>
    public const string PurgeUnsupported = "base.collection.purge.unsupported";
    /// <summary>The purge request is invalid.</summary>
    public const string PurgeInvalid = "base.collection.purge.invalid";
    /// <summary>The purge request is not authorized.</summary>
    public const string PurgeForbidden = "base.collection.purge.forbidden";
    /// <summary>A restrictive relation prevents purge.</summary>
    public const string PurgeRestricted = "base.collection.purge.restricted";
    /// <summary>The expected purge generation does not match.</summary>
    public const string PurgeGenerationConflict = "base.collection.purge.generationConflict";
    /// <summary>The purge failed with confirmed rollback.</summary>
    public const string PurgeFailed = "base.collection.purge.failed";
    /// <summary>The provider cannot determine whether purge committed.</summary>
    public const string PurgeIndeterminate = "base.collection.purge.indeterminate";
}
