using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent;

/// <summary>Serializes a finalized content capture into the tool-result projection envelope.</summary>
internal sealed class ContentCaptureJsonConverter : JsonConverter<ContentCapturePart>
{
    internal const string MarkerProperty = "$hpdCapture";
    internal const string MarkerValue = "content-capture.v1";
    internal const string NameProperty = "name";
    internal const string MediaTypeProperty = "mediaType";
    internal const string SensitivityProperty = "sensitivity";
    internal const string EncodingProperty = "encoding";
    internal const string DataProperty = "data";

    public override ContentCapturePart Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        throw new NotSupportedException("Content captures are tool outputs and cannot be deserialized.");

    public override void Write(
        Utf8JsonWriter writer,
        ContentCapturePart value,
        JsonSerializerOptions options)
    {
        var bytes = value.Complete();
        var text = IsText(value.MediaType);
        writer.WriteStartObject();
        writer.WriteString(MarkerProperty, MarkerValue);
        writer.WriteString(NameProperty, value.Name);
        writer.WriteString(MediaTypeProperty, value.MediaType);
        writer.WriteString(SensitivityProperty, value.Sensitivity.ToString());
        writer.WriteString(EncodingProperty, text ? "utf-8" : "base64");
        writer.WriteString(DataProperty, text
            ? Encoding.UTF8.GetString(bytes)
            : Convert.ToBase64String(bytes));
        writer.WriteEndObject();
    }

    private static bool IsText(string mediaType) =>
        mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
        mediaType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
        mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase) ||
        mediaType.EndsWith("+text", StringComparison.OrdinalIgnoreCase);
}
