// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent.ClientTools;

/// <summary>
/// Describes how an agent run should bind a live client app provider.
/// </summary>
/// <remarks>
/// This is the runtime-scoped reference shape. It does not contain tool definitions;
/// those come from the connected provider manifest after a binding lease is created.
/// </remarks>
[JsonConverter(typeof(ClientAppProviderReferenceConverter))]
public sealed class ClientAppProviderReference
{
    /// <summary>
    /// Gets or sets the logical app provider name, such as <c>penpot</c> or <c>code-server</c>.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets selectors used to choose one connected provider instance.
    /// </summary>
    public ClientProviderSelector? ProviderSelector { get; set; }

    /// <summary>
    /// Gets or sets selected harnesses to expose after binding. Null means all harnesses advertised by the provider.
    /// </summary>
    public IReadOnlyList<ClientToolHarnessSelector>? Harnesses { get; set; }

    /// <summary>
    /// Gets or sets specific tool names to expose across selected harnesses. Null means all tools in selected harnesses.
    /// </summary>
    public IReadOnlyList<string>? Tools { get; set; }

    /// <summary>
    /// Gets or sets whether the run requires this provider to bind before the model turn starts.
    /// </summary>
    public bool Required { get; set; }

    /// <summary>
    /// Gets or sets the binding policy for the selected provider.
    /// </summary>
    public ClientAppProviderBindingPolicy BindingPolicy { get; set; } =
        ClientAppProviderBindingPolicy.Exclusive;

    /// <summary>
    /// Converts a provider name into a provider reference.
    /// </summary>
    public static implicit operator ClientAppProviderReference(string name) => new() { Name = name };

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>
/// Selects a connected client provider instance.
/// </summary>
public sealed record ClientProviderSelector
{
    /// <summary>Gets the exact connected runtime id to bind.</summary>
    public string? ClientRuntimeId { get; init; }

    /// <summary>Gets the app kind to match, such as <c>penpot</c>.</summary>
    public string? AppKind { get; init; }

    /// <summary>Gets the exact server-authoritative HPD-OS App installation to match.</summary>
    public string? AppInstallationId { get; init; }

    /// <summary>Gets the exact browser launch session to match.</summary>
    public string? BrowserLaunchSessionId { get; init; }

    /// <summary>Gets the workspace id to match.</summary>
    public string? WorkspaceId { get; init; }

    /// <summary>Gets the document id to match.</summary>
    public string? DocumentId { get; init; }

    /// <summary>Gets the project id to match.</summary>
    public string? ProjectId { get; init; }

    /// <summary>Gets the user id or user hint to match.</summary>
    public string? UserId { get; init; }

    /// <summary>Gets required tags that the provider must advertise.</summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>Gets whether the current active provider should be preferred by the host.</summary>
    public bool? Current { get; init; }
}

/// <summary>
/// Selects one client tool harness and optionally a subset of its tools.
/// </summary>
[JsonConverter(typeof(ClientToolHarnessSelectorConverter))]
public sealed class ClientToolHarnessSelector
{
    /// <summary>Gets or sets the harness name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the tool names to expose from this harness. Null means all tools.</summary>
    public IReadOnlyList<string>? Tools { get; set; }

    /// <summary>Gets or sets whether the harness should start expanded.</summary>
    public bool? Expanded { get; set; }

    /// <summary>Gets or sets whether this harness is required on the matched provider.</summary>
    public bool Required { get; set; }

    /// <summary>
    /// Converts a harness name into a harness selector.
    /// </summary>
    public static implicit operator ClientToolHarnessSelector(string name) => new() { Name = name };

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>
/// Controls how HPD binds a provider for an agent run.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ClientAppProviderBindingPolicy>))]
public enum ClientAppProviderBindingPolicy
{
    /// <summary>Bind the provider exclusively for this runtime scope.</summary>
    Exclusive,

    /// <summary>Bind if possible, but do not require the provider to exist.</summary>
    Optional,

    /// <summary>Use the provider only when one is available.</summary>
    IfAvailable
}

/// <summary>
/// Converts <see cref="ClientAppProviderReference"/> from string or object JSON syntax.
/// </summary>
public sealed class ClientAppProviderReferenceConverter : JsonConverter<ClientAppProviderReference>
{
    /// <inheritdoc />
    public override ClientAppProviderReference? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return new ClientAppProviderReference { Name = reader.GetString() ?? string.Empty };

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Unexpected token {reader.TokenType} when reading {nameof(ClientAppProviderReference)}.");

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        var reference = new ClientAppProviderReference
        {
            Name = root.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
            Required = root.TryGetProperty("required", out var required) && required.GetBoolean()
        };

        if (root.TryGetProperty("providerSelector", out var selector))
            reference.ProviderSelector = selector.Deserialize(HPDJsonContext.Default.ClientProviderSelector);

        if (root.TryGetProperty("harnesses", out var harnesses))
            reference.Harnesses = harnesses.Deserialize(HPDJsonContext.Default.IReadOnlyListClientToolHarnessSelector);

        if (root.TryGetProperty("tools", out var tools))
            reference.Tools = tools.Deserialize(HPDJsonContext.Default.IReadOnlyListString);

        if (root.TryGetProperty("bindingPolicy", out var bindingPolicy))
            reference.BindingPolicy = bindingPolicy.Deserialize(HPDJsonContext.Default.ClientAppProviderBindingPolicy);

        return reference;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ClientAppProviderReference value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("name", value.Name);
        if (value.ProviderSelector is not null)
        {
            writer.WritePropertyName("providerSelector");
            JsonSerializer.Serialize(writer, value.ProviderSelector, HPDJsonContext.Default.ClientProviderSelector);
        }
        if (value.Harnesses is not null)
        {
            writer.WritePropertyName("harnesses");
            JsonSerializer.Serialize(writer, value.Harnesses, HPDJsonContext.Default.IReadOnlyListClientToolHarnessSelector);
        }
        if (value.Tools is not null)
        {
            writer.WritePropertyName("tools");
            JsonSerializer.Serialize(writer, value.Tools, HPDJsonContext.Default.IReadOnlyListString);
        }
        if (value.Required)
            writer.WriteBoolean("required", value.Required);
        writer.WriteString("bindingPolicy", value.BindingPolicy.ToString());
        writer.WriteEndObject();
    }
}

/// <summary>
/// Converts <see cref="ClientToolHarnessSelector"/> from string or object JSON syntax.
/// </summary>
public sealed class ClientToolHarnessSelectorConverter : JsonConverter<ClientToolHarnessSelector>
{
    /// <inheritdoc />
    public override ClientToolHarnessSelector? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return new ClientToolHarnessSelector { Name = reader.GetString() ?? string.Empty };

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Unexpected token {reader.TokenType} when reading {nameof(ClientToolHarnessSelector)}.");

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        return new ClientToolHarnessSelector
        {
            Name = root.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
            Tools = root.TryGetProperty("tools", out var tools)
                ? tools.Deserialize(HPDJsonContext.Default.IReadOnlyListString)
                : null,
            Expanded = root.TryGetProperty("expanded", out var expanded) ? expanded.GetBoolean() : null,
            Required = root.TryGetProperty("required", out var required) && required.GetBoolean()
        };
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ClientToolHarnessSelector value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("name", value.Name);
        if (value.Tools is not null)
        {
            writer.WritePropertyName("tools");
            JsonSerializer.Serialize(writer, value.Tools, HPDJsonContext.Default.IReadOnlyListString);
        }
        if (value.Expanded is not null)
            writer.WriteBoolean("expanded", value.Expanded.Value);
        if (value.Required)
            writer.WriteBoolean("required", value.Required);
        writer.WriteEndObject();
    }
}
