using HPD.Base;

namespace HPD.Base.AspNetCore;

/// <summary>
/// Names operation ids for HPD.BASE ASP.NET Core routes.
/// </summary>
public static class BaseHttpRouteNames
{
    /// <summary>Admin manifest route id.</summary>
    public const string AdminManifest = "base.admin.manifest";

    /// <summary>Admin capabilities route id.</summary>
    public const string AdminCapabilities = "base.admin.capabilities";

    /// <summary>Admin schema route id.</summary>
    public const string AdminSchema = "base.admin.schema";

    /// <summary>Public collections list route id.</summary>
    public const string CollectionsList = "base.collections.list";

    /// <summary>Public collection detail route id.</summary>
    public const string CollectionsGet = "base.collections.get";

    /// <summary>Admin collections list route id.</summary>
    public const string AdminCollectionsList = "base.admin.collections.list";

    /// <summary>Admin collection detail route id.</summary>
    public const string AdminCollectionsGet = "base.admin.collections.get";

    /// <summary>Admin health route id.</summary>
    public const string AdminHealth = "base.admin.health";

    /// <summary>Admin diagnostics route id.</summary>
    public const string AdminDiagnostics = "base.admin.diagnostics";

    /// <summary>Admin policy explain route id.</summary>
    public const string AdminPolicyExplain = "base.admin.policy.explain";

    internal static string RecordCreate => BaseRouteIds.RecordsCreate;
}
