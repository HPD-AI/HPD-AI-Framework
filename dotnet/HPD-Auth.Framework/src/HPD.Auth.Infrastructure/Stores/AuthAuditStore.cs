using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using HPD.Auth.Core.Audit;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Infrastructure.Data;
using HPD.Auth.Infrastructure.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HPD.Auth.Infrastructure.Stores;

public sealed partial class AuthAuditStore(
    HPDAuthDbContext context,
    ITenantContext tenantContext,
    ILogger<AuthAuditStore> logger,
    IAuthCorrelationContext? correlationContext = null) : IAuthAuditWriter, IAuthAuditReader
{
    public async ValueTask WriteAsync(
        AuthAuditWrite write,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        try
        {
            ValidateWrite(write);
            var facts = write.Facts
                .Select(static fact => new AuthAuditFactPersistence(fact.Key, fact.Value))
                .ToArray();
            var factsJson = JsonSerializer.Serialize(
                facts,
                HPDAuthInfrastructureJsonSerializerContext.Default.AuthAuditFactPersistenceArray);

            context.AuthAuditEntries.Add(new AuthAuditEntity
            {
                AuditId = Guid.NewGuid(),
                InstanceId = tenantContext.InstanceId,
                OccurredAtUtc = TimeProvider.System.GetUtcNow().UtcDateTime,
                Action = write.Action,
                Category = write.Category,
                Success = write.Success,
                SubjectUserId = write.SubjectUserId,
                SubjectSessionId = write.SubjectSessionId,
                IpAddress = write.IpAddress,
                UserAgent = write.UserAgent,
                FailureCode = write.FailureCode,
                CorrelationId = write.CorrelationId ?? correlationContext?.CorrelationId,
                FactsJson = factsJson
            });
            await context.SaveChangesAsync(cancellationToken);
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

    public async ValueTask<ImmutableArray<AuthAuditRecord>> ReadAsync(
        AuthAuditQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateQuery(query);

        IQueryable<AuthAuditEntity> source = context.AuthAuditEntries;
        if (query.SubjectUserId is { } userId)
            source = source.Where(entry => entry.SubjectUserId == userId);
        if (query.Action is { } action)
            source = source.Where(entry => entry.Action == action);
        if (query.Category is { } category)
            source = source.Where(entry => entry.Category == category);
        if (query.CorrelationId is { } correlation)
            source = source.Where(entry => entry.CorrelationId == correlation);
        if (query.From is { } from)
            source = source.Where(entry => entry.OccurredAtUtc >= from.UtcDateTime);
        if (query.To is { } to)
            source = source.Where(entry => entry.OccurredAtUtc < to.UtcDateTime);

        var entities = await source
            .OrderByDescending(static entry => entry.OccurredAtUtc)
            .ThenByDescending(static entry => entry.AuditId)
            .Skip(query.Offset)
            .Take(query.Limit)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        return entities.Select(MapRecord).ToImmutableArray();
    }

    private static AuthAuditRecord MapRecord(AuthAuditEntity entity)
    {
        var persisted = JsonSerializer.Deserialize(
                entity.FactsJson,
                HPDAuthInfrastructureJsonSerializerContext.Default.AuthAuditFactPersistenceArray)
            ?? throw new InvalidOperationException("Stored audit facts are invalid.");
        var facts = persisted
            .Select(static fact => new AuthAuditFact(fact.Key, fact.Value))
            .ToImmutableArray();
        return new AuthAuditRecord
        {
            AuditId = entity.AuditId,
            InstanceId = entity.InstanceId,
            OccurredAt = new DateTimeOffset(DateTime.SpecifyKind(entity.OccurredAtUtc, DateTimeKind.Utc)),
            Action = entity.Action,
            Category = entity.Category,
            Success = entity.Success,
            SubjectUserId = entity.SubjectUserId,
            SubjectSessionId = entity.SubjectSessionId,
            IpAddress = entity.IpAddress,
            UserAgent = entity.UserAgent,
            FailureCode = entity.FailureCode,
            CorrelationId = entity.CorrelationId,
            Facts = facts
        };
    }

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
        if (query.Offset is < 0 or > 100_000 || query.Limit is < 1 or > 200 ||
            query.From >= query.To)
            throw new ArgumentException("The audit query is invalid.", nameof(query));
    }

    [LoggerMessage(2301, LogLevel.Debug, "Auth audit write was cancelled")]
    private static partial void AuditWriteCancelled(ILogger logger);

    [LoggerMessage(2302, LogLevel.Error, "Auth audit write failed")]
    private static partial void AuditWriteFailed(ILogger logger);
}

internal sealed record AuthAuditFactPersistence(string Key, string Value);
