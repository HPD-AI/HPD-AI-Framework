using System.Text.Json.Serialization;
using HPD.Agent.Permissions;
using HPD.Agent.Serialization;
using HPD.Agent.TUI.Interactions;

namespace HPD.Agent.TUI.Tests;

public sealed partial class PermissionPresentationRendererTests
{
    [Fact]
    public void Registration_requires_exact_presentation_identity_and_rejects_duplicates()
    {
        HpdAgentJsonUtilities.RegisterTypeInfoResolver(TicketJsonContext.Default);
        var builder = new HpdAgentTuiBuilder();

        builder.AddPermissionPresentationRenderer(
            "test.permission.ticket",
            new TicketRenderer());

        Assert.Throws<InvalidOperationException>(() => builder.AddPermissionPresentationRenderer(
            "test.permission.ticket",
            new TicketRenderer()));
        Assert.Throws<InvalidOperationException>(() => new HpdAgentTuiBuilder()
            .AddPermissionPresentationRenderer("wrong.id", new TicketRenderer()));
    }

    [PermissionPresentation("test.permission.ticket", typeof(TicketJsonContext))]
    private sealed record Ticket(string Label);

    private sealed class TicketRenderer : IPermissionPresentationRenderer<Ticket>
    {
        public ValueTask<PermissionDecision> RenderAsync(
            Ticket presentation,
            PermissionChoiceSet choices,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PermissionDecision
            {
                Kind = PermissionDecisionKind.Allow,
                ChoiceId = "allow_once"
            });
    }

    [JsonSerializable(typeof(Ticket))]
    private sealed partial class TicketJsonContext : JsonSerializerContext;
}
