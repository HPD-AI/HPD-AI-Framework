namespace HPD.Base.Files.ProviderConformance;

public static class FileStorageProviderConformanceAssertions
{
    public static void Success<T>(OperationResult<T> result, OperationStatus expected)
    {
        Assert.Equal(expected, result.Status);
        Assert.NotNull(result.Value);
        Assert.Null(result.Error);
    }

    public static void Success(OperationResult result, OperationStatus expected)
    {
        Assert.Equal(expected, result.Status);
        Assert.Null(result.Error);
    }

    public static void Failure<T>(OperationResult<T> result, params OperationStatus[] allowed)
    {
        Assert.Contains(result.Status, allowed);
        Assert.NotNull(result.Error);
        Assert.False(string.IsNullOrWhiteSpace(result.Error!.Code));
    }

    public static void Failure(OperationResult result, params OperationStatus[] allowed)
    {
        Assert.Contains(result.Status, allowed);
        Assert.NotNull(result.Error);
        Assert.False(string.IsNullOrWhiteSpace(result.Error!.Code));
    }

    public static void MetadataShape(FileObjectMetadata metadata, FileBucketId bucketId)
    {
        Assert.Equal(bucketId, metadata.BucketId);
        Assert.False(string.IsNullOrWhiteSpace(metadata.ObjectId.Value));
        Assert.NotNull(metadata.Revision);
        Assert.True(metadata.SizeBytes is null or >= 0);
    }
}
