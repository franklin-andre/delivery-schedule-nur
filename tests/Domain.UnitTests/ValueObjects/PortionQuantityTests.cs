using delivery_schedule_management.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace delivery_schedule_management.Domain.UnitTests.ValueObjects;

public class PortionQuantityTests
{
    [TestCase(1)]
    [TestCase(4)]
    public void Ctor_ShouldAccept_PositiveValues(int value)
    {
        new PortionQuantity(value).Value.ShouldBe(value);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Ctor_ShouldThrow_WhenNotPositive(int value)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new PortionQuantity(value));
    }
}
