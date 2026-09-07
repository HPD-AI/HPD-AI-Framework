namespace HPD.TUI.Core;

/// <summary>Represents a policy-validated logical terminal hyperlink.</summary>
public sealed record TerminalHyperlink
{
    internal TerminalHyperlink(string destination)
    {
        Destination = destination;
    }

    /// <summary>Gets the validated destination.</summary>
    public string Destination { get; }
}

/// <summary>Creates terminal hyperlinks after applying the framework link policy.</summary>
public static class TerminalHyperlinkPolicy
{
    private const int MaximumDestinationLength = 8_192;

    /// <summary>Attempts to validate and create a logical hyperlink.</summary>
    public static bool TryCreate(string? destination, out TerminalHyperlink? hyperlink)
    {
        hyperlink = null;
        if (string.IsNullOrWhiteSpace(destination) || destination.Length > MaximumDestinationLength)
        {
            return false;
        }

        foreach (var character in destination)
        {
            if (TerminalTextSafety.IsUnsafe(character))
            {
                return false;
            }
        }

        if (!Uri.TryCreate(destination, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https" or "mailto" or "file"))
        {
            return false;
        }

        hyperlink = new TerminalHyperlink(destination);
        return true;
    }
}

/// <summary>Defines code units that must never reach terminal presentation unchanged.</summary>
public static class TerminalTextSafety
{
    /// <summary>Gets whether a code unit is a terminal control or bidi-direction override.</summary>
    public static bool IsUnsafe(char value) =>
        char.IsControl(value) || value is '\u001b' or '\u009b' or '\u061c' or '\u200e' or '\u200f' or
        >= '\u202a' and <= '\u202e' or >= '\u2066' and <= '\u2069';
}

/// <summary>Identifies a hyperlink within one terminal grid lifetime.</summary>
public readonly record struct TerminalHyperlinkId(int Value)
{
    /// <summary>Gets a value representing no hyperlink.</summary>
    public static TerminalHyperlinkId None => default;

    /// <summary>Gets whether the identifier refers to a hyperlink.</summary>
    public bool IsNone => Value == 0;
}

/// <summary>Supplies structural metadata for one terminal text run.</summary>
public readonly record struct TerminalRunMetadata(TerminalHyperlink? Hyperlink = null);
