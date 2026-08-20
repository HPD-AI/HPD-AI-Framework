using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HPD.Agent.ToolHarness.Coding.Ripgrep;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class RipgrepWrapperTests
{
    [Fact]
    public void RipgrepRunner_DoesNotExposeRawRunner()
    {
        typeof(IRipgrepRunner)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(method => method.Name)
            .Should()
            .BeEquivalentTo([
                nameof(IRipgrepRunner.SearchAsync),
                nameof(IRipgrepRunner.ListFilesWithMatchesAsync),
                nameof(IRipgrepRunner.CountAsync)
            ]);
    }

    [Fact]
    public void BuildArguments_AddsDeterministicSafetyArguments()
    {
        var args = RipgrepRunner.BuildArguments(new RipgrepSearchOptions
        {
            Pattern = "-TODO",
            WorkingDirectory = Directory.GetCurrentDirectory(),
            SearchPaths = ["src"]
        });

        args.Should().StartWith(["--no-config", "--json", "--no-messages", "--smart-case"]);
        args.Should().ContainInOrder("--regexp", "-TODO", "--", "src");
    }

    [Fact]
    public void BuildArguments_MapsTypedOptions()
    {
        var args = RipgrepRunner.BuildArguments(new RipgrepSearchOptions
        {
            Pattern = "TODO",
            WorkingDirectory = Directory.GetCurrentDirectory(),
            SearchPaths = ["."],
            IncludeGlobs = ["**/*.cs"],
            ExcludeGlobs = ["bin/**"],
            CaseMode = RipgrepCaseMode.Insensitive,
            FixedStrings = true,
            WordRegexp = true,
            Multiline = true,
            MultilineDotAll = true,
            IncludeHidden = true,
            RespectIgnoreFiles = false,
            FollowSymlinks = true,
            BeforeContext = 2,
            AfterContext = 3,
            MaxMatchesPerFile = 4,
            MaxDepth = 5,
            MaxColumns = 120,
            MaxFileSizeBytes = 4096,
            Threads = 2
        });

        args.Should().Contain("--ignore-case");
        args.Should().Contain("--fixed-strings");
        args.Should().Contain("--word-regexp");
        args.Should().Contain("--multiline");
        args.Should().Contain("--multiline-dotall");
        args.Should().Contain("--hidden");
        args.Should().Contain("--no-ignore");
        args.Should().Contain("--follow");
        args.Should().ContainInOrder("--glob", "**/*.cs");
        args.Should().ContainInOrder("--glob", "!bin/**");
        args.Should().ContainInOrder("--before-context", "2");
        args.Should().ContainInOrder("--after-context", "3");
        args.Should().ContainInOrder("--max-count", "4");
        args.Should().ContainInOrder("--max-depth", "5");
        args.Should().ContainInOrder("--max-columns", "120");
        args.Should().ContainInOrder("--max-filesize", "4096");
        args.Should().ContainInOrder("--threads", "2");
    }

    [Fact]
    public void BuildFilesWithMatchesArguments_UsesNativeOutputMode()
    {
        var args = RipgrepRunner.BuildFilesWithMatchesArguments(new RipgrepSearchOptions
        {
            Pattern = "TODO",
            WorkingDirectory = Directory.GetCurrentDirectory(),
            SearchPaths = ["."]
        });

        args.Should().Contain("--files-with-matches");
        args.Should().NotContain("--json");
        args.Should().ContainInOrder("--regexp", "TODO", "--", ".");
    }

    [Fact]
    public void BuildCountArguments_UsesNativeOutputModeAndStableFilenameOutput()
    {
        var args = RipgrepRunner.BuildCountArguments(new RipgrepSearchOptions
        {
            Pattern = "TODO",
            WorkingDirectory = Directory.GetCurrentDirectory(),
            SearchPaths = ["notes.txt"]
        });

        args.Should().Contain("--count");
        args.Should().Contain("--with-filename");
        args.Should().NotContain("--json");
        args.Should().ContainInOrder("--regexp", "TODO", "--", "notes.txt");
    }

    [Theory]
    [InlineData(RipgrepCaseMode.Sensitive, null)]
    [InlineData(RipgrepCaseMode.Insensitive, "--ignore-case")]
    [InlineData(RipgrepCaseMode.Smart, "--smart-case")]
    public void BuildArguments_MapsCaseMode(RipgrepCaseMode caseMode, string? expectedFlag)
    {
        var args = RipgrepRunner.BuildArguments(new RipgrepSearchOptions
        {
            Pattern = "TODO",
            WorkingDirectory = Directory.GetCurrentDirectory(),
            SearchPaths = ["."],
            CaseMode = caseMode
        });

        args.Should().Contain("--regexp");
        if (expectedFlag == null)
        {
            args.Should().NotContain("--ignore-case");
            args.Should().NotContain("--smart-case");
        }
        else
        {
            args.Should().Contain(expectedFlag);
        }
    }

    [Fact]
    public void BuildArguments_DoesNotExposeRawArguments()
    {
        typeof(RipgrepSearchOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Should()
            .NotContain(name => name.Contains("Raw", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("Additional", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parser_ParsesMatchEvents()
    {
        var parser = new RipgrepJsonParser();
        var json = """
            {"type":"match","data":{"path":{"text":"src/App.cs"},"lines":{"text":"TODO here\n"},"line_number":7,"absolute_offset":42,"submatches":[{"match":{"text":"TODO"},"start":0,"end":4}]}}
            """;

        var parsed = parser.TryParse(Encoding.UTF8.GetBytes(json), out var parsedEvent, out var error);

        parsed.Should().BeTrue();
        error.Should().BeNull();
        var match = parsedEvent.Should().BeOfType<RipgrepMatchEvent>().Subject;
        match.Path.Should().Be("src/App.cs");
        match.Text.Should().Be("TODO here\n");
        match.LineNumber.Should().Be(7);
        match.AbsoluteOffset.Should().Be(42);
        match.Submatches.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new RipgrepSubmatch { Text = "TODO", Start = 0, End = 4 });
    }

    [Fact]
    public void Parser_ParsesBeginContextEndAndSummaryEvents()
    {
        var parser = new RipgrepJsonParser();

        Parse(parser, """{"type":"begin","data":{"path":{"text":"a.txt"}}}""")
            .Should().BeOfType<RipgrepBeginEvent>()
            .Which.Path.Should().Be("a.txt");

        Parse(parser, """{"type":"context","data":{"path":{"text":"a.txt"},"lines":{"text":"before\n"},"line_number":1,"absolute_offset":0}}""")
            .Should().BeOfType<RipgrepContextEvent>()
            .Which.Text.Should().Be("before\n");

        Parse(parser, """{"type":"end","data":{"path":{"text":"a.txt"},"binary_offset":12,"stats":{"matches":1,"matched_lines":1}}}""")
            .Should().BeOfType<RipgrepEndEvent>()
            .Which.Stats!.Matches.Should().Be(1);

        Parse(parser, """{"type":"summary","data":{"stats":{"searches":2,"searches_with_match":1,"bytes_searched":10,"bytes_printed":5,"matched_lines":1,"matches":1,"elapsed":{"secs":1,"nanos":500000000}}}}""")
            .Should().BeOfType<RipgrepSummaryEvent>()
            .Which.Stats!.Elapsed.Should().Be(TimeSpan.FromSeconds(1.5));
    }

    [Fact]
    public void Parser_IgnoresUnknownAndBlankLines()
    {
        var parser = new RipgrepJsonParser();

        parser.TryParse([], out var blankEvent, out var blankError).Should().BeTrue();
        blankEvent.Should().BeNull();
        blankError.Should().BeNull();

        parser.TryParse(Encoding.UTF8.GetBytes("""{"type":"future","data":{}}"""), out var unknownEvent, out var unknownError)
            .Should().BeTrue();
        unknownEvent.Should().BeNull();
        unknownError.Should().BeNull();
    }

    [Fact]
    public void Parser_ReturnsErrorForInvalidJson()
    {
        var parser = new RipgrepJsonParser();

        parser.TryParse(Encoding.UTF8.GetBytes("{nope"), out var parsedEvent, out var error)
            .Should().BeFalse();

        parsedEvent.Should().BeNull();
        error.Should().NotBeNull();
    }

    [Fact]
    public void Parser_ParsesBase64TextPayloads()
    {
        var parser = new RipgrepJsonParser();
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("TODO from bytes\n"));
        var json = "{\"type\":\"match\",\"data\":{\"path\":{\"text\":\"src/App.cs\"},\"lines\":{\"bytes\":\"" +
            encoded +
            "\"},\"line_number\":7,\"absolute_offset\":42,\"submatches\":[]}}";

        var parsed = parser.TryParse(Encoding.UTF8.GetBytes(json), out var parsedEvent, out var error);

        parsed.Should().BeTrue();
        error.Should().BeNull();
        parsedEvent.Should().BeOfType<RipgrepMatchEvent>()
            .Which.Text.Should().Be("TODO from bytes\n");
    }

    [Fact]
    public async Task Runner_EmitsCompletionForExitCodeZero()
    {
        var runner = CreateRunner(
            new RipgrepStdoutLine("""{"type":"match","data":{"path":{"text":"a.txt"},"lines":{"text":"TODO\n"},"line_number":1,"absolute_offset":0,"submatches":[]}}"""),
            new RipgrepProcessCompleted(0, null, false, false, null));

        var events = await CollectAsync(runner.SearchAsync(DefaultOptions()));

        events.Should().HaveCount(2);
        events[0].Should().BeOfType<RipgrepMatchEvent>();
        var completion = events[1].Should().BeOfType<RipgrepCompletionEvent>().Subject;
        completion.Status.Should().Be(RipgrepCompletionStatus.Success);
        completion.ExitCode.Should().Be(0);
        completion.MatchesEmitted.Should().Be(1);
    }

    [Fact]
    public async Task Runner_PassesBuiltArgumentsToProcessExecutor()
    {
        var executor = new FakeProcessExecutor([new RipgrepProcessCompleted(1, null, false, false, null)]);
        var runner = CreateRunner(executor);

        await CollectAsync(runner.SearchAsync(DefaultOptions() with
        {
            Pattern = "-TODO",
            SearchPaths = ["src"]
        }));

        executor.LastRequest.Should().NotBeNull();
        executor.LastRequest!.Arguments.Should().ContainInOrder("--regexp", "-TODO", "--", "src");
        executor.LastRequest.WorkingDirectory.Should().Be(AppContext.BaseDirectory);
    }

    [Fact]
    public async Task Runner_ListFilesWithMatchesParsesPlainOutput()
    {
        var executor = new FakeProcessExecutor([
            new RipgrepStdoutLine("src/A.cs"),
            new RipgrepStdoutLine("src/B.cs"),
            new RipgrepProcessCompleted(0, null, false, false, null)
        ]);
        var runner = CreateRunner(executor);

        var result = await runner.ListFilesWithMatchesAsync(DefaultOptions());

        result.Files.Should().Equal("src/A.cs", "src/B.cs");
        result.Completion.Status.Should().Be(RipgrepCompletionStatus.Success);
        result.Completion.MatchesEmitted.Should().Be(2);
        executor.LastRequest!.Arguments.Should().Contain("--files-with-matches");
        executor.LastRequest.Arguments.Should().NotContain("--json");
    }

    [Fact]
    public async Task Runner_CountParsesPlainOutput()
    {
        var executor = new FakeProcessExecutor([
            new RipgrepStdoutLine("src/A.cs:2"),
            new RipgrepStdoutLine("src/path:with-colon/B.cs:3"),
            new RipgrepProcessCompleted(0, null, false, false, null)
        ]);
        var runner = CreateRunner(executor);

        var result = await runner.CountAsync(DefaultOptions());

        result.Counts.Should().Equal(
            new RipgrepCountEntry { Path = "src/A.cs", Count = 2 },
            new RipgrepCountEntry { Path = "src/path:with-colon/B.cs", Count = 3 });
        result.Completion.Status.Should().Be(RipgrepCompletionStatus.Success);
        result.Completion.MatchesEmitted.Should().Be(2);
        executor.LastRequest!.Arguments.Should().Contain("--count");
        executor.LastRequest.Arguments.Should().Contain("--with-filename");
        executor.LastRequest.Arguments.Should().NotContain("--json");
    }

    [Fact]
    public async Task Runner_LineOutputModesRespectMaxMatches()
    {
        var runner = CreateRunner(
            new RipgrepStdoutLine("src/A.cs"),
            new RipgrepStdoutLine("src/B.cs"),
            new RipgrepProcessCompleted(0, null, false, false, null));

        var result = await runner.ListFilesWithMatchesAsync(DefaultOptions() with { MaxMatches = 1 });

        result.Files.Should().ContainSingle().Which.Should().Be("src/A.cs");
        result.Completion.Status.Should().Be(RipgrepCompletionStatus.Truncated);
        result.Completion.Truncated.Should().BeTrue();
    }

    [Fact]
    public async Task Runner_EmitsNoMatchesForExitCodeOne()
    {
        var runner = CreateRunner(new RipgrepProcessCompleted(1, null, false, false, null));

        var events = await CollectAsync(runner.SearchAsync(DefaultOptions()));

        var completion = events.Should().ContainSingle().Which.Should().BeOfType<RipgrepCompletionEvent>().Subject;
        completion.Status.Should().Be(RipgrepCompletionStatus.NoMatches);
        completion.ExitCode.Should().Be(1);
    }

    [Fact]
    public async Task Runner_EmitsFailedForExitCodeTwoAndCapturesStderr()
    {
        var runner = CreateRunner(new RipgrepProcessCompleted(2, "bad regex", false, false, null));

        var events = await CollectAsync(runner.SearchAsync(DefaultOptions()));

        var completion = events.Should().ContainSingle().Which.Should().BeOfType<RipgrepCompletionEvent>().Subject;
        completion.Status.Should().Be(RipgrepCompletionStatus.Failed);
        completion.Stderr.Should().Be("bad regex");
    }

    [Fact]
    public async Task Runner_EmitsTimedOutCompletion()
    {
        var runner = CreateRunner(new RipgrepProcessCompleted(null, "partial stderr", true, false, "timeout"));

        var events = await CollectAsync(runner.SearchAsync(DefaultOptions()));

        var completion = events.Should().ContainSingle().Which.Should().BeOfType<RipgrepCompletionEvent>().Subject;
        completion.Status.Should().Be(RipgrepCompletionStatus.TimedOut);
        completion.TimedOut.Should().BeTrue();
        completion.Partial.Should().BeTrue();
        completion.Reason.Should().Be("timeout");
    }

    [Fact]
    public async Task Runner_EmitsCancelledCompletion()
    {
        var runner = CreateRunner(new RipgrepProcessCompleted(null, null, false, true, "cancelled"));

        var events = await CollectAsync(runner.SearchAsync(DefaultOptions()));

        var completion = events.Should().ContainSingle().Which.Should().BeOfType<RipgrepCompletionEvent>().Subject;
        completion.Status.Should().Be(RipgrepCompletionStatus.Cancelled);
        completion.Cancelled.Should().BeTrue();
        completion.Partial.Should().BeTrue();
        completion.Reason.Should().Be("cancelled");
    }

    [Fact]
    public async Task Runner_StopsAtMaxMatchesAndEmitsTruncatedCompletion()
    {
        var runner = CreateRunner(
            new RipgrepStdoutLine("""{"type":"match","data":{"path":{"text":"a.txt"},"lines":{"text":"one\n"},"line_number":1,"absolute_offset":0,"submatches":[]}}"""),
            new RipgrepStdoutLine("""{"type":"match","data":{"path":{"text":"a.txt"},"lines":{"text":"two\n"},"line_number":2,"absolute_offset":4,"submatches":[]}}"""),
            new RipgrepProcessCompleted(0, null, false, false, null));

        var events = await CollectAsync(runner.SearchAsync(DefaultOptions() with { MaxMatches = 1 }));

        events.Should().HaveCount(2);
        var completion = events[1].Should().BeOfType<RipgrepCompletionEvent>().Subject;
        completion.Status.Should().Be(RipgrepCompletionStatus.Truncated);
        completion.Truncated.Should().BeTrue();
        completion.Partial.Should().BeTrue();
        completion.MatchesEmitted.Should().Be(1);
    }

    [Fact]
    public async Task Runner_NonStrictParserSkipsInvalidJson()
    {
        var runner = CreateRunner(
            new RipgrepStdoutLine("{nope"),
            new RipgrepProcessCompleted(0, null, false, false, null));

        var events = await CollectAsync(runner.SearchAsync(DefaultOptions()));

        events.Should().ContainSingle().Which.Should().BeOfType<RipgrepCompletionEvent>();
    }

    [Fact]
    public async Task Runner_StrictParserFailsInvalidJson()
    {
        var runner = CreateRunner(
            new RipgrepStdoutLine("{nope"),
            new RipgrepProcessCompleted(0, null, false, false, null));

        var events = await CollectAsync(runner.SearchAsync(DefaultOptions() with { StrictJsonParsing = true }));

        var completion = events.Should().ContainSingle().Which.Should().BeOfType<RipgrepCompletionEvent>().Subject;
        completion.Status.Should().Be(RipgrepCompletionStatus.Failed);
        completion.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Runner_ValidatesOptions()
    {
        var runner = CreateRunner(new RipgrepProcessCompleted(1, null, false, false, null));

        var act = async () => await CollectAsync(runner.SearchAsync(DefaultOptions() with { Pattern = "" }));

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Pattern is required*");
    }

    [Fact]
    public async Task Provider_RejectsRelativeConfiguredPath()
    {
        var provider = new DefaultRipgrepBinaryProvider(new RipgrepBinaryProviderOptions
        {
            ConfiguredPath = "rg"
        });

        var result = await provider.ResolveAsync();

        result.IsAvailable.Should().BeFalse();
        result.ReasonUnavailable.Should().Contain("absolute");
    }

    [Fact]
    public async Task Provider_ResolvesConfiguredAbsolutePathWithoutVersionProbe()
    {
        using var temp = TempDirectory.Create();
        var binaryPath = Path.Combine(temp.Path, "rg");
        await File.WriteAllTextAsync(binaryPath, "not a real binary\n");

        var provider = new DefaultRipgrepBinaryProvider(new RipgrepBinaryProviderOptions
        {
            ConfiguredPath = binaryPath,
            CaptureVersion = false
        });

        var result = await provider.ResolveAsync();

        result.IsAvailable.Should().BeTrue();
        result.Source.Should().Be(RipgrepBinarySource.ConfiguredPath);
        result.Path.Should().Be(binaryPath);
    }

    [Fact]
    public async Task Provider_ReturnsUnavailableWhenNoBinaryExists()
    {
        var provider = new DefaultRipgrepBinaryProvider(
            new RipgrepBinaryProviderOptions(),
            pathEnvironment: null,
            baseDirectory: Directory.GetCurrentDirectory());

        var result = await provider.ResolveAsync();

        result.IsAvailable.Should().BeFalse();
        result.Source.Should().Be(RipgrepBinarySource.None);
        result.ReasonUnavailable.Should().Contain("No ripgrep binary");
    }

    [Fact]
    public async Task Provider_UsesBundledManifestAndHashWhenNoConfiguredPath()
    {
        using var temp = TempDirectory.Create();
        var binaryPath = Path.Combine(temp.Path, "rg");
        await File.WriteAllTextAsync(binaryPath, "not a real binary\n");
        var hash = Sha256(binaryPath);

        var provider = new DefaultRipgrepBinaryProvider(
            new RipgrepBinaryProviderOptions
            {
                CaptureVersion = false,
                BundledBinaries =
                [
                    new RipgrepBundledBinaryManifest
                    {
                        RuntimeIdentifier = CurrentRuntimeIdentifier(),
                        RelativePath = "rg",
                        Version = "14.1.1",
                        Sha256 = hash
                    }
                ]
            },
            pathEnvironment: null,
            baseDirectory: temp.Path);

        var result = await provider.ResolveAsync();

        result.IsAvailable.Should().BeTrue();
        result.Source.Should().Be(RipgrepBinarySource.BundledPath);
        result.Path.Should().Be(binaryPath);
        result.DetectedVersion.Should().Be("14.1.1");
        result.Sha256.Should().Be(hash);
    }

    [Fact]
    public async Task Provider_RejectsBundledHashMismatch()
    {
        using var temp = TempDirectory.Create();
        var binaryPath = Path.Combine(temp.Path, "rg");
        await File.WriteAllTextAsync(binaryPath, "not a real binary\n");

        var provider = new DefaultRipgrepBinaryProvider(
            new RipgrepBinaryProviderOptions
            {
                CaptureVersion = false,
                BundledBinaries =
                [
                    new RipgrepBundledBinaryManifest
                    {
                        RuntimeIdentifier = CurrentRuntimeIdentifier(),
                        RelativePath = "rg",
                        Version = "14.1.1",
                        Sha256 = new string('0', 64)
                    }
                ]
            },
            pathEnvironment: null,
            baseDirectory: temp.Path);

        var result = await provider.ResolveAsync();

        result.IsAvailable.Should().BeFalse();
        result.ReasonUnavailable.Should().Contain("SHA-256 mismatch");
    }

    [Fact]
    public async Task Provider_IgnoresBundledManifestForDifferentRuntimeIdentifier()
    {
        using var temp = TempDirectory.Create();
        var binaryPath = Path.Combine(temp.Path, "rg");
        await File.WriteAllTextAsync(binaryPath, "not a real binary\n");
        var hash = Sha256(binaryPath);

        var provider = new DefaultRipgrepBinaryProvider(
            new RipgrepBinaryProviderOptions
            {
                CaptureVersion = false,
                BundledBinaries =
                [
                    new RipgrepBundledBinaryManifest
                    {
                        RuntimeIdentifier = "definitely-not-this-runtime",
                        RelativePath = "rg",
                        Version = "14.1.1",
                        Sha256 = hash
                    }
                ]
            },
            pathEnvironment: null,
            baseDirectory: temp.Path);

        var result = await provider.ResolveAsync();

        result.IsAvailable.Should().BeFalse();
        result.Source.Should().Be(RipgrepBinarySource.None);
        result.ReasonUnavailable.Should().Contain("No ripgrep binary");
    }

    [Fact]
    public async Task Provider_UsesBundledManifestVersionForExactPolicy()
    {
        using var temp = TempDirectory.Create();
        var binaryPath = Path.Combine(temp.Path, "rg");
        await File.WriteAllTextAsync(binaryPath, "not a real binary\n");
        var hash = Sha256(binaryPath);

        var provider = new DefaultRipgrepBinaryProvider(
            new RipgrepBinaryProviderOptions
            {
                CaptureVersion = false,
                VersionPolicy = RipgrepVersionPolicy.Exact,
                RequiredVersion = "14.1.1",
                BundledBinaries =
                [
                    new RipgrepBundledBinaryManifest
                    {
                        RuntimeIdentifier = CurrentRuntimeIdentifier(),
                        RelativePath = "rg",
                        Version = "14.1.1",
                        Sha256 = hash
                    }
                ]
            },
            pathEnvironment: null,
            baseDirectory: temp.Path);

        var result = await provider.ResolveAsync();

        result.IsAvailable.Should().BeTrue();
        result.Source.Should().Be(RipgrepBinarySource.BundledPath);
        result.DetectedVersion.Should().Be("14.1.1");
        result.ExpectedVersion.Should().Be("14.1.1");
        result.VersionSatisfied.Should().BeTrue();
    }

    [Fact]
    public async Task Provider_RejectsBundledManifestVersionWhenExactPolicyDoesNotMatch()
    {
        using var temp = TempDirectory.Create();
        var binaryPath = Path.Combine(temp.Path, "rg");
        await File.WriteAllTextAsync(binaryPath, "not a real binary\n");
        var hash = Sha256(binaryPath);

        var provider = new DefaultRipgrepBinaryProvider(
            new RipgrepBinaryProviderOptions
            {
                CaptureVersion = false,
                VersionPolicy = RipgrepVersionPolicy.Exact,
                RequiredVersion = "15.0.0",
                BundledBinaries =
                [
                    new RipgrepBundledBinaryManifest
                    {
                        RuntimeIdentifier = CurrentRuntimeIdentifier(),
                        RelativePath = "rg",
                        Version = "14.1.1",
                        Sha256 = hash
                    }
                ]
            },
            pathEnvironment: null,
            baseDirectory: temp.Path);

        var result = await provider.ResolveAsync();

        result.IsAvailable.Should().BeFalse();
        result.Source.Should().Be(RipgrepBinarySource.BundledPath);
        result.DetectedVersion.Should().Be("14.1.1");
        result.ExpectedVersion.Should().Be("15.0.0");
        result.VersionSatisfied.Should().BeFalse();
        result.ReasonUnavailable.Should().Contain("required");
    }

    [Fact]
    public async Task Provider_DoesNotUseVersionEnvironmentOverrides()
    {
        using var temp = TempDirectory.Create();
        var binaryPath = Path.Combine(temp.Path, "rg");
        await File.WriteAllTextAsync(binaryPath, "not a real binary\n");
        var hash = Sha256(binaryPath);
        System.Environment.SetEnvironmentVariable("IVY_RIPGREP_VERSION", "99.99.99");

        try
        {
            var provider = new DefaultRipgrepBinaryProvider(
                new RipgrepBinaryProviderOptions
                {
                    CaptureVersion = false,
                    BundledBinaries =
                    [
                        new RipgrepBundledBinaryManifest
                        {
                            RuntimeIdentifier = CurrentRuntimeIdentifier(),
                            RelativePath = "rg",
                            Version = "14.1.1",
                            Sha256 = hash
                        }
                    ]
                },
                pathEnvironment: null,
                baseDirectory: temp.Path);

            var result = await provider.ResolveAsync();

            result.IsAvailable.Should().BeTrue();
            result.DetectedVersion.Should().Be("14.1.1");
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("IVY_RIPGREP_VERSION", null);
        }
    }

    [Fact]
    public async Task Provider_ResolvesSystemRgFromInjectedPath()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var temp = TempDirectory.Create();
        var binaryPath = await CreateFakeRipgrepAsync(temp.Path, "14.1.1");
        var provider = new DefaultRipgrepBinaryProvider(
            new RipgrepBinaryProviderOptions(),
            pathEnvironment: temp.Path,
            baseDirectory: Directory.GetCurrentDirectory());

        var result = await provider.ResolveAsync();

        result.IsAvailable.Should().BeTrue();
        result.Source.Should().Be(RipgrepBinarySource.SystemPath);
        result.Path.Should().Be(binaryPath);
        result.DetectedVersion.Should().Be("14.1.1");
    }

    [Fact]
    public async Task Provider_PrefersSystemRgOverBundledManifest()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var temp = TempDirectory.Create();
        var systemBinaryPath = await CreateFakeRipgrepAsync(temp.Path, "14.1.1");
        var bundledBinaryPath = Path.Combine(temp.Path, "bundled-rg");
        await File.WriteAllTextAsync(bundledBinaryPath, "not a real binary\n");
        var bundledHash = Sha256(bundledBinaryPath);

        var provider = new DefaultRipgrepBinaryProvider(
            new RipgrepBinaryProviderOptions
            {
                BundledBinaries =
                [
                    new RipgrepBundledBinaryManifest
                    {
                        RuntimeIdentifier = CurrentRuntimeIdentifier(),
                        RelativePath = "bundled-rg",
                        Version = "14.1.1",
                        Sha256 = bundledHash
                    }
                ]
            },
            pathEnvironment: temp.Path,
            baseDirectory: temp.Path);

        var result = await provider.ResolveAsync();

        result.IsAvailable.Should().BeTrue();
        result.Source.Should().Be(RipgrepBinarySource.SystemPath);
        result.Path.Should().Be(systemBinaryPath);
    }

    [Fact]
    public async Task Provider_FallsBackToBundledManifestWhenSystemRgFailsVersionPolicy()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var temp = TempDirectory.Create();
        await CreateFakeRipgrepAsync(temp.Path, "13.0.0");
        var bundledBinaryPath = Path.Combine(temp.Path, "bundled-rg");
        await File.WriteAllTextAsync(bundledBinaryPath, "not a real binary\n");
        var bundledHash = Sha256(bundledBinaryPath);

        var provider = new DefaultRipgrepBinaryProvider(
            new RipgrepBinaryProviderOptions
            {
                VersionPolicy = RipgrepVersionPolicy.Exact,
                RequiredVersion = "14.1.1",
                BundledBinaries =
                [
                    new RipgrepBundledBinaryManifest
                    {
                        RuntimeIdentifier = CurrentRuntimeIdentifier(),
                        RelativePath = "bundled-rg",
                        Version = "14.1.1",
                        Sha256 = bundledHash
                    }
                ]
            },
            pathEnvironment: temp.Path,
            baseDirectory: temp.Path);

        var result = await provider.ResolveAsync();

        result.IsAvailable.Should().BeTrue();
        result.Source.Should().Be(RipgrepBinarySource.BundledPath);
        result.Path.Should().Be(bundledBinaryPath);
        result.DetectedVersion.Should().Be("14.1.1");
        result.VersionSatisfied.Should().BeTrue();
    }

    [Fact]
    public async Task Provider_AcceptsConfiguredBinaryWhenExactVersionMatches()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var temp = TempDirectory.Create();
        var binaryPath = await CreateFakeRipgrepAsync(temp.Path, "14.1.1");
        var provider = new DefaultRipgrepBinaryProvider(new RipgrepBinaryProviderOptions
        {
            ConfiguredPath = binaryPath,
            VersionPolicy = RipgrepVersionPolicy.Exact,
            RequiredVersion = "14.1.1"
        });

        var result = await provider.ResolveAsync();

        result.IsAvailable.Should().BeTrue();
        result.Source.Should().Be(RipgrepBinarySource.ConfiguredPath);
        result.DetectedVersion.Should().Be("14.1.1");
        result.VersionSatisfied.Should().BeTrue();
    }

    [Fact]
    public async Task Provider_RejectsConfiguredBinaryWhenExactVersionDoesNotMatch()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var temp = TempDirectory.Create();
        var binaryPath = await CreateFakeRipgrepAsync(temp.Path, "14.1.1");
        var provider = new DefaultRipgrepBinaryProvider(new RipgrepBinaryProviderOptions
        {
            ConfiguredPath = binaryPath,
            VersionPolicy = RipgrepVersionPolicy.Exact,
            RequiredVersion = "15.0.0"
        });

        var result = await provider.ResolveAsync();

        result.IsAvailable.Should().BeFalse();
        result.DetectedVersion.Should().Be("14.1.1");
        result.ReasonUnavailable.Should().Contain("does not match");
    }

    [Fact]
    public async Task Provider_AppliesMinimumVersionPolicy()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var temp = TempDirectory.Create();
        var binaryPath = await CreateFakeRipgrepAsync(temp.Path, "14.1.1");
        var accepted = new DefaultRipgrepBinaryProvider(new RipgrepBinaryProviderOptions
        {
            ConfiguredPath = binaryPath,
            VersionPolicy = RipgrepVersionPolicy.Minimum,
            MinimumVersion = "14.0.0"
        });
        var rejected = new DefaultRipgrepBinaryProvider(new RipgrepBinaryProviderOptions
        {
            ConfiguredPath = binaryPath,
            VersionPolicy = RipgrepVersionPolicy.Minimum,
            MinimumVersion = "15.0.0"
        });

        (await accepted.ResolveAsync()).IsAvailable.Should().BeTrue();
        (await rejected.ResolveAsync()).IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Provider_ResolvesSystemRgFromPathWhenAvailable()
    {
        var provider = new DefaultRipgrepBinaryProvider(new RipgrepBinaryProviderOptions
        {
            CaptureVersion = true
        });

        var result = await provider.ResolveAsync();

        if (!result.IsAvailable)
            return;

        result.Source.Should().BeOneOf(RipgrepBinarySource.SystemPath, RipgrepBinarySource.ConfiguredPath, RipgrepBinarySource.BundledPath);
        result.Path.Should().NotBeNullOrWhiteSpace();
        result.DetectedVersion.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RealProcessExecutor_ClearsRipgrepConfigPath()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var temp = TempDirectory.Create();
        var script = await CreateScriptAsync(temp.Path, "envcheck", "printf '%s\\n' \"${RIPGREP_CONFIG_PATH:-cleared}\"");
        System.Environment.SetEnvironmentVariable("RIPGREP_CONFIG_PATH", "/tmp/should-not-leak");

        try
        {
            var events = await CollectProcessAsync(new RealRipgrepProcessExecutor().ExecuteAsync(
                new RipgrepProcessRequest(script, [], temp.Path, TimeSpan.FromSeconds(5), 1024),
                CancellationToken.None));

            events.OfType<RipgrepStdoutLine>().Should().ContainSingle()
                .Which.Line.Should().Be("cleared");
            events.OfType<RipgrepProcessCompleted>().Should().ContainSingle()
                .Which.ExitCode.Should().Be(0);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("RIPGREP_CONFIG_PATH", null);
        }
    }

    [Fact]
    public async Task RealProcessExecutor_UsesArgumentListForPathsWithSpaces()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var temp = TempDirectory.Create();
        var script = await CreateScriptAsync(temp.Path, "argcheck", "printf '%s\\n' \"$1\"");

        var events = await CollectProcessAsync(new RealRipgrepProcessExecutor().ExecuteAsync(
            new RipgrepProcessRequest(script, ["path with spaces"], temp.Path, TimeSpan.FromSeconds(5), 1024),
            CancellationToken.None));

        events.OfType<RipgrepStdoutLine>().Should().ContainSingle()
            .Which.Line.Should().Be("path with spaces");
    }

    [Fact]
    public async Task RealProcessExecutor_CapsHugeStderr()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var temp = TempDirectory.Create();
        var script = await CreateScriptAsync(temp.Path, "stderrcheck", "printf '%05000d' 0 >&2\nexit 2");

        var events = await CollectProcessAsync(new RealRipgrepProcessExecutor().ExecuteAsync(
            new RipgrepProcessRequest(script, [], temp.Path, TimeSpan.FromSeconds(5), 128),
            CancellationToken.None));

        var completion = events.OfType<RipgrepProcessCompleted>().Should().ContainSingle().Subject;
        completion.ExitCode.Should().Be(2);
        completion.Stderr.Should().NotBeNull();
        completion.Stderr!.Should().Contain("[stderr truncated]");
        Encoding.UTF8.GetByteCount(completion.Stderr).Should().BeLessThan(256);
    }

    [Fact]
    public async Task RealProcessExecutor_ReturnsCompletionForProcessStartFailure()
    {
        var events = await CollectProcessAsync(new RealRipgrepProcessExecutor().ExecuteAsync(
            new RipgrepProcessRequest(
                OperatingSystem.IsWindows() ? @"Z:\definitely\missing\rg.exe" : "/definitely/missing/rg",
                [],
                Directory.GetCurrentDirectory(),
                TimeSpan.FromSeconds(5),
                1024),
            CancellationToken.None));

        var completion = events.OfType<RipgrepProcessCompleted>().Should().ContainSingle().Subject;
        completion.ExitCode.Should().BeNull();
        completion.Reason.Should().Be("process_start_failed");
    }

    [Fact]
    public async Task Runner_IsSafeForConcurrentCallers()
    {
        var runners = Enumerable.Range(0, 8)
            .Select(_ => CreateRunner(new RipgrepProcessCompleted(1, null, false, false, null)))
            .ToArray();

        var results = await Task.WhenAll(runners.Select(runner => CollectAsync(runner.SearchAsync(DefaultOptions()))));

        results.Should().HaveCount(8);
        results.SelectMany(events => events).OfType<RipgrepCompletionEvent>()
            .Should().AllSatisfy(completion => completion.Status.Should().Be(RipgrepCompletionStatus.NoMatches));
    }

    [Fact]
    public async Task RealRipgrepIntegration_SearchesTempFileWhenRgIsAvailable()
    {
        var provider = new DefaultRipgrepBinaryProvider();
        var binary = await provider.ResolveAsync();
        if (!binary.IsAvailable)
            return;

        using var temp = TempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "notes.txt"), "hello\nTODO integration\n");

        var runner = new RipgrepRunner(provider);
        var events = await CollectAsync(runner.SearchAsync(new RipgrepSearchOptions
        {
            Pattern = "TODO",
            WorkingDirectory = temp.Path,
            SearchPaths = ["."]
        }));

        events.OfType<RipgrepMatchEvent>().Should().ContainSingle()
            .Which.Text.Should().Contain("TODO integration");
        events.Last().Should().BeOfType<RipgrepCompletionEvent>()
            .Which.Status.Should().Be(RipgrepCompletionStatus.Success);
    }

    [Fact]
    public async Task RealBundledRipgrepIntegration_UsesBundledManifestWhenPathIsEmpty()
    {
        var realBinary = await TryResolveRealRipgrepAsync();
        if (realBinary == null)
            return;

        using var temp = TempDirectory.Create();
        var bundledPath = await CopyRealRipgrepAsBundledAsync(temp.Path, realBinary);
        var provider = new DefaultRipgrepBinaryProvider(
            new RipgrepBinaryProviderOptions
            {
                CaptureVersion = true,
                BundledBinaries =
                [
                    new RipgrepBundledBinaryManifest
                    {
                        RuntimeIdentifier = CurrentRuntimeIdentifier(),
                        RelativePath = Path.GetRelativePath(temp.Path, bundledPath),
                        Version = "0.0.0",
                        Sha256 = Sha256(bundledPath)
                    }
                ]
            },
            pathEnvironment: null,
            baseDirectory: temp.Path);

        var resolved = await provider.ResolveAsync();

        resolved.IsAvailable.Should().BeTrue();
        resolved.Source.Should().Be(RipgrepBinarySource.BundledPath);
        resolved.Path.Should().Be(bundledPath);
        resolved.DetectedVersion.Should().NotBeNullOrWhiteSpace();
        resolved.Sha256.Should().Be(Sha256(bundledPath));

        var work = Path.Combine(temp.Path, "work");
        Directory.CreateDirectory(work);
        await File.WriteAllTextAsync(Path.Combine(work, "notes.txt"), "hello\nTODO bundled\n");

        var runner = new RipgrepRunner(provider);
        var events = await CollectAsync(runner.SearchAsync(new RipgrepSearchOptions
        {
            Pattern = "TODO",
            WorkingDirectory = work,
            SearchPaths = ["."]
        }));

        events.OfType<RipgrepMatchEvent>().Should().ContainSingle()
            .Which.Text.Should().Contain("TODO bundled");
        events.Last().Should().BeOfType<RipgrepCompletionEvent>()
            .Which.Status.Should().Be(RipgrepCompletionStatus.Success);
    }

    [Fact]
    public async Task RealBundledRipgrepIntegration_PrefersSystemRipgrepWhenBundledAlsoExists()
    {
        var realBinary = await TryResolveRealRipgrepAsync();
        if (realBinary == null)
            return;

        using var temp = TempDirectory.Create();
        var bundledPath = await CopyRealRipgrepAsBundledAsync(temp.Path, realBinary);
        var systemDirectory = Path.GetDirectoryName(realBinary);
        if (string.IsNullOrWhiteSpace(systemDirectory))
            return;

        var provider = new DefaultRipgrepBinaryProvider(
            new RipgrepBinaryProviderOptions
            {
                CaptureVersion = true,
                BundledBinaries =
                [
                    new RipgrepBundledBinaryManifest
                    {
                        RuntimeIdentifier = CurrentRuntimeIdentifier(),
                        RelativePath = Path.GetRelativePath(temp.Path, bundledPath),
                        Version = "0.0.0",
                        Sha256 = Sha256(bundledPath)
                    }
                ]
            },
            pathEnvironment: systemDirectory,
            baseDirectory: temp.Path);

        var resolved = await provider.ResolveAsync();

        resolved.IsAvailable.Should().BeTrue();
        resolved.Source.Should().Be(RipgrepBinarySource.SystemPath);
        resolved.Path.Should().Be(realBinary);
        resolved.DetectedVersion.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RealHomebrewRipgrepIntegration_PrefersHomebrewRipgrepWhenBundledAlsoExists()
    {
        var homebrewBinary = TryResolveHomebrewRipgrep();
        if (homebrewBinary == null)
            return;

        using var temp = TempDirectory.Create();
        var bundledPath = await CopyRealRipgrepAsBundledAsync(temp.Path, homebrewBinary);
        var homebrewDirectory = Path.GetDirectoryName(homebrewBinary);
        if (string.IsNullOrWhiteSpace(homebrewDirectory))
            return;

        var provider = new DefaultRipgrepBinaryProvider(
            new RipgrepBinaryProviderOptions
            {
                CaptureVersion = true,
                BundledBinaries =
                [
                    new RipgrepBundledBinaryManifest
                    {
                        RuntimeIdentifier = CurrentRuntimeIdentifier(),
                        RelativePath = Path.GetRelativePath(temp.Path, bundledPath),
                        Version = "0.0.0",
                        Sha256 = Sha256(bundledPath)
                    }
                ]
            },
            pathEnvironment: homebrewDirectory,
            baseDirectory: temp.Path);

        var resolved = await provider.ResolveAsync();

        resolved.IsAvailable.Should().BeTrue();
        resolved.Source.Should().Be(RipgrepBinarySource.SystemPath);
        resolved.Path.Should().Be(homebrewBinary);
        resolved.DetectedVersion.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Source_DoesNotUseReflectionBasedJsonDeserializeForRipgrepEvents()
    {
        var sourcePath = FindRipgrepWrapperSourcePath();
        if (sourcePath == null)
            return;

        var source = File.ReadAllText(sourcePath);

        source.Should().NotContain("JsonSerializer.Deserialize");
        source.Should().Contain("JsonDocument.ParseValue");
    }

    [Fact]
    public void Source_DoesNotUseRuntimeDownloadsOrDynamicProviderDiscovery()
    {
        var sourcePath = FindRipgrepWrapperSourcePath();
        if (sourcePath == null)
            return;

        var source = File.ReadAllText(sourcePath);

        source.Should().NotContain("HttpClient");
        source.Should().NotContain("WebClient");
        source.Should().NotContain("Activator.CreateInstance");
        source.Should().NotContain("Type.GetType");
    }

    private static RipgrepEvent Parse(RipgrepJsonParser parser, string json)
    {
        parser.TryParse(Encoding.UTF8.GetBytes(json), out var parsedEvent, out var error).Should().BeTrue(error?.Message);
        parsedEvent.Should().NotBeNull();
        return parsedEvent!;
    }

    private static RipgrepRunner CreateRunner(params RipgrepProcessEvent[] processEvents)
        => new(
            new FakeBinaryProvider(),
            new RipgrepJsonParser(),
            new FakeProcessExecutor(processEvents));

    private static RipgrepRunner CreateRunner(FakeProcessExecutor executor)
        => new(
            new FakeBinaryProvider(),
            new RipgrepJsonParser(),
            executor);

    private static RipgrepSearchOptions DefaultOptions()
        => new()
        {
            Pattern = "TODO",
            WorkingDirectory = AppContext.BaseDirectory
        };

    private static async Task<IReadOnlyList<RipgrepEvent>> CollectAsync(IAsyncEnumerable<RipgrepEvent> events)
    {
        var result = new List<RipgrepEvent>();
        await foreach (var item in events)
            result.Add(item);
        return result;
    }

    private static async Task<IReadOnlyList<RipgrepProcessEvent>> CollectProcessAsync(IAsyncEnumerable<RipgrepProcessEvent> events)
    {
        var result = new List<RipgrepProcessEvent>();
        await foreach (var item in events)
            result.Add(item);
        return result;
    }

    private static async Task<string?> TryResolveRealRipgrepAsync()
    {
        var resolution = await new DefaultRipgrepBinaryProvider(new RipgrepBinaryProviderOptions
        {
            CaptureVersion = true
        }).ResolveAsync();

        return resolution.IsAvailable && !string.IsNullOrWhiteSpace(resolution.Path)
            ? resolution.Path
            : null;
    }

    private static string? TryResolveHomebrewRipgrep()
    {
        var candidates = OperatingSystem.IsMacOS()
            ? new[] { "/opt/homebrew/bin/rg", "/usr/local/bin/rg" }
            : [];

        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task<string> CopyRealRipgrepAsBundledAsync(string baseDirectory, string realBinary)
    {
        var runtimeDirectory = Path.Combine(baseDirectory, "runtimes", CurrentRuntimeIdentifier(), "native");
        Directory.CreateDirectory(runtimeDirectory);

        var fileName = OperatingSystem.IsWindows() ? "rg.exe" : "rg";
        var bundledPath = Path.Combine(runtimeDirectory, fileName);
        File.Copy(realBinary, bundledPath, overwrite: true);

        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(realBinary);
            File.SetUnixFileMode(bundledPath, mode | UnixFileMode.UserExecute);
        }

        await Task.CompletedTask;
        return bundledPath;
    }

    private static string Sha256(string path)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
    }

    private static string CurrentRuntimeIdentifier()
    {
        var os = OperatingSystem.IsWindows()
            ? "win"
            : OperatingSystem.IsMacOS()
                ? "osx"
                : "linux";

        var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            System.Runtime.InteropServices.Architecture.X86 => "x86",
            System.Runtime.InteropServices.Architecture.Arm => "arm",
            _ => System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
        };

        return $"{os}-{arch}";
    }

    private static async Task<string> CreateFakeRipgrepAsync(string directory, string version)
    {
        var path = System.IO.Path.Combine(directory, "rg");
        await File.WriteAllTextAsync(path, $"#!/bin/sh\necho 'ripgrep {version} (fake)'\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute |
                UnixFileMode.GroupRead |
                UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead |
                UnixFileMode.OtherExecute);
        }

        return path;
    }

    private static Task<string> CreateScriptAsync(string directory, string name, string body)
        => CreateExecutableFileAsync(directory, name, "#!/bin/sh\n" + body + "\n");

    private static async Task<string> CreateExecutableFileAsync(string directory, string name, string content)
    {
        var path = System.IO.Path.Combine(directory, name);
        await File.WriteAllTextAsync(path, content);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute |
                UnixFileMode.GroupRead |
                UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead |
                UnixFileMode.OtherExecute);
        }

        return path;
    }

    private static string? FindRipgrepWrapperSourcePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "HPD-Agent.ToolHarness",
                "HPD-Agent.ToolHarness.Coding",
                "Ripgrep",
                "RipgrepWrapper.cs");

            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        return null;
    }

    private sealed class FakeBinaryProvider : IRipgrepBinaryProvider
    {
        public ValueTask<RipgrepBinaryResolution> ResolveAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new RipgrepBinaryResolution
            {
                IsAvailable = true,
                Path = OperatingSystem.IsWindows() ? @"C:\rg.exe" : "/bin/rg",
                Source = RipgrepBinarySource.ConfiguredPath,
                VersionSatisfied = true
            });
    }

    private sealed class FakeProcessExecutor(IReadOnlyList<RipgrepProcessEvent> events) : IRipgrepProcessExecutor
    {
        public RipgrepProcessRequest? LastRequest { get; private set; }

        public async IAsyncEnumerable<RipgrepProcessEvent> ExecuteAsync(
            RipgrepProcessRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            LastRequest = request;
            foreach (var item in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return item;
            }
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
            Directory.CreateDirectory(path);
        }

        public string Path { get; }

        public static TempDirectory Create()
            => new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"hpd-ripgrep-wrapper-{Guid.NewGuid():N}"));

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
