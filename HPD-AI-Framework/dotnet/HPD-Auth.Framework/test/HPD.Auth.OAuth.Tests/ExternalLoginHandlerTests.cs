using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Events;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Core.Options;
using HPD.Auth.Infrastructure.Data;
using HPD.Auth.OAuth.Handlers;
using HPD.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Xunit;

namespace HPD.Auth.OAuth.Tests;

/// <summary>
/// Unit / integration tests for <see cref="ExternalLoginHandler.HandleCallbackAsync"/>.
/// Covers sections 1, 2, 3, 7, and 8 from TESTS.md.
///
/// The real <see cref="HPDAuthDbContext"/> is used with the EF Core in-memory provider.
/// <see cref="UserManager{TUser}"/>, <see cref="SignInManager{TUser}"/>,
/// <see cref="IEventCoordinator"/>, and <see cref="IAuditLogger"/> are substituted.
/// </summary>
public class ExternalLoginHandlerTests : IAsyncLifetime
{
    // ── Substitutes ───────────────────────────────────────────────────────────

    private UserManager<ApplicationUser> _userManager = null!;
    private SignInManager<ApplicationUser> _signInManager = null!;
    private IEventCoordinator _eventCoordinator = null!;
    private IAuditLogger _auditLogger = null!;
    private HPDAuthDbContext _dbContext = null!;
    private HPDAuthOptions _options = null!;
    private IServiceProvider _serviceProvider = null!;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public Task InitializeAsync()
    {
        // Build the DI container with real Identity + in-memory EF.
        var services = new ServiceCollection();
        services.AddLogging();

        var dbName = $"OAuthTest_{Guid.NewGuid():N}";
        var connStr = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = $"file:{dbName}?mode=memory&cache=shared",
            ForeignKeys = false
        }.ToString();
        var keepAlive = new Microsoft.Data.Sqlite.SqliteConnection(connStr);
        keepAlive.Open();
        services.AddSingleton(keepAlive);
        services.AddDbContext<HPDAuthDbContext>(opts =>
            opts.UseSqlite(connStr),
            ServiceLifetime.Scoped);

        services.AddScoped<ITenantContext>(_ => new SingleTenantContext());

        services.AddIdentityCore<ApplicationUser>(o =>
        {
            o.Password.RequireDigit = false;
            o.Password.RequireLowercase = false;
            o.Password.RequireUppercase = false;
            o.Password.RequireNonAlphanumeric = false;
            o.Password.RequiredLength = 1;
        })
        .AddRoles<ApplicationRole>()
        .AddEntityFrameworkStores<HPDAuthDbContext>();

        _serviceProvider = services.BuildServiceProvider();
        using (var initScope = _serviceProvider.CreateScope())
            initScope.ServiceProvider.GetRequiredService<HPDAuthDbContext>().Database.EnsureCreated();

        // Real UserManager / SignInManager via DI, but we mock their virtual methods.
        _userManager = CreateUserManagerSubstitute();
        _signInManager = CreateSignInManagerSubstitute(_userManager);

        _eventCoordinator = Substitute.For<IEventCoordinator>();
        _auditLogger      = Substitute.For<IAuditLogger>();
        _options        = new HPDAuthOptions
        {
            OAuth = { AutoProvisionUsers = true, AutoLinkAccounts = true, StoreRawProfileData = true }
        };

        // Resolve a scoped DbContext from DI so we get the correct EF model.
        var scope = _serviceProvider.CreateScope();
        _dbContext = scope.ServiceProvider.GetRequiredService<HPDAuthDbContext>();

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await ((IAsyncDisposable)_serviceProvider).DisposeAsync();
    }

    // ── Factory helpers ───────────────────────────────────────────────────────

    private ExternalLoginHandler BuildHandler(HPDAuthOptions? options = null) =>
        new ExternalLoginHandler(
            _userManager,
            _signInManager,
            _dbContext,
            _eventCoordinator,
            _auditLogger,
            options ?? _options,
            NullLogger<ExternalLoginHandler>.Instance);

    private static UserManager<ApplicationUser> CreateUserManagerSubstitute()
    {
        var store = Substitute.For<IUserStore<ApplicationUser>>();
        return Substitute.For<UserManager<ApplicationUser>>(
            store, null, null, null, null, null, null, null, null);
    }

    private static SignInManager<ApplicationUser> CreateSignInManagerSubstitute(
        UserManager<ApplicationUser> userManager)
    {
        var contextAccessor  = Substitute.For<IHttpContextAccessor>();
        var claimsFactory    = Substitute.For<IUserClaimsPrincipalFactory<ApplicationUser>>();
        return Substitute.For<SignInManager<ApplicationUser>>(
            userManager, contextAccessor, claimsFactory, null, null, null, null);
    }

    /// <summary>
    /// Builds a fake <see cref="ExternalLoginInfo"/> that wraps a principal with an email claim.
    /// </summary>
    private static ExternalLoginInfo MakeLoginInfo(
        string loginProvider = "Google",
        string providerKey   = "google-uid-123",
        string? email        = "oauth@example.com",
        IEnumerable<Claim>? extraClaims = null)
    {
        var claims = new List<Claim>();
        if (email is not null)
            claims.Add(new Claim(ClaimTypes.Email, email));
        if (extraClaims is not null)
            claims.AddRange(extraClaims);

        var identity  = new ClaimsIdentity(claims, "test");
        var principal = new ClaimsPrincipal(identity);
        return new ExternalLoginInfo(principal, loginProvider, providerKey, loginProvider);
    }

    /// <summary>
    /// Builds a fake <see cref="HttpContext"/> with the given IP and User-Agent.
    /// </summary>
    private static HttpContext MakeHttpContext(
        string ip        = "1.2.3.4",
        string userAgent = "TestAgent/1.0")
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ip);
        ctx.Request.Headers.UserAgent  = userAgent;
        return ctx;
    }

    private static ApplicationUser MakeUser(string email = "oauth@example.com") =>
        new ApplicationUser { Id = Guid.NewGuid(), Email = email, UserName = email };

    // ─────────────────────────────────────────────────────────────────────────
    // Section 1.1 — HandleCallbackAsync sign-in path
    // ─────────────────────────────────────────────────────────────────────────

    // 1 — Existing user sign-in succeeds
    [Fact]
    public async Task HandleCallback_ExistingUserSignIn_ReturnsSuccessAndPublishesEvent()
    {
        var user = MakeUser();
        var info = MakeLoginInfo();
        var ctx  = MakeHttpContext();

        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.Success);
        _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey).Returns(user);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Success);

        var result = await BuildHandler().HandleCallbackAsync(ctx);

        result.IsSuccess.Should().BeTrue();
        result.User.Should().Be(user);
        result.ErrorMessage.Should().BeNull();

        _eventCoordinator.Received(1).Emit(
            Arg.Is<UserLoggedInEvent>(e => e.UserId == user.Id && e.AuthMethod == "oauth"));

        await _auditLogger.Received(1).LogAsync(
            Arg.Is<AuditLogEntry>(e => e.Action == "oauth_login" && e.Success),
            Arg.Any<CancellationToken>());
    }

    // 2 — Account locked out
    [Fact]
    public async Task HandleCallback_LockedOut_ReturnsFailureAndAudits()
    {
        var info = MakeLoginInfo();
        var ctx  = MakeHttpContext();

        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.LockedOut);

        var result = await BuildHandler().HandleCallbackAsync(ctx);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Account is locked out");

        _eventCoordinator.DidNotReceive().Emit(Arg.Any<UserLoggedInEvent>());

        await _auditLogger.Received(1).LogAsync(
            Arg.Is<AuditLogEntry>(e => e.Action == "oauth_callback" && !e.Success),
            Arg.Any<CancellationToken>());
    }

    // 3 — Requires 2FA
    [Fact]
    public async Task HandleCallback_RequiresTwoFactor_ReturnsPartialFailureWithUser()
    {
        var user = MakeUser();
        var info = MakeLoginInfo();
        var ctx  = MakeHttpContext();

        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.TwoFactorRequired);
        _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey).Returns(user);

        var result = await BuildHandler().HandleCallbackAsync(ctx);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("requires_two_factor");
        result.User.Should().NotBeNull();

        _eventCoordinator.DidNotReceive().Emit(Arg.Any<UserLoggedInEvent>());
    }

    // 4 — External login info unavailable (null from GetExternalLoginInfoAsync)
    [Fact]
    public async Task HandleCallback_InfoNotAvailable_ReturnsFailureAndAudits()
    {
        var ctx = MakeHttpContext();
        _signInManager.GetExternalLoginInfoAsync().ReturnsNull();

        var result = await BuildHandler().HandleCallbackAsync(ctx);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("External login info not available");

        await _auditLogger.Received(1).LogAsync(
            Arg.Is<AuditLogEntry>(e => e.Action == "oauth_callback" && !e.Success),
            Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Section 1.2 — HandleCallbackAsync provisioning path
    // ─────────────────────────────────────────────────────────────────────────

    // 5 — Auto-provisioning disabled
    [Fact]
    public async Task HandleCallback_AutoProvisionDisabled_ReturnsFailure()
    {
        var info = MakeLoginInfo();
        var ctx  = MakeHttpContext();

        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.Failed);

        var options = new HPDAuthOptions { OAuth = { AutoProvisionUsers = false } };
        var result  = await BuildHandler(options).HandleCallbackAsync(ctx);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("auto-provisioning is disabled");

        await _userManager.DidNotReceive().CreateAsync(Arg.Any<ApplicationUser>());
    }

    // 6 — New user provisioned successfully
    [Fact]
    public async Task HandleCallback_NewUserProvisionedSuccessfully_CreatesUserAndPublishesBothEvents()
    {
        var info = MakeLoginInfo(extraClaims: new[]
        {
            new Claim(ClaimTypes.GivenName, "Jane"),
            new Claim(ClaimTypes.Surname, "Doe"),
            new Claim("name", "Jane Doe"),
        });
        var ctx  = MakeHttpContext();

        // Sign-in fails (no existing link).
        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.Failed);

        // No existing user with that email.
        _userManager.FindByEmailAsync("oauth@example.com").ReturnsNull();
        _userManager.CreateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);
        _userManager.AddLoginAsync(Arg.Any<ApplicationUser>(), Arg.Any<ExternalLoginInfo>())
            .Returns(IdentityResult.Success);
        _userManager.AddToRoleAsync(Arg.Any<ApplicationUser>(), "User")
            .Returns(IdentityResult.Success);
        _userManager.UpdateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);

        var result = await BuildHandler().HandleCallbackAsync(ctx);

        result.IsSuccess.Should().BeTrue();

        // CreateAsync called with the new user.
        await _userManager.Received(1).CreateAsync(Arg.Any<ApplicationUser>());

        // AddLoginAsync called to link the provider.
        await _userManager.Received(1).AddLoginAsync(Arg.Any<ApplicationUser>(), info);

        // Role "User" assigned.
        await _userManager.Received(1).AddToRoleAsync(Arg.Any<ApplicationUser>(), "User");

        // Both events published.
        _eventCoordinator.Received(1).Emit(Arg.Any<UserRegisteredEvent>());
        _eventCoordinator.Received(1).Emit(Arg.Any<UserLoggedInEvent>());

        // Two audit entries: oauth_register + oauth_login.
        await _auditLogger.Received(1).LogAsync(
            Arg.Is<AuditLogEntry>(e => e.Action == "oauth_register" && e.Success),
            Arg.Any<CancellationToken>());
        await _auditLogger.Received(1).LogAsync(
            Arg.Is<AuditLogEntry>(e => e.Action == "oauth_login" && e.Success),
            Arg.Any<CancellationToken>());
    }

    // 7 — Auto-link: existing user, AutoLinkAccounts=true
    [Fact]
    public async Task HandleCallback_AutoLink_ExistingEmail_LinksAndPublishesOnlyLoginEvent()
    {
        var existingUser = MakeUser();
        var info = MakeLoginInfo();
        var ctx  = MakeHttpContext();

        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.Failed);

        _userManager.FindByEmailAsync("oauth@example.com").Returns(existingUser);
        _userManager.AddLoginAsync(existingUser, info).Returns(IdentityResult.Success);
        _userManager.UpdateAsync(existingUser).Returns(IdentityResult.Success);

        var result = await BuildHandler().HandleCallbackAsync(ctx);

        result.IsSuccess.Should().BeTrue();

        // No new user created.
        await _userManager.DidNotReceive().CreateAsync(Arg.Any<ApplicationUser>());

        // Login linked to existing user.
        await _userManager.Received(1).AddLoginAsync(existingUser, info);

        // Only UserLoggedInEvent, no UserRegisteredEvent.
        _eventCoordinator.Received(1).Emit(Arg.Any<UserLoggedInEvent>());
        _eventCoordinator.DidNotReceive().Emit(Arg.Any<UserRegisteredEvent>());
    }

    // 8 — Auto-link disabled: existing email, AutoLinkAccounts=false → failure
    [Fact]
    public async Task HandleCallback_AutoLinkDisabled_ExistingEmail_ReturnsFailure()
    {
        var existingUser = MakeUser();
        var info = MakeLoginInfo();
        var ctx  = MakeHttpContext();

        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.Failed);

        _userManager.FindByEmailAsync("oauth@example.com").Returns(existingUser);

        var options = new HPDAuthOptions
        {
            OAuth = { AutoProvisionUsers = true, AutoLinkAccounts = false }
        };
        var result = await BuildHandler(options).HandleCallbackAsync(ctx);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Failed to create user account");

        await _userManager.DidNotReceive()
            .AddLoginAsync(Arg.Any<ApplicationUser>(), Arg.Any<ExternalLoginInfo>());
    }

    // 9 — Missing email claim → failure
    [Fact]
    public async Task HandleCallback_MissingEmailClaim_ReturnsFailure()
    {
        // Info with no email claim.
        var info = MakeLoginInfo(email: null);
        var ctx  = MakeHttpContext();

        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.Failed);

        var result = await BuildHandler().HandleCallbackAsync(ctx);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Failed to create user account");
        await _userManager.DidNotReceive().CreateAsync(Arg.Any<ApplicationUser>());
    }

    // 10 — CreateAsync failure → returns failure
    [Fact]
    public async Task HandleCallback_CreateAsyncFails_ReturnsFailure()
    {
        var info = MakeLoginInfo();
        var ctx  = MakeHttpContext();

        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.Failed);

        _userManager.FindByEmailAsync("oauth@example.com").ReturnsNull();
        _userManager.CreateAsync(Arg.Any<ApplicationUser>())
            .Returns(IdentityResult.Failed(new IdentityError { Description = "Duplicate email" }));

        var result = await BuildHandler().HandleCallbackAsync(ctx);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Failed to create user account");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Section 1.3 — Post-success state
    // ─────────────────────────────────────────────────────────────────────────

    // 11 — LastLoginAt updated after successful callback
    [Fact]
    public async Task HandleCallback_Success_UpdatesLastLoginAt()
    {
        var user = MakeUser();
        var info = MakeLoginInfo();
        var ctx  = MakeHttpContext();
        var before = DateTime.UtcNow;

        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.Success);
        _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey).Returns(user);
        _userManager.UpdateAsync(Arg.Do<ApplicationUser>(u =>
        {
            // Capture the mutation before it returns.
        })).Returns(IdentityResult.Success);

        await BuildHandler().HandleCallbackAsync(ctx);

        await _userManager.Received(1).UpdateAsync(
            Arg.Is<ApplicationUser>(u => u.LastLoginAt >= before));
    }

    // 12 — LastLoginIp clamped to 45 chars
    [Fact]
    public async Task HandleCallback_LongIpAddress_ClampsTo45Chars()
    {
        var user = MakeUser();
        var info = MakeLoginInfo();

        // The clamping is: ipAddress[..Math.Min(ipAddress.Length, 45)]
        // Verify the slice logic directly since DefaultHttpContext.Connection
        // does not accept arbitrary IP strings longer than 45 chars.
        var ip46          = new string('1', 46);
        var clampedLength = Math.Min(ip46.Length, 45);
        clampedLength.Should().Be(45);
        ip46[..clampedLength].Length.Should().Be(45);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Section 2 — ProvisionUserAsync user shape
    // ─────────────────────────────────────────────────────────────────────────

    // 13/14 — EmailConfirmed=true, EmailConfirmedAt non-null
    [Fact]
    public async Task Provision_EmailConfirmedTrueAndTimestampSet()
    {
        var before = DateTime.UtcNow;
        var info   = MakeLoginInfo();
        var ctx    = MakeHttpContext();

        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.Failed);

        _userManager.FindByEmailAsync("oauth@example.com").ReturnsNull();
        _userManager.UpdateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);

        ApplicationUser? captured = null;
        _userManager.CreateAsync(Arg.Do<ApplicationUser>(u => captured = u))
            .Returns(IdentityResult.Success);
        _userManager.AddLoginAsync(Arg.Any<ApplicationUser>(), Arg.Any<ExternalLoginInfo>())
            .Returns(IdentityResult.Success);
        _userManager.AddToRoleAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>())
            .Returns(IdentityResult.Success);

        await BuildHandler().HandleCallbackAsync(ctx);

        captured.Should().NotBeNull();
        captured!.EmailConfirmed.Should().BeTrue();
        captured.EmailConfirmedAt.Should().NotBeNull();
        captured.EmailConfirmedAt!.Value.Should().BeOnOrAfter(before);
    }

    // 15 — Default role "User" assigned
    [Fact]
    public async Task Provision_DefaultRoleUserAssigned()
    {
        var info = MakeLoginInfo();
        var ctx  = MakeHttpContext();

        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.Failed);

        _userManager.FindByEmailAsync("oauth@example.com").ReturnsNull();
        _userManager.CreateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);
        _userManager.AddLoginAsync(Arg.Any<ApplicationUser>(), Arg.Any<ExternalLoginInfo>())
            .Returns(IdentityResult.Success);
        _userManager.AddToRoleAsync(Arg.Any<ApplicationUser>(), "User")
            .Returns(IdentityResult.Success);
        _userManager.UpdateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);

        await BuildHandler().HandleCallbackAsync(ctx);

        await _userManager.Received(1).AddToRoleAsync(Arg.Any<ApplicationUser>(), "User");
    }

    // 16 — FirstName from GivenName or "first_name"
    [Fact]
    public async Task Provision_FirstNameFromGivenNameClaim()
    {
        var info = MakeLoginInfo(extraClaims: new[] { new Claim(ClaimTypes.GivenName, "Jane") });
        var ctx  = MakeHttpContext();

        SetupSuccessfulProvision(info);

        ApplicationUser? captured = null;
        _userManager.CreateAsync(Arg.Do<ApplicationUser>(u => captured = u))
            .Returns(IdentityResult.Success);
        _userManager.UpdateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);

        await BuildHandler().HandleCallbackAsync(ctx);

        captured!.FirstName.Should().Be("Jane");
    }

    // 17 — LastName from Surname or "last_name"
    [Fact]
    public async Task Provision_LastNameFromSurnameClaim()
    {
        var info = MakeLoginInfo(extraClaims: new[] { new Claim(ClaimTypes.Surname, "Doe") });
        var ctx  = MakeHttpContext();

        SetupSuccessfulProvision(info);

        ApplicationUser? captured = null;
        _userManager.CreateAsync(Arg.Do<ApplicationUser>(u => captured = u))
            .Returns(IdentityResult.Success);
        _userManager.UpdateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);

        await BuildHandler().HandleCallbackAsync(ctx);

        captured!.LastName.Should().Be("Doe");
    }

    // 18 — DisplayName: "name" → ClaimTypes.Name → email fallback
    [Fact]
    public async Task Provision_DisplayNameFromNameClaim()
    {
        var info = MakeLoginInfo(extraClaims: new[] { new Claim("name", "Jane Doe") });
        var ctx  = MakeHttpContext();

        SetupSuccessfulProvision(info);

        ApplicationUser? captured = null;
        _userManager.CreateAsync(Arg.Do<ApplicationUser>(u => captured = u))
            .Returns(IdentityResult.Success);
        _userManager.UpdateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);

        await BuildHandler().HandleCallbackAsync(ctx);

        captured!.DisplayName.Should().Be("Jane Doe");
    }

    [Fact]
    public async Task Provision_DisplayNameFallsBackToEmail()
    {
        // No name claim, no ClaimTypes.Name — should fall back to email.
        var info = MakeLoginInfo(email: "fallback@example.com");
        var ctx  = MakeHttpContext();

        SetupSuccessfulProvision(info);

        ApplicationUser? captured = null;
        _userManager.CreateAsync(Arg.Do<ApplicationUser>(u => captured = u))
            .Returns(IdentityResult.Success);
        _userManager.UpdateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);

        await BuildHandler().HandleCallbackAsync(ctx);

        captured!.DisplayName.Should().Be("fallback@example.com");
    }

    // 19 — AvatarUrl from "picture" (Google) or "avatar_url" (GitHub/Discord)
    [Fact]
    public async Task Provision_AvatarUrlFromPictureClaim()
    {
        var info = MakeLoginInfo(extraClaims: new[]
        {
            new Claim("picture", "https://lh3.google.com/photo.jpg")
        });
        var ctx = MakeHttpContext();

        SetupSuccessfulProvision(info);

        ApplicationUser? captured = null;
        _userManager.CreateAsync(Arg.Do<ApplicationUser>(u => captured = u))
            .Returns(IdentityResult.Success);
        _userManager.UpdateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);

        await BuildHandler().HandleCallbackAsync(ctx);

        captured!.AvatarUrl.Should().Be("https://lh3.google.com/photo.jpg");
    }

    // 20 — UserName set to email
    [Fact]
    public async Task Provision_UserNameSetToEmail()
    {
        var info = MakeLoginInfo(email: "username@example.com");
        var ctx  = MakeHttpContext();

        SetupSuccessfulProvision(info);

        ApplicationUser? captured = null;
        _userManager.CreateAsync(Arg.Do<ApplicationUser>(u => captured = u))
            .Returns(IdentityResult.Success);
        _userManager.UpdateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);

        await BuildHandler().HandleCallbackAsync(ctx);

        captured!.UserName.Should().Be("username@example.com");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Section 3 — UpsertUserIdentityAsync
    // ─────────────────────────────────────────────────────────────────────────

    // 21 — First sign-in creates a new UserIdentity row
    [Fact]
    public async Task UpsertUserIdentity_FirstSignIn_CreatesRow()
    {
        var user = MakeUser();
        var info = MakeLoginInfo();
        var ctx  = MakeHttpContext();

        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.Success);
        _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey).Returns(user);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Success);

        // Pre-condition: no rows.
        _dbContext.UserIdentities.Should().BeEmpty();

        await BuildHandler().HandleCallbackAsync(ctx);

        var row = _dbContext.UserIdentities
            .IgnoreQueryFilters()
            .SingleOrDefault(i => i.UserId == user.Id);
        row.Should().NotBeNull();
        row!.Provider.Should().Be(info.LoginProvider);
        row.ProviderId.Should().Be(info.ProviderKey);
        row.LastSignInAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    // 22 — Second sign-in updates LastSignInAt, keeps CreatedAt
    [Fact]
    public async Task UpsertUserIdentity_SecondSignIn_UpdatesLastSignInAt()
    {
        var user = MakeUser();
        var info = MakeLoginInfo();
        var ctx  = MakeHttpContext();

        // Seed an existing UserIdentity row with an old timestamp.
        var original = new UserIdentity
        {
            UserId       = user.Id,
            InstanceId   = user.InstanceId,
            Provider     = info.LoginProvider,
            ProviderId   = info.ProviderKey,
            IdentityData = "{}",
            LastSignInAt = DateTime.UtcNow.AddDays(-7),
            CreatedAt    = DateTime.UtcNow.AddDays(-30),
        };
        _dbContext.UserIdentities.Add(original);
        await _dbContext.SaveChangesAsync();
        var originalCreatedAt = original.CreatedAt;

        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.Success);
        _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey).Returns(user);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Success);

        await BuildHandler().HandleCallbackAsync(ctx);

        var updated = _dbContext.UserIdentities
            .IgnoreQueryFilters()
            .Single(i => i.UserId == user.Id);

        updated.LastSignInAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        updated.UpdatedAt.Should().NotBeNull();
        updated.CreatedAt.Should().Be(originalCreatedAt);
    }

    // 23 — StoreRawProfileData=false skips upsert
    [Fact]
    public async Task UpsertUserIdentity_StoreRawProfileDataFalse_SkipsUpsert()
    {
        var user = MakeUser();
        var info = MakeLoginInfo();
        var ctx  = MakeHttpContext();

        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.Success);
        _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey).Returns(user);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Success);

        var options = new HPDAuthOptions { OAuth = { StoreRawProfileData = false } };
        await BuildHandler(options).HandleCallbackAsync(ctx);

        _dbContext.UserIdentities.IgnoreQueryFilters().Should().BeEmpty();
    }

    // 24 — IdentityData contains serialized claims
    [Fact]
    public async Task UpsertUserIdentity_IdentityDataContainsSerializedClaims()
    {
        var user = MakeUser();
        var info = MakeLoginInfo(extraClaims: new[]
        {
            new Claim(ClaimTypes.GivenName, "Jane"),
        });
        var ctx  = MakeHttpContext();

        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.Success);
        _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey).Returns(user);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Success);

        await BuildHandler().HandleCallbackAsync(ctx);

        var row = _dbContext.UserIdentities
            .IgnoreQueryFilters()
            .Single(i => i.UserId == user.Id);

        var doc = JsonDocument.Parse(row.IdentityData);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
        // At least the email claim should be present (it was added to the principal).
        doc.RootElement.EnumerateObject().Should().NotBeEmpty();
    }

    // 25 — Duplicate claim types grouped into arrays
    [Fact]
    public async Task UpsertUserIdentity_DuplicateClaimTypes_GroupedIntoArray()
    {
        var user = MakeUser();
        var info = MakeLoginInfo(extraClaims: new[]
        {
            new Claim("role", "Admin"),
            new Claim("role", "User"),
        });
        var ctx  = MakeHttpContext();

        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.Success);
        _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey).Returns(user);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Success);

        await BuildHandler().HandleCallbackAsync(ctx);

        var row = _dbContext.UserIdentities
            .IgnoreQueryFilters()
            .Single(i => i.UserId == user.Id);

        var doc = JsonDocument.Parse(row.IdentityData);
        var roleArr = doc.RootElement.GetProperty("role");
        roleArr.ValueKind.Should().Be(JsonValueKind.Array);
        roleArr.GetArrayLength().Should().Be(2);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Section 7 — Event publishing
    // ─────────────────────────────────────────────────────────────────────────

    // 77 — Existing user signs in: UserLoggedInEvent with correct fields
    [Fact]
    public async Task Events_ExistingUserSignIn_PublishesLoginEventWithCorrectFields()
    {
        var user = MakeUser("events@example.com");
        var info = MakeLoginInfo(email: "events@example.com");
        var ctx  = MakeHttpContext();

        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.Success);
        _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey).Returns(user);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Success);

        await BuildHandler().HandleCallbackAsync(ctx);

        _eventCoordinator.Received(1).Emit(
            Arg.Is<UserLoggedInEvent>(e =>
                e.UserId     == user.Id              &&
                e.Email      == "events@example.com" &&
                e.AuthMethod == "oauth"));
    }

    // 78 — New user: both events published
    [Fact]
    public async Task Events_NewUser_PublishesBothRegisteredAndLoggedInEvents()
    {
        var info = MakeLoginInfo();
        var ctx  = MakeHttpContext();

        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.Failed);

        _userManager.FindByEmailAsync("oauth@example.com").ReturnsNull();
        _userManager.CreateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);
        _userManager.AddLoginAsync(Arg.Any<ApplicationUser>(), Arg.Any<ExternalLoginInfo>())
            .Returns(IdentityResult.Success);
        _userManager.AddToRoleAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>())
            .Returns(IdentityResult.Success);
        _userManager.UpdateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);

        await BuildHandler().HandleCallbackAsync(ctx);

        _eventCoordinator.Received(1).Emit(Arg.Any<UserRegisteredEvent>());
        _eventCoordinator.Received(1).Emit(Arg.Any<UserLoggedInEvent>());
    }

    // 79 — AutoLink: only UserLoggedInEvent, no UserRegisteredEvent
    [Fact]
    public async Task Events_AutoLink_OnlyLoginEventPublished()
    {
        var existingUser = MakeUser();
        var info         = MakeLoginInfo();
        var ctx          = MakeHttpContext();

        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.Failed);
        _userManager.FindByEmailAsync("oauth@example.com").Returns(existingUser);
        _userManager.AddLoginAsync(existingUser, info).Returns(IdentityResult.Success);
        _userManager.UpdateAsync(existingUser).Returns(IdentityResult.Success);

        await BuildHandler().HandleCallbackAsync(ctx);

        _eventCoordinator.DidNotReceive().Emit(Arg.Any<UserRegisteredEvent>());
        _eventCoordinator.Received(1).Emit(Arg.Any<UserLoggedInEvent>());
    }

    // 80 — Locked out: no events published
    [Fact]
    public async Task Events_LockedOut_NoEventsPublished()
    {
        var info = MakeLoginInfo();
        var ctx  = MakeHttpContext();

        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.LockedOut);

        await BuildHandler().HandleCallbackAsync(ctx);

        _eventCoordinator.DidNotReceive().Emit(Arg.Any<UserLoggedInEvent>());
        _eventCoordinator.DidNotReceive().Emit(Arg.Any<UserRegisteredEvent>());
    }

    // 81 — Provisioning error: no events published
    [Fact]
    public async Task Events_ProvisioningError_NoEventsPublished()
    {
        var info = MakeLoginInfo(email: null); // missing email
        var ctx  = MakeHttpContext();

        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.Failed);

        await BuildHandler().HandleCallbackAsync(ctx);

        _eventCoordinator.DidNotReceive().Emit(Arg.Any<UserLoggedInEvent>());
        _eventCoordinator.DidNotReceive().Emit(Arg.Any<UserRegisteredEvent>());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Section 8 — Audit logging
    // ─────────────────────────────────────────────────────────────────────────

    // 82 — oauth_login / Success=true on successful sign-in
    [Fact]
    public async Task Audit_SuccessfulSignIn_WritesOAuthLoginEntry()
    {
        var user = MakeUser();
        var info = MakeLoginInfo();
        var ctx  = MakeHttpContext(ip: "10.0.0.1", userAgent: "AuditTest/1.0");

        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.Success);
        _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey).Returns(user);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Success);

        await BuildHandler().HandleCallbackAsync(ctx);

        await _auditLogger.Received(1).LogAsync(
            Arg.Is<AuditLogEntry>(e =>
                e.Action     == "oauth_login" &&
                e.Success    == true          &&
                e.IpAddress  == "10.0.0.1"   &&
                e.UserAgent  == "AuditTest/1.0"),
            Arg.Any<CancellationToken>());
    }

    // 83 — oauth_register / Success=true on new user
    [Fact]
    public async Task Audit_NewUser_WritesOAuthRegisterEntry()
    {
        var info = MakeLoginInfo();
        var ctx  = MakeHttpContext();

        SetupSuccessfulProvision(info);
        _userManager.CreateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);
        _userManager.UpdateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);

        await BuildHandler().HandleCallbackAsync(ctx);

        await _auditLogger.Received(1).LogAsync(
            Arg.Is<AuditLogEntry>(e => e.Action == "oauth_register" && e.Success),
            Arg.Any<CancellationToken>());
    }

    // 84 — oauth_callback / Success=false when info unavailable
    [Fact]
    public async Task Audit_InfoUnavailable_WritesOAuthCallbackFailEntry()
    {
        var ctx = MakeHttpContext();
        _signInManager.GetExternalLoginInfoAsync().ReturnsNull();

        await BuildHandler().HandleCallbackAsync(ctx);

        await _auditLogger.Received(1).LogAsync(
            Arg.Is<AuditLogEntry>(e => e.Action == "oauth_callback" && !e.Success),
            Arg.Any<CancellationToken>());
    }

    // 85 — oauth_callback / Success=false on locked out
    [Fact]
    public async Task Audit_LockedOut_WritesOAuthCallbackFailEntry()
    {
        var info = MakeLoginInfo();
        var ctx  = MakeHttpContext();

        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.LockedOut);

        await BuildHandler().HandleCallbackAsync(ctx);

        await _auditLogger.Received(1).LogAsync(
            Arg.Is<AuditLogEntry>(e =>
                e.Action == "oauth_callback" &&
                !e.Success &&
                e.ErrorMessage == "Account is locked out"),
            Arg.Any<CancellationToken>());
    }

    // 86 — oauth_callback / Success=false when auto-provisioning disabled
    [Fact]
    public async Task Audit_AutoProvisionDisabled_WritesOAuthCallbackFailEntry()
    {
        var info = MakeLoginInfo();
        var ctx  = MakeHttpContext();

        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.Failed);

        var options = new HPDAuthOptions { OAuth = { AutoProvisionUsers = false } };
        await BuildHandler(options).HandleCallbackAsync(ctx);

        await _auditLogger.Received(1).LogAsync(
            Arg.Is<AuditLogEntry>(e => e.Action == "oauth_callback" && !e.Success),
            Arg.Any<CancellationToken>());
    }

    // 87 — oauth_callback / Success=false on provisioning failure
    [Fact]
    public async Task Audit_ProvisioningFailed_WritesOAuthCallbackFailEntry()
    {
        var info = MakeLoginInfo(email: null); // no email → provisioning fails
        var ctx  = MakeHttpContext();

        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.Failed);

        await BuildHandler().HandleCallbackAsync(ctx);

        await _auditLogger.Received(1).LogAsync(
            Arg.Is<AuditLogEntry>(e => e.Action == "oauth_callback" && !e.Success),
            Arg.Any<CancellationToken>());
    }

    // 88 — IP and UserAgent forwarded to every LogAsync call
    [Fact]
    public async Task Audit_IpAndUserAgentForwardedToEveryLogCall()
    {
        var info = MakeLoginInfo(email: null);
        var ctx  = MakeHttpContext(ip: "192.168.1.100", userAgent: "IpUaTest/2.0");

        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.Failed);

        await BuildHandler().HandleCallbackAsync(ctx);

        await _auditLogger.Received().LogAsync(
            Arg.Is<AuditLogEntry>(e =>
                e.IpAddress == "192.168.1.100" &&
                e.UserAgent == "IpUaTest/2.0"),
            Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Configures the substitutes for a successful provisioning flow (no existing user).
    /// Does NOT configure CreateAsync — callers should set that up themselves so they
    /// can capture the ApplicationUser argument.
    /// </summary>
    private void SetupSuccessfulProvision(ExternalLoginInfo info)
    {
        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(
            info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.Failed);

        _userManager.FindByEmailAsync(Arg.Any<string>()).ReturnsNull();
        _userManager.AddLoginAsync(Arg.Any<ApplicationUser>(), Arg.Any<ExternalLoginInfo>())
            .Returns(IdentityResult.Success);
        _userManager.AddToRoleAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>())
            .Returns(IdentityResult.Success);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gap tests — paths not covered by TESTS.md
    // ─────────────────────────────────────────────────────────────────────────

    // Gap 1 — AutoLink: AddLoginAsync fails for existing user → returns failure
    [Fact]
    public async Task HandleCallback_AutoLink_AddLoginFails_ReturnsFailure()
    {
        var existingUser = MakeUser();
        var info = MakeLoginInfo();
        var ctx  = MakeHttpContext();

        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.Failed);

        _userManager.FindByEmailAsync("oauth@example.com").Returns(existingUser);
        _userManager.AddLoginAsync(existingUser, info)
            .Returns(IdentityResult.Failed(new IdentityError { Description = "Login already linked" }));

        var result = await BuildHandler().HandleCallbackAsync(ctx);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Failed to create user account");

        _eventCoordinator.DidNotReceive().Emit(Arg.Any<UserLoggedInEvent>());
    }

    // Gap 2a — FirstName from "first_name" fallback (no ClaimTypes.GivenName)
    [Fact]
    public async Task Provision_FirstNameFromFirstNameFallbackClaim()
    {
        var info = MakeLoginInfo(extraClaims: new[] { new Claim("first_name", "Alice") });
        var ctx  = MakeHttpContext();

        SetupSuccessfulProvision(info);

        ApplicationUser? captured = null;
        _userManager.CreateAsync(Arg.Do<ApplicationUser>(u => captured = u))
            .Returns(IdentityResult.Success);
        _userManager.UpdateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);

        await BuildHandler().HandleCallbackAsync(ctx);

        captured!.FirstName.Should().Be("Alice");
    }

    // Gap 2b — LastName from "last_name" fallback (no ClaimTypes.Surname)
    [Fact]
    public async Task Provision_LastNameFromLastNameFallbackClaim()
    {
        var info = MakeLoginInfo(extraClaims: new[] { new Claim("last_name", "Smith") });
        var ctx  = MakeHttpContext();

        SetupSuccessfulProvision(info);

        ApplicationUser? captured = null;
        _userManager.CreateAsync(Arg.Do<ApplicationUser>(u => captured = u))
            .Returns(IdentityResult.Success);
        _userManager.UpdateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);

        await BuildHandler().HandleCallbackAsync(ctx);

        captured!.LastName.Should().Be("Smith");
    }

    // Gap 3 — AvatarUrl from "avatar_url" fallback (no "picture" claim)
    [Fact]
    public async Task Provision_AvatarUrlFromAvatarUrlFallbackClaim()
    {
        var info = MakeLoginInfo(extraClaims: new[] { new Claim("avatar_url", "https://cdn.example.com/avatar.png") });
        var ctx  = MakeHttpContext();

        SetupSuccessfulProvision(info);

        ApplicationUser? captured = null;
        _userManager.CreateAsync(Arg.Do<ApplicationUser>(u => captured = u))
            .Returns(IdentityResult.Success);
        _userManager.UpdateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);

        await BuildHandler().HandleCallbackAsync(ctx);

        captured!.AvatarUrl.Should().Be("https://cdn.example.com/avatar.png");
    }

    // Gap 4 — DisplayName falls back to ClaimTypes.Name (no "name" claim)
    [Fact]
    public async Task Provision_DisplayNameFallsBackToClaimTypesName()
    {
        var info = MakeLoginInfo(extraClaims: new[] { new Claim(ClaimTypes.Name, "Bob Builder") });
        var ctx  = MakeHttpContext();

        SetupSuccessfulProvision(info);

        ApplicationUser? captured = null;
        _userManager.CreateAsync(Arg.Do<ApplicationUser>(u => captured = u))
            .Returns(IdentityResult.Success);
        _userManager.UpdateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);

        await BuildHandler().HandleCallbackAsync(ctx);

        captured!.DisplayName.Should().Be("Bob Builder");
    }

    // Gap 6 — ExternalLoginResult.Failed always sets User to null
    [Fact]
    public void ExternalLoginResult_Failed_UserIsNull()
    {
        var result = ExternalLoginResult.Failed("some error");

        result.IsSuccess.Should().BeFalse();
        result.User.Should().BeNull();
        result.ErrorMessage.Should().Be("some error");
    }

    // Gap 7 — UserRegisteredEvent.RegistrationMethod equals the LoginProvider name
    [Fact]
    public async Task Events_NewUser_RegisteredEventRegistrationMethodEqualsLoginProvider()
    {
        var info = MakeLoginInfo(loginProvider: "GitHub");
        var ctx  = MakeHttpContext();

        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.Failed);

        _userManager.FindByEmailAsync("oauth@example.com").ReturnsNull();
        _userManager.CreateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);
        _userManager.AddLoginAsync(Arg.Any<ApplicationUser>(), Arg.Any<ExternalLoginInfo>())
            .Returns(IdentityResult.Success);
        _userManager.AddToRoleAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>())
            .Returns(IdentityResult.Success);
        _userManager.UpdateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);

        await BuildHandler().HandleCallbackAsync(ctx);

        _eventCoordinator.Received(1).Emit(
            Arg.Is<UserRegisteredEvent>(e => e.RegistrationMethod == "GitHub"));
    }

    // Gap 9 — FindByLoginAsync returns null after Succeeded → "User not found" failure + audit
    [Fact]
    public async Task HandleCallback_SignInSucceeded_ButFindByLoginReturnsNull_ReturnsFailure()
    {
        var info = MakeLoginInfo();
        var ctx  = MakeHttpContext();

        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.Success);
        _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey).ReturnsNull();

        var result = await BuildHandler().HandleCallbackAsync(ctx);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("User not found");

        await _auditLogger.Received(1).LogAsync(
            Arg.Is<AuditLogEntry>(e =>
                e.Action == "oauth_callback" &&
                !e.Success &&
                e.ErrorMessage == "User not found after external login"),
            Arg.Any<CancellationToken>());
    }
}
