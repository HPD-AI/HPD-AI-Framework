using System.Xml.Linq;
using FluentAssertions;

namespace HPD.Agent.ToolHarness.Coding.Tests;

[Collection(CurrentDirectoryCollection.Name)]
public sealed class GlobSearchTests : IDisposable
{
    private readonly string _originalCwd = Directory.GetCurrentDirectory();
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"hpd-portable-glob-{Guid.NewGuid():N}");

    public GlobSearchTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src", "nested"));
        Directory.CreateDirectory(Path.Combine(_root, ".hidden"));
        Directory.SetCurrentDirectory(_root);
        File.WriteAllText(Path.Combine("src", "A.cs"), "A");
        File.WriteAllText(Path.Combine("src", "nested", "B.cs"), "B");
        File.WriteAllText(Path.Combine("src", "notes.ts"), "T");
        File.WriteAllText(Path.Combine(".hidden", "secret.cs"), "S");
    }

    [Fact]
    public async Task Recursive_glob_returns_root_relative_logical_paths()
    {
        string result = await CreateHarness().GlobSearch("**/*.cs");

        XElement root = XElement.Parse(result);
        root.Name.LocalName.Should().Be("glob");
        root.Elements("match").Select(item =>
            item.Attribute("path")!.Value).Should().Equal(
            "src/A.cs",
            "src/nested/B.cs");
    }

    [Fact]
    public async Task Search_path_limits_the_logical_namespace()
    {
        string result = await CreateHarness().GlobSearch(
            "**/*.cs",
            path: "src/nested");

        XElement root = XElement.Parse(result);
        root.Attribute("path")!.Value.Should().Be("src/nested");
        root.Elements("match").Select(item =>
            item.Attribute("path")!.Value).Should().ContainSingle()
            .Which.Should().Be("B.cs");
    }

    [Fact]
    public async Task Absolute_execution_paths_are_mapped_back_to_logical_paths()
    {
        string result = await CreateHarness().GlobSearch(
            "*.cs",
            path: Path.Combine(_root, "src"));

        XElement root = XElement.Parse(result);
        root.Attribute("path")!.Value.Should().Be("src");
        root.Elements("match").Select(item =>
            item.Attribute("path")!.Value).Should().Contain("A.cs");
    }

    [Fact]
    public async Task Hidden_files_are_explicitly_opt_in()
    {
        string hiddenByDefault = await CreateHarness()
            .GlobSearch("**/*.cs");
        string included = await CreateHarness().GlobSearch(
            "**/*.cs",
            includeHidden: true);

        XElement.Parse(hiddenByDefault).Elements("match")
            .Select(item => item.Attribute("path")!.Value)
            .Should().NotContain(".hidden/secret.cs");
        XElement.Parse(included).Elements("match")
            .Select(item => item.Attribute("path")!.Value)
            .Should().Contain(".hidden/secret.cs");
    }

    [Fact]
    public async Task Case_sensitivity_is_provider_explicit()
    {
        string insensitive = await CreateHarness()
            .GlobSearch("**/*.CS");
        string sensitive = await CreateHarness()
            .GlobSearch("**/*.CS", caseSensitive: true);

        XElement.Parse(insensitive).Elements("match").Should().HaveCount(2);
        XElement.Parse(sensitive).Elements("match").Should().BeEmpty();
    }

    [Fact]
    public async Task Directory_kind_filter_marks_workspace_directories()
    {
        string result = await CreateHarness().GlobSearch(
            "**",
            kind: GlobEntryKindFilter.Directories);

        XElement.Parse(result).Elements("match")
            .Select(item => item.Attribute("path")!.Value)
            .Should().Contain("src/")
            .And.Contain("src/nested/");
    }

    [Fact]
    public async Task Offset_and_limit_page_after_sorting()
    {
        File.WriteAllText(Path.Combine("src", "nested", "C.cs"), "C");
        string result = await CreateHarness().GlobSearch(
            "**/*.cs",
            offset: 2,
            limit: 1);

        XElement root = XElement.Parse(result);
        root.Elements("match").Select(item =>
            item.Attribute("path")!.Value).Should().ContainSingle()
            .Which.Should().Be("src/nested/B.cs");
        root.Element("next_glob").Should().NotBeNull();
    }

    [Fact]
    public async Task Missing_root_is_a_structured_error()
    {
        string result = await CreateHarness().GlobSearch(
            "**/*.cs",
            path: "missing");

        result.Should().Contain("<error");
        result.Should().Contain("Directory does not exist.");
    }

    [Fact]
    public async Task Xml_sensitive_names_are_escaped()
    {
        File.WriteAllText("A&B.cs", "x");

        string result = await CreateHarness().GlobSearch("A&B.cs");

        XElement root = XElement.Parse(result);
        root.Elements("match").Should().ContainSingle().Which
            .Attribute("path")!.Value.Should().Be("A&B.cs");
    }

    [Fact]
    public async Task Invalid_arguments_are_rejected_before_storage_access()
    {
        string result = await CreateHarness().GlobSearch(
            "**/*.cs",
            offset: 0);

        result.Should().Contain("Offset must be greater than or equal to 1.");
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
