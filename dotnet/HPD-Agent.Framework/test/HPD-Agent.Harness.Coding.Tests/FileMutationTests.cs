using System.Text;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.Tests;

[Collection(CurrentDirectoryCollection.Name)]
public sealed class FileMutationTests : IDisposable
{
    private readonly string _originalCwd = Directory.GetCurrentDirectory();
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"hpd-file-mutation-tests-{Guid.NewGuid():N}");

    public FileMutationTests()
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
    public async Task ApplyWorkspaceTextMutationAsync_NormalizesToExistingCrlfWhenRequested()
    {
        await File.WriteAllTextAsync("crlf.txt", "one\r\ntwo\r\n", new UTF8Encoding(false));
        var toolharness = CodingHarnessTestFactory.Create();

        await ApplyAsync(toolharness, CreateRequest(
            "crlf.txt",
            "three\nfour\n",
            allowCreate: false,
            normalizeLineEndings: true));

        File.ReadAllText("crlf.txt").Should().Be("three\r\nfour\r\n");
    }

    [Fact]
    public async Task ApplyWorkspaceTextMutationAsync_AllowsToolStaleValidatorToReject()
    {
        await File.WriteAllTextAsync("A.cs", "before\n");
        var toolharness = CodingHarnessTestFactory.Create();

        var act = () => ApplyAsync(toolharness, CreateRequest(
            "A.cs",
            "after\n",
            allowCreate: false,
            validateBeforeMutation: _ => throw new FileMutationException(FileMutationErrorKind.StaleFile, "stale")));

        await act.Should().ThrowAsync<FileMutationException>()
            .Where(exception => exception.Kind == FileMutationErrorKind.StaleFile);
    }

    [Fact]
    public async Task ApplyWorkspaceTextMutationAsync_OmitsOversizedTextEditPayloadInEvent()
    {
        var bigText = new string('x', 100_001);
        FileMutationEventBuildRequest? eventRequest = null;
        var toolharness = CodingHarnessTestFactory.Create();

        await ApplyAsync(toolharness, CreateRequest(
            "A.cs",
            bigText,
            textEdits:
            [
                new FileMutationTextEdit(
                    1,
                    new FileMutationRange(1, 1, 1, 1, 0, 0),
                    new FileMutationRange(1, 1, 1, bigText.Length + 1, 0, bigText.Length),
                    string.Empty,
                    bigText,
                    false,
                    null)
            ],
            eventFactory: request =>
            {
                eventRequest = request;
                return CreateWriteEvent(request);
            }));

        eventRequest!.TextEdits.Should().ContainSingle();
        eventRequest.TextEdits[0].TextOmitted.Should().BeTrue();
        eventRequest.TextEdits[0].OldText.Should().BeNull();
        eventRequest.TextEdits[0].NewText.Should().BeNull();
    }

    [Fact]
    public async Task ApplyWorkspaceTextMutationAsync_OmitsOversizedSnapshotTextInEvent()
    {
        var bigText = new string('x', 500_001);
        FileMutationEventBuildRequest? eventRequest = null;
        var toolharness = CodingHarnessTestFactory.Create();

        await ApplyAsync(toolharness, CreateRequest(
            "A.cs",
            bigText,
            eventFactory: request =>
            {
                eventRequest = request;
                return CreateWriteEvent(request);
            }));

        eventRequest!.After.TextOmitted.Should().BeTrue();
        eventRequest.After.Text.Should().BeNull();
        eventRequest.After.OmissionReason.Should().Be("snapshot_too_large");
    }

    [Fact]
    public async Task ApplyWorkspaceTextMutationAsync_NoOpDoesNotEmitMutationEvent()
    {
        await File.WriteAllTextAsync("A.cs", "same\n");
        var eventFactoryCalled = false;
        var toolharness = CodingHarnessTestFactory.Create();

        var result = await ApplyAsync(toolharness, CreateRequest(
            "A.cs",
            "same\n",
            allowCreate: false,
            eventFactory: request =>
            {
                eventFactoryCalled = true;
                return CreateWriteEvent(request);
            }));

        result.Changed.Should().BeFalse();
        result.EventEmitted.Should().BeFalse();
        eventFactoryCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyWorkspaceTextMutationAsync_EmitsCompactHunkAroundChangedRegion()
    {
        var before = string.Join('\n', Enumerable.Range(1, 80).Select(line => $"line {line}")) + "\n";
        var after = before.Replace("line 50", "line 50 changed", StringComparison.Ordinal);
        FileMutationEventBuildRequest? eventRequest = null;
        await File.WriteAllTextAsync("A.cs", before);
        var toolharness = CodingHarnessTestFactory.Create();

        await ApplyAsync(toolharness, CreateRequest(
            "A.cs",
            after,
            allowCreate: false,
            eventFactory: request =>
            {
                eventRequest = request;
                return CreateWriteEvent(request);
            }));

        var hunk = eventRequest!.Hunks.Should().ContainSingle().Subject;
        hunk.OldStart.Should().BeGreaterThan(1);
        hunk.OldStart.Should().BeLessThanOrEqualTo(50);
        hunk.Lines.Should().Contain("-line 50");
        hunk.Lines.Should().Contain("+line 50 changed");
        hunk.Lines.Should().NotContain(" line 1");
        hunk.Lines.Should().NotContain(" line 80");
    }

    private Task<FileMutationResult> ApplyAsync(CodingToolHarness harness, FileMutationRequest request)
    {
        var workspace = TestRuntimeWorkspace.Create(_tempRoot);
        return harness.ApplyWorkspaceTextMutationAsync(workspace, CodingWorkspacePathAccess.Resolve(workspace, request.Path), request);
    }

    private static FileMutationRequest CreateRequest(
        string path,
        string updatedText,
        bool allowCreate = true,
        bool createParents = true,
        bool normalizeLineEndings = false,
        IReadOnlyList<FileMutationTextEdit>? textEdits = null,
        Action<FileMutationContent>? validateBeforeMutation = null,
        FileMutationEventFactory? eventFactory = null)
        => new(
            "TestMutation",
            path,
            updatedText,
            allowCreate ? CodingFileMutationKind.Created : CodingFileMutationKind.Changed,
            allowCreate,
            createParents,
            normalizeLineEndings,
            textEdits ?? [],
            [],
            FunctionContext: null,
            ValidateBeforeMutation: validateBeforeMutation,
            EventFactory: eventFactory);

    private static string FullPath(string relativePath)
        => Path.GetFullPath(relativePath, Directory.GetCurrentDirectory());

    private static FileWriteAppliedEvent CreateWriteEvent(FileMutationEventBuildRequest request)
        => new()
        {
            ToolCallId = request.ToolCallId,
            FunctionName = request.FunctionName,
            Path = request.Path,
            DisplayPath = request.DisplayPath,
            MutationKind = request.MutationKind,
            Created = request.Created,
            Changed = request.Changed,
            Before = request.Before,
            After = request.After,
            TextEdits = request.TextEdits,
            Hunks = request.Hunks,
            HunksTruncated = request.HunksTruncated,
            DiffStat = request.DiffStat,
            Notes = request.Notes,
            Mode = request.Created ? FileWriteMode.Create : FileWriteMode.Rewrite
        };

}
