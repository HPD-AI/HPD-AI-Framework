using System.Text;
using System.Text.Json;

namespace HPD.Agent.Permissions;

internal static class PermissionValidation
{
    internal static void Text(string? value, int maximumBytes, string code, bool required = false)
    {
        if (required && string.IsNullOrWhiteSpace(value) || value is not null && Encoding.UTF8.GetByteCount(value) > maximumBytes)
            throw new InvalidOperationException(code + ": invalid or oversized text.");
    }

    internal static void ValidateResponse(PermissionRequestEvent request, PermissionResponseEvent response)
    {
        if (request.PermissionId != response.PermissionId || request.SourceName != response.SourceName ||
            !request.Choices.Any(choice => choice.Id == response.ChoiceId))
            throw new InvalidOperationException("PermissionResponseUnbound: response is outside the active choice set.");
        Text(response.Feedback, 8192, "PermissionResponseUnbound");
    }

    internal static void Validate(PermissionPolicyResult result, IPermissionPolicy? policy)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Outcome is not PermissionAsk ask)
        {
            if (result.Outcome is not (PermissionAllow or PermissionDeny) || result.Evidence is not null || result.PersistenceByChoice?.Count > 0)
                throw new InvalidOperationException("PermissionChoicePersistenceInvalid: invalid direct policy result.");
            if (result.Outcome is PermissionDeny deny)
            {
                Text(deny.Reason, 2048, "PermissionRequestInvalid", true);
                if (!Enum.IsDefined(deny.DeniedBehavior)) throw new InvalidOperationException("PermissionChoiceInvalid");
            }
            return;
        }
        Text(ask.Reason, 2048, "PermissionRequestInvalid");
        if (ask.Risk is { } risk && !Enum.IsDefined(risk)) throw new InvalidOperationException("PermissionRequestInvalid: risk.");
        if (ask.Resource is { } resource)
        {
            Text(resource.Value, 2048, "PermissionRequestInvalid", true);
            Text(resource.DisplayName, 512, "PermissionRequestInvalid", true);
        }
        if (ask.Choices is null || ask.Choices.Count is < 2 or > 16)
            throw new InvalidOperationException("PermissionChoiceInvalid: invalid choice count.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var choice in ask.Choices)
        {
            Text(choice.Id, 128, "PermissionChoiceInvalid", true);
            Text(choice.Label, 128, "PermissionChoiceInvalid", true);
            if (!ids.Add(choice.Id)) throw new InvalidOperationException("PermissionChoiceDuplicate");
            if (!Enum.IsDefined(choice.Kind) || choice.Kind == PermissionDecisionKind.Allow && choice.DeniedBehavior is not null ||
                choice.DeniedBehavior is { } behavior && !Enum.IsDefined(behavior))
                throw new InvalidOperationException("PermissionChoiceInvalid: denial behavior.");
        }
        if (!ask.Choices.Any(choice => choice.Kind == PermissionDecisionKind.Deny))
            throw new InvalidOperationException("PermissionChoiceMissingDenial");
        if (!ask.Choices.Any(choice => choice.Kind == PermissionDecisionKind.Allow))
            throw new InvalidOperationException("PermissionChoiceInvalid: missing Allow choice.");
        if (result.Evidence is { } evidence)
        {
            Text(evidence.Kind, 128, "PermissionEvidenceInvalid", true);
            Text(evidence.SchemaVersion, 64, "PermissionEvidenceInvalid", true);
            if (evidence.Payload.ValueKind == JsonValueKind.Undefined || Encoding.UTF8.GetByteCount(evidence.Payload.GetRawText()) > 16 * 1024)
                throw new InvalidOperationException("PermissionEvidenceInvalid: payload.");
        }
        foreach (var pair in result.PersistenceByChoice ?? new Dictionary<string, PermissionPersistenceProposal>())
        {
            var choice = ask.Choices.SingleOrDefault(choice => choice.Id == pair.Key);
            var proposal = pair.Value;
            if (choice?.Kind != PermissionDecisionKind.Allow || proposal is null ||
                !Enum.IsDefined(proposal.Kind))
                throw new InvalidOperationException("PermissionChoicePersistenceInvalid: invalid mapping.");
            if (proposal.Kind == PermissionPersistenceKind.Session)
            {
                if (proposal.RuleTypeId is not null || proposal.CanonicalRule is not null)
                    throw new InvalidOperationException("PermissionChoicePersistenceInvalid: unexpected session rule.");
            }
            else
            {
                Text(proposal.RuleTypeId, 128, "PermissionChoicePersistenceInvalid", true);
                if (policy is not IValidatedPermissionRulePolicy || proposal.CanonicalRule is not { } rule ||
                    PermissionRecordIdentity.Canonicalize(rule).Length > PermissionDecisionIndex.MaximumRuleBytes)
                    throw new InvalidOperationException("PermissionChoicePersistenceInvalid: invalid rule proposal.");
            }
        }
    }
}
