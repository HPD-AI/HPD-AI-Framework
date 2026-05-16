using System.Text;
using HPDOS.Harneses.Middleware;

namespace HPD.Agent.Harness.Coding.Tests;

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
    public async Task ApplyTextMutationAsync_ResolvesRelativePathAndCreatesParents()
    {
        var harness = new CodingHarness();

        var result = await harness.ApplyTextMutationAsync(CreateRequest("src/A.cs", "class A {}\n"));

        result.Path.Should().Be(FullPath("src/A.cs"));
        result.Created.Should().BeTrue();
        result.Changed.Should().BeTrue();
        File.ReadAllText(result.Path).Should().Be("class A {}\n");
    }

    [Fact]
    public async Task ApplyTextMutationAsync_RejectsMissingPath()
    {
        var harness = new CodingHarness();

        var act = () => harness.ApplyTextMutationAsync(CreateRequest(" ", "x"));

        await act.Should().ThrowAsync<FileMutationException>()
            .Where(exception => exception.Kind == FileMutationErrorKind.InvalidArguments);
    }

    [Fact]
    public async Task ApplyTextMutationAsync_RejectsDirectoryPath()
    {
        Directory.CreateDirectory("src");
        var harness = new CodingHarness();

        var act = () => harness.ApplyTextMutationAsync(CreateRequest("src", "x", allowCreate: false));

        await act.Should().ThrowAsync<FileMutationException>()
            .Where(exception => exception.Kind == FileMutationErrorKind.PathIsDirectory);
    }

    [Fact]
    public async Task ApplyTextMutationAsync_RejectsBlockedPathBeforeMutation()
    {
        var harness = new CodingHarness();

        var act = () => harness.ApplyTextMutationAsync(CreateRequest("/dev/zero", "x"));

        await act.Should().ThrowAsync<FileMutationException>()
            .Where(exception => exception.Kind == FileMutationErrorKind.BlockedDevicePath);
    }

    [Fact]
    public async Task ApplyTextMutationAsync_RejectsNotebookPath()
    {
        var harness = new CodingHarness();

        var act = () => harness.ApplyTextMutationAsync(CreateRequest("notebook.ipynb", "{}"));

        await act.Should().ThrowAsync<FileMutationException>()
            .Where(exception => exception.Kind == FileMutationErrorKind.NotebookFile);
    }

    [Fact]
    public async Task ApplyTextMutationAsync_RejectsBinaryFiles()
    {
        await File.WriteAllBytesAsync("binary.bin", [0x01, 0x02, 0x00, 0x03]);
        var harness = new CodingHarness();

        var act = () => harness.ApplyTextMutationAsync(CreateRequest("binary.bin", "x", allowCreate: false));

        await act.Should().ThrowAsync<FileMutationException>()
            .Where(exception => exception.Kind == FileMutationErrorKind.BinaryFile);
    }

    [Fact]
    public async Task ApplyTextMutationAsync_MissingNonCreatePathSuggestsSameBasename()
    {
        await File.WriteAllTextAsync("Program.cs", "class Program {}\n");
        var harness = new CodingHarness();

        var act = () => harness.ApplyTextMutationAsync(CreateRequest("Program.ts", "x", allowCreate: false));

        var assertion = await act.Should().ThrowAsync<FileMutationException>();
        assertion.Which.Kind.Should().Be(FileMutationErrorKind.FileNotFound);
        assertion.Which.Message.Should().Contain("Program.cs");
    }

    [Fact]
    public async Task ApplyTextMutationAsync_RequiresExistingParentWhenCreateParentsDisabled()
    {
        var harness = new CodingHarness();

        var act = () => harness.ApplyTextMutationAsync(CreateRequest("missing/A.cs", "x", createParents: false));

        await act.Should().ThrowAsync<FileMutationException>()
            .Where(exception => exception.Kind == FileMutationErrorKind.FileNotFound);
    }

    [Fact]
    public async Task ApplyTextMutationAsync_PreservesUtf16BomAndReportsPreciseByteLength()
    {
        await File.WriteAllTextAsync("utf16.txt", "before\n", Encoding.Unicode);
        var harness = new CodingHarness();

        var result = await harness.ApplyTextMutationAsync(CreateRequest("utf16.txt", "after\n", allowCreate: false));

        var bytes = await File.ReadAllBytesAsync("utf16.txt");
        bytes[..2].Should().Equal(0xFF, 0xFE);
        result.ByteLength.Should().Be(bytes.Length);
    }

    [Fact]
    public async Task ApplyTextMutationAsync_NormalizesToExistingCrlfWhenRequested()
    {
        await File.WriteAllTextAsync("crlf.txt", "one\r\ntwo\r\n", new UTF8Encoding(false));
        var harness = new CodingHarness();

        await harness.ApplyTextMutationAsync(CreateRequest(
            "crlf.txt",
            "three\nfour\n",
            allowCreate: false,
            normalizeLineEndings: true));

        File.ReadAllText("crlf.txt").Should().Be("three\r\nfour\r\n");
    }

    [Fact]
    public async Task ApplyTextMutationAsync_AcquiresAndReleasesConfiguredLock()
    {
        var lockProvider = new RecordingLockProvider();
        var harness = new CodingHarness(null, null, fileMutationLockProvider: lockProvider);

        await harness.ApplyTextMutationAsync(CreateRequest("A.cs", "class A {}\n"));

        lockProvider.AcquiredPath.Should().Be(FullPath("A.cs"));
        lockProvider.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyTextMutationAsync_InvokesHistorySinkAndRecordsOptionalFailure()
    {
        await File.WriteAllTextAsync("A.cs", "before\n");
        var failingHistory = new FailingHistorySink();
        FileMutationEventBuildRequest? eventRequest = null;
        var harness = new CodingHarness(null, null, fileMutationHistorySinks: [failingHistory]);

        await harness.ApplyTextMutationAsync(CreateRequest(
            "A.cs",
            "after\n",
            allowCreate: false,
            eventFactory: request =>
            {
                eventRequest = request;
                return CreateWriteEvent(request);
            }));

        failingHistory.Calls.Should().Be(1);
        eventRequest!.Notes.Should().Contain(note => note.Kind == "history_capture_failed");
    }

    [Fact]
    public async Task ApplyTextMutationAsync_UsesHostSinkWhenClaimed()
    {
        var sink = new RecordingTextSink(claims: true);
        var harness = new CodingHarness(null, null, fileMutationTextSinks: [sink]);

        var result = await harness.ApplyTextMutationAsync(CreateRequest("virtual.cs", "class V {}\n"));

        sink.Calls.Should().Be(1);
        sink.Request!.Path.Should().Be(FullPath("virtual.cs"));
        File.Exists("virtual.cs").Should().BeFalse();
        result.UpdatedText.Should().Be("class V {}\n");
    }

    [Fact]
    public async Task ApplyTextMutationAsync_FallsBackToFilesystemWhenHostSinkDoesNotClaim()
    {
        var sink = new RecordingTextSink(claims: false);
        var harness = new CodingHarness(null, null, fileMutationTextSinks: [sink]);

        await harness.ApplyTextMutationAsync(CreateRequest("A.cs", "class A {}\n"));

        sink.Calls.Should().Be(1);
        File.ReadAllText("A.cs").Should().Be("class A {}\n");
    }

    [Fact]
    public async Task ApplyTextMutationAsync_ConvertsHostSinkFailureToMutationError()
    {
        var sink = new ThrowingTextSink();
        var harness = new CodingHarness(null, null, fileMutationTextSinks: [sink]);

        var act = () => harness.ApplyTextMutationAsync(CreateRequest("A.cs", "class A {}\n"));

        await act.Should().ThrowAsync<FileMutationException>()
            .Where(exception => exception.Kind == FileMutationErrorKind.HostSinkFailed);
    }

    [Fact]
    public async Task ApplyTextMutationAsync_AllowsToolStaleValidatorToReject()
    {
        await File.WriteAllTextAsync("A.cs", "before\n");
        var harness = new CodingHarness();

        var act = () => harness.ApplyTextMutationAsync(CreateRequest(
            "A.cs",
            "after\n",
            allowCreate: false,
            validateBeforeMutation: _ => throw new FileMutationException(FileMutationErrorKind.StaleFile, "stale")));

        await act.Should().ThrowAsync<FileMutationException>()
            .Where(exception => exception.Kind == FileMutationErrorKind.StaleFile);
    }

    [Fact]
    public async Task ApplyTextMutationAsync_OmitsOversizedTextEditPayloadInEvent()
    {
        var bigText = new string('x', 100_001);
        FileMutationEventBuildRequest? eventRequest = null;
        var harness = new CodingHarness();

        await harness.ApplyTextMutationAsync(CreateRequest(
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
    public async Task ApplyTextMutationAsync_OmitsOversizedSnapshotTextInEvent()
    {
        var bigText = new string('x', 500_001);
        FileMutationEventBuildRequest? eventRequest = null;
        var harness = new CodingHarness();

        await harness.ApplyTextMutationAsync(CreateRequest(
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
    public async Task ApplyTextMutationAsync_NoOpDoesNotEmitMutationEvent()
    {
        await File.WriteAllTextAsync("A.cs", "same\n");
        var eventFactoryCalled = false;
        var harness = new CodingHarness();

        var result = await harness.ApplyTextMutationAsync(CreateRequest(
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

    private sealed class RecordingLockProvider : IFileMutationLockProvider
    {
        public string? AcquiredPath { get; private set; }
        public bool Disposed { get; private set; }

        public ValueTask<IAsyncDisposable> AcquireAsync(string fullPath, CancellationToken cancellationToken)
        {
            AcquiredPath = fullPath;
            return ValueTask.FromResult<IAsyncDisposable>(new Lease(this));
        }

        private sealed class Lease(RecordingLockProvider owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                owner.Disposed = true;
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class FailingHistorySink : IFileMutationHistorySink
    {
        public int Calls { get; private set; }

        public ValueTask CaptureBeforeMutationAsync(FileMutationHistoryRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("history unavailable");
        }
    }

    private sealed class RecordingTextSink(bool claims) : IFileMutationTextSink
    {
        public int Calls { get; private set; }
        public FileMutationSinkRequest? Request { get; private set; }

        public ValueTask<FileMutationSinkResult?> TryMutateTextAsync(FileMutationSinkRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            Request = request;
            return ValueTask.FromResult(claims
                ? new FileMutationSinkResult
                {
                    FinalText = request.AfterText,
                    LastWriteTimeUtc = DateTimeOffset.UtcNow,
                    ByteLength = Encoding.UTF8.GetByteCount(request.AfterText),
                    ContentHash = null,
                    WroteToDisk = false
                }
                : null);
        }
    }

    private sealed class ThrowingTextSink : IFileMutationTextSink
    {
        public ValueTask<FileMutationSinkResult?> TryMutateTextAsync(FileMutationSinkRequest request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("sink failed");
    }
}
