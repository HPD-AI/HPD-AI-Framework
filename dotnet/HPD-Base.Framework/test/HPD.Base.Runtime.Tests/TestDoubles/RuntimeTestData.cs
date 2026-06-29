using HPD.Base.Runtime;

namespace HPD.Base.Runtime.Tests;

internal static class RuntimeTestData
{
    public static PrincipalContext AnonymousPrincipal => new()
    {
        AuthenticationState = PrincipalAuthenticationState.Anonymous
    };

    public static OperationContext Operation(BaseOperationKind operation, string collectionId = "items") => new()
    {
        Operation = operation,
        CollectionId = collectionId,
        Now = DateTimeOffset.UnixEpoch
    };
}
