# HPD.Auth.OAuth

Adds social login with Google, GitHub, and Microsoft.

## Installation

```bash
dotnet add package HPD.Auth.OAuth
```

```csharp
builder.Services
    .AddHPDAuth(options =>
    {
        options.OAuth.Google.ClientId = "...";
        options.OAuth.Google.ClientSecret = "...";
        options.OAuth.GitHub.ClientId = "...";
        options.OAuth.GitHub.ClientSecret = "...";
    })
    .AddAuthentication()
    .AddOAuth();   // ← this package

app.UseHPDAuth();
app.MapHPDAuthEndpoints();
app.MapHPDOAuthEndpoints();   // ← add this
```

## How it works

1. User clicks "Sign in with Google" → your frontend redirects to `GET /auth/google`
2. User is redirected to Google's consent screen
3. Google redirects back to `GET /auth/google/callback`
4. HPD.Auth exchanges the code for a Google profile
5. If the email matches an existing user: logs them in + links the Google account
6. If no user exists: creates a new account + logs them in
7. Redirects to your `returnUrl` with tokens in the query string

## Account linking

If a user signs up with email/password and later uses "Sign in with Google" with the same email, the accounts are automatically linked. The user can then log in with either method.

## Endpoints

| Method | Path | Description |
|---|---|---|
| `GET` | `/auth/google` | Initiate Google OAuth |
| `GET` | `/auth/google/callback` | Google OAuth callback |
| `GET` | `/auth/github` | Initiate GitHub OAuth |
| `GET` | `/auth/github/callback` | GitHub OAuth callback |
| `GET` | `/auth/microsoft` | Initiate Microsoft OAuth |
| `GET` | `/auth/microsoft/callback` | Microsoft OAuth callback |

Only providers with credentials configured are active.

## Provider setup

### Google

1. [Google Cloud Console](https://console.cloud.google.com) → APIs & Services → Credentials → Create OAuth 2.0 Client
2. Authorized redirect URI: `https://yourapp.com/auth/google/callback`

### GitHub

1. GitHub → Settings → Developer settings → OAuth Apps → New OAuth App
2. Authorization callback URL: `https://yourapp.com/auth/github/callback`

### Microsoft

1. [Azure Portal](https://portal.azure.com) → Microsoft Entra ID → App registrations → New registration
2. Redirect URI: `https://yourapp.com/auth/microsoft/callback`
