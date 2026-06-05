using System.Text;

namespace HPD.Agent;

public static class ContentStoreScopes
{
    public static string ForBranch(string sessionId, string branchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);

        return $"branch:{Encode(sessionId)}:{Encode(branchId)}";
    }

    private static string Encode(string value)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        return encoded.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
