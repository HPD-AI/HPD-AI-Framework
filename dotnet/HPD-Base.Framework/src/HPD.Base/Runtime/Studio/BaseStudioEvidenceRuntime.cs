using System.Collections.Immutable;
using System.Security.Cryptography;

namespace HPD.Base;

/// <summary>Captures, reads, and validates provider-neutral durable Studio evidence.</summary>
public interface IBaseStudioEvidenceRuntime
{
    /// <summary>Reads one validated evidence page through the exact provider selected by Runtime.</summary>
    ValueTask<OperationResult<BaseStudioEvidencePage>> ReadPageAsync(IBaseStudioEvidenceStore provider,
        BaseStudioEvidenceRequirement requirement, BaseOwnedScopeSeekAuthority scope, BaseStudioEvidencePageRequest page,
        CancellationToken cancellationToken = default);
}

/// <summary>Default hostile-provider-validating evidence Runtime.</summary>
public sealed class DefaultBaseStudioEvidenceRuntime : IBaseStudioEvidenceRuntime
{
    private static readonly SemaphoreSlim RetainedProviderWork = new(32, 32);
    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseStudioEvidencePage>> ReadPageAsync(IBaseStudioEvidenceStore provider,
        BaseStudioEvidenceRequirement requirement, BaseOwnedScopeSeekAuthority scope, BaseStudioEvidencePageRequest page,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(provider, requirement, scope, page);
        try { return await ReadCoreAsync(provider, requirement, scope, page, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException)
        { return OperationResults.StoreError<BaseStudioEvidencePage>(new BaseError { Code = "base.studio.cancelled", Message = "The Studio evidence operation was cancelled.", Category = ErrorCategory.Store }); }
        catch
        { return OperationResults.StoreError<BaseStudioEvidencePage>(new BaseError { Code = "base.studio.unexpected", Message = "The Studio evidence provider failed unexpectedly.", Category = ErrorCategory.Unexpected }); }
    }

    private static void ValidateRequest(IBaseStudioEvidenceStore provider, BaseStudioEvidenceRequirement requirement,
        BaseOwnedScopeSeekAuthority scope, BaseStudioEvidencePageRequest page)
    {
        ArgumentNullException.ThrowIfNull(provider); ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(page);
        if (!BaseStudioEvidenceContract.Valid(requirement) || page.Take < 1 || page.Take > requirement.Limits.MaximumItems ||
            !BaseStudioEvidenceContract.Position(page.After, out _) || scope.Kind != requirement.Scope.Kind || scope.ProtectedIndexDigest.Length != 32 ||
            !CryptographicOperations.FixedTimeEquals(scope.ProtectedIndexDigest.AsSpan(), requirement.ProtectedScopeSeekChecksum.AsSpan()))
            throw new ArgumentException("The evidence request is invalid.", nameof(requirement));
    }

    private static async ValueTask<OperationResult<BaseStudioEvidencePage>> ReadCoreAsync(IBaseStudioEvidenceStore provider,
        BaseStudioEvidenceRequirement requirement, BaseOwnedScopeSeekAuthority scope, BaseStudioEvidencePageRequest page,
        CancellationToken cancellationToken = default)
    {
        _ = BaseStudioEvidenceContract.Position(page.After, out long after);
        requirement = BaseStudioEvidenceContract.Freeze(requirement);
        scope = scope with { ProtectedIndexDigest = [.. scope.ProtectedIndexDigest] };
        page = page with { After = page.After is null ? null : page.After with
        { CanonicalTuple = [.. page.After.CanonicalTuple], Checksum = [.. page.After.Checksum] } };
        BaseStudioEvidenceCapability? capability = await InvokeAsync(() => Task.FromResult(provider.EvidenceCapability),
            requirement.Limits.AcquisitionDeadline, cancellationToken).ConfigureAwait(false);
        if (capability is null) return Deadline<BaseStudioEvidencePage>();
        capability = capability with { SupportedKinds = [.. capability.SupportedKinds], BackupIncludedKinds = [.. capability.BackupIncludedKinds],
            RestoreValidatedKinds = [.. capability.RestoreValidatedKinds], CertificationChecksum = [.. capability.CertificationChecksum] };
        if (!BaseStudioEvidenceContract.Valid(capability)) return Corrupt<BaseStudioEvidencePage>();
        if (!capability.SupportedKinds.Contains(requirement.Kind) || requirement.Limits.MaximumItems > capability.MaximumItems ||
            requirement.Limits.MaximumRowsRead > capability.MaximumRowsRead || requirement.Limits.MaximumIntervals > capability.MaximumIntervals ||
            requirement.Limits.MaximumEvidenceBytes > capability.MaximumEvidenceBytes || requirement.Limits.MaximumTransientBytes > capability.MaximumTransientBytes ||
            requirement.Limits.AcquisitionDeadline > capability.AcquisitionDeadline || requirement.Limits.SessionDeadline > capability.SessionDeadline || requirement.Limits.PageDeadline > capability.PageDeadline)
            return OperationResults.Unsupported<BaseStudioEvidencePage>(new BaseError { Code = "base.studio.evidence.unsupported",
                Message = "The evidence kind or requested bounds are unsupported.", Category = ErrorCategory.Unsupported });
        OperationResult<BaseCapturedStudioEvidenceAuthority>? captured = await InvokeAsync(
            () => provider.CaptureAuthorityAsync(requirement, scope, cancellationToken).AsTask(), requirement.Limits.AcquisitionDeadline,
            cancellationToken).ConfigureAwait(false);
        if (captured is null) return Deadline<BaseStudioEvidencePage>();
        if (!captured.IsSuccess() || captured.Value is null) return SafeFailure<BaseStudioEvidencePage, BaseCapturedStudioEvidenceAuthority>(captured);
        BaseStudioEvidenceCaptureReceipt receipt = captured.Value.Receipt;
        if (!ValidReceipt(requirement, receipt)) return Corrupt<BaseStudioEvidencePage>();
        OperationResult<IBaseStudioEvidenceSession>? opened = await InvokeAsync(
            () => provider.OpenSessionAsync(captured.Value, cancellationToken).AsTask(), requirement.Limits.SessionDeadline,
            cancellationToken, static result => result.Value?.DisposeAsync().AsTask()).ConfigureAwait(false);
        if (opened is null) return Deadline<BaseStudioEvidencePage>();
        if (!opened.IsSuccess() || opened.Value is null) return SafeFailure<BaseStudioEvidencePage, IBaseStudioEvidenceSession>(opened);
        IBaseStudioEvidenceSession session = opened.Value;
        try
        {
            OperationResult<BaseStudioEvidencePage>? result = await InvokeAsync(() => session.ReadPageAsync(page, cancellationToken).AsTask(),
                requirement.Limits.PageDeadline, cancellationToken).ConfigureAwait(false);
            if (result is null) return Deadline<BaseStudioEvidencePage>();
            if (!result.IsSuccess() || result.Value is null) return SafeFailure<BaseStudioEvidencePage, BaseStudioEvidencePage>(result);
            return ValidatePage(requirement, receipt, page, after, result.Value)
                ? OperationResults.Ok(ClonePage(result.Value)) : Corrupt<BaseStudioEvidencePage>();
        }
        finally
        {
            _ = await InvokeAsync(async () => { await session.DisposeAsync().ConfigureAwait(false); return DisposeReceipt.Instance; },
                requirement.Limits.SessionDeadline, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static bool ValidReceipt(BaseStudioEvidenceRequirement request, BaseStudioEvidenceCaptureReceipt receipt) =>
        StringComparer.Ordinal.Equals(request.ApplicationId, receipt.ApplicationId) && request.Kind == receipt.Kind &&
        receipt.RestoreEpoch >= 0 && receipt.IndexGeneration >= 0 && !string.IsNullOrWhiteSpace(receipt.StoreIdentity) &&
        StringComparer.Ordinal.Equals(receipt.LogicalAccessPathId, request.Kind == BaseStudioEvidenceKind.RecordMutation
            ? BaseStudioEvidenceContract.RecordMutationPath : "base.studio.evidence." + request.Kind.ToString().ToLowerInvariant() + ".v1") &&
        receipt.ProtectedScopeSeekChecksum.Length == 32 && receipt.AuthorityChecksum.Length == 32 &&
        CryptographicOperations.FixedTimeEquals(receipt.ProtectedScopeSeekChecksum.AsSpan(), request.ProtectedScopeSeekChecksum.AsSpan()) &&
        CryptographicOperations.FixedTimeEquals(receipt.AuthorityChecksum.AsSpan(), BaseStudioEvidenceContract.AuthorityChecksum(
            request, receipt.StoreIdentity, receipt.RestoreEpoch, receipt.IndexGeneration, receipt.LogicalAccessPathId).AsSpan());

    private static bool ValidatePage(BaseStudioEvidenceRequirement request, BaseStudioEvidenceCaptureReceipt receipt,
        BaseStudioEvidencePageRequest pageRequest, long after, BaseStudioEvidencePage page)
    {
        if (page.IndexGeneration != receipt.IndexGeneration || page.Items.Length > pageRequest.Take || page.Intervals.Length is < 1 ||
            page.Intervals.Length > request.Limits.MaximumIntervals || page.Accounting.RowsRead < page.Items.Length ||
            page.Accounting.RowsRead > request.Limits.MaximumRowsRead || page.Accounting.Intervals != page.Intervals.Length ||
            page.Accounting.EvidenceBytes > request.Limits.MaximumEvidenceBytes || page.Accounting.TransientBytes > request.Limits.MaximumTransientBytes ||
            page.PageChecksum.Length != 32) return false;
        long prior = after; long measured = 0;
        foreach (BaseStudioEvidenceItem item in page.Items)
        {
            BaseStudioEvidenceBoundary itemBoundary = BaseStudioEvidenceContract.Boundary(item.Kind, item.OrderingTuple);
            if (item.Kind != request.Kind || !VariantMatchesKind(item) || !BaseStudioEvidenceContract.Position(itemBoundary, out long position) ||
                position <= prior || position > receipt.IndexGeneration || item.EvidenceChecksum.Length != 32 ||
                !CryptographicOperations.FixedTimeEquals(item.EvidenceChecksum.AsSpan(), BaseStudioEvidenceContract.ItemChecksum(item).AsSpan()) ||
                !Matches(request.Parent, item)) return false;
            prior = position; measured = checked(measured + BaseStudioEvidenceContract.Measure(item));
        }
        if (measured != page.Accounting.EvidenceBytes) return false;
        if (page.Next is { } next && (next.Kind != request.Kind || !BaseStudioEvidenceContract.Position(next, out long nextPosition) || page.Items.Length == 0 || nextPosition != prior)) return false;
        long previousUpper = -1;
        bool firstInterval = true;
        foreach (BaseStudioEvidenceReadInterval interval in page.Intervals)
        {
            if (!StringComparer.Ordinal.Equals(interval.LogicalAccessPathId, receipt.LogicalAccessPathId) || interval.ProtectedScopeSeekChecksum.Length != 32 || interval.Checksum.Length != 32 ||
                !CryptographicOperations.FixedTimeEquals(interval.ProtectedScopeSeekChecksum.AsSpan(), receipt.ProtectedScopeSeekChecksum.AsSpan()) ||
                !CryptographicOperations.FixedTimeEquals(interval.Checksum.AsSpan(), BaseStudioEvidenceContract.IntervalChecksum(interval.LogicalAccessPathId, interval.ProtectedScopeSeekChecksum, interval.LowerInclusive, interval.UpperExclusive).AsSpan()) ||
                !BaseStudioEvidenceContract.Position(BaseStudioEvidenceContract.Boundary(request.Kind, interval.LowerInclusive), out long lower) ||
                !BaseStudioEvidenceContract.Position(BaseStudioEvidenceContract.Boundary(request.Kind, interval.UpperExclusive), out long upper) ||
                lower >= upper || (firstInterval && lower > after) || (!firstInterval && lower < previousUpper)) return false;
            previousUpper = upper; firstInterval = false;
        }
        if (previousUpper <= prior) return false;
        return CryptographicOperations.FixedTimeEquals(page.PageChecksum.AsSpan(),
            BaseStudioEvidenceContract.PageChecksum(page.Items, page.IndexGeneration, page.Next, page.Intervals, page.Accounting).AsSpan());
    }

    private static bool Matches(BaseStudioEvidenceSubject parent, BaseStudioEvidenceItem item) => parent switch
    {
        BaseStudioCollectionEvidenceSubject collection when item is BaseStudioRecordMutationEvidenceItem mutation => StringComparer.Ordinal.Equals(collection.CollectionId, mutation.CollectionId),
        BaseStudioRecordEvidenceSubject record when item is BaseStudioRecordMutationEvidenceItem mutation => StringComparer.Ordinal.Equals(record.CollectionId, mutation.CollectionId) && record.RecordId == mutation.RecordId,
        _ => false,
    };

    private static OperationResult<TTarget> SafeFailure<TTarget, TSource>(OperationResult<TSource> source) => source.Status switch
    {
        OperationStatus.Unsupported => OperationResults.Unsupported<TTarget>(Safe("base.studio.evidence.unsupported", ErrorCategory.Unsupported)),
        OperationStatus.CapabilityUnavailable => OperationResults.CapabilityUnavailable<TTarget>(Safe("base.studio.evidence.unavailable", ErrorCategory.Capability)),
        OperationStatus.PolicyDenied or OperationStatus.Unauthorized => OperationResults.PolicyDenied<TTarget>(Safe("base.studio.authorityMismatch", ErrorCategory.Authorization)),
        OperationStatus.ValidationFailed => OperationResults.ValidationFailed<TTarget>(Safe("base.studio.evidence.budgetExceeded", ErrorCategory.Validation)),
        _ => OperationResults.StoreError<TTarget>(Safe("base.studio.unexpected", ErrorCategory.Unexpected)),
    };
    private static BaseError Safe(string code, ErrorCategory category) => new()
    { Code = code, Message = "The durable evidence operation could not be completed.", Category = category };
    private static OperationResult<T> Corrupt<T>() => OperationResults.StoreError<T>(new BaseError
    { Code = "base.studio.corruptEvidence", Message = "The Studio evidence could not be validated.", Category = ErrorCategory.Store });
    private static OperationResult<T> Deadline<T>() => OperationResults.StoreError<T>(new BaseError
    { Code = "base.studio.deadlineExceeded", Message = "The Studio evidence provider exceeded its deadline.", Category = ErrorCategory.Store });

    private static async ValueTask<T?> InvokeAsync<T>(Func<Task<T>> invoke, TimeSpan deadline, CancellationToken cancellationToken,
        Func<T, Task?>? lateCleanup = null) where T : class
    {
        if (!await RetainedProviderWork.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false)) return null;
        Task<T> task;
        try { task = Task.Run(invoke, CancellationToken.None); }
        catch { RetainedProviderWork.Release(); throw; }
        try
        {
            T value = await task.WaitAsync(deadline, cancellationToken).ConfigureAwait(false);
            RetainedProviderWork.Release(); return value;
        }
        catch (TimeoutException)
        {
            _ = task.ContinueWith(async completed =>
            {
                try { if (completed.Status == TaskStatus.RanToCompletion && lateCleanup?.Invoke(completed.Result) is { } cleanup) await cleanup.ConfigureAwait(false); }
                catch { }
                finally { RetainedProviderWork.Release(); }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default).Unwrap();
            return null;
        }
        catch when (!task.IsCompleted)
        {
            _ = task.ContinueWith(completed =>
            {
                try { _ = completed.Exception; }
                finally { RetainedProviderWork.Release(); }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
        catch { RetainedProviderWork.Release(); throw; }
    }
    private static bool VariantMatchesKind(BaseStudioEvidenceItem item) => item.Kind switch
    {
        BaseStudioEvidenceKind.Receipt => item is BaseStudioReceiptEvidenceItem,
        BaseStudioEvidenceKind.RecordMutation => item is BaseStudioRecordMutationEvidenceItem,
        BaseStudioEvidenceKind.ActivationOccurrence => item is BaseStudioActivationOccurrenceEvidenceItem,
        BaseStudioEvidenceKind.ActivationAttempt => item is BaseStudioActivationAttemptEvidenceItem,
        BaseStudioEvidenceKind.ActivationEffect => item is BaseStudioActivationEffectEvidenceItem,
        BaseStudioEvidenceKind.SearchRebuild => item is BaseStudioSearchRebuildEvidenceItem,
        BaseStudioEvidenceKind.Lifecycle => item is BaseStudioLifecycleEvidenceItem,
        BaseStudioEvidenceKind.Retirement => item is BaseStudioRetirementEvidenceItem,
        BaseStudioEvidenceKind.Schema => item is BaseStudioSchemaEvidenceItem,
        BaseStudioEvidenceKind.BackupRestore => item is BaseStudioBackupRestoreEvidenceItem,
        BaseStudioEvidenceKind.Maintenance => item is BaseStudioMaintenanceEvidenceItem,
        BaseStudioEvidenceKind.Quarantine => item is BaseStudioQuarantineEvidenceItem,
        BaseStudioEvidenceKind.HealthTransition => item is BaseStudioHealthTransitionEvidenceItem,
        _ => false,
    };

    private static BaseStudioEvidencePage ClonePage(BaseStudioEvidencePage page)
    {
        ImmutableArray<BaseStudioEvidenceItem> items = [.. page.Items.Select(CloneItem)];
        BaseStudioEvidenceBoundary? next = page.Next is null ? null : page.Next with
        { CanonicalTuple = [.. page.Next.CanonicalTuple], Checksum = [.. page.Next.Checksum] };
        return page with
        {
            Items = items, Next = next,
            Intervals = [.. page.Intervals.Select(static x => x with
            { LogicalAccessPathId = new string(x.LogicalAccessPathId.AsSpan()), ProtectedScopeSeekChecksum = [.. x.ProtectedScopeSeekChecksum], LowerInclusive = [.. x.LowerInclusive], UpperExclusive = [.. x.UpperExclusive], Checksum = [.. x.Checksum] })],
            Accounting = page.Accounting with { }, PageChecksum = [.. page.PageChecksum],
        };
    }

    private static BaseStudioEvidenceItem CloneItem(BaseStudioEvidenceItem item) => item switch
    {
        BaseStudioRecordMutationEvidenceItem x => x with { OrderingTuple = [.. x.OrderingTuple], EvidenceChecksum = [.. x.EvidenceChecksum], CollectionId = new string(x.CollectionId.AsSpan()), RecordId = RecordId.Create(new string(x.RecordId.Value.AsSpan())), Revision = x.Revision is null ? null : new RevisionToken(new string(x.Revision.Value.Value.AsSpan())), EvidenceId = new string(x.EvidenceId.AsSpan()), ReceiptIdentity = x.ReceiptIdentity is null ? null : new string(x.ReceiptIdentity.AsSpan()) },
        BaseStudioReceiptEvidenceItem x => x with { OrderingTuple = [.. x.OrderingTuple], EvidenceChecksum = [.. x.EvidenceChecksum], AffectedResourceIdentities = [.. x.AffectedResourceIdentities.Select(static value => new string(value.AsSpan()))] },
        BaseStudioLifecycleEvidenceItem x => x with { OrderingTuple = [.. x.OrderingTuple], EvidenceChecksum = [.. x.EvidenceChecksum], ProtectedScopeOrder = [.. x.ProtectedScopeOrder] },
        BaseStudioRetirementEvidenceItem x => x with { OrderingTuple = [.. x.OrderingTuple], EvidenceChecksum = [.. x.EvidenceChecksum], ProtectedScopeOrder = [.. x.ProtectedScopeOrder] },
        BaseStudioSchemaEvidenceItem x => x with { OrderingTuple = [.. x.OrderingTuple], EvidenceChecksum = [.. x.EvidenceChecksum], AuthorityChecksum = [.. x.AuthorityChecksum] },
        BaseStudioBackupRestoreEvidenceItem x => x with { OrderingTuple = [.. x.OrderingTuple], EvidenceChecksum = [.. x.EvidenceChecksum], ArtifactAuthorityChecksum = [.. x.ArtifactAuthorityChecksum] },
        BaseStudioActivationOccurrenceEvidenceItem x => x with { OrderingTuple = [.. x.OrderingTuple], EvidenceChecksum = [.. x.EvidenceChecksum] },
        BaseStudioActivationAttemptEvidenceItem x => x with { OrderingTuple = [.. x.OrderingTuple], EvidenceChecksum = [.. x.EvidenceChecksum] },
        BaseStudioActivationEffectEvidenceItem x => x with { OrderingTuple = [.. x.OrderingTuple], EvidenceChecksum = [.. x.EvidenceChecksum] },
        BaseStudioSearchRebuildEvidenceItem x => x with { OrderingTuple = [.. x.OrderingTuple], EvidenceChecksum = [.. x.EvidenceChecksum] },
        BaseStudioMaintenanceEvidenceItem x => x with { OrderingTuple = [.. x.OrderingTuple], EvidenceChecksum = [.. x.EvidenceChecksum] },
        BaseStudioQuarantineEvidenceItem x => x with { OrderingTuple = [.. x.OrderingTuple], EvidenceChecksum = [.. x.EvidenceChecksum] },
        BaseStudioHealthTransitionEvidenceItem x => x with { OrderingTuple = [.. x.OrderingTuple], EvidenceChecksum = [.. x.EvidenceChecksum] },
        _ => throw new InvalidOperationException("The evidence item kind is invalid."),
    };
    private sealed class DisposeReceipt { internal static DisposeReceipt Instance { get; } = new(); }
}
