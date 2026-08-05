
namespace HPD.Base;

/// <summary>Defines the ifile object metadata redactor contract.</summary>
public interface IFileObjectMetadataRedactor
{
    /// <summary>Executes the redact operation.</summary>
    FileObjectMetadata Redact(FileObjectMetadata metadata, FileBucketDescriptor bucket, FileOperationContext context);
}

internal sealed class DefaultFileObjectMetadataRedactor : IFileObjectMetadataRedactor
{
    /// <summary>Executes the redact operation.</summary>
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
