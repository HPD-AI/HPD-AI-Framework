using System.Security.Cryptography;
using System.Text;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPDOS.ToolHarnesses.Middleware;

public sealed class CodingLanguageServerMiddleware : IToolHarnessMiddleware, IAsyncDisposable
{
    private const int MaxOpenDocumentBytes = 1024 * 1024;
    private const int MaxDiagnosticSummariesPerEvent = 20;

    private static readonly HashSet<string> ObservedFunctionNames = new(StringComparer.Ordinal)
    {
        "ReadFile",
        "EditFile",
        "WriteFile"
    };

    private readonly LanguageServerOptions _options;
    private readonly ILanguageServerService _languageServerService;
    private readonly LanguageServerDiagnosticFormatter _formatter;
    private readonly bool _ownsService;

    public CodingLanguageServerMiddleware()
        : this(new LanguageServerOptions())
    {
    }

    public CodingLanguageServerMiddleware(LanguageServerOptions options)
        : this(options, new LanguageServerService(options), ownsService: true)
    {
    }

    public CodingLanguageServerMiddleware(
        LanguageServerOptions options,
        ILanguageServerService languageServerService)
        : this(options, languageServerService, ownsService: false)
    {
    }

    private CodingLanguageServerMiddleware(
        LanguageServerOptions options,
        ILanguageServerService languageServerService,
        bool ownsService)
    {
        _options = options;
        _languageServerService = languageServerService;
        _formatter = new LanguageServerDiagnosticFormatter();
        _ownsService = ownsService;
    }

    public Task BeforeIterationAsync(BeforeIterationContext context, CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !_options.Feedback.Enabled)
            return Task.CompletedTask;

        var state = context.GetMiddlewareState<LanguageServerState>();
        var pending = state?.PendingFeedback.Where(feedback => !feedback.Injected).ToArray() ?? [];
        if (pending.Length == 0)
            return Task.CompletedTask;

        var feedback = _formatter.FormatIterationFeedback(
            pending.Select(item => item.DiagnosticSet).ToArray(),
            _options.Feedback);

        if (!string.IsNullOrWhiteSpace(feedback))
        {
            context.Options.Instructions = string.IsNullOrWhiteSpace(context.Options.Instructions)
                ? feedback
                : $"{context.Options.Instructions}\n\n{feedback}";
        }

        context.UpdateMiddlewareState<LanguageServerState>(current =>
        {
            var injectedIds = pending.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            return current with
            {
                PendingFeedback = current.PendingFeedback
                    .Select(item => injectedIds.Contains(item.Id) ? item with { Injected = true } : item)
                    .ToArray()
            };
        });

        return Task.CompletedTask;
    }

    public Task BeforeFunctionAsync(BeforeFunctionContext context, CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !IsObservedCodingFunction(context.ToolHarnessName, context.Function?.Name))
            return Task.CompletedTask;

        var path = TryGetPathArgument(context.Arguments);
        context.UpdateMiddlewareState<LanguageServerState>(state =>
        {
            var pendingOperations = state.PendingOperations
                .Where(operation => !string.Equals(operation.CallId, context.FunctionCallId, StringComparison.Ordinal))
                .Append(new LanguageServerPendingOperation
                {
                    CallId = context.FunctionCallId,
                    ToolName = context.Function!.Name,
                    Path = path,
                    ObservedAt = DateTimeOffset.UtcNow
                })
                .ToArray();

            return state with { PendingOperations = pendingOperations };
        });

        return Task.CompletedTask;
    }

    public async Task<object?> WrapFunctionCallAsync(
        FunctionRequest request,
        Func<FunctionRequest, Task<object?>> handler,
        CancellationToken cancellationToken)
    {
        return await handler(request).ConfigureAwait(false);
    }

    public async Task AfterFunctionAsync(AfterFunctionContext context, CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !context.IsSuccess)
            return;

        if (!context.ResultMetadata.TryGet<ReadFileSnapshot>(
                CodingToolMetadataKeys.ReadFileSnapshot,
                out var snapshot))
        {
            if (context.ResultMetadata.TryGet<CodingFileMutationSnapshot>(
                    CodingToolMetadataKeys.FileMutationSnapshot,
                    out var mutation))
            {
                await HandleFileMutationAsync(context, mutation, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        await OpenReadFileDocumentAsync(context, snapshot, cancellationToken).ConfigureAwait(false);
    }

    public Task AfterIterationAsync(AfterIterationContext context, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return Task.CompletedTask;

        context.UpdateMiddlewareState<LanguageServerState>(state =>
        {
            if (state.PendingOperations.Count == 0)
                return state;

            var completedCallIds = context.ToolResults
                .Select(result => result.CallId)
                .Where(callId => !string.IsNullOrWhiteSpace(callId))
                .ToHashSet(StringComparer.Ordinal);

            if (completedCallIds.Count == 0)
                return state;

            return state with
            {
                PendingOperations = state.PendingOperations
                    .Where(operation => !completedCallIds.Contains(operation.CallId))
                    .ToArray()
            };
        });

        return Task.CompletedTask;
    }

    public Task AfterMessageTurnAsync(AfterMessageTurnContext context, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return Task.CompletedTask;

        context.UpdateMiddlewareState<LanguageServerState>(state => state with
        {
            PendingOperations = [],
            PendingFeedback = state.PendingFeedback
                .Where(feedback => !feedback.Injected)
                .ToArray()
        });

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_ownsService)
            await _languageServerService.DisposeAsync().ConfigureAwait(false);
    }

    private async Task OpenReadFileDocumentAsync(
        AfterFunctionContext context,
        ReadFileSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var resolution = await _languageServerService.ResolveDocumentAsync(snapshot.Path, cancellationToken)
            .ConfigureAwait(false);
        if (!resolution.HasServers || resolution.PrimaryLanguageId is null)
            return;

        var text = await TryReadDocumentTextAsync(resolution.Path, cancellationToken).ConfigureAwait(false);
        if (text == null)
            return;

        var existing = context.GetMiddlewareState<LanguageServerState>()?
            .DocumentsByPath
            .GetValueOrDefault(resolution.Path);

        var languageId = resolution.PrimaryLanguageId;
        var uri = resolution.Uri;
        var version = existing?.LanguageId == languageId ? existing.Version : 0;

        var result = await _languageServerService.OpenDocumentAsync(
            new LanguageServerDocumentOpenRequest
            {
                Path = resolution.Path,
                Uri = uri,
                LanguageId = languageId,
                Text = text,
                Version = version,
                PositionEncoding = existing?.PositionEncoding ?? "utf-16"
            },
            cancellationToken).ConfigureAwait(false);

        await RefreshUnavailableServersAsync(context, cancellationToken).ConfigureAwait(false);

        var diagnosticSets = result.Diagnostics;
        await context.PublishAsync(new LanguageServerDocumentOpenedEvent
        {
            SessionId = context.SessionId,
            ThreadId = context.ThreadId,
            Path = resolution.Path,
            Uri = uri,
            LanguageId = languageId,
            DocumentVersion = result.Version
        }, cancellationToken).ConfigureAwait(false);

        context.UpdateMiddlewareState<LanguageServerState>(state =>
        {
            var documents = new Dictionary<string, LanguageServerDocumentSnapshot>(
                state.DocumentsByPath,
                StringComparer.Ordinal)
            {
                [resolution.Path] = new LanguageServerDocumentSnapshot
                {
                    Path = resolution.Path,
                    Uri = uri,
                    LanguageId = languageId,
                    Version = result.Version,
                    Opened = result.Opened,
                    PositionEncoding = result.PositionEncoding,
                    ContentHash = ComputeContentHash(text),
                    LastWriteTimeUtc = snapshot.LastWriteTimeUtc,
                    DirtySinceLastDiagnostics = diagnosticSets.Count == 0,
                    LastObservedAt = DateTimeOffset.UtcNow
                }
            };

            var diagnostics = new Dictionary<string, LanguageServerDiagnosticSet>(
                state.DiagnosticsByPath,
                StringComparer.Ordinal);

            foreach (var diagnosticSet in diagnosticSets)
                diagnostics[CreateDiagnosticStateKey(diagnosticSet)] = diagnosticSet;

            return state with
            {
                DocumentsByPath = documents,
                DiagnosticsByPath = diagnostics
            };
        });
    }

    private async Task HandleFileMutationAsync(
        AfterFunctionContext context,
        CodingFileMutationSnapshot mutation,
        CancellationToken cancellationToken)
    {
        var resolution = await _languageServerService.ResolveDocumentAsync(mutation.Path, cancellationToken)
            .ConfigureAwait(false);
        if (!resolution.HasServers || resolution.PrimaryLanguageId is null)
            return;

        var existing = context.GetMiddlewareState<LanguageServerState>()?
            .DocumentsByPath
            .GetValueOrDefault(resolution.Path);

        var languageId = resolution.PrimaryLanguageId;
        var uri = resolution.Uri;

        var watchedChangeKind = ToWatchedFileChangeKind(mutation.Kind);
        await _languageServerService.NotifyWatchedFileChangedAsync(
            new LanguageServerWatchedFileChangeRequest
            {
                Path = resolution.Path,
                Uri = uri,
                Kind = watchedChangeKind
            },
            cancellationToken).ConfigureAwait(false);
        await context.PublishAsync(new LanguageServerWatchedFileChangedEvent
        {
            SessionId = context.SessionId,
            ThreadId = context.ThreadId,
            Path = resolution.Path,
            Uri = uri,
            ChangeKind = watchedChangeKind
        }, cancellationToken).ConfigureAwait(false);

        if (mutation.Kind == CodingFileMutationKind.Deleted)
        {
            if (existing is not null && existing.Opened)
            {
                await _languageServerService.CloseDocumentAsync(
                    new LanguageServerDocumentCloseRequest
                    {
                        Path = resolution.Path,
                        Uri = uri
                    },
                    cancellationToken).ConfigureAwait(false);
                await context.PublishAsync(new LanguageServerDocumentClosedEvent
                {
                    SessionId = context.SessionId,
                    ThreadId = context.ThreadId,
                    Path = resolution.Path,
                    Uri = uri
                }, cancellationToken).ConfigureAwait(false);
            }

            context.UpdateMiddlewareState<LanguageServerState>(state =>
            {
                var documents = new Dictionary<string, LanguageServerDocumentSnapshot>(
                    state.DocumentsByPath,
                    StringComparer.Ordinal);
                documents.Remove(resolution.Path);

                var diagnostics = new Dictionary<string, LanguageServerDiagnosticSet>(
                    state.DiagnosticsByPath,
                    StringComparer.Ordinal);
                foreach (var key in diagnostics
                             .Where(pair => string.Equals(pair.Value.Path, resolution.Path, StringComparison.Ordinal))
                             .Select(pair => pair.Key)
                             .ToArray())
                {
                    diagnostics.Remove(key);
                }

                return state with
                {
                    DocumentsByPath = documents,
                    DiagnosticsByPath = diagnostics
                };
            });

            return;
        }

        var text = mutation.Text ?? await TryReadDocumentTextAsync(resolution.Path, cancellationToken).ConfigureAwait(false);
        if (text == null)
            return;

        var version = existing == null || existing.LanguageId != languageId
            ? 0
            : existing.Version + 1;

        var diagnosticStartedAt = DateTimeOffset.UtcNow;
        IReadOnlyList<LanguageServerDiagnosticSet> diagnostics;
        var opened = true;
        if (existing is null || !existing.Opened || existing.LanguageId != languageId)
        {
            var openResult = await _languageServerService.OpenDocumentAsync(
                new LanguageServerDocumentOpenRequest
                {
                    Path = resolution.Path,
                    Uri = uri,
                    LanguageId = languageId,
                    Text = text,
                    Version = version,
                    PositionEncoding = existing?.PositionEncoding ?? "utf-16"
                },
                cancellationToken).ConfigureAwait(false);

            diagnostics = openResult.Diagnostics;
            opened = openResult.Opened;
            await context.PublishAsync(new LanguageServerDocumentOpenedEvent
            {
                SessionId = context.SessionId,
                ThreadId = context.ThreadId,
                Path = resolution.Path,
                Uri = uri,
                LanguageId = languageId,
                DocumentVersion = version
            }, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var changeResult = await _languageServerService.ChangeDocumentAsync(
                new LanguageServerDocumentChangeRequest
                {
                    Path = resolution.Path,
                    Uri = uri,
                    Text = text,
                    Version = version
                },
                cancellationToken).ConfigureAwait(false);

            diagnostics = changeResult.Diagnostics;
            await context.PublishAsync(new LanguageServerDocumentChangedEvent
            {
                SessionId = context.SessionId,
                ThreadId = context.ThreadId,
                Path = resolution.Path,
                Uri = uri,
                LanguageId = languageId,
                DocumentVersion = version
            }, cancellationToken).ConfigureAwait(false);
        }

        await _languageServerService.SaveDocumentAsync(
            new LanguageServerDocumentSaveRequest
            {
                Path = resolution.Path,
                Uri = uri,
                Text = text
            },
            cancellationToken).ConfigureAwait(false);
        await context.PublishAsync(new LanguageServerDocumentSavedEvent
        {
            SessionId = context.SessionId,
            ThreadId = context.ThreadId,
            Path = resolution.Path,
            Uri = uri
        }, cancellationToken).ConfigureAwait(false);

        diagnostics = await _languageServerService.GetDiagnosticsAsync(
            new LanguageServerDiagnosticRequest
            {
                Path = resolution.Path,
                Uri = uri,
                Mode = LanguageServerDiagnosticMode.Document,
                DocumentVersion = version,
                StartedAt = diagnosticStartedAt
            },
            cancellationToken).ConfigureAwait(false);
        await context.PublishAsync(new LanguageServerDiagnosticsReceivedEvent
        {
            SessionId = context.SessionId,
            ThreadId = context.ThreadId,
            Path = resolution.Path,
            Uri = uri,
            DiagnosticSetCount = diagnostics.Count,
            ErrorCount = diagnostics.Sum(set => set.Diagnostics.Count(diagnostic =>
                diagnostic.Severity == LanguageServerDiagnosticSeverity.Error)),
            WarningCount = diagnostics.Sum(set => set.Diagnostics.Count(diagnostic =>
                diagnostic.Severity == LanguageServerDiagnosticSeverity.Warning)),
            InformationCount = diagnostics.Sum(set => set.Diagnostics.Count(diagnostic =>
                diagnostic.Severity == LanguageServerDiagnosticSeverity.Information)),
            HintCount = diagnostics.Sum(set => set.Diagnostics.Count(diagnostic =>
                diagnostic.Severity == LanguageServerDiagnosticSeverity.Hint)),
            Diagnostics = CreateDiagnosticSummaries(diagnostics, MaxDiagnosticSummariesPerEvent),
            DiagnosticsTruncated = diagnostics.Sum(set => set.Diagnostics.Count) > MaxDiagnosticSummariesPerEvent
        }, cancellationToken).ConfigureAwait(false);

        await RefreshUnavailableServersAsync(context, cancellationToken).ConfigureAwait(false);

        context.UpdateMiddlewareState<LanguageServerState>(state =>
        {
            var documents = new Dictionary<string, LanguageServerDocumentSnapshot>(
                state.DocumentsByPath,
                StringComparer.Ordinal)
            {
                [resolution.Path] = new LanguageServerDocumentSnapshot
                {
                    Path = resolution.Path,
                    Uri = uri,
                    LanguageId = languageId,
                    Version = version,
                    Opened = opened,
                    PositionEncoding = existing?.PositionEncoding ?? "utf-16",
                    ContentHash = ComputeContentHash(text),
                    LastWriteTimeUtc = mutation.LastWriteTimeUtc,
                    DirtySinceLastDiagnostics = diagnostics.Count == 0,
                    LastObservedAt = DateTimeOffset.UtcNow
                }
            };

            var diagnosticsByPath = new Dictionary<string, LanguageServerDiagnosticSet>(
                state.DiagnosticsByPath,
                StringComparer.Ordinal);

            foreach (var diagnosticSet in diagnostics)
                diagnosticsByPath[CreateDiagnosticStateKey(diagnosticSet)] = diagnosticSet;

            var pendingFeedback = state.PendingFeedback.ToList();
            AddPendingFeedback(pendingFeedback, diagnostics);

            return state with
            {
                DocumentsByPath = documents,
                DiagnosticsByPath = diagnosticsByPath,
                PendingFeedback = pendingFeedback
            };
        });

        var formatted = _formatter.FormatMutationDiagnostics(
            resolution.Path,
            mutation.ToolName,
            diagnostics,
            _options.Feedback);

        if (!string.IsNullOrWhiteSpace(formatted))
            context.Result = AppendToResult(context.Result, formatted);
    }

    private async Task RefreshUnavailableServersAsync(HookContext context, CancellationToken cancellationToken)
    {
        IReadOnlyList<LanguageServerStatus> statuses;
        try
        {
            statuses = await _languageServerService.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return;
        }

        var unavailable = statuses
            .Where(status => status.Status == LanguageServerStatusKind.Unavailable)
            .ToArray();
        if (unavailable.Length == 0)
            return;

        context.UpdateMiddlewareState<LanguageServerState>(state =>
        {
            var unavailableServers = new Dictionary<string, LanguageServerUnavailableServer>(
                state.UnavailableServers,
                StringComparer.Ordinal);

            foreach (var status in unavailable)
            {
                var unavailableServer = new LanguageServerUnavailableServer
                {
                    ServerId = status.ServerId,
                    Root = status.Root,
                    ConfigVersion = _options.ConfigVersion,
                    Reason = status.Message,
                    LastAttemptedAt = DateTimeOffset.UtcNow
                };
                unavailableServers[CreateUnavailableStateKey(unavailableServer)] = unavailableServer;
            }

            return state with { UnavailableServers = unavailableServers };
        });
    }

    private static async Task<string?> TryReadDocumentTextAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists || fileInfo.Length > MaxOpenDocumentBytes)
                return null;

            return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return null;
        }
    }

    private static object AppendToResult(object? result, string appendix)
        => result switch
        {
            null => appendix,
            string text when string.IsNullOrWhiteSpace(text) => appendix,
            string text => $"{text}\n{appendix}",
            _ => $"{result}\n{appendix}"
        };

    private static string ComputeContentHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void AddPendingFeedback(
        List<LanguageServerPendingFeedback> pendingFeedback,
        IReadOnlyList<LanguageServerDiagnosticSet> diagnostics)
    {
        foreach (var diagnosticSet in diagnostics)
        {
            if (diagnosticSet.Diagnostics.Count == 0)
                continue;

            var id = string.Join(
                "|",
                diagnosticSet.ServerId,
                diagnosticSet.Path,
                diagnosticSet.Version?.ToString() ?? "unversioned",
                diagnosticSet.ResultId ?? diagnosticSet.ReceivedAt.ToUnixTimeMilliseconds().ToString());

            if (pendingFeedback.Any(item => item.Id == id))
                continue;

            pendingFeedback.Add(new LanguageServerPendingFeedback
            {
                Id = id,
                DiagnosticSet = diagnosticSet,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
    }

    private static IReadOnlyList<LanguageServerDiagnosticSummary> CreateDiagnosticSummaries(
        IReadOnlyList<LanguageServerDiagnosticSet> diagnostics,
        int maxSummaries)
    {
        if (diagnostics.Count == 0 || maxSummaries <= 0)
            return [];

        var summaries = new List<LanguageServerDiagnosticSummary>(Math.Min(maxSummaries, 8));

        foreach (var set in diagnostics)
        {
            foreach (var diagnostic in set.Diagnostics)
            {
                if (summaries.Count >= maxSummaries)
                    return summaries;

                summaries.Add(new LanguageServerDiagnosticSummary
                {
                    Path = set.Path,
                    ServerId = set.ServerId,
                    Source = set.Source,
                    Severity = diagnostic.Severity,
                    Line = diagnostic.Line,
                    Character = diagnostic.Character,
                    Code = diagnostic.Code,
                    Message = diagnostic.Message
                });
            }
        }

        return summaries;
    }

    private static bool IsObservedCodingFunction(string? toolharnessName, string? functionName)
        => string.Equals(toolharnessName, nameof(CodingToolHarness), StringComparison.Ordinal) &&
           functionName is not null &&
           ObservedFunctionNames.Contains(functionName);

    private static string? TryGetPathArgument(IReadOnlyDictionary<string, object?> arguments)
    {
        foreach (var name in new[] { "path", "Path", "filePath", "FilePath" })
        {
            if (arguments.TryGetValue(name, out var value) && value is not null)
                return value.ToString();
        }

        return null;
    }

    private static string CreateDiagnosticStateKey(LanguageServerDiagnosticSet diagnosticSet)
        => string.Join('\u001f', diagnosticSet.ServerId, diagnosticSet.Path);

    private static string CreateUnavailableStateKey(LanguageServerUnavailableServer unavailableServer)
        => string.Join('\u001f', unavailableServer.ServerId, unavailableServer.Root, unavailableServer.ConfigVersion.ToString());

    private static LanguageServerWatchedFileChangeKind ToWatchedFileChangeKind(CodingFileMutationKind kind)
        => kind switch
        {
            CodingFileMutationKind.Created => LanguageServerWatchedFileChangeKind.Created,
            CodingFileMutationKind.Changed => LanguageServerWatchedFileChangeKind.Changed,
            CodingFileMutationKind.Deleted => LanguageServerWatchedFileChangeKind.Deleted,
            _ => LanguageServerWatchedFileChangeKind.Changed
        };
}
