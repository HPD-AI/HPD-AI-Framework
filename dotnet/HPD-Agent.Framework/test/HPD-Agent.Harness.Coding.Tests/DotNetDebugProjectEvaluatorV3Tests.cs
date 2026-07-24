using System.Text;
using FluentAssertions;
using HPD.Agent.Middleware;
using HPD.Agent.Sandbox;
using HPD.Agent.Sandbox.Local;
using HPD.Agent.Sandbox.ProcessIsolation;
using HPD.Agent.ToolHarness.Coding.Debugging;
using HPD.Environment.Contracts;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class DotNetDebugProjectEvaluatorV3Tests
{
    [Theory]
    [InlineData("Exe", false, false, "Application", "None")]
    [InlineData("Library", false, false, "Library", "None")]
    [InlineData("Library", true, false, "Test", "VSTest")]
    [InlineData("Exe", true, true, "Test", "MicrosoftTestingPlatform")]
    public async Task Evaluation_classifies_the_evaluated_execution_shape(
        string outputType,
        bool isTest,
        bool isMtp,
        string expectedKind,
        string expectedPlatform)
    {
        using var fixture = new EvaluationFixture();
        var provider = new EvaluationProcessProvider(spec =>
            fixture.Json(outputType, isTest, isMtp, "net10.0"));

        var result = await new DotNetDebugProjectEvaluator().EvaluateAsync(
            fixture.Request(provider),
            CancellationToken.None);

        result.ProjectKind.ToString().Should().Be(expectedKind);
        result.TestPlatform.ToString().Should().Be(expectedPlatform);
        result.SelectedTargetFramework.Should().Be("net10.0");
        result.TargetPath.Should().Be(fixture.TargetPath);
        result.ArtifactIsCurrent.Should().BeTrue();
        provider.Specifications.Should().OnlyContain(spec =>
            spec.Command.FileName == "dotnet" &&
            spec.Command.Arguments.Contains("msbuild") &&
            spec.Isolation.Network == NetworkEgressPolicy.Blocked);
    }

    [Fact]
    public async Task Evaluation_rejects_an_ambiguous_target_framework()
    {
        using var fixture = new EvaluationFixture();
        var provider = new EvaluationProcessProvider(_ =>
            fixture.Json("Exe", false, false, "net9.0;net10.0"));

        var action = () => new DotNetDebugProjectEvaluator().EvaluateAsync(
            fixture.Request(provider),
            CancellationToken.None).AsTask();

        var exception = await action.Should().ThrowAsync<DebugStartPlanningException>();
        exception.Which.Kind.Should().Be("debug_target_framework_ambiguous");
    }

    [Fact]
    public async Task Evaluation_honors_the_invocation_wide_disabled_process_sandbox()
    {
        using var fixture = new EvaluationFixture();
        var provider = new EvaluationProcessProvider(_ =>
            fixture.Json("Exe", false, false, "net10.0"));

        await new DotNetDebugProjectEvaluator().EvaluateAsync(
            fixture.Request(provider) with
            {
                ProcessSandbox = new AgentProcessSandboxPolicy
                {
                    Mode = AgentProcessIsolationMode.Disabled
                }
            },
            CancellationToken.None);

        provider.Specifications.Should().OnlyContain(spec =>
            spec.Isolation.Mode == ProcessIsolationMode.Disabled);
    }

    [Fact]
    public async Task Evaluation_accepts_an_explicit_declared_target_framework()
    {
        using var fixture = new EvaluationFixture();
        var provider = new EvaluationProcessProvider(spec =>
            fixture.Json("Exe", false, false,
                spec.Command.Arguments.Contains("-p:TargetFramework=net10.0")
                    ? "net10.0"
                    : "net9.0;net10.0"));

        var result = await new DotNetDebugProjectEvaluator().EvaluateAsync(
            fixture.Request(provider) with { TargetFramework = "net10.0" },
            CancellationToken.None);

        result.SelectedTargetFramework.Should().Be("net10.0");
        provider.Specifications.Should().HaveCount(2);
        provider.Specifications[1].Command.Arguments.Should()
            .Contain("-p:TargetFramework=net10.0");
    }

    [Fact]
    public async Task Evaluation_rejects_an_explicit_undeclared_target_framework()
    {
        using var fixture = new EvaluationFixture();
        var provider = new EvaluationProcessProvider(_ =>
            fixture.Json("Exe", false, false, "net9.0;net10.0"));

        var action = () => new DotNetDebugProjectEvaluator().EvaluateAsync(
            fixture.Request(provider) with { TargetFramework = "net8.0" },
            CancellationToken.None).AsTask();

        var exception = await action.Should().ThrowAsync<DebugStartPlanningException>();
        exception.Which.Kind.Should().Be("debug_target_framework_invalid");
    }

    [Fact]
    public async Task Evaluation_rejects_projects_outside_the_workspace()
    {
        using var fixture = new EvaluationFixture();
        var outsideWorkspace = Path.Combine(fixture.Root, "authorized");
        Directory.CreateDirectory(outsideWorkspace);
        var provider = new EvaluationProcessProvider(_ =>
            fixture.Json("Exe", false, false, "net10.0"));

        var action = () => new DotNetDebugProjectEvaluator().EvaluateAsync(
            fixture.Request(provider) with
            {
                Workspace = Workspace(outsideWorkspace)
            },
            CancellationToken.None).AsTask();

        var exception = await action.Should().ThrowAsync<DebugStartPlanningException>();
        exception.Which.Kind.Should().Be("debug_project_outside_workspace");
        provider.Specifications.Should().BeEmpty();
    }

    [Fact]
    public async Task Evaluation_rejects_malformed_msbuild_output()
    {
        using var fixture = new EvaluationFixture();
        var provider = new EvaluationProcessProvider(_ => "{ definitely-not-json }");

        var action = () => new DotNetDebugProjectEvaluator().EvaluateAsync(
            fixture.Request(provider),
            CancellationToken.None).AsTask();

        var exception = await action.Should().ThrowAsync<DebugStartPlanningException>();
        exception.Which.Kind.Should().Be("debug_project_evaluation_failed");
    }

    [Fact]
    public async Task Evaluation_marks_a_newer_source_as_build_required()
    {
        using var fixture = new EvaluationFixture();
        File.SetLastWriteTimeUtc(
            fixture.SourcePath,
            DateTime.UtcNow.AddSeconds(2));
        var provider = new EvaluationProcessProvider(_ =>
            fixture.Json("Exe", false, false, "net10.0"));

        var result = await new DotNetDebugProjectEvaluator().EvaluateAsync(
            fixture.Request(provider),
            CancellationToken.None);

        result.ArtifactIsCurrent.Should().BeFalse();
    }

    [Theory]
    [InlineData("App.csproj")]
    [InlineData("Directory.Build.props")]
    [InlineData("Directory.Build.targets")]
    public async Task Evaluation_marks_newer_build_inputs_as_build_required(
        string inputName)
    {
        using var fixture = new EvaluationFixture();
        var input = inputName == "App.csproj"
            ? fixture.ProjectPath
            : Path.Combine(fixture.Root, inputName);
        if (!File.Exists(input))
            File.WriteAllText(input, "<Project />");
        File.SetLastWriteTimeUtc(input, DateTime.UtcNow.AddSeconds(2));
        var provider = new EvaluationProcessProvider(_ =>
            fixture.Json("Exe", false, false, "net10.0"));

        var result = await new DotNetDebugProjectEvaluator().EvaluateAsync(
            fixture.Request(provider),
            CancellationToken.None);

        result.ArtifactIsCurrent.Should().BeFalse();
    }

    [Fact]
    public async Task Evaluation_uses_the_requested_configuration_and_invariant_environment()
    {
        using var fixture = new EvaluationFixture();
        var provider = new EvaluationProcessProvider(_ =>
            fixture.Json("Exe", false, false, "net10.0"));

        await new DotNetDebugProjectEvaluator().EvaluateAsync(
            fixture.Request(provider) with { Configuration = "Release" },
            CancellationToken.None);

        provider.Specifications.Should().OnlyContain(spec =>
            spec.Command.Arguments.Contains("-p:Configuration=Release") &&
            spec.Command.Environment["DOTNET_CLI_UI_LANGUAGE"] == "en-US" &&
            spec.Command.Environment["MSBUILDTERMINALLOGGER"] == "off" &&
            spec.Policy.Timeout == TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task Real_msbuild_evaluation_honors_imported_and_conditioned_properties()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "hpd-real-msbuild-evaluation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await using var isolation = new SandboxIsolationManager();
        try
        {
            var project = Path.Combine(root, "Imported.csproj");
            File.WriteAllText(
                Path.Combine(root, "Directory.Build.props"),
                """
                <Project>
                  <PropertyGroup>
                    <AssemblyName>ImportedAssembly</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                project,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <OutputType Condition="'$(Configuration)' == 'Debug'">WinExe</OutputType>
                    <OutputType Condition="'$(Configuration)' != 'Debug'">Library</OutputType>
                  </PropertyGroup>
                </Project>
                """);
            var provider = new LocalProcessProvider(
                new SandboxIsolationPlanner(),
                new HostSandboxApplicator(isolation));
            var result = await new DotNetDebugProjectEvaluator().EvaluateAsync(
                new()
                {
                    CanonicalProjectPath = project,
                    Configuration = "Debug",
                    ProcessExecution = new()
                    {
                        EnvironmentId = "local",
                        EnvironmentRevision = 1,
                        ProcessProvider = provider,
                        ExecutionTarget = ExecutionTarget()
                    },
                    Workspace = Workspace(root)
                },
                CancellationToken.None);

            result.AssemblyName.Should().Be("ImportedAssembly");
            result.OutputType.Should().Be("WinExe");
            result.ProjectKind.Should().Be(DotNetDebugProjectKind.Application);
            result.SelectedTargetFramework.Should().Be("net10.0");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData("xunit")]
    [InlineData("NUnit")]
    [InlineData("NUnit3TestAdapter")]
    [InlineData("MSTest.TestFramework")]
    [InlineData("MSTest.TestAdapter")]
    public async Task Known_test_framework_packages_establish_intent_but_not_a_runner(
        string packageId)
    {
        using var fixture = new EvaluationFixture();
        var provider = new EvaluationProcessProvider(_ =>
            fixture.Json("Library", false, false, "net10.0", packageId));

        var result = await new DotNetDebugProjectEvaluator().EvaluateAsync(
            fixture.Request(provider),
            CancellationToken.None);

        result.ProjectKind.Should().Be(DotNetDebugProjectKind.Test);
        result.TestPlatform.Should().Be(DotNetTestPlatformKind.Unknown);
    }

    [Fact]
    public async Task Executable_application_requires_runtime_metadata()
    {
        using var fixture = new EvaluationFixture();
        File.Delete(Path.ChangeExtension(fixture.TargetPath, ".runtimeconfig.json"));
        var provider = new EvaluationProcessProvider(_ =>
            fixture.Json("Exe", false, false, "net10.0"));

        var result = await new DotNetDebugProjectEvaluator().EvaluateAsync(
            fixture.Request(provider),
            CancellationToken.None);

        result.ProjectKind.Should().Be(DotNetDebugProjectKind.Application);
        result.ArtifactIsCurrent.Should().BeFalse();
    }

    [Fact]
    public async Task WinExe_application_selects_the_evaluated_apphost()
    {
        using var fixture = new EvaluationFixture();
        var appHost = Path.Combine(
            Path.GetDirectoryName(fixture.TargetPath)!,
            Path.GetFileNameWithoutExtension(fixture.TargetPath));
        File.WriteAllBytes(appHost, [0]);
        File.SetLastWriteTimeUtc(
            appHost,
            File.GetLastWriteTimeUtc(fixture.TargetPath));
        var provider = new EvaluationProcessProvider(_ =>
            fixture.Json("WinExe", false, false, "net10.0")
                .Replace(
                    "\"UseAppHost\": \"false\"",
                    "\"UseAppHost\": \"true\"",
                    StringComparison.Ordinal));

        var result = await new DotNetDebugProjectEvaluator().EvaluateAsync(
            fixture.Request(provider),
            CancellationToken.None);

        result.ProjectKind.Should().Be(DotNetDebugProjectKind.Application);
        result.AppHostPath.Should().Be(appHost);
        result.ArtifactIsCurrent.Should().BeTrue();
    }

    [Fact]
    public async Task Evaluation_timeout_is_classified_without_parsing_partial_output()
    {
        using var fixture = new EvaluationFixture();
        var provider = new FixedResultProcessProvider(new()
        {
            ExitCode = null,
            CompletionKind = ProcessCompletionKind.TimedOut,
            Output = new()
            {
                Stdout = new()
                {
                    CapturedBytes = Encoding.UTF8.GetBytes(
                        fixture.Json("Exe", false, false, "net10.0"))
                },
                Stderr = new(),
                OutputDrainTimeout = TimeSpan.Zero
            }
        });

        var action = () => new DotNetDebugProjectEvaluator().EvaluateAsync(
            fixture.Request(provider),
            CancellationToken.None).AsTask();

        var exception = await action.Should().ThrowAsync<DebugStartPlanningException>();
        exception.Which.Kind.Should().Be("debug_project_evaluation_failed");
    }

    [Fact]
    public async Task Stale_copied_reference_output_requires_a_rebuild()
    {
        using var fixture = new EvaluationFixture();
        var referenceDirectory = Path.Combine(fixture.Root, "generator");
        Directory.CreateDirectory(referenceDirectory);
        var reference = Path.Combine(referenceDirectory, "Generator.csproj");
        var referenceOutput = Path.Combine(
            referenceDirectory, "bin", "Debug", "net10.0", "Generator.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(referenceOutput)!);
        var consumedCopy = Path.Combine(
            Path.GetDirectoryName(fixture.TargetPath)!,
            "Generator.dll");
        File.WriteAllText(reference, "<Project />");
        File.WriteAllBytes(referenceOutput, [1]);
        File.WriteAllBytes(consumedCopy, [1]);
        var now = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(reference, now.AddSeconds(-3));
        File.SetLastWriteTimeUtc(referenceOutput, now);
        File.SetLastWriteTimeUtc(consumedCopy, now.AddSeconds(-2));
        var provider = new EvaluationProcessProvider(spec =>
            spec.Command.Arguments[1] == fixture.ProjectPath
                ? ProjectJson(fixture.ProjectPath, fixture.TargetPath, [reference])
                : ProjectJson(reference, referenceOutput, []));

        var result = await new DotNetDebugProjectEvaluator().EvaluateAsync(
            fixture.Request(provider),
            CancellationToken.None);

        result.ArtifactIsCurrent.Should().BeFalse();
    }

    [Fact]
    public async Task Analyzer_only_reference_is_validated_without_requiring_a_runtime_copy()
    {
        using var fixture = new EvaluationFixture();
        var analyzerDirectory = Path.Combine(fixture.Root, "generator");
        Directory.CreateDirectory(analyzerDirectory);
        var analyzerProject = Path.Combine(analyzerDirectory, "Generator.csproj");
        var analyzerSource = Path.Combine(analyzerDirectory, "Generator.cs");
        var analyzerOutput = Path.Combine(
            analyzerDirectory, "bin", "Debug", "net10.0", "Generator.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(analyzerOutput)!);
        File.WriteAllText(analyzerProject, "<Project />");
        File.WriteAllText(analyzerSource, "class Generator { }");
        File.WriteAllBytes(analyzerOutput, [1]);
        var targetTime = File.GetLastWriteTimeUtc(fixture.TargetPath);
        File.SetLastWriteTimeUtc(analyzerProject, targetTime.AddSeconds(-3));
        File.SetLastWriteTimeUtc(analyzerSource, targetTime.AddSeconds(-3));
        File.SetLastWriteTimeUtc(analyzerOutput, targetTime.AddSeconds(-1));
        var provider = new EvaluationProcessProvider(spec =>
            spec.Command.Arguments[1] == fixture.ProjectPath
                ? ProjectJson(
                    fixture.ProjectPath,
                    fixture.TargetPath,
                    [analyzerProject],
                    new HashSet<string>(StringComparer.Ordinal)
                    {
                        analyzerProject
                    })
                : ProjectJson(analyzerProject, analyzerOutput, []));

        var result = await new DotNetDebugProjectEvaluator().EvaluateAsync(
            fixture.Request(provider),
            CancellationToken.None);

        result.ArtifactIsCurrent.Should().BeTrue();
        result.ProjectReferenceOutputs.Should().ContainSingle()
            .Which.RequiresRuntimeCopy.Should().BeFalse();
        File.Exists(Path.Combine(
            Path.GetDirectoryName(fixture.TargetPath)!,
            "Generator.dll")).Should().BeFalse();
    }

    [Fact]
    public async Task Runtime_reference_in_another_configured_workspace_root_is_allowed()
    {
        using var fixture = new EvaluationFixture();
        var secondRoot = Path.Combine(
            Path.GetTempPath(),
            "hpd-dotnet-reference-root-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(secondRoot);
            var referenceProject = Path.Combine(secondRoot, "Shared.csproj");
            var referenceSource = Path.Combine(secondRoot, "Shared.cs");
            var referenceOutput = Path.Combine(
                secondRoot, "bin", "Debug", "net10.0", "Shared.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(referenceOutput)!);
            File.WriteAllText(referenceProject, "<Project />");
            File.WriteAllText(referenceSource, "class Shared { }");
            File.WriteAllBytes(referenceOutput, [1]);
            var loadedCopy = Path.Combine(
                Path.GetDirectoryName(fixture.TargetPath)!,
                "Shared.dll");
            File.WriteAllBytes(loadedCopy, [1]);
            var targetTime = File.GetLastWriteTimeUtc(fixture.TargetPath);
            File.SetLastWriteTimeUtc(referenceProject, targetTime.AddSeconds(-3));
            File.SetLastWriteTimeUtc(referenceSource, targetTime.AddSeconds(-3));
            File.SetLastWriteTimeUtc(referenceOutput, targetTime.AddSeconds(-1));
            File.SetLastWriteTimeUtc(loadedCopy, targetTime);
            var provider = new EvaluationProcessProvider(spec =>
                spec.Command.Arguments[1] == fixture.ProjectPath
                    ? ProjectJson(
                        fixture.ProjectPath,
                        fixture.TargetPath,
                        [referenceProject])
                    : ProjectJson(referenceProject, referenceOutput, []));

            var result = await new DotNetDebugProjectEvaluator().EvaluateAsync(
                fixture.Request(provider) with
                {
                    Workspace = Workspace(fixture.Root, secondRoot)
                },
                CancellationToken.None);

            result.ArtifactIsCurrent.Should().BeTrue();
            result.ProjectReferences.Should().ContainSingle()
                .Which.Should().Be(referenceProject);
        }
        finally
        {
            try { Directory.Delete(secondRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Newer_analyzer_output_still_requires_the_application_to_be_rebuilt()
    {
        using var fixture = new EvaluationFixture();
        var analyzerDirectory = Path.Combine(fixture.Root, "generator");
        Directory.CreateDirectory(analyzerDirectory);
        var analyzerProject = Path.Combine(analyzerDirectory, "Generator.csproj");
        var analyzerOutput = Path.Combine(
            analyzerDirectory, "bin", "Debug", "net10.0", "Generator.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(analyzerOutput)!);
        File.WriteAllText(analyzerProject, "<Project />");
        File.WriteAllBytes(analyzerOutput, [1]);
        var targetTime = File.GetLastWriteTimeUtc(fixture.TargetPath);
        File.SetLastWriteTimeUtc(analyzerProject, targetTime.AddSeconds(-2));
        File.SetLastWriteTimeUtc(analyzerOutput, targetTime.AddSeconds(1));
        var provider = new EvaluationProcessProvider(spec =>
            spec.Command.Arguments[1] == fixture.ProjectPath
                ? ProjectJson(
                    fixture.ProjectPath,
                    fixture.TargetPath,
                    [analyzerProject],
                    new HashSet<string>(StringComparer.Ordinal)
                    {
                        analyzerProject
                    })
                : ProjectJson(analyzerProject, analyzerOutput, []));

        var result = await new DotNetDebugProjectEvaluator().EvaluateAsync(
            fixture.Request(provider),
            CancellationToken.None);

        result.ArtifactIsCurrent.Should().BeFalse();
    }

    [Fact]
    public async Task Evaluation_walks_transitive_project_references_and_validates_consumed_copies()
    {
        using var fixture = new EvaluationFixture();
        var libraryDirectory = Path.Combine(fixture.Root, "libraries");
        Directory.CreateDirectory(libraryDirectory);
        var middleProject = Path.Combine(libraryDirectory, "Middle.csproj");
        var leafProject = Path.Combine(libraryDirectory, "Leaf.csproj");
        var middleOutput = Path.Combine(
            libraryDirectory, "bin", "Debug", "net10.0", "Middle.dll");
        var leafOutput = Path.Combine(
            libraryDirectory, "bin", "Debug", "net10.0", "Leaf.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(middleOutput)!);
        File.WriteAllText(middleProject, "<Project />");
        File.WriteAllText(leafProject, "<Project />");
        File.WriteAllBytes(middleOutput, [1]);
        File.WriteAllBytes(leafOutput, [2]);
        File.WriteAllBytes(
            Path.Combine(Path.GetDirectoryName(fixture.TargetPath)!, "Middle.dll"),
            [1]);
        File.WriteAllBytes(
            Path.Combine(Path.GetDirectoryName(fixture.TargetPath)!, "Leaf.dll"),
            [2]);
        var now = DateTime.UtcNow;
        foreach (var project in new[] { middleProject, leafProject })
            File.SetLastWriteTimeUtc(project, now.AddSeconds(-2));
        foreach (var output in new[]
                 {
                     middleOutput,
                     leafOutput,
                     Path.Combine(Path.GetDirectoryName(fixture.TargetPath)!, "Middle.dll"),
                     Path.Combine(Path.GetDirectoryName(fixture.TargetPath)!, "Leaf.dll")
                 })
            File.SetLastWriteTimeUtc(output, now);
        var provider = new EvaluationProcessProvider(spec =>
        {
            var project = spec.Command.Arguments[1];
            if (project == fixture.ProjectPath)
                return ProjectJson(project, fixture.TargetPath, [middleProject]);
            if (project == middleProject)
                return ProjectJson(project, middleOutput, [leafProject]);
            return ProjectJson(project, leafOutput, []);
        });

        var result = await new DotNetDebugProjectEvaluator().EvaluateAsync(
            fixture.Request(provider),
            CancellationToken.None);

        result.ProjectReferences.Should().BeEquivalentTo(
            [middleProject, leafProject]);
        result.ProjectReferenceOutputs.Should().HaveCount(2);
        result.ArtifactIsCurrent.Should().BeTrue();
    }

    [Fact]
    public async Task Evaluation_propagates_caller_cancellation()
    {
        using var fixture = new EvaluationFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var provider = new CancellationProcessProvider();

        var action = () => new DotNetDebugProjectEvaluator().EvaluateAsync(
            fixture.Request(provider),
            cancellation.Token).AsTask();

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private static string ProjectJson(
        string project,
        string target,
        IReadOnlyList<string> references,
        IReadOnlySet<string>? buildOnlyReferences = null)
        => $$"""
        {
          "Properties": {
            "MSBuildProjectFullPath": "{{Escape(project)}}",
            "TargetFramework": "net10.0",
            "TargetFrameworks": "net10.0",
            "TargetPath": "{{Escape(target)}}",
            "AssemblyName": "{{Path.GetFileNameWithoutExtension(project)}}",
            "OutputType": "Library",
            "IsTestProject": "false",
            "IsTestApplication": "false",
            "IsTestingPlatformApplication": "false",
            "UseMicrosoftTestingPlatformRunner": "false",
            "UseAppHost": "false"
          },
          "Items": {
            "ProjectReference": [
              {{string.Join(",", references.Select(reference =>
                  buildOnlyReferences?.Contains(reference) == true
                      ? $$"""{"FullPath":"{{Escape(reference)}}","OutputItemType":"Analyzer","ReferenceOutputAssembly":"false"}"""
                      : $$"""{"FullPath":"{{Escape(reference)}}"}"""))}}
            ],
            "PackageReference": []
          }
        }
        """;

    private static string Escape(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal);

    private sealed class EvaluationFixture : IDisposable
    {
        public EvaluationFixture()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "hpd-dotnet-evaluation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            ProjectPath = Path.Combine(Root, "App.csproj");
            SourcePath = Path.Combine(Root, "Program.cs");
            TargetPath = Path.Combine(Root, "bin", "Debug", "net10.0", "App.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(TargetPath)!);
            File.WriteAllText(ProjectPath, "<Project />");
            File.WriteAllText(SourcePath, "class Program { }");
            File.WriteAllBytes(TargetPath, [0]);
            File.WriteAllText(
                Path.ChangeExtension(TargetPath, ".runtimeconfig.json"),
                "{}");
            File.WriteAllText(
                Path.ChangeExtension(TargetPath, ".deps.json"),
                "{}");
            var outputTime = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(ProjectPath, outputTime.AddSeconds(-2));
            File.SetLastWriteTimeUtc(SourcePath, outputTime.AddSeconds(-2));
            File.SetLastWriteTimeUtc(TargetPath, outputTime);
        }

        public string Root { get; }
        public string ProjectPath { get; }
        public string SourcePath { get; }
        public string TargetPath { get; }

        public DotNetDebugProjectEvaluationRequest Request(IProcessProvider provider)
            => new()
            {
                CanonicalProjectPath = ProjectPath,
                Configuration = "Debug",
                ProcessExecution = new()
                {
                    EnvironmentId = "environment",
                    EnvironmentRevision = 1,
                    ProcessProvider = provider,
                    ExecutionTarget = ExecutionTarget()
                },
                Workspace = Workspace(Root)
            };

        public string Json(
            string outputType,
            bool isTest,
            bool isMtp,
            string frameworks,
            string? packageId = null)
            => $$"""
            {
              "Properties": {
                "MSBuildProjectFullPath": "{{Escape(ProjectPath)}}",
                "TargetFramework": "{{(frameworks.Contains(';') ? "" : frameworks)}}",
                "TargetFrameworks": "{{frameworks}}",
                "TargetPath": "{{Escape(TargetPath)}}",
                "AssemblyName": "App",
                "OutputType": "{{outputType}}",
                "IsTestProject": "{{isTest}}",
                "IsTestApplication": "false",
                "IsTestingPlatformApplication": "{{isMtp}}",
                "UseMicrosoftTestingPlatformRunner": "{{isMtp}}",
                "UseAppHost": "false"
              },
              "Items": {
                "ProjectReference": [],
                "PackageReference": [
                  {{(packageId is not null
                      ? $$"""{"Identity":"{{packageId}}","Version":"1.0.0"}"""
                      : isTest && !isMtp
                          ? """{"Identity":"Microsoft.NET.Test.Sdk","Version":"17.0.0"}"""
                          : """{"Identity":"Fixture","Version":"1.0.0"}""")}}
                ]
              }
            }
            """;

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
            }
        }

        private static string Escape(string value)
            => value.Replace("\\", "\\\\", StringComparison.Ordinal);
    }

    private sealed class EvaluationProcessProvider(
        Func<ProcessInvocationSpec, string> output) : IProcessProvider
    {
        public ProviderId ProviderId => new("test.process");
        public List<ProcessInvocationSpec> Specifications { get; } = [];

        public ValueTask<ProcessInvocationResult> RunAsync(
            ProcessInvocationSpec spec,
            IProcessOutputSink? outputSink = null,
            CancellationToken cancellationToken = default)
        {
            Specifications.Add(spec);
            return ValueTask.FromResult(new ProcessInvocationResult
            {
                ExitCode = 0,
                CompletionKind = ProcessCompletionKind.Exited,
                Output = new()
                {
                    Stdout = new()
                    {
                        CapturedBytes = Encoding.UTF8.GetBytes(output(spec))
                    },
                    Stderr = new(),
                    OutputDrainTimeout = TimeSpan.Zero
                }
            });
        }

        public ValueTask<IProcessInvocationHandle> StartAsync(
            ProcessInvocationSpec spec,
            IProcessOutputSink? output = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask SignalAsync(
            TargetHandle<ProcessInvocation> process,
            ProcessSignal signal,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask ResizeTerminalAsync(
            TargetHandle<ProcessInvocation> process,
            TerminalSpec size,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<ProcessInvocationResult> WaitAsync(
            TargetHandle<ProcessInvocation> process,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(
            TargetHandle<ProcessInvocation> process,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class CancellationProcessProvider : IProcessProvider
    {
        public ProviderId ProviderId => new("test.cancel");

        public ValueTask<ProcessInvocationResult> RunAsync(
            ProcessInvocationSpec spec,
            IProcessOutputSink? outputSink = null,
            CancellationToken cancellationToken = default)
            => ValueTask.FromCanceled<ProcessInvocationResult>(cancellationToken);

        public ValueTask<IProcessInvocationHandle> StartAsync(
            ProcessInvocationSpec spec,
            IProcessOutputSink? output = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask SignalAsync(
            TargetHandle<ProcessInvocation> process,
            ProcessSignal signal,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask ResizeTerminalAsync(
            TargetHandle<ProcessInvocation> process,
            TerminalSpec size,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<ProcessInvocationResult> WaitAsync(
            TargetHandle<ProcessInvocation> process,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(
            TargetHandle<ProcessInvocation> process,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FixedResultProcessProvider(ProcessInvocationResult result)
        : IProcessProvider
    {
        public ProviderId ProviderId => new("test.result");

        public ValueTask<ProcessInvocationResult> RunAsync(
            ProcessInvocationSpec spec,
            IProcessOutputSink? outputSink = null,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(result);

        public ValueTask<IProcessInvocationHandle> StartAsync(
            ProcessInvocationSpec spec,
            IProcessOutputSink? output = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask SignalAsync(
            TargetHandle<ProcessInvocation> process,
            ProcessSignal signal,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask ResizeTerminalAsync(
            TargetHandle<ProcessInvocation> process,
            TerminalSpec size,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<ProcessInvocationResult> WaitAsync(
            TargetHandle<ProcessInvocation> process,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(
            TargetHandle<ProcessInvocation> process,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private static TargetHandle<ExecutionUnit> ExecutionTarget()
        => new(
            new TargetRoute
            {
                Kind = new("test.execution"),
                Scope = new("test")
            },
            TargetHandleLifetime.LiveCapability,
            TargetHandleAuthority.Control | TargetHandleAuthority.Observe);

    private static AgentWorkspace Workspace(params string[] roots)
    {
        var entries = roots.Select((path, index) => new AgentWorkspaceRoot(
            index == 0 ? "default" : $"root-{index + 1}",
            path,
            Path.GetFileName(path))).ToArray();
        return new AgentWorkspace(entries[0].Id, entries[0].Path, entries);
    }
}
