using System.Collections.Immutable;
using System.Xml.Linq;
using System.Security.Cryptography;
using HPD.Auth.Base;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Audit;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Core.Models;
using HPD.Auth.Extensions;
using HPD.Auth.Testing;
using HPD.Auth.NativeAotSmoke;
using HPD.Base;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

const string applicationName = "HPD Auth Native AOT Smoke";
var services = new ServiceCollection();
services.AddLogging();
services.AddHttpContextAccessor();
var verificationTime = new VerificationTimeProvider(DateTimeOffset.UtcNow);
services.AddSingleton<TimeProvider>(verificationTime);
services.AddHPDAuth(options =>
{
    options.AppName = applicationName;
    options.Password.RequiredLength = 8;
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
}).UseBaseTestHost(applicationName, NativeAotAuthConsumer.Install);

await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
{
    ValidateOnBuild = true,
    ValidateScopes = true,
});
await provider.InitializeHPDAuthBaseTestHostAsync(applicationName);

IHostedService[] hosted = provider.GetServices<IHostedService>().ToArray();
foreach (IHostedService service in hosted)
    await service.StartAsync(CancellationToken.None);

try
{
    using IServiceScope scope = provider.CreateScope();
    RoleManager<ApplicationRole> roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
    UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var role = new ApplicationRole("User") { Id = Guid.NewGuid() };
    Require(await roles.CreateAsync(role), "role creation");

    var user = new ApplicationUser
    {
        Id = Guid.NewGuid(),
        UserName = "native-aot@example.test",
        Email = "native-aot@example.test",
        EmailConfirmed = true,
        IsActive = true,
    };
    Require(await users.CreateAsync(user, "initial-password"), "user creation");
    Require(await users.AddToRoleAsync(user, "User"), "role membership");
    Require(await users.ChangePasswordAsync(user, "initial-password", "changed-password"),
        "password change");
    IAuthPasswordResetCommand passwordReset = scope.ServiceProvider
        .GetRequiredService<IAuthPasswordResetCommand>();
    Require(await passwordReset.ResetByAuthorityAsync(user, "reset-password"),
        "atomic password reset");
    if (!await users.CheckPasswordAsync(user, "reset-password"))
        throw new InvalidOperationException("The Native AOT atomic password reset did not persist.");

    IAuthAuditWriter auditWriter = scope.ServiceProvider.GetRequiredService<IAuthAuditWriter>();
    IAuthAuditReader auditReader = scope.ServiceProvider.GetRequiredService<IAuthAuditReader>();
    await auditWriter.WriteAsync(new AuthAuditWrite(
        "native-aot.executed", "verification", true, user.Id, null, "127.0.0.1",
        "native-aot-smoke", null, "native-aot-correlation",
        [new AuthAuditFact("runtime", "native-aot")]));
    ImmutableArray<AuthAuditRecord> auditRecords = await auditReader.ReadAsync(new AuthAuditQuery
    {
        SubjectUserId = user.Id,
        Action = "native-aot.executed",
        Category = "verification",
        CorrelationId = "native-aot-correlation",
        Limit = 10,
    });
    if (auditRecords is not [{ Facts: [{ Key: "runtime", Value: "native-aot" }] }])
        throw new InvalidOperationException("The Native AOT audit record did not round-trip.");

    ISessionManager sessions = scope.ServiceProvider.GetRequiredService<ISessionManager>();
    UserSession session = await sessions.CreateSessionAsync(user.Id,
        new SessionContext("127.0.0.1", "native-aot-smoke"));
    if ((await sessions.GetActiveSessionsAsync(user.Id)).All(value => value.Id != session.Id))
        throw new InvalidOperationException("The Native AOT session was not persisted.");
    await sessions.RevokeSessionAsync(session.Id);

    UserSession expired = await sessions.CreateSessionAsync(user.Id,
        new SessionContext("127.0.0.1", "native-aot-expired", Lifetime: TimeSpan.FromMinutes(-1)));
    BaseSession system = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
    {
        AuthenticationState = PrincipalAuthenticationState.System,
        SubjectKind = AccessSubjectKind.System,
        SubjectId = "hpd.auth",
        CurrentTenantId = Guid.Empty.ToString("D"),
        AuthSource = "hpd.auth.native-aot-smoke.v1",
    });
    BaseBinary dataProtectionDiscriminator = BaseBinary.From(
        SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(applicationName)));
    IReadOnlyList<BaseGeneratedScheduleRegistration> schedules =
        AuthScheduleDeclarations.Create(dataProtectionDiscriminator);
    BaseGeneratedScheduleRegistration sessionExpirationSchedule = schedules.Single(static schedule =>
        schedule.Definition.Id == "hpd.auth.schedule.session-expiration.v1");
    BaseGeneratedScheduleRegistration dataProtectionSchedule = schedules.Single(static schedule =>
        schedule.Definition.Id == "hpd.auth.schedule.data-protection-refresh.v1");
    RequireBase(await system.Activations.GetSchedule(sessionExpirationSchedule.Identity)
        .CreateAsync(Identity("session-expiration-schedule-create", "one")),
        "session-expiration schedule creation");
    RequireBase(await system.Activations.GetSchedule(dataProtectionSchedule.Identity)
        .CreateAsync(Identity("data-protection-refresh-schedule-create", "one")),
        "Data Protection refresh schedule creation");
    verificationTime.Advance(TimeSpan.FromMinutes(5));
    OperationResult<BaseScheduleMaintenancePage> expirationOccurrences = await system.Activations
        .GetSchedule(sessionExpirationSchedule.Identity)
        .AdvanceAsync(Identity("session-expiration-schedule", "one"));
    RequireBase(expirationOccurrences, "session-expiration schedule materialization");
    if (!expirationOccurrences.Value!.Occurrences.Any(static occurrence =>
            occurrence.Disposition is BaseOccurrenceMaterialized))
        throw new InvalidOperationException("The Native AOT session-expiration schedule did not materialize a due occurrence.");
    OperationResult<BaseActivationDispatchResult> expirationDispatch = await system.Activations
        .GetWorker(AuthLifecycleActivationDeclarations.Sessions.Identity).RunOneAsync();
    RequireBase(expirationDispatch, "session-expiration dispatch");
    if ((await sessions.GetActiveSessionsAsync(user.Id)).Any(value => value.Id == expired.Id))
        throw new InvalidOperationException("The Native AOT expiration cohort did not revoke its session.");

    string securityStamp = await users.GetSecurityStampAsync(user);
    IRefreshTokenStore refresh = scope.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
    var issuance = new RefreshTokenIssueRequest
    {
        UserId = user.Id,
        SecurityStamp = securityStamp,
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        RequestScope = "native-aot-smoke",
        IdempotencyKey = "issue-1",
    };
    RefreshTokenPersistenceResult issued = await refresh.IssueAsync(issuance);
    RefreshTokenPersistenceResult replayed = await refresh.IssueAsync(issuance);
    if (!string.Equals(issued.Token, replayed.Token, StringComparison.Ordinal))
        throw new InvalidOperationException("The Native AOT issuance receipt did not replay.");
    var rotationRequest = new RefreshTokenRotateRequest
    {
        PredecessorToken = issued.Token,
        SecurityStamp = securityStamp,
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
    };
    RefreshTokenPersistenceResult? rotated = await refresh.RotateAsync(rotationRequest);
    if (rotated is null || string.Equals(rotated.Token, issued.Token, StringComparison.Ordinal))
        throw new InvalidOperationException("The Native AOT refresh rotation did not commit.");
    RefreshTokenPersistenceResult? rotationReplay = await refresh.RotateAsync(rotationRequest);
    if (rotationReplay is null || !string.Equals(rotationReplay.Token, rotated.Token, StringComparison.Ordinal))
        throw new InvalidOperationException("The Native AOT refresh rotation response-loss replay was not exact.");

    IXmlRepository keys = scope.ServiceProvider.GetRequiredService<IXmlRepository>();
    keys.StoreElement(XElement.Parse("<key id=\"native-aot\" />"), "native-aot");
    if (keys.GetAllElements().All(value => value.Attribute("id")?.Value != "native-aot"))
        throw new InvalidOperationException("The Native AOT Data Protection key was not cached.");
    verificationTime.Advance(TimeSpan.FromSeconds(30));
    OperationResult<BaseScheduleMaintenancePage> dataProtectionOccurrences = await system.Activations
        .GetSchedule(dataProtectionSchedule.Identity)
        .AdvanceAsync(Identity("data-protection-refresh-schedule", "one"));
    RequireBase(dataProtectionOccurrences, "Data Protection refresh schedule materialization");
    if (!dataProtectionOccurrences.Value!.Occurrences.Any(static occurrence =>
            occurrence.Disposition is BaseOccurrenceMaterialized))
        throw new InvalidOperationException("The Native AOT Data Protection schedule did not materialize a due occurrence.");
    RequireBase(await system.Activations
        .GetWorker(AuthLifecycleActivationDeclarations.DataProtection.Identity).RunOneAsync(),
        "Data Protection refresh dispatch");

    BaseSession service = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
    {
        AuthenticationState = PrincipalAuthenticationState.Service,
        SubjectKind = AccessSubjectKind.ServicePrincipal,
        SubjectId = "hpd.auth",
        CurrentTenantId = Guid.Empty.ToString("D"),
        AuthSource = "hpd.auth.native-aot-smoke.acquire.v1",
    });
    BaseResult<AuthUserSubjectAcquisitionReadV1.Row?> acquired = await service.Reads.FirstAsync(
        AuthUserSubjectAcquisitionReadV1.Handle,
        new AuthUserSubjectAcquisitionReadV1
        {
            UserId = BaseRecordId<AuthUserRecordV1>.Create(user.Id.ToString("D")),
        });
    BaseSubjectReference<AuthUserSubject> subject = acquired.RequireValue()?.Reference
        ?? throw new InvalidOperationException("The Native AOT Auth subject was not acquired.");
    Guid customerProfileId = Guid.NewGuid();
    BaseCollectionSession<NativeAotCustomerProfile> customerProfiles =
        service.Collection(NativeAotCustomerProfile.Collection);
    BaseRecord<NativeAotCustomerProfile> customerProfile = (await customerProfiles.CreateAsync(
        RecordId.Create(customerProfileId.ToString("D")),
        new NativeAotCustomerProfile
        {
            Id = customerProfileId,
            TenantId = Guid.Empty,
            User = subject,
        })).RequireValue();
    BaseRecord<NativeAotCustomerProfile> readCustomerProfile = (await customerProfiles.GetAsync(
        customerProfile.Id)).RequireValue();
    if (readCustomerProfile.Value.User != subject)
        throw new InvalidOperationException("The external consumer's typed Auth reference did not round-trip.");
    IUserStore<ApplicationUser> userStore = scope.ServiceProvider
        .GetRequiredService<IUserStore<ApplicationUser>>();
    Require(await userStore.DeleteAsync(user, CancellationToken.None), "user tombstone");
    BaseInstalledSubjectRetirementConsumer<AuthUserSubject> retirementConsumer =
        system.SubjectRetirements.Get(NativeAotAuthConsumer.RetirementIdentity);
    await using (IAsyncEnumerator<BaseSubjectRequiredLifecycleDelivery<AuthUserSubject>> deliveries =
        retirementConsumer.ReadRequiredAsync().GetAsyncEnumerator())
    {
        if (!await deliveries.MoveNextAsync())
            throw new InvalidOperationException("The external consumer did not receive the Auth tombstone.");
        BaseSubjectRequiredLifecycleDelivery<AuthUserSubject> delivery = deliveries.Current;
        if (delivery.Lifecycle.Fact.Subject != subject
            || delivery.Lifecycle.Fact.Fact.Kind != BaseSubjectLifecycleFactKind.Transitioned
            || delivery.Lifecycle.Fact.Fact.Transitioned?.CurrentState != BaseSubjectLifecycleState.Tombstoned)
            throw new InvalidOperationException("The external consumer received the wrong Auth lifecycle fact.");
        BaseResult<BaseSubjectAcknowledgementResult> acknowledged = await retirementConsumer.AcknowledgeAsync(
            delivery.Acknowledgement,
            BaseSubjectAcknowledgementDisposition.Completed,
            delivery.AcknowledgementIdentity);
        if (acknowledged.RequireValue().Outcome != BaseSubjectRetirementMutationOutcome.Applied)
            throw new InvalidOperationException("The external consumer's required retirement acknowledgement was not applied.");
        BaseResult<BaseSubjectLifecycleCheckpointResult> advanced = await system.SubjectLifecycle
            .Get(NativeAotAuthConsumer.LifecycleIdentity)
            .AdvanceAsync(delivery.Lifecycle.Checkpoint, delivery.Lifecycle.AdvanceIdentity);
        if (advanced.RequireValue().Duplicate)
            throw new InvalidOperationException("The external consumer lifecycle checkpoint was unexpectedly a duplicate.");
    }
    OperationResult<BaseActivationDispatchResult> bootstrap = await system.Activations
        .GetWorker(AuthLifecycleActivationDeclarations.BootstrapUser.Identity).RunOneAsync();
    RequireBase(bootstrap, "cleanup bootstrap");
    BaseInstalledActivationWorkerHandle<AuthUserCleanupInputV1, AuthCleanupResultV1> cleanupWorker =
        system.Activations.GetWorker(AuthCleanupActivationDeclarations.User.Identity);
    BaseActivationState cleanupState;
    do
    {
        OperationResult<BaseActivationDispatchResult> cleanup = await cleanupWorker.RunOneAsync();
        RequireBase(cleanup, "cleanup cohort");
        cleanupState = cleanup.Value?.State
            ?? throw new InvalidOperationException("Native AOT cleanup returned no activation state.");
    }
    while (cleanupState == BaseActivationState.YieldPending);
    BaseResult<AuthCleanupWorkReadV1.Row?> cleanupWork = await system.Reads.FirstAsync(
        AuthCleanupWorkReadV1.Handle,
        new AuthCleanupWorkReadV1
        {
            TenantId = Guid.Empty,
            SubjectKind = AuthCleanupSubjectKindV1.user,
            SubjectId = user.Id,
            Incarnation = BaseBinary.From(subject.Incarnation.ToArray()),
        });
    if (cleanupWork.RequireValue() is not { State: AuthCleanupStateV1.waitingRetention })
        throw new InvalidOperationException("The Native AOT cleanup workflow did not enter retention.");

    verificationTime.Advance(TimeSpan.FromDays(31));
    do
    {
        OperationResult<BaseActivationDispatchResult> cleanup = await cleanupWorker.RunOneAsync();
        RequireBase(cleanup, "post-retention cleanup cohort");
        cleanupState = cleanup.Value?.State
            ?? throw new InvalidOperationException("Native AOT post-retention cleanup returned no state.");
    }
    while (cleanupState == BaseActivationState.YieldPending);

    RequireBase(await system.Activations
        .GetWorker(AuthLifecycleActivationDeclarations.RetireUser.Identity).RunOneAsync(),
        "semantic and physical subject retirement");
    cleanupWork = await system.Reads.FirstAsync(
        AuthCleanupWorkReadV1.Handle,
        new AuthCleanupWorkReadV1
        {
            TenantId = Guid.Empty,
            SubjectKind = AuthCleanupSubjectKindV1.user,
            SubjectId = user.Id,
            Incarnation = BaseBinary.From(subject.Incarnation.ToArray()),
        });
    if (cleanupWork.RequireValue() is not { State: AuthCleanupStateV1.complete })
        throw new InvalidOperationException("The Native AOT cleanup workflow did not complete retirement.");
    BaseResult<BaseRecord<AuthUserRecordV1>> retired = await system
        .Collection(AuthUserRecordV1.Collection)
        .GetAsync(RecordId.Create(user.Id.ToString("D")));
    if (retired.Status != OperationStatus.NotFound)
        throw new InvalidOperationException("The Native AOT private Auth subject was not physically retired.");
    BaseRecord<NativeAotCustomerProfile> survivingConsumerRecord = (await customerProfiles.GetAsync(
        customerProfile.Id)).RequireValue();
    if (survivingConsumerRecord.Value.User != subject)
        throw new InvalidOperationException("Auth retirement mutated the external module's owned record.");
}
finally
{
    for (int index = hosted.Length - 1; index >= 0; index--)
        await hosted[index].StopAsync(CancellationToken.None);
}

static void Require(IdentityResult result, string operation)
{
    if (!result.Succeeded)
        throw new InvalidOperationException($"Native AOT {operation} failed: "
            + string.Join(", ", result.Errors.Select(static error => error.Code)));
}

static void RequireBase<T>(OperationResult<T> result, string operation)
{
    if (!result.IsSuccess())
        throw new InvalidOperationException($"Native AOT {operation} failed: {result.Error?.Code ?? "unknown"}.");
}

static BaseMutationRequestIdentity Identity(string operation, string item) =>
    BaseMutationRequestIdentity.Create(
        "hpd.auth.native-aot-smoke",
        operation,
        item,
        BaseMutationRequestFingerprint.Create(SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"hpd.auth.native-aot-smoke.{operation}.{item}.v1"))));

sealed class VerificationTimeProvider(DateTimeOffset initial) : TimeProvider
{
    private DateTimeOffset _now = initial;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan duration) => _now = _now.Add(duration);
}
