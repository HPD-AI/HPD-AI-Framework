// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Text.Json;

namespace HPD.Agent.ClientTools;

/// <summary>
/// Defines a tool that executes on the Client.
/// Mirrors the structure Clients provide: name, description, parameters (JSON Schema).
/// Tools are always registered inside a <see cref="clientToolHarnessDefinition"/> (container).
/// </summary>
public sealed record ClientToolDefinition
{
    /// <summary>
    /// Gets the unique tool name used in function calls.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the human-readable description shown to the model.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Gets the JSON schema defining the tool parameters.
    /// </summary>
    public required JsonElement ParametersSchema { get; init; }

    /// <summary>Gets the base policy for this tool.</summary>
    public ClientToolPolicy DefaultPolicy { get; init; } = new();

    /// <summary>Gets the closed operation contract when this is a compound tool.</summary>
    public ClientToolOperationContract? OperationContract { get; init; }

    /// <summary>Gets application-defined tool metadata.</summary>
    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }

    /// <summary>
    /// Validates the tool definition.
    /// </summary>
    /// <exception cref="ArgumentException">If name or description is empty</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("Tool name is required", nameof(Name));

        if (string.IsNullOrWhiteSpace(Description))
            throw new ArgumentException("Tool description is required", nameof(Description));

        if (ParametersSchema.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Tool parameters schema must be a JSON object.");

        if (OperationContract is not null)
            ClientToolContractValidator.Validate(ParametersSchema, OperationContract);
    }

    /// <summary>Resolves the selected operation and its effective policy.</summary>
    public ClientToolResolvedOperation? ResolveOperation(
        IReadOnlyDictionary<string, object?> arguments)
    {
        if (OperationContract is null)
            return null;

        var action = AgentInvocationModes.ResolveDiscriminator(arguments, OperationContract.Discriminator);

        if (!OperationContract.Actions.TryGetValue(action, out var actionPolicy))
            throw new ArgumentException($"Unknown compound tool action '{action}'.", nameof(arguments));

        return new ClientToolResolvedOperation(
            OperationContract.Discriminator,
            action,
            ClientToolPolicy.Resolve(DefaultPolicy, actionPolicy));
    }

}

/// <summary>Defines a closed discriminated operation family.</summary>
public sealed record ClientToolOperationContract
{
    public required string Discriminator { get; init; }
    public required IReadOnlyDictionary<string, ClientToolPolicy> Actions { get; init; }
}

/// <summary>Defines security and invocation behavior for a client tool operation.</summary>
public sealed record ClientToolPolicy
{
    /// <summary>Gets the complete normalized permission declaration for this transport operation.</summary>
    public AIFunctionPermissionDeclaration? Permission { get; init; }
    public bool? MutatesState { get; init; }
    public bool? RequiresFreshContext { get; init; }
    public bool? Destructive { get; init; }
    public bool? Idempotent { get; init; }
    public AgentInvocationModePolicy? InvocationModePolicy { get; init; }
    public AgentOperationNotificationPolicy? OperationNotification { get; init; }

    public static ClientToolPolicy Resolve(
        ClientToolPolicy? basePolicy,
        ClientToolPolicy? operationPolicy = null) =>
        new()
        {
            Permission = operationPolicy?.Permission ?? basePolicy?.Permission,
            MutatesState = operationPolicy?.MutatesState ?? basePolicy?.MutatesState ?? false,
            RequiresFreshContext = operationPolicy?.RequiresFreshContext ??
                basePolicy?.RequiresFreshContext ?? false,
            Destructive = operationPolicy?.Destructive ?? basePolicy?.Destructive ?? false,
            Idempotent = operationPolicy?.Idempotent ?? basePolicy?.Idempotent ?? false,
            InvocationModePolicy = operationPolicy?.InvocationModePolicy ??
                basePolicy?.InvocationModePolicy ?? AgentInvocationModePolicy.SynchronousOnly,
            OperationNotification = operationPolicy?.OperationNotification ??
                basePolicy?.OperationNotification ??
                new AgentOperationNotificationPolicy()
        };
}

/// <summary>Resolved compound operation sent to a provider.</summary>
public sealed record ClientToolResolvedOperation(
    string Discriminator,
    string Action,
    ClientToolPolicy Policy);

internal static class ClientToolContractValidator
{
    public static void Validate(
        JsonElement schema,
        ClientToolOperationContract contract)
    {
        if (string.IsNullOrWhiteSpace(contract.Discriminator))
            throw new ArgumentException("Compound tool discriminator is required.");

        if (!schema.TryGetProperty("oneOf", out var oneOf) ||
            oneOf.ValueKind != JsonValueKind.Array ||
            oneOf.GetArrayLength() == 0)
        {
            throw new ArgumentException("Compound tool schema must contain a non-empty oneOf.");
        }

        var schemaActions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var branch in oneOf.EnumerateArray())
        {
            if (!branch.TryGetProperty("properties", out var properties) ||
                !properties.TryGetProperty(contract.Discriminator, out var discriminatorSchema) ||
                !discriminatorSchema.TryGetProperty("const", out var constant) ||
                constant.ValueKind != JsonValueKind.String ||
                !branch.TryGetProperty("required", out var required) ||
                required.ValueKind != JsonValueKind.Array ||
                !required.EnumerateArray().Any(item =>
                    item.ValueKind == JsonValueKind.String &&
                    item.GetString() == contract.Discriminator))
            {
                throw new ArgumentException(
                    $"Every compound branch must require '{contract.Discriminator}' with one string const.");
            }

            var action = constant.GetString()!;
            if (!schemaActions.Add(action))
                throw new ArgumentException($"Duplicate compound tool action '{action}'.");
        }

        if (!schemaActions.SetEquals(contract.Actions.Keys))
            throw new ArgumentException(
                "Compound schema action set must exactly match the operation policy action set.");

        foreach (var (action, policy) in contract.Actions)
        {
            if (policy.Destructive is true && policy.Permission?.RequiresPermission is not true)
                throw new ArgumentException($"Destructive action '{action}' must require permission.");
            if (policy.Permission is { RequiresPermission: true } permission &&
                string.IsNullOrWhiteSpace(permission.Authority))
            {
                throw new ArgumentException(
                    $"Permissioned action '{action}' requires a permission authority.");
            }
            if (policy.MutatesState is true &&
                (policy.Permission is null || policy.RequiresFreshContext is null))
            {
                throw new ArgumentException(
                    $"Mutating action '{action}' must explicitly declare permission and freshness.");
            }
        }
    }
}
