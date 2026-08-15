using Microsoft.AspNetCore.Http;

namespace HPD.Base.AspNetCore;

/// <summary>Provides one bounded correlation identifier for a BASE HTTP request.</summary>
public interface IBaseHttpCorrelationProvider
{
    /// <summary>Gets the request correlation identifier.</summary>
    string GetCorrelationId(HttpContext context);
}

internal sealed class BaseHttpCorrelationException : Exception
{
    internal BaseHttpCorrelationException() : base("The request correlation identifier is invalid.") { }
}
