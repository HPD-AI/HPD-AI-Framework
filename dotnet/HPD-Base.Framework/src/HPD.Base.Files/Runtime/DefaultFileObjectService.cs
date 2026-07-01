using HPD.Base.Files.Buckets;
using HPD.Base.Files.Configuration;
using HPD.Base.Files.Objects;
using HPD.Base.Files.Observability;
using HPD.Base.Files.Policy;
using HPD.Base.Files.Providers;
using HPD.Base.Files.Validation;
using HPD.Base.Observability;
using HPD.Base.Results;
using HPD.Base.Runtime.Results;
using Microsoft.Extensions.Options;

namespace HPD.Base.Files.Runtime;

internal sealed class DefaultFileObjectService : IFileObjectService
{
    private readonly HPDBaseFilesOptions _options;
    private readonly IFileBucketRegistry _buckets;
    private readonly IFilePolicyOrchestrator _policy;
    private readonly IFileStorageProviderResolver _providers;
    private readonly IFileObjectKeyValidator _keyValidator;
    private readonly IFileObjectMetadataRedactor _redactor;

    public DefaultFileObjectService(
        IOptions<HPDBaseFilesOptions> options,
        IFileBucketRegistry buckets,
        IFilePolicyOrchestrator policy,
        IFileStorageProviderResolver providers,
        IFileObjectKeyValidator keyValidator,
        IFileObjectMetadataRedactor redactor)
    {
        _options = options.Value;
        _buckets = buckets;
        _policy = policy;
        _providers = providers;
        _keyValidator = keyValidator;
        _redactor = redactor;
    }

    public ValueTask<OperationResult<FileObjectUploadResult>> UploadAsync(FileObjectUploadRequest request, FileOperationContext context, CancellationToken cancellationToken = default) =>
        HPDBaseFilesTelemetry.TraceAsync(
            HPDBaseTelemetrySpans.FilesObjectUpload,
            FileOperationValues.Upload,
            context,
            request.SizeBytes,
            request.Overwrite,
            () => UploadCoreAsync(request, context, cancellationToken));

    private async ValueTask<OperationResult<FileObjectUploadResult>> UploadCoreAsync(FileObjectUploadRequest request, FileOperationContext context, CancellationToken cancellationToken = default)
    {
        if (request.Key is null)
            return Validation<FileObjectUploadResult>("key", "Object key is required for the first scaffold.");

        var resolved = await ResolveAsync<FileObjectUploadResult>(request.BucketId, FilePolicyActions.Upload, context, null, null, cancellationToken);
        if (!resolved.IsSuccess)
            return resolved.Failure;

        var key = _keyValidator.Normalize(request.Key.Value.Value);
        if (!key.IsSuccess())
            return Failure<FileObjectUploadResult>(key);

        request = request with { Key = key.Value };
        var constraint = ValidateUploadConstraints(resolved.Bucket, request);
        if (constraint is not null)
            return constraint;

        var provider = await ResolveProviderAsync<FileObjectUploadResult>(resolved.Bucket, cancellationToken);
        if (!provider.IsSuccess)
            return provider.Failure;

        var result = await provider.Value.UploadAsync(resolved.Bucket, request, context, cancellationToken);
        return result.IsSuccess() && result.Value is not null
            ? result with { Value = result.Value with { Metadata = _redactor.Redact(result.Value.Metadata, resolved.Bucket, context) } }
            : result;
    }

    public ValueTask<OperationResult<FileObjectDownloadResult>> OpenDownloadAsync(FileObjectDownloadRequest request, FileOperationContext context, CancellationToken cancellationToken = default) =>
        HPDBaseFilesTelemetry.TraceAsync(
            HPDBaseTelemetrySpans.FilesObjectDownloadOpen,
            FileOperationValues.DownloadOpen,
            context,
            sizeBytes: null,
            overwriteRequested: null,
            () => OpenDownloadCoreAsync(request, context, cancellationToken));

    private async ValueTask<OperationResult<FileObjectDownloadResult>> OpenDownloadCoreAsync(FileObjectDownloadRequest request, FileOperationContext context, CancellationToken cancellationToken = default)
    {
        var id = ValidateObjectId<FileObjectDownloadResult>(request.ObjectId);
        if (id is not null)
            return id;

        var resolved = await ResolveAsync<FileObjectDownloadResult>(request.BucketId, FilePolicyActions.Download, context, null, request.ObjectId, cancellationToken);
        if (!resolved.IsSuccess)
            return resolved.Failure;

        var provider = await ResolveProviderAsync<FileObjectDownloadResult>(resolved.Bucket, cancellationToken);
        if (!provider.IsSuccess)
            return provider.Failure;

        var result = await provider.Value.OpenDownloadAsync(resolved.Bucket, request, context, cancellationToken);
        return result.IsSuccess() && result.Value is not null
            ? result with { Value = result.Value with { Metadata = _redactor.Redact(result.Value.Metadata, resolved.Bucket, context) } }
            : result;
    }

    public ValueTask<OperationResult<FileObjectMetadata>> GetMetadataAsync(FileObjectMetadataRequest request, FileOperationContext context, CancellationToken cancellationToken = default) =>
        HPDBaseFilesTelemetry.TraceAsync(
            HPDBaseTelemetrySpans.FilesObjectMetadataGet,
            FileOperationValues.MetadataGet,
            context,
            sizeBytes: null,
            overwriteRequested: null,
            () => GetMetadataCoreAsync(request, context, cancellationToken));

    private async ValueTask<OperationResult<FileObjectMetadata>> GetMetadataCoreAsync(FileObjectMetadataRequest request, FileOperationContext context, CancellationToken cancellationToken = default)
    {
        var id = ValidateObjectId<FileObjectMetadata>(request.ObjectId);
        if (id is not null)
            return id;

        var resolved = await ResolveAsync<FileObjectMetadata>(request.BucketId, FilePolicyActions.MetadataRead, context, null, request.ObjectId, cancellationToken);
        if (!resolved.IsSuccess)
            return resolved.Failure;

        var provider = await ResolveProviderAsync<FileObjectMetadata>(resolved.Bucket, cancellationToken);
        if (!provider.IsSuccess)
            return provider.Failure;

        var result = await provider.Value.GetMetadataAsync(resolved.Bucket, request, context, cancellationToken);
        return result.IsSuccess() && result.Value is not null
            ? result with { Value = _redactor.Redact(result.Value, resolved.Bucket, context) }
            : result;
    }

    public ValueTask<OperationResult> DeleteAsync(FileObjectDeleteRequest request, FileOperationContext context, CancellationToken cancellationToken = default) =>
        HPDBaseFilesTelemetry.TraceAsync(
            HPDBaseTelemetrySpans.FilesObjectDelete,
            FileOperationValues.Delete,
            context,
            sizeBytes: null,
            overwriteRequested: null,
            () => DeleteCoreAsync(request, context, cancellationToken));

    private async ValueTask<OperationResult> DeleteCoreAsync(FileObjectDeleteRequest request, FileOperationContext context, CancellationToken cancellationToken = default)
    {
        var id = ValidateObjectId<object>(request.ObjectId);
        if (id is not null)
            return new OperationResult { Status = id.Status, Error = id.Error };

        var resolved = await ResolveAsync<object>(request.BucketId, FilePolicyActions.Delete, context, null, request.ObjectId, cancellationToken);
        if (!resolved.IsSuccess)
            return new OperationResult { Status = resolved.Failure.Status, Error = resolved.Failure.Error };

        var provider = await ResolveProviderAsync<object>(resolved.Bucket, cancellationToken);
        return provider.IsSuccess ? await provider.Value.DeleteAsync(resolved.Bucket, request, context, cancellationToken) : new OperationResult { Status = provider.Failure.Status, Error = provider.Failure.Error };
    }

    public ValueTask<OperationResult<FileObjectListResult>> ListMetadataAsync(FileObjectListRequest request, FileOperationContext context, CancellationToken cancellationToken = default) =>
        HPDBaseFilesTelemetry.TraceAsync(
            HPDBaseTelemetrySpans.FilesObjectList,
            FileOperationValues.List,
            context,
            sizeBytes: null,
            overwriteRequested: null,
            () => ListMetadataCoreAsync(request, context, cancellationToken));

    private async ValueTask<OperationResult<FileObjectListResult>> ListMetadataCoreAsync(FileObjectListRequest request, FileOperationContext context, CancellationToken cancellationToken = default)
    {
        if (request.Prefix is not null)
        {
            var prefix = _keyValidator.Normalize(request.Prefix.Value.Value);
            if (!prefix.IsSuccess())
                return Failure<FileObjectListResult>(prefix);
            request = request with { Prefix = prefix.Value };
        }

        var resolved = await ResolveAsync<FileObjectListResult>(request.BucketId, FilePolicyActions.List, context, request.Prefix, null, cancellationToken);
        if (!resolved.IsSuccess)
            return resolved.Failure;

        var provider = await ResolveProviderAsync<FileObjectListResult>(resolved.Bucket, cancellationToken);
        if (!provider.IsSuccess)
            return provider.Failure;

        var result = await provider.Value.ListMetadataAsync(resolved.Bucket, request, context, cancellationToken);
        return result.IsSuccess() && result.Value is not null
            ? result with { Value = result.Value with { Items = result.Value.Items.Select(item => _redactor.Redact(item, resolved.Bucket, context)).ToArray() } }
            : result;
    }

    private async ValueTask<Resolved<T>> ResolveAsync<T>(FileBucketId bucketId, string action, FileOperationContext context, FileObjectKey? key, FileObjectId? objectId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bucketId.Value) || bucketId.Value.Any(char.IsControl))
            return Resolved<T>.Fail(Validation<T>("bucketId", "Bucket id is required."));

        if (!_options.Enabled)
            return Resolved<T>.Fail(OperationResults.CapabilityUnavailable<T>(Error(FileDiagnosticIds.BucketDisabled, "Files module is disabled.", ErrorCategory.Capability, bucketId.Value)));

        var bucket = await _buckets.FindAsync(bucketId, cancellationToken);
        if (bucket is null)
            return Resolved<T>.Fail(OperationResults.NotFound<T>(Error("files.bucket.notFound", "File bucket was not found.", ErrorCategory.NotFound, bucketId.Value)));

        if (!bucket.Enabled)
            return Resolved<T>.Fail(OperationResults.CapabilityUnavailable<T>(Error(FileDiagnosticIds.BucketDisabled, "File bucket is disabled.", ErrorCategory.Capability, bucketId.Value)));

        var policy = await _policy.EvaluateAsync(new FilePolicyRequest
        {
            Action = action,
            Context = context,
            Resource = new FilePolicyResource { Bucket = bucket, ObjectKey = key, ObjectId = objectId }
        }, cancellationToken);
        HPDBaseFilesTelemetry.RecordPolicyEvaluation(action, policy.Status, policy.Error);

        if (!policy.IsSuccess())
            return Resolved<T>.Fail(new OperationResult<T> { Status = policy.Status, Error = policy.Error });

        if (policy.Value?.Allowed != true)
            return Resolved<T>.Fail(OperationResults.PolicyDenied<T>(Error("files.policy.denied", policy.Value?.Reason ?? "File policy denied the operation.", ErrorCategory.Authorization, bucketId.Value)));

        return Resolved<T>.Ok(bucket);
    }

    private async ValueTask<ProviderResolved<T>> ResolveProviderAsync<T>(FileBucketDescriptor bucket, CancellationToken cancellationToken)
    {
        var provider = await _providers.ResolveAsync(bucket, cancellationToken);
        return provider is null
            ? ProviderResolved<T>.Fail(OperationResults.CapabilityUnavailable<T>(Error(FileDiagnosticIds.NoProvider, "No file storage provider is configured for this bucket.", ErrorCategory.Capability, bucket.BucketId.Value)))
            : ProviderResolved<T>.Ok(provider);
    }

    private OperationResult<FileObjectUploadResult>? ValidateUploadConstraints(FileBucketDescriptor bucket, FileObjectUploadRequest request)
    {
        var max = bucket.MaxObjectBytes ?? _options.MaxObjectBytes;
        if (max is not null && request.SizeBytes is null)
            return OperationResults.ValidationFailed<FileObjectUploadResult>(Error(FileDiagnosticIds.SizeExceeded, "Object size must be declared when an upload size limit is configured.", ErrorCategory.Validation, "sizeBytes"));

        if (max is not null && request.SizeBytes is not null && request.SizeBytes > max)
            return OperationResults.ValidationFailed<FileObjectUploadResult>(Error(FileDiagnosticIds.SizeExceeded, "Object size exceeds the configured limit.", ErrorCategory.Validation, "sizeBytes"));

        if (bucket.RequireChecksum && request.Checksum is null)
            return OperationResults.ValidationFailed<FileObjectUploadResult>(Error("files.checksum.required", "Checksum is required for this bucket.", ErrorCategory.Validation, "checksum"));

        if (request.Checksum is not null && !IsValidChecksum(request.Checksum.Value.Value))
            return OperationResults.ValidationFailed<FileObjectUploadResult>(Error(FileDiagnosticIds.ChecksumRejected, "Checksum is not valid.", ErrorCategory.Validation, "checksum"));

        if (request.Overwrite && !bucket.AllowOverwrite)
            return OperationResults.Conflict<FileObjectUploadResult>(Error("files.object.overwriteRejected", "Object overwrite is not allowed for this bucket.", ErrorCategory.Conflict, "overwrite"));

        if (bucket.AllowedContentTypes is { Length: > 0 } allowed && !string.IsNullOrWhiteSpace(request.ContentType)
            && !allowed.Any(contentType => string.Equals(contentType, request.ContentType, StringComparison.OrdinalIgnoreCase)))
            return OperationResults.ValidationFailed<FileObjectUploadResult>(Error(FileDiagnosticIds.ContentTypeRejected, "Content type is not allowed for this bucket.", ErrorCategory.Validation, "contentType"));

        return null;
    }

    private static bool IsValidChecksum(string checksum) =>
        !string.IsNullOrWhiteSpace(checksum)
        && checksum.Length <= 512
        && !checksum.Any(char.IsControl)
        && !checksum.Contains(' ', StringComparison.Ordinal);

    private static OperationResult<T>? ValidateObjectId<T>(FileObjectId objectId) =>
        string.IsNullOrWhiteSpace(objectId.Value) || objectId.Value.Any(char.IsControl)
            ? Validation<T>("objectId", "Object id is required.")
            : null;

    private static OperationResult<T> Validation<T>(string target, string message) =>
        OperationResults.ValidationFailed<T>(Error("files.validation", message, ErrorCategory.Validation, target));

    private static OperationResult<T> Failure<T>(OperationResult<FileObjectKey> result) =>
        new() { Status = result.Status, Error = result.Error, Warnings = result.Warnings, Diagnostics = result.Diagnostics };

    private static BaseError Error(string code, string message, ErrorCategory category, string? target) => new()
    {
        Code = code,
        Message = message,
        Category = category,
        Target = target
    };

    private readonly record struct Resolved<T>(bool IsSuccess, FileBucketDescriptor Bucket, OperationResult<T> Failure)
    {
        public static Resolved<T> Ok(FileBucketDescriptor bucket) => new(true, bucket, default!);
        public static Resolved<T> Fail(OperationResult<T> failure) => new(false, default!, failure);
    }

    private readonly record struct ProviderResolved<T>(bool IsSuccess, IFileStorageProvider Value, OperationResult<T> Failure)
    {
        public static ProviderResolved<T> Ok(IFileStorageProvider provider) => new(true, provider, default!);
        public static ProviderResolved<T> Fail(OperationResult<T> failure) => new(false, default!, failure);
    }
}
