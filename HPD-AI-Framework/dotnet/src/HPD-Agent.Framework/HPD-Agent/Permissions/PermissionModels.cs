using System.Collections.Generic;

/// <summary>
/// Represents the application's decision about a function permission request.
/// Used by permission Middlewares.
/// </summary>
public class PermissionDecision
{
    public bool Approved { get; set; }

    /// <summary>
    /// Optional: Remember this choice for future invocations.
    /// If set, the permission will be stored according to the Collapse implied by
    /// the conversationId parameter passed to SavePermissionAsync.
    /// Pass conversationId to Collapse to the current conversation, or omit for global Collapse.
    /// </summary>
    public PermissionChoice? RememberAs { get; set; }
}

/// <summary>
/// User's preference for how to handle permission requests for a function.
/// </summary>
public enum PermissionChoice
{
    /// <summary>Ask for permission each time (safe default)</summary>
    Ask = 0,

    /// <summary>Always allow this function without asking</summary>
    AlwaysAllow = 1,

    /// <summary>Always deny this function without asking</summary>
    AlwaysDeny = 2
}