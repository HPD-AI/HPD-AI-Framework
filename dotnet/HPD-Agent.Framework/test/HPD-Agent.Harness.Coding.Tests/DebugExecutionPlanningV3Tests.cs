using FluentAssertions;
using HPD.Agent.ToolHarness.Coding.Debugging;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol;
using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Environment.Contracts;
using HPDOS.ToolHarnesses.Middleware;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class DebugExecutionPlanningV3Tests
{
    [Fact]
    public void Public_target_union_contains_only_v3_shapes()
    {
        var derived = typeof(DebugTarget)
            .GetCustomAttributes<System.Text.Json.Serialization.JsonDerivedTypeAttribute>()
            .Select(attribute => (
                attribute.TypeDiscriminator?.ToString(),
                attribute.DerivedType))
            .ToArray();

        derived.Select(item => item.Item1).Should().BeEquivalentTo(
            "sourceFile", "applicationProject", "executable", "test");
        typeof(LaunchDebugOperation).GetProperties()
            .Should().NotContain(property => property.Name == "Arguments");
        typeof(SourceFileDebugTarget).GetProperty("Arguments").Should().NotBeNull();
        typeof(ApplicationProjectDebugTarget).GetProperty("Arguments").Should().NotBeNull();
        typeof(ExecutableDebugTarget).GetProperty("Arguments").Should().NotBeNull();
    }

    [Fact]
    public void Vstest_readiness_parser_accepts_one_official_process_id()
    {
        var parser = new VSTestHostDebugReadinessParser();

        var observation = parser.Observe(
            "Host debugging is enabled. Please attach debugger to testhost process to continue.\n" +
            "Process Id: 42017, Name: dotnet",
            DebugReadinessMultiplicity.ExactlyOne);

        observation.Status.Should().Be(DebugHostReadinessStatus.Ready);
        observation.Ready!.SystemProcessId.Should().Be(42017);
        observation.Ready.SafeProcessRole.Should().Be("testhost");
    }

    [Theory]
    [InlineData("Test run successful. Total tests: 42")]
    [InlineData("Process Id: 0")]
    [InlineData("Process Id: -1")]
    [InlineData("Process Id: 10\nProcess Id: 11")]
    public void Vstest_readiness_parser_rejects_missing_or_ambiguous_identity(string transcript)
    {
        new VSTestHostDebugReadinessParser()
            .Observe(transcript, DebugReadinessMultiplicity.ExactlyOne)
            .Status.Should().NotBe(DebugHostReadinessStatus.Ready);
    }

    [Fact]
    public async Task Planner_registry_selects_one_highest_priority_match()
    {
        var expected = new StubExecutionPlan
        {
            PlannerId = "high",
            SemanticStartKind = DebugSemanticStartKind.DirectLaunch,
            EnvironmentId = "environment",
            EnvironmentRevision = 1,
            CanonicalWorkingDirectory = "/workspace",
            InitialConfiguration = new()
        };
        var registry = new DebugExecutionTargetPlannerRegistry(
        [
            new StubPlanner("low", 10, new StubExecutionPlan
            {
                PlannerId = "low",
                SemanticStartKind = DebugSemanticStartKind.DirectLaunch,
                EnvironmentId = "environment",
                EnvironmentRevision = 1,
                CanonicalWorkingDirectory = "/workspace",
                InitialConfiguration = new()
            }),
            new StubPlanner("high", 20, expected)
        ]);

        var result = await registry.PlanAsync(Context(), CancellationToken.None);

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task Planner_registry_rejects_equal_priority_ambiguity()
    {
        var plan = new StubExecutionPlan
        {
            PlannerId = "one",
            SemanticStartKind = DebugSemanticStartKind.DirectLaunch,
            EnvironmentId = "environment",
            EnvironmentRevision = 1,
            CanonicalWorkingDirectory = "/workspace",
            InitialConfiguration = new()
        };
        var registry = new DebugExecutionTargetPlannerRegistry(
        [
            new StubPlanner("one", 10, plan),
            new StubPlanner("two", 10, plan with { PlannerId = "two" })
        ]);

        var action = () => registry.PlanAsync(Context(), CancellationToken.None).AsTask();

        var exception = await action.Should().ThrowAsync<DebugStartPlanningException>();
        exception.Which.Kind.Should().Be("debug_planner_ambiguous");
    }

    [Fact]
    public async Task Planner_registry_rejects_an_unsupported_target()
    {
        var registry = new DebugExecutionTargetPlannerRegistry([]);

        var action = () => registry.PlanAsync(Context(), CancellationToken.None).AsTask();

        var exception = await action.Should().ThrowAsync<DebugStartPlanningException>();
        exception.Which.Kind.Should().Be("debug_target_unsupported");
    }

    [Fact]
    public async Task Planner_registry_honors_cancellation_before_evaluating_a_planner()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var planner = new CountingPlanner();
        var registry = new DebugExecutionTargetPlannerRegistry([planner]);

        var action = () => registry.PlanAsync(Context(), cancellation.Token).AsTask();

        await action.Should().ThrowAsync<OperationCanceledException>();
        planner.EvaluationCount.Should().Be(0);
    }

    [Fact]
    public async Task Executable_artifact_classifier_requires_an_exact_test_output_match()
    {
        await using var fixture = new PlannerContextFixture(
            new ExecutableDebugTarget("bin/Fixture.dll"));
        var classifier = new DotNetDebugExecutableArtifactClassifier(
            new FixedProjectEvaluator(
                fixture.Evaluation(DotNetDebugProjectKind.Test)));

        var result = await classifier.ClassifyAsync(
            fixture.Context with { CanonicalTargetPath = fixture.Output },
            CancellationToken.None);

        result.Kind.Should().Be(DebugExecutableArtifactKind.Test);
        result.ProjectPath.Should().Be(fixture.Project);
    }

    [Fact]
    public async Task Executable_artifact_classifier_does_not_use_nearby_test_evidence()
    {
        await using var fixture = new PlannerContextFixture(
            new ExecutableDebugTarget("bin/Unrelated.dll"));
        var unrelated = Path.Combine(fixture.Root, "bin", "Unrelated.dll");
        File.WriteAllBytes(unrelated, [0]);
        var classifier = new DotNetDebugExecutableArtifactClassifier(
            new FixedProjectEvaluator(
                fixture.Evaluation(DotNetDebugProjectKind.Test)));

        var result = await classifier.ClassifyAsync(
            fixture.Context with { CanonicalTargetPath = unrelated },
            CancellationToken.None);

        result.Kind.Should().Be(DebugExecutableArtifactKind.Unassociated);
    }

    [Fact]
    public async Task Executable_artifact_classifier_rejects_multiple_exact_claims()
    {
        await using var fixture = new PlannerContextFixture(
            new ExecutableDebugTarget("bin/Fixture.dll"));
        var secondProject = Path.Combine(fixture.Root, "Second.csproj");
        File.WriteAllText(secondProject, "<Project />");
        var context = fixture.Context with
        {
            CanonicalTargetPath = fixture.Output,
            Evidence = fixture.Context.Evidence with
            {
                MatchedPaths = [fixture.Project, secondProject]
            }
        };
        var classifier = new DotNetDebugExecutableArtifactClassifier(
            new FixedProjectEvaluator(
                fixture.Evaluation(DotNetDebugProjectKind.Test)));

        var classify = () => classifier.ClassifyAsync(
            context,
            CancellationToken.None).AsTask();

        var failure = await classify.Should()
            .ThrowAsync<DebugStartPlanningException>();
        failure.Which.Kind.Should().Be(
            "debug_artifact_project_ambiguous");
    }

    [Fact]
    public void Real_planners_and_plan_shapes_cannot_retain_live_invocation_state()
    {
        var plannerTypes = new[]
        {
            typeof(DirectSourceDebugExecutionTargetPlanner),
            typeof(DirectExecutableDebugExecutionTargetPlanner),
            typeof(DotNetApplicationDebugExecutionTargetPlanner),
            typeof(DotNetTestDebugExecutionTargetPlanner)
        };
        plannerTypes.SelectMany(type => type.GetFields(
                BindingFlags.Instance |
                BindingFlags.NonPublic |
                BindingFlags.Public))
            .Select(field => field.FieldType)
            .Should().NotContain(type =>
                typeof(IProcessInvocationHandle).IsAssignableFrom(type) ||
                typeof(IDebugOwnedResource).IsAssignableFrom(type) ||
                typeof(IDebugProtocolTransport).IsAssignableFrom(type) ||
                typeof(IDebugSessionManager).IsAssignableFrom(type));
        plannerTypes.SelectMany(type => type.GetMethods(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic))
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Should().NotContain(typeof(DebugPermissionDecision),
                "planners receive semantic evidence, never permission-minting authority");

        var planTypes = new[]
        {
            typeof(DirectAdapterDebugExecutionPlan),
            typeof(HostedAttachDebugExecutionPlan),
            typeof(PreparedAdapterDebugExecutionPlan)
        };
        planTypes.SelectMany(type => type.GetProperties(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic))
            .Select(property => property.PropertyType)
            .Should().NotContain(type =>
                typeof(IProcessInvocationHandle).IsAssignableFrom(type) ||
                typeof(IDebugOwnedResource).IsAssignableFrom(type) ||
                typeof(IDebugProtocolTransport).IsAssignableFrom(type) ||
                typeof(IDebugSessionManager).IsAssignableFrom(type));
    }

    [Theory]
    [InlineData(".sln")]
    [InlineData(".slnx")]
    public void Project_selection_reads_solution_membership_without_guessing(
        string extension)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "hpd-project-selection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = Path.Combine(root, "App.csproj");
            var solution = Path.Combine(root, "App" + extension);
            File.WriteAllText(project, "<Project />");
            File.WriteAllText(
                solution,
                extension == ".slnx"
                    ? "<Solution><Project Path=\"App.csproj\" /></Solution>"
                    : "Project(\"{00000000-0000-0000-0000-000000000000}\") = \"App\", \"App.csproj\", \"{00000000-0000-0000-0000-000000000001}\"");
            var workspace = new AgentWorkspace(
                "root",
                root,
                [new("root", root)]);
            var context = Context() with
            {
                Target = new ApplicationProjectDebugTarget(root),
                Operation = new LaunchDebugOperation(
                    new ApplicationProjectDebugTarget(root)),
                Workspace = workspace,
                CanonicalWorkspacePath = root,
                CanonicalTargetPath = solution,
                Evidence = new(
                    root,
                    solution,
                    new HashSet<string>([extension]),
                    [solution],
                    "solution")
            };

            DotNetProjectSelection.Select(context, null).Should().Be(project);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Project_selection_allows_solution_membership_in_another_workspace_root()
    {
        var container = Path.Combine(
            Path.GetTempPath(),
            "hpd-multi-root-selection-" + Guid.NewGuid().ToString("N"));
        var solutionRoot = Path.Combine(container, "solution");
        var projectRoot = Path.Combine(container, "project");
        Directory.CreateDirectory(solutionRoot);
        Directory.CreateDirectory(projectRoot);
        try
        {
            var project = Path.Combine(projectRoot, "App.csproj");
            var solution = Path.Combine(solutionRoot, "App.slnx");
            File.WriteAllText(project, "<Project />");
            File.WriteAllText(
                solution,
                "<Solution><Project Path=\"../project/App.csproj\" /></Solution>");
            var workspace = new AgentWorkspace(
                "solution",
                solutionRoot,
                [
                    new("solution", solutionRoot),
                    new("project", projectRoot)
                ]);
            var context = Context() with
            {
                Target = new ApplicationProjectDebugTarget(solution),
                Operation = new LaunchDebugOperation(
                    new ApplicationProjectDebugTarget(solution)),
                Workspace = workspace,
                CanonicalWorkspacePath = solutionRoot,
                CanonicalTargetPath = solution,
                Evidence = new(
                    solutionRoot,
                    solutionRoot,
                    new HashSet<string>(["*.slnx"]),
                    [solution],
                    "solution")
            };

            DotNetProjectSelection.Select(context, null).Should().Be(project);
        }
        finally
        {
            try { Directory.Delete(container, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Project_selection_rejects_ambiguous_projects()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "hpd-project-selection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var first = Path.Combine(root, "One.csproj");
            var second = Path.Combine(root, "Two.csproj");
            File.WriteAllText(first, "<Project />");
            File.WriteAllText(second, "<Project />");
            var workspace = new AgentWorkspace(
                "root",
                root,
                [new("root", root)]);
            var context = Context() with
            {
                Target = new ApplicationProjectDebugTarget(root),
                Operation = new LaunchDebugOperation(
                    new ApplicationProjectDebugTarget(root)),
                Workspace = workspace,
                CanonicalWorkspacePath = root,
                CanonicalTargetPath = root,
                Evidence = new(
                    root,
                    first,
                    new HashSet<string>(["*.csproj"]),
                    [first, second],
                    "ambiguous")
            };

            var action = () => DotNetProjectSelection.Select(context, null);

            var exception = action.Should()
                .Throw<DebugStartPlanningException>();
            exception.Which.Kind.Should().Be("debug_project_ambiguous");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Project_selection_prefers_an_exact_project_target_over_broader_evidence()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "hpd-exact-project-selection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var exact = Path.Combine(root, "Exact.csproj");
            var unrelated = Path.Combine(root, "Unrelated.csproj");
            File.WriteAllText(exact, "<Project />");
            File.WriteAllText(unrelated, "<Project />");
            var workspace = new AgentWorkspace(
                "root",
                root,
                [new("root", root)]);
            var context = Context() with
            {
                Target = new TestDebugTarget(exact),
                Operation = new LaunchDebugOperation(new TestDebugTarget(exact)),
                Workspace = workspace,
                CanonicalWorkspacePath = root,
                CanonicalTargetPath = exact,
                Evidence = new(
                    root,
                    exact,
                    new HashSet<string>(["*.csproj"]),
                    [exact, unrelated],
                    "broader-evidence")
            };

            DotNetProjectSelection.Select(context, null).Should().Be(exact);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(".sln")]
    [InlineData(".slnx")]
    public void Project_selection_rejects_solution_manifests_beyond_the_bound(
        string extension)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "hpd-project-manifest-bound-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var solution = Path.Combine(root, "Oversized" + extension);
            File.WriteAllText(
                solution,
                extension == ".slnx"
                    ? "<Solution>" + string.Concat(
                        Enumerable.Range(0, 258).Select(index =>
                            $"<Project Path=\"P{index}.csproj\" />")) + "</Solution>"
                    : string.Join(
                        System.Environment.NewLine,
                        Enumerable.Range(0, 10_002).Select(index =>
                            $"Project(\"{{0}}\") = \"P{index}\", \"P{index}.csproj\", \"{{1}}\"")));
            var workspace = new AgentWorkspace(
                "root",
                root,
                [new("root", root)]);
            var context = Context() with
            {
                Target = new ApplicationProjectDebugTarget(root),
                Operation = new LaunchDebugOperation(
                    new ApplicationProjectDebugTarget(root)),
                Workspace = workspace,
                CanonicalWorkspacePath = root,
                CanonicalTargetPath = solution,
                Evidence = new(
                    root,
                    solution,
                    new HashSet<string>([extension]),
                    [solution],
                    "oversized")
            };

            var action = () => DotNetProjectSelection.Select(context, null);

            var exception = action.Should().Throw<DebugStartPlanningException>();
            exception.Which.Kind.Should().Be("debug_project_evaluation_failed");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData("Test", "debug_test_target_requires_test_target")]
    [InlineData("Library", "debug_library_requires_host")]
    [InlineData("Unknown", "debug_project_execution_shape_unknown")]
    public async Task Application_planner_rejects_non_application_evaluation(
        string kindName,
        string expectedKind)
    {
        var kind = Enum.Parse<DotNetDebugProjectKind>(kindName);
        await using var fixture = new PlannerContextFixture(
            new ApplicationProjectDebugTarget("."));
        var planner = new DotNetApplicationDebugExecutionTargetPlanner(
            null!,
            new FixedProjectEvaluator(fixture.Evaluation(kind)));

        var action = () => planner.EvaluateAsync(
            fixture.Context,
            CancellationToken.None).AsTask();

        var exception = await action.Should().ThrowAsync<DebugStartPlanningException>();
        exception.Which.Kind.Should().Be(expectedKind);
        fixture.Manager.ListTrees(fixture.Scope).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Application", "debug_test_project_required")]
    [InlineData("Library", "debug_library_requires_host")]
    [InlineData("Unknown", "debug_test_project_required")]
    public async Task Test_planner_rejects_non_test_evaluation(
        string kindName,
        string expectedKind)
    {
        var kind = Enum.Parse<DotNetDebugProjectKind>(kindName);
        await using var fixture = new PlannerContextFixture(
            new TestDebugTarget(".", DebugTestFramework.DotNet));
        var planner = new DotNetTestDebugExecutionTargetPlanner(
            null!,
            new FixedProjectEvaluator(fixture.Evaluation(kind)));

        var action = () => planner.EvaluateAsync(
            fixture.Context,
            CancellationToken.None).AsTask();

        var exception = await action.Should().ThrowAsync<DebugStartPlanningException>();
        exception.Which.Kind.Should().Be(expectedKind);
        fixture.Manager.ListTrees(fixture.Scope).Should().BeEmpty();
    }

    [Theory]
    [InlineData(".cs")]
    [InlineData(".csx")]
    [InlineData(".fs")]
    [InlineData(".vb")]
    public async Task Direct_source_planner_rejects_dotnet_source_files(string extension)
    {
        await using var fixture = new PlannerContextFixture(
            new SourceFileDebugTarget("Program" + extension));
        var planner = new DirectSourceDebugExecutionTargetPlanner(null!);

        var action = () => planner.EvaluateAsync(
            fixture.Context,
            CancellationToken.None).AsTask();

        var exception = await action.Should().ThrowAsync<DebugStartPlanningException>();
        exception.Which.Kind.Should().Be("source_target_requires_project");
        fixture.Manager.ListTrees(fixture.Scope).Should().BeEmpty();
    }

    [Fact]
    public void Breakpoint_counts_distinguish_acknowledged_verified_and_pending()
    {
        var store = new DebugAdapterBreakpointStateStore();
        store.Replace(DebugBreakpointKind.Function,
        [
            new HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated.Breakpoint
            {
                Id = 1,
                Verified = true
            },
            new HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated.Breakpoint
            {
                Id = 2,
                Verified = false,
                Message = "Module has not loaded."
            }
        ]);

        var states = store.Snapshot;
        var counts = new DebugBreakpointCounts(
            Requested: 2,
            Acknowledged: states.Length,
            Verified: states.Count(state => state.Verified),
            Pending: states.Count(state => !state.Verified));

        counts.Should().Be(new DebugBreakpointCounts(2, 2, 1, 1));
    }

    [Fact]
    public void Initial_breakpoint_policy_allows_pending_by_default()
    {
        var states = new[]
        {
            new DebugAdapterBreakpointState(
                DebugBreakpointKind.Function,
                1,
                false,
                "Module has not loaded.",
                null,
                null,
                null,
                null,
                null)
        };

        var action = () => DebugProtocolSessionStarter.EnsureBreakpointPolicy(
            DebugInitialBreakpointPolicy.AllowPending,
            states);

        action.Should().NotThrow();
    }

    [Fact]
    public void Initial_breakpoint_policy_can_require_immediate_verification()
    {
        var states = new[]
        {
            new DebugAdapterBreakpointState(
                DebugBreakpointKind.Function,
                1,
                false,
                "Module has not loaded.",
                null,
                null,
                null,
                null,
                null)
        };

        var action = () => DebugProtocolSessionStarter.EnsureBreakpointPolicy(
            DebugInitialBreakpointPolicy.RequireImmediatelyVerified,
            states);

        var exception = action.Should().Throw<DebugStartPlanningException>();
        exception.Which.Kind.Should().Be("debug_initial_breakpoint_unverified");
    }

    [Fact]
    public async Task Terminal_record_replaces_live_tree_without_crossing_owner_scope()
    {
        await using var manager = new DebugSessionManager(new DebugTerminalRecordStore(new DebugTerminalRecordStoreOptions()));
        await using var reservation = manager.ReserveTree(
            "session", "thread", "environment", 1, "tree");
        using var arguments = JsonDocument.Parse("{}");
        var adapter = AdapterPlan(arguments.RootElement);
        var runtime = new DebugRuntimeBinding
        {
            AgentRuntimeRegistrationId = manager.RuntimeId,
            SessionId = "session",
            ThreadId = "thread",
            SessionManager = manager,
            EventScope = new(null, "session", "thread"),
            State = new()
        };
        var tree = new DebugSessionTree
        {
            Ownership = reservation.Ownership,
            RootSessionId = "root",
            RuntimeBinding = runtime,
            Authorization = DebugTreeAuthorization.Create(
                runtime,
                reservation.Ownership,
                adapter,
                DebugSemanticStartKind.DirectLaunch,
                "test",
                new()),
            Artifacts = new DebugArtifactWriter(
                null,
                ContentScope.Create("debug:test"),
                new Dictionary<string, string>())
        };
        tree.Breakpoints.Seed(new DebugInitialConfiguration
        {
            SourceBreakpoints =
            [
                new DebugSourceBreakpoint("/workspace/Program.cs", 10)
            ]
        });
        reservation.Commit(tree);
        var owner = new DebugTreeLookupScope(manager.RuntimeId, "session", "thread");

        await manager.RetainAndDisposeAsync(
            owner,
            "tree",
            "Terminated",
            "TEST");

        manager.TryResolveTerminal(owner, "tree", out var retained).Should().BeTrue();
        retained.Breakpoints.Pending.Should().Be(1);
        var foreign = new DebugTreeLookupScope(manager.RuntimeId, "other", "thread");
        var action = () => manager.TryResolveTerminal(foreign, "tree", out _);
        action.Should().Throw<DebugSessionOwnershipException>();
        var liveLookup = () => manager.ResolveTree(owner, "tree");
        liveLookup.Should().Throw<InvalidOperationException>();
        var lifecycle = new DebugLifecycleService(
            manager,
            new DebugSemanticService(manager));
        var stateChange = () => lifecycle.TerminateAsync(
            owner,
            "tree",
            null,
            DebugTerminationScope.Tree,
            terminateDebuggee: true,
            CancellationToken.None);
        await stateChange.Should().ThrowAsync<InvalidOperationException>();
        manager.TryResolveTerminal(owner, "tree", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Terminal_factory_removes_all_live_output_tokens()
    {
        await using var manager = new DebugSessionManager(
            new DebugTerminalRecordStore(new DebugTerminalRecordStoreOptions()));
        await using var reservation = manager.ReserveTree(
            "session", "thread", "environment", 1, "tree");
        using var arguments = JsonDocument.Parse("{}");
        var adapter = AdapterPlan(arguments.RootElement);
        var runtime = new DebugRuntimeBinding
        {
            AgentRuntimeRegistrationId = manager.RuntimeId,
            SessionId = "session",
            ThreadId = "thread",
            SessionManager = manager,
            EventScope = new(null, "session", "thread"),
            State = new()
        };
        var tree = new DebugSessionTree
        {
            Ownership = reservation.Ownership,
            RootSessionId = "root",
            RuntimeBinding = runtime,
            Authorization = DebugTreeAuthorization.Create(
                runtime,
                reservation.Ownership,
                adapter,
                DebugSemanticStartKind.DirectLaunch,
                "test",
                new()),
            Artifacts = new DebugArtifactWriter(
                null,
                ContentScope.Create("debug:test"),
                new Dictionary<string, string>())
        };
        var transport = new InMemoryDebugProtocolTransport();
        var session = new DebugSession
        {
            SessionId = "root",
            RootSessionId = "root",
            AdapterStartMethod = DebugAdapterStartMethod.Launch,
            AdapterPlan = adapter,
            Protocol = new DebugProtocolClient(transport)
        };
        tree.AddSession(session);
        session.Output.Append(
            "tree",
            "root",
            new HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated.OutputEventBody
            {
                Category = "stdout",
                Output = "safe"
            },
            allowAnsi: false,
            variablesToken: "variables-live",
            locationToken: "location-live");
        tree.OwnedResources.Enqueue(new CallbackOwnedResource(() =>
            session.Output.Append(
                "tree",
                "root",
                new HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated.OutputEventBody
                {
                    Category = "stderr",
                    Output = "late-owned-output"
                },
                allowAnsi: false)));

        await tree.StopAndDrainOwnedResourcesAsync();

        var terminal = DebugTerminalRecordFactory.Create(
            tree,
            "Terminated",
            "TEST");

        terminal.Output.Records.Should().HaveCount(2);
        terminal.Output.Records.Should().Contain(record =>
            record.Text == "late-owned-output");
        terminal.Output.Records.Should().OnlyContain(record =>
            record.VariablesToken == null && record.LocationToken == null);
        await tree.DisposeAsync();
    }

    [Fact]
    public void Terminal_store_evicts_oldest_record_at_the_count_bound()
    {
        var store = new DebugTerminalRecordStore(new DebugTerminalRecordStoreOptions());
        var evictions = new List<(string TreeId, string Reason)>();
        var runtime = "runtime";
        var owner = new DebugTreeLookupScope(runtime, "session", "thread");
        var start = DateTimeOffset.UtcNow.AddMinutes(-1);
        for (var index = 0; index <= 64; index++)
            store.Retain(
                Terminal(
                    runtime,
                    $"tree-{index:D3}",
                    start.AddSeconds(index)),
                (record, reason) => evictions.Add(
                    (record.Ownership.DebugTreeId, reason)));

        store.TryGet(owner, "tree-000", out _).Should().BeFalse();
        store.TryGet(owner, "tree-001", out _).Should().BeTrue();
        store.TryGet(
            owner,
            $"tree-{64:D3}",
            out _).Should().BeTrue();
        evictions.Should().ContainSingle()
            .Which.Should().Be(("tree-000", "COUNT_BOUND"));
    }

    [Fact]
    public void Terminal_store_expires_old_records()
    {
        var store = new DebugTerminalRecordStore(new DebugTerminalRecordStoreOptions());
        var terminal = Terminal(
            "runtime",
            "expired",
            DateTimeOffset.UtcNow - TimeSpan.FromMinutes(15) -
                TimeSpan.FromSeconds(1));

        string? reason = null;
        store.Retain(terminal, (_, value) => reason = value);

        store.TryGet(
            new("runtime", "session", "thread"),
            "expired",
            out _).Should().BeFalse();
        reason.Should().Be("EXPIRED");
    }

    [Fact]
    public void Terminal_store_evicts_deterministically_at_the_actual_byte_bound()
    {
        var first = Terminal("runtime", "first", DateTimeOffset.UtcNow.AddSeconds(-1));
        var second = Terminal("runtime", "second", DateTimeOffset.UtcNow);
        var oneRecordBytes = DebugTerminalRecordStore.EstimateBytes(first);
        var store = new DebugTerminalRecordStore(new DebugTerminalRecordStoreOptions
        {
            MaximumRecords = 8,
            MaximumAggregateBytes = oneRecordBytes + 16
        });
        var evictions = new List<(string TreeId, string Reason)>();

        store.Retain(first, Observe);
        store.Retain(second, Observe);

        store.TryGet(
            new("runtime", "session", "thread"),
            "first",
            out _).Should().BeFalse();
        store.TryGet(
            new("runtime", "session", "thread"),
            "second",
            out _).Should().BeTrue();
        evictions.Should().ContainSingle()
            .Which.Should().Be(("first", "BYTE_BOUND"));

        void Observe(DebugTerminalRecord record, string reason)
            => evictions.Add((record.Ownership.DebugTreeId, reason));
    }

    [Fact]
    public async Task Owned_process_resource_stops_and_disposes_exactly_once()
    {
        var handle = new CountingInvocationHandle();
        var resource = new DebugOwnedProcessResource(
            handle,
            "fixture-host",
            TimeSpan.FromSeconds(1));

        await resource.StopAsync("first", CancellationToken.None);
        await resource.StopAsync("second", CancellationToken.None);
        await resource.DisposeAsync();
        await resource.DisposeAsync();

        handle.StopCount.Should().Be(1);
        handle.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task Owned_process_resource_continues_draining_and_bounds_host_output()
    {
        var handle = new OutputInvocationHandle(new string('x', 128));
        var resource = new DebugOwnedProcessResource(
            handle,
            "fixture-host",
            TimeSpan.FromSeconds(1));

        resource.BeginObservation(16, 16);
        await handle.Observed.Task.WaitAsync(TimeSpan.FromSeconds(1));

        resource.OutputSnapshot.Stdout.Should().HaveLength(16);
        resource.OutputSnapshot.RetainedBytes.Should().Be(16);
        resource.OutputSnapshot.DroppedBytes.Should().Be(112);
        handle.WaitCount.Should().Be(1);
        await resource.DisposeAsync();
    }

    private static DebugExecutionPlanningContext Context() => new()
    {
        Operation = new LaunchDebugOperation(new ExecutableDebugTarget("/workspace/app")),
        Target = new ExecutableDebugTarget("/workspace/app"),
        Runtime = null!,
        Workspace = null!,
        CanonicalWorkspacePath = "/workspace",
        CanonicalTargetPath = "/workspace/app",
        Evidence = new(
            "/workspace",
            null,
            new HashSet<string>(),
            [],
            "none")
    };

    private sealed record StubExecutionPlan : DebugExecutionPlan;

    private sealed class StubPlanner(
        string id,
        int priority,
        DebugExecutionPlan plan) : IDebugExecutionTargetPlanner
    {
        public string Id => id;
        public int Priority => priority;

        public ValueTask<DebugExecutionPlanningResult> EvaluateAsync(
            DebugExecutionPlanningContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(DebugExecutionPlanningResult.Applicable(plan));
    }

    private sealed class CountingPlanner : IDebugExecutionTargetPlanner
    {
        public string Id => "counting";
        public int Priority => 1;
        public int EvaluationCount { get; private set; }

        public ValueTask<DebugExecutionPlanningResult> EvaluateAsync(
            DebugExecutionPlanningContext context,
            CancellationToken cancellationToken)
        {
            EvaluationCount++;
            return ValueTask.FromResult(DebugExecutionPlanningResult.NotApplicable);
        }
    }

    private sealed class FixedProjectEvaluator(DotNetDebugProjectEvaluation evaluation)
        : IDotNetDebugProjectEvaluator
    {
        public ValueTask<DotNetDebugProjectEvaluation> EvaluateAsync(
            DotNetDebugProjectEvaluationRequest request,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(evaluation);
    }

    private sealed class PlannerContextFixture : IAsyncDisposable
    {
        public PlannerContextFixture(DebugTarget target)
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "hpd-planner-context-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Project = Path.Combine(Root, "Fixture.csproj");
            Output = Path.Combine(Root, "bin", "Fixture.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(Output)!);
            File.WriteAllText(Project, "<Project />");
            File.WriteAllBytes(Output, [0]);
            Manager = new DebugSessionManager(
                new DebugTerminalRecordStore(new DebugTerminalRecordStoreOptions()));
            Scope = new(Manager.RuntimeId, "session", "thread");
            var process = new RuntimeProcessExecutionBinding
            {
                EnvironmentId = "environment",
                EnvironmentRevision = 1,
                ProcessProvider = new NeverProcessProvider(),
                ExecutionTarget = new(
                    new TargetRoute
                    {
                        Kind = new("test.execution"),
                        Scope = new("test")
                    },
                    TargetHandleLifetime.LiveCapability,
                    TargetHandleAuthority.Control | TargetHandleAuthority.Observe)
            };
            var workspace = new AgentWorkspace(
                "root",
                Root,
                [new("root", Root)]);
            Context = new()
            {
                Operation = new LaunchDebugOperation(target),
                Target = target,
                Runtime = new()
                {
                    AgentRuntimeRegistrationId = Manager.RuntimeId,
                    SessionId = "session",
                    ThreadId = "thread",
                    SessionManager = Manager,
                    EventScope = new(null, "session", "thread"),
                    ProcessExecution = process,
                    State = new()
                },
                Workspace = workspace,
                CanonicalWorkspacePath = Root,
                CanonicalTargetPath = target is SourceFileDebugTarget source
                    ? Path.Combine(Root, source.Path)
                    : Root,
                Evidence = new(
                    Root,
                    Project,
                    new HashSet<string>(["*.csproj"]),
                    [Project],
                    "fixture")
            };
        }

        public string Root { get; }
        public string Project { get; }
        public string Output { get; }
        public DebugSessionManager Manager { get; }
        public DebugTreeLookupScope Scope { get; }
        public DebugExecutionPlanningContext Context { get; }

        public DotNetDebugProjectEvaluation Evaluation(DotNetDebugProjectKind kind)
            => new()
            {
                ProjectPath = Project,
                ProjectKind = kind,
                TestPlatform = kind == DotNetDebugProjectKind.Test
                    ? DotNetTestPlatformKind.VSTest
                    : DotNetTestPlatformKind.None,
                AssemblyName = "Fixture",
                OutputType = kind == DotNetDebugProjectKind.Application ? "Exe" : "Library",
                IsTestProject = kind == DotNetDebugProjectKind.Test,
                IsDirectlyExecutable = kind == DotNetDebugProjectKind.Application,
                TargetFrameworks = ["net10.0"],
                SelectedTargetFramework = "net10.0",
                TargetPath = Output,
                ProjectReferences = [],
                ProjectReferenceOutputs = [],
                Packages = [],
                EvaluationFingerprint = "fixture",
                ArtifactIsCurrent = true
            };

        public async ValueTask DisposeAsync()
        {
            await Manager.DisposeAsync();
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }

    private sealed class NeverProcessProvider : IProcessProvider
    {
        public ProviderId ProviderId => new("test.never");
        public ValueTask<ProcessInvocationResult> RunAsync(
            ProcessInvocationSpec spec,
            IProcessOutputSink? outputSink = null,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Planning unexpectedly started a process.");
        public ValueTask<IProcessInvocationHandle> StartAsync(
            ProcessInvocationSpec spec,
            IProcessOutputSink? output = null,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Planning unexpectedly acquired a handle.");
        public ValueTask SignalAsync(TargetHandle<ProcessInvocation> process, ProcessSignal signal, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask ResizeTerminalAsync(TargetHandle<ProcessInvocation> process, TerminalSpec size, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ProcessInvocationResult> WaitAsync(TargetHandle<ProcessInvocation> process, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(TargetHandle<ProcessInvocation> process, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static DebugTerminalRecord Terminal(
        string runtime,
        string treeId,
        DateTimeOffset completedAt)
    {
        var ownership = new DebugTreeOwnership(
            runtime,
            "session",
            "thread",
            treeId,
            "environment",
            1);
        return new()
        {
            Ownership = ownership,
            SemanticStartKind = DebugSemanticStartKind.DirectLaunch,
            AdapterStartMethod = DebugAdapterStartMethod.Launch,
            AdapterId = "fixture",
            FinalStatus = "Terminated",
            ExitCode = 0,
            StartedAt = completedAt.AddSeconds(-1),
            CompletedAt = completedAt,
            Breakpoints = new(0, 0, 0, 0),
            Snapshot = new(
                treeId,
                null,
                "Terminated",
                [],
                0,
                false,
                0,
                0,
                0,
                0,
                0),
            Output = new([], 1, 0, 0, 0, 0),
            Artifacts = []
        };
    }

    private sealed class CountingInvocationHandle : IProcessInvocationHandle
    {
        public int StopCount { get; private set; }
        public int DisposeCount { get; private set; }
        public TargetHandle<ProcessInvocation> Handle { get; } = new(
            new TargetRoute { Kind = new("test.process"), Scope = new("test") },
            TargetHandleLifetime.LiveCapability,
            TargetHandleAuthority.Control | TargetHandleAuthority.Observe);
        public ResourceRef<ProcessInvocation>? Resource => null;
        public ProcessInvocationSpec Spec { get; } = new()
        {
            Target = new TargetHandle<ExecutionUnit>(
                new TargetRoute
                {
                    Kind = new("test.execution"),
                    Scope = new("test")
                },
                TargetHandleLifetime.LiveCapability,
                TargetHandleAuthority.Control | TargetHandleAuthority.Observe),
            Command = new() { FileName = "fixture" }
        };

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask StopAsync(
            ProcessStopRequest request,
            CancellationToken cancellationToken = default)
        {
            StopCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteStdinAsync(
            ReadOnlyMemory<byte> bytes,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask CloseStdinAsync(
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask SignalAsync(
            ProcessSignal signal,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask ResizeTerminalAsync(
            TerminalSpec size,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<ProcessInvocationResult> WaitAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new ProcessInvocationResult
            {
                CompletionKind = ProcessCompletionKind.Completed,
                Output = new()
                {
                    Stdout = new(),
                    Stderr = new(),
                    OutputDrainTimeout = TimeSpan.Zero
                }
            });

        public async IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class OutputInvocationHandle(string output)
        : IProcessInvocationHandle
    {
        public TaskCompletionSource Observed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int WaitCount { get; private set; }
        public TargetHandle<ProcessInvocation> Handle { get; } = new(
            new TargetRoute { Kind = new("test.process"), Scope = new("test") },
            TargetHandleLifetime.LiveCapability,
            TargetHandleAuthority.Control | TargetHandleAuthority.Observe);
        public ResourceRef<ProcessInvocation>? Resource => null;
        public ProcessInvocationSpec Spec { get; } = new()
        {
            Target = new TargetHandle<ExecutionUnit>(
                new TargetRoute
                {
                    Kind = new("test.execution"),
                    Scope = new("test")
                },
                TargetHandleLifetime.LiveCapability,
                TargetHandleAuthority.Control | TargetHandleAuthority.Observe),
            Command = new() { FileName = "fixture" }
        };

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public ValueTask StopAsync(
            ProcessStopRequest request,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask<ProcessInvocationResult> WaitAsync(
            CancellationToken cancellationToken = default)
        {
            WaitCount++;
            Observed.TrySetResult();
            return ValueTask.FromResult(new ProcessInvocationResult
            {
                ExitCode = 0,
                CompletionKind = ProcessCompletionKind.Exited,
                Output = new()
                {
                    Stdout = new(),
                    Stderr = new(),
                    OutputDrainTimeout = TimeSpan.Zero
                }
            });
        }

        public async IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new(
                Handle,
                ProcessOutputStream.Stdout,
                1,
                DateTimeOffset.UtcNow,
                System.Text.Encoding.UTF8.GetBytes(output),
                ProcessOutputChunkFlags.Final);
            await Task.CompletedTask;
        }

        public ValueTask WriteStdinAsync(
            ReadOnlyMemory<byte> bytes,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
        public ValueTask CloseStdinAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
        public ValueTask SignalAsync(
            ProcessSignal signal,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
        public ValueTask ResizeTerminalAsync(
            TerminalSpec size,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    private sealed class CallbackOwnedResource(Action dispose) : IDebugOwnedResource
    {
        public string Kind => "callback";
        public string SafeIdentity => "callback";

        public ValueTask StopAsync(
            string reason,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync()
        {
            dispose();
            return ValueTask.CompletedTask;
        }
    }

    private static DebugAdapterStartPlan AdapterPlan(JsonElement arguments) => new()
    {
        Method = DebugAdapterStartMethod.Launch,
        AdapterId = "fixture",
        EnvironmentId = "environment",
        EnvironmentRevision = 1,
        PolicyRevision = 1,
        EndpointCatalogRevision = 1,
        PackageProvenance = new()
        {
            PackageId = "fixture",
            PackageVersion = "1",
            AssemblyName = "fixture"
        },
        TrustDecision = new()
        {
            TrustLevel = DebugAdapterTrustLevel.Trusted,
            PolicyRevision = "1",
            ReasonCode = "TEST"
        },
        CanonicalWorkingDirectory = "/workspace",
        AuthorizationScope = "debug.adapter.launch",
        FilteredEnvironment = new Dictionary<string, string?>(),
        Transport = new()
        {
            Kind = DebugAdapterTransportKind.ApprovedTcpConnect,
            Command = string.Empty,
            EndpointId = "endpoint",
            AuthorizedAddress = "loopback:1",
            AuthorityReference = "authority"
        },
        Arguments = arguments.Clone()
    };
}
