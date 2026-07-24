namespace HPD.Agent.ToolHarness.Coding.Debugging;

internal enum DebugExecutableArtifactKind
{
    Unassociated,
    Application,
    Test
}

internal sealed record DebugExecutableArtifactClassification(
    DebugExecutableArtifactKind Kind,
    string? ProjectPath = null);

internal interface IDebugExecutableArtifactClassifier
{
    ValueTask<DebugExecutableArtifactClassification> ClassifyAsync(
        DebugExecutionPlanningContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// Associates a managed artifact only when its canonical path exactly matches evaluated project output.
/// </summary>
internal sealed class DotNetDebugExecutableArtifactClassifier(
    IDotNetDebugProjectEvaluator evaluator) : IDebugExecutableArtifactClassifier
{
    public async ValueTask<DebugExecutableArtifactClassification> ClassifyAsync(
        DebugExecutionPlanningContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var directCandidates = context.Evidence.MatchedPaths
            .Where(DotNetDebugProjectEvaluator.IsProject)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var candidates = directCandidates.Length > 0
            ? directCandidates
            : DotNetProjectSelection.Candidates(context);
        if (candidates.Count == 0)
            return new(DebugExecutableArtifactKind.Unassociated);
        var process = context.Runtime.ProcessExecution ??
            throw new InvalidOperationException(
                "Executable artifact classification requires process execution.");
        var matches = new List<DotNetDebugProjectEvaluation>();
        foreach (var project in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var evaluation = await evaluator.EvaluateAsync(new()
            {
                CanonicalProjectPath = project,
                Configuration = InferConfiguration(context.CanonicalTargetPath),
                TargetFramework = InferTargetFramework(context.CanonicalTargetPath),
                ProcessExecution = process,
                ProcessSandbox = context.Runtime.ProcessSandbox,
                Workspace = context.Workspace
            }, cancellationToken).ConfigureAwait(false);
            if (SamePath(context.CanonicalTargetPath, evaluation.TargetPath) ||
                evaluation.AppHostPath is not null &&
                SamePath(context.CanonicalTargetPath, evaluation.AppHostPath))
                matches.Add(evaluation);
        }

        if (matches.Count == 0)
            return new(DebugExecutableArtifactKind.Unassociated);
        if (matches.Count > 1)
            throw new DebugStartPlanningException(
                "debug_artifact_project_ambiguous",
                "Multiple evaluated projects produce the requested executable artifact.");
        var match = matches[0];
        return new(
            match.ProjectKind == DotNetDebugProjectKind.Test
                ? DebugExecutableArtifactKind.Test
                : match.ProjectKind == DotNetDebugProjectKind.Application
                    ? DebugExecutableArtifactKind.Application
                    : DebugExecutableArtifactKind.Unassociated,
            match.ProjectPath);
    }

    private static string InferConfiguration(string artifact)
    {
        var segments = Segments(artifact);
        return segments.FirstOrDefault(segment =>
                   segment.Equals("Release", StringComparison.OrdinalIgnoreCase))
               ?? "Debug";
    }

    private static string? InferTargetFramework(string artifact)
        => Segments(artifact).FirstOrDefault(segment =>
            segment.StartsWith("net", StringComparison.OrdinalIgnoreCase) &&
            segment.Length > 3 &&
            segment.Skip(3).Any(char.IsDigit));

    private static IReadOnlyList<string> Segments(string path)
        => path.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

    private static bool SamePath(string left, string right)
        => string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
}
