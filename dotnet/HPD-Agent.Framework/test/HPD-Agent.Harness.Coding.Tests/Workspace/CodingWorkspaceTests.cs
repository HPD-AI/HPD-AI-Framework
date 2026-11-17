using System.Text.Json;
using FluentAssertions;
using HPD.Agent.Security;
using HPD.Agent.ToolHarness.Coding;

namespace HPD.Agent.ToolHarness.Coding.Tests.Workspace;

public sealed class CodingWorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"hpd-workspace-tests-{Guid.NewGuid():N}");
    private readonly string _docs;

    public CodingWorkspaceTests()
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
    public void CaptureContextSnapshot_OwnsRootCollectionAcrossInvocations()
    {
        var roots = new List<CodingWorkspaceRoot> { new("os", _root, CodingWorkspaceAccess.ReadWrite) };
        var source = new CodingWorkspace("os", _root, roots);
        var first = (CodingWorkspace)source.CaptureContextSnapshot();
        var second = (CodingWorkspace)source.CaptureContextSnapshot();

        roots[0] = new CodingWorkspaceRoot("other", _docs, CodingWorkspaceAccess.ReadWrite);
        ((CodingWorkspaceRoot[])first.Roots)[0] = new CodingWorkspaceRoot("changed", _docs, CodingWorkspaceAccess.ReadWrite);

        second.Roots[0].Id.Should().Be("os");
        second.ResolveWorkspacePath("file.txt").Should().Be(AgentFilesystemPath.Canonicalize(Path.Combine(_root, "file.txt")));
    }

    [Fact]
    public void From_ParsesJsonElementWorkspace()
    {
        var json = $$"""
        {
          "version": 1,
          "defaultRootId": "os",
          "roots": [
            { "id": "os", "access": "readWrite", "path": "{{JsonEscape(_root)}}" },
            { "id": "docs", "access": "readWrite", "label": "Docs", "path": "{{JsonEscape(_docs)}}" }
          ]
        }
        """;

        using var document = JsonDocument.Parse(json);
        var runConfig = new AgentRunConfig
        {
            Context = new AgentContextRunConfig { Properties = new Dictionary<string, object>()
            {
                [CodingWorkspace.ContextKey] = document.RootElement.Clone()
            } }
        };

        var workspace = CodingWorkspace.From(runConfig);

        workspace.DefaultRootId.Should().Be("os");
        workspace.DefaultRootPath.Should().Be(AgentFilesystemPath.Canonicalize(_root));
        workspace.Roots.Should().HaveCount(2);
    }

    [Fact]
    public void ResolvePath_ResolvesRelativeUnderDefaultRoot()
    {
        var workspace = CreateWorkspace();

        workspace.ResolveWorkspacePath("src/app.cs")
            .Should().Be(AgentFilesystemPath.Canonicalize(Path.Combine(_root, "src", "app.cs")));
    }

    [Fact]
    public void ResolvePath_ResolvesRootQualifiedPath()
    {
        var workspace = CreateWorkspace();

        workspace.ResolveWorkspacePath("@docs/readme.md")
            .Should().Be(AgentFilesystemPath.Canonicalize(Path.Combine(_docs, "readme.md")));
    }

    [Fact]
    public void ResolvePath_RejectsPathOutsideWorkspace()
    {
        var workspace = CreateWorkspace();
        var outside = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.txt");

        var act = () => workspace.ResolveWorkspacePath(outside);

        act.Should().Throw<CodingWorkspaceException>()
            .Where(ex => ex.Kind == CodingWorkspaceErrorKind.PathOutsideWorkspace);
    }

    [Fact]
    public void ResolvePath_RejectsRootQualifiedEscape()
    {
        var workspace = CreateWorkspace();

        var act = () => workspace.ResolveWorkspacePath("@docs/../outside.txt");

        act.Should().Throw<CodingWorkspaceException>()
            .Where(ex => ex.Kind == CodingWorkspaceErrorKind.PathOutsideWorkspace);
    }

    [Fact]
    public void From_RejectsMissingWorkspace()
    {
        var act = () => CodingWorkspace.From(new AgentRunConfig());

        act.Should().Throw<CodingWorkspaceException>()
            .WithMessage("*Workspace is required*");
    }

    private CodingWorkspace CreateWorkspace()
        => CodingWorkspace.From(new AgentRunConfig
        {
            Context = new AgentContextRunConfig { Properties = new Dictionary<string, object>()
            {
                [CodingWorkspace.ContextKey] = new CodingWorkspace(
                    "os",
                    _root,
                    [
                        new CodingWorkspaceRoot("os", _root, CodingWorkspaceAccess.ReadWrite),
                        new CodingWorkspaceRoot("docs", _docs, CodingWorkspaceAccess.ReadWrite, "Docs")
                    ])
            } }
        });

    private static string JsonEscape(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
