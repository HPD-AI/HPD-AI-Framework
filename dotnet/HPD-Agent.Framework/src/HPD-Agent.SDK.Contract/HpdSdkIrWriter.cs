using System.Text;
using System.Text.Json;

namespace HPD.Agent.SDK.Contract;

/// <summary>Writes deterministic, indented JSON snapshots of HPD Agent SDK IR.</summary>
public static class HpdSdkIrWriter
{
    /// <summary>Serializes an IR graph as UTF-8 JSON text with a trailing newline.</summary>
    public static string Write(HpdSdkIr ir)
    {
        ArgumentNullException.ThrowIfNull(ir);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
                   stream,
                   new JsonWriterOptions
                   {
                       Indented = true
                   }))
        {
            WriteIr(writer, ir);
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static void WriteIr(Utf8JsonWriter writer, HpdSdkIr ir)
    {
        writer.WriteStartObject();
        writer.WriteString("irVersion", ir.IrVersion);
        writer.WriteString("generatorVersion", ir.GeneratorVersion);
        writer.WriteString("sourceOpenApiSha256", ir.SourceOpenApiSha256);
        WriteNullableString(writer, "eventDigest", ir.EventDigest);
        writer.WriteString("inputDigest", ir.InputDigest);
        writer.WritePropertyName("api");
        WriteApi(writer, ir.Api);
        writer.WritePropertyName("types");
        writer.WriteStartArray();
        foreach (var type in ir.Types)
        {
            writer.WriteStartObject();
            writer.WriteString("componentId", type.ComponentId);
            writer.WritePropertyName("schema");
            WriteType(writer, type.Schema);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WritePropertyName("operations");
        writer.WriteStartArray();
        foreach (var operation in ir.Operations)
            WriteOperation(writer, operation);
        writer.WriteEndArray();
        writer.WritePropertyName("unions");
        writer.WriteStartArray();
        foreach (var union in ir.Unions)
            WriteUnion(writer, union);
        writer.WriteEndArray();
        writer.WritePropertyName("capabilities");
        writer.WriteStartArray();
        foreach (var capability in ir.Capabilities)
            WriteCapability(writer, capability);
        writer.WriteEndArray();
        writer.WritePropertyName("diagnostics");
        writer.WriteStartArray();
        foreach (var diagnostic in ir.Diagnostics)
            WriteDiagnostic(writer, diagnostic);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteApi(Utf8JsonWriter writer, HpdApiIr api)
    {
        writer.WriteStartObject();
        writer.WriteString("title", api.Title);
        writer.WriteString("apiVersion", api.ApiVersion);
        writer.WriteString("openApiVersion", api.OpenApiVersion);
        writer.WriteEndObject();
    }

    private static void WriteType(Utf8JsonWriter writer, HpdTypeIr type)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", type.Kind);
        WriteNullableString(writer, "primitive", type.Primitive);
        WriteNullableString(writer, "format", type.Format);
        writer.WriteBoolean("nullable", type.Nullable);
        WriteNullableString(writer, "reference", type.Reference);
        WriteNullableString(writer, "literal", type.Literal);
        if (type.EnumValues is { Count: > 0 })
        {
            writer.WritePropertyName("enumValues");
            writer.WriteStartArray();
            foreach (var value in type.EnumValues)
                writer.WriteStringValue(value);
            writer.WriteEndArray();
        }
        if (type.Properties is { Count: > 0 })
        {
            writer.WritePropertyName("properties");
            writer.WriteStartArray();
            foreach (var property in type.Properties)
            {
                writer.WriteStartObject();
                writer.WriteString("name", property.Name);
                writer.WriteBoolean("required", property.Required);
                writer.WritePropertyName("type");
                WriteType(writer, property.Type);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        if (type.RequiredProperties is { Count: > 0 })
        {
            writer.WritePropertyName("requiredProperties");
            writer.WriteStartArray();
            foreach (var property in type.RequiredProperties)
                writer.WriteStringValue(property);
            writer.WriteEndArray();
        }
        if (type.ItemType is not null)
        {
            writer.WritePropertyName("itemType");
            WriteType(writer, type.ItemType);
        }
        if (type.ValueType is not null)
        {
            writer.WritePropertyName("valueType");
            WriteType(writer, type.ValueType);
        }
        if (type.AllowsAdditionalProperties)
            writer.WriteBoolean("allowsAdditionalProperties", true);
        if (type.ObjectModel)
            writer.WriteBoolean("objectModel", true);
        WriteNullableString(writer, "composition", type.Composition);
        if (type.Branches is { Count: > 0 })
        {
            writer.WritePropertyName("branches");
            writer.WriteStartArray();
            foreach (var branch in type.Branches)
                WriteType(writer, branch);
            writer.WriteEndArray();
        }
        if (type.Annotations is { Count: > 0 })
        {
            writer.WritePropertyName("annotations");
            writer.WriteStartObject();
            foreach (var annotation in type.Annotations)
            {
                writer.WritePropertyName(annotation.Key);
                annotation.Value.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }

    private static void WriteOperation(Utf8JsonWriter writer, HpdOperationIr operation)
    {
        writer.WriteStartObject();
        writer.WriteString("operationId", operation.OperationId);
        writer.WriteString("method", operation.Method);
        writer.WriteString("path", operation.Path);
        writer.WriteString("tag", operation.Tag);
        writer.WriteString("summary", operation.Summary);
        writer.WritePropertyName("parameters");
        writer.WriteStartArray();
        foreach (var parameter in operation.Parameters)
        {
            writer.WriteStartObject();
            writer.WriteString("name", parameter.Name);
            writer.WriteString("location", parameter.Location);
            writer.WriteBoolean("required", parameter.Required);
            writer.WritePropertyName("type");
            WriteType(writer, parameter.Type);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        if (operation.RequestBody is not null)
        {
            writer.WritePropertyName("requestBody");
            writer.WriteStartObject();
            writer.WriteBoolean("required", operation.RequestBody.Required);
            writer.WriteString("mediaType", operation.RequestBody.MediaType);
            writer.WritePropertyName("type");
            WriteType(writer, operation.RequestBody.Type);
            writer.WriteEndObject();
        }
        writer.WriteString("successStatus", operation.SuccessStatus);
        WriteNullableString(writer, "successMediaType", operation.SuccessMediaType);
        if (operation.SuccessType is not null)
        {
            writer.WritePropertyName("successType");
            WriteType(writer, operation.SuccessType);
        }
        writer.WriteBoolean("hasNotFoundResponse", operation.HasNotFoundResponse);
        writer.WriteString("contentKind", operation.ContentKind);
        writer.WriteBoolean("replaySafe", operation.ReplaySafe);
        writer.WriteEndObject();
    }

    private static void WriteUnion(Utf8JsonWriter writer, HpdUnionIr union)
    {
        writer.WriteStartObject();
        writer.WriteString("name", union.Name);
        writer.WriteString("role", union.Role);
        writer.WriteString("unknownVariantPolicy", union.UnknownVariantPolicy);
        writer.WritePropertyName("variants");
        writer.WriteStartArray();
        foreach (var variant in union.Variants)
        {
            writer.WriteStartObject();
            writer.WriteString("componentId", variant.ComponentId);
            writer.WriteString("discriminator", variant.Discriminator);
            writer.WritePropertyName("type");
            WriteType(writer, variant.Type);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteCapability(Utf8JsonWriter writer, HpdCapabilityIr capability)
    {
        writer.WriteStartObject();
        writer.WriteString("name", capability.Name);
        writer.WriteBoolean("available", capability.Available);
        writer.WritePropertyName("operations");
        writer.WriteStartArray();
        foreach (var operation in capability.Operations)
            writer.WriteStringValue(operation);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteDiagnostic(Utf8JsonWriter writer, HpdDiagnosticIr diagnostic)
    {
        writer.WriteStartObject();
        writer.WriteString("severity", diagnostic.Severity);
        writer.WriteString("code", diagnostic.Code);
        writer.WriteString("path", diagnostic.Path);
        writer.WriteString("message", diagnostic.Message);
        writer.WriteEndObject();
    }

    private static void WriteNullableString(
        Utf8JsonWriter writer,
        string propertyName,
        string? value)
    {
        if (value is not null)
            writer.WriteString(propertyName, value);
    }
}
