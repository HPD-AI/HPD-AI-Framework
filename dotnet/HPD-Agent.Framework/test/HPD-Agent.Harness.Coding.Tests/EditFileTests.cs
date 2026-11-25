using HPD.Agent;
using HPD.Agent.Sandbox;
using HPD.Agent.ToolHarness.Coding;
using HPD.Agent.Middleware;
using HPD.Events;
using HPD.Events.Core;
using HPDOS.ToolHarnesses.Middleware;
using Microsoft.Extensions.AI;
using System.Collections.Immutable;
using System.Diagnostics;
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
        var toolharness = CodingHarnessTestFactory.Create();

        var missingPath = await EditFileTextAsync(CreateAgentContext(), toolharness, " ", "old", "new");
        var same = await EditFileTextAsync(CreateAgentContext(), toolharness, "A.cs", "same", "same");
        var missingEdits = await EditFileTextAsync(CreateAgentContext(), toolharness, "A.cs", []);

        missingPath.Should().Contain("kind=\"invalid_arguments\"");
        same.Should().Contain("OldString and NewString must be different");
        missingEdits.Should().Contain("At least one edit is required.");
    }

    [Fact]
    public async Task EditFile_UsesConfiguredEditCount()
    {
        await File.WriteAllTextAsync("A.cs", "one two\n");
        var harness = CodingHarnessTestFactory.Create(
            editFileConfig: new EditFileConfig { MaxEdits = 1 });
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "A.cs");

        var result = await EditFileTextAsync(context, harness, "A.cs",
        [
            new FileEditReplacement("one", "ONE"),
            new FileEditReplacement("two", "TWO")
        ]);

        result.Should().Contain("kind=\"invalid_arguments\"");
        result.Should().Contain("Maximum is 1");
        File.ReadAllText("A.cs").Should().Be("one two\n");
    }

    [Fact]
    public async Task EditFile_ValidatesDirectHarnessConfiguration()
    {
        var harness = CodingHarnessTestFactory.Create(
            editFileConfig: new EditFileConfig { MaxEdits = 0 });

        var act = () => EditFileTextAsync(
            CreateAgentContext(),
            harness,
            "A.cs",
            "old",
            "new");

        var error = await act.Should()
            .ThrowAsync<AgentRunConfigurationException>();
        error.Which.Code.Should().Be("CodingEditSettingsInvalid");
    }

    [Fact]
    public async Task EditFile_UsesConfiguredReplacementCandidateLimit()
    {
        await File.WriteAllTextAsync("A.cs", "aaa\n");
        var harness = CodingHarnessTestFactory.Create(
            editFileConfig: new EditFileConfig
            {
                MaxReplacementCandidates = 2
            });
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "A.cs");

        var result = await EditFileTextAsync(
            context,
            harness,
            "A.cs",
            "a",
            "b",
            replaceAll: true);

        result.Should().Contain("kind=\"replacement_limit_exceeded\"");
        result.Should().Contain("selected at least 3 occurrences");
        File.ReadAllText("A.cs").Should().Be("aaa\n");
    }

    [Fact]
    public async Task EditFile_RecoveryPropagatesConfiguredReplacementCandidateLimit()
    {
        var original = string.Concat(
            Enumerable.Repeat(
                "    target()\n    {\n        work();\n    }\n",
                3));
        await File.WriteAllTextAsync("A.cs", original);
        var harness = CodingHarnessTestFactory.Create(
            editFileConfig: new EditFileConfig
            {
                MaxRecoveryCandidates = 20,
                MaxReplacementCandidates = 2
            });
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "A.cs");

        var result = await EditFileTextAsync(
            context,
            harness,
            "A.cs",
            "target()\n{\n    work();\n}",
            "changed()\n{\n    next();\n}",
            replaceAll: true);

        result.Should().Contain("kind=\"replacement_limit_exceeded\"");
        result.Should().Contain("selected at least 3 occurrences");
        result.Should().Contain("strategy=\"indentation_only_block\"");
        File.ReadAllText("A.cs").Should().Be(original);
    }

    [Fact]
    public async Task EditFile_UsesConfiguredRecoveryStrategiesAndMap()
    {
        await File.WriteAllTextAsync("quote.txt", "var s = “hello”;\n");
        await File.WriteAllTextAsync("token.txt", "TOKEN marker\n");
        var quoteHarness = CodingHarnessTestFactory.Create(
            editFileConfig: new EditFileConfig
            {
                Recovery = new EditRecoveryConfig
                {
                    QuoteNormalization = false
                }
            });
        var quoteContext = CreateAgentContext();
        await ReadFileTextAsync(quoteContext, quoteHarness, "quote.txt");

        var quote = await EditFileTextAsync(
            quoteContext,
            quoteHarness,
            "quote.txt",
            "var s = \"hello\";",
            "var s = \"bye\";");

        var tokenHarness = CodingHarnessTestFactory.Create(
            editFileConfig: new EditFileConfig
            {
                Recovery = new EditRecoveryConfig
                {
                    DesanitizationMap = ImmutableDictionary
                        .Create<string, string>(StringComparer.Ordinal)
                        .Add("token", "TOKEN")
                }
            });
        var tokenContext = CreateAgentContext();
        await ReadFileTextAsync(tokenContext, tokenHarness, "token.txt");
        var token = await EditFileTextAsync(
            tokenContext,
            tokenHarness,
            "token.txt",
            "token marker",
            "changed marker");

        quote.Should().Contain("kind=\"no_match\"");
        File.ReadAllText("quote.txt").Should().Be("var s = “hello”;\n");
        token.Should().Contain("kind=\"desanitized\"");
        File.ReadAllText("token.txt").Should().Be("changed marker\n");
    }

    [Fact]
    public async Task EditFile_UsesConfiguredMutableFileLimit()
    {
        await File.WriteAllTextAsync("A.cs", "before\n");
        var harness = CodingHarnessTestFactory.Create(
            fileMutationConfig: new FileMutationConfig
            {
                MaxMutableFileBytes = 4
            });

        var result = await EditFileTextAsync(
            CreateAgentContext(),
            harness,
            "A.cs",
            "before",
            "after");

        result.Should().Contain("kind=\"file_too_large\"");
        File.ReadAllText("A.cs").Should().Be("before\n");
    }

    [Fact]
    public async Task EditFile_PropagatesSharedGuardErrors()
    {
        Directory.CreateDirectory("dir");
        await File.WriteAllBytesAsync("binary.bin", [0x01, 0x00, 0x02]);
        await File.WriteAllTextAsync("notebook.ipynb", "{}");
        await using (var stream = new FileStream("large.txt", FileMode.Create, FileAccess.Write))
            stream.SetLength(50L * 1024 * 1024 + 1);

        var toolharness = CodingHarnessTestFactory.Create();

        (await EditFileTextAsync(CreateAgentContext(), toolharness, "dir", "x", "y")).Should().Contain("kind=\"path_is_directory\"");
        var unresolved = await EditFileTextAsync(CreateAgentContext(), toolharness, "/dev/zero", "x", "y");
        unresolved.Should().Contain("Absolute execution paths are not model-facing workspace addresses.");
        unresolved.Should().Contain("path=\"/dev/zero\"");
        (await EditFileTextAsync(CreateAgentContext(), toolharness, "//server/share/file.txt", "x", "y")).Should().Contain("UNC and network-style paths are not workspace addresses.");
        (await EditFileTextAsync(CreateAgentContext(), toolharness, "binary.bin", "x", "y")).Should().Contain("kind=\"binary_file\"");
        var notebook = await EditFileTextAsync(CreateAgentContext(), toolharness, "./notebook.ipynb", "x", "y");
        notebook.Should().Contain("kind=\"notebook_file\"");
        notebook.Should().Contain("path=\"notebook.ipynb\"");
        (await EditFileTextAsync(CreateAgentContext(), toolharness, "large.txt", "x", "y")).Should().Contain("kind=\"file_too_large\"");
    }

    [Fact]
    public async Task EditFile_MissingNonCreatePathIsReportedWithoutHostInspection()
    {
        await File.WriteAllTextAsync("Program.txt", "class Program {}\n");
        var toolharness = CodingHarnessTestFactory.Create();

        var result = await EditFileTextAsync(CreateAgentContext(), toolharness, "program.cs", "Program", "App");

        result.Should().Contain("kind=\"file_not_found\"");
        result.Should().Contain("File does not exist.");
    }

    [Fact]
    public async Task EditFile_CreatesAndFillsOnlyWithSingleEmptyOldString()
    {
        var toolharness = CodingHarnessTestFactory.Create();
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
    public async Task EditFile_MissingFileReportsInvalidEmptyEditIndex()
    {
        var harness = CodingHarnessTestFactory.Create();

        var result = await EditFileTextAsync(CreateAgentContext(), harness, "missing.txt",
        [
            new FileEditReplacement(string.Empty, "first"),
            new FileEditReplacement("second", "changed")
        ]);

        result.Should().Contain("kind=\"invalid_empty_old_string\"");
        result.Should().Contain("edit 1");
        File.Exists("missing.txt").Should().BeFalse();
    }

    [Fact]
    public async Task EditFile_RejectsOmissionPlaceholderDuringCreateAndFill()
    {
        var harness = CodingHarnessTestFactory.Create();

        var create = await EditFileTextAsync(
            CreateAgentContext(),
            harness,
            "new.txt",
            string.Empty,
            "// rest of methods ...");
        await File.WriteAllTextAsync("empty.txt", string.Empty);
        var fill = await EditFileTextAsync(
            CreateAgentContext(),
            harness,
            "empty.txt",
            string.Empty,
            "// rest of methods ...");

        create.Should().Contain("kind=\"new_omission_placeholder\"");
        fill.Should().Contain("kind=\"new_omission_placeholder\"");
        File.Exists("new.txt").Should().BeFalse();
        File.ReadAllText("empty.txt").Should().BeEmpty();
    }

    [Fact]
    public async Task EditFile_ExistingFileRequiresPriorReadAndRejectsStaleRead()
    {
        await File.WriteAllTextAsync("A.cs", "before\n");
        var toolharness = CodingHarnessTestFactory.Create();

        var notRead = await EditFileTextAsync(CreateAgentContext(), toolharness, "A.cs", "before", "after");

        var agentContext = CreateAgentContext();
        await ReadFileTextAsync(agentContext, toolharness, "A.cs");
        await File.WriteAllTextAsync("A.cs", "changed externally\n");
        var stale = await EditFileTextAsync(agentContext, toolharness, "A.cs", "before", "after");

        notRead.Should().Contain("kind=\"not_read\"");
        stale.Should().Contain("kind=\"stale_read\"");
    }

    [Fact]
    public async Task EditFile_RejectsFileDeletedBeforeFinalMutationSnapshot()
    {
        await File.WriteAllTextAsync("A.cs", "before\n");
        var toolharness = CodingHarnessTestFactory.Create();
        var workspace = TestRuntimeWorkspace.Create(
            Directory.GetCurrentDirectory(),
            rootId: "default");
        var context = CreateAgentContext(workspace: workspace);
        await ReadFileTextAsync(context, toolharness, "A.cs");
        context.RuntimeCapabilities.Set(
            TestRuntimeWorkspace.WithMutationBeforeTextRead(
                workspace,
                readNumber: 2,
                () => File.Delete("A.cs")));

        var result = await EditFileTextAsync(
            context,
            toolharness,
            "A.cs",
            "before",
            "after");

        result.Should().Contain("kind=\"stale_read\"");
        File.Exists("A.cs").Should().BeFalse();
    }

    [Fact]
    public async Task EditFile_RejectsFileDeletedAfterReadBeforeToolValidation()
    {
        await File.WriteAllTextAsync("A.cs", "before\n");
        var toolharness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, toolharness, "A.cs");
        File.Delete("A.cs");

        var result = await EditFileTextAsync(
            context,
            toolharness,
            "A.cs",
            "before",
            "after");

        result.Should().Contain("kind=\"stale_read\"");
        File.Exists("A.cs").Should().BeFalse();
    }

    [Fact]
    public async Task EditFile_RejectsFileEmptiedBeforeFinalMutationSnapshot()
    {
        await File.WriteAllTextAsync("A.cs", "before\n");
        var toolharness = CodingHarnessTestFactory.Create();
        var workspace = TestRuntimeWorkspace.Create(
            Directory.GetCurrentDirectory(),
            rootId: "default");
        var context = CreateAgentContext(workspace: workspace);
        await ReadFileTextAsync(context, toolharness, "A.cs");
        context.RuntimeCapabilities.Set(
            TestRuntimeWorkspace.WithMutationBeforeTextRead(
                workspace,
                readNumber: 2,
                () => File.WriteAllText("A.cs", string.Empty)));

        var result = await EditFileTextAsync(
            context,
            toolharness,
            "A.cs",
            "before",
            "after");

        result.Should().Contain("kind=\"stale_read\"");
        File.ReadAllText("A.cs").Should().BeEmpty();
    }

    [Fact]
    public async Task EditFile_RejectsCreationWhenFileAppearsBeforeFinalMutationSnapshot()
    {
        var harness = CodingHarnessTestFactory.Create();
        var workspace = TestRuntimeWorkspace.Create(
            Directory.GetCurrentDirectory(),
            rootId: "default");
        var context = CreateAgentContext(workspace: workspace);
        context.RuntimeCapabilities.Set(
            TestRuntimeWorkspace.WithMutationBeforeInfo(
                workspace,
                readNumber: 2,
                () => File.WriteAllText("created.txt", "external\n")));

        var result = await EditFileTextAsync(
            context,
            harness,
            "created.txt",
            string.Empty,
            "agent\n");

        result.Should().Contain("kind=\"stale_read\"");
        File.ReadAllText("created.txt").Should().Be("external\n");
    }

    [Fact]
    public async Task EditFile_RejectsChangedContentWithSameSizeAndTimestamp()
    {
        await File.WriteAllTextAsync("A.cs", "before\n");
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "A.cs");
        var originalTimestamp = File.GetLastWriteTimeUtc("A.cs");

        await File.WriteAllTextAsync("A.cs", "after!\n");
        File.SetLastWriteTimeUtc("A.cs", originalTimestamp);
        var result = await EditFileTextAsync(
            context,
            harness,
            "A.cs",
            "after!",
            "changed");

        result.Should().Contain("kind=\"stale_read\"");
        File.ReadAllText("A.cs").Should().Be("after!\n");
    }

    [Fact]
    public async Task EditFile_AllowsPartialReadInsideRangeAndRejectsOutsideRange()
    {
        await File.WriteAllTextAsync("A.cs", "one\ntwo\nthree\n");
        var toolharness = CodingHarnessTestFactory.Create();

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
        var toolharness = CodingHarnessTestFactory.Create();

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
        var toolharness = CodingHarnessTestFactory.Create();
        var agentContext = CreateAgentContext();
        await ReadFileTextAsync(agentContext, toolharness, "A.cs");

        var ambiguous = await EditFileTextAsync(agentContext, toolharness, "A.cs", "cat", "fox");
        var replaceAll = await EditFileTextAsync(agentContext, toolharness, "A.cs", "cat", "fox", replaceAll: true);
        var multi = await EditFileTextAsync(agentContext, toolharness, "A.cs",
        [
            new FileEditReplacement("fox", "wolf", ReplaceAll: true),
            new FileEditReplacement("dog", "hound")
        ]);
        var overlap = await EditFileTextAsync(agentContext, toolharness, "A.cs",
        [
            new FileEditReplacement("hound", "hound pup"),
            new FileEditReplacement("pup", "puppy")
        ]);

        ambiguous.Should().Contain("kind=\"ambiguous_match\"");
        replaceAll.Should().Contain("replacements=\"2\"");
        multi.Should().Contain("edits=\"2\"");
        File.ReadAllText("A.cs").Should().Be("wolf wolf hound\n");
        overlap.Should().Contain("kind=\"overlapping_multi_edit\"");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EditFile_RejectsOverlappingOccurrences(bool replaceAll)
    {
        const string original = "ababa\n";
        await File.WriteAllTextAsync("A.cs", original);
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "A.cs");

        var result = await EditFileTextAsync(
            context,
            harness,
            "A.cs",
            "aba",
            "x",
            replaceAll);

        result.Should().Contain("kind=\"ambiguous_match\"");
        File.ReadAllText("A.cs").Should().Be(original);
    }

    [Fact]
    public async Task EditFile_RejectsCandidateSpanningOriginalAndGeneratedContent()
    {
        const string original = "AAABBB";
        await File.WriteAllTextAsync("A.cs", original);
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "A.cs");

        var result = await EditFileTextAsync(context, harness, "A.cs",
        [
            new FileEditReplacement("BBB", "ZZZ"),
            new FileEditReplacement("AAAZZZ", "OK")
        ]);

        result.Should().Contain("kind=\"overlapping_multi_edit\"");
        File.ReadAllText("A.cs").Should().Be(original);
    }

    [Fact]
    public async Task EditFile_AllowsButReportsDeletionCreatedContinuity()
    {
        await File.WriteAllTextAsync("A.cs", "AAAXXXBBB");
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "A.cs");

        var result = await EditFileTextAsync(context, harness, "A.cs",
        [
            new FileEditReplacement("XXX", string.Empty),
            new FileEditReplacement("AAABBB", "OK")
        ]);

        result.Should().Contain("kind=\"source_discontinuity\"");
        result.Should().Contain("count=\"1\"");
        File.ReadAllText("A.cs").Should().Be("OK");
    }

    [Fact]
    public async Task EditFile_RejectsExactReplaceAllAboveCandidateLimit()
    {
        var original = new string('x', 20_001);
        await File.WriteAllTextAsync("A.cs", original);
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "A.cs");

        var result = await EditFileTextAsync(
            context,
            harness,
            "A.cs",
            "x",
            "y",
            replaceAll: true);

        result.Should().Contain("kind=\"replacement_limit_exceeded\"");
        File.ReadAllText("A.cs").Should().Be(original);
    }

    [Fact]
    public async Task EditFile_DeletesFollowingNewlineAndRejectsNoChange()
    {
        await File.WriteAllTextAsync("A.cs", "one\ntwo\nthree\n");
        var toolharness = CodingHarnessTestFactory.Create();
        var agentContext = CreateAgentContext();
        await ReadFileTextAsync(agentContext, toolharness, "A.cs");

        var deleted = await EditFileTextAsync(agentContext, toolharness, "A.cs", "two", string.Empty);
        var noChange = await EditFileTextAsync(agentContext, toolharness, "A.cs", "missing", string.Empty);

        deleted.Should().Contain("changed=\"true\"");
        File.ReadAllText("A.cs").Should().Be("one\nthree\n");
        noChange.Should().Contain("kind=\"no_match\"");
    }

    [Fact]
    public async Task EditFile_DeletionDoesNotConsumeNewlineAfterMidLineMatch()
    {
        await File.WriteAllTextAsync("A.cs", "int x = 1;\nfoo\n");
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "A.cs");

        var result = await EditFileTextAsync(
            context,
            harness,
            "A.cs",
            "x = 1;",
            string.Empty);

        result.Should().Contain("changed=\"true\"");
        File.ReadAllText("A.cs").Should().Be("int \nfoo\n");
    }

    [Fact]
    public async Task EditFile_ReplaceAllDeletionConsumesEachLineEndingStyle()
    {
        await File.WriteAllTextAsync("mixed.txt", "drop\r\nkeep\ndrop\nkeep\r\n");
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "mixed.txt");

        var result = await EditFileTextAsync(
            context,
            harness,
            "mixed.txt",
            "drop",
            string.Empty,
            replaceAll: true);

        result.Should().Contain("replacements=\"2\"");
        File.ReadAllText("mixed.txt").Should().Be("keep\nkeep\r\n");
    }

    [Fact]
    public async Task EditFile_DeletionKeepsWholeLineAndMidLineMatchesAmbiguous()
    {
        const string original = "x = 1;\ntext x = 1;\n";
        await File.WriteAllTextAsync("A.cs", original);
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "A.cs");

        var result = await EditFileTextAsync(
            context,
            harness,
            "A.cs",
            "x = 1;",
            string.Empty);

        result.Should().Contain("kind=\"ambiguous_match\"");
        File.ReadAllText("A.cs").Should().Be(original);
    }

    [Fact]
    public async Task EditFile_HandlesNoOpNewlineAndTrailingNewlineSemantics()
    {
        await File.WriteAllTextAsync("A.cs", "one\ntwo\nthree\n");
        var toolharness = CodingHarnessTestFactory.Create();
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
    public async Task EditFile_RejectsMixedInteriorAndEofReplacementNewlinePolicy()
    {
        const string original = "target\ntarget";
        await File.WriteAllTextAsync("A.cs", original);
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "A.cs");

        var result = await EditFileTextAsync(
            context,
            harness,
            "A.cs",
            "target",
            "changed\n",
            replaceAll: true);

        result.Should().Contain("kind=\"invalid_arguments\"");
        result.Should().Contain("combines an EOF match with interior matches");
        File.ReadAllText("A.cs").Should().Be(original);
    }

    [Fact]
    public async Task EditFile_RecoversMechanicalOldStringMismatches()
    {
        await File.WriteAllTextAsync("A.cs", "var s = “hello”;\r\nvar xml = <function_results>ewoof</function_results>;\r\nvar escaped = \"a\\nb\";\r\n");
        var toolharness = CodingHarnessTestFactory.Create();
        var agentContext = CreateAgentContext();
        await ReadFileTextAsync(agentContext, toolharness, "A.cs");

        var lineEnding = await EditFileTextAsync(agentContext, toolharness, "A.cs", "var s = “hello”;\n", "var s = “hi”;\n");
        var quote = await EditFileTextAsync(agentContext, toolharness, "A.cs", "var s = \"hi\";", "var s = \"bye\";");
        var desanitize = await EditFileTextAsync(agentContext, toolharness, "A.cs", "var xml = <fnr>ewoof</function_results>;", "var xml = <function_results>codex</function_results>;");
        var escaped = await EditFileTextAsync(agentContext, toolharness, "A.cs", "var escaped = \"a\\\\nb\";", "var escaped = \"a\\nc\";");

        lineEnding.Should().Contain("kind=\"line_ending_normalized\"");
        quote.Should().Contain("kind=\"quote_normalized\"");
        desanitize.Should().Contain("kind=\"desanitized\"");
        escaped.Should().Contain("kind=\"escaped_string_normalized\"");
        File.ReadAllText("A.cs").Should().Contain("\"bye\"");
        File.ReadAllText("A.cs").Should().Contain("<function_results>codex</function_results>");
    }

    [Fact]
    public async Task EditFile_BomHiddenFirstLineAndEscapedDollarRecoveryWorkLiterally()
    {
        await File.WriteAllTextAsync("bom.cs", "first\nsecond\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var toolharness = CodingHarnessTestFactory.Create();
        var bomContext = CreateAgentContext();
        await ReadFileTextAsync(bomContext, toolharness, "bom.cs");

        var bom = await EditFileTextAsync(bomContext, toolharness, "bom.cs", "first", "FIRST");

        await File.WriteAllTextAsync("dollar.cs", "var s = \"$1 $&\";\n");
        var dollarContext = CreateAgentContext();
        await ReadFileTextAsync(dollarContext, toolharness, "dollar.cs");
        var dollar = await EditFileTextAsync(dollarContext, toolharness, "dollar.cs", "var s = \"\\$1 \\$&\";", "var s = \"$& $1\";");

        bom.Should().Contain("kind=\"exact\"");
        File.ReadAllBytes("bom.cs")[0..3].Should().NotEqual([0xEF, 0xBB, 0xBF]);
        dollar.Should().NotContain("<error");
        File.ReadAllText("dollar.cs").Should().Be("var s = \"$& $1\";\n");
    }

    [Fact]
    public async Task EditFile_RecoversTrimmedBoundaryAndIndentationBlocks()
    {
        await File.WriteAllTextAsync("A.cs", "class A\n{\n    void M()\n    {\n        Console.WriteLine(1);\n    }\n}\n");
        var toolharness = CodingHarnessTestFactory.Create();
        var agentContext = CreateAgentContext();
        await ReadFileTextAsync(agentContext, toolharness, "A.cs");

        var trimmed = await EditFileTextAsync(agentContext, toolharness, "A.cs", "  Console.WriteLine(1);  ", "Console.WriteLine(2);");
        var indentation = await EditFileTextAsync(agentContext, toolharness, "A.cs", "void M()\n{\n    Console.WriteLine(2);\n}", "void M()\n{\n    Console.WriteLine(3);\n}");

        trimmed.Should().Contain("kind=\"trimmed_boundary\"");
        indentation.Should().Contain("kind=\"indentation_only_block\"");
        File.ReadAllText("A.cs").Should().Contain("        Console.WriteLine(3);");
    }

    [Fact]
    public async Task EditFile_TrimmedBoundaryAlsoTrimsReplacementContext()
    {
        await File.WriteAllTextAsync("A.cs", "fooBar(123)\n");
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "A.cs");

        var result = await EditFileTextAsync(
            context,
            harness,
            "A.cs",
            "\n fooBar(123)",
            "\n barBaz(456)");

        result.Should().Contain("kind=\"trimmed_boundary\"");
        result.Should().Contain("kind=\"replacement_boundary_trimmed\"");
        File.ReadAllText("A.cs").Should().Be("barBaz(456)\n");
    }

    [Fact]
    public async Task EditFile_RejectsIndentationRecoveryWhenReplacementIndentDiffers()
    {
        await File.WriteAllTextAsync("A.cs", "    void M()\n    {\n        Work();\n    }\n");
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "A.cs");

        var result = await EditFileTextAsync(
            context,
            harness,
            "A.cs",
            "  void M()\n  {\n      Work();\n  }",
            "void Changed()\n{\n    Next();\n}");

        result.Should().Contain("kind=\"recovery_semantic_difference\"");
        File.ReadAllText("A.cs").Should().Be("    void M()\n    {\n        Work();\n    }\n");
    }

    [Fact]
    public async Task EditFile_DoesNotIgnoreWhitespaceInsideStringLiteral()
    {
        const string original =
            "header\n    setText(\"John Smith\");\n    finish();\nfooter\n";
        await File.WriteAllTextAsync("A.cs", original);
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "A.cs");

        var result = await EditFileTextAsync(
            context,
            harness,
            "A.cs",
            "setText(\"JohnSmith\");\n    finish();",
            "setText(\"Changed\");\n    finish();");

        result.Should().Contain("kind=\"no_match\"");
        File.ReadAllText("A.cs").Should().Be(original);
    }

    [Fact]
    public async Task EditFile_IndentationIgnoresGeneratedCandidateWhenRebasing()
    {
        await File.WriteAllTextAsync(
            "A.cs",
            "header\n    void Target()\n    {\n        Work();\n    }\nfooter\n");
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "A.cs");

        var result = await EditFileTextAsync(context, harness, "A.cs",
        [
            new FileEditReplacement(
                "header",
                "header\n  void Target()\n  {\n      Work();\n  }"),
            new FileEditReplacement(
                "void Target()\n{\n    Work();\n}",
                "void Changed()\n{\n    Next();\n}")
        ]);

        result.Should().Contain("kind=\"generated_matches_skipped\"");
        result.Should().Contain("kind=\"replacement_indentation_rebased\"");
        result.Should().Contain("kind=\"replacement_line_ending_preserved\"");
        File.ReadAllText("A.cs").Should().Be(
            "header\n  void Target()\n  {\n      Work();\n  }\n    void Changed()\n    {\n        Next();\n    }\nfooter\n");
    }

    [Fact]
    public async Task EditFile_IndentationRejectsOutsideCandidateBeforeRebasing()
    {
        const string original =
            "header\n    void Target()\n    {\n        Work();\n    }\nfooter\n";
        await File.WriteAllTextAsync("A.cs", original);
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "A.cs", offset: 1, limit: 1);

        var result = await EditFileTextAsync(
            context,
            harness,
            "A.cs",
            "  void Target()\n  {\n      Work();\n  }",
            "void Changed()\n{\n    Next();\n}");

        result.Should().Contain("kind=\"recovery_outside_read_range\"");
        File.ReadAllText("A.cs").Should().Be(original);
    }

    [Fact]
    public async Task EditFile_RecoveryOverlapErrorIsTerminal()
    {
        await File.WriteAllTextAsync("A.cs", "header\n");
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "A.cs");

        var result = await EditFileTextAsync(context, harness, "A.cs",
        [
            new FileEditReplacement(
                "header",
                "header\n  void Target()\n  {\n      Work();\n  }"),
            new FileEditReplacement(
                "void Target()\n{\n    Work();\n}",
                "void Changed()\n{\n    Next();\n}")
        ]);

        result.Should().Contain("kind=\"overlapping_multi_edit\"");
        result.Should().NotContain("kind=\"no_match\"");
        File.ReadAllText("A.cs").Should().Be("header\n");
    }

    [Fact]
    public async Task EditFile_ReplaceAllRecoveryRejectsMixedRecoveredCandidateShapes()
    {
        await File.WriteAllTextAsync("A.cs", "alpha\r\nbeta\nmiddle\nalpha\nbeta\n");
        var toolharness = CodingHarnessTestFactory.Create();
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
        result.Should().Contain("different literal representations");
    }

    [Fact]
    public async Task EditFile_RejectsRecoveryAboveCandidateLimit()
    {
        var original = string.Concat(
            Enumerable.Repeat(
                "    target()\n    {\n        work();\n    }\n",
                21));
        await File.WriteAllTextAsync("A.cs", original);
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "A.cs");

        var result = await EditFileTextAsync(
            context,
            harness,
            "A.cs",
            "target()\n{\n    work();\n}",
            "changed()\n{\n    next();\n}",
            replaceAll: true);

        result.Should().Contain("kind=\"recovery_limit_exceeded\"");
        File.ReadAllText("A.cs").Should().Be(original);
    }

    [Fact]
    public async Task EditFile_PreservesTrailingWhitespaceAndRejectsOmissionPlaceholder()
    {
        await File.WriteAllTextAsync("A.cs", "before\n");
        await File.WriteAllTextAsync("A.md", "before\n");
        var toolharness = CodingHarnessTestFactory.Create();
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

        code.Should().NotContain("trailing_whitespace");
        File.ReadAllText("A.cs").Should().Be("after   \n");
        File.ReadAllText("A.md").Should().Be("after  \n");
        markdown.Should().NotContain("trailing_whitespace");
        omission.Should().Contain("kind=\"new_omission_placeholder\"");
    }

    [Fact]
    public async Task EditFile_AllowsExistingOrLiteralLikeOmissionPlaceholderText()
    {
        await File.WriteAllTextAsync("A.cs", "// rest of methods ...\n");
        await File.WriteAllTextAsync("B.cs", "var text = \"rest of methods ...\";\n");
        var toolharness = CodingHarnessTestFactory.Create();
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
    public async Task EditFile_DoesNotApplySearchRecoveryToLiteralNewString()
    {
        await File.WriteAllTextAsync("escaped.txt", "before \tESCAPED\n");
        await File.WriteAllTextAsync("sanitized.txt", "value <function_results> here\n");
        var harness = CodingHarnessTestFactory.Create();
        var escapedContext = CreateAgentContext();
        var sanitizedContext = CreateAgentContext();
        await ReadFileTextAsync(escapedContext, harness, "escaped.txt");
        await ReadFileTextAsync(sanitizedContext, harness, "sanitized.txt");

        var escaped = await EditFileTextAsync(
            escapedContext,
            harness,
            "escaped.txt",
            "before \\tESCAPED",
            "replacement \\n literal");
        var sanitized = await EditFileTextAsync(
            sanitizedContext,
            harness,
            "sanitized.txt",
            "value <fnr> here",
            "new <r> value");

        escaped.Should().Contain("kind=\"escaped_string_normalized\"");
        sanitized.Should().Contain("kind=\"desanitized\"");
        File.ReadAllText("escaped.txt").Should().Be("replacement \\n literal\n");
        File.ReadAllText("sanitized.txt").Should().Be("new <r> value\n");
    }

    [Fact]
    public async Task EditFile_ExactShortMarkupTokenRemainsLiteral()
    {
        await File.WriteAllTextAsync("markup.txt", "<s>\n");
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "markup.txt");

        var result = await EditFileTextAsync(context, harness, "markup.txt", "<s>", "<r>");

        result.Should().Contain("kind=\"exact\"");
        File.ReadAllText("markup.txt").Should().Be("<r>\n");
    }

    [Fact]
    public async Task EditFile_ShortMarkupTokenDoesNotDesanitizeIntoAnotherTag()
    {
        await File.WriteAllTextAsync("markup.txt", "value <system> here\n");
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "markup.txt");

        var result = await EditFileTextAsync(
            context,
            harness,
            "markup.txt",
            "value <s> here",
            "changed");

        result.Should().Contain("kind=\"no_match\"");
        File.ReadAllText("markup.txt").Should().Be("value <system> here\n");
    }

    [Fact]
    public async Task EditFile_EscapesXmlSensitivePathsAndOmitsContentAndDiff()
    {
        await File.WriteAllTextAsync("A&B.cs", "before\n");
        var toolharness = CodingHarnessTestFactory.Create();
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
        var toolharness = CodingHarnessTestFactory.Create();
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
    public async Task EditFile_EventDistinguishesRequestedAndActualRecoveredOldString()
    {
        await File.WriteAllTextAsync(
            "A.cs",
            "var xml = <function_results>old</function_results>;\n");
        using var coordinator = new EventCoordinator();
        var events = new List<FileEditAppliedEvent>();
        using var subscription = coordinator.Subscribe<FileEditAppliedEvent>(evt =>
        {
            events.Add(evt);
            return ValueTask.CompletedTask;
        });
        var context = CreateAgentContext(coordinator);
        var harness = CodingHarnessTestFactory.Create();
        await ReadFileTextAsync(context, harness, "A.cs");

        await EditFileWithContextAsync(
            context,
            harness,
            "A.cs",
            "var xml = <fnr>old</function_results>;",
            "var xml = <function_results>new</function_results>;");

        await WaitUntilAsync(() => events.Count == 1);
        var replacement = events.Should().ContainSingle()
            .Which.Replacements.Should().ContainSingle().Subject;
        replacement.MatchStrategy.Should().Be("desanitized");
        replacement.RequestedOldStringHash.Should()
            .NotBe(replacement.ActualOldStringHash);
        replacement.RequestedOldStringLength.Should()
            .NotBe(replacement.ActualOldStringLength);
    }

    [Fact]
    public async Task EditFile_DeletionMetadataIncludesConsumedLineEnding()
    {
        await File.WriteAllTextAsync("A.cs", "one\ntwo\nthree\n");
        using var coordinator = new EventCoordinator();
        var events = new List<FileEditAppliedEvent>();
        using var subscription = coordinator.Subscribe<FileEditAppliedEvent>(evt =>
        {
            events.Add(evt);
            return ValueTask.CompletedTask;
        });
        var context = CreateAgentContext(coordinator);
        var harness = CodingHarnessTestFactory.Create();
        await ReadFileTextAsync(context, harness, "A.cs");

        await EditFileWithContextAsync(
            context,
            harness,
            "A.cs",
            "two",
            string.Empty);

        await WaitUntilAsync(() => events.Count == 1);
        var replacement = events.Should().ContainSingle()
            .Which.Replacements.Should().ContainSingle().Subject;
        replacement.RequestedOldStringLength.Should().Be(3);
        replacement.ActualOldStringLength.Should().Be(4);
        replacement.RequestedOldStringHash.Should()
            .NotBe(replacement.ActualOldStringHash);
    }

    [Fact]
    public async Task EditFile_UsesConfiguredEventBudgets()
    {
        await File.WriteAllTextAsync("A.cs", "before\n");
        using var coordinator = new EventCoordinator();
        var events = new List<FileEditAppliedEvent>();
        using var subscription = coordinator.Subscribe<FileEditAppliedEvent>(evt =>
        {
            events.Add(evt);
            return ValueTask.CompletedTask;
        });
        var context = CreateAgentContext(coordinator);
        var harness = CodingHarnessTestFactory.Create(
            fileMutationConfig: new FileMutationConfig
            {
                MaxEventSnapshotChars = 2,
                MaxEventTextEditChars = 2
            });
        await ReadFileTextAsync(context, harness, "A.cs");

        await EditFileWithContextAsync(
            context,
            harness,
            "A.cs",
            "before",
            "after");

        await WaitUntilAsync(() => events.Count == 1);
        var editEvent = events.Should().ContainSingle().Subject;
        editEvent.Before.Text.Should().BeNull();
        editEvent.After.Text.Should().BeNull();
        editEvent.TextEdits.Should().ContainSingle().Which.TextOmitted.Should().BeTrue();
    }

    [Fact]
    public async Task EditFile_EventEmissionFailureDoesNotFailSuccessfulEdit()
    {
        await File.WriteAllTextAsync("A.cs", "before\n");
        var agentContext = CreateAgentContext(new ThrowingEventCoordinator());
        var toolharness = CodingHarnessTestFactory.Create();
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
            "src/HPD-Agent.Harness/HPD-Agent.Harness.Coding/CodingHarness.EditFile.cs"));

        source.Should().NotContain("LanguageServer");
        source.Should().NotContain("LSP");
        source.Should().NotContain("Process.Start");
        source.Should().NotContain("ExecuteCommand");
        source.Should().NotContain("git apply");
        source.Should().NotContain("FileSystemWatcher");
        source.Should().NotContain("catch (InvalidOperationException)");
    }

    [Fact]
    public async Task EditFile_FollowupEditAfterSuccessfulEditDoesNotFailStale()
    {
        await File.WriteAllTextAsync("A.cs", "one two\n");
        var agentContext = CreateAgentContext();
        var toolharness = CodingHarnessTestFactory.Create();
        await ReadFileTextAsync(agentContext, toolharness, "A.cs");

        var first = await EditFileTextAsync(agentContext, toolharness, "A.cs", "one", "ONE");
        var second = await EditFileTextAsync(agentContext, toolharness, "A.cs", "two", "TWO");

        first.Should().Contain("changed=\"true\"");
        second.Should().Contain("changed=\"true\"");
        File.ReadAllText("A.cs").Should().Be("ONE TWO\n");
    }

    [Fact]
    public async Task EditFile_BomFileStillChecksAllExactOccurrences()
    {
        await File.WriteAllTextAsync("bom-duplicates.txt", "target\nmiddle\ntarget\n", new UTF8Encoding(true));
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "bom-duplicates.txt");

        var ambiguous = await EditFileTextAsync(context, harness, "bom-duplicates.txt", "target", "changed");
        var replaceAll = await EditFileTextAsync(context, harness, "bom-duplicates.txt", "target", "changed", replaceAll: true);

        ambiguous.Should().Contain("kind=\"ambiguous_match\"");
        replaceAll.Should().Contain("replacements=\"2\"");
        replaceAll.Should().Contain("kind=\"exact\"");
        File.ReadAllText("bom-duplicates.txt").Should().Be("changed\nmiddle\nchanged\n");
    }

    [Fact]
    public async Task EditFile_PartialReadTracksOriginalLinesAfterInsertion()
    {
        const string original = "before\nfirst\nsecond\nafter\n";
        await File.WriteAllTextAsync("range.txt", original);
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "range.txt", offset: 2, limit: 2);

        var result = await EditFileTextAsync(context, harness, "range.txt",
        [
            new FileEditReplacement("first", "first expanded\nextra"),
            new FileEditReplacement("second", "SECOND")
        ]);

        result.Should().Contain("changed=\"true\"");
        File.ReadAllText("range.txt").Should().Be("before\nfirst expanded\nextra\nSECOND\nafter\n");
    }

    [Fact]
    public async Task EditFile_PartialReadTracksOriginalLineAfterDeletion()
    {
        const string original = "before\nfirst\nsecond\nafter\n";
        await File.WriteAllTextAsync("range.txt", original);
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "range.txt", offset: 2, limit: 2);

        var result = await EditFileTextAsync(context, harness, "range.txt",
        [
            new FileEditReplacement("first\n", string.Empty),
            new FileEditReplacement("second", "SECOND")
        ]);

        result.Should().Contain("changed=\"true\"");
        File.ReadAllText("range.txt").Should().Be("before\nSECOND\nafter\n");
    }

    [Fact]
    public async Task EditFile_PartialReadDoesNotAllowUnreadLineAfterInsertion()
    {
        const string original = "before\nfirst\nsecond\nafter\n";
        await File.WriteAllTextAsync("range.txt", original);
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "range.txt", offset: 2, limit: 2);

        var result = await EditFileTextAsync(context, harness, "range.txt",
        [
            new FileEditReplacement("first", "first expanded\nextra"),
            new FileEditReplacement("after", "AFTER")
        ]);

        result.Should().Contain("kind=\"outside_read_range\"");
        File.ReadAllText("range.txt").Should().Be(original);
    }

    [Fact]
    public async Task EditFile_PartialReadDoesNotAllowUnreadLineAfterDeletion()
    {
        const string original = "before\nfirst\nsecond\nafter\n";
        await File.WriteAllTextAsync("range.txt", original);
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "range.txt", offset: 2, limit: 2);

        var result = await EditFileTextAsync(context, harness, "range.txt",
        [
            new FileEditReplacement("first\nsecond", "combined"),
            new FileEditReplacement("after", "AFTER")
        ]);

        result.Should().Contain("kind=\"outside_read_range\"");
        File.ReadAllText("range.txt").Should().Be(original);
    }

    [Fact]
    public async Task EditFile_CarriageReturnPartialReadUsesSameLineNumbers()
    {
        const string original = "header\rfirst\rsecond\rfooter\r";
        await File.WriteAllTextAsync("classic.txt", original);
        var harness = CodingHarnessTestFactory.Create();
        var insideContext = CreateAgentContext();
        await ReadFileTextAsync(insideContext, harness, "classic.txt", offset: 2, limit: 1);
        var inside = await EditFileTextAsync(
            insideContext,
            harness,
            "classic.txt",
            "first",
            "FIRST");

        await File.WriteAllTextAsync("classic.txt", original);
        var outsideContext = CreateAgentContext();
        await ReadFileTextAsync(outsideContext, harness, "classic.txt", offset: 2, limit: 1);
        var outside = await EditFileTextAsync(
            outsideContext,
            harness,
            "classic.txt",
            "second",
            "SECOND");

        inside.Should().Contain("changed=\"true\"");
        outside.Should().Contain("kind=\"outside_read_range\"");
        File.ReadAllText("classic.txt").Should().Be(original);
    }

    [Fact]
    public async Task EditFile_RejectsNullEditElementAsInvalidArguments()
    {
        var harness = CodingHarnessTestFactory.Create();
        var result = await EditFileTextAsync(CreateAgentContext(), harness, "A.cs", [null!]);

        result.Should().Contain("kind=\"invalid_arguments\"");
        result.Should().Contain("edit 1");
    }

    [Fact]
    public async Task EditFile_ReportsIndexOfEmptyOldStringInMultiEdit()
    {
        await File.WriteAllTextAsync("A.cs", "first\nsecond\n");
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "A.cs");

        var result = await EditFileTextAsync(context, harness, "A.cs",
        [
            new FileEditReplacement("first", "FIRST"),
            new FileEditReplacement("", "insert")
        ]);

        result.Should().Contain("kind=\"invalid_empty_old_string\"");
        result.Should().Contain("edit 2");
        File.ReadAllText("A.cs").Should().Be("first\nsecond\n");
    }

    [Fact]
    public async Task EditFile_MixedQuoteRecoveryRejectsDifferentCandidateShapes()
    {
        await File.WriteAllTextAsync("quotes.txt", "var text = “hello”;\nvar text = ”hello“;\n");
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "quotes.txt");

        var result = await EditFileTextAsync(context, harness, "quotes.txt", "var text = \"hello\";", "var text = \"goodbye\";", replaceAll: true);

        result.Should().Contain("kind=\"recovery_semantic_difference\"");
        File.ReadAllText("quotes.txt").Should().Be("var text = “hello”;\nvar text = ”hello“;\n");
    }

    [Fact]
    public async Task EditFile_AllowsSeparateMatchThatAlsoAppearsInEarlierInsertion()
    {
        await File.WriteAllTextAsync("overlap.txt", "first\nseparate } target\n");
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "overlap.txt");

        var result = await EditFileTextAsync(context, harness, "overlap.txt",
        [
            new FileEditReplacement("first", "inserted } text"),
            new FileEditReplacement("}", "]")
        ]);

        result.Should().Contain("changed=\"true\"");
        result.Should().Contain("kind=\"generated_matches_skipped\"");
        result.Should().Contain("count=\"1\"");
        File.ReadAllText("overlap.txt").Should().Be("inserted } text\nseparate ] target\n");
    }

    [Fact]
    public async Task EditFile_RejectsMatchInsideEarlierInsertion()
    {
        await File.WriteAllTextAsync("overlap.txt", "first\n");
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "overlap.txt");

        var result = await EditFileTextAsync(context, harness, "overlap.txt",
        [
            new FileEditReplacement("first", "inserted target"),
            new FileEditReplacement("target", "changed")
        ]);

        result.Should().Contain("kind=\"overlapping_multi_edit\"");
        File.ReadAllText("overlap.txt").Should().Be("first\n");
    }

    [Fact]
    public async Task EditFile_ReplaceAllSkipsGeneratedButEditsOriginalMatches()
    {
        await File.WriteAllTextAsync("overlap.txt", "first\nsecond target\n");
        using var coordinator = new EventCoordinator();
        var events = new List<FileEditAppliedEvent>();
        using var subscription = coordinator.Subscribe<FileEditAppliedEvent>(evt =>
        {
            events.Add(evt);
            return ValueTask.CompletedTask;
        });
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext(coordinator);
        await ReadFileTextAsync(context, harness, "overlap.txt");

        var result = ResultToString((await EditFileWithContextAsync(
            context,
            harness,
            "overlap.txt",
            [
                new FileEditReplacement("first", "generated target"),
                new FileEditReplacement("target", "changed", ReplaceAll: true)
            ])).Result);

        result.Should().Contain("replacements=\"2\"");
        result.Should().Contain("kind=\"generated_matches_skipped\"");
        result.Should().Contain("count=\"1\"");
        File.ReadAllText("overlap.txt").Should().Be("generated target\nsecond changed\n");
        await WaitUntilAsync(() => events.Count == 1);
        events.Should().ContainSingle()
            .Which.Normalizations.Should()
            .ContainSingle(note =>
                note.Kind == "generated_matches_skipped" &&
                note.Count == 1);
    }

    [Fact]
    public async Task EditFile_PreservesTrailingTabsInMultilineReplacement()
    {
        await File.WriteAllTextAsync("values.txt", "before\n");
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "values.txt");

        var result = await EditFileTextAsync(
            context,
            harness,
            "values.txt",
            "before",
            "first \nsecond\t");

        result.Should().Contain("changed=\"true\"");
        File.ReadAllText("values.txt").Should().Be("first \nsecond\t\n");
    }

    [Fact]
    public async Task EditFile_IndentationRecoveryHandlesCarriageReturnLines()
    {
        await File.WriteAllTextAsync("classic.txt", "header\r    alpha\r    beta\r    gamma\rfooter\r");
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "classic.txt");

        var result = await EditFileTextAsync(context, harness, "classic.txt", "alpha\rbeta\rgamma", "ALPHA\rbeta\rgamma");

        result.Should().Contain("kind=\"indentation_only_block\"");
        File.ReadAllText("classic.txt").Should().Contain("ALPHA");
    }

    [Fact]
    public void EditOriginMap_InvalidRangeReturnsStructuredFailure()
    {
        var result = EditOriginMap
            .Create(length: 5)
            .ApplyReplacements(
                [new EditCandidate(Start: 4, Length: 2)],
                replacementLength: 1);

        result.Status.Should().Be(EditOriginMapApplyStatus.InvalidRange);
        result.Map.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task EditFile_LargeReplaceAllFollowedBySequentialEditRemainsBounded()
    {
        var original = string.Join(
            '\n',
            Enumerable.Repeat("xxxxxx", 1_500)) + "\nMARKER";
        await File.WriteAllTextAsync("A.cs", original);
        var harness = CodingHarnessTestFactory.Create();
        var context = CreateAgentContext();
        await ReadFileTextAsync(context, harness, "A.cs");
        var stopwatch = Stopwatch.StartNew();

        var result = await EditFileTextAsync(context, harness, "A.cs",
        [
            new FileEditReplacement("x", "y", ReplaceAll: true),
            new FileEditReplacement("MARKER", "DONE")
        ]);
        stopwatch.Stop();

        result.Should().Contain("changed=\"true\"");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        File.ReadAllText("A.cs").Should().Be(
            string.Join('\n', Enumerable.Repeat("yyyyyy", 1_500)) +
            "\nDONE");
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

        await new EnvironmentContextMiddleware(new EnvironmentContextConfig()).AfterFunctionAsync(afterContext, CancellationToken.None);
        return ResultToString(afterContext.Result);
    }

    private static Task<string> EditFileTextAsync(
        AgentContext agentContext,
        CodingToolHarness toolharness,
        string path,
        string oldString,
        string newString,
        bool replaceAll = false)
        => EditFileTextAsync(agentContext, toolharness, path, [new FileEditReplacement(oldString, newString, replaceAll)]);

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
        => EditFileWithContextAsync(agentContext, toolharness, path, [new FileEditReplacement(oldString, newString, replaceAll)]);

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

        await new EnvironmentContextMiddleware(new EnvironmentContextConfig()).AfterFunctionAsync(afterContext, CancellationToken.None);
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
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "HPD-Agent.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? AppContext.BaseDirectory;
    }

    private static AgentContext CreateAgentContext(
        IEventCoordinator? eventCoordinator = null,
        RuntimeWorkspaceBinding? workspace = null)
    {
        var state = AgentLoopState.InitialSafe(
            [],
            "test-run",
            "test-conversation",
            "test-agent");

        var context = new AgentContext(
            "test-agent",
            "test-conversation",
            state,
            eventCoordinator ?? new EventCoordinator(),
            new Session("test-session"),
            new Thread("test-session", "test-agent"),
            CancellationToken.None,
            config: new AgentConfig(),
            runtimeOptions: new AgentRuntimeOptions
            {
                EventComposition = CodingEventTestCodec.Composition
            });
        context.RuntimeCapabilities.Set(
            TestRuntimeWorkspace.FromRunConfig(
                CreateWorkspaceRunConfig(workspace)));
        return context;
    }

    private static BeforeFunctionContext CreateBeforeFunctionContext(AgentContext? agentContext = null)
    {
        agentContext ??= CreateAgentContext();
        RuntimeWorkspaceBinding workspace =
            agentContext.RuntimeCapabilities.GetRequired<RuntimeWorkspaceBinding>();
        return agentContext.AsBeforeFunction(
            function: null,
            callId: "call-1",
            arguments: new Dictionary<string, object?>(),
            runConfig: TestRuntimeWorkspace.RunConfig(workspace),
            toolharnessName: "CodingToolHarness");
    }

    private static AgentRunConfig CreateWorkspaceRunConfig(RuntimeWorkspaceBinding? workspace = null)
    {
        return TestRuntimeWorkspace.RunConfig(
            workspace ?? TestRuntimeWorkspace.Create(
                Directory.GetCurrentDirectory(),
                rootId: "default"));
    }

    private sealed class ThrowingEventCoordinator : IEventCoordinator
    {
        private readonly EventCoordinator _inner = new();

        public IEventFlowRegistry EventFlows => _inner.EventFlows;
        public IReadOnlyList<PendingRequestSnapshot> GetPendingRequests() => _inner.GetPendingRequests();

        public void Emit(Event evt) => throw new InvalidOperationException("boom");
        public void Emit(Event evt, EventRouteDescriptor? route) => Emit(evt);

        public ValueTask EmitAsync(Event evt, CancellationToken ct = default)
            => throw new InvalidOperationException("boom");
        public ValueTask EmitAsync(Event evt, EventRouteDescriptor? route, CancellationToken ct = default) => EmitAsync(evt, ct);

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
        public IEventCoordinator CreateChild(EventChildOwnership ownership) => _inner.CreateChild(ownership);
        public IDisposable ForwardTo(IEventCoordinator destination, EventForwardingOptions? options = null) => _inner.ForwardTo(destination, options);

        public RequestHandle StartRequest<TRequest, TResponse>(
            TRequest request,
            RequestOptions? options = null)
            where TRequest : Event, IRequestEvent
            where TResponse : Event, IResponseEvent
            => _inner.StartRequest<TRequest, TResponse>(request, options);
        public RequestHandle StartRequest<TRequest, TResponse>(TRequest request, EventRouteDescriptor? route, RequestOptions? options = null)
            where TRequest : Event, IRequestEvent where TResponse : Event, IResponseEvent
            => _inner.StartRequest<TRequest, TResponse>(request, route, options);

        public RequestHandle RegisterRequest<TRequest, TResponse>(
            TRequest request,
            RequestOptions? options = null)
            where TRequest : Event, IRequestEvent
            where TResponse : Event, IResponseEvent
            => _inner.RegisterRequest<TRequest, TResponse>(request, options);
        public RequestHandle RegisterRequest<TRequest, TResponse>(TRequest request, EventRouteDescriptor? route, RequestOptions? options = null)
            where TRequest : Event, IRequestEvent where TResponse : Event, IResponseEvent
            => _inner.RegisterRequest<TRequest, TResponse>(request, route, options);

        public Task<TResponse> RequestAsync<TRequest, TResponse>(TRequest request, TimeSpan timeout, CancellationToken ct = default)
            where TRequest : Event, IRequestEvent
            where TResponse : Event, IResponseEvent
            => _inner.RequestAsync<TRequest, TResponse>(request, timeout, ct);

        public RespondResult Respond(Event response) => _inner.Respond(response);

        public RespondResult Respond(string requestId, Event response) => _inner.Respond(requestId, response);

        public ValueTask<RespondResult> RespondAsync(string requestId, Event response, Func<Event, CancellationToken, ValueTask<Event>> beforeCompletion, CancellationToken cancellationToken = default)
            => _inner.RespondAsync(requestId, response, beforeCompletion, cancellationToken);

        public EventCoordinatorStats GetStats() => _inner.GetStats();
    }
}
