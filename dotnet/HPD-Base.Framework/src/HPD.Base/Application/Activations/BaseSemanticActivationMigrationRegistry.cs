using System.Collections.Immutable;
using System.Security.Cryptography;

namespace HPD.Base;

internal sealed class BaseSemanticActivationMigrationRegistry
{
    private readonly Dictionary<(string Id, int Version, string Checksum), BaseSemanticActivationMigrationDefinition> edges;

    internal BaseSemanticActivationMigrationRegistry(IEnumerable<BaseSemanticActivationMigrationDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        edges = definitions.Select(BaseSemanticActivationMigrationContract.Seal).ToDictionary(
            static value => (value.From.Id, value.From.Version, Convert.ToHexString(value.From.Checksum.AsSpan())));
    }

    internal bool MatchesInstalledChain(
        BaseSemanticActivationDefinitionKey source,
        BaseSemanticActivationDefinitionIdentity target,
        ImmutableArray<BaseSemanticActivationDefinitionMigrationAuthority> returned)
    {
        if (DefinitionEqual(source, target)) return returned.IsDefaultOrEmpty;
        if (returned.IsDefaultOrEmpty) return false;
        BaseSemanticActivationDefinitionKey cursor = source;
        int ordinal = 0;
        while (edges.TryGetValue((cursor.Id, cursor.Version, Convert.ToHexString(cursor.Checksum.AsSpan())), out BaseSemanticActivationMigrationDefinition? installed))
        {
            if (ordinal >= returned.Length) return false;
            BaseSemanticActivationDefinitionMigrationAuthority authority = returned[ordinal++];
            if (!string.Equals(authority.MigrationId, installed.Id, StringComparison.Ordinal)
                || authority.MigrationVersion != installed.Version
                || !DefinitionEqual(authority.From, installed.From)
                || !DefinitionEqual(authority.To, installed.To)
                || !CryptographicOperations.FixedTimeEquals(authority.Checksum.AsSpan(),
                    BaseSemanticActivationMigrationAuthorityContract.Checksum(authority).AsSpan())) return false;
            cursor = installed.To;
            if (DefinitionEqual(cursor, target)) return ordinal == returned.Length;
        }
        return false;
    }

    private static bool DefinitionEqual(BaseSemanticActivationDefinitionKey left, BaseSemanticActivationDefinitionKey right) =>
        string.Equals(left.Id, right.Id, StringComparison.Ordinal) && left.Version == right.Version
        && CryptographicOperations.FixedTimeEquals(left.Checksum.AsSpan(), right.Checksum.AsSpan());

    private static bool DefinitionEqual(BaseSemanticActivationDefinitionKey left, BaseSemanticActivationDefinitionIdentity right) =>
        string.Equals(left.Id, right.Id, StringComparison.Ordinal) && left.Version == right.Version
        && CryptographicOperations.FixedTimeEquals(left.Checksum.AsSpan(), right.Checksum.AsSpan());
}
