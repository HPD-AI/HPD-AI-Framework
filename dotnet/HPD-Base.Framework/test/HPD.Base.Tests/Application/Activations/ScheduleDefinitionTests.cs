using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace HPD.Base.Tests.Application.Activations;

public sealed class ScheduleDefinitionTests
{
    [Fact]
    public void Cron_is_normalized_and_next_occurrence_is_strict()
    {
        BaseScheduleDefinition schedule = Definition(new BaseCronSchedule("0 30 9 * * 1-5", "UTC"));

        ((BaseCronSchedule)schedule.Expression).Expression.Should().Be(
            "0 30 9 * 1,2,3,4,5,6,7,8,9,10,11,12 1,2,3,4,5");
        long friday = new DateTimeOffset(2026, 8, 21, 9, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        long monday = new DateTimeOffset(2026, 8, 24, 9, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        BaseScheduleDefinitionBuilder.NextNominal(schedule.Expression, friday).Should().Be(monday);
    }

    [Fact]
    public void Interval_uses_checked_smallest_strict_successor()
    {
        BaseScheduleExpression interval = Definition(new BaseIntervalSchedule(100, 25)).Expression;
        BaseScheduleDefinitionBuilder.NextNominal(interval, null).Should().Be(100);
        BaseScheduleDefinitionBuilder.NextNominal(interval, 100).Should().Be(125);
        BaseScheduleDefinitionBuilder.NextNominal(interval, 149).Should().Be(150);
    }

    [Fact]
    public void Subday_calendar_frequency_advances_in_its_declared_unit()
    {
        var secondly = new BaseCalendarSchedule(BaseCalendarFrequency.Secondly, 15,
            new BaseLocalTime { Hour = 0, Minute = 0, Second = 0, Millisecond = 0 }, new BaseEveryCalendarPeriod(), "UTC");
        BaseScheduleDefinitionBuilder.NextNominal(secondly, 0).Should().Be(15_000);
        BaseScheduleDefinitionBuilder.NextNominal(secondly, 15_000).Should().Be(30_000);

        var hourly = new BaseCalendarSchedule(BaseCalendarFrequency.Hourly, 2,
            new BaseLocalTime { Hour = 1, Minute = 0, Second = 0, Millisecond = 0 }, new BaseEveryCalendarPeriod(), "UTC");
        BaseScheduleDefinitionBuilder.NextNominal(hourly, 3_600_000).Should().Be(10_800_000);
    }

    [Fact]
    public void Installed_transition_authority_resolves_gap_and_both_overlap_instants()
    {
        BaseTimeZoneAuthority authority = BaseTimeZoneAuthorityBuilder.Create(new BaseTimeZoneAuthority
        {
            Generation = 1, ReleaseId = "2026a", MinimumUtcSecond = 0, MaximumUtcSecond = 4_102_444_800,
            Sources = [Source("2026a.tar.lz"), Source("tzdata.zi"), Source("zone.tab"), Source("zone1970.tab"), Source("backward")],
            Zones = [new BaseTimeZoneDefinition
            {
                Id = "America/Chicago", InitialOffsetSeconds = -21_600,
                Transitions =
                [
                    new BaseTimeZoneTransition { UtcSecond = new DateTimeOffset(2026, 3, 8, 8, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(), OffsetSeconds = -18_000, DaylightSaving = true, Abbreviation = "CDT" },
                    new BaseTimeZoneTransition { UtcSecond = new DateTimeOffset(2026, 11, 1, 7, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(), OffsetSeconds = -21_600, DaylightSaving = false, Abbreviation = "CST" },
                ],
            }], Aliases = [], CompiledBytes = [], Checksum = [],
        });
        var zones = new BaseTimeZoneRegistry(authority);
        long beforeGap = new DateTimeOffset(2026, 3, 7, 8, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        long gap = BaseScheduleDefinitionBuilder.NextNominal(new BaseCronSchedule("0 30 2 * * *", "America/Chicago"),
            beforeGap, zones, BaseTimeGapPolicy.NextValid, BaseTimeOverlapPolicy.EarlierOffset)!.Value;
        gap.Should().Be(new DateTimeOffset(2026, 3, 8, 8, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds());

        long beforeOverlap = new DateTimeOffset(2026, 10, 31, 7, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var expression = new BaseCronSchedule("0 30 1 * * *", "America/Chicago");
        long earlier = BaseScheduleDefinitionBuilder.NextNominal(expression, beforeOverlap, zones,
            BaseTimeGapPolicy.Skip, BaseTimeOverlapPolicy.Both)!.Value;
        long later = BaseScheduleDefinitionBuilder.NextNominal(expression, earlier, zones,
            BaseTimeGapPolicy.Skip, BaseTimeOverlapPolicy.Both)!.Value;
        later.Should().Be(checked(earlier + 3_600_000));
        BaseScheduleDefinitionBuilder.OverlapOrdinal(expression, earlier, zones,
            BaseTimeGapPolicy.Skip, BaseTimeOverlapPolicy.Both).Should().Be(0);
        BaseScheduleDefinitionBuilder.OverlapOrdinal(expression, later, zones,
            BaseTimeGapPolicy.Skip, BaseTimeOverlapPolicy.Both).Should().Be(1);
    }

    private static BaseTimeZoneSourceReceipt Source(string name)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(name);
        return new BaseTimeZoneSourceReceipt { Name = name, ByteLength = bytes.Length, Checksum = SHA256.HashData(bytes).ToImmutableArray() };
    }

    private static BaseScheduleDefinition Definition(BaseScheduleExpression expression)
    {
        return BaseScheduleDefinitionBuilder.CreateGenerated(new BaseScheduleDefinitionDraft
        {
            Id = "test.schedule", Version = 1, OwningModuleId = "test.module",
            ManageGrantId = "test.schedule.manage", MaterializeGrantId = "test.schedule.materialize",
            Expression = expression, GapPolicy = BaseTimeGapPolicy.Skip, TimeOverlapPolicy = BaseTimeOverlapPolicy.EarlierOffset,
            MisfirePolicy = BaseScheduleMisfirePolicy.RunAll, ActivationOverlapPolicy = BaseScheduleOverlapPolicy.Allow,
            OverlapKeyKind = BaseScheduleOverlapKeyKind.Schedule, Priority = 0, MaximumSplayMilliseconds = 0,
        }, Target, ScheduleTestDtos.HPDBaseActivationDtoAuthority, new ScheduleTestInput { Value = "input" }).Definition;
    }

    private static BaseActivationHandlerRegistration<ScheduleTestInput, ScheduleTestResult> Target { get; } =
        BaseActivationDefinitionBuilder.CreateGenerated(new BaseActivationDefinitionDraft
        {
            Id = "test.activation", Version = 1, OwningModuleId = "test.module",
            ExecutionClass = BaseActivationExecutionClass.AtLeastOnceWorker,
            Grants = new BaseActivationGrantSet
            {
                Enqueue = "test.activation.enqueue", Observe = "test.activation.observe", Claim = "test.activation.claim",
                Execute = "test.activation.execute", Renew = "test.activation.renew", Complete = "test.activation.complete",
                Fail = "test.activation.fail", Yield = "test.activation.yield", Cancel = "test.activation.cancel", Inspect = "test.activation.inspect",
                Replay = "test.activation.replay", Migrate = "test.activation.migrate", Reconcile = "test.activation.reconcile",
                Retry = "test.activation.retry", Dispose = "test.activation.dispose", Remove = "test.activation.remove",
                Repair = "test.activation.repair",
            },
            SourceGrantIds = [],
            Retry = new BaseActivationRetryProfile { MaximumAttempts = 1, InitialDelayMilliseconds = 1,
                MaximumDelayMilliseconds = 1, MultiplierNumerator = 1, MultiplierDenominator = 1,
                JitterBasisPoints = 0, RetryableFailureCodes = [] },
            ReceiptRetention = new BaseActivationReceiptRetentionPolicy
            {
                FormatVersion = 1, DuplicateResolutionLifetime = TimeSpan.FromHours(24),
                ProtectedBackupCoverage = BaseActivationProtectedBackupCoverage.NotRequired,
            },
            Limits = new BaseActivationLimits
            {
                MaximumInputBytes = 256, MaximumResultBytes = 256, MaximumAttempts = 1, MaximumYields = 0,
                MaximumRenewalsPerSlice = 1, MaximumChildrenPerSlice = 1, MaximumLineageDepth = 1,
                LeaseDuration = TimeSpan.FromSeconds(5), HandlerTimeout = TimeSpan.FromSeconds(5),
                Provider = ProviderLimits(), AtomicCreation = AtomicLimits(),
            },
            Handler = new BaseActivationHandlerDraft { Id = "test.schedule.handler", Version = 1,
                FactoryId = "test.schedule.handler.factory", WorkerSubjectKind = AccessSubjectKind.System,
                SemanticAuthority = BaseActivationHandlerSemanticAuthority.Create("test.schedule.handler.semantics", 1) },
        }, ScheduleTestDtos.HPDBaseActivationDtoAuthority, static _ => new ScheduleTestHandler());

    private static BaseActivationExecutionLimits ProviderLimits() => new()
    {
        MaximumCandidates = 1, MaximumInputBytes = 256, MaximumResultBytes = 256, MaximumEvidenceBytes = 1024,
        MaximumTransientBytes = 4096, MaximumReadIntervals = 1, MaximumIndexOperations = 1,
        AcquisitionTimeout = TimeSpan.FromSeconds(1), TransactionTimeout = TimeSpan.FromSeconds(1),
        CommitObservationTimeout = TimeSpan.FromSeconds(1), ReceiptResolutionTimeout = TimeSpan.FromSeconds(1),
    };

    private static BaseAtomicMutationExecutionLimits AtomicLimits() =>
        DefaultBaseModuleMutationRuntime.ResolveExecutionLimits(BaseModuleMutationPlatform.MaximumLimits);
}

internal sealed record ScheduleTestInput
{
    [BaseField("schedule.input.value", MaximumUtf8Bytes = 16), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)]
    public required string Value { get; init; }
}
internal sealed record ScheduleTestResult
{
    [BaseField("schedule.result.value", MaximumUtf8Bytes = 16), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)]
    public required string Value { get; init; }
}
internal sealed class ScheduleTestHandler : IBaseActivationHandler<ScheduleTestInput, ScheduleTestResult>
{
    public ValueTask<BaseActivationHandlerResult<ScheduleTestResult>> ExecuteAsync(
        BaseActivationContext context, ScheduleTestInput input, CancellationToken cancellationToken) =>
        ValueTask.FromResult<BaseActivationHandlerResult<ScheduleTestResult>>(new BaseActivationSucceeded<ScheduleTestResult>
        { Result = new ScheduleTestResult { Value = input.Value } });
}
[BaseActivationDtoAuthority("test.schedule.dto", 1, "test.module", "test.schedule.input", "test.schedule.result",
    typeof(ScheduleTestJsonContext), typeof(ScheduleTestInput), typeof(ScheduleTestResult))]
internal static partial class ScheduleTestDtos;
[JsonSerializable(typeof(ScheduleTestInput))]
[JsonSerializable(typeof(ScheduleTestResult))]
internal sealed partial class ScheduleTestJsonContext : JsonSerializerContext;
