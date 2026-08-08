using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

internal static class BaseVectorConsistencyTokenIssuer
{
    internal const string Purpose = "hpd.base.vector.consistency.v1";
    internal static readonly byte[] Scope = SHA256.HashData(Encoding.UTF8.GetBytes(Purpose));

    internal static BaseVectorConsistencyToken Issue(
        BaseVectorAuthoritySnapshot snapshot,
        BaseOpaqueTokenProtector tokens,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(expiresAt.UtcTicks);
            writer.Write(issuedAt.UtcTicks);
            writer.Write(snapshot.StoreIdentityDigest);
            writer.Write(snapshot.RestoreEpoch);
            writer.Write(snapshot.SchemaGeneration);
            writer.Write(snapshot.CollectionId);
            writer.Write(snapshot.PurgeGeneration);
            writer.Write(snapshot.VectorIndexId);
            writer.Write(snapshot.VectorIndexGeneration);
            writer.Write(snapshot.VectorSpaceId);
            writer.Write(snapshot.HighWatermark.Value);
        }
        return BaseVectorConsistencyToken.Parse(tokens.Protect(Purpose, 1, stream.ToArray(), Scope));
    }
}
