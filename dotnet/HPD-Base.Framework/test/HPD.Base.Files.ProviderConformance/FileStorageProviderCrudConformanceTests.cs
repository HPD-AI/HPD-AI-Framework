namespace HPD.Base.Files.ProviderConformance;

public abstract class FileStorageProviderCrudConformanceTests<TFixture> : FileStorageProviderConformanceTestBase<TFixture>
    where TFixture : IFileStorageProviderConformanceFixture, new()
{
    [Fact]
    public async Task UploadMetadataDownloadListDeleteRoundTrips()
    {
        var provider = await CreateProviderAsync();

        var upload = await provider.UploadAsync(Bucket, Upload(Bucket.BucketId, "docs/a.txt", "hello"), UserContext());
        FileStorageProviderConformanceAssertions.Success(upload, OperationStatus.Created);
        FileStorageProviderConformanceAssertions.MetadataShape(upload.Value!.Metadata, Bucket.BucketId);

        var metadata = await provider.GetMetadataAsync(Bucket, new FileObjectMetadataRequest
        {
            BucketId = Bucket.BucketId,
            ObjectId = upload.Value.Metadata.ObjectId
        }, UserContext());
        FileStorageProviderConformanceAssertions.Success(metadata, OperationStatus.Ok);
        Assert.Equal("docs/a.txt", metadata.Value!.Key?.Value);

        var download = await provider.OpenDownloadAsync(Bucket, new FileObjectDownloadRequest
        {
            BucketId = Bucket.BucketId,
            ObjectId = upload.Value.Metadata.ObjectId
        }, UserContext());
        FileStorageProviderConformanceAssertions.Success(download, OperationStatus.Ok);
        var downloadResult = download.Value!;
        await using (downloadResult)
        {
            Assert.Equal("hello", await ReadAsync(downloadResult.Content));
            Assert.Equal(5, downloadResult.ContentLength);
        }

        var list = await provider.ListMetadataAsync(Bucket, new FileObjectListRequest
        {
            BucketId = Bucket.BucketId,
            Prefix = new FileObjectKey("docs")
        }, UserContext());
        FileStorageProviderConformanceAssertions.Success(list, OperationStatus.Ok);
        Assert.Contains(list.Value!.Items, item => item.ObjectId == upload.Value.Metadata.ObjectId);

        var delete = await provider.DeleteAsync(Bucket, new FileObjectDeleteRequest
        {
            BucketId = Bucket.BucketId,
            ObjectId = upload.Value.Metadata.ObjectId
        }, UserContext());
        FileStorageProviderConformanceAssertions.Success(delete, OperationStatus.NoContent);

        var missing = await provider.GetMetadataAsync(Bucket, new FileObjectMetadataRequest
        {
            BucketId = Bucket.BucketId,
            ObjectId = upload.Value.Metadata.ObjectId
        }, UserContext());
        FileStorageProviderConformanceAssertions.Failure(missing, OperationStatus.NotFound);
    }

    [Fact]
    public async Task DuplicateKeyConflictsUnlessOverwriteIsRequested()
    {
        var provider = await CreateProviderAsync();

        var first = await provider.UploadAsync(Bucket, Upload(Bucket.BucketId, "same.txt", "one"), UserContext());
        FileStorageProviderConformanceAssertions.Success(first, OperationStatus.Created);

        var duplicate = await provider.UploadAsync(Bucket, Upload(Bucket.BucketId, "same.txt", "two"), UserContext());
        FileStorageProviderConformanceAssertions.Failure(duplicate, OperationStatus.Conflict);

        var overwrite = await provider.UploadAsync(Bucket, Upload(Bucket.BucketId, "same.txt", "two", overwrite: true), UserContext());
        FileStorageProviderConformanceAssertions.Success(overwrite, OperationStatus.Created);
        Assert.Equal(first.Value!.Metadata.ObjectId, overwrite.Value!.Metadata.ObjectId);

        var download = await provider.OpenDownloadAsync(Bucket, new FileObjectDownloadRequest
        {
            BucketId = Bucket.BucketId,
            ObjectId = overwrite.Value.Metadata.ObjectId
        }, UserContext());
        FileStorageProviderConformanceAssertions.Success(download, OperationStatus.Ok);
        var downloadResult = download.Value!;
        await using (downloadResult)
        {
            Assert.Equal("two", await ReadAsync(downloadResult.Content));
        }
    }
}
