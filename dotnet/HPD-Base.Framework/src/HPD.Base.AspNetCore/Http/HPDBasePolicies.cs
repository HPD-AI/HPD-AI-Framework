namespace HPD.Base.AspNetCore;

/// <summary>
/// Names ASP.NET Core authorization policies used by HPD.BASE endpoints.
/// </summary>
public static class HPDBasePolicies
{
    /// <summary>
    /// Default policy name for authenticated HPD.BASE record routes.
    /// </summary>
    public const string Authenticated = "HPD.Base.Authenticated";

    /// <summary>
    /// Default policy name for HPD.BASE admin metadata routes.
    /// </summary>
    public const string Admin = "HPD.Base.Admin";
}
