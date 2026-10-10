using delivery_schedule_management.Application.Common.Interfaces;
using delivery_schedule_management.Domain.Enums;
using delivery_schedule_management.Infrastructure.Catalog;
using delivery_schedule_management.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Shouldly;

namespace delivery_schedule_management.Application.UnitTests.Infrastructure;

public class InitialiserAndWiringTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    private static ApplicationDbContextInitialiser CreateInitialiser(ApplicationDbContext context)
    {
        var time = new Mock<TimeProvider>();
        time.Setup(t => t.GetUtcNow()).Returns(Now);
        return new ApplicationDbContextInitialiser(NullLogger<ApplicationDbContextInitialiser>.Instance, context, time.Object);
    }

    // ───────── Seeder ─────────

    [Test]
    public async Task Seed_ShouldCreateThreePatients_CoveringEveryStatusExceptInProduction()
    {
        using var db = new InMemoryDb();
        await using var context = db.CreateContext();

        await CreateInitialiser(context).SeedAsync();

        var calendars = await context.Calendars.Include(c => c.Days).ToListAsync();
        calendars.Count.ShouldBe(3);

        var statuses = calendars.SelectMany(c => c.Days).Select(d => d.Status).ToHashSet();
        statuses.ShouldContain(DayStatus.Pending);
        statuses.ShouldContain(DayStatus.Scheduled);
        statuses.ShouldContain(DayStatus.ReadyForProduction);
        statuses.ShouldContain(DayStatus.Delivered);
        statuses.ShouldContain(DayStatus.Failed);
        statuses.ShouldContain(DayStatus.Cancelled);
        statuses.ShouldNotContain(DayStatus.InProduction);
    }

    [Test]
    public async Task Seed_ShouldPreConsolidateTomorrowsProductionOrder_WithRealDayIds()
    {
        using var db = new InMemoryDb();
        await using var context = db.CreateContext();

        await CreateInitialiser(context).SeedAsync();

        var order = await context.ProductionOrders.Include(o => o.Lines).SingleAsync();
        order.DeliveryDate.Value.ShouldBe(DateOnly.FromDateTime(Now.UtcDateTime).AddDays(1));
        order.Lines.ShouldNotBeEmpty();
        order.Lines.ShouldAllBe(l => l.DeliveryDayId > 0); // CLAUDE.md gotcha: ids exist only after the first save
    }

    [Test]
    public async Task Seed_ShouldBeIdempotent_WhenDataAlreadyExists()
    {
        using var db = new InMemoryDb();
        await using var context = db.CreateContext();
        var initialiser = CreateInitialiser(context);

        await initialiser.SeedAsync();
        var firstCount = await context.Calendars.CountAsync();
        await initialiser.SeedAsync();

        (await context.Calendars.CountAsync()).ShouldBe(firstCount);
    }

    [Test]
    public async Task Seed_ShouldLogAndRethrow_WhenDatabaseUnavailable()
    {
        using var db = new InMemoryDb();
        var context = db.CreateContext();
        await context.DisposeAsync(); // any use of a disposed context throws

        await Should.ThrowAsync<ObjectDisposedException>(() => CreateInitialiser(context).SeedAsync());
    }

    [Test]
    public async Task Initialise_ShouldLogAndRethrow_WhenMigrationFails()
    {
        // MigrateAsync needs a relational provider; the in-memory provider makes it fail, exercising the error path.
        using var db = new InMemoryDb();
        await using var context = db.CreateContext();

        await Should.ThrowAsync<InvalidOperationException>(() => CreateInitialiser(context).InitialiseAsync());
    }

    // ───────── Dependency injection wiring ─────────

    private static HostApplicationBuilder BuilderWith(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:delivery-schedule-management-db"] = "Host=localhost;Database=unused;Username=u;Password=p"
        });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddLogging();
        return builder;
    }

    [Test]
    public void AddInfrastructureServices_ShouldUseMockCatalog_ByDefault()
    {
        var builder = BuilderWith([]);

        builder.AddInfrastructureServices();
        using var provider = builder.Services.BuildServiceProvider();

        provider.GetRequiredService<ICatalogPort>().ShouldBeOfType<MockCatalogPort>();
        provider.GetRequiredService<IClock>().ShouldNotBeNull();
    }

    [Test]
    public void AddInfrastructureServices_ShouldUseHttpCatalog_WhenMockDisabled()
    {
        var builder = BuilderWith(new() { ["Catalog:UseMock"] = "false", ["Catalog:BaseUrl"] = "http://ms2.local/" });

        builder.AddInfrastructureServices();
        using var provider = builder.Services.BuildServiceProvider();

        provider.GetRequiredService<ICatalogPort>().ShouldBeOfType<HttpCatalogPort>();
    }

    [Test]
    public void AddInfrastructureServices_ShouldFail_WhenMockDisabledWithoutBaseUrl()
    {
        var builder = BuilderWith(new() { ["Catalog:UseMock"] = "false" });

        Should.Throw<ArgumentException>(() => builder.AddInfrastructureServices());
    }

    [Test]
    public void AddInfrastructureServices_ShouldFail_WhenConnectionStringMissing()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        Should.Throw<ArgumentNullException>(() => builder.AddInfrastructureServices());
    }

    [Test]
    public void AddApplicationServices_ShouldRegisterMediatorAndValidators()
    {
        var builder = BuilderWith([]);
        builder.Services.AddLogging();

        builder.AddApplicationServices();
        using var provider = builder.Services.BuildServiceProvider();

        provider.GetRequiredService<MediatR.IMediator>().ShouldNotBeNull();
        provider.GetServices<FluentValidation.IValidator<Application.Calendars.Commands.CreateSchedule.CreateScheduleCommand>>()
            .ShouldNotBeEmpty();
    }
}
