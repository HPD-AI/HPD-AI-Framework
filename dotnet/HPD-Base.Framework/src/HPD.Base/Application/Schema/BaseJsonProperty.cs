using System.Text.Json.Serialization.Metadata;

namespace HPD.Base;

/// <summary>
/// Identifies one frozen source-generated JSON property used by a manually
/// authored BASE schema.
/// </summary>
/// <typeparam name="TRecord">The containing record type.</typeparam>
/// <typeparam name="TValue">The declared property value type.</typeparam>
public sealed class BaseJsonProperty<TRecord, TValue>
{
    private BaseJsonProperty(JsonTypeInfo<TRecord> owner, JsonPropertyInfo property)
    {
        Owner = owner;
        Property = property;
        WireName = new string(property.Name.AsSpan());
    }

    internal JsonTypeInfo<TRecord> Owner { get; }
    internal JsonPropertyInfo Property { get; }
    internal string WireName { get; }

    /// <summary>
    /// Binds an exact wire name to one property in read-only source-generated
    /// metadata. The returned handle owns the verified property association.
    /// </summary>
    /// <param name="metadata">Read-only source-generated metadata.</param>
    /// <param name="wireName">The exact serializer-owned property name.</param>
    /// <returns>An opaque verified property handle.</returns>
    public static BaseJsonProperty<TRecord, TValue> Bind(JsonTypeInfo<TRecord> metadata, string wireName)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(wireName);
        if (metadata.Type != typeof(TRecord) || metadata.Kind != JsonTypeInfoKind.Object)
            throw new InvalidOperationException("base.schema.serializer.metadataInvalid");

        metadata.Options.MakeReadOnly();
        metadata.MakeReadOnly();
        JsonPropertyInfo[] matches = metadata.Properties
            .Where(property => string.Equals(property.Name, wireName, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1 || matches[0].PropertyType != typeof(TValue) || matches[0].Get is null)
            throw new InvalidOperationException("base.schema.serializer.metadataInvalid");

        return new BaseJsonProperty<TRecord, TValue>(metadata, matches[0]);
    }
}
