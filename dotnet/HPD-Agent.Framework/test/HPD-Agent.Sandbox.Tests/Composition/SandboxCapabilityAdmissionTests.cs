using HPD.Agent.Sandbox;
using HPD.Environment.Contracts;
using Xunit;
using EnvironmentCapabilityId = HPD.Environment.Contracts.CapabilityId;

namespace HPD.Agent.Sandbox.Core.Tests.Composition;

public sealed class SandboxCapabilityAdmissionTests
{
    private static readonly ProviderId TestProvider =
        new("hpd.execution.test");

    [Fact]
    public void Missing_required_capability_fails_closed()
    {
        SandboxSessionRequest request = Request();
        SandboxBackendCapabilities capabilities = Capabilities(
            required: [],
            requested: []);

        SandboxCapabilityAssessment assessment =
            SandboxCapabilityAdmission.Assess(
                capabilities,
                SandboxCapabilityAdmission.RequiredFor(request));

        Assert.False(assessment.IsAdmitted);
        Assert.NotEmpty(assessment.Issues);
        Assert.Contains(
            assessment.Issues,
            issue =>
                issue.Capability ==
                StandardSandboxEnvironmentCapabilities.ProcessExecution);
    }

    [Fact]
    public void Matching_provider_and_sandbox_facts_are_admitted()
    {
        SandboxSessionRequest request = Request();
        IReadOnlyList<EnvironmentCapabilityId> required =
            SandboxCapabilityAdmission.RequiredFor(request);
        SandboxBackendCapabilities capabilities = Capabilities(
            required,
            required);

        SandboxCapabilityAssessment assessment =
            SandboxCapabilityAdmission.Assess(
                capabilities,
                required);

        Assert.True(
            assessment.IsAdmitted,
            string.Join(
                " | ",
                assessment.Issues.Select(issue =>
                    $"{issue.Capability.Value}: {issue.Reason}")));
    }

    [Fact]
    public void Sandbox_fact_must_agree_with_provider_report()
    {
        EnvironmentCapabilityId capability =
            StandardSandboxEnvironmentCapabilities.ProcessExecution;
        ProviderCapabilityReport report = new()
        {
            ProviderId = TestProvider,
            Capabilities =
            [
                Fact(capability, CapabilityState.Unsupported),
            ],
        };
        SandboxBackendCapabilities capabilities = new(
            report,
            [
                new SandboxCapabilityFact(
                    Fact(capability, CapabilityState.Supported),
                    SandboxEnforcementLocation.Host),
            ]);

        SandboxCapabilityAssessment assessment =
            SandboxCapabilityAdmission.Assess(
                capabilities,
                [capability]);

        Assert.False(assessment.IsAdmitted);
        Assert.Contains(
            assessment.Issues,
            issue => issue.Reason.Contains(
                "disagrees",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Degraded_capability_requires_explicit_request_permission()
    {
        EnvironmentCapabilityId capability =
            StandardSandboxEnvironmentCapabilities.ProcessExecution;
        SandboxBackendCapabilities capabilities = Capabilities(
            [capability],
            [capability],
            CapabilityState.Degraded);

        SandboxCapabilityAssessment failClosed =
            SandboxCapabilityAdmission.Assess(
                capabilities,
                [capability]);
        SandboxCapabilityAssessment explicitlyAllowed =
            SandboxCapabilityAdmission.Assess(
                capabilities,
                [capability],
                allowDegradation: true);

        Assert.False(failClosed.IsAdmitted);
        Assert.True(explicitlyAllowed.IsAdmitted);
    }

    [Fact]
    public void Workspace_request_requires_content_projection()
    {
        WorkspaceRootBinding root = new()
        {
            Id = new WorkspaceRootId("workspace"),
            DisplayName = "Workspace",
            DefaultExecutionPath = new WorkspacePath(
                new WorkspaceRootId("workspace"),
                string.Empty),
            Content = new WorkspaceContentIdentity("content-1"),
            PathSemantics = new WorkspacePathSemantics(
                CaseSensitive: true,
                SymlinkEvaluationPolicy.DenySymlinks),
        };
        SandboxSessionRequest request = Request() with
        {
            Workspaces =
            [
                new SandboxWorkspaceRequest
                {
                    Source = new ContentSelector
                    {
                        Kind = ContentSelectorKind.HostPath,
                        HostPath = new HostPathSelection(
                            new HostPath("/workspace"),
                            HostPathKind.Directory,
                            RequireExists: false),
                    },
                    Root = root,
                    ExecutionPath =
                        new WorkspaceExecutionPath("/workspace"),
                },
            ],
        };

        IReadOnlyList<EnvironmentCapabilityId> required =
            SandboxCapabilityAdmission.RequiredFor(request);

        Assert.Contains(
            StandardSandboxEnvironmentCapabilities.ContentProjection,
            required);
    }

    private static SandboxSessionRequest Request() =>
        new()
        {
            Isolation = ProcessIsolationPolicy.Default,
        };

    private static SandboxBackendCapabilities Capabilities(
        IReadOnlyList<EnvironmentCapabilityId> required,
        IReadOnlyList<EnvironmentCapabilityId> requested,
        CapabilityState state = CapabilityState.Supported)
    {
        HashSet<EnvironmentCapabilityId> all = [.. required, .. requested];
        List<CapabilityFact> providerFacts = all
            .Select(capability => Fact(capability, state))
            .ToList();
        ProviderCapabilityReport report = new()
        {
            ProviderId = TestProvider,
            Capabilities = providerFacts,
        };

        return new SandboxBackendCapabilities(
            report,
            providerFacts
                .Select(fact => new SandboxCapabilityFact(
                    fact,
                    SandboxEnforcementLocation.Provider))
                .ToArray());
    }

    private static CapabilityFact Fact(
        EnvironmentCapabilityId capability,
        CapabilityState state) =>
        new()
        {
            Id = capability,
            Category = new CapabilityCategory("sandbox"),
            AppliesTo = ProviderContractKind.ProcessInvocation,
            State = state,
            Detail = "Test capability.",
        };
}
