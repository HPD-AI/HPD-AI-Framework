using System.Text;
using HPD.Agent;
using HPD.Agent.ToolHarness.Coding;
using HPD.Agent.Middleware;
using HPD.Events.Core;
using HPDOS.ToolHarnesses.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent.ToolHarness.Coding.Tests;

[Collection(CurrentDirectoryCollection.Name)]
public sealed class ReadFileTests : IDisposable
{
    private readonly string _originalCwd = Directory.GetCurrentDirectory();
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"hpd-read-file-tests-{Guid.NewGuid():N}");

    public ReadFileTests()
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
    public void ReadFile_RequiresPermission()
    {
        var method = typeof(CodingToolHarness).GetMethod(nameof(CodingToolHarness.ReadFile));

        method.Should().NotBeNull();
        method!.GetCustomAttributes(typeof(RequiresPermissionAttribute), inherit: false)
            .Should().ContainSingle();
    }

    [Fact]
    public async Task ReadFile_ReadsSmallTextFileWithLineNumbers()
    {
        await File.WriteAllTextAsync("example.cs", "using System;\n\npublic class Example\n");

        var result = await ReadFileTextAsync(new CodingToolHarness(), "example.cs");

        result.Should().Contain("""<file path=""");
        result.Should().Contain("start_line=\"1\"");
        result.Should().Contain("lines_read=\"3\"");
        result.Should().Contain("1\tusing System;");
        result.Should().Contain("2\t");
        result.Should().Contain("3\tpublic class Example");
    }

    [Fact]
    public async Task ReadFile_SupportsOffsetAndLimit()
    {
        await File.WriteAllTextAsync("range.txt", "one\ntwo\nthree\nfour\n");

        var result = await ReadFileTextAsync(new CodingToolHarness(), "range.txt", offset: 2, limit: 2);

        result.Should().Contain("start_line=\"2\"");
        result.Should().Contain("lines_read=\"2\"");
        result.Should().Contain("2\ttwo");
        result.Should().Contain("3\tthree");
        result.Should().NotContain("1\tone");
        result.Should().NotContain("4\tfour");
    }

    [Fact]
    public async Task ReadFile_EmitsNextReadWhenLineLimitLeavesMoreContent()
    {
        var lines = Enumerable.Range(1, 20).Select(i => $"line{i}");
        await File.WriteAllTextAsync("many-lines.txt", string.Join('\n', lines));

        var result = await ReadFileTextAsync(new CodingToolHarness(), "many-lines.txt", limit: 10);

        result.Should().Contain("lines_read=\"10\"");
        result.Should().Contain("truncated=\"true\"");
        result.Should().Contain("10\tline10");
        result.Should().NotContain("11\tline11");
        result.Should().Contain("<next_read offset=\"11\" limit=\"2000\" reason=\"output_truncated\" />");
    }

    [Fact]
    public async Task ReadFile_PreservesSourceWhitespace()
    {
        await File.WriteAllTextAsync("whitespace.txt", "  indented\tvalue  \n\tleading-tab\n");

        var result = await ReadFileTextAsync(new CodingToolHarness(), "whitespace.txt");

        result.Should().Contain("1\t  indented\tvalue  ");
        result.Should().Contain("2\t\tleading-tab");
    }

    [Fact]
    public async Task ReadFile_RejectsInvalidArguments()
    {
        var toolharness = new CodingToolHarness();

        (await ReadFileTextAsync(toolharness, null!)).Should().Contain("Path is required.");
        (await ReadFileTextAsync(toolharness, "file.txt", offset: 0)).Should().Contain("Offset must be greater than or equal to 1.");
        (await ReadFileTextAsync(toolharness, "file.txt", limit: 0)).Should().Contain("Limit must be between 1 and 2000.");
        (await ReadFileTextAsync(toolharness, "file.txt", limit: 2001)).Should().Contain("Limit must be between 1 and 2000.");
    }

    [Fact]
    public async Task ReadFile_RejectsMissingFilesAndSuggestsSameBasename()
    {
        await File.WriteAllTextAsync("Agent.ts", "export {}\n");

        var result = await ReadFileTextAsync(new CodingToolHarness(), "Agent.cs");

        result.Should().Contain("<error tool=\"ReadFile\"");
        result.Should().Contain("File does not exist. Did you mean Agent.ts?");
    }

    [Fact]
    public async Task ReadFile_RejectsDirectories()
    {
        Directory.CreateDirectory("src");

        var result = await ReadFileTextAsync(new CodingToolHarness(), "src");

        result.Should().Contain("Path is a directory. Use ListDirectory instead.");
    }

    [Fact]
    public async Task ReadFile_EscapesXmlSensitiveContent()
    {
        await File.WriteAllTextAsync("xml.txt", "if (x < y && y > z) return \"&\";\n");

        var result = await ReadFileTextAsync(new CodingToolHarness(), "xml.txt");

        result.Should().Contain("x &lt; y &amp;&amp; y &gt; z");
        result.Should().Contain("\"&amp;\"");
    }

    [Fact]
    public async Task ReadFile_ReadsSvgAsEscapedText()
    {
        await File.WriteAllTextAsync("image.svg", "<svg><circle cx=\"50\" cy=\"50\" r=\"40\" /></svg>\n");

        var result = await ReadFileTextAsync(new CodingToolHarness(), "image.svg");

        result.Should().Contain("1\t&lt;svg&gt;&lt;circle");
        result.Should().Contain("cx=\"50\"");
        result.Should().NotContain("Cannot read binary file.");
    }

    [Fact]
    public async Task ReadFile_HandlesEmptyFilesAndOffsetBeyondEnd()
    {
        await File.WriteAllTextAsync("empty.txt", string.Empty);
        await File.WriteAllTextAsync("small.txt", "one\n");

        var empty = await ReadFileTextAsync(new CodingToolHarness(), "empty.txt");
        var beyondEnd = await ReadFileTextAsync(new CodingToolHarness(), "small.txt", offset: 50);

        empty.Should().Contain("<empty_file");
        beyondEnd.Should().Contain("<no_content reason=\"offset_beyond_end\"");
    }

    [Fact]
    public async Task ReadFile_RejectsBinaryContentWithNullBytes()
    {
        await File.WriteAllBytesAsync("binary.bin", [0x01, 0x02, 0x00, 0x03]);

        var result = await ReadFileTextAsync(new CodingToolHarness(), "binary.bin");

        result.Should().Contain("Cannot read binary file.");
    }

    [Fact]
    public async Task ReadFile_ReadsUtf16LittleEndianWithBom()
    {
        await File.WriteAllTextAsync("utf16.txt", "hello\nworld\n", Encoding.Unicode);

        var result = await ReadFileTextAsync(new CodingToolHarness(), "utf16.txt");

        result.Should().Contain("1\thello");
        result.Should().Contain("2\tworld");
        result.Should().NotContain("Cannot read binary file.");
    }

    [Fact]
    public async Task ReadFile_ReadsUtf16BigEndianWithBom()
    {
        await File.WriteAllTextAsync("utf16be.txt", "hello\nworld\n", Encoding.BigEndianUnicode);

        var result = await ReadFileTextAsync(new CodingToolHarness(), "utf16be.txt");

        result.Should().Contain("1\thello");
        result.Should().Contain("2\tworld");
        result.Should().NotContain("Cannot read binary file.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadFile_ReadsUtf32WithBom(bool bigEndian)
    {
        var encoding = new UTF32Encoding(bigEndian, byteOrderMark: true, throwOnInvalidCharacters: true);
        await File.WriteAllTextAsync(bigEndian ? "utf32be.txt" : "utf32le.txt", "hello\nworld\n", encoding);

        var result = await ReadFileTextAsync(new CodingToolHarness(), bigEndian ? "utf32be.txt" : "utf32le.txt");

        result.Should().Contain("1\thello");
        result.Should().Contain("2\tworld");
        result.Should().NotContain("Cannot read binary file.");
    }

    [Fact]
    public async Task ReadFile_UsesUdeDetectedNonUtfEncoding()
    {
        var latin1Bytes = Encoding.Latin1.GetBytes("café déjà vu\nfaçade naïve\n");
        await File.WriteAllBytesAsync("latin1.txt", latin1Bytes);

        var result = await ReadFileTextAsync(new CodingToolHarness(), "latin1.txt");

        result.Should().Contain("1\tcafé déjà vu");
        result.Should().Contain("2\tfaçade naïve");
        result.Should().NotContain("Unable to decode file as text.");
    }

    [Fact]
    public async Task ReadFile_UsesUdeDetectedShiftJisEncoding()
    {
        byte[] shiftJisBytes =
        [
            0x82, 0xB1, 0x82, 0xF1, 0x82, 0xC9, 0x82, 0xBF,
            0x82, 0xCD, 0x81, 0x41, 0x90, 0xA2, 0x8A, 0x45,
            0x81, 0x49, 0x93, 0xFA, 0x96, 0x7B, 0x8C, 0xEA,
            0x82, 0xCC, 0x83, 0x65, 0x83, 0x58, 0x83, 0x67,
            0x82, 0xC5, 0x82, 0xB7, 0x81, 0x42, 0x0A
        ];
        await File.WriteAllBytesAsync("shift-jis.txt", shiftJisBytes);

        var result = await ReadFileTextAsync(new CodingToolHarness(), "shift-jis.txt");

        result.Should().Contain("1\tこんにちは、世界！日本語のテストです。");
        result.Should().NotContain("Unable to decode file as text.");
        result.Should().NotContain("\uFFFD");
    }

    [Fact]
    public async Task ReadFile_StripsUtf8Bom()
    {
        await File.WriteAllTextAsync("bom.txt", "hello\nworld\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var result = await ReadFileTextAsync(new CodingToolHarness(), "bom.txt");

        result.Should().Contain("1\thello");
        result.Should().NotContain("\uFEFF");
    }

    [Fact]
    public async Task ReadFile_ReturnsDecodeErrorForInvalidUtf8WithBom()
    {
        await File.WriteAllBytesAsync("invalid-utf8.txt", [0xEF, 0xBB, 0xBF, 0xC3, 0x28, 0x0A]);

        var result = await ReadFileTextAsync(new CodingToolHarness(), "invalid-utf8.txt");

        result.Should().Contain("Unable to decode file as text.");
        result.Should().NotContain("\uFFFD");
    }

    [Fact]
    public async Task ReadFile_TruncatesSelectedOutputAtByteCap()
    {
        var lines = Enumerable.Range(1, 300)
            .Select(i => $"{i:D3} {new string('x', 1900)}");
        await File.WriteAllTextAsync("byte-cap.txt", string.Join('\n', lines));

        var result = await ReadFileTextAsync(new CodingToolHarness(), "byte-cap.txt");

        result.Should().Contain("truncated=\"true\"");
        result.Should().Contain("<next_read offset=");
        result.Should().NotContain("300\t300 ");
    }

    [Fact]
    public async Task ReadFile_ShortensOverlongLinesAndEmitsNextReadWhenLimited()
    {
        var longLine = new string('a', 2200);
        await File.WriteAllTextAsync("large.txt", $"{longLine}\nsecond\nthird\n");

        var result = await ReadFileTextAsync(new CodingToolHarness(), "large.txt", limit: 2);

        result.Should().Contain("[line truncated]");
        result.Should().Contain("truncated=\"true\"");
        result.Should().Contain("<next_read offset=\"3\" limit=\"2000\" reason=\"output_truncated\" />");
    }

    [Fact]
    public async Task ReadFile_BlocksUnixDevicePaths()
    {
        if (OperatingSystem.IsWindows())
            return;

        var result = await ReadFileTextAsync(new CodingToolHarness(), "/dev/zero");

        result.Should().Contain("Cannot read blocked device path.");
    }

    [Fact]
    public async Task ReadFile_UsesRegisteredTextSourceBeforeFilesystem()
    {
        await File.WriteAllTextAsync("source.txt", "disk\n");
        var source = new FakeTextSource("source.txt", "editor\ntext\n");

        var result = await ReadFileTextAsync(new CodingToolHarness([source]), "source.txt");

        result.Should().Contain("1\teditor");
        result.Should().Contain("2\ttext");
        result.Should().NotContain("disk");
    }

    [Fact]
    public async Task ReadFile_FallsBackToFilesystemWhenTextSourceDoesNotOwnPath()
    {
        await File.WriteAllTextAsync("disk.txt", "disk\ncontent\n");
        var source = new FakeTextSource("other.txt", "editor\ntext\n");

        var result = await ReadFileTextAsync(new CodingToolHarness([source]), "disk.txt");

        result.Should().Contain("1\tdisk");
        result.Should().Contain("2\tcontent");
        result.Should().NotContain("editor");
    }

    [Fact]
    public async Task ReadFile_StoresReadStateAndReturnsUnchangedForSameVisibleRange()
    {
        await File.WriteAllTextAsync("state.txt", "one\ntwo\n");
        var agentContext = CreateAgentContext();

        var toolharness = new CodingToolHarness();

        var first = await ReadFileThroughMiddlewareAsync(agentContext, toolharness, "state.txt");
        var second = await ReadFileThroughMiddlewareAsync(agentContext, toolharness, "state.txt");

        first.Should().Contain("1\tone");
        second.Should().Contain("<file_unchanged");

        var state = GetReadFileState(agentContext);
        state.Should().NotBeNull();
        state!.FilesByPath.Should().ContainSingle();
        var snapshot = state.FilesByPath.Values.Single();
        snapshot.LinesRead.Should().Be(2);
        snapshot.StartLine.Should().Be(1);
        snapshot.EndLine.Should().Be(2);
        snapshot.Coverage.Should().Be(ReadFileCoverage.FullFile);
        snapshot.SourceKind.Should().Be(ReadFileSourceKind.FileSystem);
        snapshot.SourceVersion.Should().BeNull();
        snapshot.ReturnedContentHash.Should().Be(ComputeReturnedContentHash(["one", "two"]));
    }

    [Fact]
    public async Task ReadFile_StoresCoverageForEmptyPartialAndTruncatedReads()
    {
        await File.WriteAllTextAsync("empty-state.txt", string.Empty);
        await File.WriteAllTextAsync("partial-state.txt", "one\ntwo\nthree\n");
        var longLine = new string('x', 2200);
        await File.WriteAllTextAsync("truncated-state.txt", $"{longLine}\nsecond\n");
        var agentContext = CreateAgentContext();

        var toolharness = new CodingToolHarness();

        await ReadFileThroughMiddlewareAsync(agentContext, toolharness, "empty-state.txt");
        await ReadFileThroughMiddlewareAsync(agentContext, toolharness, "partial-state.txt", offset: 2, limit: 1);
        await ReadFileThroughMiddlewareAsync(agentContext, toolharness, "truncated-state.txt");

        var state = GetReadFileState(agentContext)!;

        var empty = state.FilesByPath[Path.GetFullPath("empty-state.txt")];
        empty.Coverage.Should().Be(ReadFileCoverage.EmptyFile);
        empty.StartLine.Should().Be(0);
        empty.EndLine.Should().Be(0);

        var partial = state.FilesByPath[Path.GetFullPath("partial-state.txt")];
        partial.Coverage.Should().Be(ReadFileCoverage.PartialRange);
        partial.StartLine.Should().Be(2);
        partial.EndLine.Should().Be(2);
        partial.ReturnedContentHash.Should().Be(ComputeReturnedContentHash(["two"]));

        var truncated = state.FilesByPath[Path.GetFullPath("truncated-state.txt")];
        truncated.Coverage.Should().Be(ReadFileCoverage.Truncated);
        truncated.Truncated.Should().BeTrue();
    }

    [Fact]
    public async Task ReadFile_DoesNotReturnUnchangedWhenCompactionIsNewerThanPriorRead()
    {
        await File.WriteAllTextAsync("reduced.txt", "one\ntwo\n");
        var agentContext = CreateAgentContext();
        var toolharness = new CodingToolHarness();

        await ReadFileThroughMiddlewareAsync(agentContext, toolharness, "reduced.txt");

        CreateBeforeFunctionContext(agentContext)
            .UpdateMiddlewareState<CompactionStateData>(state =>
                state.WithCompactionApplied(DateTimeOffset.UtcNow.AddSeconds(1)));

        var second = await ReadFileThroughMiddlewareAsync(agentContext, toolharness, "reduced.txt");

        second.Should().Contain("1\tone");
        second.Should().NotContain("<file_unchanged");
    }

    [Fact]
    public async Task ReadFile_DoesNotReturnUnchangedWhenSameMetadataSourceContentChanges()
    {
        var agentContext = CreateAgentContext();
        var lastWriteTime = DateTimeOffset.UtcNow;
        var source = new MutableTextSource("virtual.txt", "one\ntwo\n", lastWriteTime);

        var toolharness = new CodingToolHarness([source]);

        var first = await ReadFileThroughMiddlewareAsync(agentContext, toolharness, "virtual.txt");
        source.Content = "ONE\ntwo\n";
        var second = await ReadFileThroughMiddlewareAsync(agentContext, toolharness, "virtual.txt");

        first.Should().Contain("1\tone");
        second.Should().Contain("1\tONE");
        second.Should().NotContain("<file_unchanged");
    }

    [Fact]
    public async Task ReadFile_RecordsTextSourceKindAndVersionAndUsesVersionForDedup()
    {
        var agentContext = CreateAgentContext();
        var source = new MutableTextSource("versioned.txt", "one\ntwo\n", DateTimeOffset.UtcNow)
        {
            Version = "v1"
        };

        var toolharness = new CodingToolHarness([source]);

        var first = await ReadFileThroughMiddlewareAsync(agentContext, toolharness, "versioned.txt");
        var second = await ReadFileThroughMiddlewareAsync(agentContext, toolharness, "versioned.txt");
        source.Version = "v2";
        var third = await ReadFileThroughMiddlewareAsync(agentContext, toolharness, "versioned.txt");

        first.Should().Contain("1\tone");
        second.Should().Contain("<file_unchanged");
        third.Should().Contain("1\tone");
        third.Should().NotContain("<file_unchanged");

        var snapshot = GetReadFileState(agentContext)!
            .FilesByPath[Path.GetFullPath("versioned.txt")];

        snapshot.SourceKind.Should().Be(ReadFileSourceKind.TextSource);
        snapshot.SourceVersion.Should().Be("v2");
        snapshot.Coverage.Should().Be(ReadFileCoverage.FullFile);
    }

    [Fact]
    public void FunctionExecutionContext_DoesNotExposeStateMutationApis()
    {
        typeof(FunctionExecutionContext).GetMethod("UpdateState").Should().BeNull();
        typeof(FunctionExecutionContext).GetMethod("UpdateMiddlewareState").Should().BeNull();
        typeof(FunctionExecutionContext).GetProperty("State").Should().BeNull();
        typeof(FunctionExecutionContext).GetProperty("Session").Should().BeNull();
        typeof(FunctionExecutionContext).GetProperty("Thread").Should().BeNull();
    }

    [Fact]
    public async Task ReadFile_RootQualifiedPath_IsTreatedAsLiteralPath()
    {
        var docsRoot = Path.Combine(_tempRoot, "docs");
        Directory.CreateDirectory(docsRoot);
        await File.WriteAllTextAsync(Path.Combine(docsRoot, "notes.md"), "# docs\n");

        var result = await ReadFileTextAsync(
            new CodingToolHarness(),
            "@docs/notes.md",
            runConfig: CreateWorkspaceRunConfig(_tempRoot, docsRoot));

        result.Should().Contain("File does not exist");
        result.Should().Contain("@docs/notes.md");
    }

    [Fact]
    public async Task ReadFile_AbsolutePathOutsideWorkspace_ReadsWhenInvoked()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"hpd-read-outside-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(outside, "secret");
        try
        {
            var result = await ReadFileTextAsync(
                new CodingToolHarness(),
                outside,
                runConfig: CreateWorkspaceRunConfig(_tempRoot));

            result.Should().Contain("1\tsecret");
            result.Should().Contain(outside);
        }
        finally
        {
            if (File.Exists(outside))
                File.Delete(outside);
        }
    }

    private static async Task<string> ReadFileTextAsync(
        CodingToolHarness toolharness,
        string? path,
        int offset = 1,
        int limit = 2000,
        AgentRunConfig? runConfig = null)
    {
        var agentContext = CreateAgentContext();
        var beforeContext = CreateBeforeFunctionContext(agentContext);
        runConfig ??= CreateWorkspaceRunConfig();
        var request = new FunctionRequest
        {
            Function = AIFunctionFactory.Create(
                (object? _, CancellationToken _) => Task.FromResult<object?>(null),
                new AIFunctionFactoryOptions
                {
                    Name = "ReadFile",
                    Description = "Test ReadFile function"
                }),
            CallId = beforeContext.FunctionCallId,
            Arguments = new Dictionary<string, object?>
            {
                ["path"] = path,
                ["offset"] = offset,
                ["limit"] = limit
            },
            State = agentContext.State,
            RunConfig = runConfig,
            EventCoordinator = agentContext.EventCoordinator
        };
        var functionContext = new FunctionExecutionContext(beforeContext, request);
        var result = await toolharness.ReadFile(path!, functionContext, offset, limit);
        return result switch
        {
            string text => text,
            _ => result?.ToString() ?? string.Empty
        };
    }

    private static async Task<string> ReadFileThroughMiddlewareAsync(
        AgentContext agentContext,
        CodingToolHarness toolharness,
        string? path,
        int offset = 1,
        int limit = 2000)
    {
        var beforeContext = CreateBeforeFunctionContext(agentContext);
        var runConfig = CreateWorkspaceRunConfig();
        var request = new FunctionRequest
        {
            Function = AIFunctionFactory.Create(
                (object? _, CancellationToken _) => Task.FromResult<object?>(null),
                new AIFunctionFactoryOptions
                {
                    Name = "ReadFile",
                    Description = "Test ReadFile function"
                }),
            CallId = beforeContext.FunctionCallId,
            Arguments = new Dictionary<string, object?>(),
            State = agentContext.State,
            RunConfig = runConfig,
            EventCoordinator = agentContext.EventCoordinator
        };

        var functionContext = new FunctionExecutionContext(beforeContext, request);
        var result = await toolharness.ReadFile(path!, functionContext, offset, limit);

        var afterContext = agentContext.AsAfterFunction(
            function: null,
            callId: beforeContext.FunctionCallId,
            result: result,
            exception: null,
            runConfig: runConfig,
            toolharnessName: "CodingToolHarness",
            resultMetadata: request.ResultMetadata);

        await new EnvironmentContextMiddleware().AfterFunctionAsync(afterContext, CancellationToken.None);

        return afterContext.Result switch
        {
            string text => text,
            _ => afterContext.Result?.ToString() ?? string.Empty
        };
    }

    private static ReadFileState? GetReadFileState(AgentContext agentContext)
        => CreateBeforeFunctionContext(agentContext).GetMiddlewareState<ReadFileState>();

    private static AgentContext CreateAgentContext()
    {
        var state = AgentLoopState.InitialSafe(
            [],
            "test-run",
            "test-conversation",
            "test-agent");

        return new AgentContext(
            "test-agent",
            "test-conversation",
            state,
            new EventCoordinator(),
            new Session("test-session"),
            new Thread("test-session"),
            CancellationToken.None);
    }

    private static BeforeFunctionContext CreateBeforeFunctionContext(AgentContext? agentContext = null)
    {
        agentContext ??= CreateAgentContext();
        return agentContext.AsBeforeFunction(
            function: null,
            callId: "call-1",
            arguments: new Dictionary<string, object?>(),
            runConfig: CreateWorkspaceRunConfig(),
            toolharnessName: "CodingToolHarness");
    }

    private static AgentRunConfig CreateWorkspaceRunConfig(string? defaultRoot = null, string? docsRoot = null)
    {
        var cwd = Path.GetFullPath(defaultRoot ?? Directory.GetCurrentDirectory());
        var roots = new List<AgentWorkspaceRoot>
        {
            new("default", cwd)
        };

        if (docsRoot is not null)
            roots.Add(new AgentWorkspaceRoot("docs", Path.GetFullPath(docsRoot), "Docs"));

        return new AgentRunConfig
        {
            ContextOverrides = new()
            {
                [AgentWorkspace.ContextKey] = new AgentWorkspace(
                    "default",
                    cwd,
                    roots)
            }
        };
    }

    private static string ComputeReturnedContentHash(IReadOnlyList<string> lines)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines)));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed class FakeTextSource(string fileName, string content) : IReadFileTextSource
    {
        public ValueTask<ReadFileTextSourceResult?> TryReadTextAsync(string fullPath, CancellationToken cancellationToken)
        {
            if (!string.Equals(Path.GetFileName(fullPath), fileName, StringComparison.Ordinal))
                return ValueTask.FromResult<ReadFileTextSourceResult?>(null);

            return ValueTask.FromResult<ReadFileTextSourceResult?>(new ReadFileTextSourceResult
            {
                FullPath = fullPath,
                Reader = new StringReader(content),
                LastWriteTimeUtc = DateTimeOffset.UtcNow,
                Length = content.Length,
                IsUnsavedEditorContent = true
            });
        }
    }

    private sealed class MutableTextSource : IReadFileTextSource
    {
        private readonly string _fileName;
        private readonly int _length;
        private readonly DateTimeOffset _lastWriteTimeUtc;

        public MutableTextSource(string fileName, string content, DateTimeOffset lastWriteTimeUtc)
        {
            _fileName = fileName;
            _length = content.Length;
            _lastWriteTimeUtc = lastWriteTimeUtc;
            Content = content;
        }

        public string Content { get; set; }

        public string? Version { get; set; }

        public ValueTask<ReadFileTextSourceResult?> TryReadTextAsync(string fullPath, CancellationToken cancellationToken)
        {
            if (!string.Equals(Path.GetFileName(fullPath), _fileName, StringComparison.Ordinal))
                return ValueTask.FromResult<ReadFileTextSourceResult?>(null);

            return ValueTask.FromResult<ReadFileTextSourceResult?>(new ReadFileTextSourceResult
            {
                FullPath = fullPath,
                Reader = new StringReader(Content),
                LastWriteTimeUtc = _lastWriteTimeUtc,
                Length = _length,
                Version = Version,
                IsUnsavedEditorContent = true
            });
        }
    }
}
