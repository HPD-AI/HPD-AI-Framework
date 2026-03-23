namespace HPD.Agent.Secrets;

/// <summary>
/// Exception thrown when a required secret cannot be resolved.
/// </summary>
public class SecretNotFoundException : Exception
{
    /// <summary>The secret key that was not found.</summary>
    public string Key { get; }

    /// <summary>The display name for the secret (for user-friendly error messages).</summary>
    public string DisplayName { get; }

    public SecretNotFoundException(string message, string key, string displayName) : base(message)
    {
        Key = key;
        DisplayName = displayName;
    }
}
