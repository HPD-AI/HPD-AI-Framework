using HPD.Agent.Serialization;

namespace HPD.Agent.AspNetCore.Tests.TestInfrastructure;

internal static class SseTestEventReader
{
    public static async Task<IReadOnlyList<AgentEvent>> ReadUntilAsync(
        HttpClient client,
        string sessionId,
        string threadId,
        Func<IReadOnlyList<AgentEvent>, bool> completed,
        TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/agents/test-agent/sessions/{sessionId}/threads/{threadId}/events?after=1:0");
        request.Headers.Accept.ParseAdd("text/event-stream");

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellation.Token);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellation.Token);
        using var reader = new StreamReader(stream);
        var events = new List<AgentEvent>();

        try
        {
            while (!completed(events))
            {
                var line = await reader.ReadLineAsync(cancellation.Token);
                if (line is null)
                    break;
                if (line.StartsWith("data: ", StringComparison.Ordinal))
                    events.Add(AgentEventSerializer.DeserializeEventJson(line[6..]));
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A short timeout is also used to inspect the finite replay currently available.
        }

        return events;
    }
}
