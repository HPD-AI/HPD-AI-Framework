using System.Net.Http;
using Microsoft.AspNetCore.TestHost;

namespace HPD.Auth.Authentication.Tests.Helpers;

/// <summary>
/// Extension helpers for <see cref="TestServer"/> used across integration and
/// cookie-auth tests.
/// </summary>
internal static class TestServerExtensions
{
    /// <summary>
    /// Creates an <see cref="HttpClient"/> backed by the test server that
    /// persists cookies across requests (like a browser session).
    /// Redirects are NOT followed so tests can assert the raw status code.
    /// </summary>
    public static HttpClient CreateCookieClient(this TestServer server)
    {
        var jar     = new SimpleCookieJar();
        var handler = new CookieJarHandler(jar, server.CreateHandler());
        return new HttpClient(handler) { BaseAddress = server.BaseAddress };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SimpleCookieJar — a plain dictionary that maps cookie name → value.
    // Avoids all the domain/path/SameSite complexities of System.Net.CookieContainer.
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class SimpleCookieJar
    {
        private readonly Dictionary<string, string> _store = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Parses Set-Cookie headers and updates the jar.</summary>
        public void SetFromHeader(string setCookieHeader)
        {
            // Format: "name=value; Path=/; SameSite=Lax; HttpOnly"
            var parts     = setCookieHeader.Split(';');
            var nameValue = parts[0].Trim();
            var eq        = nameValue.IndexOf('=');
            if (eq <= 0) return;

            var name  = nameValue[..eq].Trim();
            var value = nameValue[(eq + 1)..].Trim();

            // An empty value signals deletion.
            if (string.IsNullOrEmpty(value))
                _store.Remove(name);
            else
                _store[name] = value;

        }

        /// <summary>Returns the Cookie header value to send on the next request.</summary>
        public string GetCookieHeader()
        {
            if (_store.Count == 0) return string.Empty;
            return string.Join("; ", _store.Select(kv => $"{kv.Key}={kv.Value}"));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CookieJarHandler — injects and collects cookies on every request.
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class CookieJarHandler : DelegatingHandler
    {
        private readonly SimpleCookieJar _jar;

        public CookieJarHandler(SimpleCookieJar jar, HttpMessageHandler inner) : base(inner)
        {
            _jar = jar;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // Send stored cookies.
            var cookieHeader = _jar.GetCookieHeader();
            if (!string.IsNullOrEmpty(cookieHeader))
                request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

            var response = await base.SendAsync(request, cancellationToken);

            // Collect Set-Cookie headers from the response.
            if (response.Headers.TryGetValues("Set-Cookie", out var headers))
                foreach (var h in headers)
                    _jar.SetFromHeader(h);

            return response;
        }
    }
}
