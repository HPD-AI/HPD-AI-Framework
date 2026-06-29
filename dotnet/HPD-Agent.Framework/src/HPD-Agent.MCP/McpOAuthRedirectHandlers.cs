using System.Diagnostics;
using System.Net;
using System.Text;
using ModelContextProtocol.Authentication;

namespace HPD.Agent.MCP;

/// <summary>
/// Built-in redirect handlers for MCP OAuth authorization flows.
/// </summary>
public static class McpOAuthRedirectHandlers
{
    private const string DefaultSuccessHtml = """
        <!doctype html>
        <html>
        <head><meta charset="utf-8"><title>HPD MCP authorization complete</title></head>
        <body><h1>Authorization complete</h1><p>You can close this window and return to HPD.</p></body>
        </html>
        """;

    private const string DefaultFailureHtml = """
        <!doctype html>
        <html>
        <head><meta charset="utf-8"><title>HPD MCP authorization failed</title></head>
        <body><h1>Authorization failed</h1><p>Return to HPD and try again.</p></body>
        </html>
        """;

    /// <summary>
    /// Creates an OAuth redirect handler that opens the authorization URL in a browser and captures
    /// the authorization code through a localhost HTTP callback.
    /// </summary>
    /// <param name="openBrowser">
    /// Optional browser opener. When omitted, the handler uses the platform default browser.
    /// Tests and host applications can override this to integrate with custom UI.
    /// </param>
    /// <param name="successHtml">HTML returned to the browser after a successful callback.</param>
    /// <param name="failureHtml">HTML returned to the browser after an OAuth error or missing code.</param>
    /// <param name="timeout">Maximum time to wait for the callback. Defaults to five minutes.</param>
    public static AuthorizationRedirectDelegate LocalBrowser(
        Action<Uri>? openBrowser = null,
        string? successHtml = null,
        string? failureHtml = null,
        TimeSpan? timeout = null)
    {
        return async (authorizationUri, redirectUri, cancellationToken) =>
        {
            ValidateLocalHttpRedirectUri(redirectUri);

            using var listener = new HttpListener();
            listener.Prefixes.Add(CreateListenerPrefix(redirectUri));
            listener.Start();

            using var timeoutCts = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            openBrowser ??= OpenDefaultBrowser;
            openBrowser(authorizationUri);

            try
            {
                var context = await listener.GetContextAsync().WaitAsync(linkedCts.Token).ConfigureAwait(false);
                return await HandleCallbackAsync(
                    context,
                    successHtml ?? DefaultSuccessHtml,
                    failureHtml ?? DefaultFailureHtml,
                    linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Timed out waiting for OAuth callback on '{redirectUri}'.");
            }
            finally
            {
                listener.Stop();
            }
        };
    }

    private static async Task<string?> HandleCallbackAsync(
        HttpListenerContext context,
        string successHtml,
        string failureHtml,
        CancellationToken cancellationToken)
    {
        var request = context.Request;
        var code = request.QueryString["code"];
        var error = request.QueryString["error"];

        if (!string.IsNullOrWhiteSpace(code))
        {
            await WriteHtmlResponseAsync(context.Response, HttpStatusCode.OK, successHtml, cancellationToken).ConfigureAwait(false);
            return code;
        }

        var status = string.IsNullOrWhiteSpace(error)
            ? HttpStatusCode.BadRequest
            : HttpStatusCode.Unauthorized;
        await WriteHtmlResponseAsync(context.Response, status, failureHtml, cancellationToken).ConfigureAwait(false);
        return null;
    }

    private static async Task WriteHtmlResponseAsync(
        HttpListenerResponse response,
        HttpStatusCode statusCode,
        string html,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(html);
        response.StatusCode = (int)statusCode;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        response.Close();
    }

    private static void ValidateLocalHttpRedirectUri(Uri redirectUri)
    {
        if (!redirectUri.IsAbsoluteUri ||
            redirectUri.Scheme != Uri.UriSchemeHttp ||
            !IsLocalhost(redirectUri.Host))
        {
            throw new ArgumentException(
                "LocalBrowser OAuth redirect handling requires an absolute HTTP localhost redirect URI.",
                nameof(redirectUri));
        }
    }

    private static bool IsLocalhost(string host)
    {
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "[::1]", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateListenerPrefix(Uri redirectUri)
    {
        var builder = new UriBuilder(redirectUri)
        {
            Path = "/",
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.AbsoluteUri;
    }

    private static void OpenDefaultBrowser(Uri authorizationUri)
    {
        var url = authorizationUri.AbsoluteUri;
        if (OperatingSystem.IsMacOS())
        {
            Process.Start(new ProcessStartInfo("open", url) { UseShellExecute = false });
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo("cmd", $"/c start \"\" \"{url}\"") { CreateNoWindow = true });
            return;
        }

        Process.Start(new ProcessStartInfo("xdg-open", url) { UseShellExecute = false });
    }
}
