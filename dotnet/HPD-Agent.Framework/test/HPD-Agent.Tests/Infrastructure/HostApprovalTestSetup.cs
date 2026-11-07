using HPD.Agent.Middleware;

namespace HPD.Agent;

/// <summary>Uses real enforcement with the standard host AutoApprove run policy.</summary>
internal static class HostApprovalTestSetup
{
    internal static AgentBuilder WithHostAutoApproval(this AgentBuilder builder)
    {
        if (builder.Config.SessionStore is null)
            builder.WithSessionStore(new InMemorySessionStore(
                builder.Config.EventComposition?.Codec ?? HPD.Agent.Tests.TestEventApplication.Codec));
        return builder.WithPermissions().WithMiddleware(new ConfigureHostApproval());
    }

    private sealed class ConfigureHostApproval : IAgentMiddleware
    {
        public Task BeforeMessageTurnAsync(BeforeMessageTurnContext context, CancellationToken cancellationToken)
        {
            context.RunConfig.Security = context.RunConfig.Security with { Approval = AgentApprovalPolicy.AutoApprove };
            return Task.CompletedTask;
        }
    }
}
