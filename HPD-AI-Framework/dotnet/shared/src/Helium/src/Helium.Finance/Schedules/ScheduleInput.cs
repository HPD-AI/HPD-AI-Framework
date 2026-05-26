using Helium.Finance.Calendars;

namespace Helium.Finance.Schedules;

public readonly record struct ScheduleInput(
    DateOnly EffectiveDate,
    DateOnly TerminationDate,
    ScheduleTenor Tenor,
    HolidayCalendar Calendar,
    BusinessDayConvention Convention,
    BusinessDayConvention TerminationDateConvention,
    DateGenerationRule Rule,
    bool EndOfMonth = false,
    DateOnly? FirstDate = null,
    DateOnly? NextToLastDate = null)
{
    public static ScheduleInput Forward(
        DateOnly effectiveDate,
        DateOnly terminationDate,
        ScheduleTenor tenor,
        HolidayCalendar? calendar = null,
        BusinessDayConvention convention = BusinessDayConvention.Following,
        BusinessDayConvention? terminationDateConvention = null,
        bool endOfMonth = false,
        DateOnly? firstDate = null,
        DateOnly? nextToLastDate = null)
    {
        return new ScheduleInput(
            effectiveDate,
            terminationDate,
            tenor,
            calendar ?? HolidayCalendar.WeekendsOnly,
            convention,
            terminationDateConvention ?? convention,
            DateGenerationRule.Forward,
            endOfMonth,
            firstDate,
            nextToLastDate);
    }

    public static ScheduleInput Backward(
        DateOnly effectiveDate,
        DateOnly terminationDate,
        ScheduleTenor tenor,
        HolidayCalendar? calendar = null,
        BusinessDayConvention convention = BusinessDayConvention.Following,
        BusinessDayConvention? terminationDateConvention = null,
        bool endOfMonth = false,
        DateOnly? firstDate = null,
        DateOnly? nextToLastDate = null)
    {
        return new ScheduleInput(
            effectiveDate,
            terminationDate,
            tenor,
            calendar ?? HolidayCalendar.WeekendsOnly,
            convention,
            terminationDateConvention ?? convention,
            DateGenerationRule.Backward,
            endOfMonth,
            firstDate,
            nextToLastDate);
    }
}
