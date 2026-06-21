using System.Runtime.CompilerServices;
using System.Reflection;
using HPD.Agent.ToolHarness.Coding.Ripgrep;
using HPD.Agent.Middleware;

namespace HPD.Agent.ToolHarness.Coding.Tests;

[Collection(CurrentDirectoryCollection.Name)]
public sealed class GrepTests : IDisposable
{
    private readonly string _originalCwd = Directory.GetCurrentDirectory();
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"hpd-grep-tests-{Guid.NewGuid():N}");

    public GrepTests()
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
    public void Grep_RequiresPermission()
    {
        var method = typeof(CodingToolHarness).GetMethod(nameof(CodingToolHarness.Grep));

        method.Should().NotBeNull();
        method!.GetCustomAttributes(typeof(RequiresPermissionAttribute), inherit: false)
            .Should().ContainSingle();
    }

    [Fact]
    public async Task Grep_DefaultsToFilesWithMatches()
    {
        Directory.CreateDirectory("src");
        await File.WriteAllTextAsync(Path.Combine("src", "A.cs"), "TODO\n");
        await File.WriteAllTextAsync(Path.Combine("src", "B.cs"), "TODO\n");
        File.SetLastWriteTimeUtc(Path.Combine("src", "A.cs"), DateTime.UtcNow.AddMinutes(-1));
        File.SetLastWriteTimeUtc(Path.Combine("src", "B.cs"), DateTime.UtcNow);
        var runner = new FakeRipgrepRunner
        {
            FilesWithMatchesResult = new RipgrepFilesWithMatchesResult
            {
                Files = ["src/A.cs", "src/B.cs"],
                Completion = Completion(RipgrepCompletionStatus.Success, 2)
            }
        };

        var result = await CreateToolHarness(runner).Grep("TODO");

        result.Should().Contain("output_mode=\"files_with_matches\"");
        result.Should().Contain("<file path=\"src/B.cs\" />");
        result.Should().Contain("<file path=\"src/A.cs\" />");
        result.IndexOf("src/B.cs", StringComparison.Ordinal)
            .Should().BeLessThan(result.IndexOf("src/A.cs", StringComparison.Ordinal));
        runner.FilesWithMatchesOptions.Should().NotBeNull();
        runner.FilesWithMatchesOptions!.Pattern.Should().Be("TODO");
        runner.FilesWithMatchesOptions.ExcludeGlobs.Should().Contain("**/.git/**");
    }

    [Fact]
    public async Task Grep_ContentOutputStreamsMatchEvents()
    {
        await File.WriteAllTextAsync("notes.txt", "hello\nTODO here\n");
        var runner = new FakeRipgrepRunner
        {
            SearchEvents =
            [
                new RipgrepMatchEvent
                {
                    Path = "notes.txt",
                    Text = "TODO here\n",
                    LineNumber = 2,
                    AbsoluteOffset = 6,
                    Submatches = []
                },
                Completion(RipgrepCompletionStatus.Success, 1)
            ]
        };

        var result = await CreateToolHarness(runner).Grep("TODO", path: "notes.txt", outputMode: GrepOutputMode.Content);

        result.Should().Contain("output_mode=\"content\"");
        result.Should().Contain("<match path=\"notes.txt\" line=\"2\">2\tTODO here</match>");
        runner.SearchOptions.Should().NotBeNull();
        runner.SearchOptions!.SearchPaths.Should().Equal("notes.txt");
    }

    [Fact]
    public async Task Grep_CountOutputFormatsCounts()
    {
        Directory.CreateDirectory("src");
        await File.WriteAllTextAsync(Path.Combine("src", "A.cs"), "TODO\nTODO\n");
        var runner = new FakeRipgrepRunner
        {
            CountResult = new RipgrepCountResult
            {
                Counts =
                [
                    new RipgrepCountEntry { Path = "src/A.cs", Count = 2 }
                ],
                Completion = Completion(RipgrepCompletionStatus.Success, 1)
            }
        };

        var result = await CreateToolHarness(runner).Grep("TODO", outputMode: GrepOutputMode.Count);

        result.Should().Contain("output_mode=\"count\"");
        result.Should().Contain("total_matches=\"2\"");
        result.Should().Contain("<count path=\"src/A.cs\" matches=\"2\" />");
    }

    [Fact]
    public async Task Grep_RejectsInvalidArguments()
    {
        var toolharness = CreateToolHarness(new FakeRipgrepRunner());

        (await toolharness.Grep(null!)).Should().Contain("Pattern is required.");
        (await toolharness.Grep("TODO", path: null!)).Should().Contain("Path is required.");
        (await toolharness.Grep("TODO", offset: 0)).Should().Contain("Offset must be greater than or equal to 1.");
        (await toolharness.Grep("TODO", limit: 0)).Should().Contain("Limit must be between 1 and 1000.");
        (await toolharness.Grep("TODO", limit: 1001)).Should().Contain("Limit must be between 1 and 1000.");
        (await toolharness.Grep("TODO", contextLines: -1, outputMode: GrepOutputMode.Content))
            .Should().Contain("ContextLines must be between 0 and 20.");
        (await toolharness.Grep("TODO", contextLines: 21, outputMode: GrepOutputMode.Content))
            .Should().Contain("ContextLines must be between 0 and 20.");
        (await toolharness.Grep("TODO", beforeContext: -1, outputMode: GrepOutputMode.Content))
            .Should().Contain("BeforeContext must be between 0 and 20.");
        (await toolharness.Grep("TODO", outputMode: GrepOutputMode.Count, contextLines: 1))
            .Should().Contain("Context parameters require outputMode Content.");
        (await toolharness.Grep("TODO", beforeContext: 21, outputMode: GrepOutputMode.Content))
            .Should().Contain("BeforeContext must be between 0 and 20.");
        (await toolharness.Grep("TODO", afterContext: -1, outputMode: GrepOutputMode.Content))
            .Should().Contain("AfterContext must be between 0 and 20.");
        (await toolharness.Grep("TODO", afterContext: 21, outputMode: GrepOutputMode.Content))
            .Should().Contain("AfterContext must be between 0 and 20.");
        (await toolharness.Grep("TODO", maxMatchesPerFile: 0))
            .Should().Contain("MaxMatchesPerFile must be between 1 and 1000.");
        (await toolharness.Grep("TODO", maxMatchesPerFile: 1001))
            .Should().Contain("MaxMatchesPerFile must be between 1 and 1000.");
        (await toolharness.Grep("TODO", maxDepth: 0))
            .Should().Contain("MaxDepth must be between 1 and 100.");
        (await toolharness.Grep("TODO", maxDepth: 101))
            .Should().Contain("MaxDepth must be between 1 and 100.");
        (await toolharness.Grep("TODO", outputMode: (GrepOutputMode)999))
            .Should().Contain("OutputMode must be a valid GrepOutputMode value.");
        (await toolharness.Grep("TODO", caseMode: (GrepCaseMode)999))
            .Should().Contain("CaseMode must be a valid GrepCaseMode value.");
        (await toolharness.Grep("TODO", path: "missing"))
            .Should().Contain("Path does not exist.");
    }

    [Fact]
    public async Task Grep_MapsAdvancedRipgrepOptions()
    {
        await File.WriteAllTextAsync("notes.txt", "TODO\n");
        var runner = new FakeRipgrepRunner
        {
            SearchEvents =
            [
                new RipgrepMatchEvent
                {
                    Path = "notes.txt",
                    Text = "TODO\n",
                    LineNumber = 1,
                    AbsoluteOffset = 0,
                    Submatches = []
                },
                Completion(RipgrepCompletionStatus.Success, 1)
            ]
        };

        var result = await CreateToolHarness(runner).Grep(
            "TODO",
            path: ".",
            outputMode: GrepOutputMode.Content,
            includeGlobs: [" **/*.cs "],
            excludeGlobs: [@"**\bin\**"],
            caseMode: GrepCaseMode.Insensitive,
            fixedStrings: true,
            wordRegexp: true,
            contextLines: 1,
            beforeContext: 2,
            afterContext: 3,
            maxMatchesPerFile: 4,
            maxDepth: 5,
            multiline: true,
            includeHidden: true,
            respectIgnoreFiles: false);

        runner.SearchOptions.Should().NotBeNull();
        runner.SearchOptions!.IncludeGlobs.Should().Equal("**/*.cs");
        runner.SearchOptions.ExcludeGlobs.Should().Contain("**/bin/**");
        runner.SearchOptions.ExcludeGlobs.Should().Contain("**/.git/**");
        runner.SearchOptions.CaseMode.Should().Be(RipgrepCaseMode.Insensitive);
        runner.SearchOptions.FixedStrings.Should().BeTrue();
        runner.SearchOptions.WordRegexp.Should().BeTrue();
        runner.SearchOptions.BeforeContext.Should().Be(2);
        runner.SearchOptions.AfterContext.Should().Be(3);
        runner.SearchOptions.MaxMatchesPerFile.Should().Be(4);
        runner.SearchOptions.MaxDepth.Should().Be(5);
        runner.SearchOptions.MaxColumns.Should().Be(500);
        runner.SearchOptions.Timeout.Should().Be(TimeSpan.FromSeconds(20));
        runner.SearchOptions.StrictJsonParsing.Should().BeFalse();
        runner.SearchOptions.Multiline.Should().BeTrue();
        runner.SearchOptions.MultilineDotAll.Should().BeTrue();
        runner.SearchOptions.IncludeHidden.Should().BeTrue();
        runner.SearchOptions.RespectIgnoreFiles.Should().BeFalse();
        result.Should().Contain("before_context=\"2\"");
        result.Should().Contain("after_context=\"3\"");
        result.Should().Contain("max_matches_per_file=\"4\"");
        result.Should().Contain("max_depth=\"5\"");
        result.Should().Contain("<include_glob pattern=\"**/*.cs\" />");
        result.Should().Contain("<exclude_glob pattern=\"**/bin/**\" />");
    }

    [Fact]
    public async Task Grep_MapsFilesAndCountModesWithoutContentContext()
    {
        await File.WriteAllTextAsync("notes.txt", "TODO\n");
        var filesRunner = new FakeRipgrepRunner();
        var countRunner = new FakeRipgrepRunner();

        await CreateToolHarness(filesRunner).Grep(
            "TODO",
            includeGlobs: ["*.txt"],
            excludeGlobs: ["obj/**"],
            caseMode: GrepCaseMode.Sensitive,
            fixedStrings: true,
            wordRegexp: true,
            maxMatchesPerFile: 3,
            maxDepth: 4,
            multiline: true,
            includeHidden: true,
            respectIgnoreFiles: false);

        await CreateToolHarness(countRunner).Grep(
            "TODO",
            outputMode: GrepOutputMode.Count,
            offset: 2,
            limit: 5,
            includeGlobs: ["*.txt"],
            excludeGlobs: ["obj/**"],
            caseMode: GrepCaseMode.Sensitive,
            fixedStrings: true,
            wordRegexp: true,
            maxMatchesPerFile: 3,
            maxDepth: 4,
            multiline: true,
            includeHidden: true,
            respectIgnoreFiles: false);

        filesRunner.FilesWithMatchesOptions.Should().NotBeNull();
        filesRunner.FilesWithMatchesOptions!.Pattern.Should().Be("TODO");
        filesRunner.FilesWithMatchesOptions.BeforeContext.Should().BeNull();
        filesRunner.FilesWithMatchesOptions.AfterContext.Should().BeNull();
        filesRunner.FilesWithMatchesOptions.IncludeGlobs.Should().Equal("*.txt");
        filesRunner.FilesWithMatchesOptions.ExcludeGlobs.Should().Contain("obj/**");
        filesRunner.FilesWithMatchesOptions.CaseMode.Should().Be(RipgrepCaseMode.Sensitive);
        filesRunner.FilesWithMatchesOptions.FixedStrings.Should().BeTrue();
        filesRunner.FilesWithMatchesOptions.WordRegexp.Should().BeTrue();
        filesRunner.FilesWithMatchesOptions.MaxMatchesPerFile.Should().Be(3);
        filesRunner.FilesWithMatchesOptions.MaxDepth.Should().Be(4);
        filesRunner.FilesWithMatchesOptions.Multiline.Should().BeTrue();
        filesRunner.FilesWithMatchesOptions.MultilineDotAll.Should().BeTrue();
        filesRunner.FilesWithMatchesOptions.IncludeHidden.Should().BeTrue();
        filesRunner.FilesWithMatchesOptions.RespectIgnoreFiles.Should().BeFalse();

        countRunner.CountOptions.Should().NotBeNull();
        countRunner.CountOptions!.BeforeContext.Should().BeNull();
        countRunner.CountOptions.AfterContext.Should().BeNull();
        countRunner.CountOptions.MaxMatches.Should().Be(7);
        countRunner.CountOptions.IncludeGlobs.Should().Equal("*.txt");
        countRunner.CountOptions.ExcludeGlobs.Should().Contain("obj/**");
        countRunner.CountOptions.CaseMode.Should().Be(RipgrepCaseMode.Sensitive);
        countRunner.CountOptions.FixedStrings.Should().BeTrue();
        countRunner.CountOptions.WordRegexp.Should().BeTrue();
        countRunner.CountOptions.MaxMatchesPerFile.Should().Be(3);
        countRunner.CountOptions.MaxDepth.Should().Be(4);
        countRunner.CountOptions.Multiline.Should().BeTrue();
        countRunner.CountOptions.MultilineDotAll.Should().BeTrue();
        countRunner.CountOptions.IncludeHidden.Should().BeTrue();
        countRunner.CountOptions.RespectIgnoreFiles.Should().BeFalse();
    }

    [Fact]
    public async Task Grep_ContentOutputPreservesBeforeAndAfterContext()
    {
        await File.WriteAllTextAsync("notes.txt", "before\nTODO here\nafter\n");
        var runner = new FakeRipgrepRunner
        {
            SearchEvents =
            [
                new RipgrepContextEvent
                {
                    Path = "notes.txt",
                    Text = "before\n",
                    LineNumber = 1,
                    AbsoluteOffset = 0
                },
                new RipgrepMatchEvent
                {
                    Path = "notes.txt",
                    Text = "TODO here\n",
                    LineNumber = 2,
                    AbsoluteOffset = 7,
                    Submatches = []
                },
                new RipgrepContextEvent
                {
                    Path = "notes.txt",
                    Text = "after\n",
                    LineNumber = 3,
                    AbsoluteOffset = 17
                },
                Completion(RipgrepCompletionStatus.Success, 1)
            ]
        };

        var result = await CreateToolHarness(runner).Grep(
            "TODO",
            path: "notes.txt",
            outputMode: GrepOutputMode.Content,
            contextLines: 1);

        result.Should().Contain("<context line=\"1\">1\tbefore</context>");
        result.Should().Contain("<line>2\tTODO here</line>");
        result.Should().Contain("<context line=\"3\">3\tafter</context>");
        result.IndexOf("line=\"1\"", StringComparison.Ordinal)
            .Should().BeLessThan(result.IndexOf("<line>2", StringComparison.Ordinal));
        result.IndexOf("<line>2", StringComparison.Ordinal)
            .Should().BeLessThan(result.IndexOf("line=\"3\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Grep_NoMatchesIncludesIgnoredFilesHint()
    {
        await File.WriteAllTextAsync("notes.txt", "hello\n");
        var result = await CreateToolHarness(new FakeRipgrepRunner()).Grep("TODO");

        result.Should().Contain("<no_matches />");
        result.Should().Contain("<search_hint>");
    }

    [Theory]
    [InlineData(GrepCaseMode.Sensitive, RipgrepCaseMode.Sensitive)]
    [InlineData(GrepCaseMode.Insensitive, RipgrepCaseMode.Insensitive)]
    [InlineData(GrepCaseMode.Smart, RipgrepCaseMode.Smart)]
    public async Task Grep_MapsCaseModes(GrepCaseMode input, RipgrepCaseMode expected)
    {
        await File.WriteAllTextAsync("notes.txt", "TODO\n");
        var runner = new FakeRipgrepRunner();

        await CreateToolHarness(runner).Grep("TODO", caseMode: input);

        runner.FilesWithMatchesOptions.Should().NotBeNull();
        runner.FilesWithMatchesOptions!.CaseMode.Should().Be(expected);
    }

    [Fact]
    public async Task Grep_ResolvesDirectoryAndFilePaths()
    {
        Directory.CreateDirectory("src");
        await File.WriteAllTextAsync(Path.Combine("src", "notes.txt"), "TODO\n");
        var directoryRunner = new FakeRipgrepRunner();
        var fileRunner = new FakeRipgrepRunner();

        await CreateToolHarness(directoryRunner).Grep("TODO", path: "src");
        await CreateToolHarness(fileRunner).Grep("TODO", path: Path.Combine("src", "notes.txt"));

        directoryRunner.FilesWithMatchesOptions.Should().NotBeNull();
        directoryRunner.FilesWithMatchesOptions!.WorkingDirectory.Should().Be(Path.GetFullPath("src"));
        directoryRunner.FilesWithMatchesOptions.SearchPaths.Should().Equal(".");
        fileRunner.FilesWithMatchesOptions.Should().NotBeNull();
        fileRunner.FilesWithMatchesOptions!.WorkingDirectory.Should().Be(Path.GetFullPath("src"));
        fileRunner.FilesWithMatchesOptions.SearchPaths.Should().Equal("notes.txt");
    }

    [Fact]
    public async Task Grep_SupportsAbsolutePaths()
    {
        await File.WriteAllTextAsync("notes.txt", "TODO\n");
        var fullPath = Path.GetFullPath("notes.txt");
        var runner = new FakeRipgrepRunner();

        var result = await CreateToolHarness(runner).Grep("TODO", path: fullPath);

        result.Should().Contain($"path=\"{fullPath}\"");
        runner.FilesWithMatchesOptions.Should().NotBeNull();
        runner.FilesWithMatchesOptions!.SearchPaths.Should().Equal("notes.txt");
    }

    [Fact]
    public async Task Grep_BlocksKnownSystemPaths()
    {
        if (OperatingSystem.IsWindows())
            return;

        var result = await CreateToolHarness(new FakeRipgrepRunner()).Grep("TODO", path: "/dev");

        result.Should().Contain("Cannot search blocked system path.");
    }

    [Fact]
    public async Task Grep_PaginatesFilesAndEmitsNextGrep()
    {
        await File.WriteAllTextAsync("a.txt", "TODO\n");
        await File.WriteAllTextAsync("b.txt", "TODO\n");
        await File.WriteAllTextAsync("c.txt", "TODO\n");
        var runner = new FakeRipgrepRunner
        {
            FilesWithMatchesResult = new RipgrepFilesWithMatchesResult
            {
                Files = ["a.txt", "b.txt", "c.txt"],
                Completion = Completion(RipgrepCompletionStatus.Success, 3)
            }
        };

        var result = await CreateToolHarness(runner).Grep("TODO", offset: 2, limit: 1);

        result.Should().Contain("results_read=\"1\"");
        result.Should().Contain("<next_grep offset=\"3\" limit=\"1\" reason=\"more_matches_available\" />");
        result.Should().Contain("truncation_reason=\"limit\"");
        result.Should().Contain("<truncation_hint>");
    }

    [Fact]
    public async Task Grep_PaginatesContentAndPassesBoundedMaxMatches()
    {
        await File.WriteAllTextAsync("notes.txt", "TODO 1\nTODO 2\nTODO 3\n");
        var runner = new FakeRipgrepRunner
        {
            SearchEvents =
            [
                Match("notes.txt", 1, "TODO 1\n"),
                Match("notes.txt", 2, "TODO 2\n"),
                Match("notes.txt", 3, "TODO 3\n"),
                Completion(RipgrepCompletionStatus.Success, 3)
            ]
        };

        var result = await CreateToolHarness(runner).Grep(
            "TODO",
            path: "notes.txt",
            outputMode: GrepOutputMode.Content,
            offset: 2,
            limit: 1);

        result.Should().NotContain("TODO 1");
        result.Should().Contain("TODO 2");
        result.Should().NotContain("TODO 3");
        result.Should().Contain("<next_grep offset=\"3\" limit=\"1\" reason=\"more_matches_available\" />");
        result.Should().Contain("truncation_reason=\"limit\"");
        runner.SearchOptions.Should().NotBeNull();
        runner.SearchOptions!.MaxMatches.Should().Be(3);
    }

    [Fact]
    public async Task Grep_PartialResultsReportUnknownTotals()
    {
        await File.WriteAllTextAsync("a.txt", "TODO\n");
        await File.WriteAllTextAsync("b.txt", "TODO\n");
        var filesRunner = new FakeRipgrepRunner
        {
            FilesWithMatchesResult = new RipgrepFilesWithMatchesResult
            {
                Files = ["a.txt", "b.txt"],
                Completion = Completion(RipgrepCompletionStatus.Truncated, 2, truncated: true, reason: "max_matches_reached")
            }
        };
        var countRunner = new FakeRipgrepRunner
        {
            CountResult = new RipgrepCountResult
            {
                Counts =
                [
                    new RipgrepCountEntry { Path = "a.txt", Count = 1 },
                    new RipgrepCountEntry { Path = "b.txt", Count = 1 }
                ],
                Completion = Completion(RipgrepCompletionStatus.Truncated, 2, truncated: true, reason: "max_matches_reached")
            }
        };
        var contentRunner = new FakeRipgrepRunner
        {
            SearchEvents =
            [
                Match("a.txt", 1, "TODO\n"),
                Match("b.txt", 1, "TODO\n"),
                Completion(RipgrepCompletionStatus.Truncated, 2, truncated: true, partial: true, reason: "max_matches_reached")
            ]
        };

        var files = await CreateToolHarness(filesRunner).Grep("TODO", limit: 1);
        var counts = await CreateToolHarness(countRunner).Grep("TODO", outputMode: GrepOutputMode.Count, limit: 1);
        var content = await CreateToolHarness(contentRunner).Grep("TODO", outputMode: GrepOutputMode.Content, limit: 1);

        files.Should().Contain("total_results=\"unknown\"");
        files.Should().Contain("total_matches=\"unknown\"");
        counts.Should().Contain("total_results=\"unknown\"");
        counts.Should().Contain("total_matches=\"unknown\"");
        content.Should().Contain("total_results=\"unknown\"");
        content.Should().Contain("total_matches=\"unknown\"");
    }

    [Fact]
    public async Task Grep_EscapesXmlSensitiveTextAndPaths()
    {
        await File.WriteAllTextAsync("notes.txt", "TODO\n");
        var runner = new FakeRipgrepRunner
        {
            SearchEvents =
            [
                Match("weird<&>.txt", 4, "TODO <tag> & value\n"),
                Completion(RipgrepCompletionStatus.Success, 1)
            ]
        };

        var result = await CreateToolHarness(runner).Grep("TODO", outputMode: GrepOutputMode.Content);

        result.Should().Contain("path=\"weird&lt;&amp;&gt;.txt\"");
        result.Should().Contain("TODO &lt;tag&gt; &amp; value");
    }

    [Fact]
    public async Task Grep_LongLinesAreShortenedAndMarkedTruncated()
    {
        await File.WriteAllTextAsync("notes.txt", "TODO\n");
        var longLine = "TODO " + new string('x', 2500) + "\n";
        var runner = new FakeRipgrepRunner
        {
            SearchEvents =
            [
                Match("notes.txt", 1, longLine),
                Completion(RipgrepCompletionStatus.Success, 1)
            ]
        };

        var result = await CreateToolHarness(runner).Grep("TODO", outputMode: GrepOutputMode.Content);

        result.Should().Contain("[line truncated]");
        result.Should().Contain("truncated=\"true\"");
        result.Should().Contain("truncation_reason=\"line_length\"");
        result.Should().NotContain("<next_grep");
    }

    [Fact]
    public async Task Grep_ReturnsPartialOutputForTimeoutAfterMatches()
    {
        await File.WriteAllTextAsync("notes.txt", "TODO\n");
        var runner = new FakeRipgrepRunner
        {
            SearchEvents =
            [
                Match("notes.txt", 1, "TODO\n"),
                Completion(RipgrepCompletionStatus.TimedOut, 1, timedOut: true, partial: true, truncated: true, reason: "timeout")
            ]
        };

        var result = await CreateToolHarness(runner).Grep("TODO", outputMode: GrepOutputMode.Content);

        result.Should().Contain("<match path=\"notes.txt\" line=\"1\">1\tTODO</match>");
        result.Should().Contain("status=\"timedout\"");
        result.Should().Contain("truncation_reason=\"timeout\"");
        result.Should().Contain("reason=\"partial_results_timeout\"");
    }

    [Fact]
    public async Task Grep_NoMatchesReportsZeroTotalMatches()
    {
        await File.WriteAllTextAsync("notes.txt", "hello\n");
        var result = await CreateToolHarness(new FakeRipgrepRunner()).Grep("TODO");

        result.Should().Contain("total_matches=\"0\"");
    }

    [Fact]
    public async Task Grep_FailedCompletionWithoutMatchesReturnsError()
    {
        await File.WriteAllTextAsync("notes.txt", "hello\n");
        var runner = new FakeRipgrepRunner
        {
            SearchEvents =
            [
                Completion(RipgrepCompletionStatus.Failed, 0, reason: "invalid_regex", stderr: "regex error")
            ]
        };

        var result = await CreateToolHarness(runner).Grep("(", outputMode: GrepOutputMode.Content);

        result.Should().StartWith("<error");
        result.Should().Contain("Ripgrep search failed.");
    }

    [Fact]
    public async Task Grep_FailedCompletionAfterMatchesReturnsPartialResult()
    {
        await File.WriteAllTextAsync("notes.txt", "TODO\n");
        var runner = new FakeRipgrepRunner
        {
            SearchEvents =
            [
                Match("notes.txt", 1, "TODO\n"),
                Completion(RipgrepCompletionStatus.Failed, 1, partial: true, truncated: true, reason: "failed")
            ]
        };

        var result = await CreateToolHarness(runner).Grep("TODO", outputMode: GrepOutputMode.Content);

        result.Should().Contain("<grep");
        result.Should().Contain("<match path=\"notes.txt\" line=\"1\">1\tTODO</match>");
        result.Should().Contain("truncation_reason=\"failed\"");
        result.Should().Contain("reason=\"partial_results_failed\"");
    }

    [Fact]
    public async Task Grep_NormalizesLeadingDotSlashResultPaths()
    {
        await File.WriteAllTextAsync("notes.txt", "TODO\n");
        var runner = new FakeRipgrepRunner
        {
            SearchEvents =
            [
                Match("./notes.txt", 1, "TODO\n"),
                Completion(RipgrepCompletionStatus.Success, 1)
            ]
        };

        var result = await CreateToolHarness(runner).Grep("TODO", outputMode: GrepOutputMode.Content);

        result.Should().Contain("<match path=\"notes.txt\" line=\"1\">1\tTODO</match>");
        result.Should().NotContain("path=\"./notes.txt\"");
    }

    [Fact]
    public async Task Grep_ReturnsClearErrorsForUnavailableAndFailedRipgrep()
    {
        await File.WriteAllTextAsync("notes.txt", "TODO\n");
        var unavailable = new FakeRipgrepRunner { ExceptionToThrow = new InvalidOperationException("Ripgrep is unavailable.") };
        var failed = new FakeRipgrepRunner { ExceptionToThrow = new InvalidOperationException("Ripgrep search failed.") };

        (await CreateToolHarness(unavailable).Grep("TODO")).Should().Contain("Ripgrep is unavailable.");
        (await CreateToolHarness(failed).Grep("TODO")).Should().Contain("Ripgrep search failed.");
    }

    [Fact]
    public async Task Grep_EmitsDiagnostics()
    {
        await File.WriteAllTextAsync("notes.txt", "TODO\n");
        var runner = new FakeRipgrepRunner
        {
            FilesWithMatchesResult = new RipgrepFilesWithMatchesResult
            {
                Files = ["notes.txt"],
                Completion = Completion(RipgrepCompletionStatus.Success, 1, stderr: "warning <xml>")
            }
        };

        var result = await CreateToolHarness(runner).Grep("TODO");

        result.Should().Contain("<diagnostic>warning &lt;xml&gt;</diagnostic>");
    }

    [Fact]
    public void Grep_DoesNotExposeRawRipgrepArgumentsOrCaches()
    {
        var parameters = typeof(CodingToolHarness)
            .GetMethod(nameof(CodingToolHarness.Grep))!
            .GetParameters()
            .Select(parameter => parameter.Name)
            .ToArray();

        parameters.Should().NotContain(name => name!.Contains("raw", StringComparison.OrdinalIgnoreCase));
        parameters.Should().NotContain(name => name!.Contains("argument", StringComparison.OrdinalIgnoreCase));

        typeof(CodingToolHarness)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
            .Where(field => field.DeclaringType == typeof(CodingToolHarness))
            .Select(field => field.Name)
            .Should()
            .NotContain(name => name.Contains("grep", StringComparison.OrdinalIgnoreCase) &&
                                name.Contains("cache", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Grep_IntegrationFindsRealMatchWhenRipgrepIsAvailable()
    {
        if (!await IsRipgrepAvailableAsync())
            return;

        await File.WriteAllTextAsync("notes.txt", "hello\nTODO here\n");

        var result = await new CodingToolHarness().Grep("TODO", outputMode: GrepOutputMode.Content);

        result.Should().Contain("line=\"2\"");
        result.Should().Contain("2\tTODO here");
    }

    [Fact]
    public async Task Grep_IntegrationNormalizesRealDotSlashPathsWhenRipgrepIsAvailable()
    {
        if (!await IsRipgrepAvailableAsync())
            return;

        await File.WriteAllTextAsync("notes.txt", "TODO here\n");

        var result = await new CodingToolHarness().Grep("TODO", outputMode: GrepOutputMode.Content);

        result.Should().Contain("path=\"notes.txt\"");
        result.Should().NotContain("path=\"./notes.txt\"");
    }

    [Fact]
    public async Task Grep_IntegrationInvalidRegexReturnsErrorWhenRipgrepIsAvailable()
    {
        if (!await IsRipgrepAvailableAsync())
            return;

        await File.WriteAllTextAsync("notes.txt", "TODO here\n");

        var result = await new CodingToolHarness().Grep("[", outputMode: GrepOutputMode.Content);

        result.Should().StartWith("<error");
        result.Should().Contain("Ripgrep search failed.");
    }

    [Fact]
    public async Task Grep_IntegrationRespectsAndBypassesGitIgnoreWhenRipgrepIsAvailable()
    {
        if (!await IsRipgrepAvailableAsync())
            return;

        Directory.CreateDirectory(".git");
        await File.WriteAllTextAsync(".gitignore", "ignored.txt\n");
        await File.WriteAllTextAsync("ignored.txt", "TODO ignored\n");

        var respected = await new CodingToolHarness().Grep("TODO");
        var bypassed = await new CodingToolHarness().Grep("TODO", respectIgnoreFiles: false);

        respected.Should().NotContain("ignored.txt");
        bypassed.Should().Contain("ignored.txt");
    }

    [Fact]
    public async Task Grep_IntegrationExcludesVcsDirectoriesEvenWhenHiddenFilesAreIncluded()
    {
        if (!await IsRipgrepAvailableAsync())
            return;

        Directory.CreateDirectory(".git");
        await File.WriteAllTextAsync(Path.Combine(".git", "config"), "TODO in git metadata\n");
        await File.WriteAllTextAsync(".hidden.txt", "TODO in hidden file\n");

        var result = await new CodingToolHarness().Grep(
            "TODO",
            includeHidden: true,
            respectIgnoreFiles: false);

        result.Should().Contain(".hidden.txt");
        result.Should().NotContain(".git/config");
    }

    [Fact]
    public async Task Grep_IntegrationSupportsMultilineWhenRipgrepIsAvailable()
    {
        if (!await IsRipgrepAvailableAsync())
            return;

        await File.WriteAllTextAsync("notes.txt", "alpha\nbeta\n");

        var result = await new CodingToolHarness().Grep(
            "alpha\\nbeta",
            outputMode: GrepOutputMode.Content,
            multiline: true);

        result.Should().Contain("notes.txt");
        result.Should().Contain("alpha");
    }

    private static CodingToolHarness CreateToolHarness(IRipgrepRunner runner)
        => new(null, null, null, null, runner);

    private static RipgrepMatchEvent Match(string path, int lineNumber, string text)
        => new()
        {
            Path = path,
            Text = text,
            LineNumber = lineNumber,
            AbsoluteOffset = 0,
            Submatches = []
        };

    private static RipgrepCompletionEvent Completion(
        RipgrepCompletionStatus status,
        int matches,
        bool timedOut = false,
        bool partial = false,
        bool truncated = false,
        string? stderr = null,
        string? reason = null)
        => new()
        {
            Status = status,
            ExitCode = status == RipgrepCompletionStatus.NoMatches ? 1 : 0,
            Partial = partial,
            TimedOut = timedOut,
            Cancelled = false,
            Truncated = truncated,
            MatchesEmitted = matches,
            Stderr = stderr,
            Reason = reason
        };

    private static async Task<bool> IsRipgrepAvailableAsync()
    {
        var resolution = await new DefaultRipgrepBinaryProvider().ResolveAsync();
        return resolution.IsAvailable;
    }

    private sealed class FakeRipgrepRunner : IRipgrepRunner
    {
        public RipgrepSearchOptions? SearchOptions { get; private set; }
        public RipgrepSearchOptions? FilesWithMatchesOptions { get; private set; }
        public RipgrepSearchOptions? CountOptions { get; private set; }
        public Exception? ExceptionToThrow { get; init; }
        public IReadOnlyList<RipgrepEvent> SearchEvents { get; init; } = [Completion(RipgrepCompletionStatus.NoMatches, 0)];
        public RipgrepFilesWithMatchesResult FilesWithMatchesResult { get; init; } = new()
        {
            Files = [],
            Completion = Completion(RipgrepCompletionStatus.NoMatches, 0)
        };
        public RipgrepCountResult CountResult { get; init; } = new()
        {
            Counts = [],
            Completion = Completion(RipgrepCompletionStatus.NoMatches, 0)
        };

        public async IAsyncEnumerable<RipgrepEvent> SearchAsync(
            RipgrepSearchOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow != null)
                throw ExceptionToThrow;

            SearchOptions = options;
            foreach (var item in SearchEvents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return item;
            }
        }

        public Task<RipgrepFilesWithMatchesResult> ListFilesWithMatchesAsync(
            RipgrepSearchOptions options,
            CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow != null)
                throw ExceptionToThrow;

            FilesWithMatchesOptions = options;
            return Task.FromResult(FilesWithMatchesResult);
        }

        public Task<RipgrepCountResult> CountAsync(
            RipgrepSearchOptions options,
            CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow != null)
                throw ExceptionToThrow;

            CountOptions = options;
            return Task.FromResult(CountResult);
        }
    }
}
