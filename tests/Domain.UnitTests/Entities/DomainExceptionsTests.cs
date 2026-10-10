using delivery_schedule_management.Domain.Enums;
using delivery_schedule_management.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace delivery_schedule_management.Domain.UnitTests.Entities;

public class DomainExceptionsTests
{
    [Test]
    public void DeliveryWindowLockedException_ShouldMentionDayAndDate()
    {
        var ex = new DeliveryWindowLockedException(7, new DateOnly(2026, 8, 10));

        ex.Message.ShouldContain("7");
        ex.Message.ShouldContain("2026-08-10");
    }

    [Test]
    public void InvalidDeliveryDayTransitionException_ShouldMentionStatusAndAction()
    {
        var ex = new InvalidDeliveryDayTransitionException(3, DayStatus.Delivered, "cancel");

        ex.Message.ShouldContain("Delivered");
        ex.Message.ShouldContain("cancel");
    }
}
