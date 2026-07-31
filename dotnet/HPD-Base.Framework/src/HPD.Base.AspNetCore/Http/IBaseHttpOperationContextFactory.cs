using HPD.Base;
using Microsoft.AspNetCore.Http;

namespace HPD.Base.AspNetCore;

/// <summary>
/// Creates HPD.BASE operation contexts from ASP.NET Core HTTP requests.
/// </summary>
public interface IBaseHttpOperationContextFactory
{
    /// <summary>
    /// Creates an operation context for a known BASE operation.
    /// </summary>
    OperationContext Create(
        HttpContext httpContext,
        PrincipalContext principal,
        BaseOperationKind operation,
        string collectionId,
        string? recordId = null,
        OperationMode mode = OperationMode.User);
}
