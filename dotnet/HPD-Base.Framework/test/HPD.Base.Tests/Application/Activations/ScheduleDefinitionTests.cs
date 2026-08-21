using System.Collections.Immutable;
using System.Security.Cryptography;

namespace HPD.Base.Tests.Application.Activations;

public sealed class ScheduleDefinitionTests
{
    [Fact]
    public void Cron_is_normalized_and_next_occurrence_is_strict()
    {
        BaseScheduleDefinition schedule = BaseScheduleDefinitionBuilder.Create(Definition(
            new BaseCronSchedule("0 30 9 * * 1-5", "UTC")));

        ((BaseCronSchedule)schedule.Expression).Expression.Should().Be(
            "0 30 9 * 1,2,3,4,5,6,7,8,9,10,11,12 1,2,3,4,5");
        long friday = new DateTimeOffset(2026, 8, 21, 9, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        long monday = new DateTimeOffset(2026, 8, 24, 9, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        BaseScheduleDefinitionBuilder.NextNominal(schedule.Expression, friday).Should().Be(monday);
    }

    [Fact]
    public void Interval_uses_checked_smallest_strict_successor()
    {
        BaseScheduleExpression interval = BaseScheduleDefinitionBuilder.Create(Definition(new BaseIntervalSchedule(100, 25))).Expression;
        BaseScheduleDefinitionBuilder.NextNominal(interval, null).Should().Be(100);
        BaseScheduleDefinitionBuilder.NextNominal(interval, 100).Should().Be(125);
        BaseScheduleDefinitionBuilder.NextNominal(interval, 149).Should().Be(150);
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
    }

    private static BaseTimeZoneSourceReceipt Source(string name)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(name);
        return new BaseTimeZoneSourceReceipt { Name = name, ByteLength = bytes.Length, Checksum = SHA256.HashData(bytes).ToImmutableArray() };
    }

    private static BaseScheduleDefinition Definition(BaseScheduleExpression expression)
    {
        byte[] input = "input"u8.ToArray();
        return new BaseScheduleDefinition
        {
            Id = "test.schedule", Version = 1, OwningModuleId = "test.module",
            ManageGrantId = "test.schedule.manage", MaterializeGrantId = "test.schedule.materialize",
            Activation = new BaseActivationDefinitionKey { Id = "test.activation", Version = 1, Checksum = new byte[32].ToImmutableArray() },
            CanonicalInput = input.ToImmutableArray(), InputChecksum = SHA256.HashData(input).ToImmutableArray(),
            Expression = expression, GapPolicy = BaseTimeGapPolicy.Skip, TimeOverlapPolicy = BaseTimeOverlapPolicy.EarlierOffset,
            MisfirePolicy = BaseScheduleMisfirePolicy.RunAll, ActivationOverlapPolicy = BaseScheduleOverlapPolicy.Allow,
            OverlapKeyKind = BaseScheduleOverlapKeyKind.Schedule, Priority = 0, MaximumSplayMilliseconds = 0,
            Checksum = [],
        };
    }
}
