using delivery_schedule_management.Domain.Common;
using NUnit.Framework;
using Shouldly;

namespace delivery_schedule_management.Domain.UnitTests.Common;

public class BaseTypesTests
{
    private sealed class Money(decimal amount, string currency) : ValueObject
    {
        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return amount;
            yield return currency;
        }
    }

    private sealed class Other(decimal amount, string currency) : ValueObject
    {
        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return amount;
            yield return currency;
        }
    }

    private sealed class Event : BaseEvent;

    private sealed class Entity : BaseEntity;

    // ───────── ValueObject ─────────

    [Test]
    public void Equals_ShouldBeTrue_WhenAllComponentsMatch()
    {
        new Money(5, "USD").Equals(new Money(5, "USD")).ShouldBeTrue();
        (new Money(5, "USD") == new Money(5, "USD")).ShouldBeTrue();
        (new Money(5, "USD") != new Money(6, "USD")).ShouldBeTrue();
    }

    [Test]
    public void Equals_ShouldBeFalse_WhenComponentOrTypeOrNullDiffers()
    {
        new Money(5, "USD").Equals(new Money(5, "EUR")).ShouldBeFalse();
        new Money(5, "USD").Equals(new Other(5, "USD")).ShouldBeFalse();
        new Money(5, "USD").Equals(null).ShouldBeFalse();
    }

    [Test]
    public void GetHashCode_ShouldMatch_ForEqualObjects()
    {
        new Money(5, "USD").GetHashCode().ShouldBe(new Money(5, "USD").GetHashCode());
    }

    [Test]
    public void Operators_ShouldHandleNulls()
    {
        Money? none = null;
        var some = new Money(1, "USD");

        (none! == (Money)null!).ShouldBeTrue();
        (some == none!).ShouldBeFalse();
        (none! != some).ShouldBeTrue();
    }

    // ───────── BaseEntity domain events ─────────

    [Test]
    public void DomainEvents_ShouldBeAddedRemovedAndCleared()
    {
        var entity = new Entity();
        var first = new Event();
        var second = new Event();

        entity.AddDomainEvent(first);
        entity.AddDomainEvent(second);
        entity.DomainEvents.Count.ShouldBe(2);

        entity.RemoveDomainEvent(first);
        entity.DomainEvents.ShouldHaveSingleItem().ShouldBe(second);

        entity.ClearDomainEvents();
        entity.DomainEvents.ShouldBeEmpty();
    }

    [Test]
    public void Id_ShouldDefaultToZero_UntilPersisted()
    {
        new Entity().Id.ShouldBe(0);
    }
}
