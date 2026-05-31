// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Threading.Channels;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.Realtime;

/// <summary>
/// Executes realtime function calls through HPD function middleware and sends results back to the session.
/// </summary>
internal sealed class RealtimeToolBridge : IDisposable
{
    private readonly IRealtimeClientSession _session;
    private readonly IRuntimeFunctionExecutor _functionExecutor;
    private readonly AgentRunConfig? _runConfig;
    private readonly Channel<IReadOnlyList<FunctionCallContent>> _queue =
        Channel.CreateUnbounded<IReadOnlyList<FunctionCallContent>>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    public RealtimeToolBridge(
        IRealtimeClientSession session,
        IRuntimeFunctionExecutor functionExecutor,
        AgentRunConfig? runConfig = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _functionExecutor = functionExecutor ?? throw new ArgumentNullException(nameof(functionExecutor));
        _runConfig = runConfig;
    }

    public bool TryEnqueue(RealtimeServerMessage message)
    {
        var calls = ExtractFunctionCalls(message);
        if (calls.Count == 0)
            return false;

        return _queue.Writer.TryWrite(calls);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await foreach (var calls in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            var results = await _functionExecutor.ExecuteFunctionCallsAsync(
                calls,
                _runConfig,
                cancellationToken).ConfigureAwait(false);

            if (results.Count == 0)
                continue;

            foreach (var result in results)
            {
                await _session.SendAsync(
                    new CreateConversationItemRealtimeClientMessage(
                        new RealtimeConversationItem([result])),
                    cancellationToken).ConfigureAwait(false);
            }

            await _session.SendAsync(
                new CreateResponseRealtimeClientMessage(),
                cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose() => _queue.Writer.TryComplete();

    private static List<FunctionCallContent> ExtractFunctionCalls(RealtimeServerMessage message)
    {
        if (message is not ResponseOutputItemRealtimeServerMessage output ||
            output.Type != RealtimeServerMessageType.ResponseOutputItemDone ||
            output.Item?.Contents is not { Count: > 0 } contents)
        {
            return [];
        }

        var calls = new List<FunctionCallContent>();
        for (var i = 0; i < contents.Count; i++)
        {
            if (contents[i] is FunctionCallContent call)
                calls.Add(call);
        }

        return calls;
    }
}
