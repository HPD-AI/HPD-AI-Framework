using System.Text;

namespace HPD.Agent.Bots.Teams;

/// <summary>
/// Platform key codec for Teams conversations. Raw Teams conversation IDs and
/// service URLs must go through <see cref="FormatRaw"/> because they contain
/// characters used by the generated thread ID delimiter format.
/// </summary>
[ThreadId("teams:{ConversationId}:{ServiceUrl}")]
public partial record TeamsThreadId(string ConversationId, string ServiceUrl)
{
    public static string FormatRaw(string conversationId, string serviceUrl)
        => Format(Base64UrlEncode(conversationId), Base64UrlEncode(serviceUrl));

    public string DecodedConversationId => Base64UrlDecode(ConversationId);

    public string DecodedServiceUrl => Base64UrlDecode(ServiceUrl);

    public string BaseConversationId
    {
        get
        {
            var decoded = DecodedConversationId;
            var messageIdIndex = decoded.IndexOf(";messageid=", StringComparison.OrdinalIgnoreCase);
            return messageIdIndex >= 0 ? decoded[..messageIdIndex] : decoded;
        }
    }

    private static string Base64UrlEncode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string Base64UrlDecode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }
}
