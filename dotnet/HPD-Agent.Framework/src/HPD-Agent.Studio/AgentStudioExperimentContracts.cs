using System.Text;
using HPD.AI.Platform.Studio;

namespace HPD.Agent.Studio;

/// <summary>Owns the closed command contracts for the Agent Studio experimentation loop.</summary>
internal static class AgentStudioExperimentContracts
{
    internal const string CreateSession = "agent.session.create";
    internal const string CreateThread = "agent.thread.create";
    internal const string ForkThread = "agent.thread.fork";
    internal const string ClientToolRespond = "agent.client-tool.respond";
    internal const string ContinuationRespond = "agent.continuation.respond";
    internal const string PermissionRespond = "agent.permission.respond";
    internal const string QuestionRespond = "agent.question.respond";
    internal const string SteerInput = "agent.input.steer";
    internal const string SubmitInput = "agent.input.submit";
    internal const string CancelExecution = "agent.execution.cancel";
    internal const string DeleteSession = "agent.session.delete";
    internal const string DeleteThread = "agent.thread.delete";

    private static readonly StudioNamedTypeContract EmptyInput = Node("agent.experiment.empty-input",
        Obj());
    private static readonly StudioNamedTypeContract CreateThreadInput = Node("agent.experiment.thread-create-input",
        Obj(P("name", "agent.studio.thread-name"), P("sessionId", "agent.studio.text")));
    private static readonly StudioNamedTypeContract ForkThreadInput = Node("agent.experiment.thread-fork-input",
        Obj(PN("fromMessageId", "agent.studio.optional-text"), P("name", "agent.studio.thread-name"),
            P("sessionId", "agent.studio.text"), P("threadId", "agent.studio.text")));
    private static readonly StudioNamedTypeContract SubmitInputInput = Node("agent.experiment.input-submit-input",
        Obj(P("sessionId", "agent.studio.text"), P("text", "agent.studio.message-text"),
            P("threadId", "agent.studio.text")));
    private static readonly StudioNamedTypeContract SteerInputInput = Node("agent.experiment.input-steer-input",
        Obj(P("sessionId", "agent.studio.text"), P("text", "agent.studio.message-text"),
            P("threadExecutionId", "agent.studio.text"), P("threadId", "agent.studio.text")));
    private static readonly StudioNamedTypeContract CancelExecutionInput = Node("agent.experiment.execution-cancel-input",
        Obj(P("sessionId", "agent.studio.text"), P("threadExecutionId", "agent.studio.text"),
            P("threadId", "agent.studio.text")));
    private static readonly StudioNamedTypeContract PermissionRespondInput = Node("agent.experiment.permission-respond-input",
        Obj(P("choiceId", "agent.studio.text"), PN("feedback", "agent.studio.optional-text"),
            P("requestId", "agent.studio.text"), P("sessionId", "agent.studio.text"),
            P("sourceName", "agent.studio.text"), P("threadId", "agent.studio.text")));
    private static readonly StudioNamedTypeContract QuestionRespondInput = Node("agent.experiment.question-respond-input",
        Obj(P("answersJson", "agent.studio.answers-json"), P("outcome", "agent.studio.question-outcome"),
            P("requestId", "agent.studio.text"), P("sessionId", "agent.studio.text"),
            P("sourceName", "agent.studio.text"), P("threadId", "agent.studio.text")));
    private static readonly StudioNamedTypeContract ContinuationRespondInput = Node("agent.experiment.continuation-respond-input",
        Obj(P("approved", "agent.studio.boolean"), P("extensionAmount", "agent.studio.extension-count"),
            P("requestId", "agent.studio.text"), P("sessionId", "agent.studio.text"),
            P("sourceName", "agent.studio.text"), P("threadId", "agent.studio.text")));
    private static readonly StudioNamedTypeContract ClientToolRespondInput = Node("agent.experiment.client-tool-respond-input",
        Obj(PN("contentText", "agent.studio.optional-text"), PN("errorMessage", "agent.studio.optional-text"),
            P("outcome", "agent.studio.client-tool-outcome"), P("requestId", "agent.studio.text"),
            P("sessionId", "agent.studio.text"), P("threadId", "agent.studio.text")));
    private static readonly StudioNamedTypeContract DeleteThreadInput = Node("agent.experiment.thread-delete-input",
        Obj(P("recursive", "agent.studio.boolean")));

    private static readonly StudioNamedTypeContract CreateSessionResult = ResultNode(
        "agent.experiment.session-create-result",
        PN("sessionId", "agent.studio.optional-text"), PN("threadId", "agent.studio.optional-text"));
    private static readonly StudioNamedTypeContract CreateThreadResult = ResultNode(
        "agent.experiment.thread-create-result",
        PN("name", "agent.studio.optional-text"), PN("sessionId", "agent.studio.optional-text"),
        PN("threadId", "agent.studio.optional-text"));
    private static readonly StudioNamedTypeContract ForkThreadResult = ResultNode(
        "agent.experiment.thread-fork-result",
        PN("name", "agent.studio.optional-text"), PN("sessionId", "agent.studio.optional-text"),
        PN("sourceThreadId", "agent.studio.optional-text"), PN("threadId", "agent.studio.optional-text"));
    private static readonly StudioNamedTypeContract SubmitInputResult = ResultNode(
        "agent.experiment.input-submit-result",
        PN("disposition", "agent.studio.optional-text"), PN("sessionId", "agent.studio.optional-text"),
        PN("startedAt", "agent.studio.optional-text"), PN("threadExecutionId", "agent.studio.optional-text"),
        PN("threadId", "agent.studio.optional-text"));
    private static readonly StudioNamedTypeContract SteerInputResult = ResultNode(
        "agent.experiment.input-steer-result",
        PN("disposition", "agent.studio.optional-text"), PN("sessionId", "agent.studio.optional-text"),
        PN("startedAt", "agent.studio.optional-text"), PN("threadExecutionId", "agent.studio.optional-text"),
        PN("threadId", "agent.studio.optional-text"));
    private static readonly StudioNamedTypeContract CancelExecutionResult = ResultNode(
        "agent.experiment.execution-cancel-result",
        PN("cancellationApplied", "agent.studio.boolean"), PN("queuePromotion", "agent.studio.optional-text"),
        PN("sessionId", "agent.studio.optional-text"), PN("status", "agent.studio.optional-text"),
        PN("threadExecutionId", "agent.studio.optional-text"), PN("threadId", "agent.studio.optional-text"));
    private static readonly StudioNamedTypeContract PermissionRespondResult = ResultNode(
        "agent.experiment.permission-respond-result",
        PN("message", "agent.studio.optional-text"), PN("requestId", "agent.studio.optional-text"),
        PN("status", "agent.studio.optional-text"));
    private static readonly StudioNamedTypeContract QuestionRespondResult = ResultNode(
        "agent.experiment.question-respond-result",
        PN("message", "agent.studio.optional-text"), PN("requestId", "agent.studio.optional-text"),
        PN("status", "agent.studio.optional-text"));
    private static readonly StudioNamedTypeContract ContinuationRespondResult = ResultNode(
        "agent.experiment.continuation-respond-result",
        PN("message", "agent.studio.optional-text"), PN("requestId", "agent.studio.optional-text"),
        PN("status", "agent.studio.optional-text"));
    private static readonly StudioNamedTypeContract ClientToolRespondResult = ResultNode(
        "agent.experiment.client-tool-respond-result",
        PN("message", "agent.studio.optional-text"), PN("requestId", "agent.studio.optional-text"),
        PN("status", "agent.studio.optional-text"));
    private static readonly StudioNamedTypeContract DeleteResult = Node(
        "agent.experiment.delete-result",
        Obj([
            "{\"name\":\"acknowledgements\",\"wireName\":\"acknowledgements\",\"typeId\":\"agent.studio.command-acknowledgements.one\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}",
            "{\"name\":\"deleted\",\"wireName\":\"deleted\",\"typeId\":\"agent.studio.boolean\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"}",
            "{\"name\":\"expiresAtUtc\",\"wireName\":\"expiresAtUtc\",\"typeId\":\"agent.studio.optional-text\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"}",
            "{\"name\":\"mode\",\"wireName\":\"mode\",\"typeId\":\"agent.studio.experiment-mode\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}",
            "{\"name\":\"previewChecksum\",\"wireName\":\"previewChecksum\",\"typeId\":\"agent.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}",
            "{\"name\":\"receiptChecksum\",\"wireName\":\"receiptChecksum\",\"typeId\":\"agent.studio.optional-sha256\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"}",
        ]));

    internal static StudioNamedTypeContract Input(string commandId) => commandId switch
    {
        CreateSession => EmptyInput,
        CreateThread => CreateThreadInput,
        ForkThread => ForkThreadInput,
        ContinuationRespond => ContinuationRespondInput,
        ClientToolRespond => ClientToolRespondInput,
        PermissionRespond => PermissionRespondInput,
        QuestionRespond => QuestionRespondInput,
        SteerInput => SteerInputInput,
        SubmitInput => SubmitInputInput,
        CancelExecution => CancelExecutionInput,
        DeleteSession => EmptyInput,
        DeleteThread => DeleteThreadInput,
        _ => throw new ArgumentOutOfRangeException(nameof(commandId)),
    };

    internal static StudioNamedTypeContract Result(string commandId) => commandId switch
    {
        CreateSession => CreateSessionResult,
        CreateThread => CreateThreadResult,
        ForkThread => ForkThreadResult,
        ContinuationRespond => ContinuationRespondResult,
        ClientToolRespond => ClientToolRespondResult,
        PermissionRespond => PermissionRespondResult,
        QuestionRespond => QuestionRespondResult,
        SteerInput => SteerInputResult,
        SubmitInput => SubmitInputResult,
        CancelExecution => CancelExecutionResult,
        DeleteSession => DeleteResult,
        DeleteThread => DeleteResult,
        _ => throw new ArgumentOutOfRangeException(nameof(commandId)),
    };

    private static StudioNamedTypeContract ResultNode(string id, params string[] fields) =>
        Node(id, Obj([
            "{\"name\":\"acknowledgements\",\"wireName\":\"acknowledgements\",\"typeId\":\"agent.studio.experiment-empty-acknowledgements\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}",
            "{\"name\":\"expiresAtUtc\",\"wireName\":\"expiresAtUtc\",\"typeId\":\"agent.studio.optional-text\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"}",
            "{\"name\":\"mode\",\"wireName\":\"mode\",\"typeId\":\"agent.studio.experiment-mode\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}",
            "{\"name\":\"previewChecksum\",\"wireName\":\"previewChecksum\",\"typeId\":\"agent.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}",
            "{\"name\":\"receiptChecksum\",\"wireName\":\"receiptChecksum\",\"typeId\":\"agent.studio.optional-sha256\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"}",
            .. fields,
        ]));

    private static StudioNamedTypeContract Node(string id, string descriptor) =>
        StudioNamedTypeContract.Create(id, Encoding.UTF8.GetBytes(descriptor));

    private static string P(string name, string typeId) =>
        $"{{\"name\":\"{name}\",\"wireName\":\"{name}\",\"typeId\":\"{typeId}\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}}";

    private static string PN(string name, string typeId) =>
        $"{{\"name\":\"{name}\",\"wireName\":\"{name}\",\"typeId\":\"{typeId}\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"}}";

    private static string Obj(params string[] properties) =>
        $"{{\"kind\":\"object\",\"properties\":[{string.Join(',', properties.Order(StringComparer.Ordinal))}],\"additionalProperties\":false}}";
}
