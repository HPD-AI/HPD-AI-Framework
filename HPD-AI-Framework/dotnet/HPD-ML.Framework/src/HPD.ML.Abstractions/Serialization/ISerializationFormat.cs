namespace HPD.ML.Abstractions;

public interface ISerializationFormat
{
    string FormatId { get; }
    bool SupportsContent(SaveContent content);
}
