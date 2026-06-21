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
    // This attribute acts as a simple boolean flag and requires no parameters.
}
