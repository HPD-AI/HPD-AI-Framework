using HPD.Agent.Serialization;
using System.Text.Json;

namespace HPD.Agent.AspNetCore.Tests.TestInfrastructure;

internal static class SseTestEventReader
{
    internal static AgentEvent DeserializeDeliveryEvent(string json) =>
        HPD.Agent.AspNetCore.Tests.TestEventApplication.Codec.DeserializeEvent(
            JsonDocument.Parse(json).RootElement.GetProperty("event").GetRawText());

    public static async Task<IReadOnlyList<AgentEvent>> ReadUntilAsync(
        HttpClient client,
        string sessionId,
        string threadId,
        Func<IReadOnlyList<AgentEvent>, bool> completed,
        TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        var events = new List<AgentEvent>();

        while (!completed(events) && !cancellation.IsCancellationRequested)
        {
            try
            {
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
                while (!completed(events))
                {
                    var line = await reader.ReadLineAsync(cancellation.Token);
                    if (line is null)
                        break;
                    if (line.StartsWith("data: ", StringComparison.Ordinal))
                        events.Add(DeserializeDeliveryEvent(line[6..]));
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                    break;
            }
            catch (IOException exception)
                when (exception.InnerException is OperationCanceledException &&
                      !cancellation.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(25, cancellation.Token);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        return events;
    }
}
