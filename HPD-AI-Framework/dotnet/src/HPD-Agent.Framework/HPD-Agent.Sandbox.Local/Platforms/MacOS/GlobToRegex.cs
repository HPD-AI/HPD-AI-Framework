using System.Text;

namespace HPD.Sandbox.Local.Platforms.MacOS;

/// <summary>
/// Converts glob patterns to regular expressions for macOS sandbox profiles.
/// </summary>
/// <remarks>
/// <para><b>Supported Patterns:</b></para>
/// <list type="bullet">
/// <item><c>*</c> - matches any characters except /</item>
/// <item><c>**</c> - matches any characters including /</item>
/// <item><c>?</c> - matches any single character except /</item>
/// <item><c>[abc]</c> - matches any character in the set</item>
/// </list>
///
/// <para><b>Usage in Sandbox Profiles:</b></para>
/// <code>
/// (deny file-write* (regex "^/path/.*\\.config$"))
/// </code>
/// </remarks>
public static partial class GlobToRegex
{
    /// <summary>
    /// Converts a glob pattern to a regex pattern for use in macOS sandbox profiles.
    /// </summary>
    /// <param name="globPattern">The glob pattern to convert</param>
    /// <returns>A regex pattern string (without delimiters)</returns>
    public static string Convert(string globPattern)
    {
        if (string.IsNullOrEmpty(globPattern))
            return "^$";

        var sb = new StringBuilder();
        sb.Append('^');

        var i = 0;
        while (i < globPattern.Length)
        {
            var c = globPattern[i];

            switch (c)
            {
                // Escape regex special characters (except glob chars)
                case '.':
                case '^':
                case '$':
                case '+':
                case '{':
                case '}':
                case '(':
                case ')':
                case '|':
                case '\\':
                    sb.Append('\\');
                    sb.Append(c);
                    break;

                // ** matches anything including /
                case '*' when i + 1 < globPattern.Length && globPattern[i + 1] == '*':
                    // Check for **/ pattern
                    if (i + 2 < globPattern.Length && globPattern[i + 2] == '/')
                    {
                        // **/ matches zero or more directories
                        sb.Append("(.*/)?");
                        i += 2; // Skip the extra * and /
                    }
                    else
                    {
                        // ** matches anything
                        sb.Append(".*");
                        i++; // Skip the extra *
                    }
                    break;

                // * matches anything except /
                case '*':
                    sb.Append("[^/]*");
                    break;

                // ? matches any single character except /
                case '?':
                    sb.Append("[^/]");
                    break;

                // Character class - pass through mostly as-is
                case '[':
                    var classEnd = FindMatchingBracket(globPattern, i);
                    if (classEnd == -1)
                    {
                        // Unclosed bracket - escape it
                        sb.Append("\\[");
                    }
                    else
                    {
                        // Copy the character class
                        var classContent = globPattern.Substring(i, classEnd - i + 1);

                        // Handle negation: [!...] becomes [^...]
                        if (classContent.StartsWith("[!"))
                        {
                            sb.Append("[^");
                            sb.Append(classContent.AsSpan(2));
                        }
                        else
                        {
                            sb.Append(classContent);
                        }
                        i = classEnd;
                    }
                    break;

                default:
                    sb.Append(c);
                    break;
            }

            i++;
        }

        sb.Append('$');
        return sb.ToString();
    }

    /// <summary>
    /// Converts a glob pattern and escapes it for use in a sandbox profile string.
    /// </summary>
    /// <param name="globPattern">The glob pattern</param>
    /// <returns>Escaped regex pattern for sandbox profile</returns>
    public static string ConvertAndEscape(string globPattern)
    {
        var regex = Convert(globPattern);
        // Double-escape backslashes for the sandbox profile string
        return regex.Replace("\\", "\\\\");
    }

    /// <summary>
    /// Checks if a string contains glob characters.
    /// </summary>
    public static bool ContainsGlobChars(string path)
    {
        return path.Contains('*') || path.Contains('?') || path.Contains('[');
    }

    private static int FindMatchingBracket(string str, int openIndex)
    {
        if (openIndex >= str.Length || str[openIndex] != '[')
            return -1;

        for (var i = openIndex + 1; i < str.Length; i++)
        {
            if (str[i] == ']' && i > openIndex + 1)
                return i;
        }

        return -1; // No matching bracket
    }
}
