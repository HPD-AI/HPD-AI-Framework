namespace HPD.Environment.AppleVirtualization.Tests;

using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Xunit;

public sealed class AppleVirtualizationGuestAgentAuthorityStdioTests
{
    [Fact]
    public async Task Guest_agent_isolated_process_applies_environment_policy_when_no_other_features_are_requested()
    {
        JsonDocument[] responses = await RunGuestAgentAsync(
            new Dictionary<string, string?>(),
            ProcessStartRequest(
                requestId: "start-1",
                sequence: 1,
                processId: "process-env",
                fileName: "/usr/bin/env",
                arguments: [],
                isolation: new
                {
                    Mode = 2,
                    Filesystem = new
                    {
                        DangerousPaths = new
                        {
                            ProtectSensitiveDefaults = false,
                        },
                    },
                    Network = new
                    {
                        Mode = 2,
                    },
                    Environment = new
                    {
                        AllowedVariables = new[] { "HPD_ALLOWED" },
                        InjectedVariables = new Dictionary<string, string>
                        {
                            ["HPD_INJECTED"] = "injected",
                        },
                        StripUnlistedVariables = true,
                    },
                },
                environment: new Dictionary<string, string?>
                {
                    ["HPD_ALLOWED"] = "visible",
                    ["HPD_SECRET"] = "hidden",
                }),
            ProcessWaitRequest("wait-1", 2, "process-env"));

        responses[0].RootElement.GetProperty("ResponseStatus").GetInt32().Should().Be(0);
        JsonElement result = responses[1].RootElement
            .GetProperty("ProcessStatusResponse")
            .GetProperty("Result");
        string stdout = DecodeCapturedOutput(result.GetProperty("Output").GetProperty("Stdout"));
        stdout.Should().Contain("HPD_ALLOWED=visible");
        stdout.Should().Contain("HPD_INJECTED=injected");
        stdout.Should().NotContain("HPD_SECRET=hidden");
    }

    [Fact]
    public async Task Guest_agent_isolated_process_prefers_sandbox_plan_over_raw_policy()
    {
        JsonDocument[] responses = await RunGuestAgentAsync(
            new Dictionary<string, string?>(),
            ProcessStartRequest(
                requestId: "start-1",
                sequence: 1,
                processId: "process-plan-env",
                fileName: "/usr/bin/env",
                arguments: [],
                isolation: new
                {
                    Mode = 2,
                    Filesystem = new
                    {
                        DangerousPaths = new
                        {
                            ProtectSensitiveDefaults = false,
                        },
                    },
                    Network = new
                    {
                        Mode = 2,
                    },
                    Environment = new
                    {
                        StripUnlistedVariables = false,
                    },
                },
                sandboxPlan: new
                {
                    Plan = new
                    {
                        Filesystem = new
                        {
                            DangerousPaths = new
                            {
                                ProtectSensitiveDefaults = false,
                            },
                        },
                        Network = new
                        {
                            Mode = 2,
                        },
                        Environment = new
                        {
                            AllowedVariables = new[] { "HPD_ALLOWED" },
                            InjectedVariables = new Dictionary<string, string>
                            {
                                ["HPD_PLAN_INJECTED"] = "plan",
                            },
                            StripUnlistedVariables = true,
                        },
                    },
                },
                environment: new Dictionary<string, string?>
                {
                    ["HPD_ALLOWED"] = "visible",
                    ["HPD_SECRET"] = "hidden",
                }),
            ProcessWaitRequest("wait-1", 2, "process-plan-env"));

        responses[0].RootElement.GetProperty("ResponseStatus").GetInt32().Should().Be(0);
        JsonElement result = responses[1].RootElement
            .GetProperty("ProcessStatusResponse")
            .GetProperty("Result");
        string stdout = DecodeCapturedOutput(result.GetProperty("Output").GetProperty("Stdout"));
        stdout.Should().Contain("HPD_ALLOWED=visible");
        stdout.Should().Contain("HPD_PLAN_INJECTED=plan");
        stdout.Should().NotContain("HPD_SECRET=hidden");
    }

    [Fact]
    public async Task Guest_agent_isolated_process_fails_closed_for_unsupported_filesystem_rule_patterns()
    {
        JsonDocument[] responses = await RunGuestAgentAsync(
            new Dictionary<string, string?>(),
            ProcessStartRequest(
                requestId: "start-1",
                sequence: 1,
                processId: "process-isolated",
                fileName: "/bin/echo",
                arguments: ["hello"],
                isolation: new
                {
                    Mode = 2,
                    Filesystem = new
                    {
                        Rules = new[]
                        {
                            new
                            {
                                Kind = 2,
                                PatternKind = 2,
                                Path = new
                                {
                                    Value = "/workspace/**",
                                },
                            },
                        },
                        DangerousPaths = new
                        {
                            ProtectSensitiveDefaults = false,
                        },
                    },
                    Network = new
                    {
                        Mode = 2,
                    },
                },
                environment: new Dictionary<string, string?>()));

        responses[0].RootElement.GetProperty("ResponseStatus").GetInt32().Should().Be(2);
        JsonElement error = responses[0].RootElement.GetProperty("Error");
        error.GetProperty("Code").GetString()
            .Should().Be("AppleVirtualization.GuestAgentProcessIsolationUnsupported");
        error.GetProperty("Message").GetString()
            .Should().Contain("filesystem.rule.pattern");
    }

    [Fact]
    public async Task Guest_agent_isolated_process_fails_closed_when_filesystem_isolation_requires_missing_bwrap()
    {
        using TemporaryAuthorityWorkspace workspace = TemporaryAuthorityWorkspace.Create();

        JsonDocument[] responses = await RunGuestAgentAsync(
            new Dictionary<string, string?>
            {
                ["PATH"] = string.Empty,
            },
            ProcessStartRequest(
                requestId: "start-1",
                sequence: 1,
                processId: "process-isolated",
                fileName: "/bin/echo",
                arguments: ["hello"],
                isolation: new
                {
                    Mode = 2,
                    Filesystem = new
                    {
                        Rules = new[]
                        {
                            new
                            {
                                Kind = 2,
                                Path = new
                                {
                                    Value = workspace.Root,
                                },
                            },
                        },
                        DangerousPaths = new
                        {
                            ProtectSensitiveDefaults = false,
                        },
                    },
                    Network = new
                    {
                        Mode = 2,
                    },
                },
                environment: new Dictionary<string, string?>()));

        responses[0].RootElement.GetProperty("ResponseStatus").GetInt32().Should().Be(2);
        JsonElement error = responses[0].RootElement.GetProperty("Error");
        error.GetProperty("Code").GetString()
            .Should().Be("AppleVirtualization.GuestAgentProcessIsolationUnavailable");
        error.GetProperty("Message").GetString()
            .Should().Contain("bubblewrap");
    }

    [SkippableFact]
    public async Task Guest_agent_isolated_process_uses_bwrap_for_allowed_writable_paths()
    {
        Skip.IfNot(CanRunBubblewrap(), "bubblewrap is not available or cannot run on this host.");

        using TemporaryAuthorityWorkspace allowed = TemporaryAuthorityWorkspace.Create("allowed.sock");
        using TemporaryAuthorityWorkspace outside = TemporaryAuthorityWorkspace.Create("outside.sock");
        string allowedFile = Path.Combine(allowed.Root, "allowed.txt");
        string deniedFile = Path.Combine(allowed.Root, "denied.txt");
        string outsideFile = Path.Combine(outside.Root, "blocked.txt");
        File.WriteAllText(deniedFile, "protected");
        string script = string.Join(
            " ",
            "printf ok > " + ShellQuote(allowedFile) + ";",
            "if printf nope > " + ShellQuote(deniedFile) + " 2>/dev/null; then echo denied-writable; else echo denied-blocked; fi;",
            "if printf nope > " + ShellQuote(outsideFile) + " 2>/dev/null; then echo outside-writable; else echo outside-blocked; fi;",
            "cat " + ShellQuote(allowedFile));

        JsonDocument[] responses = await RunGuestAgentAsync(
            new Dictionary<string, string?>(),
            ProcessStartRequest(
                requestId: "start-1",
                sequence: 1,
                processId: "process-bwrap",
                fileName: "/bin/sh",
                arguments: ["-c", script],
                isolation: new
                {
                    Mode = 2,
                    Filesystem = new
                    {
                        Rules = new[]
                        {
                            new
                            {
                                Kind = 2,
                                Path = new
                                {
                                    Value = allowed.Root,
                                },
                            },
                            new
                            {
                                Kind = 3,
                                Path = new
                                {
                                    Value = deniedFile,
                                },
                            },
                        },
                        DangerousPaths = new
                        {
                            ProtectSensitiveDefaults = false,
                        },
                    },
                    Network = new
                    {
                        Mode = 2,
                    },
                },
                environment: new Dictionary<string, string?>()),
            ProcessWaitRequest("wait-1", 2, "process-bwrap"));

        responses[0].RootElement.GetProperty("ResponseStatus").GetInt32().Should().Be(0);
        JsonElement result = responses[1].RootElement
            .GetProperty("ProcessStatusResponse")
            .GetProperty("Result");
        result.GetProperty("ExitCode").GetInt32().Should().Be(0);
        string stdout = DecodeCapturedOutput(result.GetProperty("Output").GetProperty("Stdout"));
        stdout.Should().Contain("denied-blocked");
        stdout.Should().Contain("outside-blocked");
        stdout.Should().Contain("ok");
        File.ReadAllText(allowedFile).Should().Be("ok");
        File.ReadAllText(deniedFile).Should().Be("protected");
        File.Exists(outsideFile).Should().BeFalse();
    }

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
            .Should().Equal(3);
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

    private static string ProcessStartRequest(
        string requestId,
        int sequence,
        string processId,
        string fileName,
        string[] arguments,
        object isolation,
        object? sandboxPlan,
        IReadOnlyDictionary<string, string?> environment)
    {
        var request = new
        {
            ProtocolVersion = "1.0",
            MessageType = 0,
            Operation = 22,
            RequestId = requestId,
            SequenceNumber = sequence,
            ProcessStartRequest = new
            {
                ProcessId = processId,
                UnitId = "unit-1",
                Command = new
                {
                    FileName = fileName,
                    Arguments = arguments,
                    Environment = environment,
                },
                Isolation = isolation,
                SandboxPlan = sandboxPlan,
            },
        };

        return JsonSerializer.Serialize(request);
    }

    private static string ProcessStartRequest(
        string requestId,
        int sequence,
        string processId,
        string fileName,
        string[] arguments,
        object isolation,
        IReadOnlyDictionary<string, string?> environment) =>
        ProcessStartRequest(requestId, sequence, processId, fileName, arguments, isolation, sandboxPlan: null, environment);

    private static string ProcessWaitRequest(string requestId, int sequence, string processId)
    {
        var request = new
        {
            ProtocolVersion = "1.0",
            MessageType = 0,
            Operation = 27,
            RequestId = requestId,
            SequenceNumber = sequence,
            ProcessLifecycleRequest = new
            {
                ProcessId = processId,
            },
        };

        return JsonSerializer.Serialize(request);
    }

    private static string DecodeCapturedOutput(JsonElement streamOutput)
    {
        string encoded = streamOutput.GetProperty("CapturedBytes").GetString() ?? string.Empty;
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
    }

    private static bool CanRunBubblewrap()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("bwrap", "--ro-bind / / -- /bin/true")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                },
            };
            process.Start().Should().BeTrue();
            bool exited = process.WaitForExit(5_000);
            return exited && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string ShellQuote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

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
                "HPD-Environment.Framework",
                "src",
                "HPD-Environment.AppleVirtualization",
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
