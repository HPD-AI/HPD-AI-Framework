using System.Text.Json;
using HPD.Agent;
using HPD.Agent.ClientTools;
using HPD.Agent.Hosting.Data;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Hosting.Extensions;

/// <summary>
/// Extension methods for converting between domain objects and DTOs.
/// </summary>
public static class DtoMappingExtensions
{
    /// <summary>
    /// Convert a Session to a SessionDto.
    /// </summary>
    public static SessionDto ToDto(this Session session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new SessionDto(
            session.Id,
            session.CreatedAt,
            session.LastActivity,
            session.Metadata.Count > 0 ? session.Metadata : null);
    }

    /// <summary>
    /// Convert a Branch to a BranchDto.
    /// </summary>
    public static BranchDto ToDto(this Branch branch, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(branch);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        return new BranchDto(
            branch.Id,
            sessionId,
            branch.GetDisplayName(),
            branch.Description,
            branch.ForkedFrom,
            branch.ForkedAtMessageIndex,
            branch.CreatedAt,
            branch.LastActivity,
            branch.MessageCount,
            branch.Tags,
            branch.Ancestors,
            branch.SiblingIndex,
            branch.TotalSiblings,
            branch.IsOriginal,
            branch.OriginalBranchId,
            branch.PreviousSiblingId,
            branch.NextSiblingId,
            branch.TotalForks);
    }

    /// <summary>
    /// Convert a ChatMessage to a MessageDto.
    /// </summary>
    public static MessageDto ToDto(this ChatMessage message, int index, DateTime timestamp)
    {
        ArgumentNullException.ThrowIfNull(message);

        var contents = message.Contents
            .Where(c => c is not UsageContent)
            .ToList();

        return new MessageDto(
            message.MessageId ?? $"msg-{index}",
            message.Role.Value,
            contents,
            message.AuthorName,
            (message.CreatedAt?.UtcDateTime ?? timestamp).ToString("O"));
    }

    /// <summary>
    /// Convert ContentInfo to AssetDto.
    /// </summary>
    public static AssetDto ToDto(this ContentInfo asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        return new AssetDto(
            asset.Id,
            asset.ContentType,
            asset.SizeBytes,
            asset.CreatedAt.ToString("O"));
    }

    /// <summary>
    /// Convert StreamMessage to ChatMessage.
    /// </summary>
    public static ChatMessage ToChatMessage(this StreamMessage streamMessage)
    {
        ArgumentNullException.ThrowIfNull(streamMessage);

        var role = streamMessage.Role?.ToLowerInvariant() switch
        {
            "user" => ChatRole.User,
            "assistant" => ChatRole.Assistant,
            "system" => ChatRole.System,
            _ => ChatRole.User
        };

        return new ChatMessage(role, streamMessage.Content);
    }

    /// <summary>
    /// Convert StreamRunConfigDto to AgentRunConfig.
    /// Only maps serializable properties.
    /// </summary>
    public static AgentRunConfig? ToAgentRunConfig(this StreamRunConfigDto? dto)
    {
        if (dto == null) return null;

        var config = new AgentRunConfig();

        if (dto.ProviderKey != null)
            config.ProviderKey = dto.ProviderKey;

        if (dto.ModelId != null)
            config.ModelId = dto.ModelId;

        if (dto.AdditionalSystemInstructions != null)
            config.AdditionalSystemInstructions = dto.AdditionalSystemInstructions;

        if (dto.ContextOverrides != null)
            config.ContextOverrides = dto.ContextOverrides;

        if (dto.PermissionOverrides != null)
            config.PermissionOverrides = dto.PermissionOverrides;

        if (dto.CoalesceDeltas.HasValue)
            config.CoalesceDeltas = dto.CoalesceDeltas.Value;

        if (dto.SkipTools.HasValue)
            config.SkipTools = dto.SkipTools.Value;

        if (dto.RunTimeout != null && TimeSpan.TryParse(dto.RunTimeout, out var timeout))
            config.RunTimeout = timeout;

        // Map chat options
        if (dto.Chat != null)
        {
            var chatOptions = new ChatRunConfig();

            if (dto.Chat.Temperature.HasValue)
                chatOptions.Temperature = dto.Chat.Temperature.Value;

            if (dto.Chat.MaxOutputTokens.HasValue)
                chatOptions.MaxOutputTokens = dto.Chat.MaxOutputTokens.Value;

            if (dto.Chat.TopP.HasValue)
                chatOptions.TopP = dto.Chat.TopP.Value;

            if (dto.Chat.FrequencyPenalty.HasValue)
                chatOptions.FrequencyPenalty = dto.Chat.FrequencyPenalty.Value;

            if (dto.Chat.PresencePenalty.HasValue)
                chatOptions.PresencePenalty = dto.Chat.PresencePenalty.Value;

            if (dto.Chat.AdditionalProperties is { Count: > 0 })
            {
                chatOptions.AdditionalProperties = new Dictionary<string, object>(dto.Chat.AdditionalProperties.Count);
                foreach (var (k, v) in dto.Chat.AdditionalProperties)
                {
                    chatOptions.AdditionalProperties[k] = v is System.Text.Json.JsonElement je
                        ? CoerceJsonElement(je)
                        : v;
                }
            }

            if (dto.Chat.Reasoning != null)
                chatOptions.Reasoning = dto.Chat.Reasoning;

            config.Chat = chatOptions;
        }

        return config;
    }

    /// <summary>
    /// Convert a StreamRequest's client tool fields into an AgentClientInput.
    /// Returns null if no client tool data is present.
    /// </summary>
    public static AgentClientInput? ToAgentClientInput(this StreamRequest request)
    {
        if (request.clientToolKits == null &&
            request.Context == null &&
            request.State == null &&
            request.ExpandedContainers == null &&
            request.HiddenTools == null &&
            !request.ResetClientState)
        {
            return null;
        }

        List<clientToolKitDefinition>? groups = null;
        if (request.clientToolKits is { Count: > 0 })
        {
            groups = new List<clientToolKitDefinition>(request.clientToolKits.Count);
            foreach (var element in request.clientToolKits)
            {
                var group = JsonSerializer.Deserialize(element, HPDJsonContext.Default.clientToolKitDefinition);
                if (group != null)
                    groups.Add(group);
            }
        }

        List<ContextItem>? context = null;
        if (request.Context is { Count: > 0 })
        {
            context = new List<ContextItem>(request.Context.Count);
            foreach (var element in request.Context)
            {
                var item = JsonSerializer.Deserialize(element, HPDJsonContext.Default.ContextItem);
                if (item != null)
                    context.Add(item);
            }
        }

        return new AgentClientInput
        {
            clientToolKits = groups,
            Context = context,
            State = request.State,
            ExpandedContainers = request.ExpandedContainers is { Count: > 0 }
                ? new HashSet<string>(request.ExpandedContainers)
                : null,
            HiddenTools = request.HiddenTools is { Count: > 0 }
                ? new HashSet<string>(request.HiddenTools)
                : null,
            ResetClientState = request.ResetClientState,
        };
    }

    private static object CoerceJsonElement(System.Text.Json.JsonElement el) => el.ValueKind switch
    {
        System.Text.Json.JsonValueKind.True    => (object)true,
        System.Text.Json.JsonValueKind.False   => false,
        System.Text.Json.JsonValueKind.Number  => el.TryGetInt64(out var i) ? i : (object)el.GetDouble(),
        System.Text.Json.JsonValueKind.String  => el.GetString() ?? string.Empty,
        System.Text.Json.JsonValueKind.Null    => null!,
        _                                      => el.GetRawText(),
    };
}
