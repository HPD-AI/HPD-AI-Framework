using System.Text.Json;
using FluentAssertions;
using HPD.Agent.ToolHarness.Coding;

namespace HPD.Agent.ToolHarness.Coding.Tests.Workspace;

public sealed class AgentWorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"hpd-workspace-tests-{Guid.NewGuid():N}");
    private readonly string _docs;

    public AgentWorkspaceTests()
    {
        _docs = Path.Combine(_root, "docs");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_docs);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void From_ParsesJsonElementWorkspace()
    {
        var json = $$"""
        {
          "version": 1,
          "defaultRootId": "os",
          "roots": [
            { "id": "os", "path": "{{JsonEscape(_root)}}" },
            { "id": "docs", "label": "Docs", "path": "{{JsonEscape(_docs)}}" }
          ]
        }
        """;

        using var document = JsonDocument.Parse(json);
        var runConfig = new AgentRunConfig
        {
            ContextOverrides = new()
            {
                [AgentWorkspace.ContextKey] = document.RootElement.Clone()
            }
        };

        var workspace = AgentWorkspace.From(runConfig);

        workspace.DefaultRootId.Should().Be("os");
        workspace.DefaultRootPath.Should().Be(Path.GetFullPath(_root));
        workspace.Roots.Should().HaveCount(2);
    }

    [Fact]
    public void ResolvePath_ResolvesRelativeUnderDefaultRoot()
    {
        var workspace = CreateWorkspace();

        workspace.ResolveWorkspacePath("src/app.cs")
            .Should().Be(Path.GetFullPath(Path.Combine(_root, "src", "app.cs")));
    }

    [Fact]
    public void ResolvePath_ResolvesRootQualifiedPath()
    {
        var workspace = CreateWorkspace();

        workspace.ResolveWorkspacePath("@docs/readme.md")
            .Should().Be(Path.GetFullPath(Path.Combine(_docs, "readme.md")));
    }

    [Fact]
    public void ResolvePath_RejectsPathOutsideWorkspace()
    {
        var workspace = CreateWorkspace();
        var outside = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.txt");

        var act = () => workspace.ResolveWorkspacePath(outside);

        act.Should().Throw<AgentWorkspaceException>()
            .Where(ex => ex.Kind == AgentWorkspaceErrorKind.PathOutsideWorkspace);
    }

    [Fact]
    public void ResolvePath_RejectsRootQualifiedEscape()
    {
        var workspace = CreateWorkspace();

        var act = () => workspace.ResolveWorkspacePath("@docs/../outside.txt");

        act.Should().Throw<AgentWorkspaceException>()
            .Where(ex => ex.Kind == AgentWorkspaceErrorKind.PathOutsideWorkspace);
    }

    [Fact]
    public void From_RejectsMissingWorkspace()
    {
        var act = () => AgentWorkspace.From(new AgentRunConfig());

        act.Should().Throw<AgentWorkspaceException>()
            .WithMessage("*Workspace is required*");
    }

    private AgentWorkspace CreateWorkspace()
        => AgentWorkspace.From(new AgentRunConfig
        {
            ContextOverrides = new()
            {
                [AgentWorkspace.ContextKey] = new AgentWorkspace(
                    "os",
                    _root,
                    [
                        new AgentWorkspaceRoot("os", _root),
                        new AgentWorkspaceRoot("docs", _docs, "Docs")
                    ])
            }
        });

    private static string JsonEscape(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
