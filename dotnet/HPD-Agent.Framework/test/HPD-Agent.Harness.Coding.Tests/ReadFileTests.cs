using System.Xml.Linq;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.ToolHarness.Coding;
using HPD.Events.Core;
using HPDOS.ToolHarnesses.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent.ToolHarness.Coding.Tests;

[Collection(CurrentDirectoryCollection.Name)]
public sealed class ReadFileTests : IDisposable
{
    private readonly string _originalCwd = Directory.GetCurrentDirectory();
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"hpd-portable-read-{Guid.NewGuid():N}");

    public ReadFileTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "docs"));
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        Directory.SetCurrentDirectory(_root);
        File.WriteAllText("lines.txt", "one\ntwo\nthree\nfour\n");
        File.WriteAllText("whitespace.txt", "  first\n\nlast  \n");
        File.WriteAllText("empty.txt", string.Empty);
        File.WriteAllBytes("binary.bin", [0x01, 0x00, 0x02]);
        File.WriteAllText("xml.txt", "x < y && y > z");
        File.WriteAllText(
            Path.Combine("docs", "guide.md"),
            "# guide\n");
    }

    [Fact]
    public void ReadFile_requires_permission_metadata()
    {
        var method = typeof(CodingToolHarness).GetMethod(
            nameof(CodingToolHarness.ReadFile));

        method.Should().NotBeNull();
        method!.GetCustomAttributes(
                typeof(RequiresPermissionAttribute),
                inherit: false)
            .Should().ContainSingle();
    }

    [Fact]
    public async Task Reads_bounded_line_numbered_text()
    {
        string result = await ReadAsync(CreateContext(), "lines.txt");

        XElement root = XElement.Parse(result);
        root.Name.LocalName.Should().Be("file");
        root.Attribute("path")!.Value.Should().Be("lines.txt");
        root.Value.Should().Contain("1\tone")
            .And.Contain("4\tfour");
    }

    [Fact]
    public async Task Offset_and_limit_return_partial_range()
    {
        string result = await ReadAsync(
            CreateContext(),
            "lines.txt",
            offset: 2,
            limit: 2);

        XElement root = XElement.Parse(result);
        root.Attribute("start_line")!.Value.Should().Be("2");
        root.Attribute("lines_read")!.Value.Should().Be("2");
        root.Value.Should().Contain("2\ttwo")
            .And.Contain("3\tthree")
            .And.NotContain("1\tone");
        root.Element("next_read").Should().NotBeNull();
    }

    [Fact]
    public async Task Empty_and_whitespace_only_content_is_preserved()
    {
        string empty = await ReadAsync(CreateContext(), "empty.txt");
        string whitespace = await ReadAsync(
            CreateContext(),
            "whitespace.txt");

        XElement.Parse(empty).Elements("line").Should().BeEmpty();
        whitespace.Should().Contain("1\t  first")
            .And.Contain("2\t")
            .And.Contain("3\tlast  ");
    }

    [Fact]
    public async Task Xml_sensitive_content_is_escaped()
    {
        string result = await ReadAsync(CreateContext(), "xml.txt");

        XElement.Parse(result).Value.Should()
            .Contain("x < y && y > z");
        result.Should().Contain("&lt;").And.Contain("&amp;");
    }

    [Fact]
    public async Task Binary_directory_and_missing_paths_are_distinct()
    {
        string binary = await ReadAsync(CreateContext(), "binary.bin");
        string directory = await ReadAsync(CreateContext(), "src");
        string missing = await ReadAsync(CreateContext(), "missing.txt");

        binary.Should().Contain("Cannot read binary file.");
        directory.Should().Contain(
            "Path is a directory. Use ListDirectory instead.");
        missing.Should().Contain("File does not exist.");
    }

    [Fact]
    public async Task Multi_root_qualified_path_is_logical()
    {
        string docs = Path.Combine(_root, "docs");
        AgentRunConfig runConfig = CreateRunConfig(_root, docs);
        string result = await ReadAsync(
            CreateContext(runConfig),
            "@docs/guide.md");

        XElement root = XElement.Parse(result);
        root.Attribute("path")!.Value.Should().Be("@docs/guide.md");
        root.Value.Should().Contain("# guide");
    }

    [Fact]
    public async Task Absolute_execution_path_is_translated_but_outside_paths_fail_closed()
    {
        string inside = await ReadAsync(
            CreateContext(),
            Path.Combine(Directory.GetCurrentDirectory(), "lines.txt"));
        string outside = await ReadAsync(
            CreateContext(),
            Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.txt"));

        XElement.Parse(inside).Attribute("path")!.Value.Should()
            .Be("lines.txt");
        outside.Should().Contain(
            "Absolute execution paths are not model-facing workspace addresses.");
    }

    [Fact]
    public async Task Traversal_is_rejected_after_logical_normalization()
    {
        string result = await ReadAsync(
            CreateContext(),
            "../outside.txt");

        result.Should().Contain("Workspace path traversal is denied.");
    }

    [Fact]
    public async Task Repeated_same_range_returns_file_unchanged()
    {
        AgentContext context = CreateContext();
        TestCodingHarness harness = CodingHarnessTestFactory.Create();

        string first = await ReadThroughMiddlewareAsync(
            context,
            harness,
            "lines.txt");
        string second = await ReadThroughMiddlewareAsync(
            context,
            harness,
            "lines.txt");

        first.Should().Contain("1\tone");
        second.Should().Contain("<file_unchanged");
    }

    [Fact]
    public async Task Invalid_arguments_are_rejected_before_storage_access()
    {
        string result = await ReadAsync(
            CreateContext(),
            "lines.txt",
            offset: 0);

        result.Should().Contain(
            "Offset must be greater than or equal to 1.");
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_originalCwd);
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static Task<string> ReadAsync(
        AgentContext context,
        string path,
        int offset = 1,
        int limit = 2000)
    {
        TestCodingHarness harness = CodingHarnessTestFactory.Create();
        return ReadThroughMiddlewareAsync(
            context,
            harness,
            path,
            offset,
            limit);
    }

    private static async Task<string> ReadThroughMiddlewareAsync(
        AgentContext context,
        TestCodingHarness harness,
        string path,
        int offset = 1,
        int limit = 2000)
    {
        AIFunction function = CreateFunction();
        BeforeFunctionContext before = CreateBeforeContext(
            context,
            function);
        var request = new FunctionRequest
        {
            Function = function,
            CallId = before.FunctionCallId,
            Arguments = new Dictionary<string, object?>
            {
                ["path"] = path,
                ["offset"] = offset,
                ["limit"] = limit,
            },
            State = before.State,
            RunConfig = before.RunConfig,
            ResultMetadata = new ToolResultMetadata(),
            EventCoordinator = context.EventCoordinator,
        };
        var functionContext = new FunctionExecutionContext(before, request);
        object result = await harness.ReadFile(
            path,
            functionContext,
            offset,
            limit);
        AfterFunctionContext after = context.AsAfterFunction(
            function: null,
            callId: before.FunctionCallId,
            result,
            exception: null,
            runConfig: before.RunConfig,
            toolharnessName: "CodingToolHarness",
            resultMetadata: request.ResultMetadata);
        await new EnvironmentContextMiddleware(
            new EnvironmentContextConfig())
            .AfterFunctionAsync(after, CancellationToken.None);
        return after.Result?.ToString() ?? string.Empty;
    }

    private static AgentContext CreateContext(
        AgentRunConfig? runConfig = null)
    {
        AgentLoopState state = AgentLoopState.InitialSafe(
            [],
            "run",
            "conversation",
            "agent");
        var context = new AgentContext(
            "agent",
            "conversation",
            state,
            new EventCoordinator(),
            new Session("session"),
            new Thread("session", "agent"),
            CancellationToken.None);
        context.RuntimeCapabilities.Set(
            TestRuntimeWorkspace.FromRunConfig(
                runConfig ?? CreateRunConfig()));
        return context;
    }

    private static BeforeFunctionContext CreateBeforeContext(
        AgentContext context,
        AIFunction function) =>
        context.AsBeforeFunction(
            function,
            "read-call",
            new Dictionary<string, object?>(),
            CreateRunConfig(),
            toolharnessName: "CodingToolHarness");

    private static AIFunction CreateFunction()
    {
        return AIFunctionFactory.Create(
            () => "ok",
            new AIFunctionFactoryOptions
            {
                Name = "ReadFile",
                Description = "test",
            });
    }

    private static AgentRunConfig CreateRunConfig(
        string? defaultRoot = null,
        string? docsRoot = null)
    {
        string cwd = Path.GetFullPath(
            defaultRoot ?? Directory.GetCurrentDirectory());
        var roots = new List<AgentWorkspaceRoot>
        {
            new("default", cwd, AgentWorkspaceAccess.ReadWrite),
        };
        if (docsRoot is not null)
        {
            roots.Add(new AgentWorkspaceRoot(
                "docs",
                Path.GetFullPath(docsRoot),
                AgentWorkspaceAccess.ReadWrite,
                "Docs"));
        }
        return new AgentRunConfig
        {
            Context = new AgentContextRunConfig
            {
                Properties = new Dictionary<string, object>
                {
                    [AgentWorkspace.ContextKey] = new AgentWorkspace(
                        "default",
                        cwd,
                        roots),
                },
            },
        };
    }
}
