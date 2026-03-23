namespace Rhodium.Primitives;

/// <summary>
/// Contingency behavior for order lists.
/// </summary>
public enum ContingencyType : byte
{
    /// <summary>
    /// One-Triggers-Other: When first order fills, submit the others.
    /// Use case: Entry order triggers stop-loss and take-profit.
    /// </summary>
    OTO = 1,

    /// <summary>
    /// One-Cancels-Other: When any order fills, cancel the others.
    /// Use case: Stop-loss OR take-profit, whichever hits first.
    /// </summary>
    OCO = 2,

    /// <summary>
    /// One-Updates-Other: When first order partially fills, update quantities of others.
    /// Use case: Scale stop-loss with partial fills.
    /// </summary>
    OUO = 3
}
