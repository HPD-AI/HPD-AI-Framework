namespace HPD.ML.Transforms;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Extracts pixel values from an image byte array as a float tensor.
/// Output shape: [channels, height, width] (CHW format for ONNX/PyTorch).
/// Concrete implementation deferred to platform-specific image library.
/// </summary>
public sealed class ImagePixelExtractTransform : ITransform
{
    private readonly string _imageColumn;
    private readonly string _outputColumn;
    private readonly int _channels;
    private readonly int _height;
    private readonly int _width;
    private readonly bool _normalize;

    public ImagePixelExtractTransform(
        string imageColumn,
        int channels,
        int height,
        int width,
        bool normalize = true,
        string outputColumn = "Pixels")
    {
        _imageColumn = imageColumn;
        _channels = channels;
        _height = height;
        _width = width;
        _normalize = normalize;
        _outputColumn = outputColumn;
    }

    public TransformProperties Properties => new() { PreservesRowCount = true };

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        var columns = inputSchema.Columns.ToList();
        columns.Add(new Column(
            _outputColumn,
            FieldType.Vector<float>(_channels, _height, _width)));
        return new Schema(columns, inputSchema.Level);
    }

    public IDataHandle Apply(IDataHandle input)
    {
        throw new NotImplementedException(
            "Pixel extraction requires a platform-specific image library.");
    }
}
