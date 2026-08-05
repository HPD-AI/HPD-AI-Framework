
namespace HPD.Base;

/// <summary>
/// Creates principal-bound BASE application sessions.
/// </summary>
public interface IBaseSessionFactory
{
    /// <summary>
    /// Creates a session for one trusted principal projection.
    /// </summary>
    BaseSession For(
        PrincipalContext principal,
        Action<BaseSessionOptions>? configure = null);
}
