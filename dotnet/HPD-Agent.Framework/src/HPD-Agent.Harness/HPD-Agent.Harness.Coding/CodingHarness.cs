using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using HPD.Agent;
using HPD.Agent.ToolHarness.Coding.Ripgrep;
using HPDOS.ToolHarnesses.Middleware;

/// <summary>
/// Shared partial class for coding toolharness functions.
/// </summary>
[Collapse(
    "Contains tools for coding operations: file operations, code search, shell execution, and code analysis.",
    SystemPrompt = CodingToolHarnessPrompts.SystemPrompt,
    Middlewares = [typeof(EnvironmentContextMiddleware), typeof(CodingLanguageServerMiddleware)])]
public partial class CodingToolHarness
{
    private static readonly HashSet<string> BuiltInRecursiveSkips = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".hg",
        ".svn",
        "node_modules",
        "bin",
        "obj",
        ".vs",
        ".vscode",
        ".idea",
        "dist",
        "build",
        "coverage",
        "target"
    };

    private readonly IReadOnlyList<IReadFileTextSource> _readFileTextSources;
    private readonly IReadOnlyList<IDirectoryListingSource> _directoryListingSources;
    private readonly IReadOnlyList<IGlobSearchPathResolver> _globSearchPathResolvers;
    private readonly GlobSearchOptions _globSearchOptions;
    private readonly IRipgrepRunner _ripgrepRunner;
    private readonly IFileMutationLockProvider _fileMutationLockProvider;
    private readonly IReadOnlyList<IFileMutationTextSink> _fileMutationTextSinks;
    private readonly IReadOnlyList<IFileMutationHistorySink> _fileMutationHistorySinks;
    private readonly ExecuteCommandOptions _executeCommandOptions;

    static CodingToolHarness()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Creates a coding toolharness with filesystem-backed sources.
    /// </summary>
    public CodingToolHarness()
        : this([])
    {
    }

    /// <summary>
    /// Creates a coding toolharness with optional host-provided text sources.
    /// </summary>
    public CodingToolHarness(IEnumerable<IReadFileTextSource>? readFileTextSources)
        : this(readFileTextSources, null)
    {
    }

    /// <summary>
    /// Creates a coding toolharness with optional host-provided sources.
    /// </summary>
    public CodingToolHarness(
        IEnumerable<IReadFileTextSource>? readFileTextSources,
        IEnumerable<IDirectoryListingSource>? directoryListingSources,
        IEnumerable<IGlobSearchPathResolver>? globSearchPathResolvers = null,
        GlobSearchOptions? globSearchOptions = null,
        IRipgrepRunner? ripgrepRunner = null,
        IFileMutationLockProvider? fileMutationLockProvider = null,
        IEnumerable<IFileMutationTextSink>? fileMutationTextSinks = null,
        IEnumerable<IFileMutationHistorySink>? fileMutationHistorySinks = null,
        ExecuteCommandOptions? executeCommandOptions = null)
    {
        _readFileTextSources = readFileTextSources?.ToArray() ?? [];
        _directoryListingSources = directoryListingSources?.ToArray() ?? [];
        _globSearchPathResolvers = globSearchPathResolvers?.ToArray() ?? [];
        _globSearchOptions = globSearchOptions ?? GlobSearchOptions.Default;
        _ripgrepRunner = ripgrepRunner ?? new RipgrepRunner();
        _fileMutationLockProvider = fileMutationLockProvider ?? NoOpFileMutationLockProvider.Instance;
        _fileMutationTextSinks = fileMutationTextSinks?.ToArray() ?? [];
        _fileMutationHistorySinks = fileMutationHistorySinks?.ToArray() ?? [];
        _executeCommandOptions = executeCommandOptions ?? new ExecuteCommandOptions();
    }

    private static IEnumerable<string> EnumerateFileSystemEntries(string fullPath, bool throwOnFailure)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(fullPath);
        }
        catch when (!throwOnFailure)
        {
            return [];
        }
    }

    private static StringComparer GetPathComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool IsHiddenPath(string relativePath)
        => relativePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(part => part.StartsWith(".", StringComparison.Ordinal));

    private static string NormalizePatternSeparators(string pattern)
        => pattern.Replace('\\', '/');

    private static string BuildMissingDirectoryMessage(string fullPath)
    {
        var suggestions = FindSimilarDirectories(fullPath);
        return suggestions.Count switch
        {
            0 => "Directory does not exist.",
            1 => $"Directory does not exist. Did you mean {suggestions[0]}?",
            _ => $"Directory does not exist. Did you mean one of these? {string.Join(", ", suggestions)}"
        };
    }

    private static IReadOnlyList<string> FindSimilarDirectories(string fullPath)
    {
        var parent = Path.GetDirectoryName(fullPath);
        var requestedName = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(requestedName) || !Directory.Exists(parent))
            return [];

        var normalizedRequestedName = NormalizeSuggestionName(requestedName);

        try
        {
            return Directory.EnumerateDirectories(parent)
                .Select(path => new
                {
                    FullPath = path,
                    Name = Path.GetFileName(path),
                    NormalizedName = NormalizeSuggestionName(Path.GetFileName(path))
                })
                .Where(item =>
                    string.Equals(item.NormalizedName, normalizedRequestedName, StringComparison.OrdinalIgnoreCase) ||
                    item.NormalizedName.Contains(normalizedRequestedName, StringComparison.OrdinalIgnoreCase) ||
                    normalizedRequestedName.Contains(item.NormalizedName, StringComparison.OrdinalIgnoreCase) ||
                    HasSmallEditDistance(item.NormalizedName, normalizedRequestedName))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .Select(item => item.FullPath)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static string NormalizeSuggestionName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }

    private static bool HasSmallEditDistance(string left, string right)
    {
        if (Math.Abs(left.Length - right.Length) > 1)
            return false;

        var i = 0;
        var j = 0;
        var edits = 0;

        while (i < left.Length && j < right.Length)
        {
            if (left[i] == right[j])
            {
                i++;
                j++;
                continue;
            }

            edits++;
            if (edits > 1)
                return false;

            if (left.Length > right.Length)
                i++;
            else if (right.Length > left.Length)
                j++;
            else
            {
                i++;
                j++;
            }
        }

        return edits + (left.Length - i) + (right.Length - j) <= 1;
    }

    private static HpdIgnoreMatcher CreateIgnoreMatcher(string rootPath)
    {
        var orderedRules = new List<HpdIgnoreRule>();
        var gitignorePath = Path.Combine(rootPath, ".gitignore");

        if (File.Exists(gitignorePath))
        {
            try
            {
                foreach (var rawRule in File.ReadLines(gitignorePath))
                {
                    var rule = rawRule.Trim();
                    if (string.IsNullOrWhiteSpace(rule) || rule.StartsWith("#", StringComparison.Ordinal))
                        continue;

                    var isNegation = rule.StartsWith("!", StringComparison.Ordinal);
                    var matchRule = isNegation ? rule[1..] : rule;
                    if (string.IsNullOrWhiteSpace(matchRule))
                        continue;

                    orderedRules.Add(new HpdIgnoreRule(matchRule, isNegation));
                }
            }
            catch
            {
                // Ignore parse/read failures; listing/search should still work.
            }
        }

        return new HpdIgnoreMatcher(orderedRules);
    }

    private static XmlWriter CreateCodingToolHarnessXmlWriter(StringBuilder builder)
    {
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = false,
            NewLineHandling = NewLineHandling.None
        };

        return XmlWriter.Create(builder, settings);
    }

    private static string FormatBool(bool value)
        => value.ToString().ToLowerInvariant();

    private static string FormatEnum<TEnum>(TEnum value)
        where TEnum : struct, Enum
        => string.Concat(value.ToString().Select((ch, index) =>
            index > 0 && char.IsUpper(ch) ? "_" + char.ToLowerInvariant(ch) : char.ToLowerInvariant(ch).ToString()));

    private sealed record HpdIgnoreMatcher(IReadOnlyList<HpdIgnoreRule> OrderedRules)
    {
        public bool IsIgnored(string relativePath, bool pathIsDirectory)
        {
            var ignored = false;
            foreach (var rule in OrderedRules)
            {
                if (rule.Matches(relativePath, pathIsDirectory))
                    ignored = !rule.IsNegation;
            }

            return ignored;
        }
    }

    private sealed record HpdIgnoreRule(string Pattern, bool IsNegation)
    {
        public bool Matches(string relativePath, bool pathIsDirectory)
        {
            var normalizedRelativePath = NormalizePatternSeparators(relativePath.TrimEnd('/'));
            var comparer = GetPathComparer();

            foreach (var pattern in ExpandGitIgnorePattern(Pattern, pathIsDirectory))
            {
                if (comparer.Equals(normalizedRelativePath, pattern.TrimEnd('/')))
                    return true;

                if (MatchesIgnorePattern(normalizedRelativePath, pattern))
                    return true;
            }

            return false;
        }

        private IEnumerable<string> ExpandGitIgnorePattern(string pattern, bool pathIsDirectory)
        {
            var normalized = NormalizePatternSeparators(pattern.Trim());
            var directoryOnly = normalized.EndsWith("/", StringComparison.Ordinal);
            normalized = normalized.Trim('/');
            if (string.IsNullOrEmpty(normalized))
                yield break;

            if (!normalized.Contains('/', StringComparison.Ordinal))
            {
                yield return normalized;
                yield return "**/" + normalized;
            }
            else
            {
                yield return normalized;
            }

            var shouldMatchSubtree = pathIsDirectory || (directoryOnly && !IsNegation);
            if (shouldMatchSubtree)
            {
                yield return normalized;
                yield return normalized + "/**";
                if (!normalized.Contains('/', StringComparison.Ordinal))
                    yield return "**/" + normalized + "/**";
            }
        }

        private static bool MatchesIgnorePattern(string path, string pattern)
        {
            var regexPattern = new StringBuilder("^");
            var normalizedPattern = NormalizePatternSeparators(pattern.Trim('/'));

            for (var i = 0; i < normalizedPattern.Length; i++)
            {
                var ch = normalizedPattern[i];
                if (ch == '*')
                {
                    if (i + 1 < normalizedPattern.Length && normalizedPattern[i + 1] == '*')
                    {
                        regexPattern.Append(".*");
                        i++;
                    }
                    else
                    {
                        regexPattern.Append("[^/]*");
                    }

                    continue;
                }

                regexPattern.Append(ch == '?' ? "[^/]" : Regex.Escape(ch.ToString()));
            }

            regexPattern.Append('$');
            var options = OperatingSystem.IsWindows() ? RegexOptions.IgnoreCase : RegexOptions.None;
            return Regex.IsMatch(path, regexPattern.ToString(), options, TimeSpan.FromMilliseconds(100));
        }
    }
}
