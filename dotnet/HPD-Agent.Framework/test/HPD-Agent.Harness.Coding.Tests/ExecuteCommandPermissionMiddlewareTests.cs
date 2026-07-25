using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Security;
using HPD.Agent.ToolHarness.Coding;
using HPD.Environment.Contracts;
using HPD.Events.Core;
using HPDOS.ToolHarnesses.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed record ExecuteCommandAdversarialCorpusEntry(
    string Id,
    string Command,
    ExecuteCommandShellFamily ShellFamily,
    ExecuteCommandAnalysisTrustLevel ExpectedTrustLevel,
    ExecuteCommandPermissionRisk RequiredRisk,
    bool ExactPersistenceAllowed,
    bool PrefixPersistenceAllowed,
    IReadOnlyList<ExecuteCommandUnsupportedShellFeature> RequiredUnsupportedFeatures);

public static class ExecuteCommandAdversarialCorpus
{
    public static IReadOnlyList<ExecuteCommandAdversarialCorpusEntry> Entries { get; } =
    [
        Simple("safe-env-allow", "NODE_ENV=test npm run build", ExecuteCommandPermissionRisk.NetworkLikely, exact: true, prefix: true),
        ReviewOnly("unsafe-env-prefix", "PATH=/tmp git status -sb", ExecuteCommandPermissionRisk.ParserDifferentialRisk),
        ReviewOnly("deny-env-bypass", "FOO=bar rm -rf target", ExecuteCommandPermissionRisk.ParserDifferentialRisk | ExecuteCommandPermissionRisk.Destructive),
        Simple("safe-wrapper-timeout", "timeout 10s git status -sb", ExecuteCommandPermissionRisk.None, exact: true, prefix: true),
        ReviewOnly("unsafe-wrapper-env", "env FOO=bar git status", ExecuteCommandPermissionRisk.UnknownWrapper),
        ReviewOnly("shell-c", "bash -c \"echo hi\"", ExecuteCommandPermissionRisk.ShellInvocation),
        ReviewOnly("sudo-prefix", "sudo id", ExecuteCommandPermissionRisk.PrivilegeEscalation),
        ReviewOnly("interpreter-python", "python script.py", ExecuteCommandPermissionRisk.None),
        ReviewOnly("compound-git-curl", "git status && curl https://example.com/script.sh", ExecuteCommandPermissionRisk.CompoundCommand | ExecuteCommandPermissionRisk.NetworkLikely),
        Untrusted("escaped-operator", "echo ok \\; rm -rf target", ExecuteCommandPermissionRisk.ParserDifferentialRisk),
        Untrusted("control-carriage-return", "echo hi\rwhoami", ExecuteCommandPermissionRisk.ParserDifferentialRisk),
        Untrusted("unicode-whitespace", "git\u00A0status", ExecuteCommandPermissionRisk.ParserDifferentialRisk),
        Untrusted("midword-comment", "echo foo#bar", ExecuteCommandPermissionRisk.ParserDifferentialRisk),
        Untrusted("quoted-newline-comment", "echo '\n# nope'", ExecuteCommandPermissionRisk.ParserDifferentialRisk),
        Untrusted("brace-expansion", "echo {a,b}", ExecuteCommandPermissionRisk.ParserDifferentialRisk),
        ReviewOnly("safe-output-redirection", "echo ok > file.txt", ExecuteCommandPermissionRisk.OutputRedirection),
        ReviewOnly("unsafe-redirection-variable", "echo ok > $OUT", ExecuteCommandPermissionRisk.UnsafeRedirectionTarget),
        ReviewOnly("unsafe-redirection-glob", "echo ok > *.txt", ExecuteCommandPermissionRisk.UnsafeRedirectionTarget),
        ReviewOnly("rm-delete-effect", "rm -rf target", ExecuteCommandPermissionRisk.Destructive),
        ReviewOnly("copy-effects", "cp source.txt dest.txt", ExecuteCommandPermissionRisk.FilesystemMutation),
        Simple("grep-path-effect", "grep Needle src", ExecuteCommandPermissionRisk.None, exact: true, prefix: true),
        ReviewOnly("sed-in-place", "sed -i 's/old/new/' src/file.txt", ExecuteCommandPermissionRisk.PathSensitiveWrite),
        ReviewOnly("sed-script-file", "sed -f scripts/edit.sed src/file.txt", ExecuteCommandPermissionRisk.None),
        ReviewOnly("jq-rawfile", "jq --rawfile payload data/payload.txt '.payload' input.json", ExecuteCommandPermissionRisk.OutsideWorkspaceReference),
        ReviewOnly("jq-library-path", "jq -L jq-lib '.items[]' input.json", ExecuteCommandPermissionRisk.OutsideWorkspaceReference),
        ReviewOnly("cd-plus-write", "cd src && touch generated.txt", ExecuteCommandPermissionRisk.CompoundWithDirectoryChange),
        Untrusted("segment-fanout-cap", string.Join(";", Enumerable.Repeat("git status", 51)), ExecuteCommandPermissionRisk.UnknownOrUnparseable),
        ReviewOnly("powershell-encoded", "powershell -EncodedCommand SQBFAFgA", ExecuteCommandPermissionRisk.ShellInvocation, ExecuteCommandShellFamily.PowerShell, features: [ExecuteCommandUnsupportedShellFeature.EncodedCommand]),
        ReviewOnly("powershell-pipeline", "Get-ChildItem | Out-File result.txt", ExecuteCommandPermissionRisk.CompoundCommand, ExecuteCommandShellFamily.PowerShell, features: [ExecuteCommandUnsupportedShellFeature.Pipeline, ExecuteCommandUnsupportedShellFeature.PowerShellFileWritingCommand]),
        ReviewOnly("cmd-redirection", "cmd /c dir > out.txt", ExecuteCommandPermissionRisk.ShellInvocation, ExecuteCommandShellFamily.Cmd, features: [ExecuteCommandUnsupportedShellFeature.CmdCommandSwitch, ExecuteCommandUnsupportedShellFeature.OutputRedirection]),
        ReviewOnly("cmd-for-loop", "for %f in (*) do echo %f", ExecuteCommandPermissionRisk.ParserDifferentialRisk, ExecuteCommandShellFamily.Cmd, features: [ExecuteCommandUnsupportedShellFeature.CmdFor, ExecuteCommandUnsupportedShellFeature.CmdPercentExpansion]),
        Simple("arity-gh-pr-check", "gh pr check 123", ExecuteCommandPermissionRisk.NetworkLikely, exact: true, prefix: true),
        ReviewOnly("arity-git-remote-set-url", "git remote set-url origin https://example.com/repo.git", ExecuteCommandPermissionRisk.NetworkLikely),
        ReviewOnly("external-write-overlay", $"touch {Path.Combine(Path.GetTempPath(), "hpd-external-corpus.txt")}", ExecuteCommandPermissionRisk.OutsideWorkspaceReference)
    ];

    private static ExecuteCommandAdversarialCorpusEntry Simple(
        string id,
        string command,
        ExecuteCommandPermissionRisk risk,
        bool exact,
        bool prefix,
        ExecuteCommandShellFamily family = ExecuteCommandShellFamily.Zsh,
        IReadOnlyList<ExecuteCommandUnsupportedShellFeature>? features = null)
        => new(id, command, family, ExecuteCommandAnalysisTrustLevel.Simple, risk, exact, prefix, features ?? []);

    private static ExecuteCommandAdversarialCorpusEntry Segmented(
        string id,
        string command,
        ExecuteCommandPermissionRisk risk,
        ExecuteCommandShellFamily family = ExecuteCommandShellFamily.Zsh,
        IReadOnlyList<ExecuteCommandUnsupportedShellFeature>? features = null)
        => new(id, command, family, ExecuteCommandAnalysisTrustLevel.Segmented, risk, false, true, features ?? []);

    private static ExecuteCommandAdversarialCorpusEntry ReviewOnly(
        string id,
        string command,
        ExecuteCommandPermissionRisk risk,
        ExecuteCommandShellFamily family = ExecuteCommandShellFamily.Zsh,
        bool exact = false,
        bool prefix = false,
        IReadOnlyList<ExecuteCommandUnsupportedShellFeature>? features = null)
        => new(id, command, family, ExecuteCommandAnalysisTrustLevel.ReviewOnly, risk, exact, prefix, features ?? []);

    private static ExecuteCommandAdversarialCorpusEntry Untrusted(
        string id,
        string command,
        ExecuteCommandPermissionRisk risk,
        ExecuteCommandShellFamily family = ExecuteCommandShellFamily.Zsh,
        IReadOnlyList<ExecuteCommandUnsupportedShellFeature>? features = null)
        => new(id, command, family, ExecuteCommandAnalysisTrustLevel.Untrusted, risk, false, false, features ?? []);
}

[Collection(CurrentDirectoryCollection.Name)]
public sealed class ExecuteCommandPermissionMiddlewareTests : IDisposable
{
    private readonly string _originalCwd = Directory.GetCurrentDirectory();
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"hpd-execute-command-permission-tests-{Guid.NewGuid():N}");

    public static IEnumerable<object[]> AdversarialCorpus()
    {
        foreach (var entry in ExecuteCommandAdversarialCorpus.Entries)
            yield return [entry];
    }

    public ExecuteCommandPermissionMiddlewareTests()
    {
        Directory.CreateDirectory(_tempRoot);
        Directory.SetCurrentDirectory(_tempRoot);
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_originalCwd);
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void Analyzer_OffersPrefixPersistence_ForGitStatus()
    {
        var plan = Analyze("git status -sb");

        var simple = plan.Should().BeOfType<SimpleCommandPermissionPlan>().Subject;
        simple.CommandPlan.SafePrefix.Should().Be("git status");
        simple.PrefixAllowRule.Should().NotBeNull();
        ExecuteCommandPermissionChoiceBuilder.Build(simple, [])
            .OfType<PersistRuleChoice>()
            .Should().Contain(choice => choice.Id == "allow_similar");
    }

    [Fact]
    public void Analyzer_StripsSafeEnvironmentPrefix_ForAllowPersistence()
    {
        var plan = Analyze("NODE_ENV=test npm run build");

        var simple = plan.Should().BeOfType<SimpleCommandPermissionPlan>().Subject;
        simple.CommandPlan.EnvironmentAssignments.Should().ContainKey("NODE_ENV");
        simple.CommandPlan.SafePrefix.Should().Be("npm run build");
        simple.PrefixAllowRule.Should().NotBeNull();
    }

    [Fact]
    public void Analyzer_BlocksPersistentAllow_ForUnsafeEnvironmentPrefix()
    {
        var plan = Analyze("PATH=/tmp git status -sb");

        var review = plan.Should().BeOfType<ReviewOnlyCommandPermissionPlan>().Subject;
        review.Risk.Should().HaveFlag(ExecuteCommandPermissionRisk.ParserDifferentialRisk);
        ExecuteCommandPermissionChoiceBuilder.Build(review, [])
            .OfType<PersistRuleChoice>()
            .Should().BeEmpty();
    }

    [Fact]
    public void RuleMatcher_StripsUnsafeEnvironmentPrefix_ForDenyRules()
    {
        var plan = Analyze("PATH=/tmp rm -rf target");
        var deny = CreateRule(plan, ExecuteCommandPermissionBehavior.Deny, ExecuteCommandPermissionMatchKind.Prefix, "rm");

        var match = ExecuteCommandPermissionRuleMatcher.Match(plan, [deny]);

        match.Decision.Should().BeSameAs(deny);
    }

    [Fact]
    public void RuleMatcher_DoesNotStripUnsafeEnvironmentPrefix_ForAllowRules()
    {
        var plan = Analyze("PATH=/tmp git status -sb");
        var allow = CreateRule(plan, ExecuteCommandPermissionBehavior.Allow, ExecuteCommandPermissionMatchKind.Prefix, "git status");

        var match = ExecuteCommandPermissionRuleMatcher.Match(plan, [allow]);

        match.Decision.Should().BeNull();
    }

    [Fact]
    public void RuleMatcher_UsesAnalyzerArgvShape_ForAllowPrefixRules()
    {
        var plan = Analyze("NODE_ENV=test npm run build");
        var allow = CreateRule(plan, ExecuteCommandPermissionBehavior.Allow, ExecuteCommandPermissionMatchKind.Prefix, "npm run build");

        var match = ExecuteCommandPermissionRuleMatcher.Match(plan, [allow]);

        match.Decision.Should().BeSameAs(allow);
        plan.Should().BeOfType<SimpleCommandPermissionPlan>()
            .Subject.CommandPlan.Argv.Should().Equal(["npm", "run", "build"]);
    }

    [Fact]
    public void RuleMatcher_UsesAnalyzerDefensiveArgvShape_ForDenyRules()
    {
        var plan = Analyze("FOO=bar rm -rf target");
        var deny = CreateRule(plan, ExecuteCommandPermissionBehavior.Deny, ExecuteCommandPermissionMatchKind.Prefix, "rm");

        var match = ExecuteCommandPermissionRuleMatcher.Match(plan, [deny]);

        match.Decision.Should().BeSameAs(deny);
        plan.Should().BeOfType<ReviewOnlyCommandPermissionPlan>()
            .Subject.VisibleSegments[0].DefensiveArgv.Should().Equal(["rm", "-rf", "target"]);
    }

    [Fact]
    public void Analyzer_SegmentedSimilarProposal_ContainsPerSegmentRulesOnly()
    {
        var plan = Analyze("git status && dotnet test");

        var segmented = plan.Should().BeOfType<SegmentedCommandPermissionPlan>().Subject;
        segmented.SegmentRuleBundle.SegmentRules.Select(rule => rule.Pattern)
            .Should().BeEquivalentTo(["git status", "dotnet test"]);
        segmented.SegmentRuleBundle.Rule.Pattern.Should().Be("git status && dotnet test");
    }

    [Theory]
    [InlineData("git remote -v", "git remote -v")]
    [InlineData("gh pr check 123", "gh pr check")]
    [InlineData("npm run build", "npm run build")]
    public void Analyzer_UsesArityAwarePrefixSuggestions(string command, string expectedPrefix)
    {
        var plan = Analyze(command);

        var simple = plan.Should().BeOfType<SimpleCommandPermissionPlan>().Subject;
        simple.CommandPlan.SafePrefix.Should().Be(expectedPrefix);
        simple.PrefixAllowRule.Should().NotBeNull();
    }

    [Theory]
    [InlineData("git status -sb", "git status", ExecuteCommandPolicyReadiness.PrefixAllowAllowed)]
    [InlineData("cat README.md", "cat", ExecuteCommandPolicyReadiness.ExactAllowOnly)]
    public void Analyzer_ReadinessComesFromSemanticPolicy(string command, string expectedPrefix, ExecuteCommandPolicyReadiness expectedReadiness)
    {
        var plan = Analyze(command);

        var simple = plan.Should().BeOfType<SimpleCommandPermissionPlan>().Subject;
        simple.CommandPlan.SafePrefix.Should().Be(expectedPrefix);
        simple.CommandPlan.Readiness.Should().Be(expectedReadiness);
    }

    [Fact]
    public void SemanticPolicy_DefaultsUnknownCommandFamiliesToOneTimeOnly()
    {
        var plan = Analyze("custom-tool inspect workspace");

        var review = plan.Should().BeOfType<ReviewOnlyCommandPermissionPlan>().Subject;
        review.VisibleSegments.Should().ContainSingle(segment =>
            segment.BaseCommand == "custom-tool" &&
            segment.Readiness == ExecuteCommandPolicyReadiness.OneTimeOnly);
        ExecuteCommandPermissionChoiceBuilder.Build(review, [])
            .OfType<PersistRuleChoice>()
            .Should().BeEmpty();
    }

    [Fact]
    public void SemanticPolicy_UsesCurrentPolicyVersionForCommandFamilies()
    {
        ExecuteCommandSemanticPolicy.Default.CommandFamilies
            .Should().OnlyContain(policy =>
                policy.SemanticPolicyVersion == ExecuteCommandPermissionAnalyzerVersions.SemanticPolicy);
    }

    [Fact]
    public void SemanticPolicy_PersistableFamiliesDeclareCoveredParityGates()
    {
        var gates = ExecuteCommandPermissionParityChecklist.Entries
            .ToDictionary(gate => gate.Id, StringComparer.Ordinal);

        ExecuteCommandSemanticPolicy.Default.CommandFamilies
            .Where(policy => policy.Readiness > ExecuteCommandPolicyReadiness.OneTimeOnly)
            .Should().OnlyContain(policy => HasCoveredRequiredParityGates(policy, gates));
    }

    private static bool HasCoveredRequiredParityGates(
        ExecuteCommandCommandFamilyPolicy policy,
        IReadOnlyDictionary<string, ExecuteCommandPermissionParityGate> gates)
        => policy.RequiredParityGateIds.Count > 0 &&
           policy.RequiredParityGateIds.All(id =>
               gates.TryGetValue(id, out var gate) &&
               gate.Status >= policy.MinimumParityStatus &&
               gate.RequiredCorpusIds.Count > 0);

    [Fact]
    public void SemanticPolicy_ResolveReadinessDowngradesMissingParityRequirements()
    {
        var policy = new ExecuteCommandCommandFamilyPolicy
        {
            SemanticPolicyVersion = ExecuteCommandPermissionAnalyzerVersions.SemanticPolicy,
            Pattern = "unsafe persist",
            Readiness = ExecuteCommandPolicyReadiness.PrefixAllowAllowed,
            SuggestionArities = [2],
            RequiredParityGateIds = ["missing-gate"]
        };

        ExecuteCommandSemanticPolicy.Default.ResolveReadiness(policy)
            .Should().Be(ExecuteCommandPolicyReadiness.OneTimeOnly);
    }

    [Fact]
    public void SemanticPolicy_ResolveReadinessDowngradesUnderCoveredParityRequirements()
    {
        var policy = new ExecuteCommandCommandFamilyPolicy
        {
            SemanticPolicyVersion = ExecuteCommandPermissionAnalyzerVersions.SemanticPolicy,
            Pattern = "unsafe persist",
            Readiness = ExecuteCommandPolicyReadiness.PrefixAllowAllowed,
            SuggestionArities = [2],
            RequiredParityGateIds = ["safe-env-allow"],
            MinimumParityStatus = ExecuteCommandPermissionParityStatus.PersistenceEnabled
        };

        ExecuteCommandSemanticPolicy.Default.ResolveReadiness(policy)
            .Should().Be(ExecuteCommandPolicyReadiness.OneTimeOnly);
    }

    [Fact]
    public void SemanticPolicy_ResolveReadinessPreservesCoveredParityRequirements()
    {
        var policy = new ExecuteCommandCommandFamilyPolicy
        {
            SemanticPolicyVersion = ExecuteCommandPermissionAnalyzerVersions.SemanticPolicy,
            Pattern = "safe persist",
            Readiness = ExecuteCommandPolicyReadiness.ExactAllowOnly,
            SuggestionArities = [2],
            RequiredParityGateIds = ["safe-env-allow"]
        };

        ExecuteCommandSemanticPolicy.Default.ResolveReadiness(policy)
            .Should().Be(ExecuteCommandPolicyReadiness.ExactAllowOnly);
    }

    [Fact]
    public void PosixParser_ProducesTypedOperatorsAndSegments()
    {
        var parse = ParsePosix("git status && dotnet test || npm run build");

        parse.Segments.Select(segment => segment.Text)
            .Should().Equal("git status", "dotnet test", "npm run build");
        parse.Operators.Select(op => op.Kind)
            .Should().Equal(ExecuteCommandShellOperatorKind.And, ExecuteCommandShellOperatorKind.Or);
        parse.Risk.Should().HaveFlag(ExecuteCommandPermissionRisk.CompoundCommand);
    }

    [Fact]
    public void PosixParser_QuotedOperatorsStayInsideSegmentTokens()
    {
        var parse = ParsePosix("echo 'a && b' && git status");

        parse.Segments.Should().HaveCount(2);
        parse.Segments[0].Tokens.Select(token => token.Text).Should().Equal("echo", "a && b");
        parse.Segments[0].Tokens[1].Kind.Should().Be(ExecuteCommandShellTokenKind.SingleQuoted);
        parse.Operators.Should().ContainSingle(op => op.Kind == ExecuteCommandShellOperatorKind.And);
    }

    [Fact]
    public void PosixParser_ProducesTypedRedirectionNodes()
    {
        var parse = ParsePosix("cat < input.txt > logs/out.txt");

        parse.Segments.Should().ContainSingle();
        parse.Segments[0].Tokens.Select(token => token.Text).Should().Equal("cat");
        parse.Segments[0].Redirections.Should().BeEquivalentTo([
            new ExecuteCommandRedirectionPlan
            {
                Kind = ExecuteCommandRedirectionKind.Input,
                Target = "input.txt",
                Operation = ExecuteCommandFilesystemOperation.Read,
                TargetStaticallyResolved = true
            },
            new ExecuteCommandRedirectionPlan
            {
                Kind = ExecuteCommandRedirectionKind.Output,
                Target = "logs/out.txt",
                Operation = ExecuteCommandFilesystemOperation.Write,
                TargetStaticallyResolved = true
            }
        ]);
    }

    [Fact]
    public void PosixParser_ReportsTypedParserDifferentialFeatures()
    {
        var parse = ParsePosix("echo ok \\; rm -rf target");

        parse.Risk.Should().HaveFlag(ExecuteCommandPermissionRisk.ParserDifferentialRisk);
        parse.UnsupportedFeatures.Should().Contain(ExecuteCommandUnsupportedShellFeature.ParserDifferential);
        parse.UnsupportedFeatures.Should().Contain(ExecuteCommandUnsupportedShellFeature.EscapedOperator);
    }

    [Fact]
    public void PosixParser_ProducesTypedCommandSubstitutionNodesAndDoesNotSplitInternalOperators()
    {
        var parse = ParsePosix("echo $(whoami | wc -c) && git status");

        parse.Segments.Select(segment => segment.Text)
            .Should().Equal("echo $(whoami | wc -c)", "git status");
        parse.Operators.Should().ContainSingle(op => op.Kind == ExecuteCommandShellOperatorKind.And);
        parse.Expansions.Should().ContainSingle(expansion =>
            expansion.Kind == ExecuteCommandShellExpansionKind.CommandSubstitution &&
            expansion.Text == "$(whoami | wc -c)");
        parse.Segments[0].Expansions.Should().ContainSingle(expansion =>
            expansion.Kind == ExecuteCommandShellExpansionKind.CommandSubstitution &&
            expansion.Text == "$(whoami | wc -c)");
        parse.Risk.Should().HaveFlag(ExecuteCommandPermissionRisk.CommandSubstitution);
    }

    [Fact]
    public void PosixParser_ProducesTypedBareVariableExpansionNodesButSuppressesSingleQuotedVariables()
    {
        var parse = ParsePosix("echo \"$TARGET\" '$IGNORED'");

        parse.Expansions.Should().ContainSingle(expansion =>
            expansion.Kind == ExecuteCommandShellExpansionKind.BareVariable &&
            expansion.Text == "$TARGET");
        parse.Expansions.Should().NotContain(expansion => expansion.Text == "$IGNORED");
        parse.Risk.Should().HaveFlag(ExecuteCommandPermissionRisk.BareVariableExpansion);
    }

    [Fact]
    public void PosixParser_ProducesTypedHeredocNodes()
    {
        var parse = ParsePosix("cat <<'EOF'\n$HOME\nEOF");

        parse.Heredocs.Should().ContainSingle(heredoc =>
            heredoc.Operator == "<<" &&
            heredoc.Delimiter == "EOF" &&
            heredoc.DelimiterQuoted &&
            heredoc.Body == "$HOME");
        parse.Risk.Should().HaveFlag(ExecuteCommandPermissionRisk.Heredoc);
        parse.UnsupportedFeatures.Should().Contain(ExecuteCommandUnsupportedShellFeature.Heredoc);
    }

    [Fact]
    public void PosixParser_ProducesTypedSubshellNodesAndDoesNotSplitInternalOperators()
    {
        var parse = ParsePosix("(cd src; make) && git status");

        parse.Segments.Select(segment => segment.Text)
            .Should().Equal("(cd src; make)", "git status");
        parse.Operators.Should().ContainSingle(op => op.Kind == ExecuteCommandShellOperatorKind.And);
        parse.Subshells.Should().ContainSingle(subshell => subshell.Text == "(cd src; make)");
        parse.Segments[0].Subshells.Should().ContainSingle(subshell => subshell.Text == "(cd src; make)");
        parse.Risk.Should().HaveFlag(ExecuteCommandPermissionRisk.Subshell);
        parse.UnsupportedFeatures.Should().Contain(ExecuteCommandUnsupportedShellFeature.Subshell);
    }

    [Theory]
    [InlineData("rm -rf target", ExecuteCommandPermissionRisk.FilesystemMutation | ExecuteCommandPermissionRisk.Destructive)]
    [InlineData("npm install", ExecuteCommandPermissionRisk.NetworkLikely)]
    [InlineData("bash -c \"echo hi\"", ExecuteCommandPermissionRisk.ShellInvocation)]
    [InlineData("sudo id", ExecuteCommandPermissionRisk.PrivilegeEscalation)]
    [InlineData("sed -i 's/a/b/' file.txt", ExecuteCommandPermissionRisk.PathSensitiveWrite | ExecuteCommandPermissionRisk.FilesystemMutation)]
    [InlineData("jq --rawfile payload data.txt '.payload' input.json", ExecuteCommandPermissionRisk.OutsideWorkspaceReference)]
    [InlineData("find src -delete", ExecuteCommandPermissionRisk.DangerousShellBuiltin | ExecuteCommandPermissionRisk.FilesystemMutation)]
    [InlineData("git diff --no-index left.txt right.txt", ExecuteCommandPermissionRisk.OutsideWorkspaceReference)]
    public void Analyzer_CommandFamilyRiskComesFromSemanticPolicy(string command, ExecuteCommandPermissionRisk expectedRisk)
    {
        var plan = Analyze(command);

        plan.Risk.Should().HaveFlag(expectedRisk);
    }

    [Theory]
    [InlineData("cat")]
    [InlineData("rg")]
    [InlineData("rm")]
    [InlineData("cp")]
    [InlineData("mv")]
    [InlineData("sed")]
    [InlineData("jq")]
    [InlineData("find")]
    public void SemanticPolicy_PathSensitiveFamiliesDeclareFilesystemExtractors(string pattern)
    {
        ExecuteCommandSemanticPolicy.Default.CommandFamilies
            .Where(policy => policy.Pattern == pattern)
            .Should().OnlyContain(policy => policy.FilesystemEffectExtractor != null);
    }

    [Theory]
    [InlineData("npm")]
    [InlineData("git")]
    [InlineData("gh")]
    [InlineData("curl")]
    [InlineData("wget")]
    public void SemanticPolicy_NetworkFamiliesDeclareNetworkClassifiers(string pattern)
    {
        ExecuteCommandSemanticPolicy.Default.CommandFamilies
            .Where(policy => policy.Pattern == pattern)
            .Should().OnlyContain(policy => policy.NetworkEffectClassifier != null);
    }

    [Theory]
    [MemberData(nameof(AdversarialCorpus))]
    public void Analyzer_AdversarialCorpus_MatchesExpectedPermissionShape(ExecuteCommandAdversarialCorpusEntry entry)
    {
        var plan = Analyze(entry.Command, entry.ShellFamily);
        var choices = ExecuteCommandPermissionChoiceBuilder.Build(plan, []);

        plan.TrustLevel.Should().Be(entry.ExpectedTrustLevel, entry.Id);
        if (entry.RequiredRisk != ExecuteCommandPermissionRisk.None)
            plan.Risk.Should().HaveFlag(entry.RequiredRisk, entry.Id);
        foreach (var feature in entry.RequiredUnsupportedFeatures)
            plan.UnsupportedShellFeatures.Should().Contain(feature, entry.Id);
        choices.OfType<PersistRuleChoice>()
            .Any(choice => choice.Id.StartsWith("allow_exact", StringComparison.Ordinal))
            .Should().Be(entry.ExactPersistenceAllowed, entry.Id);
        choices.OfType<PersistRuleChoice>()
            .Any(choice => choice.Id == "allow_similar")
            .Should().Be(entry.PrefixPersistenceAllowed, entry.Id);
    }

    [Fact]
    public void PermissionParityChecklist_CorpusCoveredGatesMustReferenceExistingCorpusEntries()
    {
        var corpusIds = ExecuteCommandAdversarialCorpus.Entries
            .Select(entry => entry.Id)
            .ToHashSet(StringComparer.Ordinal);

        ExecuteCommandPermissionParityChecklist.Entries
            .Where(entry => entry.Status >= ExecuteCommandPermissionParityStatus.CorpusCovered)
            .Should().OnlyContain(entry =>
                entry.RequiredCorpusIds.Count > 0 &&
                entry.RequiredCorpusIds.All(corpusIds.Contains));
    }

    [Fact]
    public void PermissionParityChecklist_RequiredCorpusIdsMustExist()
    {
        var corpusIds = ExecuteCommandAdversarialCorpus.Entries
            .Select(entry => entry.Id)
            .ToHashSet(StringComparer.Ordinal);

        ExecuteCommandPermissionParityChecklist.Entries
            .SelectMany(entry => entry.RequiredCorpusIds)
            .Should().OnlyContain(id => corpusIds.Contains(id));
    }

    [Theory]
    [InlineData("python script.py")]
    [InlineData("node server.js")]
    [InlineData("git remote set-url origin https://example.com/repo.git")]
    public void Analyzer_DoesNotBroadenDangerousOrMultiMeaningPrefixes(string command)
    {
        var plan = Analyze(command);

        plan.Should().BeOfType<ReviewOnlyCommandPermissionPlan>();
        ExecuteCommandPermissionChoiceBuilder.Build(plan, [])
            .OfType<PersistRuleChoice>()
            .Should().BeEmpty();
    }

    [Fact]
    public void RuleMatcher_AllowsSegmentedPlan_OnlyWhenEverySegmentHasAllowRule()
    {
        var plan = Analyze("git status && dotnet test").Should().BeOfType<SegmentedCommandPermissionPlan>().Subject;
        var gitRule = CreateRule(plan, ExecuteCommandPermissionBehavior.Allow, ExecuteCommandPermissionMatchKind.Prefix, "git status");
        var dotnetRule = CreateRule(plan, ExecuteCommandPermissionBehavior.Allow, ExecuteCommandPermissionMatchKind.Prefix, "dotnet test");

        ExecuteCommandPermissionRuleMatcher.Match(plan, [gitRule]).Decision.Should().BeNull();
        ExecuteCommandPermissionRuleMatcher.Match(plan, [gitRule, dotnetRule]).Decision.Should().NotBeNull();
    }

    [Fact]
    public void RuleMatcher_AskShadowsAllowAndReportsDiagnostics()
    {
        var plan = Analyze("git status -sb");
        var ask = CreateRule(plan, ExecuteCommandPermissionBehavior.Ask, ExecuteCommandPermissionMatchKind.Prefix, "git status");
        var allow = CreateRule(plan, ExecuteCommandPermissionBehavior.Allow, ExecuteCommandPermissionMatchKind.Prefix, "git status");

        var match = ExecuteCommandPermissionRuleMatcher.Match(plan, [allow, ask]);

        match.Decision.Should().BeSameAs(ask);
        match.Diagnostics.DecisionRuleId.Should().Be(ask.Id);
        match.Diagnostics.DecisionBehavior.Should().Be(ExecuteCommandPermissionBehavior.Ask);
        match.Diagnostics.MatchingRules.Should().BeEquivalentTo([allow, ask]);
        match.Diagnostics.ShadowedRules.Should().ContainSingle(rule => rule.Id == allow.Id);
    }

    [Fact]
    public void RuleMatcher_DenyShadowsAskAndAllowAndReportsDiagnostics()
    {
        var plan = Analyze("rm -rf target");
        var deny = CreateRule(plan, ExecuteCommandPermissionBehavior.Deny, ExecuteCommandPermissionMatchKind.Prefix, "rm");
        var ask = CreateRule(plan, ExecuteCommandPermissionBehavior.Ask, ExecuteCommandPermissionMatchKind.Prefix, "rm");
        var allow = CreateRule(plan, ExecuteCommandPermissionBehavior.Allow, ExecuteCommandPermissionMatchKind.Prefix, "rm");

        var match = ExecuteCommandPermissionRuleMatcher.Match(plan, [allow, ask, deny]);

        match.Decision.Should().BeSameAs(deny);
        match.Diagnostics.DecisionBehavior.Should().Be(ExecuteCommandPermissionBehavior.Deny);
        match.Diagnostics.ShadowedRules.Select(rule => rule.Id).Should().BeEquivalentTo([ask.Id, allow.Id]);
    }

    [Fact]
    public void RuleMatcher_ReportsInactiveRuleReasons()
    {
        var plan = Analyze("git status -sb");
        var versionMismatch = CreateRule(plan, ExecuteCommandPermissionBehavior.Allow, ExecuteCommandPermissionMatchKind.Prefix, "git status")
            with { AnalyzerVersion = ExecuteCommandPermissionAnalyzerVersions.Analyzer + 1 };
        var workspaceMismatch = CreateRule(plan, ExecuteCommandPermissionBehavior.Allow, ExecuteCommandPermissionMatchKind.Prefix, "git status")
            with
            {
                Workspace = plan.Workspace with { RootId = "other-root" }
            };
        var sandboxMismatch = CreateRule(plan, ExecuteCommandPermissionBehavior.Allow, ExecuteCommandPermissionMatchKind.Prefix, "git status")
            with
            {
                RequestedSandboxFingerprint = (plan.RequestedSandbox with
                {
                    Security = plan.RequestedSandbox.Security with
                    {
                        Sandbox = AgentSandboxPolicy.Disabled
                    }
                })
                    .Canonicalize(plan.WorkingDirectory)
            };

        var match = ExecuteCommandPermissionRuleMatcher.Match(plan, [versionMismatch, workspaceMismatch, sandboxMismatch]);

        match.Decision.Should().BeNull();
        match.Diagnostics.InactiveRules.Should().Contain(rule =>
            rule.RuleId == versionMismatch.Id && rule.Reason == "analyzer_version_mismatch");
        match.Diagnostics.InactiveRules.Should().Contain(rule =>
            rule.RuleId == workspaceMismatch.Id && rule.Reason == "workspace_root_mismatch");
        match.Diagnostics.InactiveRules.Should().Contain(rule =>
            rule.RuleId == sandboxMismatch.Id && rule.Reason == "sandbox_scope_mismatch");
    }

    [Fact]
    public void RuleLifecycle_WithValidatedRuleAcceptsAnalyzerApprovedPrefixAllow()
    {
        var plan = Analyze("git status -sb");
        var rule = CreateRule(plan, ExecuteCommandPermissionBehavior.Allow, ExecuteCommandPermissionMatchKind.Prefix, "git status");

        var state = new ExecuteCommandPermissionStateData().WithValidatedRule(rule);

        state.Rules.Should().ContainSingle().Which.Should().BeSameAs(rule);
    }

    [Fact]
    public void RuleLifecycle_WithValidatedRuleRejectsUnmodeledAllowPrefix()
    {
        var plan = Analyze("git status -sb");
        var rule = CreateRule(plan, ExecuteCommandPermissionBehavior.Allow, ExecuteCommandPermissionMatchKind.Prefix, "bash");

        var act = () => new ExecuteCommandPermissionStateData().WithValidatedRule(rule);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*prefix_allow_not_reproducible*");
    }

    [Fact]
    public void RuleLifecycle_WithValidatedRuleRejectsAllowWildcard()
    {
        var plan = Analyze("git status -sb");
        var rule = CreateRule(plan, ExecuteCommandPermissionBehavior.Allow, ExecuteCommandPermissionMatchKind.Wildcard, "*");

        var act = () => new ExecuteCommandPermissionStateData().WithValidatedRule(rule);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*allow_wildcard_not_persistable*");
    }

    [Fact]
    public void RuleLifecycle_DenyRulesDoNotRequireAllowReadiness()
    {
        var plan = Analyze("bash -c \"echo hi\"");
        var rule = CreateRule(plan, ExecuteCommandPermissionBehavior.Deny, ExecuteCommandPermissionMatchKind.Prefix, "bash");

        var state = new ExecuteCommandPermissionStateData().WithValidatedRule(rule);

        state.Rules.Should().ContainSingle().Which.Should().BeSameAs(rule);
    }

    [Fact]
    public void RuleLifecycle_ImportRulesAddsOnlyAnalyzerValidatedRules()
    {
        var plan = Analyze("git status -sb");
        var valid = CreateRule(plan, ExecuteCommandPermissionBehavior.Allow, ExecuteCommandPermissionMatchKind.Prefix, "git status");
        var invalid = CreateRule(plan, ExecuteCommandPermissionBehavior.Allow, ExecuteCommandPermissionMatchKind.Prefix, "bash");

        var result = new ExecuteCommandPermissionStateData()
            .ImportRules([valid, invalid], CreateWorkspaceRunConfig());

        result.Success.Should().BeFalse();
        result.State.Rules.Should().ContainSingle().Which.Should().BeSameAs(valid);
        result.State.InactiveRules.Should().ContainSingle(inactive =>
            inactive.Rule == invalid &&
            inactive.Reason == "prefix_allow_not_reproducible");
        result.AuditRecords.Should().Contain(record =>
            record.Operation == ExecuteCommandPermissionRuleLifecycleOperation.Import &&
            record.Action == ExecuteCommandPermissionRuleLifecycleAction.Activated &&
            record.RuleId == valid.Id);
        result.AuditRecords.Should().Contain(record =>
            record.Operation == ExecuteCommandPermissionRuleLifecycleOperation.Import &&
            record.Action == ExecuteCommandPermissionRuleLifecycleAction.Inactivated &&
            record.RuleId == invalid.Id &&
            record.Reason == "prefix_allow_not_reproducible");
        result.Issues.Should().ContainSingle(issue =>
            issue.RuleId == invalid.Id &&
            issue.Pattern == invalid.Pattern &&
            issue.Reason == "prefix_allow_not_reproducible");
    }

    [Fact]
    public void RuleLifecycle_ReplaceRuleRequiresExistingIdAndAnalyzerValidatedReplacement()
    {
        var plan = Analyze("git status -sb");
        var original = CreateRule(plan, ExecuteCommandPermissionBehavior.Allow, ExecuteCommandPermissionMatchKind.Prefix, "git status");
        var replacement = CreateRule(plan, ExecuteCommandPermissionBehavior.Allow, ExecuteCommandPermissionMatchKind.Exact, "git status -sb")
            with { Id = original.Id };
        var state = new ExecuteCommandPermissionStateData().WithValidatedRule(original);

        var result = state.ReplaceRule(original.Id, replacement, CreateWorkspaceRunConfig());

        result.Success.Should().BeTrue();
        result.State.Rules.Should().ContainSingle().Which.Should().BeSameAs(replacement);
        result.AuditRecords.Should().ContainSingle(record =>
            record.Operation == ExecuteCommandPermissionRuleLifecycleOperation.Replace &&
            record.Action == ExecuteCommandPermissionRuleLifecycleAction.Activated &&
            record.RuleId == replacement.Id);
    }

    [Fact]
    public void RuleLifecycle_ReplaceRuleRejectsIdMismatch()
    {
        var plan = Analyze("git status -sb");
        var original = CreateRule(plan, ExecuteCommandPermissionBehavior.Allow, ExecuteCommandPermissionMatchKind.Prefix, "git status");
        var replacement = CreateRule(plan, ExecuteCommandPermissionBehavior.Allow, ExecuteCommandPermissionMatchKind.Exact, "git status -sb");
        var state = new ExecuteCommandPermissionStateData().WithValidatedRule(original);

        var result = state.ReplaceRule(original.Id, replacement, CreateWorkspaceRunConfig());

        result.State.Should().BeSameAs(state);
        result.Issues.Should().ContainSingle(issue =>
            issue.RuleId == replacement.Id &&
            issue.Reason == "rule_id_mismatch");
        result.AuditRecords.Should().ContainSingle(record =>
            record.Operation == ExecuteCommandPermissionRuleLifecycleOperation.Replace &&
            record.Action == ExecuteCommandPermissionRuleLifecycleAction.Rejected &&
            record.RuleId == replacement.Id &&
            record.Reason == "rule_id_mismatch");
    }

    [Fact]
    public void RuleLifecycle_RevalidateRulesReportsAndRemovesStaleWorkspaceRules()
    {
        var plan = Analyze("git status -sb");
        var valid = CreateRule(plan, ExecuteCommandPermissionBehavior.Allow, ExecuteCommandPermissionMatchKind.Prefix, "git status");
        var movedWorkspacePath = Path.Combine(_tempRoot, "moved-workspace");
        Directory.CreateDirectory(movedWorkspacePath);
        var changedWorkspace = CreateRule(plan, ExecuteCommandPermissionBehavior.Allow, ExecuteCommandPermissionMatchKind.Prefix, "git status")
            with
            {
                Workspace = plan.Workspace with
                {
                    RootPath = movedWorkspacePath
                }
            };
        var state = new ExecuteCommandPermissionStateData()
            .WithValidatedRule(valid)
            .WithValidatedRule(changedWorkspace);

        var result = state.RevalidateRules(CreateWorkspaceRunConfig());

        result.Success.Should().BeFalse();
        result.State.Rules.Should().ContainSingle().Which.Should().BeSameAs(valid);
        result.State.InactiveRules.Should().ContainSingle(inactive =>
            inactive.Rule == changedWorkspace &&
            inactive.Reason == "workspace_root_path_changed");
        result.AuditRecords.Should().ContainSingle(record =>
            record.Operation == ExecuteCommandPermissionRuleLifecycleOperation.Revalidate &&
            record.Action == ExecuteCommandPermissionRuleLifecycleAction.Inactivated &&
            record.RuleId == changedWorkspace.Id &&
            record.Reason == "workspace_root_path_changed");
        result.Issues.Should().ContainSingle(issue =>
            issue.RuleId == changedWorkspace.Id &&
            issue.Reason == "workspace_root_path_changed");
    }

    [Fact]
    public void RuleLifecycle_RevalidateRulesReportsAndRemovesObsoleteVersionRules()
    {
        var plan = Analyze("git status -sb");
        var valid = CreateRule(plan, ExecuteCommandPermissionBehavior.Allow, ExecuteCommandPermissionMatchKind.Prefix, "git status");
        var stale = CreateRule(plan, ExecuteCommandPermissionBehavior.Allow, ExecuteCommandPermissionMatchKind.Prefix, "git status")
            with { AnalyzerVersion = ExecuteCommandPermissionAnalyzerVersions.Analyzer + 1 };
        var state = new ExecuteCommandPermissionStateData
        {
            Rules = [valid, stale]
        };

        var result = state.RevalidateRules(CreateWorkspaceRunConfig());

        result.Success.Should().BeFalse();
        result.State.Rules.Should().ContainSingle().Which.Should().BeSameAs(valid);
        result.State.InactiveRules.Should().ContainSingle(inactive =>
            inactive.Rule == stale &&
            inactive.Reason == "analyzer_version_mismatch");
        result.Issues.Should().ContainSingle(issue =>
            issue.RuleId == stale.Id &&
            issue.Reason == "analyzer_version_mismatch");
    }

    [Fact]
    public void RuleLifecycle_WithoutRuleRemovesActiveAndInactiveRules()
    {
        var plan = Analyze("git status -sb");
        var active = CreateRule(plan, ExecuteCommandPermissionBehavior.Allow, ExecuteCommandPermissionMatchKind.Prefix, "git status");
        var inactive = CreateRule(plan, ExecuteCommandPermissionBehavior.Allow, ExecuteCommandPermissionMatchKind.Prefix, "bash");
        var state = new ExecuteCommandPermissionStateData()
            .WithValidatedRule(active)
            .ImportRules([inactive], CreateWorkspaceRunConfig())
            .State;

        var removed = state.WithoutRule(inactive.Id);

        removed.Rules.Should().ContainSingle().Which.Should().BeSameAs(active);
        removed.InactiveRules.Should().BeEmpty();
    }

    [Fact]
    public void RuleLifecycle_ClearRemovesActiveAndInactiveRules()
    {
        var plan = Analyze("git status -sb");
        var active = CreateRule(plan, ExecuteCommandPermissionBehavior.Allow, ExecuteCommandPermissionMatchKind.Prefix, "git status");
        var inactive = CreateRule(plan, ExecuteCommandPermissionBehavior.Allow, ExecuteCommandPermissionMatchKind.Prefix, "bash");
        var state = new ExecuteCommandPermissionStateData()
            .WithValidatedRule(active)
            .ImportRules([inactive], CreateWorkspaceRunConfig())
            .State;

        var cleared = state.Clear();

        cleared.Rules.Should().BeEmpty();
        cleared.InactiveRules.Should().BeEmpty();
    }

    [Theory]
    [InlineData("bash -c \"echo hi\"")]
    [InlineData("echo $(whoami)")]
    [InlineData("cat <<EOF\nhello\nEOF")]
    [InlineData("echo ok > file.txt")]
    public void Analyzer_ReviewOnlyCommands_DoNotOfferPersistentChoices(string command)
    {
        var plan = Analyze(command);

        plan.TrustLevel.Should().Be(ExecuteCommandAnalysisTrustLevel.ReviewOnly);
        ExecuteCommandPermissionChoiceBuilder.Build(plan, [])
            .OfType<PersistRuleChoice>()
            .Should().BeEmpty();
    }

    [Fact]
    public void Analyzer_StaticOutputRedirection_ProducesWriteEffectAndNoPersistentAllow()
    {
        var plan = Analyze("echo ok > logs/out.txt");

        var review = plan.Should().BeOfType<ReviewOnlyCommandPermissionPlan>().Subject;
        review.VisibleSegments.Should().ContainSingle();
        review.VisibleSegments[0].Redirections.Should().ContainSingle(redirection =>
            redirection.Kind == ExecuteCommandRedirectionKind.Output &&
            redirection.Operation == ExecuteCommandFilesystemOperation.Write &&
            redirection.Target == "logs/out.txt" &&
            redirection.TargetStaticallyResolved);
        review.FilesystemEffects.Should().ContainSingle(effect =>
            effect.Operation == ExecuteCommandFilesystemOperation.Write &&
            effect.Path == Path.GetFullPath(Path.Combine(_tempRoot, "logs/out.txt")) &&
            effect.WithinWorkspace);
        ExecuteCommandPermissionChoiceBuilder.Build(review, [])
            .OfType<PersistRuleChoice>()
            .Should().BeEmpty();
    }

    [Fact]
    public void Analyzer_InputRedirection_ProducesReadEffect()
    {
        var plan = Analyze("cat < input.txt");

        var review = plan.Should().BeOfType<ReviewOnlyCommandPermissionPlan>().Subject;
        review.VisibleSegments[0].Redirections.Should().ContainSingle(redirection =>
            redirection.Kind == ExecuteCommandRedirectionKind.Input &&
            redirection.Operation == ExecuteCommandFilesystemOperation.Read &&
            redirection.Target == "input.txt");
        review.FilesystemEffects.Should().Contain(effect =>
            effect.Operation == ExecuteCommandFilesystemOperation.Read &&
            effect.Path == Path.GetFullPath(Path.Combine(_tempRoot, "input.txt")));
    }

    [Theory]
    [InlineData("echo ok > $OUT")]
    [InlineData("echo ok > *.txt")]
    [InlineData("echo ok > ~/out.txt")]
    [InlineData("echo ok > $(pwd)/out.txt")]
    public void Analyzer_UnsafeRedirectionTargets_DoNotPersist(string command)
    {
        var plan = Analyze(command);

        plan.TrustLevel.Should().Be(ExecuteCommandAnalysisTrustLevel.ReviewOnly);
        plan.Risk.Should().HaveFlag(ExecuteCommandPermissionRisk.UnsafeRedirectionTarget);
        ExecuteCommandPermissionChoiceBuilder.Build(plan, [])
            .OfType<PersistRuleChoice>()
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("rm -rf target", ExecuteCommandFilesystemOperation.Delete, "target")]
    [InlineData("cp source.txt dest.txt", ExecuteCommandFilesystemOperation.Read, "source.txt")]
    [InlineData("cp source.txt dest.txt", ExecuteCommandFilesystemOperation.Write, "dest.txt")]
    [InlineData("mv source.txt dest.txt", ExecuteCommandFilesystemOperation.Delete, "source.txt")]
    [InlineData("mv source.txt dest.txt", ExecuteCommandFilesystemOperation.Write, "dest.txt")]
    [InlineData("mkdir -p generated", ExecuteCommandFilesystemOperation.Create, "generated")]
    [InlineData("touch output.txt", ExecuteCommandFilesystemOperation.Create, "output.txt")]
    [InlineData("chmod 600 output.txt", ExecuteCommandFilesystemOperation.Write, "output.txt")]
    [InlineData("rg Needle src", ExecuteCommandFilesystemOperation.Read, "src")]
    public void Analyzer_PathSensitiveCommands_ProduceFilesystemEffects(
        string command,
        ExecuteCommandFilesystemOperation expectedOperation,
        string expectedPath)
    {
        var plan = Analyze(command);

        plan.FilesystemEffects.Should().Contain(effect =>
            effect.Operation == expectedOperation &&
            effect.Path == Path.GetFullPath(Path.Combine(_tempRoot, expectedPath)) &&
            effect.WithinWorkspace);
    }

    [Fact]
    public void Analyzer_SearchCommands_DoNotTreatPatternAsPath()
    {
        var plan = Analyze("rg Needle src");

        plan.FilesystemEffects.Should().NotContain(effect =>
            effect.Path == Path.GetFullPath(Path.Combine(_tempRoot, "Needle")));
        plan.FilesystemEffects.Should().Contain(effect =>
            effect.Operation == ExecuteCommandFilesystemOperation.Read &&
            effect.Path == Path.GetFullPath(Path.Combine(_tempRoot, "src")));
    }

    [Theory]
    [InlineData("sed -i 's/old/new/' src/file.txt", ExecuteCommandFilesystemOperation.Write, "src/file.txt")]
    [InlineData("sed -f scripts/edit.sed src/file.txt", ExecuteCommandFilesystemOperation.Read, "scripts/edit.sed")]
    [InlineData("jq --rawfile payload data/payload.txt '.payload' input.json", ExecuteCommandFilesystemOperation.Read, "data/payload.txt")]
    [InlineData("jq -L jq-lib '.items[]' input.json", ExecuteCommandFilesystemOperation.Read, "jq-lib")]
    [InlineData("find src -name '*.cs'", ExecuteCommandFilesystemOperation.Read, "src")]
    [InlineData("find build -delete", ExecuteCommandFilesystemOperation.Delete, ".")]
    [InlineData("git diff --no-index left.txt right.txt", ExecuteCommandFilesystemOperation.Read, "left.txt")]
    [InlineData("git diff --no-index left.txt right.txt", ExecuteCommandFilesystemOperation.Read, "right.txt")]
    public void Analyzer_SpecialPathSensitiveFamilies_ProduceFilesystemEffects(
        string command,
        ExecuteCommandFilesystemOperation expectedOperation,
        string expectedPath)
    {
        var plan = Analyze(command);

        plan.TrustLevel.Should().Be(ExecuteCommandAnalysisTrustLevel.ReviewOnly);
        ExecuteCommandPermissionChoiceBuilder.Build(plan, [])
            .OfType<PersistRuleChoice>()
            .Should().BeEmpty();
        plan.FilesystemEffects.Should().Contain(effect =>
            effect.Operation == expectedOperation &&
            effect.Path == Path.GetFullPath(Path.Combine(_tempRoot, expectedPath)));
    }

    [Fact]
    public void ChoiceBuilder_DoesNotOfferSandboxOverlay_ForExternalFilesystemEffect()
    {
        var externalPath = $"/private/tmp/hpd-execute-command-permission-{Guid.NewGuid():N}.txt";
        var plan = Analyze($"touch {externalPath}");

        plan.FilesystemEffects.Should().ContainSingle(effect =>
            effect.Operation == ExecuteCommandFilesystemOperation.Create &&
            effect.Path == externalPath &&
            !effect.WithinWorkspace &&
            !effect.CoveredBySandbox);

        ExecuteCommandPermissionChoiceBuilder.Build(plan, [])
            .Select(choice => choice.Id)
            .Should().OnlyContain(id => !id.Contains("sandbox", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ChoiceBuilder_DoesNotOfferFilesystemOverlay_WhenRequestedSandboxAlreadyCoversPath()
    {
        var externalPath = $"/private/tmp/hpd-execute-command-permission-{Guid.NewGuid():N}.txt";
        var plan = Analyze(
            $"touch {externalPath}",
            sandboxPolicy: new AgentSandboxConfiguration
            {
                Filesystem = [new AgentSandboxPathGrant { Access = AgentSandboxPathAccess.Write, Path = externalPath }]
            });

        plan.FilesystemEffects.Should().ContainSingle(effect =>
            effect.Path == externalPath &&
            effect.CoveredBySandbox);
        ExecuteCommandPermissionChoiceBuilder.Build(plan, [])
            .Select(choice => choice.Id)
            .Should().OnlyContain(id => !id.Contains("sandbox", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyzer_MarksFilesystemEffectCovered_WhenGrantCoversParentDirectory()
    {
        var externalRoot = Path.Combine(Path.GetTempPath(), $"hpd-execute-command-permission-{Guid.NewGuid():N}");
        var externalPath = Path.Combine(externalRoot, "nested", "out.txt");
        var plan = Analyze(
            $"touch {externalPath}",
            sandboxPolicy: new AgentSandboxConfiguration
            {
                Filesystem = [new AgentSandboxPathGrant { Access = AgentSandboxPathAccess.Write, Path = externalRoot }]
            });

        plan.FilesystemEffects.Should().ContainSingle(effect =>
            effect.Path == externalPath &&
            effect.CoveredBySandbox);
        ExecuteCommandPermissionChoiceBuilder.Build(plan, [])
            .Select(choice => choice.Id)
            .Should().OnlyContain(id => !id.Contains("sandbox", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ChoiceBuilder_DoesNotOfferSandboxOverlay_WhenNetworkIsBlocked()
    {
        var plan = Analyze("npm install");

        plan.NetworkEffects.Should().ContainSingle(effect => !effect.CoveredBySandbox);
        ExecuteCommandPermissionChoiceBuilder.Build(plan, [])
            .Select(choice => choice.Id)
            .Should().OnlyContain(id => !id.Contains("sandbox", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RuleMatcher_MatchesPersistedRuleAgainstRequestedPolicyShape()
    {
        var plan = Analyze("git status -sb");
        var rule = CreateRule(plan, ExecuteCommandPermissionBehavior.Allow, ExecuteCommandPermissionMatchKind.Prefix, "git status");

        var match = ExecuteCommandPermissionRuleMatcher.Match(plan, [rule]);

        match.Decision.Should().Be(rule);
        match.Decision!.RequestedSandboxFingerprint.Should()
            .Be(plan.RequestedSandbox.Canonicalize(plan.WorkingDirectory));
    }

    [Fact]
    public void RuleMatcher_IgnoresObsoletePersistedRuleVersions()
    {
        var plan = Analyze("git status -sb");
        var staleRule = CreateRule(plan, ExecuteCommandPermissionBehavior.Allow, ExecuteCommandPermissionMatchKind.Prefix, "git status")
            with
            {
                RuleSchemaVersion = ExecuteCommandPermissionAnalyzerVersions.RuleSchema - 1,
                AnalyzerVersion = ExecuteCommandPermissionAnalyzerVersions.Analyzer - 1,
                NormalizationVersion = ExecuteCommandPermissionAnalyzerVersions.Normalization - 1
            };

        var match = ExecuteCommandPermissionRuleMatcher.Match(plan, [staleRule]);

        match.Decision.Should().BeNull();
        match.Diagnostics.InactiveRules.Should().Contain(rule =>
            rule.RuleId == staleRule.Id &&
            rule.Reason == "rule_schema_version_mismatch");
    }

    [Theory]
    [InlineData("echo ok \\; rm -rf target")]
    [InlineData("echo hi\rwhoami")]
    [InlineData("git\u00A0status")]
    [InlineData("echo foo#bar")]
    [InlineData("echo {a,b}")]
    public void Analyzer_ParserDifferentialCases_AreUntrustedAndDoNotPersist(string command)
    {
        var plan = Analyze(command);

        plan.TrustLevel.Should().Be(ExecuteCommandAnalysisTrustLevel.Untrusted);
        plan.Risk.Should().HaveFlag(ExecuteCommandPermissionRisk.ParserDifferentialRisk);
        ExecuteCommandPermissionChoiceBuilder.Build(plan, [])
            .OfType<PersistRuleChoice>()
            .Should().BeEmpty();
    }

    [Fact]
    public void Analyzer_BareVariableExpansion_IsReviewOnlyAndDoesNotPersist()
    {
        var plan = Analyze("$COMMAND --version");

        plan.TrustLevel.Should().Be(ExecuteCommandAnalysisTrustLevel.ReviewOnly);
        plan.Risk.Should().HaveFlag(ExecuteCommandPermissionRisk.BareVariableExpansion);
        ExecuteCommandPermissionChoiceBuilder.Build(plan, [])
            .OfType<PersistRuleChoice>()
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("timeout 10s git status -sb", "timeout 10s", "git status")]
    [InlineData("time -p git status -sb", "time -p", "git status")]
    [InlineData("nice -n 10 dotnet test", "nice -n 10", "dotnet test")]
    [InlineData("nohup dotnet build", "nohup", "dotnet build")]
    [InlineData("stdbuf -oL npm run build", "stdbuf -oL", "npm run build")]
    public void Analyzer_NormalizesKnownSafeWrappers(string command, string expectedWrapper, string expectedPrefix)
    {
        var plan = Analyze(command);

        var simple = plan.Should().BeOfType<SimpleCommandPermissionPlan>().Subject;
        simple.CommandPlan.NormalizedWrappers.Should().Contain(expectedWrapper);
        simple.CommandPlan.SafePrefix.Should().Be(expectedPrefix);
        simple.PrefixAllowRule.Should().NotBeNull();
    }

    [Theory]
    [InlineData("timeout --bad 10s git status")]
    [InlineData("time -v git status")]
    [InlineData("nice --adjustment=10 git status")]
    [InlineData("stdbuf --output=L git status")]
    [InlineData("env FOO=bar git status")]
    [InlineData("xargs git status")]
    public void Analyzer_UnknownOrAuthorityChangingWrappers_DoNotPersist(string command)
    {
        var plan = Analyze(command);

        plan.TrustLevel.Should().Be(ExecuteCommandAnalysisTrustLevel.ReviewOnly);
        plan.Risk.Should().HaveFlag(ExecuteCommandPermissionRisk.UnknownWrapper);
        ExecuteCommandPermissionChoiceBuilder.Build(plan, [])
            .OfType<PersistRuleChoice>()
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData(ExecuteCommandShellFamily.PowerShell, "Get-ChildItem | Out-File result.txt")]
    [InlineData(ExecuteCommandShellFamily.PowerShell, "powershell -EncodedCommand SQBFAFgA")]
    [InlineData(ExecuteCommandShellFamily.Cmd, "cmd /c dir > out.txt")]
    [InlineData(ExecuteCommandShellFamily.Cmd, "for %f in (*) do echo %f")]
    public void Analyzer_NonPosixShellFamilies_DoNotUsePosixPersistenceHeuristics(
        ExecuteCommandShellFamily family,
        string command)
    {
        var plan = Analyze(command, family);

        plan.TrustLevel.Should().Be(ExecuteCommandAnalysisTrustLevel.ReviewOnly);
        plan.Shell.Family.Should().Be(family);
        plan.ShellAnalyzerName.Should().Be(family switch
        {
            ExecuteCommandShellFamily.PowerShell => "PowerShellShellFamilyAnalyzer",
            ExecuteCommandShellFamily.Cmd => "CmdShellFamilyAnalyzer",
            _ => throw new InvalidOperationException("Unexpected shell family.")
        });
        plan.ShellUnsupportedFeatureReason.Should().NotBeNullOrWhiteSpace();
        ExecuteCommandPermissionChoiceBuilder.Build(plan, [])
            .OfType<PersistRuleChoice>()
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData(ExecuteCommandShellFamily.Zsh)]
    [InlineData(ExecuteCommandShellFamily.Bash)]
    [InlineData(ExecuteCommandShellFamily.Sh)]
    public void Analyzer_PosixShellFamilies_UsePosixAdapter(ExecuteCommandShellFamily family)
    {
        var plan = Analyze("git status -sb", family);

        plan.TrustLevel.Should().Be(ExecuteCommandAnalysisTrustLevel.Simple);
        plan.ShellAnalyzerName.Should().Be("PosixShellFamilyAnalyzer");
        plan.ShellUnsupportedFeatureReason.Should().BeNull();
    }

    [Theory]
    [InlineData("echo $(whoami)", ExecuteCommandUnsupportedShellFeature.CommandSubstitution, ExecuteCommandShellFamily.Zsh)]
    [InlineData("cat <<EOF\nhi\nEOF", ExecuteCommandUnsupportedShellFeature.Heredoc, ExecuteCommandShellFamily.Zsh)]
    [InlineData("echo $TARGET", ExecuteCommandUnsupportedShellFeature.BareVariableExpansion, ExecuteCommandShellFamily.Zsh)]
    [InlineData("echo ok > $OUT", ExecuteCommandUnsupportedShellFeature.UnsafeRedirectionTarget, ExecuteCommandShellFamily.Zsh)]
    [InlineData("echo {a,b}", ExecuteCommandUnsupportedShellFeature.BraceExpansion, ExecuteCommandShellFamily.Zsh)]
    [InlineData("& { Get-ChildItem }", ExecuteCommandUnsupportedShellFeature.ScriptBlockInvocation, ExecuteCommandShellFamily.PowerShell)]
    [InlineData("build.cmd", ExecuteCommandUnsupportedShellFeature.CmdBatchDispatch, ExecuteCommandShellFamily.Cmd)]
    public void Analyzer_ReportsTypedUnsupportedShellFeatures(
        string command,
        ExecuteCommandUnsupportedShellFeature expectedFeature,
        ExecuteCommandShellFamily family)
    {
        var plan = Analyze(command, family);

        plan.UnsupportedShellFeatures.Should().Contain(expectedFeature);
    }

    [Fact]
    public void PermissionParityChecklist_TracksMandatoryProposalGates()
    {
        var entries = ExecuteCommandPermissionParityChecklist.Entries;

        entries.Select(entry => entry.Id).Should().BeEquivalentTo([
            "safe-env-allow",
            "broad-env-deny-ask",
            "wrapper-normalization",
            "banned-prefixes",
            "prefix-compound-rejection",
            "too-complex-parser-handling",
            "parser-differential-corpus",
            "original-redirection-validation",
            "path-sensitive-extraction",
            "sed-dangerous-forms",
            "jq-dangerous-forms",
            "directory-change-risk",
            "sandbox-deny-ask-precedence",
            "segment-fanout-cap",
            "powershell-cmd-family-adapters",
            "suggestion-arity",
            "external-workspace-overlays"
        ]);
        entries.Should().OnlyContain(entry => !string.IsNullOrWhiteSpace(entry.Gate));
        entries.Should().OnlyContain(entry => !string.IsNullOrWhiteSpace(entry.RequiredTests));
    }

    [Fact]
    public void PermissionParityChecklist_DoesNotEnablePersistenceWithoutCorpusCoverage()
    {
        ExecuteCommandPermissionParityChecklist.Entries
            .Where(entry => entry.Status == ExecuteCommandPermissionParityStatus.PersistenceEnabled)
            .All(entry => entry.Status >= ExecuteCommandPermissionParityStatus.CorpusCovered)
            .Should().BeTrue();
    }

    [Fact]
    public async Task Middleware_PromptsForUnknownRunCommand()
    {
        var middleware = new ExecuteCommandPermissionMiddleware();
        var coordinator = new EventCoordinator();
        var requests = new List<ExecuteCommandPermissionRequestEvent>();
        using var subscription = RespondToPermissionRequests(coordinator, requests, _ => "allow_once");
        var agentContext = CreateAgentContext(coordinator);
        var context = CreateBeforeFunctionContext(agentContext, "custom-tool inspect workspace");

        await middleware.BeforeFunctionAsync(context, CancellationToken.None);

        requests.Should().ContainSingle();
        requests[0].Plan.Command.Value.Should().Be("custom-tool inspect workspace");
        context.BlockExecution.Should().BeFalse();
    }

    [Fact]
    public async Task Middleware_FullAccess_BypassesExecuteCommandPrompt()
    {
        var middleware = new ExecuteCommandPermissionMiddleware();
        var coordinator = new EventCoordinator();
        var requests = new List<ExecuteCommandPermissionRequestEvent>();
        using var subscription = RespondToPermissionRequests(coordinator, requests, _ => "deny");
        var agentContext = CreateAgentContext(coordinator);
        var runConfig = CreateWorkspaceRunConfig();
        runConfig.Security = new AgentSecurityProfile
        {
            Approval = AgentApprovalPolicy.AutoApprove
        };
        var context = CreateBeforeFunctionContext(
            agentContext,
            "custom-tool inspect workspace",
            runConfig: runConfig);

        await middleware.BeforeFunctionAsync(context, CancellationToken.None);

        requests.Should().BeEmpty();
        context.BlockExecution.Should().BeFalse();
    }

    [Fact]
    public async Task Middleware_InvalidExecuteCommandArguments_ReturnsInvalidArgumentsNotPermissionDenied()
    {
        var middleware = new ExecuteCommandPermissionMiddleware();
        var agentContext = CreateAgentContext(new EventCoordinator());
        var context = CreateBeforeFunctionContext(agentContext, "");

        await middleware.BeforeFunctionAsync(context, CancellationToken.None);

        context.BlockExecution.Should().BeTrue();
        context.OverrideResult.Should().BeOfType<string>()
            .Which.Should().Contain("<execute_command_error kind=\"invalid_arguments\"");
        context.OverrideResult.Should().BeOfType<string>()
            .Which.Should().NotContain("<execute_command_permission_denied");
        context.OverrideResult.ToString().Should().Contain("Run requires command.");
    }

    [Fact]
    public async Task Middleware_AllowOnce_DoesNotPersistRule()
    {
        var middleware = new ExecuteCommandPermissionMiddleware();
        var coordinator = new EventCoordinator();
        using var subscription = RespondToPermissionRequests(coordinator, [], _ => "allow_once");
        var agentContext = CreateAgentContext(coordinator);
        var context = CreateBeforeFunctionContext(agentContext, "git status -sb");

        await middleware.BeforeFunctionAsync(context, CancellationToken.None);

        context.BlockExecution.Should().BeFalse();
        context.GetMiddlewareState<ExecuteCommandPermissionStateData>()?.Rules.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task Middleware_AlwaysAllowExact_PersistsExactRule()
    {
        var middleware = new ExecuteCommandPermissionMiddleware();
        var coordinator = new EventCoordinator();
        using var subscription = RespondToPermissionRequests(coordinator, [], _ => "allow_exact");
        var agentContext = CreateAgentContext(coordinator);
        var context = CreateBeforeFunctionContext(agentContext, "git status -sb");

        await middleware.BeforeFunctionAsync(context, CancellationToken.None);

        var rule = context.GetMiddlewareState<ExecuteCommandPermissionStateData>()!
            .Rules.Should().ContainSingle().Subject;
        rule.Behavior.Should().Be(ExecuteCommandPermissionBehavior.Allow);
        rule.MatchKind.Should().Be(ExecuteCommandPermissionMatchKind.Exact);
        rule.Pattern.Should().Be("git status -sb");
    }

    [Fact]
    public async Task Middleware_AlwaysAllowSimilar_PersistsSafePrefixRule()
    {
        var middleware = new ExecuteCommandPermissionMiddleware();
        var coordinator = new EventCoordinator();
        using var subscription = RespondToPermissionRequests(coordinator, [], _ => "allow_similar");
        var agentContext = CreateAgentContext(coordinator);
        var context = CreateBeforeFunctionContext(agentContext, "git status -sb");

        await middleware.BeforeFunctionAsync(context, CancellationToken.None);

        var rule = context.GetMiddlewareState<ExecuteCommandPermissionStateData>()!
            .Rules.Should().ContainSingle().Subject;
        rule.Behavior.Should().Be(ExecuteCommandPermissionBehavior.Allow);
        rule.MatchKind.Should().Be(ExecuteCommandPermissionMatchKind.Prefix);
        rule.Pattern.Should().Be("git status");
    }

    [Fact]
    public async Task Middleware_Deny_BlocksExecution()
    {
        var middleware = new ExecuteCommandPermissionMiddleware();
        var coordinator = new EventCoordinator();
        using var subscription = RespondToPermissionRequests(coordinator, [], _ => "deny");
        var interruptions = new List<InterruptionRequestEvent>();
        var agentContext = CreateAgentContext(coordinator, interruptions: interruptions);
        var context = CreateBeforeFunctionContext(agentContext, "git status -sb");

        await middleware.BeforeFunctionAsync(context, CancellationToken.None);

        context.BlockExecution.Should().BeTrue();
        context.OverrideResult.Should().BeOfType<string>()
            .Which.Should().Contain("<execute_command_permission_denied");
        interruptions.Should().ContainSingle()
            .Which.Should().Match<InterruptionRequestEvent>(evt =>
                evt.EventFlowId == "call-1" &&
                evt.Source == InterruptionSource.Middleware &&
                evt.Reason == "User denied ExecuteCommand.");
    }

    [Fact]
    public async Task Middleware_Feedback_BlocksExecutionAndReturnsGuidance()
    {
        var middleware = new ExecuteCommandPermissionMiddleware();
        var coordinator = new EventCoordinator();
        using var subscription = RespondToPermissionRequests(
            coordinator,
            [],
            _ => "feedback",
            "Use git status instead.");
        var interruptions = new List<InterruptionRequestEvent>();
        var agentContext = CreateAgentContext(coordinator, interruptions: interruptions);
        var context = CreateBeforeFunctionContext(agentContext, "custom-tool inspect workspace");

        await middleware.BeforeFunctionAsync(context, CancellationToken.None);

        context.BlockExecution.Should().BeTrue();
        context.OverrideResult.Should().BeOfType<string>()
            .Which.Should().Contain("Use git status instead.");
        interruptions.Should().BeEmpty();
    }

    [Fact]
    public async Task Middleware_PersistedAllowRule_SkipsPromptAndApprovesInvocation()
    {
        var middleware = new ExecuteCommandPermissionMiddleware();
        var coordinator = new EventCoordinator();
        var requests = new List<ExecuteCommandPermissionRequestEvent>();
        using var subscription = RespondToPermissionRequests(coordinator, requests, _ => "deny");
        var plan = Analyze("git status -sb");
        var rule = CreateRule(plan, ExecuteCommandPermissionBehavior.Allow, ExecuteCommandPermissionMatchKind.Prefix, "git status");
        var initialState = CreateState(new ExecuteCommandPermissionStateData().WithValidatedRule(rule));
        var agentContext = CreateAgentContext(coordinator, initialState);
        var context = CreateBeforeFunctionContext(agentContext, "git status -sb");

        await middleware.BeforeFunctionAsync(context, CancellationToken.None);

        requests.Should().BeEmpty();
        context.BlockExecution.Should().BeFalse();
    }

    [Fact]
    public async Task Middleware_ParallelBatch_KeysDecisionsByFingerprint()
    {
        var middleware = new ExecuteCommandPermissionMiddleware();
        var coordinator = new EventCoordinator();
        var requests = new List<ExecuteCommandPermissionRequestEvent>();
        using var subscription = RespondToPermissionRequests(
            coordinator,
            requests,
            request => request.Plan.Command.Value.StartsWith("rm ", StringComparison.Ordinal)
                ? "deny"
                : "allow_once");
        var agentContext = CreateAgentContext(coordinator);
        var batch = agentContext.AsBeforeParallelBatch(
            [
                MakeParallelFunctionInfo("git-call", "git status -sb"),
                MakeParallelFunctionInfo("rm-call", "rm -rf target")
            ],
            CreateWorkspaceRunConfig());

        await middleware.BeforeParallelBatchAsync(batch, CancellationToken.None);

        batch.GetMiddlewareState<ExecuteCommandBatchPermissionStateData>()!
            .DecisionsByFingerprint.Should().HaveCount(2);
        requests.Should().HaveCount(2);

        var gitContext = CreateBeforeFunctionContext(agentContext, "git status -sb", "git-call");
        var rmContext = CreateBeforeFunctionContext(agentContext, "rm -rf target", "rm-call");

        await middleware.BeforeFunctionAsync(gitContext, CancellationToken.None);
        await middleware.BeforeFunctionAsync(rmContext, CancellationToken.None);

        gitContext.BlockExecution.Should().BeFalse();
        rmContext.BlockExecution.Should().BeTrue();
        requests.Should().HaveCount(2);
    }

    private ExecuteCommandPermissionPlan Analyze(
        string command,
        ExecuteCommandShellFamily shellFamily = ExecuteCommandShellFamily.Zsh,
        AgentSandboxConfiguration? sandboxPolicy = null)
        => ExecuteCommandPermissionMiddleware.ExecuteCommandPermissionAnalyzer.Analyze(
            RunArguments(command),
            CreateWorkspaceRunConfig(sandboxPolicy),
            new ExecuteCommandOptions(),
            new ExecuteCommandShellScope
            {
                Executable = shellFamily switch
                {
                    ExecuteCommandShellFamily.PowerShell => "pwsh",
                    ExecuteCommandShellFamily.Cmd => "cmd.exe",
                    ExecuteCommandShellFamily.Bash => "bash",
                    ExecuteCommandShellFamily.Sh => "sh",
                    _ => "zsh"
                },
                Family = shellFamily
            });

    private static ExecuteCommandShellParseResult ParsePosix(string command)
        => ExecuteCommandShellAnalyzer.ParsePosix(
            new RawCommandText(command),
            ExecuteCommandShellFamily.Zsh);

    private ExecuteCommandPermissionRule CreateRule(
        ExecuteCommandPermissionPlan plan,
        ExecuteCommandPermissionBehavior behavior,
        ExecuteCommandPermissionMatchKind matchKind,
        string pattern)
        => new()
        {
            Id = $"rule_{Guid.NewGuid():N}",
            RuleSchemaVersion = ExecuteCommandPermissionAnalyzerVersions.RuleSchema,
            AnalyzerVersion = ExecuteCommandPermissionAnalyzerVersions.Analyzer,
            NormalizationVersion = ExecuteCommandPermissionAnalyzerVersions.Normalization,
            Behavior = behavior,
            MatchKind = matchKind,
            Pattern = pattern,
            Shell = plan.Shell,
            RequestedSandboxFingerprint = plan.RequestedSandbox.Canonicalize(plan.WorkingDirectory),
            Workspace = plan.Workspace,
            Risk = plan.Risk,
            MinimumTrustLevel = plan.TrustLevel
        };

    private static AgentLoopState CreateState(ExecuteCommandPermissionStateData? permissionState = null)
    {
        var state = AgentLoopState.InitialSafe(
            messages: [],
            runId: "test-run",
            conversationId: "test-conversation",
            agentName: "TestAgent");

        if (permissionState is null)
            return state;

        var key = typeof(ExecuteCommandPermissionStateData).FullName
            ?? throw new InvalidOperationException("ExecuteCommandPermissionStateData must have a full name.");
        return state with
        {
            MiddlewareState = state.MiddlewareState.SetState(key, permissionState)
        };
    }

    private static AgentContext CreateAgentContext(
        EventCoordinator coordinator,
        AgentLoopState? state = null,
        List<InterruptionRequestEvent>? interruptions = null)
    {
        Func<AgentInputEvent, CancellationToken, ValueTask>? inputHandler = interruptions is null
            ? null
            : (input, _) =>
            {
                if (input is InterruptionRequestEvent interruption)
                    interruptions.Add(interruption);
                return ValueTask.CompletedTask;
            };

        return new(
            "TestAgent",
            "test-conversation",
            state ?? CreateState(),
            coordinator,
            new Session("test-session"),
            new Thread("test-session", "test-agent") { Id = "test-thread" },
            CancellationToken.None,
            inputHandler: inputHandler);
    }

    private BeforeFunctionContext CreateBeforeFunctionContext(
        AgentContext agentContext,
        string command,
        string callId = "call-1",
        AgentRunConfig? runConfig = null)
        => agentContext.AsBeforeFunction(
            ExecuteCommandFunction(),
            callId,
            RunArguments(command),
            runConfig ?? CreateWorkspaceRunConfig(),
            toolharnessName: nameof(CodingToolHarness),
            skillName: null);

    private static AIFunction ExecuteCommandFunction()
        => AIFunctionFactory.Create(() => "ok", name: nameof(CodingToolHarness.ExecuteCommand));

    private static ParallelFunctionInfo MakeParallelFunctionInfo(string callId, string command)
        => new(
            ExecuteCommandFunction(),
            callId,
            RunArguments(command));

    private static IReadOnlyDictionary<string, object?> RunArguments(string command)
        => new Dictionary<string, object?>
        {
            ["request"] = new Dictionary<string, object?>
            {
                ["action"] = "run",
                ["command"] = command
            }
        };

    private static IDisposable RespondToPermissionRequests(
        EventCoordinator coordinator,
        List<ExecuteCommandPermissionRequestEvent> requests,
        Func<ExecuteCommandPermissionRequestEvent, string> choiceSelector,
        string? feedbackText = null)
        => coordinator.Subscribe<ExecuteCommandPermissionRequestEvent>(request =>
        {
            lock (requests)
            {
                requests.Add(request);
            }

            var choiceId = choiceSelector(request);
            var result = coordinator.Respond(new ExecuteCommandPermissionResponseEvent(
                request.PermissionId,
                request.SourceName,
                choiceId,
                feedbackText));
            result.Accepted.Should().BeTrue(result.Message);
            return ValueTask.CompletedTask;
        });

    private AgentRunConfig CreateWorkspaceRunConfig(AgentSandboxConfiguration? sandboxPolicy = null)
    {
        var overrides = new Dictionary<string, object>
        {
            [AgentWorkspace.ContextKey] = new AgentWorkspace(
                "default",
                _tempRoot,
                [new AgentWorkspaceRoot("default", _tempRoot)])
        };
        return new AgentRunConfig
        {
            ContextOverrides = overrides,
            Sandbox = sandboxPolicy ?? new AgentSandboxConfiguration()
        };
    }
}
