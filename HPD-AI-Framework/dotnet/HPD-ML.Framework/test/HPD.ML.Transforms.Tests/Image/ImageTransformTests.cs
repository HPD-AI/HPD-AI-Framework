namespace HPD.ML.Transforms.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public class ImageTransformTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            if (File.Exists(f)) File.Delete(f);
    }

    [Fact]
    public void ImageLoad_ReadsBytes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hpdml_img_{Guid.NewGuid():N}.bin");
        var content = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG header
        File.WriteAllBytes(path, content);
        _tempFiles.Add(path);

        var data = TestHelper.Data(("Path", new string[] { path }));
        var transform = new ImageLoadTransform("Path");
        var result = transform.Apply(data);
        using var cursor = result.GetCursor(["Image"]);
        Assert.True(cursor.MoveNext());
        var bytes = cursor.Current.GetValue<byte[]>("Image");
        Assert.Equal(content, bytes);
    }

    [Fact]
    public void ImageLoad_Schema_AddsByteArrayCol()
    {
        var schema = new SchemaBuilder().AddColumn("Path", new FieldType(typeof(string))).Build();
        var transform = new ImageLoadTransform("Path");
        var outSchema = transform.GetOutputSchema(schema);
        Assert.NotNull(outSchema.FindByName("Image"));
        Assert.Equal(typeof(byte[]), outSchema.FindByName("Image")!.Type.ClrType);
    }

    [Fact]
    public void ImageResize_Throws_NotImplemented()
    {
        var data = TestHelper.Data(("Image", new byte[][] { [1, 2, 3] }));
        var transform = new ImageResizeTransform("Image", 224, 224);
        Assert.Throws<NotImplementedException>(() => transform.Apply(data));
    }

    [Fact]
    public void ImagePixelExtract_Throws_NotImplemented()
    {
        var data = TestHelper.Data(("Image", new byte[][] { [1, 2, 3] }));
        var transform = new ImagePixelExtractTransform("Image", 3, 224, 224);
        Assert.Throws<NotImplementedException>(() => transform.Apply(data));
    }

    [Fact]
    public void ImagePixelExtract_Schema_VectorDimensions()
    {
        var schema = new SchemaBuilder().AddColumn("Image", new FieldType(typeof(byte[]))).Build();
        var transform = new ImagePixelExtractTransform("Image", channels: 3, height: 224, width: 224);
        var outSchema = transform.GetOutputSchema(schema);
        var col = outSchema.FindByName("Pixels")!;
        Assert.True(col.Type.IsVector);
        Assert.Equal([3, 224, 224], col.Type.Dimensions);
    }
}
