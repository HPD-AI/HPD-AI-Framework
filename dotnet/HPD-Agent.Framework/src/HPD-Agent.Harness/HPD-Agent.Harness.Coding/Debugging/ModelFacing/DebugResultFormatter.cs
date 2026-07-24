using System.Globalization;
using System.Text;
using System.Xml;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

internal sealed record DebugOperationMetadata(
    string Action,
    string? DebugTreeId,
    string? DebugSessionId,
    bool Success,
    string? ErrorKind = null);

internal sealed record DebugStopInspectionMetadata(
    DebugSemanticThread Thread,
    DebugSemanticStackTrace Stack,
    IReadOnlyList<DebugSemanticScope> Scopes,
    IReadOnlyList<DebugSemanticVariables> Variables,
    DebugOutputSnapshot Output);

internal sealed record DebugResultItem(
    IEnumerable<KeyValuePair<string, object?>> Attributes);

internal sealed class DebugResultFormatter
{
    private const int MaximumResultBytes = 64 * 1024;

    public string Success(
        string action,
        IEnumerable<KeyValuePair<string, object?>>? attributes = null,
        IEnumerable<string>? items = null)
        => Write("debug", action, success: true, errorKind: null, attributes, items, structuredItems: null, message: null);

    public string StructuredSuccess(
        string action,
        IEnumerable<KeyValuePair<string, object?>>? attributes,
        IEnumerable<DebugResultItem> items)
        => Write("debug", action, success: true, errorKind: null, attributes, items: null, items, message: null);

    public string Failure(
        string action,
        string kind,
        string message,
        IEnumerable<KeyValuePair<string, object?>>? attributes = null,
        IEnumerable<string>? items = null)
        => Write("error", action, success: false, kind, attributes, items, structuredItems: null, Bound(message, 4096));

    private static string Write(
        string root,
        string action,
        bool success,
        string? errorKind,
        IEnumerable<KeyValuePair<string, object?>>? attributes,
        IEnumerable<string>? items,
        IEnumerable<DebugResultItem>? structuredItems,
        string? message)
    {
        var builder = new StringBuilder();
        using var writer = XmlWriter.Create(builder, new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            ConformanceLevel = ConformanceLevel.Fragment,
            NewLineHandling = NewLineHandling.None
        });
        writer.WriteStartElement(root);
        writer.WriteAttributeString("tool", "Debug");
        writer.WriteAttributeString("action", action);
        writer.WriteAttributeString("success", success ? "true" : "false");
        if (errorKind is not null)
            writer.WriteAttributeString("kind", errorKind);
        if (attributes is not null)
        {
            foreach (var pair in attributes.Take(64))
            {
                if (pair.Value is null) continue;
                writer.WriteAttributeString(ToSnakeCase(pair.Key), FormatValue(pair.Value));
            }
        }
        if (message is not null)
            writer.WriteString(message);
        if (items is not null)
        {
            foreach (var item in items.Take(256))
            {
                writer.WriteStartElement("item");
                writer.WriteString(Bound(item, 4096));
                writer.WriteEndElement();
            }
        }
        if (structuredItems is not null)
        {
            foreach (var item in structuredItems.Take(256))
            {
                writer.WriteStartElement("item");
                foreach (var pair in item.Attributes.Take(32))
                {
                    if (pair.Value is null) continue;
                    writer.WriteAttributeString(ToSnakeCase(pair.Key), Bound(FormatValue(pair.Value), 4096));
                }
                writer.WriteEndElement();
            }
        }
        writer.WriteEndElement();
        writer.Flush();
        var value = builder.ToString();
        if (Encoding.UTF8.GetByteCount(value) <= MaximumResultBytes)
            return value;
        return "<error tool=\"Debug\" action=\"" + SecurityElementEscape(action) +
               "\" success=\"false\" kind=\"output_too_large\">The bounded debugger result exceeded its inline limit.</error>";
    }

    private static string FormatValue(object value) => value switch
    {
        bool boolean => boolean ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => value.ToString() ?? string.Empty
    };

    private static string ToSnakeCase(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (char.IsUpper(current) && index > 0)
                builder.Append('_');
            builder.Append(char.ToLowerInvariant(current));
        }
        return builder.ToString();
    }

    private static string Bound(string value, int maximumCharacters)
        => value.Length <= maximumCharacters ? value : value[..maximumCharacters];

    private static string SecurityElementEscape(string value)
        => value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}
