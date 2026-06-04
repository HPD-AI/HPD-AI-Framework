// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>
/// Container for all middleware state.
/// Properties are source-generated from [MiddlewareState] types.
/// </summary>
/// <remarks>
/// <para><b>Internal Storage:</b></para>
/// <para>
/// Uses ImmutableDictionary&lt;string, object?&gt; as backing storage.
/// This pattern is proven by Microsoft.Extensions.AI (see AIJsonUtilities.Defaults.cs).
/// During deserialization from durable JSON, values become JsonElement which are
/// transparently converted to concrete types by the smart accessor.
/// </para>
///
/// <para><b>Performance:</b></para>
/// <list type="bullet">
/// <item>Runtime reads: ~20-25ns (dictionary lookup + pattern match)</item>
/// <item>Post-deserialization first read: ~150ns (JsonElement deserialize)</item>
/// <item>Post-deserialization cached reads: ~20ns (from cache)</item>
/// <item>Immutable updates: ~30ns (ImmutableDictionary.SetItem)</item>
/// </list>
///
/// <para><b>Usage:</b></para>
/// <code>
/// // Access middleware state
/// var state = context.State.MiddlewareState.CircuitBreaker ?? new();
///
/// // Update middleware state (immutable)
/// context.UpdateState(s => s with
/// {
///     MiddlewareState = s.MiddlewareState.WithCircuitBreaker(newState)
/// });
/// </code>
/// </remarks>
[JsonConverter(typeof(MiddlewareStateJsonConverter))]
public sealed partial class MiddlewareState
{
    //      
    // BACKING STORAGE (Internal)
    //      

    /// <summary>
    /// Internal storage for middleware states.
    /// Keys: Fully-qualified type names (e.g., "HPD.Agent.CircuitBreakerStateData")
    /// Values: State instances (runtime) or JsonElement (deserialized)
    /// </summary>
    /// <remarks>
    /// <para>  <b>Internal API - Do not use directly!</b></para>
    /// <para>
    /// This property is public only for JSON serialization compatibility.
    /// Always use the generated properties (e.g., <c>CircuitBreaker</c>, <c>ErrorTracking</c>)
    /// instead of accessing this dictionary directly.
    /// </para>
    /// <para>
    /// JSON serialization: This gets serialized as a dictionary.
    /// On deserialization, values become JsonElement which are converted by smart accessor.
    /// </para>
    /// </remarks>
    [JsonPropertyName("states")]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public ImmutableDictionary<string, object?> States { get; init; }

    //      
    // SCHEMA METADATA (Runtime Fields)
    //      

    /// <summary>
    /// Schema signature of the code that created this serialized state.
    /// Comma-separated list of middleware state FQNs in alphabetical order.
    /// Null for serialized states created before schema versioning was added.
    /// </summary>
    [JsonPropertyName("schemaSignature")]
    public string? SchemaSignature { get; init; }

    /// <summary>
    /// Container schema version (for future container-level migrations).
    /// Always 1 in this version of HPD-Agent.
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    /// <summary>
    /// Per-state version mapping (type FQN → version).
    /// Used for detecting individual state schema evolution.
    /// Null for serialized states created before schema versioning was added.
    /// </summary>
    [JsonPropertyName("stateVersions")]
    public ImmutableDictionary<string, int>? StateVersions { get; init; }

    /// <summary>
    /// Lazy cache for deserialized JsonElement states.
    /// Each container instance gets its own cache to maintain immutability.
    /// Only initialized when deserialization occurs (zero overhead for runtime-only scenarios).
    /// </summary>
    [JsonIgnore]
    private readonly Lazy<ConcurrentDictionary<string, object?>> _deserializedCache;

    //      
    // CONSTRUCTORS
    //      

    /// <summary>
    /// Creates an empty middleware state container.
    /// Schema metadata is computed from runtime-registered factories (not compiled constants).
    /// </summary>
    public MiddlewareState()
    {
        States = ImmutableDictionary<string, object?>.Empty;
        _deserializedCache = new Lazy<ConcurrentDictionary<string, object?>>(
            () => new ConcurrentDictionary<string, object?>());

        // Schema metadata is now computed at runtime from registered factories
        // See ValidateAndMigrateSchema in Agent.cs
        SchemaSignature = null;
        SchemaVersion = 1;
        StateVersions = null;
    }

    //      
    // SMART ACCESSOR (Protected - Used by Generated Code)
    //      

    /// <summary>
    /// Smart accessor that handles both runtime and deserialized states.
    /// </summary>
    /// <typeparam name="TState">The middleware state type</typeparam>
    /// <param name="key">Fully-qualified type name (e.g., "HPD.Agent.CircuitBreakerStateData")</param>
    /// <returns>The state instance, or null if not present</returns>
    /// <remarks>
    /// <para><b>State Transitions:</b></para>
    /// <list type="bullet">
    /// <item>Runtime: value is TState (direct cast, ~20-25ns)</item>
    /// <item>Deserialized: value is JsonElement (deserialize first access ~150ns, cached ~20ns)</item>
    /// </list>
    ///
    /// <para><b>Timeline of State Transitions:</b></para>
    /// <para>
    /// T0: Runtime - Store concrete type in _states[key] = TState instance
    /// T1: Serialize to JSON.
    /// T2: Deserialize from JSON (restore) - _states[key] = JsonElement
    /// T3: Access via property - Smart accessor detects JsonElement, deserializes to TState, caches result
    /// T4: Update state - _states[key] = TState instance (concrete type again)
    /// </para>
    ///
    /// <para><b>External Package Usage:</b></para>
    /// <para>
    /// External packages can use this method directly
    /// with the fully-qualified type name as the key. The source generator will
    /// also create typed properties for consumer projects that reference the package.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // External package can use:
    /// var state = context.State.MiddlewareState.GetState&lt;PackageState&gt;(
    ///     "Example.Package.PackageState");
    /// </code>
    /// </example>
    public TState? GetState<TState>(string key) where TState : class
    {
        // Fast path: Check deserialization cache first.
        if (_deserializedCache.IsValueCreated &&
            _deserializedCache.Value.TryGetValue(key, out var cached))
        {
            return cached as TState;
        }

        if (!States.TryGetValue(key, out var value) || value is null)
            return null;

        // Pattern match handles both cases transparently
        var result = value switch
        {
            TState typed => typed,  // Runtime: already correct type
            JsonElement elem => DeserializeJsonElement<TState>(key, elem),  // Deserialized from durable JSON
            _ => throw new InvalidOperationException(
                $"Unexpected type {value.GetType().Name} for middleware state '{key}'. " +
                $"Expected {typeof(TState).Name} or JsonElement.")
        };

        // Cache deserialized JsonElement results for subsequent accesses
        if (value is JsonElement && result != null)
        {
            _deserializedCache.Value.TryAdd(key, result);
        }

        return result;
    }

    private static TState? DeserializeJsonElement<TState>(
        string key,
        JsonElement element) where TState : class
    {
        var (_, _, states) = AgentGeneratedRegistry.Snapshot();
        foreach (var factory in states)
        {
            if (string.Equals(factory.FullyQualifiedName, key, StringComparison.Ordinal)
                && factory.StateType == typeof(TState))
            {
                return factory.Deserialize(element.GetRawText()) as TState;
            }
        }

        throw new NotSupportedException(
            $"No middleware state factory is registered for '{key}'.");
    }

    /// <summary>
    /// Creates new container with updated state (immutable).
    /// Preserves schema metadata across updates.
    /// </summary>
    /// <typeparam name="TState">The middleware state type</typeparam>
    /// <param name="key">Fully-qualified type name</param>
    /// <param name="state">New state value</param>
    /// <returns>New container with updated state</returns>
    /// <example>
    /// <code>
    /// // External package can use:
    /// context.UpdateState(s => s with
    /// {
    ///     MiddlewareState = s.MiddlewareState.SetState(
    ///         "Example.Package.PackageState",
    ///         newPackageState)
    /// });
    /// </code>
    /// </example>
    public MiddlewareState SetState<TState>(
        string key,
        TState state) where TState : class
    {
        return new MiddlewareState
        {
            States = States.SetItem(key, state),

            // Preserve schema metadata across updates
            SchemaSignature = this.SchemaSignature,
            SchemaVersion = this.SchemaVersion,
            StateVersions = this.StateVersions
        };
    }

    //
    // PERSISTENCE API (Session + Branch Synchronization)
    //
    // State is split by scope.
    // - Session-scoped: LoadFromSession/SaveToSession (filters Scope == Session)
    // - Branch-scoped: LoadFromBranch/SaveToBranch (filters Scope == Branch)

    /// <summary>
    /// Load session-scoped persistent middleware state from session.
    /// Only loads states marked with Scope = StateScope.Session.
    /// Uses the agent's registered factories to deserialize correctly.
    /// </summary>
    /// <param name="session">Session to load state from (null returns empty state).</param>
    /// <param name="factories">Middleware state factories from the agent's registry.</param>
    /// <returns>MiddlewareState with restored session-scoped persistent states.</returns>
    /// <remarks>
    /// <para>Filters by Scope == StateScope.Session.</para>
    /// <para>
    /// Session-scoped states (permissions, preferences) are shared across all branches.
    /// Branch-scoped states (plan progress, history cache) use LoadFromBranch instead.
    /// </para>
    /// </remarks>
    public static MiddlewareState LoadFromSession(
        Session? session,
        IReadOnlyDictionary<string, MiddlewareStateFactory> factories)
    {
        if (session == null)
            return new MiddlewareState();

        var state = new MiddlewareState();

        // Load all registered session-scoped persistent states
        foreach (var (key, factory) in factories)
        {
            if (factory.Persistent && factory.Scope == StateScope.Session)
            {
                var json = session.GetMiddlewareState(key);
                if (json != null)
                {
                    try
                    {
                        var data = factory.Deserialize(json);
                        if (data != null)
                        {
                            state = new MiddlewareState
                            {
                                States = state.States.SetItem(key, data),
                                SchemaSignature = state.SchemaSignature,
                                SchemaVersion = state.SchemaVersion,
                                StateVersions = state.StateVersions
                            };
                        }
                    }
                    catch
                    {
                        // Ignore deserialization errors - state will be missing
                        // This handles schema evolution gracefully
                    }
                }
            }
        }

        return state;
    }

    /// <summary>
    /// Load branch-scoped persistent middleware state from branch.
    /// Only loads states marked with Scope = StateScope.Branch (the default).
    /// Uses the agent's registered factories to deserialize correctly.
    /// </summary>
    /// <param name="branch">Branch to load state from (null returns empty state).</param>
    /// <param name="factories">Middleware state factories from the agent's registry.</param>
    /// <returns>MiddlewareState with restored branch-scoped persistent states.</returns>
    /// <remarks>
    /// <para>Loads branch-scoped state.</para>
    /// <para>
    /// Branch-scoped states (plan progress, history cache) are per-conversation path.
    /// Session-scoped states (permissions, preferences) use LoadFromSession instead.
    /// </para>
    /// </remarks>
    public static MiddlewareState LoadFromBranch(
        Branch? branch,
        IReadOnlyDictionary<string, MiddlewareStateFactory> factories)
    {
        if (branch == null)
            return new MiddlewareState();

        var state = new MiddlewareState();

        // Load all registered branch-scoped persistent states
        foreach (var (key, factory) in factories)
        {
            if (factory.Persistent && factory.Scope == StateScope.Branch)
            {
                var json = branch.GetMiddlewareState(key);
                if (json != null)
                {
                    try
                    {
                        var data = factory.Deserialize(json);
                        if (data != null)
                        {
                            state = new MiddlewareState
                            {
                                States = state.States.SetItem(key, data),
                                SchemaSignature = state.SchemaSignature,
                                SchemaVersion = state.SchemaVersion,
                                StateVersions = state.StateVersions
                            };
                        }
                    }
                    catch
                    {
                        // Ignore deserialization errors - state will be missing
                        // This handles schema evolution gracefully
                    }
                }
            }
        }

        return state;
    }

    /// <summary>
    /// Save session-scoped persistent middleware state to session.
    /// Only saves states marked with Scope = StateScope.Session.
    /// Uses the agent's registered factories to determine which states are persistent.
    /// </summary>
    /// <param name="session">Session to save state to.</param>
    /// <param name="factories">Middleware state factories from the agent's registry.</param>
    /// <exception cref="ArgumentNullException">Thrown if session is null.</exception>
    /// <remarks>
    /// <para>Filters by Scope == StateScope.Session.</para>
    /// <para>
    /// Session-scoped states (permissions, preferences) are shared across all branches.
    /// Branch-scoped states (plan progress, history cache) use SaveToBranch instead.
    /// </para>
    /// </remarks>
    public void SaveToSession(
        Session session,
        IReadOnlyDictionary<string, MiddlewareStateFactory> factories)
    {
        if (session == null)
            throw new ArgumentNullException(nameof(session));

        // Save all registered session-scoped persistent states
        foreach (var (key, factory) in factories)
        {
            if (factory.Persistent && factory.Scope == StateScope.Session &&
                States.TryGetValue(key, out var value) && value != null)
            {
                try
                {
                    var json = factory.Serialize(value);
                    session.SetMiddlewareState(key, json);
                }
                catch
                {
                    // Ignore serialization errors - state will not be persisted
                    // This handles schema evolution gracefully
                }
            }
        }
    }

    /// <summary>
    /// Save branch-scoped persistent middleware state to branch.
    /// Only saves states marked with Scope = StateScope.Branch (the default).
    /// Uses the agent's registered factories to determine which states are persistent.
    /// </summary>
    /// <param name="branch">Branch to save state to.</param>
    /// <param name="factories">Middleware state factories from the agent's registry.</param>
    /// <exception cref="ArgumentNullException">Thrown if branch is null.</exception>
    /// <remarks>
    /// <para>Saves branch-scoped state.</para>
    /// <para>
    /// Branch-scoped states (plan progress, history cache) are per-conversation path.
    /// Session-scoped states (permissions, preferences) use SaveToSession instead.
    /// </para>
    /// </remarks>
    public void SaveToBranch(
        Branch branch,
        IReadOnlyDictionary<string, MiddlewareStateFactory> factories)
    {
        if (branch == null)
            throw new ArgumentNullException(nameof(branch));

        // Save all registered branch-scoped persistent states
        foreach (var (key, factory) in factories)
        {
            if (factory.Persistent && factory.Scope == StateScope.Branch &&
                States.TryGetValue(key, out var value) && value != null)
            {
                try
                {
                    var json = factory.Serialize(value);
                    branch.SetMiddlewareState(key, json);
                }
                catch
                {
                    // Ignore serialization errors - state will not be persisted
                    // This handles schema evolution gracefully
                }
            }
        }
    }

    /// <summary>
    /// Merges another MiddlewareState into this one.
    /// States from the other container override states in this container for the same key.
    /// Used to combine session-scoped and branch-scoped states at load time.
    /// </summary>
    /// <param name="other">The other middleware state to merge in.</param>
    /// <returns>New MiddlewareState containing states from both containers.</returns>
    public MiddlewareState Merge(MiddlewareState other)
    {
        if (other == null || other.States.IsEmpty)
            return this;

        if (States.IsEmpty)
            return other;

        var merged = States;
        foreach (var (key, value) in other.States)
        {
            merged = merged.SetItem(key, value);
        }

        return new MiddlewareState
        {
            States = merged,
            SchemaSignature = SchemaSignature ?? other.SchemaSignature,
            SchemaVersion = Math.Max(SchemaVersion, other.SchemaVersion),
            StateVersions = StateVersions ?? other.StateVersions
        };
    }
}

internal sealed class MiddlewareStateJsonConverter : JsonConverter<MiddlewareState>
{
    public override MiddlewareState Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var states = ImmutableDictionary<string, object?>.Empty;
        string? schemaSignature = null;
        var schemaVersion = 1;
        ImmutableDictionary<string, int>? stateVersions = null;

        if (root.TryGetProperty("states", out var statesElement)
            && statesElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in statesElement.EnumerateObject())
            {
                states = states.SetItem(property.Name, property.Value.Clone());
            }
        }

        if (root.TryGetProperty("schemaSignature", out var schemaSignatureElement)
            && schemaSignatureElement.ValueKind == JsonValueKind.String)
        {
            schemaSignature = schemaSignatureElement.GetString();
        }

        if (root.TryGetProperty("schemaVersion", out var schemaVersionElement)
            && schemaVersionElement.ValueKind == JsonValueKind.Number
            && schemaVersionElement.TryGetInt32(out var parsedSchemaVersion))
        {
            schemaVersion = parsedSchemaVersion;
        }

        if (root.TryGetProperty("stateVersions", out var stateVersionsElement)
            && stateVersionsElement.ValueKind == JsonValueKind.Object)
        {
            var builder = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
            foreach (var property in stateVersionsElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Number
                    && property.Value.TryGetInt32(out var version))
                {
                    builder[property.Name] = version;
                }
            }

            stateVersions = builder.ToImmutable();
        }

        return new MiddlewareState
        {
            States = states,
            SchemaSignature = schemaSignature,
            SchemaVersion = schemaVersion,
            StateVersions = stateVersions
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        MiddlewareState value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("states");
        writer.WriteStartObject();
        foreach (var (key, stateValue) in value.States)
        {
            writer.WritePropertyName(key);
            WriteStateValue(writer, key, stateValue, options);
        }
        writer.WriteEndObject();

        if (value.SchemaSignature is not null)
            writer.WriteString("schemaSignature", value.SchemaSignature);

        writer.WriteNumber("schemaVersion", value.SchemaVersion);

        if (value.StateVersions is not null)
        {
            writer.WritePropertyName("stateVersions");
            writer.WriteStartObject();
            foreach (var (key, version) in value.StateVersions)
                writer.WriteNumber(key, version);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void WriteStateValue(
        Utf8JsonWriter writer,
        string key,
        object? value,
        JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        if (value is JsonElement element)
        {
            element.WriteTo(writer);
            return;
        }

        if (TryWriteWithFactory(writer, key, value))
            return;

        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }

    private static bool TryWriteWithFactory(
        Utf8JsonWriter writer,
        string key,
        object value)
    {
        var (_, _, states) = AgentGeneratedRegistry.Snapshot();
        foreach (var factory in states)
        {
            if (!string.Equals(factory.FullyQualifiedName, key, StringComparison.Ordinal))
                continue;

            using var document = JsonDocument.Parse(factory.Serialize(value));
            document.RootElement.WriteTo(writer);
            return true;
        }

        return false;
    }
}
