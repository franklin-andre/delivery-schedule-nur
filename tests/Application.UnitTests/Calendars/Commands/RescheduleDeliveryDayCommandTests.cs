using Ardalis.GuardClauses;
using delivery_schedule_management.Application.Calendars.Commands.CreateSchedule;
using delivery_schedule_management.Application.Calendars.Commands.RescheduleDeliveryDay;
using delivery_schedule_management.Application.UnitTests.Common.Fakes;
using delivery_schedule_management.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace delivery_schedule_management.Application.UnitTests.Calendars.Commands;

public class RescheduleDeliveryDayCommandTests
{
    private static async Task<(FakeCalendarRepository Repo, int CalendarId, int DayId)> SeedCalendar()
    {
        var repository = new FakeCalendarRepository();
        var createHandler = new CreateScheduleCommandHandler(repository, new FakeApplicationDbContext());

        var created = await createHandler.Handle(
            new CreateScheduleCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15, new DateOnly(2026, 8, 10)),
            CancellationToken.None);

        return (repository, created.CalendarId, created.Days[0].Id);
    }

    [Test]
    public async Task Handle_ShouldUpdateDate_WhenOutsideLockWindow()
    {
        var (repository, calendarId, dayId) = await SeedCalendar();
        var clock = new TestClock(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));
        var handler = new RescheduleDeliveryDayCommandHandler(repository, new FakeApplicationDbContext(), clock);

        var result = await handler.Handle(
            new RescheduleDeliveryDayCommand(calendarId, dayId, new DateOnly(2026, 8, 30)),
            CancellationToken.None);

        result.DayId.ShouldBe(dayId);
    }

    [Test]
    public async Task Handle_ShouldThrowDeliveryWindowLockedException_WhenInsideFortyEightHourWindow()
    {
        var (repository, calendarId, dayId) = await SeedCalendar();
        var clock = new TestClock(new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero)); // < 48h before Aug 10
        var handler = new RescheduleDeliveryDayCommandHandler(repository, new FakeApplicationDbContext(), clock);

        await Should.ThrowAsync<DeliveryWindowLockedException>(() =>
            handler.Handle(new RescheduleDeliveryDayCommand(calendarId, dayId, new DateOnly(2026, 8, 25)), CancellationToken.None));
    }

    [Test]
    public void Handle_ShouldThrowNotFoundException_WhenCalendarMissing()
    {
        var repository = new FakeCalendarRepository();
        var handler = new RescheduleDeliveryDayCommandHandler(repository, new FakeApplicationDbContext(), new TestClock(DateTimeOffset.UtcNow));

        Should.ThrowAsync<NotFoundException>(() =>
            handler.Handle(new RescheduleDeliveryDayCommand(999, 1, new DateOnly(2026, 8, 25)), CancellationToken.None));
    }
}
