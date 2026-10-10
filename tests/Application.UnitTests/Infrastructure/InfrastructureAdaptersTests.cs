using delivery_schedule_management.Infrastructure.Catalog;
using delivery_schedule_management.Infrastructure.Common;
using Moq;
using NUnit.Framework;
using Shouldly;

namespace delivery_schedule_management.Application.UnitTests.Infrastructure;

public class InfrastructureAdaptersTests
{
    [Test]
    public async Task MockCatalogPort_ShouldBeDeterministic_ForSamePlanAndDate()
    {
        var port = new MockCatalogPort();
        var plan = Guid.NewGuid();
        var date = new DateOnly(2026, 8, 11);

        var a = await port.GetDayResolutionAsync(plan, date, CancellationToken.None);
        var b = await port.GetDayResolutionAsync(plan, date, CancellationToken.None);

        a.ShouldBe(b);
        a.RecipeRef.ShouldBe("recipe-" + plan.ToString("N") + "-20260811");
        a.Portions.ShouldBeInRange(1, 4);
    }

    [Test]
    public async Task MockCatalogPort_ShouldVaryRecipe_ByDate()
    {
        var port = new MockCatalogPort();
        var plan = Guid.NewGuid();

        var d1 = await port.GetDayResolutionAsync(plan, new DateOnly(2026, 8, 11), CancellationToken.None);
        var d2 = await port.GetDayResolutionAsync(plan, new DateOnly(2026, 8, 12), CancellationToken.None);

        d1.RecipeRef.ShouldNotBe(d2.RecipeRef);
    }

    [Test]
    public void SystemClock_ShouldDelegateToTimeProvider()
    {
        var instant = new DateTimeOffset(2026, 8, 10, 1, 2, 3, TimeSpan.Zero);
        var provider = new Mock<TimeProvider>();
        provider.Setup(p => p.GetUtcNow()).Returns(instant);

        new SystemClock(provider.Object).Now.ShouldBe(instant);
    }
}
