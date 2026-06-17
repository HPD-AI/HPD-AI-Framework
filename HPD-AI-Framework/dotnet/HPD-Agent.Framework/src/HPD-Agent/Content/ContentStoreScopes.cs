using System.Text;

namespace HPD.Agent;

public static class ContentStoreScopes
{
    public static string ForThread(string sessionId, string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        return $"thread:{Encode(sessionId)}:{Encode(threadId)}";
    }

    private static string Encode(string value)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        return encoded.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
