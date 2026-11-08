using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent;

internal sealed class RuntimeAgentInputHandler : IAgentInputHandler<RuntimeAgentInputEvent>
{
    public async ValueTask<AgentInputResult> HandleAsync(
        RuntimeAgentInputEvent input,
        AgentInputHandlingContext context,
        CancellationToken cancellationToken)
    {
        var registry = context.Services?.GetService<RuntimeAgentExtensionRegistry>()
            ?? throw new InvalidOperationException(
                "Runtime input was submitted but no RuntimeAgentExtensionRegistry is registered.");
        if (!registry.TryGetInputHandler(input, out var handler))
        {
            throw new InvalidOperationException(
                $"Runtime input schema '{input.ExtensionNamespace}:{input.SchemaName}@{input.SchemaVersion}' " +
                "has no matching trusted handler or digest.");
        }
        var key = $"{input.ExtensionNamespace}:{input.SchemaName}@{input.SchemaVersion}";
        if (!registry.TryValidate(key, input.Payload, out var validationError))
            throw new InvalidOperationException(validationError);

        var result = await handler.HandleAsync(
                input,
                new RuntimeAgentInputContext(
                    context.AgentName,
                    input.SessionId,
                    input.ThreadId,
                    input.RunConfig ?? context.RuntimeRunConfig,
                    context.Services),
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.Accepted)
        {
            throw new InvalidOperationException(
                result.ErrorCode ?? "The runtime input handler rejected the input.");
        }
        return new AgentInputResult.Control(AgentInputDisposition.Accepted, input.ThreadExecutionId);
    }
}

/// <summary>Dependency-injection registration for runtime extension schemas and handlers.</summary>
public static class RuntimeAgentExtensionServiceCollectionExtensions
{
    public static IServiceCollection AddHpdRuntimeAgentExtensions(
        this IServiceCollection services,
        RuntimeAgentExtensionRegistry? registry = null,
        IRuntimeAgentExtensionSchemaStore? schemaStore = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(registry ?? new RuntimeAgentExtensionRegistry(store: schemaStore));
        if (schemaStore is not null)
            services.AddSingleton(schemaStore);
        return services;
    }
}
