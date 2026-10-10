using delivery_schedule_management.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace delivery_schedule_management.Domain.UnitTests.ValueObjects;

public class DeliveryAddressTests
{
    private static readonly Coordinates Quito = new(-0.18, -78.47);

    [Test]
    public void Ctor_ShouldKeepAllFields_WhenValid()
    {
        var address = new DeliveryAddress("Av. Amazonas 1", "Quito", Quito, "Blue door", "Ring twice");

        address.Street.ShouldBe("Av. Amazonas 1");
        address.City.ShouldBe("Quito");
        address.Coordinates.ShouldBe(Quito);
        address.References.ShouldBe("Blue door");
        address.Notes.ShouldBe("Ring twice");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Ctor_ShouldThrow_WhenStreetBlank(string? street)
    {
        Should.Throw<ArgumentException>(() => new DeliveryAddress(street!, "Quito", Quito));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Ctor_ShouldThrow_WhenCityBlank(string? city)
    {
        Should.Throw<ArgumentException>(() => new DeliveryAddress("Street", city!, Quito));
    }

    [Test]
    public void Equality_ShouldBeValueBased()
    {
        var a = new DeliveryAddress("S", "C", Quito);
        var b = new DeliveryAddress("S", "C", new Coordinates(-0.18, -78.47));

        a.ShouldBe(b);
    }
}
