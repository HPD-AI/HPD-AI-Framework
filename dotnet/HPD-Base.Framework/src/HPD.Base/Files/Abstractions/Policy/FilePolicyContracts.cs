
namespace HPD.Base;

/// <summary>Defines the ifile policy orchestrator contract.</summary>
public interface IFilePolicyOrchestrator
{
    /// <summary>Executes the evaluate async operation.</summary>
    ValueTask<OperationResult<FilePolicyEvaluation>> EvaluateAsync(FilePolicyRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Represents a file policy request.</summary>
public sealed record FilePolicyRequest
{
    /// <summary>Gets or sets the action.</summary>
    public required string Action { get; init; }
    /// <summary>Gets or sets the resource.</summary>
    public required FilePolicyResource Resource { get; init; }
    /// <summary>Gets or sets the context.</summary>
    public required FileOperationContext Context { get; init; }
}

/// <summary>Represents a file policy resource.</summary>
public sealed record FilePolicyResource
{
    /// <summary>Gets or sets the bucket.</summary>
    public required FileBucketDescriptor Bucket { get; init; }
    /// <summary>Gets or sets the object ID.</summary>
    public FileObjectId? ObjectId { get; init; }
    /// <summary>Gets or sets the object key.</summary>
    public FileObjectKey? ObjectKey { get; init; }
}

/// <summary>Represents a file policy evaluation.</summary>
public sealed record FilePolicyEvaluation
{
    /// <summary>Gets or sets the allowed.</summary>
    public required bool Allowed { get; init; }
    /// <summary>Gets or sets the reason.</summary>
    public string? Reason { get; init; }
}

/// <summary>Represents a file policy actions.</summary>
public static class FilePolicyActions
{
    /// <summary>Provides the upload value.</summary>
    public const string Upload = "files.object.upload";
    /// <summary>Provides the download value.</summary>
    public const string Download = "files.object.download";
    /// <summary>Provides the metadata read value.</summary>
    public const string MetadataRead = "files.object.metadata.read";
    /// <summary>Provides the delete value.</summary>
    public const string Delete = "files.object.delete";
    /// <summary>Provides the list value.</summary>
    public const string List = "files.object.list";
}
