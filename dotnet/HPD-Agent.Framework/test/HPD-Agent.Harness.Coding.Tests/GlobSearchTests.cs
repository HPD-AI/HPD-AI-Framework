using HPD.Agent;
using HPD.Agent.ToolHarness.Coding;
using HPD.Agent.Middleware;
using HPD.Events.Core;
using HPDOS.ToolHarnesses.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent.ToolHarness.Coding.Tests;

[Collection(CurrentDirectoryCollection.Name)]
public sealed class GlobSearchTests : IDisposable
{
    private readonly string _originalCwd = Directory.GetCurrentDirectory();
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"hpd-glob-search-tests-{Guid.NewGuid():N}");

    public GlobSearchTests()
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
    public void GlobSearch_RequiresPermission()
    {
        var method = typeof(CodingToolHarness).GetMethod(nameof(CodingToolHarness.GlobSearch));

        method.Should().NotBeNull();
        method!.GetCustomAttributes(typeof(RequiresPermissionAttribute), inherit: false)
            .Should().ContainSingle();
    }

    [Fact]
    public async Task GlobSearch_FindsFilesWithRecursivePattern()
    {
        Directory.CreateDirectory("src");
        await File.WriteAllTextAsync(Path.Combine("src", "Program.cs"), "class Program {}\n");
        await File.WriteAllTextAsync(Path.Combine("src", "Program.ts"), "export {}\n");

        var result = await new CodingToolHarness().GlobSearch("**/*.cs");

        result.Should().Contain("<glob ");
        result.Should().Contain("pattern=\"**/*.cs\"");
        result.Should().Contain("original_pattern=\"**/*.cs\"");
        result.Should().Contain("matches_read=\"1\"");
        result.Should().Contain("<match kind=\"file\" path=\"src/Program.cs\" />");
        result.Should().NotContain("Program.ts");
    }

    [Fact]
    public async Task GlobSearch_SupportsBraceExpansionPatterns()
    {
        await File.WriteAllTextAsync("file.js", "export {}\n");
        await File.WriteAllTextAsync("file.ts", "export {}\n");
        await File.WriteAllTextAsync("file.py", "print('no')\n");

        var result = await new CodingToolHarness().GlobSearch("*.{js,ts}");

        result.Should().Contain("pattern=\"*.{js,ts}\"");
        result.Should().Contain("path=\"file.js\"");
        result.Should().Contain("path=\"file.ts\"");
        result.Should().NotContain("file.py");
    }

    [Fact]
    public async Task GlobSearch_SupportsRecursiveBraceExpansionPatterns()
    {
        Directory.CreateDirectory(Path.Combine("src", "components"));
        await File.WriteAllTextAsync(Path.Combine("src", "components", "Button.tsx"), "export {}\n");
        await File.WriteAllTextAsync(Path.Combine("src", "components", "Card.jsx"), "export {}\n");
        await File.WriteAllTextAsync(Path.Combine("src", "components", "style.css"), ".card {}\n");

        var result = await new CodingToolHarness().GlobSearch("**/*.{tsx,jsx}");

        result.Should().Contain("pattern=\"**/*.{tsx,jsx}\"");
        result.Should().Contain("path=\"src/components/Button.tsx\"");
        result.Should().Contain("path=\"src/components/Card.jsx\"");
        result.Should().NotContain("style.css");
    }

    [Fact]
    public async Task GlobSearch_FindsFilesWithSimpleRootPattern()
    {
        await File.WriteAllTextAsync("fileA.txt", "content\n");
        await File.WriteAllTextAsync("FileB.TXT", "content\n");
        Directory.CreateDirectory("sub");
        await File.WriteAllTextAsync(Path.Combine("sub", "nested.txt"), "content\n");

        var result = await new CodingToolHarness().GlobSearch("*.txt");

        result.Should().Contain("matches_read=\"2\"");
        result.Should().Contain("path=\"fileA.txt\"");
        result.Should().Contain("path=\"FileB.TXT\"");
        result.Should().NotContain("nested.txt");
    }

    [Fact]
    public async Task GlobSearch_DefaultsToCurrentDirectoryWhenPathIsOmitted()
    {
        await File.WriteAllTextAsync("Root.cs", "class Root {}\n");
        Directory.CreateDirectory("src");
        await File.WriteAllTextAsync(Path.Combine("src", "Nested.cs"), "class Nested {}\n");

        var result = await new CodingToolHarness().GlobSearch("**/*.cs");

        result.Should().Contain($"path=\"{Directory.GetCurrentDirectory()}");
        result.Should().Contain("path=\"Root.cs\"");
        result.Should().Contain("path=\"src/Nested.cs\"");
    }

    [Fact]
    public async Task GlobSearch_UsesRunConfigWorkspaceWhenContextIsProvided()
    {
        var workspaceRoot = Path.Combine(_tempRoot, "workspace-root");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(Path.Combine(workspaceRoot, "src"));
        await File.WriteAllTextAsync(Path.Combine(workspaceRoot, "src", "Workspace.cs"), "class Workspace {}\n");
        await File.WriteAllTextAsync(Path.Combine(_tempRoot, "Cwd.cs"), "class Cwd {}\n");

        var result = await new CodingToolHarness().GlobSearch(
            "**/*.cs",
            context: CreateFunctionContext(CreateWorkspaceRunConfig(workspaceRoot)));

        result.Should().Contain($"path=\"{workspaceRoot}");
        result.Should().Contain("path=\"src/Workspace.cs\"");
        result.Should().NotContain("Cwd.cs");
    }

    [Fact]
    public async Task GlobSearch_ResolvesRelativePathFromRunConfigWorkspace()
    {
        var workspaceRoot = Path.Combine(_tempRoot, "workspace-root");
        Directory.CreateDirectory(Path.Combine(workspaceRoot, "src"));
        await File.WriteAllTextAsync(Path.Combine(workspaceRoot, "src", "App.cs"), "class App {}\n");
        await File.WriteAllTextAsync(Path.Combine(workspaceRoot, "Root.cs"), "class Root {}\n");

        var result = await new CodingToolHarness().GlobSearch(
            "**/*.cs",
            path: "src",
            context: CreateFunctionContext(CreateWorkspaceRunConfig(workspaceRoot)));

        result.Should().Contain($"path=\"{Path.Combine(workspaceRoot, "src")}");
        result.Should().Contain("path=\"App.cs\"");
        result.Should().NotContain("Root.cs");
    }

    [Fact]
    public async Task GlobSearch_RejectsAbsolutePatternOutsideRunConfigWorkspace()
    {
        var workspaceRoot = Path.Combine(_tempRoot, "workspace-root");
        var outsideRoot = Path.Combine(_tempRoot, "outside-root");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(outsideRoot);
        var outsidePattern = Path.Combine(outsideRoot, "*.cs");

        var result = await new CodingToolHarness().GlobSearch(
            outsidePattern,
            context: CreateFunctionContext(CreateWorkspaceRunConfig(workspaceRoot)));

        result.Should().Contain("<error tool=\"GlobSearch\"");
        result.Should().Contain("Path is outside the configured workspace.");
    }

    [Fact]
    public async Task GlobSearch_NormalizesBareFilenameToRecursiveSearch()
    {
        Directory.CreateDirectory(Path.Combine("src", "app"));
        await File.WriteAllTextAsync(Path.Combine("src", "app", "Program.cs"), "class Program {}\n");

        var result = await new CodingToolHarness().GlobSearch("Program.cs");

        result.Should().Contain("pattern=\"**/Program.cs\"");
        result.Should().Contain("original_pattern=\"Program.cs\"");
        result.Should().Contain("path=\"src/app/Program.cs\"");
    }

    [Fact]
    public async Task GlobSearch_NormalizesTrailingSlashToTreeSearch()
    {
        Directory.CreateDirectory(Path.Combine("src", "app"));
        await File.WriteAllTextAsync(Path.Combine("src", "app", "Program.cs"), "class Program {}\n");

        var result = await new CodingToolHarness().GlobSearch("src/", kind: GlobEntryKindFilter.All);

        result.Should().Contain("pattern=\"src/**\"");
        result.Should().Contain("<match kind=\"directory\" path=\"src/\" />");
        result.Should().Contain("<match kind=\"file\" path=\"src/app/Program.cs\" />");
    }

    [Fact]
    public async Task GlobSearch_ExtractsStaticBaseDirectory()
    {
        Directory.CreateDirectory(Path.Combine("src", "tools"));
        await File.WriteAllTextAsync(Path.Combine("src", "tools", "GlobTool.cs"), "class GlobTool {}\n");
        await File.WriteAllTextAsync(Path.Combine("src", "Other.cs"), "class Other {}\n");

        var result = await new CodingToolHarness().GlobSearch("src/tools/**/*.cs");

        result.Should().Contain("effective_path=\"");
        result.Should().Contain("src/tools");
        result.Should().Contain("pattern=\"**/*.cs\"");
        result.Should().Contain("original_pattern=\"src/tools/**/*.cs\"");
        result.Should().Contain("path=\"GlobTool.cs\"");
        result.Should().NotContain("Other.cs");
    }

    [Fact]
    public async Task GlobSearch_FindsDirectoryPrefixedLiteralFilenamePattern()
    {
        Directory.CreateDirectory("src");
        await File.WriteAllTextAsync(Path.Combine("src", "Program.cs"), "class Program {}\n");
        await File.WriteAllTextAsync("Program.cs", "class RootProgram {}\n");

        var result = await new CodingToolHarness().GlobSearch("src/Program.cs");

        result.Should().Contain("effective_path=\"");
        result.Should().Contain("src");
        result.Should().Contain("pattern=\"Program.cs\"");
        result.Should().Contain("original_pattern=\"src/Program.cs\"");
        result.Should().Contain("path=\"Program.cs\"");
        result.Should().NotContain("path=\"src/Program.cs\"");
    }

    [Fact]
    public async Task GlobSearch_SupportsAbsoluteGlobPatterns()
    {
        var externalRoot = Path.Combine(Path.GetTempPath(), $"hpd-glob-absolute-{Guid.NewGuid():N}");
        Directory.CreateDirectory(externalRoot);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(externalRoot, "one.md"), "# one\n");
            await File.WriteAllTextAsync(Path.Combine(externalRoot, "two.md"), "# two\n");
            await File.WriteAllTextAsync(Path.Combine(externalRoot, "three.txt"), "three\n");

            var result = await new CodingToolHarness().GlobSearch(Path.Combine(externalRoot, "*.md"));

            result.Should().Contain($"effective_path=\"{externalRoot}");
            result.Should().Contain("pattern=\"*.md\"");
            result.Should().Contain("path=\"one.md\"");
            result.Should().Contain("path=\"two.md\"");
            result.Should().NotContain("three.txt");
        }
        finally
        {
            if (Directory.Exists(externalRoot))
                Directory.Delete(externalRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GlobSearch_ResolvesRelativePathFromCurrentDirectory()
    {
        Directory.CreateDirectory("src");
        await File.WriteAllTextAsync(Path.Combine("src", "App.cs"), "class App {}\n");
        await File.WriteAllTextAsync("Root.cs", "class Root {}\n");

        var result = await new CodingToolHarness().GlobSearch("**/*.cs", path: "src");

        result.Should().Contain("path=\"");
        result.Should().Contain("hpd-glob-search-tests-");
        result.Should().Contain("/src\"");
        result.Should().Contain("effective_path=\"");
        result.Should().Contain("path=\"App.cs\"");
        result.Should().NotContain("Root.cs");
    }

    [Fact]
    public async Task GlobSearch_SupportsAbsoluteSearchPath()
    {
        Directory.CreateDirectory("src");
        await File.WriteAllTextAsync(Path.Combine("src", "App.cs"), "class App {}\n");

        var absolutePath = Path.Combine(_tempRoot, "src");
        var result = await new CodingToolHarness().GlobSearch("**/*.cs", path: absolutePath);

        result.Should().Contain($"path=\"{absolutePath}");
        result.Should().Contain("<match kind=\"file\" path=\"App.cs\" />");
    }

    [Fact]
    public async Task GlobSearch_AllowsNarrowLiteralFilesystemRootSearch()
    {
        var root = Path.GetPathRoot(_tempRoot)!;
        var missingRootLiteral = Path.Combine(root, $"hpd-definitely-missing-{Guid.NewGuid():N}.cs");

        var result = await new CodingToolHarness().GlobSearch(missingRootLiteral);

        result.Should().Contain("<glob ");
        result.Should().Contain("<no_matches");
        result.Should().NotContain("Pattern is too broad.");
    }

    [Fact]
    public async Task GlobSearch_RejectsInvalidArguments()
    {
        var toolharness = new CodingToolHarness();

        (await toolharness.GlobSearch(null!)).Should().Contain("Pattern is required.");
        (await toolharness.GlobSearch("   ")).Should().Contain("Pattern is required.");
        (await toolharness.GlobSearch("**/*.cs", path: null!)).Should().Contain("Path is required.");
        (await toolharness.GlobSearch("**/*.cs", path: "   ")).Should().Contain("Path is required.");
        (await toolharness.GlobSearch("**/*.cs", offset: 0)).Should().Contain("Offset must be greater than or equal to 1.");
        (await toolharness.GlobSearch("**/*.cs", limit: 0)).Should().Contain("Limit must be between 1 and 1000.");
        (await toolharness.GlobSearch("**/*.cs", limit: 1001)).Should().Contain("Limit must be between 1 and 1000.");
        (await toolharness.GlobSearch("**/*.cs", kind: (GlobEntryKindFilter)999)).Should().Contain("Kind must be a valid GlobEntryKindFilter value.");
        (await toolharness.GlobSearch("**/*.cs", sortBy: (GlobSortBy)999)).Should().Contain("SortBy must be a valid GlobSortBy value.");
        (await toolharness.GlobSearch("**/*.cs", sortDirection: (SortDirection)999)).Should().Contain("SortDirection must be a valid SortDirection value.");
    }

    [Fact]
    public async Task GlobSearch_RejectsMissingRootsAndSuggestsSimilarSibling()
    {
        Directory.CreateDirectory("src");

        var result = await new CodingToolHarness().GlobSearch("**/*.cs", path: "srcc");

        result.Should().Contain("<error tool=\"GlobSearch\"");
        result.Should().Contain("Directory does not exist. Did you mean");
        result.Should().Contain("src");
    }

    [Fact]
    public async Task GlobSearch_RejectsFileSearchRootsWithReadFileHint()
    {
        await File.WriteAllTextAsync("Program.cs", "class Program {}\n");

        var result = await new CodingToolHarness().GlobSearch("**/*.cs", path: "Program.cs");

        result.Should().Contain("Path is a file. Use ReadFile instead.");
    }

    [Fact]
    public async Task GlobSearch_RejectsBroadFilesystemRootSearches()
    {
        var result = await new CodingToolHarness().GlobSearch("**/*", path: Path.GetPathRoot(_tempRoot)!);

        result.Should().Contain("Pattern is too broad. Use a more specific path or pattern.");
    }

    [Fact]
    public async Task GlobSearch_BlocksUnixSystemPaths()
    {
        if (OperatingSystem.IsWindows())
            return;

        var result = await new CodingToolHarness().GlobSearch("**/*", path: "/dev");

        result.Should().Contain("Cannot search blocked system path.");
    }

    [Fact]
    public async Task GlobSearch_BlocksUncOrNetworkStylePaths()
    {
        var result = await new CodingToolHarness().GlobSearch("**/*.cs", path: "//server/share");
        var patternResult = await new CodingToolHarness().GlobSearch("//server/share/**/*.cs");

        result.Should().Contain("Cannot search blocked system path.");
        patternResult.Should().Contain("Cannot search blocked system path.");
    }

    [Fact]
    public async Task GlobSearch_HandlesNoMatchesAndEscapesXmlSensitiveFilenames()
    {
        await File.WriteAllTextAsync("a<&>.cs", "class A {}\n");

        var noMatches = await new CodingToolHarness().GlobSearch("**/*.go");
        var escaped = await new CodingToolHarness().GlobSearch("**/*.cs");

        noMatches.Should().Contain("<no_matches");
        escaped.Should().Contain("a&lt;&amp;&gt;.cs");
    }

    [Fact]
    public async Task GlobSearch_AppliesOffsetLimitAfterSortingAndEmitsNextGlob()
    {
        await File.WriteAllTextAsync("a.cs", "a\n");
        await File.WriteAllTextAsync("b.cs", "b\n");
        await File.WriteAllTextAsync("c.cs", "c\n");

        var result = await new CodingToolHarness().GlobSearch("**/*.cs", offset: 2, limit: 1);

        result.Should().Contain("matches_read=\"1\"");
        result.Should().Contain("truncated=\"true\"");
        result.Should().Contain("truncation_reason=\"limit\"");
        result.Should().Contain("path=\"b.cs\"");
        result.Should().NotContain("path=\"a.cs\"");
        result.Should().Contain("<next_glob offset=\"3\" limit=\"1\" reason=\"more_matches_available\" />");
        result.Should().Contain("<truncation_hint>");
    }

    [Fact]
    public async Task GlobSearch_HidesDotfilesByDefaultAndCanIncludeThem()
    {
        await File.WriteAllTextAsync(".env", "secret\n");
        await File.WriteAllTextAsync("visible.env", "visible\n");

        var hiddenByDefault = await new CodingToolHarness().GlobSearch("**/*.env");
        var included = await new CodingToolHarness().GlobSearch("**/*.env", includeHidden: true);

        hiddenByDefault.Should().NotContain("path=\".env\"");
        hiddenByDefault.Should().Contain("visible.env");
        included.Should().Contain(".env");
    }

    [Fact]
    public async Task GlobSearch_IncludesWindowsHiddenAttributeFilesWhenRequested()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await File.WriteAllTextAsync("secret.cs", "class Secret {}\n");
        await File.WriteAllTextAsync("visible.cs", "class Visible {}\n");
        File.SetAttributes("secret.cs", File.GetAttributes("secret.cs") | FileAttributes.Hidden);

        var hiddenByDefault = await new CodingToolHarness().GlobSearch("**/*.cs");
        var included = await new CodingToolHarness().GlobSearch("**/*.cs", includeHidden: true);

        hiddenByDefault.Should().NotContain("secret.cs");
        hiddenByDefault.Should().Contain("visible.cs");
        included.Should().Contain("secret.cs");
    }

    [Fact]
    public async Task GlobSearch_RespectsGitignoreWhenEnabled()
    {
        await File.WriteAllTextAsync(".gitignore", "ignored.cs\nignored-dir/\n");
        await File.WriteAllTextAsync("ignored.cs", "class Ignored {}\n");
        await File.WriteAllTextAsync("visible.cs", "class Visible {}\n");
        Directory.CreateDirectory("ignored-dir");
        await File.WriteAllTextAsync(Path.Combine("ignored-dir", "Nested.cs"), "class Nested {}\n");

        var respected = await new CodingToolHarness().GlobSearch("**/*.cs", includeHidden: true);
        var ignoredDisabled = await new CodingToolHarness().GlobSearch("**/*.cs", includeHidden: true, respectIgnoreFiles: false);

        respected.Should().NotContain("ignored.cs");
        respected.Should().NotContain("Nested.cs");
        respected.Should().Contain("visible.cs");
        respected.Should().Contain("ignored_count=\"");
        ignoredDisabled.Should().Contain("ignored.cs");
        ignoredDisabled.Should().Contain("ignored-dir/Nested.cs");
    }

    [Fact]
    public async Task GlobSearch_DoesNotApplyParentGitignoreOutsideSearchRoot()
    {
        var parent = "home";
        var repo = Path.Combine(parent, "repo");
        Directory.CreateDirectory(repo);
        await File.WriteAllTextAsync(Path.Combine(parent, ".gitignore"), "*\n!.gitignore\n");
        await File.WriteAllTextAsync(Path.Combine(repo, "package.json"), "{ \"name\": \"demo\" }\n");

        var result = await new CodingToolHarness().GlobSearch("**/*.json", path: repo);

        result.Should().Contain("path=\"package.json\"");
        result.Should().NotContain("<no_matches");
    }

    [Fact]
    public async Task GlobSearch_RespectsGitignoreNegationRules()
    {
        Directory.CreateDirectory("config");
        await File.WriteAllTextAsync(
            ".gitignore",
            "config/*\n!config/\n!config/settings.json\n!package.json\n");
        await File.WriteAllTextAsync("package.json", "{ \"name\": \"demo\" }\n");
        await File.WriteAllTextAsync(Path.Combine("config", "settings.json"), "{ \"editor\": true }\n");
        await File.WriteAllTextAsync(Path.Combine("config", "extensions.json"), "{ \"extensions\": [] }\n");

        var result = await new CodingToolHarness().GlobSearch("**/*.json", includeHidden: true);

        result.Should().Contain("path=\"package.json\"");
        result.Should().Contain("path=\"config/settings.json\"");
        result.Should().NotContain("path=\"config/extensions.json\"");
    }

    [Fact]
    public async Task GlobSearch_SkipsBuiltInGeneratedDirectories()
    {
        Directory.CreateDirectory("node_modules");
        await File.WriteAllTextAsync(Path.Combine("node_modules", "package.cs"), "class Package {}\n");
        await File.WriteAllTextAsync("App.cs", "class App {}\n");

        var result = await new CodingToolHarness().GlobSearch("**/*.cs", respectIgnoreFiles: false);

        result.Should().Contain("App.cs");
        result.Should().NotContain("node_modules");
        result.Should().Contain("ignored_count=\"");
    }

    [Fact]
    public async Task GlobSearch_FiltersByKindAndMarksDirectories()
    {
        Directory.CreateDirectory(Path.Combine("src", "features"));
        await File.WriteAllTextAsync(Path.Combine("src", "features", "Feature.cs"), "class Feature {}\n");

        var directories = await new CodingToolHarness().GlobSearch("src/**", kind: GlobEntryKindFilter.Directories);
        var all = await new CodingToolHarness().GlobSearch("src/**", kind: GlobEntryKindFilter.All);

        directories.Should().Contain("<match kind=\"directory\" path=\"features/\" />");
        directories.Should().NotContain("Feature.cs");
        all.Should().Contain("<match kind=\"file\" path=\"features/Feature.cs\" />");
    }

    [Fact]
    public async Task GlobSearch_FiltersToFiles()
    {
        Directory.CreateDirectory("src");
        await File.WriteAllTextAsync(Path.Combine("src", "App.cs"), "class App {}\n");

        var result = await new CodingToolHarness().GlobSearch("src/**", kind: GlobEntryKindFilter.Files);

        result.Should().Contain("<match kind=\"file\" path=\"App.cs\" />");
        result.Should().NotContain("kind=\"directory\"");
    }

    [Fact]
    public async Task GlobSearch_MatchesCaseInsensitivelyByDefaultAndCaseSensitivelyWhenRequested()
    {
        await File.WriteAllTextAsync("Program.CS", "class Program {}\n");

        var insensitive = await new CodingToolHarness().GlobSearch("**/*.cs");
        var sensitive = await new CodingToolHarness().GlobSearch("**/*.cs", caseSensitive: true);

        insensitive.Should().Contain("Program.CS");
        insensitive.Should().Contain("case_sensitive=\"false\"");
        sensitive.Should().NotContain("Program.CS");
        sensitive.Should().Contain("case_sensitive=\"true\"");
    }

    [Fact]
    public async Task GlobSearch_SortsByModifiedTimeBeforePaging()
    {
        await File.WriteAllTextAsync("older.cs", "older\n");
        await File.WriteAllTextAsync("middle.cs", "middle\n");
        await File.WriteAllTextAsync("newer.cs", "newer\n");

        File.SetLastWriteTimeUtc("older.cs", DateTime.UtcNow.AddDays(-5));
        File.SetLastWriteTimeUtc("middle.cs", DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc("newer.cs", DateTime.UtcNow);

        var result = await new CodingToolHarness().GlobSearch(
            "**/*.cs",
            limit: 1,
            sortBy: GlobSortBy.ModifiedTime,
            sortDirection: SortDirection.Descending);

        result.Should().Contain("path=\"newer.cs\"");
        result.Should().NotContain("older.cs");
        result.Should().Contain("sort_by=\"modified_time\"");
    }

    [Fact]
    public async Task GlobSearch_SortsByPathByDefault()
    {
        await File.WriteAllTextAsync("b.cs", "b\n");
        await File.WriteAllTextAsync("a.cs", "a\n");

        var result = await new CodingToolHarness().GlobSearch("**/*.cs");

        result.IndexOf("path=\"a.cs\"", StringComparison.Ordinal)
            .Should().BeLessThan(result.IndexOf("path=\"b.cs\"", StringComparison.Ordinal));
        result.Should().Contain("sort_by=\"path\"");
    }

    [Fact]
    public async Task GlobSearch_SortsBySize()
    {
        await File.WriteAllTextAsync("small.cs", "x\n");
        await File.WriteAllTextAsync("large.cs", new string('x', 100));

        var result = await new CodingToolHarness().GlobSearch(
            "**/*.cs",
            sortBy: GlobSortBy.Size,
            sortDirection: SortDirection.Descending);

        result.IndexOf("path=\"large.cs\"", StringComparison.Ordinal)
            .Should().BeLessThan(result.IndexOf("path=\"small.cs\"", StringComparison.Ordinal));
        result.Should().Contain("sort_by=\"size\"");
    }

    [Fact]
    public async Task GlobSearch_SortsByKind()
    {
        Directory.CreateDirectory("src");
        await File.WriteAllTextAsync("App.cs", "class App {}\n");

        var result = await new CodingToolHarness().GlobSearch("**", kind: GlobEntryKindFilter.All, sortBy: GlobSortBy.Kind);

        result.IndexOf("kind=\"directory\" path=\"src/\"", StringComparison.Ordinal)
            .Should().BeLessThan(result.IndexOf("kind=\"file\" path=\"App.cs\"", StringComparison.Ordinal));
        result.Should().Contain("sort_by=\"kind\"");
    }

    [Fact]
    public async Task GlobSearch_AppliesDescendingSortDirection()
    {
        await File.WriteAllTextAsync("a.cs", "a\n");
        await File.WriteAllTextAsync("z.cs", "z\n");

        var result = await new CodingToolHarness().GlobSearch(
            "**/*.cs",
            sortDirection: SortDirection.Descending);

        result.IndexOf("path=\"z.cs\"", StringComparison.Ordinal)
            .Should().BeLessThan(result.IndexOf("path=\"a.cs\"", StringComparison.Ordinal));
        result.Should().Contain("sort_direction=\"descending\"");
    }

    [Fact]
    public async Task GlobSearch_SortsByRecencyWithRecentMatchesFirst()
    {
        await File.WriteAllTextAsync("alpha.cs", "old alpha\n");
        await File.WriteAllTextAsync("zeta.cs", "recent zeta\n");
        await File.WriteAllTextAsync("middle.cs", "old middle\n");

        File.SetLastWriteTimeUtc("alpha.cs", DateTime.UtcNow.AddDays(-3));
        File.SetLastWriteTimeUtc("middle.cs", DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc("zeta.cs", DateTime.UtcNow);

        var result = await new CodingToolHarness().GlobSearch("**/*.cs", sortBy: GlobSortBy.Recency);

        result.IndexOf("path=\"zeta.cs\"", StringComparison.Ordinal)
            .Should().BeLessThan(result.IndexOf("path=\"alpha.cs\"", StringComparison.Ordinal));
        result.IndexOf("path=\"alpha.cs\"", StringComparison.Ordinal)
            .Should().BeLessThan(result.IndexOf("path=\"middle.cs\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GlobSearch_SortsCompleteStableCandidateSetBeforeRecencyPaging()
    {
        await File.WriteAllTextAsync("a-old.cs", "old\n");
        await File.WriteAllTextAsync("b-new.cs", "new\n");
        await File.WriteAllTextAsync("c-old.cs", "old\n");

        File.SetLastWriteTimeUtc("a-old.cs", DateTime.UtcNow.AddDays(-3));
        File.SetLastWriteTimeUtc("c-old.cs", DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc("b-new.cs", DateTime.UtcNow);

        var result = await new CodingToolHarness().GlobSearch("**/*.cs", limit: 1, sortBy: GlobSortBy.Recency);

        result.Should().Contain("path=\"b-new.cs\"");
        result.Should().NotContain("a-old.cs");
        result.Should().Contain("<next_glob");
    }

    [Fact]
    public async Task GlobSearch_StopsAtTraversalCapAndReturnsStructuredPartialOutput()
    {
        await File.WriteAllTextAsync("a.cs", "a\n");
        await File.WriteAllTextAsync("b.cs", "b\n");
        await File.WriteAllTextAsync("c.cs", "c\n");
        var toolharness = new CodingToolHarness(
            null,
            null,
            null,
            new GlobSearchOptions { MaxTraversalEntries = 2, TraversalTimeoutMilliseconds = 10_000 });

        var result = await toolharness.GlobSearch("**/*.cs", limit: 10);

        result.Should().Contain("truncated=\"true\"");
        result.Should().Contain("truncation_reason=\"traversal_cap\"");
        result.Should().Contain("<truncation_hint>");
        result.Should().Contain("matches_read=\"2\"");
    }

    [Fact]
    public async Task GlobSearch_StopsAtTimeoutAndReturnsStructuredPartialOutput()
    {
        await File.WriteAllTextAsync("a.cs", "a\n");
        var toolharness = new CodingToolHarness(
            null,
            null,
            null,
            new GlobSearchOptions { MaxTraversalEntries = 50_000, TraversalTimeoutMilliseconds = -1 });

        var result = await toolharness.GlobSearch("**/*.cs", limit: 10);

        result.Should().Contain("truncated=\"true\"");
        result.Should().Contain("truncation_reason=\"timeout\"");
        result.Should().Contain("total_matches=\"unknown\"");
        result.Should().Contain("<truncation_hint>Traversal timed out. Use a more specific path or pattern.</truncation_hint>");
    }

    [Fact]
    public async Task GlobSearch_DoesNotFollowSymlinkedDirectories()
    {
        if (OperatingSystem.IsWindows())
            return;

        Directory.CreateDirectory("real-dir");
        await File.WriteAllTextAsync(Path.Combine("real-dir", "inside.cs"), "class Inside {}\n");

        try
        {
            Directory.CreateSymbolicLink("linked-dir", Path.GetFullPath("real-dir"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        var result = await new CodingToolHarness().GlobSearch("**/*.cs", respectIgnoreFiles: false);
        var directories = await new CodingToolHarness().GlobSearch("**", kind: GlobEntryKindFilter.Directories, respectIgnoreFiles: false);

        result.Should().Contain("real-dir/inside.cs");
        result.Should().NotContain("linked-dir/inside.cs");
        directories.Should().Contain("linked-dir/");
    }

    [Fact]
    public async Task GlobSearch_FindsLiteralFilenamesContainingGlobSpecialCharacters()
    {
        await File.WriteAllTextAsync("file[1].cs", "class File1 {}\n");

        var result = await new CodingToolHarness().GlobSearch("file[1].cs");

        result.Should().Contain("original_pattern=\"file[1].cs\"");
        result.Should().Contain("path=\"file[1].cs\"");
    }

    [Fact]
    public async Task GlobSearch_FindsLiteralPathsContainingGlobSpecialCharacters()
    {
        var directory = Path.Combine("src", "app", "[test]", "(dashboard)", "components");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "code.tsx"), "export {}\n");

        var result = await new CodingToolHarness().GlobSearch("src/app/[test]/(dashboard)/components/code.tsx");

        result.Should().Contain("original_pattern=\"src/app/[test]/(dashboard)/components/code.tsx\"");
        result.Should().Contain("path=\"code.tsx\"");
    }

    [Fact]
    public async Task GlobSearch_UsesHostPathResolverBeforeLocalResolution()
    {
        Directory.CreateDirectory("workspace-a");
        await File.WriteAllTextAsync(Path.Combine("workspace-a", "App.ts"), "export {}\n");
        await File.WriteAllTextAsync("App.ts", "export {}\n");
        var resolver = new FakeGlobSearchPathResolver(
            new ResolvedGlobSearch(
                "workspace-a",
                "workspace-a/**/*.ts",
                _tempRoot,
                Path.Combine(_tempRoot, "workspace-a"),
                "**/*.ts"));

        var result = await new CodingToolHarness(null, null, [resolver]).GlobSearch("workspace-a/**/*.ts");

        result.Should().Contain($"effective_path=\"{Path.Combine(_tempRoot, "workspace-a")}");
        result.Should().Contain("path=\"App.ts\"");
        result.Should().NotContain("path=\"workspace-a/App.ts\"");
    }

    [Fact]
    public void GlobSearch_DoesNotUseStaticOrInstanceCachesForGlobState()
    {
        var fields = typeof(CodingToolHarness).GetFields(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public);

        fields
            .Where(field => field.DeclaringType == typeof(CodingToolHarness))
            .Select(field => field.Name)
            .Should().NotContain(name => name.Contains("cache", StringComparison.OrdinalIgnoreCase));
    }

    private static FunctionExecutionContext CreateFunctionContext(AgentRunConfig runConfig)
    {
        var function = AIFunctionFactory.Create(
            () => "ok",
            new AIFunctionFactoryOptions
            {
                Name = "GlobSearch",
                Description = "Test function"
            });
        var state = AgentLoopState.InitialSafe([], "run-1", "conversation-1", "AgentA");
        var agentContext = new AgentContext(
            "AgentA",
            "conversation-1",
            state,
            new EventCoordinator(),
            new Session("session-1"),
            new Thread("session-1"),
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

    private static AgentRunConfig CreateWorkspaceRunConfig(string defaultRoot)
    {
        var fullRoot = Path.GetFullPath(defaultRoot);
        return new AgentRunConfig
        {
            ContextOverrides = new()
            {
                [AgentWorkspace.ContextKey] = new AgentWorkspace(
                    "default",
                    fullRoot,
                    [new AgentWorkspaceRoot("default", fullRoot)])
            }
        };
    }

    private sealed class FakeGlobSearchPathResolver(ResolvedGlobSearch resolved) : IGlobSearchPathResolver
    {
        public ValueTask<ResolvedGlobSearch?> TryResolveAsync(
            AgentWorkspace workspace,
            string path,
            string pattern,
            CancellationToken cancellationToken)
            => ValueTask.FromResult<ResolvedGlobSearch?>(resolved);
    }
}
