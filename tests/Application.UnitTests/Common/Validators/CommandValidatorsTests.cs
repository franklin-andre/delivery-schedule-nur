using delivery_schedule_management.Application.Calendars.Commands.ApplyDeliveryResult;
using delivery_schedule_management.Application.Calendars.Commands.CancelDeliveryDay;
using delivery_schedule_management.Application.Calendars.Commands.CreateSchedule;
using delivery_schedule_management.Application.Calendars.Commands.RescheduleDeliveryDay;
using delivery_schedule_management.Application.Calendars.Commands.SetDeliveryAddress;
using delivery_schedule_management.Domain.Enums;
using FluentValidation.TestHelper;
using NUnit.Framework;

namespace delivery_schedule_management.Application.UnitTests.Common.Validators;

public class CommandValidatorsTests
{
    private static readonly DateOnly Date = new(2026, 8, 10);

    // ---- CreateSchedule ----
    private readonly CreateScheduleCommandValidator _create = new();

    [Test]
    public void CreateSchedule_ShouldPass_WhenValid()
    {
        _create.TestValidate(new CreateScheduleCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15, Date))
            .ShouldNotHaveAnyValidationErrors();
    }

    [TestCase(0)]
    [TestCase(10)]
    [TestCase(45)]
    public void CreateSchedule_ShouldFail_WhenDurationNot15Or30(int duration)
    {
        _create.TestValidate(new CreateScheduleCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), duration, Date))
            .ShouldHaveValidationErrorFor(x => x.Duration);
    }

    [Test]
    public void CreateSchedule_ShouldFail_WhenIdsEmptyOrDateDefault()
    {
        var result = _create.TestValidate(new CreateScheduleCommand(Guid.Empty, Guid.Empty, Guid.Empty, 30, default));

        result.ShouldHaveValidationErrorFor(x => x.ContractId);
        result.ShouldHaveValidationErrorFor(x => x.PatientId);
        result.ShouldHaveValidationErrorFor(x => x.PlanId);
        result.ShouldHaveValidationErrorFor(x => x.StartDate);
    }

    // ---- SetDeliveryAddress ----
    private readonly SetDeliveryAddressCommandValidator _address = new();

    [Test]
    public void SetAddress_ShouldFail_WhenStreetCityBlankOrCoordinatesOutOfRange()
    {
        var result = _address.TestValidate(new SetDeliveryAddressCommand(1, 1, "", "", 91, 181, null, null));

        result.ShouldHaveValidationErrorFor(x => x.Street);
        result.ShouldHaveValidationErrorFor(x => x.City);
        result.ShouldHaveValidationErrorFor(x => x.Latitude);
        result.ShouldHaveValidationErrorFor(x => x.Longitude);
    }

    [Test]
    public void SetAddress_ShouldPass_WhenValid()
    {
        _address.TestValidate(new SetDeliveryAddressCommand(1, 1, "Street", "Quito", -0.18, -78.47, null, null))
            .ShouldNotHaveAnyValidationErrors();
    }

    // ---- Reschedule ----
    private readonly RescheduleDeliveryDayCommandValidator _reschedule = new();

    [Test]
    public void Reschedule_ShouldFail_WhenIdsNotPositiveOrDateDefault()
    {
        var result = _reschedule.TestValidate(new RescheduleDeliveryDayCommand(0, 0, default));

        result.ShouldHaveValidationErrorFor(x => x.CalendarId);
        result.ShouldHaveValidationErrorFor(x => x.DayId);
        result.ShouldHaveValidationErrorFor(x => x.NewDate);
    }

    [Test]
    public void Reschedule_ShouldPass_WhenValid()
    {
        _reschedule.TestValidate(new RescheduleDeliveryDayCommand(1, 2, Date)).ShouldNotHaveAnyValidationErrors();
    }

    // ---- Cancel ----
    private readonly CancelDeliveryDayCommandValidator _cancel = new();

    [Test]
    public void Cancel_ShouldFail_WhenIdsNotPositive()
    {
        var result = _cancel.TestValidate(new CancelDeliveryDayCommand(-1, 0));

        result.ShouldHaveValidationErrorFor(x => x.CalendarId);
        result.ShouldHaveValidationErrorFor(x => x.DayId);
    }

    // ---- ApplyDeliveryResult ----
    private readonly ApplyDeliveryResultCommandValidator _result = new();

    [Test]
    public void ApplyResult_ShouldFail_WhenDayIdInvalidOrResultUndefined()
    {
        var result = _result.TestValidate(new ApplyDeliveryResultCommand(0, (DeliveryResult)99, null, null));

        result.ShouldHaveValidationErrorFor(x => x.DayId);
        result.ShouldHaveValidationErrorFor(x => x.Result);
    }

    [Test]
    public void ApplyResult_ShouldPass_WhenValid()
    {
        _result.TestValidate(new ApplyDeliveryResultCommand(5, DeliveryResult.Delivered, null, null))
            .ShouldNotHaveAnyValidationErrors();
    }
}
