namespace HPD.Execution.AppleVirtualization.Tests;

using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Xunit;

public sealed class AppleVirtualizationGuestAgentAuthorityStdioTests
{
    [Fact]
    public async Task Guest_agent_authority_bind_status_and_revoke_emit_evidence_conditions_and_audit()
    {
        using TemporaryAuthorityWorkspace workspace = TemporaryAuthorityWorkspace.Create();
        File.WriteAllText(workspace.SourceSocket, string.Empty);

        JsonDocument[] responses = await RunGuestAgentAsync(
            new Dictionary<string, string?>
            {
                ["HPD_GUEST_AGENT_ENGINE_SOCKET"] = workspace.SourceSocket,
            },
            AuthorityRequest(44, "bind-1", 1, workspace.TargetSocket),
            AuthorityRequest(45, "status-1", 2, workspace.TargetSocket),
            AuthorityRequest(46, "revoke-1", 3, workspace.TargetSocket));

        Authority(responses[0]).GetProperty("BindingPhase").GetInt32().Should().Be(2);
        Authority(responses[0]).GetProperty("Conditions")[0].GetProperty("Type").GetString()
            .Should().Be("AppleVirtualization.GuestAgentAuthorityProjected");
        Authority(responses[0]).GetProperty("RevocationEvidence")[0].GetProperty("Kind").GetInt32()
            .Should().Be(6);
        Authority(responses[0]).GetProperty("AuditEvents")[0].GetProperty("Kind").GetInt32()
            .Should().Be(0);

        Authority(responses[1]).GetProperty("BindingPhase").GetInt32().Should().Be(2);
        Authority(responses[1]).GetProperty("AuditEvents")[0].GetProperty("Kind").GetInt32()
            .Should().Be(3);

        Authority(responses[2]).GetProperty("BindingPhase").GetInt32().Should().Be(5);
        Authority(responses[2]).GetProperty("RevocationStatus").GetInt32().Should().Be(2);
        Authority(responses[2]).GetProperty("RevocationEvidence").EnumerateArray()
            .Select(evidence => evidence.GetProperty("Kind").GetInt32())
            .Should().Contain([6, 3]);
        Authority(responses[2]).GetProperty("AuditEvents")[0].GetProperty("Kind").GetInt32()
            .Should().Be(5);
        File.Exists(workspace.TargetSocket).Should().BeFalse();
    }

    [Theory]
    [InlineData("missing", "AppleVirtualization.GuestAgentAuthorityTargetMissing", 3, 2)]
    [InlineData("unmanaged", "AppleVirtualization.GuestAgentAuthorityTargetUnmanaged", 3, 3)]
    [InlineData("wrong-symlink", "AppleVirtualization.GuestAgentAuthorityWrongTarget", 3, 3)]
    public async Task Guest_agent_authority_status_distinguishes_degraded_projection_states(
        string mode,
        string expectedCondition,
        int expectedPhase,
        int expectedRevocationStatus)
    {
        using TemporaryAuthorityWorkspace workspace = TemporaryAuthorityWorkspace.Create();
        File.WriteAllText(workspace.SourceSocket, string.Empty);

        if (mode == "unmanaged")
        {
            File.WriteAllText(workspace.TargetSocket, string.Empty);
        }
        else if (mode == "wrong-symlink")
        {
            string wrongSource = Path.Combine(workspace.Root, "src", "wrong.sock");
            File.WriteAllText(wrongSource, string.Empty);
            File.CreateSymbolicLink(workspace.TargetSocket, wrongSource);
        }

        JsonDocument[] responses = await RunGuestAgentAsync(
            new Dictionary<string, string?>
            {
                ["HPD_GUEST_AGENT_ENGINE_SOCKET"] = workspace.SourceSocket,
            },
            AuthorityRequest(45, "status-1", 1, workspace.TargetSocket));

        JsonElement authority = Authority(responses[0]);
        authority.GetProperty("BindingPhase").GetInt32().Should().Be(expectedPhase);
        authority.GetProperty("RevocationStatus").GetInt32().Should().Be(expectedRevocationStatus);
        authority.GetProperty("Conditions")[0].GetProperty("Type").GetString()
            .Should().Be(expectedCondition);
        authority.GetProperty("AuditEvents")[0].GetProperty("Kind").GetInt32()
            .Should().Be(7);
    }

    [Fact]
    public async Task Guest_agent_authority_bind_rejects_missing_source_socket_with_retryable_error()
    {
        using TemporaryAuthorityWorkspace workspace = TemporaryAuthorityWorkspace.Create();

        JsonDocument[] responses = await RunGuestAgentAsync(
            new Dictionary<string, string?>
            {
                ["HPD_GUEST_AGENT_ENGINE_SOCKET"] = workspace.SourceSocket,
            },
            AuthorityRequest(44, "bind-1", 1, workspace.TargetSocket));

        responses[0].RootElement.GetProperty("ResponseStatus").GetInt32().Should().Be(2);
        JsonElement error = responses[0].RootElement.GetProperty("Error");
        error.GetProperty("Code").GetString()
            .Should().Be("AppleVirtualization.GuestAgentAuthoritySourceSocketMissing");
        error.GetProperty("Retryable").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Guest_agent_authority_uses_containerd_source_for_containerd_projection()
    {
        using TemporaryAuthorityWorkspace workspace = TemporaryAuthorityWorkspace.Create("containerd.sock");
        File.WriteAllText(workspace.SourceSocket, string.Empty);

        JsonDocument[] responses = await RunGuestAgentAsync(
            new Dictionary<string, string?>
            {
                ["HPD_GUEST_AGENT_ENGINE_SOCKET"] = Path.Combine(workspace.Root, "src", "docker.sock"),
                ["HPD_GUEST_AGENT_CONTAINERD_SOCKET"] = workspace.SourceSocket,
            },
            AuthorityRequest(44, "bind-1", 1, workspace.TargetSocket),
            AuthorityRequest(45, "status-1", 2, workspace.TargetSocket));

        Authority(responses[0]).GetProperty("BindingPhase").GetInt32().Should().Be(2);
        File.ResolveLinkTarget(workspace.TargetSocket, returnFinalTarget: false)!.FullName
            .Should().Be(workspace.SourceSocket);
        Authority(responses[1]).GetProperty("BindingPhase").GetInt32().Should().Be(2);

        JsonDocument[] revoked = await RunGuestAgentAsync(
            new Dictionary<string, string?>
            {
                ["HPD_GUEST_AGENT_CONTAINERD_SOCKET"] = workspace.SourceSocket,
            },
            AuthorityRequest(46, "revoke-1", 3, workspace.TargetSocket));

        Authority(revoked[0]).GetProperty("BindingPhase").GetInt32().Should().Be(5);
        File.Exists(workspace.TargetSocket).Should().BeFalse();
    }

    [Fact]
    public async Task Guest_agent_authority_uses_podman_source_for_podman_projection()
    {
        using TemporaryAuthorityWorkspace workspace = TemporaryAuthorityWorkspace.Create("podman.sock");
        File.WriteAllText(workspace.SourceSocket, string.Empty);

        JsonDocument[] responses = await RunGuestAgentAsync(
            new Dictionary<string, string?>
            {
                ["HPD_GUEST_AGENT_ENGINE_SOCKET"] = Path.Combine(workspace.Root, "src", "docker.sock"),
                ["HPD_GUEST_AGENT_PODMAN_SOCKET"] = workspace.SourceSocket,
            },
            AuthorityRequest(44, "bind-1", 1, workspace.TargetSocket),
            AuthorityRequest(45, "status-1", 2, workspace.TargetSocket));

        Authority(responses[0]).GetProperty("BindingPhase").GetInt32().Should().Be(2);
        File.ResolveLinkTarget(workspace.TargetSocket, returnFinalTarget: false)!.FullName
            .Should().Be(workspace.SourceSocket);
        Authority(responses[1]).GetProperty("BindingPhase").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Guest_agent_authority_uses_rootful_podman_source_for_rootful_podman_projection()
    {
        using TemporaryAuthorityWorkspace workspace = TemporaryAuthorityWorkspace.Create("podman-rootful.sock");
        string rootlessSource = Path.Combine(workspace.Root, "src", "podman.sock");
        File.WriteAllText(workspace.SourceSocket, string.Empty);
        File.WriteAllText(rootlessSource, string.Empty);

        JsonDocument[] responses = await RunGuestAgentAsync(
            new Dictionary<string, string?>
            {
                ["HPD_GUEST_AGENT_ENGINE_SOCKET"] = Path.Combine(workspace.Root, "src", "docker.sock"),
                ["HPD_GUEST_AGENT_PODMAN_SOCKET"] = workspace.SourceSocket,
            },
            AuthorityRequest(44, "bind-1", 1, workspace.TargetSocket));

        Authority(responses[0]).GetProperty("BindingPhase").GetInt32().Should().Be(2);
        File.ResolveLinkTarget(workspace.TargetSocket, returnFinalTarget: false)!.FullName
            .Should().Be(workspace.SourceSocket);
    }

    [Fact]
    public async Task Guest_agent_authority_uses_rootful_buildkit_source_for_rootful_buildkit_projection()
    {
        using TemporaryAuthorityWorkspace workspace = TemporaryAuthorityWorkspace.Create("buildkitd-rootful.sock");
        File.WriteAllText(workspace.SourceSocket, string.Empty);

        JsonDocument[] responses = await RunGuestAgentAsync(
            new Dictionary<string, string?>
            {
                ["HPD_GUEST_AGENT_BUILDKIT_SOCKET"] = workspace.SourceSocket,
            },
            AuthorityRequest(44, "bind-1", 1, workspace.TargetSocket));

        Authority(responses[0]).GetProperty("BindingPhase").GetInt32().Should().Be(2);
        File.ResolveLinkTarget(workspace.TargetSocket, returnFinalTarget: false)!.FullName
            .Should().Be(workspace.SourceSocket);
    }

    private static JsonElement Authority(JsonDocument document) =>
        document.RootElement.GetProperty("AuthorityBindingResponse");

    private static string AuthorityRequest(int operation, string requestId, int sequence, string targetSocket)
    {
        var request = new
        {
            ProtocolVersion = "1.0",
            MessageType = 0,
            Operation = operation,
            RequestId = requestId,
            SequenceNumber = sequence,
            AuthorityBindingRequest = new
            {
                BindingId = "binding-1",
                Direction = 3,
                EffectiveAuthorityClass = 4,
                Redaction = 2,
                AuditCorrelationId = "audit-binding-1",
                Source = new
                {
                    Kind = 1,
                    SensitiveEndpointKind = 1,
                    RedactedDisplayName = "engine-socket:***",
                },
                Target = new
                {
                    Kind = 0,
                },
                Projection = new
                {
                    Kind = 0,
                    TargetSocketPath = new
                    {
                        Value = targetSocket,
                    },
                },
            },
        };

        return JsonSerializer.Serialize(request);
    }

    private static async Task<JsonDocument[]> RunGuestAgentAsync(
        IReadOnlyDictionary<string, string?> environment,
        params string[] requests)
    {
        string guestAgentPath = ResolveGuestAgentPath();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("python3", $"{guestAgentPath} --stdio")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        foreach ((string key, string? value) in environment)
        {
            process.StartInfo.Environment[key] = value;
        }

        process.Start().Should().BeTrue();
        foreach (string request in requests)
        {
            await process.StandardInput.WriteLineAsync(request);
        }

        process.StandardInput.Close();

        var output = new List<string>(requests.Length);
        for (int i = 0; i < requests.Length; i++)
        {
            string? line = await process.StandardOutput.ReadLineAsync();
            line.Should().NotBeNull();
            output.Add(line!);
        }

        string stderr = await process.StandardError.ReadToEndAsync();
        bool exited = process.WaitForExit(10_000);
        exited.Should().BeTrue("guest-agent stderr: {0}", stderr);
        process.ExitCode.Should().Be(0, "guest-agent stderr: {0}", stderr);

        return output.Select(line => JsonDocument.Parse(line)).ToArray();
    }

    private static string ResolveGuestAgentPath()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName,
                "HPD-AI-Framework",
                "dotnet",
                "HPD-Agent.Framework",
                "src",
                "HPD-Execution",
                "hpd-guest-agent",
                "src",
                "hpd_guest_agent.py");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate hpd_guest_agent.py from test output directory.");
    }

    private sealed class TemporaryAuthorityWorkspace : IDisposable
    {
        private TemporaryAuthorityWorkspace(string root, string socketName)
        {
            Root = root;
            Directory.CreateDirectory(Path.Combine(root, "src"));
            Directory.CreateDirectory(Path.Combine(root, "projection"));
            SourceSocket = Path.Combine(root, "src", socketName);
            TargetSocket = Path.Combine(root, "projection", socketName);
        }

        public string Root { get; }
        public string SourceSocket { get; }
        public string TargetSocket { get; }

        public static TemporaryAuthorityWorkspace Create(string socketName = "docker.sock") =>
            new(
                Path.Combine(Path.GetTempPath(), "hpd-guest-agent-authority-" + Guid.NewGuid().ToString("N")),
                socketName);

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
