namespace HPD.Base.Auth;

internal sealed class HPDBaseAuthProjectionException(string code, int statusCode) : Exception
{
    internal string Code { get; } = code;
    internal int StatusCode { get; } = statusCode;
}
