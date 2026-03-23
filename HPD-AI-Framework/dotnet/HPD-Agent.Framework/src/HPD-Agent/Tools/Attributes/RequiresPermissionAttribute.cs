using System;

/// <summary>
/// Marks a capability (AIFunction, Skill, or SubAgent) as requiring user permission before execution.
/// Capabilities with this attribute will trigger permission requests to the configured handler.
///
/// Note: SubAgents always require permission by default (this attribute is implicit).
/// For AIFunctions and Skills, this attribute must be explicitly added.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequiresPermissionAttribute : Attribute
{
    // This attribute acts as a simple boolean flag and requires no parameters.
}
