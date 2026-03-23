namespace HPD.Agent.Hosting.Data;

/// <summary>
/// Individual message in a stream request.
/// </summary>
/// <param name="Content">Message content</param>
/// <param name="Role">Message role (user, assistant, system)</param>
public record StreamMessage(
    string Content,
    string Role);
