using System.Collections.Immutable;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite;

public sealed partial class SqliteRecordStore : IBaseLogicalIndexCertificationInspection
{
    BaseLogicalIndexProviderCapability IBaseLogicalIndexCertificationInspection
        .LogicalIndexCertificationCapability =>
        BaseLogicalIndexProviderContract.CloneCapability(_logicalIndexCapability);

    async ValueTask<BaseLogicalIndexCertificationSnapshot>
        IBaseLogicalIndexCertificationInspection.InspectLogicalIndexForCertificationAsync(
            string collectionId,
            BaseLogicalIndexChecksum indexChecksum,
            CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenInitializedAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        SqlitePhysicalModel.CollectionModel collection = _physical.Collection(collectionId);
        SqlitePhysicalModel.IndexModel index = collection.Indexes.SingleOrDefault(
            value => value.Definition.StoreRequired && value.Definition.Checksum == indexChecksum)
            ?? throw new InvalidOperationException(BaseSchemaErrorCodes.ProviderEvidenceInvalid);

        long generation;
        BaseSchemaAuthorityChecksum logicalPublication;
        byte[] previousDirectoryPublication;
        byte[] directoryPublication;
        byte[] memberSet;
        long postingCount;
        long directoryBytes;
        long comparisonCount;
        long transientBytes;
        await using (SqliteCommand authority = connection.CreateCommand())
        {
            authority.Transaction = transaction;
            authority.CommandTimeout = TimeoutSeconds();
            authority.CommandText = $"SELECT generation,state,publication_checksum,previous_directory_publication_checksum,directory_publication_checksum,member_set_checksum,posting_count,directory_bytes,comparison_count,transient_bytes FROM {_names.LogicalIndexes} WHERE collection_id=$collection AND index_checksum=$index;";
            authority.Parameters.AddWithValue("$collection", collectionId);
            authority.Parameters.Add("$index", SqliteType.Blob).Value = indexChecksum.ToArray();
            await using SqliteDataReader reader = await authority.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                || reader.GetInt64(0) <= 0
                || reader.GetInt64(1) != (long)BaseLogicalIndexGenerationState.Ready
                || reader.GetFieldValue<byte[]>(2).Length != 32
                || reader.GetFieldValue<byte[]>(3).Length is not (0 or 32)
                || reader.GetFieldValue<byte[]>(4).Length != 32
                || reader.GetFieldValue<byte[]>(5).Length != 32
                || reader.GetInt64(6) < 0 || reader.GetInt64(7) < 0
                || reader.GetInt64(8) < 0 || reader.GetInt64(9) < 0)
                throw new InvalidOperationException(BaseSchemaErrorCodes.ProviderEvidenceInvalid);
            generation = reader.GetInt64(0);
            logicalPublication = BaseSchemaAuthorityChecksum.Create(reader.GetFieldValue<byte[]>(2));
            previousDirectoryPublication = reader.GetFieldValue<byte[]>(3);
            directoryPublication = reader.GetFieldValue<byte[]>(4);
            memberSet = reader.GetFieldValue<byte[]>(5);
            postingCount = reader.GetInt64(6);
            directoryBytes = reader.GetInt64(7);
            comparisonCount = reader.GetInt64(8);
            transientBytes = reader.GetInt64(9);
        }
        if (generation == 1 && previousDirectoryPublication.Length != 0
            || generation > 1 && previousDirectoryPublication.Length != 32)
            throw new InvalidOperationException(BaseSchemaErrorCodes.ProviderEvidenceInvalid);

        var records = new List<(RecordId Id, RecordPayload Payload)>();
        await using (SqliteCommand contents = connection.CreateCommand())
        {
            contents.Transaction = transaction;
            contents.CommandTimeout = TimeoutSeconds();
            contents.CommandText = $"SELECT {collection.SelectList} FROM {collection.Table};";
            await using SqliteDataReader reader = await contents.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                RecordEnvelope envelope = collection.ReadEnvelope(reader, _options.StoreId, out _);
                records.Add((RecordId.Create(envelope.Id.Value),
                    RecordCloneHelpers.ClonePayload(envelope.Payload)));
            }
        }
        if (!BaseLogicalIndexDirectoryContract.TryCreate(collection.Definition, index.Definition,
                records, BaseLogicalIndexDirectoryContract.Limits(_logicalIndexCapability),
                out BaseLogicalIndexDirectory? directory)
            || directory is null
            || !CryptographicOperations.FixedTimeEquals(memberSet, directory.MemberSetChecksum.AsSpan())
            || postingCount != directory.Accounting.Postings
            || directoryBytes != directory.Accounting.RetainedDirectoryBytes)
            throw new InvalidOperationException(BaseSchemaErrorCodes.ProviderEvidenceInvalid);
        directory = directory with
        {
            Accounting = directory.Accounting with
            {
                Comparisons = comparisonCount,
                TransientBytes = transientBytes,
            },
        };
        var snapshot = new BaseLogicalIndexCertificationSnapshot
        {
            Authority = new BaseLogicalIndexDirectoryAuthority
            {
                IndexId = BaseLogicalIndexId.Create(index.Definition.Id.ToString()),
                IndexVersion = index.Definition.Version,
                IndexChecksum = BaseLogicalIndexChecksum.Create(indexChecksum.ToArray()),
                Generation = generation,
                LogicalPublicationChecksum = logicalPublication,
                PreviousDirectoryPublicationChecksum = previousDirectoryPublication.ToImmutableArray(),
                DirectoryPublicationChecksum = directoryPublication.ToImmutableArray(),
                MemberSetChecksum = memberSet.ToImmutableArray(),
            },
            Directory = directory.DeepClone(),
        };
        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.DeepClone();
    }

    async ValueTask IBaseLogicalIndexCertificationInspection
        .CorruptLogicalIndexMemberSetForCertificationAsync(
            string collectionId,
            BaseLogicalIndexChecksum indexChecksum,
            CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenInitializedAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = TimeoutSeconds();
        command.CommandText = $"UPDATE {_names.LogicalIndexes} SET member_set_checksum=$member WHERE collection_id=$collection AND index_checksum=$index;";
        command.Parameters.AddWithValue("$collection", collectionId);
        command.Parameters.Add("$index", SqliteType.Blob).Value = indexChecksum.ToArray();
        byte[] current;
        await using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandTimeout = TimeoutSeconds();
            read.CommandText = $"SELECT member_set_checksum FROM {_names.LogicalIndexes} WHERE collection_id=$collection AND index_checksum=$index;";
            read.Parameters.AddWithValue("$collection", collectionId);
            read.Parameters.Add("$index", SqliteType.Blob).Value = indexChecksum.ToArray();
            current = (byte[]?)await read.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException(BaseSchemaErrorCodes.ProviderEvidenceInvalid);
        }
        if (current.Length != 32)
            throw new InvalidOperationException(BaseSchemaErrorCodes.ProviderEvidenceInvalid);
        current[0] ^= 0x01;
        command.Parameters.Add("$member", SqliteType.Blob).Value = current;
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidOperationException(BaseSchemaErrorCodes.ProviderEvidenceInvalid);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
