# Introduction

HPD.Auth is an authentication library for ASP.NET Core. It gives you a complete auth REST API — signup, login, sessions, 2FA, passkeys, OAuth, and an admin API — by adding a few lines to `Program.cs`.

## What it is

HPD.Auth wraps ASP.NET Core Identity's trusted primitives (password hashing, security stamps, lockout, token providers) and exposes them as a ready-made HTTP API. You configure it once, call `MapHPDAuthEndpoints()`, and your app immediately has a working auth layer.

The mental model is: **you get the developer experience of a hosted auth service, but it runs in-process as a library.**

With Auth0 or Clerk, you configure things in a dashboard, call HTTP endpoints from your app, and never touch the internals. With HPD.Auth you do the same — but the configuration is in `Program.cs` instead of a dashboard, and the server runs in your process instead of someone else's cloud.

## What it is not

- **Not a reimplementation of cryptography.** Password hashing, JWT validation, and WebAuthn all come from the .NET platform and ASP.NET Identity.
- **Not an OIDC server.** HPD.Auth does not issue tokens to external clients or act as a federation gateway. If you need that, look at Duende IdentityServer or OpenIddict.
- **Not a full framework.** You don't adopt HPD.Auth as a framework — you add it to your existing ASP.NET app.

## How it compares

| | HPD.Auth | Raw ASP.NET Identity | Auth0 / Clerk |
|---|---|---|---|
| HTTP endpoints included | Yes | No — you build them | Yes |
| Session management | Yes | No | Yes |
| Admin API | Yes | No | Yes |
| Audit log | Yes | No | Yes |
| Runs in your process | Yes | Yes | No |
| Per-user pricing | No | No | Yes |
| Data leaves your infra | No | No | Yes |

## Package structure

HPD.Auth is split into packages so you install only what you need:

| Package | What it adds |
|---|---|
| `HPD.Auth` | Core endpoints: signup, login, logout, sessions, password reset |
| `HPD.Auth.Authentication` | JWT + Cookie + dual-auth PolicyScheme |
| `HPD.Auth.TwoFactor` | TOTP (authenticator apps) + passkeys (FIDO2/WebAuthn) |
| `HPD.Auth.OAuth` | Google, GitHub, Microsoft social login |
| `HPD.Auth.Admin` | Admin endpoints: user management, ban, roles, audit log |
| `HPD.Auth.Authorization` | Built-in policies, rate limiting, feature flags |
| `HPD.Auth.Audit` | Event publishing, audit log enrichment |

## Next steps

- [Install HPD.Auth →](/Getting Started/01 Installation)
- [5-minute quick start →](/Getting Started/02 Quick Start)
- [Full configuration reference →](/Getting Started/03 Configuration Reference)
