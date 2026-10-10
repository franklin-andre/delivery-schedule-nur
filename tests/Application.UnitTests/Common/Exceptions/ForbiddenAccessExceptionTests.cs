using delivery_schedule_management.Application.Common.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace delivery_schedule_management.Application.UnitTests.Common.Exceptions;

public class ForbiddenAccessExceptionTests
{
    [Test]
    public void Constructor_ShouldCreateException_WhenCalled()
    {
        var exception = new ForbiddenAccessException();

        exception.ShouldBeAssignableTo<Exception>();
    }

    [Test]
    public void Constructor_ShouldHaveNoInnerException_WhenCalled()
    {
        var exception = new ForbiddenAccessException();

        exception.InnerException.ShouldBeNull();
    }

    [Test]
    public void Throw_ShouldBeCatchableAsForbiddenAccessException_WhenThrown()
    {
        Should.Throw<ForbiddenAccessException>(() => throw new ForbiddenAccessException());
    }
}
