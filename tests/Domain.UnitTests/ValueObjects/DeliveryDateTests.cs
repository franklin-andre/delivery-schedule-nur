using delivery_schedule_management.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace delivery_schedule_management.Domain.UnitTests.ValueObjects;

public class DeliveryDateTests
{
    [Test]
    public void Ctor_ShouldThrow_WhenDefaultDate()
    {
        Should.Throw<ArgumentException>(() => new DeliveryDate(default));
    }

    [Test]
    public void AddDays_ShouldReturnNewInstance_AndLeaveOriginalUntouched()
    {
        var date = new DeliveryDate(new DateOnly(2026, 8, 10));

        var next = date.AddDays(3);

        next.Value.ShouldBe(new DateOnly(2026, 8, 13));
        date.Value.ShouldBe(new DateOnly(2026, 8, 10));
    }
}
