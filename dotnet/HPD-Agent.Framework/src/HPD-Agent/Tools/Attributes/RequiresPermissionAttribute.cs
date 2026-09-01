using System;

/// <summary>
/// Marks a capability (AIFunction, Skill, or SubAgent) with permission metadata.
/// Permission middleware decides how this metadata is enforced.
///
/// Note: SubAgents always require permission by default (this attribute is implicit).
/// For AIFunctions and Skills, this attribute must be explicitly added.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequiresPermissionAttribute : Attribute
{
    /// <summary>Gets or sets the application-owned stable permission authority identifier.</summary>
    public string? PermissionAuthority { get; set; }

    /// <summary>Gets or sets the permission policy implementation for this function.</summary>
    public Type? PermissionPolicy { get; set; }

    /// <summary>Gets or sets the permission interaction implementation for this function.</summary>
    public Type? PermissionInteraction { get; set; }
}
