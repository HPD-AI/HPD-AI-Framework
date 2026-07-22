using System.Text;
using HPD.Agent.Serialization;

namespace HPD.Agent;

internal static class AgentEventContentPersistence
{
    public static async Task<ContentInfo?> PersistAsync(
        IContentStore? contentStore,
        AgentEvent evt,
        string? defaultScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var request = evt.GetContentPersistenceRequest();
        if (contentStore == null || request == null)
            return null;

        var kind = NormalizeKind(request.Kind);
        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["kind"] = kind,
            ["event.type"] = AgentEventSerializer.GetEventTypeName(evt)
        };

        AddIfPresent(tags, "event.id", evt.EventId);
        AddIfPresent(tags, "session", evt.SessionId);
        AddIfPresent(tags, "thread", evt.ThreadId);
        AddIfPresent(tags, "trace", evt.TraceId);
        AddIfPresent(tags, "span", evt.SpanId);

        if (evt.Metadata != null)
        {
            AddIfPresent(tags, "agent.name", evt.Metadata.AgentName);
            AddIfPresent(tags, "agent.id", evt.Metadata.AgentId);
        }

        if (request.Tags != null)
        {
            foreach (var tag in request.Tags)
            {
                if (!string.IsNullOrWhiteSpace(tag.Key) && tag.Value != null)
                {
                    tags[tag.Key] = tag.Value;
                }
            }
        }

        var json = AgentEventSerializer.ToJson(evt);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        return await contentStore.WriteAsync(
            ContentScope.Create(request.Scope ?? defaultScope ?? ContentScope.Global.Value),
            stream,
            new ContentMetadata
            {
                Name = request.Name,
                ContentType = request.ContentType,
                Description = request.Description,
                Origin = request.Origin,
                Tags = tags
            },
            request.Options,
            cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeKind(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        return kind.Trim();
    }

    private static void AddIfPresent(Dictionary<string, string> tags, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            tags[key] = value;
        }
    }
}
