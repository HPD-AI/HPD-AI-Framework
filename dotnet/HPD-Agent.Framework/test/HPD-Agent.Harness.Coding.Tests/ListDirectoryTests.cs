using System.Xml.Linq;
using FluentAssertions;

namespace HPD.Agent.ToolHarness.Coding.Tests;

[Collection(CurrentDirectoryCollection.Name)]
public sealed class ListDirectoryTests : IDisposable
{
    private readonly string _originalCwd = Directory.GetCurrentDirectory();
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"hpd-portable-list-{Guid.NewGuid():N}");

    public ListDirectoryTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src", "nested"));
        Directory.CreateDirectory(Path.Combine(_root, ".hidden"));
        Directory.CreateDirectory(Path.Combine(_root, "empty"));
        Directory.SetCurrentDirectory(_root);
        File.WriteAllText(Path.Combine("src", "A.cs"), "abc");
        File.WriteAllText(Path.Combine("src", "nested", "B.cs"), "12345");
        File.WriteAllText(Path.Combine(".hidden", "secret.txt"), "x");
        File.WriteAllText(Path.Combine("A&B.cs"), "x");
    }

    [Fact]
    public void ListDirectory_requires_permission_metadata()
    {
        var method = typeof(CodingToolHarness).GetMethod(
            nameof(CodingToolHarness.ListDirectory));

        method.Should().NotBeNull();
        method!.GetCustomAttributes(
                typeof(RequiresPermissionAttribute),
                inherit: false)
            .Should().ContainSingle();
    }

    [Fact]
    public async Task Lists_directories_first_with_logical_relative_paths()
    {
        string result = await CreateHarness().ListDirectory(".");

        XElement root = XElement.Parse(result);
        root.Attribute("path")!.Value.Should().Be(".");
        root.Elements("entry").Select(item => (
                item.Attribute("kind")!.Value,
                item.Attribute("path")!.Value))
            .Should().Equal(
                ("directory", "empty/"),
                ("directory", "src/"),
                ("file", "A&B.cs"));
    }

    [Fact]
    public async Task Absolute_execution_path_maps_to_same_workspace_directory()
    {
        string relative = await CreateHarness().ListDirectory("src");
        string absolute = await CreateHarness().ListDirectory(
            Path.Combine(_root, "src"));

        XElement.Parse(relative).Attribute("path")!.Value.Should().Be("src");
        XElement.Parse(absolute).Attribute("path")!.Value.Should().Be("src");
        XElement.Parse(absolute).Elements("entry").Select(item =>
                item.Attribute("path")!.Value)
            .Should().Contain("A.cs");
    }

    [Fact]
    public async Task Recursive_listing_uses_depth_and_relative_paths()
    {
        string result = await CreateHarness().ListDirectory(
            "src",
            recursive: true,
            maxDepth: 2);

        XElement root = XElement.Parse(result);
        root.Elements("entry").Select(item =>
            item.Attribute("path")!.Value).Should().Equal(
            "nested/",
            "A.cs",
            "nested/B.cs");
    }

    [Fact]
    public async Task Hidden_entries_are_explicitly_included()
    {
        string hiddenByDefault = await CreateHarness()
            .ListDirectory(".");
        string included = await CreateHarness().ListDirectory(
            ".",
            includeHidden: true);

        XElement.Parse(hiddenByDefault).Elements("entry")
            .Select(item => item.Attribute("path")!.Value)
            .Should().NotContain(".hidden/");
        XElement.Parse(included).Elements("entry")
            .Select(item => item.Attribute("path")!.Value)
            .Should().Contain(".hidden/");
    }

    [Fact]
    public async Task Kind_filter_and_metadata_are_applied_by_the_tool()
    {
        string files = await CreateHarness().ListDirectory(
            "src",
            kind: DirectoryEntryKindFilter.Files,
            includeMetadata: true);

        XElement root = XElement.Parse(files);
        root.Elements("entry").Should().ContainSingle().Which
            .Attribute("path")!.Value.Should().Be("A.cs");
        root.Elements("entry").Single().Attribute("size")!.Value.Should()
            .Be("3");
    }

    [Fact]
    public async Task Offset_limit_and_next_page_are_bounded()
    {
        string result = await CreateHarness().ListDirectory(
            ".",
            offset: 2,
            limit: 1);

        XElement root = XElement.Parse(result);
        root.Elements("entry").Should().ContainSingle();
        root.Attribute("truncated")!.Value.Should().Be("true");
    }

    [Fact]
    public async Task Empty_and_missing_directories_are_distinct()
    {
        string empty = await CreateHarness().ListDirectory("empty");
        string missing = await CreateHarness().ListDirectory("missing");

        XElement.Parse(empty).Name.LocalName.Should().Be("directory");
        missing.Should().Contain("Directory does not exist.");
    }

    [Fact]
    public async Task File_path_returns_list_directory_redirect()
    {
        string result = await CreateHarness().ListDirectory("src/A.cs");

        result.Should().Contain("Path is a file. Use ReadFile instead.");
    }

    [Fact]
    public async Task Xml_sensitive_names_are_escaped()
    {
        string result = await CreateHarness().ListDirectory(".");

        XElement.Parse(result).Elements("entry")
            .Select(item => item.Attribute("path")!.Value)
            .Should().Contain("A&B.cs");
    }

    [Fact]
    public async Task Invalid_arguments_are_rejected_before_storage_access()
    {
        string result = await CreateHarness().ListDirectory(
            ".",
            limit: 1001);

        result.Should().Contain("Limit must be between 1 and 1000.");
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_originalCwd);
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static TestCodingHarness CreateHarness() =>
        CodingHarnessTestFactory.Create();
}
