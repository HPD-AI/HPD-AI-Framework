using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

internal sealed class BaseTextConsistencyTokenCodec(BaseOpaqueTokenProtector tokens, TimeProvider timeProvider)
{
    private const string Purpose = "hpd.base.text.consistency.v1";
    private static readonly byte[] Scope = SHA256.HashData(Encoding.ASCII.GetBytes(Purpose));

    internal BaseTextConsistencyToken Issue(BaseTextAuthoritySnapshot snapshot)
    {
        DateTimeOffset issued = timeProvider.GetUtcNow(), expires = checked(issued + TimeSpan.FromHours(24));
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(expires.UtcTicks); writer.Write(issued.UtcTicks); writer.Write(snapshot.StoreIdentityDigest);
            writer.Write(snapshot.RestoreEpoch); writer.Write(snapshot.SchemaGeneration); writer.Write(snapshot.CollectionId);
            writer.Write(snapshot.PurgeGeneration); writer.Write(snapshot.TextIndexId); writer.Write(snapshot.TextIndexVersion);
            writer.Write(snapshot.TextIndexGeneration); writer.Write(snapshot.SearchVisibleThrough.Value);
        }
        return BaseTextConsistencyToken.Create(tokens.Protect(Purpose, 1, stream.ToArray(), Scope));
    }

    internal bool Satisfied(BaseTextConsistencyToken token, BaseTextAuthoritySnapshot snapshot)
    {
        BaseOpaqueTokenResult result = tokens.Unprotect(Purpose, 1, token.Encode(), 32, 4096, Scope);
        if (result.Status != BaseOpaqueTokenStatus.Valid || result.Plaintext is null) return false;
        try
        {
            using var reader = new BinaryReader(new MemoryStream(result.Plaintext, writable: false), Encoding.UTF8);
            long expires = reader.ReadInt64(); _ = reader.ReadInt64(); string store = reader.ReadString(); long restore = reader.ReadInt64(); long schema = reader.ReadInt64(); string collection = reader.ReadString(); long purge = reader.ReadInt64(); string index = reader.ReadString(); int version = reader.ReadInt32(); long generation = reader.ReadInt64(); long position = reader.ReadInt64();
            return reader.BaseStream.Position == reader.BaseStream.Length && timeProvider.GetUtcNow().UtcTicks <= expires
                && store == snapshot.StoreIdentityDigest && restore == snapshot.RestoreEpoch && schema == snapshot.SchemaGeneration
                && collection == snapshot.CollectionId && purge == snapshot.PurgeGeneration && index == snapshot.TextIndexId
                && version == snapshot.TextIndexVersion && generation == snapshot.TextIndexGeneration && position <= snapshot.SearchVisibleThrough.Value;
        }
        catch (Exception exception) when (exception is EndOfStreamException or IOException) { return false; }
    }
}
