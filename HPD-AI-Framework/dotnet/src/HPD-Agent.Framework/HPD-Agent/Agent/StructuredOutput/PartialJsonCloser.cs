// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text;
using System.Text.Json;

namespace HPD.Agent.StructuredOutput;

/// <summary>
/// Closes incomplete JSON strings by adding missing brackets and quotes.
/// Uses bracket stack for correct LIFO ordering.
/// </summary>
/// <remarks>
/// <para>
/// This utility is used during streaming structured output to parse partial JSON
/// as it arrives token-by-token. It attempts to "close" incomplete JSON by adding
/// the necessary closing brackets, quotes, etc.
/// </para>
/// <para>
/// <b>Limitations</b> (these cases fail parsing and are silently skipped for partials):
/// </para>
/// <list type="bullet">
/// <item>Does not handle incomplete numbers (e.g., {"value": 123)</item>
/// <item>Does not handle incomplete booleans (e.g., {"flag": fal)</item>
/// <item>Trailing commas are not cleaned (e.g., {"items": [1, 2,)</item>
/// </list>
/// <para>
/// </para>
/// </remarks>
public static class PartialJsonCloser
{
    /// <summary>
    /// Attempts to close incomplete JSON and return a parseable string.
    /// </summary>
    /// <param name="json">Potentially incomplete JSON string</param>
    /// <param name="closeTrailingStrings">If true, close incomplete strings; if false, backtrack</param>
    /// <returns>Closed JSON string, or null if unparseable</returns>
    public static string? TryClose(string json, bool closeTrailingStrings = true)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        var result = new StringBuilder(json.Length + 16);
        var closingStack = new Stack<char>(8);

        bool inString = false;
        bool escaped = false;

        foreach (char c in json)
        {
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    result.Append(c);
                }
                else if (c == '\\')
                {
                    escaped = true;
                    result.Append(c);
                }
                else if (c == '"')
                {
                    inString = false;
                    result.Append(c);
                }
                else if (c == '\n')
                {
                    // Escape unescaped newlines in strings (common LLM issue)
                    result.Append("\\n");
                }
                else
                {
                    result.Append(c);
                }
            }
            else
            {
                result.Append(c);
                switch (c)
                {
                    case '"':
                        inString = true;
                        break;
                    case '{':
                        closingStack.Push('}');
                        break;
                    case '[':
                        closingStack.Push(']');
                        break;
                    case '}' or ']':
                        if (closingStack.Count > 0 && closingStack.Peek() == c)
                            closingStack.Pop();
                        break;
                }
            }
        }

        // Handle incomplete string
        if (inString)
        {
            if (closeTrailingStrings)
            {
                // Remove trailing escape if incomplete, then close string
                if (escaped && result.Length > 0)
                    result.Length--;
                result.Append('"');
            }
            else
            {
                // Backtrack to last valid state
                return TryBacktrackToValid(result, closingStack);
            }
        }

        // Append closing brackets in LIFO order
        while (closingStack.Count > 0)
            result.Append(closingStack.Pop());

        return result.ToString();
    }

    private static string? TryBacktrackToValid(StringBuilder partial, Stack<char> stack)
    {
        var closers = string.Concat(stack);

        while (partial.Length > 0)
        {
            var attempt = partial.ToString() + closers;
            if (IsValidJson(attempt))
                return attempt;
            partial.Length--;
        }

        return null;
    }

    private static bool IsValidJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
