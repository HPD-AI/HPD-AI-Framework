using Microsoft.Extensions.AI;

namespace HPD.Agent.MCP;

/// <summary>Activates an optional SDK extension without adding it to the base MCP dependency graph.</summary>
internal interface IMcpRemoteTaskAdapter
{
    ValueTask<AgentInvocationResult?> TryStartAsync(
        McpToolInvocationRuntime.McpToolInvocationRequest request,
        AIFunctionArguments arguments,
        CancellationToken cancellationToken);

    bool CanRecover(AgentOperationRecoveryReference recoveryReference);

    ValueTask<bool> TryRecoverAsync(
        AgentOperation operation,
        McpRuntime runtime,
        IMcpRecoveryReferenceProtector protector,
        AgentCapabilityLease revisionLease,
        CancellationToken cancellationToken);
}
