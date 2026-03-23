using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace HPD.Auth.Admin.Tests.Helpers;

/// <summary>
/// Fake authentication handler that reads roles and user-id from HTTP request headers,
/// allowing tests to control the authenticated principal without real JWT infrastructure.
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestScheme";

    /// <summary>Header that carries comma-separated roles (e.g., "Admin").</summary>
    public const string RolesHeader = "X-Test-Roles";

    /// <summary>Header that carries the authenticated user ID (GUID string).</summary>
    public const string UserIdHeader = "X-Test-UserId";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // No roles header → unauthenticated.
        if (!Request.Headers.ContainsKey(RolesHeader))
            return Task.FromResult(AuthenticateResult.NoResult());

        var rolesHeader = Request.Headers[RolesHeader].ToString();
        var userId = Request.Headers[UserIdHeader].ToString();
        if (string.IsNullOrWhiteSpace(userId))
            userId = Guid.NewGuid().ToString();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, "testuser"),
        };

        foreach (var role in rolesHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            claims.Add(new Claim(ClaimTypes.Role, role));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
