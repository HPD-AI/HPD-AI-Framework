using System.Xml.Linq;
using FluentAssertions;

namespace HPD.Agent.ToolHarness.Coding.Tests;

[Collection(CurrentDirectoryCollection.Name)]
public sealed class GrepTests : IDisposable
{
    private readonly string _originalCwd = Directory.GetCurrentDirectory();
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"hpd-portable-grep-{Guid.NewGuid():N}");

    public GrepTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        Directory.SetCurrentDirectory(_root);
        File.WriteAllText(
            Path.Combine("src", "A.cs"),
            "TODO alpha\nother\n");
        File.WriteAllText(
            Path.Combine("src", "B.cs"),
            "beta TODO\n");
        File.WriteAllText(
            Path.Combine("src", "notes.md"),
            "todo markdown\n");
        File.SetLastWriteTimeUtc(
            Path.Combine("src", "A.cs"),
            DateTime.UtcNow.AddMinutes(-1));
        File.SetLastWriteTimeUtc(
            Path.Combine("src", "B.cs"),
            DateTime.UtcNow);
    }

    [Fact]
    public async Task Files_with_matches_returns_logical_workspace_paths()
    {
        string result = await CreateHarness().Grep("TODO");

        XElement root = XElement.Parse(result);
        root.Name.LocalName.Should().Be("grep");
        root.Attribute("output_mode")!.Value.Should()
            .Be("files_with_matches");
        root.Elements("file").Select(item => item.Attribute("path")!.Value)
            .Should().Equal("src/B.cs", "src/A.cs");
    }

    [Fact]
    public async Task Content_and_count_modes_use_portable_search()
    {
        string content = await CreateHarness().Grep(
            "TODO",
            outputMode: GrepOutputMode.Content);
        string count = await CreateHarness().Grep(
            "TODO",
            outputMode: GrepOutputMode.Count);

        XElement contentXml = XElement.Parse(content);
        contentXml.Elements("match").Should().NotBeEmpty();
        contentXml.Elements("match").All(item =>
            item.Attribute("path")!.Value.StartsWith(
                "src/",
                StringComparison.Ordinal)).Should().BeTrue();
        XElement countXml = XElement.Parse(count);
        countXml.Attribute("output_mode")!.Value.Should().Be("count");
        countXml.Elements("count").Sum(item =>
            int.Parse(item.Attribute("matches")!.Value)).Should().Be(2);
    }

    [Fact]
    public async Task Absolute_execution_path_is_translated_to_logical_root()
    {
        string result = await CreateHarness().Grep(
            "TODO",
            path: Path.Combine(_root, "src"));

        XElement root = XElement.Parse(result);
        root.Attribute("path")!.Value.Should().Be("src");
        root.Elements("file").Select(item =>
            item.Attribute("path")!.Value).Should().Equal(
            "src/B.cs",
            "src/A.cs");
    }

    [Fact]
    public async Task Include_and_exclude_globs_are_root_scoped()
    {
        string result = await CreateHarness().Grep(
            "TODO",
            includeGlobs: ["**/*.cs"],
            excludeGlobs: ["src/B.cs"]);

        XElement root = XElement.Parse(result);
        root.Elements("file").Select(item =>
            item.Attribute("path")!.Value).Should().ContainSingle()
            .Which.Should().Be("src/A.cs");
    }

    [Fact]
    public async Task Case_modes_are_applied_by_the_workspace_store()
    {
        string sensitive = await CreateHarness().Grep(
            "TODO",
            caseMode: GrepCaseMode.Sensitive);
        string insensitive = await CreateHarness().Grep(
            "todo",
            caseMode: GrepCaseMode.Insensitive);

        XElement.Parse(sensitive).Elements("file")
            .Select(item => item.Attribute("path")!.Value)
            .Should().NotContain("src/notes.md");
        XElement.Parse(insensitive).Elements("file").Select(item =>
            item.Attribute("path")!.Value).Should().Contain(
            "src/notes.md");
    }

    [Fact]
    public async Task Fixed_string_and_regex_modes_are_explicit()
    {
        string literal = await CreateHarness().Grep(
            "TODO",
            fixedStrings: true,
            wordRegexp: true);
        string regex = await CreateHarness().Grep(
            @"TOD[O]",
            outputMode: GrepOutputMode.Content);

        XElement.Parse(literal).Elements("file").Should().HaveCount(2);
        XElement.Parse(regex).Elements("match").Should().NotBeEmpty();
    }

    [Fact]
    public async Task Missing_path_is_reported_without_host_fallback()
    {
        string result = await CreateHarness().Grep(
            "TODO",
            path: "missing");

        result.Should().Contain("Path does not exist.");
    }

    [Fact]
    public async Task Multiline_search_uses_backend_match_semantics()
    {
        string result = await CreateHarness().Grep(
            "TODO.*other",
            outputMode: GrepOutputMode.Content,
            multiline: true);

        XElement root = XElement.Parse(result);
        root.Elements("match").Should().ContainSingle();
        root.Elements("match").Single()
            .Value.Should().Contain("TODO alpha");
        root.Elements("match").Single()
            .Value.Should().Contain("other");
    }

    [Fact]
    public async Task Invalid_arguments_do_not_touch_a_backend()
    {
        string result = await CreateHarness().Grep(
            "TODO",
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
