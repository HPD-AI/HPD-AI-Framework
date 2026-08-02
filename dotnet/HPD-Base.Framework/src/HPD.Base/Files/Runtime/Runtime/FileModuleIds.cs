
namespace HPD.Base;

/// <summary>Represents a file module IDs.</summary>
public static class FileModuleIds
{
    /// <summary>Provides the module value.</summary>
    public const string Module = "hpd.base.files";
    /// <summary>Provides the asp net core module value.</summary>
    public const string AspNetCoreModule = "hpd.base.files.aspnetcore";
}

/// <summary>Represents a file feature IDs.</summary>
public static class FileFeatureIds
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
    /// <summary>Provides the bucket describe value.</summary>
    public const string BucketDescribe = "files.bucket.describe";
    /// <summary>Provides the access create value.</summary>
    public const string AccessCreate = "files.object.access.create";
}

/// <summary>Represents a file health IDs.</summary>
public static class FileHealthIds
{
    /// <summary>Provides the registration value.</summary>
    public const string Registration = "hpd.base.files.registration";
    /// <summary>Provides the provider value.</summary>
    public const string Provider = "hpd.base.files.provider";

    /// <summary>Executes the bucket operation.</summary>
    public static string Bucket(FileBucketId bucketId) => "hpd.base.files.bucket." + bucketId.Value;
}

/// <summary>Represents a file diagnostic IDs.</summary>
public static class FileDiagnosticIds
{
    /// <summary>Provides the no provider value.</summary>
    public const string NoProvider = "hpd.base.files.noProvider";
    /// <summary>Provides the policy unavailable value.</summary>
    public const string PolicyUnavailable = "hpd.base.files.policyUnavailable";
    /// <summary>Provides the bucket disabled value.</summary>
    public const string BucketDisabled = "hpd.base.files.bucketDisabled";
    /// <summary>Provides the invalid key value.</summary>
    public const string InvalidKey = "hpd.base.files.invalidKey";
    /// <summary>Provides the content type rejected value.</summary>
    public const string ContentTypeRejected = "hpd.base.files.contentTypeRejected";
    /// <summary>Provides the size exceeded value.</summary>
    public const string SizeExceeded = "hpd.base.files.sizeExceeded";
    /// <summary>Provides the checksum rejected value.</summary>
    public const string ChecksumRejected = "hpd.base.files.checksumRejected";
    /// <summary>Provides the provider secret redacted value.</summary>
    public const string ProviderSecretRedacted = "hpd.base.files.providerSecretRedacted";
    /// <summary>Provides the public bucket warning value.</summary>
    public const string PublicBucketWarning = "hpd.base.files.publicBucketWarning";
}

/// <summary>Represents a file DTO IDs.</summary>
public static class FileDtoIds
{
    /// <summary>Provides the bucket descriptor value.</summary>
    public const string BucketDescriptor = "hpd.base.files.bucketDescriptor";
    /// <summary>Provides the object ref value.</summary>
    public const string ObjectRef = "hpd.base.files.objectRef";
    /// <summary>Provides the object metadata value.</summary>
    public const string ObjectMetadata = "hpd.base.files.objectMetadata";
    /// <summary>Provides the object upload result value.</summary>
    public const string ObjectUploadResult = "hpd.base.files.objectUploadResult";
    /// <summary>Provides the object list result value.</summary>
    public const string ObjectListResult = "hpd.base.files.objectListResult";
    /// <summary>Provides the object event value.</summary>
    public const string ObjectEvent = "hpd.base.files.objectEvent";
}

/// <summary>Represents a file event type names.</summary>
public static class FileEventTypeNames
{
    /// <summary>Provides the object uploaded value.</summary>
    public const string ObjectUploaded = "base.files.object.uploaded";
    /// <summary>Provides the object metadata updated value.</summary>
    public const string ObjectMetadataUpdated = "base.files.object.metadataUpdated";
    /// <summary>Provides the object deleted value.</summary>
    public const string ObjectDeleted = "base.files.object.deleted";
    /// <summary>Provides the object access created value.</summary>
    public const string ObjectAccessCreated = "base.files.object.accessCreated";
    /// <summary>Provides the record attachment created value.</summary>
    public const string RecordAttachmentCreated = "base.files.recordAttachment.created";
    /// <summary>Provides the record attachment removed value.</summary>
    public const string RecordAttachmentRemoved = "base.files.recordAttachment.removed";
}
