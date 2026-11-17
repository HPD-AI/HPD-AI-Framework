using HPD.AI.Platform.Studio;

namespace HPD.Auth.Studio;

/// <summary>Configures the hosted HPD.Auth session used by HPD AI Platform Studio.</summary>
public sealed class HPDAuthStudioOptions
{
    public StudioAuthenticationKind Kind { get; set; } = StudioAuthenticationKind.CookieBff;
    public string IntegrationId { get; set; } = "hpd.auth.studio";
    public int IntegrationVersion { get; set; } = 1;
    public string AuthenticationScheme { get; set; } = "Cookies";
    public string Audience { get; set; } = "hpd.studio";
    public IReadOnlyList<string> AllowedOrigins { get; set; } = [];
    public string StudioLoginRoute { get; set; } = "/auth/studio/login";
    public string StudioCallbackRoute { get; set; } = "/auth/studio/callback";
    public string StudioLogoutRoute { get; set; } = "/auth/studio/logout";
    public string StudioSessionRoute { get; set; } = "/auth/studio/session";
    public string ProviderLoginRoute { get; set; } = "/auth/login";
    public string ProviderReturnQueryParameter { get; set; } = "return";
    public string FreshAuthenticationRoute { get; set; } = "/auth/studio-fresh";
    public TimeSpan MaximumSession { get; set; } = TimeSpan.FromHours(8);
    public TimeSpan AuthorizationLifetime { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan ReturnTargetLifetime { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan FreshChallengeLifetime { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan MaximumFreshAuthenticationAge { get; set; } = TimeSpan.FromMinutes(5);
    public string DefaultReturnTarget { get; set; } = "/studio";
    public string AntiForgeryHeaderName { get; set; } = "X-HPD-Studio-Authority";
    public string AntiForgeryPurpose { get; set; } = "hpd.studio.auth";
    public IReadOnlyList<string> MultiFactorClaimTypes { get; set; } = ["amr", "acr", "aal"];
    public IReadOnlyList<string> MultiFactorClaimValues { get; set; } =
        ["mfa", "totp", "webauthn", "aal2", "aal3"];
    public IReadOnlyList<string> HardwareClaimValues { get; set; } =
        ["webauthn", "fido2", "hardware", "aal3"];
    public IReadOnlyList<StudioFreshAuthenticationClass> SupportedFreshAuthentication { get; set; } =
        [StudioFreshAuthenticationClass.Password, StudioFreshAuthenticationClass.MultiFactor];
}
