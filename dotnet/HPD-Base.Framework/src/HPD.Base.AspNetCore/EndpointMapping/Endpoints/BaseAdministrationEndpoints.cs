using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace HPD.Base.AspNetCore;

internal static class BaseAdministrationEndpoints
{
    internal static void Map(RouteGroupBuilder group, IServiceProvider services, Action<IEndpointConventionBuilder, HPDBaseEndpointDescriptor> convention)
    {
        HPDBaseAdministrationHttpSnapshot options = services.GetRequiredService<HPDBaseAspNetCoreSnapshot>().Administration;
        if (options.StagingRoot is null)
            throw new InvalidOperationException("base.admin.stagingRequired");
        group.MapPost("/administration/purge", (RequestDelegate)PurgeAsync)
            .WithHPDBaseEndpoint("base.admin.purge", HPDBaseEndpointAudience.ControlPlane, HPDBaseEndpointOperation.AdministrativePurge, HPDBaseCapabilities.AdministrationRecordsPurge, convention)
            .WithName("base.admin.purge");
        group.MapPost("/administration/backups:create", (RequestDelegate)CreateAsync)
            .WithHPDBaseEndpoint("base.admin.backup.create", HPDBaseEndpointAudience.ControlPlane, HPDBaseEndpointOperation.BackupCreate, HPDBaseCapabilities.AdministrationBackupCreate, convention)
            .WithName("base.admin.backup.create");
        group.MapPost("/administration/backups:validate", (RequestDelegate)ValidateAsync)
            .WithHPDBaseEndpoint("base.admin.backup.validate", HPDBaseEndpointAudience.ControlPlane, HPDBaseEndpointOperation.BackupValidate, HPDBaseCapabilities.AdministrationBackupValidate, convention)
            .WithName("base.admin.backup.validate");
        group.MapPost("/administration/backups:restore", (RequestDelegate)RestoreAsync)
            .WithHPDBaseEndpoint("base.admin.backup.restore", HPDBaseEndpointAudience.ControlPlane, HPDBaseEndpointOperation.BackupRestore, HPDBaseCapabilities.AdministrationBackupRestore, convention)
            .WithName("base.admin.backup.restore");
        group.MapPost("/administration/subjects:rotate-epoch", (RequestDelegate)RotateSubjectEpochAsync)
            .WithHPDBaseEndpoint("base.admin.subject.epoch.rotate", HPDBaseEndpointAudience.ControlPlane, HPDBaseEndpointOperation.SubjectEpochRotate, HPDBaseCapabilities.AdministrationSubjectEpochRotate, convention)
            .WithName("base.admin.subject.epoch.rotate");
    }

    private static async Task PurgeAsync(HttpContext context)
    {
        BasePurgeHttpRequest? request;
        try { request = await ReadJsonBoundedAsync(context, BaseAdministrationHttpJsonContext.Default.BasePurgeHttpRequest).ConfigureAwait(false); }
        catch (InvalidDataException) { await ProblemAsync(context, 400, "base.admin.requestInvalid").ConfigureAwait(false); return; }
        if (request is null) { await ProblemAsync(context, 400, "base.admin.requestInvalid").ConfigureAwait(false); return; }
        PrincipalContext principal = await PrincipalAsync(context).ConfigureAwait(false);
        BaseResult<BasePurgeResult> result = await context.RequestServices.GetRequiredService<IHPDBaseAdministration>().PurgeAsync(new BasePurgeRequest
        {
            CollectionId = request.CollectionId,
            RecordIds = request.RecordIds.Select(static id => new RecordId(id)).ToArray(),
            Principal = principal,
            ReasonCode = request.ReasonCode,
            AuditReference = request.AuditReference,
            EvaluatedAt = request.EvaluatedAt,
            ExpectedPurgeGeneration = request.ExpectedPurgeGeneration
        }, context.RequestAborted).ConfigureAwait(false);
        await WriteResultAsync(context, result).ConfigureAwait(false);
    }

    private static async Task CreateAsync(HttpContext context)
    {
        BaseBackupCreateHttpRequest? request;
        try { request = await ReadJsonBoundedAsync(context, BaseAdministrationHttpJsonContext.Default.BaseBackupCreateHttpRequest).ConfigureAwait(false); }
        catch (InvalidDataException) { await ProblemAsync(context, 400, "base.admin.requestInvalid").ConfigureAwait(false); return; }
        if (request is null) { await ProblemAsync(context, 400, "base.admin.requestInvalid").ConfigureAwait(false); return; }
        BaseAdministrationStagingCoordinator coordinator = context.RequestServices.GetRequiredService<BaseAdministrationStagingCoordinator>();
        await using BaseAdministrationStagingLease? lease = await coordinator.AcquireAsync(context.RequestAborted).ConfigureAwait(false);
        if (lease is null) { await ProblemAsync(context, 503, "base.admin.backup.busy").ConfigureAwait(false); return; }
        PrincipalContext principal = await PrincipalAsync(context).ConfigureAwait(false);
        BaseResult<BaseBackupManifest> result;
        await using (FileStream output = lease.CreateWriteStream())
            result = await context.RequestServices.GetRequiredService<IHPDBaseAdministration>().CreateBackupAsync(output, new BaseBackupRequest { StoreId = request.StoreId, Principal = principal, ExpectedStoreIdentityDigest = request.ExpectedStoreIdentityDigest }, context.RequestAborted).ConfigureAwait(false);
        if (!result.TryGetValue(out BaseBackupManifest? manifest) || manifest is null) { await WriteFailureAsync(context, result).ConfigureAwait(false); return; }
        long artifactLength = lease.Length;
        if (artifactLength <= 0 || artifactLength > coordinator.MaximumArtifactBytes) { await ProblemAsync(context, 500, "base.admin.backup.artifactInvalid").ConfigureAwait(false); return; }
        await using (FileStream validationInput = lease.OpenReadStream())
        {
            BaseResult<BaseBackupManifest> validated = await context.RequestServices.GetRequiredService<IHPDBaseAdministration>().ValidateBackupAsync(validationInput, new BaseBackupValidationRequest { StoreId = request.StoreId, Principal = principal, ExpectedArtifactStoreIdentityDigest = manifest.StoreIdentityDigest }, context.RequestAborted).ConfigureAwait(false);
            if (!validated.TryGetValue(out BaseBackupManifest? validatedManifest) || validatedManifest is null || !ManifestEquals(manifest, validatedManifest)) { await ProblemAsync(context, 500, "base.admin.backup.artifactInvalid").ConfigureAwait(false); return; }
        }
        byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, BaseAdministrationHttpJsonContext.Default.BaseBackupManifest);
        if (manifestBytes.Length > 64 * 1024) { await ProblemAsync(context, 500, "base.admin.backup.artifactInvalid").ConfigureAwait(false); return; }
        string boundary = "hpd-base-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        byte[] prefix = Encoding.ASCII.GetBytes($"--{boundary}\r\nContent-Type: application/json\r\nContent-Length: {manifestBytes.Length}\r\n\r\n");
        byte[] middle = Encoding.ASCII.GetBytes($"\r\n--{boundary}\r\nContent-Type: application/octet-stream\r\nContent-Length: {artifactLength}\r\n\r\n");
        byte[] suffix = Encoding.ASCII.GetBytes($"\r\n--{boundary}--\r\n");
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = $"multipart/mixed; boundary={boundary}";
        context.Response.ContentLength = prefix.Length + manifestBytes.Length + middle.Length + artifactLength + suffix.Length;
        await context.Response.Body.WriteAsync(prefix, context.RequestAborted).ConfigureAwait(false);
        await context.Response.Body.WriteAsync(manifestBytes, context.RequestAborted).ConfigureAwait(false);
        await context.Response.Body.WriteAsync(middle, context.RequestAborted).ConfigureAwait(false);
        await using (FileStream input = lease.OpenReadStream()) await input.CopyToAsync(context.Response.Body, 64 * 1024, context.RequestAborted).ConfigureAwait(false);
        await context.Response.Body.WriteAsync(suffix, context.RequestAborted).ConfigureAwait(false);
        try { await lease.CleanupAsync().ConfigureAwait(false); } catch { /* delivery is complete; the retained lease remains quarantined */ }
    }

    private static async Task ValidateAsync(HttpContext context)
    {
        (BaseBackupValidationHttpRequest Request, BaseAdministrationStagingLease Lease)? parsed = await ParseMultipartAsync<BaseBackupValidationHttpRequest>(context).ConfigureAwait(false);
        if (parsed is null) { await ProblemAsync(context, 400, "base.admin.backup.multipartInvalid").ConfigureAwait(false); return; }
        await using BaseAdministrationStagingLease lease = parsed.Value.Lease;
        PrincipalContext principal = await PrincipalAsync(context).ConfigureAwait(false);
        await using FileStream input = lease.OpenReadStream();
        BaseResult<BaseBackupManifest> result = await context.RequestServices.GetRequiredService<IHPDBaseAdministration>().ValidateBackupAsync(input, new BaseBackupValidationRequest { StoreId = parsed.Value.Request.StoreId, Principal = principal, ExpectedArtifactStoreIdentityDigest = parsed.Value.Request.ExpectedArtifactStoreIdentityDigest }, context.RequestAborted).ConfigureAwait(false);
        await lease.CleanupAsync().ConfigureAwait(false);
        await WriteResultAsync(context, result).ConfigureAwait(false);
    }

    private static async Task RestoreAsync(HttpContext context)
    {
        (BaseRestoreHttpRequest Request, BaseAdministrationStagingLease Lease)? parsed = await ParseMultipartAsync<BaseRestoreHttpRequest>(context).ConfigureAwait(false);
        if (parsed is null) { await ProblemAsync(context, 400, "base.admin.backup.multipartInvalid").ConfigureAwait(false); return; }
        await using BaseAdministrationStagingLease lease = parsed.Value.Lease;
        PrincipalContext principal = await PrincipalAsync(context).ConfigureAwait(false);
        await using FileStream input = lease.OpenReadStream();
        BaseRestoreHttpRequest request = parsed.Value.Request;
        BaseResult<BaseRestoreResult> result = await context.RequestServices.GetRequiredService<IHPDBaseAdministration>().RestoreAsync(input, new BaseRestoreRequest
        {
            StoreId = request.StoreId, Principal = principal, ExpectedCurrentStoreIdentityDigest = request.ExpectedCurrentStoreIdentityDigest,
            ExpectedArtifactStoreIdentityDigest = request.ExpectedArtifactStoreIdentityDigest, IdentityMode = request.IdentityMode,
            RecoveryImageRetention = request.RecoveryImageRetention, ConfirmDestructiveReplacement = request.ConfirmDestructiveReplacement
        }, context.RequestAborted).ConfigureAwait(false);
        await lease.CleanupAsync().ConfigureAwait(false);
        await WriteResultAsync(context, result).ConfigureAwait(false);
    }

    private static async Task RotateSubjectEpochAsync(HttpContext context)
    {
        BaseSubjectEpochRotationHttpRequest? request;
        try { request = await ReadJsonBoundedAsync(context, BaseAdministrationHttpJsonContext.Default.BaseSubjectEpochRotationHttpRequest).ConfigureAwait(false); }
        catch (InvalidDataException) { await ProblemAsync(context, 400, "base.admin.requestInvalid").ConfigureAwait(false); return; }
        if (request is null) { await ProblemAsync(context, 400, "base.admin.requestInvalid").ConfigureAwait(false); return; }
        if (!long.TryParse(request.ExpectedStateGeneration, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long expectedStateGeneration)
            || expectedStateGeneration <= 0 || !string.Equals(request.ExpectedStateGeneration, expectedStateGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))
        { await ProblemAsync(context, 400, "base.admin.requestInvalid").ConfigureAwait(false); return; }
        PrincipalContext principal = await PrincipalAsync(context).ConfigureAwait(false);
        BaseResult<BaseSubjectEpochRotationResult> result = await context.RequestServices.GetRequiredService<IHPDBaseAdministration>()
            .RotateSubjectEpochAsync(request.StoreId, principal, new BaseSubjectEpochRotationRequest
            {
                ContractId = request.ContractId,
                ContractVersion = request.ContractVersion,
                ExpectedStateGeneration = expectedStateGeneration,
                DestructiveIntent = request.DestructiveIntent,
            }, context.RequestAborted).ConfigureAwait(false);
        await WriteResultAsync(context, result).ConfigureAwait(false);
    }

    private static async Task<(T Request, BaseAdministrationStagingLease Lease)?> ParseMultipartAsync<T>(HttpContext context)
    {
        if (!MediaTypeHeaderValue.TryParse(context.Request.ContentType, out MediaTypeHeaderValue? contentType) || !contentType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)) return null;
        string rawBoundary = contentType.Boundary.Value ?? "";
        if (rawBoundary.Length >= 2 && rawBoundary[0] == '"' && rawBoundary[^1] == '"') return null;
        string boundary = rawBoundary;
        if (boundary.Length is < 1 or > 70 || boundary.Any(static value => value < 33 || value > 126)) return null;
        BaseAdministrationStagingCoordinator coordinator = context.RequestServices.GetRequiredService<BaseAdministrationStagingCoordinator>();
        long maximumMultipartBytes = checked(coordinator.MaximumArtifactBytes + 96 * 1024);
        if (context.Request.ContentLength is long declaredTotal && (declaredTotal <= 0 || declaredTotal > maximumMultipartBytes)) return null;
        BaseAdministrationStagingLease? lease = await coordinator.AcquireAsync(context.RequestAborted).ConfigureAwait(false);
        if (lease is null) return null;
        try
        {
            var reader = new Microsoft.AspNetCore.WebUtilities.MultipartReader(boundary, context.Request.Body) { BodyLengthLimit = maximumMultipartBytes, HeadersLengthLimit = 16 * 1024, HeadersCountLimit = 8 };
            Microsoft.AspNetCore.WebUtilities.MultipartSection? requestSection = await reader.ReadNextSectionAsync(context.RequestAborted).ConfigureAwait(false);
            if (requestSection is null || PartName(requestSection) != "request") throw new InvalidDataException();
            System.Text.Json.Serialization.Metadata.JsonTypeInfo? typeInfo = BaseAdministrationHttpJsonContext.Default.GetTypeInfo(typeof(T));
            if (typeInfo is null) throw new InvalidDataException();
            T? request = await ReadSectionJsonAsync(requestSection.Body, typeInfo, context.RequestAborted).ConfigureAwait(false) is T typed ? typed : default;
            if (request is null) throw new InvalidDataException();
            Microsoft.AspNetCore.WebUtilities.MultipartSection? artifact = await reader.ReadNextSectionAsync(context.RequestAborted).ConfigureAwait(false);
            if (artifact is null || PartName(artifact) != "artifact") throw new InvalidDataException();
            if (artifact.Headers is not null && artifact.Headers.TryGetValue("Content-Length", out Microsoft.Extensions.Primitives.StringValues declaredValues))
            {
                if (declaredValues.Count != 1 || !long.TryParse(declaredValues[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long declaredArtifactLength)
                    || declaredArtifactLength <= 0 || declaredArtifactLength > coordinator.MaximumArtifactBytes) throw new InvalidDataException();
            }
            await using (FileStream output = lease.CreateWriteStream()) await artifact.Body.CopyToAsync(output, 64 * 1024, context.RequestAborted).ConfigureAwait(false);
            if (lease.Length <= 0 || lease.Length > coordinator.MaximumArtifactBytes) throw new InvalidDataException();
            if (await reader.ReadNextSectionAsync(context.RequestAborted).ConfigureAwait(false) is not null) throw new InvalidDataException();
            return (request, lease);
        }
        catch { await lease.DisposeAsync().ConfigureAwait(false); return null; }
    }

    private static string? PartName(Microsoft.AspNetCore.WebUtilities.MultipartSection section) => ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out ContentDispositionHeaderValue? value) ? HeaderUtilities.RemoveQuotes(value.Name).Value : null;
    private static async ValueTask<T?> ReadJsonBoundedAsync<T>(HttpContext context, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        const int limit = 64 * 1024;
        if (context.Request.ContentLength is > limit) throw new InvalidDataException();
        await using var bounded = new MemoryStream(limit);
        byte[] buffer = new byte[8 * 1024]; int total = 0;
        while (true) { int read = await context.Request.Body.ReadAsync(buffer, context.RequestAborted).ConfigureAwait(false); if (read == 0) break; total += read; if (total > limit) throw new InvalidDataException(); await bounded.WriteAsync(buffer.AsMemory(0, read), context.RequestAborted).ConfigureAwait(false); }
        bounded.Position = 0;
        try { return await JsonSerializer.DeserializeAsync(bounded, typeInfo, context.RequestAborted).ConfigureAwait(false); }
        catch (JsonException exception) { throw new InvalidDataException("base.admin.requestInvalid", exception); }
    }
    private static async ValueTask<object?> ReadSectionJsonAsync(Stream source, System.Text.Json.Serialization.Metadata.JsonTypeInfo typeInfo, CancellationToken cancellationToken)
    {
        const int limit = 64 * 1024;
        await using var bounded = new MemoryStream(limit); byte[] buffer = new byte[8 * 1024]; int total = 0;
        while (true) { int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false); if (read == 0) break; total += read; if (total > limit) throw new InvalidDataException(); await bounded.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false); }
        bounded.Position = 0; return await JsonSerializer.DeserializeAsync(bounded, typeInfo, cancellationToken).ConfigureAwait(false);
    }
    private static bool ManifestEquals(BaseBackupManifest left, BaseBackupManifest right) =>
        JsonSerializer.SerializeToUtf8Bytes(left, BaseAdministrationHttpJsonContext.Default.BaseBackupManifest)
            .AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(right, BaseAdministrationHttpJsonContext.Default.BaseBackupManifest));
    private static ValueTask<PrincipalContext> PrincipalAsync(HttpContext context) => context.RequestServices.GetRequiredService<IBaseHttpPrincipalContextFactory>().CreateAsync(context, context.RequestAborted);
    private static async Task WriteResultAsync<T>(HttpContext context, BaseResult<T> result)
    {
        if (!result.TryGetValue(out T? value) || value is null) { await WriteFailureAsync(context, result).ConfigureAwait(false); return; }
        IResult response = value switch
        {
            BasePurgeResult purge => Results.Json(purge, BaseAdministrationHttpJsonContext.Default.BasePurgeResult),
            BaseBackupManifest manifest => Results.Json(manifest, BaseAdministrationHttpJsonContext.Default.BaseBackupManifest),
            BaseRestoreResult restore => Results.Json(restore, BaseAdministrationHttpJsonContext.Default.BaseRestoreResult),
            BaseSubjectEpochRotationResult rotation => Results.Json(new BaseSubjectEpochRotationHttpResult
            {
                ContractId = rotation.ContractId,
                ContractVersion = rotation.ContractVersion,
                PreviousStateGeneration = rotation.PreviousStateGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture),
                PublishedStateGeneration = rotation.PublishedStateGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture),
                PublicationPosition = rotation.PublicationPosition.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ExaminedRecords = rotation.ExaminedRecords.ToString(System.Globalization.CultureInfo.InvariantCulture),
                RewrittenReferences = rotation.RewrittenReferences.ToString(System.Globalization.CultureInfo.InvariantCulture),
            }, BaseAdministrationHttpJsonContext.Default.BaseSubjectEpochRotationHttpResult),
            _ => Problem(500, "base.admin.responseInvalid")
        };
        await response.ExecuteAsync(context).ConfigureAwait(false);
    }
    private static int Status(OperationStatus status) => status switch { OperationStatus.ValidationFailed => 400, OperationStatus.PolicyDenied => 403, OperationStatus.NotFound => 404, OperationStatus.Conflict => 409, OperationStatus.Unsupported or OperationStatus.CapabilityUnavailable => 424, _ => 500 };
    private static IResult Problem(int status, string code) => Results.Problem(statusCode: status, title: "The BASE administration operation failed.", extensions: new Dictionary<string, object?> { ["code"] = code });
    private static Task ProblemAsync(HttpContext context, int status, string code) => Problem(status, code).ExecuteAsync(context);
    private static Task WriteFailureAsync<T>(HttpContext context, BaseResult<T> result) => Problem(Status(result.Status), (result as BaseFailure<T>)?.Error.Code ?? "base.admin.failed").ExecuteAsync(context);
}

internal sealed record BaseBackupCreateHttpRequest { public required string StoreId { get; init; } public string? ExpectedStoreIdentityDigest { get; init; } }
internal sealed record BaseBackupValidationHttpRequest { public required string StoreId { get; init; } public string? ExpectedArtifactStoreIdentityDigest { get; init; } }
internal sealed record BaseRestoreHttpRequest { public required string StoreId { get; init; } public required string ExpectedCurrentStoreIdentityDigest { get; init; } public required string ExpectedArtifactStoreIdentityDigest { get; init; } public required BaseRestoreIdentityMode IdentityMode { get; init; } public required BaseRecoveryImageRetention RecoveryImageRetention { get; init; } public required bool ConfirmDestructiveReplacement { get; init; } }
internal sealed record BasePurgeHttpRequest { public required string CollectionId { get; init; } public required string[] RecordIds { get; init; } public required string ReasonCode { get; init; } public required string AuditReference { get; init; } public required DateTimeOffset EvaluatedAt { get; init; } public long? ExpectedPurgeGeneration { get; init; } }
internal sealed record BaseSubjectEpochRotationHttpRequest { public required string StoreId { get; init; } public required string ContractId { get; init; } public required int ContractVersion { get; init; } public required string ExpectedStateGeneration { get; init; } public required string DestructiveIntent { get; init; } }
internal sealed record BaseSubjectEpochRotationHttpResult { public required string ContractId { get; init; } public required int ContractVersion { get; init; } public required string PreviousStateGeneration { get; init; } public required string PublishedStateGeneration { get; init; } public required string PublicationPosition { get; init; } public required string ExaminedRecords { get; init; } public required string RewrittenReferences { get; init; } }

[System.Text.Json.Serialization.JsonSourceGenerationOptions(PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseBackupCreateHttpRequest))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseBackupValidationHttpRequest))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseRestoreHttpRequest))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BasePurgeHttpRequest))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseSubjectEpochRotationHttpRequest))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseBackupManifest))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BasePurgeResult))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseRestoreResult))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseSubjectEpochRotationHttpResult))]
internal partial class BaseAdministrationHttpJsonContext : System.Text.Json.Serialization.JsonSerializerContext;

internal sealed class BaseAdministrationStagingCoordinator : IBaseHealthContributor, IBaseDiagnosticContributor
{
    private readonly HPDBaseAdministrationHttpSnapshot _options;
    private readonly SemaphoreSlim _slots;
    private readonly TimeProvider _timeProvider;
    private int _quarantined;
    public BaseAdministrationStagingCoordinator(HPDBaseAspNetCoreSnapshot options, TimeProvider timeProvider) { _options = options.Administration; _slots = new(_options.MaxConcurrentStaging, _options.MaxConcurrentStaging); _timeProvider = timeProvider; }
    public string Id => "hpd.base.aspnet.administrationStaging";
    internal long MaximumArtifactBytes => _options.MaxArtifactBytes;
    internal async ValueTask<BaseAdministrationStagingLease?> AcquireAsync(CancellationToken cancellationToken)
    {
        if (_options.StagingRoot is null || !await _slots.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false)) return null;
        try
        {
            bool existed = Directory.Exists(_options.StagingRoot);
            Directory.CreateDirectory(_options.StagingRoot);
            ValidateRoot(_options.StagingRoot);
            if (!OperatingSystem.IsWindows())
            {
                if (!existed) File.SetUnixFileMode(_options.StagingRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                UnixFileMode mode = File.GetUnixFileMode(_options.StagingRoot);
                if ((mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
                    throw new InvalidOperationException("base.admin.stagingUnsafe");
            }
            return new BaseAdministrationStagingLease(Path.Combine(_options.StagingRoot, $"hpd-base-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24))}.stage"), _slots, _options.CleanupTimeout, delta => Interlocked.Add(ref _quarantined, delta));
        }
        catch { _slots.Release(); throw; }
    }
    private static void ValidateRoot(string root) { for (DirectoryInfo? item = new(root); item is not null; item = item.Parent) if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("base.admin.stagingUnsafe"); }
    public ValueTask<HealthDescriptor[]> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); int quarantined = Volatile.Read(ref _quarantined);
        HealthStatus status = _options.StagingRoot is null ? HealthStatus.Disabled : quarantined == 0 ? HealthStatus.Healthy : HealthStatus.Degraded;
        return ValueTask.FromResult<HealthDescriptor[]>([new HealthDescriptor { Id = "hpd.base.aspnet.administrationStaging", Scope = HealthScope.Module, TargetRef = Id, Status = status, CheckedAt = _timeProvider.GetUtcNow(), Summary = status == HealthStatus.Disabled ? "Administration staging is not configured." : quarantined == 0 ? "Administration staging is available." : "Administration staging has quarantined cleanup work.", PublicSafe = false, Visibility = VisibilityLevel.Admin, Metrics = [new HealthMetric { Name = "quarantined", Kind = HealthMetricValueKind.Number, NumberValue = quarantined }, new HealthMetric { Name = "availableSlots", Kind = HealthMetricValueKind.Number, NumberValue = _slots.CurrentCount }] }]);
    }
    public ValueTask<DiagnosticDescriptor[]> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult<DiagnosticDescriptor[]>([new DiagnosticDescriptor { Id = "hpd.base.aspnet.administrationStaging", Code = "base.admin.staging.status", TargetRef = Id, Severity = Volatile.Read(ref _quarantined) == 0 ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning, Message = $"Administration staging quarantine count: {Volatile.Read(ref _quarantined)}.", EmittedAt = _timeProvider.GetUtcNow(), Category = DiagnosticCategory.Projection, Visibility = VisibilityLevel.Admin }]);
    }
}

internal sealed class BaseAdministrationStagingLease(string path, SemaphoreSlim slots, TimeSpan cleanupTimeout, Action<int> quarantine) : IAsyncDisposable
{
    private bool _released;
    private bool _quarantined;
    private Task? _cleanupTask;
    internal long Length => new FileInfo(path).Length;
    internal FileStream CreateWriteStream()
    {
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return stream;
    }
    internal FileStream OpenReadStream() => new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
    internal async ValueTask CleanupAsync()
    {
        if (_released) return;
        if (_cleanupTask is not null) throw new IOException("base.admin.backup.cleanupFailed");
        _cleanupTask = Task.Run(() => { File.Delete(path); if (File.Exists(path)) throw new IOException("base.admin.backup.cleanupFailed"); });
        try { await _cleanupTask.WaitAsync(cleanupTimeout).ConfigureAwait(false); Release(); }
        catch
        {
            if (!_quarantined) { _quarantined = true; quarantine(1); }
            if (!_cleanupTask.IsCompleted) _ = ObserveLateCleanupAsync(_cleanupTask);
            throw;
        }
    }
    private async Task ObserveLateCleanupAsync(Task cleanupTask)
    {
        try { await cleanupTask.ConfigureAwait(false); if (!File.Exists(path)) Release(); }
        catch { /* failed cleanup intentionally retains its root and capacity */ }
    }
    private void Release()
    {
        if (_released) return;
        _released = true;
        if (_quarantined) { _quarantined = false; quarantine(-1); }
        slots.Release();
    }
    public async ValueTask DisposeAsync() { if (!_released) { try { await CleanupAsync().ConfigureAwait(false); } catch { /* retain the capacity slot and artifact for quarantine */ } } }
}
