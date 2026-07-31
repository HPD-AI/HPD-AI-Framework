
namespace HPD.Base;

public interface IFilePolicyOrchestrator
{
    ValueTask<OperationResult<FilePolicyEvaluation>> EvaluateAsync(FilePolicyRequest request, CancellationToken cancellationToken = default);
}

public sealed record FilePolicyRequest
{
    public required string Action { get; init; }
    public required FilePolicyResource Resource { get; init; }
    public required FileOperationContext Context { get; init; }
}

public sealed record FilePolicyResource
{
    public required FileBucketDescriptor Bucket { get; init; }
    public FileObjectId? ObjectId { get; init; }
    public FileObjectKey? ObjectKey { get; init; }
}

public sealed record FilePolicyEvaluation
{
    public required bool Allowed { get; init; }
    public string? Reason { get; init; }
}

public static class FilePolicyActions
{
    public const string Upload = "files.object.upload";
    public const string Download = "files.object.download";
    public const string MetadataRead = "files.object.metadata.read";
    public const string Delete = "files.object.delete";
    public const string List = "files.object.list";
}
