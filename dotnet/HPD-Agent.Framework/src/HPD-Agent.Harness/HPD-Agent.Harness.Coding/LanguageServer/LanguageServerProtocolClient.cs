using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HPDOS.ToolHarnesses.Middleware;

internal sealed class LanguageServerProtocolClient : IAsyncDisposable
{
    private readonly string _serverId;
    private readonly string _root;
    private readonly LanguageServerLaunchDescriptor _launchDescriptor;
    private readonly LanguageServerOptions _options;
    private readonly IReadOnlyDictionary<string, object?> _workspaceConfiguration;
    private readonly IReadOnlyDictionary<string, object?> _clientCapabilityOverrides;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode?>> _pendingRequests = new();
    private readonly ConcurrentDictionary<string, LanguageServerDiagnosticSet> _pushDiagnostics = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, LanguageServerDiagnosticSet> _pullDiagnostics = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, LanguageServerDynamicRegistration> _dynamicRegistrations = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly object _diagnosticRegistrationSignalLock = new();
    private TaskCompletionSource<int> _diagnosticRegistrationSignal =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Process? _process;
    private Stream? _stdin;
    private Stream? _stdout;
    private Task? _readLoop;
    private int _nextRequestId;
    private int _diagnosticRegistrationVersion;
    private bool _shutdownStarted;

    public LanguageServerProtocolClient(
        string serverId,
        string root,
        LanguageServerLaunchDescriptor launchDescriptor,
        LanguageServerOptions options,
        LanguageServerInitialization? initialization = null)
    {
        _serverId = serverId;
        _root = root;
        _launchDescriptor = launchDescriptor;
        _options = options;
        _workspaceConfiguration = MergeConfiguration(
            options.WorkspaceConfiguration,
            initialization?.WorkspaceConfiguration);
        _clientCapabilityOverrides = initialization?.ClientCapabilities
            ?? new Dictionary<string, object?>(StringComparer.Ordinal);
    }

    public LanguageServerClientCapabilities Capabilities { get; private set; } = new();

    public bool IsRunning => _process is { HasExited: false };

    public IReadOnlyList<LanguageServerDiagnosticSet> CurrentDiagnostics
        => GetMergedCurrentDiagnostics();

    public IReadOnlyDictionary<string, LanguageServerDynamicRegistration> DynamicRegistrations => _dynamicRegistrations;

    public bool DiagnosticsRefreshRequested { get; private set; }

    internal void AcceptPublishedDiagnosticsForTesting(JsonNode? parameters)
        => HandleNotification("textDocument/publishDiagnostics", parameters);

    internal JsonObject RegisterCapabilityForTesting(JsonNode? parameters)
        => HandleRegisterCapability(parameters);

    internal JsonObject UnregisterCapabilityForTesting(JsonNode? parameters)
        => HandleUnregisterCapability(parameters);

    internal JsonObject CreateInitializeParametersForTesting()
        => CreateInitializeParameters();

    internal JsonArray HandleWorkspaceConfigurationForTesting(JsonNode? parameters)
        => HandleWorkspaceConfiguration(parameters);

    internal IReadOnlyList<LanguageServerDynamicRegistration> GetDiagnosticRegistrationsForTesting(
        bool requireWorkspaceDiagnostics)
        => GetDiagnosticRegistrations(requireWorkspaceDiagnostics);

    internal LanguageServerDiagnosticPullResult ParseDocumentDiagnosticReportForTesting(
        string path,
        string uri,
        JsonNode? result)
        => ParseDocumentDiagnosticReport(path, uri, result);

    internal JsonArray CreateDidChangeContentChangesForTesting(
        LanguageServerTextDocumentSyncKind changeKind,
        string text,
        string previousText)
        => CreateDidChangeContentChanges(changeKind, text, previousText);

    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        if (_process != null)
            return;

        var startInfo = new ProcessStartInfo
        {
            FileName = _launchDescriptor.FileName,
            WorkingDirectory = _launchDescriptor.WorkingDirectory ?? _root,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in _launchDescriptor.Arguments)
            startInfo.ArgumentList.Add(argument);

        foreach (var pair in _launchDescriptor.Environment)
            startInfo.Environment[pair.Key] = pair.Value;

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
            throw new InvalidOperationException($"Language server '{_serverId}' did not start.");

        _process = process;
        _stdin = process.StandardInput.BaseStream;
        _stdout = process.StandardOutput.BaseStream;
        _readLoop = Task.Run(() => ReadLoopAsync(_disposeCts.Token), CancellationToken.None);

        await InitializeAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DidOpenAsync(LanguageServerDocumentOpenRequest request, CancellationToken cancellationToken)
    {
        if (!Capabilities.OpenClose)
            return;

        await SendNotificationAsync(
            "textDocument/didOpen",
            new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = request.Uri,
                    ["languageId"] = request.LanguageId,
                    ["version"] = request.Version,
                    ["text"] = request.Text
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DidChangeAsync(
        LanguageServerDocumentChangeRequest request,
        string previousText,
        CancellationToken cancellationToken)
    {
        if (Capabilities.Change == LanguageServerTextDocumentSyncKind.None)
            return;

        await SendNotificationAsync(
            "textDocument/didChange",
            new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = request.Uri,
                    ["version"] = request.Version
                },
                ["contentChanges"] = CreateDidChangeContentChanges(Capabilities.Change, request.Text, previousText)
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static JsonArray CreateDidChangeContentChanges(
        LanguageServerTextDocumentSyncKind changeKind,
        string text,
        string previousText)
    {
        var contentChanges = new JsonArray();
        if (changeKind == LanguageServerTextDocumentSyncKind.Incremental)
        {
            var end = GetEndPosition(previousText);
            contentChanges.Add((JsonNode)new JsonObject
            {
                ["range"] = new JsonObject
                {
                    ["start"] = new JsonObject
                    {
                        ["line"] = 0,
                        ["character"] = 0
                    },
                    ["end"] = new JsonObject
                    {
                        ["line"] = end.Line,
                        ["character"] = end.Character
                    }
                },
                ["text"] = text
            });
            return contentChanges;
        }

        contentChanges.Add((JsonNode)new JsonObject
        {
            ["text"] = text
        });
        return contentChanges;
    }

    private static (int Line, int Character) GetEndPosition(string text)
    {
        var line = 0;
        var character = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (current == '\r')
            {
                line++;
                character = 0;
                if (index + 1 < text.Length && text[index + 1] == '\n')
                    index++;
                continue;
            }

            if (current == '\n')
            {
                line++;
                character = 0;
                continue;
            }

            character++;
        }

        return (line, character);
    }

    public async ValueTask DidSaveAsync(LanguageServerDocumentSaveRequest request, CancellationToken cancellationToken)
    {
        if (!Capabilities.Save)
            return;

        var textDocument = new JsonObject { ["uri"] = request.Uri };
        var parameters = new JsonObject { ["textDocument"] = textDocument };
        if (Capabilities.IncludeTextOnSave && request.Text is not null)
            parameters["text"] = request.Text;

        await SendNotificationAsync("textDocument/didSave", parameters, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DidCloseAsync(LanguageServerDocumentCloseRequest request, CancellationToken cancellationToken)
    {
        if (!Capabilities.OpenClose)
            return;

        await SendNotificationAsync(
            "textDocument/didClose",
            new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = request.Uri }
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DidChangeWatchedFilesAsync(
        LanguageServerWatchedFileChangeRequest request,
        CancellationToken cancellationToken)
    {
        var changes = new JsonArray();
        changes.Add((JsonNode)new JsonObject
        {
            ["uri"] = request.Uri,
            ["type"] = (int)request.Kind
        });

        await SendNotificationAsync(
            "workspace/didChangeWatchedFiles",
            new JsonObject { ["changes"] = changes },
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<LanguageServerDiagnosticSet>> PullDiagnosticsAsync(
        LanguageServerDiagnosticRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Mode == LanguageServerDiagnosticMode.None)
            return GetCurrentDiagnosticsForPath(request.Path);

        var timeout = request.Timeout <= TimeSpan.Zero ? TimeSpan.Zero : request.Timeout;
        var deadline = DateTimeOffset.UtcNow + timeout;
        var registrationVersion = GetDiagnosticRegistrationVersion();
        var freshPush = WaitForFreshPushDiagnosticsAsync(request, cancellationToken).AsTask();

        while (true)
        {
            var pull = request.Mode == LanguageServerDiagnosticMode.Workspace
                ? await RequestWorkspaceDiagnosticsAsync(request, cancellationToken).ConfigureAwait(false)
                : await RequestDocumentDiagnosticsAsync(request, cancellationToken).ConfigureAwait(false);

            if (pull.MatchedRequestedDocument)
                return GetCurrentDiagnosticsForPath(request.Path);

            if (timeout == TimeSpan.Zero || DateTimeOffset.UtcNow >= deadline)
                return GetCurrentDiagnosticsForPath(request.Path);

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return GetCurrentDiagnosticsForPath(request.Path);

            var registrationChange = WaitForDiagnosticRegistrationChangeAsync(
                registrationVersion,
                remaining,
                cancellationToken).AsTask();

            var delay = Task.Delay(remaining, cancellationToken);
            var completed = await Task.WhenAny(freshPush, registrationChange, delay).ConfigureAwait(false);

            if (completed == freshPush)
            {
                if (await freshPush.ConfigureAwait(false))
                    return GetCurrentDiagnosticsForPath(request.Path);

                freshPush = WaitForFreshPushDiagnosticsAsync(request, cancellationToken).AsTask();
                continue;
            }

            if (completed == registrationChange)
            {
                if (await registrationChange.ConfigureAwait(false))
                {
                    registrationVersion = GetDiagnosticRegistrationVersion();
                    continue;
                }

                return GetCurrentDiagnosticsForPath(request.Path);
            }

            return GetCurrentDiagnosticsForPath(request.Path);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_shutdownStarted)
            return;

        _shutdownStarted = true;
        try
        {
            if (IsRunning)
            {
                using var shutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await SendRequestAsync("shutdown", null, TimeSpan.FromSeconds(2), shutdownCts.Token).ConfigureAwait(false);
                await SendNotificationAsync("exit", null, shutdownCts.Token).ConfigureAwait(false);
            }
        }
        catch
        {
        }
        finally
        {
            _disposeCts.Cancel();
            try
            {
                if (_process is { HasExited: false })
                    _process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            if (_readLoop is not null)
            {
                try { await _readLoop.ConfigureAwait(false); }
                catch { }
            }

            _process?.Dispose();
            _disposeCts.Dispose();
            _sendLock.Dispose();
        }
    }

    private async ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        var parameters = CreateInitializeParameters();
        var result = await SendRequestAsync(
            "initialize",
            parameters,
            TimeSpan.FromSeconds(45),
            cancellationToken).ConfigureAwait(false);

        Capabilities = ParseServerCapabilities(result);
        await SendNotificationAsync("initialized", new JsonObject(), cancellationToken).ConfigureAwait(false);
    }

    private JsonObject CreateInitializeParameters()
    {
        var initializationOptions = new JsonObject();
        foreach (var pair in _launchDescriptor.InitializationOptions)
            initializationOptions[pair.Key] = ToJsonNode(pair.Value);

        foreach (var pair in _workspaceConfiguration)
        {
            if (!initializationOptions.ContainsKey(pair.Key))
                initializationOptions[pair.Key] = ToJsonNode(pair.Value);
        }

        var workspaceFolders = new JsonArray();
        workspaceFolders.Add((JsonNode)new JsonObject
        {
            ["uri"] = new Uri(_root).AbsoluteUri,
            ["name"] = Path.GetFileName(_root)
        });

        return new JsonObject
        {
            ["processId"] = Environment.ProcessId,
            ["rootUri"] = new Uri(_root).AbsoluteUri,
            ["workspaceFolders"] = workspaceFolders,
            ["capabilities"] = CreateClientCapabilities(),
            ["initializationOptions"] = initializationOptions
        };
    }

    private JsonObject CreateClientCapabilities()
    {
        var capabilities = new JsonObject
        {
            ["general"] = new JsonObject
            {
                ["positionEncodings"] = new JsonArray("utf-16")
            },
            ["textDocument"] = new JsonObject
            {
                ["synchronization"] = new JsonObject
                {
                    ["didSave"] = true
                },
                ["publishDiagnostics"] = new JsonObject
                {
                    ["relatedInformation"] = true,
                    ["versionSupport"] = true
                },
                ["diagnostic"] = new JsonObject
                {
                    ["dynamicRegistration"] = true,
                    ["relatedDocumentSupport"] = true
                }
            },
            ["workspace"] = new JsonObject
            {
                ["configuration"] = true,
                ["workspaceFolders"] = true,
                ["didChangeWatchedFiles"] = new JsonObject
                {
                    ["dynamicRegistration"] = true
                },
                ["diagnostics"] = new JsonObject
                {
                    ["refreshSupport"] = true
                }
            }
        };

        MergeInto(capabilities, _clientCapabilityOverrides);
        return capabilities;
    }

    private async Task<JsonNode?> SendRequestAsync(
        string method,
        JsonNode? parameters,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (_shutdownStarted && method != "shutdown")
            throw new InvalidOperationException("Language server client is shutting down.");

        var id = Interlocked.Increment(ref _nextRequestId);
        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[id] = tcs;

        await SendMessageAsync(
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method,
                ["params"] = parameters
            },
            cancellationToken).ConfigureAwait(false);

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        await using (linked.Token.Register(() => tcs.TrySetCanceled(linked.Token)))
        {
            try
            {
                return await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                _pendingRequests.TryRemove(id, out _);
            }
        }
    }

    private ValueTask SendNotificationAsync(string method, JsonNode? parameters, CancellationToken cancellationToken)
    {
        if (_shutdownStarted && method != "exit")
            return ValueTask.CompletedTask;

        return SendMessageAsync(
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
                ["params"] = parameters
            },
            cancellationToken);
    }

    private async ValueTask SendMessageAsync(JsonObject message, CancellationToken cancellationToken)
    {
        if (_stdin is null)
            throw new InvalidOperationException("Language server process is not started.");

        var payload = Encoding.UTF8.GetBytes(message.ToJsonString());
        var header = Encoding.ASCII.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"Content-Length: {payload.Length}\r\n\r\n"));

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stdin.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await _stdin.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await _stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        if (_stdout is null)
            return;

        while (!cancellationToken.IsCancellationRequested)
        {
            var message = await ReadMessageAsync(_stdout, cancellationToken).ConfigureAwait(false);
            if (message is null)
                return;

            HandleMessage(message);
        }
    }

    private async Task<JsonObject?> ReadMessageAsync(Stream stream, CancellationToken cancellationToken)
    {
        var contentLength = 0;
        while (true)
        {
            var line = await ReadAsciiLineAsync(stream, cancellationToken).ConfigureAwait(false);
            if (line is null)
                return null;

            if (line.Length == 0)
                break;

            const string contentLengthHeader = "Content-Length:";
            if (line.StartsWith(contentLengthHeader, StringComparison.OrdinalIgnoreCase))
            {
                var value = line.Substring(contentLengthHeader.Length).Trim();
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out contentLength);
            }
        }

        if (contentLength <= 0)
            return null;

        var buffer = new byte[contentLength];
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return null;

            offset += read;
        }

        return JsonNode.Parse(buffer)?.AsObject();
    }

    private static async Task<string?> ReadAsciiLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        while (true)
        {
            var buffer = new byte[1];
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return bytes.Count == 0 ? null : Encoding.ASCII.GetString(bytes.ToArray());

            if (buffer[0] == (byte)'\n')
            {
                if (bytes.Count > 0 && bytes[^1] == (byte)'\r')
                    bytes.RemoveAt(bytes.Count - 1);

                return Encoding.ASCII.GetString(bytes.ToArray());
            }

            bytes.Add(buffer[0]);
        }
    }

    private void HandleMessage(JsonObject message)
    {
        if (message.TryGetPropertyValue("id", out var idNode) &&
            message.TryGetPropertyValue("method", out var requestMethodNode) &&
            requestMethodNode is not null)
        {
            _ = HandleServerRequestAsync(idNode, requestMethodNode.GetValue<string>(), message["params"]);
            return;
        }

        if (message.TryGetPropertyValue("id", out idNode) &&
            idNode is not null &&
            _pendingRequests.TryRemove(idNode.GetValue<int>(), out var pending))
        {
            if (message.TryGetPropertyValue("error", out var errorNode) && errorNode is not null)
                pending.TrySetException(new InvalidOperationException(errorNode.ToJsonString()));
            else
                pending.TrySetResult(message["result"]);

            return;
        }

        if (message.TryGetPropertyValue("method", out var methodNode) && methodNode is not null)
            HandleNotification(methodNode.GetValue<string>(), message["params"]);
    }

    private async Task HandleServerRequestAsync(JsonNode? idNode, string method, JsonNode? parameters)
    {
        var workspaceFolders = new JsonArray();
        workspaceFolders.Add((JsonNode)new JsonObject
        {
            ["uri"] = new Uri(_root).AbsoluteUri,
            ["name"] = Path.GetFileName(_root)
        });

        JsonNode? result = method switch
        {
            "workspace/configuration" => HandleWorkspaceConfiguration(parameters),
            "workspace/workspaceFolders" => workspaceFolders,
            "client/registerCapability" => HandleRegisterCapability(parameters),
            "client/unregisterCapability" => HandleUnregisterCapability(parameters),
            "workspace/diagnostic/refresh" => HandleDiagnosticRefresh(),
            _ => null
        };

        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = idNode?.DeepClone()
        };

        if (result is null && method is not ("workspace/configuration" or "workspace/workspaceFolders" or "client/registerCapability" or "client/unregisterCapability" or "workspace/diagnostic/refresh"))
        {
            response["error"] = new JsonObject
            {
                ["code"] = -32601,
                ["message"] = $"Unsupported request method '{method}'."
            };
        }
        else
        {
            response["result"] = result;
        }

        try
        {
            await SendMessageAsync(response, _disposeCts.Token).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private JsonArray HandleWorkspaceConfiguration(JsonNode? parameters)
    {
        var result = new JsonArray();
        var items = parameters?["items"]?.AsArray();
        if (items is null)
            return result;

        foreach (var item in items)
        {
            var section = item?["section"]?.GetValue<string>();
            if (section is not null && _workspaceConfiguration.TryGetValue(section, out var value))
                result.Add(ToJsonNode(value));
            else
                result.Add(null);
        }

        return result;
    }

    private JsonObject HandleRegisterCapability(JsonNode? parameters)
    {
        var registrations = parameters?["registrations"]?.AsArray();
        if (registrations is null)
            return new JsonObject();

        var changed = false;
        foreach (var registration in registrations)
        {
            var id = registration?["id"]?.GetValue<string>();
            var method = registration?["method"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(method))
                continue;

            var options = registration?["registerOptions"];
            _dynamicRegistrations[id] = new LanguageServerDynamicRegistration
            {
                Id = id,
                Method = method,
                Identifier = options?["identifier"]?.GetValue<string>(),
                WorkspaceDiagnostics = options?["workspaceDiagnostics"]?.GetValue<bool?>() ?? false
            };

            if (method == "textDocument/diagnostic")
                changed = true;
        }

        if (changed)
            SignalDiagnosticRegistrationChanged();

        return new JsonObject();
    }

    private JsonObject HandleUnregisterCapability(JsonNode? parameters)
    {
        var unregisterations = parameters?["unregisterations"]?.AsArray();
        if (unregisterations is null)
            unregisterations = parameters?["unregistrations"]?.AsArray();
        if (unregisterations is null)
            return new JsonObject();

        var changed = false;
        foreach (var registration in unregisterations)
        {
            var id = registration?["id"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(id) &&
                _dynamicRegistrations.TryRemove(id, out var removed) &&
                removed.Method == "textDocument/diagnostic")
            {
                changed = true;
            }
        }

        if (changed)
            SignalDiagnosticRegistrationChanged();

        return new JsonObject();
    }

    private JsonObject HandleDiagnosticRefresh()
    {
        DiagnosticsRefreshRequested = true;
        return new JsonObject();
    }

    private static JsonNode? ToJsonNode(object? value)
        => value switch
        {
            null => null,
            JsonNode node => node.DeepClone(),
            string text => JsonValue.Create(text),
            bool boolean => JsonValue.Create(boolean),
            int number => JsonValue.Create(number),
            long number => JsonValue.Create(number),
            double number => JsonValue.Create(number),
            float number => JsonValue.Create(number),
            decimal number => JsonValue.Create(number),
            IReadOnlyDictionary<string, object?> dictionary => DictionaryToJsonObject(dictionary),
            IEnumerable<object?> values => EnumerableToJsonArray(values),
            _ => JsonValue.Create(value.ToString())
        };

    private static JsonObject DictionaryToJsonObject(IReadOnlyDictionary<string, object?> dictionary)
    {
        var result = new JsonObject();
        foreach (var pair in dictionary)
            result[pair.Key] = ToJsonNode(pair.Value);

        return result;
    }

    private static JsonArray EnumerableToJsonArray(IEnumerable<object?> values)
    {
        var result = new JsonArray();
        foreach (var value in values)
            result.Add(ToJsonNode(value));

        return result;
    }

    private void HandleNotification(string method, JsonNode? parameters)
    {
        if (method != "textDocument/publishDiagnostics")
            return;

        var uri = parameters?["uri"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(uri))
            return;

        var path = TryGetPathFromUri(uri);
        if (path is null)
            return;

        var version = parameters?["version"]?.GetValue<int?>();
        var diagnostics = ParseDiagnostics(parameters?["diagnostics"]?.AsArray());
        if (version is not null &&
            _pushDiagnostics.TryGetValue(uri, out var existing) &&
            existing.Version is not null &&
            existing.Version > version)
        {
            return;
        }

        _pushDiagnostics[uri] = new LanguageServerDiagnosticSet
        {
            Path = path,
            ServerId = _serverId,
            Source = LanguageServerDiagnosticSource.Publish,
            Version = version,
            Diagnostics = diagnostics,
            ReceivedAt = DateTimeOffset.UtcNow
        };
    }

    private async ValueTask<bool> WaitForFreshPushDiagnosticsAsync(
        LanguageServerDiagnosticRequest request,
        CancellationToken cancellationToken)
    {
        var timeout = request.Timeout <= TimeSpan.Zero ? TimeSpan.Zero : request.Timeout;
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            var diagnostics = GetFreshPushDiagnostics(request);
            if (diagnostics.Length > 0)
            {
                if (request.Debounce > TimeSpan.Zero)
                {
                    var remaining = deadline - DateTimeOffset.UtcNow;
                    var debounce = request.Debounce < remaining ? request.Debounce : remaining;
                    if (debounce > TimeSpan.Zero)
                        await Task.Delay(debounce, cancellationToken).ConfigureAwait(false);

                    diagnostics = GetFreshPushDiagnostics(request);
                }

                return diagnostics.Length > 0;
            }

            if (timeout == TimeSpan.Zero || DateTimeOffset.UtcNow >= deadline)
                return false;

            var waitRemaining = deadline - DateTimeOffset.UtcNow;
            var delay = waitRemaining < TimeSpan.FromMilliseconds(50)
                ? waitRemaining
                : TimeSpan.FromMilliseconds(50);
            if (delay <= TimeSpan.Zero)
                return false;

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private LanguageServerDiagnosticSet[] GetFreshPushDiagnostics(LanguageServerDiagnosticRequest request)
        => _pushDiagnostics.Values
                .Where(set => IsFreshDiagnosticSet(set, request))
                .ToArray();

    private static bool IsFreshDiagnosticSet(
        LanguageServerDiagnosticSet set,
        LanguageServerDiagnosticRequest request)
    {
        if (!string.Equals(set.Path, request.Path, StringComparison.Ordinal))
            return false;

        if (set.Version is not null)
            return set.Version == request.DocumentVersion;

        return set.ReceivedAt >= request.StartedAt;
    }

    private IReadOnlyList<LanguageServerDiagnosticSet> GetCurrentDiagnosticsForPath(string path)
        => CurrentDiagnostics
            .Where(set => string.Equals(set.Path, path, StringComparison.Ordinal))
            .ToArray();

    private IReadOnlyList<LanguageServerDiagnosticSet> GetMergedCurrentDiagnostics()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<LanguageServerDiagnosticSet>();

        foreach (var set in _pushDiagnostics.Values.Concat(_pullDiagnostics.Values))
        {
            var diagnostics = new List<LanguageServerDiagnostic>();
            foreach (var diagnostic in set.Diagnostics)
            {
                var key = CreateDiagnosticKey(set.ServerId, set.Path, diagnostic);
                if (seen.Add(key))
                    diagnostics.Add(diagnostic);
            }

            result.Add(set with { Diagnostics = diagnostics });
        }

        return result;
    }

    private LanguageServerDynamicRegistration[] GetDiagnosticRegistrations(bool requireWorkspaceDiagnostics)
        => _dynamicRegistrations.Values
            .Where(registration =>
                registration.Method == "textDocument/diagnostic" &&
                (!requireWorkspaceDiagnostics || registration.WorkspaceDiagnostics))
            .ToArray();

    private int GetDiagnosticRegistrationVersion()
    {
        lock (_diagnosticRegistrationSignalLock)
            return _diagnosticRegistrationVersion;
    }

    private ValueTask<bool> WaitForDiagnosticRegistrationChangeAsync(
        int observedVersion,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Task<int> signalTask;
        lock (_diagnosticRegistrationSignalLock)
        {
            if (_diagnosticRegistrationVersion != observedVersion)
                return ValueTask.FromResult(true);

            signalTask = _diagnosticRegistrationSignal.Task;
        }

        return WaitForDiagnosticRegistrationChangeCoreAsync(signalTask, timeout, cancellationToken);
    }

    private static async ValueTask<bool> WaitForDiagnosticRegistrationChangeCoreAsync(
        Task<int> signalTask,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
            return false;

        var completed = await Task.WhenAny(signalTask, Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);
        return completed == signalTask;
    }

    private void SignalDiagnosticRegistrationChanged()
    {
        TaskCompletionSource<int> previous;
        var version = Interlocked.Increment(ref _diagnosticRegistrationVersion);
        lock (_diagnosticRegistrationSignalLock)
        {
            previous = _diagnosticRegistrationSignal;
            _diagnosticRegistrationSignal = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        previous.TrySetResult(version);
    }

    private static string CreateDiagnosticKey(
        string serverId,
        string path,
        LanguageServerDiagnostic diagnostic)
        => string.Join(
            '\u001f',
            serverId,
            path,
            ((int)diagnostic.Severity).ToString(CultureInfo.InvariantCulture),
            diagnostic.Line.ToString(CultureInfo.InvariantCulture),
            diagnostic.Character.ToString(CultureInfo.InvariantCulture),
            diagnostic.Code ?? string.Empty,
            diagnostic.Message);

    private static async ValueTask<LanguageServerDiagnosticPullResult> MergePullResultsAsync(
        string requestedPath,
        IReadOnlyList<Task<LanguageServerDiagnosticPullResult>> tasks)
    {
        if (tasks.Count == 0)
            return LanguageServerDiagnosticPullResult.NotHandled;

        var sets = new List<LanguageServerDiagnosticSet>();
        var handled = false;
        var matched = false;
        foreach (var task in tasks)
        {
            var result = await task.ConfigureAwait(false);
            handled |= result.Handled;
            matched |= result.MatchedRequestedDocument;
            sets.AddRange(result.Sets);
        }

        return new LanguageServerDiagnosticPullResult
        {
            Handled = handled,
            MatchedRequestedDocument = matched ||
                sets.Any(set => string.Equals(set.Path, requestedPath, StringComparison.Ordinal)),
            Sets = sets
        };
    }

    private async ValueTask<LanguageServerDiagnosticPullResult> RequestDocumentDiagnosticsAsync(
        LanguageServerDiagnosticRequest request,
        CancellationToken cancellationToken)
    {
        var registrations = GetDiagnosticRegistrations(requireWorkspaceDiagnostics: false);
        if (!Capabilities.DocumentDiagnostics && registrations.Length == 0)
            return LanguageServerDiagnosticPullResult.NotHandled;

        var tasks = new List<Task<LanguageServerDiagnosticPullResult>>();
        if (Capabilities.DocumentDiagnostics)
            tasks.Add(RequestDocumentDiagnosticReportAsync(request, identifier: null, cancellationToken).AsTask());

        foreach (var registration in registrations)
            tasks.Add(RequestDocumentDiagnosticReportAsync(request, registration.Identifier, cancellationToken).AsTask());

        return await MergePullResultsAsync(request.Path, tasks).ConfigureAwait(false);
    }

    private async ValueTask<LanguageServerDiagnosticPullResult> RequestWorkspaceDiagnosticsAsync(
        LanguageServerDiagnosticRequest request,
        CancellationToken cancellationToken)
    {
        var tasks = new List<Task<LanguageServerDiagnosticPullResult>>();
        if (Capabilities.DocumentDiagnostics)
            tasks.Add(RequestDocumentDiagnosticReportAsync(request, identifier: null, cancellationToken).AsTask());

        foreach (var registration in GetDiagnosticRegistrations(requireWorkspaceDiagnostics: false))
            tasks.Add(RequestDocumentDiagnosticReportAsync(request, registration.Identifier, cancellationToken).AsTask());

        if (Capabilities.WorkspaceDiagnostics)
            tasks.Add(RequestWorkspaceDiagnosticReportAsync(request, identifier: null, cancellationToken).AsTask());

        foreach (var registration in GetDiagnosticRegistrations(requireWorkspaceDiagnostics: true))
            tasks.Add(RequestWorkspaceDiagnosticReportAsync(request, registration.Identifier, cancellationToken).AsTask());

        return await MergePullResultsAsync(request.Path, tasks).ConfigureAwait(false);
    }

    private async ValueTask<LanguageServerDiagnosticPullResult> RequestDocumentDiagnosticReportAsync(
        LanguageServerDiagnosticRequest request,
        string? identifier,
        CancellationToken cancellationToken)
    {
        JsonNode? result;
        try
        {
            result = await SendRequestAsync(
                "textDocument/diagnostic",
                new JsonObject
                {
                    ["identifier"] = identifier,
                    ["textDocument"] = new JsonObject { ["uri"] = request.Uri }
                },
                request.RequestTimeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return LanguageServerDiagnosticPullResult.NotHandled;
        }

        return ParseDocumentDiagnosticReport(request.Path, request.Uri, result);
    }

    private async ValueTask<LanguageServerDiagnosticPullResult> RequestWorkspaceDiagnosticReportAsync(
        LanguageServerDiagnosticRequest request,
        string? identifier,
        CancellationToken cancellationToken)
    {
        JsonNode? result;
        try
        {
            result = await SendRequestAsync(
                "workspace/diagnostic",
                new JsonObject
                {
                    ["identifier"] = identifier,
                    ["previousResultIds"] = new JsonArray()
                },
                request.RequestTimeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return LanguageServerDiagnosticPullResult.NotHandled;
        }

        return ParseWorkspaceDiagnosticReport(request.Path, result);
    }

    private LanguageServerDiagnosticPullResult ParseDocumentDiagnosticReport(
        string path,
        string uri,
        JsonNode? result)
    {
        if (result is null)
            return LanguageServerDiagnosticPullResult.NotHandled;

        var kind = result?["kind"]?.GetValue<string>();
        if (kind == "unchanged")
        {
            if (!_pullDiagnostics.TryGetValue(uri, out var existing))
                return LanguageServerDiagnosticPullResult.NotHandled;

            var unchanged = existing with
            {
                ResultId = result?["resultId"]?.GetValue<string>() ?? existing.ResultId,
                ReceivedAt = DateTimeOffset.UtcNow
            };
            _pullDiagnostics[uri] = unchanged;
            return new LanguageServerDiagnosticPullResult
            {
                Handled = true,
                MatchedRequestedDocument = string.Equals(existing.Path, path, StringComparison.Ordinal),
                Sets = [unchanged]
            };
        }

        var sets = new List<LanguageServerDiagnosticSet>();
        if (result["items"] is JsonArray items)
        {
            sets.Add(new LanguageServerDiagnosticSet
            {
                Path = path,
                ServerId = _serverId,
                Source = LanguageServerDiagnosticSource.DocumentPull,
                ResultId = result?["resultId"]?.GetValue<string>(),
                Diagnostics = ParseDiagnostics(items),
                ReceivedAt = DateTimeOffset.UtcNow
            });
        }

        var relatedDocuments = result?["relatedDocuments"]?.AsObject();
        if (relatedDocuments is not null)
        {
            foreach (var related in relatedDocuments)
            {
                var relatedPath = TryGetPathFromUri(related.Key);
                if (relatedPath is null)
                    continue;

                if (related.Value?["kind"]?.GetValue<string>() == "unchanged")
                {
                    if (_pullDiagnostics.TryGetValue(related.Key, out var existingRelated))
                    {
                        sets.Add(existingRelated with
                        {
                            ResultId = related.Value?["resultId"]?.GetValue<string>() ?? existingRelated.ResultId,
                            ReceivedAt = DateTimeOffset.UtcNow
                        });
                    }

                    continue;
                }

                if (related.Value?["items"] is not JsonArray relatedItems)
                    continue;

                sets.Add(new LanguageServerDiagnosticSet
                {
                    Path = relatedPath,
                    ServerId = _serverId,
                    Source = LanguageServerDiagnosticSource.DocumentPull,
                    ResultId = related.Value?["resultId"]?.GetValue<string>(),
                    Diagnostics = ParseDiagnostics(relatedItems),
                    ReceivedAt = DateTimeOffset.UtcNow
                });
            }
        }

        foreach (var set in sets)
            _pullDiagnostics[new Uri(set.Path).AbsoluteUri] = set;

        return new LanguageServerDiagnosticPullResult
        {
            Handled = sets.Count > 0,
            MatchedRequestedDocument = sets.Any(set => string.Equals(set.Path, path, StringComparison.Ordinal)),
            Sets = sets
        };
    }

    private LanguageServerDiagnosticPullResult ParseWorkspaceDiagnosticReport(string requestedPath, JsonNode? result)
    {
        var items = result?["items"]?.AsArray();
        if (items is null)
            return LanguageServerDiagnosticPullResult.NotHandled;

        var diagnosticSetsByUri = new Dictionary<string, LanguageServerDiagnosticSet>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var kind = item?["kind"]?.GetValue<string>();
            if (kind == "unchanged")
                continue;

            var uri = item?["uri"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(uri))
                continue;

            var path = TryGetPathFromUri(uri);
            if (path is null)
                continue;

            diagnosticSetsByUri[uri] = new LanguageServerDiagnosticSet
            {
                Path = path,
                ServerId = _serverId,
                Source = LanguageServerDiagnosticSource.WorkspacePull,
                ResultId = item?["resultId"]?.GetValue<string>(),
                Diagnostics = ParseDiagnostics(item?["items"]?.AsArray()),
                ReceivedAt = DateTimeOffset.UtcNow
            };
        }

        foreach (var diagnosticSet in diagnosticSetsByUri)
            _pullDiagnostics[diagnosticSet.Key] = diagnosticSet.Value;

        var sets = diagnosticSetsByUri.Values.ToArray();
        return new LanguageServerDiagnosticPullResult
        {
            Handled = true,
            MatchedRequestedDocument = sets.Any(set => string.Equals(set.Path, requestedPath, StringComparison.Ordinal)),
            Sets = sets
        };
    }

    private static IReadOnlyDictionary<string, object?> MergeConfiguration(
        IReadOnlyDictionary<string, object?> baseConfiguration,
        IReadOnlyDictionary<string, object?>? overrideConfiguration)
    {
        if (overrideConfiguration is null || overrideConfiguration.Count == 0)
            return baseConfiguration;

        var merged = new Dictionary<string, object?>(baseConfiguration, StringComparer.Ordinal);
        foreach (var pair in overrideConfiguration)
            merged[pair.Key] = pair.Value;

        return merged;
    }

    private static void MergeInto(JsonObject target, IReadOnlyDictionary<string, object?> values)
    {
        foreach (var pair in values)
        {
            var value = ToJsonNode(pair.Value);
            if (value is JsonObject valueObject &&
                target[pair.Key] is JsonObject existingObject)
            {
                MergeInto(existingObject, valueObject);
                continue;
            }

            target[pair.Key] = value;
        }
    }

    private static void MergeInto(JsonObject target, JsonObject values)
    {
        foreach (var pair in values)
        {
            var value = pair.Value?.DeepClone();
            if (value is JsonObject valueObject &&
                target[pair.Key] is JsonObject existingObject)
            {
                MergeInto(existingObject, valueObject);
                continue;
            }

            target[pair.Key] = value;
        }
    }

    private static IReadOnlyList<LanguageServerDiagnostic> ParseDiagnostics(JsonArray? array)
    {
        if (array is null)
            return [];

        var diagnostics = new List<LanguageServerDiagnostic>();
        foreach (var node in array)
        {
            var range = node?["range"];
            var start = range?["start"];
            diagnostics.Add(new LanguageServerDiagnostic
            {
                Severity = (LanguageServerDiagnosticSeverity)(node?["severity"]?.GetValue<int?>() ?? 1),
                Line = start?["line"]?.GetValue<int?>() ?? 0,
                Character = start?["character"]?.GetValue<int?>() ?? 0,
                Code = node?["code"]?.ToString(),
                Message = node?["message"]?.GetValue<string>() ?? string.Empty
            });
        }

        return diagnostics;
    }

    private static LanguageServerClientCapabilities ParseServerCapabilities(JsonNode? initializeResult)
    {
        var capabilities = initializeResult?["capabilities"];
        var positionEncoding = capabilities?["positionEncoding"]?.GetValue<string>() ?? "utf-16";
        var sync = capabilities?["textDocumentSync"];

        var openClose = false;
        var change = LanguageServerTextDocumentSyncKind.None;
        var save = false;
        var includeText = false;

        if (sync is JsonValue syncValue && syncValue.TryGetValue<int>(out var numericSync))
        {
            openClose = numericSync != 0;
            change = (LanguageServerTextDocumentSyncKind)numericSync;
        }
        else if (sync is JsonObject syncObject)
        {
            openClose = syncObject["openClose"]?.GetValue<bool?>() ?? false;
            change = (LanguageServerTextDocumentSyncKind)(syncObject["change"]?.GetValue<int?>() ?? 0);
            var saveNode = syncObject["save"];
            if (saveNode is JsonValue saveValue && saveValue.TryGetValue<bool>(out var saveBool))
            {
                save = saveBool;
            }
            else if (saveNode is JsonObject saveObject)
            {
                save = true;
                includeText = saveObject["includeText"]?.GetValue<bool?>() ?? false;
            }
        }

        return new LanguageServerClientCapabilities
        {
            OpenClose = openClose,
            Change = change,
            Save = save,
            IncludeTextOnSave = includeText,
            PositionEncoding = positionEncoding,
            DocumentDiagnostics = capabilities?["diagnosticProvider"] is not null,
            WorkspaceDiagnostics = capabilities?["diagnosticProvider"]?["workspaceDiagnostics"]?.GetValue<bool?>() ?? false
        };
    }

    private static string? TryGetPathFromUri(string uri)
    {
        try
        {
            return new Uri(uri).LocalPath;
        }
        catch
        {
            return null;
        }
    }
}

internal sealed record LanguageServerDynamicRegistration
{
    public required string Id { get; init; }
    public required string Method { get; init; }
    public string? Identifier { get; init; }
    public bool WorkspaceDiagnostics { get; init; }
}

internal sealed record LanguageServerDiagnosticPullResult
{
    public static LanguageServerDiagnosticPullResult NotHandled { get; } = new();

    public bool Handled { get; init; }
    public bool MatchedRequestedDocument { get; init; }
    public IReadOnlyList<LanguageServerDiagnosticSet> Sets { get; init; } = [];
}
