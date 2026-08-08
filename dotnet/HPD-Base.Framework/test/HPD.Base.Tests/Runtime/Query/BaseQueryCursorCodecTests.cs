using FluentAssertions;
using Microsoft.Extensions.Options;

namespace HPD.Base.Tests.Runtime.Query;

public sealed class BaseQueryCursorCodecTests
{
    [Fact]
    public void CursorClassifiesPurposeScopeSchemaRestoreAndGuaranteeFailures()
    {
        using BaseOpaqueTokenProtector protector = Protector();
        var codec = new BaseQueryCursorCodec(protector, TimeProvider.System);
        RecordQuery query = Query();
        OperationContext context = Context();
        var payload = new BaseQueryCursorPayload
        {
            Guarantee = QueryCursorGuarantee.StableHistory,
            Direction = QueryCursorDirection.After,
            RestoreEpoch = 3,
            SchemaGeneration = 7,
            AppendHighWater = 11,
            PurgeGeneration = 2,
            Keys = [new BaseQueryCursorKey(true, "\"value\"")],
            RecordId = "record",
        };
        string token = codec.Protect(payload, query, 10, "store", "items", context);
        string wrongPurpose = protector.Protect(
            "hpd.base.realtime.cursor",
            1,
            new byte[110],
            new byte[32]);

        Read(token, codec, query, context, restore: 4, schema: 7, QueryCursorGuarantee.StableHistory, purge: 2)
            .Status.Should().Be(BaseQueryCursorStatus.RestoreInvalidated);
        Read(token, codec, query, context, restore: 3, schema: 8, QueryCursorGuarantee.StableHistory, purge: 2)
            .Status.Should().Be(BaseQueryCursorStatus.SchemaChanged);
        Read(token, codec, query, context, restore: 3, schema: 7, QueryCursorGuarantee.Seek, purge: 2)
            .Status.Should().Be(BaseQueryCursorStatus.GuaranteeUnavailable);
        codec.Unprotect(token, query, 10, "other", "items", context, 3, 7, QueryCursorGuarantee.StableHistory, 2)
            .Status.Should().Be(BaseQueryCursorStatus.ScopeMismatch);
        codec.Unprotect(token, query, 10, "store", "other", context, 3, 7, QueryCursorGuarantee.StableHistory, 2)
            .Status.Should().Be(BaseQueryCursorStatus.ScopeMismatch);
        codec.Unprotect(token, query, 10, "store", "items", context with { ProjectId = "other" }, 3, 7, QueryCursorGuarantee.StableHistory, 2)
            .Status.Should().Be(BaseQueryCursorStatus.ScopeMismatch);
        Read(wrongPurpose, codec, query, context, 3, 7, QueryCursorGuarantee.StableHistory, 2)
            .Status.Should().Be(BaseQueryCursorStatus.Invalid);
        Read("not-a-token", codec, query, context, 3, 7, QueryCursorGuarantee.StableHistory, 2)
            .Status.Should().Be(BaseQueryCursorStatus.Invalid);
    }

    private static BaseQueryCursorReadResult Read(
        string token,
        BaseQueryCursorCodec codec,
        RecordQuery query,
        OperationContext context,
        long restore,
        long schema,
        QueryCursorGuarantee guarantee,
        long purge) => codec.Unprotect(
            token, query, 10, "store", "items", context, restore, schema, guarantee, purge);

    private static RecordQuery Query() => new()
    {
        Sort = [new QuerySort("title")],
        Page = new QueryPage
        {
            Mode = QueryPaginationMode.Cursor,
            Limit = 10,
            CursorDirection = QueryCursorDirection.After,
        },
    };

    private static OperationContext Context() => new()
    {
        Operation = BaseOperationKind.Query,
        CollectionId = "items",
        TenantId = "tenant",
        ProjectId = "project",
        Now = DateTimeOffset.UtcNow,
    };

    private static BaseOpaqueTokenProtector Protector() => new(Options.Create(
        new HPDBaseTokenProtectionOptions
        {
            ActiveKey = new BaseOpaqueTokenKey
            {
                Id = 4,
                Key = Enumerable.Repeat((byte)0x44, 32).ToArray(),
                IssueNotBefore = DateTimeOffset.UnixEpoch,
            },
        }));
}
