using System.Text.Json;
using HPD.Agent.Middleware;
using HPD.Agent.Permissions;

namespace HPD.Agent.Security;

/// <summary>Projects one sandbox capability escalation onto the shared permission lifecycle.</summary>
internal static class SandboxCapabilityPermission
{
    private const string PolicyDescriptorId = "hpd.sandbox.capability.v1";

    internal static async ValueTask<bool> RequestAsync(
        AgentCapabilityKind capability,
        PermissionResource? resource,
        string reason,
        FunctionExecutionContext context,
        IReadOnlyDictionary<string, string?>? publicArguments,
        CancellationToken cancellationToken)
    {
        var authority = AuthorityFor(capability);
        var permissionId = Guid.NewGuid().ToString("N");
        var choices = ChoicesFor(capability, context.ThreadExecutionId);
        var request = new PermissionRequestEvent(
            permissionId,
            context.FunctionCallId,
            nameof(SandboxCapabilityPermission),
            PermissionDomains.SandboxCapability,
            authority,
            context.ResolvedAction,
            resource,
            reason,
            RiskFor(capability),
            choices,
            FunctionName: context.FunctionName,
            Arguments: BuildArguments(capability, resource, publicArguments));

        PermissionChoice selected;
        string? feedback = null;
        var source = PermissionGrantSource.UserDecision;
        try
        {
            var response = await context.RequestAsync<PermissionRequestEvent, PermissionResponseEvent>(
                request, cancellationToken, timeout: null).ConfigureAwait(false);
            if (!string.Equals(response.PermissionId, permissionId, StringComparison.Ordinal) ||
                !string.Equals(response.SourceName, request.SourceName, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "PermissionResponseUnbound: sandbox response does not own this request.");
            selected = choices.SingleOrDefault(choice =>
                    string.Equals(choice.Id, response.ChoiceId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException(
                    $"PermissionResponseUnbound: unknown sandbox choice '{response.ChoiceId}'.");
            feedback = response.Feedback;
        }
        catch (TimeoutException)
        {
            selected = DenialChoice(choices);
            source = PermissionGrantSource.TimedOut;
            feedback = "Sandbox permission request timed out.";
        }
        catch (OperationCanceledException)
        {
            selected = DenialChoice(choices);
            source = PermissionGrantSource.Cancelled;
            feedback = "Sandbox permission request was cancelled.";
        }

        var denialReason = selected.Kind == PermissionDecisionKind.Allow
            ? null
            : feedback ?? reason;
        await context.PublishAsync(new PermissionResolvedEvent(
            permissionId,
            context.FunctionCallId,
            context.AgentName,
            PermissionDomains.SandboxCapability,
            authority,
            context.ResolvedAction,
            PolicyDescriptorId,
            resource,
            selected.Kind,
            selected.Id,
            source,
            denialReason), source is PermissionGrantSource.Cancelled or PermissionGrantSource.TimedOut
            ? CancellationToken.None
            : cancellationToken).ConfigureAwait(false);
        return selected.Kind == PermissionDecisionKind.Allow;
    }

    private static IReadOnlyList<PermissionChoice> ChoicesFor(
        AgentCapabilityKind capability,
        string? threadExecutionId)
    {
        var choices = new List<PermissionChoice>
        {
            new("allow_once", "Allow once", PermissionDecisionKind.Allow)
        };
        if (!string.IsNullOrWhiteSpace(threadExecutionId))
            choices.Add(new("allow_for_turn", TurnLabel(capability), PermissionDecisionKind.Allow));
        choices.Add(new("deny_once", "Deny", PermissionDecisionKind.Deny));
        return choices;
    }

    private static PermissionChoice DenialChoice(IReadOnlyList<PermissionChoice> choices) =>
        choices.FirstOrDefault(choice => choice.Id == "deny_once")
        ?? choices.First(choice => choice.Kind == PermissionDecisionKind.Deny);

    private static string AuthorityFor(AgentCapabilityKind capability) => capability switch
    {
        AgentCapabilityKind.FilesystemRead => "sandbox/filesystem-read",
        AgentCapabilityKind.FilesystemWrite => "sandbox/filesystem-write",
        AgentCapabilityKind.NetworkEgress => "sandbox/network-egress",
        AgentCapabilityKind.LocalBinding => "sandbox/local-binding",
        AgentCapabilityKind.InteractiveTerminal => "sandbox/interactive-terminal",
        AgentCapabilityKind.UnsandboxedExecution => "sandbox/unsandboxed-execution",
        _ => throw new ArgumentOutOfRangeException(nameof(capability))
    };

    private static PermissionRisk RiskFor(AgentCapabilityKind capability) => capability switch
    {
        AgentCapabilityKind.FilesystemRead => PermissionRisk.Medium,
        AgentCapabilityKind.FilesystemWrite => PermissionRisk.High,
        AgentCapabilityKind.NetworkEgress => PermissionRisk.High,
        AgentCapabilityKind.LocalBinding => PermissionRisk.Medium,
        AgentCapabilityKind.InteractiveTerminal => PermissionRisk.High,
        AgentCapabilityKind.UnsandboxedExecution => PermissionRisk.Critical,
        _ => throw new ArgumentOutOfRangeException(nameof(capability))
    };

    private static string TurnLabel(AgentCapabilityKind capability) => capability switch
    {
        AgentCapabilityKind.FilesystemRead or AgentCapabilityKind.FilesystemWrite =>
            "Allow all files this turn",
        AgentCapabilityKind.NetworkEgress => "Allow all network access this turn",
        AgentCapabilityKind.LocalBinding => "Allow all local bindings this turn",
        AgentCapabilityKind.InteractiveTerminal => "Allow all terminal access this turn",
        AgentCapabilityKind.UnsandboxedExecution => "Allow all unsandboxed runs this turn",
        _ => throw new ArgumentOutOfRangeException(nameof(capability))
    };

    private static JsonElement BuildArguments(
        AgentCapabilityKind capability,
        PermissionResource? resource,
        IReadOnlyDictionary<string, string?>? publicArguments)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("capability", capability.ToString());
            if (resource is not null)
                writer.WriteString("resource", resource.Value);
            if (publicArguments is not null)
            {
                foreach (var (name, value) in publicArguments)
                {
                    if (value is not null)
                        writer.WriteString(name, value);
                }
            }
            writer.WriteEndObject();
        }
        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }
}
