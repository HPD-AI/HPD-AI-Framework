using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using HPD.Auth.Base;
using HPD.Auth.Core.Audit;
using HPD.Auth.Infrastructure.Base;
using HPD.Auth.Infrastructure.Serialization;
using HPD.Base;
using Microsoft.Extensions.Logging;

namespace HPD.Auth.Infrastructure.Stores;

/// <summary>Persists best-effort Auth security audit records through HPD Base.</summary>
internal sealed partial class AuthAuditStore(
    AuthBaseRuntime runtime,
    ILogger<AuthAuditStore> logger) : IAuthAuditWriter, IAuthAuditReader
{
    /// <inheritdoc />
    public async ValueTask WriteAsync(AuthAuditWrite write, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        try
        {
            ValidateWrite(write);
            Guid auditId = Guid.NewGuid();
            DateTimeOffset occurredAt = runtime.GetUtcNow();
            var request = new AuthAuditAppendV1
            {
                AuditId = auditId, TenantId = runtime.TenantId, OccurredAt = occurredAt,
                Action = write.Action, Category = write.Category, Success = write.Success,
                SubjectUserId = write.SubjectUserId, SubjectSessionId = write.SubjectSessionId,
                IpAddress = write.IpAddress, UserAgent = write.UserAgent,
                FailureCode = write.FailureCode, CorrelationId = write.CorrelationId,
                Facts = CanonicalFacts(write.Facts),
            };
            BaseInstalledModuleMutationHandle<AuthAuditAppendV1, AuthAuditAppendResultV1> operation = runtime
                .OpenServiceSession().ModuleMutations.Get(AuthAuditAppendOperationV1.Identity);
            BaseMutationRequestIdentity identity = operation.CreateRequestIdentity(
                request, $"audit:{auditId:D}");
            BaseResult<BaseModuleMutationExecutionResult<AuthAuditAppendResultV1>> result = await operation
                .ExecuteAsync(request, identity, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (result is BaseFailure<BaseModuleMutationExecutionResult<AuthAuditAppendResultV1>>)
                AuditWriteFailed(logger);
        }
        catch (OperationCanceledException)
        {
            AuditWriteCancelled(logger);
        }
        catch
        {
            AuditWriteFailed(logger);
        }
    }

    /// <inheritdoc />
    public async ValueTask<ImmutableArray<AuthAuditRecord>> ReadAsync(
        AuthAuditQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateQuery(query);
        BaseResult<BasePage<AuthAuditReadV1.Row>> result = await runtime.OpenServiceSession().Reads.ExecuteOffsetAsync(
            AuthAuditReadV1.Handle,
            new AuthAuditReadV1
            {
                TenantId = runtime.TenantId, SubjectUserId = query.SubjectUserId,
                Action = query.Action, Category = query.Category,
                CorrelationId = query.CorrelationId, From = query.From, To = query.To,
            }, BaseReadOffsetRequest.Create(query.Offset, query.Limit), cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<BasePage<AuthAuditReadV1.Row>> failure)
            throw new AuthBasePersistenceException(failure.Error.Code);
        return result.RequireValue().Items.Select(MapRecord).ToImmutableArray();
    }

    private static AuthAuditRecord MapRecord(AuthAuditReadV1.Row row) => new()
    {
        AuditId = row.AuditId, InstanceId = row.InstanceId, OccurredAt = row.OccurredAt,
        Action = row.Action, Category = row.Category, Success = row.Success,
        SubjectUserId = row.SubjectUserId, SubjectSessionId = row.SubjectSessionId,
        IpAddress = row.IpAddress, UserAgent = row.UserAgent, FailureCode = row.FailureCode,
        CorrelationId = row.CorrelationId, Facts = ParseFacts(row.Facts),
    };

    private static BaseCanonicalJson CanonicalFacts(ImmutableArray<AuthAuditFact> facts)
    {
        Dictionary<string, string> values = facts.ToDictionary(static fact => fact.Key, static fact => fact.Value, StringComparer.Ordinal);
        return BaseCanonicalJson.ParseAndValidate(
            JsonSerializer.SerializeToUtf8Bytes(
                values, HPDAuthInfrastructureJsonSerializerContext.Default.DictionaryStringString),
            FactsLimits());
    }

    private static ImmutableArray<AuthAuditFact> ParseFacts(BaseCanonicalJson facts)
    {
        using JsonDocument document = JsonDocument.Parse(facts.Utf8);
        return document.RootElement.EnumerateObject()
            .Select(static property => new AuthAuditFact(property.Name, property.Value.GetString()!))
            .ToImmutableArray();
    }

    private static BaseCanonicalJsonLimits FactsLimits() => new()
    {
        MaximumCanonicalBytes = 1_024, MaximumDepth = 16,
        MaximumArrayItemsPerContainer = 8, MaximumObjectPropertiesPerContainer = 8,
        MaximumTotalNodes = 9, MaximumTotalStringUtf8Bytes = 1_024,
        MaximumTotalNameUtf8Bytes = 1_024,
    };

    private static void ValidateWrite(AuthAuditWrite write)
    {
        if (write.SubjectUserId == Guid.Empty || write.SubjectSessionId == Guid.Empty ||
            write.Facts.Length > 8 || write.Success == (write.FailureCode is not null) ||
            Encoding.UTF8.GetByteCount(write.UserAgent ?? string.Empty) > 512)
            throw new InvalidOperationException("The audit write is invalid.");
        if (write.CorrelationId is { } correlation &&
            (correlation.Length > 128 || correlation.Any(static value => value < 0x21 || value > 0x7e)))
            throw new InvalidOperationException("The audit write is invalid.");
        if (write.Facts.Select(static fact => fact.Key).Distinct(StringComparer.Ordinal).Count() != write.Facts.Length)
            throw new InvalidOperationException("The audit write is invalid.");
    }

    private static void ValidateQuery(AuthAuditQuery query)
    {
        if (query.Offset is < 0 or > 100_000 || query.Limit is < 1 or > 200 || query.From >= query.To)
            throw new ArgumentException("The audit query is invalid.", nameof(query));
    }

    [LoggerMessage(2301, LogLevel.Debug, "Auth audit write was cancelled")]
    private static partial void AuditWriteCancelled(ILogger logger);

    [LoggerMessage(2302, LogLevel.Error, "Auth audit write failed")]
    private static partial void AuditWriteFailed(ILogger logger);
}
