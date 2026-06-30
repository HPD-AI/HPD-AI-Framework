using HPD.Base.Files.Objects;

namespace HPD.Base.Files.Runtime;

public static class FileModuleIds
{
    public const string Module = "hpd.base.files";
    public const string AspNetCoreModule = "hpd.base.files.aspnetcore";
}

public static class FileFeatureIds
{
    public const string Upload = "files.object.upload";
    public const string Download = "files.object.download";
    public const string MetadataRead = "files.object.metadata.read";
    public const string Delete = "files.object.delete";
    public const string List = "files.object.list";
    public const string BucketDescribe = "files.bucket.describe";
    public const string AccessCreate = "files.object.access.create";
}

public static class FileHealthIds
{
    public const string Registration = "hpd.base.files.registration";
    public const string Provider = "hpd.base.files.provider";

    public static string Bucket(FileBucketId bucketId) => "hpd.base.files.bucket." + bucketId.Value;
}

public static class FileDiagnosticIds
{
    public const string NoProvider = "hpd.base.files.noProvider";
    public const string PolicyUnavailable = "hpd.base.files.policyUnavailable";
    public const string BucketDisabled = "hpd.base.files.bucketDisabled";
    public const string InvalidKey = "hpd.base.files.invalidKey";
    public const string ContentTypeRejected = "hpd.base.files.contentTypeRejected";
    public const string SizeExceeded = "hpd.base.files.sizeExceeded";
    public const string ChecksumRejected = "hpd.base.files.checksumRejected";
    public const string ProviderSecretRedacted = "hpd.base.files.providerSecretRedacted";
    public const string PublicBucketWarning = "hpd.base.files.publicBucketWarning";
}

public static class FileDtoIds
{
    public const string BucketDescriptor = "hpd.base.files.bucketDescriptor";
    public const string ObjectRef = "hpd.base.files.objectRef";
    public const string ObjectMetadata = "hpd.base.files.objectMetadata";
    public const string ObjectUploadResult = "hpd.base.files.objectUploadResult";
    public const string ObjectListResult = "hpd.base.files.objectListResult";
    public const string ObjectEvent = "hpd.base.files.objectEvent";
}

public static class FileEventTypeNames
{
    public const string ObjectUploaded = "base.files.object.uploaded";
    public const string ObjectMetadataUpdated = "base.files.object.metadataUpdated";
    public const string ObjectDeleted = "base.files.object.deleted";
    public const string ObjectAccessCreated = "base.files.object.accessCreated";
    public const string RecordAttachmentCreated = "base.files.recordAttachment.created";
    public const string RecordAttachmentRemoved = "base.files.recordAttachment.removed";
}
