namespace HPD.Base.AspNetCore;

internal static class BaseManifestExpandBinder
{
    /// <summary>Provides the allowed tokens value.</summary>
    public static readonly string[] AllowedTokens = ["schema", "capabilities", "health", "diagnostics", "collections"];
}
