using delivery_schedule_management.Domain.Common;
using delivery_schedule_management.Domain.Entities;
using delivery_schedule_management.Domain.ValueObjects;
using delivery_schedule_management.Infrastructure.Data.Interceptors;
using delivery_schedule_management.Infrastructure.Data.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using NUnit.Framework;
using Shouldly;

namespace delivery_schedule_management.Application.UnitTests.Infrastructure;

public class RepositoryAndInterceptorTests
{
    private sealed class TestEvent : BaseEvent;

    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    private static DeliveryCalendar NewCalendar(DateOnly? start = null, Guid? contract = null) =>
        DeliveryCalendar.Create(
            Guid.NewGuid(),
            new ContractReference(contract ?? Guid.NewGuid()),
            new PlanReference(Guid.NewGuid()),
            new PlanDuration(15),
            new DeliveryDate(start ?? new DateOnly(2026, 9, 1)));

    private static Mock<TimeProvider> FixedTime()
    {
        var time = new Mock<TimeProvider>();
        time.Setup(t => t.GetUtcNow()).Returns(Now);
        return time;
    }

    // ───────── AuditableEntityInterceptor ─────────

    [Test]
    public async Task Save_ShouldStampCreatedAndLastModified_OnInsert()
    {
        using var db = new InMemoryDb(new AuditableEntityInterceptor(FixedTime().Object));
        var calendar = NewCalendar();

        await using (var context = db.CreateContext())
        {
            new CalendarRepository(context).Add(calendar);
            await context.SaveChangesAsync();
        }

        calendar.Created.ShouldBe(Now);
        calendar.LastModified.ShouldBe(Now);
    }

    [Test]
    public async Task Save_ShouldUpdateOnlyLastModified_OnUpdate()
    {
        var time = FixedTime();
        using var db = new InMemoryDb(new AuditableEntityInterceptor(time.Object));
        var order = ProductionOrder.Create(new DeliveryDate(new DateOnly(2026, 9, 2)), [], Now);

        await using (var context = db.CreateContext())
        {
            new ProductionOrderRepository(context).Add(order);
            await context.SaveChangesAsync();
        }

        var later = Now.AddHours(5);
        time.Setup(t => t.GetUtcNow()).Returns(later);

        await using (var context = db.CreateContext())
        {
            var loaded = await new ProductionOrderRepository(context).GetByDeliveryDateAsync(new DateOnly(2026, 9, 2), default);
            loaded!.ReplaceLines([], later); // modifies the aggregate root itself
            await context.SaveChangesAsync();

            loaded.Created.ShouldBe(Now);
            loaded.LastModified.ShouldBe(later);
        }
    }

    [Test]
    public void Save_Sync_ShouldAlsoStampAuditFields()
    {
        using var db = new InMemoryDb(new AuditableEntityInterceptor(FixedTime().Object));
        var calendar = NewCalendar();

        using var context = db.CreateContext();
        new CalendarRepository(context).Add(calendar);
        context.SaveChanges();

        calendar.Created.ShouldBe(Now);
    }

    [Test]
    public void UpdateEntities_ShouldIgnoreNullContext()
    {
        Should.NotThrow(() => new AuditableEntityInterceptor(FixedTime().Object).UpdateEntities(null));
    }

    // ───────── DispatchDomainEventsInterceptor ─────────

    [Test]
    public async Task Save_ShouldPublishAndClearDomainEvents()
    {
        var mediator = new Mock<IMediator>();
        using var db = new InMemoryDb(new DispatchDomainEventsInterceptor(mediator.Object));
        var calendar = NewCalendar();
        calendar.AddDomainEvent(new TestEvent());

        await using (var context = db.CreateContext())
        {
            new CalendarRepository(context).Add(calendar);
            await context.SaveChangesAsync();
        }

        mediator.Verify(m => m.Publish(It.IsAny<BaseEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        calendar.DomainEvents.ShouldBeEmpty();
    }

    [Test]
    public void Save_Sync_ShouldPublishDomainEvents()
    {
        var mediator = new Mock<IMediator>();
        using var db = new InMemoryDb(new DispatchDomainEventsInterceptor(mediator.Object));
        var calendar = NewCalendar();
        calendar.AddDomainEvent(new TestEvent());

        using var context = db.CreateContext();
        new CalendarRepository(context).Add(calendar);
        context.SaveChanges();

        mediator.Verify(m => m.Publish(It.IsAny<BaseEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task DispatchDomainEvents_ShouldIgnoreNullContext_AndNotPublish()
    {
        var mediator = new Mock<IMediator>();

        await new DispatchDomainEventsInterceptor(mediator.Object).DispatchDomainEvents(null);

        mediator.VerifyNoOtherCalls();
    }

    // ───────── Repositories ─────────

    [Test]
    public async Task CalendarRepository_ShouldFindByIdContractDayAndDate()
    {
        using var db = new InMemoryDb();
        var contract = Guid.NewGuid();
        var calendar = NewCalendar(new DateOnly(2026, 9, 1), contract);

        await using (var context = db.CreateContext())
        {
            new CalendarRepository(context).Add(calendar);
            await context.SaveChangesAsync();
        }

        await using var read = db.CreateContext();
        var repo = new CalendarRepository(read);
        var dayId = calendar.Days.First().Id;

        (await repo.GetByIdAsync(calendar.Id, default))!.Days.Count.ShouldBe(15);
        (await repo.GetByContractIdAsync(contract, default))!.Id.ShouldBe(calendar.Id);
        (await repo.GetByDeliveryDayIdAsync(dayId, default))!.Id.ShouldBe(calendar.Id);
        (await repo.GetActiveForDeliveryDateAsync(new DateOnly(2026, 9, 5), default)).Count.ShouldBe(1);

        (await repo.GetByIdAsync(999, default)).ShouldBeNull();
        (await repo.GetByContractIdAsync(Guid.NewGuid(), default)).ShouldBeNull();
        (await repo.GetByDeliveryDayIdAsync(999, default)).ShouldBeNull();
        (await repo.GetActiveForDeliveryDateAsync(new DateOnly(2030, 1, 1), default)).ShouldBeEmpty();
    }

    [Test]
    public async Task ProductionOrderRepository_ShouldPersistLines_AndFindByDeliveryDate()
    {
        using var db = new InMemoryDb();
        var date = new DeliveryDate(new DateOnly(2026, 9, 2));
        var line = new ProductionOrderLine(
            7, Guid.NewGuid(), new DeliveryAddress("Main", "Quito", new Coordinates(-0.18, -78.47)), new PlanReference(Guid.NewGuid()), new TimeOnly(9, 30));
        var order = ProductionOrder.Create(date, [line], Now);

        await using (var context = db.CreateContext())
        {
            new ProductionOrderRepository(context).Add(order);
            await context.SaveChangesAsync();
        }

        await using var read = db.CreateContext();
        var repo = new ProductionOrderRepository(read);

        var loaded = await repo.GetByDeliveryDateAsync(date.Value, default);
        loaded.ShouldNotBeNull();
        loaded.ProductionDate.Value.ShouldBe(new DateOnly(2026, 9, 1));
        var loadedLine = loaded.Lines.ShouldHaveSingleItem();
        loadedLine.Hour.ShouldBe(new TimeOnly(9, 30));
        loadedLine.Coordinates.Latitude.ShouldBe(-0.18);
        loadedLine.Address.Street.ShouldBe("Main");

        (await repo.GetByDeliveryDateAsync(new DateOnly(2030, 1, 1), default)).ShouldBeNull();
    }
}
