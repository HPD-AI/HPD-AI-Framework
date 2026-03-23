namespace HPD.ML.Serialization.Zip;

using HPD.ML.Abstractions;

/// <summary>
/// ZIP archive serialization format.
/// </summary>
public sealed class ZipFormat : ISerializationFormat
{
    public string FormatId => "hpd-ml-zip-v1";

    public bool SupportsContent(SaveContent content) => true;
}
