using System.Text.Json.Serialization;

namespace HPD.Base;

/// <summary>Represents a hpdbase files JSON serializer context.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters =
    [
        typeof(LowerCamelJsonStringEnumConverter<FileBucketVisibility>),
        typeof(FileBucketIdJsonConverter),
        typeof(FileObjectIdJsonConverter),
        typeof(FileObjectKeyJsonConverter),
        typeof(FileObjectRevisionJsonConverter),
        typeof(FileObjectChecksumJsonConverter),
        typeof(FileProviderRefJsonConverter)
    ])]
[JsonSerializable(typeof(FileBucketDescriptor))]
[JsonSerializable(typeof(FileBucketDescriptor[]))]
[JsonSerializable(typeof(FileBucketCapabilities))]
[JsonSerializable(typeof(FileBucketAdminConfigSummary))]
[JsonSerializable(typeof(FileModuleOptionsContract))]
[JsonSerializable(typeof(FileBucketRegistration))]
[JsonSerializable(typeof(FileObjectRef))]
[JsonSerializable(typeof(FileObjectMetadata))]
[JsonSerializable(typeof(FileObjectUploadResult))]
[JsonSerializable(typeof(FileObjectDownloadRequest))]
[JsonSerializable(typeof(FileObjectMetadataRequest))]
[JsonSerializable(typeof(FileObjectListRequest))]
[JsonSerializable(typeof(FileObjectListResult))]
[JsonSerializable(typeof(FileObjectDeleteRequest))]
[JsonSerializable(typeof(FileObjectEventPayload))]
[JsonSerializable(typeof(FilePolicyRequest))]
[JsonSerializable(typeof(FilePolicyResource))]
[JsonSerializable(typeof(FilePolicyEvaluation))]
[JsonSerializable(typeof(OperationResult<FileObjectUploadResult>))]
[JsonSerializable(typeof(OperationResult<FileObjectMetadata>))]
[JsonSerializable(typeof(OperationResult<FileObjectListResult>))]
public partial class HPDBaseFilesJsonSerializerContext : JsonSerializerContext
{
}
