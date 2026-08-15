namespace HPD.Payments.Runtime.History;

/// <summary>Defines the exact H0–H13 semantic coordinates of one adversarial runtime history.</summary>
public sealed record RuntimeHistoryTrace
{
    private readonly string[] _steps;
    private readonly string[] _terminalAnswers;

    /// <summary>Gets the stable scenario name.</summary>
    public string Name { get; }
    /// <summary>Gets defensive H0–H13 content in exact order.</summary>
    public IReadOnlyList<string> Steps => _steps.ToArray();
    /// <summary>Gets defensive answers to the eight mandatory terminal questions.</summary>
    public IReadOnlyList<string> TerminalAnswers => _terminalAnswers.ToArray();

    /// <summary>Creates one complete semantic history.</summary>
    /// <param name="name">Bounded scenario token.</param>
    /// <param name="steps">Exactly fourteen non-empty H0–H13 entries.</param>
    /// <param name="terminalAnswers">Exactly eight non-empty terminal answers.</param>
    public RuntimeHistoryTrace(string name, ReadOnlySpan<string> steps, ReadOnlySpan<string> terminalAnswers)
    {
        if (!Primitives.Identity.ScopeId.TryCreate("history", "scenario", name, out _) || steps.Length != 14 ||
            terminalAnswers.Length != 8 || steps.ToArray().Any(string.IsNullOrWhiteSpace) ||
            terminalAnswers.ToArray().Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Runtime history requires a bounded name, H0-H13, and all eight terminal answers.");
        Name = name;
        _steps = steps.ToArray();
        _terminalAnswers = terminalAnswers.ToArray();
    }
}
