using HPD.Auth.Admin.Extensions;
using HPD.Auth.Audit.Extensions;
using HPD.Auth.Authentication.Extensions;
using HPD.Auth.Authorization.Extensions;
using HPD.Auth.Extensions;
using HPD.Auth.TwoFactor.Extensions;

// ─────────────────────────────────────────────────────────────────────────────
// HPD.Auth.Sample — Canary Program.cs
//
// Purpose: Verify the full DI chain wires up correctly.
// Running this sample should start the app without exceptions.
//
// This uses cookie-only mode (no JWT key configured) — appropriate for verifying
// that the DI registrations don't throw at startup.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

// ── 1. Register core HPD.Auth services ───────────────────────────────────────
builder.Services
    .AddHPDAuth(options =>
    {
        options.AppName = "TestApp";

        // Password policy (relaxed for sample — do not use in production)
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;

        // Disable email confirmation for the sample so signup returns tokens immediately.
        options.Features.RequireEmailConfirmation = false;

        // Enable audit log.
        options.Features.EnableAuditLog = true;
    })
    // ── 2. Register sub-package services ─────────────────────────────────────
    .AddAuthentication()       // Cookie auth (cookie-only mode: no Jwt.Secret configured)
    .AddAudit()                // Event publisher + audit log enrichment
    .AddAuthorization()        // HPD policies, rate-limit service, result handler
    .AddTwoFactor()            // TOTP service
    .AddAdmin();               // No-op today; reserved for future admin-specific services

// ── 3. Memory cache (used by PasswordEndpoints resend rate-limit) ─────────────
builder.Services.AddMemoryCache();

var app = builder.Build();

// ── 4. Middleware pipeline ────────────────────────────────────────────────────
app.UseHPDAuth();   // UseAuthentication + UseAuthorization in correct order

// ── 5. Map endpoints ─────────────────────────────────────────────────────────
app.MapHPDAuthEndpoints();        // Core: signup, token, logout, user, recover, verify, resend, sessions
app.MapHPDAdminEndpoints();       // Admin: /api/admin/users, /api/admin/audit-logs, etc.
app.MapHPDTwoFactorEndpoints();   // 2FA: /api/auth/factors, /api/auth/passkey/**

app.Run();
