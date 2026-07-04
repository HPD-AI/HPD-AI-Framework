using System.Text.Json;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

public enum AgentMessageSource
{
    Unspecified = 0,
    UserInput,
    AssistantOutput,
    SystemInstruction,
    RuntimeContext,
    BackgroundNotification,
    ToolResult,
    PermissionResponse,
    Steering,
    Internal
}

public enum AgentMessageVisibility
{
    Transcript = 0,
    Hidden,
    Diagnostic
}

public enum AgentMessagePersistence
{
    ThreadHistory = 0,
    ModelContextOnly,
    None
}

public static class AgentMessagePolicy
{
    public const string SourcePropertyName = "hpd.message.source";
    public const string VisibilityPropertyName = "hpd.message.visibility";
    public const string PersistencePropertyName = "hpd.message.persistence";

    public static ChatMessage WithPolicy(
        this ChatMessage message,
        AgentMessageSource source,
        AgentMessageVisibility visibility,
        AgentMessagePersistence persistence)
    {
        ArgumentNullException.ThrowIfNull(message);

        message.AdditionalProperties ??= [];
        message.AdditionalProperties[SourcePropertyName] = source.ToString();
        message.AdditionalProperties[VisibilityPropertyName] = visibility.ToString();
        message.AdditionalProperties[PersistencePropertyName] = persistence.ToString();
        return message;
    }

    public static AgentMessageSource GetSource(this ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (TryGetEnum(message.AdditionalProperties, SourcePropertyName, out AgentMessageSource source))
            return source;

        return message.Role == ChatRole.User ? AgentMessageSource.UserInput :
            message.Role == ChatRole.Assistant ? AgentMessageSource.AssistantOutput :
            message.Role == ChatRole.System ? AgentMessageSource.SystemInstruction :
            message.Role == ChatRole.Tool ? AgentMessageSource.ToolResult :
            AgentMessageSource.Unspecified;
    }

    public static AgentMessageVisibility GetVisibility(this ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (TryGetEnum(message.AdditionalProperties, VisibilityPropertyName, out AgentMessageVisibility visibility))
            return visibility;

        return message.Role == ChatRole.System || message.Role == ChatRole.Tool
            ? AgentMessageVisibility.Hidden
            : AgentMessageVisibility.Transcript;
    }

    public static AgentMessagePersistence GetPersistence(this ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (TryGetEnum(message.AdditionalProperties, PersistencePropertyName, out AgentMessagePersistence persistence))
            return persistence;

        return message.Role == ChatRole.User ||
               message.Role == ChatRole.Assistant ||
               message.Role == ChatRole.System ||
               message.Role == ChatRole.Tool
            ? AgentMessagePersistence.ThreadHistory
            : AgentMessagePersistence.None;
    }

    internal static void StampDefaults(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        message.WithPolicy(
            message.GetSource(),
            message.GetVisibility(),
            message.GetPersistence());
    }

    private static bool TryGetEnum<TEnum>(
        AdditionalPropertiesDictionary? properties,
        string key,
        out TEnum value)
        where TEnum : struct, Enum
    {
        value = default;

        if (properties?.TryGetValue(key, out var raw) != true || raw is null)
            return false;

        if (raw is TEnum enumValue)
        {
            value = enumValue;
            return true;
        }

        if (raw is string text && Enum.TryParse(text, ignoreCase: true, out value))
            return true;

        if (raw is JsonElement json)
        {
            if (json.ValueKind == JsonValueKind.String &&
                Enum.TryParse(json.GetString(), ignoreCase: true, out value))
            {
                return true;
            }

            if (json.ValueKind == JsonValueKind.Number &&
                json.TryGetInt32(out var intValue) &&
                Enum.IsDefined(typeof(TEnum), intValue))
            {
                value = (TEnum)Enum.ToObject(typeof(TEnum), intValue);
                return true;
            }
        }

        return false;
    }
}
