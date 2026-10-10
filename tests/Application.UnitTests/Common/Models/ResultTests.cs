using delivery_schedule_management.Application.Common.Models;
using NUnit.Framework;
using Shouldly;

namespace delivery_schedule_management.Application.UnitTests.Common.Models;

public class ResultTests
{
    [Test]
    public void Success_ShouldReturnSucceededResult_WhenCalled()
    {
        var result = Result.Success();

        result.Succeeded.ShouldBeTrue();
    }

    [Test]
    public void Success_ShouldHaveNoErrors_WhenCalled()
    {
        var result = Result.Success();

        result.Errors.ShouldBeEmpty();
    }

    [Test]
    public void Failure_ShouldReturnFailedResult_WhenCalled()
    {
        var result = Result.Failure(new[] { "boom" });

        result.Succeeded.ShouldBeFalse();
    }

    [Test]
    public void Failure_ShouldExposeAllErrorsInOrder_WhenMultipleErrorsGiven()
    {
        var result = Result.Failure(new List<string> { "first", "second" });

        result.Errors.ShouldBe(new[] { "first", "second" });
    }

    [Test]
    public void Failure_ShouldHaveEmptyErrors_WhenNoErrorsGiven()
    {
        var result = Result.Failure(Array.Empty<string>());

        result.Succeeded.ShouldBeFalse();
        result.Errors.ShouldBeEmpty();
    }
}
