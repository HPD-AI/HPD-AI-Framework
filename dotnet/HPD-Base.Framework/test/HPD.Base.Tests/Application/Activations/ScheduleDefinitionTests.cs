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

    private static BaseScheduleDefinition Definition(BaseScheduleExpression expression)
    {
        byte[] input = "input"u8.ToArray();
        return new BaseScheduleDefinition
        {
            Id = "test.schedule", Version = 1, OwningModuleId = "test.module",
            Activation = new BaseActivationDefinitionKey { Id = "test.activation", Version = 1, Checksum = new byte[32].ToImmutableArray() },
            CanonicalInput = input.ToImmutableArray(), InputChecksum = SHA256.HashData(input).ToImmutableArray(),
            Expression = expression, GapPolicy = BaseTimeGapPolicy.Skip, TimeOverlapPolicy = BaseTimeOverlapPolicy.EarlierOffset,
            MisfirePolicy = BaseScheduleMisfirePolicy.RunAll, ActivationOverlapPolicy = BaseScheduleOverlapPolicy.Allow,
            OverlapKeyKind = BaseScheduleOverlapKeyKind.Schedule, Priority = 0, MaximumSplayMilliseconds = 0,
            Checksum = [],
        };
    }
}
