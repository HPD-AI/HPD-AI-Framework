using HPD.Agent;
using HPD.Agent.ToolHarness.Coding;
using HPD.Agent.Middleware;
using HPD.Events.Core;
using HPDOS.ToolHarnesses.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent.ToolHarness.Coding.Tests;

[Collection(CurrentDirectoryCollection.Name)]
public sealed class ListDirectoryTests : IDisposable
{
    private readonly string _originalCwd = Directory.GetCurrentDirectory();
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"hpd-list-directory-tests-{Guid.NewGuid():N}");

    public ListDirectoryTests()
    {
        Directory.CreateDirectory(_tempRoot);
        Directory.SetCurrentDirectory(_tempRoot);
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_originalCwd);
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void ListDirectory_RequiresPermission()
    {
        var method = typeof(CodingToolHarness).GetMethod(nameof(CodingToolHarness.ListDirectory));

        method.Should().NotBeNull();
        method!.GetCustomAttributes(typeof(RequiresPermissionAttribute), inherit: false)
            .Should().ContainSingle();
    }

    [Fact]
    public async Task ListDirectory_ListsSmallDirectoryWithDirectoriesFirst()
    {
        Directory.CreateDirectory("src");
        await File.WriteAllTextAsync("README.md", "# demo\n");
        await File.WriteAllTextAsync("Program.cs", "class Program {}\n");

        var result = await new CodingToolHarness().ListDirectory(".");

        result.Should().Contain("<directory path=");
        result.Should().Contain("recursive=\"false\"");
        result.Should().Contain("entries_read=\"3\"");
        result.Should().Contain("<entry kind=\"directory\" path=\"src/\" />");
        result.Should().Contain("<entry kind=\"file\" path=\"Program.cs\" />");

        result.IndexOf("path=\"src/\"", StringComparison.Ordinal)
            .Should().BeLessThan(result.IndexOf("path=\"Program.cs\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListDirectory_ResolvesRelativeAndAbsolutePaths()
    {
        Directory.CreateDirectory("src");
        await File.WriteAllTextAsync(Path.Combine("src", "A.cs"), "class A {}\n");

        var relative = await new CodingToolHarness().ListDirectory("src");
        var absolute = await new CodingToolHarness().ListDirectory(Path.Combine(_tempRoot, "src"));

        relative.Should().Contain("path=\"A.cs\"");
        absolute.Should().Contain("path=\"A.cs\"");
        absolute.Should().Contain(Path.Combine(_tempRoot, "src"));
    }

    [Fact]
    public async Task ListDirectory_RootQualifiedPath_IsTreatedAsLiteralPath()
    {
        var docsRoot = Path.Combine(_tempRoot, "docs");
        Directory.CreateDirectory(docsRoot);
        await File.WriteAllTextAsync(Path.Combine(docsRoot, "notes.md"), "# docs\n");

        var result = await new CodingToolHarness().ListDirectory(
            "@docs",
            context: CreateFunctionContext(CreateWorkspaceRunConfig(_tempRoot, docsRoot)));

        result.Should().Contain("Directory does not exist");
        result.Should().Contain("@docs");
    }

    [Fact]
    public async Task ListDirectory_AbsolutePathOutsideWorkspace_ListsWithSandboxDisabled()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"hpd-list-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        try
        {
            var runConfig = CreateWorkspaceRunConfig(_tempRoot);
            runConfig.Security = new AgentSecurityProfile
            {
                Approval = AgentApprovalPolicy.AutoApprove,
                Sandbox = AgentSandboxPolicy.Disabled,
                SandboxEscape = AgentSandboxEscapePolicy.Deny
            };
            var result = await new CodingToolHarness().ListDirectory(
                outside,
                context: CreateFunctionContext(runConfig));

            result.Should().Contain("<empty_directory");
            result.Should().Contain(outside);
        }
        finally
        {
            if (Directory.Exists(outside))
                Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task ListDirectory_RejectsInvalidArguments()
    {
        var toolharness = new CodingToolHarness();

        (await toolharness.ListDirectory(null!)).Should().Contain("Path is required.");
        (await toolharness.ListDirectory(".", offset: 0)).Should().Contain("Offset must be greater than or equal to 1.");
        (await toolharness.ListDirectory(".", limit: 0)).Should().Contain("Limit must be between 1 and 1000.");
        (await toolharness.ListDirectory(".", limit: 1001)).Should().Contain("Limit must be between 1 and 1000.");
        (await toolharness.ListDirectory(".", maxDepth: -1, recursive: true)).Should().Contain("MaxDepth must be between 0 and 25.");
        (await toolharness.ListDirectory(".", maxDepth: 26, recursive: true)).Should().Contain("MaxDepth must be between 0 and 25.");
        (await toolharness.ListDirectory(".", maxDepth: 2)).Should().Contain("MaxDepth requires recursive mode.");
    }

    [Fact]
    public async Task ListDirectory_RejectsMissingDirectoriesAndSuggestsSimilarSibling()
    {
        Directory.CreateDirectory("src");

        var result = await new CodingToolHarness().ListDirectory("srcc");

        result.Should().Contain("<error tool=\"ListDirectory\"");
        result.Should().Contain("Directory does not exist. Did you mean");
        result.Should().Contain("src");
    }

    [Fact]
    public async Task ListDirectory_RejectsFilePathsWithReadFileHint()
    {
        await File.WriteAllTextAsync("Program.cs", "class Program {}\n");

        var result = await new CodingToolHarness().ListDirectory("Program.cs");

        result.Should().Contain("Path is a file. Use ReadFile instead.");
    }

    [Fact]
    public async Task ListDirectory_HandlesEmptyDirectoryAndOffsetBeyondEnd()
    {
        Directory.CreateDirectory("empty");
        await File.WriteAllTextAsync("one.txt", "one\n");

        var empty = await new CodingToolHarness().ListDirectory("empty");
        var beyondEnd = await new CodingToolHarness().ListDirectory(".", offset: 50);

        empty.Should().Contain("<empty_directory");
        beyondEnd.Should().Contain("<no_content reason=\"offset_beyond_end\"");
    }

    [Fact]
    public async Task ListDirectory_AppliesOffsetLimitAndEmitsNextList()
    {
        await File.WriteAllTextAsync("a.txt", "a\n");
        await File.WriteAllTextAsync("b.txt", "b\n");
        await File.WriteAllTextAsync("c.txt", "c\n");

        var result = await new CodingToolHarness().ListDirectory(".", offset: 2, limit: 1);

        result.Should().Contain("entries_read=\"1\"");
        result.Should().Contain("truncated=\"true\"");
        result.Should().Contain("<entry kind=\"file\" path=\"b.txt\" />");
        result.Should().NotContain("path=\"a.txt\"");
        result.Should().Contain("<next_list offset=\"3\" limit=\"1\"");
        result.Should().Contain("reason=\"more_entries_available\"");
    }

    [Fact]
    public async Task ListDirectory_DoesNotMarkFinalPageAsTruncated()
    {
        for (var i = 1; i <= 10; i++)
            await File.WriteAllTextAsync($"file-{i:D2}.txt", $"{i}\n");

        var result = await new CodingToolHarness().ListDirectory(".", offset: 6, limit: 5);

        result.Should().Contain("entries_read=\"5\"");
        result.Should().Contain("truncated=\"false\"");
        result.Should().Contain("path=\"file-06.txt\"");
        result.Should().Contain("path=\"file-10.txt\"");
        result.Should().NotContain("<next_list");
        result.Should().NotContain("path=\"file-05.txt\"");
    }

    [Fact]
    public async Task ListDirectory_EscapesXmlSensitiveFilenames()
    {
        await File.WriteAllTextAsync("a<&>.txt", "xml\n");

        var result = await new CodingToolHarness().ListDirectory(".");

        result.Should().Contain("a&lt;&amp;&gt;.txt");
    }

    [Fact]
    public async Task ListDirectory_HidesDotfilesByDefaultAndCanIncludeThem()
    {
        await File.WriteAllTextAsync(".env", "secret\n");
        await File.WriteAllTextAsync("visible.txt", "visible\n");

        var hiddenByDefault = await new CodingToolHarness().ListDirectory(".");
        var included = await new CodingToolHarness().ListDirectory(".", includeHidden: true);

        hiddenByDefault.Should().NotContain(".env");
        hiddenByDefault.Should().Contain("visible.txt");
        included.Should().Contain(".env");
    }

    [Fact]
    public async Task ListDirectory_RespectsGitignoreWhenEnabled()
    {
        await File.WriteAllTextAsync(".gitignore", "ignored.txt\nignored-dir/\n");
        await File.WriteAllTextAsync("ignored.txt", "ignore\n");
        await File.WriteAllTextAsync("visible.txt", "visible\n");
        Directory.CreateDirectory("ignored-dir");
        await File.WriteAllTextAsync(Path.Combine("ignored-dir", "nested.txt"), "nested\n");

        var respected = await new CodingToolHarness().ListDirectory(".", includeHidden: true);
        var ignoredDisabled = await new CodingToolHarness().ListDirectory(".", includeHidden: true, respectIgnoreFiles: false);

        respected.Should().NotContain("ignored.txt");
        respected.Should().NotContain("ignored-dir/");
        respected.Should().Contain("visible.txt");
        ignoredDisabled.Should().Contain("ignored.txt");
        ignoredDisabled.Should().Contain("ignored-dir/");
    }

    [Fact]
    public async Task ListDirectory_FiltersByKind()
    {
        Directory.CreateDirectory("src");
        await File.WriteAllTextAsync("Program.cs", "class Program {}\n");

        var files = await new CodingToolHarness().ListDirectory(".", kind: DirectoryEntryKindFilter.Files);
        var directories = await new CodingToolHarness().ListDirectory(".", kind: DirectoryEntryKindFilter.Directories);

        files.Should().Contain("Program.cs");
        files.Should().NotContain("src/");
        directories.Should().Contain("src/");
        directories.Should().NotContain("Program.cs");
    }

    [Fact]
    public async Task ListDirectory_IncludesMetadataWhenRequested()
    {
        await File.WriteAllTextAsync("Program.cs", "class Program {}\n");

        var result = await new CodingToolHarness().ListDirectory(".", includeMetadata: true);

        result.Should().Contain("path=\"Program.cs\"");
        result.Should().Contain("size=\"");
        result.Should().Contain("last_write_time_utc=\"");
    }

    [Fact]
    public async Task ListDirectory_SortsBySizeAndDirection()
    {
        await File.WriteAllTextAsync("small.txt", "x\n");
        await File.WriteAllTextAsync("large.txt", new string('x', 100));

        var result = await new CodingToolHarness().ListDirectory(
            ".",
            sortBy: DirectorySortBy.Size,
            sortDirection: SortDirection.Descending);

        result.IndexOf("path=\"large.txt\"", StringComparison.Ordinal)
            .Should().BeLessThan(result.IndexOf("path=\"small.txt\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListDirectory_RecursiveModeRespectsMaxDepthAndUsesRelativePaths()
    {
        Directory.CreateDirectory(Path.Combine("src", "Controllers"));
        await File.WriteAllTextAsync(Path.Combine("src", "Controllers", "HomeController.cs"), "class HomeController {}\n");

        var depthOne = await new CodingToolHarness().ListDirectory(".", recursive: true, maxDepth: 1);
        var depthTwo = await new CodingToolHarness().ListDirectory(".", recursive: true, maxDepth: 2);
        var depthThree = await new CodingToolHarness().ListDirectory(".", recursive: true, maxDepth: 3);

        depthOne.Should().Contain("path=\"src/\"");
        depthOne.Should().NotContain("HomeController.cs");
        depthTwo.Should().Contain("path=\"src/Controllers/\"");
        depthTwo.Should().NotContain("HomeController.cs");
        depthThree.Should().Contain("path=\"src/Controllers/HomeController.cs\"");
    }

    [Fact]
    public async Task ListDirectory_RecursiveModeSkipsBuiltInGeneratedDirectories()
    {
        Directory.CreateDirectory("node_modules");
        await File.WriteAllTextAsync(Path.Combine("node_modules", "package.js"), "module.exports = {}\n");
        Directory.CreateDirectory("src");
        await File.WriteAllTextAsync(Path.Combine("src", "App.cs"), "class App {}\n");

        var result = await new CodingToolHarness().ListDirectory(".", recursive: true, maxDepth: 2);

        result.Should().NotContain("node_modules");
        result.Should().Contain("src/App.cs");
        result.Should().Contain("ignored_count=\"");
    }

    [Fact]
    public async Task ListDirectory_RecursiveModeDoesNotFollowSymlinkedDirectories()
    {
        if (OperatingSystem.IsWindows())
            return;

        Directory.CreateDirectory("real-dir");
        await File.WriteAllTextAsync(Path.Combine("real-dir", "inside.txt"), "inside\n");

        try
        {
            Directory.CreateSymbolicLink("linked-dir", Path.GetFullPath("real-dir"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        var result = await new CodingToolHarness().ListDirectory(
            ".",
            recursive: true,
            maxDepth: 3,
            includeMetadata: true,
            respectIgnoreFiles: false);

        result.Should().Contain("path=\"linked-dir/\"");
        result.Should().Contain("symlink=\"true\"");
        result.Should().Contain("path=\"real-dir/inside.txt\"");
        result.Should().NotContain("path=\"linked-dir/inside.txt\"");
    }

    [Fact]
    public async Task ListDirectory_IncludesSymlinkMetadataForSymlinkedFiles()
    {
        if (OperatingSystem.IsWindows())
            return;

        await File.WriteAllTextAsync("real.txt", "real\n");

        try
        {
            File.CreateSymbolicLink("linked.txt", Path.GetFullPath("real.txt"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        var result = await new CodingToolHarness().ListDirectory(".", includeMetadata: true);

        result.Should().Contain("path=\"linked.txt\"");
        result.Should().Contain("symlink=\"true\"");
        result.Should().Contain("path=\"real.txt\"");
    }

    [Fact]
    public async Task ListDirectory_BlocksUnixSystemPaths()
    {
        if (OperatingSystem.IsWindows())
            return;

        var result = await new CodingToolHarness().ListDirectory("/dev");

        result.Should().Contain("Cannot list blocked system path.");
    }

    [Fact]
    public async Task ListDirectory_ReturnsErrorWhenRequestedDirectoryCannotBeEnumerated()
    {
        if (OperatingSystem.IsWindows())
            return;

        Directory.CreateDirectory("restricted");

        try
        {
            File.SetUnixFileMode("restricted", UnixFileMode.UserWrite);

            var result = await new CodingToolHarness().ListDirectory("restricted");

            result.Should().Contain("Unable to list directory:");
        }
        finally
        {
            File.SetUnixFileMode(
                "restricted",
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public async Task ListDirectory_RecursiveModeSkipsInaccessibleChildSubtreesWithoutFailingWholeListing()
    {
        if (OperatingSystem.IsWindows())
            return;

        await File.WriteAllTextAsync("good.txt", "good\n");
        Directory.CreateDirectory("restricted-child");

        try
        {
            File.SetUnixFileMode("restricted-child", UnixFileMode.UserWrite);

            var result = await new CodingToolHarness().ListDirectory(".", recursive: true, maxDepth: 2);

            result.Should().Contain("good.txt");
            result.Should().Contain("restricted-child/");
            result.Should().NotContain("Unable to list directory:");
        }
        finally
        {
            File.SetUnixFileMode(
                "restricted-child",
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public async Task ListDirectory_UsesDirectoryListingSourceBeforeFilesystem()
    {
        await File.WriteAllTextAsync("disk.txt", "disk\n");
        var source = new FakeDirectoryListingSource("virtual.txt");

        var result = await new CodingToolHarness(null, [source]).ListDirectory(".");

        result.Should().Contain("virtual.txt");
        result.Should().NotContain("disk.txt");
    }

    [Fact]
    public async Task ListDirectory_UsesDirectoryListingSourceForNonLocalPaths()
    {
        var source = new FakeDirectoryListingSource("virtual.txt");

        var result = await new CodingToolHarness(null, [source]).ListDirectory("virtual-folder");

        result.Should().Contain("virtual.txt");
        result.Should().NotContain("Directory does not exist.");
    }

    private static FunctionExecutionContext CreateFunctionContext(AgentRunConfig runConfig)
    {
        var function = AIFunctionFactory.Create(
            () => "ok",
            new AIFunctionFactoryOptions
            {
                Name = "ListDirectory",
                Description = "Test function"
            });
        var state = AgentLoopState.InitialSafe([], "run-1", "conversation-1", "AgentA");
        var agentContext = new AgentContext(
            "AgentA",
            "conversation-1",
            state,
            new EventCoordinator(),
            new Session("session-1"),
            new Thread("session-1", "test-agent"),
            CancellationToken.None);
        var beforeContext = agentContext.AsBeforeFunction(
            function,
            "call-1",
            new Dictionary<string, object?>(),
            runConfig,
            toolharnessName: "CodingToolHarness");
        var request = new FunctionRequest
        {
            Function = function,
            CallId = "call-1",
            Arguments = new Dictionary<string, object?>(),
            State = state,
            RunConfig = runConfig
        };

        return new FunctionExecutionContext(beforeContext, request);
    }

    private static AgentRunConfig CreateWorkspaceRunConfig(string defaultRoot, string? docsRoot = null)
    {
        var roots = new List<AgentWorkspaceRoot>
        {
            new("default", Path.GetFullPath(defaultRoot))
        };

        if (docsRoot is not null)
            roots.Add(new AgentWorkspaceRoot("docs", Path.GetFullPath(docsRoot), "Docs"));

        return new AgentRunConfig
        {
            ContextOverrides = new()
            {
                [AgentWorkspace.ContextKey] = new AgentWorkspace(
                    "default",
                    Path.GetFullPath(defaultRoot),
                    roots)
            }
        };
    }

    private sealed class FakeDirectoryListingSource(string fileName) : IDirectoryListingSource
    {
        public ValueTask<DirectoryListingSourceResult?> TryListAsync(
            DirectoryListingRequest request,
            CancellationToken cancellationToken)
            => ValueTask.FromResult<DirectoryListingSourceResult?>(new DirectoryListingSourceResult
            {
                FullPath = request.FullPath,
                Entries =
                [
                    new DirectoryEntryInfo
                    {
                        RelativePath = fileName,
                        Kind = DirectoryEntryKind.File,
                        Size = 12,
                        LastWriteTimeUtc = DateTimeOffset.UtcNow
                    }
                ],
                TotalEntries = "1",
                IgnoredCount = 0,
                Truncated = false
            });
    }
}
