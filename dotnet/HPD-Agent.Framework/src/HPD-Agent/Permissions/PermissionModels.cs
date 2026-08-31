using System.Collections.Generic;

/// <summary>
/// Describes how the runtime should proceed when a permission response denies execution.
/// </summary>
public enum PermissionDeniedBehavior
{
    /// <summary>Stop the active turn after returning the denied tool result.</summary>
    InterruptTurn = 0,

    /// <summary>Return the denied tool result to the model and let the turn continue.</summary>
    ReturnToModel = 1
}
