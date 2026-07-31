using HPD.Base;
using HPD.Base.Files.Buckets;
using HPD.Base.Files.Objects;

namespace HPD.Base.Files.Runtime;

public interface IFileObjectMetadataRedactor
{
    FileObjectMetadata Redact(FileObjectMetadata metadata, FileBucketDescriptor bucket, FileOperationContext context);
}

internal sealed class DefaultFileObjectMetadataRedactor : IFileObjectMetadataRedactor
{
    public FileObjectMetadata Redact(FileObjectMetadata metadata, FileBucketDescriptor bucket, FileOperationContext context)
    {
        if (context.IsAdmin)
            return metadata;

        var keysArePublicSafe = bucket.Visibility == FileBucketVisibility.PublicRead
            && bucket.DescriptorVisibility == VisibilityLevel.Public;

        return metadata with
        {
            Key = keysArePublicSafe ? metadata.Key : null,
            Name = keysArePublicSafe ? metadata.Name : null,
            Checksum = null,
            OwnerSubjectId = null,
            TenantId = null
        };
    }
}
