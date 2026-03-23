namespace HPD.ML.Transforms;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Resizes images (as byte arrays) to target width and height.
/// Concrete implementation deferred to platform-specific image library.
/// </summary>
public sealed class ImageResizeTransform : ITransform
{
    private readonly string _imageColumn;
    private readonly int _width;
    private readonly int _height;
    private readonly ResizeMode _mode;

    public ImageResizeTransform(
        string imageColumn,
        int width,
        int height,
        ResizeMode mode = ResizeMode.ScaleToFit)
    {
        _imageColumn = imageColumn;
        _width = width;
        _height = height;
        _mode = mode;
    }

    public TransformProperties Properties => new() { PreservesRowCount = true };
    public ISchema GetOutputSchema(ISchema inputSchema) => inputSchema;

    public IDataHandle Apply(IDataHandle input)
    {
        throw new NotImplementedException(
            "Image resize requires a platform-specific image library (SkiaSharp or System.Drawing).");
    }
}

public enum ResizeMode { ScaleToFit, Crop, Pad }
