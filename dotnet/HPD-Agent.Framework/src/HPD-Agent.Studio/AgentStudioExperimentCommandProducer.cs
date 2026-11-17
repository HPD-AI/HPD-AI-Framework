using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.AI.Platform.Studio;
using HPD.Agent;
using HPD.Agent.ClientTools;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HPD.Agent.Studio;

/// <summary>
/// Reviews and executes Agent session, thread, input, and cancellation commands
/// through the installed Agent hosting authority.
/// </summary>
internal sealed class AgentStudioExperimentCommandProducer(IServiceProvider services, string commandId)
    : IStudioCommandProducer
{
    private const string PageId = "agent.workspace";
    private const int MaximumTextLength = 32_768;
    private readonly ConcurrentDictionary<string, PreviewEvidence> _previews = new(StringComparer.Ordinal);

    public async ValueTask<StudioCanonicalJson?> PreviewAsync(
        StudioProducerInvocation invocation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await AuthorizedAsync(invocation, "base.studio.action.preview", cancellationToken).ConfigureAwait(false) ||
            !TryReadPreview(invocation.Request, out CommandTarget? target, out JsonElement input) ||
            !TargetMatches(target!, invocation))
            return null;

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        if (!await ValidatePreviewAsync(scope.ServiceProvider, target!, input, cancellationToken).ConfigureAwait(false))
            return null;

        string responseAuthority = Hex(invocation.Authority.Checksum);
        string clientInputId = commandId == AgentStudioExperimentContracts.SubmitInput
            ? Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)
            : "";
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        string previewChecksum = Hash(string.Join('\n', commandId, target!.AuthorityChecksum,
            responseAuthority, input.GetRawText(), clientInputId));
        foreach ((string key, PreviewEvidence stale) in _previews)
            if (stale.ExpiresAtUtc <= DateTimeOffset.UtcNow) _previews.TryRemove(key, out _);
        if (_previews.Count >= 1_024 ||
            !_previews.TryAdd(previewChecksum, new PreviewEvidence(
                target.ApplicationId, target.AgentId, target.AuthorityChecksum,
                responseAuthority, input.Clone(), clientInputId, expiresAt)))
            return null;

        return Result("preview", previewChecksum, expiresAt, null, null);
    }

    public async ValueTask<StudioCanonicalJson?> ExecuteAsync(
        StudioProducerInvocation invocation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await AuthorizedAsync(invocation, "base.studio.action.execute", cancellationToken).ConfigureAwait(false) ||
            !TryReadExecute(invocation.Request, out CommandTarget? target, out string? requestIdentity,
                out string? previewChecksum) ||
            target is null || requestIdentity is null || previewChecksum is null ||
            !_previews.TryGetValue(previewChecksum, out PreviewEvidence? preview) ||
            preview.ExpiresAtUtc <= DateTimeOffset.UtcNow ||
            !StringComparer.Ordinal.Equals(preview.ApplicationId, target.ApplicationId) ||
            !StringComparer.Ordinal.Equals(preview.AgentId, target.AgentId) ||
            !StringComparer.Ordinal.Equals(preview.TargetAuthorityChecksum, target.AuthorityChecksum) ||
            !StringComparer.Ordinal.Equals(preview.ResponseAuthorityChecksum, Hex(invocation.Authority.Checksum)))
            throw new StudioCommandFailedBeforeInfluenceException();

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        CommandResult result;
        try
        {
            result = await InvokeAsync(scope.ServiceProvider, target, preview, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            scope.ServiceProvider.GetService<ILogger<AgentStudioExperimentCommandProducer>>()?
                .LogWarning(exception, "Agent Studio command {CommandId} was cancelled after dispatch.", commandId);
            _previews.TryRemove(previewChecksum, out _);
            throw new StudioCommandIndeterminateException();
        }
        catch (StudioCommandFailedBeforeInfluenceException exception)
        {
            scope.ServiceProvider.GetService<ILogger<AgentStudioExperimentCommandProducer>>()?
                .LogWarning(exception, "Agent Studio command {CommandId} failed before influence.", commandId);
            throw;
        }
        catch (Exception exception)
        {
            scope.ServiceProvider.GetService<ILogger<AgentStudioExperimentCommandProducer>>()?
                .LogError(exception, "Agent Studio command {CommandId} failed after dispatch.", commandId);
            _previews.TryRemove(previewChecksum, out _);
            throw new StudioCommandIndeterminateException();
        }

        if (!result.Accepted)
            throw new StudioCommandFailedBeforeInfluenceException();

        _previews.TryRemove(previewChecksum, out _);
        string receipt = Hash(string.Join('\n', commandId, requestIdentity, previewChecksum,
            result.ReceiptMaterial));
        return Result("execute", previewChecksum, null, receipt, result);
    }

    private async ValueTask<bool> ValidatePreviewAsync(IServiceProvider scopedServices, CommandTarget target,
        JsonElement input, CancellationToken cancellationToken)
    {
        if (scopedServices.GetService<IAgentDefinitionService>() is not { } definitions ||
            await definitions.GetAgentAsync(target.AgentId, cancellationToken).ConfigureAwait(false) is null)
            return false;

        if (commandId == AgentStudioExperimentContracts.CreateSession)
            return input.EnumerateObject().Any() is false;

        if (!TryInputText(input, "sessionId", out string? sessionId)) return false;
        if (scopedServices.GetService<IAgentSessionService>() is not { } sessions ||
            await sessions.GetSessionAsync(sessionId!, cancellationToken).ConfigureAwait(false) is null)
            return false;
        if (scopedServices.GetService<IAgentThreadService>() is not { } threads)
            return false;

        if (commandId == AgentStudioExperimentContracts.CreateThread)
        {
            if (!TryInputText(input, "name", out string? name) || name!.Length > 256) return false;
            AgentServiceResult<IReadOnlyList<ThreadDto>> existing =
                await threads.ListThreadsAsync(sessionId!, cancellationToken).ConfigureAwait(false);
            return existing.Status == AgentServiceStatus.Success &&
                existing.Value?.Any(value => StringComparer.Ordinal.Equals(value.DefaultAgentId, target.AgentId)) == true;
        }

        if (!TryInputText(input, "threadId", out string? threadId)) return false;
        AgentServiceResult<ThreadDto> thread =
            await threads.GetThreadAsync(sessionId!, threadId!, cancellationToken).ConfigureAwait(false);
        if (thread.Status != AgentServiceStatus.Success || thread.Value is null ||
            !StringComparer.Ordinal.Equals(thread.Value.DefaultAgentId, target.AgentId))
            return false;

        if (commandId == AgentStudioExperimentContracts.ForkThread)
        {
            if (!TryOptionalInputText(input, "fromMessageId", out string? fromMessageId) ||
                !TryInputText(input, "name", out string? name) || name!.Length > 256)
                return false;
            if (string.IsNullOrEmpty(fromMessageId)) return true;
            if (scopedServices.GetService<SessionManager>() is not { Store: var store }) return false;
            HPD.Agent.Thread? projection = await store.ProjectThreadAsync(
                sessionId!, threadId!, ThreadProjectionPurpose.ThreadHistory, cancellationToken).ConfigureAwait(false);
            return projection?.Messages.Any(message =>
                StringComparer.Ordinal.Equals(message.MessageId, fromMessageId)) == true;
        }

        if (commandId == AgentStudioExperimentContracts.PermissionRespond)
        {
            if (!TryInputText(input, "requestId", out string? requestId) ||
                !TryInputText(input, "sourceName", out string? sourceName) ||
                !TryInputText(input, "choiceId", out string? choiceId) ||
                !TryOptionalInputText(input, "feedback", out string? feedback) ||
                feedback?.Length > 8_192 ||
                scopedServices.GetService<IAgentStreamingService>() is not { } streaming)
                return false;
            AgentServiceResult<ThreadRuntimeStateDto> state =
                await streaming.GetThreadStateAsync(target.AgentId, sessionId!, threadId!, cancellationToken)
                    .ConfigureAwait(false);
            PermissionRequestEvent? request = state.Status == AgentServiceStatus.Success
                ? state.Value?.PendingRequests.Select(static value => value.Request)
                    .OfType<PermissionRequestEvent>()
                    .SingleOrDefault(value => StringComparer.Ordinal.Equals(value.RequestId, requestId) &&
                        StringComparer.Ordinal.Equals(value.SourceName, sourceName))
                : null;
            return request is not null && request.Choices.Any(value =>
                StringComparer.Ordinal.Equals(value.Id, choiceId));
        }

        if (commandId == AgentStudioExperimentContracts.QuestionRespond)
        {
            if (!TryInputText(input, "requestId", out string? requestId) ||
                !TryInputText(input, "sourceName", out string? sourceName) ||
                !TryInputText(input, "outcome", out string? outcomeText) ||
                !Enum.TryParse(outcomeText, true, out QuestionOutcome outcome) ||
                !TryInputText(input, "answersJson", out string? answersJson) ||
                !TryParseQuestionAnswers(answersJson!, out QuestionAnswer[]? answers) ||
                scopedServices.GetService<IAgentStreamingService>() is not { } streaming)
                return false;
            AgentServiceResult<ThreadRuntimeStateDto> state =
                await streaming.GetThreadStateAsync(target.AgentId, sessionId!, threadId!, cancellationToken)
                    .ConfigureAwait(false);
            UserQuestionRequestEvent? request = state.Status == AgentServiceStatus.Success
                ? state.Value?.PendingRequests.Select(static value => value.Request)
                    .OfType<UserQuestionRequestEvent>()
                    .SingleOrDefault(value => StringComparer.Ordinal.Equals(value.RequestId, requestId) &&
                        StringComparer.Ordinal.Equals(value.SourceName, sourceName))
                : null;
            if (request is null) return false;
            try
            {
                QuestionValidation.ValidateResponse(request.Questions,
                    new QuestionResponseEvent(request.RequestId, request.SourceName, outcome, answers!));
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        if (commandId == AgentStudioExperimentContracts.ContinuationRespond)
        {
            if (!TryInputText(input, "requestId", out string? requestId) ||
                !TryInputText(input, "sourceName", out string? sourceName) ||
                !TryInputBool(input, "approved", out bool _) ||
                !TryInputInt(input, "extensionAmount", out int extensionAmount) ||
                extensionAmount is < 0 or > 1_000 ||
                scopedServices.GetService<IAgentStreamingService>() is not { } streaming)
                return false;
            AgentServiceResult<ThreadRuntimeStateDto> state =
                await streaming.GetThreadStateAsync(target.AgentId, sessionId!, threadId!, cancellationToken)
                    .ConfigureAwait(false);
            return state.Status == AgentServiceStatus.Success &&
                state.Value?.PendingRequests.Select(static value => value.Request)
                    .OfType<ContinuationRequestEvent>()
                    .Any(value => StringComparer.Ordinal.Equals(value.RequestId, requestId) &&
                        StringComparer.Ordinal.Equals(value.SourceName, sourceName)) == true;
        }

        if (commandId == AgentStudioExperimentContracts.ClientToolRespond)
        {
            if (!TryInputText(input, "requestId", out string? requestId) ||
                !TryInputText(input, "outcome", out string? outcomeText) ||
                !Enum.TryParse(outcomeText, true, out ClientToolInvokeOutcomeKind outcome) ||
                outcome == ClientToolInvokeOutcomeKind.AcceptedBackground ||
                !TryOptionalInputText(input, "contentText", out string? contentText) ||
                !TryOptionalInputText(input, "errorMessage", out string? errorMessage) ||
                (outcome == ClientToolInvokeOutcomeKind.Completed && string.IsNullOrWhiteSpace(contentText)) ||
                (outcome is ClientToolInvokeOutcomeKind.Failed or ClientToolInvokeOutcomeKind.Rejected &&
                    string.IsNullOrWhiteSpace(errorMessage)) ||
                scopedServices.GetService<IAgentStreamingService>() is not { } streaming)
                return false;
            AgentServiceResult<ThreadRuntimeStateDto> state =
                await streaming.GetThreadStateAsync(target.AgentId, sessionId!, threadId!, cancellationToken)
                    .ConfigureAwait(false);
            return state.Status == AgentServiceStatus.Success &&
                state.Value?.PendingRequests.Select(static value => value.Request)
                    .OfType<ClientToolInvokeRequestEvent>()
                    .Any(value => StringComparer.Ordinal.Equals(value.RequestId, requestId)) == true;
        }

        if (commandId == AgentStudioExperimentContracts.SteerInput)
        {
            if (!TryInputText(input, "text", out string? text) || text!.Length > MaximumTextLength ||
                !TryInputText(input, "threadExecutionId", out string? executionId) ||
                scopedServices.GetService<IAgentStreamingService>() is not { } streaming)
                return false;
            AgentServiceResult<ThreadRuntimeStateDto> state =
                await streaming.GetThreadStateAsync(target.AgentId, sessionId!, threadId!, cancellationToken)
                    .ConfigureAwait(false);
            return state.Status == AgentServiceStatus.Success &&
                StringComparer.Ordinal.Equals(state.Value?.ActiveExecution?.ThreadExecutionId, executionId);
        }

        if (commandId == AgentStudioExperimentContracts.SubmitInput)
        {
            if (!TryInputText(input, "text", out string? text) || text!.Length > MaximumTextLength) return false;
            if (scopedServices.GetService<IAgentStreamingService>() is not { } streaming) return false;
            AgentServiceResult<ThreadRuntimeStateDto> state =
                await streaming.GetThreadStateAsync(target.AgentId, sessionId!, threadId!, cancellationToken)
                    .ConfigureAwait(false);
            return state.Status == AgentServiceStatus.Success && state.Value?.ActiveExecution is null;
        }

        if (commandId == AgentStudioExperimentContracts.CancelExecution)
        {
            if (!TryInputText(input, "threadExecutionId", out string? executionId) ||
                scopedServices.GetService<IAgentThreadExecutionService>() is not { } executions)
                return false;
            AgentServiceResult<ThreadExecutionDto> execution = await executions.GetExecutionAsync(
                target.AgentId, sessionId!, threadId!, executionId!, cancellationToken).ConfigureAwait(false);
            return execution.Status == AgentServiceStatus.Success &&
                execution.Value?.Status == ThreadExecutionStatus.Active;
        }

        return false;
    }

    private async ValueTask<CommandResult> InvokeAsync(IServiceProvider scopedServices, CommandTarget target,
        PreviewEvidence preview, CancellationToken cancellationToken)
    {
        switch (commandId)
        {
            case AgentStudioExperimentContracts.CreateSession:
            {
                if (scopedServices.GetService<IAgentSessionService>() is not { } sessions)
                    return CommandResult.Rejected;
                SessionDto session = await sessions.CreateSessionAsync(
                    new CreateSessionRequest(AgentId: target.AgentId), cancellationToken).ConfigureAwait(false);
                return CommandResult.Success(
                    $"session.create\n{session.Id}\nmain",
                    Fields(
                        ("sessionId", writer => writer.WriteString("sessionId", session.Id)),
                        ("threadId", writer => writer.WriteString("threadId", "main"))));
            }
            case AgentStudioExperimentContracts.CreateThread:
            {
                if (!TryInputText(preview.Input, "sessionId", out string? sessionId) ||
                    !TryInputText(preview.Input, "name", out string? name) ||
                    scopedServices.GetService<IAgentThreadService>() is not { } threads)
                    return CommandResult.Rejected;
                AgentServiceResult<ThreadDto> result = await threads.CreateThreadAsync(
                    target.AgentId, sessionId!, new CreateThreadRequest("", name!, null, null),
                    cancellationToken).ConfigureAwait(false);
                if (result.Status != AgentServiceStatus.Success || result.Value is null)
                    return CommandResult.Rejected;
                ThreadDto thread = result.Value;
                return CommandResult.Success(
                    $"thread.create\n{sessionId}\n{thread.Id}\n{thread.Name}",
                    Fields(
                        ("name", writer => writer.WriteString("name", thread.Name)),
                        ("sessionId", writer => writer.WriteString("sessionId", sessionId)),
                        ("threadId", writer => writer.WriteString("threadId", thread.Id))));
            }
            case AgentStudioExperimentContracts.ForkThread:
            {
                if (!TryInputText(preview.Input, "sessionId", out string? sessionId) ||
                    !TryInputText(preview.Input, "threadId", out string? threadId) ||
                    !TryInputText(preview.Input, "name", out string? name) ||
                    !TryOptionalInputText(preview.Input, "fromMessageId", out string? fromMessageId) ||
                    scopedServices.GetService<IAgentThreadService>() is not { } threads)
                    return CommandResult.Rejected;
                AgentServiceResult<ThreadForkResultDto> result = await threads.ForkThreadAsync(
                    target.AgentId, sessionId!, threadId!,
                    new ForkThreadRequest(null, string.IsNullOrEmpty(fromMessageId) ? null : fromMessageId,
                        name!, null, null),
                    cancellationToken).ConfigureAwait(false);
                if (result.Status != AgentServiceStatus.Success || result.Value is null)
                    return CommandResult.Rejected;
                ThreadDto targetThread = result.Value.Target;
                return CommandResult.Success(
                    $"thread.fork\n{sessionId}\n{threadId}\n{targetThread.Id}\n{fromMessageId}",
                    Fields(
                        ("name", writer => writer.WriteString("name", targetThread.Name)),
                        ("sessionId", writer => writer.WriteString("sessionId", sessionId)),
                        ("sourceThreadId", writer => writer.WriteString("sourceThreadId", threadId)),
                        ("threadId", writer => writer.WriteString("threadId", targetThread.Id))));
            }
            case AgentStudioExperimentContracts.PermissionRespond:
            {
                if (!TryInputText(preview.Input, "sessionId", out string? sessionId) ||
                    !TryInputText(preview.Input, "threadId", out string? threadId) ||
                    !TryInputText(preview.Input, "requestId", out string? requestId) ||
                    !TryInputText(preview.Input, "sourceName", out string? sourceName) ||
                    !TryInputText(preview.Input, "choiceId", out string? choiceId) ||
                    !TryOptionalInputText(preview.Input, "feedback", out string? feedback) ||
                    scopedServices.GetService<IAgentMiddlewareResponseService>() is not { } responses)
                    return CommandResult.Rejected;
                AgentServiceResult<AgentRespondResult> result = await responses.AnswerRequestAsync(
                    target.AgentId, sessionId!, threadId!,
                    new PermissionResponseEvent(requestId!, sourceName!, choiceId!, feedback),
                    cancellationToken).ConfigureAwait(false);
                if (result.Status != AgentServiceStatus.Success || result.Value?.Accepted != true)
                    return CommandResult.Rejected;
                AgentRespondResult answer = result.Value;
                return CommandResult.Success(
                    $"permission.respond\n{requestId}\n{choiceId}\n{answer.RequestId}\n{answer.Status}",
                    ResponseFields(answer));
            }
            case AgentStudioExperimentContracts.QuestionRespond:
            {
                if (!TryInputText(preview.Input, "sessionId", out string? sessionId) ||
                    !TryInputText(preview.Input, "threadId", out string? threadId) ||
                    !TryInputText(preview.Input, "requestId", out string? requestId) ||
                    !TryInputText(preview.Input, "sourceName", out string? sourceName) ||
                    !TryInputText(preview.Input, "outcome", out string? outcomeText) ||
                    !Enum.TryParse(outcomeText, true, out QuestionOutcome outcome) ||
                    !TryInputText(preview.Input, "answersJson", out string? answersJson) ||
                    !TryParseQuestionAnswers(answersJson!, out QuestionAnswer[]? answers) ||
                    scopedServices.GetService<IAgentMiddlewareResponseService>() is not { } responses)
                    return CommandResult.Rejected;
                AgentServiceResult<AgentRespondResult> result = await responses.AnswerRequestAsync(
                    target.AgentId, sessionId!, threadId!,
                    new QuestionResponseEvent(requestId!, sourceName!, outcome, answers!),
                    cancellationToken).ConfigureAwait(false);
                if (result.Status != AgentServiceStatus.Success || result.Value?.Accepted != true)
                    return CommandResult.Rejected;
                AgentRespondResult answer = result.Value;
                return CommandResult.Success(
                    $"question.respond\n{requestId}\n{outcome}\n{answer.RequestId}\n{answer.Status}",
                    ResponseFields(answer));
            }
            case AgentStudioExperimentContracts.ContinuationRespond:
            {
                if (!TryInputText(preview.Input, "sessionId", out string? sessionId) ||
                    !TryInputText(preview.Input, "threadId", out string? threadId) ||
                    !TryInputText(preview.Input, "requestId", out string? requestId) ||
                    !TryInputText(preview.Input, "sourceName", out string? sourceName) ||
                    !TryInputBool(preview.Input, "approved", out bool approved) ||
                    !TryInputInt(preview.Input, "extensionAmount", out int extensionAmount) ||
                    scopedServices.GetService<IAgentMiddlewareResponseService>() is not { } responses)
                    return CommandResult.Rejected;
                AgentServiceResult<AgentRespondResult> result = await responses.AnswerRequestAsync(
                    target.AgentId, sessionId!, threadId!,
                    new ContinuationResponseEvent(requestId!, sourceName!, approved, extensionAmount),
                    cancellationToken).ConfigureAwait(false);
                if (result.Status != AgentServiceStatus.Success || result.Value?.Accepted != true)
                    return CommandResult.Rejected;
                AgentRespondResult answer = result.Value;
                return CommandResult.Success(
                    $"continuation.respond\n{requestId}\n{approved}\n{extensionAmount}\n{answer.Status}",
                    ResponseFields(answer));
            }
            case AgentStudioExperimentContracts.ClientToolRespond:
            {
                if (!TryInputText(preview.Input, "sessionId", out string? sessionId) ||
                    !TryInputText(preview.Input, "threadId", out string? threadId) ||
                    !TryInputText(preview.Input, "requestId", out string? requestId) ||
                    !TryInputText(preview.Input, "outcome", out string? outcomeText) ||
                    !Enum.TryParse(outcomeText, true, out ClientToolInvokeOutcomeKind outcome) ||
                    !TryOptionalInputText(preview.Input, "contentText", out string? contentText) ||
                    !TryOptionalInputText(preview.Input, "errorMessage", out string? errorMessage) ||
                    scopedServices.GetService<IAgentMiddlewareResponseService>() is not { } responses)
                    return CommandResult.Rejected;
                var response = new ClientToolInvokeOutcomeEvent
                {
                    RequestId = requestId!,
                    Outcome = outcome,
                    Content = outcome == ClientToolInvokeOutcomeKind.Completed && contentText is not null
                        ? [new HPD.Agent.ClientTools.TextContent(contentText)]
                        : null,
                    ErrorMessage = errorMessage,
                };
                AgentServiceResult<AgentRespondResult> result = await responses.AnswerRequestAsync(
                    target.AgentId, sessionId!, threadId!, response, cancellationToken).ConfigureAwait(false);
                if (result.Status != AgentServiceStatus.Success || result.Value?.Accepted != true)
                    return CommandResult.Rejected;
                AgentRespondResult answer = result.Value;
                return CommandResult.Success(
                    $"client-tool.respond\n{requestId}\n{outcome}\n{answer.Status}",
                    ResponseFields(answer));
            }
            case AgentStudioExperimentContracts.SteerInput:
            {
                if (!TryInputText(preview.Input, "sessionId", out string? sessionId) ||
                    !TryInputText(preview.Input, "threadId", out string? threadId) ||
                    !TryInputText(preview.Input, "text", out string? text) ||
                    !TryInputText(preview.Input, "threadExecutionId", out string? executionId) ||
                    scopedServices.GetService<IAgentStreamingService>() is not { } streaming)
                    return CommandResult.Rejected;
                AgentServiceResult<InputSubmissionDto> result = await streaming.SubmitInputAsync(
                    target.AgentId, sessionId!, threadId!,
                    new UserMessagesInputEvent
                    {
                        Messages = [new ChatMessage(ChatRole.User, text!)],
                        Delivery = AgentInputDelivery.Steer,
                        ClientInputId = preview.ClientInputId,
                        ThreadExecutionId = executionId,
                    },
                    cancellationToken).ConfigureAwait(false);
                if (result.Status != AgentServiceStatus.Success || result.Value is null)
                    return CommandResult.Rejected;
                InputSubmissionDto submission = result.Value;
                return CommandResult.Success(
                    $"input.steer\n{sessionId}\n{threadId}\n{submission.Disposition}\n{submission.ThreadExecutionId}",
                    Fields(
                        ("disposition", writer => writer.WriteString("disposition", submission.Disposition)),
                        ("sessionId", writer => writer.WriteString("sessionId", sessionId)),
                        ("startedAt", writer => WriteOptional(writer, "startedAt",
                            submission.StartedAt is { } startedAt ? Time(startedAt) : null)),
                        ("threadExecutionId", writer => WriteOptional(writer, "threadExecutionId",
                            submission.ThreadExecutionId)),
                        ("threadId", writer => writer.WriteString("threadId", threadId))));
            }
            case AgentStudioExperimentContracts.SubmitInput:
            {
                if (!TryInputText(preview.Input, "sessionId", out string? sessionId) ||
                    !TryInputText(preview.Input, "threadId", out string? threadId) ||
                    !TryInputText(preview.Input, "text", out string? text) ||
                    scopedServices.GetService<IAgentStreamingService>() is not { } streaming)
                    return CommandResult.Rejected;
                AgentServiceResult<InputSubmissionDto> result = await streaming.SubmitInputAsync(
                    target.AgentId, sessionId!, threadId!,
                    new UserMessagesInputEvent
                    {
                        Messages = [new ChatMessage(ChatRole.User, text!)],
                        ClientInputId = preview.ClientInputId,
                    },
                    cancellationToken).ConfigureAwait(false);
                if (result.Status != AgentServiceStatus.Success || result.Value is null)
                    return CommandResult.Rejected;
                InputSubmissionDto submission = result.Value;
                return CommandResult.Success(
                    $"input.submit\n{sessionId}\n{threadId}\n{submission.Disposition}\n{submission.ThreadExecutionId}",
                    Fields(
                        ("disposition", writer => writer.WriteString("disposition", submission.Disposition)),
                        ("sessionId", writer => writer.WriteString("sessionId", sessionId)),
                        ("startedAt", writer => WriteOptional(writer, "startedAt",
                            submission.StartedAt is { } startedAt ? Time(startedAt) : null)),
                        ("threadExecutionId", writer => WriteOptional(writer, "threadExecutionId",
                            submission.ThreadExecutionId)),
                        ("threadId", writer => writer.WriteString("threadId", threadId))));
            }
            case AgentStudioExperimentContracts.CancelExecution:
            {
                if (!TryInputText(preview.Input, "sessionId", out string? sessionId) ||
                    !TryInputText(preview.Input, "threadId", out string? threadId) ||
                    !TryInputText(preview.Input, "threadExecutionId", out string? executionId) ||
                    scopedServices.GetService<IAgentThreadExecutionService>() is not { } executions)
                    return CommandResult.Rejected;
                AgentServiceResult<ThreadExecutionCancellationDto> result =
                    await executions.CancelExecutionAsync(
                        target.AgentId, sessionId!, threadId!, executionId!, cancellationToken)
                        .ConfigureAwait(false);
                if (result.Status != AgentServiceStatus.Success || result.Value is null)
                    return CommandResult.Rejected;
                ThreadExecutionCancellationDto cancellation = result.Value;
                return CommandResult.Success(
                    $"execution.cancel\n{sessionId}\n{threadId}\n{cancellation.ThreadExecutionId}\n{cancellation.Status}\n{cancellation.CancellationApplied}",
                    Fields(
                        ("cancellationApplied", writer =>
                            writer.WriteBoolean("cancellationApplied", cancellation.CancellationApplied)),
                        ("queuePromotion", writer =>
                            writer.WriteString("queuePromotion", cancellation.QueuePromotion)),
                        ("sessionId", writer => writer.WriteString("sessionId", sessionId)),
                        ("status", writer => writer.WriteString("status", cancellation.Status)),
                        ("threadExecutionId", writer =>
                            writer.WriteString("threadExecutionId", cancellation.ThreadExecutionId)),
                        ("threadId", writer => writer.WriteString("threadId", threadId))));
            }
            default:
                return CommandResult.Rejected;
        }
    }

    private async ValueTask<bool> AuthorizedAsync(
        StudioProducerInvocation invocation, string capability, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        AuthorizationResult result = await scope.ServiceProvider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(invocation.Bootstrap.HttpContext.User, null, capability).ConfigureAwait(false);
        return result.Succeeded;
    }

    private static bool TargetMatches(CommandTarget target, StudioProducerInvocation invocation) =>
        StringComparer.Ordinal.Equals(target.ApplicationId, invocation.Bootstrap.ApplicationGraph.ApplicationId) &&
        target.AgentId.Length > 0 && target.AuthorityChecksum.Length == 64;

    private bool TryReadPreview(StudioCanonicalJson request, out CommandTarget? target, out JsonElement input)
    {
        target = null;
        input = default;
        try
        {
            using JsonDocument document = JsonDocument.Parse(request.ToArray());
            JsonElement root = document.RootElement;
            if (!Exact(root, "commandId", "input", "pageId", "responseAuthorityChecksum", "target") ||
                !StringComparer.Ordinal.Equals(root.GetProperty("commandId").GetString(), commandId) ||
                !StringComparer.Ordinal.Equals(root.GetProperty("pageId").GetString(), PageId) ||
                !TryTarget(root.GetProperty("target"), out target))
                return false;
            input = root.GetProperty("input").Clone();
            return target is not null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryReadExecute(StudioCanonicalJson request, out CommandTarget? target,
        out string? requestIdentity, out string? previewChecksum)
    {
        target = null;
        requestIdentity = null;
        previewChecksum = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(request.ToArray());
            JsonElement root = document.RootElement;
            if (!Exact(root, "acknowledgements", "commandId", "freshAuthentication", "pageId",
                    "preview", "requestIdentity", "responseAuthorityChecksum", "target") ||
                root.GetProperty("acknowledgements").GetArrayLength() != 0 ||
                root.GetProperty("freshAuthentication").ValueKind != JsonValueKind.Null ||
                !TryTarget(root.GetProperty("target"), out target))
                return false;
            requestIdentity = root.GetProperty("requestIdentity").GetString();
            previewChecksum = root.GetProperty("preview").GetProperty("previewChecksum").GetString();
            return target is not null &&
                requestIdentity is { Length: >= 1 and <= 128 } &&
                previewChecksum is { Length: 64 };
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryTarget(JsonElement element, out CommandTarget? target)
    {
        target = null;
        try
        {
            if (!StringComparer.Ordinal.Equals(element.GetProperty("kind").GetString(), "agent")) return false;
            target = new CommandTarget(
                element.GetProperty("applicationId").GetString() ?? "",
                element.GetProperty("agentId").GetString() ?? "",
                element.GetProperty("authorityChecksum").GetString() ?? "");
            return target.ApplicationId.Length > 0 && target.AgentId.Length > 0 &&
                target.AuthorityChecksum.Length == 64;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryInputText(JsonElement input, string name, out string? value)
    {
        value = input.TryGetProperty(name, out JsonElement member) && member.ValueKind == JsonValueKind.String
            ? member.GetString()
            : null;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryOptionalInputText(JsonElement input, string name, out string? value)
    {
        if (!input.TryGetProperty(name, out JsonElement member) || member.ValueKind == JsonValueKind.Null)
        {
            value = null;
            return true;
        }
        value = member.ValueKind == JsonValueKind.String ? member.GetString() : null;
        return member.ValueKind == JsonValueKind.String;
    }

    private static bool TryInputBool(JsonElement input, string name, out bool value)
    {
        value = default;
        if (!input.TryGetProperty(name, out JsonElement member) ||
            member.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return false;
        value = member.GetBoolean();
        return true;
    }

    private static bool TryInputInt(JsonElement input, string name, out int value)
    {
        value = default;
        return input.TryGetProperty(name, out JsonElement member) &&
            member.ValueKind == JsonValueKind.Number && member.TryGetInt32(out value);
    }

    private static bool Exact(JsonElement value, params string[] names) =>
        value.ValueKind == JsonValueKind.Object &&
        value.EnumerateObject().Select(static item => item.Name).Order(StringComparer.Ordinal)
            .SequenceEqual(names.Order(StringComparer.Ordinal), StringComparer.Ordinal);

    private StudioCanonicalJson Result(string mode, string previewChecksum,
        DateTimeOffset? expiresAt, string? receiptChecksum, CommandResult? result)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            var fields = new SortedDictionary<string, Action<Utf8JsonWriter>>(StringComparer.Ordinal)
            {
                ["acknowledgements"] = output =>
                {
                    output.WriteStartArray("acknowledgements");
                    output.WriteEndArray();
                },
                ["expiresAtUtc"] = output => WriteOptional(output, "expiresAtUtc",
                    expiresAt is { } expiry ? Time(expiry) : null),
                ["mode"] = output => output.WriteString("mode", mode),
                ["previewChecksum"] = output => output.WriteString("previewChecksum", previewChecksum),
                ["receiptChecksum"] = output => WriteOptional(output, "receiptChecksum", receiptChecksum),
            };
            if (result is null) WritePreviewFields(fields);
            else foreach ((string name, Action<Utf8JsonWriter> write) in result.Fields)
                fields.Add(name, write);
            writer.WriteStartObject();
            foreach (Action<Utf8JsonWriter> write in fields.Values) write(writer);
            writer.WriteEndObject();
        }
        return StudioCanonicalJson.Create(buffer.WrittenSpan, 1_048_576);
    }

    private void WritePreviewFields(IDictionary<string, Action<Utf8JsonWriter>> target)
    {
        string[] fieldNames = commandId switch
        {
            AgentStudioExperimentContracts.CreateSession => ["sessionId", "threadId"],
            AgentStudioExperimentContracts.CreateThread => ["name", "sessionId", "threadId"],
            AgentStudioExperimentContracts.ForkThread =>
                ["name", "sessionId", "sourceThreadId", "threadId"],
            AgentStudioExperimentContracts.PermissionRespond or
                AgentStudioExperimentContracts.QuestionRespond or
                AgentStudioExperimentContracts.ContinuationRespond or
                AgentStudioExperimentContracts.ClientToolRespond =>
                ["message", "requestId", "status"],
            AgentStudioExperimentContracts.SteerInput =>
                ["disposition", "sessionId", "startedAt", "threadExecutionId", "threadId"],
            AgentStudioExperimentContracts.SubmitInput =>
                ["disposition", "sessionId", "startedAt", "threadExecutionId", "threadId"],
            AgentStudioExperimentContracts.CancelExecution =>
                ["cancellationApplied", "queuePromotion", "sessionId", "status", "threadExecutionId", "threadId"],
            _ => [],
        };
        foreach (string name in fieldNames)
            target.Add(name, writer => writer.WriteNull(name));
    }

    private static IReadOnlyDictionary<string, Action<Utf8JsonWriter>> Fields(
        params (string Name, Action<Utf8JsonWriter> Write)[] values) =>
        values.ToDictionary(value => value.Name, value => value.Write, StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, Action<Utf8JsonWriter>> ResponseFields(AgentRespondResult result) =>
        Fields(
            ("message", writer => WriteOptional(writer, "message", result.Message)),
            ("requestId", writer => writer.WriteString("requestId", result.RequestId)),
            ("status", writer => writer.WriteString("status", result.Status.ToString())));

    private static bool TryParseQuestionAnswers(string json, out QuestionAnswer[]? answers)
    {
        answers = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() > 3) return false;
            var parsed = new List<QuestionAnswer>();
            foreach (JsonElement item in root.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("questionId", out JsonElement questionId) ||
                    questionId.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(questionId.GetString()) ||
                    !item.TryGetProperty("selectedOptionIds", out JsonElement selected) ||
                    selected.ValueKind != JsonValueKind.Array)
                    return false;
                string[] selectedIds = selected.EnumerateArray().Select(value =>
                    value.ValueKind == JsonValueKind.String ? value.GetString() : null).ToArray()!;
                if (selectedIds.Any(static value => value is null)) return false;
                string? customText = item.TryGetProperty("customText", out JsonElement custom) &&
                    custom.ValueKind == JsonValueKind.String ? custom.GetString() : null;
                string? notes = item.TryGetProperty("notes", out JsonElement note) &&
                    note.ValueKind == JsonValueKind.String ? note.GetString() : null;
                parsed.Add(new QuestionAnswer(questionId.GetString()!, selectedIds!, customText, notes));
            }
            answers = parsed.ToArray();
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private static void WriteOptional(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null) writer.WriteNull(name);
        else writer.WriteString(name, value);
    }

    private static string Time(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    private static string Hex(StudioSha256 value) =>
        Convert.ToHexString(value.ToArray()).ToLowerInvariant();

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record CommandTarget(string ApplicationId, string AgentId, string AuthorityChecksum);

    private sealed record PreviewEvidence(string ApplicationId, string AgentId, string TargetAuthorityChecksum,
        string ResponseAuthorityChecksum, JsonElement Input, string ClientInputId, DateTimeOffset ExpiresAtUtc);

    private sealed record CommandResult(bool Accepted, string ReceiptMaterial,
        IReadOnlyDictionary<string, Action<Utf8JsonWriter>> Fields)
    {
        internal static CommandResult Rejected { get; } =
            new(false, "", new Dictionary<string, Action<Utf8JsonWriter>>());

        internal static CommandResult Success(string receiptMaterial,
            IReadOnlyDictionary<string, Action<Utf8JsonWriter>> fields) =>
            new(true, receiptMaterial, fields);
    }
}
