using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Web;

namespace HPDOS.Core.Auth;

public class OAuthCallbackServer : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly string? _expectedState;
    private readonly int _port;
    private readonly string _callbackPath;
    private readonly TaskCompletionSource<OAuthCallbackResult> _resultTcs;
    private readonly CancellationTokenSource _timeoutCts;
    private Task? _listenerTask;

    private const int DefaultTimeoutMinutes = 5;
    private const string DefaultCallbackPath = "/auth/callback";

    public OAuthCallbackServer(int port, string? expectedState, string? callbackPath = null, TimeSpan? timeout = null)
    {
        _port = port;
        _expectedState = expectedState;
        _callbackPath = callbackPath ?? DefaultCallbackPath;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _resultTcs = new TaskCompletionSource<OAuthCallbackResult>();
        _timeoutCts = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(DefaultTimeoutMinutes));
        _timeoutCts.Token.Register(() => _resultTcs.TrySetResult(new OAuthCallbackResult.Timeout()));
    }

    public string CallbackUrl => $"http://localhost:{_port}{_callbackPath}";

    public async Task<OAuthCallbackResult> WaitForCallbackAsync(CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _timeoutCts.Token);
        try
        {
            _listener.Start();
            _listenerTask = ListenAsync(linkedCts.Token);
            var completedTask = await Task.WhenAny(_resultTcs.Task, Task.Delay(Timeout.Infinite, linkedCts.Token));
            return completedTask == _resultTcs.Task ? await _resultTcs.Task : new OAuthCallbackResult.Cancelled();
        }
        catch (OperationCanceledException) { return new OAuthCallbackResult.Cancelled(); }
        catch (Exception ex) { return new OAuthCallbackResult.Error(ex.Message, ex); }
        finally { await StopAsync(); }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync().WaitAsync(cancellationToken);
                await HandleRequestAsync(context);
            }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) { break; }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        try
        {
            if (!request.Url?.AbsolutePath.StartsWith(_callbackPath) == true)
            {
                response.StatusCode = 404;
                await WriteResponseAsync(response, "Not Found", "text/plain");
                return;
            }

            var query = HttpUtility.ParseQueryString(request.Url?.Query ?? "");
            var code = query["code"];
            var state = query["state"];
            var error = query["error"];
            var errorDescription = query["error_description"];

            if (!string.IsNullOrEmpty(error))
            {
                var msg = string.IsNullOrEmpty(errorDescription) ? error : $"{error}: {errorDescription}";
                _resultTcs.TrySetResult(new OAuthCallbackResult.Error(msg));
                await WriteHtmlResponseAsync(response, false, msg);
                return;
            }

            // State validation — only enforced when the provider sends state back.
            // Some providers (e.g. OpenRouter) don't echo state in their callback.
            if (_expectedState != null)
            {
                if (string.IsNullOrEmpty(state))
                {
                    _resultTcs.TrySetResult(new OAuthCallbackResult.Error("Missing state parameter"));
                    await WriteHtmlResponseAsync(response, false, "Missing state parameter. This may be a security issue.");
                    return;
                }

                if (state != _expectedState)
                {
                    _resultTcs.TrySetResult(new OAuthCallbackResult.Error("Invalid state parameter"));
                    await WriteHtmlResponseAsync(response, false, "Invalid state parameter. This may be a security issue.");
                    return;
                }
            }

            if (string.IsNullOrEmpty(code))
            {
                _resultTcs.TrySetResult(new OAuthCallbackResult.Error("Missing authorization code"));
                await WriteHtmlResponseAsync(response, false, "Missing authorization code.");
                return;
            }

            _resultTcs.TrySetResult(new OAuthCallbackResult.Success(code));
            await WriteHtmlResponseAsync(response, true, "Authentication successful! You can close this window.");
        }
        catch (Exception ex)
        {
            _resultTcs.TrySetResult(new OAuthCallbackResult.Error(ex.Message, ex));
            await WriteHtmlResponseAsync(response, false, $"An error occurred: {ex.Message}");
        }
        finally { response.Close(); }
    }

    private static async Task WriteResponseAsync(HttpListenerResponse response, string content, string contentType)
    {
        response.ContentType = contentType;
        var buffer = Encoding.UTF8.GetBytes(content);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
    }

    private static async Task WriteHtmlResponseAsync(HttpListenerResponse response, bool success, string message)
    {
        var color = success ? "#10b981" : "#ef4444";
        var icon = success ? "&#10003;" : "&#10007;";
        var title = success ? "Authentication Successful" : "Authentication Failed";
        var autoCloseScript = success ? "setTimeout(() => window.close(), 3000);" : "";

        var html = $@"<!DOCTYPE html>
<html>
<head>
    <title>{title} - HPDOS</title>
    <style>
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            display: flex; justify-content: center; align-items: center;
            min-height: 100vh; margin: 0;
            background: linear-gradient(135deg, #1e1e2e 0%, #2d2d3f 100%);
            color: #cdd6f4;
        }}
        .container {{
            text-align: center; padding: 2rem; background: #313244;
            border-radius: 12px; box-shadow: 0 4px 6px rgba(0,0,0,0.3); max-width: 400px;
        }}
        .icon {{ font-size: 4rem; color: {color}; margin-bottom: 1rem; }}
        h1 {{ margin: 0 0 1rem 0; font-size: 1.5rem; color: #cdd6f4; }}
        p {{ margin: 0; color: #a6adc8; line-height: 1.5; }}
        .close-hint {{ margin-top: 1.5rem; font-size: 0.875rem; color: #6c7086; }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""icon"">{icon}</div>
        <h1>{title}</h1>
        <p>{message}</p>
        <p class=""close-hint"">You can close this window and return to the app.</p>
    </div>
    <script>{autoCloseScript}</script>
</body>
</html>";

        response.StatusCode = success ? 200 : 400;
        await WriteResponseAsync(response, html, "text/html; charset=utf-8");
    }

    private async Task StopAsync()
    {
        try
        {
            if (_listener.IsListening) _listener.Stop();
            if (_listenerTask != null)
                await _listenerTask.WaitAsync(TimeSpan.FromSeconds(1))
                    .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        catch { }
    }

    public async ValueTask DisposeAsync()
    {
        _timeoutCts.Cancel();
        await StopAsync();
        _listener.Close();
        _timeoutCts.Dispose();
    }

    public static int FindAvailablePort(int preferredPort = 19876)
    {
        if (IsPortAvailable(preferredPort)) return preferredPort;
        for (var port = preferredPort + 1; port < preferredPort + 100; port++)
            if (IsPortAvailable(port)) return port;
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var p = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return p;
    }

    private static bool IsPortAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch { return false; }
    }
}

public abstract record OAuthCallbackResult
{
    public sealed record Success(string Code) : OAuthCallbackResult;
    public sealed record Cancelled : OAuthCallbackResult;
    public sealed record Timeout : OAuthCallbackResult;
    public sealed record Error(string Message, Exception? Exception = null) : OAuthCallbackResult;
}
