using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Base;
using Microsoft.AspNetCore.Http;

namespace HPD.Base.AspNetCore;

internal static class BaseAtomicBatchIdentityBinder
{
    private const string Operation = "base.atomicBatch.v1";

    internal static OperationResult<BaseRecordBatchRequest> Bind(
        BaseRecordBatchRequest request,
        HttpContext context,
        PrincipalContext principal,
        IRecordStoreRegistry stores)
    {
        if (!context.Request.Headers.TryGetValue(BaseHttpHeaders.IdempotencyKey, out var values))
            return OperationResults.Ok(request);
        if (values.Count != 1 || request.Mode != BaseRecordBatchExecutionMode.Atomic)
            return Invalid("Idempotency-Key is valid only once on an atomic batch request.");

        try
        {
            string key = values[0] ?? string.Empty;
            string[] storeIds = request.Operations
                .Select(operation => stores.GetRegistrationForCollection(operation.CollectionId)?.StoreId ?? string.Empty)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (storeIds is not [var storeId] || string.IsNullOrWhiteSpace(storeId))
                return Invalid("The atomic batch does not resolve to one exact store.");

            string tenant = principal.CurrentTenantId ?? context.Request.RouteValues["tenantId"]?.ToString() ?? string.Empty;
            string project = context.Request.RouteValues["projectId"]?.ToString() ?? string.Empty;
            string scope = $"tenant:{tenant.Normalize(NormalizationForm.FormC)}|project:{project.Normalize(NormalizationForm.FormC)}";
            byte[] canonical = JsonSerializer.SerializeToUtf8Bytes(
                request with { RequestIdentity = null },
                HPDBaseJsonSerializerContext.Default.BaseRecordBatchRequest);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Add(hash, Operation);
            Add(hash, canonical);
            Add(hash, principal.AuthenticationState.ToString());
            Add(hash, principal.SubjectKind.ToString());
            Add(hash, principal.SubjectId ?? string.Empty);
            Add(hash, tenant);
            Add(hash, project);
            Add(hash, storeId);
            BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
                scope,
                Operation,
                key,
                BaseMutationRequestFingerprint.Create(hash.GetHashAndReset()));
            return OperationResults.Ok(request with { RequestIdentity = identity });
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        {
            return Invalid("The atomic batch idempotency identity is invalid.");
        }
    }

    private static void Add(IncrementalHash hash, string value) => Add(hash, Encoding.UTF8.GetBytes(value));
    private static void Add(IncrementalHash hash, ReadOnlySpan<byte> value)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
    }

    private static OperationResult<BaseRecordBatchRequest> Invalid(string message) =>
        OperationResults.ValidationFailed<BaseRecordBatchRequest>(new BaseError
        {
            Code = "base.http.idempotencyKey.invalid",
            Message = message,
            Category = ErrorCategory.Validation,
        });
}
