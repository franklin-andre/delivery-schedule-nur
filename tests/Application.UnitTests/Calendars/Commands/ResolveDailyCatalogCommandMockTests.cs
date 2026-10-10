using delivery_schedule_management.Application.Calendars.Commands.ResolveDailyCatalog;
using delivery_schedule_management.Application.Common.Interfaces;
using delivery_schedule_management.Application.UnitTests.Common.Fakes;
using delivery_schedule_management.Domain.Entities;
using delivery_schedule_management.Domain.Enums;
using delivery_schedule_management.Domain.ValueObjects;
using Moq;
using NUnit.Framework;
using Shouldly;

namespace delivery_schedule_management.Application.UnitTests.Calendars.Commands;

/// <summary>Interaction-style tests: ports are Moq mocks so calls can be verified.</summary>
public class ResolveDailyCatalogCommandMockTests
{
    private static readonly DateOnly Target = new(2026, 8, 11);

    private Mock<ICalendarRepository> _calendars = null!;
    private Mock<IProductionOrderRepository> _orders = null!;
    private Mock<ICatalogPort> _catalog = null!;
    private Mock<IApplicationDbContext> _context = null!;
    private TestClock _clock = null!;

    [SetUp]
    public void SetUp()
    {
        _calendars = new Mock<ICalendarRepository>();
        _orders = new Mock<IProductionOrderRepository>();
        _catalog = new Mock<ICatalogPort>();
        _context = new Mock<IApplicationDbContext>();
        _clock = new TestClock(new DateTimeOffset(2026, 8, 10, 8, 0, 0, TimeSpan.Zero));
    }

    private ResolveDailyCatalogCommandHandler CreateHandler() =>
        new(_calendars.Object, _orders.Object, _catalog.Object, _context.Object, _clock);

    private static DeliveryCalendar CalendarWithScheduledDay()
    {
        var calendar = DeliveryCalendar.Create(
            Guid.NewGuid(),
            new ContractReference(Guid.NewGuid()),
            new PlanReference(Guid.NewGuid()),
            new PlanDuration(15),
            new DeliveryDate(new DateOnly(2026, 8, 10)));

        var day = calendar.Days.First(d => d.Date.Value == Target);
        day.SetAddress(new DeliveryAddress("Street", "Quito", new Coordinates(-0.18, -78.47)));
        return calendar;
    }

    private void SetupNoExistingOrder() =>
        _orders.Setup(r => r.GetByDeliveryDateAsync(Target, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductionOrder?)null);

    [Test]
    public async Task Handle_ShouldCallCatalogOncePerScheduledDay_AndSaveOnce()
    {
        var calendar = CalendarWithScheduledDay();
        _calendars.Setup(r => r.GetActiveForDeliveryDateAsync(Target, It.IsAny<CancellationToken>()))
            .ReturnsAsync([calendar]);
        SetupNoExistingOrder();
        _catalog.Setup(c => c.GetDayResolutionAsync(calendar.PlanId.Value, Target, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogDayResolution("recipe-1", 2));

        await CreateHandler().Handle(new ResolveDailyCatalogCommand(Target), CancellationToken.None);

        _catalog.Verify(c => c.GetDayResolutionAsync(calendar.PlanId.Value, Target, It.IsAny<CancellationToken>()), Times.Once);
        _orders.Verify(r => r.Add(It.Is<ProductionOrder>(o => o.Lines.Count == 1)), Times.Once);
        _context.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        calendar.Days.First(d => d.Date.Value == Target).Status.ShouldBe(DayStatus.ReadyForProduction);
    }

    [Test]
    public async Task Handle_ShouldNotCallCatalog_WhenNoCalendarHasScheduledDay()
    {
        _calendars.Setup(r => r.GetActiveForDeliveryDateAsync(Target, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        SetupNoExistingOrder();

        await CreateHandler().Handle(new ResolveDailyCatalogCommand(Target), CancellationToken.None);

        _catalog.Verify(c => c.GetDayResolutionAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Handle_ShouldDefaultToTomorrow_WhenNoTargetDate()
    {
        _calendars.Setup(r => r.GetActiveForDeliveryDateAsync(Target, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        SetupNoExistingOrder();

        await CreateHandler().Handle(new ResolveDailyCatalogCommand(null), CancellationToken.None);

        _calendars.Verify(r => r.GetActiveForDeliveryDateAsync(Target, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Handle_ShouldNotSave_WhenCatalogThrows()
    {
        var calendar = CalendarWithScheduledDay();
        _calendars.Setup(r => r.GetActiveForDeliveryDateAsync(Target, It.IsAny<CancellationToken>()))
            .ReturnsAsync([calendar]);
        _catalog.Setup(c => c.GetDayResolutionAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("MS2 down"));

        await Should.ThrowAsync<HttpRequestException>(() =>
            CreateHandler().Handle(new ResolveDailyCatalogCommand(Target), CancellationToken.None));

        _context.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
