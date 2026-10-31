using HPD.Agent.Middleware;
using HPD.Agent.Security;
using HPD.Agent.ToolHarness.Coding.Security;
using HPD.Events.Core;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.ToolHarness.Coding.Tests.Workspace;

public sealed class WorkspaceAccessTests : IDisposable
{
    private readonly string _root = AgentFilesystemPath.Canonicalize(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
    private string Evidence => Path.Combine(_root, "evidence");
    private string Outputs => Path.Combine(_root, "outputs");
    public WorkspaceAccessTests() { Directory.CreateDirectory(Evidence); Directory.CreateDirectory(Outputs); }
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public async Task EvidenceReadsAndOutputWritesWorkButEvidenceWritesAreDenied()
    {
        var evidence = Path.Combine(Evidence, "original.txt"); await File.WriteAllTextAsync(evidence, "original");
        var context = Context(Config());
        var authorizedRead = await AgentFilesystemAccess.AuthorizeReadAsync(evidence, "read", context, default);
        Assert.Equal("original", await File.ReadAllTextAsync(authorizedRead));
        await Assert.ThrowsAsync<AgentCapabilityDeniedException>(async () => await AgentFilesystemAccess.AuthorizeWriteAsync(evidence, "write", context, default));
        var authorizedOutput = await AgentFilesystemAccess.AuthorizeWriteAsync(Path.Combine(Outputs, "report.txt"), "write", context, default);
        await File.WriteAllTextAsync(authorizedOutput, "report");
        Assert.Equal("report", await File.ReadAllTextAsync(authorizedOutput));
        Assert.Equal("original", await File.ReadAllTextAsync(evidence));
    }

    [Fact]
    public void NativePolicyContainsNoEvidenceWriteGrantAndEscalationCannotAddOne()
    {
        var runtime = CodingSandboxRuntime.Capture(Config());
        Assert.DoesNotContain(runtime.Filesystem, grant => grant.Access == AgentSandboxPathAccess.Write && AgentFilesystemPath.Contains(grant.Path, Evidence));
        var policy = runtime.ToProcessIsolationPolicy(Outputs);
        Assert.Equal(runtime.Filesystem.Count, policy.Filesystem.Rules.Count);
        Assert.Throws<AgentRunConfigurationException>(() => runtime.WithPathGrant(AgentSandboxPathAccess.Write, Evidence));
        Assert.Throws<AgentRunConfigurationException>(() => runtime.WithPathGrant(AgentSandboxPathAccess.Write, _root));
    }

    [Fact]
    public void BroadWriteGrantAndDisabledIsolationCannotDefeatReadOnly()
    {
        var config = Config();
        config.Security = new() { Sandbox = new() { Capabilities = new() { Filesystem = [new() { Access = AgentSandboxPathAccess.Write, Path = _root }] } } };
        Assert.Throws<InvalidOperationException>(() => CodingSandboxRuntime.Capture(config));
        config.Security = new() { Sandbox = new() { Mode = AgentSandboxPolicy.Disabled } };
        Assert.Throws<InvalidOperationException>(() => CodingSandboxRuntime.Capture(config));
    }

    [Fact]
    public void HostCeilingRejectsWorkspaceThatRequestsBroaderAccess()
    {
        var config = Config();
        config.Security = new() { CapabilityCeiling = new() { Filesystem = [new() { Access = AgentSandboxPathAccess.Read, Path = _root }] } };
        Assert.Throws<AgentRunConfigurationException>(() => CodingSandboxRuntime.Capture(config));
    }

    [Fact]
    public void ReadOnlyChildUnderWritableParentIsRejected()
    {
        var config = Config();
        config.Context!.Properties![AgentWorkspace.ContextKey] = new AgentWorkspace("all", _root,
            [new("all", _root, AgentWorkspaceAccess.ReadWrite), new("evidence", Evidence, AgentWorkspaceAccess.ReadOnly)]);
        Assert.Throws<AgentWorkspaceException>(() => AgentWorkspace.From(config));
        Assert.Throws<AgentWorkspaceException>(() => CodingSandboxRuntime.Capture(config));
    }

    [Fact]
    public void WritableChildUnderReadOnlyParentIsRejected()
    {
        var writableChild = Path.Combine(Evidence, "generated");
        Directory.CreateDirectory(writableChild);
        var config = Config();
        config.Context!.Properties![AgentWorkspace.ContextKey] = new AgentWorkspace("evidence", Evidence,
            [
                new("evidence", Evidence, AgentWorkspaceAccess.ReadOnly),
                new("generated", writableChild, AgentWorkspaceAccess.ReadWrite)
            ]);
        Assert.Throws<AgentWorkspaceException>(() => AgentWorkspace.From(config));
        Assert.Throws<AgentWorkspaceException>(() => CodingSandboxRuntime.Capture(config));
    }

    [Fact]
    public async Task SymlinksCannotTurnAnEvidencePathIntoAWritableAlias()
    {
        var target = Path.Combine(Outputs, "generated.txt"); await File.WriteAllTextAsync(target, "original");
        var alias = Path.Combine(Evidence, "alias.txt"); File.CreateSymbolicLink(alias, target);
        await Assert.ThrowsAsync<AgentCapabilityDeniedException>(async () => await AgentFilesystemAccess.AuthorizeWriteAsync(alias, "write", Context(Config()), default));
    }

    [Fact]
    public void SerializedRootsRequireExplicitAccess()
    {
        using var document = System.Text.Json.JsonDocument.Parse("{\"version\":1,\"defaultRootId\":\"evidence\",\"roots\":[{\"id\":\"evidence\",\"path\":" + System.Text.Json.JsonSerializer.Serialize(Evidence) + "}]}");
        var config = Config(); config.Context!.Properties![AgentWorkspace.ContextKey] = document.RootElement.Clone();
        Assert.Throws<AgentWorkspaceException>(() => AgentWorkspace.From(config));
    }

    private AgentRunConfig Config() => new()
    {
        Context = new() { Properties = new Dictionary<string, object>
        {
            [AgentWorkspace.ContextKey] = new AgentWorkspace("outputs", Outputs,
                [new("evidence", Evidence, AgentWorkspaceAccess.ReadOnly), new("outputs", Outputs, AgentWorkspaceAccess.ReadWrite)])
        } }
    };
    private static FunctionExecutionContext Context(AgentRunConfig config)
    {
        var state = AgentLoopState.InitialSafe([], "test-run", "test-thread", "test-agent");
        var agentContext = new AgentContext("test-agent", "test-thread", state, new EventCoordinator(), new Session("test-session"),
            new HPD.Agent.Thread("test-session", "test-thread"), CancellationToken.None);
        var before = agentContext.AsBeforeFunction(null, "test-call", new Dictionary<string, object?>(), config, "CodingToolHarness");
        return new FunctionExecutionContext(before, new FunctionRequest
        {
            Function = AIFunctionFactory.Create(() => "test", "test"), CallId = before.FunctionCallId,
            Arguments = new Dictionary<string, object?>(), State = state, RunConfig = config, EventCoordinator = agentContext.EventCoordinator
        });
    }
}
