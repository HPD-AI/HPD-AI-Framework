namespace HPD.Base.StoreConformance;

public static class RecordStoreConformanceAssertions
{
    private static readonly OperationStatus[] ExpectedFailureStatuses =
    [
        OperationStatus.NotFound,
        OperationStatus.Conflict,
        OperationStatus.ValidationFailed,
        OperationStatus.Unsupported,
        OperationStatus.CapabilityUnavailable,
        OperationStatus.StoreError
    ];

    public static bool IsSuccess(OperationStatus status) =>
        status is OperationStatus.Ok
            or OperationStatus.Created
            or OperationStatus.Updated
            or OperationStatus.Deleted
            or OperationStatus.NoContent;

    public static void Success<T>(OperationResult<T> result, OperationStatus status)
    {
        Assert.Equal(status, result.Status);
        Assert.NotNull(result.Value);
        Assert.Null(result.Error);
    }

    public static void Failure<T>(OperationResult<T> result, params OperationStatus[] allowed)
    {
        Assert.Contains(result.Status, allowed.Length == 0 ? ExpectedFailureStatuses : allowed);
        Assert.NotNull(result.Error);
        Assert.False(string.IsNullOrWhiteSpace(result.Error!.Code));
        Assert.False(string.IsNullOrWhiteSpace(result.Error.Message));
        AssertErrorCategory(result.Status, result.Error.Category);
    }

    public static void Failure(OperationResult result, params OperationStatus[] allowed)
    {
        Assert.Contains(result.Status, allowed.Length == 0 ? ExpectedFailureStatuses : allowed);
        Assert.NotNull(result.Error);
        Assert.False(string.IsNullOrWhiteSpace(result.Error!.Code));
        Assert.False(string.IsNullOrWhiteSpace(result.Error.Message));
        AssertErrorCategory(result.Status, result.Error.Category);
    }

    public static void HasField(RecordEnvelope envelope, string field, string value)
    {
        Assert.NotNull(envelope.Payload.Fields);
        Assert.True(envelope.Payload.Fields!.TryGetValue(field, out var element), $"Expected payload field '{field}'.");
        Assert.Equal(value, element.GetString());
    }

    public static void HasNullField(RecordEnvelope envelope, string field)
    {
        Assert.NotNull(envelope.Payload.Fields);
        Assert.True(envelope.Payload.Fields!.TryGetValue(field, out var element), $"Expected payload field '{field}'.");
        Assert.Equal(JsonValueKind.Null, element.ValueKind);
    }

    public static void EnvelopeShape(RecordEnvelope envelope, CollectionDefinition collection)
    {
        Assert.Equal(collection.Id, envelope.CollectionId);
        Assert.False(string.IsNullOrWhiteSpace(envelope.Id.Value));
        Assert.NotNull(envelope.Payload);
        Assert.NotNull(envelope.Metadata);
    }

    public static void PageShape(RecordPage page)
    {
        Assert.NotNull(page.Items);
        Assert.NotNull(page.Page);
        Assert.True(page.Page.PerPage is null or >= 0);
        Assert.True(page.Page.Limit is null or >= 0);
    }

    private static void AssertErrorCategory(OperationStatus status, ErrorCategory category)
    {
        var expected = status switch
        {
            OperationStatus.NotFound => ErrorCategory.NotFound,
            OperationStatus.Conflict => ErrorCategory.Conflict,
            OperationStatus.ValidationFailed => ErrorCategory.Validation,
            OperationStatus.Unsupported => ErrorCategory.Unsupported,
            OperationStatus.CapabilityUnavailable => ErrorCategory.Capability,
            OperationStatus.StoreError => ErrorCategory.Store,
            OperationStatus.PolicyDenied => ErrorCategory.Authorization,
            OperationStatus.Unauthorized => ErrorCategory.Authentication,
            _ => ErrorCategory.None
        };

        Assert.Equal(expected, category);
    }
}
