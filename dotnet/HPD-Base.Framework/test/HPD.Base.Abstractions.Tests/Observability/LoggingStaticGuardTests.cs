using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HPD.Base.Abstractions.Tests.Observability;

/// <summary>
/// Guards the source and dependency boundaries of the BASE structured logging contract.
/// </summary>
public sealed partial class LoggingStaticGuardTests
{
    private static readonly string[] ForbiddenTemplateAndParameterTokens =
    [
        "BucketId",
        "ChannelId",
        "ChannelName",
        "CollectionId",
        "ConnectionId",
        "CorrelationId",
        "CredentialId",
        "GrantId",
        "ModuleId",
        "ObjectId",
        "ObjectKey",
        "ProjectId",
        "RecordId",
        "RequestId",
        "SessionId",
        "SubjectId",
        "TenantId",
        "UserId"
    ];

    private static readonly string[] ForbiddenParameterTypeTokens =
    [
        "Context",
        "Dto",
        "Exception",
        "Event",
        "Principal",
        "Query",
        "Request",
        "Result"
    ];

    /// <summary>
    /// Ensures production projects reference only the logging abstraction package.
    /// </summary>
    [Fact]
    public void ProductionProjectsUseOnlyApprovedLoggingDependencies()
    {
        var root = FindSolutionRoot();
        var unexpected = ProductionProjects(root)
            .SelectMany(project => PackageReferences(project)
                .Select(package => new { Project = project, Package = package }))
            .Where(item => IsForbiddenLoggingPackage(item.Package))
            .Select(item => $"{Path.GetRelativePath(root, item.Project)} -> {item.Package}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(unexpected);
    }

    /// <summary>
    /// Ensures reference in-memory providers do not retain hypothetical logging dependencies.
    /// </summary>
    [Theory]
    [InlineData("HPD.Base.InMemory")]
    [InlineData("HPD.Base.Files.InMemory")]
    public void InMemoryProjectsDoNotReferenceLoggingAbstractions(string projectName)
    {
        var root = FindSolutionRoot();
        var project = Path.Combine(root, "src", projectName, $"{projectName}.csproj");

        Assert.DoesNotContain(
            "Microsoft.Extensions.Logging.Abstractions",
            PackageReferences(project),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures an emitting non-ASP.NET project declares logging abstractions directly.
    /// </summary>
    [Fact]
    public void EmittingNonAspNetCoreProjectsReferenceLoggingAbstractionsDirectly()
    {
        var root = FindSolutionRoot();
        var unexpected = ProductionProjects(root)
            .Where(project => !HasFrameworkReference(project, "Microsoft.AspNetCore.App"))
            .Where(project => ProjectSources(project).Any(source =>
                LoggerMessageAttributeRegex().IsMatch(File.ReadAllText(source))))
            .Where(project => !PackageReferences(project).Contains(
                "Microsoft.Extensions.Logging.Abstractions",
                StringComparer.OrdinalIgnoreCase))
            .Select(project => Path.GetRelativePath(root, project))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(unexpected);
    }

    /// <summary>
    /// Ensures production logging uses source-generated methods without scopes or disabled checks.
    /// </summary>
    [Fact]
    public void ProductionLoggingUsesOnlyApprovedGeneratedSurface()
    {
        var root = FindSolutionRoot();
        var violations = new List<string>();

        foreach (var source in ProductionSources(root))
        {
            var text = File.ReadAllText(source);
            if (RawLoggerCallRegex().IsMatch(text))
                violations.Add($"{Path.GetRelativePath(root, source)}: raw ILogger.Log call");
            if (text.Contains("LoggerExtensions.", StringComparison.Ordinal)
                || text.Contains("LoggerMessage.Define", StringComparison.Ordinal))
                violations.Add($"{Path.GetRelativePath(root, source)}: non-generated logging helper");
            if (text.Contains("BeginScope", StringComparison.Ordinal))
                violations.Add($"{Path.GetRelativePath(root, source)}: BeginScope");
            if (SkipEnabledCheckRegex().IsMatch(text))
                violations.Add($"{Path.GetRelativePath(root, source)}: SkipEnabledCheck=true");
        }

        Assert.Empty(violations.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Ensures generated message templates are constant and contain no forbidden property tokens.
    /// </summary>
    [Fact]
    public void LoggerMessageTemplatesAreConstantAndUseSafeTokens()
    {
        var root = FindSolutionRoot();
        var violations = new List<string>();

        foreach (var source in ProductionSources(root))
        {
            var text = File.ReadAllText(source);
            foreach (Match match in LoggerMessageAttributeRegex().Matches(text))
            {
                var arguments = match.Groups["arguments"].Value;
                var message = ConstantMessageRegex().Match(arguments);
                if (!message.Success)
                {
                    violations.Add($"{Path.GetRelativePath(root, source)}: LoggerMessage Message is not a constant string");
                    continue;
                }

                var template = message.Groups["message"].Value;
                foreach (var token in ForbiddenTemplateAndParameterTokens)
                {
                    if (template.Contains(token, StringComparison.OrdinalIgnoreCase))
                        violations.Add($"{Path.GetRelativePath(root, source)}: template contains {token}");
                }
            }
        }

        Assert.Empty(violations.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Ensures generated logger methods accept only bounded scalar-style parameters.
    /// </summary>
    [Fact]
    public void LoggerMessageMethodsDoNotAcceptForbiddenParameters()
    {
        var root = FindSolutionRoot();
        var violations = new List<string>();

        foreach (var source in ProductionSources(root))
        {
            var text = File.ReadAllText(source);
            foreach (Match match in LoggerMessageMethodRegex().Matches(text))
            {
                var parameters = match.Groups["parameters"].Value
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Where(parameter => !parameter.Contains("ILogger", StringComparison.Ordinal))
                    .ToArray();

                foreach (var parameter in parameters)
                {
                    foreach (var token in ForbiddenParameterTypeTokens.Concat(ForbiddenTemplateAndParameterTokens))
                    {
                        if (parameter.Contains(token, StringComparison.OrdinalIgnoreCase))
                            violations.Add($"{Path.GetRelativePath(root, source)}: logger parameter contains {token}");
                    }
                }
            }
        }

        Assert.Empty(violations.Order(StringComparer.Ordinal));
    }

    private static IEnumerable<string> ProductionProjects(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, "src"), "HPD.Base*.csproj", SearchOption.AllDirectories);

    private static IEnumerable<string> ProductionSources(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !HasPathSegment(path, "bin") && !HasPathSegment(path, "obj"));

    private static IEnumerable<string> ProjectSources(string project)
    {
        var directory = Path.GetDirectoryName(project)!;
        return Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !HasPathSegment(path, "bin") && !HasPathSegment(path, "obj"));
    }

    private static string[] PackageReferences(string project) =>
        XDocument.Load(project)
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(package => !string.IsNullOrWhiteSpace(package))
            .Cast<string>()
            .ToArray();

    private static bool HasFrameworkReference(string project, string reference) =>
        XDocument.Load(project)
            .Descendants()
            .Where(element => element.Name.LocalName == "FrameworkReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Any(include => string.Equals(include, reference, StringComparison.OrdinalIgnoreCase));

    private static bool IsForbiddenLoggingPackage(string package) =>
        package.StartsWith("OpenTelemetry", StringComparison.OrdinalIgnoreCase)
        || package.StartsWith("Microsoft.Extensions.Diagnostics.Testing", StringComparison.OrdinalIgnoreCase)
        || package.StartsWith("Microsoft.Extensions.Logging", StringComparison.OrdinalIgnoreCase)
            && !package.Equals("Microsoft.Extensions.Logging.Abstractions", StringComparison.OrdinalIgnoreCase)
        || package.StartsWith("Serilog", StringComparison.OrdinalIgnoreCase)
        || package.StartsWith("NLog", StringComparison.OrdinalIgnoreCase)
        || package.StartsWith("log4net", StringComparison.OrdinalIgnoreCase);

    private static bool HasPathSegment(string path, string segment) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Contains(segment, StringComparer.OrdinalIgnoreCase);

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HPD-Base.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory.FullName;
    }

    [GeneratedRegex(@"\.Log(?:Trace|Debug|Information|Warning|Error|Critical)?\s*\(")]
    private static partial Regex RawLoggerCallRegex();

    [GeneratedRegex(@"SkipEnabledCheck\s*=\s*true", RegexOptions.IgnoreCase)]
    private static partial Regex SkipEnabledCheckRegex();

    [GeneratedRegex(@"\[(?:Microsoft\.Extensions\.Logging\.)?LoggerMessage\s*\((?<arguments>.*?)\)\s*\]", RegexOptions.Singleline)]
    private static partial Regex LoggerMessageAttributeRegex();

    [GeneratedRegex(@"Message\s*=\s*""(?<message>(?:\\.|[^""\\])*)""", RegexOptions.Singleline)]
    private static partial Regex ConstantMessageRegex();

    [GeneratedRegex(@"\[(?:Microsoft\.Extensions\.Logging\.)?LoggerMessage\s*\(.*?\)\s*\]\s*(?:private|internal|public)?\s*static\s+partial\s+void\s+\w+\s*\((?<parameters>.*?)\)\s*;", RegexOptions.Singleline)]
    private static partial Regex LoggerMessageMethodRegex();
}
