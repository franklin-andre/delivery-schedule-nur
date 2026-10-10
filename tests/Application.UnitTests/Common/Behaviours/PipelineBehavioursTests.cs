using delivery_schedule_management.Application.Common.Behaviours;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Shouldly;
using ValidationException = delivery_schedule_management.Application.Common.Exceptions.ValidationException;

namespace delivery_schedule_management.Application.UnitTests.Common.Behaviours;

public class PipelineBehavioursTests
{
    public record SampleRequest(string Name);

    private static void VerifyLogged(Mock<ILogger<SampleRequest>> logger, LogLevel level, Times times) =>
        logger.Verify(l => l.Log(
            level,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), times);

    private static Mock<RequestHandlerDelegate<string>> NextReturning(string value)
    {
        var next = new Mock<RequestHandlerDelegate<string>>();
        next.Setup(n => n(It.IsAny<CancellationToken>())).ReturnsAsync(value);
        return next;
    }

    // ---------- ValidationBehaviour ----------

    [Test]
    public async Task Validation_ShouldCallNext_WhenNoValidatorsRegistered()
    {
        var behaviour = new ValidationBehaviour<SampleRequest, string>([]);
        var next = NextReturning("ok");

        var response = await behaviour.Handle(new SampleRequest("x"), next.Object, CancellationToken.None);

        response.ShouldBe("ok");
        next.Verify(n => n(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Validation_ShouldThrowAndSkipNext_WhenValidatorFails()
    {
        var validator = new Mock<IValidator<SampleRequest>>();
        validator
            .Setup(v => v.ValidateAsync(It.IsAny<IValidationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult([new ValidationFailure("Name", "Name is required")]));
        var behaviour = new ValidationBehaviour<SampleRequest, string>([validator.Object]);
        var next = NextReturning("ok");

        var ex = await Should.ThrowAsync<ValidationException>(() =>
            behaviour.Handle(new SampleRequest(""), next.Object, CancellationToken.None));

        ex.Errors["Name"].ShouldContain("Name is required");
        next.Verify(n => n(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Validation_ShouldCallNext_WhenValidatorPasses()
    {
        var validator = new Mock<IValidator<SampleRequest>>();
        validator
            .Setup(v => v.ValidateAsync(It.IsAny<IValidationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        var behaviour = new ValidationBehaviour<SampleRequest, string>([validator.Object]);

        var response = await behaviour.Handle(new SampleRequest("x"), NextReturning("ok").Object, CancellationToken.None);

        response.ShouldBe("ok");
    }

    // ---------- UnhandledExceptionBehaviour ----------

    [Test]
    public async Task UnhandledException_ShouldLogErrorAndRethrow()
    {
        var logger = new Mock<ILogger<SampleRequest>>();
        var behaviour = new UnhandledExceptionBehaviour<SampleRequest, string>(logger.Object);
        var next = new Mock<RequestHandlerDelegate<string>>();
        next.Setup(n => n(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));

        await Should.ThrowAsync<InvalidOperationException>(() =>
            behaviour.Handle(new SampleRequest("x"), next.Object, CancellationToken.None));

        VerifyLogged(logger, LogLevel.Error, Times.Once());
    }

    [Test]
    public async Task UnhandledException_ShouldNotLog_WhenNextSucceeds()
    {
        var logger = new Mock<ILogger<SampleRequest>>();
        var behaviour = new UnhandledExceptionBehaviour<SampleRequest, string>(logger.Object);

        (await behaviour.Handle(new SampleRequest("x"), NextReturning("ok").Object, CancellationToken.None)).ShouldBe("ok");

        VerifyLogged(logger, LogLevel.Error, Times.Never());
    }

    // ---------- PerformanceBehaviour ----------

    [Test]
    public async Task Performance_ShouldNotWarn_WhenFast()
    {
        var logger = new Mock<ILogger<SampleRequest>>();
        var behaviour = new PerformanceBehaviour<SampleRequest, string>(logger.Object);

        (await behaviour.Handle(new SampleRequest("x"), NextReturning("ok").Object, CancellationToken.None)).ShouldBe("ok");

        VerifyLogged(logger, LogLevel.Warning, Times.Never());
    }

    [Test]
    public async Task Performance_ShouldWarn_WhenSlowerThan500Ms()
    {
        var logger = new Mock<ILogger<SampleRequest>>();
        var behaviour = new PerformanceBehaviour<SampleRequest, string>(logger.Object);
        var next = new Mock<RequestHandlerDelegate<string>>();
        next.Setup(n => n(It.IsAny<CancellationToken>())).Returns(async () =>
        {
            await Task.Delay(600);
            return "slow";
        });

        await behaviour.Handle(new SampleRequest("x"), next.Object, CancellationToken.None);

        VerifyLogged(logger, LogLevel.Warning, Times.Once());
    }

    // ---------- LoggingBehaviour ----------

    [Test]
    public async Task Logging_ShouldLogRequestAtInformation()
    {
        var logger = new Mock<ILogger<SampleRequest>>();
        var behaviour = new LoggingBehaviour<SampleRequest>(logger.Object);

        await behaviour.Process(new SampleRequest("x"), CancellationToken.None);

        VerifyLogged(logger, LogLevel.Information, Times.Once());
    }
}
