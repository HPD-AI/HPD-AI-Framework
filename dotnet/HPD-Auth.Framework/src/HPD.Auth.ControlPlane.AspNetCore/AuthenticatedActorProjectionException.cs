namespace HPD.Auth.ControlPlane;

/// <summary>A bounded failure produced while projecting authorized actor facts.</summary>
public sealed class AuthenticatedActorProjectionException : Exception
{
    public string Code { get; }

    internal AuthenticatedActorProjectionException(string code)
        : base("The authenticated actor could not be projected.") => Code = code;
}
