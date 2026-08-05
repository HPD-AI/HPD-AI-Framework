namespace HPD.Agent.Providers.OpenRouter;

/// <summary>OpenRouter-specific reusable client acquisition configuration.</summary>
public sealed class OpenRouterProviderConfig : IProviderConfig
{
    /// <summary>Gets or sets the HTTP-Referer attribution header.</summary>
    public string? HttpReferer { get; set; }

    /// <summary>Gets or sets the X-Title attribution header.</summary>
    public string? AppName { get; set; }
}
