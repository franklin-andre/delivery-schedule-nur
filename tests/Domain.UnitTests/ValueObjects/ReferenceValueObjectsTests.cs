using delivery_schedule_management.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace delivery_schedule_management.Domain.UnitTests.ValueObjects;

public class ReferenceValueObjectsTests
{
    [Test]
    public void ContractReference_ShouldThrow_WhenEmptyGuid()
    {
        Should.Throw<ArgumentException>(() => new ContractReference(Guid.Empty));
    }

    [Test]
    public void PlanReference_ShouldThrow_WhenEmptyGuid()
    {
        Should.Throw<ArgumentException>(() => new PlanReference(Guid.Empty));
    }

    [Test]
    public void References_ShouldBeEqual_WhenSameValue()
    {
        var id = Guid.NewGuid();

        new ContractReference(id).ShouldBe(new ContractReference(id));
        new PlanReference(id).ShouldBe(new PlanReference(id));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public void RecipeReference_ShouldThrow_WhenBlank(string? value)
    {
        Should.Throw<ArgumentException>(() => new RecipeReference(value!));
    }

    [Test]
    public void RecipeReference_ShouldKeepValue()
    {
        new RecipeReference("recipe-1").Value.ShouldBe("recipe-1");
    }

    [Test]
    public void DeliveryEvidence_ShouldKeepOptionalFields()
    {
        var at = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

        var evidence = new DeliveryEvidence(at, "http://p", null);

        evidence.RecordedAt.ShouldBe(at);
        evidence.PhotoUrl.ShouldBe("http://p");
        evidence.SignatureUrl.ShouldBeNull();
    }
}
