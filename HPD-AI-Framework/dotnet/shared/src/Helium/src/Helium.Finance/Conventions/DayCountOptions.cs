using Helium.Finance.Calendars;

namespace Helium.Finance.Conventions;

public readonly record struct DayCountOptions(
    DateOnly? TerminationDate = null,
    HolidayCalendar? Calendar = null);
