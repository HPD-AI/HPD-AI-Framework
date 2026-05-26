using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

internal sealed record HpdosTerminalShell(string Id, string Label, string Command, IReadOnlyList<string> Args);

internal sealed record HpdosTerminalSummary(
    string Id,
    string WorkspaceId,
    string RootId,
    string RootLabel,
    string Title,
    string Shell,
    string Command,
    IReadOnlyList<string> Args,
    string Cwd,
    string Status,
    int? Pid,
    int? ExitCode,
    string CreatedAt,
    string UpdatedAt,
    long Cursor,
    long OldestCursor,
    string Preview);

internal sealed record HpdosCreateTerminalRequest(
    string? Title,
    string? Name,
    string? RootId,
    string? Path,
    string? Shell,
    int? Cols,
    int? Rows);

internal sealed record HpdosUpdateTerminalRequest(string? Title);
internal sealed record HpdosTerminalConnectToken(string Ticket, int ExpiresInSeconds);

internal sealed class HpdosTerminalService : IAsyncDisposable
{
    private const int DefaultCols = 80;
    private const int DefaultRows = 24;
    private const int DefaultBufferLimit = 2 * 1024 * 1024;
    private const int TicketSeconds = 30;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HpdosWorkspaceStoreService workspaces;
    private readonly string backendDirectory;
    private readonly int bufferLimit;
    private readonly ConcurrentDictionary<string, TerminalSession> terminals = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConnectTicket> tickets = new(StringComparer.Ordinal);
    private readonly TerminalHelper helper;

    public HpdosTerminalService(HpdosWorkspaceStoreService workspaces, IConfiguration? configuration = null)
    {
        this.workspaces = workspaces;
        bufferLimit = ResolveBufferLimit(configuration);
        backendDirectory = AppContext.BaseDirectory;
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "terminal-helper.ts")))
            {
                backendDirectory = current.FullName;
                break;
            }
            current = current.Parent;
        }
        helper = new TerminalHelper(Path.Combine(backendDirectory, "terminal-helper.ts"), OnHelperEvent);
    }

    public IReadOnlyList<HpdosTerminalShell> Shells()
    {
        var shell = Environment.GetEnvironmentVariable("SHELL");
        if (string.IsNullOrWhiteSpace(shell) || !File.Exists(shell))
            shell = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/zsh";
        if (!OperatingSystem.IsWindows() && !File.Exists(shell))
            shell = "/bin/sh";

        return
        [
            new("default", Path.GetFileName(shell), shell, ["-l"]),
            new("sh", "sh", OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh", [])
        ];
    }

    public async Task<IReadOnlyList<HpdosTerminalSummary>> ListAsync(CancellationToken ct)
    {
        var workspace = await RequireActiveWorkspaceAsync(ct);
        return terminals.Values
            .Where(terminal => terminal.BelongsTo(workspace.Id))
            .OrderByDescending(terminal => terminal.UpdatedAt)
            .Select(terminal => terminal.ToSummary())
            .ToList();
    }

    public async Task<HpdosTerminalSummary?> GetAsync(string id, CancellationToken ct) =>
        await TryGetActiveTerminalAsync(id, ct) is { } terminal ? terminal.ToSummary() : null;

    public async Task<HpdosTerminalSummary> CreateAsync(HpdosCreateTerminalRequest request, CancellationToken ct)
    {
        var workspace = await workspaces.GetActiveWorkspaceAsync(ct)
            ?? throw new InvalidOperationException("No active workspace is configured.");
        var root = ResolveRoot(workspace, request.RootId);
        var cwd = ResolveWorkspacePath(root, request.Path);
        if (!Directory.Exists(cwd))
            throw new DirectoryNotFoundException($"Terminal directory was not found: {cwd}");

        var shell = Shells().FirstOrDefault(item => string.Equals(item.Id, request.Shell, StringComparison.OrdinalIgnoreCase))
            ?? Shells()[0];
        var title = FirstNonBlank(request.Title, request.Name) ?? $"{root.Label} terminal";
        var terminal = new TerminalSession(
            id: "term_" + Guid.NewGuid().ToString("n"),
            workspace.Id,
            root.Id,
            root.Label,
            title,
            shell.Id,
            shell.Command,
            shell.Args,
            cwd,
            bufferLimit);

        if (!terminals.TryAdd(terminal.Id, terminal))
            throw new InvalidOperationException("Could not create terminal.");

        try
        {
            await helper.SendAsync(new
            {
                type = "create",
                terminalId = terminal.Id,
                command = terminal.Command,
                args = terminal.Args,
                cwd = terminal.Cwd,
                env = new Dictionary<string, string>
                {
                    ["TERM"] = "xterm-256color",
                    ["COLORTERM"] = "truecolor"
                },
                cols = Math.Max(20, request.Cols ?? DefaultCols),
                rows = Math.Max(8, request.Rows ?? DefaultRows)
            }, ct);
        }
        catch
        {
            terminals.TryRemove(terminal.Id, out _);
            throw;
        }

        return terminal.ToSummary();
    }

    public async Task<HpdosTerminalSummary?> UpdateAsync(string id, HpdosUpdateTerminalRequest request, CancellationToken ct)
    {
        var terminal = await TryGetActiveTerminalAsync(id, ct);
        if (terminal is null)
            return null;
        if (!string.IsNullOrWhiteSpace(request.Title))
            terminal.Rename(request.Title.Trim());
        return terminal.ToSummary();
    }

    public async Task<bool> ResizeAsync(string id, int cols, int rows, CancellationToken ct)
    {
        if (await TryGetActiveTerminalAsync(id, ct) is null)
            return false;
        await helper.SendAsync(new
        {
            type = "resize",
            terminalId = id,
            cols = Math.Max(20, cols),
            rows = Math.Max(8, rows)
        }, ct);
        return true;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        var terminal = await TryGetActiveTerminalAsync(id, ct);
        if (terminal is null)
            return false;
        terminals.TryRemove(id, out _);

        terminal.Dispose();
        await helper.SendAsync(new { type = "kill", terminalId = id }, ct);
        return true;
    }

    public async Task<HpdosTerminalConnectToken> CreateConnectTokenAsync(string id, CancellationToken ct)
    {
        var workspace = await RequireActiveWorkspaceAsync(ct);
        var terminal = await TryGetActiveTerminalAsync(id, ct);
        if (terminal is null)
            throw new KeyNotFoundException("Unknown terminal.");

        PruneTickets();
        var ticket = Convert.ToHexString(Guid.NewGuid().ToByteArray()) + Convert.ToHexString(Guid.NewGuid().ToByteArray());
        tickets[ticket] = new ConnectTicket(id, workspace.Id, DateTimeOffset.UtcNow.AddSeconds(TicketSeconds));
        return new HpdosTerminalConnectToken(ticket, TicketSeconds);
    }

    public async Task ConnectAsync(string id, string? ticket, long? cursor, WebSocket socket, CancellationToken ct)
    {
        var workspace = await RequireActiveWorkspaceAsync(ct);
        if (!ValidateTicket(id, workspace.Id, ticket))
        {
            await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Invalid terminal ticket.", ct);
            return;
        }

        if (!terminals.TryGetValue(id, out var terminal) || !terminal.BelongsTo(workspace.Id))
        {
            await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Unknown terminal.", ct);
            return;
        }

        using var subscription = terminal.Subscribe();
        await terminal.ReplayAsync(socket, cursor, ct);

        var output = Task.Run(async () =>
        {
            try
            {
                await foreach (var frame in subscription.Reader.ReadAllAsync(ct))
                    await SendSocketAsync(socket, frame, ct);

                await TryCloseWebSocketAsync(socket, "Terminal closed.");
            }
            catch (OperationCanceledException)
            {
                // The request is going away; the outer finally handles cleanup.
            }
        }, ct);

        var inputBuffer = new byte[8192];
        try
        {
            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var payload = await ReceiveTextMessageAsync(socket, inputBuffer, ct);
                if (payload is null)
                    break;

                TerminalClientMessage? message;
                try
                {
                    message = JsonSerializer.Deserialize<TerminalClientMessage>(payload, JsonOptions);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (message?.Type == "input" && message.Data is { Length: > 0 })
                    await helper.SendAsync(new { type = "write", terminalId = id, data = message.Data }, ct);
                if (message?.Type == "resize" && message.Cols is > 0 && message.Rows is > 0)
                    await helper.SendAsync(new
                    {
                        type = "resize",
                        terminalId = id,
                        cols = Math.Max(20, message.Cols.Value),
                        rows = Math.Max(8, message.Rows.Value)
                    }, ct);
            }
        }
        finally
        {
            subscription.Dispose();
            await TryCloseWebSocketAsync(socket, "Terminal detached.");
            await output.WaitAsync(TimeSpan.FromMilliseconds(250)).ContinueWith(_ => { });
        }
    }

    private static async Task TryCloseWebSocketAsync(WebSocket socket, string description)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
            return;

        try
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, description, CancellationToken.None);
        }
        catch (Exception caught) when (
            caught is OperationCanceledException ||
            caught is WebSocketException ||
            caught is IOException ||
            caught is ObjectDisposedException ||
            caught.GetType().Name == "ConnectionAbortedException")
        {
            // Browser detach and backend shutdown commonly race with WebSocket close.
        }
    }

    private static async Task<string?> ReceiveTextMessageAsync(WebSocket socket, byte[] buffer, CancellationToken ct)
    {
        using var stream = new MemoryStream();
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            if (result.MessageType != WebSocketMessageType.Text)
                continue;

            stream.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
                continue;

            return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var terminal in terminals.Values)
            terminal.Dispose();
        terminals.Clear();
        await helper.DisposeAsync();
    }

    private void OnHelperEvent(HelperEvent helperEvent)
    {
        if (helperEvent.TerminalId is not { Length: > 0 } id)
            return;
        if (!terminals.TryGetValue(id, out var terminal))
            return;

        switch (helperEvent.Type)
        {
            case "created":
                terminal.MarkStarted(helperEvent.Pid);
                break;
            case "output":
                terminal.Append(helperEvent.Data ?? "");
                break;
            case "exit":
                terminal.MarkExited(helperEvent.ExitCode);
                break;
            case "error":
                terminal.Append($"\r\n[hpdos terminal error] {helperEvent.Message}\r\n");
                terminal.MarkExited(helperEvent.ExitCode);
                break;
        }
    }

    private static async Task SendSocketAsync(WebSocket socket, object value, CancellationToken ct)
    {
        if (socket.State != WebSocketState.Open)
            return;

        var json = JsonSerializer.Serialize(value, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    private static HpdosWorkspaceRoot ResolveRoot(HpdosWorkspace workspace, string? rootId)
    {
        var root = string.IsNullOrWhiteSpace(rootId)
            ? workspace.Roots.FirstOrDefault(item => string.Equals(item.Id, workspace.DefaultRootId, StringComparison.OrdinalIgnoreCase))
                ?? workspace.Roots.FirstOrDefault()
            : workspace.Roots.FirstOrDefault(item => string.Equals(item.Id, rootId, StringComparison.OrdinalIgnoreCase));
        return root ?? throw new ArgumentException("Workspace root was not found.");
    }

    private static string ResolveWorkspacePath(HpdosWorkspaceRoot root, string? relativePath)
    {
        var cleanRelative = (relativePath ?? "")
            .Replace('\\', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root.Path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, cleanRelative));

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(fullRoot, fullPath, comparison)
            && !fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison)
            && !fullPath.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, comparison))
            throw new UnauthorizedAccessException("Terminal path is outside the active workspace.");

        return fullPath;
    }

    private async Task<HpdosWorkspace> RequireActiveWorkspaceAsync(CancellationToken ct) =>
        await workspaces.GetActiveWorkspaceAsync(ct)
            ?? throw new InvalidOperationException("No active workspace is configured.");

    private async Task<TerminalSession?> TryGetActiveTerminalAsync(string id, CancellationToken ct)
    {
        var workspace = await RequireActiveWorkspaceAsync(ct);
        return terminals.TryGetValue(id, out var terminal) && terminal.BelongsTo(workspace.Id)
            ? terminal
            : null;
    }

    private bool ValidateTicket(string terminalId, string workspaceId, string? ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket))
            return false;
        PruneTickets();
        if (!tickets.TryRemove(ticket, out var record))
            return false;
        return string.Equals(record.TerminalId, terminalId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(record.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase)
            && record.ExpiresAt > DateTimeOffset.UtcNow;
    }

    private void PruneTickets()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var item in tickets)
        {
            if (item.Value.ExpiresAt <= now)
                tickets.TryRemove(item.Key, out _);
        }
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static int ResolveBufferLimit(IConfiguration? configuration)
    {
        var configured = configuration?["HPDOS:TerminalBufferLimitBytes"];
        return int.TryParse(configured, out var value)
            ? Math.Clamp(value, 1024, DefaultBufferLimit)
            : DefaultBufferLimit;
    }

    private sealed record ConnectTicket(string TerminalId, string WorkspaceId, DateTimeOffset ExpiresAt);
    private sealed record TerminalClientMessage(string Type, string? Data, int? Cols, int? Rows);

    private sealed class TerminalSubscription : IDisposable
    {
        private readonly TerminalSession terminal;
        public ChannelReader<object> Reader { get; }
        public ChannelWriter<object> Writer { get; }

        public TerminalSubscription(TerminalSession terminal)
        {
            this.terminal = terminal;
            var channel = Channel.CreateUnbounded<object>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
            Reader = channel.Reader;
            Writer = channel.Writer;
        }

        public void Dispose() => terminal.Unsubscribe(this);
    }

    private sealed class TerminalSession : IDisposable
    {
        private readonly List<TerminalSubscription> subscribers = [];
        private readonly object gate = new();
        private readonly StringBuilder buffer = new();
        private readonly int bufferLimit;

        public string Id { get; }
        public string WorkspaceId { get; }
        public string RootId { get; }
        public string RootLabel { get; }
        public string Title { get; private set; }
        public string Shell { get; }
        public string Command { get; }
        public IReadOnlyList<string> Args { get; }
        public string Cwd { get; }
        public string Status { get; private set; } = "starting";
        public int? Pid { get; private set; }
        public int? ExitCode { get; private set; }
        public long Cursor { get; private set; }
        public long OldestCursor { get; private set; }
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

        public TerminalSession(
            string id,
            string workspaceId,
            string rootId,
            string rootLabel,
            string title,
            string shell,
            string command,
            IReadOnlyList<string> args,
            string cwd,
            int bufferLimit)
        {
            Id = id;
            WorkspaceId = workspaceId;
            RootId = rootId;
            RootLabel = rootLabel;
            Title = title;
            Shell = shell;
            Command = command;
            Args = args;
            Cwd = cwd;
            this.bufferLimit = bufferLimit;
        }

        public bool BelongsTo(string workspaceId) =>
            string.Equals(WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase);

        public void Rename(string title)
        {
            lock (gate)
            {
                Title = title;
                UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        public void MarkStarted(int? pid)
        {
            lock (gate)
            {
                Pid = pid;
                Status = "running";
                UpdatedAt = DateTimeOffset.UtcNow;
            }
            Broadcast(new { type = "metadata", terminal = ToSummary() });
        }

        public void MarkExited(int? exitCode)
        {
            lock (gate)
            {
                ExitCode = exitCode;
                Status = "exited";
                UpdatedAt = DateTimeOffset.UtcNow;
            }
            Broadcast(new { type = "exit", exitCode, terminal = ToSummary() });
        }

        public void Append(string chunk)
        {
            if (string.IsNullOrEmpty(chunk))
                return;

            long cursor;
            lock (gate)
            {
                buffer.Append(chunk);
                Cursor += chunk.Length;
                if (buffer.Length > bufferLimit)
                {
                    var remove = buffer.Length - bufferLimit;
                    buffer.Remove(0, remove);
                    OldestCursor += remove;
                }
                cursor = Cursor;
                UpdatedAt = DateTimeOffset.UtcNow;
            }
            Broadcast(new { type = "output", cursor, data = chunk });
        }

        public TerminalSubscription Subscribe()
        {
            var subscription = new TerminalSubscription(this);
            lock (gate)
                subscribers.Add(subscription);
            return subscription;
        }

        public void Unsubscribe(TerminalSubscription subscription)
        {
            lock (gate)
                subscribers.Remove(subscription);
        }

        public async Task ReplayAsync(WebSocket socket, long? requestedCursor, CancellationToken ct)
        {
            string replay;
            bool truncated;
            long cursor;
            long oldestCursor;
            lock (gate)
            {
                var startCursor = requestedCursor ?? OldestCursor;
                truncated = startCursor < OldestCursor;
                var offset = (int)Math.Clamp(startCursor - OldestCursor, 0, buffer.Length);
                replay = buffer.ToString(offset, buffer.Length - offset);
                cursor = Cursor;
                oldestCursor = OldestCursor;
            }

            await SendSocketAsync(socket, new
            {
                type = "ready",
                cursor,
                oldestCursor,
                truncated,
                terminal = ToSummary()
            }, ct);
            if (replay.Length > 0)
                await SendSocketAsync(socket, new { type = "output", cursor, data = replay, replay = true }, ct);
        }

        public HpdosTerminalSummary ToSummary()
        {
            string preview;
            lock (gate)
                preview = buffer.Length <= 360 ? buffer.ToString() : buffer.ToString(buffer.Length - 360, 360);
            return new HpdosTerminalSummary(
                Id,
                WorkspaceId,
                RootId,
                RootLabel,
                Title,
                Shell,
                Command,
                Args,
                Cwd,
                Status,
                Pid,
                ExitCode,
                CreatedAt.ToString("O"),
                UpdatedAt.ToString("O"),
                Cursor,
                OldestCursor,
                preview.Trim());
        }

        private void Broadcast(object frame)
        {
            TerminalSubscription[] snapshot;
            lock (gate)
                snapshot = subscribers.ToArray();

            foreach (var subscriber in snapshot)
                subscriber.Writer.TryWrite(frame);
        }

        public void Dispose()
        {
            lock (gate)
            {
                foreach (var subscriber in subscribers)
                    subscriber.Writer.TryComplete();
                subscribers.Clear();
            }
        }
    }

    private sealed class TerminalHelper : IAsyncDisposable
    {
        private readonly string helperPath;
        private readonly Action<HelperEvent> onEvent;
        private readonly SemaphoreSlim startGate = new(1, 1);
        private readonly SemaphoreSlim writeGate = new(1, 1);
        private Process? process;

        public TerminalHelper(string helperPath, Action<HelperEvent> onEvent)
        {
            this.helperPath = helperPath;
            this.onEvent = onEvent;
        }

        public async Task SendAsync(object message, CancellationToken ct)
        {
            await EnsureStartedAsync(ct);
            var json = JsonSerializer.Serialize(message, JsonOptions);
            await writeGate.WaitAsync(ct);
            try
            {
                await process!.StandardInput.WriteLineAsync(json.AsMemory(), ct);
                await process.StandardInput.FlushAsync(ct);
            }
            finally
            {
                writeGate.Release();
            }
        }

        private async Task EnsureStartedAsync(CancellationToken ct)
        {
            if (process is { HasExited: false })
                return;

            await startGate.WaitAsync(ct);
            try
            {
                if (process is { HasExited: false })
                    return;
                if (!File.Exists(helperPath))
                    throw new FileNotFoundException("HPDOS terminal helper was not found.", helperPath);

                var bun = ResolveBun();
                process?.Dispose();
                process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = bun,
                        WorkingDirectory = Path.GetDirectoryName(helperPath)!,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    },
                    EnableRaisingEvents = true
                };
                process.StartInfo.ArgumentList.Add(helperPath);
                process.Start();
                _ = Task.Run(() => ReadEventsAsync(process, CancellationToken.None));
                _ = Task.Run(() => ReadErrorsAsync(process, CancellationToken.None));
            }
            finally
            {
                startGate.Release();
            }
        }

        private async Task ReadEventsAsync(Process current, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !current.HasExited)
            {
                var line = await current.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) break;
                try
                {
                    var helperEvent = JsonSerializer.Deserialize<HelperEvent>(line, JsonOptions);
                    if (helperEvent is not null)
                        onEvent(helperEvent);
                }
                catch
                {
                    // Helper stdout is reserved for JSON events; ignore malformed lines.
                }
            }
        }

        private async Task ReadErrorsAsync(Process current, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !current.HasExited)
            {
                var line = await current.StandardError.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) break;
                onEvent(new HelperEvent("error", "", null, null, null, line));
            }
        }

        private static string ResolveBun()
        {
            var configured = Environment.GetEnvironmentVariable("HPDOS_BUN");
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
                return configured;

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var userBun = Path.Combine(home, ".bun", "bin", OperatingSystem.IsWindows() ? "bun.exe" : "bun");
            if (File.Exists(userBun))
                return userBun;

            return "bun";
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (process is { HasExited: false })
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort helper cleanup.
            }
            process?.Dispose();
            await Task.CompletedTask;
        }
    }

    private sealed record HelperEvent(
        string Type,
        string? TerminalId,
        string? Data,
        int? Pid,
        int? ExitCode,
        string? Message);
}
