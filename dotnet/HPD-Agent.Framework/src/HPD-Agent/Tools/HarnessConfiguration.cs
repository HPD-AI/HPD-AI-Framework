using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using HPD.Agent.Middleware;

namespace HPD.Agent;

/// <summary>Allows this setting (or complete immutable section) to vary per execution.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class HarnessRunSettingAttribute : Attribute;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class HarnessConfigurationSectionAttribute(string section, string? projection = null) : Attribute
{
    public string Section { get; } = section;
    public string? Projection { get; } = projection;
}

/// <summary>Generated assembly evidence for a verified immutable settings contract.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class HarnessImmutableContractAttribute(Type configurationType) : Attribute
{
    public Type ConfigurationType { get; } = configurationType;
}

public interface IHarnessConfig<out T> where T : class
{
    T AgentDefaults { get; }
    T For(IHarnessConfigurationContext context);
}

/// <summary>Exact, runtime-owned configuration binding; callers cannot manufacture bindings.</summary>
public interface IHarnessConfigurationContext
{
    HarnessConfigurationBinding? HarnessConfiguration { get; }
}

public sealed class HarnessConfigurationBinding
{
    internal HarnessConfigurationBinding(HarnessExecutionConfiguration execution, string owner)
        => (Execution, Owner) = (execution, owner);
    internal HarnessExecutionConfiguration Execution { get; }
    internal string Owner { get; }
}

public readonly struct HarnessOverrideValue<T>
{
    private readonly T _value;
    private HarnessOverrideValue(T value) => (_value, IsSpecified) = (value, true);
    public bool IsSpecified { get; }
    public T Value => IsSpecified ? _value : throw new InvalidOperationException("Override is omitted.");
    public static implicit operator HarnessOverrideValue<T>(T value) => new(value);
}

public interface IHarnessSettingsValidator { void ValidateSettings(); }

public interface IHarnessOverrides
{
    Type ConfigurationType { get; }
    JsonElement ToJson();
}

public sealed class HarnessRunOverride
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    [JsonPropertyName("config")]
    public JsonElement? Config { get; set; }
    [JsonPropertyName("resets")]
    public List<string>? Resets { get; set; }
    internal IReadOnlyDictionary<string, HPD.Serialization.HpdConfigSourceLocation>? SourceLocations { get; set; }
    internal HarnessRunOverride Capture() => new()
    {
        Name = Name, Config = Config?.Clone(), Resets = Resets?.ToList(), SourceLocations = SourceLocations
    };
}

public static class HarnessRunOverridesExtensions
{
    public static HarnessRunOverrideSelection<T> For<T>(this List<HarnessRunOverride> overrides) where T : class
        => new(overrides);
}

public readonly struct HarnessRunOverrideSelection<T>(List<HarnessRunOverride> overrides) where T : class
{
    public void Override(IHarnessOverrides settings, params string[] resets)
    {
        ArgumentNullException.ThrowIfNull(settings);
        System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(typeof(T).Assembly.ManifestModule.ModuleHandle);
        var factory = AgentGeneratedRegistry.Snapshot().ToolHarnesses.SingleOrDefault(value => value.ToolHarnessType == typeof(T))
            ?? throw HarnessConfigurationContract.Error("HarnessNotRegistered", "harnesses",
                $"Harness '{typeof(T)}' has no generated registration.");
        if (factory.Configuration?.ConfigurationType != settings.ConfigurationType)
            throw HarnessConfigurationContract.Error("HarnessConfigTypeMismatch", "harnesses",
                $"Override contract does not belong to harness '{typeof(T)}'.");
        overrides.Add(new() { Name = factory.ActivationIdentity, Config = settings.ToJson(), Resets = resets.ToList() });
    }
}

public enum HarnessSettingKind { Scalar, Object, Array, Dictionary, Json }

/// <summary>Generated data-only contract used by every configuration entry point.</summary>
public sealed record HarnessSettingSchema(HarnessSettingKind Kind, bool Run, bool Nullable,
    IReadOnlyDictionary<string, HarnessSettingSchema>? Properties = null, HarnessSettingSchema? Element = null)
{
    internal bool FullyOverridable => Run || Kind == HarnessSettingKind.Object &&
        Properties is { Count: > 0 } && Properties.Values.All(value => value.FullyOverridable);
    internal bool HasRunSettings => Run || Properties?.Values.Any(value => value.HasRunSettings) == true;
}

public abstract class HarnessConfigurationContract
{
    public abstract Type ConfigurationType { get; }
    public abstract HarnessSettingSchema Schema { get; }
    public abstract object CreateDefaults();
    public abstract JsonElement Encode(object value);
    public abstract object Decode(JsonElement json);
    public abstract object CreateHandle(object agentToken, string owner, object defaults);
    public virtual void Validate(object value) { }

    internal JsonElement Merge(JsonElement baseline, JsonElement patch, bool run, string path)
    {
        var node = JsonNode.Parse(baseline.GetRawText());
        MergeNode(ref node, patch, Schema, run, path);
        return JsonSerializer.SerializeToElement(node, HPDJsonContext.Default.JsonNode);
    }

    private static void MergeNode(ref JsonNode? baseline, JsonElement patch, HarnessSettingSchema schema, bool run, string path)
    {
        if (run && !schema.HasRunSettings)
            throw Error("HarnessRunOverrideNotSupported", path, "This setting is construction-only.");
        if (patch.ValueKind == JsonValueKind.Null)
        {
            if (!schema.Nullable) throw Error("HarnessConfigInvalidValue", path, "This setting cannot be null.");
            if (run && !schema.FullyOverridable) throw Error("HarnessRunOverrideNotSupported", path, "This section cannot be replaced per run.");
            baseline = null; return;
        }
        if (schema.Kind == HarnessSettingKind.Object)
        {
            if (patch.ValueKind != JsonValueKind.Object) throw Error("HarnessConfigInvalidType", path, "Expected an object.");
            var target = baseline as JsonObject ?? new JsonObject();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in patch.EnumerateObject())
            {
                var fieldPath = path + "." + property.Name;
                if (!seen.Add(property.Name)) throw Error("HarnessConfigDuplicateProperty", fieldPath, "Duplicate setting.");
                if (schema.Properties?.TryGetValue(property.Name, out var childSchema) != true)
                    throw Error("HarnessConfigUnknownProperty", fieldPath, "Unknown setting; names are case-sensitive.");
                var child = target[property.Name]?.DeepClone();
                MergeNode(ref child, property.Value, childSchema, run, fieldPath);
                target[property.Name] = child;
            }
            baseline = target;
        }
        else
        {
            if (schema.Kind == HarnessSettingKind.Array && patch.ValueKind != JsonValueKind.Array ||
                schema.Kind == HarnessSettingKind.Dictionary && patch.ValueKind != JsonValueKind.Object)
                throw Error("HarnessConfigInvalidType", path, "Invalid collection type.");
            // Elements are complete replacements, not patches. Validate their full shape on decode.
            baseline = JsonNode.Parse(patch.GetRawText());
        }
    }

    internal JsonElement ApplyRun(JsonElement baseline, JsonElement libraryDefaults, HarnessRunOverride input, string path)
    {
        var patch = input.Config ?? JsonSerializer.SerializeToElement(new JsonObject(), HPDJsonContext.Default.JsonObject);
        if (patch.ValueKind != JsonValueKind.Object) throw Error("HarnessConfigInvalidType", path + ".config", "Expected an object.");
        var resets = input.Resets ?? [];
        var parsed = resets.Select(pointer => (Pointer: pointer, Parts: ParsePointer(pointer, path + ".resets"))).ToArray();
        var supplied = new List<string[]>();
        void Collect(JsonElement value, string[] prefix)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.EnumerateObject().Any()) { supplied.Add(prefix); return; }
            foreach (var property in value.EnumerateObject()) Collect(property.Value, [.. prefix, property.Name]);
        }
        if (patch.EnumerateObject().Any()) Collect(patch, []);
        static bool Overlap(string[] first, string[] second) => first.Take(Math.Min(first.Length, second.Length))
            .SequenceEqual(second.Take(Math.Min(first.Length, second.Length)), StringComparer.Ordinal);
        for (var index = 0; index < parsed.Length; index++)
        {
            if (parsed.Take(index).Any(other => Overlap(other.Parts, parsed[index].Parts)) ||
                supplied.Any(other => Overlap(other, parsed[index].Parts)))
                throw Error("HarnessResetConflict", path + ".resets", "Reset and value targets must not overlap.");
        }
        var resetPatch = new JsonObject();
        foreach (var (_, parts) in parsed)
        {
            var schema = Schema; var value = libraryDefaults; var target = resetPatch;
            for (var index = 0; index < parts.Length; index++)
            {
                if (schema.Properties?.TryGetValue(parts[index], out var child) != true)
                    throw Error("HarnessResetInvalid", path + ".resets", "Reset must target a declared setting.");
                schema = child;
                if (!value.TryGetProperty(parts[index], out value))
                    throw Error("HarnessResetInvalid", path + ".resets", "Library default has no value at this target.");
                if (index == parts.Length - 1)
                {
                    if (!schema.FullyOverridable) throw Error("HarnessRunOverrideNotSupported", path + ".resets", "Reset targets a construction-only setting.");
                    target[parts[index]] = JsonNode.Parse(value.GetRawText());
                }
                else
                {
                    if (target[parts[index]] is not JsonObject next) target[parts[index]] = next = new JsonObject();
                    target = next;
                }
            }
        }
        if (resets.Count > 0) baseline = Merge(baseline,
            JsonSerializer.SerializeToElement(resetPatch, HPDJsonContext.Default.JsonObject), true, path + ".config");
        return Merge(baseline, patch, true, path + ".config");
    }

    private static string[] ParsePointer(string pointer, string path)
    {
        if (string.IsNullOrEmpty(pointer) || pointer[0] != '/' || pointer.Split('/').Skip(1).Any(part =>
            System.Text.RegularExpressions.Regex.IsMatch(part, "~(?![01])")))
            throw Error("HarnessResetInvalid", path, "Expected a non-root RFC 6901 pointer.");
        return pointer[1..].Split('/').Select(part => part.Replace("~1", "/").Replace("~0", "~")).ToArray();
    }

    internal static AgentRunConfigurationException Error(string code, string path, string message)
        => new(code, path, message);
}

public sealed class HarnessConfigurationContract<T>(Func<T> defaults, Func<JsonElement, T> read,
    Func<T, JsonElement> write, HarnessSettingSchema schema, Action<T>? validate = null) : HarnessConfigurationContract where T : class
{
    public override Type ConfigurationType => typeof(T);
    public override HarnessSettingSchema Schema => schema;
    public override object CreateDefaults() => defaults();
    public override JsonElement Encode(object value)
    {
        if (value.GetType() != typeof(T)) throw Error("HarnessConfigTypeMismatch", "config", "Configuration must have its exact generated immutable type.");
        return write((T)value);
    }
    public override object Decode(JsonElement json) => read(json);
    public override void Validate(object value) => validate?.Invoke((T)value);
    public override object CreateHandle(object agentToken, string owner, object value)
        => new HarnessConfig<T>(agentToken, owner, (T)value);
}

internal sealed class HarnessConfig<T>(object token, string owner, T defaults) : IHarnessConfig<T> where T : class
{
    public T AgentDefaults => defaults;
    public T For(IHarnessConfigurationContext context)
    {
        var binding = context.HarnessConfiguration
            ?? throw HarnessConfigurationContract.Error("HarnessConfigurationUnbound", "harnesses", "No execution configuration binding is available.");
        if (!ReferenceEquals(token, binding.Execution.AgentToken) || binding.Owner != owner)
            throw HarnessConfigurationContract.Error("HarnessConfigurationOwnerMismatch", "harnesses", "The configuration handle belongs to another agent or owner.");
        return binding.Execution.Values.TryGetValue(owner, out var value) && value is T typed ? typed
            : throw HarnessConfigurationContract.Error("HarnessConfigurationUnbound", "harnesses", "Owner configuration is unavailable.");
    }
}

internal sealed record HarnessExecutionConfiguration(object AgentToken, ImmutableDictionary<string, object> Values)
{
    internal HarnessConfigurationBinding ForOwner(string owner) => new(this, owner);
}

internal sealed record HarnessConfigurationEntry(ToolHarnessFactory Factory, object LibraryDefaults,
    object AgentDefaults, JsonElement LibraryJson, JsonElement AgentJson, object Handle);

internal sealed class HarnessConfigurationCatalog(object token)
{
    private readonly Dictionary<string, HarnessConfigurationEntry> _entries = new(StringComparer.Ordinal);
    internal HarnessConfigurationEntry Bind(ToolHarnessFactory factory, JsonElement? document, object? configured, ToolHarnessReference? source = null)
    {
        var path = source?.SourcePath ?? "toolHarnesses[" + factory.Name + "]";
        try { return BindCore(factory, document, configured, path); }
        catch (AgentRunConfigurationException ex) { throw HarnessConfigurationDiagnostics.At(ex, source?.SourcePath ?? "toolHarnesses[" + factory.Name + "]", source?.SourceLocations); }
        catch (Exception ex) when (ex is JsonException or FormatException or OverflowException or InvalidOperationException or KeyNotFoundException)
        {
            throw HarnessConfigurationDiagnostics.At(new AgentRunConfigurationException(
                "HarnessConfigInvalidValue",
                "config",
                "Configuration does not match the generated immutable settings contract.",
                innerException: ex), path, source?.SourceLocations);
        }
    }
    private HarnessConfigurationEntry BindCore(ToolHarnessFactory factory, JsonElement? document, object? configured, string path)
    {
        if (_entries.TryGetValue(factory.ActivationIdentity, out var existing)) return existing;
        var contract = factory.Configuration!;
        var baseline = contract.CreateDefaults();
        contract.Validate(baseline);
        var libraryJson = contract.Encode(baseline);
        var agentJson = configured is not null ? contract.Encode(configured)
            : document is { } json ? contract.Merge(libraryJson, json, false, path + ".config") : libraryJson;
        var defaults = contract.Decode(agentJson);
        contract.Validate(defaults);
        var entry = new HarnessConfigurationEntry(factory, baseline, defaults, libraryJson, agentJson,
            contract.CreateHandle(token, factory.ActivationIdentity, defaults));
        _entries.Add(factory.ActivationIdentity, entry);
        return entry;
    }
    internal HarnessExecutionConfiguration Capture(
        List<HarnessRunOverride> overrides,
        IReadOnlyList<List<HarnessRunOverride>>? inheritedLayers = null,
        string currentPath = "harnesses")
    {
        var values = _entries.ToImmutableDictionary(
            static pair => pair.Key,
            static pair => pair.Value.AgentDefaults,
            StringComparer.Ordinal).ToBuilder();
        var effectiveJson = _entries.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.AgentJson,
            StringComparer.Ordinal);
        var layers = inheritedLayers?
            .Select(static layer => layer.ToList())
            .ToList() ?? [];
        if (overrides.Count > 0)
            layers.Add(overrides);

        for (var layerIndex = 0; layerIndex < layers.Count; layerIndex++)
        {
            var layer = layers[layerIndex];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < layer.Count; index++)
            {
                var input = layer[index];
                var pathRoot = currentPath;
                if (inheritedLayers is not null && layerIndex < inheritedLayers.Count)
                {
                    var prefix = currentPath.EndsWith(".harnesses", StringComparison.Ordinal)
                        ? currentPath[..^".harnesses".Length]
                        : null;
                    pathRoot = string.IsNullOrEmpty(prefix)
                        ? $"harnessOverrideLayers[{layerIndex}].harnesses"
                        : $"{prefix}.harnessOverrideLayers[{layerIndex}].harnesses";
                }
                var path = $"{pathRoot}[{index}]";
                var matches = _entries.Values.Where(entry =>
                    entry.Factory.ActivationIdentity == input.Name || entry.Factory.Name == input.Name).ToArray();
                if (matches.Length != 1)
                    throw HarnessConfigurationContract.Error("HarnessNotRegistered", path + ".name",
                        "Harness name is unknown or ambiguous in this agent.");

                var entry = matches[0];
                var owner = entry.Factory.ActivationIdentity;
                if (!seen.Add(owner))
                    throw HarnessConfigurationContract.Error("DuplicateHarness", path + ".name",
                        "Duplicate harness override.");

                try
                {
                    var json = entry.Factory.Configuration!.ApplyRun(
                        effectiveJson[owner], entry.LibraryJson, input, path);
                    object value;
                    try
                    {
                        value = entry.Factory.Configuration.Decode(json);
                    }
                    catch (Exception ex) when (ex is JsonException or FormatException or OverflowException or
                        InvalidOperationException && ex is not AgentRunConfigurationException)
                    {
                        throw HarnessConfigurationContract.Error(
                            "HarnessConfigInvalidValue",
                            path + ".config",
                            "Value does not match the generated configuration contract.");
                    }
                    entry.Factory.Configuration.Validate(value);
                    effectiveJson[owner] = json;
                    values[owner] = value;
                }
                catch (AgentRunConfigurationException ex)
                {
                    throw HarnessConfigurationDiagnostics.At(ex, path, input.SourceLocations);
                }
            }
        }
        return new(token, values.ToImmutable());
    }
}

internal static class HarnessConfigurationDiagnostics
{
    internal static AgentRunConfigurationException At(AgentRunConfigurationException error, string root,
        IReadOnlyDictionary<string, HPD.Serialization.HpdConfigSourceLocation>? locations)
    {
        var path = error.Path == "config" || error.Path.StartsWith("config.", StringComparison.Ordinal)
            ? root + "." + error.Path : error.Path;
        HPD.Serialization.HpdConfigSourceLocation? location = null;
        if (locations is not null)
        {
            var lookup = path;
            while (!locations.TryGetValue(lookup, out location))
            {
                var separator = lookup.LastIndexOf('.');
                if (separator < 0) break;
                lookup = lookup[..separator];
            }
        }
        return new(error.Code, path, error.Message, error.ProviderKey, error.ExpectedType, error.ActualType,
            location?.Line, location?.Column, error);
    }
}
