using delivery_schedule_management.Domain.Entities;
using delivery_schedule_management.Domain.Enums;
using delivery_schedule_management.Domain.ValueObjects;
using delivery_schedule_management.Infrastructure.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace delivery_schedule_management.Infrastructure.IntegrationTests;

/// <summary>Owned-type and constraint behaviour that only a real Postgres can prove.</summary>
public class DeliveryDayPersistenceTests : RepositoryTestBase
{
    private static DeliveryCalendar NewCalendar(Guid? contractId = null) =>
        DeliveryCalendar.Create(
            Guid.NewGuid(),
            new ContractReference(contractId ?? Guid.NewGuid()),
            new PlanReference(Guid.NewGuid()),
            new PlanDuration(15),
            new DeliveryDate(new DateOnly(2026, 8, 1)));

    [Test]
    public async Task DeliveryLifecycle_ShouldPersistRecipePortionsAndEvidence()
    {
        var calendar = NewCalendar();
        await using (var context = IntegrationTestSetup.CreateContext())
        {
            new CalendarRepository(context).Add(calendar);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        var dayId = calendar.Days.First().Id;
        var now = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

        await using (var context = IntegrationTestSetup.CreateContext())
        {
            var loaded = await new CalendarRepository(context).GetByIdAsync(calendar.Id, CancellationToken.None);
            var day = loaded!.Days.First(d => d.Id == dayId);

            day.SetAddress(new DeliveryAddress("Main St 1", "Quito", new Coordinates(-0.18, -78.47)));
            day.MarkReadyForProduction(new RecipeReference("recipe-1"), new PortionQuantity(3));
            day.ApplyDeliveryResult(DeliveryResult.Delivered, new DeliveryEvidence(now, "http://photo", "http://sign"), now);

            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using (var context = IntegrationTestSetup.CreateContext())
        {
            var loaded = await new CalendarRepository(context).GetByDeliveryDayIdAsync(dayId, CancellationToken.None);
            var day = loaded!.Days.First(d => d.Id == dayId);

            day.Status.ShouldBe(DayStatus.Delivered);
            day.RecipeRef!.Value.ShouldBe("recipe-1");
            day.Portions!.Value.ShouldBe(3);
            day.Evidence.ShouldNotBeNull();
            day.Evidence.PhotoUrl.ShouldBe("http://photo");
            day.Evidence.SignatureUrl.ShouldBe("http://sign");
            day.Evidence.RecordedAt.ShouldBe(now);
        }
    }

    [Test]
    public async Task Rescheduled_ShouldPersistNewDate()
    {
        var calendar = NewCalendar();
        await using (var context = IntegrationTestSetup.CreateContext())
        {
            new CalendarRepository(context).Add(calendar);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        var dayId = calendar.Days.First().Id;
        var newDate = new DateOnly(2026, 9, 15);

        await using (var context = IntegrationTestSetup.CreateContext())
        {
            var loaded = await new CalendarRepository(context).GetByIdAsync(calendar.Id, CancellationToken.None);
            loaded!.Days.First(d => d.Id == dayId)
                .Reschedule(new DeliveryDate(newDate), new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using (var context = IntegrationTestSetup.CreateContext())
        {
            var loaded = await new CalendarRepository(context).GetByIdAsync(calendar.Id, CancellationToken.None);
            loaded!.Days.First(d => d.Id == dayId).Date.Value.ShouldBe(newDate);
        }
    }

    // ───────── incorrect flow: the DB itself enforces one calendar per contract ─────────

    [Test]
    public async Task Add_ShouldFailWithUniqueViolation_WhenContractAlreadyHasCalendar()
    {
        var contractId = Guid.NewGuid();

        await using (var context = IntegrationTestSetup.CreateContext())
        {
            new CalendarRepository(context).Add(NewCalendar(contractId));
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using var second = IntegrationTestSetup.CreateContext();
        new CalendarRepository(second).Add(NewCalendar(contractId));

        await Should.ThrowAsync<DbUpdateException>(() => second.SaveChangesAsync(CancellationToken.None));
    }

    [Test]
    public async Task GetByIdAsync_ShouldReturnNull_WhenCalendarMissing()
    {
        await using var context = IntegrationTestSetup.CreateContext();

        var loaded = await new CalendarRepository(context).GetByIdAsync(999999, CancellationToken.None);

        loaded.ShouldBeNull();
    }
}
