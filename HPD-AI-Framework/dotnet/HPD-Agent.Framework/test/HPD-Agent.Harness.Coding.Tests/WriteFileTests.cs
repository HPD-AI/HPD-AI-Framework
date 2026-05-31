using System.Text;
using HPD.Agent;
using HPD.Agent.Harness.Coding;
using HPD.Agent.Middleware;
using HPD.Events;
using HPD.Events.Core;
using HPDOS.Harneses.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Harness.Coding.Tests;

[Collection(CurrentDirectoryCollection.Name)]
public sealed class WriteFileTests : IDisposable
{
    private readonly string _originalCwd = Directory.GetCurrentDirectory();
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"hpd-write-file-tests-{Guid.NewGuid():N}");

    public WriteFileTests()
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
    public void WriteFile_RequiresPermission()
    {
        var method = typeof(CodingHarness).GetMethod(nameof(CodingHarness.WriteFile));

        method.Should().NotBeNull();
        method!.GetCustomAttributes(typeof(RequiresPermissionAttribute), inherit: false)
            .Should().ContainSingle();
    }

    [Fact]
    public async Task WriteFile_RejectsMissingPathAndNullContent()
    {
        var harness = new CodingHarness();

        var missingPath = await WriteFileTextAsync(CreateAgentContext(), harness, " ", "content");
        var nullContent = await WriteFileTextAsync(CreateAgentContext(), harness, "A.cs", null!);

        missingPath.Should().Contain("kind=\"invalid_arguments\"");
        missingPath.Should().Contain("Path is required.");
        nullContent.Should().Contain("kind=\"invalid_arguments\"");
        nullContent.Should().Contain("Content is required.");
    }

    [Fact]
    public async Task WriteFile_AllowsEmptyContent()
    {
        var result = await WriteFileTextAsync(CreateAgentContext(), new CodingHarness(), "empty.txt", string.Empty);

        result.Should().Contain("mode=\"create\"");
        File.ReadAllText("empty.txt").Should().BeEmpty();
    }

    [Fact]
    public async Task WriteFile_PropagatesSharedGuardErrors()
    {
        Directory.CreateDirectory("dir");
        await File.WriteAllBytesAsync("binary.bin", [0x01, 0x00, 0x02]);
        await File.WriteAllTextAsync("notebook.ipynb", "{}");
        await using (var stream = new FileStream("large.txt", FileMode.Create, FileAccess.Write))
            stream.SetLength(50L * 1024 * 1024 + 1);

        var harness = new CodingHarness();

        (await WriteFileTextAsync(CreateAgentContext(), harness, "dir", "x")).Should().Contain("kind=\"path_is_directory\"");
        (await WriteFileTextAsync(CreateAgentContext(), harness, "/dev/zero", "x")).Should().Contain("kind=\"blocked_device_path\"");
        (await WriteFileTextAsync(CreateAgentContext(), harness, "//server/share/file.txt", "x")).Should().Contain("kind=\"windows_unc_path\"");
        (await WriteFileTextAsync(CreateAgentContext(), harness, "binary.bin", "x")).Should().Contain("kind=\"binary_file\"");
        (await WriteFileTextAsync(CreateAgentContext(), harness, "notebook.ipynb", "x")).Should().Contain("kind=\"notebook_file\"");
        (await WriteFileTextAsync(CreateAgentContext(), harness, "large.txt", "x")).Should().Contain("kind=\"file_too_large\"");
    }

    [Fact]
    public async Task WriteFile_CreatesMissingFileAndParents()
    {
        var agentContext = CreateAgentContext();

        var result = await WriteFileTextAsync(agentContext, new CodingHarness(), "src/NewFile.cs", "class NewFile {}\n");

        result.Should().Contain("mode=\"create\"");
        result.Should().Contain("changed=\"true\"");
        result.Should().Contain("created=\"true\"");
        result.Should().Contain("event_emitted=\"true\"");
        File.ReadAllText(Path.Combine("src", "NewFile.cs")).Should().Be("class NewFile {}\n");
        GetReadFileState(agentContext)!.FilesByPath[FullPath("src/NewFile.cs")].Coverage.Should().Be(ReadFileCoverage.FullFile);
    }

    [Fact]
    public async Task WriteFile_FillsExistingEmptyFileWithoutPriorRead()
    {
        await File.WriteAllTextAsync("empty.txt", string.Empty);

        var result = await WriteFileTextAsync(CreateAgentContext(), new CodingHarness(), "empty.txt", "filled\n");

        result.Should().Contain("mode=\"fill_empty\"");
        result.Should().Contain("changed=\"true\"");
        File.ReadAllText("empty.txt").Should().Be("filled\n");
    }

    [Fact]
    public async Task WriteFile_ExistingNonEmptyFileRequiresFullRead()
    {
        await File.WriteAllTextAsync("A.cs", "class A {}\n");

        var result = await WriteFileTextAsync(CreateAgentContext(), new CodingHarness(), "A.cs", "class B {}\n");

        result.Should().Contain("kind=\"not_read\"");
        File.ReadAllText("A.cs").Should().Be("class A {}\n");
    }

    [Fact]
    public async Task WriteFile_RejectsPartialAndTruncatedPriorReads()
    {
        await File.WriteAllTextAsync("partial.cs", "one\ntwo\nthree\n");
        var longLine = new string('x', 2200);
        await File.WriteAllTextAsync("truncated.cs", $"{longLine}\nsecond\n");
        var harness = new CodingHarness();

        var partialContext = CreateAgentContext();
        await ReadFileTextAsync(partialContext, harness, "partial.cs", offset: 2, limit: 1);
        var partial = await WriteFileTextAsync(partialContext, harness, "partial.cs", "rewrite\n");

        var truncatedContext = CreateAgentContext();
        await ReadFileTextAsync(truncatedContext, harness, "truncated.cs");
        var truncated = await WriteFileTextAsync(truncatedContext, harness, "truncated.cs", "rewrite\n");

        partial.Should().Contain("kind=\"partial_read\"");
        truncated.Should().Contain("kind=\"partial_read\"");
    }

    [Fact]
    public async Task WriteFile_RejectsHistoryReducedRead()
    {
        await File.WriteAllTextAsync("A.cs", "before\n");
        var agentContext = CreateAgentContext();
        var harness = new CodingHarness();
        await ReadFileTextAsync(agentContext, harness, "A.cs");
        CreateBeforeFunctionContext(agentContext)
            .UpdateMiddlewareState<CompactionStateData>(state =>
                state.WithCompactionApplied(DateTimeOffset.UtcNow.AddSeconds(1)));

        var result = await WriteFileTextAsync(agentContext, harness, "A.cs", "after\n");

        result.Should().Contain("kind=\"history_reduced_read\"");
    }

    [Fact]
    public async Task WriteFile_RejectsStaleLengthOrTimestamp()
    {
        await File.WriteAllTextAsync("A.cs", "before\n");
        var agentContext = CreateAgentContext();
        var harness = new CodingHarness();
        await ReadFileTextAsync(agentContext, harness, "A.cs");
        await File.WriteAllTextAsync("A.cs", "changed externally\n");

        var result = await WriteFileTextAsync(agentContext, harness, "A.cs", "after\n");

        result.Should().Contain("kind=\"stale_read\"");
    }

    [Fact]
    public async Task WriteFile_AllowsTimestampOnlyChangeWhenContentHashIsUnchanged()
    {
        await File.WriteAllTextAsync("A.cs", "before\n");
        var agentContext = CreateAgentContext();
        var harness = new CodingHarness();
        await ReadFileTextAsync(agentContext, harness, "A.cs");
        File.SetLastWriteTimeUtc("A.cs", DateTime.UtcNow.AddMinutes(2));

        var result = await WriteFileTextAsync(agentContext, harness, "A.cs", "after\n");

        result.Should().Contain("mode=\"rewrite\"");
        result.Should().Contain("changed=\"true\"");
        File.ReadAllText("A.cs").Should().Be("after\n");
    }

    [Fact]
    public async Task WriteFile_RewritesAfterFullReadAndNoOpDoesNotEmitEvent()
    {
        await File.WriteAllTextAsync("A.cs", "before\n");
        var agentContext = CreateAgentContext();
        var harness = new CodingHarness();
        await ReadFileTextAsync(agentContext, harness, "A.cs");

        var rewrite = await WriteFileTextAsync(agentContext, harness, "A.cs", "after\n");
        var noOp = await WriteFileTextAsync(agentContext, harness, "A.cs", "after\n");

        rewrite.Should().Contain("mode=\"rewrite\"");
        rewrite.Should().Contain("changed=\"true\"");
        rewrite.Should().Contain("event_emitted=\"true\"");
        noOp.Should().Contain("changed=\"false\"");
        noOp.Should().Contain("event_emitted=\"false\"");
    }

    [Fact]
    public async Task WriteFile_PreservesCallerProvidedLineEndings()
    {
        var harness = new CodingHarness();
        await WriteFileTextAsync(CreateAgentContext(), harness, "lf.txt", "one\ntwo\n");
        await WriteFileTextAsync(CreateAgentContext(), harness, "crlf.txt", "one\r\ntwo\r\n");

        File.ReadAllText("lf.txt").Should().Be("one\ntwo\n");
        File.ReadAllText("crlf.txt").Should().Be("one\r\ntwo\r\n");
    }

    [Fact]
    public async Task WriteFile_DoesNotForceOldLineEndingsOnRewrite()
    {
        await File.WriteAllTextAsync("old-crlf.txt", "one\r\ntwo\r\n");
        var agentContext = CreateAgentContext();
        var harness = new CodingHarness();
        await ReadFileTextAsync(agentContext, harness, "old-crlf.txt");

        await WriteFileTextAsync(agentContext, harness, "old-crlf.txt", "three\nfour\n");

        File.ReadAllText("old-crlf.txt").Should().Be("three\nfour\n");
    }

    [Fact]
    public async Task WriteFile_PreservesExistingBomAndNewFilesDefaultToUtf8WithoutBom()
    {
        await File.WriteAllTextAsync("bom.txt", "before\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var agentContext = CreateAgentContext();
        var harness = new CodingHarness();
        await ReadFileTextAsync(agentContext, harness, "bom.txt");

        await WriteFileTextAsync(agentContext, harness, "bom.txt", "after\n");
        await WriteFileTextAsync(CreateAgentContext(), harness, "new.txt", "new\n");

        (await File.ReadAllBytesAsync("bom.txt"))[..3].Should().Equal(0xEF, 0xBB, 0xBF);
        (await File.ReadAllBytesAsync("new.txt"))[..3].Should().NotEqual([0xEF, 0xBB, 0xBF]);
    }

    [Fact]
    public async Task WriteFile_RejectsOmissionPlaceholdersButAllowsStringLiteralText()
    {
        var harness = new CodingHarness();

        var rejected = await WriteFileTextAsync(CreateAgentContext(), harness, "bad.cs", "class A\n{\n    // rest of methods ...\n}\n");
        var allowed = await WriteFileTextAsync(CreateAgentContext(), harness, "ok.cs", "const string s = \"rest of methods ...\";\n");

        rejected.Should().Contain("kind=\"new_omission_placeholder\"");
        allowed.Should().Contain("changed=\"true\"");
    }

    [Fact]
    public async Task WriteFile_XmlOmitsContentAndDiffButIncludesHashLengthAndLines()
    {
        var result = await WriteFileTextAsync(CreateAgentContext(), new CodingHarness(), "A.cs", "secret content\nline2\n");

        result.Should().StartWith("<write_file ");
        result.Should().Contain("content_hash=\"sha256:");
        result.Should().Contain("byte_length=\"");
        result.Should().Contain("first_changed_line=\"");
        result.Should().Contain("last_changed_line=\"");
        result.Should().NotContain("secret content");
        result.Should().NotContain("---");
        result.Should().NotContain("+++");
    }

    [Fact]
    public async Task WriteFile_SetsMutationMetadataWithKindAndByteLength()
    {
        var agentContext = CreateAgentContext();
        var result = await WriteFileWithContextAsync(agentContext, new CodingHarness(), "A.cs", "class A {}\n");

        result.Metadata.TryGet<CodingFileMutationSnapshot>(
            CodingToolMetadataKeys.FileMutationSnapshot,
            out var mutation).Should().BeTrue();
        mutation!.ToolName.Should().Be("WriteFile");
        mutation.Kind.Should().Be(CodingFileMutationKind.Created);
        mutation.ByteLength.Should().Be(Encoding.UTF8.GetByteCount("class A {}\n"));
        mutation.Text.Should().Be("class A {}\n");
    }

    [Fact]
    public async Task WriteFile_EmitsFileWriteAppliedEventWithSharedMutationData()
    {
        using var coordinator = new EventCoordinator();
        var events = new List<FileWriteAppliedEvent>();
        using var subscription = coordinator.Subscribe<FileWriteAppliedEvent>(evt =>
        {
            events.Add(evt);
            return ValueTask.CompletedTask;
        });
        var agentContext = CreateAgentContext(coordinator);
        var content = "class A {}\n";

        var result = await WriteFileTextAsync(agentContext, new CodingHarness(), "A.cs", content);

        result.Should().Contain("event_emitted=\"true\"");
        var writeEvent = events.Should().ContainSingle().Subject;
        writeEvent.ToolCallId.Should().Be("call-1");
        writeEvent.FunctionName.Should().Be(nameof(CodingHarness.WriteFile));
        writeEvent.Path.Should().Be(FullPath("A.cs"));
        writeEvent.Mode.Should().Be(FileWriteMode.Create);
        writeEvent.MutationKind.Should().Be(CodingFileMutationKind.Created);
        writeEvent.Created.Should().BeTrue();
        writeEvent.Changed.Should().BeTrue();
        writeEvent.Before.Text.Should().BeEmpty();
        writeEvent.After.Text.Should().Be(content);
        writeEvent.After.ByteLength.Should().Be(Encoding.UTF8.GetByteCount(content));
        writeEvent.TextEdits.Should().ContainSingle()
            .Which.NewText.Should().Be(content);
        writeEvent.Hunks.Should().NotBeEmpty();
        writeEvent.DiffStat.AddedLines.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task WriteFile_NoOpDoesNotSetMutationMetadataOrEmitEvent()
    {
        await File.WriteAllTextAsync("A.cs", "same\n");
        using var coordinator = new EventCoordinator();
        var events = new List<FileWriteAppliedEvent>();
        using var subscription = coordinator.Subscribe<FileWriteAppliedEvent>(evt =>
        {
            events.Add(evt);
            return ValueTask.CompletedTask;
        });
        var agentContext = CreateAgentContext(coordinator);
        var harness = new CodingHarness();
        await ReadFileTextAsync(agentContext, harness, "A.cs");

        var result = await WriteFileWithContextAsync(agentContext, harness, "A.cs", "same\n");

        ResultToString(result.Result).Should().Contain("changed=\"false\"");
        ResultToString(result.Result).Should().Contain("event_emitted=\"false\"");
        events.Should().BeEmpty();
        result.Metadata.TryGet<CodingFileMutationSnapshot>(
            CodingToolMetadataKeys.FileMutationSnapshot,
            out _).Should().BeFalse();
    }

    [Fact]
    public async Task WriteFile_ExistingUtf16BomReportsExactByteLengthInXmlAndMetadata()
    {
        await File.WriteAllTextAsync("utf16.txt", "before\n", Encoding.Unicode);
        var agentContext = CreateAgentContext();
        var harness = new CodingHarness();
        await ReadFileTextAsync(agentContext, harness, "utf16.txt");

        var result = await WriteFileWithContextAsync(agentContext, harness, "utf16.txt", "after\n");

        var expectedLength = Encoding.Unicode.GetPreamble().Length + Encoding.Unicode.GetByteCount("after\n");
        ResultToString(result.Result).Should().Contain($"byte_length=\"{expectedLength}\"");
        new FileInfo("utf16.txt").Length.Should().Be(expectedLength);
        result.Metadata.TryGet<CodingFileMutationSnapshot>(
            CodingToolMetadataKeys.FileMutationSnapshot,
            out var mutation).Should().BeTrue();
        mutation!.ByteLength.Should().Be(expectedLength);
        (await File.ReadAllBytesAsync("utf16.txt"))[..2].Should().Equal(0xFF, 0xFE);
    }

    private static async Task<string> ReadFileTextAsync(
        AgentContext agentContext,
        CodingHarness harness,
        string path,
        int offset = 1,
        int limit = 2000)
    {
        var beforeContext = CreateBeforeFunctionContext(agentContext);
        var request = CreateFunctionRequest(agentContext, beforeContext, nameof(CodingHarness.ReadFile), new Dictionary<string, object?>
        {
            ["path"] = path,
            ["offset"] = offset,
            ["limit"] = limit
        });

        var functionContext = new FunctionExecutionContext(beforeContext, request);
        var result = await harness.ReadFile(path, functionContext, offset, limit);

        var afterContext = agentContext.AsAfterFunction(
            function: null,
            callId: beforeContext.FunctionCallId,
            result: result,
            exception: null,
            runConfig: beforeContext.RunConfig,
            harnessName: "CodingHarness",
            resultMetadata: request.ResultMetadata);

        await new EnvironmentContextMiddleware().AfterFunctionAsync(afterContext, CancellationToken.None);
        return ResultToString(afterContext.Result);
    }

    private static async Task<string> WriteFileTextAsync(
        AgentContext agentContext,
        CodingHarness harness,
        string path,
        string content)
        => ResultToString((await WriteFileWithContextAsync(agentContext, harness, path, content)).Result);

    private static async Task<(object? Result, ToolResultMetadata Metadata)> WriteFileWithContextAsync(
        AgentContext agentContext,
        CodingHarness harness,
        string path,
        string content)
    {
        var beforeContext = CreateBeforeFunctionContext(agentContext);
        var request = CreateFunctionRequest(agentContext, beforeContext, nameof(CodingHarness.WriteFile), new Dictionary<string, object?>
        {
            ["path"] = path,
            ["content"] = content
        });

        var functionContext = new FunctionExecutionContext(beforeContext, request);
        var result = await harness.WriteFile(path, content, functionContext);

        var afterContext = agentContext.AsAfterFunction(
            function: null,
            callId: beforeContext.FunctionCallId,
            result: result,
            exception: null,
            runConfig: beforeContext.RunConfig,
            harnessName: "CodingHarness",
            resultMetadata: request.ResultMetadata);

        await new EnvironmentContextMiddleware().AfterFunctionAsync(afterContext, CancellationToken.None);
        return (afterContext.Result, request.ResultMetadata);
    }

    private static FunctionRequest CreateFunctionRequest(
        AgentContext agentContext,
        BeforeFunctionContext beforeContext,
        string name,
        IReadOnlyDictionary<string, object?> arguments)
        => new()
        {
            Function = AIFunctionFactory.Create(
                (object? _, CancellationToken _) => Task.FromResult<object?>(null),
                new AIFunctionFactoryOptions
                {
                    Name = name,
                    Description = $"Test {name} function"
                }),
            CallId = beforeContext.FunctionCallId,
            Arguments = arguments,
            State = agentContext.State,
            RunConfig = beforeContext.RunConfig,
            EventCoordinator = agentContext.EventCoordinator
        };

    private static string ResultToString(object? result)
        => result switch
        {
            string text => text,
            _ => result?.ToString() ?? string.Empty
        };

    private static ReadFileState? GetReadFileState(AgentContext agentContext)
        => CreateBeforeFunctionContext(agentContext).GetMiddlewareState<ReadFileState>();

    private static AgentContext CreateAgentContext(IEventCoordinator? eventCoordinator = null)
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
            eventCoordinator ?? new EventCoordinator(),
            new Session("test-session"),
            new Branch("test-session"),
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
            harnessName: "CodingHarness");
    }

    private static AgentRunConfig CreateWorkspaceRunConfig()
    {
        var cwd = Directory.GetCurrentDirectory();
        return new AgentRunConfig
        {
            ContextOverrides = new()
            {
                [AgentWorkspace.ContextKey] = new AgentWorkspace(
                    "default",
                    cwd,
                    [new AgentWorkspaceRoot("default", cwd)])
            }
        };
    }

    private static string FullPath(string relativePath)
        => Path.GetFullPath(relativePath, Directory.GetCurrentDirectory());
}
