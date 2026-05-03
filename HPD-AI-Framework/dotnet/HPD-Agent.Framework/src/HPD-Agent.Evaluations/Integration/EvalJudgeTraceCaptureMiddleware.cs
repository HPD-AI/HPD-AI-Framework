// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Evaluations.Integration;

/// <summary>
/// Captures the sanitized model request that a judge agent is about to send to
/// its model. Register this after prompt/privacy middleware so evaluation traces
/// store the post-middleware prompt rather than the raw evaluator prompt.
/// </summary>
public sealed class EvalJudgeTraceCaptureMiddleware : IAgentMiddleware
{
    /// <inheritdoc />
    public IAsyncEnumerable<ChatResponseUpdate>? WrapModelCallStreamingAsync(
        ModelRequest request,
        Func<ModelRequest, IAsyncEnumerable<ChatResponseUpdate>> handler,
        CancellationToken cancellationToken)
    {
        if (EvalTraceContext.CurrentEvaluatorName is not null)
            EvalTraceContext.AddCapturedJudgePrompt(ClonePrompt(request.Messages));

        return CaptureAsync(request, handler, cancellationToken);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> CaptureAsync(
        ModelRequest request,
        Func<ModelRequest, IAsyncEnumerable<ChatResponseUpdate>> handler,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var update in handler(request).WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            yield return update;
        }
    }

    private static IReadOnlyList<ChatMessage> ClonePrompt(IEnumerable<ChatMessage> messages) =>
        messages.Select(message => new ChatMessage(message.Role, message.Text)
        {
            MessageId = message.MessageId,
            AuthorName = message.AuthorName,
        }).ToList();
}
