using System.Xml.Linq;
using System.Diagnostics;
using System.Text.RegularExpressions;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Sandbox;
using HPD.Agent.ToolHarness.Coding;
using HPD.Agent.ToolHarness.Coding.Debugging;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol;
using HPD.Agent.Sandbox.Local;
using HPD.Agent.Sandbox.ProcessIsolation;
using HPD.Events.Core;
using HPD.Environment.Contracts;
using HPDOS.ToolHarnesses.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Agent.ToolHarness.Coding.Tests;

[Collection(DebugRealAdapterCollection.Name)]
public sealed class DebugPublicHostedRealAdapterTests
{
    [RealAdapterFact("HPD_NETCOREDBG", "HPD_DOTNET")]
    [Trait("Category", "RealAdapter")]
    public async Task Public_launch_rejects_invalid_exception_filter_before_session_publication()
    {
        await using var fixture = new PublicFixture();
        var project = FixtureProject(
            "HPD-Agent.Harness.Coding.ApplicationRealAdapterFixture");
        await BuildFixtureAsync(project);
        var workspaceRoot = FixtureWorkspace(project);
        var context = fixture.Context(
            "invalid-exception-filter-public",
            "launch",
            workspaceRoot);

        var xml = await new CodingToolHarness().Debug(
            new LaunchDebugOperation(
                new ApplicationProjectDebugTarget(
                    Path.GetDirectoryName(project)!,
                    ProjectPath: project,
                    TargetFramework: "net10.0"),
                AdapterId: "netcoredbg",
                WorkspacePath: workspaceRoot,
                InitialConfiguration: new DebugInitialConfigurationInput(
                    ExceptionBreakpoints:
                    [
                        new DebugExceptionBreakpointInput(
                            "not-an-advertised-filter")
                    ])),
            context,
            CancellationToken.None);

        var root = XDocument.Parse(xml).Root!;
        root.Attribute("success")!.Value.Should().Be("false");
        root.Attribute("kind")!.Value.Should().Be(
            "invalid_exception_filter");
        root.Attribute("available_filter_ids").Should().NotBeNull();
        root.Elements("item").Should().NotBeEmpty();
        fixture.Manager.ListTrees(fixture.Scope).Should().BeEmpty();
        context.ResultMetadata.TryGet<
            IReadOnlyList<DebugExceptionFilterMetadata>>(
            CodingToolMetadataKeys.DebugExceptionFilters,
            out var filters).Should().BeTrue();
        filters.Should().NotBeEmpty();
    }

    [RealAdapterFact("HPD_NETCOREDBG", "HPD_DOTNET")]
    [Trait("Category", "RealAdapter")]
    public async Task Public_executable_launch_rejects_exact_test_artifact()
    {
        await using var fixture = new PublicFixture();
        var project = FixtureProject(
            "HPD-Agent.Harness.Coding.RealAdapterFixture");
        await BuildFixtureAsync(project);
        var workspaceRoot = FixtureWorkspace(project);
        var output = Path.Combine(
            Path.GetDirectoryName(project)!,
            "bin",
            "Debug",
            "net10.0",
            "HPD-Agent.Harness.Coding.RealAdapterFixture.dll");

        var xml = await new CodingToolHarness().Debug(
            new LaunchDebugOperation(
                new ExecutableDebugTarget(output),
                AdapterId: "netcoredbg",
                WorkspacePath: workspaceRoot),
            fixture.Context(
                "test-artifact-public",
                "launch",
                workspaceRoot),
            CancellationToken.None);

        var root = XDocument.Parse(xml).Root!;
        root.Attribute("success")!.Value.Should().Be("false");
        root.Attribute("kind")!.Value.Should().Be(
            "debug_test_artifact_requires_test_target");
        fixture.Manager.ListTrees(fixture.Scope).Should().BeEmpty();
    }

    [RealAdapterFact("HPD_NETCOREDBG", "HPD_DOTNET")]
    [Trait("Category", "RealAdapter")]
    public async Task Public_application_project_resolves_launches_and_inspects_locals()
    {
        await using var fixture = new PublicFixture();
        var project = FixtureProject(
            "HPD-Agent.Harness.Coding.ApplicationRealAdapterFixture");
        await BuildFixtureAsync(project);
        var workspaceRoot = FixtureWorkspace(project);
        var source = Path.Combine(Path.GetDirectoryName(project)!, "Program.cs");
        var launch = new LaunchDebugOperation(
            new ApplicationProjectDebugTarget(
                Path.GetDirectoryName(project)!,
                ProjectPath: project,
                TargetFramework: "net10.0"),
            AdapterId: "netcoredbg",
            WorkspacePath: workspaceRoot,
            StopOnEntry: false,
            InitialConfiguration: new DebugInitialConfigurationInput(
                FunctionBreakpoints:
                [
                    new DebugFunctionBreakpointInput(
                        "HPD.Agent.ToolHarness.Coding.ApplicationRealAdapterFixture.Program.Main")
                ]));
        var launchContext = fixture.Context(
            "application-project-public",
            "launch",
            workspaceRoot);
        var launchXml = await new CodingToolHarness().Debug(
            launch,
            launchContext,
            CancellationToken.None);
        var launchRoot = XDocument.Parse(launchXml).Root!;
        launchRoot.Attribute("success")!.Value.Should().Be(
            "true",
            fixture.DescribeFailure(launchXml, launchContext));
        launchRoot.Attribute("semantic_start")!.Value.Should().Be(
            nameof(DebugSemanticStartKind.DirectLaunch));
        launchRoot.Attribute("adapter_method")!.Value.Should().Be(
            nameof(DebugAdapterStartMethod.Launch));
        var treeId = launchRoot.Attribute("debug_tree_id")!.Value;

        var entry = await WaitForInspectionAsync(
            fixture,
            treeId,
            workspaceRoot,
            "application-project-entry",
            launchXml);
        entry.Attribute("reason")!.Value.Should().Be("breakpoint");
        entry.Attribute("supported_optional_actions").Should().NotBeNull();
        entry.Attribute("unsupported_optional_actions").Should().NotBeNull();
        entry.Attribute("exception_filter_ids").Should().NotBeNull();
        entry.Attribute("supported_optional_actions")!.Value.Should().Contain("getModules");
        entry.Attribute("unsupported_optional_actions")!.Value.Should().NotContain("getModules");
        var modulesContext = fixture.Context(
            "application-project-event-backed-modules",
            "getModules",
            workspaceRoot);
        var modulesXml = await new CodingToolHarness().Debug(
            new GetModulesOperation(treeId, Count: 20),
            modulesContext,
            CancellationToken.None);
        var modulesRoot = XDocument.Parse(modulesXml).Root!;
        modulesRoot.Attribute("success")!.Value.Should().Be("true", modulesXml);
        int.Parse(
                modulesRoot.Attribute("count")!.Value,
                System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeGreaterThan(0);
        modulesRoot.Elements("item").Should().NotBeEmpty();
        modulesRoot.Attribute("module_source")!.Value.Should().Be("RetainedEvents");
        modulesRoot.Attribute("inventory_completeness")!.Value.Should().Be("ObservedOnly");
        modulesRoot.Elements("item").Should().OnlyContain(item =>
            item.Attribute("name") != null &&
            item.Attribute("module_token") == null);
        modulesContext.ResultMetadata.TryGet<DebugModulePageMetadata>(
            CodingToolMetadataKeys.DebugModules,
            out var modulePage).Should().BeTrue();
        modulePage!.Source.Should().Be(DebugModuleInventorySource.RetainedEvents);
        modulePage.Completeness.Should().Be(DebugModuleInventoryCompleteness.ObservedOnly);
        if (modulePage.ContinuationToken is { } moduleContinuation)
        {
            var nextModulesXml = await new CodingToolHarness().Debug(
                new GetModulesOperation(
                    treeId,
                    Count: 20,
                    ContinuationToken: moduleContinuation),
                fixture.Context(
                    "application-project-modules-next-page",
                    "getModules",
                    workspaceRoot),
                CancellationToken.None);
            XDocument.Parse(nextModulesXml).Root!
                .Attribute("success")!.Value.Should().Be("true", nextModulesXml);
        }
        var installSourceXml = await new CodingToolHarness().Debug(
            new SetSourceBreakpointsOperation(
                treeId,
                [new DebugSourceBreakpointInput(source, 14)]),
            fixture.Context(
                "application-project-install-source-breakpoint",
                "setSourceBreakpoints",
                workspaceRoot),
            CancellationToken.None);
        var installSourceRoot = XDocument.Parse(installSourceXml).Root!;
        installSourceRoot.Attribute("success")!.Value.Should().Be("true", installSourceXml);
        installSourceRoot.Attribute("resolved_count")!.Value
            .Should().Be("2");
        installSourceRoot.Attribute("hit_count")!.Value
            .Should().Be("0");
        installSourceRoot.Attribute("unknown_hit_count")!.Value
            .Should().Be("1");
        var entryContinue = await new CodingToolHarness().Debug(
            new ContinueDebugOperation(
                treeId,
                int.Parse(
                    entry.Attribute("thread_id")!.Value,
                    System.Globalization.CultureInfo.InvariantCulture)),
            fixture.Context(
                "application-project-entry-continue",
                "continue",
                workspaceRoot),
            CancellationToken.None);
        var entryContinueRoot = XDocument.Parse(entryContinue).Root!;
        entryContinueRoot.Attribute("success")!.Value.Should().Be("true", entryContinue);
        entryContinueRoot.Attribute("prior_suspension_tokens_invalidated")!.Value
            .Should().Be("true");
        entryContinueRoot.Elements("item").Single().Value
            .Should().Contain("suspension-bound tokens");

        var stop = await WaitForInspectionAsync(
            fixture,
            treeId,
            workspaceRoot,
            "application-project-stop");
        stop.Attribute("reason")!.Value.Should().Be("breakpoint");
        stop.Value.Should().Contain("Program.cs:14");
        stop.Value.Should().Contain("first=40");
        int.Parse(
                stop.Attribute("variable_count")!.Value,
                System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeGreaterThan(0);

        var frameToken = ExtractToken(stop, "frameToken");
        var variablesToken = ExtractToken(stop, "variablesToken");
        var unsupportedDataBreakpointXml = await new CodingToolHarness().Debug(
            new DiscoverDataBreakpointOperation(
                treeId,
                "first",
                FrameToken: frameToken),
            fixture.Context(
                "application-project-data-breakpoint-capability",
                "discoverDataBreakpoint",
                workspaceRoot),
            CancellationToken.None);
        XDocument.Parse(unsupportedDataBreakpointXml).Root!
            .Attribute("kind")!.Value.Should().Be(
                "capability_unavailable",
                unsupportedDataBreakpointXml);

        var setExpressionXml = await new CodingToolHarness().Debug(
            new SetDebugExpressionOperation(
                treeId,
                "first",
                "41",
                FrameToken: frameToken),
            fixture.Context(
                "application-project-set-expression",
                "setExpression",
                workspaceRoot),
            CancellationToken.None);
        AssertMutationInvalidation(setExpressionXml);

        var preservedFrameXml = await new CodingToolHarness().Debug(
            new GetScopesOperation(treeId, frameToken),
            fixture.Context(
                "application-project-preserved-frame",
                "getScopes",
                workspaceRoot),
            CancellationToken.None);
        XDocument.Parse(preservedFrameXml).Root!.Attribute("success")!.Value
            .Should().Be("true", preservedFrameXml);

        var expiredVariablesXml = await new CodingToolHarness().Debug(
            new GetVariablesOperation(treeId, variablesToken),
            fixture.Context(
                "application-project-expired-variables",
                "getVariables",
                workspaceRoot),
            CancellationToken.None);
        XDocument.Parse(expiredVariablesXml).Root!.Attribute("kind")!.Value
            .Should().Be("reference_expired", expiredVariablesXml);

        var mutatedStop = await InspectAsync(
            fixture,
            treeId,
            workspaceRoot,
            "application-project-mutated-stop");
        mutatedStop.Value.Should().Contain("first=41");
        var refreshedVariablesToken = ExtractToken(mutatedStop, "variablesToken");
        var setVariableXml = await new CodingToolHarness().Debug(
            new SetDebugVariableOperation(
                treeId,
                refreshedVariablesToken,
                "first",
                "40"),
            fixture.Context(
                "application-project-set-variable",
                "setVariable",
                workspaceRoot),
            CancellationToken.None);
        AssertMutationInvalidation(setVariableXml);

        var restoredStop = await InspectAsync(
            fixture,
            treeId,
            workspaceRoot,
            "application-project-restored-stop");
        restoredStop.Value.Should().Contain("first=40");

        var clearBreakpointsXml = await new CodingToolHarness().Debug(
            new SetSourceBreakpointsOperation(treeId, []),
            fixture.Context(
                "application-project-clear-breakpoints",
                "setSourceBreakpoints",
                workspaceRoot),
            CancellationToken.None);
        XDocument.Parse(clearBreakpointsXml).Root!.Attribute("success")!.Value
            .Should().Be("true", clearBreakpointsXml);

        var continueXml = await new CodingToolHarness().Debug(
            new ContinueDebugOperation(
                treeId,
                int.Parse(
                    stop.Attribute("thread_id")!.Value,
                    System.Globalization.CultureInfo.InvariantCulture)),
            fixture.Context(
                "application-project-continue",
                "continue",
                workspaceRoot),
            CancellationToken.None);
        XDocument.Parse(continueXml).Root!.Attribute("success")!.Value
            .Should().Be("true", continueXml);
        var terminal = await WaitForTerminalAsync(fixture, treeId);
        string.Concat(terminal.Output.Records.Select(record => record.Text))
            .Should().Contain("42");
    }

    private static void AssertMutationInvalidation(string xml)
    {
        var root = XDocument.Parse(xml).Root!;
        root.Attribute("success")!.Value.Should().Be("true", xml);
        root.Attribute("prior_variable_tokens_invalidated")!.Value.Should().Be("true");
        root.Attribute("frame_tokens_remain_valid")!.Value.Should().Be("true");
        root.Attribute("next_action")!.Value.Should().Be("inspectStop");
        root.Elements("item").Single().Value.Should().Contain("value-location tokens");
    }

    private static async Task<XElement> InspectAsync(
        PublicFixture fixture,
        string treeId,
        string workspaceRoot,
        string callId)
    {
        var xml = await new CodingToolHarness().Debug(
            new InspectDebugStopOperation(
                treeId,
                IncludeVariables: true,
                MaximumVariablesPerScope: 20,
                MaximumFrames: 5),
            fixture.Context(callId, "inspectStop", workspaceRoot),
            CancellationToken.None);
        var root = XDocument.Parse(xml).Root!;
        root.Attribute("success")!.Value.Should().Be("true", xml);
        return root;
    }

    private static string ExtractToken(XElement result, string name)
    {
        var match = Regex.Match(
            result.Value,
            $@"{Regex.Escape(name)}=(?<token>[a-f0-9]{{32}})",
            RegexOptions.CultureInvariant);
        match.Success.Should().BeTrue(
            $"the debugger result should contain {name}: {result}");
        return match.Groups["token"].Value;
    }

    [RealAdapterFact("HPD_NETCOREDBG", "HPD_DOTNET")]
    [Trait("Category", "RealAdapter")]
    public async Task Public_test_target_stops_executes_and_retains_natural_completion()
        => await RunHostedBreakpointAsync(
            "HPD-Agent.Harness.Coding.RealAdapterFixture",
            "HostedDebuggeeTests.cs",
            8,
            "FullyQualifiedName~HostedDebuggeeTests.Hosted_debuggee_executes",
            requireInitiallyPending: false);

    [RealAdapterFact("HPD_NETCOREDBG", "HPD_DOTNET")]
    [Trait("Category", "RealAdapter")]
    public async Task Public_nunit_target_executes_through_the_owned_testhost()
        => await RunHostedBreakpointAsync(
            "HPD-Agent.Harness.Coding.NUnitRealAdapterFixture",
            "HostedNUnitDebuggeeTests.cs",
            10,
            "FullyQualifiedName~HostedNUnitDebuggeeTests.Hosted_nunit_debuggee_executes",
            requireInitiallyPending: false);

    [RealAdapterFact("HPD_NETCOREDBG", "HPD_DOTNET")]
    [Trait("Category", "RealAdapter")]
    public async Task Public_mstest_target_executes_through_the_owned_testhost()
        => await RunHostedBreakpointAsync(
            "HPD-Agent.Harness.Coding.MSTestRealAdapterFixture",
            "HostedMSTestDebuggeeTests.cs",
            11,
            "FullyQualifiedName~HostedMSTestDebuggeeTests.Hosted_mstest_debuggee_executes",
            requireInitiallyPending: false);

    [RealAdapterFact("HPD_NETCOREDBG", "HPD_DOTNET")]
    [Trait("Category", "RealAdapter")]
    public async Task Public_source_generator_breakpoint_transitions_from_pending_and_stops()
        => await RunHostedBreakpointAsync(
            "HPD-Agent.Harness.Coding.RealAdapterFixture",
            "HostedSourceGeneratorTests.cs",
            33,
            "FullyQualifiedName~HostedSourceGeneratorTests." +
            "Source_generator_executes_inside_testhost",
            requireInitiallyPending: true);

    [RealAdapterFact("HPD_NETCOREDBG", "HPD_DOTNET")]
    [Trait("Category", "RealAdapter")]
    public async Task Public_restart_replans_and_owns_a_fresh_runner()
    {
        await using var fixture = new PublicFixture();
        var project = FixtureProject("HPD-Agent.Harness.Coding.RealAdapterFixture");
        await BuildFixtureAsync(project);
        var workspaceRoot = FixtureWorkspace(project);
        var source = Path.Combine(
            Path.GetDirectoryName(project)!,
            "HostedDebuggeeTests.cs");
        var operation = TestLaunch(
            project,
            "FullyQualifiedName~HostedDebuggeeTests.Hosted_debuggee_executes",
            new DebugInitialConfigurationInput(
                SourceBreakpoints:
                [
                    new DebugSourceBreakpointInput(source, 8)
                ]));
        var launchContext = fixture.Context("restart-launch", "launch", workspaceRoot);
        var launchXml = await new CodingToolHarness().Debug(
            operation,
            launchContext,
            CancellationToken.None);
        var launchRoot = XDocument.Parse(launchXml).Root!;
        launchRoot.Attribute("success")!.Value.Should().Be(
            "true",
            fixture.DescribeFailure(launchXml, launchContext));
        var oldTreeId = launchRoot.Attribute("debug_tree_id")!.Value;
        var oldResource = fixture.Manager.ResolveTree(fixture.Scope, oldTreeId)
            .OwnedResources.OfType<DebugOwnedProcessResource>().Single();

        var restartContext = fixture.Context("restart-public", "restart", workspaceRoot);
        var restartXml = await new CodingToolHarness().Debug(
            new RestartDebugOperation(oldTreeId),
            restartContext,
            CancellationToken.None);
        var restartRoot = XDocument.Parse(restartXml).Root!;
        restartRoot.Attribute("success")!.Value.Should().Be(
            "true",
            fixture.DescribeFailure(restartXml, restartContext));
        var newTreeId = restartRoot.Attribute("debug_tree_id")!.Value;
        newTreeId.Should().NotBe(oldTreeId);
        var newResource = fixture.Manager.ResolveTree(fixture.Scope, newTreeId)
            .OwnedResources.OfType<DebugOwnedProcessResource>().Single();
        newResource.Should().NotBeSameAs(oldResource);
        fixture.Manager.ListTrees(fixture.Scope)
            .Should().NotContain(tree =>
                tree.Ownership.DebugTreeId == oldTreeId);

        var terminateXml = await new CodingToolHarness().Debug(
            new TerminateDebugOperation(newTreeId),
            fixture.Context("restart-terminate", "terminate", workspaceRoot),
            CancellationToken.None);
        XDocument.Parse(terminateXml).Root!.Attribute("success")!.Value
            .Should().Be("true", terminateXml);
    }

    [RealAdapterFact("HPD_NETCOREDBG", "HPD_DOTNET")]
    [Trait("Category", "RealAdapter")]
    public async Task Public_cancellation_after_runner_start_leaks_no_tree_or_handle()
    {
        using var cancellation = new CancellationTokenSource();
        await using var fixture = new PublicFixture(
            process => new CancelAfterHostStartProcessProvider(
                process,
                cancellation));
        var project = FixtureProject("HPD-Agent.Harness.Coding.RealAdapterFixture");
        await BuildFixtureAsync(project);
        var workspaceRoot = FixtureWorkspace(project);

        var xml = await new CodingToolHarness().Debug(
            TestLaunch(
                project,
                "FullyQualifiedName~HostedDebuggeeTests.Hosted_debuggee_executes",
                initialConfiguration: null),
            fixture.Context("cancel-host-public", "launch", workspaceRoot),
            cancellation.Token);

        var root = XDocument.Parse(xml).Root!;
        root.Attribute("success")!.Value.Should().Be("false", xml);
        root.Attribute("kind")!.Value.Should().Be(
            "debug_activation_cancelled",
            xml);
        fixture.Manager.ListTrees(fixture.Scope).Should().BeEmpty();
        fixture.Backgrounds.ListHandles(new()).Should().BeEmpty();
    }

    [RealAdapterFact("HPD_NETCOREDBG", "HPD_DOTNET")]
    [Trait("Category", "RealAdapter")]
    public async Task Public_testhost_crash_produces_bounded_terminal_evidence()
    {
        await using var fixture = new PublicFixture();
        var project = FixtureProject("HPD-Agent.Harness.Coding.RealAdapterFixture");
        await BuildFixtureAsync(project);
        var workspaceRoot = FixtureWorkspace(project);
        var source = Path.Combine(
            Path.GetDirectoryName(project)!,
            "HostedCrashTests.cs");
        var launchContext = fixture.Context(
            "testhost-crash-public",
            "launch",
            workspaceRoot);
        var launchXml = await new CodingToolHarness().Debug(
            TestLaunch(
                project,
                "FullyQualifiedName~HostedCrashTests." +
                "Testhost_crashes_after_debugger_attachment",
                new DebugInitialConfigurationInput(
                    SourceBreakpoints:
                    [
                        new DebugSourceBreakpointInput(source, 7)
                    ])),
            launchContext,
            CancellationToken.None);
        var launchRoot = XDocument.Parse(launchXml).Root!;
        launchRoot.Attribute("success")!.Value.Should().Be(
            "true",
            fixture.DescribeFailure(launchXml, launchContext));
        var treeId = launchRoot.Attribute("debug_tree_id")!.Value;
        await ReleaseHostedHostAsync(
            fixture,
            treeId,
            workspaceRoot,
            "testhost-crash");
        var stop = await WaitForInspectionAsync(
            fixture,
            treeId,
            workspaceRoot,
            "testhost-crash-stop");
        var resumeXml = await new CodingToolHarness().Debug(
            new ContinueDebugOperation(
                treeId,
                int.Parse(
                    stop.Attribute("thread_id")!.Value,
                    System.Globalization.CultureInfo.InvariantCulture)),
            fixture.Context(
                "testhost-crash-resume",
                "continue",
                workspaceRoot),
            CancellationToken.None);
        XDocument.Parse(resumeXml).Root!.Attribute("success")!.Value
            .Should().Be("true", resumeXml);
        var crashStop = await WaitForInspectionAsync(
            fixture,
            treeId,
            workspaceRoot,
            "testhost-crash-exception");
        crashStop.Attribute("reason")!.Value.Should().BeOneOf(
            "exception",
            "breakpoint");
        var crashContinueXml = await new CodingToolHarness().Debug(
            new ContinueDebugOperation(
                treeId,
                int.Parse(
                    crashStop.Attribute("thread_id")!.Value,
                    System.Globalization.CultureInfo.InvariantCulture)),
            fixture.Context(
                "testhost-crash-final-continue",
                "continue",
                workspaceRoot),
            CancellationToken.None);
        XDocument.Parse(crashContinueXml).Root!.Attribute("success")!.Value
            .Should().Be("true", crashContinueXml);

        var terminal = await WaitForTerminalAsync(fixture, treeId);
        terminal.FinalStatus.Should().BeOneOf("Terminated", "Faulted");
        terminal.Output.RetainedBytes.Should().BeLessThanOrEqualTo(16 * 1024);
        string.Concat(terminal.Output.Records.Select(record => record.Text))
            .Should().Contain("crash qualification");
        fixture.Manager.ListTrees(fixture.Scope).Should().BeEmpty();
    }

    [RealAdapterFact("HPD_NETCOREDBG", "HPD_DOTNET")]
    [Trait("Category", "RealAdapter")]
    public async Task Public_runner_crash_disposes_the_attached_tree()
    {
        await using var fixture = new PublicFixture();
        var project = FixtureProject("HPD-Agent.Harness.Coding.RealAdapterFixture");
        await BuildFixtureAsync(project);
        var workspaceRoot = FixtureWorkspace(project);
        var launchContext = fixture.Context(
            "runner-crash-public",
            "launch",
            workspaceRoot);
        var launchXml = await new CodingToolHarness().Debug(
            TestLaunch(
                project,
                "FullyQualifiedName~HostedDebuggeeTests.Hosted_debuggee_executes",
                initialConfiguration: null),
            launchContext,
            CancellationToken.None);
        var launchRoot = XDocument.Parse(launchXml).Root!;
        launchRoot.Attribute("success")!.Value.Should().Be(
            "true",
            fixture.DescribeFailure(launchXml, launchContext));
        var treeId = launchRoot.Attribute("debug_tree_id")!.Value;
        var runner = fixture.Manager.ResolveTree(fixture.Scope, treeId)
            .OwnedResources.OfType<DebugOwnedProcessResource>().Single();

        await runner.Process.StopAsync(new(
            StopKind.Kill,
            "runner-crash-qualification"));

        var terminal = await WaitForTerminalAsync(fixture, treeId);
        terminal.FinalStatus.Should().BeOneOf("Terminated", "Faulted");
        fixture.Manager.ListTrees(fixture.Scope).Should().BeEmpty();
    }

    [RealAdapterFact("HPD_NETCOREDBG", "HPD_DOTNET")]
    [Trait("Category", "RealAdapter")]
    public async Task Repeated_public_hosted_lifecycles_leave_no_live_tree()
    {
        for (var iteration = 0; iteration < 3; iteration++)
        {
            await RunHostedBreakpointAsync(
                "HPD-Agent.Harness.Coding.RealAdapterFixture",
                "HostedDebuggeeTests.cs",
                8,
                "FullyQualifiedName~HostedDebuggeeTests.Hosted_debuggee_executes",
                requireInitiallyPending: false);
        }
    }

    private static async Task RunHostedBreakpointAsync(
        string fixtureDirectoryName,
        string sourceFileName,
        int breakpointLine,
        string filter,
        bool requireInitiallyPending)
    {
        await using var fixture = new PublicFixture();
        var project = FixtureProject(fixtureDirectoryName);
        await BuildFixtureAsync(project);
        var source = Path.Combine(
            Path.GetDirectoryName(project)!,
            sourceFileName);
        var workspaceRoot = FixtureWorkspace(project);
        var launch = TestLaunch(
            project,
            filter,
            new DebugInitialConfigurationInput(
                SourceBreakpoints:
                [
                    new DebugSourceBreakpointInput(source, breakpointLine)
                ]));

        var launchContext = fixture.Context("launch-public", "launch", workspaceRoot);
        var xml = await new CodingToolHarness().Debug(
            launch,
            launchContext,
            CancellationToken.None);

        var root = XDocument.Parse(xml).Root!;
        root.Attribute("success")!.Value.Should().Be(
            "true",
            fixture.DescribeFailure(xml, launchContext));
        root.Attribute("semantic_start")!.Value
            .Should().Be(nameof(DebugSemanticStartKind.HostedLaunchAttach));
        root.Attribute("adapter_method")!.Value
            .Should().Be(nameof(DebugAdapterStartMethod.Attach));
        if (requireInitiallyPending)
            int.Parse(
                    root.Attribute("pending_breakpoints")!.Value,
                    System.Globalization.CultureInfo.InvariantCulture)
                .Should().BeGreaterThan(0, xml);
        var treeId = root.Attribute("debug_tree_id")!.Value;
        var breakpointsXml = await new CodingToolHarness().Debug(
            new GetDebugBreakpointsOperation(treeId),
            fixture.Context(
                "breakpoints-public",
                "getBreakpoints",
                workspaceRoot),
            CancellationToken.None);
        var threadsXml = await new CodingToolHarness().Debug(
            new GetThreadsOperation(treeId),
            fixture.Context(
                "threads-public",
                "getThreads",
                workspaceRoot),
            CancellationToken.None);
        var initialThreadId = int.Parse(
            XDocument.Parse(threadsXml).Root!.Elements("item").First().Value
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)[0],
            System.Globalization.CultureInfo.InvariantCulture);
        var pauseXml = await new CodingToolHarness().Debug(
            new PauseDebugOperation(treeId, initialThreadId),
            fixture.Context("pause-host-public", "pause", workspaceRoot),
            CancellationToken.None);
        XDocument.Parse(pauseXml).Root!.Attribute("success")!.Value
            .Should().Be("true", pauseXml);
        initialThreadId = fixture.Manager.ResolveTree(fixture.Scope, treeId)
            .SelectSession(null).State.PrimaryStoppedThreadId ?? initialThreadId;
        var continueXml = await new CodingToolHarness().Debug(
            new ContinueDebugOperation(treeId, initialThreadId),
            fixture.Context("continue-public", "continue", workspaceRoot),
            CancellationToken.None);
        var continueRoot = XDocument.Parse(continueXml).Root!;
        continueRoot.Attribute("success")!.Value.Should().Be(
            "true",
            continueXml);

        var breakpoint = await WaitForInspectionAsync(
            fixture,
            treeId,
            workspaceRoot,
            "inspect-breakpoint");
        breakpoint.Attribute("reason")!.Value.Should().Be("breakpoint");
        breakpoint.Value.Should().Contain($"{sourceFileName}:{breakpointLine}");
        int.Parse(
                breakpoint.Attribute("frame_count")!.Value,
                System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeGreaterThan(0);
        int.Parse(
                breakpoint.Attribute("scope_count")!.Value,
                System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeGreaterThan(0);
        int.Parse(
                breakpoint.Attribute("variable_count")!.Value,
                System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeGreaterThan(0);

        var resumeXml = await new CodingToolHarness().Debug(
            new ContinueDebugOperation(
                treeId,
                int.Parse(
                    breakpoint.Attribute("thread_id")!.Value,
                    System.Globalization.CultureInfo.InvariantCulture)),
            fixture.Context("resume-public", "continue", workspaceRoot),
            CancellationToken.None);
        XDocument.Parse(resumeXml).Root!
            .Attribute("success")!.Value.Should().Be("true", resumeXml);

        var terminal = await WaitForTerminalAsync(fixture, treeId);
        terminal.FinalStatus.Should().Be("Terminated");
        terminal.SafeReasonCode.Should().BeOneOf(
            "PROCESS_EXITED",
            "ADAPTER_TERMINATED");
        string.Concat(terminal.Output.Records.Select(record => record.Text))
            .Should().Contain("Passed!");

        var statusXml = await new CodingToolHarness().Debug(
            new GetDebugStatusOperation(treeId),
            fixture.Context("terminal-status-public", "getStatus", workspaceRoot),
            CancellationToken.None);
        var snapshotXml = await new CodingToolHarness().Debug(
            new SnapshotDebugOperation(treeId),
            fixture.Context("terminal-snapshot-public", "snapshot", workspaceRoot),
            CancellationToken.None);
        var outputXml = await new CodingToolHarness().Debug(
            new GetDebugOutputOperation(treeId),
            fixture.Context("terminal-output-public", "getOutput", workspaceRoot),
            CancellationToken.None);
        var terminalBreakpointsXml = await new CodingToolHarness().Debug(
            new GetDebugBreakpointsOperation(treeId),
            fixture.Context("terminal-breakpoints-public", "getBreakpoints", workspaceRoot),
            CancellationToken.None);
        XDocument.Parse(statusXml).Root!.Attribute("retained")!.Value
            .Should().Be("true", statusXml);
        XDocument.Parse(snapshotXml).Root!.Attribute("retained")!.Value
            .Should().Be("true", snapshotXml);
        var terminalBreakpoints = XDocument.Parse(terminalBreakpointsXml).Root!;
        terminalBreakpoints.Attribute("retained")!.Value.Should().Be("true");
        terminalBreakpoints.Attribute("details_retained")!.Value.Should().Be("false");
        terminalBreakpoints.Attribute("debug_session_id").Should().BeNull();
        terminalBreakpoints.Attribute("source_count").Should().BeNull();
        terminalBreakpoints.Attribute("function_count").Should().BeNull();
        outputXml.Should().Contain("Passed!");

        fixture.Manager.ListTrees(fixture.Scope).Should().BeEmpty();
        fixture.Backgrounds.ListHandles(new()).Should().ContainSingle();
    }

    private static LaunchDebugOperation TestLaunch(
        string project,
        string filter,
        DebugInitialConfigurationInput? initialConfiguration)
        => new(
            new TestDebugTarget(
                project,
                DebugTestFramework.DotNet,
                Filter: filter,
                TargetFramework: "net10.0"),
            AdapterId: "netcoredbg",
            WorkspacePath: FixtureWorkspace(project),
            StopOnEntry: true,
            InitialConfiguration: initialConfiguration);

    private static string FixtureProject(string fixtureDirectoryName)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            fixtureDirectoryName,
            $"{fixtureDirectoryName}.csproj"));

    private static string FixtureWorkspace(string project)
        => Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(project)!,
            "..",
            ".."));

    private static async Task<XElement> WaitForInspectionAsync(
        PublicFixture fixture,
        string treeId,
        string workspaceRoot,
        string callPrefix,
        string? diagnostic = null)
    {
        string? last = null;
        for (var attempt = 0; attempt < 150; attempt++)
        {
            var xml = await new CodingToolHarness().Debug(
                new InspectDebugStopOperation(treeId),
                fixture.Context(
                    $"{callPrefix}-{attempt}",
                    "inspectStop",
                    workspaceRoot),
                CancellationToken.None);
            last = xml;
            var root = XDocument.Parse(xml).Root!;
            if (root.Attribute("success")?.Value == "true")
                return root;
            if (fixture.Manager.TryResolveTerminal(
                    fixture.Scope,
                    treeId,
                    out var terminal))
                throw new InvalidOperationException(
                    $"The public debugger terminated before exposing a stopped thread. " +
                    $"Last result: {last}. Diagnostic: {diagnostic}; " +
                    $"terminal_reason={terminal.SafeReasonCode}; " +
                    $"terminal_status={terminal.FinalStatus}; " +
                    $"output={string.Concat(terminal.Output.Records.Select(record => record.Text))}");
            await Task.Delay(100);
        }

        var threads = await new CodingToolHarness().Debug(
            new GetThreadsOperation(treeId),
            fixture.Context(
                $"{callPrefix}-failure-threads",
                "getThreads",
                workspaceRoot),
            CancellationToken.None);
        var breakpoints = await new CodingToolHarness().Debug(
            new GetDebugBreakpointsOperation(treeId),
            fixture.Context(
                $"{callPrefix}-failure-breakpoints",
                "getBreakpoints",
                workspaceRoot),
            CancellationToken.None);
        throw new InvalidOperationException(
            $"The public debugger did not expose a stopped thread. Last result: {last}. " +
            $"Diagnostic: {diagnostic}; threads={threads}; breakpoints={breakpoints}; " +
            $"host={fixture.DescribeHost(treeId)}");
    }

    private static async Task<DebugTerminalRecord> WaitForTerminalAsync(
        PublicFixture fixture,
        string treeId)
    {
        for (var attempt = 0; attempt < 150; attempt++)
        {
            if (fixture.Manager.TryResolveTerminal(
                    fixture.Scope,
                    treeId,
                    out var terminal))
                return terminal;
            await Task.Delay(100);
        }

        throw new InvalidOperationException(
            $"The public debug tree did not reach terminal retention; " +
            $"host={fixture.DescribeHost(treeId)}");
    }

    private static async Task ReleaseHostedHostAsync(
        PublicFixture fixture,
        string treeId,
        string workspaceRoot,
        string callPrefix)
    {
        var threadsXml = await new CodingToolHarness().Debug(
            new GetThreadsOperation(treeId),
            fixture.Context(
                $"{callPrefix}-threads",
                "getThreads",
                workspaceRoot),
            CancellationToken.None);
        var threadId = int.Parse(
            XDocument.Parse(threadsXml).Root!.Elements("item").First().Value
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)[0],
            System.Globalization.CultureInfo.InvariantCulture);
        var pauseXml = await new CodingToolHarness().Debug(
            new PauseDebugOperation(treeId, threadId),
            fixture.Context($"{callPrefix}-pause", "pause", workspaceRoot),
            CancellationToken.None);
        XDocument.Parse(pauseXml).Root!.Attribute("success")!.Value
            .Should().Be("true", pauseXml);
        threadId = fixture.Manager.ResolveTree(fixture.Scope, treeId)
            .SelectSession(null).State.PrimaryStoppedThreadId ?? threadId;
        var continueXml = await new CodingToolHarness().Debug(
            new ContinueDebugOperation(treeId, threadId),
            fixture.Context($"{callPrefix}-continue", "continue", workspaceRoot),
            CancellationToken.None);
        XDocument.Parse(continueXml).Root!.Attribute("success")!.Value
            .Should().Be("true", continueXml);
    }

    private static async Task BuildFixtureAsync(string project)
    {
        var start = new ProcessStartInfo
        {
            FileName = System.Environment.GetEnvironmentVariable("HPD_DOTNET")!,
            WorkingDirectory = Path.GetDirectoryName(project)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add("build");
        start.ArgumentList.Add(project);
        start.ArgumentList.Add("--nologo");
        start.ArgumentList.Add("--verbosity");
        start.ArgumentList.Add("quiet");
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("The fixture build did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        process.ExitCode.Should().Be(
            0,
            $"fixture build stdout: {await stdout}; stderr: {await stderr}");
    }

    private sealed class PublicFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly EventCoordinator _events = new();
        private readonly SandboxIsolationManager _isolation = new();
        private readonly IProcessProvider _process;

        public PublicFixture(
            Func<IProcessProvider, IProcessProvider>? decorateProcess = null)
        {
            var localProcess = new LocalProcessProvider(
                new SandboxIsolationPlanner(),
                new HostSandboxApplicator(_isolation));
            _process = decorateProcess?.Invoke(localProcess) ?? localProcess;
            Manager = new DebugSessionManager(
                new DebugTerminalRecordStore(new DebugTerminalRecordStoreOptions()));
            Scope = new(Manager.RuntimeId, "session", "thread");
            Backgrounds = new();
            var services = new ServiceCollection();
            services.AddHPDCodingDebugging();
            services.AddHPDBuiltInDebugAdapters();
            services.Replace(ServiceDescriptor.Singleton<
                IDebugAdapterTrustPolicy, AllowQualificationTrustPolicy>());
            _services = services.BuildServiceProvider();
        }

        public DebugSessionManager Manager { get; }
        public DebugTreeLookupScope Scope { get; }
        public BackgroundRegistry Backgrounds { get; }

        public string DescribeHost(string treeId)
        {
            var tree = Manager.ResolveTree(Scope, treeId);
            var output = tree.OwnedResources
                .OfType<DebugOwnedProcessResource>()
                .Select(resource => resource.OutputSnapshot)
                .FirstOrDefault();
            return output is null
                ? "none"
                : $"stdout={output.Stdout}; stderr={output.Stderr}; " +
                  $"retained={output.RetainedBytes}; dropped={output.DroppedBytes}";
        }

        public string DescribeFailure(
            string xml,
            FunctionExecutionContext context)
        {
            if (!context.ResultMetadata.TryGet<string>(
                    CodingToolMetadataKeys.DebugAdapterDiagnosticReference,
                    out var reference) ||
                !_services.GetRequiredService<IDebugAdapterDiagnosticStore>()
                    .TryGet(reference, out var diagnostic))
                return xml;
            return $"{xml}; adapter phase={diagnostic.Phase}; " +
                   $"exit={diagnostic.ExitCode}; stderr={diagnostic.StandardError}";
        }

        public FunctionExecutionContext Context(
            string callId,
            string action,
            string workspaceRoot)
        {
            var workspace = new AgentWorkspace(
                "root",
                workspaceRoot,
                [new("root", workspaceRoot)]);
            var runConfig = new AgentRunConfig
            {
                Context = new AgentContextRunConfig { Properties = new Dictionary<string, object>
                {
                    [AgentWorkspace.ContextKey] = workspace
                } }
            };
            var initial = AgentLoopState.InitialSafe(
                [], "run", "conversation", "DebugPublicReal");
            var state = initial with
            {
                MiddlewareState = initial.MiddlewareState.SetState(
                    typeof(DebugPermissionStateData).FullName!,
                    new DebugPermissionStateData().WithDecision(
                        callId,
                        action,
                        DebugPermissionMiddleware.Classify(action)))
            };
            var session = new Session("session");
            var thread = new HPD.Agent.Thread("session", "debug-public")
            {
                Id = "thread"
            };
            var agent = new AgentContext(
                "DebugPublicReal",
                "conversation",
                state,
                _events,
                session,
                thread,
                CancellationToken.None,
                services: _services);
            agent.RuntimeCapabilities.Set<IDebugSessionManager>(Manager);
            agent.RuntimeCapabilities.Set(new DebugRuntimeBindingState());
            agent.RuntimeCapabilities.Set(new RuntimeProcessExecutionBinding
            {
                EnvironmentId = "local",
                EnvironmentRevision = 1,
                ProcessProvider = _process,
                ExecutionTarget = new(
                    new TargetRoute
                    {
                        Kind = new("local.execution"),
                        Scope = new("qualification")
                    },
                    TargetHandleLifetime.LiveCapability,
                    TargetHandleAuthority.Control |
                    TargetHandleAuthority.Observe)
            });
            var function = AIFunctionFactory.Create(
                () => "ok",
                new AIFunctionFactoryOptions
                {
                    Name = "Debug",
                    Description = "qualification"
                });
            var before = agent.AsBeforeFunction(
                function,
                callId,
                new Dictionary<string, object?>(),
                runConfig,
                nameof(CodingToolHarness),
                backgroundHandles: Backgrounds);
            return new FunctionExecutionContext(before, new FunctionRequest
            {
                Function = function,
                CallId = callId,
                Arguments = new Dictionary<string, object?>(),
                State = state,
                RunConfig = runConfig,
                ResultMetadata = new ToolResultMetadata(),
                EventCoordinator = _events,
                BackgroundHandles = Backgrounds
            });
        }

        public async ValueTask DisposeAsync()
        {
            await Manager.DisposeAsync();
            await _services.DisposeAsync();
            await _isolation.DisposeAsync();
            // dotnet test may reap its detached testhost shortly after the
            // owning runner exits. Keep independent real-adapter cases from
            // racing that bounded operating-system teardown.
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            _events.Dispose();
        }
    }

    private sealed class AllowQualificationTrustPolicy : IDebugAdapterTrustPolicy
    {
        public DebugAdapterTrustDecision Evaluate(DebugAdapterDescriptor descriptor)
            => new()
            {
                TrustLevel = DebugAdapterTrustLevel.Trusted,
                PolicyRevision = "qualification",
                ReasonCode = "QUALIFIED_BUILT_IN"
            };
    }

    private sealed class CancelAfterHostStartProcessProvider(
        IProcessProvider inner,
        CancellationTokenSource cancellation) : IProcessProvider
    {
        public ProviderId ProviderId => inner.ProviderId;

        public async ValueTask<IProcessInvocationHandle> StartAsync(
            ProcessInvocationSpec spec,
            IProcessOutputSink? output = null,
            CancellationToken cancellationToken = default)
        {
            var handle = await inner.StartAsync(spec, output, cancellationToken);
            if (spec.Command.Arguments.FirstOrDefault() == "test")
                cancellation.Cancel();
            return handle;
        }

        public ValueTask<ProcessInvocationResult> RunAsync(
            ProcessInvocationSpec spec,
            IProcessOutputSink? output = null,
            CancellationToken cancellationToken = default)
            => inner.RunAsync(spec, output, cancellationToken);

        public ValueTask SignalAsync(
            TargetHandle<ProcessInvocation> process,
            ProcessSignal signal,
            CancellationToken cancellationToken = default)
            => inner.SignalAsync(process, signal, cancellationToken);

        public ValueTask ResizeTerminalAsync(
            TargetHandle<ProcessInvocation> process,
            TerminalSpec size,
            CancellationToken cancellationToken = default)
            => inner.ResizeTerminalAsync(process, size, cancellationToken);

        public ValueTask<ProcessInvocationResult> WaitAsync(
            TargetHandle<ProcessInvocation> process,
            CancellationToken cancellationToken = default)
            => inner.WaitAsync(process, cancellationToken);

        public IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(
            TargetHandle<ProcessInvocation> process,
            CancellationToken cancellationToken = default)
            => inner.ReadOutputAsync(process, cancellationToken);
    }

    private sealed class BackgroundRegistry : IAgentBackgroundHandleRegistry
    {
        private readonly Dictionary<string, RegisteredBackgroundHandle> _handles = [];

        public ValueTask<BackgroundHandleRegistration> RegisterHandleAsync(
            BackgroundHandleDescriptor descriptor,
            IBackgroundHandle handle,
            CancellationToken cancellationToken = default)
        {
            var id = descriptor.HandleId!;
            _handles[id] = new(id, descriptor, handle, DateTimeOffset.UtcNow);
            return ValueTask.FromResult(new BackgroundHandleRegistration(
                id,
                descriptor.Name,
                descriptor.Kind,
                descriptor.SourceKind));
        }

        public bool TryGetHandle(
            string handleId,
            BackgroundHandleScope scope,
            out RegisteredBackgroundHandle handle)
            => _handles.TryGetValue(handleId, out handle!);

        public IReadOnlyList<RegisteredBackgroundHandle> ListHandles(
            BackgroundHandleQuery query)
            => _handles.Values.ToArray();
    }
}
