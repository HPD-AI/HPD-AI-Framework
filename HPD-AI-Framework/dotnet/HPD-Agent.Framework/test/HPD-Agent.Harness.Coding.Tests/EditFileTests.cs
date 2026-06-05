using HPD.Agent;
using HPD.Agent.ToolHarness.Coding;
using HPD.Agent.Middleware;
using HPD.Events;
using HPD.Events.Core;
using HPDOS.ToolHarnesses.Middleware;
using Microsoft.Extensions.AI;
using System.Text;

namespace HPD.Agent.ToolHarness.Coding.Tests;

[Collection(CurrentDirectoryCollection.Name)]
public sealed class EditFileTests : IDisposable
{
    private readonly string _originalCwd = Directory.GetCurrentDirectory();
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"hpd-edit-file-tests-{Guid.NewGuid():N}");

    public EditFileTests()
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
    public void EditFile_RequiresPermission()
    {
        var method = typeof(CodingToolHarness).GetMethod(
            nameof(CodingToolHarness.EditFile),
            [typeof(string), typeof(IReadOnlyList<FileEditReplacement>), typeof(FunctionExecutionContext)]);

        method.Should().NotBeNull();
        method!.GetCustomAttributes(typeof(RequiresPermissionAttribute), inherit: false)
            .Should().ContainSingle();
    }

    [Fact]
    public async Task EditFile_RejectsInvalidArguments()
    {
        var toolharness = new CodingToolHarness();

        var missingPath = await EditFileTextAsync(CreateAgentContext(), toolharness, " ", "old", "new");
        var same = await EditFileTextAsync(CreateAgentContext(), toolharness, "A.cs", "same", "same");
        var missingEdits = await EditFileTextAsync(CreateAgentContext(), toolharness, "A.cs", []);

        missingPath.Should().Contain("kind=\"invalid_arguments\"");
        same.Should().Contain("OldString and NewString must be different");
        missingEdits.Should().Contain("At least one edit is required.");
    }

    [Fact]
    public async Task EditFile_PropagatesSharedGuardErrors()
    {
        Directory.CreateDirectory("dir");
        await File.WriteAllBytesAsync("binary.bin", [0x01, 0x00, 0x02]);
        await File.WriteAllTextAsync("notebook.ipynb", "{}");
        await using (var stream = new FileStream("large.txt", FileMode.Create, FileAccess.Write))
            stream.SetLength(50L * 1024 * 1024 + 1);

        var toolharness = new CodingToolHarness();

        (await EditFileTextAsync(CreateAgentContext(), toolharness, "dir", "x", "y")).Should().Contain("kind=\"path_is_directory\"");
        (await EditFileTextAsync(CreateAgentContext(), toolharness, "/dev/zero", "x", "y")).Should().Contain("kind=\"blocked_device_path\"");
        (await EditFileTextAsync(CreateAgentContext(), toolharness, "//server/share/file.txt", "x", "y")).Should().Contain("kind=\"windows_unc_path\"");
        (await EditFileTextAsync(CreateAgentContext(), toolharness, "binary.bin", "x", "y")).Should().Contain("kind=\"binary_file\"");
        (await EditFileTextAsync(CreateAgentContext(), toolharness, "notebook.ipynb", "x", "y")).Should().Contain("kind=\"notebook_file\"");
        (await EditFileTextAsync(CreateAgentContext(), toolharness, "large.txt", "x", "y")).Should().Contain("kind=\"file_too_large\"");
    }

    [Fact]
    public async Task EditFile_MissingNonCreatePathReturnsSuggestionWhenPossible()
    {
        await File.WriteAllTextAsync("Program.txt", "class Program {}\n");
        var toolharness = new CodingToolHarness();

        var result = await EditFileTextAsync(CreateAgentContext(), toolharness, "program.cs", "Program", "App");

        result.Should().Contain("kind=\"file_not_found\"");
        result.Should().Contain("Did you mean Program.txt?");
    }

    [Fact]
    public async Task EditFile_CreatesAndFillsOnlyWithSingleEmptyOldString()
    {
        var toolharness = new CodingToolHarness();
        var agentContext = CreateAgentContext();

        var created = await EditFileTextAsync(agentContext, toolharness, "src/New.cs", string.Empty, "class New {}\n");
        await File.WriteAllTextAsync("empty.txt", string.Empty);
        var filled = await EditFileTextAsync(CreateAgentContext(), toolharness, "empty.txt", string.Empty, "filled\n");
        await File.WriteAllTextAsync("nonempty.txt", "x\n");
        var invalid = await EditFileTextAsync(CreateAgentContext(), toolharness, "nonempty.txt", string.Empty, "y\n");

        created.Should().Contain("created=\"true\"");
        File.ReadAllText(Path.Combine("src", "New.cs")).Should().Be("class New {}\n");
        filled.Should().Contain("created=\"false\"");
        File.ReadAllText("empty.txt").Should().Be("filled\n");
        invalid.Should().Contain("kind=\"invalid_empty_old_string\"");
    }

    [Fact]
    public async Task EditFile_ExistingFileRequiresPriorReadAndRejectsStaleRead()
    {
        await File.WriteAllTextAsync("A.cs", "before\n");
        var toolharness = new CodingToolHarness();

        var notRead = await EditFileTextAsync(CreateAgentContext(), toolharness, "A.cs", "before", "after");

        var agentContext = CreateAgentContext();
        await ReadFileTextAsync(agentContext, toolharness, "A.cs");
        await File.WriteAllTextAsync("A.cs", "changed externally\n");
        var stale = await EditFileTextAsync(agentContext, toolharness, "A.cs", "before", "after");

        notRead.Should().Contain("kind=\"not_read\"");
        stale.Should().Contain("kind=\"stale_read\"");
    }

    [Fact]
    public async Task EditFile_RejectsHistoryReducedRead()
    {
        await File.WriteAllTextAsync("A.cs", "before\n");
        var agentContext = CreateAgentContext();
        var toolharness = new CodingToolHarness();
        await ReadFileTextAsync(agentContext, toolharness, "A.cs");
        CreateBeforeFunctionContext(agentContext)
            .UpdateMiddlewareState<CompactionStateData>(state =>
                state.WithCompactionApplied(DateTimeOffset.UtcNow.AddSeconds(1)));

        var result = await EditFileTextAsync(agentContext, toolharness, "A.cs", "before", "after");

        result.Should().Contain("kind=\"history_reduced_read\"");
    }

    [Fact]
    public async Task EditFile_AllowsPartialReadInsideRangeAndRejectsOutsideRange()
    {
        await File.WriteAllTextAsync("A.cs", "one\ntwo\nthree\n");
        var toolharness = new CodingToolHarness();

        var insideContext = CreateAgentContext();
        await ReadFileTextAsync(insideContext, toolharness, "A.cs", offset: 2, limit: 1);
        var inside = await EditFileTextAsync(insideContext, toolharness, "A.cs", "two", "TWO");

        await File.WriteAllTextAsync("A.cs", "one\ntwo\nthree\n");
        var outsideContext = CreateAgentContext();
        await ReadFileTextAsync(outsideContext, toolharness, "A.cs", offset: 2, limit: 1);
        var outside = await EditFileTextAsync(outsideContext, toolharness, "A.cs", "three", "THREE");

        inside.Should().Contain("changed=\"true\"");
        outside.Should().Contain("kind=\"outside_read_range\"");
    }

    [Fact]
    public async Task EditFile_PartialReadStillRejectsDuplicateOrReplaceAllMatchesOutsideRange()
    {
        await File.WriteAllTextAsync("A.cs", "target\nmiddle\ntarget\n");
        var toolharness = new CodingToolHarness();

        var ambiguousContext = CreateAgentContext();
        await ReadFileTextAsync(ambiguousContext, toolharness, "A.cs", offset: 1, limit: 1);
        var ambiguous = await EditFileTextAsync(ambiguousContext, toolharness, "A.cs", "target", "changed");

        var replaceAllContext = CreateAgentContext();
        await ReadFileTextAsync(replaceAllContext, toolharness, "A.cs", offset: 1, limit: 1);
        var replaceAll = await EditFileTextAsync(replaceAllContext, toolharness, "A.cs", "target", "changed", replaceAll: true);

        ambiguous.Should().Contain("kind=\"ambiguous_match\"");
        replaceAll.Should().Contain("kind=\"outside_read_range\"");
        File.ReadAllText("A.cs").Should().Be("target\nmiddle\ntarget\n");
    }

    [Fact]
    public async Task EditFile_ExactAmbiguousReplaceAllAndMultiEditBehaviors()
    {
        await File.WriteAllTextAsync("A.cs", "cat cat dog\n");
        var toolharness = new CodingToolHarness();
        var agentContext = CreateAgentContext();
        await ReadFileTextAsync(agentContext, toolharness, "A.cs");

        var ambiguous = await EditFileTextAsync(agentContext, toolharness, "A.cs", "cat", "fox");
        var replaceAll = await EditFileTextAsync(agentContext, toolharness, "A.cs", "cat", "fox", replaceAll: true);
        var multi = await EditFileTextAsync(agentContext, toolharness, "A.cs",
        [
            new FileEditReplacement { OldString = "fox", NewString = "wolf", ReplaceAll = true },
            new FileEditReplacement { OldString = "dog", NewString = "hound" }
        ]);
        var overlap = await EditFileTextAsync(agentContext, toolharness, "A.cs",
        [
            new FileEditReplacement { OldString = "hound", NewString = "hound pup" },
            new FileEditReplacement { OldString = "pup", NewString = "puppy" }
        ]);

        ambiguous.Should().Contain("kind=\"ambiguous_match\"");
        replaceAll.Should().Contain("replacements=\"2\"");
        multi.Should().Contain("edits=\"2\"");
        File.ReadAllText("A.cs").Should().Be("wolf wolf hound\n");
        overlap.Should().Contain("kind=\"overlapping_multi_edit\"");
    }

    [Fact]
    public async Task EditFile_DeletesFollowingNewlineAndRejectsNoChange()
    {
        await File.WriteAllTextAsync("A.cs", "one\ntwo\nthree\n");
        var toolharness = new CodingToolHarness();
        var agentContext = CreateAgentContext();
        await ReadFileTextAsync(agentContext, toolharness, "A.cs");

        var deleted = await EditFileTextAsync(agentContext, toolharness, "A.cs", "two", string.Empty);
        var noChange = await EditFileTextAsync(agentContext, toolharness, "A.cs", "missing", string.Empty);

        deleted.Should().Contain("changed=\"true\"");
        File.ReadAllText("A.cs").Should().Be("one\nthree\n");
        noChange.Should().Contain("kind=\"no_match\"");
    }

    [Fact]
    public async Task EditFile_HandlesNoOpNewlineAndTrailingNewlineSemantics()
    {
        await File.WriteAllTextAsync("A.cs", "one\ntwo\nthree\n");
        var toolharness = new CodingToolHarness();
        var agentContext = CreateAgentContext();
        await ReadFileTextAsync(agentContext, toolharness, "A.cs");

        var noChange = await EditFileTextAsync(agentContext, toolharness, "A.cs", "two", "two");
        var includesNewline = await EditFileTextAsync(agentContext, toolharness, "A.cs", "two\n", string.Empty);

        await File.WriteAllTextAsync("B.cs", "before");
        var noTrailingNewlineContext = CreateAgentContext();
        await ReadFileTextAsync(noTrailingNewlineContext, toolharness, "B.cs");
        var noTrailingNewline = await EditFileTextAsync(noTrailingNewlineContext, toolharness, "B.cs", "before", "after\n");

        noChange.Should().Contain("kind=\"invalid_arguments\"");
        includesNewline.Should().Contain("changed=\"true\"");
        File.ReadAllText("A.cs").Should().Be("one\nthree\n");
        noTrailingNewline.Should().Contain("changed=\"true\"");
        noTrailingNewline.Should().Contain("kind=\"trailing_newline_preserved\"");
        File.ReadAllText("B.cs").Should().Be("after");
    }

    [Fact]
    public async Task EditFile_RecoversMechanicalOldStringMismatches()
    {
        await File.WriteAllTextAsync("A.cs", "var s = “hello”;\r\nvar xml = <name>ewoof</name>;\r\nvar escaped = \"a\\nb\";\r\n");
        var toolharness = new CodingToolHarness();
        var agentContext = CreateAgentContext();
        await ReadFileTextAsync(agentContext, toolharness, "A.cs");

        var lineEnding = await EditFileTextAsync(agentContext, toolharness, "A.cs", "var s = “hello”;\n", "var s = “hi”;\n");
        var quote = await EditFileTextAsync(agentContext, toolharness, "A.cs", "var s = \"hi\";", "var s = \"bye\";");
        var desanitize = await EditFileTextAsync(agentContext, toolharness, "A.cs", "var xml = <n>ewoof</n>;", "var xml = <name>codex</name>;");
        var escaped = await EditFileTextAsync(agentContext, toolharness, "A.cs", "var escaped = \"a\\\\nb\";", "var escaped = \"a\\nc\";");

        lineEnding.Should().Contain("kind=\"line_ending_normalized\"");
        quote.Should().Contain("kind=\"quote_normalized\"");
        desanitize.Should().Contain("kind=\"desanitized\"");
        escaped.Should().Contain("kind=\"escaped_string_normalized\"");
        File.ReadAllText("A.cs").Should().Contain("“bye”");
        File.ReadAllText("A.cs").Should().Contain("<name>codex</name>");
    }

    [Fact]
    public async Task EditFile_BomHiddenFirstLineAndEscapedDollarRecoveryWorkLiterally()
    {
        await File.WriteAllTextAsync("bom.cs", "first\nsecond\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var toolharness = new CodingToolHarness();
        var bomContext = CreateAgentContext();
        await ReadFileTextAsync(bomContext, toolharness, "bom.cs");

        var bom = await EditFileTextAsync(bomContext, toolharness, "bom.cs", "first", "FIRST");

        await File.WriteAllTextAsync("dollar.cs", "var s = \"$1 $&\";\n");
        var dollarContext = CreateAgentContext();
        await ReadFileTextAsync(dollarContext, toolharness, "dollar.cs");
        var dollar = await EditFileTextAsync(dollarContext, toolharness, "dollar.cs", "var s = \"\\$1 \\$&\";", "var s = \"$& $1\";");

        bom.Should().Contain("kind=\"bom_hidden_first_line\"");
        File.ReadAllBytes("bom.cs")[0..3].Should().Equal(0xEF, 0xBB, 0xBF);
        dollar.Should().Contain("kind=\"escaped_string_normalized\"");
        File.ReadAllText("dollar.cs").Should().Be("var s = \"$& $1\";\n");
    }

    [Fact]
    public async Task EditFile_RecoversTrimmedIndentationAndWhitespaceAnchoredBlocks()
    {
        await File.WriteAllTextAsync("A.cs", "class A\n{\n    void M()\n    {\n        Console.WriteLine(1);\n    }\n}\n");
        var toolharness = new CodingToolHarness();
        var agentContext = CreateAgentContext();
        await ReadFileTextAsync(agentContext, toolharness, "A.cs");

        var trimmed = await EditFileTextAsync(agentContext, toolharness, "A.cs", "  Console.WriteLine(1);  ", "Console.WriteLine(2);");
        var indentation = await EditFileTextAsync(agentContext, toolharness, "A.cs", "void M()\n{\n    Console.WriteLine(2);\n}", "void M()\n{\n    Console.WriteLine(3);\n}");

        await File.WriteAllTextAsync("B.cs", "class B\n{\n    void N()\n    {\n        Console.WriteLine(3);\n    }\n}\n");
        var whitespaceContext = CreateAgentContext();
        await ReadFileTextAsync(whitespaceContext, toolharness, "B.cs");
        var whitespace = await EditFileTextAsync(whitespaceContext, toolharness, "B.cs", "void N()\n{\nConsole.WriteLine(3);\n}", "void N()\n{\nConsole.WriteLine(4);\n}");

        trimmed.Should().Contain("kind=\"trimmed_boundary\"");
        indentation.Should().Contain("kind=\"indentation_only_block\"");
        whitespace.Should().Contain("kind=\"whitespace_only_anchored_block\"");
    }

    [Fact]
    public async Task EditFile_ReplaceAllRecoveryRejectsMixedRecoveredCandidateShapes()
    {
        await File.WriteAllTextAsync("A.cs", "alpha\r\nbeta\nmiddle\nalpha\nbeta\n");
        var toolharness = new CodingToolHarness();
        var agentContext = CreateAgentContext();
        await ReadFileTextAsync(agentContext, toolharness, "A.cs");

        var result = await EditFileTextAsync(
            agentContext,
            toolharness,
            "A.cs",
            "alpha\r\nbeta\r\n",
            "omega\nbeta\n",
            replaceAll: true);

        result.Should().Contain("kind=\"recovery_semantic_difference\"");
        result.Should().Contain("strategy=\"line_ending_normalized\"");
    }

    [Fact]
    public async Task EditFile_NormalizesTrailingWhitespaceExceptMarkdownAndRejectsOmissionPlaceholder()
    {
        await File.WriteAllTextAsync("A.cs", "before\n");
        await File.WriteAllTextAsync("A.md", "before\n");
        var toolharness = new CodingToolHarness();
        var codeContext = CreateAgentContext();
        var markdownContext = CreateAgentContext();
        await ReadFileTextAsync(codeContext, toolharness, "A.cs");
        await ReadFileTextAsync(markdownContext, toolharness, "A.md");

        var code = await EditFileTextAsync(codeContext, toolharness, "A.cs", "before", "after   ");
        var markdown = await EditFileTextAsync(markdownContext, toolharness, "A.md", "before", "after  ");
        await File.WriteAllTextAsync("B.cs", "before\n");
        var omissionContext = CreateAgentContext();
        await ReadFileTextAsync(omissionContext, toolharness, "B.cs");
        var omission = await EditFileTextAsync(omissionContext, toolharness, "B.cs", "before", "// rest of methods ...");

        code.Should().Contain("kind=\"trailing_whitespace\"");
        File.ReadAllText("A.cs").Should().Be("after\n");
        File.ReadAllText("A.md").Should().Be("after  \n");
        markdown.Should().NotContain("trailing_whitespace");
        omission.Should().Contain("kind=\"new_omission_placeholder\"");
    }

    [Fact]
    public async Task EditFile_AllowsExistingOrLiteralLikeOmissionPlaceholderText()
    {
        await File.WriteAllTextAsync("A.cs", "// rest of methods ...\n");
        await File.WriteAllTextAsync("B.cs", "var text = \"rest of methods ...\";\n");
        var toolharness = new CodingToolHarness();
        var existingContext = CreateAgentContext();
        var literalContext = CreateAgentContext();
        await ReadFileTextAsync(existingContext, toolharness, "A.cs");
        await ReadFileTextAsync(literalContext, toolharness, "B.cs");

        var existing = await EditFileTextAsync(existingContext, toolharness, "A.cs", "// rest of methods ...", "// rest of code ...");
        var literal = await EditFileTextAsync(literalContext, toolharness, "B.cs", "\"rest of methods ...\"", "\"rest of code ...\"");

        existing.Should().Contain("changed=\"true\"");
        literal.Should().Contain("changed=\"true\"");
    }

    [Fact]
    public async Task EditFile_EscapesXmlSensitivePathsAndOmitsContentAndDiff()
    {
        await File.WriteAllTextAsync("A&B.cs", "before\n");
        var toolharness = new CodingToolHarness();
        var agentContext = CreateAgentContext();
        await ReadFileTextAsync(agentContext, toolharness, "A&B.cs");

        var result = await EditFileTextAsync(agentContext, toolharness, "A&B.cs", "before", "after");

        result.Should().Contain("A&amp;B.cs");
        result.Should().NotContain("before\n");
        result.Should().NotContain("after\n");
        result.Should().NotContain("@@");
    }

    [Fact]
    public async Task EditFile_EmitsMetadataAndFileEditAppliedEvent()
    {
        await File.WriteAllTextAsync("A.cs", "before\n");
        using var coordinator = new EventCoordinator();
        var events = new List<FileEditAppliedEvent>();
        using var subscription = coordinator.Subscribe<FileEditAppliedEvent>(evt =>
        {
            events.Add(evt);
            return ValueTask.CompletedTask;
        });
        var agentContext = CreateAgentContext(coordinator);
        var toolharness = new CodingToolHarness();
        await ReadFileTextAsync(agentContext, toolharness, "A.cs");

        var result = await EditFileWithContextAsync(agentContext, toolharness, "A.cs", "before", "after");

        ResultToString(result.Result).Should().Contain("event_emitted=\"true\"");
        result.Metadata.TryGet<CodingFileMutationSnapshot>(
            CodingToolMetadataKeys.FileMutationSnapshot,
            out var mutation).Should().BeTrue();
        mutation!.Kind.Should().Be(CodingFileMutationKind.Changed);
        mutation.Text.Should().Be("after\n");
        await WaitUntilAsync(() => events.Count == 1);
        var editEvent = events.Should().ContainSingle().Subject;
        editEvent.EditCount.Should().Be(1);
        editEvent.ReplacementCount.Should().Be(1);
        editEvent.Replacements.Should().ContainSingle().Which.MatchStrategy.Should().Be("exact");
        editEvent.Before.Text.Should().Be("before\n");
        editEvent.After.Text.Should().Be("after\n");
        editEvent.TextEdits.Should().ContainSingle();
    }

    [Fact]
    public async Task EditFile_EventEmissionFailureDoesNotFailSuccessfulEdit()
    {
        await File.WriteAllTextAsync("A.cs", "before\n");
        var agentContext = CreateAgentContext(new ThrowingEventCoordinator());
        var toolharness = new CodingToolHarness();
        await ReadFileTextAsync(agentContext, toolharness, "A.cs");

        var result = await EditFileWithContextAsync(agentContext, toolharness, "A.cs", "before", "after");

        ResultToString(result.Result).Should().Contain("changed=\"true\"");
        ResultToString(result.Result).Should().Contain("event_emitted=\"false\"");
        File.ReadAllText("A.cs").Should().Be("after\n");
    }

    [Fact]
    public void EditFile_DoesNotUseForbiddenIntegrationPaths()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Agent.Harness/HPD-Agent.Harness.Coding/CodingHarness.EditFile.cs"));

        source.Should().NotContain("LanguageServer");
        source.Should().NotContain("LSP");
        source.Should().NotContain("Process.Start");
        source.Should().NotContain("ExecuteCommand");
        source.Should().NotContain("git apply");
        source.Should().NotContain("FileSystemWatcher");
    }

    [Fact]
    public async Task EditFile_FollowupEditAfterSuccessfulEditDoesNotFailStale()
    {
        await File.WriteAllTextAsync("A.cs", "one two\n");
        var agentContext = CreateAgentContext();
        var toolharness = new CodingToolHarness();
        await ReadFileTextAsync(agentContext, toolharness, "A.cs");

        var first = await EditFileTextAsync(agentContext, toolharness, "A.cs", "one", "ONE");
        var second = await EditFileTextAsync(agentContext, toolharness, "A.cs", "two", "TWO");

        first.Should().Contain("changed=\"true\"");
        second.Should().Contain("changed=\"true\"");
        File.ReadAllText("A.cs").Should().Be("ONE TWO\n");
    }

    private static async Task<string> ReadFileTextAsync(
        AgentContext agentContext,
        CodingToolHarness toolharness,
        string path,
        int offset = 1,
        int limit = 2000)
    {
        var beforeContext = CreateBeforeFunctionContext(agentContext);
        var request = CreateFunctionRequest(agentContext, beforeContext, nameof(CodingToolHarness.ReadFile), new Dictionary<string, object?>
        {
            ["path"] = path,
            ["offset"] = offset,
            ["limit"] = limit
        });

        var functionContext = new FunctionExecutionContext(beforeContext, request);
        var result = await toolharness.ReadFile(path, functionContext, offset, limit);

        var afterContext = agentContext.AsAfterFunction(
            function: null,
            callId: beforeContext.FunctionCallId,
            result: result,
            exception: null,
            runConfig: beforeContext.RunConfig,
            toolharnessName: "CodingToolHarness",
            resultMetadata: request.ResultMetadata);

        await new EnvironmentContextMiddleware().AfterFunctionAsync(afterContext, CancellationToken.None);
        return ResultToString(afterContext.Result);
    }

    private static Task<string> EditFileTextAsync(
        AgentContext agentContext,
        CodingToolHarness toolharness,
        string path,
        string oldString,
        string newString,
        bool replaceAll = false)
        => EditFileTextAsync(agentContext, toolharness, path, [new FileEditReplacement { OldString = oldString, NewString = newString, ReplaceAll = replaceAll }]);

    private static async Task<string> EditFileTextAsync(
        AgentContext agentContext,
        CodingToolHarness toolharness,
        string path,
        IReadOnlyList<FileEditReplacement> edits)
        => ResultToString((await EditFileWithContextAsync(agentContext, toolharness, path, edits)).Result);

    private static Task<(object? Result, ToolResultMetadata Metadata)> EditFileWithContextAsync(
        AgentContext agentContext,
        CodingToolHarness toolharness,
        string path,
        string oldString,
        string newString,
        bool replaceAll = false)
        => EditFileWithContextAsync(agentContext, toolharness, path, [new FileEditReplacement { OldString = oldString, NewString = newString, ReplaceAll = replaceAll }]);

    private static async Task<(object? Result, ToolResultMetadata Metadata)> EditFileWithContextAsync(
        AgentContext agentContext,
        CodingToolHarness toolharness,
        string path,
        IReadOnlyList<FileEditReplacement> edits)
    {
        var beforeContext = CreateBeforeFunctionContext(agentContext);
        var request = CreateFunctionRequest(agentContext, beforeContext, nameof(CodingToolHarness.EditFile), new Dictionary<string, object?>
        {
            ["path"] = path,
            ["edits"] = edits
        });

        var functionContext = new FunctionExecutionContext(beforeContext, request);
        var result = await toolharness.EditFile(path, edits, functionContext);

        var afterContext = agentContext.AsAfterFunction(
            function: null,
            callId: beforeContext.FunctionCallId,
            result: result,
            exception: null,
            runConfig: beforeContext.RunConfig,
            toolharnessName: "CodingToolHarness",
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

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!condition() && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(10);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
            directory = directory.Parent;

        return directory?.FullName ?? AppContext.BaseDirectory;
    }

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
            toolharnessName: "CodingToolHarness");
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

    private sealed class ThrowingEventCoordinator : IEventCoordinator
    {
        private readonly EventCoordinator _inner = new();

        public IEventFlowRegistry EventFlows => _inner.EventFlows;

        public void Emit(Event evt) => throw new InvalidOperationException("boom");

        public ValueTask EmitAsync(Event evt, CancellationToken ct = default)
            => throw new InvalidOperationException("boom");

        public IDisposable Subscribe<TEvent>(Func<TEvent, ValueTask> handler, EventSubscriptionOptions? options = null)
            where TEvent : Event
            => _inner.Subscribe(handler, options);

        public IDisposable SubscribeAny(Func<Event, ValueTask> handler, EventSubscriptionOptions? options = null)
            => _inner.SubscribeAny(handler, options);

        public EventInbox<TEvent> CreateInbox<TEvent>(EventInboxOptions? options = null)
            where TEvent : Event
            => _inner.CreateInbox<TEvent>(options);

        public EventInbox<Event> CreateChannelInbox(EventChannel channel, EventInboxOptions? options = null)
            => _inner.CreateChannelInbox(channel, options);

        public void SetParent(IEventCoordinator parent) => _inner.SetParent(parent);

        public Task<TResponse> RequestAsync<TRequest, TResponse>(TRequest request, TimeSpan timeout, CancellationToken ct = default)
            where TRequest : Event, IBidirectionalEvent
            where TResponse : Event
            => _inner.RequestAsync<TRequest, TResponse>(request, timeout, ct);

        public void Respond(string requestId, Event response) => _inner.Respond(requestId, response);

        public bool TryRespond(string requestId, Event response) => _inner.TryRespond(requestId, response);

        public EventCoordinatorStats GetStats() => _inner.GetStats();
    }
}
