# OAuth Endpoints

Social login endpoints for Google, GitHub, and Microsoft.

Requires `HPD.Auth.OAuth` and `app.MapHPDOAuthEndpoints()`.


## GET /auth/:provider

Initiate an OAuth login flow. Redirects the user to the provider's consent screen.

**Authentication:** None required.

### Supported providers

| Provider | Path |
|---|---|
| Google | `GET /auth/google` |
| GitHub | `GET /auth/github` |
| Microsoft | `GET /auth/microsoft` |

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `returnUrl` | `string?` | URL to redirect to after successful login. |

**Response `302 Redirect`**

Redirects to the OAuth provider's authorization URL.

### Example

```html
<a href="/auth/google?returnUrl=/dashboard">Sign in with Google</a>
```


## GET /auth/:provider/callback

OAuth callback endpoint. Called by the provider after the user authorizes. Not called directly.

**Authentication:** None required.

### How it works

1. Provider redirects back with an authorization code
2. HPD.Auth exchanges the code for a profile
3. If a user with that email exists: logs them in and links the external login
4. If no user exists: creates a new account and logs them in
5. Redirects to `returnUrl` with the token in the query string

**Response `302 Redirect`**

Redirects to the `returnUrl` with the token appended:

```
https://yourapp.com/dashboard?access_token=eyJhbGci...&refresh_token=abc123...
```

Extract the tokens in your frontend and store them as you would a normal login response.


## Configuration

Configure provider credentials in `HPDAuthOptions`:

```csharp
builder.Services.AddHPDAuth(options =>
{
    options.OAuth.Google.ClientId = "your-google-client-id";
    options.OAuth.Google.ClientSecret = "your-google-client-secret";

    options.OAuth.GitHub.ClientId = "your-github-client-id";
    options.OAuth.GitHub.ClientSecret = "your-github-client-secret";

    options.OAuth.Microsoft.ClientId = "your-microsoft-client-id";
    options.OAuth.Microsoft.ClientSecret = "your-microsoft-client-secret";
});
```

### Google setup

1. Go to [Google Cloud Console](https://console.cloud.google.com)
2. Create an OAuth 2.0 client
3. Set the redirect URI to `https://yourapp.com/auth/google/callback`

### GitHub setup

1. Go to GitHub → Settings → Developer Settings → OAuth Apps
2. Set the callback URL to `https://yourapp.com/auth/github/callback`

### Microsoft setup

1. Go to [Azure Portal](https://portal.azure.com) → App registrations
2. Set the redirect URI to `https://yourapp.com/auth/microsoft/callback`
